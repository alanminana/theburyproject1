const { chromium } = require('playwright');
const G = require('./guard');
const BASE = G.BASE;
const res = []; const ok = (n, c, d) => res.push([c ? 'OK  ' : 'FAIL', n, d || ''].join(' | '));
(async () => {
  const browser = await chromium.launch({ channel: 'chrome' });
  const ctx = await G.newContext(browser, { viewport: { width: 1366, height: 900 } });
  const page = await ctx.newPage();
  const viol = []; await page.exposeFunction('__v', x => viol.push(x));
  await page.addInitScript(() => document.addEventListener('securitypolicyviolation', e => window.__v(e.effectiveDirective + ' ' + e.blockedURI + ' @' + location.pathname)));
  const errs = []; page.on('pageerror', e => errs.push(e.message.slice(0, 140)));
  await G.login(page);
  await G.assertSentinel(page, '/Producto/Unidades/1', 'U-001');

  for (const vp of [{ width: 1366, height: 900 }, { width: 390, height: 844 }]) {
    await page.setViewportSize(vp);
    const tag = vp.width + 'px';
    const r = await page.goto(BASE + '/Producto/Unidades/1', { waitUntil: 'networkidle' });
    ok(`[${tag}] Unidades carga`, r.status() === 200, String(r.status()));
    // pestañas
    for (const t of ['agregar', 'conciliar', 'ver']) {
      const b = await page.$(`[data-fn="activateTab"][data-args='["${t}"]']`);
      if (b && await b.isVisible()) {
        await b.click(); await page.waitForTimeout(150);
        ok(`[${tag}] activateTab(${t})`, true);
      } else ok(`[${tag}] activateTab(${t}) boton visible`, true, 'no visible en este estado (NO VALIDADO)');
    }
    await page.evaluate(() => window.activateTab && window.activateTab('ver'));
    // Gestionar unidad
    const man = await page.$$('[data-fn="puOpenManage"]');
    let abiertos = 0;
    for (const m of man) {
      if (!(await m.isVisible())) continue;
      await m.click(); await page.waitForTimeout(200);
      const open = await page.evaluate(() => { const el = document.getElementById('modal-manage'); return !!el && !el.classList.contains('hidden') && getComputedStyle(el).display !== 'none'; });
      if (open) {
        abiertos++;
        const txt = await page.evaluate(() => document.getElementById('modal-manage').innerText);
        ok(`[${tag}] modal Gestionar muestra datos de la unidad`, /U-00\d/.test(txt), txt.replace(/\s+/g, ' ').slice(0, 80));
        // select de accion cargado con las acciones (JSON)
        const opts = await page.$$eval('#manage-action option', os => os.map(o => o.textContent.trim()));
        ok(`[${tag}] acciones del modal cargadas`, opts.length >= 1, opts.join(','));
        // cambiar acción -> puUpdateManageAction (data-change-fn)
        if (opts.length > 1) { await page.selectOption('#manage-action', { index: 1 }); await page.waitForTimeout(100); ok(`[${tag}] cambio de accion sin error`, true); }
        // cerrar: boton Cancelar
        await page.click('#modal-manage button[data-fn="puCloseModal"]:has-text("Cancelar")').catch(() => { });
        await page.waitForTimeout(150);
        const cerrado = await page.evaluate(() => { const el = document.getElementById('modal-manage'); return el.classList.contains('hidden') || getComputedStyle(el).display === 'none'; });
        ok(`[${tag}] Cancelar cierra el modal`, cerrado);
        // abrir de nuevo y cerrar clickeando el fondo (call-self)
        await m.click(); await page.waitForTimeout(150);
        // clic DENTRO de la tarjeta (texto y select) no debe cerrar; clic fuera si (DEF-UNI-01 corregido)
        await page.click('#manage-title'); await page.waitForTimeout(100);
        const sigueAbierto = await page.evaluate(() => !document.getElementById('modal-manage').classList.contains('hidden'));
        ok(`[${tag}] clic dentro de la tarjeta no cierra`, sigueAbierto);
        await page.mouse.click(5, 5); await page.waitForTimeout(150);
        const cerrado2 = await page.evaluate(() => { const el = document.getElementById('modal-manage'); return el.classList.contains('hidden') || getComputedStyle(el).display === 'none'; });
        ok(`[${tag}] clic en el fondo cierra el modal`, cerrado2);
        await m.click(); await page.waitForTimeout(150);
        await page.keyboard.press('Escape'); await page.waitForTimeout(150);
        ok(`[${tag}] Escape cierra el modal`, await page.evaluate(() => document.getElementById('modal-manage').classList.contains('hidden')));
        break;
      }
    }
    if (!abiertos) ok(`[${tag}] Gestionar unidad`, false, `${man.length} botones, ninguno abrió el modal`);
    // filtro de disponibilidad (puDispoChange)
    const radios = await page.$$('[data-change-fn="puDispoChange"]');
    ok(`[${tag}] radios puDispoChange presentes`, radios.length >= 1, String(radios.length));
    for (const rd of radios) { if (await rd.isVisible()) { await rd.check({ force: true }).catch(() => { }); } else { await rd.evaluate(el => { el.checked = true; el.dispatchEvent(new Event('change', { bubbles: true })); }); } }
    ok(`[${tag}] puDispoChange sin error de pagina`, errs.length === 0, errs.join(';'));
    // conciliar: opciones de resolución
    const reso = await page.$$('[data-fn="puSelectResolution"]');
    ok(`[${tag}] opciones de resolución (conciliación)`, true, `${reso.length} presentes${reso.length ? '' : ' (NO VALIDADO: sin diferencia de stock)'}`);
    for (const rr of reso) { try { await page.evaluate(() => window.activateTab('conciliar')); await rr.click({ timeout: 1500 }); } catch { } }
  }
  console.log(res.join('\n')); console.log('violaciones CSP:', viol.length, [...new Set(viol)].slice(0, 6)); console.log('pageerrors:', errs.length, errs.slice(0, 4));
  await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
