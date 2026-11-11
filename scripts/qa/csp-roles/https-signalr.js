#!/usr/bin/env node
// D-05 prueba 7: HTTPS + SignalR tras proxy. Contra un Caddy local con `tls internal` (certificado propio, ignoreHTTPSErrors) delante de
// un stack QA aislado con la app en Report-Only. Verifica: la pagina carga por HTTPS, el WebSocket /hubs/notificaciones se abre (wss) y
// permanece abierto, y no hay violaciones CSP (connect-src 'self' debe cubrir el wss del mismo origen) ni errores de pagina.
// Uso: E2E_HTTPS_URL=https://localhost:18443 node scripts/qa/csp-roles/https-signalr.js [--browser chromium|firefox|webkit]
'use strict';
const { chromium, firefox, webkit } = require('playwright');

const URL_BASE = process.env.E2E_HTTPS_URL || 'https://localhost:18443';
if (!/^https:\/\/(127\.0\.0\.1|localhost)(:\d+)?$/.test(URL_BASE)) { console.error('Solo un proxy local aislado.'); process.exit(2); }
const i = process.argv.indexOf('--browser');
const browserName = i > -1 ? process.argv[i + 1] : 'chromium';

(async () => {
  const browser = await { chromium, firefox, webkit }[browserName].launch();
  const ctx = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1366, height: 768 } });
  await ctx.addInitScript(() => {
    window.__cspViol = [];
    document.addEventListener('securitypolicyviolation', (e) => window.__cspViol.push(`${e.violatedDirective} | ${(e.blockedURI || '').slice(0, 80)}`));
  });
  const page = await ctx.newPage();
  const ws = [];
  const errores = [];
  let consolaCsp = 0;
  page.on('websocket', (w) => { const r = { url: w.url(), cerrado: false }; w.on('close', () => { r.cerrado = true; }); ws.push(r); });
  page.on('pageerror', (e) => errores.push(String(e.message).slice(0, 160)));
  page.on('console', (m) => { if (/content security policy/i.test(m.text()) && !/frame-ancestors.*report-only/i.test(m.text())) consolaCsp++; });

  await page.goto(`${URL_BASE}/Identity/Account/Login`, { waitUntil: 'domcontentloaded' });
  await page.fill('#Input_UserName, input[name="Input.UserName"]', 'administrador');
  await page.fill('#input-password, input[name="Input.Password"]', 'Admin123!');
  await page.click('button[type="submit"]');
  const nombre = page.locator('input[placeholder="Nombre y apellido"]');
  if (await nombre.isVisible({ timeout: 5000 }).catch(() => false)) {
    await nombre.fill('CSP https');
    await page.getByRole('checkbox', { name: /Declaro que le.* y acepto/i }).setChecked(true);
    await page.getByRole('button', { name: 'Aceptar y continuar' }).click();
  }
  await page.waitForURL((u) => !u.toString().toLowerCase().includes('/login'), { timeout: 20000 });
  await page.goto(`${URL_BASE}/`, { waitUntil: 'networkidle' });
  await page.waitForTimeout(4000);

  const viol = await page.evaluate(() => window.__cspViol).catch(() => []);
  const hub = ws.filter((w) => /\/hubs\/notificaciones/.test(w.url));
  const protocoloOk = hub.length > 0 && hub.every((w) => w.url.startsWith('wss://'));
  const abierto = hub.some((w) => !w.cerrado);
  const ok = protocoloOk && abierto && viol.length === 0 && consolaCsp === 0 && errores.length === 0;
  console.log(`${ok ? 'OK ' : 'FALLA'} ${browserName}: websockets=${hub.length} wss=${protocoloOk} abierto=${abierto} violaciones=${viol.length}${viol.length ? ' ' + viol.join(';') : ''} consolaCSP=${consolaCsp} pageerrors=${errores.length}`);
  await browser.close();
  process.exit(ok ? 0 : 1);
})();
