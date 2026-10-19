// @ts-check
/**
 * RESCATE-PRODUCTO-P0 — regresión determinista del modal "Editar Producto" (wwwroot/js/producto-editar-modal.js).
 *
 * Riesgo cubierto: Subcategoría/Submarca se cargan por fetch DESPUÉS de abrir el modal. Mientras la carga está
 * pendiente (o si falla) el select queda vacío; si ese vacío se enviaba, EditAjax lo interpreta como "quitar la
 * selección" y se borraba una relación guardada. Contrato verificado acá:
 *   - carga pendiente  -> no se puede guardar (botón inactivo y submit bloqueado);
 *   - carga fallida    -> el select queda deshabilitado => el campo NO se envía => el servidor conserva lo guardado;
 *   - "Sin submarca"/"Sin subcategoría" elegido a propósito -> se envía vacío (= null);
 *   - respuestas tardías / fuera de orden / de otro producto -> se descartan.
 *
 * No necesita app, base de datos ni Docker: carga el JS real en una página sintética y controla cada respuesta
 * HTTP con page.route (compuertas explícitas, sin temporizadores arbitrarios).
 *
 * Ejecutar:  npx.cmd playwright test -c playwright.js-harness.config.js
 * Contra otro archivo JS (p. ej. para reproducir el defecto anterior):  $env:EDITAR_MODAL_JS = "ruta\al\viejo.js"
 */
const { test, expect } = require('playwright/test');
const fs = require('fs');
const path = require('path');

const ORIGIN = 'http://harness.test';
const JS_PATH = process.env.EDITAR_MODAL_JS || path.join(__dirname, '..', 'wwwroot', 'js', 'producto-editar-modal.js');

// Ids que el JS consulta con el('prod-edit-...'): se generan para no depender de la vista Razor.
const SELECTS = {
    'prod-edit-categoriaId': 'CategoriaId',
    'prod-edit-subcategoriaId': 'SubcategoriaId',
    'prod-edit-marcaId': 'MarcaId',
    'prod-edit-submarcaId': 'SubmarcaId',
    'prod-edit-alicuotaIVAId': 'AlicuotaIVAId',
    'prod-edit-porcentajeIVACompra': 'PorcentajeIVACompra',
    'prod-edit-maxCuotasSinInteres': 'MaxCuotasSinInteres',
};

function html() {
    const src = fs.readFileSync(JS_PATH, 'utf8');
    const ids = [...new Set([...src.matchAll(/(?:el\(|id: )'(prod-edit-[A-Za-z0-9_-]+)'/g)].map(m => m[1]), ...Object.keys(SELECTS))];
    const campos = ids.map(id => {
        if (SELECTS[id]) return `<select id="${id}" name="${SELECTS[id]}"></select>`;
        if (id === 'prod-edit-activo') return `<input id="${id}" name="Activo" type="checkbox">`;
        if (id === 'prod-edit-caracteristicas-body') return `<table><tbody id="${id}"></tbody></table>`;
        if (id === 'prod-edit-validation-summary' || id === 'prod-edit-precio-contexto-texto') return '';
        if (id.endsWith('-count') || id.endsWith('-texto')) return `<span id="${id}"></span>`;
        return `<input id="${id}" name="${id.replace('prod-edit-', '')}" value="">`;
    }).join('\n');
    return `<!doctype html><html><head><meta charset="utf-8"><style>.hidden{display:none}</style></head><body>
<table><tr><td data-prod-nombre>Producto</td><td><button type="button" data-prod-edit-id="7">Editar 7</button>
<button type="button" data-prod-edit-id="8">Editar 8</button></td></tr></table>
<div id="modal-editar-producto" class="hidden">
  <div id="prod-edit-validation-summary" class="hidden"><span id="prod-edit-validation-text"></span></div>
  <form id="form-editar-producto" method="post" action="/Producto/EditAjax/0">
    ${campos}
    <button type="button" data-prod-edit-modal-close>Cerrar</button>
    <button type="submit" id="btn-guardar-producto-edit">Guardar cambios</button>
  </form>
</div>
<script>
  window.CatalogoData = { categorias: [{id:1,nombre:'Cat 1'},{id:2,nombre:'Cat 2'}],
                          marcas: [{id:10,nombre:'Marca 10'},{id:20,nombre:'Marca 20'},{id:30,nombre:'Marca 30'}] };
  window.ProductoModalTabs = { wire: function () { return { activate: function () {}, findTabForField: function () { return null; } }; } };
  window.ProductoCreditoPersonalUI = { renderCards: function () {}, aplicarModo: function () {}, wireModo: function () {} };
</script>
<script src="${ORIGIN}/producto-modal-validacion.js"></script>
<script src="${ORIGIN}/producto-editar-modal.js"></script>
</body></html>`;
}

/** Compuerta controlable: la respuesta HTTP no se entrega hasta llamar a release()/fail(). */
function gate() {
    /** @type {(v: 'ok'|'fail') => void} */
    let resolve = () => {};
    const promise = new Promise(r => { resolve = r; });
    return { promise, release: () => resolve('ok'), fail: () => resolve('fail') };
}

const PRODUCTOS = {
    7: { id: 7, rowVersion: 'AAAA', codigo: 'P7', nombre: 'Producto 7', categoriaId: 1, subcategoriaId: 5, subcategoriaNombre: 'Sub 5',
         marcaId: 10, submarcaId: 50, submarcaNombre: 'Submarca 50', activo: true },
    8: { id: 8, rowVersion: 'BBBB', codigo: 'P8', nombre: 'Producto 8', categoriaId: 2, subcategoriaId: null, subcategoriaNombre: null,
         marcaId: 20, submarcaId: null, submarcaNombre: null, activo: true },
};
const SUBCATEGORIAS = { 1: [{ id: 5, nombre: 'Sub 5' }, { id: 6, nombre: 'Sub 6' }], 2: [{ id: 7, nombre: 'Sub 7' }] };
const SUBMARCAS = {
    10: [{ id: 50, nombre: 'Submarca 50' }, { id: 51, nombre: 'Submarca 51' }],
    20: [{ id: 60, nombre: 'Submarca 60' }],
    30: [{ id: 70, nombre: 'Submarca 70' }],
};

/**
 * @param {import('playwright/test').Page} page
 * @param {{ gates?: Record<string, ReturnType<typeof gate>>, fail?: Record<string, number|'abort'> }} [cfg]
 *   gates/fail se indexan por la ruta ("/Producto/GetSubmarcas/10", "/Producto/GetJson/7"...).
 */
async function preparar(page, cfg = {}) {
    const gates = cfg.gates || {};
    const fail = cfg.fail || {};
    /** @type {{url: string, body: string}[]} */
    const posts = [];
    await page.route(ORIGIN + '/**', async route => {
        const req = route.request();
        const p = new URL(req.url()).pathname;
        if (p === '/') return route.fulfill({ contentType: 'text/html; charset=utf-8', body: html() });
        if (p === '/producto-editar-modal.js') return route.fulfill({ contentType: 'text/javascript; charset=utf-8', body: fs.readFileSync(JS_PATH, 'utf8') });
        if (p === '/producto-modal-validacion.js') {
            return route.fulfill({ contentType: 'text/javascript; charset=utf-8', body: fs.readFileSync(path.join(__dirname, '..', 'wwwroot', 'js', 'producto-modal-validacion.js'), 'utf8') });
        }
        if (req.method() === 'POST') {
            posts.push({ url: p, body: req.postData() || '' });
            return route.fulfill({ contentType: 'application/json', body: JSON.stringify({ success: true, message: 'ok', entity: { nombre: 'Producto', activo: true } }) });
        }
        let json;
        const m = p.match(/^\/Producto\/(GetJson|GetSubcategorias|GetSubmarcas)\/(\d+)$/);
        if (!m) return route.fulfill({ status: 404, body: '' });
        const id = Number(m[2]);
        json = m[1] === 'GetJson' ? PRODUCTOS[id] : (m[1] === 'GetSubcategorias' ? SUBCATEGORIAS[id] : SUBMARCAS[id]) || [];
        const g = gates[p];
        let modo = 'ok';
        if (g) modo = await g.promise;
        const f = fail[p];
        if (f === 'abort' || modo === 'fail' && f === undefined) return route.abort('failed');
        if (typeof f === 'number') return route.fulfill({ status: f, contentType: 'text/html', body: '<html>error</html>' });
        return route.fulfill({ contentType: 'application/json', body: JSON.stringify(json) });
    });
    await page.goto(ORIGIN + '/');
    return posts;
}

/** Valor enviado para un campo del multipart, o undefined si la clave no viajó. */
function campo(body, name) {
    const m = body.match(new RegExp('name="' + name + '"\\r?\\n\\r?\\n([^\\r\\n]*)'));
    return m ? m[1] : undefined;
}

const abrir = (page, id = 7) => page.click(`[data-prod-edit-id="${id}"]`);
const guardar = page => page.click('#btn-guardar-producto-edit');
const modalAbierto = page => page.locator('#modal-editar-producto');

test.describe('Editar producto: Subcategoría/Submarca no se borran por carga asíncrona', () => {
    test('abrir y guardar de inmediato (con las cargas ya resueltas) conserva ambas relaciones', async ({ page }) => {
        const posts = await preparar(page);
        await abrir(page);
        await expect(page.locator('#prod-edit-subcategoriaId')).toBeEnabled();
        await expect(page.locator('#prod-edit-submarcaId')).toBeEnabled();
        await expect(page.locator('#prod-edit-subcategoriaId')).toHaveValue('5');
        await expect(page.locator('#prod-edit-submarcaId')).toHaveValue('50');
        await guardar(page);
        await expect(modalAbierto(page)).toBeHidden();
        expect(posts).toHaveLength(1);
        expect(campo(posts[0].body, 'SubcategoriaId')).toBe('5');
        expect(campo(posts[0].body, 'SubmarcaId')).toBe('50');
    });

    test('con las cargas retrasadas no se puede guardar ni se envían vacíos accidentales', async ({ page }) => {
        const gSub = gate(), gMar = gate();
        const posts = await preparar(page, { gates: { '/Producto/GetSubcategorias/1': gSub, '/Producto/GetSubmarcas/10': gMar } });
        await abrir(page);
        await expect(modalAbierto(page)).toBeVisible();

        // Cargas pendientes: botón inactivo, selects deshabilitados (no viajan) y submit forzado bloqueado.
        await expect(page.locator('#btn-guardar-producto-edit')).toBeDisabled();
        await expect(page.locator('#prod-edit-subcategoriaId')).toBeDisabled();
        await expect(page.locator('#prod-edit-submarcaId')).toBeDisabled();
        await page.evaluate(() => /** @type {HTMLFormElement} */ (document.getElementById('form-editar-producto')).requestSubmit());
        await expect(page.locator('#prod-edit-validation-text')).toContainText('Se están cargando');
        expect(posts).toHaveLength(0);

        // Llega una sola: sigue bloqueado hasta que llegan ambas.
        gSub.release();
        await expect(page.locator('#prod-edit-subcategoriaId')).toBeEnabled();
        await expect(page.locator('#btn-guardar-producto-edit')).toBeDisabled();

        gMar.release();
        await expect(page.locator('#btn-guardar-producto-edit')).toBeEnabled();
        await guardar(page);
        await expect(modalAbierto(page)).toBeHidden();
        expect(posts).toHaveLength(1);
        expect(campo(posts[0].body, 'SubcategoriaId')).toBe('5');
        expect(campo(posts[0].body, 'SubmarcaId')).toBe('50');
    });

    for (const modo of [500, 'abort']) {
        test(`si falla la carga de submarcas (${modo}) la relación guardada no se elimina`, async ({ page }) => {
            const posts = await preparar(page, { fail: { '/Producto/GetSubmarcas/10': /** @type {any} */ (modo) } });
            await abrir(page);
            await expect(page.locator('#prod-edit-submarcaId')).toBeDisabled();
            await expect(page.locator('#prod-edit-submarcaId')).toHaveAttribute('aria-busy', 'false');
            // Se muestra lo guardado, solo lectura.
            await expect(page.locator('#prod-edit-submarcaId option:checked')).toHaveText('Submarca 50');
            await expect(page.locator('#prod-edit-subcategoriaId')).toHaveValue('5');
            await guardar(page);
            await expect(modalAbierto(page)).toBeHidden();
            expect(posts).toHaveLength(1);
            // La clave no viaja: EditAjax conserva la submarca persistida.
            expect(campo(posts[0].body, 'SubmarcaId')).toBeUndefined();
            expect(campo(posts[0].body, 'SubcategoriaId')).toBe('5');
        });
    }

    test('si falla la carga de subcategorías la relación guardada no se elimina', async ({ page }) => {
        const posts = await preparar(page, { fail: { '/Producto/GetSubcategorias/1': 500 } });
        await abrir(page);
        await expect(page.locator('#prod-edit-subcategoriaId')).toBeDisabled();
        await guardar(page);
        await expect(modalAbierto(page)).toBeHidden();
        expect(campo(posts[0].body, 'SubcategoriaId')).toBeUndefined();
        expect(campo(posts[0].body, 'SubmarcaId')).toBe('50');
    });

    test('elegir explícitamente "Sin submarca" guarda vacío (null) y no afecta la subcategoría', async ({ page }) => {
        const posts = await preparar(page);
        await abrir(page);
        await expect(page.locator('#prod-edit-submarcaId')).toBeEnabled();
        await page.selectOption('#prod-edit-submarcaId', '');
        await guardar(page);
        await expect(modalAbierto(page)).toBeHidden();
        expect(campo(posts[0].body, 'SubmarcaId')).toBe('');
        expect(campo(posts[0].body, 'SubcategoriaId')).toBe('5');
    });

    test('cambiar de marca a propósito: espera las submarcas nuevas y envía la elegida', async ({ page }) => {
        const gNueva = gate();
        const posts = await preparar(page, { gates: { '/Producto/GetSubmarcas/20': gNueva } });
        await abrir(page);
        await expect(page.locator('#prod-edit-submarcaId')).toBeEnabled();
        await page.selectOption('#prod-edit-marcaId', '20');
        await expect(page.locator('#prod-edit-submarcaId')).toBeDisabled();
        await expect(page.locator('#btn-guardar-producto-edit')).toBeDisabled();
        gNueva.release();
        await expect(page.locator('#prod-edit-submarcaId')).toBeEnabled();
        // La submarca vieja (de la marca 10) no sobrevive al cambio de marca.
        await expect(page.locator('#prod-edit-submarcaId')).toHaveValue('');
        await page.selectOption('#prod-edit-submarcaId', '60');
        await guardar(page);
        await expect(modalAbierto(page)).toBeHidden();
        expect(campo(posts[0].body, 'MarcaId')).toBe('20');
        expect(campo(posts[0].body, 'SubmarcaId')).toBe('60');
    });

    test('cambiar de marca y fallar la carga de sus submarcas bloquea el guardado (no se falsea la relación)', async ({ page }) => {
        const posts = await preparar(page, { fail: { '/Producto/GetSubmarcas/20': 500 } });
        await abrir(page);
        await expect(page.locator('#prod-edit-submarcaId')).toBeEnabled();
        await page.selectOption('#prod-edit-marcaId', '20');
        await expect(page.locator('#prod-edit-submarcaId')).toBeDisabled();
        await expect(page.locator('#prod-edit-submarcaId')).toHaveAttribute('aria-busy', 'false');
        await guardar(page);
        await expect(page.locator('#prod-edit-validation-text')).toContainText('No se pudieron cargar');
        await expect(modalAbierto(page)).toBeVisible();
        expect(posts).toHaveLength(0);
        // Elegir otra marca con carga correcta lo destraba.
        await page.selectOption('#prod-edit-marcaId', '30');
        await expect(page.locator('#prod-edit-submarcaId')).toBeEnabled();
        await guardar(page);
        await expect(modalAbierto(page)).toBeHidden();
        expect(campo(posts[0].body, 'MarcaId')).toBe('30');
    });

    test('respuestas de submarcas fuera de orden: gana la última selección', async ({ page }) => {
        const g20 = gate(), g30 = gate();
        const posts = await preparar(page, { gates: { '/Producto/GetSubmarcas/20': g20, '/Producto/GetSubmarcas/30': g30 } });
        await abrir(page);
        await expect(page.locator('#prod-edit-submarcaId')).toBeEnabled();
        await page.selectOption('#prod-edit-marcaId', '20'); // pedido A (lento)
        await page.selectOption('#prod-edit-marcaId', '30'); // pedido B (último)
        g30.release();
        await expect(page.locator('#prod-edit-submarcaId')).toBeEnabled();
        await expect(page.locator('#prod-edit-submarcaId option')).toHaveText(['Sin submarca', 'Submarca 70']);
        g20.release(); // llega A tarde: debe descartarse
        // Sincronía determinista: una ida y vuelta de red posterior al release.
        await page.evaluate(() => fetch('/Producto/GetJson/8').then(r => r.json()));
        await expect(page.locator('#prod-edit-submarcaId option')).toHaveText(['Sin submarca', 'Submarca 70']);
        await page.selectOption('#prod-edit-submarcaId', '70');
        await guardar(page);
        await expect(modalAbierto(page)).toBeHidden();
        expect(campo(posts[0].body, 'MarcaId')).toBe('30');
        expect(campo(posts[0].body, 'SubmarcaId')).toBe('70');
    });

    test('respuesta tardía del producto anterior no pisa el producto abierto después', async ({ page }) => {
        const gViejo = gate();
        const posts = await preparar(page, { gates: { '/Producto/GetSubmarcas/10': gViejo } });
        await abrir(page, 7);                       // submarcas de la marca 10 quedan pendientes
        await expect(modalAbierto(page)).toBeVisible();
        await page.click('[data-prod-edit-modal-close]');
        await abrir(page, 8);                       // otro producto: marca 20, sin relaciones
        await expect(page.locator('#prod-edit-codigo')).toHaveValue('P8');
        await expect(page.locator('#prod-edit-submarcaId')).toBeEnabled();
        gViejo.release();
        await page.evaluate(() => fetch('/Producto/GetJson/8').then(r => r.json()));
        await expect(page.locator('#prod-edit-submarcaId option')).toHaveText(['Sin submarca', 'Submarca 60']);
        await expect(page.locator('#prod-edit-submarcaId')).toHaveValue('');
        await guardar(page);
        await expect(modalAbierto(page)).toBeHidden();
        expect(campo(posts[0].body, 'SubmarcaId')).toBe('');
        expect(campo(posts[0].body, 'SubcategoriaId')).toBe('');
    });

    test('dos aperturas seguidas con GetJson fuera de orden muestran el último producto pedido', async ({ page }) => {
        const g7 = gate();
        await preparar(page, { gates: { '/Producto/GetJson/7': g7 } });
        await abrir(page, 7);   // respuesta lenta
        await abrir(page, 8);   // respuesta inmediata
        await expect(page.locator('#prod-edit-codigo')).toHaveValue('P8');
        g7.release();
        await page.evaluate(() => fetch('/Producto/GetJson/8').then(r => r.json()));
        await expect(page.locator('#prod-edit-codigo')).toHaveValue('P8');
        await expect(page.locator('#prod-edit-submarcaId')).toHaveValue('');
    });

    test('reabrir el modal para otro producto no arrastra la selección anterior', async ({ page }) => {
        await preparar(page);
        await abrir(page, 7);
        await expect(page.locator('#prod-edit-submarcaId')).toHaveValue('50');
        await page.click('[data-prod-edit-modal-close]');
        await abrir(page, 8);
        await expect(page.locator('#prod-edit-codigo')).toHaveValue('P8');
        await expect(page.locator('#prod-edit-submarcaId')).toBeEnabled();
        await expect(page.locator('#prod-edit-subcategoriaId')).toBeEnabled();
        await expect(page.locator('#prod-edit-submarcaId')).toHaveValue('');
        await expect(page.locator('#prod-edit-subcategoriaId')).toHaveValue('');
    });
});
