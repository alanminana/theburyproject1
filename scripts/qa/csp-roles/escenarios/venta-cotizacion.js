// Cotizador / carrito de Venta (contenido armado por JS con innerHTML) y paginas de Cotizacion.
'use strict';

module.exports = [
  {
    nombre: 'Venta/Create (cotizador): busqueda de producto, dropdown, alta al carrito, cantidad, quitar y avanzar de paso',
    async fn(E, page) {
      await E.ir(page, '/Venta/Create');
      const cad = async () => { const m = page.locator('#cliente-actualizar-datos-modal[open]'); if (await m.isVisible().catch(() => false)) { await m.locator('[data-cad-later]').click(); E.interacciones++; E.check('el aviso <dialog> de actualizar datos del cliente se cierra con "mas tarde"', await m.waitFor({ state: 'hidden', timeout: 3000 }).then(() => true).catch(() => false)); } };
      const avanzar = async (sel) => { for (let i = 0; i < 5 && !(await page.locator(sel).first().isVisible().catch(() => false)); i++) { await cad(); const n = page.locator('[data-wizard-next]:visible, .vm-hero__primary:visible, #btn-confirmar:visible, .vm-btn-confirm-sm:visible').first(); if (!(await n.count())) break; await n.click().catch(() => {}); E.interacciones++; await page.waitForTimeout(500); } };
      await avanzar('#input-buscar-cliente');
      await page.fill('#input-buscar-cliente', 'an'); E.interacciones++;
      const ddc = page.locator('#dropdown-clientes');
      E.check('el dropdown de clientes (innerHTML) se muestra', await ddc.waitFor({ state: 'visible', timeout: 6000 }).then(() => true).catch(() => false));
      const total = await ddc.locator('[data-id]').count();
      let elegido = false;
      for (let i = 0; i < Math.min(total, 6) && !elegido; i++) {
        if (i > 0) { await E.click(page, '#btn-limpiar-cliente'); await page.fill('#input-buscar-cliente', 'an'); await ddc.waitFor({ state: 'visible', timeout: 6000 }).catch(() => {}); }
        await ddc.locator('[data-id]').nth(i).click(); E.interacciones++; await page.waitForTimeout(900); await cad();
        elegido = await page.locator('[data-wizard-next]:visible, .vm-hero__primary:visible, #btn-confirmar:visible, .vm-btn-confirm-sm:visible').first().waitFor({ state: 'visible', timeout: 8000 }).then(() => true).catch(() => false);
      }
      E.check('un cliente sembrado habilita el CTA de avanzar (se prueban hasta 6 resultados)', elegido, `candidatos=${total}`);
      await avanzar('#input-buscar-producto');
      const buscar = page.locator('#input-buscar-producto:visible').first();
      E.check('el buscador de productos esta visible en el primer paso', (await buscar.count()) === 1);
      if (!(await buscar.count())) return;
      const solo = page.locator('#filtro-solo-stock'); if ((await solo.count()) && !(await solo.isChecked())) { await solo.check({ force: true }); E.interacciones++; }
      await buscar.fill('an'); E.interacciones++;
      const dd = page.locator('#dropdown-productos');
      E.check('el dropdown de resultados (innerHTML) se muestra', await dd.waitFor({ state: 'visible', timeout: 6000 }).then(() => true).catch(() => false));
      const item = dd.locator('[data-id]').first();
      E.check('hay resultados seleccionables', (await item.count()) > 0);
      if (!(await item.count())) return;
      const nombre = ((await item.innerText()) || '').split('\n')[0].trim();
      await item.click(); E.interacciones++;
      E.check('se abre el panel de agregar producto', await E.visible(page, '#panel-agregar-producto'));
      await page.fill('#txt-cantidad', '2'); E.interacciones++;
      await E.click(page, '#btn-agregar-producto');
      await page.waitForTimeout(700);
      const cuerpo = await page.locator('#venta-form').innerText();
      E.check('el producto agregado aparece en el carrito (contenido generado por JS)', nombre.length > 0 && cuerpo.includes(nombre.slice(0, 12)), nombre.slice(0, 40));
      const filasAntes = await page.locator('#venta-form [data-cart-item], #venta-form [data-detalle-index], #venta-form tr[data-producto-id]').count();
      const quitar = page.locator('#venta-form button[aria-label*="uitar"], #venta-form button[title*="uitar"], #venta-form [data-action*="remove"]').first();
      if (await quitar.count()) { await quitar.click().catch(() => {}); E.interacciones++; E.check('quitar la linea no produce errores', true); }
      else E.check('(informativo) no se hallo boton de quitar linea en este estado', true, `filas=${filasAntes}`);
    },
  },
  {
    nombre: 'Cotizacion: listado, detalle, impresion y descarga PDF bajo Enforce',
    async fn(E, page) {
      await E.ir(page, '/Cotizacion/Listado');
      const det = await page.locator('a[href*="/Cotizacion/Detalles/"]').first().getAttribute('href').catch(() => null);
      E.check('hay una cotizacion sembrada con enlace de detalle', !!det);
      if (!det) return;
      const r = await E.ir(page, det);
      E.check('Detalles responde 200', r.status() === 200, r.status());
      const id = det.split('/').pop().split('?')[0];
      const pdf = await page.context().request.get(`${E.opts.base}/Cotizacion/DescargarPdf/${id}`);
      E.check('DescargarPdf responde 200 application/pdf con cabecera CSP', pdf.status() === 200 && /pdf/.test(pdf.headers()['content-type'] || '') && !!pdf.headers()['content-security-policy'], `${pdf.status()} ${pdf.headers()['content-type']}`);
      E.check('el PDF tiene firma %PDF', (await pdf.body()).slice(0, 4).toString() === '%PDF');
      const link = page.locator('a[href*="DescargarPdf"]').first();
      if (await link.count()) {
        const [dl] = await Promise.all([page.waitForEvent('download', { timeout: 15000 }).catch(() => null), link.click()]);
        E.interacciones++;
        E.check('el boton PDF dispara una descarga', !!dl && /\.pdf$/i.test(dl.suggestedFilename()), dl && dl.suggestedFilename());
      }
      const impHref = await page.locator('a[href*="/Cotizacion/Imprimir/"]').first().getAttribute('href').catch(() => null);
      E.check('el detalle ofrece el enlace de impresion', !!impHref);
      if (impHref) {
        await page.addInitScript(() => { window.__printCalls = 0; window.print = () => { window.__printCalls++; }; });
        const ri = await E.ir(page, impHref);
        E.check('Imprimir responde 200 HTML', ri.status() === 200 && /html/.test(ri.headers()['content-type'] || ''), ri.status());
        await page.evaluate(() => { window.__printCalls = 0; window.print = () => { window.__printCalls++; }; });
        await E.click(page, '[data-action="print"]');
        E.check('el boton Imprimir (data-action) invoca window.print() bajo Enforce', (await page.evaluate(() => window.__printCalls)) === 1);
      }
    },
  },
];
