// Documentos: vista previa PDF en iframe (blob:), listados y descargas.
'use strict';

module.exports = [
  {
    nombre: 'DocumentosConfig: vista previa PDF de plantilla/paquete/regla en iframe (blob:) y abrir en pestana',
    async fn(E, page) {
      for (const ruta of ['/DocumentosConfig/Plantillas', '/DocumentosConfig/Paquetes', '/DocumentosConfig/Reglas']) {
        await E.ir(page, ruta);
        const btn = page.locator('[data-doc-preview-url]').first();
        const hay = (await btn.count()) > 0;
        E.check(`${ruta}: hay al menos un boton de vista previa (datos sembrados con --configurar-documentos)`, hay);
        if (!hay) continue;
        const [resp] = await Promise.all([page.waitForResponse((r) => /VistaPreviaPdf/.test(r.url()), { timeout: 20000 }), E.click(page, btn)]);
        E.check(`${ruta}: la vista previa responde 200 application/pdf`, resp.status() === 200 && /pdf/.test(resp.headers()['content-type'] || ''), `${resp.status()} ${resp.headers()['content-type']}`);
        await page.waitForFunction(() => { const f = document.querySelector('#doc-preview-modal iframe'); return f && !f.hidden && /^blob:/.test(f.getAttribute('src') || ''); }, null, { timeout: 8000 }).catch(() => {});
        const src = await page.evaluate(() => (document.querySelector('#doc-preview-modal iframe') || {}).src || '');
        E.check(`${ruta}: el iframe recibe el blob`, /^blob:/.test(src), src.slice(0, 30));
        await page.waitForTimeout(1200); // da tiempo al evento de violacion frame-src
        E.check(`${ruta}: el enlace "abrir" apunta al blob`, /^blob:/.test((await page.locator('#doc-preview-modal [data-doc-modal-open]').getAttribute('href')) || ''));
        await page.keyboard.press('Escape'); E.interacciones++;
        E.check(`${ruta}: Escape cierra el dialogo`, await page.evaluate(() => !document.getElementById('doc-preview-modal').open));
      }
    },
  },
];
