#!/usr/bin/env node
// D-05 (activacion de Enforce): humo de SOLO LECTURA contra un despliegue (el real o un stack QA). Sirve para el pre-check y la
// verificacion inmediata de la ventana (docs/csp-enforce-activacion.md).
//
// Garantias de solo lectura: toda request que no sea GET se ABORTA, salvo el POST del formulario de login (/Identity/Account/Login) y el handshake de SignalR (POST /hubs/<hub>/negotiate, sin efecto de negocio).
// No hace clic en nada: navega pantallas por GET. Si el usuario no tiene los terminos aceptados (aparece el modal de aceptacion, que
// ESCRIBE) el script se detiene con exit 3: aceptarlos una vez a mano antes de la ventana.
//
// Credenciales por entorno (nunca por argumento ni en logs): CSP_HUMO_USER / CSP_HUMO_PASS. Un usuario de prueba del propietario, no uno real.
// Uso:
//   CSP_HUMO_URL=http://127.0.0.1:18787 CSP_HUMO_USER=... CSP_HUMO_PASS=... node scripts/ops/csp-humo-lectura.js --esperar reportonly
//   ... --esperar enforce --paginas "/,/Venta,/Caja" [--browser chromium|firefox|webkit] [--https-propio] [--salida r.json]
//   --esperar off|reportonly|enforce : politica que DEBE estar sirviendo (se verifica la cabecera correcta y que no este la otra). Obligatorio.
//   --https-propio : acepta el certificado de la CA interna de Caddy (ignoreHTTPSErrors); solo para validar el proxy desde un equipo que aun no confia en la CA.
// Codigos de salida: 0 todo bien; 1 alguna comprobacion fallo; 2 uso incorrecto; 3 no se pudo medir (login, terminos, nada recorrido).
'use strict';
const fs = require('fs');
const { chromium, firefox, webkit } = require('playwright');

const arg = (n, d) => { const i = process.argv.indexOf(n); return i > -1 ? process.argv[i + 1] : d; };
const flag = (n) => process.argv.includes(n);
const BASE = (process.env.CSP_HUMO_URL || '').replace(/\/+$/, '');
const USER = process.env.CSP_HUMO_USER || '';
const PASS = process.env.CSP_HUMO_PASS || '';
const esperar = (arg('--esperar', '') || '').toLowerCase();
const browserName = arg('--browser', 'chromium');
const paginas = (arg('--paginas', '/,/Venta,/Venta/Create,/Cliente,/Caja,/Credito,/Reporte')).split(',').map((s) => s.trim()).filter(Boolean);
const salida = arg('--salida', '');
if (!/^https?:\/\/[^/\s]+$/.test(BASE)) { console.error('CSP_HUMO_URL invalida (ej. http://127.0.0.1:18787 o https://tbp).'); process.exit(2); }
if (!['off', 'reportonly', 'enforce'].includes(esperar)) { console.error('--esperar off|reportonly|enforce es obligatorio.'); process.exit(2); }
if (!USER || !PASS) { console.error('Definir CSP_HUMO_USER y CSP_HUMO_PASS (usuario de prueba).'); process.exit(2); }
if (!{ chromium, firefox, webkit }[browserName]) { console.error('--browser invalido'); process.exit(2); }
if (paginas.some((p) => !p.startsWith('/'))) { console.error('--paginas: rutas que empiezan con /'); process.exit(2); }

const H_ENF = 'content-security-policy';
const H_RO = 'content-security-policy-report-only';

(async () => {
  const fallos = [];
  const resultados = [];
  const browser = await { chromium, firefox, webkit }[browserName].launch();
  const ctx = await browser.newContext({ viewport: { width: 1366, height: 768 }, ignoreHTTPSErrors: flag('--https-propio'), serviceWorkers: 'block' });
  const violaciones = [];
  const pageerrors = [];
  const consola = [];
  const bloqueadas = [];
  const sockets = [];
  await ctx.exposeBinding('__cspHumoV', (src, v) => violaciones.push(`${v.directiva} | ${String(v.bloqueado || '').slice(0, 80)} | ${v.ruta}`));
  await ctx.addInitScript(() => {
    document.addEventListener('securitypolicyviolation', (e) => {
      try { window.__cspHumoV({ directiva: e.effectiveDirective, bloqueado: e.blockedURI, ruta: location.pathname }); } catch (_) { /* ignorar */ }
    });
  });
  // SOLO LECTURA: se aborta cualquier escritura que no sea el login.
  await ctx.route('**/*', (route) => {
    const r = route.request();
    if (r.method() === 'GET' || r.method() === 'HEAD' || (r.method() === 'POST' && /^\/Identity\/Account\/Login$/i.test(new URL(r.url()).pathname))
      || (r.method() === 'POST' && /^\/hubs\/[A-Za-z]+\/negotiate$/.test(new URL(r.url()).pathname))) return route.continue(); // negotiate de SignalR: handshake sin efecto de negocio
    bloqueadas.push(`${r.method()} ${new URL(r.url()).pathname}`);
    return route.abort();
  });
  const page = await ctx.newPage();
  page.setDefaultTimeout(20000);
  page.on('pageerror', (e) => pageerrors.push(`${page.url()} :: ${String(e.message).slice(0, 160)}`));
  page.on('console', (m) => {
    const t = m.text();
    if (/content security policy|violates the following|refused to (apply|execute|load|connect|frame)/i.test(t) && !/frame-ancestors.*report-only/i.test(t)) consola.push(`consola CSP: ${t.slice(0, 200)}`);
  });
  page.on('websocket', (w) => { const s = { url: w.url(), cerrado: false }; w.on('close', () => { s.cerrado = true; }); sockets.push(s); });

  const verificarCabecera = (nombre, h) => {
    const enf = h[H_ENF]; const ro = h[H_RO];
    if (esperar === 'off') return !enf && !ro ? [] : [`${nombre}: con Off no debe haber cabecera CSP y hay ${enf ? 'Content-Security-Policy' : 'Report-Only'}`];
    const debe = esperar === 'enforce' ? enf : ro; const noDebe = esperar === 'enforce' ? ro : enf;
    const errs = [];
    if (!debe) errs.push(`${nombre}: falta la cabecera ${esperar === 'enforce' ? 'Content-Security-Policy' : 'Content-Security-Policy-Report-Only'}`);
    if (noDebe) errs.push(`${nombre}: tambien se sirve ${esperar === 'enforce' ? 'Report-Only' : 'Content-Security-Policy (Enforce)'}`);
    if (debe && !/report-uri \/csp-report/.test(debe)) errs.push(`${nombre}: la politica no declara report-uri /csp-report`);
    if (debe && /unsafe-eval|script-src[^;]*unsafe-inline/.test(debe)) errs.push(`${nombre}: la politica contiene unsafe-eval/unsafe-inline en script-src`);
    return errs;
  };

  try {
    const rl = await page.goto(`${BASE}/Identity/Account/Login`, { waitUntil: 'domcontentloaded', timeout: 30000 });
    const errsL = verificarCabecera('Login', rl.headers());
    fallos.push(...errsL);
    resultados.push({ ruta: '/Identity/Account/Login', status: rl.status(), cabecera: errsL.length === 0 });
    await page.fill('#Input_UserName, input[name="Input.UserName"]', USER);
    await page.fill('#input-password, input[name="Input.Password"]', PASS);
    await page.click('button[type="submit"]');
    if (await page.locator('input[placeholder="Nombre y apellido"]').isVisible({ timeout: 4000 }).catch(() => false)) {
      console.error('El usuario no acepto aun los terminos (el modal escribe). Aceptarlos a mano una vez y repetir.');
      await browser.close(); process.exit(3);
    }
    await page.waitForURL((u) => !u.toString().toLowerCase().includes('/login'), { timeout: 20000 });
  } catch (e) {
    console.error('No se pudo iniciar sesion / medir: ' + String(e.message).split('\n')[0]);
    await browser.close(); process.exit(3);
  }

  for (const ruta of paginas) {
    const antes = violaciones.length + pageerrors.length + consola.length;
    let status = 0; let errs = [];
    try {
      const r = await page.goto(BASE + ruta, { waitUntil: 'networkidle', timeout: 45000 });
      status = r.status();
      if (status !== 200) errs.push(`${ruta}: HTTP ${status}`);
      errs.push(...verificarCabecera(ruta, r.headers()));
      if (/\/login/i.test(page.url())) errs.push(`${ruta}: redirigio a login (sesion o permiso)`);
      await page.waitForTimeout(1200);
    } catch (e) { errs.push(`${ruta}: ${String(e.message).split('\n')[0]}`); }
    const nuevos = violaciones.length + pageerrors.length + consola.length - antes;
    if (nuevos) errs.push(`${ruta}: ${nuevos} violacion(es)/error(es) de pagina durante la carga`);
    resultados.push({ ruta, status, ok: errs.length === 0 });
    fallos.push(...errs);
  }
  await page.waitForTimeout(3000);
  const hub = sockets.filter((s) => /\/hubs\//.test(s.url));
  if (!hub.length) fallos.push('SignalR: no se abrio ningun WebSocket /hubs/*');
  else if (!hub.some((s) => !s.cerrado)) fallos.push('SignalR: el WebSocket se cerro');
  if (BASE.startsWith('https://') && hub.length && !hub.every((s) => s.url.startsWith('wss://'))) fallos.push('SignalR: no usa wss:// bajo HTTPS');
  for (const v of violaciones) fallos.push('violacion CSP: ' + v);
  for (const e of pageerrors) fallos.push('pageerror: ' + e);
  for (const c of consola) fallos.push(c);
  if (resultados.length <= 1) { console.error('No se recorrio ninguna pantalla.'); await browser.close(); process.exit(3); }

  console.log(`humo de lectura ${browserName} esperando ${esperar}: ${resultados.length - 1} pantallas, SignalR sockets=${hub.length} (abiertos ${hub.filter((s) => !s.cerrado).length}), violaciones=${violaciones.length}, pageerrors=${pageerrors.length}, consolaCSP=${consola.length}, escrituras abortadas=${bloqueadas.length}`);
  for (const r of resultados) console.log(`  ${r.ok === false ? 'FALLA' : 'ok   '} ${r.status} ${r.ruta}`);
  if (fallos.length) { console.log('FALLOS:'); fallos.forEach((f) => console.log('  - ' + f)); }
  if (salida) fs.writeFileSync(salida, JSON.stringify({ browser: browserName, esperar, resultados, fallos, bloqueadas }, null, 2));
  await browser.close();
  console.log(fallos.length ? 'RESULTADO: FALLA' : 'RESULTADO: OK');
  process.exit(fallos.length ? 1 : 0);
})();
