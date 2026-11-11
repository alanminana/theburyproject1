#!/usr/bin/env node
// D-05 pruebas 3 y 4: recorre, por cada rol de prueba, las pantallas GET que ese rol ve (menu + un nivel de enlaces de
// detalle/impresion de las listas) con la CSP en ReportOnly. NO cuenta violaciones: eso lo hace scripts/qa/csp-medicion/preload.js
// (via NODE_OPTIONS) y se resume con resumen.js. Este script solo navega y registra por rol: paginas visitadas, HTTP>=400 y errores.
//
// Solo contra un stack QA aislado (usuarios sinteticos de DbInitializer.CreateTestUsersAsync, ya versionados). Nunca la base real.
// Uso:
//   CSP_MEDIR_OUT=out.jsonl NODE_OPTIONS="--require scripts/qa/csp-medicion/preload.js" \
//   E2E_BASE_URL=http://127.0.0.1:18180 node scripts/qa/csp-roles/crawl.js [--browser chromium|firefox|webkit] [--roles a,b]
//   node scripts/qa/csp-medicion/resumen.js out.jsonl --max-violaciones 0
'use strict';
const { chromium, firefox, webkit } = require('playwright');

const BASE = process.env.E2E_BASE_URL || 'http://127.0.0.1:18080';
if (!/^http:\/\/(127\.0\.0\.1|localhost)(:\d+)?$/.test(BASE)) {
  console.error('Solo se recorre un stack local aislado (127.0.0.1/localhost).');
  process.exit(2);
}
const arg = (n, d) => { const i = process.argv.indexOf(n); return i > -1 ? process.argv[i + 1] : d; };
const browserName = arg('--browser', 'chromium');
const USERS = {
  administrador: 'Admin123!', gerente: 'Gerente123!', vendedor: 'Vendedor123!', cajero: 'Cajero123!',
  repositor: 'Repositor123!', tecnico: 'Tecnico123!', contador: 'Contador123!',
};
const MIN_PAGINAS = Number(arg('--min-paginas', '10')); // un recorrido con menos paginas por rol (inventario vacio/menu roto) NO es verde
const roles = (arg('--roles', Object.keys(USERS).join(','))).split(',');
const NO_VISITAR = /logout|logoff|signout|\/delete|\/eliminar|\/anular|\/cancelar|\/borrar|descargar|exportar|\.(pdf|xlsx|csv)(\?|$)|\/api\/|\/hubs?\/|health|swagger/i;
const DETALLE = /\/(details|detalles|detalle|edit|ver|imprimir|comprobante|print|ficha)[a-z]*(\/|\?|$)/i;

async function login(page, user, pass) {
  await page.goto(`${BASE}/Identity/Account/Login`, { waitUntil: 'domcontentloaded', timeout: 30000 });
  await page.fill('#Input_UserName, input[name="Input.UserName"]', user);
  await page.fill('#input-password, input[name="Input.Password"]', pass);
  await page.click('button[type="submit"]');
  const nombre = page.locator('input[placeholder="Nombre y apellido"]');
  if (await nombre.isVisible({ timeout: 5000 }).catch(() => false)) {
    await nombre.fill(`CSP ${user}`);
    await page.getByRole('checkbox', { name: /Declaro que le.* y acepto/i }).setChecked(true);
    await page.getByRole('button', { name: 'Aceptar y continuar' }).click();
  }
  await page.waitForURL((u) => !u.toString().toLowerCase().includes('/login'), { timeout: 20000 });
}

function normalizar(href) {
  try {
    const u = new URL(href, BASE);
    if (u.origin !== BASE) return null;
    u.hash = '';
    return u.pathname + u.search;
  } catch { return null; }
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
  const tipo = { chromium, firefox, webkit }[browserName];
  const browser = await tipo.launch();
  const resumen = [];
  for (const rol of roles) {
    const ctx = await browser.newContext({ viewport: { width: 1366, height: 768 }, locale: 'es-AR' });
    const page = await ctx.newPage();
    const r = { rol, visitadas: 0, pocas: false, http4xx: [], http5xx: [], errores: [], violaciones: [], notas: 0 };
    // Medicion propia (independiente de preload.js, que solo engancha Chromium): securitypolicyviolation + consola.
    await ctx.addInitScript(() => {
      window.__cspViol = [];
      document.addEventListener('securitypolicyviolation', (e) => window.__cspViol.push(`${e.violatedDirective} | ${(e.blockedURI || '').slice(0, 80)} | ${e.disposition}`));
    });
    page.on('console', (m) => {
      const t = m.text();
      if (!/content security policy|csp/i.test(t)) return;
      // Aviso informativo de WebKit (no es una violacion): frame-ancestors no se evalua en Report-Only; recien aplica en Enforce.
      if (/frame-ancestors.*ignored when delivered in a report-only/i.test(t)) { r.notas++; return; }
      r.violaciones.push(`consola: ${t.slice(0, 160)}`);
    });
    page.on('pageerror', (e) => r.errores.push(String(e.message).slice(0, 160)));
    try {
      await login(page, rol, USERS[rol]);
      await page.goto(`${BASE}/`, { waitUntil: 'domcontentloaded' });
      const menu = await enlaces(page, false);
      const visitadas = new Set();
      const visitar = async (ruta, esMenu) => {
        if (visitadas.has(ruta)) return [];
        visitadas.add(ruta);
        const resp = await page.goto(BASE + ruta, { waitUntil: 'networkidle', timeout: 30000 }).catch((e) => { r.errores.push(`${ruta}: ${String(e.message).slice(0, 100)}`); return null; });
        r.visitadas++;
        const v = await page.evaluate(() => window.__cspViol || []).catch(() => []);
        for (const x of v) r.violaciones.push(`${ruta} :: ${x}`);
        const st = resp ? resp.status() : 0;
        if (st >= 500) r.http5xx.push(`${st} ${ruta}`);
        else if (st >= 400) r.http4xx.push(`${st} ${ruta}`);
        return esMenu && st < 400 ? (await enlaces(page, true)).slice(0, 3) : [];
      };
      const segundoNivel = [];
      for (const ruta of menu) segundoNivel.push(...(await visitar(ruta, true)));
      for (const ruta of segundoNivel) await visitar(ruta, false);
    } catch (e) {
      r.errores.push(`login/recorrido: ${String(e.message).slice(0, 160)}`);
    }
    if (r.visitadas < MIN_PAGINAS) { r.pocas = true; r.errores.push(`inventario insuficiente: ${r.visitadas} paginas visitadas (minimo ${MIN_PAGINAS})`); }
    await ctx.close();
    resumen.push(r);
    console.log(`${rol}: ${r.visitadas} paginas, 4xx=${r.http4xx.length}, 5xx=${r.http5xx.length}, errores=${r.errores.length}, violaciones CSP=${r.violaciones.length}`);
    for (const x of r.violaciones.slice(0, 10)) console.log(`  CSP ${x}`);
    for (const x of r.http5xx) console.log(`  5XX ${x}`);
    for (const x of r.errores.slice(0, 5)) console.log(`  ERR ${x}`);
  }
  await browser.close();
  const total = resumen.reduce((n, r) => n + r.visitadas, 0);
  console.log(`TOTAL ${browserName}: ${total} paginas en ${resumen.length} roles; 5xx=${resumen.reduce((n, r) => n + r.http5xx.length, 0)}, violaciones CSP=${resumen.reduce((n, r) => n + r.violaciones.length, 0)}, avisos frame-ancestors (solo Report-Only)=${resumen.reduce((n, r) => n + r.notas, 0)}`);
  if (!resumen.length || total === 0) { console.error('Recorrido vacio: 0 roles o 0 paginas visitadas.'); process.exit(1); }
  process.exit(resumen.some((r) => r.http5xx.length || r.errores.length || r.violaciones.length) ? 1 : 0);
})();
