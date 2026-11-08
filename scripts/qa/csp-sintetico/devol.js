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
    const r = await page.goto(BASE + '/Devolucion/Detalles/2', { waitUntil: 'networkidle' });
    ok(`[${tag}] Detalles carga`, r.status() === 200 && /Detalles/.test(page.url()), page.url());
    const hid = id => page.evaluate(i => { const e = document.getElementById(i); return e ? e.classList.contains('hidden') : null; }, id);
    const bR = await page.$('[data-action="toggle-hide"][data-toggle="rechazar-panel"]');
    const bC = await page.$('[data-action="toggle-hide"][data-toggle="cancelar-panel"]');
    ok(`[${tag}] botones Rechazar/Cancelar presentes`, !!bR && !!bC, `rechazar=${!!bR} cancelar=${!!bC}`);
    if (bR && bC) {
      ok(`[${tag}] paneles ocultos al inicio`, (await hid('rechazar-panel')) === true && (await hid('cancelar-panel')) === true);
      await bR.click(); ok(`[${tag}] Rechazar muestra su panel`, (await hid('rechazar-panel')) === false && (await hid('cancelar-panel')) === true);
      await bC.click(); ok(`[${tag}] Cancelar muestra el suyo y oculta el otro`, (await hid('cancelar-panel')) === false && (await hid('rechazar-panel')) === true);
      await bC.click(); ok(`[${tag}] Cancelar de nuevo lo oculta (toggle)`, (await hid('cancelar-panel')) === true);
      // los formularios de los paneles no se envían sin motivo (validación nativa) -> no probamos POST (acción destructiva)
    }
  }
  console.log(res.join('\n')); console.log('violaciones CSP:', viol.length, [...new Set(viol)].slice(0, 5)); console.log('pageerrors:', errs.length, errs.slice(0, 3));
  await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
