// @ts-check
/**
 * Regresiones de envío involuntario (Agente 4, UX funcional). Sin app ni base: carga el JS real en una página
 * sintética y cuenta los POST con page.route.
 *
 *  1. data-prevent-double-submit (shared-ui.js): reenvío (Enter repetido / requestSubmit) en un formulario que mueve dinero
 *     (Caja Registrar movimiento / Abrir / Cerrar, Compras, Tesorería, CxP) envía UNA sola vez; si la validación
 *     o la confirmación cancelan el envío, no bloquea el formulario.
 *  2. Compras (ordencompra-form.js): Enter en el buscador de productos nunca envía la orden.
 *
 * Ejecutar:  npx playwright test -c playwright.js-harness.config.js e2e-js/compras-caja-envio-involuntario.spec.js
 * Contra JS anterior (para ver el defecto):  JS_DIR=<carpeta con shared-ui.js y ordencompra-form.js viejos>
 */
const { test, expect } = require('playwright/test');
const fs = require('fs');
const path = require('path');

const ORIGIN = 'http://harness.test';
const JS_DIR = process.env.JS_DIR || path.join(__dirname, '..', 'wwwroot', 'js');
const read = (f) => fs.readFileSync(path.join(JS_DIR, f), 'utf8');

async function montar(page, html, scripts, posts) {
    await page.route(`${ORIGIN}/**`, async (route) => {
        const req = route.request();
        if (req.method() === 'POST') {
            posts.push(req.url());
            // Respuesta lenta: simula el servidor procesando mientras el operador vuelve a hacer click.
            await new Promise((r) => setTimeout(r, 300));
            return route.fulfill({ status: 200, contentType: 'text/html', body: '<html><body>ok</body></html>' });
        }
        return route.fulfill({ status: 200, contentType: 'text/html', body: `<!doctype html><html><body>${html}</body></html>` });
    });
    await page.goto(`${ORIGIN}/p`);
    for (const s of scripts) await page.addScriptTag({ content: read(s) });
}

const FORM_CAJA = (attr) => `<form action="/Caja/RegistrarMovimiento" method="post" id="f" ${attr}>
    <input name="Monto" id="monto" required value="10"><button type="submit" id="go">Registrar</button></form>`;

test.describe('anti doble envío', () => {
    test('Enter repetido envía una sola vez', async ({ page }) => {
        const posts = [];
        await montar(page, FORM_CAJA('data-prevent-double-submit'), ['shared-ui.js'], posts);
        await page.focus('#monto');
        await page.keyboard.press('Enter');
        await expect.poll(() => posts.length).toBe(1);
        await page.keyboard.press('Enter');
        await page.waitForTimeout(700);
        expect(posts.length).toBe(1);
    });

    test('validación nativa fallida no bloquea el formulario', async ({ page }) => {
        const posts = [];
        await montar(page, FORM_CAJA('data-prevent-double-submit'), ['shared-ui.js'], posts);
        await page.fill('#monto', '');
        await page.click('#go');
        await expect(page.locator('#go')).toBeEnabled();
        expect(await page.locator('#f').getAttribute('data-submitting')).toBeNull();
        await page.fill('#monto', '5');
        await page.click('#go');
        await page.waitForTimeout(500);
        expect(posts.length).toBe(1);
    });

    test('handler que cancela el envío (confirmación pendiente) no bloquea; el reenvío sí se envía', async ({ page }) => {
        const posts = [];
        await montar(page, FORM_CAJA('data-prevent-double-submit'), ['shared-ui.js'], posts);
        await page.evaluate(() => {
            const f = document.getElementById('f');
            let ok = false;
            f.addEventListener('submit', (e) => { if (!ok) { e.preventDefault(); ok = true; } });
        });
        await page.click('#go');
        await expect(page.locator('#go')).toBeEnabled();
        await page.click('#go');
        await page.waitForTimeout(500);
        expect(posts.length).toBe(1);
    });
});

test.describe('Compras: buscador de productos', () => {
    const HTML = `
    <form id="form-orden" method="post" action="/OrdenCompra/Create">
      <select id="ProveedorId"><option value="">-</option><option value="1">P1</option></select>
      <div id="ordencompra-feedback" hidden><span data-feedback-icon></span><p data-feedback-title></p><p data-feedback-message></p><a data-feedback-link hidden></a><button type="button" data-feedback-close></button></div>
      <input id="inp-buscar-producto"><div id="dropdown-productos" class="hidden"></div>
      <input id="inp-cantidad" value="1"><input id="inp-precio"><button type="button" id="btn-agregar">+</button>
      <input id="inp-descuento" value="0">
      <table><tbody id="tabla-productos"></tbody></table>
      <span id="lbl-subtotal"></span><span id="lbl-descuento"></span><span id="lbl-iva"></span><span id="lbl-total"></span>
      <input id="hdn-subtotal"><input id="hdn-descuento"><input id="hdn-iva"><input id="hdn-total">
      <button type="submit" id="guardar">Guardar</button>
    </form>
    <script type="application/json" id="ordencompra-productos-data">[{"id":1,"nombre":"Tornillo","codigo":"T1","precioCompra":10,"porcentajeIva":21}]</script>
    <script type="application/json" id="ordencompra-proveedores-data">[]</script>
    <script type="application/json" id="ordencompra-detalles-data">[{"productoId":1,"productoNombre":"Tornillo","productoCodigo":"T1","cantidad":2,"precioUnitario":10}]</script>`;

    test('Enter con texto sin coincidencias no envía la orden y explica el motivo', async ({ page }) => {
        const posts = [];
        await montar(page, HTML, ['ordencompra-form.js'], posts);
        await page.fill('#inp-buscar-producto', 'zzzz');
        await page.press('#inp-buscar-producto', 'Enter');
        await page.waitForTimeout(500);
        expect(posts.length).toBe(0);
        await expect(page.locator('#ordencompra-feedback')).toBeVisible();
    });

    test('Enter con coincidencia elige el producto y pasa a Cantidad, sin enviar', async ({ page }) => {
        const posts = [];
        await montar(page, HTML, ['ordencompra-form.js'], posts);
        await page.fill('#inp-buscar-producto', 'torn');
        await page.press('#inp-buscar-producto', 'Enter');
        await expect(page.locator('#inp-cantidad')).toBeFocused();
        await page.waitForTimeout(400);
        expect(posts.length).toBe(0);
    });
});
