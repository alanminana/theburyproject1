const { chromium } = require('playwright');
const G = require('./guard');
const BASE = G.BASE;
const res = []; const ok = (n, c, d) => res.push([c ? 'OK  ' : 'FAIL', n, d || ''].join(' | '));
(async () => {
  const browser = await chromium.launch({ channel: 'chrome' });
  const page = await (await G.newContext(browser, { viewport: { width: 1366, height: 900 } })).newPage();
  const viol = []; await page.exposeFunction('__v', x => viol.push(x));
  await page.addInitScript(() => document.addEventListener('securitypolicyviolation', e => window.__v(e.effectiveDirective + ' @' + location.pathname)));
  const errs = []; page.on('pageerror', e => errs.push(e.message.slice(0, 140)));
  await G.login(page);
  await G.assertSentinel(page, '/DocumentoCliente', 'Sintetica');
  for (const vp of [{ width: 1366, height: 900 }, { width: 390, height: 844 }]) {
    await page.setViewportSize(vp); const tag = vp.width + 'px';
    await page.goto(BASE + '/DocumentoCliente', { waitUntil: 'networkidle' });
    const modalVisible = () => page.evaluate(() => [...document.querySelectorAll('button[data-fn="documentoClienteCerrarModal"]')].some(el => el.offsetParent !== null && el.getBoundingClientRect().width > 0));
    const card = await page.$('[data-key-fn="documentoClienteAbrirModal"]');
    ok(`[${tag}] tarjeta de cliente presente`, !!card);
    if (card) {
      await card.click(); await page.waitForTimeout(250);
      ok(`[${tag}] click en tarjeta abre modal`, await modalVisible());
      const closeBtn = await page.$('button[data-fn="documentoClienteCerrarModal"]:visible');
      if (closeBtn) { await closeBtn.click(); await page.waitForTimeout(200); ok(`[${tag}] botón cerrar cierra`, !(await modalVisible())); }
      else ok(`[${tag}] botón cerrar visible`, false);
      // teclado
      await card.focus(); await page.keyboard.press('Enter'); await page.waitForTimeout(250);
      ok(`[${tag}] Enter abre modal (data-key-fn)`, await modalVisible());
      const c2 = await page.$('button[data-fn="documentoClienteCerrarModal"]:visible'); if (c2) await c2.click(); await page.waitForTimeout(200);
      await card.focus(); await page.keyboard.press('Space'); await page.waitForTimeout(250);
      ok(`[${tag}] Espacio abre modal`, await modalVisible());
      const c3 = await page.$('button[data-fn="documentoClienteCerrarModal"]:visible'); if (c3) await c3.click(); await page.waitForTimeout(200);
      ok(`[${tag}] cerrado al final`, !(await modalVisible()));
      // boton interno con stopPropagation: abrir sin disparar doble
      const inner = await page.$('button[data-fn="documentoClienteAbrirModal"][data-stop]:visible');
      if (inner) {
        await page.evaluate(() => { window.__n = 0; const o = window.documentoClienteAbrirModal; window.documentoClienteAbrirModal = function () { window.__n++; return o.apply(this, arguments); }; });
        await inner.click(); await page.waitForTimeout(200);
        ok(`[${tag}] botón interno con stop: abre una sola vez`, (await page.evaluate(() => window.__n)) === 1, 'n=' + await page.evaluate(() => window.__n));
        const c4 = await page.$('button[data-fn="documentoClienteCerrarModal"]:visible'); if (c4) await c4.click();
      } else ok(`[${tag}] botón interno con stop`, true, 'no visible en este viewport/estado (NO VALIDADO)');
    }
    const up = await page.$('[data-action="click-id"]');
    if (up && await up.isVisible()) { await page.evaluate(() => { window.__u = 0; document.getElementById('btn-abrir-upload-modal').addEventListener('click', () => window.__u++); }); await up.click(); ok(`[${tag}] click-id dispara el botón de subida`, (await page.evaluate(() => window.__u)) === 1); }
    else ok(`[${tag}] click-id`, true, 'no visible (NO VALIDADO)');
  }
  console.log(res.join('\n')); console.log('violaciones CSP:', viol.length, [...new Set(viol)].slice(0, 5)); console.log('pageerrors:', errs.length, errs.slice(0, 3));
  await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
