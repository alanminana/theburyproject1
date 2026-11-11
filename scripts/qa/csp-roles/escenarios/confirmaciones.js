// Confirmaciones nativas declarativas (data-confirm de csp-actions.js) y acciones data-action, ejercitadas bajo Enforce.
// Las paginas reales con data-confirm necesitan datos que la base sintetica no tiene (medios de pago eliminables, plantillas, alertas de mora/stock,
// lotes de precios): se verifica el MISMO manejador (csp-actions.js, cargado por el layout) sobre elementos con el mismo marcado, inyectados en una
// pagina real del stack. Es una prueba del manejador, no de cada pantalla: ver docs/csp-enforce-pendientes.md (limites).
'use strict';

module.exports = [{
  nombre: 'Manejadores data-confirm / data-action (csp-actions.js) sobre una pagina real: confirm cancelar/aceptar, call-self, toggle-hide, remove-closest, history-back, print',
  async fn(E, page) {
    const inyectar = () => page.evaluate(() => {
      const d = document.createElement('div'); d.id = 'qa-csp-zona'; d.style.cssText = 'position:fixed;top:60px;left:10px;z-index:99999;background:#fff;padding:8px';
      d.innerHTML = [
        '<form id="qa-f" method="post" action="/csp-report-qa" data-confirm="¿Eliminar QA?"><button id="qa-f-btn" type="submit">enviar</button></form>',
        '<button id="qa-b" type="button" data-confirm="¿Seguro QA?" data-action="toggle-hide" data-toggle="qa-t" data-hide="qa-h">toggle</button>',
        '<div id="qa-t" class="hidden">T</div><div id="qa-h">H</div>',
        '<div id="qa-r"><button id="qa-rm" type="button" data-action="remove-closest" data-target="#qa-r">quitar</button></div>',
        '<button id="qa-print" type="button" data-action="print">imprimir</button>',
        '<button id="qa-back" type="button" data-action="history-back">volver</button>',
      ].join('');
      document.body.appendChild(d);
      window.__printCalls = 0; window.print = () => { window.__printCalls++; };
    });
    await E.ir(page, '/Catalogo');
    await inyectar();
    // 1) formulario con data-confirm: cancelar no envia; aceptar envia (el POST lo aborta el arnes y queda registrado)
    E.aceptarDialogos = false;
    let antes = E.bloqueadas.length;
    await E.click(page, '#qa-f-btn'); await page.waitForTimeout(300);
    E.check('form data-confirm: aparece el confirm() con el texto', E.dialogs.some((x) => x.includes('¿Eliminar QA?')), E.dialogs.join('|'));
    E.check('form data-confirm: cancelar no envia', E.bloqueadas.length === antes);
    E.aceptarDialogos = true;
    await E.click(page, '#qa-f-btn'); await page.waitForTimeout(500);
    E.check('form data-confirm: aceptar envia el formulario', E.bloqueadas.length > antes, E.bloqueadas.slice(-1)[0]);
    await E.ir(page, '/Catalogo');
    await inyectar();
    // 2) boton con data-confirm + data-action: cancelar detiene la accion, aceptar la ejecuta
    E.aceptarDialogos = false;
    await E.click(page, '#qa-b'); await page.waitForTimeout(200);
    E.check('boton data-confirm: cancelar detiene toggle-hide', await page.evaluate(() => document.getElementById('qa-t').classList.contains('hidden') && !document.getElementById('qa-h').classList.contains('hidden')));
    E.aceptarDialogos = true;
    await E.click(page, '#qa-b'); await page.waitForTimeout(200);
    E.check('boton data-confirm: aceptar ejecuta toggle-hide', await page.evaluate(() => !document.getElementById('qa-t').classList.contains('hidden') && document.getElementById('qa-h').classList.contains('hidden')));
    // 3) remove-closest, print, history-back
    await E.click(page, '#qa-rm');
    E.check('remove-closest elimina el contenedor', (await page.locator('#qa-r').count()) === 0);
    await E.click(page, '#qa-print');
    E.check('print invoca window.print()', (await page.evaluate(() => window.__printCalls)) === 1);
    const url = page.url();
    await E.ir(page, '/Cliente');
    await page.evaluate(() => { const b = document.createElement('button'); b.id = 'qa-back2'; b.setAttribute('data-action', 'history-back'); b.textContent = 'v'; b.style.cssText = 'position:fixed;top:60px;left:10px;z-index:99999'; document.body.appendChild(b); });
    await Promise.all([page.waitForURL(url, { timeout: 8000 }).catch(() => {}), E.click(page, '#qa-back2')]);
    E.check('history-back vuelve a la pagina anterior', page.url() === url, page.url());
  },
}];
