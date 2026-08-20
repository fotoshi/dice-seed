#!/usr/bin/env bash
# comparar_implementaciones.sh
#
# Comprueba que las 3 implementaciones (Python, JavaScript, C#) producen
# EXACTAMENTE las mismas 12 palabras BIP-39 cuando reciben las mismas 50
# tiradas de dado, y deja un informe en Markdown por cada ronda para poder
# verificar la entropia a mano con herramientas externas.
#
# No instala nada nuevo: reutiliza los mismos interpretes/compiladores que
# cada script ya necesita para funcionar (python3, node, y mono o dotnet
# para C#; python3 tambien se usa para las conversiones de base del informe).
# Genera tiradas de prueba con /dev/urandom, que ya trae el sistema operativo.
#
# USO:
#     ./comparar_implementaciones.sh          # 5 rondas aleatorias
#     ./comparar_implementaciones.sh 20        # 20 rondas aleatorias
#
# Cada ronda genera un fichero en informes/<entropia-hex>.md (o
# informes/<entropia-hex>_DESAJUSTE.md si las implementaciones no coinciden)
# con: las tiradas usadas, el comando exacto para relanzar cada
# implementacion, las 12 palabras de cada una y la entropia en hexadecimal,
# binario, base 6 y decimal.
#
# Termina con codigo de salida 0 si todo coincide, 1 si alguna ronda difiere.

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PY="$ROOT/python/bip39_dados.py"
JS="$ROOT/javascript/bip39_dados.js"
CS="$ROOT/csharp/bip39_dados.cs"
INFORMES="$ROOT/informes"

N_RONDAS="${1:-5}"

mkdir -p "$INFORMES"

TMPDIR="$(mktemp -d)"
trap 'rm -rf "$TMPDIR"' EXIT

# ---------------------------------------------------------------------------
# Localizar como ejecutar C#: Mono si esta disponible (compila el fichero
# suelto, sin crear un proyecto), si no dotnet (necesita un proyecto en
# .NET 6-9). CS_LABEL/CS_CMD_SETUP/CS_CMD_RUN son solo para MOSTRAR en el
# informe el comando reproducible; cs_run() es lo que de verdad se ejecuta.
# ---------------------------------------------------------------------------
if command -v mcs >/dev/null 2>&1 && command -v mono >/dev/null 2>&1; then
  mcs -r:System.Numerics.dll -out:"$TMPDIR/bip39.exe" "$CS" >/dev/null
  cs_run() { mono "$TMPDIR/bip39.exe" "$@"; }
  CS_LABEL="Mono"
  CS_CMD_SETUP="mcs -r:System.Numerics.dll -out:bip39_dados.exe scripts/csharp/bip39_dados.cs"
  CS_CMD_RUN="mono bip39_dados.exe"
elif command -v dotnet >/dev/null 2>&1; then
  PROJ="$TMPDIR/csproj"
  dotnet new console -o "$PROJ" >/dev/null
  cp "$CS" "$PROJ/Program.cs"
  dotnet build "$PROJ" -c Release -o "$PROJ/out" >/dev/null
  DLL="$PROJ/out/$(basename "$PROJ").dll"
  cs_run() { dotnet "$DLL" "$@"; }
  CS_LABEL="dotnet"
  CS_CMD_SETUP="dotnet new console -o bip39 && cp scripts/csharp/bip39_dados.cs bip39/Program.cs"
  CS_CMD_RUN="dotnet run --project bip39"
else
  echo "ERROR: no se encontro ni mono ni dotnet. Instala uno de los dos para C#." >&2
  exit 1
fi

echo "== Paso 1: autocomprobacion individual (--test) de cada implementacion =="
python3 "$PY" --test
node "$JS" --test
cs_run --test
echo

# ---------------------------------------------------------------------------
# Extrae de la salida completa de un programa la linea "Entropia : <hex>" y
# la lista de 12 palabras, y las junta en un solo string "hex|palabras".
# Las tres implementaciones imprimen ambas cosas con el mismo formato
# ("Entropia : ..." y "NN. palabra"), asi que un mismo grep/awk vale para
# las tres.
# ---------------------------------------------------------------------------
extraer() {
  local salida="$1"
  local hex
  hex="$(grep -m1 '^Entropia' <<<"$salida" | awk '{print $3}')"
  local palabras
  palabras="$(grep -E '^[[:space:]]*[0-9]+\.[[:space:]]' <<<"$salida" | awk '{print $2}' | tr '\n' ' ')"
  echo "${hex}|${palabras% }"
}

# ---------------------------------------------------------------------------
# Imprime (a stdout) las filas de una tabla Markdown con la entropia dada en
# hexadecimal, binario, base 6 y decimal. Usa python3 porque bash no maneja
# enteros de 128 bits; python3 ya es una dependencia del proyecto.
# ---------------------------------------------------------------------------
formatos_md() {
  local hex="$1"
  python3 - "$hex" <<'PYEOF'
import sys
hexs = sys.argv[1]
n = int(hexs, 16)
binv = bin(n)[2:].zfill(128)

def a_base6(n):
    if n == 0:
        return "0"
    digitos = []
    while n:
        digitos.append(str(n % 6))
        n //= 6
    return "".join(reversed(digitos))

b6 = a_base6(n).zfill(50)
print('| Hexadecimal | `%s` |' % hexs)
print('| Binario (128 bits) | `%s` |' % binv)
print('| Base 6 (128 bits, digitos 0-5) | `%s` |' % b6)
print('| Decimal | `%s` |' % n)
PYEOF
}

# ---------------------------------------------------------------------------
# Genera el informe Markdown de una ronda (a stdout; el llamador redirige a
# fichero). El nombre del fichero lo pone el llamador, basado en la entropia.
# ---------------------------------------------------------------------------
generar_informe() {
  local ronda="$1" tiradas="$2" coincide="$3"
  local hex_py="$4" pal_py="$5"
  local hex_js="$6" pal_js="$7"
  local hex_cs="$8" pal_cs="$9"
  local fecha
  fecha="$(date '+%Y-%m-%d %H:%M:%S %z')"

  printf '# Informe de comprobacion cruzada BIP-39 dado\n\n'
  printf '**Identificador (entropia Python, hex):** `%s`\n\n' "$hex_py"
  printf '**Fecha:** %s\n\n' "$fecha"
  printf '**Ronda:** %s de %s\n\n' "$ronda" "$N_RONDAS"
  printf '**Tiradas (50 valores 1-6, tal como se introdujeron):**\n\n'
  printf '```\n%s\n```\n\n' "$tiradas"

  printf '## Resultado\n\n'
  if [[ "$coincide" == "si" ]]; then
    printf '**OK** - las 3 implementaciones producen la misma entropia y las mismas 12 palabras.\n\n'
  else
    printf '**DESAJUSTE** - alguna implementacion difiere. No uses este metodo hasta resolverlo.\n\n'
  fi

  printf '## Comandos ejecutados\n\n'
  printf 'Lanzados desde la raiz del repositorio, con las tiradas de arriba como entrada estandar.\n\n'

  printf '### Python\n\n'
  printf '```bash\necho %s | python3 scripts/python/bip39_dados.py\n```\n\n' "$tiradas"

  printf '### JavaScript / Node.js\n\n'
  printf '```bash\necho %s | node scripts/javascript/bip39_dados.js\n```\n\n' "$tiradas"

  printf '### C# (%s)\n\n' "$CS_LABEL"
  printf '```bash\n%s\necho %s | %s\n```\n\n' "$CS_CMD_SETUP" "$tiradas" "$CS_CMD_RUN"

  printf '## Palabras resultantes\n\n'
  printf '| Implementacion | Palabras (1 -> 12) |\n'
  printf '|---|---|\n'
  printf '| Python | %s |\n' "$pal_py"
  printf '| JavaScript | %s |\n' "$pal_js"
  printf '| C# | %s |\n\n' "$pal_cs"

  if [[ "$coincide" == "si" ]]; then
    printf '## Entropia en distintos formatos\n\n'
    printf 'Para verificar a mano con herramientas externas (por ejemplo, un conversor de\n'
    printf 'bases, o https://iancoleman.io/bip39/ pegando la entropia en hexadecimal).\n\n'
    printf '| Formato | Valor |\n'
    printf '|---|---|\n'
    formatos_md "$hex_py"
    printf '\n'
  else
    printf '## Entropia por implementacion (no coinciden)\n\n'
    printf '### Python\n\n| Formato | Valor |\n|---|---|\n'
    formatos_md "$hex_py"
    printf '\n### JavaScript\n\n| Formato | Valor |\n|---|---|\n'
    formatos_md "$hex_js"
    printf '\n### C#\n\n| Formato | Valor |\n|---|---|\n'
    formatos_md "$hex_cs"
    printf '\n'
  fi

  printf -- '---\n'
  printf 'Generado automaticamente por `comparar_implementaciones.sh`.\n'
}

echo "== Paso 2: comparacion cruzada con las mismas tiradas =="
fallos=0
for ((i = 1; i <= N_RONDAS; i++)); do
  # 50 tiradas de un d6 (1-6), leidas de /dev/urandom: nada que instalar.
  tiradas="$(od -An -tu1 -N50 /dev/urandom \
             | tr -s ' \n' '\n' | grep -v '^$' \
             | awk '{print ($1 % 6) + 1}' | tr '\n' ' ')"

  salida_py="$(printf '%s\n' "$tiradas" | python3 "$PY")"
  salida_js="$(printf '%s\n' "$tiradas" | node "$JS")"
  salida_cs="$(printf '%s\n' "$tiradas" | cs_run)"

  r_py="$(extraer "$salida_py")"
  r_js="$(extraer "$salida_js")"
  r_cs="$(extraer "$salida_cs")"

  hex_py="${r_py%%|*}"; pal_py="${r_py#*|}"
  hex_js="${r_js%%|*}"; pal_js="${r_js#*|}"
  hex_cs="${r_cs%%|*}"; pal_cs="${r_cs#*|}"

  if [[ "$r_py" == "$r_js" && "$r_js" == "$r_cs" ]]; then
    coincide="si"
    informe="$INFORMES/${hex_py}.md"
  else
    coincide="no"
    fallos=$((fallos + 1))
    informe="$INFORMES/${hex_py}_DESAJUSTE.md"
  fi

  generar_informe "$i" "$tiradas" "$coincide" \
    "$hex_py" "$pal_py" "$hex_js" "$pal_js" "$hex_cs" "$pal_cs" > "$informe"

  if [[ "$coincide" == "si" ]]; then
    echo "ronda $i: OK  (entropia $hex_py) -> ${informe#"$ROOT"/}"
  else
    echo "ronda $i: FALLO -> ${informe#"$ROOT"/}"
    echo "  tiradas : $tiradas"
    echo "  python  : $r_py"
    echo "  node    : $r_js"
    echo "  csharp  : $r_cs"
  fi
done

echo
if [[ $fallos -eq 0 ]]; then
  echo "OK: $N_RONDAS rondas, las 3 implementaciones coincidieron siempre."
  echo "Informes en: $INFORMES"
  exit 0
else
  echo "FALLO: $fallos de $N_RONDAS rondas no coincidieron."
  echo "Informes en: $INFORMES"
  exit 1
fi
