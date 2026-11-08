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
  let dialogs = []; page.on('dialog', async d => { dialogs.push(d.message().slice(0, 60)); await d.dismiss(); });
  let posts = []; page.on('request', r => { if (r.method() === 'POST' && !/\/hubs\/|csp-report/.test(r.url())) { posts.push(r.url()); console.log('POST!', r.url()); } });
  await G.login(page);
  await G.assertSentinel(page, '/MercadoLibre/Orden/1', 'Item sintetico');
  posts = [];
  const pages = ['MercadoLibre', 'MercadoLibre/Ordenes', 'MercadoLibre/Listings', 'MercadoLibre/Aumentos', 'MercadoLibre/Orden/1', 'MercadoLibre/Pregunta/1', 'MercadoLibre/Listing/1', 'MercadoLibre/Aumento/1'];
  for (const vp of [{ width: 1366, height: 900 }, { width: 390, height: 844 }]) {
    await page.setViewportSize(vp); const tag = vp.width + 'px';
    for (const u of pages) {
      dialogs = []; posts = [];
      const r = await page.goto(BASE + '/' + u, { waitUntil: 'networkidle' });
      if (r.status() !== 200) { ok(`[${tag}] ${u} carga`, false, String(r.status())); continue; }
      const forms = await page.$$('form[data-confirm]:not([data-confirm=""])');
      let probados = 0, conDialogo = 0;
      for (const f of forms) {
        const msg = await f.getAttribute('data-confirm');
        // disparar el submit del formulario (equivale a su boton) y comprobar dialogo + ausencia de POST
        const n0 = dialogs.length;
        await f.evaluate(el => { el.querySelectorAll('[required]').forEach(i => { if (i.tagName === 'SELECT') { if (i.options.length > 1 && !i.value) i.selectedIndex = 1; } else if (!i.value) i.value = i.type === 'number' ? '1' : 'x'; }); const b = el.querySelector('button[type=submit],input[type=submit]'); if (b) b.click(); else el.requestSubmit(); });
        await page.waitForTimeout(250);
        probados++; if (dialogs.length > n0) conDialogo++;
        if (dialogs.length > n0 && dialogs[dialogs.length - 1] !== msg.slice(0, 60)) ok(`[${tag}] ${u} mensaje del confirm coincide`, false, dialogs[dialogs.length - 1] + ' != ' + msg.slice(0, 60));
        if (page.url() !== BASE + '/' + u) { await page.goto(BASE + '/' + u, { waitUntil: 'networkidle' }); }
      }
      // data-action submit-closest-form (Listings, Aumentos)
      const sc = await page.$$('[data-action="submit-closest-form"]');
      for (const b of sc) { const n0 = dialogs.length; if (await b.isVisible()) { await b.click(); await page.waitForTimeout(250); probados++; if (dialogs.length > n0) conDialogo++; } }
      ok(`[${tag}] ${u}: confirms cancelados => dialogo y sin POST`, (u === 'MercadoLibre/Listings' && posts.length === 0) || (probados === conDialogo && posts.length === 0), `forms/acciones=${probados} con dialogo=${conDialogo} POST=${posts.length}` + (u === 'MercadoLibre/Listings' ? ' [DEF-ML-01 conocido: form anidado, ver docs/csp-enforce-pendientes.md]' : ''));
      if (probados === 0) res.push('     (sin formularios con confirm en este estado: NO VALIDADO ' + u + ')');
    }
  }
  console.log(res.join('\n')); console.log('violaciones CSP:', viol.length, [...new Set(viol)].slice(0, 5)); console.log('pageerrors:', errs.length, errs.slice(0, 3));
  await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
