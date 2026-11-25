#!/usr/bin/env node
// A3 + A8: accesibilidad tecnica global que el crawl axe de paginas GET no cubre. Por navegador y sobre las pantallas del menu del
// administrador (pila QA local aislada, con datos sinteticos sembrados):
//  - teclado: Tab sin trampas de foco, elementos enfocados visibles, indicador de foco presente;
//  - (Tab confinado solo se exige a dialogos aria-modal="true"; un dialogo no modal como 'Notas rapidas' puede ceder el foco)
//  - modales/drawers abiertos por su disparador (data-open-modal, aria-haspopup, data-*modal*...): el foco entra al dialogo, Tab no se
//    escapa, Esc lo cierra, axe sin violaciones con el dialogo abierto. Las escrituras (POST/PUT/DELETE) se abortan: solo se abre, nunca se confirma;
//  - estados de error con axe: 404, acceso denegado (vendedor en /Seguridad) y login con credenciales invalidas;
//  - reflow WCAG 1.4.10: zoom equivalente al 200 % (640x512) y 400 % (320x256), sin desborde horizontal del documento;
//  - moviles 360x740 y 412x915 con hasTouch (+isMobile salvo Firefox): sin desborde, menu por toque, objetivos tactiles (WCAG 2.5.8, informativo);
//  - prefers-reduced-motion: ninguna animacion infinita; forced-colors: el foco sigue visible y los controles conservan borde/contorno.
// Uso: NODE_PATH=... AXE_PATH=... E2E_BASE_URL=http://127.0.0.1:P node a11y-global.js [--browser b] [--salida out.json] [--max-paginas 30]
'use strict';
const fs = require('fs');
const { chromium, firefox, webkit } = require('playwright');
const BASE = process.env.E2E_BASE_URL || '';
if (!/^http:\/\/(127\.0\.0\.1|localhost)(:\d+)?$/.test(BASE)) { console.error('Solo stack local aislado (E2E_BASE_URL).'); process.exit(2); }
const AXE_SRC = fs.readFileSync(process.env.AXE_PATH, 'utf8');
const arg = (n, d) => { const i = process.argv.indexOf(n); return i > -1 ? process.argv[i + 1] : d; };
const browserName = arg('--browser', 'chromium');
const SALIDA = arg('--salida', '');
const MAXP = Number(arg('--max-paginas', '30'));
const NO_VISITAR = /logout|logoff|signout|\/delete|\/eliminar|\/anular|\/cancelar|\/borrar|descargar|exportar|\.(pdf|xlsx|csv)(\?|$)|\/api\/|\/hubs?\/|health|swagger|imprimir|comprobante/i;
const R = { navegador: browserName, teclado: [], modales: [], errores: [], reflow: [], moviles: [], reducedMotion: [], forcedColors: [], fallos: [], info: [] };
const fallo = (m) => { R.fallos.push(m); console.log('  FALLO:', m); };
const AXE_TAGS = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'best-practice'];

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
  return page.evaluate(async (tags) => (await axe.run(document, { runOnly: { type: 'tag', values: tags } })).violations
    .map((v) => `${v.id}(${v.impact}) x${v.nodes.length}: ${((v.nodes[0] && v.nodes[0].html) || '').slice(0, 90)}`), AXE_TAGS);
}
// Navega y verifica que la pantalla pedida realmente cargo (no un error ni el documento anterior ni Login/AccessDenied).
async function ir(page, ruta) {
  let err = null;
  const r = await page.goto(BASE + ruta, { waitUntil: 'networkidle' }).catch((e) => { err = String(e.message).slice(0, 70); return null; });
  if (!r) { fallo(`${ruta}: la navegacion fallo (${err || 'sin respuesta'})`); return false; }
  if (r.status() >= 400) { fallo(`${ruta}: HTTP ${r.status()}`); return false; }
  if (/\/(login|AccessDenied)/i.test(new URL(page.url()).pathname)) { fallo(`${ruta}: redirigio a ${new URL(page.url()).pathname}`); return false; }
  return true;
}
async function menu(page) {
  await page.goto(`${BASE}/`, { waitUntil: 'domcontentloaded' });
  const hs = await page.$$eval('a[href]', (as) => as.map((a) => a.getAttribute('href')));
  const out = new Set();
  for (const h of hs) {
    if (!h || h.startsWith('#') || /^(mailto|tel|javascript):/i.test(h)) continue;
    try { const u = new URL(h, BASE); if (u.origin === BASE && !NO_VISITAR.test(u.pathname)) out.add(u.pathname + u.search); } catch { /* ignorar */ }
  }
  const base = [...out].slice(0, MAXP);
  // una pantalla de detalle/edicion por modulo (donde viven los modales con datos)
  const extra = new Set();
  for (const r of base.slice(0, 24)) {
    await page.goto(BASE + r, { waitUntil: 'domcontentloaded' }).catch(() => {});
    const ds = await page.$$eval('a[href]', (as) => as.map((a) => a.getAttribute('href')));
    for (const h of ds) { try { const u = new URL(h, BASE); if (u.origin === BASE && /\/(details|detalles|edit)[a-z]*\/\d+/i.test(u.pathname) && !NO_VISITAR.test(u.pathname) && !base.includes(u.pathname)) { extra.add(u.pathname); break; } } catch { /* */ } }
  }
  return [...base, ...extra].slice(0, MAXP + 20);
}
const soloLectura = (ctx) => ctx.route('**/*', (route) => {
  const m = route.request().method();
  if (m === 'GET' || m === 'HEAD' || m === 'OPTIONS') return route.continue();
  return route.abort('blockedbyclient');
});

async function teclado(page, ruta) {
  if (!(await ir(page, ruta))) return;
  await page.evaluate(() => { if (document.activeElement && document.activeElement.blur) document.activeElement.blur(); window.scrollTo(0, 0); });
  const total = await page.evaluate(() => document.querySelectorAll('a[href],button,input,select,textarea,[tabindex]:not([tabindex="-1"])').length);
  const seq = []; let sinFoco = 0, invisibles = 0, sinIndicador = 0; const ejemplos = []; const sinEj = [];
  for (let i = 0; i < 70; i++) {
    await page.keyboard.press('Tab');
    const e = await page.evaluate(() => {
      const el = document.activeElement; if (!el || el === document.body) return null;
      const r = el.getBoundingClientRect(); const cs = getComputedStyle(el);
      const visible = r.width > 0 && r.height > 0 && cs.visibility !== 'hidden' && cs.display !== 'none';
      const ind = (cs.outlineStyle !== 'none' && parseFloat(cs.outlineWidth) > 0) || cs.boxShadow !== 'none';
      return { id: el.tagName + '#' + (el.id || '') + '.' + String(el.className).slice(0, 30) + '@' + Math.round(r.x) + ',' + Math.round(r.y), visible, ind, html: el.outerHTML.slice(0, 80) };
    });
    if (!e) { sinFoco++; if (seq.length > 3 && sinFoco > 1) break; continue; }
    seq.push(e.id);
    if (!e.visible) { invisibles++; if (ejemplos.length < 3) ejemplos.push(e.html); }
    if (!e.ind) { sinIndicador++; if (sinEj.length < 3) sinEj.push(e.html); }
  }
  const distintos = new Set(seq).size;
  const trampa = total > 6 && seq.length >= 30 && distintos <= 3;
  await page.keyboard.press('Shift+Tab');
  R.teclado.push({ ruta, focusables: total, tabs: seq.length, distintos, invisibles, sinIndicador, trampa });
  if (trampa) fallo(`teclado ${ruta}: trampa de foco (${distintos} elementos distintos en ${seq.length} Tab)`);
  if (invisibles > 0) fallo(`teclado ${ruta}: ${invisibles} elementos enfocados no visibles (${ejemplos.join(' | ')})`);
  if (seq.length && sinIndicador / seq.length > 0.5) fallo(`teclado ${ruta}: ${sinIndicador}/${seq.length} elementos sin indicador de foco (p. ej. ${sinEj.join(' | ')})`);
}

async function modales(page, ruta) {
  if (!(await ir(page, ruta))) return;
  const SEL = '[data-open-modal],[aria-haspopup="dialog"],[data-open-ticket-modal],[data-open-devolucion-modal],[data-modal-target],[data-bs-toggle="modal"],[data-open-drawer],[data-drawer-target]';
  const SEL2 = 'button[type="button"]:not([aria-expanded]):not([data-sidebar-toggle])';
  const n = await page.locator(SEL).count();
  for (let i = 0; i < Math.min(n, 6); i++) {
    if (!(await ir(page, ruta))) continue;
    const t = page.locator(SEL).nth(i);
    if (!(await t.isVisible().catch(() => false))) continue;
    const desc = (await t.evaluate((e) => (e.getAttribute('aria-label') || e.textContent || e.id || e.tagName).trim().replace(/\s+/g, ' ').slice(0, 40))).replace(/[^\wáéíóúñ ¿?.-]/gi, '');
    await t.click({ timeout: 4000 }).catch(() => {});
    await page.waitForTimeout(500);
    const estado = await page.evaluate(() => {
      const ds = [...document.querySelectorAll('[role="dialog"],[aria-modal="true"],dialog')].filter((d) => d.getClientRects().length && getComputedStyle(d).visibility !== 'hidden' && getComputedStyle(d).display !== 'none');
      const d = ds[ds.length - 1]; if (!d) return null;
      return { id: d.id, rol: d.getAttribute('role'), modal: d.getAttribute('aria-modal'), nombre: d.getAttribute('aria-label') || d.getAttribute('aria-labelledby'), focoDentro: d.contains(document.activeElement) };
    });
    if (!estado) continue; // el disparador no abrio un dialogo (panel de navegacion, etc.)
    const m = { ruta, disparador: desc, ...estado, tabDentro: true, escCierra: false, axe: [] };
    for (let k = 0; k < 14; k++) {
      await page.keyboard.press('Tab');
      const ok = await page.evaluate((id) => { const d = id ? document.getElementById(id) : null; return !d || d.contains(document.activeElement); }, estado.id);
      if (!ok) { m.tabDentro = false; break; }
    }
    try { m.axe = await axe(page); } catch (e) { m.axe = [`axe no ejecuto: ${String(e.message).slice(0, 60)}`]; }
    await page.keyboard.press('Escape'); await page.waitForTimeout(400);
    m.escCierra = await page.evaluate((id) => { const d = id ? document.getElementById(id) : null; return !d || d.getClientRects().length === 0 || getComputedStyle(d).display === 'none' || getComputedStyle(d).visibility === 'hidden'; }, estado.id);
    R.modales.push(m);
    if (!m.nombre) fallo(`modal ${ruta} [${desc}]: sin nombre accesible (aria-label/labelledby)`);
    if (!m.focoDentro) fallo(`modal ${ruta} [${desc}]: el foco no entro al dialogo al abrir`);
    if (estado.modal === 'true' && !m.tabDentro) fallo(`modal ${ruta} [${desc}]: Tab escapa del dialogo`);
    if (!m.escCierra) fallo(`modal ${ruta} [${desc}]: Esc no cierra el dialogo`);
    if (m.axe.length) fallo(`modal ${ruta} [${desc}]: axe ${m.axe.join(' ; ')}`);
  }
}

async function modalesAccion(page, ruta) {
  if (!(await ir(page, ruta))) return;
  const RX = /^(nuev[oa]|crear|agregar|editar|ajustar|registrar|devol|pagar|cobrar|adelant|filtr|configurar|asignar|cargar)/i;
  const cands = await page.$$eval('button[type="button"],a[role="button"]', (bs) => bs.filter((b) => b.getClientRects().length && !b.closest('form[method="post" i] [type=submit]')).map((b, i) => ({ i, t: (b.getAttribute('aria-label') || b.textContent || '').trim().replace(/\s+/g, ' ').slice(0, 40) })));
  const usar = cands.filter((c) => RX.test(c.t.replace(/^[a-z_]+\s+(?=[A-Z])/, ''))).slice(0, 4);
  for (const c of usar) {
    if (!(await ir(page, ruta))) continue;
    const t = page.locator('button[type="button"],a[role="button"]').filter({ hasText: c.t.slice(0, 20) }).first();
    if (!(await t.isVisible().catch(() => false))) continue;
    await t.click({ timeout: 3000 }).catch(() => {}); await page.waitForTimeout(900);
    const estado = await page.evaluate(() => {
      const ds = [...document.querySelectorAll('[role="dialog"],[aria-modal="true"],dialog')].filter((d) => d.getClientRects().length && getComputedStyle(d).visibility !== 'hidden' && getComputedStyle(d).display !== 'none' && d.id !== 'notas-panel');
      const d = ds[ds.length - 1];
      if (!d) { const p = [...document.querySelectorAll('[id^="m-"],[class*="drawer" i][id],[id*="modal" i],[id*="drawer" i]')].find((x) => x.getBoundingClientRect().height > 20 && x.children.length > 0 && x.getClientRects().length && getComputedStyle(x).display !== 'none' && getComputedStyle(x).visibility !== 'hidden' && !x.closest('[role="dialog"],dialog')); return p ? { sinRol: p.id || p.className.toString().slice(0, 30) } : null; }
      return { id: d.id, rol: d.getAttribute('role'), modal: d.getAttribute('aria-modal'), nombre: d.getAttribute('aria-label') || d.getAttribute('aria-labelledby') || (d.querySelector('h1,h2,h3,h4,h5') ? 'encabezado' : null), focoDentro: d.contains(document.activeElement) };
    });
    if (!estado) continue;
    if (estado.sinRol) { fallo(`panel ${ruta} [${c.t}]: se abre un panel (${estado.sinRol}) sin role="dialog" ni aria-modal`); continue; }
    const m = { ruta, disparador: c.t, ...estado, tabDentro: true, escCierra: false, axe: [] };
    for (let k = 0; k < 14; k++) { await page.keyboard.press('Tab'); const ok = await page.evaluate((id) => { const d = id ? document.getElementById(id) : null; return !d || d.contains(document.activeElement); }, estado.id); if (!ok) { m.tabDentro = false; break; } }
    try { m.axe = await axe(page); } catch (e) { m.axe = [`axe no ejecuto: ${String(e.message).slice(0, 60)}`]; }
    await page.keyboard.press('Escape'); await page.waitForTimeout(400);
    m.escCierra = await page.evaluate((id) => { const d = id ? document.getElementById(id) : null; return !d || d.getClientRects().length === 0 || getComputedStyle(d).display === 'none' || getComputedStyle(d).visibility === 'hidden'; }, estado.id);
    R.modales.push(m);
    const tag = `modal ${ruta} [${c.t}]`;
    if (!m.nombre) fallo(`${tag}: sin nombre accesible`);
    if (!m.focoDentro) fallo(`${tag}: el foco no entro al dialogo`);
    if (m.modal === 'true' && !m.tabDentro) fallo(`${tag}: Tab escapa del dialogo modal`);
    if (!m.escCierra) fallo(`${tag}: Esc no cierra`);
    if (m.axe.length) fallo(`${tag}: axe ${m.axe.join(' ; ')}`);
  }
}

(async () => {
  const browser = await { chromium, firefox, webkit }[browserName].launch();
  // --- escritorio: teclado + modales + estados de error
  const ctx = await browser.newContext({ viewport: { width: 1366, height: 768 }, locale: 'es-AR' });
  const page = await ctx.newPage();
  await login(page, 'administrador', 'Admin123!');
  const rutas = await menu(page);
  R.info.push(`paginas del menu: ${rutas.length}`);
  await soloLectura(ctx);
  for (const r of rutas) { await teclado(page, r); await modales(page, r); await modalesAccion(page, r); }
  const e404 = await page.goto(`${BASE}/Venta/Details/99999999`, { waitUntil: 'networkidle' }).catch(() => null);
  const v404 = await axe(page).catch((e) => [`axe: ${e.message}`]);
  R.errores.push({ caso: '404 Venta/Details/99999999', http: e404 && e404.status(), axe: v404 });
  if (v404.length) fallo(`estado 404: axe ${v404.join(' ; ')}`);
  await ctx.close();

  const cv = await browser.newContext({ viewport: { width: 1366, height: 768 }, locale: 'es-AR' });
  const pv = await cv.newPage();
  await login(pv, 'vendedor', 'Vendedor123!');
  const den = await pv.goto(`${BASE}/Seguridad`, { waitUntil: 'networkidle' }).catch(() => null);
  const vden = await axe(pv).catch((e) => [`axe: ${e.message}`]);
  R.errores.push({ caso: 'acceso denegado (vendedor /Seguridad)', http: den && den.status(), axe: vden, url: pv.url() });
  if (vden.length) fallo(`estado acceso denegado: axe ${vden.join(' ; ')}`);
  await cv.close();

  const cl = await browser.newContext({ locale: 'es-AR' });
  const pl = await cl.newPage();
  await pl.goto(`${BASE}/Identity/Account/Login`, { waitUntil: 'domcontentloaded' });
  await pl.fill('#Input_UserName, input[name="Input.UserName"]', 'administrador');
  await pl.fill('#input-password, input[name="Input.Password"]', 'incorrecta-1');
  await Promise.all([pl.waitForResponse((r) => r.request().method() === 'POST' && /Login/i.test(r.url())), pl.click('button[type="submit"]')]);
  await pl.waitForLoadState('networkidle');
  const vlog = await axe(pl).catch((e) => [`axe: ${e.message}`]);
  const msg = await pl.locator('.text-danger, [role="alert"], .validation-summary-errors, .field-validation-error').count();
  R.errores.push({ caso: 'login con credenciales invalidas', axe: vlog, mensajes: msg });
  if (!msg) fallo('login invalido: no se muestra mensaje de error');
  if (vlog.length) fallo(`login invalido: axe ${vlog.join(' ; ')}`);
  await cl.close();

  // --- reflow (zoom 200/400 %)
  const cr = await browser.newContext({ viewport: { width: 1280, height: 1024 }, locale: 'es-AR' });
  const pr = await cr.newPage();
  await login(pr, 'administrador', 'Admin123!');
  for (const [nom, vp] of [['zoom200', { width: 640, height: 512 }], ['zoom400', { width: 320, height: 256 }]]) {
    await pr.setViewportSize(vp);
    for (const r of rutas.slice(0, 14)) {
      if (!(await ir(pr, r))) continue;
      const sobra = await pr.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
      R.reflow.push({ nom, ruta: r, sobra });
      if (sobra > 1) fallo(`reflow ${nom} ${r}: desborde horizontal +${sobra}px`);
    }
  }
  await cr.close();

  // --- moviles con toque
  for (const [nom, vp] of [['360x740', { width: 360, height: 740 }], ['412x915', { width: 412, height: 915 }]]) {
    const o = { viewport: vp, locale: 'es-AR', hasTouch: true, deviceScaleFactor: 2 };
    if (browserName !== 'firefox') o.isMobile = true;
    const cm = await browser.newContext(o);
    const pm = await cm.newPage();
    await login(pm, 'administrador', 'Admin123!');
    for (const r of rutas.slice(0, 20)) {
      if (!(await ir(pm, r))) continue;
      const m = await pm.evaluate(() => {
        const pequenos = [...document.querySelectorAll('a[href],button,input:not([type=hidden]),select,textarea,[role=button],[role=tab]')].filter((e) => {
          const b = e.getBoundingClientRect(); const cs = getComputedStyle(e);
          return b.width > 0 && b.height > 0 && cs.visibility !== 'hidden' && (b.width < 24 || b.height < 24) && !(e.tagName === 'A' && e.closest('p,li,td,span') && cs.display === 'inline');
        }).length;
        return { sobra: document.documentElement.scrollWidth - document.documentElement.clientWidth, pequenos };
      });
      R.moviles.push({ nom, ruta: r, ...m });
      if (m.sobra > 1) fallo(`movil ${nom} ${r}: desborde horizontal +${m.sobra}px`);
      if (m.pequenos > 0) R.info.push(`movil ${nom} ${r}: ${m.pequenos} objetivos tactiles < 24px`);
    }
    await pm.goto(`${BASE}/`, { waitUntil: 'networkidle' });
    const toggle = pm.locator('[aria-label*="men" i], button[aria-controls*="sidebar" i], #sidebar-toggle, [data-sidebar-toggle]').first();
    if (await toggle.count()) {
      await toggle.tap().catch(() => {}); await pm.waitForTimeout(400);
      const abre = await pm.evaluate(() => [...document.querySelectorAll('nav a, aside a')].some((a) => a.getClientRects().length > 0));
      R.moviles.push({ nom, ruta: '(tap menu)', menuAbre: abre });
      if (!abre) fallo(`movil ${nom}: el menu no abre al tocar`);
    } else R.info.push(`movil ${nom}: no se encontro el boton de menu para la prueba tactil`);
    await cm.close();
  }

  // --- reduced motion y forced colors
  for (const [nom, opts] of [['reduced-motion', { reducedMotion: 'reduce' }], ['forced-colors', { forcedColors: 'active' }]]) {
    let c2;
    try { c2 = await browser.newContext({ viewport: { width: 1366, height: 768 }, locale: 'es-AR', ...opts }); } catch (e) { R.info.push(`${nom} no soportado en ${browserName}: ${String(e.message).slice(0, 60)}`); continue; }
    const p2 = await c2.newPage();
    await login(p2, 'administrador', 'Admin123!');
    for (const r of rutas.slice(0, 12)) {
      if (!(await ir(p2, r))) continue;
      if (nom === 'reduced-motion') {
        const inf = await p2.evaluate(() => [...document.querySelectorAll('*')].filter((e) => { const cs = getComputedStyle(e); return cs.animationName !== 'none' && cs.animationIterationCount === 'infinite' && parseFloat(cs.animationDuration) > 0.01 && e.getClientRects().length; }).map((e) => e.tagName + '.' + String(e.className).slice(0, 30)));
        R.reducedMotion.push({ ruta: r, animacionesInfinitas: inf });
        if (inf.length) fallo(`reduced-motion ${r}: ${inf.length} animaciones infinitas activas (${inf.slice(0, 2).join(', ')})`);
      } else {
        await p2.keyboard.press('Tab'); await p2.keyboard.press('Tab');
        const f = await p2.evaluate(() => {
          const el = document.activeElement; if (!el || el === document.body) return { foco: false, forced: matchMedia('(forced-colors: active)').matches };
          const cs = getComputedStyle(el);
          const botones = [...document.querySelectorAll('button:not([disabled]),a.btn-erp-primary,a.btn-erp-secondary,input:not([type=hidden]),select')].filter((e) => e.getClientRects().length);
          const sinContorno = botones.filter((e) => { const s = getComputedStyle(e); return s.borderTopStyle === 'none' && s.outlineStyle === 'none' && s.boxShadow === 'none' && !e.closest('table,nav,aside'); }).length;
          return { foco: true, outline: cs.outlineStyle !== 'none' && parseFloat(cs.outlineWidth) > 0, forced: matchMedia('(forced-colors: active)').matches, sinContorno, total: botones.length };
        });
        R.forcedColors.push({ ruta: r, ...f });
        if (!f.forced) R.info.push(`forced-colors ${r}: el media query no esta activo en ${browserName}`);
        if (f.foco && f.forced && !f.outline) fallo(`forced-colors ${r}: el elemento enfocado no muestra contorno`);
        if (f.forced && f.total && f.sinContorno / f.total > 0.5) fallo(`forced-colors ${r}: ${f.sinContorno}/${f.total} controles sin borde/contorno`);
      }
    }
    if (nom === 'forced-colors' && SALIDA) await p2.screenshot({ path: SALIDA.replace(/\.json$/, '') + '-forced.png' }).catch(() => {});
    await c2.close();
  }
  await browser.close();
  console.log(`[${browserName}] teclado=${R.teclado.length} pag, modales=${R.modales.length}, reflow=${R.reflow.length}, moviles=${R.moviles.length}, fallos=${R.fallos.length}`);
  if (SALIDA) fs.writeFileSync(SALIDA, JSON.stringify(R, null, 2));
  process.exit(R.fallos.length ? 1 : 0);
})().catch((e) => { console.error('ERROR', e); process.exit(3); });
