#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
bip39_dados.py - 12 palabras BIP39 a partir de 50 tiradas de un dado de 6 caras.

USO:
    python3 bip39_dados.py              # pide las tiradas por teclado
    python3 bip39_dados.py --detalle    # ademas, muestra todos los pasos intermedios
    python3 bip39_dados.py --test       # autocomprobacion con vectores oficiales

EL METODO EN CINCO PASOS:

    50 tiradas de d6
          |  PASO 1: leer las tiradas como un numero en base 6
          v
    numero entero N  (unos 129,25 bits)
          |  PASO 2: quedarse con los 128 bits bajos
          v
    entropia = 16 bytes = 128 bits
          |  PASO 3: checksum = 4 primeros bits de SHA-256(entropia)
          v
    132 bits = 128 + 4
          |  PASO 4: escribir esos 132 bits como un numero en base 2048
          v
    12 indices (0-2047)
          |  PASO 5: buscar cada indice en la lista oficial
          v
    12 palabras

    Los pasos 1 y 4 son la MISMA operacion en sentidos opuestos: una conversion
    de base. Primero LEEMOS los dados en base 6, luego ESCRIBIMOS el resultado
    en base 2048. Esa simetria es el corazon del metodo.

SEGURIDAD:
    - Ejecutalo en un ordenador SIN RED, idealmente arrancado desde un USB live.
    - No pases las tiradas como argumento: quedarian en el historial del shell.
    - Apunta las palabras en papel. Borra la pantalla al terminar (clear/reset).
    - Usa un dado de casino y tira sobre una superficie dura.
    - El modo --detalle es para APRENDER. No lo uses con una semilla real que
      vayas a custodiar: llena la pantalla de material sensible sin necesidad.

Dependencias: ninguna, solo la libreria estandar de Python 3.
"""

import hashlib
import sys


# ============================================================================
#  CONSTANTES - de donde sale cada numero
# ============================================================================

# Un dado de 6 caras aporta log2(6) = 2,585 bits por tirada.
# 128 / 2,585 = 49,5 -> redondeando hacia arriba, 50 tiradas.
# (Con 49 tendriamos 126,66 bits: insuficiente.)
N_TIRADAS = 50

# BIP39 define frases de 128, 160, 192, 224 o 256 bits de entropia.
# 128 bits es el estandar para 12 palabras.
BITS_ENTROPIA = 128

# La especificacion fija: bits de checksum = bits de entropia / 32.
# 128 / 32 = 4 bits. No aportan seguridad, sirven para detectar erratas.
BITS_CHECKSUM = BITS_ENTROPIA // 32

# La lista oficial tiene 2048 palabras, y 2048 = 2^11.
# Por eso cada palabra codifica exactamente 11 bits.
PALABRAS_EN_LISTA = 2048
BITS_POR_PALABRA = 11

# 128 + 4 = 132 bits, y 132 / 11 = 12 palabras exactas. No sobra ni falta
# un bit: todo el diseño de BIP39 esta hecho para que esta division sea limpia.
N_PALABRAS = (BITS_ENTROPIA + BITS_CHECKSUM) // BITS_POR_PALABRA


# ============================================================================
#  PASOS 1 y 2 - de las tiradas a la entropia
# ============================================================================

def tiradas_a_entropia(tiradas):
    """Convierte una lista de 50 enteros del 1 al 6 en 16 bytes de entropia."""

    if len(tiradas) != N_TIRADAS:
        raise ValueError("hacen falta exactamente %d tiradas, recibidas %d"
                         % (N_TIRADAS, len(tiradas)))

    # ---- PASO 1: las tiradas son los digitos de un numero en base 6 --------
    #
    # El dado no tiene cara 0, asi que restamos 1 para pasar de 1..6 a 0..5.
    # El numero que formamos es:
    #
    #     N = d1*6^49 + d2*6^48 + ... + d49*6^1 + d50*6^0
    #
    # pero no calculamos potencias: usamos el METODO DE HORNER, que multiplica
    # el acumulado por 6 y le suma el digito nuevo. Con las tiradas 3,1,6,4:
    #
    #     n=0  -> 0*6+2 = 2  -> 2*6+0 = 12  -> 12*6+5 = 77  -> 77*6+3 = 465
    #
    # OJO: la PRIMERA tirada acaba siendo el digito MAS SIGNIFICATIVO.
    # Cambiar el orden de las tiradas produce una semilla completamente
    # distinta. Por eso el script imprime la secuencia al final.
    #
    n = 0
    for tirada in tiradas:
        if not 1 <= tirada <= 6:
            raise ValueError("tirada fuera del rango 1-6: %r" % tirada)
        n = n * 6 + (tirada - 1)

    # ---- PASO 2: recortar a exactamente 128 bits ---------------------------
    #
    # N puede llegar hasta 6^50 - 1, que es 2,375 veces mayor que 2^128.
    # Nos quedamos con los 128 bits bajos.
    #
    # SOBRE EL SESGO: como 6^50 no es multiplo exacto de 2^128, algunos
    # resultados salen 3 veces y otros 2. La entropia real baja de 128 a
    # 127,97 bits. Son 0,03 bits: irrelevante.
    #
    # Si lo quieres matematicamente perfecto, descomenta estas dos lineas.
    # Solo aceptan N por debajo de 2^129, rango en el que 2^128 cabe justo
    # 2 veces y el modulo es uniforme. Precio: repetir las 50 tiradas el
    # 15,8% de las veces.
    #
    # if n >= (1 << (BITS_ENTROPIA + 1)):
    #     raise ValueError("descarta estas tiradas y vuelve a tirar los 50 dados")

    n %= 1 << BITS_ENTROPIA                     # 1 << 128 es 2^128, escrito barato

    # to_bytes empaqueta el entero en 16 bytes, el mas significativo primero
    # ("big endian"), que es el orden en que leemos los numeros a mano.
    return n.to_bytes(BITS_ENTROPIA // 8, "big")


# ============================================================================
#  PASO 3 - el checksum
# ============================================================================

def calcular_checksum(entropia):
    """Devuelve los 4 bits de checksum como un entero de 0 a 15."""

    # Los 4 bits mas altos del PRIMER BYTE del SHA-256 de la entropia.
    #
    #     SHA-256(entropia) = 05 fd a8 7a ...
    #                         ^^
    #                    primer byte = 0x05 = 00000101
    #                                         ----
    #                                    >> 4 nos deja 0000
    #
    # Estos bits no dan seguridad. Sirven para que, si copias mal una palabra,
    # la cartera rechace la frase (15 de cada 16 veces) en lugar de abrir en
    # silencio una cuenta vacia que no es la tuya.
    digest = hashlib.sha256(entropia).digest()
    return digest[0] >> (8 - BITS_CHECKSUM)


# ============================================================================
#  PASOS 4 y 5 - de la entropia a las palabras
# ============================================================================

def entropia_a_indices(entropia):
    """Convierte 16 bytes de entropia en los 12 indices de la lista (0-2047)."""

    if len(entropia) != BITS_ENTROPIA // 8:
        raise ValueError("la entropia debe ocupar %d bytes, recibidos %d"
                         % (BITS_ENTROPIA // 8, len(entropia)))

    # ---- Juntar entropia y checksum en un solo numero de 132 bits ----------
    #
    #     [ ---------- 128 bits de entropia ---------- ][chk]
    #      \___________________ 132 bits ___________________/
    #
    # "<< 4" corre la entropia 4 posiciones a la izquierda, dejando 4 ceros al
    # final. El "|" (OR bit a bit) rellena justo esos 4 huecos con el checksum.
    v = int.from_bytes(entropia, "big") << BITS_CHECKSUM
    v |= calcular_checksum(entropia)

    # ---- PASO 4: escribir esos 132 bits como un numero en base 2048 --------
    #
    # Esto es EXACTAMENTE la operacion inversa del paso 1. Alli leiamos digitos
    # en base 6 y construiamos un numero; aqui cogemos un numero y extraemos
    # sus digitos en base 2048. Cada "digito" es un indice de palabra.
    #
    # divmod(v, 2048) devuelve (cociente, resto) de una sola vez. El resto son
    # los 11 bits mas bajos, es decir, el digito de menor peso. Como salen del
    # reves, cada uno se inserta al PRINCIPIO de la lista.
    #
    #   v = 1746·2048^11 + 1100·2048^10 + ... + 723·2048^1 + 896·2048^0
    #        \____/                                            \___/
    #      1ª palabra                                     12ª palabra
    #
    indices = []
    for _ in range(N_PALABRAS):
        v, resto = divmod(v, PALABRAS_EN_LISTA)
        indices.insert(0, resto)

    # Tras 12 divisiones no debe quedar nada: 12 x 11 = 132 bits, justos.
    assert v == 0, "quedaron bits sin repartir: revisa las constantes"

    return indices


def entropia_a_palabras(entropia):
    """Convierte 16 bytes de entropia en las 12 palabras BIP39."""
    # ---- PASO 5: cada indice es una posicion directa en la lista -----------
    return [PALABRAS[i] for i in entropia_a_indices(entropia)]


# ============================================================================
#  ENTRADA - leer las tiradas del usuario
# ============================================================================

def leer_tiradas():
    """Lee 50 tiradas por teclado (o por stdin, si se canaliza)."""
    print("Introduce %d tiradas de un dado de 6 caras." % N_TIRADAS)
    print("Puedes escribirlas de una en una o varias juntas (ej: 3 1 6 4 2).")
    print("Ctrl-C para abortar.\n")

    tiradas = []
    while len(tiradas) < N_TIRADAS:
        try:
            linea = input("[%2d/%d] > " % (len(tiradas), N_TIRADAS))
        except EOFError:
            break                       # se acabo la entrada canalizada
        for caracter in linea:
            if caracter in "123456":
                tiradas.append(int(caracter))
                if len(tiradas) == N_TIRADAS:
                    break
            elif not caracter.isspace() and caracter not in ",;-.":
                # Avisamos en vez de callar: un '7' o una 'o' suele ser un error
                # de tecleo que cambiaria la semilla sin que te enteres.
                print("  aviso: ignorado el caracter %r" % caracter)

    if len(tiradas) != N_TIRADAS:
        sys.exit("\nERROR: solo se leyeron %d tiradas de %d. Abortado."
                 % (len(tiradas), N_TIRADAS))
    return tiradas


# ============================================================================
#  SALIDA
# ============================================================================

def mostrar_resultado(tiradas, entropia, palabras):
    """Imprime lo imprescindible: tiradas, entropia y las 12 palabras."""
    print("\n" + "=" * 52)
    print("Tiradas  : %s" % "".join(str(t) for t in tiradas))
    print("Entropia : %s" % entropia.hex())
    print("=" * 52)
    for numero, palabra in enumerate(palabras, 1):
        print("%2d. %s" % (numero, palabra))
    print("=" * 52)
    print("Apuntalas en papel, en este orden. Nunca en un fichero ni en una foto.")
    print("Cuando termines: 'clear' o 'reset' para limpiar la pantalla.")


def mostrar_detalle(tiradas, entropia):
    """Imprime todos los valores intermedios. Solo para aprender y enseñar."""
    # Rehacemos el paso 1 aqui para poder enseñar N, que la funcion principal
    # no devuelve. Mantener las funciones "limpias" y explicar aparte es mejor
    # que llenarlas de prints.
    n = 0
    for tirada in tiradas:
        n = n * 6 + (tirada - 1)

    checksum = calcular_checksum(entropia)
    indices = entropia_a_indices(entropia)
    bits_entropia = bin(int.from_bytes(entropia, "big"))[2:].zfill(BITS_ENTROPIA)
    bits_totales = bits_entropia + bin(checksum)[2:].zfill(BITS_CHECKSUM)

    print("\n" + "-" * 52)
    print("PASO 1 - las tiradas como numero en base 6")
    print("-" * 52)
    print("  digitos (tirada - 1): %s" % "".join(str(t - 1) for t in tiradas))
    print("  N  = %d" % n)
    print("  N ocupa %d bits" % n.bit_length())

    print("\n" + "-" * 52)
    print("PASO 2 - recorte a %d bits" % BITS_ENTROPIA)
    print("-" * 52)
    print("  2^%d = %d" % (BITS_ENTROPIA, 1 << BITS_ENTROPIA))
    if n < (1 << BITS_ENTROPIA):
        print("  N ya era menor que 2^%d: el modulo no cambia nada" % BITS_ENTROPIA)
    else:
        print("  N era mayor: se le resta 2^%d" % BITS_ENTROPIA)
    print("  entropia (hex) = %s" % entropia.hex())
    print("  entropia (bin) = %s" % bits_entropia)

    print("\n" + "-" * 52)
    print("PASO 3 - checksum")
    print("-" * 52)
    print("  SHA-256(entropia) = %s..." % hashlib.sha256(entropia).hexdigest()[:16])
    primer_byte = hashlib.sha256(entropia).digest()[0]
    print("  primer byte = 0x%02x = %s" % (primer_byte, bin(primer_byte)[2:].zfill(8)))
    print("  >> %d          =     %s   <- checksum"
          % (8 - BITS_CHECKSUM, bin(checksum)[2:].zfill(BITS_CHECKSUM)))

    print("\n" + "-" * 52)
    print("PASOS 4 y 5 - %d bits en grupos de %d"
          % (BITS_ENTROPIA + BITS_CHECKSUM, BITS_POR_PALABRA))
    print("-" * 52)
    print("  %s|%s" % (bits_entropia, bin(checksum)[2:].zfill(BITS_CHECKSUM)))
    for posicion, indice in enumerate(indices):
        inicio = posicion * BITS_POR_PALABRA
        grupo = bits_totales[inicio:inicio + BITS_POR_PALABRA]
        print("  %2d. %s = %4d -> %s" % (posicion + 1, grupo, indice, PALABRAS[indice]))
    print("\n  Los ultimos %d bits del grupo %d son el checksum calculado arriba."
          % (BITS_CHECKSUM, N_PALABRAS))


# ============================================================================
#  AUTOCOMPROBACION
# ============================================================================

def autotest():
    """Vectores oficiales de BIP39 mas comprobaciones propias del dado."""

    # La lista incrustada debe ser byte a byte la oficial. Si alguien cambiara
    # una sola letra, este hash no cuadraria.
    assert len(PALABRAS) == PALABRAS_EN_LISTA, "la lista debe tener 2048 palabras"
    assert hashlib.sha256(("\n".join(PALABRAS) + "\n").encode()).hexdigest() == \
        "2f5eed53a4727b4bf8880d8f3f199efc90e58503646d9ff8eff3a2ed3b24dbda", \
        "la lista de palabras NO coincide con la oficial de BIP-39"

    # Vectores de la especificacion (github.com/trezor/python-mnemonic).
    vectores = [
        ("00000000000000000000000000000000",
         "abandon abandon abandon abandon abandon abandon "
         "abandon abandon abandon abandon abandon about"),
        ("7f7f7f7f7f7f7f7f7f7f7f7f7f7f7f7f",
         "legal winner thank year wave sausage worth useful "
         "legal winner thank yellow"),
        ("80808080808080808080808080808080",
         "letter advice cage absurd amount doctor acoustic avoid "
         "letter advice cage above"),
        ("ffffffffffffffffffffffffffffffff",
         "zoo zoo zoo zoo zoo zoo zoo zoo zoo zoo zoo wrong"),
        ("9e885d952ad362caeb4efe34a8e91bd2",
         "ozone drill grab fiber curtain grace pudding thank "
         "cruise elder eight picnic"),
    ]
    for hexadecimal, esperado in vectores:
        obtenido = " ".join(entropia_a_palabras(bytes.fromhex(hexadecimal)))
        assert obtenido == esperado, "fallo en %s:\n  %s" % (hexadecimal, obtenido)

    # 50 unos -> todos los digitos a 0 -> N = 0 -> entropia toda a cero
    assert tiradas_a_entropia([1] * 50).hex() == "0" * 32
    # 50 seises -> N = 6^50 - 1, contrastado con el calculo directo
    assert tiradas_a_entropia([6] * 50) == \
        ((6 ** 50 - 1) % (1 << 128)).to_bytes(16, "big")
    # El orden importa: la primera tirada pesa 6^49, la ultima 6^0
    assert tiradas_a_entropia([2] + [1] * 49) != tiradas_a_entropia([1] * 49 + [2])
    # Las constantes tienen que encajar sin bits sobrantes
    assert N_PALABRAS * BITS_POR_PALABRA == BITS_ENTROPIA + BITS_CHECKSUM
    assert 2 ** BITS_POR_PALABRA == PALABRAS_EN_LISTA

    print("OK: %d vectores oficiales y 5 comprobaciones propias superados."
          % len(vectores))


# ============================================================================
#  PROGRAMA PRINCIPAL
# ============================================================================

def main():
    if "--test" in sys.argv:
        autotest()
        return

    tiradas = leer_tiradas()
    entropia = tiradas_a_entropia(tiradas)          # pasos 1 y 2
    palabras = entropia_a_palabras(entropia)        # pasos 3, 4 y 5

    if "--detalle" in sys.argv:
        mostrar_detalle(tiradas, entropia)
    mostrar_resultado(tiradas, entropia, palabras)



# ============================================================================
#  LA LISTA OFICIAL - 2048 palabras del BIP-39 en ingles
# ============================================================================
#
#  Dos propiedades la hacen especial:
#    - Son 2048 = 2^11 exactas, para que cada palabra valga 11 bits limpios.
#    - Las 4 PRIMERAS LETRAS son unicas (aban/abil/able...). Puedes apuntar
#      solo 4 letras por palabra sin perder informacion, util al grabar la
#      semilla en metal.
#
#  El orden es alfabetico y NO se puede alterar: la posicion ES el dato.
#
PALABRAS = """
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
""".split()


if __name__ == "__main__":
    main()
