# Instrucciones de uso — C#

A diferencia de Python y Node, C# hay que compilarlo antes de ejecutarlo.
Elige **una** de las tres vías según lo que tengas instalado.

## Opción A — .NET 6, 7, 8 o 9 (la más habitual)

```bash
dotnet new console -o bip39
cp bip39_dados.cs bip39/Program.cs      # sustituye el Program.cs generado
cd bip39
dotnet run -- --test
dotnet run                              # modo normal
dotnet run -- --detalle
```

Puede aparecer el aviso `CS8600` al compilar (`Console.ReadLine()` devuelve
`string?` porque la plantilla activa `<Nullable>enable</Nullable>`). Es solo
un aviso: compila y funciona igual.

## Opción B — .NET 10 o superior (sin crear proyecto)

```bash
dotnet run bip39_dados.cs -- --test
dotnet run bip39_dados.cs
dotnet run bip39_dados.cs -- --detalle
```

## Opción C — Mono (cualquier Unix, sin instalar .NET)

```bash
mcs -r:System.Numerics.dll -out:bip39_dados.exe bip39_dados.cs
mono bip39_dados.exe --test
mono bip39_dados.exe
mono bip39_dados.exe --detalle
```

El `-r:System.Numerics.dll` es obligatorio en Mono; con `dotnet` no hace
falta.

## Cómo introducir las tiradas

Puedes escribirlas de una en una o varias juntas, separadas por espacios:

```
[ 0/50] > 3 1 6 4 2 5 6 1 ...
```

Cualquier carácter que no sea `1`-`6` se ignora con un aviso, salvo espacios,
comas (`,`), puntos y comas (`;`), guiones (`-`) y puntos (`.`), que se
ignoran en silencio.

## `--detalle`

Imprime el número `N` en base 6, el recorte a 128 bits, el cálculo del
checksum y los 12 grupos de 11 bits. Es solo para **aprender y enseñar el
método**: no lo uses con tiradas de una semilla real.

## Seguridad

- Ejecútalo en un ordenador **sin red**, idealmente arrancado desde un USB live.
- **No pases las tiradas como argumento** de línea de comandos: quedarían
  guardadas en el historial de tu shell.
- Apunta las palabras **en papel**, en el orden en que salen. Nada de fotos
  ni ficheros.
- Al terminar, borra la pantalla (`clear` o `cls`).
