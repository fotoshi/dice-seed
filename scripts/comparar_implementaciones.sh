#!/usr/bin/env bash
# comparar_implementaciones.sh
#
# Comprueba que las 2 implementaciones (Python, JavaScript) producen
# EXACTAMENTE las mismas 12 palabras BIP-39 cuando reciben las mismas 50
# tiradas de dado, y deja un informe en Markdown por cada ronda para poder
# verificar la entropia a mano con herramientas externas.
#
# No instala nada nuevo: reutiliza los mismos interpretes que cada script ya
# necesita para funcionar (python3 y node; python3 tambien se usa para las
# conversiones de base del informe). Genera tiradas de prueba con
# /dev/urandom, que ya trae el sistema operativo.
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
INFORMES="$ROOT/informes"

N_RONDAS="${1:-5}"

mkdir -p "$INFORMES"

TMPDIR="$(mktemp -d)"
trap 'rm -rf "$TMPDIR"' EXIT

echo "== Paso 1: autocomprobacion individual (--test) de cada implementacion =="
python3 "$PY" --test
node "$JS" --test
echo

# ---------------------------------------------------------------------------
# Extrae de la salida completa de un programa la linea "Entropia : <hex>" y
# la lista de 12 palabras, y las junta en un solo string "hex|palabras".
# Las dos implementaciones imprimen ambas cosas con el mismo formato
# ("Entropia : ..." y "NN. palabra"), asi que un mismo grep/awk vale para
# las dos.
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
    printf '**OK** - las 2 implementaciones producen la misma entropia y las mismas 12 palabras.\n\n'
  else
    printf '**DESAJUSTE** - alguna implementacion difiere. No uses este metodo hasta resolverlo.\n\n'
  fi

  printf '## Comandos ejecutados\n\n'
  printf 'Lanzados desde la raiz del repositorio, con las tiradas de arriba como entrada estandar.\n\n'

  printf '### Python\n\n'
  printf '```bash\necho %s | python3 scripts/python/bip39_dados.py\n```\n\n' "$tiradas"

  printf '### JavaScript / Node.js\n\n'
  printf '```bash\necho %s | node scripts/javascript/bip39_dados.js\n```\n\n' "$tiradas"

  printf '## Palabras resultantes\n\n'
  printf '| Implementacion | Palabras (1 -> 12) |\n'
  printf '|---|---|\n'
  printf '| Python | %s |\n' "$pal_py"
  printf '| JavaScript | %s |\n\n' "$pal_js"

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

  r_py="$(extraer "$salida_py")"
  r_js="$(extraer "$salida_js")"

  hex_py="${r_py%%|*}"; pal_py="${r_py#*|}"
  hex_js="${r_js%%|*}"; pal_js="${r_js#*|}"

  if [[ "$r_py" == "$r_js" ]]; then
    coincide="si"
    informe="$INFORMES/${hex_py}.md"
  else
    coincide="no"
    fallos=$((fallos + 1))
    informe="$INFORMES/${hex_py}_DESAJUSTE.md"
  fi

  generar_informe "$i" "$tiradas" "$coincide" \
    "$hex_py" "$pal_py" "$hex_js" "$pal_js" > "$informe"

  if [[ "$coincide" == "si" ]]; then
    echo "ronda $i: OK  (entropia $hex_py) -> ${informe#"$ROOT"/}"
  else
    echo "ronda $i: FALLO -> ${informe#"$ROOT"/}"
    echo "  tiradas : $tiradas"
    echo "  python  : $r_py"
    echo "  node    : $r_js"
  fi
done

echo
if [[ $fallos -eq 0 ]]; then
  echo "OK: $N_RONDAS rondas, las 2 implementaciones coincidieron siempre."
  echo "Informes en: $INFORMES"
  exit 0
else
  echo "FALLO: $fallos de $N_RONDAS rondas no coincidieron."
  echo "Informes en: $INFORMES"
  exit 1
fi
