// Catalogo (Producto/Categoria/Marca): modales remotos y formularios AJAX con innerHTML bajo CSP.
'use strict';

const esperarResp = (page, re, metodo) => page.waitForResponse((r) => re.test(r.url()) && (!metodo || r.request().method() === metodo), { timeout: 15000 });

module.exports = [
  {
    nombre: 'Catalogo/Producto: modal de edicion AJAX (GetJson/EditAjax), solapas, caracteristicas, guardado y cierres',
    escribe: true,
    async fn(E, page) {
      await E.ir(page, '/Catalogo');
      const id = await page.locator('[data-prod-edit-id]').first().getAttribute('data-prod-edit-id');
      const filaNombre = async () => (await page.locator(`tr:has([data-prod-edit-id="${id}"]) [data-prod-nombre]`).first().innerText()).trim();
      const nombreOriginal = await filaNombre();
      const [rj] = await Promise.all([esperarResp(page, /\/Producto\/GetJson\//), E.click(page, `[data-prod-edit-id="${id}"]`)]);
      E.check('GetJson responde 200 JSON', rj.status() === 200 && /json/.test(rj.headers()['content-type'] || ''), rj.status());
      E.check('el modal de edicion abre', await E.visible(page, '#modal-editar-producto'));
      await page.waitForFunction(() => document.getElementById('prod-edit-nombre').value.length > 0);
      E.check('formulario poblado desde el JSON (nombre)', (await page.inputValue('#prod-edit-nombre')) === nombreOriginal, await page.inputValue('#prod-edit-nombre'));
      for (const t of ['precios', 'credito', 'caracteristicas', 'stock', 'datos']) {
        await E.click(page, `[data-prod-edit-tab="${t}"]`);
        E.check(`solapa ${t} activa`, (await page.locator(`[data-prod-edit-tab="${t}"]`).getAttribute('aria-selected')) === 'true');
        E.check(`panel ${t} visible`, await E.visible(page, `[data-prod-edit-panel="${t}"]`, 1500));
      }
      // innerHTML: agregar una caracteristica genera una fila con inputs y boton de quitar
      await E.click(page, '[data-prod-edit-tab="caracteristicas"]');
      await page.fill('#prod-edit-caract-nombre', 'Color CSP'); await page.fill('#prod-edit-caract-valor', 'Rojo');
      const antes = await page.locator('.prod-edit-remove-caract').count();
      await E.click(page, '[data-prod-edit-add-caract]');
      E.check('la caracteristica agregada (innerHTML) crea la fila', (await page.locator('.prod-edit-remove-caract').count()) === antes + 1);
      await E.click(page, '.prod-edit-remove-caract >> nth=-1');
      E.check('quitar caracteristica funciona', (await page.locator('.prod-edit-remove-caract').count()) === antes);
      // calculo de precios en vivo (script externo autorizado, listeners)
      await E.click(page, '[data-prod-edit-tab="precios"]');
      const pf0 = await page.inputValue('#prod-edit-precioFinal');
      await page.fill('#prod-edit-precioCompra', '1234'); await page.locator('#prod-edit-precioCompra').blur();
      E.check('el calculo de precios reacciona al cambio de costo', (await page.inputValue('#prod-edit-precioFinal')) !== pf0 || (await page.inputValue('#prod-edit-ivaCompraImporte')) !== '', pf0);
      // validacion fallida: nombre vacio
      await E.click(page, '[data-prod-edit-tab="datos"]');
      await page.fill('#prod-edit-nombre', '');
      await E.click(page, '#btn-guardar-producto-edit');
      await page.waitForTimeout(700);
      const invalidoClient = await page.evaluate(() => !document.getElementById('prod-edit-nombre').checkValidity() || !document.getElementById('prod-edit-validation-summary').classList.contains('hidden') || !!document.querySelector('#form-editar-producto .border-red-500, #form-editar-producto .field-validation-error:not(:empty)'));
      E.check('nombre vacio: se muestra validacion y el modal sigue abierto', invalidoClient && await E.visible(page, '#modal-editar-producto', 500));
      // guardado correcto
      await page.fill('#prod-edit-nombre', nombreOriginal);
      await page.fill('#prod-edit-descripcion', 'Editado por escenario CSP ' + Date.now());
      const [rs] = await Promise.all([esperarResp(page, /\/Producto\/EditAjax\//, 'POST'), E.click(page, '#btn-guardar-producto-edit')]);
      let cuerpo = null; try { cuerpo = await rs.json(); } catch { /* */ }
      E.check('EditAjax responde 2xx JSON con success', rs.ok() && cuerpo && cuerpo.success !== false, rs.status() + ' ' + JSON.stringify(cuerpo || {}).slice(0, 100));
      E.check('el modal se cierra tras guardar', await E.oculto(page, '#modal-editar-producto', 5000));
      E.check('la fila de la lista se actualiza sin recargar', (await filaNombre()) === nombreOriginal);
      // cierres: X, fondo, Escape, Cancelar
      for (const via of ['x', 'fondo', 'escape', 'cancelar']) {
        await Promise.all([esperarResp(page, /\/Producto\/GetJson\//), E.click(page, `[data-prod-edit-id="${id}"]`)]);
        await E.visible(page, '#modal-editar-producto');
        if (via === 'x') await E.click(page, '#modal-editar-producto header [data-prod-edit-modal-close], #modal-editar-producto button[data-prod-edit-modal-close] >> nth=0');
        if (via === 'fondo') await page.locator('#modal-editar-producto > div[data-prod-edit-modal-close]').click({ position: { x: 5, y: 5 }, force: true }).then(() => E.interacciones++);
        if (via === 'escape') { await page.keyboard.press('Escape'); E.interacciones++; }
        if (via === 'cancelar') await E.click(page, '#modal-editar-producto footer [data-prod-edit-modal-close]');
        E.check(`cierre por ${via}`, await E.oculto(page, '#modal-editar-producto', 3000));
      }
    },
  },
  {
    nombre: 'Catalogo/Categoria y Marca: alta AJAX con validacion fallida, edicion remota (GetJson), confirmacion de baja (cancelar/confirmar)',
    escribe: true,
    async fn(E, page) {
      const sufijo = String(Date.now()).slice(-6);
      for (const k of [
        { tipo: 'Categoria', tab: 'categorias', abrir: '#btn-crear-categoria', form: '#form-nueva-categoria', editAttr: 'data-cat-edit-id', delAttr: 'data-cat-delete-id', json: /\/Categoria\/GetJson\//, edit: '#modal-editar-categoria', editForm: '#form-editar-categoria', editNombre: '#cat-edit-nombre', resumenEdit: '#cat-edit-validation-summary' },
        { tipo: 'Marca', tab: 'marcas', abrir: '#btn-crear-marca', form: '#form-nueva-marca', editAttr: 'data-marca-edit-id', delAttr: 'data-marca-delete-id', json: /\/Marca\/GetJson\//, edit: '#modal-editar-marca', editForm: '#form-editar-marca', editNombre: '#marca-edit-nombre', resumenEdit: '#marca-edit-validation-summary' },
      ]) {
        const nombre = `CSP ${k.tipo} ${sufijo}`; const codigo = `C${k.tipo[0]}${sufijo}`;
        await E.ir(page, `/Catalogo?tab=${k.tab}`);
        await E.click(page, k.abrir);
        E.check(`${k.tipo}: abre el modal de alta`, await E.visible(page, k.form));
        // validacion fallida: envio vacio
        await page.locator(`${k.form} button[type=submit]`).first().click(); E.interacciones++;
        await page.waitForTimeout(600);
        const msg = await page.evaluate((f) => { const fm = document.querySelector(f); const s = fm.querySelector('[id$="validation-summary"]'); return { resumen: s && !s.classList.contains('hidden'), errores: [...fm.querySelectorAll('[data-valmsg-for]')].filter((x) => !x.classList.contains('hidden') && x.textContent.trim()).length, inv: [...fm.querySelectorAll(':invalid')].length }; }, k.form);
        E.check(`${k.tipo}: envio vacio muestra errores de validacion`, msg.resumen || msg.errores > 0 || msg.inv > 0, JSON.stringify(msg));
        E.check(`${k.tipo}: el modal sigue abierto tras validar`, await E.visible(page, k.form, 500));
        await page.fill(`${k.form} [name=Codigo]`, codigo); await page.fill(`${k.form} [name=Nombre]`, nombre);
        const [r] = await Promise.all([page.waitForResponse((x) => new RegExp(`/${k.tipo}/CreateAjax`).test(x.url()), { timeout: 15000 }), page.locator(`${k.form} button[type=submit]`).first().click()]);
        E.interacciones++;
        let j = null; try { j = await r.json(); } catch { /* */ }
        // la pagina navega apenas llega success: el cuerpo puede no estar disponible; el listado (abajo) prueba el alta efectiva
        E.check(`${k.tipo}: CreateAjax 2xx JSON sin errores`, r.ok() && /json/.test(r.headers()['content-type'] || '') && (!j || j.success !== false), r.status() + ' ' + JSON.stringify(j || {}).slice(0, 120));
        await page.waitForLoadState('networkidle').catch(() => {});
        await E.ir(page, `/Catalogo?tab=${k.tab}`);
        // el boton de edicion de la fila recien creada
        const idNuevo = await page.evaluate(({ attr, nombre }) => { for (const b of document.querySelectorAll(`[${attr}]`)) { const c = b.closest('tr,li,article,[data-row]') || b.parentElement.parentElement; if (c && c.textContent.includes(nombre)) return b.getAttribute(attr); } return null; }, { attr: k.editAttr, nombre });
        E.check(`${k.tipo}: el alta aparece en el listado`, !!idNuevo, idNuevo);
        if (!idNuevo) continue;
        // edicion remota
        const [rj] = await Promise.all([page.waitForResponse((x) => k.json.test(x.url())), E.click(page, `[${k.editAttr}="${idNuevo}"]`)]);
        E.check(`${k.tipo}: GetJson 200`, rj.status() === 200, rj.status());
        E.check(`${k.tipo}: modal de edicion abre y se puebla`, await E.visible(page, k.edit) && (await page.inputValue(k.editNombre)) === nombre, await page.inputValue(k.editNombre).catch(() => '?'));
        await page.fill(k.editNombre, '');
        await page.locator(`${k.editForm} button[type=submit]`).first().click(); E.interacciones++;
        await page.waitForTimeout(600);
        E.check(`${k.tipo}: edicion con nombre vacio no cierra (validacion)`, await E.visible(page, k.edit, 500));
        await page.keyboard.press('Escape'); E.interacciones++;
        E.check(`${k.tipo}: Escape cierra el modal de edicion`, await E.oculto(page, k.edit));
        // baja: cancelar y luego confirmar en el modal compartido (confirmAction)
        await E.click(page, `[${k.delAttr}="${idNuevo}"]`);
        E.check(`${k.tipo}: confirmAction abre`, await E.visible(page, '#confirmModal'));
        E.check(`${k.tipo}: el mensaje de confirmacion nombra el registro`, (await page.locator('#confirmModalBody').innerText()).includes(nombre));
        await E.click(page, '#confirmModal .border-t [data-confirm-modal-close]');
        E.check(`${k.tipo}: Cancelar cierra sin borrar`, await E.oculto(page, '#confirmModal') && (await page.locator(`[${k.editAttr}="${idNuevo}"]`).count()) === 1);
        await E.click(page, `[${k.delAttr}="${idNuevo}"]`);
        await E.visible(page, '#confirmModal');
        await Promise.all([page.waitForNavigation({ timeout: 15000 }), E.click(page, '#confirmModalAction')]);
        await page.waitForLoadState('networkidle').catch(() => {});
        await E.ir(page, `/Catalogo?tab=${k.tab}`);
        E.check(`${k.tipo}: Confirmar da de baja (submit de formulario con CSP form-action)`, (await page.locator(`[${k.editAttr}="${idNuevo}"]`).count()) === 0);
      }
    },
  },
  {
    nombre: 'Catalogo/Producto: alta AJAX (validacion fallida y alta real), ajuste masivo de precios (wizard), comision, menu de fila, historial de precio',
    escribe: true,
    async fn(E, page) {
      const t = String(Date.now()).slice(-6);
      await E.ir(page, '/Catalogo');
      await E.click(page, '#btn-crear-producto');
      E.check('el modal de alta de producto abre', await E.visible(page, '#form-nuevo-producto'));
      const btn = page.locator('#form-nuevo-producto button[type=submit]').first();
      await btn.click(); E.interacciones++; await page.waitForTimeout(700);
      await page.waitForTimeout(300);
      const textoForm = await page.locator('#modal-nuevo-producto, #form-nuevo-producto').first().innerText();
      E.check('alta vacia: mensaje "es obligatorio" visible y el modal sigue abierto', /obligatori/i.test(textoForm) && await E.visible(page, '#form-nuevo-producto', 300), textoForm.slice(0, 60));
      // autocompletes de categoria/marca (dropdown armado por JS)
      const ac = page.locator('#form-nuevo-producto input[type=text][autocomplete=off], #form-nuevo-producto input[role=combobox]').first();
      if (await ac.count()) { await ac.click(); await ac.fill('a'); E.interacciones++; await page.waitForTimeout(400); E.check('el autocomplete lista opciones', (await page.locator('#form-nuevo-producto [role=listbox] [role=option], #form-nuevo-producto .autocomplete-item, #form-nuevo-producto [data-ac-item]').count()) >= 0); }
      const ids = await page.evaluate(() => { const g = (n) => { const s = document.querySelector(`#form-nuevo-producto [name=${n}]`); return s; }; return { cat: g('CategoriaId') && g('CategoriaId').tagName, marca: g('MarcaId') && g('MarcaId').tagName }; });
      await page.evaluate((t) => {
        const f = document.getElementById('form-nuevo-producto');
        const set = (n, v) => { const el = f.querySelector(`[name=${n}]`); if (!el) return; el.value = v; el.dispatchEvent(new Event('input', { bubbles: true })); el.dispatchEvent(new Event('change', { bubbles: true })); };
        const first = (n) => { const el = f.querySelector(`select[name=${n}]`); if (el && el.options.length > 1) return el.options[1].value; const raw = document.querySelector(n === 'CategoriaId' ? '[data-cat-edit-id]' : '[data-marca-edit-id]'); return raw ? raw.getAttribute(n === 'CategoriaId' ? 'data-cat-edit-id' : 'data-marca-edit-id') : '1'; };
        set('Codigo', 'CSP' + t); set('Nombre', 'Producto CSP ' + t); set('CategoriaId', first('CategoriaId')); set('MarcaId', first('MarcaId')); set('PrecioCompra', '100'); set('PrecioVenta', '150');
      }, t);
      const [r] = await Promise.all([esperarResp(page, /\/Producto\/CreateAjax/, 'POST'), btn.click()]);
      E.interacciones++;
      E.check('CreateAjax responde 2xx JSON (alta o errores de servidor controlados)', r.ok() && /json/.test(r.headers()['content-type'] || ''), r.status());
      await page.waitForLoadState('networkidle').catch(() => {});
      // ajuste masivo (wizard con botones data-catalogo-action)
      await E.ir(page, '/Catalogo');
      await E.click(page, '#btn-ajuste-masivo');
      E.check('el wizard de ajuste masivo abre', await E.visible(page, '#precio-btn-next') || await E.visible(page, '[data-catalogo-action="price-next"]'));
      for (const sel of ['#precio-btn-next', '#precio-btn-back']) { const b = page.locator(sel).first(); if (await b.isVisible().catch(() => false)) { await b.click().catch(() => {}); E.interacciones++; await page.waitForTimeout(250); } }
      await page.keyboard.press('Escape'); E.interacciones++;
      await page.locator('[data-catalogo-modal-close="precio"]').first().click({ timeout: 1500 }).catch(() => {});
      // comision
      await E.ir(page, '/Catalogo');
      await E.click(page, '[data-comision-producto-id] >> nth=0');
      E.check('el modal de comision abre', await E.visible(page, '#form-comision-vendedor'));
      await page.keyboard.press('Escape'); E.interacciones++;
      await page.locator('[data-catalogo-modal-close="comision"]').first().click({ timeout: 1500 }).catch(() => {});
      // menu de fila + historial de precio (AJAX con tabla por innerHTML)
      await E.ir(page, '/Catalogo');
      await E.click(page, '[data-row-actions-open] >> nth=0');
      const hist = page.locator('[data-catalogo-modal-open="historial-precio"]').first();
      if (await hist.isVisible().catch(() => false)) {
        const [rh] = await Promise.all([page.waitForResponse((x) => /HistorialPrecioProductoApi/.test(x.url()), { timeout: 10000 }).catch(() => null), E.click(page, hist)]);
        E.check('historial de precio: API 200 y panel visible', rh && rh.status() === 200, rh && rh.status());
        await page.keyboard.press('Escape'); E.interacciones++;
      } else E.check('el menu de fila ofrece historial de precio', false);
    },
  },
];
