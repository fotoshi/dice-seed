# Requisitos — C#

Necesitas **una** de estas dos opciones (no ambas):

| Opción | Mínimo | Notas |
|---|---|---|
| .NET SDK | 6, 7, 8 o 9 | https://dotnet.microsoft.com/download — el más habitual hoy |
| Mono | cualquier versión reciente | No requiere crear un proyecto; compila el fichero suelto |

- **Ninguna dependencia externa ni paquete NuGet.** El script solo usa
  `System.Numerics.BigInteger` y `System.Security.Cryptography.SHA256`,
  disponibles desde .NET Framework 4.0 / .NET Core 1.0.
- Compila con `-langversion:6` (C# 6, del 2015): cualquier compilador de los
  últimos diez años lo acepta sin cambios.

Comprueba qué tienes instalado:

```bash
dotnet --version   # o
mono --version
```

Con .NET 10 o superior también puedes ejecutar el fichero suelto sin crear
proyecto (`dotnet run bip39_dados.cs`); con .NET 6-9 hace falta el paso de
`dotnet new console` que se describe en `INSTRUCCIONES.md`.
