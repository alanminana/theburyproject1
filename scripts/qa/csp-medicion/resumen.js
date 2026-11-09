// Resume el .jsonl que genera preload.js. Uso: node resumen.js <archivo.jsonl> [--max-violaciones N]
// Siempre imprime el resumen; con --max-violaciones sale con código 1 si hay más violaciones que N (por defecto: solo informa).
'use strict';
const fs = require('fs');

const archivo = process.argv[2];
const iMax = process.argv.indexOf('--max-violaciones');
const max = iMax > -1 ? Number(process.argv[iMax + 1]) : null;
if (!archivo) { console.error('uso: node resumen.js <archivo.jsonl> [--max-violaciones N]'); process.exit(2); }

const lineas = fs.existsSync(archivo) ? fs.readFileSync(archivo, 'utf8').split('\n').filter(Boolean) : [];
const ev = lineas.map((l) => { try { return JSON.parse(l); } catch { return null; } }).filter(Boolean);
const viol = ev.filter((e) => e.t === 'violacion');
const errs = ev.filter((e) => e.t === 'pageerror');
const ctxs = ev.filter((e) => e.t === 'contexto').length;
const fallos = ev.filter((e) => e.t === 'fallo-instrumentacion');
const cons = ev.filter((e) => e.t === 'consola-csp');

const agrupar = (arr, clave) => {
  const m = new Map();
  for (const e of arr) { const k = clave(e); m.set(k, (m.get(k) || 0) + 1); }
  return [...m.entries()].sort((a, b) => b[1] - a[1]);
};

console.log(`Contextos instrumentados: ${ctxs} (procesos: ${new Set(ev.filter((e) => e.t === 'contexto').map((e) => e.pid)).size}); fallos de instrumentación: ${fallos.length}`);
console.log(`Eventos: violaciones CSP=${viol.length}, errores de página=${errs.length}, consola CSP=${cons.length}`);
if (!ctxs) console.log('(ningún contexto instrumentado: la medición no se enganchó; verificar NODE_OPTIONS/CSP_MEDIR_OUT)');
for (const [k, n] of agrupar(viol, (e) => `${e.directiva} | ${e.bloqueado} | ${e.ruta} | ${e.disposicion}`).slice(0, 60)) console.log(`  VIOLACION x${n}: ${k}`);
for (const [k, n] of agrupar(errs, (e) => e.mensaje).slice(0, 30)) console.log(`  PAGEERROR x${n}: ${k}`);
if (fallos.length) { for (const f of fallos.slice(0, 5)) console.error('Fallo de instrumentación: ' + f.mensaje); console.error('Medición inválida: hubo contextos sin instrumentar.'); process.exit(3); }
if (!ctxs) { console.error('Medición inválida: 0 contextos instrumentados.'); process.exit(3); }
if (max !== null && viol.length > max) { console.error(`Violaciones CSP (${viol.length}) > máximo permitido (${max}).`); process.exit(1); }
