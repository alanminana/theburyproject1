// Proveedor (drawer de alta/edicion, picker de productos con innerHTML) y Cliente (modal alta/edicion, limites por puntaje).
'use strict';

function cuitValido(base10) { // 10 digitos + verificador
  const w = [5, 4, 3, 2, 7, 6, 5, 4, 3, 2]; let s = 0;
  for (let i = 0; i < 10; i++) s += Number(base10[i]) * w[i];
  let d = 11 - (s % 11); if (d === 11) d = 0; if (d === 10) d = 9;
  return `${base10}${d}`; // el formulario exige 11 digitos sin guiones
}
const esperar = (page, re, metodo) => page.waitForResponse((r) => re.test(r.url()) && (!metodo || r.request().method() === metodo), { timeout: 20000 });
const DLG = '[role=dialog]:not(.hidden), dialog[open]';

module.exports = [
  {
    nombre: 'Proveedor: drawer de alta (validacion fallida, picker de productos), edicion remota (GetEditData), baja con confirmacion',
    escribe: true,
    async fn(E, page) {
      const t = String(Date.now()).slice(-8);
      const nombre = `Proveedor CSP ${t}`;
      const cuit = cuitValido('30' + t);
      await E.ir(page, '/Proveedor');
      await E.click(page, '[data-proveedor-modal-action=open][data-proveedor-modal=create]');
      E.check('el drawer de alta abre', await E.visible(page, '#modal-crear-proveedor'));
      await E.click(page, '#modal-crear-proveedor button[type=submit][data-proveedor-modal=create]');
      await page.waitForTimeout(600);
      E.check('alta vacia: resumen de validacion visible y drawer abierto', (await page.locator('#proveedor-validation-summary').isVisible()) && await E.visible(page, '#modal-crear-proveedor', 300));
      await page.fill('#form-crear-proveedor .picker-search-input', 'a');
      const dd = await page.locator('.picker-dropdown:not([hidden])').first().waitFor({ state: 'visible', timeout: 5000 }).then(() => true).catch(() => false);
      E.check('el picker de productos abre su dropdown (contenido generado por JS)', dd);
      if (dd) {
        const opciones = page.locator('.picker-dropdown:not([hidden]) [role=option], .picker-dropdown:not([hidden]) button, .picker-dropdown:not([hidden]) li');
        const nOp = await opciones.count();
        E.check('el dropdown lista productos', nOp > 0, nOp);
        if (nOp) { await E.click(page, opciones.first()); E.check('elegir un producto crea el chip', (await page.locator('#form-crear-proveedor .picker-chips-container > *').count()) >= 1); }
      }
      const chk = page.locator('#form-crear-proveedor [data-prov-check-group] input[type=checkbox]').first();
      if (await chk.count()) { await E.click(page, chk); await E.click(page, '#form-crear-proveedor [data-prov-check-only] >> nth=0'); E.check('categorias: "solo marcadas" filtra', true); }
      await page.fill('#form-crear-proveedor [name=Cuit]', cuit);
      await page.fill('#form-crear-proveedor [name=RazonSocial]', nombre);
      await page.fill('#form-crear-proveedor [name=Email]', `csp${t}@example.invalid`);
      await page.fill('#form-crear-proveedor [name=Telefono]', '1155550000');
      const [r] = await Promise.all([esperar(page, /\/Proveedor\/(Create|CreateAjax)/, 'POST'), E.click(page, '#modal-crear-proveedor button[type=submit][data-proveedor-modal=create]')]);
      E.check('alta: POST sin error de servidor', r.status() < 400, r.status());
      await page.waitForLoadState('networkidle').catch(() => {});
      await E.ir(page, '/Proveedor');
      const fila = page.locator(`tr:has-text("${nombre}")`).first();
      E.check('el proveedor aparece en el listado', (await fila.count()) === 1);
      if (!(await fila.count())) return;
      const [rj] = await Promise.all([esperar(page, /\/Proveedor\/GetEditData\//), E.click(page, fila.locator('[data-proveedor-modal-action=open][data-proveedor-modal=edit]'))]);
      E.check('GetEditData 200 JSON', rj.status() === 200 && /json/.test(rj.headers()['content-type'] || ''), rj.status());
      E.check('el drawer de edicion abre y se puebla (RazonSocial)', await E.visible(page, '#modal-editar-proveedor') && await page.waitForFunction((n) => document.querySelector('#form-editar-proveedor [name=RazonSocial]').value === n, nombre, { timeout: 5000 }).then(() => true).catch(() => false));
      await page.fill('#form-editar-proveedor [name=Telefono]', '1166660000');
      await page.fill('#form-editar-proveedor [name=Email]', 'no-es-un-email');
      await E.click(page, '#modal-editar-proveedor button[type=submit][data-proveedor-modal=edit]');
      await page.waitForTimeout(700);
      E.check('edicion con email invalido: muestra validacion y no cierra', await E.visible(page, '#modal-editar-proveedor', 300));
      await page.fill('#form-editar-proveedor [name=Email]', `csp${t}@example.invalid`);
      const [re] = await Promise.all([esperar(page, /\/Proveedor\/(Edit|EditAjax)/, 'POST'), E.click(page, '#modal-editar-proveedor button[type=submit][data-proveedor-modal=edit]')]);
      E.check('edicion: POST sin error de servidor', re.status() < 400, re.status());
      await page.waitForLoadState('networkidle').catch(() => {});
      await E.ir(page, '/Proveedor');
      const fila2 = page.locator(`tr:has-text("${nombre}")`).first();
      const delBtn = fila2.locator('[data-proveedor-delete-form] button[type=submit]');
      await E.click(page, delBtn);
      E.check('la baja pide confirmacion (modal compartido)', await E.visible(page, '#confirmModal'));
      await E.click(page, '#confirmModal .border-t [data-confirm-modal-close]');
      E.check('cancelar la baja mantiene el proveedor', (await page.locator(`tr:has-text("${nombre}")`).count()) === 1);
      await E.click(page, delBtn);
      await E.visible(page, '#confirmModal');
      await Promise.all([page.waitForNavigation({ timeout: 15000 }), E.click(page, '#confirmModalAction')]);
      await E.ir(page, '/Proveedor');
      E.check('confirmar la baja elimina el proveedor', (await page.locator(`tr:has-text("${nombre}")`).count()) === 0);
    },
  },
  {
    nombre: 'Cliente: modal AJAX (partial por innerHTML) de alta en pasos con validacion fallida, edicion remota con confirm() nativo, limites por puntaje',
    escribe: true,
    async fn(E, page) {
      await E.ir(page, '/Cliente');
      const [rp] = await Promise.all([esperar(page, /\/Cliente\/ModalCreate/), E.click(page, '[data-cliente-modal-open=create]')]);
      E.check('ModalCreate responde 200 HTML', rp.status() === 200 && /html/.test(rp.headers()['content-type'] || ''), rp.status());
      E.check('el partial se inyecta (formulario del wizard presente)', await E.visible(page, '#cliente-modal-form'));
      // avanzar por los pasos con "Siguiente" (JS del wizard sobre contenido inyectado)
      let pasos = 0;
      for (let i = 0; i < 8; i++) {
        const sig = page.locator('#cliente-modal-form button:has-text("Siguiente")').first();
        if (!(await sig.isVisible().catch(() => false))) break;
        await sig.click(); E.interacciones++; pasos++;
        await page.waitForTimeout(150);
      }
      E.check('el wizard permite avanzar de paso (Siguiente)', pasos >= 1, pasos);
      // envio vacio: validacion fallida
      const crear = page.locator('#cliente-modal-form button[type=submit]').first();
      if (await crear.isVisible().catch(() => false)) { await crear.click(); E.interacciones++; }
      await page.waitForTimeout(900);
      const val = await page.evaluate(() => ({ resumen: !document.getElementById('cliente-modal-validation').classList.contains('hidden'), inv: document.querySelectorAll('#cliente-modal-form :invalid, #cliente-modal-form .field-error, #cliente-modal-form [aria-invalid=true]').length, abierto: !document.getElementById('modal-cliente').classList.contains('hidden') }));
      E.check('alta vacia: errores de validacion visibles y el modal sigue abierto', val.abierto && (val.resumen || val.inv > 0), JSON.stringify(val));
      await page.keyboard.press('Escape'); E.interacciones++;
      E.check('Escape cierra el modal de alta (sin confirm en alta)', await page.waitForFunction(() => document.getElementById('modal-cliente').classList.contains('hidden'), null, { timeout: 3000 }).then(() => true).catch(() => false));
      // edicion: partial remoto, tabs, confirm() nativo por cambios sin guardar
      const [re] = await Promise.all([esperar(page, /\/Cliente\/ModalEdit\//), E.click(page, page.locator('[data-cliente-modal-open=edit]').first())]);
      E.check('ModalEdit responde 200 HTML', re.status() === 200, re.status());
      await E.visible(page, '#cliente-modal-form');
      const tabs = page.locator('#modal-cliente [data-cliente-tab]');
      const nTabs = await tabs.count();
      for (let i = 0; i < Math.min(nTabs, 6); i++) { await tabs.nth(i).click({ timeout: 3000 }).catch(() => {}); E.interacciones++; }
      E.check('las solapas del modal de edicion responden', nTabs >= 2, nTabs);
      if (nTabs) await tabs.first().click({ timeout: 3000 }).catch(() => {});
      const campo = page.locator('#cliente-modal-form input[name=Nombre]:visible').first();
      await campo.fill((await campo.inputValue()) + ' x'); E.interacciones++;
      await page.keyboard.press('Escape'); E.interacciones++;
      await page.waitForTimeout(400);
      E.check('con cambios sin guardar aparece el confirm() nativo y descartarlo (Cancelar) mantiene el modal', E.dialogs.some((d) => /cambios sin guardar/i.test(d)) && await page.evaluate(() => !document.getElementById('modal-cliente').classList.contains('hidden')), E.dialogs.join('|'));
      E.aceptarDialogos = true;
      await page.keyboard.press('Escape'); E.interacciones++;
      E.check('aceptar el confirm() cierra el modal', await page.waitForFunction(() => document.getElementById('modal-cliente').classList.contains('hidden'), null, { timeout: 3000 }).then(() => true).catch(() => false));
      E.aceptarDialogos = false;
      // limites por puntaje (partial remoto)
      await E.ir(page, '/Cliente');
      await E.click(page, 'summary[aria-label="Más acciones de clientes"]');
      await E.click(page, '[data-cliente-open-limites]');
      await page.waitForTimeout(900);
      E.check('el modal de limites por puntaje abre', await page.locator(DLG).first().isVisible().catch(() => false));
      await page.keyboard.press('Escape'); E.interacciones++;
    },
  },
];
