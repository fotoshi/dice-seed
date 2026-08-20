# Requisitos — JavaScript / Node.js

- **Node.js 10.4 o superior.** El script usa `BigInt` (soportado de forma
  nativa desde esa versión) para trabajar con números de 129 bits, que no
  caben en el tipo `Number` de JavaScript.
- **Ninguna dependencia externa.** Solo usa los módulos incluidos `crypto` y
  `readline`. No hay `package.json` ni `node_modules`.
- Descarga: https://nodejs.org (se recomienda la versión LTS).

Comprueba tu versión antes de empezar:

```bash
node --version
```

No hace falta `npm install` nada.
