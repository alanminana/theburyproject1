#!/usr/bin/env node
// C-03: un test omitido no cuenta como aprobado. Lee el JSON de Playwright y compara los omitidos con
// scripts/qa/omitidos-esperados.json (clasificados en docs/e2e-omitidos-clasificacion.md).
// Falla (exit 1) si hay omitidos no clasificados, mas omitidos que los esperados, o si el JSON no existe/esta vacio.
// Un esperado que ya no se omite solo avisa: significa que hay que actualizar la lista (mejora, no regresion).
'use strict';
const fs = require('fs');
const path = require('path');

const jsonPath = process.argv[2];
if (!jsonPath || !fs.existsSync(jsonPath)) {
  console.error(`No existe el JSON de Playwright: ${jsonPath}`);
  process.exit(1);
}
const reporte = JSON.parse(fs.readFileSync(jsonPath, 'utf8'));
const esperados = JSON.parse(fs.readFileSync(path.join(__dirname, 'omitidos-esperados.json'), 'utf8'));

const omitidos = [];
let total = 0;
(function recorrer(suite, archivo) {
  const f = suite.file || archivo;
  for (const spec of suite.specs || []) {
    for (const t of spec.tests || []) {
      total++;
      const ultimo = (t.results || [])[t.results.length - 1];
      if (t.status === 'skipped' || (ultimo && ultimo.status === 'skipped')) {
        const motivo = (t.annotations || []).filter(a => a.type === 'skip').map(a => a.description).join(' ');
        omitidos.push({ archivo: path.basename(f || spec.file || ''), titulo: spec.title, motivo });
      }
    }
  }
  for (const s of suite.suites || []) recorrer(s, f);
})({ suites: reporte.suites }, '');

if (total === 0) {
  console.error('El reporte no contiene tests: la corrida no ejecuto nada.');
  process.exit(1);
}

const restantes = esperados.map(e => ({ ...e, faltan: e.cantidad }));
const noClasificados = [];
for (const o of omitidos) {
  const e = restantes.find(x => x.faltan > 0 && o.archivo === x.archivo && o.titulo.includes(x.titulo));
  if (e) e.faltan--; else noClasificados.push(o);
}

console.log(`Tests: ${total}. Omitidos: ${omitidos.length} (esperados clasificados: ${esperados.reduce((n, e) => n + e.cantidad, 0)}).`);
for (const e of esperados) console.log(`  - [${e.clase}] ${e.archivo} :: ${e.titulo} x${e.cantidad}`);
const yaNoOmitidos = restantes.filter(e => e.faltan > 0);
for (const e of yaNoOmitidos) console.log(`AVISO: ya no se omiten ${e.faltan} de "${e.archivo} :: ${e.titulo}"; actualizar scripts/qa/omitidos-esperados.json.`);
if (noClasificados.length) {
  console.error(`ERROR: ${noClasificados.length} omitido(s) NO clasificado(s):`);
  for (const o of noClasificados) console.error(`  - ${o.archivo} :: ${o.titulo} (${o.motivo})`);
  process.exit(1);
}
