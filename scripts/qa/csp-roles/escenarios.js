#!/usr/bin/env node
// F2.1/F2.2 (2026-11-11): escenarios con interacciones reales bajo CSP (por defecto ENFORCE) con comprobaciones funcionales.
// Complementa crawl.js (navegacion GET de menu) e impresion.js. Cada escenario vive en scripts/qa/csp-roles/escenarios/*.js.
// Falla (exit 1) ante: violacion CSP, pageerror, error de consola, 5xx, 4xx no declarado, documento HTML sin cabecera CSP, request fallida,
// check funcional en falso, o un escenario sin ninguna comprobacion/interaccion. Falla (exit 3) si no se ejecuto ningun escenario.
// SOLO contra un stack QA aislado y descartable (crea/edita filas sinteticas): exige CSP_QA_STACK_AISLADO=1 y E2E_BASE_URL local.
// Uso: CSP_QA_STACK_AISLADO=1 E2E_BASE_URL=http://127.0.0.1:18220 node scripts/qa/csp-roles/escenarios.js [--browser chromium|firefox|webkit]
//        [--modo enforce|reportonly] [--solo <regex>] [--out resultados.json] [--viewport 1366x768]
'use strict';
const fs = require('fs');
const path = require('path');
const { chromium, firefox, webkit } = require('playwright');
const { BASE, assertEntorno, Escenario, login, instrumentar } = require('./escenarios/lib');

const arg = (n, d) => { const i = process.argv.indexOf(n); return i > -1 ? process.argv[i + 1] : d; };
const browserName = arg('--browser', 'chromium');
const modo = arg('--modo', 'enforce');
const solo = arg('--solo', '') ? new RegExp(arg('--solo'), 'i') : null;
const out = arg('--out', '');
const [vw, vh] = arg('--viewport', '1366x900').split('x').map(Number);
if (!['enforce', 'reportonly'].includes(modo)) { console.error('--modo: enforce|reportonly'); process.exit(2); }
assertEntorno();

const dir = path.join(__dirname, 'escenarios');
const defs = fs.readdirSync(dir).filter((f) => f.endsWith('.js') && f !== 'lib.js').sort()
  .flatMap((f) => require(path.join(dir, f)).map((d) => ({ ...d, modulo: f })));

(async () => {
  const browser = await { chromium, firefox, webkit }[browserName].launch();
  // 1) la politica realmente aplicada: sin esto, "0 violaciones" no significa nada
  const sonda = await browser.newContext();
  const rp = await (await sonda.newPage()).goto(`${BASE}/Identity/Account/Login`);
  const h = rp.headers();
  const esperada = modo === 'enforce' ? 'content-security-policy' : 'content-security-policy-report-only';
  const contraria = modo === 'enforce' ? 'content-security-policy-report-only' : 'content-security-policy';
  if (!h[esperada] || h[contraria]) { console.error(`FALLA: la app no sirve ${esperada} (o tambien sirve la otra). Levantar el stack con Csp__Mode=${modo === 'enforce' ? 'Enforce' : 'ReportOnly'}.`); process.exit(3); }
  await sonda.close();

  const auth = await browser.newContext({ viewport: { width: vw, height: vh } });
  await login(await auth.newPage(), BASE);
  const estado = await auth.storageState();
  await auth.close();

  const resultados = [];
  let ejecutados = 0;
  for (const def of defs) {
    if (solo && !solo.test(def.nombre)) continue;
    ejecutados++;
    const ctx = await browser.newContext({ viewport: { width: vw, height: vh }, locale: 'es-AR', storageState: estado, serviceWorkers: 'block', acceptDownloads: true });
    const E = new Escenario(def.nombre, ctx, { base: BASE, modo });
    E.escribe = !!def.escribe;
    ctx.__escActual = E;
    await instrumentar(ctx, E);
    await E.instalarRutas();
    const t0 = Date.now();
    let error = null;
    try {
      const page = await E.nuevaPagina();
      await def.fn(E, page);
    } catch (e) { error = String(e && e.message || e).split('\n').filter((l) => /locator|waiting for|expect|Error|Timeout|page\./.test(l)).slice(0, 3).join(' / ').slice(0, 300) + ` [ultimo click: ${E.ultimo}]`; }
    const permit = (E.permit4xx || []);
    const http4xx = E.http4xx.filter((x) => !permit.some((re) => re.test(x)));
    const fallos = [];
    if (error) fallos.push('excepcion: ' + error);
    for (const c of E.checks) if (!c.ok) fallos.push(`check: ${c.desc}${c.detalle ? ' (' + c.detalle + ')' : ''}`);
    if (!E.checks.length) fallos.push('escenario sin comprobaciones');
    if (def.requiereInteraccion !== false && !E.interacciones) fallos.push('escenario sin interacciones');
    for (const v of E.viol) fallos.push('CSP: ' + v);
    for (const v of E.pageerrors) fallos.push('pageerror: ' + v);
    for (const v of E.consola) fallos.push('consola: ' + v);
    for (const v of E.http5xx) fallos.push('5xx: ' + v);
    for (const v of http4xx) fallos.push('4xx: ' + v);
    for (const v of E.fallidas) fallos.push('request fallida: ' + v);
    for (const v of E.csp) fallos.push('documento sin cabecera CSP: ' + v);
    const r = {
      nombre: def.nombre, modulo: def.modulo, ok: fallos.length === 0, ms: Date.now() - t0,
      checks: E.checks.length, interacciones: E.interacciones, paginas: E.paginas, violaciones: E.viol.length, pageerrors: E.pageerrors.length,
      consola: E.consola.length, http5xx: E.http5xx.length, dialogos: E.dialogs.length, bloqueadasPorElArnes: E.bloqueadas.length, fallos,
    };
    resultados.push(r);
    console.log(`${r.ok ? 'OK   ' : 'FALLA'} ${r.nombre} | checks=${r.checks} interacciones=${r.interacciones} paginas=${r.paginas} viol=${r.violaciones} pageerrors=${r.pageerrors} consola=${r.consola} 5xx=${r.http5xx} dialogos=${r.dialogos}`);
    for (const f of fallos.slice(0, 12)) console.log(`       - ${f}`);
    await ctx.close();
  }
  await browser.close();
  const sum = (k) => resultados.reduce((n, r) => n + r[k], 0);
  console.log(`TOTAL ${browserName} (${modo}, ${vw}x${vh}): escenarios=${resultados.length} correctos=${resultados.filter((r) => r.ok).length} fallidos=${resultados.filter((r) => !r.ok).length} checks=${sum('checks')} interacciones=${sum('interacciones')} paginas=${sum('paginas')} violaciones=${sum('violaciones')} pageerrors=${sum('pageerrors')} consola=${sum('consola')} 5xx=${sum('http5xx')} dialogos=${sum('dialogos')}`);
  if (out) fs.writeFileSync(out, JSON.stringify({ browser: browserName, modo, viewport: `${vw}x${vh}`, resultados }, null, 1));
  if (!ejecutados) { console.error('Ningun escenario ejecutado (filtro --solo vacio o sin definiciones): medicion invalida.'); process.exit(3); }
  process.exit(resultados.every((r) => r.ok) ? 0 : 1);
})().catch((e) => { console.error(e); process.exit(1); });
