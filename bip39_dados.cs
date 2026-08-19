// bip39_dados.cs - 12 palabras BIP39 a partir de 50 tiradas de un dado de 6 caras.
//
// COMPILAR Y EJECUTAR:
//
//     .NET 6, 7, 8 o 9 (lo habitual hoy):
//         dotnet new console -o bip39
//         cp bip39_dados.cs bip39/Program.cs      # sustituye el generado
//         cd bip39 && dotnet run -- --test
//       Puede salir el aviso CS8600 en Console.ReadLine(): las plantillas
//       activan <Nullable>enable</Nullable> y ese metodo devuelve string?.
//       Es solo un AVISO, compila y funciona. No se usa "string?" aqui para
//       mantener la compatibilidad con compiladores antiguos.
//
//     .NET 10 o superior (permite ficheros sueltos, sin proyecto):
//         dotnet run bip39_dados.cs --test
//
//     Mono (cualquier Unix, sin instalar .NET):
//         mcs -r:System.Numerics.dll -out:bip39_dados.exe bip39_dados.cs
//         mono bip39_dados.exe --test
//       El -r:System.Numerics.dll es obligatorio en Mono; con dotnet no.
//
// REQUISITOS MINIMOS:
//     Este fichero compila con -langversion:6 (C# 6, ano 2015) y solo usa
//     BigInteger y SHA256, ambos disponibles desde .NET Framework 4.0.
//     Cualquier .NET de los ultimos diez anyos lo ejecuta sin cambios.
//
// USO:
//     bip39_dados                # pide las tiradas por teclado
//     bip39_dados --detalle      // ademas, muestra los pasos intermedios
//     bip39_dados --test         // autocomprobacion con vectores oficiales
//
// EL METODO EN CINCO PASOS:
//
//     50 tiradas de d6
//           |  PASO 1: leer las tiradas como un numero en base 6
//           v
//     numero entero N  (unos 129,25 bits)
//           |  PASO 2: quedarse con los 128 bits bajos
//           v
//     entropia = 16 bytes = 128 bits
//           |  PASO 3: checksum = 4 primeros bits de SHA-256(entropia)
//           v
//     132 bits = 128 + 4
//           |  PASO 4: escribir esos 132 bits como un numero en base 2048
//           v
//     12 indices (0-2047)
//           |  PASO 5: buscar cada indice en la lista oficial
//           v
//     12 palabras
//
//     Los pasos 1 y 4 son la MISMA operacion en sentidos opuestos: una
//     conversion de base. Primero LEEMOS los dados en base 6, luego ESCRIBIMOS
//     el resultado en base 2048.
//
// NOTA SOBRE C#:
//     long solo llega a 64 bits y nuestro N tiene 129, asi que usamos
//     System.Numerics.BigInteger. A diferencia de Java, BigInteger en C# TIENE
//     SOBRECARGA DE OPERADORES: puedes escribir "n = n * 6 + d" tal cual, sin
//     llamadas a .Multiply() ni .Add(). Eso hace que el bucle de Horner se lea
//     casi igual que en Python.
//
//     CUIDADO con ToByteArray(): devuelve los bytes en LITTLE ENDIAN y anyade
//     un byte de signo si el bit mas alto vale 1. Para 2^128-1 devuelve 17
//     bytes, no 16. Por eso este programa NO lo usa directamente: los helpers
//     ABytesBigEndian y DeBytesBigEndian de abajo hacen la conversion a mano.
//     Es la trampa numero uno al portar este algoritmo a C#.
//
// SEGURIDAD:
//     - Ejecutalo en un ordenador SIN RED, idealmente arrancado desde un USB live.
//     - No pases las tiradas como argumento: quedarian en el historial del shell.
//     - Apunta las palabras en papel. Borra la pantalla al terminar.
//     - El modo --detalle es para APRENDER, no para una semilla real.
//
// Dependencias: ninguna, solo la biblioteca estandar de .NET.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

static class Bip39Dados
{
    // ========================================================================
    //  CONSTANTES - de donde sale cada numero
    // ========================================================================

    // Un dado de 6 caras aporta log2(6) = 2,585 bits por tirada.
    // 128 / 2,585 = 49,5 -> redondeando hacia arriba, 50 tiradas.
    const int N_TIRADAS = 50;

    // BIP39 admite 128, 160, 192, 224 o 256 bits. 128 es el estandar de 12 palabras.
    const int BITS_ENTROPIA = 128;

    // La especificacion fija: bits de checksum = bits de entropia / 32.
    const int BITS_CHECKSUM = BITS_ENTROPIA / 32;            // 4

    // La lista oficial tiene 2048 palabras, y 2048 = 2^11.
    const int PALABRAS_EN_LISTA = 2048;
    const int BITS_POR_PALABRA = 11;

    // 128 + 4 = 132, y 132 / 11 = 12 exactas. No sobra ni falta un bit.
    const int N_PALABRAS = (BITS_ENTROPIA + BITS_CHECKSUM) / BITS_POR_PALABRA;   // 12

    const int BYTES_ENTROPIA = BITS_ENTROPIA / 8;            // 16

    // BigInteger.Pow(2, 128). Se calcula una vez al arrancar.
    static readonly BigInteger DOS_ELEVADO_A_128 = BigInteger.Pow(2, BITS_ENTROPIA);


    // ========================================================================
    //  PASOS 1 y 2 - de las tiradas a la entropia
    // ========================================================================

    /// <summary>Convierte 50 enteros del 1 al 6 en 16 bytes de entropia.</summary>
    static byte[] TiradasAEntropia(IList<int> tiradas)
    {
        if (tiradas.Count != N_TIRADAS)
            throw new ArgumentException(
                $"hacen falta exactamente {N_TIRADAS} tiradas, recibidas {tiradas.Count}");

        // ---- PASO 1: las tiradas son los digitos de un numero en base 6 ----
        //
        // El dado no tiene cara 0, asi que restamos 1 para pasar de 1..6 a 0..5.
        // El numero que formamos es:
        //
        //     N = d1*6^49 + d2*6^48 + ... + d49*6^1 + d50*6^0
        //
        // pero no calculamos potencias: usamos el METODO DE HORNER, que
        // multiplica el acumulado por 6 y le suma el digito nuevo.
        // Con las tiradas 3,1,6,4:
        //
        //     n=0 -> 0*6+2 = 2 -> 2*6+0 = 12 -> 12*6+5 = 77 -> 77*6+3 = 465
        //
        // OJO: la PRIMERA tirada acaba siendo el digito MAS SIGNIFICATIVO.
        // Cambiar el orden produce una semilla completamente distinta.
        //
        BigInteger n = BigInteger.Zero;
        foreach (int tirada in tiradas)
        {
            if (tirada < 1 || tirada > 6)
                throw new ArgumentException($"tirada fuera del rango 1-6: {tirada}");
            n = n * 6 + (tirada - 1);       // sobrecarga de operadores: se lee solo
        }

        // ---- PASO 2: recortar a exactamente 128 bits -----------------------
        //
        // N puede llegar hasta 6^50 - 1, que es 2,375 veces mayor que 2^128.
        //
        // SOBRE EL SESGO: como 6^50 no es multiplo exacto de 2^128, algunos
        // resultados salen 3 veces y otros 2. La entropia real baja de 128 a
        // 127,97 bits. Son 0,03 bits: irrelevante.
        //
        // Si lo quieres perfecto, descomenta estas dos lineas. Solo aceptan N
        // por debajo de 2^129, rango donde 2^128 cabe justo 2 veces y el modulo
        // es uniforme. Precio: repetir las 50 tiradas el 15,8% de las veces.
        //
        // if (n >= BigInteger.Pow(2, BITS_ENTROPIA + 1))
        //     throw new ArgumentException("descarta estas tiradas y vuelve a tirar los 50 dados");

        n %= DOS_ELEVADO_A_128;

        return ABytesBigEndian(n, BYTES_ENTROPIA);
    }

    /// <summary>
    /// Empaqueta un BigInteger no negativo en un array de tamanyo fijo, byte mas
    /// significativo primero ("big endian").
    ///
    /// NO usamos ToByteArray() porque devuelve LITTLE ENDIAN y anyade un byte de
    /// signo cuando el bit alto vale 1 (para 2^128-1 devolveria 17 bytes). En
    /// .NET Core 3.0+ existe ToByteArray(isUnsigned: true, isBigEndian: true),
    /// pero lo hacemos a mano para que el codigo funcione tambien en Mono y en
    /// .NET Framework, y sobre todo para que se VEA lo que ocurre.
    /// </summary>
    static byte[] ABytesBigEndian(BigInteger n, int nBytes)
    {
        if (n.Sign < 0) throw new ArgumentException("solo numeros no negativos");
        var bytes = new byte[nBytes];
        for (int i = nBytes - 1; i >= 0; i--)     // se rellena desde el final
        {
            bytes[i] = (byte)(n & 0xFF);          // los 8 bits mas bajos
            n >>= 8;                              // y se descartan
        }
        if (!n.IsZero) throw new ArgumentException($"el numero no cabe en {nBytes} bytes");
        return bytes;
    }

    /// <summary>
    /// Convierte bytes big endian en BigInteger. Tampoco usamos el constructor
    /// new BigInteger(byte[]) porque espera little endian e interpreta el bit
    /// alto como signo: un array que empiece por 0xFF saldria NEGATIVO.
    /// </summary>
    static BigInteger DeBytesBigEndian(byte[] bytes)
    {
        BigInteger n = BigInteger.Zero;
        foreach (byte b in bytes) n = (n << 8) | b;   // Horner, pero en base 256
        return n;
    }


    // ========================================================================
    //  PASO 3 - el checksum
    // ========================================================================

    /// <summary>Devuelve los 4 bits de checksum como un int de 0 a 15.</summary>
    static int CalcularChecksum(byte[] entropia)
    {
        // Los 4 bits mas altos del PRIMER BYTE del SHA-256 de la entropia.
        //
        //     SHA-256(entropia) = 05 fd a8 7a ...
        //                         ^^
        //                    primer byte = 0x05 = 00000101
        //                                         ^^^^
        //                                    estos 4 son el checksum
        //
        // Pasamos por el byte porque C#, como casi todos los lenguajes, no sabe
        // indexar BITS: la unidad minima direccionable de un array es el byte.
        //
        // Estos bits no dan seguridad. Sirven para que, si copias mal una
        // palabra, la cartera rechace la frase (15 de cada 16 veces) en vez de
        // abrir en silencio una cuenta vacia que no es la tuya.
        using (var sha = SHA256.Create())
        {
            byte[] digest = sha.ComputeHash(entropia);
            return digest[0] >> (8 - BITS_CHECKSUM);
        }
    }


    // ========================================================================
    //  PASOS 4 y 5 - de la entropia a las palabras
    // ========================================================================

    /// <summary>Convierte 16 bytes de entropia en los 12 indices (0-2047).</summary>
    static int[] EntropiaAIndices(byte[] entropia)
    {
        if (entropia.Length != BYTES_ENTROPIA)
            throw new ArgumentException(
                $"la entropia debe ocupar {BYTES_ENTROPIA} bytes, recibidos {entropia.Length}");

        // ---- Juntar entropia y checksum en un solo numero de 132 bits ------
        //
        //     [ ---------- 128 bits de entropia ---------- ][chk]
        //      \___________________ 132 bits ___________________/
        //
        // "<< 4" corre la entropia 4 posiciones a la izquierda, dejando 4 ceros
        // al final. El "|" (OR bit a bit) rellena justo esos huecos.
        BigInteger v = DeBytesBigEndian(entropia) << BITS_CHECKSUM;
        v |= CalcularChecksum(entropia);

        // ---- PASO 4: escribir los 132 bits como un numero en base 2048 -----
        //
        // Esto es EXACTAMENTE la operacion inversa del paso 1. Alli leiamos
        // digitos en base 6 y construiamos un numero; aqui cogemos un numero y
        // extraemos sus digitos en base 2048. Cada "digito" es un indice.
        //
        //   v = 1746·2048^11 + 1100·2048^10 + ... + 723·2048^1 + 896·2048^0
        //        \____/                                            \___/
        //      1ª palabra                                     12ª palabra
        //
        // BigInteger.DivRem da cociente y resto de una sola pasada, igual que
        // el divmod de Python. El resto son los 11 bits bajos: el digito de
        // menor peso. Como salen del reves, se rellena el array desde el final.
        //
        var indices = new int[N_PALABRAS];
        for (int i = N_PALABRAS - 1; i >= 0; i--)
        {
            BigInteger resto;
            v = BigInteger.DivRem(v, PALABRAS_EN_LISTA, out resto);
            indices[i] = (int)resto;
        }

        // Tras 12 divisiones no debe quedar nada: 12 x 11 = 132 bits, justos.
        if (!v.IsZero)
            throw new InvalidOperationException("quedaron bits sin repartir: revisa las constantes");

        return indices;
    }

    /// <summary>Convierte 16 bytes de entropia en las 12 palabras BIP39.</summary>
    static string[] EntropiaAPalabras(byte[] entropia)
    {
        // ---- PASO 5: cada indice es una posicion directa en la lista -------
        return EntropiaAIndices(entropia).Select(i => PALABRAS[i]).ToArray();
    }


    // ========================================================================
    //  ENTRADA - leer las tiradas del usuario
    // ========================================================================

    static List<int> LeerTiradas()
    {
        Console.WriteLine($"Introduce {N_TIRADAS} tiradas de un dado de 6 caras.");
        Console.WriteLine("Puedes escribirlas de una en una o varias juntas (ej: 3 1 6 4 2).");
        Console.WriteLine("Ctrl-C para abortar.\n");

        var tiradas = new List<int>();
        while (tiradas.Count < N_TIRADAS)
        {
            Console.Write($"[{tiradas.Count,2}/{N_TIRADAS}] > ");
            string linea = Console.ReadLine();
            if (linea == null) break;              // fin de la entrada canalizada

            foreach (char c in linea)
            {
                if (c >= '1' && c <= '6')
                {
                    tiradas.Add(c - '0');
                    if (tiradas.Count == N_TIRADAS) break;
                }
                else if (!char.IsWhiteSpace(c) && ",;-.".IndexOf(c) < 0)
                {
                    // Avisamos en vez de callar: un '7' o una 'o' suele ser un
                    // error de tecleo que cambiaria la semilla sin que te enteres.
                    Console.WriteLine($"  aviso: ignorado el caracter '{c}'");
                }
            }
        }

        if (tiradas.Count != N_TIRADAS)
            throw new InvalidOperationException(
                $"solo se leyeron {tiradas.Count} tiradas de {N_TIRADAS}. Abortado.");
        return tiradas;
    }


    // ========================================================================
    //  SALIDA
    // ========================================================================

    static string AHex(byte[] bytes)
    {
        var sb = new StringBuilder();
        foreach (byte b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    /// <summary>Representacion binaria de un BigInteger, sin ceros a la izquierda.</summary>
    static string ABinario(BigInteger n)
    {
        if (n.IsZero) return "0";
        var sb = new StringBuilder();
        while (!n.IsZero) { sb.Insert(0, (char)('0' + (int)(n & 1))); n >>= 1; }
        return sb.ToString();
    }

    static void MostrarResultado(IList<int> tiradas, byte[] entropia, string[] palabras)
    {
        string raya = new string('=', 52);
        Console.WriteLine("\n" + raya);
        Console.WriteLine("Tiradas  : " + string.Concat(tiradas));
        Console.WriteLine("Entropia : " + AHex(entropia));
        Console.WriteLine(raya);
        for (int i = 0; i < palabras.Length; i++)
            Console.WriteLine($"{i + 1,2}. {palabras[i]}");
        Console.WriteLine(raya);
        Console.WriteLine("Apuntalas en papel, en este orden. Nunca en un fichero ni en una foto.");
        Console.WriteLine("Cuando termines: limpia la pantalla (clear / cls).");
    }

    /// <summary>Imprime todos los valores intermedios. Solo para aprender y enseñar.</summary>
    static void MostrarDetalle(IList<int> tiradas, byte[] entropia)
    {
        // Rehacemos el paso 1 aqui para poder enseñar N, que la funcion
        // principal no devuelve. Mantener limpias las funciones de calculo y
        // separar la pedagogia es mejor que llenarlas de Console.WriteLine.
        BigInteger n = BigInteger.Zero;
        foreach (int t in tiradas) n = n * 6 + (t - 1);

        int checksum = CalcularChecksum(entropia);
        int[] indices = EntropiaAIndices(entropia);
        string bitsEntropia = ABinario(DeBytesBigEndian(entropia)).PadLeft(BITS_ENTROPIA, '0');
        string bitsChecksum = Convert.ToString(checksum, 2).PadLeft(BITS_CHECKSUM, '0');
        string bitsTotales = bitsEntropia + bitsChecksum;

        byte[] digest;
        using (var sha = SHA256.Create()) digest = sha.ComputeHash(entropia);

        string raya = new string('-', 52);

        Console.WriteLine("\n" + raya);
        Console.WriteLine("PASO 1 - las tiradas como numero en base 6");
        Console.WriteLine(raya);
        Console.WriteLine("  digitos (tirada - 1): " + string.Concat(tiradas.Select(t => t - 1)));
        Console.WriteLine("  N  = " + n);
        Console.WriteLine("  N ocupa " + ABinario(n).Length + " bits");

        Console.WriteLine("\n" + raya);
        Console.WriteLine($"PASO 2 - recorte a {BITS_ENTROPIA} bits");
        Console.WriteLine(raya);
        Console.WriteLine($"  2^{BITS_ENTROPIA} = {DOS_ELEVADO_A_128}");
        Console.WriteLine(n < DOS_ELEVADO_A_128
            ? $"  N ya era menor que 2^{BITS_ENTROPIA}: el modulo no cambia nada"
            : $"  N era mayor: se le resta 2^{BITS_ENTROPIA}");
        Console.WriteLine("  entropia (hex) = " + AHex(entropia));
        Console.WriteLine("  entropia (bin) = " + bitsEntropia);

        Console.WriteLine("\n" + raya);
        Console.WriteLine("PASO 3 - checksum");
        Console.WriteLine(raya);
        Console.WriteLine("  SHA-256(entropia) = " + AHex(digest).Substring(0, 16) + "...");
        Console.WriteLine($"  primer byte = 0x{digest[0]:x2} = {Convert.ToString(digest[0], 2).PadLeft(8, '0')}");
        Console.WriteLine($"  sus {BITS_CHECKSUM} bits altos = {bitsChecksum}   <- checksum");

        Console.WriteLine("\n" + raya);
        Console.WriteLine($"PASOS 4 y 5 - {BITS_ENTROPIA + BITS_CHECKSUM} bits en grupos de {BITS_POR_PALABRA}");
        Console.WriteLine(raya);
        Console.WriteLine("  " + bitsEntropia + "|" + bitsChecksum);
        for (int pos = 0; pos < indices.Length; pos++)
        {
            string grupo = bitsTotales.Substring(pos * BITS_POR_PALABRA, BITS_POR_PALABRA);
            Console.WriteLine($"  {pos + 1,2}. {grupo} = {indices[pos],4} -> {PALABRAS[indices[pos]]}");
        }
        Console.WriteLine($"\n  Los ultimos {BITS_CHECKSUM} bits del grupo {N_PALABRAS} son el checksum de arriba.");
    }


    // ========================================================================
    //  AUTOCOMPROBACION
    // ========================================================================

    static void Comprobar(bool condicion, string mensaje)
    {
        // No usamos Debug.Assert: desaparece al compilar en modo Release, igual
        // que los assert de Python con -O. Una excepcion normal no se puede
        // desactivar por accidente.
        if (!condicion) throw new Exception("FALLO: " + mensaje);
    }

    static void Autotest()
    {
        // La lista incrustada debe ser byte a byte la oficial. Si alguien
        // cambiara una sola letra, este hash no cuadraria.
        Comprobar(PALABRAS.Length == PALABRAS_EN_LISTA, "la lista debe tener 2048 palabras");
        byte[] hashLista;
        using (var sha = SHA256.Create())
            hashLista = sha.ComputeHash(Encoding.ASCII.GetBytes(string.Join("\n", PALABRAS) + "\n"));
        Comprobar(AHex(hashLista) == "2f5eed53a4727b4bf8880d8f3f199efc90e58503646d9ff8eff3a2ed3b24dbda",
            "la lista de palabras NO coincide con la oficial de BIP-39");

        // Vectores de la especificacion (github.com/trezor/python-mnemonic).
        var vectores = new[] {
            new[] { "00000000000000000000000000000000",
                    "abandon abandon abandon abandon abandon abandon " +
                    "abandon abandon abandon abandon abandon about" },
            new[] { "7f7f7f7f7f7f7f7f7f7f7f7f7f7f7f7f",
                    "legal winner thank year wave sausage worth useful " +
                    "legal winner thank yellow" },
            new[] { "80808080808080808080808080808080",
                    "letter advice cage absurd amount doctor acoustic avoid " +
                    "letter advice cage above" },
            new[] { "ffffffffffffffffffffffffffffffff",
                    "zoo zoo zoo zoo zoo zoo zoo zoo zoo zoo zoo wrong" },
            new[] { "9e885d952ad362caeb4efe34a8e91bd2",
                    "ozone drill grab fiber curtain grace pudding thank " +
                    "cruise elder eight picnic" },
        };
        foreach (var v in vectores)
        {
            string obtenido = string.Join(" ", EntropiaAPalabras(DeHex(v[0])));
            Comprobar(obtenido == v[1], $"vector {v[0]}:\n  {obtenido}");
        }

        // 50 unos -> todos los digitos a 0 -> N = 0 -> entropia toda a cero
        Comprobar(AHex(TiradasAEntropia(Repetir(1, 50))) == new string('0', 32),
            "50 unos deberian dar entropia cero");
        // 50 seises -> N = 6^50 - 1, contrastado con el calculo directo
        Comprobar(AHex(TiradasAEntropia(Repetir(6, 50))) ==
            AHex(ABytesBigEndian((BigInteger.Pow(6, 50) - 1) % DOS_ELEVADO_A_128, 16)),
            "50 seises no cuadran con 6^50-1 mod 2^128");
        // El orden importa: la primera tirada pesa 6^49, la ultima 6^0
        var a = Repetir(1, 50); a[0] = 2;
        var b = Repetir(1, 50); b[49] = 2;
        Comprobar(AHex(TiradasAEntropia(a)) != AHex(TiradasAEntropia(b)),
            "el orden de las tiradas deberia importar");
        // Los helpers de bytes deben ser inversos exactos, incluido el bit alto a 1
        byte[] extremo = DeHex("ffffffffffffffffffffffffffffffff");
        Comprobar(AHex(ABytesBigEndian(DeBytesBigEndian(extremo), 16)) == AHex(extremo),
            "ida y vuelta de bytes rota en el caso extremo");
        // Las constantes tienen que encajar sin bits sobrantes
        Comprobar(N_PALABRAS * BITS_POR_PALABRA == BITS_ENTROPIA + BITS_CHECKSUM,
            "las constantes no encajan");

        Console.WriteLine($"OK: {vectores.Length} vectores oficiales y 5 comprobaciones propias superados.");
    }

    static List<int> Repetir(int valor, int veces)
    {
        var lista = new List<int>();
        for (int i = 0; i < veces; i++) lista.Add(valor);
        return lista;
    }

    static byte[] DeHex(string hex)
    {
        var bytes = new byte[hex.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        return bytes;
    }


    // ========================================================================
    //  PROGRAMA PRINCIPAL
    // ========================================================================

    static int Main(string[] args)
    {
        try
        {
            if (Array.IndexOf(args, "--test") >= 0) { Autotest(); return 0; }

            var tiradas = LeerTiradas();
            byte[] entropia = TiradasAEntropia(tiradas);        // pasos 1 y 2
            string[] palabras = EntropiaAPalabras(entropia);    // pasos 3, 4 y 5

            if (Array.IndexOf(args, "--detalle") >= 0) MostrarDetalle(tiradas, entropia);
            MostrarResultado(tiradas, entropia, palabras);
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("\nERROR: " + e.Message);
            return 1;
        }
    }


    // ========================================================================
    //  LA LISTA OFICIAL - 2048 palabras del BIP-39 en ingles
    // ========================================================================
    //
    //  Dos propiedades la hacen especial:
    //    - Son 2048 = 2^11 exactas, para que cada palabra valga 11 bits limpios.
    //    - Las 4 PRIMERAS LETRAS son unicas (aban/abil/able...). Puedes apuntar
    //      solo 4 letras por palabra sin perder informacion, util al grabar la
    //      semilla en metal.
    //
    //  El orden es alfabetico y NO se puede alterar: la posicion ES el dato.
    //
    static readonly string[] PALABRAS = (@"
        abandon ability able about above absent absorb abstract
        absurd abuse access accident account accuse achieve acid
        acoustic acquire across act action actor actress actual
        adapt add addict address adjust admit adult advance
        advice aerobic affair afford afraid again age agent
        agree ahead aim air airport aisle alarm album
        alcohol alert alien all alley allow almost alone
        alpha already also alter always amateur amazing among
        amount amused analyst anchor ancient anger angle angry
        animal ankle announce annual another answer antenna antique
        anxiety any apart apology appear apple approve april
        arch arctic area arena argue arm armed armor
        army around arrange arrest arrive arrow art artefact
        artist artwork ask aspect assault asset assist assume
        asthma athlete atom attack attend attitude attract auction
        audit august aunt author auto autumn average avocado
        avoid awake aware away awesome awful awkward axis
        baby bachelor bacon badge bag balance balcony ball
        bamboo banana banner bar barely bargain barrel base
        basic basket battle beach bean beauty because become
        beef before begin behave behind believe below belt
        bench benefit best betray better between beyond bicycle
        bid bike bind biology bird birth bitter black
        blade blame blanket blast bleak bless blind blood
        blossom blouse blue blur blush board boat body
        boil bomb bone bonus book boost border boring
        borrow boss bottom bounce box boy bracket brain
        brand brass brave bread breeze brick bridge brief
        bright bring brisk broccoli broken bronze broom brother
        brown brush bubble buddy budget buffalo build bulb
        bulk bullet bundle bunker burden burger burst bus
        business busy butter buyer buzz cabbage cabin cable
        cactus cage cake call calm camera camp can
        canal cancel candy cannon canoe canvas canyon capable
        capital captain car carbon card cargo carpet carry
        cart case cash casino castle casual cat catalog
        catch category cattle caught cause caution cave ceiling
        celery cement census century cereal certain chair chalk
        champion change chaos chapter charge chase chat cheap
        check cheese chef cherry chest chicken chief child
        chimney choice choose chronic chuckle chunk churn cigar
        cinnamon circle citizen city civil claim clap clarify
        claw clay clean clerk clever click client cliff
        climb clinic clip clock clog close cloth cloud
        clown club clump cluster clutch coach coast coconut
        code coffee coil coin collect color column combine
        come comfort comic common company concert conduct confirm
        congress connect consider control convince cook cool copper
        copy coral core corn correct cost cotton couch
        country couple course cousin cover coyote crack cradle
        craft cram crane crash crater crawl crazy cream
        credit creek crew cricket crime crisp critic crop
        cross crouch crowd crucial cruel cruise crumble crunch
        crush cry crystal cube culture cup cupboard curious
        current curtain curve cushion custom cute cycle dad
        damage damp dance danger daring dash daughter dawn
        day deal debate debris decade december decide decline
        decorate decrease deer defense define defy degree delay
        deliver demand demise denial dentist deny depart depend
        deposit depth deputy derive describe desert design desk
        despair destroy detail detect develop device devote diagram
        dial diamond diary dice diesel diet differ digital
        dignity dilemma dinner dinosaur direct dirt disagree discover
        disease dish dismiss disorder display distance divert divide
        divorce dizzy doctor document dog doll dolphin domain
        donate donkey donor door dose double dove draft
        dragon drama drastic draw dream dress drift drill
        drink drip drive drop drum dry duck dumb
        dune during dust dutch duty dwarf dynamic eager
        eagle early earn earth easily east easy echo
        ecology economy edge edit educate effort egg eight
        either elbow elder electric elegant element elephant elevator
        elite else embark embody embrace emerge emotion employ
        empower empty enable enact end endless endorse enemy
        energy enforce engage engine enhance enjoy enlist enough
        enrich enroll ensure enter entire entry envelope episode
        equal equip era erase erode erosion error erupt
        escape essay essence estate eternal ethics evidence evil
        evoke evolve exact example excess exchange excite exclude
        excuse execute exercise exhaust exhibit exile exist exit
        exotic expand expect expire explain expose express extend
        extra eye eyebrow fabric face faculty fade faint
        faith fall false fame family famous fan fancy
        fantasy farm fashion fat fatal father fatigue fault
        favorite feature february federal fee feed feel female
        fence festival fetch fever few fiber fiction field
        figure file film filter final find fine finger
        finish fire firm first fiscal fish fit fitness
        fix flag flame flash flat flavor flee flight
        flip float flock floor flower fluid flush fly
        foam focus fog foil fold follow food foot
        force forest forget fork fortune forum forward fossil
        foster found fox fragile frame frequent fresh friend
        fringe frog front frost frown frozen fruit fuel
        fun funny furnace fury future gadget gain galaxy
        gallery game gap garage garbage garden garlic garment
        gas gasp gate gather gauge gaze general genius
        genre gentle genuine gesture ghost giant gift giggle
        ginger giraffe girl give glad glance glare glass
        glide glimpse globe gloom glory glove glow glue
        goat goddess gold good goose gorilla gospel gossip
        govern gown grab grace grain grant grape grass
        gravity great green grid grief grit grocery group
        grow grunt guard guess guide guilt guitar gun
        gym habit hair half hammer hamster hand happy
        harbor hard harsh harvest hat have hawk hazard
        head health heart heavy hedgehog height hello helmet
        help hen hero hidden high hill hint hip
        hire history hobby hockey hold hole holiday hollow
        home honey hood hope horn horror horse hospital
        host hotel hour hover hub huge human humble
        humor hundred hungry hunt hurdle hurry hurt husband
        hybrid ice icon idea identify idle ignore ill
        illegal illness image imitate immense immune impact impose
        improve impulse inch include income increase index indicate
        indoor industry infant inflict inform inhale inherit initial
        inject injury inmate inner innocent input inquiry insane
        insect inside inspire install intact interest into invest
        invite involve iron island isolate issue item ivory
        jacket jaguar jar jazz jealous jeans jelly jewel
        job join joke journey joy judge juice jump
        jungle junior junk just kangaroo keen keep ketchup
        key kick kid kidney kind kingdom kiss kit
        kitchen kite kitten kiwi knee knife knock know
        lab label labor ladder lady lake lamp language
        laptop large later latin laugh laundry lava law
        lawn lawsuit layer lazy leader leaf learn leave
        lecture left leg legal legend leisure lemon lend
        length lens leopard lesson letter level liar liberty
        library license life lift light like limb limit
        link lion liquid list little live lizard load
        loan lobster local lock logic lonely long loop
        lottery loud lounge love loyal lucky luggage lumber
        lunar lunch luxury lyrics machine mad magic magnet
        maid mail main major make mammal man manage
        mandate mango mansion manual maple marble march margin
        marine market marriage mask mass master match material
        math matrix matter maximum maze meadow mean measure
        meat mechanic medal media melody melt member memory
        mention menu mercy merge merit merry mesh message
        metal method middle midnight milk million mimic mind
        minimum minor minute miracle mirror misery miss mistake
        mix mixed mixture mobile model modify mom moment
        monitor monkey monster month moon moral more morning
        mosquito mother motion motor mountain mouse move movie
        much muffin mule multiply muscle museum mushroom music
        must mutual myself mystery myth naive name napkin
        narrow nasty nation nature near neck need negative
        neglect neither nephew nerve nest net network neutral
        never news next nice night noble noise nominee
        noodle normal north nose notable note nothing notice
        novel now nuclear number nurse nut oak obey
        object oblige obscure observe obtain obvious occur ocean
        october odor off offer office often oil okay
        old olive olympic omit once one onion online
        only open opera opinion oppose option orange orbit
        orchard order ordinary organ orient original orphan ostrich
        other outdoor outer output outside oval oven over
        own owner oxygen oyster ozone pact paddle page
        pair palace palm panda panel panic panther paper
        parade parent park parrot party pass patch path
        patient patrol pattern pause pave payment peace peanut
        pear peasant pelican pen penalty pencil people pepper
        perfect permit person pet phone photo phrase physical
        piano picnic picture piece pig pigeon pill pilot
        pink pioneer pipe pistol pitch pizza place planet
        plastic plate play please pledge pluck plug plunge
        poem poet point polar pole police pond pony
        pool popular portion position possible post potato pottery
        poverty powder power practice praise predict prefer prepare
        present pretty prevent price pride primary print priority
        prison private prize problem process produce profit program
        project promote proof property prosper protect proud provide
        public pudding pull pulp pulse pumpkin punch pupil
        puppy purchase purity purpose purse push put puzzle
        pyramid quality quantum quarter question quick quit quiz
        quote rabbit raccoon race rack radar radio rail
        rain raise rally ramp ranch random range rapid
        rare rate rather raven raw razor ready real
        reason rebel rebuild recall receive recipe record recycle
        reduce reflect reform refuse region regret regular reject
        relax release relief rely remain remember remind remove
        render renew rent reopen repair repeat replace report
        require rescue resemble resist resource response result retire
        retreat return reunion reveal review reward rhythm rib
        ribbon rice rich ride ridge rifle right rigid
        ring riot ripple risk ritual rival river road
        roast robot robust rocket romance roof rookie room
        rose rotate rough round route royal rubber rude
        rug rule run runway rural sad saddle sadness
        safe sail salad salmon salon salt salute same
        sample sand satisfy satoshi sauce sausage save say
        scale scan scare scatter scene scheme school science
        scissors scorpion scout scrap screen script scrub sea
        search season seat second secret section security seed
        seek segment select sell seminar senior sense sentence
        series service session settle setup seven shadow shaft
        shallow share shed shell sheriff shield shift shine
        ship shiver shock shoe shoot shop short shoulder
        shove shrimp shrug shuffle shy sibling sick side
        siege sight sign silent silk silly silver similar
        simple since sing siren sister situate six size
        skate sketch ski skill skin skirt skull slab
        slam sleep slender slice slide slight slim slogan
        slot slow slush small smart smile smoke smooth
        snack snake snap sniff snow soap soccer social
        sock soda soft solar soldier solid solution solve
        someone song soon sorry sort soul sound soup
        source south space spare spatial spawn speak special
        speed spell spend sphere spice spider spike spin
        spirit split spoil sponsor spoon sport spot spray
        spread spring spy square squeeze squirrel stable stadium
        staff stage stairs stamp stand start state stay
        steak steel stem step stereo stick still sting
        stock stomach stone stool story stove strategy street
        strike strong struggle student stuff stumble style subject
        submit subway success such sudden suffer sugar suggest
        suit summer sun sunny sunset super supply supreme
        sure surface surge surprise surround survey suspect sustain
        swallow swamp swap swarm swear sweet swift swim
        swing switch sword symbol symptom syrup system table
        tackle tag tail talent talk tank tape target
        task taste tattoo taxi teach team tell ten
        tenant tennis tent term test text thank that
        theme then theory there they thing this thought
        three thrive throw thumb thunder ticket tide tiger
        tilt timber time tiny tip tired tissue title
        toast tobacco today toddler toe together toilet token
        tomato tomorrow tone tongue tonight tool tooth top
        topic topple torch tornado tortoise toss total tourist
        toward tower town toy track trade traffic tragic
        train transfer trap trash travel tray treat tree
        trend trial tribe trick trigger trim trip trophy
        trouble truck true truly trumpet trust truth try
        tube tuition tumble tuna tunnel turkey turn turtle
        twelve twenty twice twin twist two type typical
        ugly umbrella unable unaware uncle uncover under undo
        unfair unfold unhappy uniform unique unit universe unknown
        unlock until unusual unveil update upgrade uphold upon
        upper upset urban urge usage use used useful
        useless usual utility vacant vacuum vague valid valley
        valve van vanish vapor various vast vault vehicle
        velvet vendor venture venue verb verify version very
        vessel veteran viable vibrant vicious victory video view
        village vintage violin virtual virus visa visit visual
        vital vivid vocal voice void volcano volume vote
        voyage wage wagon wait walk wall walnut want
        warfare warm warrior wash wasp waste water wave
        way wealth weapon wear weasel weather web wedding
        weekend weird welcome west wet whale what wheat
        wheel when where whip whisper wide width wife
        wild will win window wine wing wink winner
        winter wire wisdom wise wish witness wolf woman
        wonder wood wool word work world worry worth
        wrap wreck wrestle wrist write wrong yard year
        yellow you young youth zebra zero zone zoo
").Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
}
