#!/usr/bin/env node
// Recorrido de accesibilidad y responsive: por cada rol de prueba visita las pantallas GET del menu (y hasta 3 enlaces de detalle por
// pantalla) en escritorio, tablet y movil. Por pagina registra: HTTP >= 400, errores de pagina (pageerror), desborde horizontal del
// documento y violaciones axe-core (WCAG 2.x A/AA + buenas practicas). Solo contra un stack QA aislado (127.0.0.1/localhost) con los
// usuarios sinteticos de DbInitializer.CreateTestUsersAsync. Nunca la base real. No sustituye una prueba con lector de pantalla real.
//
// Uso: NODE_PATH=<node_modules> E2E_BASE_URL=http://127.0.0.1:19103 AXE_PATH=<axe.min.js> \
//        node scripts/qa/a11y-responsive/crawl.js [--roles administrador,cajero] [--viewports 1366x768,768x1024,390x844] [--salida out.json]
'use strict';
const fs = require('fs');
const path = require('path');
const { chromium, firefox, webkit } = require('playwright');

const BASE = process.env.E2E_BASE_URL || 'http://127.0.0.1:18080';
if (!/^https?:\/\/(127\.0\.0\.1|localhost)(:\d+)?$/.test(BASE)) { console.error('Solo se recorre un stack local aislado.'); process.exit(2); }
const AXE = process.env.AXE_PATH;
if (!AXE || !fs.existsSync(AXE)) { console.error('Definir AXE_PATH con la ruta de axe.min.js.'); process.exit(2); }
const arg = (n, d) => { const i = process.argv.indexOf(n); return i > -1 ? process.argv[i + 1] : d; };
const USERS = {
  administrador: 'Admin123!', gerente: 'Gerente123!', vendedor: 'Vendedor123!', cajero: 'Cajero123!',
  repositor: 'Repositor123!', tecnico: 'Tecnico123!', contador: 'Contador123!',
};
const BROWSER = arg('--browser', 'chromium'); // chromium | firefox | webkit
const roles = arg('--roles', 'administrador').split(',');
const viewports = arg('--viewports', '1366x768,768x1024,390x844').split(',').map((v) => { const [w, h] = v.split('x').map(Number); return { width: w, height: h }; });
const AXE_SRC = fs.readFileSync(AXE, 'utf8');
const SALIDA = arg('--salida', '');
const VISTAS = arg('--vistas', ''); // carpeta Views/: agrega /Controlador/Accion por cada vista no parcial (las que exigen id responden 4xx y se informan aparte)
const NO_VISITAR = /logout|logoff|signout|\/delete|\/eliminar|\/anular|\/cancelar|\/borrar|descargar|exportar|\.(pdf|xlsx|csv)(\?|$)|\/api\/|\/hubs?\/|health|swagger/i;
const DETALLE = /\/(details|detalles|detalle|edit|ver|imprimir|comprobante|print|ficha)[a-z]*(\/|\?|$)/i;

async function login(page, user, pass) {
  await page.goto(`${BASE}/Identity/Account/Login`, { waitUntil: 'domcontentloaded', timeout: 30000 });
  await page.fill('#Input_UserName, input[name="Input.UserName"]', user);
  await page.fill('#input-password, input[name="Input.Password"]', pass);
  await page.click('button[type="submit"]');
  const nombre = page.locator('input[placeholder="Nombre y apellido"]');
  if (await nombre.isVisible({ timeout: 5000 }).catch(() => false)) {
    await nombre.fill(`QA ${user}`);
    await page.getByRole('checkbox', { name: /Declaro que le.* y acepto/i }).setChecked(true);
    await page.getByRole('button', { name: 'Aceptar y continuar' }).click();
  }
  await page.waitForURL((u) => !u.toString().toLowerCase().includes('/login'), { timeout: 20000 });
}

function normalizar(href) {
  try { const u = new URL(href, BASE); if (u.origin !== BASE) return null; u.hash = ''; return u.pathname + u.search; } catch { return null; }
}

async function enlaces(page, soloDetalle) {
  const hrefs = await page.$$eval('a[href]', (as) => as.map((a) => a.getAttribute('href')));
  const out = new Set();
  for (const h of hrefs) {
    if (!h || h.startsWith('#') || /^(mailto|tel|javascript):/i.test(h)) continue;
    const n = normalizar(h);
    if (!n || NO_VISITAR.test(n)) continue;
    if (soloDetalle && !DETALLE.test(n)) continue;
    out.add(n);
  }
  return [...out];
}

(async () => {
  const browser = await { chromium, firefox, webkit }[BROWSER].launch();
  const resultado = [];
  for (const rol of roles) {
    const ctx = await browser.newContext({ viewport: viewports[0], locale: 'es-AR', ignoreHTTPSErrors: true });
    const page = await ctx.newPage();
    const r = { rol, paginas: 0, http4xx: [], http5xx: [], errores: [], desborde: [], axe: {}, csp: [], axeCompletadas: 0, axeFallidas: [] };
    await ctx.addInitScript(() => { window.__csp = []; document.addEventListener('securitypolicyviolation', (e) => window.__csp.push(`${e.violatedDirective} | ${(e.blockedURI || '').slice(0, 80)}`)); });
    page.on('pageerror', (e) => r.errores.push(String(e.message).slice(0, 160)));
    try {
      await login(page, rol, USERS[rol]);
      await page.goto(`${BASE}/`, { waitUntil: 'domcontentloaded' });
      const menu = await enlaces(page, false);
      const rutas = new Set(menu);
      for (const m of menu) {
        const resp = await page.goto(BASE + m, { waitUntil: 'domcontentloaded', timeout: 30000 }).catch(() => null);
        if (resp && resp.status() < 400) {
          for (const d of (await enlaces(page, true)).slice(0, 3)) rutas.add(d);
          // Pantallas hermanas del mismo modulo (Create, Reportes, subpaginas...): hasta 8 por pantalla del menu.
          const modulo = m.split('/')[1] || '';
          for (const d of (await enlaces(page, false)).filter((x) => modulo && x.split('/')[1] === modulo && !/\/\d+(\/|$|\?)|[0-9a-f]{8}-/i.test(x)).slice(0, 8)) rutas.add(d);
        }
      }
      if (VISTAS) {
        for (const c of fs.readdirSync(VISTAS, { withFileTypes: true }).filter((d) => d.isDirectory() && !/^(Shared)$/i.test(d.name))) {
          for (const f of fs.readdirSync(path.join(VISTAS, c.name))) {
            const m = /^([A-Za-z][A-Za-z0-9]*?)(?:_tw)?\.cshtml$/.exec(f);
            if (m && !NO_VISITAR.test(`/${c.name}/${m[1]}`)) rutas.add(`/${c.name}/${m[1] === 'Index' ? '' : m[1]}`);
          }
        }
      }
      for (const ruta of rutas) {
        for (const vp of viewports) {
          await page.setViewportSize(vp);
          const resp = await page.goto(BASE + ruta, { waitUntil: 'networkidle', timeout: 30000 }).catch((e) => { if (/Download is starting/.test(String(e.message))) { (r.descargas = r.descargas || []).push(ruta); return null; } r.errores.push(`${ruta}: ${String(e.message).slice(0, 80)}`); return null; }); // una ruta que responde con un archivo (PDF/Excel) es una descarga, no un error de pagina
          const st = resp ? resp.status() : 0;
          if (vp === viewports[0]) r.paginas++;
          if (st >= 500) { r.http5xx.push(`${st} ${ruta}`); continue; }
          if (st >= 400) { r.http4xx.push(`${st} ${ruta}`); continue; }
          if (!resp) continue;
          const tam = `${vp.width}x${vp.height}`;
          for (const c of await page.evaluate(() => window.__csp || []).catch(() => [])) r.csp.push(`${tam} ${ruta} :: ${c}`);
          const sobra = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
          if (sobra > 1) r.desborde.push(`${tam} ${ruta} (+${sobra}px)`);
          let v = null;
          try {
            // page.evaluate (CDP) y no addScriptTag: con CSP en Enforce la politica bloquea el <script> inyectado y axe no se cargaria.
            await page.evaluate(AXE_SRC);
            v = await page.evaluate(async () => {
              if (typeof axe === 'undefined') throw new Error('axe no esta definido tras inyectar el script');
              const x = await axe.run(document, { runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'best-practice'] } });
              return x.violations.map((y) => ({ id: y.id, impacto: y.impact, nodos: y.nodes.length, ejemplo: y.nodes[0] ? y.nodes[0].html.slice(0, 140) : '' }));
            });
          } catch (e) { r.axeFallidas.push(`${tam} ${ruta}: ${String(e.message).slice(0, 100)}`); continue; }
          r.axeCompletadas++;
          for (const y of v) { (r.axe[y.id] ||= { impacto: y.impacto, paginas: [], ejemplo: y.ejemplo }).paginas.push(`${tam} ${ruta}`); }
        }
      }
    } catch (e) { r.errores.push(`login/recorrido: ${String(e.message).slice(0, 160)}`); }
    await ctx.close();
    resultado.push(r);
    console.log(`${rol}: ${r.paginas} paginas x ${viewports.length} tamanos; 4xx=${r.http4xx.length} 5xx=${r.http5xx.length} errores=${r.errores.length} desborde=${r.desborde.length} reglas axe=${Object.keys(r.axe).length} auditorias axe=${r.axeCompletadas} (fallidas ${r.axeFallidas.length}) violaciones CSP=${r.csp.length}`);
  }
  await browser.close();
  if (SALIDA) fs.writeFileSync(path.resolve(SALIDA), JSON.stringify({ base: BASE, viewports, roles: resultado }, null, 2));
  // Codigo de salida: una auditoria axe que no se pudo ejecutar, un 5xx, un desborde o una violacion CSP invalidan el recorrido.
  const falla = resultado.some((r) => r.axeFallidas.length || r.http5xx.length || r.desborde.length || r.csp.length || r.errores.length || r.axeCompletadas === 0);
  if (falla) console.error('Recorrido con fallos: revisar auditorias axe fallidas, 5xx, desbordes, violaciones CSP o errores de pagina.');
  process.exit(falla ? 1 : 0);
})();
