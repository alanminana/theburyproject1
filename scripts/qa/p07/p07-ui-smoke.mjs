// P-07 - QA de interfaz en el contenedor: abre las pantallas de los recorridos (con los datos que dejo p07_recorridos.py),
// a 1366x768 y 390x844, y registra errores de consola/pagina, respuestas same-origin >= 400, desborde horizontal,
// recursos que no cargan (fuentes/iconos/CSS) y moneda en formato es-AR. Guarda capturas. Solo lectura (GET).
// Uso: node p07-ui-smoke.mjs <baseUrl> <outDir> [usuario] [password]   (usa Chrome del sistema con playwright-core)
import { createRequire } from 'node:module';
import fs from 'node:fs';
import path from 'node:path';

const require = createRequire(process.env.P07_NODE_MODULES ?? 'D:/git/theburyproject1/node_modules/');
const { chromium } = require('playwright-core');

const [base, out, user = 'administrador', pass = 'Admin123!'] = process.argv.slice(2);
if (!base || !out) { console.error('uso: node p07-ui-smoke.mjs <baseUrl> <outDir> [user] [pass]'); process.exit(2); }
fs.mkdirSync(out, { recursive: true });

const rutas = [
  ['dashboard', '/Dashboard'], ['cliente-index', '/Cliente'], ['cliente-details', '/Cliente/Details/1'],
  ['catalogo', '/Catalogo'], ['orden-compra-index', '/OrdenCompra'], ['orden-compra-details', '/OrdenCompra/Details/1'],
  ['movimientos-stock', '/MovimientoStock'], ['kardex', '/MovimientoStock/Kardex/1'],
  ['venta-index', '/Venta'], ['venta-details-contado', '/Venta/Details/1'], ['venta-details-credito', '/Venta/Details/4'],
  ['cotizacion-listado', '/Cotizacion/Listado'], ['cotizacion-detalles', '/Cotizacion/Detalles/1'],
  ['caja-index', '/Caja'], ['caja-apertura', '/Caja/DetallesApertura/1'], ['caja-historial', '/Caja/Historial'],
  ['credito-index', '/Credito'], ['credito-details', '/Credito/Details/1'],
  ['devolucion-index', '/Devolucion'], ['devolucion-detalle', '/Devolucion/Detalles/1'],
  ['reporte-ventas', '/Reporte/Ventas'], ['reporte-movimientos', '/Reporte/MovimientosValorizados'],
  ['configuracion-pagos', '/ConfiguracionPago/MediosPago'], ['configuracion-credito', '/ConfiguracionPago/CreditoPersonal'],
];
const viewports = [['1366', { width: 1366, height: 768 }], ['390', { width: 390, height: 844 }]];

const browser = await chromium.launch({ channel: 'chrome', headless: true });
const resultados = [];
for (const [vpName, viewport] of viewports) {
  const ctx = await browser.newContext({ viewport, locale: 'es-AR' });
  const page = await ctx.newPage();
  await page.goto(base + '/Identity/Account/Login', { waitUntil: 'domcontentloaded' });
  await page.fill('input[name="Input.UserName"]', user);
  await page.fill('input[name="Input.Password"]', pass);
  await Promise.all([page.waitForLoadState('domcontentloaded'), page.locator('form button[type="submit"]').first().click()]);
  if (await page.locator('input[name="Input.NombreCompleto"]').count()) {
    await page.fill('input[name="Input.NombreCompleto"]', 'Usuario QA P07');
    await page.check('input[name="Input.AceptaTerminos"]', { force: true }).catch(() => {});
    await Promise.all([page.waitForLoadState('domcontentloaded'), page.locator('form button[type="submit"]').first().click()]);
  }
  for (const [name, ruta] of rutas) {
    const errores = [], fallidas = [];
    const onConsole = m => { if (m.type() === 'error') errores.push(m.text().slice(0, 160)); };
    const onPageError = e => errores.push('pageerror: ' + String(e).slice(0, 160));
    const onResp = r => { if (r.url().startsWith(base) && r.status() >= 400) fallidas.push(r.status() + ' ' + r.url().replace(base, '')); };
    const onFail = r => { if (r.url().startsWith(base)) fallidas.push('FAILED ' + r.url().replace(base, '')); };
    page.on('console', onConsole); page.on('pageerror', onPageError); page.on('response', onResp); page.on('requestfailed', onFail);
    let status = 0, titulo = '';
    try {
      const resp = await page.goto(base + ruta, { waitUntil: 'networkidle', timeout: 45000 });
      status = resp?.status() ?? 0; titulo = (await page.title()).trim();
    } catch (e) { errores.push('goto: ' + String(e).slice(0, 120)); }
    const m = await page.evaluate(() => ({
      overflow: document.documentElement.scrollWidth > window.innerWidth + 1,
      moneda: (document.body.innerText.match(/¤/g) || []).length,
      pesos: (document.body.innerText.match(/\$\s?\d{1,3}(\.\d{3})*,\d{2}/g) || []).length,
      usd: (document.body.innerText.match(/\$\s?\d{1,3}(,\d{3})+\.\d{2}/g) || []).length,
      login: location.pathname.toLowerCase().includes('/account/login'),
      iconos: [...document.querySelectorAll('.material-symbols-outlined')].slice(0, 3).map(e => getComputedStyle(e).fontFamily.slice(0, 40)),
    })).catch(() => ({}));
    await page.screenshot({ path: path.join(out, `${name}-${vpName}.png`), fullPage: false }).catch(() => {});
    page.off('console', onConsole); page.off('pageerror', onPageError); page.off('response', onResp); page.off('requestfailed', onFail);
    const ok = status === 200 && !m.login && !m.overflow && m.moneda === 0 && m.usd === 0 && errores.length === 0 && fallidas.length === 0;
    resultados.push({ vp: vpName, name, ruta, status, titulo, ok, ...m, errores, fallidas });
    console.log(`${ok ? 'OK   ' : 'FALLA'} ${vpName} ${name} [${status}] ${titulo.slice(0, 40)}${m.overflow ? ' OVERFLOW' : ''}${m.moneda ? ' moneda¤=' + m.moneda : ''}${m.usd ? ' monedaUS=' + m.usd : ''}${errores.length ? ' consola=' + errores.length : ''}${fallidas.length ? ' red=' + fallidas.join(',') : ''}`);
  }
  await ctx.close();
}
await browser.close();
fs.writeFileSync(path.join(out, 'resultado-ui.json'), JSON.stringify(resultados, null, 1));
const bad = resultados.filter(r => !r.ok);
console.log(`\nUI: ${resultados.length} pantallas x viewport, ${resultados.length - bad.length} OK, ${bad.length} con observaciones`);
for (const b of bad) console.log('  ', b.vp, b.name, JSON.stringify({ status: b.status, overflow: b.overflow, moneda: b.moneda, usd: b.usd, errores: b.errores.slice(0, 2), fallidas: b.fallidas.slice(0, 3) }));
process.exit(bad.length ? 1 : 0);
