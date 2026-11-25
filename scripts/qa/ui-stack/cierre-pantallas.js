#!/usr/bin/env node
// A6: evidencia renderizada para el cierre formal de pantallas que quedaron en "◐" (Dashboard, Seguridad) con los estados que sus
// cierres previos marcaron NO VALIDADOS: Dashboard con roles de permisos restringidos y con datos reales (cuotas), y Seguridad (Index con
// sus 4 pestanas, RolDetails, EditUsuario, estados de error). Solo lectura (las escrituras se abortan). Pila QA local aislada.
// Por pantalla/rol/viewport: HTTP, errores de pagina, desborde, axe, y capturas en --salida. Para Dashboard ademas: todo enlace/boton de
// acceso rapido visible lleva a un destino que el MISMO rol puede abrir (sin 403 ni redireccion a AccessDenied).
// Uso: NODE_PATH=... AXE_PATH=... E2E_BASE_URL=http://127.0.0.1:P node cierre-pantallas.js [--browser b] [--salida DIR]
'use strict';
const fs = require('fs');
const path = require('path');
const { chromium, firefox, webkit } = require('playwright');
const BASE = process.env.E2E_BASE_URL || '';
if (!/^http:\/\/(127\.0\.0\.1|localhost)(:\d+)?$/.test(BASE)) { console.error('Solo stack local aislado (E2E_BASE_URL).'); process.exit(2); }
const AXE_SRC = fs.readFileSync(process.env.AXE_PATH, 'utf8');
const arg = (n, d) => { const i = process.argv.indexOf(n); return i > -1 ? process.argv[i + 1] : d; };
const browserName = arg('--browser', 'chromium');
const OUT = arg('--salida', '/tmp/cierre'); fs.mkdirSync(OUT, { recursive: true });
const USERS = { administrador: 'Admin123!', gerente: 'Gerente123!', vendedor: 'Vendedor123!', cajero: 'Cajero123!', repositor: 'Repositor123!', tecnico: 'Tecnico123!', contador: 'Contador123!' };
const VPS = [[1440, 900], [1024, 768], [768, 1024], [390, 844], [360, 740]];
const res = { navegador: browserName, filas: [], fallos: [] };
const fallo = (m) => { res.fallos.push(m); console.log('  FALLO:', m); };

async function login(page, user, pass) {
  await page.goto(`${BASE}/Identity/Account/Login`, { waitUntil: 'networkidle' });
  await page.fill('#Input_UserName, input[name="Input.UserName"]', user);
  await page.fill('#input-password, input[name="Input.Password"]', pass);
  await Promise.all([page.waitForLoadState('domcontentloaded').catch(() => {}), page.click('button[type="submit"]')]);
  const nombre = page.locator('input[placeholder="Nombre y apellido"]');
  if (await nombre.isVisible({ timeout: 5000 }).catch(() => false)) {
    await nombre.fill(`QA ${user}`);
    await page.getByRole('checkbox', { name: /Declaro que le.* y acepto/i }).setChecked(true);
    await page.getByRole('button', { name: 'Aceptar y continuar' }).click();
  }
  await page.waitForURL((u) => !u.toString().toLowerCase().includes('/login'), { timeout: 30000 }).catch(async (e) => {
    const t = (await page.evaluate(() => document.body.innerText).catch(() => '')).replace(/\s+/g, ' ').slice(0, 200);
    throw new Error(`login ${user} no salio de /login (${page.url()}): ${t} :: ${e.message.slice(0, 60)}`);
  });
}
async function axe(page) {
  await page.evaluate(() => document.fonts && document.fonts.ready).catch(() => {});
  await page.evaluate(AXE_SRC);
  return page.evaluate(async () => (await axe.run(document, { runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'best-practice'] } })).violations.map((v) => `${v.id} x${v.nodes.length}`));
}
async function medir(page, rol, ruta, etiqueta) {
  const errores = []; const h = (e) => errores.push(String(e.message).slice(0, 100)); page.on('pageerror', h);
  const fila = { rol, ruta, etiqueta, vp: {} };
  for (const [w, hh] of VPS) {
    await page.setViewportSize({ width: w, height: hh });
    let navError = null;
    const r = await page.goto(BASE + ruta, { waitUntil: 'networkidle' }).catch((e) => { navError = String(e.message).slice(0, 80); return null; });
    const st = r ? r.status() : 0;
    // Una navegacion fallida deja el documento anterior: no se mide nada sobre una pantalla que no cargo.
    if (!r) { fallo(`${rol} ${ruta} @${w}: la navegacion fallo (${navError || 'sin respuesta'})`); fila.vp[`${w}x${hh}`] = { http: 0, navError }; continue; }
    const esperaAbrir = !/acceso|403|404/i.test(etiqueta);
    if (esperaAbrir && st < 400 && /\/(login|AccessDenied)/i.test(new URL(page.url()).pathname)) { fallo(`${rol} ${ruta} @${w}: redirigio a ${new URL(page.url()).pathname} (se esperaba que la pantalla abriera)`); fila.vp[`${w}x${hh}`] = { http: st, redirigido: page.url() }; continue; }
    const sobra = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    const v = st < 400 ? await axe(page).catch((e) => [`axe fallo: ${e.message.slice(0, 50)}`]) : [];
    fila.vp[`${w}x${hh}`] = { http: st, sobra, axe: v };
    if (st >= 400 && !/acceso|403|404/i.test(etiqueta)) fallo(`${rol} ${ruta} @${w}: HTTP ${st}`);
    if (st < 400 && sobra > 1) fallo(`${rol} ${ruta} @${w}: desborde +${sobra}px`);
    if (v.length) fallo(`${rol} ${ruta} @${w}: axe ${v.join(', ')}`);
    if (st < 400 && [1440, 390].includes(w)) await page.screenshot({ path: path.join(OUT, `${rol}-${etiqueta.replace(/\W+/g, '_')}-${w}.png`), fullPage: true });
  }
  page.off('pageerror', h);
  if (errores.length) fallo(`${rol} ${ruta}: errores de pagina ${errores.slice(0, 2).join(' | ')}`);
  res.filas.push(fila);
}

(async () => {
  const browser = await { chromium, firefox, webkit }[browserName].launch();
  // --- Dashboard con cada rol: carga, y cada acceso visible es abrible por ese rol
  for (const rol of Object.keys(USERS)) {
    const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: 'es-AR' });
    const page = await ctx.newPage();
    await login(page, rol, USERS[rol]);
    const rd = await page.goto(`${BASE}/Dashboard`, { waitUntil: 'networkidle' }).catch(() => null);
    if (rd && rd.status() < 400 && !/AccessDenied/i.test(page.url())) {
      await medir(page, rol, '/Dashboard', 'dashboard');
      await page.setViewportSize({ width: 1440, height: 900 }); await page.goto(`${BASE}/Dashboard`, { waitUntil: 'networkidle' });
      const hrefs = await page.$$eval('main a[href]', (as) => as.filter((a) => a.getClientRects().length).map((a) => a.getAttribute('href')));
      const destinos = [...new Set(hrefs.filter((h) => h && h.startsWith('/') && !/logout|descargar|exportar|eliminar|anular/i.test(h)))].slice(0, 25);
      let sin = 0;
      for (const d of destinos) {
        const rr = await page.goto(BASE + d, { waitUntil: 'domcontentloaded' }).catch(() => null);
        const denegado = !rr || rr.status() >= 400 || /AccessDenied|acceso-denegado/i.test(page.url());
        if (denegado) { sin++; fallo(`dashboard ${rol}: el acceso ${d} lleva a un destino que ese rol no puede abrir (HTTP ${rr && rr.status()}, ${page.url()})`); }
      }
      res.filas.push({ rol, ruta: '/Dashboard (accesos)', destinosProbados: destinos.length, inaccesibles: sin });
    } else {
      // Solo una denegacion de permiso (401/403/redireccion a AccessDenied) es esperable; un 5xx o una navegacion fallida es un defecto.
      const denegado = rd && (rd.status() === 401 || rd.status() === 403 || (rd.status() < 400 && /AccessDenied/i.test(page.url())));
      if (!denegado) fallo(`dashboard ${rol}: /Dashboard fallo (HTTP ${rd ? rd.status() : 'sin respuesta'})`);
      res.filas.push({ rol, ruta: '/Dashboard', http: rd && rd.status(), nota: 'el rol no ve el Dashboard', denegado });
    }
    await ctx.close();
  }
  // --- Pantallas sin cierre de 4 capas: carga, desborde, axe y capturas en 5 viewports (reutiliza medir)
  {
    const cp = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: 'es-AR' });
    const pp = await cp.newPage();
    await login(pp, 'administrador', 'Admin123!');
    for (const [nom, ruta] of [['tesoreria', '/Tesoreria'], ['mora', '/Mora'], ['mora-alertas', '/Mora/Alertas'], ['reportes', '/Reporte'], ['devolucion', '/Devolucion'],
      ['cuentas-por-pagar', '/CuentaPorPagar'], ['listas-de-precio', '/ListaPrecio'], ['credito', '/Credito']]) await medir(pp, 'administrador', ruta, `pantalla ${nom}`);
    await cp.close();
  }
  // --- Seguridad (administrador): pestanas, detalle, edicion y estados de error; roles sin permiso => denegado
  const ca = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: 'es-AR' });
  const pa = await ca.newPage();
  await login(pa, 'administrador', 'Admin123!');
  // Solo lectura desde aqui (el login es un POST legitimo y va antes).
  await ca.route('**/*', (r) => (['GET', 'HEAD', 'OPTIONS'].includes(r.request().method()) ? r.continue() : r.abort('blockedbyclient')));
  await pa.goto(`${BASE}/Seguridad`, { waitUntil: 'networkidle' });
  const tabs = await pa.$$eval('a[href*="Seguridad"]', (as) => [...new Set(as.map((a) => a.getAttribute('href')))].filter((h) => h && !/delete|eliminar|toggle|Exportar/i.test(h)));
  const rutas = [...new Set(['/Seguridad', ...tabs])].slice(0, 14);
  for (const r of rutas) await medir(pa, 'administrador', r, `seguridad ${r}`);
  // detalle de rol / edicion de usuario descubiertos desde las tablas
  await pa.goto(`${BASE}/Seguridad`, { waitUntil: 'networkidle' });
  const det = await pa.$$eval('a[href]', (as) => as.map((a) => a.getAttribute('href')).filter((h) => h && /Seguridad\/(RolDetails|EditUsuario)/i.test(h)));
  for (const r of [...new Set(det)].slice(0, 4)) await medir(pa, 'administrador', r, `seguridad detalle ${r}`);
  // estados de error: filtro de fechas invertido (TempData Error), rol inexistente, usuario inexistente
  const e1 = await pa.goto(`${BASE}/Seguridad?tab=auditoria&desde=2026-12-31&hasta=2026-01-01`, { waitUntil: 'domcontentloaded' }).catch(() => null);
  const t1 = await pa.evaluate(() => document.body.textContent.replace(/\s+/g, ' '));
  res.filas.push({ rol: 'administrador', ruta: 'Seguridad fechas invertidas', http: e1 && e1.status(), muestraError: /no puede ser posterior|inv[aá]lid/i.test(t1) });
  if (!/no puede ser posterior|inv[aá]lid|error/i.test(t1)) fallo(`Seguridad: fechas invertidas no muestran mensaje de error (url ${pa.url()}; texto: ${t1.slice(0, 160)})`);
  for (const r of ['/Seguridad/RolDetails/no-existe', '/Seguridad/EditUsuario/no-existe']) {
    const rr = await pa.goto(BASE + r, { waitUntil: 'networkidle' }).catch(() => null);
    const txt = await pa.evaluate(() => document.body.innerText);
    const v = await axe(pa).catch(() => ['axe fallo']);
    res.filas.push({ rol: 'administrador', ruta: r, http: rr && rr.status(), url: pa.url(), mensaje: txt.slice(0, 120).replace(/\s+/g, ' '), axe: v });
    if (rr && rr.status() >= 500) fallo(`${r}: HTTP ${rr.status()} (debe manejar el error)`);
    if (v.length) fallo(`${r}: axe ${v.join(', ')}`);
  }
  await ca.close();
  for (const rol of ['vendedor', 'cajero', 'repositor', 'tecnico', 'contador', 'gerente']) {
    const c = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: 'es-AR' }); const p = await c.newPage();
    await login(p, rol, USERS[rol]);
    const r = await p.goto(`${BASE}/Seguridad`, { waitUntil: 'networkidle' }).catch(() => null);
    const abre = r && r.status() < 400 && !/AccessDenied/i.test(p.url());
    res.filas.push({ rol, ruta: '/Seguridad', abre, url: p.url() });
    if (abre && rol !== 'gerente') fallo(`/Seguridad no deberia abrir para ${rol}`);
    await c.close();
  }
  await browser.close();
  fs.writeFileSync(path.join(OUT, `cierre-${browserName}.json`), JSON.stringify(res, null, 2));
  console.log(`[${browserName}] cierre-pantallas: filas=${res.filas.length} fallos=${res.fallos.length}`);
  process.exit(res.fallos.length ? 1 : 0);
})().catch((e) => { console.error('ERROR', e); process.exit(3); });
