// @ts-check
/**
 * DESCUENTO-PERMISO-01 (PROMPT 76) — el permiso independiente ventas.discount decide qué ve y qué envía
 * el wizard de Venta y el Cotizador.
 *
 * Con permiso  : usuario por defecto de la suite (E2E_USER/E2E_PASS, debe tener ventas.discount: SuperAdmin
 *                y Administrador lo reciben por el seeder).
 * Sin permiso  : E2E_SIN_DESCUENTO_USER / E2E_SIN_DESCUENTO_PASS (un usuario con ventas.create/update pero sin
 *                ventas.discount, p. ej. Gerente). Sin ellas el escenario se omite; con E2E_REQUIRE_DESCUENTO_PERMISO=1 FALLA.
 * Edit con descuentos persistidos: E2E_VENTA_CON_DESCUENTO_ID + E2E_VENTA_CON_DESCUENTO_TOTAL (total real de esa
 *                venta, p. ej. 1600). Una venta en Cotización/Presupuesto con una línea con descuento y caja abierta
 *                para el usuario sin permiso. Igual que arriba: se omite salvo E2E_REQUIRE_DESCUENTO_PERMISO=1.
 * Búsqueda sintética (opcional): E2E_CLIENTE_TERM / E2E_PRODUCTO_TERM (por defecto "an").
 */
const { test, expect } = require('playwright/test');
const { searchAndSelectClient, activarFiltroStock, irAlPaso } = require('./helpers');

const SIN_USER = process.env.E2E_SIN_DESCUENTO_USER;
const SIN_PASS = process.env.E2E_SIN_DESCUENTO_PASS;
const VENTA_ID = process.env.E2E_VENTA_CON_DESCUENTO_ID;
const VENTA_TOTAL = process.env.E2E_VENTA_CON_DESCUENTO_TOTAL;
const TERM_CLIENTE = process.env.E2E_CLIENTE_TERM || 'an';
const TERM_PRODUCTO = process.env.E2E_PRODUCTO_TERM || 'an';
// Opt-in: en un pipeline que provea el usuario sin permiso, la venta con descuento y la caja abierta para ese usuario,
// E2E_REQUIRE_DESCUENTO_PERMISO=1 convierte la falta de esa configuración en FALLO (en vez de omitir en silencio).
const EXIGIR = process.env.E2E_REQUIRE_DESCUENTO_PERMISO === '1';

/** Sin las credenciales/venta del escenario "sin permiso": se omite (con aviso) salvo que EXIGIR esté activo. */
function exigirOOmitir(faltan, mensaje) {
    if (!faltan) return;
    if (EXIGIR) throw new Error(`[E2E_REQUIRE_DESCUENTO_PERMISO] Falta configuración obligatoria del spec de descuentos: ${mensaje}`);
    test.skip(true, mensaje);
}

function vigilar(page) {
    const issues = { console: [], pageerror: [], http5xx: [] };
    page.on('console', (m) => {
        if (m.type() !== 'error') return;
        const u = m.location().url || '';
        if (/fonts\.(googleapis|gstatic)|favicon/i.test(u)) return;
        issues.console.push(`${m.text()} @ ${u}`);
    });
    page.on('pageerror', (e) => issues.pageerror.push(String(e)));
    page.on('response', (r) => { if (r.status() >= 500) issues.http5xx.push(`${r.status()} ${r.request().method()} ${r.url()}`); });
    return issues;
}

async function contextoSinPermiso(browser, baseURL, viewport) {
    // storageState vacío: sin sesión heredada del usuario con permiso (la config del proyecto lo pasa por defecto).
    const context = await browser.newContext({ baseURL, viewport, locale: 'es-AR', storageState: { cookies: [], origins: [] } });
    const page = await context.newPage();
    // Google Fonts bloqueado: evita ruido de red en el log de consola (mismo criterio que el resto de la suite).
    await page.route(/fonts\.(googleapis|gstatic)\.com/, (route) => route.abort());
    await page.goto('/Identity/Account/Login', { waitUntil: 'domcontentloaded', timeout: 30_000 });
    await page.fill('#Input_UserName, input[name="Input.UserName"], input[type="text"]', SIN_USER);
    await page.fill('#input-password, input[name="Input.Password"], input[type="password"]', SIN_PASS);
    await page.click('button[type="submit"]');
    const nombreCompleto = page.locator('input[placeholder="Nombre y apellido"]');
    if (await nombreCompleto.isVisible({ timeout: 5_000 }).catch(() => false)) {
        await nombreCompleto.fill(`E2E ${SIN_USER}`);
        await page.getByRole('checkbox', { name: /Declaro que le.* y acepto/i }).setChecked(true);
        await page.getByRole('button', { name: 'Aceptar y continuar' }).click();
    }
    await page.waitForURL((url) => !url.toString().toLowerCase().includes('/login'), { timeout: 15_000 });
    return { context, page };
}

/**
 * Igual que helpers.addProduct, pero (a) permite cargar el "Desc. %" antes de agregar (solo existe con permiso)
 * y (b) captura el panel "agregar producto" para la revisión visual.
 */
async function agregarProducto(page, term, { descuento, captura } = {}) {
    await page.locator('#input-buscar-producto').fill(term);
    await page.waitForTimeout(700);
    const dropdown = page.locator('#dropdown-productos');
    if (!(await dropdown.isVisible({ timeout: 5_000 }).catch(() => false))) return null;
    const item = dropdown.locator('[data-id]').first();
    if (!(await item.isVisible({ timeout: 3_000 }).catch(() => false))) return null;
    const id = (await item.getAttribute('data-id')) ?? '';
    await item.click();
    await page.locator('#panel-agregar-producto').waitFor({ state: 'visible', timeout: 3_000 });
    await page.fill('#txt-cantidad', '1');
    if (descuento !== undefined) await page.fill('#txt-descuento-item', String(descuento));
    if (captura) await page.locator('#panel-agregar-producto').screenshot({ path: captura });
    await page.click('#btn-agregar-producto');
    await page.locator('#panel-agregar-producto').waitFor({ state: 'hidden', timeout: 3_000 }).catch(() => null);
    return { id };
}

async function prepararProductos(page) {
    await page.goto('/Venta/Create', { waitUntil: 'domcontentloaded' });
    await expect(page.locator('#venta-form')).toBeVisible({ timeout: 15_000 });
    await irAlPaso(page, 'cliente');
    expect(await searchAndSelectClient(page, TERM_CLIENTE), 'cliente sintético disponible').toBeTruthy();
    await irAlPaso(page, 'productos');
    await activarFiltroStock(page);
}

/** Agrega un producto al Cotizador (partial dentro de Venta/Create) y devuelve la tarjeta del carrito. */
async function agregarAlCotizador(page, term) {
    await page.goto('/Venta/Create', { waitUntil: 'domcontentloaded' });
    await page.locator('#cotizacion-producto-buscar').fill(term);
    const item = page.locator('#cotizacion-productos-dropdown button').first();
    await expect(item).toBeVisible({ timeout: 10_000 });
    await item.click();
    const fila = page.locator('#cotizacion-productos-tbody .cart-row').first();
    await expect(fila).toBeVisible();
    return fila;
}

async function descuentoDeLaPrimeraLinea(page, ventaId) {
    await page.goto(`/Venta/Edit/${ventaId}`, { waitUntil: 'domcontentloaded' });
    return page.evaluate(() => window.ventaInicial?.detalles?.[0]?.descuento);
}

test.describe('Con ventas.discount (usuario por defecto de la suite)', () => {
    test('Create: el wizard ofrece el descuento por producto y lo refleja en la tabla y en el preview', async ({ page }, testInfo) => {
        const issues = vigilar(page);
        await prepararProductos(page);
        await expect(page.locator('#venta-form')).toHaveAttribute('data-puede-descuentos', 'true');

        const previews = [];
        page.on('request', (r) => { if (/CalcularTotalesVenta/i.test(r.url())) previews.push(r.postDataJSON()); });
        expect(await agregarProducto(page, TERM_PRODUCTO, { descuento: 10, captura: testInfo.outputPath('con-permiso-panel-agregar.png') })).toBeTruthy();

        await expect(page.locator('#tbody-detalles tr').first().locator('td[data-col="descuento"]')).toContainText('10%');
        await expect(page.locator('#tbody-detalles tr').first().locator('td')).toHaveCount(7);
        await expect(page.locator('#venta-detalles-scroll thead th', { hasText: 'Desc.' })).toHaveCount(1);
        await expect.poll(() => previews.some((p) => p.detalles?.[0]?.descuento === 10)).toBeTruthy();
        await page.screenshot({ path: testInfo.outputPath('con-permiso-productos.png'), fullPage: true });

        expect(issues.pageerror).toEqual([]);
        expect(issues.http5xx).toEqual([]);
        expect(issues.console).toEqual([]);
    });

    test('Cotizador: ofrece el descuento general, el descuento por línea y el total de descuento', async ({ page }, testInfo) => {
        const fila = await agregarAlCotizador(page, TERM_PRODUCTO);
        await expect(page.locator('#cotizacion-descuento-gral-pct')).toHaveCount(1);
        await expect(page.locator('#cotizacion-descuento-gral-importe')).toHaveCount(1);
        await expect(page.locator('#cotizacion-descuento')).toHaveCount(1);
        await expect(fila.locator('.cart-row__label', { hasText: 'Descuento' })).toHaveCount(1);
        await fila.screenshot({ path: testInfo.outputPath('con-permiso-cotizador-linea.png') });
    });
});

test.describe('Sin ventas.discount', () => {
    test.beforeEach(() => exigirOOmitir(!SIN_USER || !SIN_PASS, 'Definí E2E_SIN_DESCUENTO_USER / E2E_SIN_DESCUENTO_PASS (usuario sin ventas.discount).'));

    test('Create: no hay control de descuento, la tabla no lo muestra y el wizard envía descuento 0', async ({ browser, baseURL, viewport }, testInfo) => {
        const { context, page } = await contextoSinPermiso(browser, baseURL, viewport);
        try {
            const issues = vigilar(page);
            const previews = [];
            const respuestas = [];
            page.on('request', (r) => { if (/CalcularTotalesVenta/i.test(r.url())) previews.push(r.postDataJSON()); });
            page.on('response', (r) => { if (/CalcularTotalesVenta/i.test(r.url())) respuestas.push(r.status()); });

            await prepararProductos(page);
            expect(await agregarProducto(page, TERM_PRODUCTO, { captura: testInfo.outputPath('sin-permiso-panel-agregar.png') })).toBeTruthy();

            await expect(page.locator('#venta-form')).toHaveAttribute('data-puede-descuentos', 'false');
            await expect(page.locator('#txt-descuento-item')).toHaveCount(0);
            await expect(page.locator('[data-rev-descuento]')).toHaveCount(0);
            await expect(page.locator('#venta-detalles-scroll thead th', { hasText: 'Desc.' })).toHaveCount(0);
            // La celda de descuento existe (display:none) sólo para no romper el layout mobile de la fila.
            await expect(page.locator('#tbody-detalles tr').first().locator('td[data-col="descuento"]')).toBeHidden();
            await expect(page.locator('#tbody-detalles')).not.toContainText('%');

            await expect.poll(() => previews.length).toBeGreaterThan(0);
            expect(previews.every((p) => p.detalles.every((d) => d.descuento === 0) && p.descuentoGeneral === 0)).toBeTruthy();
            expect(respuestas.every((s) => s === 200)).toBeTruthy();

            await page.screenshot({ path: testInfo.outputPath('sin-permiso-productos.png'), fullPage: true });
            expect(issues.pageerror).toEqual([]);
            expect(issues.http5xx).toEqual([]);
            expect(issues.console).toEqual([]);
        } finally {
            await context.close();
        }
    });

    test('Cotizador: sin descuento general, sin descuento por línea y sin total de descuento', async ({ browser, baseURL, viewport }, testInfo) => {
        const { context, page } = await contextoSinPermiso(browser, baseURL, viewport);
        try {
            const fila = await agregarAlCotizador(page, TERM_PRODUCTO);
            await expect(page.locator('[data-cotizacion-simulador]')).toHaveAttribute('data-puede-descuentos', 'false');
            await expect(page.locator('#cotizacion-descuento-gral-pct')).toHaveCount(0);
            await expect(page.locator('#cotizacion-descuento-gral-importe')).toHaveCount(0);
            await expect(page.locator('[data-descuentos-ambos]')).toHaveCount(0);
            await expect(page.locator('#cotizacion-descuento')).toHaveCount(0);
            await expect(fila.locator('.cart-row__label', { hasText: 'Descuento' })).toHaveCount(0);
            await expect(fila.locator('[data-cotizacion-desc-pct-index], [data-cotizacion-desc-importe-index]')).toHaveCount(0);
            await fila.screenshot({ path: testInfo.outputPath('sin-permiso-cotizador-linea.png') });
        } finally {
            await context.close();
        }
    });

    test('API: un POST manipulado con descuento recibe 403 con el mensaje de permiso desde la sesión del navegador', async ({ browser, baseURL, viewport }) => {
        const { context, page } = await contextoSinPermiso(browser, baseURL, viewport);
        try {
            const resp = await page.request.post('/api/ventas/CalcularTotalesVenta', {
                data: { detalles: [{ productoId: 1, cantidad: 2, precioUnitario: 1000, descuento: 50 }], descuentoGeneral: 0, descuentoEsPorcentaje: false, tipoPago: 0 },
            });
            expect(resp.status()).toBe(403);
            expect((await resp.json()).error).toMatch(/permiso/i);
            const sim = await page.request.post('/api/cotizacion/simular', {
                data: { productos: [{ productoId: 1, cantidad: 1, descuentoPorcentaje: 10 }] },
            });
            expect(sim.status()).toBe(403);
            expect((await sim.json()).error).toMatch(/permiso/i);
        } finally {
            await context.close();
        }
    });

    test('Edit de una venta con descuentos: totales reales, sin controles ni porcentaje en el navegador', async ({ browser, baseURL, viewport }, testInfo) => {
        exigirOOmitir(!VENTA_ID || !VENTA_TOTAL, 'Definí E2E_VENTA_CON_DESCUENTO_ID y E2E_VENTA_CON_DESCUENTO_TOTAL.');
        const { context, page } = await contextoSinPermiso(browser, baseURL, viewport);
        try {
            const issues = vigilar(page);
            await page.goto(`/Venta/Edit/${VENTA_ID}`, { waitUntil: 'domcontentloaded' });
            await expect(page.locator('#venta-form')).toHaveAttribute('data-puede-descuentos', 'false');
            await expect(page.locator('#txt-descuento-item')).toHaveCount(0);
            expect(Number(await page.locator('#hdn-descuento').inputValue())).toBe(0);   // el general persistido no viaja en el HTML (el JS lo reescribe como 0.00)

            const seed = await page.evaluate(() => window.ventaInicial);
            expect(seed.detalles.every((d) => d.descuento === 0)).toBeTruthy();           // el porcentaje persistido no llega al navegador
            expect(seed.detalles.every((d) => Number.isInteger(d.lineaId) && d.lineaId > 0)).toBeTruthy();
            expect(seed.id).toBe(Number(VENTA_ID));

            // El preview (con ventaId + lineaId) devuelve el total real aunque el navegador no conozca el descuento.
            await irAlPaso(page, 'revision');
            const entero = Math.trunc(Number(VENTA_TOTAL)).toString();
            const miles = entero.length > 3 ? `${entero.slice(0, -3)}[.\\s]?${entero.slice(-3)}` : entero;
            await expect(page.locator('#total-final')).toHaveText(new RegExp(`\\$\\s?${miles},00`), { timeout: 15_000 });
            await expect(page.locator('[data-rev-descuento]')).toHaveCount(0);
            await page.screenshot({ path: testInfo.outputPath('sin-permiso-edit-revision.png'), fullPage: true });

            expect(issues.pageerror).toEqual([]);
            expect(issues.http5xx).toEqual([]);
            expect(issues.console).toEqual([]);
        } finally {
            await context.close();
        }
    });

    test('Edit: una línea guardada no puede crecer sin permiso (mensaje claro) y guardar conserva el descuento', async ({ browser, baseURL, viewport, page: paginaConPermiso }) => {
        exigirOOmitir(!VENTA_ID || !VENTA_TOTAL, 'Definí E2E_VENTA_CON_DESCUENTO_ID y E2E_VENTA_CON_DESCUENTO_TOTAL.');
        const descuentoAntes = await descuentoDeLaPrimeraLinea(paginaConPermiso, VENTA_ID);   // el usuario con permiso sí lo ve
        expect(descuentoAntes, 'la venta de prueba debe tener descuento en su primera línea').toBeGreaterThan(0);

        const { context, page } = await contextoSinPermiso(browser, baseURL, viewport);
        try {
            await page.goto(`/Venta/Edit/${VENTA_ID}`, { waitUntil: 'domcontentloaded' });
            await irAlPaso(page, 'productos');

            // Aumentar la cantidad de la línea guardada: el navegador lo frena y explica cómo agregar unidades.
            const cantidad = page.locator('[data-quantity-input][data-index="0"]');
            const valorOriginal = await cantidad.inputValue();
            await page.locator('[data-quantity-change="1"][data-index="0"]').click();
            await expect(page.locator('#detalle-cantidad-error-0')).toContainText('línea nueva');
            await expect(cantidad).toHaveValue(valorOriginal);

            // Guardar sin confirmar: el POST no trae descuentos y el servidor conserva los persistidos.
            const posts = [];
            page.on('request', (r) => { if (r.method() === 'POST' && /\/Venta\/Edit\//i.test(r.url())) posts.push(r.postData() ?? ''); });
            await irAlPaso(page, 'revision');
            await page.locator('#btn-guardar-sin-confirmar').click();
            await page.waitForURL(/\/Venta\/Details\/\d+/, { timeout: 20_000 });
            expect(posts.length).toBe(1);
            expect(posts[0]).toMatch(/Detalles%5B0%5D\.Id=\d+/);                          // la línea viaja identificada
            expect(posts[0]).toMatch(/Detalles%5B0%5D\.Descuento=0(&|$)/);                // y sin descuento
        } finally {
            await context.close();
        }

        expect(await descuentoDeLaPrimeraLinea(paginaConPermiso, VENTA_ID)).toBe(descuentoAntes);
    });
});
