// Resume el .jsonl que genera preload.js y es el GATE de CSP del job Playwright.
// Uso: node resumen.js <archivo.jsonl> [--max-violaciones N] [--playwright-json <reporte.json>] [--min-ratio R]
//
// Siempre imprime el resumen. Codigos de salida (un resultado vacio o dudoso NUNCA da verde):
//   0  medicion valida y dentro del umbral
//   1  hay mas violaciones CSP (eventos securitypolicyviolation + errores de consola CSP) que --max-violaciones
//   2  uso incorrecto
//   3  medicion INVALIDA: archivo ausente/vacio/sin contextos, lineas truncadas o corruptas, fallo de instalacion,
//      contextos creados que no se instrumentaron, o (con --playwright-json) muy pocos contextos medidos para los tests ejecutados.
// Los errores de pagina (pageerror) se informan y cuentan en el resumen; --max-pageerrors N los vuelve gate (opt-in).
'use strict';
const fs = require('fs');

const arg = (n) => { const i = process.argv.indexOf(n); return i > -1 ? process.argv[i + 1] : null; };
const archivo = process.argv[2];
const max = arg('--max-violaciones') !== null ? Number(arg('--max-violaciones')) : null;
const maxPageErrors = arg('--max-pageerrors') !== null ? Number(arg('--max-pageerrors')) : null;
const jsonPw = arg('--playwright-json');
const ratio = arg('--min-ratio') !== null ? Number(arg('--min-ratio')) : 0.5;
if (!archivo || archivo.startsWith('--')) { console.error('uso: node resumen.js <archivo.jsonl> [--max-violaciones N] [--max-pageerrors N] [--playwright-json f.json] [--min-ratio R]'); process.exit(2); }
for (const [n, v] of [['--max-violaciones', max], ['--max-pageerrors', maxPageErrors], ['--min-ratio', ratio]]) {
  if (v !== null && (!Number.isFinite(v) || v < 0)) { console.error(`${n} invalido`); process.exit(2); }
}

const invalidos = [];
let lineas = [];
if (!fs.existsSync(archivo)) invalidos.push(`no existe el archivo de resultados (${archivo}): la medicion no se enganchó o se interrumpio`);
else {
  const crudo = fs.readFileSync(archivo, 'utf8');
  lineas = crudo.split('\n').filter((l) => l.trim() !== '');
  // appendFileSync escribe lineas completas terminadas en \n: un final sin \n indica escritura interrumpida.
  if (crudo.length && !crudo.endsWith('\n')) invalidos.push('el archivo no termina en salto de linea: escritura truncada/interrumpida');
}
const ev = [];
lineas.forEach((l, i) => {
  try { const o = JSON.parse(l); if (o && typeof o === 'object' && typeof o.t === 'string') ev.push(o); else invalidos.push(`linea ${i + 1}: evento sin tipo`); }
  catch { invalidos.push(`linea ${i + 1}: JSON invalido/corrupto (${l.slice(0, 40)})`); }
});

const viol = ev.filter((e) => e.t === 'violacion');
const errs = ev.filter((e) => e.t === 'pageerror');
const ctxs = ev.filter((e) => e.t === 'contexto').length;
const creados = ev.filter((e) => e.t === 'contexto-creado').length;
const fallos = ev.filter((e) => e.t === 'fallo-instrumentacion');
const cons = ev.filter((e) => e.t === 'consola-csp');

const agrupar = (arr, clave) => {
  const m = new Map();
  for (const e of arr) { const k = clave(e); m.set(k, (m.get(k) || 0) + 1); }
  return [...m.entries()].sort((a, b) => b[1] - a[1]);
};

console.log(`Contextos instrumentados: ${ctxs} de ${creados} creados (procesos: ${new Set(ev.filter((e) => e.t === 'contexto').map((e) => e.pid)).size}); fallos de instrumentación: ${fallos.length}`);
console.log(`Eventos: violaciones CSP=${viol.length}, errores de página=${errs.length}, consola CSP=${cons.length}`);
if (!ctxs) console.log('(ningún contexto instrumentado: la medición no se enganchó; verificar NODE_OPTIONS/CSP_MEDIR_OUT)');
for (const [k, n] of agrupar(viol, (e) => `${e.directiva} | ${e.bloqueado} | ${e.ruta} | ${e.disposicion}`).slice(0, 60)) console.log(`  VIOLACION x${n}: ${k}`);
for (const [k, n] of agrupar(cons, (e) => e.mensaje).slice(0, 30)) console.log(`  CONSOLA-CSP x${n}: ${k}`);
for (const [k, n] of agrupar(errs, (e) => e.mensaje).slice(0, 30)) console.log(`  PAGEERROR x${n}: ${k}`);

// --- validez de la medicion (exit 3) ---
for (const f of fallos.slice(0, 5)) invalidos.push('fallo de instrumentación: ' + f.mensaje);
if (!ctxs) invalidos.push('0 contextos instrumentados');
if (creados > ctxs + fallos.length) invalidos.push(`hay contextos creados sin instrumentar (${creados} creados, ${ctxs} instrumentados)`);
if (jsonPw) {
  try {
    const st = JSON.parse(fs.readFileSync(jsonPw, 'utf8')).stats;
    const corridos = (st.expected || 0) + (st.unexpected || 0) + (st.flaky || 0);
    console.log(`Tests ejecutados según Playwright: ${corridos} (omitidos ${st.skipped || 0}); contextos medidos/test: ${corridos ? (ctxs / corridos).toFixed(2) : 'n/a'} (mínimo exigido ${ratio})`);
    if (!corridos) invalidos.push('el reporte de Playwright indica 0 tests ejecutados');
    else if (ctxs < Math.floor(corridos * ratio)) invalidos.push(`contextos medidos (${ctxs}) < ${ratio} x tests ejecutados (${corridos}): hay tests que no se midieron`);
  } catch (e) { invalidos.push(`no se pudo leer el reporte de Playwright (${jsonPw}): ${String(e.message).slice(0, 80)}`); }
}
if (invalidos.length) {
  for (const m of invalidos.slice(0, 10)) console.error('Medición inválida: ' + m);
  process.exit(3);
}

// --- umbrales (exit 1) ---
const totalViol = viol.length + cons.length; // un bloqueo CSP que solo dejo rastro en la consola tambien cuenta
if (max !== null && totalViol > max) { console.error(`Violaciones CSP (${totalViol}: ${viol.length} eventos + ${cons.length} de consola) > máximo permitido (${max}).`); process.exit(1); }
if (maxPageErrors !== null && errs.length > maxPageErrors) { console.error(`Errores de página (${errs.length}) > máximo permitido (${maxPageErrors}).`); process.exit(1); }
