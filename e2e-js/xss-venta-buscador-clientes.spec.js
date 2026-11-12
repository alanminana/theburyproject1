// @ts-check
/**
 * XSS-A-001 — Stored XSS (CWE-79) en el buscador de clientes del wizard de Venta (wwwroot/js/venta-create.js).
 *
 * Los datos del cliente (Nombre, Apellido, TipoDocumento, NumeroDocumento, Telefono, Domicilio, Localidad,
 * Provincia, CodigoPostal, display) son texto libre persistido (el servidor no restringe caracteres). El dropdown
 * se armaba con `innerHTML` interpolando esos valores crudos, tanto como contenido como dentro de atributos
 * data-*. Un cliente creado con `<img src=x onerror=...>` ejecutaba JS en la sesión de cualquier usuario que
 * buscara ese cliente al armar una venta (y un valor con comillas rompía el atributo).
 *
 * Contrato: el payload se muestra como texto, no ejecuta JS (al renderizar ni al hacer click), y llega intacto
 * a la selección (el cliente elegido conserva el valor literal).
 *
 * No necesita app, base ni Docker: carga el JS real en una página sintética y controla /api/ventas con page.route.
 * Ejecutar:  npx.cmd playwright test -c playwright.js-harness.config.js e2e-js/xss-venta-buscador-clientes.spec.js
 * Contra el JS anterior:  $env:VENTA_CREATE_JS = "ruta\vieja\venta-create.js"
 */
const { test, expect } = require('playwright/test');
const fs = require('fs');
const path = require('path');

const ORIGIN = 'http://harness.test';
const VENTA_JS = process.env.VENTA_CREATE_JS || path.join(__dirname, '..', 'wwwroot', 'js', 'venta-create.js');

const BOOM = 'window.__xss=(window.__xss||0)+1';
const PAYLOADS = [
    `<img src=x onerror="${BOOM}">`,
    `<svg onclick="${BOOM}"><rect width="400" height="400"/></svg>`,
    `" onclick="${BOOM}" x="`,
    `</div><img src=x onerror="${BOOM}">`,
];
const LEGIT = `O'Brien <Ana> & "Cía"`;

const BODY = `
<div><input id="input-buscar-cliente"></div>
<div id="dropdown-clientes" class="hidden"></div>
<input id="hdn-cliente-id" type="hidden">
<div id="info-cliente" class="hidden"><span id="info-cliente-nombre"></span><span id="info-cliente-doc"></span></div>`;

function cliente(over) {
    return {
        id: 7, nombre: 'Ana', apellido: 'Lopez', tipoDocumento: 'DNI', numeroDocumento: '30111222',
        telefono: '1155550000', email: 'a@b.c', display: 'Lopez, Ana', domicilio: 'Calle 1', localidad: 'CABA',
        provincia: 'BA', codigoPostal: '1000', requiereActualizacion: false, diasSinActualizar: 0, ...over,
    };
}

async function abrirBuscador(page, clientes) {
    const errors = [];
    const dialogs = [];
    page.on('dialog', (d) => { dialogs.push(d.message()); d.dismiss(); });
    page.on('pageerror', (e) => errors.push(e.message));
    await page.route(`${ORIGIN}/**`, (route) => {
        const url = route.request().url();
        if (url === `${ORIGIN}/`) {
            return route.fulfill({ contentType: 'text/html', body: `<!doctype html><html><head><meta charset="utf-8"><style>.hidden{display:none}</style></head><body>${BODY}</body></html>` });
        }
        if (url.includes('/api/ventas/BuscarClientes')) {
            return route.fulfill({ contentType: 'application/json', body: JSON.stringify(clientes) });
        }
        return route.fulfill({ status: 200, contentType: 'application/json', body: '[]' });
    });
    await page.goto(`${ORIGIN}/`);
    await page.addScriptTag({ content: fs.readFileSync(VENTA_JS, 'utf8') });
    await page.fill('#input-buscar-cliente', 'abc');
    await page.waitForFunction(() => document.querySelectorAll('#dropdown-clientes [data-id]').length > 0, null, { timeout: 4000 });
    return { errors, dialogs };
}

test.describe('XSS-A-001 buscador de clientes de Venta', () => {
    for (const payload of PAYLOADS) {
        test(`ningún campo del cliente ejecuta JS: ${payload.slice(0, 36)}`, async ({ page }) => {
            const { dialogs } = await abrirBuscador(page, [cliente({
                nombre: payload, apellido: payload, display: payload, numeroDocumento: payload,
                telefono: payload, domicilio: payload, localidad: payload, provincia: payload, codigoPostal: payload,
            })]);
            for (const handle of await page.locator('#dropdown-clientes *').elementHandles()) {
                await handle.click({ force: true, timeout: 1000 }).catch(() => {});
            }
            expect(await page.evaluate(() => window.__xss || 0)).toBe(0);
            expect(await page.locator('#dropdown-clientes img, #dropdown-clientes svg, #info-cliente img, #info-cliente svg, [onclick], [onerror]').count()).toBe(0);
            expect(dialogs).toEqual([]);
        });
    }

    test('el texto legítimo con caracteres especiales se muestra literal y llega intacto a la selección', async ({ page }) => {
        await abrirBuscador(page, [cliente({ nombre: LEGIT, apellido: LEGIT, display: LEGIT, domicilio: LEGIT })]);
        await expect(page.locator('#dropdown-clientes [data-id] p').first()).toHaveText(LEGIT);
        await page.locator('#dropdown-clientes [data-id]').click();
        await expect(page.locator('#info-cliente-nombre')).toHaveText(`${LEGIT} ${LEGIT}`);
    });
});
