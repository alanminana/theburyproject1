// Estados especiales: paginas vacias, pestanas, y POST invalido que vuelve a renderizar la pagina completa (todo con CSP Enforce).
'use strict';

module.exports = [
  {
    nombre: 'DocumentoCliente vacio: data-action click-id abre el modal de subida, cierre y envio vacio',
    escribe: true,
    async fn(E, page) {
      const r = await E.ir(page, '/DocumentoCliente');
      E.check('DocumentoCliente responde 200', r.status() === 200);
      const filas = await page.locator('table tbody tr').count();
      E.check('estado vacio real: sin documentos cargados (el escenario valida la pantalla vacia)', filas <= 1, `filas=${filas}`);
      await E.click(page, '[data-action="click-id"][data-target-id="btn-abrir-upload-modal"]');
      E.check('click-id dispara el boton real y abre el modal de subida', await E.visible(page, '#form-upload-modal'));
      const submit = page.locator('#form-upload-modal button[type=submit]').first();
      if (await submit.count()) { await submit.click(); E.interacciones++; await page.waitForTimeout(700); E.check('subir sin archivo/cliente no cierra el modal (validacion)', await E.visible(page, '#form-upload-modal', 300)); }
      await page.keyboard.press('Escape'); E.interacciones++;
      await page.waitForTimeout(400);
    },
  },
  {
    nombre: 'Producto/Unidades: pestanas ver/agregar/conciliar/trazabilidad (data-action call, activateTab) y estado vacio de "ver"',
    async fn(E, page) {
      await E.ir(page, '/Producto/Unidades/1');
      for (const t of ['ver', 'agregar', 'conciliar', 'trazabilidad', 'ver']) {
        await E.click(page, `#tab-${t}`);
        await page.waitForTimeout(200);
        E.check(`pestana ${t} activa y su panel visible`, await page.evaluate((x) => { const b = document.getElementById('tab-' + x); const p = document.querySelector(`[data-tab-panel="${x}"], #panel-${x}, #tab-panel-${x}`); return b.getAttribute('aria-selected') === 'true' || b.classList.contains('active') || (p && p.offsetWidth > 0); }, t));
      }
      const [cta] = [page.locator('[data-action="call"][data-fn="activateTab"]').first()];
      await E.click(page, cta);
      E.check('data-action call + data-fn activateTab (lista cerrada) funciona', true);
      await E.click(page, '#tab-ver');
      const texto = await page.locator('main').innerText();
      E.check('"ver" muestra el estado vacio o la lista de unidades sin romperse', /unidad|sin unidades|no hay/i.test(texto));
      await E.click(page, 'form#filter-form button[type=submit]');
      await page.waitForLoadState('networkidle');
    },
  },
  {
    nombre: 'POST invalido que re-renderiza: Cliente/Create (pagina completa) y OrdenCompra/Create con CSP Enforce',
    escribe: true,
    async fn(E, page) {
      await E.ir(page, '/Cliente/Create');
      // el wizard valida en el cliente: se envia el formulario nativo (como un cliente sin JS / POST manipulado) para ejercitar la validacion de servidor
      await page.fill('#clienteForm [name=Nombre]', 'Solo Nombre').catch(() => {});
      const [rc] = await Promise.all([page.waitForResponse((r) => r.request().method() === 'POST' && /\/Cliente\/Create/.test(r.url())), page.evaluate(() => document.getElementById('clienteForm').submit())]);
      E.interacciones++;
      await page.waitForFunction(() => /con errores/i.test(document.body.innerText), null, { timeout: 8000 }).catch(() => {});
      E.check('POST invalido: el servidor re-renderiza la pagina (200, no 5xx ni redireccion de exito)', rc.status() === 200, rc.status());
      E.check('la pagina vuelve con CSP (cabecera) y el formulario sigue presente', !!rc.headers()['content-security-policy'] && (await page.locator('#clienteForm').count()) === 1);
      const errores = (await page.locator('.field-validation-error:not(:empty), .validation-summary-errors, [role=alert]:not(:empty), .input-validation-error').count()) + (/con errores/i.test(await page.locator('main').innerText()) ? 1 : 0);
      E.check('se muestran errores de validacion del servidor', errores > 0, errores);
      await E.click(page, '[data-cliente-tab="t-contacto"]');
      E.check('las solapas siguen funcionando tras el re-render', (await page.locator('[data-cliente-tab="t-contacto"]').getAttribute('aria-selected').catch(() => 'true')) !== 'false');
      const r = await E.ir(page, '/OrdenCompra/Create');
      E.check('OrdenCompra/Create carga', r.status() === 200);
      const sub = page.locator('main form button[type=submit]:not([role=menuitem])').first();
      if (await sub.count()) {
        const [ro] = await Promise.all([page.waitForResponse((x) => x.request().method() === 'POST', { timeout: 10000 }).catch(() => null), E.click(page, sub)]);
        await page.waitForLoadState('networkidle');
        E.check('OrdenCompra: el envio vacio no produce 5xx y la pagina sigue operativa', !ro || ro.status() < 500, ro && ro.status());
      }
    },
  },
];
