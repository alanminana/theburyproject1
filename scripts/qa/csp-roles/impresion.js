#!/usr/bin/env node
// D-05 prueba 4: paginas sin layout / impresion. Abre cada pagina, verifica que "Imprimir" invoque window.print() y que "Volver"
// regrese a la pagina anterior (acciones data-action de csp-actions.js), y cuenta violaciones CSP (securitypolicyviolation + consola).
// Solo contra un stack QA local aislado. Las rutas con id se pasan por argumento: --rutas "/Cotizacion/Imprimir/9,/Venta/ComprobanteFactura?facturaId=1"
'use strict';
const { chromium, firefox, webkit } = require('playwright');

const BASE = process.env.E2E_BASE_URL || 'http://127.0.0.1:18080';
if (!/^http:\/\/(127\.0\.0\.1|localhost)(:\d+)?$/.test(BASE)) { console.error('Solo stack local aislado.'); process.exit(2); }
const arg = (n, d) => { const i = process.argv.indexOf(n); return i > -1 ? process.argv[i + 1] : d; };
const browserName = arg('--browser', 'chromium');
const rutas = arg('--rutas', '').split(',').filter(Boolean);
if (!rutas.length) { console.error('Falta --rutas'); process.exit(2); }

(async () => {
  const browser = await { chromium, firefox, webkit }[browserName].launch();
  const ctx = await browser.newContext({ viewport: { width: 1366, height: 768 } });
  await ctx.addInitScript(() => {
    window.__cspViol = [];
    window.__printCalls = 0;
    window.print = () => { window.__printCalls++; };
    document.addEventListener('securitypolicyviolation', (e) => window.__cspViol.push(`${e.violatedDirective} | ${(e.blockedURI || '').slice(0, 80)}`));
  });
  const page = await ctx.newPage();
  let consola = 0;
  page.on('console', (m) => { if (/content security policy/i.test(m.text()) && !/frame-ancestors.*report-only/i.test(m.text())) consola++; });
  await page.goto(`${BASE}/Identity/Account/Login`);
  await page.fill('#Input_UserName, input[name="Input.UserName"]', 'administrador');
  await page.fill('#input-password, input[name="Input.Password"]', 'Admin123!');
  await page.click('button[type="submit"]');
  await page.waitForURL((u) => !u.toString().toLowerCase().includes('/login'), { timeout: 20000 });

  let fallos = 0;
  for (const ruta of rutas) {
    await page.goto(`${BASE}/`, { waitUntil: 'domcontentloaded' });
    const previa = page.url();
    const resp = await page.goto(BASE + ruta, { waitUntil: 'networkidle' });
    const st = resp.status();
    const imprimir = page.locator('[data-action="print"]').first();
    const volver = page.locator('[data-action="history-back"]').first();
    const tieneBotones = (await imprimir.count()) > 0;
    let imprimio = null, volvio = null;
    let viol = [];
    if (tieneBotones) {
      await imprimir.click();
      imprimio = (await page.evaluate(() => window.__printCalls)) === 1;
      viol = await page.evaluate(() => window.__cspViol).catch(() => []);
      await volver.click();
      await page.waitForURL((u) => u.toString() === previa, { timeout: 5000 }).then(() => { volvio = true; }).catch(() => { volvio = false; });
    }
    if (!tieneBotones) viol = await page.evaluate(() => window.__cspViol).catch(() => []);
    // Cada ruta debe ofrecer Imprimir y Volver y ambos deben funcionar: si pierden su data-action, falla (nunca pasa sin ejercitarlos).
    const ok = st === 200 && tieneBotones && imprimio === true && volvio === true && viol.length === 0;
    if (!ok) fallos++;
    console.log(`${ok ? 'OK ' : 'FALLA'} ${ruta} http=${st} botones=${tieneBotones} imprimir=${imprimio} volver=${volvio} violaciones=${viol.length}${viol.length ? ' ' + viol.join(';') : ''}`);
  }
  console.log(`consola CSP=${consola}`);
  await browser.close();
  process.exit(fallos || consola ? 1 : 0);
})();
