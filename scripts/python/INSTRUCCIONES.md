# Instrucciones de uso — Python

Ejecuta siempre desde esta carpeta (o pasa la ruta completa al fichero).

```bash
python3 bip39_dados.py --test      # autocomprobación: vectores oficiales de BIP-39
python3 bip39_dados.py             # modo normal: pide las 50 tiradas por teclado
python3 bip39_dados.py --detalle   # además, muestra todos los pasos intermedios
```

## Cómo introducir las tiradas

Puedes escribirlas de una en una o varias juntas, separadas por espacios:

```
[ 0/50] > 3 1 6 4 2 5 6 1 ...
```

Cualquier carácter que no sea `1`-`6` se ignora con un aviso, salvo espacios,
comas (`,`), puntos y comas (`;`), guiones (`-`) y puntos (`.`), que se ignoran
en silencio.

## `--detalle`

Imprime el número `N` en base 6, el recorte a 128 bits, el cálculo del
checksum y los 12 grupos de 11 bits. Es solo para **aprender y enseñar el
método**: no lo uses con tiradas de una semilla real, porque llena la
pantalla de material sensible sin necesidad.

## Seguridad

- Ejecútalo en un ordenador **sin red**, idealmente arrancado desde un USB live.
- **No pases las tiradas como argumento** de línea de comandos: quedarían
  guardadas en el historial de tu shell.
- Apunta las palabras **en papel**, en el orden en que salen. Nada de fotos
  ni ficheros.
- Al terminar, borra la pantalla (`clear` o `reset`).
