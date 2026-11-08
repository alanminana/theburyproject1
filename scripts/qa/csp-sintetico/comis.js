const { chromium } = require('playwright');
const BASE = process.env.CSP_BASE_URL || 'http://127.0.0.1:5199';
const res = []; const ok = (n, c, d) => res.push([c ? 'OK  ' : 'FAIL', n, d || ''].join(' | '));
(async () => {
  const browser = await chromium.launch({ channel: 'chrome' });
  const page = await (await browser.newContext({ viewport: { width: 1366, height: 900 } })).newPage();
  const viol = []; await page.exposeFunction('__v', x => viol.push(x));
  await page.addInitScript(() => document.addEventListener('securitypolicyviolation', e => window.__v(e.effectiveDirective + ' @' + location.pathname)));
  const errs = []; page.on('pageerror', e => errs.push(e.message.slice(0, 140)));
  await page.goto(BASE + '/Identity/Account/Login');
  await page.fill('input[name="Input.UserName"]', 'admin'); await page.fill('input[name="Input.Password"]', 'Admin123!');
  await Promise.all([page.waitForNavigation(), page.click('form[method=post] button[type=submit]')]);
  for (const vp of [{ width: 1366, height: 900 }, { width: 390, height: 844 }]) {
    await page.setViewportSize(vp); const tag = vp.width + 'px';
    await page.goto(BASE + '/Reporte/ComisionesVendedores', { waitUntil: 'networkidle' });
    const row = await page.$('tr[data-fn="toggleVendorDetail"]');
    ok(`[${tag}] fila de vendedor presente`, !!row);
    if (!row) continue;
    const vis = () => page.evaluate(() => { const e = document.getElementById('vendor-detail-0'); return e ? (!e.classList.contains('hidden') && e.offsetParent !== null) : null; });
    const antes = await vis();
    await row.click(); await page.waitForTimeout(250);
    const abierto = await vis();
    ok(`[${tag}] click en fila expande el detalle`, antes === false && abierto === true, `antes=${antes} despues=${abierto}`);
    // enlace dentro del detalle: stopPropagation (no debe colapsar la fila)
    const link = await page.$('#vendor-detail-0 a[data-action="stop"]');
    if (link) {
      await page.evaluate(() => document.querySelectorAll('#vendor-detail-0 a[data-action="stop"]').forEach(a => a.addEventListener('click', e => e.preventDefault())));
      await link.click(); await page.waitForTimeout(150);
      ok(`[${tag}] enlace con stop no colapsa`, (await vis()) === true);
    } else ok(`[${tag}] enlace con data-action=stop`, true, 'no presente en el detalle (NO VALIDADO)');
    await row.click(); await page.waitForTimeout(250);
    ok(`[${tag}] segundo click colapsa`, (await vis()) === false);
  }
  console.log(res.join('\n')); console.log('violaciones CSP:', viol.length, [...new Set(viol)].slice(0, 5)); console.log('pageerrors:', errs.length, errs.slice(0, 3));
  await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
