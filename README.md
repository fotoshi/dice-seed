# dice-seed

**Una semilla BIP-39 de 12 palabras a partir de 50 tiradas de un dado de 6 caras.**
Un script en Python, sin dependencias, comentado paso a paso para poder auditarlo
y enseñarlo.

**A 12-word BIP-39 seed from 50 rolls of a six-sided die.** A single Python
script, no dependencies, commented step by step so it can be audited and taught.

🌐 **[fotoshi.github.io/dice-seed](https://fotoshi.github.io/dice-seed)** —
explicación interactiva del método / interactive walkthrough

[**Español**](#español) · [**English**](#english)

> ⚠️ **Ejecuta esto en un ordenador sin conexión a internet.**
> **Run this on a computer with no internet connection.**

---


# Español

## Qué es esto

Cuando una cartera genera una semilla por ti, confías en que su generador de
números aleatorios es bueno y honesto. No puedes comprobar ninguna de las dos
cosas. Con un dado sí: la aleatoriedad la produces tú, delante de ti, y cada paso
del cálculo se puede verificar a mano.

Este repositorio contiene el algoritmo en **Python**, más una web didáctica que
explica el método. No hay dependencias externas: solo la biblioteca estándar de
Python. El script está dividido en dos grupos: primero la generación de la
entropía y luego la de las 12 palabras.

## Aviso de seguridad

- Ejecútalo en un ordenador **sin red**, idealmente arrancado desde un USB live.
- **No pases las tiradas como argumento**: quedarían en el historial del shell.
- Apunta las palabras **en papel**, en orden. Nada de fotos ni ficheros.
- Usa un **dado de casino**, con aristas vivas, sobre una superficie dura.
- Borra la pantalla al terminar (`clear` o `reset`).
- Prueba a **restaurar la semilla** en una cartera vacía antes de confiarle nada.

La web incluye una lista de comprobación completa.

## Empezar rápido

```bash
cd scripts/python
python3 bip39_dados.py             # pedir las 50 tiradas
python3 bip39_dados.py --detalle   # mostrar todos los pasos intermedios
```

La carpeta `scripts/python/` incluye además un `REQUISITOS.md` con lo necesario
para ejecutarlo y un `INSTRUCCIONES.md` con el paso a paso.

El modo `--detalle` imprime N, el recorte a 128 bits, el checksum y los 12 grupos
de bits. Es para **aprender y enseñar**, no para generar una semilla real.

## El método en cinco pasos

```
50 tiradas de d6
      │  1. leer las tiradas como un número en base 6
      ▼
número entero N  (~129,25 bits)
      │  2. quedarse con los 128 bits bajos
      ▼
entropía = 16 bytes
      │  3. checksum = 4 primeros bits de SHA-256(entropía)
      ▼
132 bits = 128 + 4
      │  4. escribir esos 132 bits como número en base 2048
      ▼
12 índices (0–2047)
      │  5. buscar cada índice en la lista oficial
      ▼
12 palabras
```

Los pasos 1 y 4 son **la misma operación en sentidos opuestos**: una conversión de
base. Primero *leemos* los dados en base 6; después *escribimos* el resultado en
base 2048. La lista tiene 2048 = 2¹¹ palabras, por eso cada una vale 11 bits y
132 = 12 × 11 sin que sobre nada.

Explicación completa en [`tutorial_bip39_dados.md`](tutorial_bip39_dados.md), con
el código comentado línea a línea.

## Verificar antes de usar

Todo se publica firmado por **ADN**. Verifica esta huella en varios canales antes
de confiar en nada:

```
05E6 6020 81F7 AFD7 2957  36B4 D37C BA46 955B 9F95
```

| Canal | Dónde |
|---|---|
| Clave pública | [`ADN-clave-publica.asc`](ADN-clave-publica.asc) |
| Servidor de claves | `gpg --keyserver keys.openpgp.org --recv-keys D37CBA46955B9F95` |
| Nostr | `npub1gke055lhcwndepddgzl4gdcgy86h0g5vgsjsmtpl44mftt6zyn9s8wk7e5` |

```bash
gpg --import ADN-clave-publica.asc
gpg --verify SHA256SUMS.asc SHA256SUMS

shasum -a 256 -c SHA256SUMS      # macOS
sha256sum -c SHA256SUMS          # Linux
```

**El aviso `WARNING: This key is not certified with a trusted signature` es
normal.** Solo significa que no has marcado la clave como de confianza en tu
llavero, algo que casi nadie hace. Lo que importa es que ponga `Good signature`,
que la huella coincida y que la identidad sea exactamente
`ADN <fotoshi@protonmail.com>`.

`SHA256SUMS` es la referencia autoritativa de los hashes. Detalles del proceso en
[`FIRMAR.md`](FIRMAR.md).

## Requisitos

| Lenguaje | Mínimo | Notas |
|---|---|---|
| Python | 3.2 | Preinstalado en macOS y Linux |

No necesita instalar paquetes.

## Diferencias con otras herramientas

**BIP-39 estandariza el paso entropía → palabras, pero no estandariza el paso
dados → entropía.** Cada implementación elige ahí, así que dos herramientas
correctas pueden dar palabras distintas con las mismas tiradas.

La herramienta de Ian Coleman usa desde su versión 0.5.0 (octubre de 2020) un
método sin sesgo que consume ~1,667 bits por tirada, frente a los 2,585 de este:
necesita unas 77 tiradas donde aquí bastan 50.

**No metas tus 50 tiradas en otra herramienta esperando las mismas palabras.**
Para verificación cruzada, introduce la **entropía en hexadecimal** que imprime el
script.

El script incluye un bloque comentado de rechazo que elimina el sesgo por
completo, a cambio de repetir las 50 tiradas el 15,8 % de las veces (~59 tiradas
esperadas, aun así menos que 77).

## Contenido

| Fichero | Qué es |
|---|---|
| `scripts/python/bip39_dados.py` | Implementación en Python 3 |
| `scripts/python/REQUISITOS.md` | Qué necesitas instalado |
| `scripts/python/INSTRUCCIONES.md` | Cómo lanzar el script |
| `index.html` | Web didáctica bilingüe, autocontenida |
| `tutorial_bip39_dados.md` | Tutorial largo con el código explicado |
| `FIRMAR.md` | Cómo se firma y se verifica este repositorio |
| `ADN-clave-publica.asc` | Clave pública GPG |
| `SHA256SUMS` · `.asc` · `.ots` | Hashes, firma y sello temporal |

`index.html` lleva el script incrustado y calcula su SHA-256 en el navegador.
**Puedes guardarlo y llevártelo al ordenador sin red**: funciona entero sin
conexión, con explicación, código y descarga.

## Contribuir

Se agradecen especialmente:

- **Revisiones del código.** Es criptografía: cuantos más ojos, mejor.
- **Traducciones** a otros idiomas.
- **Implementaciones en otros lenguajes**, siempre que usen solo la biblioteca
  estándar y reproduzcan los vectores oficiales de BIP-39.

Toda propuesta debe reproducir los vectores oficiales de BIP-39 antes de
enviarse. Si encuentras un fallo de seguridad, escribe en privado antes de
publicarlo.

## Licencia

MIT. Haz lo que quieras con esto: es contenido para la comunidad.

**Sin garantía de ningún tipo.** Eres responsable de auditar el código que
ejecutas y de custodiar tu propia semilla. Nadie puede recuperarla por ti.

---

# English

## What this is

When a wallet generates a seed for you, you're trusting that its random number
generator is both sound and honest. You can verify neither. With a die you can:
you produce the randomness yourself, in front of you, and every step of the
calculation can be checked by hand.

This repository holds the algorithm in **Python**, plus a teaching site that
explains the method. No external dependencies — Python's standard library only.
The script is split into two groups: first the entropy generation, then the 12
words.

## Safety notice

- Run it on a computer **with no network**, ideally booted from a live USB.
- **Don't pass the rolls as arguments**: they'd land in your shell history.
- Write the words **on paper**, in order. No photos, no files.
- Use a **casino-grade die** with sharp edges, on a hard surface.
- Clear the screen afterwards (`clear` or `reset`).
- **Test-restore the seed** into an empty wallet before trusting it with anything.

The site has a full checklist.

## Quick start

```bash
cd scripts/python
python3 bip39_dados.py             # prompt for the 50 rolls
python3 bip39_dados.py --detalle   # print every intermediate value
```

The `scripts/python/` folder also ships a `REQUISITOS.md` with what you need
installed to run it, and an `INSTRUCCIONES.md` with the step-by-step.

`--detalle` prints N, the trim to 128 bits, the checksum and the 12 bit groups.
It's for **learning and teaching**, not for generating a real seed.

## The method in five steps

```
50 rolls of a d6
      │  1. read the rolls as a base-6 number
      ▼
integer N  (~129.25 bits)
      │  2. keep the low 128 bits
      ▼
entropy = 16 bytes
      │  3. checksum = first 4 bits of SHA-256(entropy)
      ▼
132 bits = 128 + 4
      │  4. write those 132 bits as a base-2048 number
      ▼
12 indices (0–2047)
      │  5. look each index up in the official list
      ▼
12 words
```

Steps 1 and 4 are **the same operation in opposite directions**: a base
conversion. First we *read* the dice in base 6; then we *write* the result in
base 2048. The list holds 2048 = 2¹¹ words, which is why each is worth 11 bits
and 132 = 12 × 11 with nothing left over.

Full walkthrough in [`tutorial_bip39_dados.md`](tutorial_bip39_dados.md)
(Spanish), with the code explained line by line.

## Verify before you use it

Everything is published signed by **ADN**. Check this fingerprint across several
channels before trusting anything:

```
05E6 6020 81F7 AFD7 2957  36B4 D37C BA46 955B 9F95
```

| Channel | Where |
|---|---|
| Public key | [`ADN-clave-publica.asc`](ADN-clave-publica.asc) |
| Key server | `gpg --keyserver keys.openpgp.org --recv-keys D37CBA46955B9F95` |
| Nostr | `npub1gke055lhcwndepddgzl4gdcgy86h0g5vgsjsmtpl44mftt6zyn9s8wk7e5` |

```bash
gpg --import ADN-clave-publica.asc
gpg --verify SHA256SUMS.asc SHA256SUMS

shasum -a 256 -c SHA256SUMS      # macOS
sha256sum -c SHA256SUMS          # Linux
```

**The `WARNING: This key is not certified with a trusted signature` notice is
normal.** It only means you haven't marked the key as trusted in your keyring,
which almost nobody does. What matters is that it says `Good signature`, that the
fingerprint matches, and that the identity reads exactly
`ADN <fotoshi@protonmail.com>`.

`SHA256SUMS` is the authoritative hash reference. Process details in
[`FIRMAR.md`](FIRMAR.md) (Spanish).

## Requirements

| Language | Minimum | Notes |
|---|---|---|
| Python | 3.2 | Preinstalled on macOS and Linux |

It requires no package installation.

## How this differs from other tools

**BIP-39 standardises the entropy → words step, but it does not standardise
dice → entropy.** Every implementation chooses there, so two correct tools can
produce different words from the same rolls.

Since version 0.5.0 (October 2020), Ian Coleman's tool uses an unbiased method
that yields ~1.667 bits per roll, against 2.585 here: it needs about 77 rolls
where this needs 50.

**Don't feed your 50 rolls into another tool expecting the same words.** For
cross-checking, enter the **hex entropy** the script prints.

The script ships a commented-out rejection block that removes the bias entirely,
at the cost of re-rolling all 50 dice 15.8% of the time (~59 expected rolls,
still fewer than 77).

## Contents

| File | What it is |
|---|---|
| `scripts/python/bip39_dados.py` | Python 3 implementation |
| `scripts/python/REQUISITOS.md` | What you need installed |
| `scripts/python/INSTRUCCIONES.md` | How to run the script |
| `index.html` | Bilingual teaching site, self-contained |
| `tutorial_bip39_dados.md` | Long-form tutorial (Spanish) |
| `FIRMAR.md` | How this repository is signed and verified (Spanish) |
| `ADN-clave-publica.asc` | GPG public key |
| `SHA256SUMS` · `.asc` · `.ots` | Hashes, signature and timestamp |

`index.html` embeds the script and computes its SHA-256 in the browser.
**You can save it and carry it to your offline computer**: it works entirely
without a connection — explanation, code and downloads included.

## Contributing

Especially welcome:

- **Code review.** This is cryptography: the more eyes, the better.
- **Translations** into other languages.
- **Ports to other languages**, as long as they use only the standard library
  and reproduce the official BIP-39 vectors.

Any proposal must reproduce the official BIP-39 vectors before submission. If you
find a security issue, please write privately before disclosing it.

## Licence

MIT. Do whatever you like with it — this is community material.

**No warranty of any kind.** You are responsible for auditing the code you run
and for safeguarding your own seed. Nobody can recover it for you.
