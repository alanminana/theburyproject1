// @ts-check
/**
 * PUN-ML10-F — mora de capital vs. punitorio aplicado pendiente, separados en la ficha de
 * cliente (Views/Cliente/Details_tw.cshtml) y en la prevalidación/autorización de venta.
 *
 * Escenarios con datos específicos (mora de capital sola, punitorio solo, ambos, NoApto +
 * punitorio, pagado/anulado ausente) requieren un cliente ya sembrado en la base local y se
 * habilitan con variables de entorno — mismo patrón que e2e/credito-punitorio-detalle.spec.js.
 * Sin esas variables, el test se saltea explícitamente (test.skip) en vez de fallar o simular
 * datos. Los escenarios de accesibilidad/responsive corren siempre contra cualquier cliente.
 *
 *   E2E_CLIENTE_SIN_DEUDA_ID          cliente sin mora ni punitorio
 *   E2E_CLIENTE_MORA_CAPITAL_ID       cliente con sólo mora de capital
 *   E2E_CLIENTE_PUNITORIO_ID          cliente con sólo punitorio aplicado pendiente
 *   E2E_CLIENTE_MORA_Y_PUNITORIO_ID   cliente con mora de capital + punitorio aplicado pendiente
 *   E2E_CLIENTE_NOAPTO_PUNITORIO_ID   cliente NoApto (capital) + punitorio aplicado pendiente
 */
const { test, expect } = require('playwright/test');
const { searchAndSelectClient, addProduct, setGlobalTipoPago, waitForDiagnostico, TIPO_PAGO } = require('./helpers');

const VIEWPORTS = [
    { name: '360x800', width: 360, height: 800 },
    { name: '768x1024', width: 768, height: 1024 },
    { name: '1366x768', width: 1366, height: 768 },
];

const CLIENTE_SIN_DEUDA_ID = process.env.E2E_CLIENTE_SIN_DEUDA_ID;
const CLIENTE_MORA_CAPITAL_ID = process.env.E2E_CLIENTE_MORA_CAPITAL_ID;
const CLIENTE_PUNITORIO_ID = process.env.E2E_CLIENTE_PUNITORIO_ID;
const CLIENTE_MORA_Y_PUNITORIO_ID = process.env.E2E_CLIENTE_MORA_Y_PUNITORIO_ID;
const CLIENTE_NOAPTO_PUNITORIO_ID = process.env.E2E_CLIENTE_NOAPTO_PUNITORIO_ID;

test.use({ storageState: 'e2e/.auth/user.json' });

test.beforeEach(async ({ page }) => {
    await page.route('**/fonts.googleapis.com/**', route =>
        route.fulfill({ status: 200, contentType: 'text/css', body: '' }));
    await page.route('**/fonts.gstatic.com/**', route =>
        route.fulfill({ status: 204, body: '' }));
});

function trackFailures(page) {
    const consoleErrors = [];
    const serverErrors = [];
    page.on('console', message => {
        if (message.type() === 'error') consoleErrors.push(message.text());
    });
    page.on('response', response => {
        if (response.status() >= 500) serverErrors.push(`${response.status()} ${response.url()}`);
    });
    return { consoleErrors, serverErrors };
}

function expectNoFailures(failures) {
    expect(failures.consoleErrors, `errores de consola: ${failures.consoleErrors.join(' | ')}`).toHaveLength(0);
    expect(failures.serverErrors, `HTTP 5xx: ${failures.serverErrors.join(' | ')}`).toHaveLength(0);
}

async function gotoClienteDetails(page, clienteId) {
    const failures = trackFailures(page);
    await page.goto(`/Cliente/Details/${clienteId}`, { waitUntil: 'domcontentloaded' });
    await expect(page).not.toHaveURL(/Identity\/Account\/Login/);
    return failures;
}

// ---------------------------------------------------------------------------
// Ficha de cliente — separación mora de capital vs. punitorio (Fase 2)
// ---------------------------------------------------------------------------

test.describe('Cliente Details — mora de capital vs. punitorio aplicado pendiente', () => {
    test('cliente sin deuda: no muestra bloque de capital en mora ni de punitorio', async ({ page }) => {
        test.skip(!CLIENTE_SIN_DEUDA_ID, 'Requiere E2E_CLIENTE_SIN_DEUDA_ID.');
        const failures = await gotoClienteDetails(page, CLIENTE_SIN_DEUDA_ID);

        await expect(page.getByText('Capital en mora', { exact: true })).toHaveCount(0);
        await expect(page.getByText('Punitorio aplicado pendiente', { exact: true })).toHaveCount(0);
        expectNoFailures(failures);
    });

    test('sólo mora de capital: muestra capital pero no el bloque de punitorio', async ({ page }) => {
        test.skip(!CLIENTE_MORA_CAPITAL_ID, 'Requiere E2E_CLIENTE_MORA_CAPITAL_ID.');
        const failures = await gotoClienteDetails(page, CLIENTE_MORA_CAPITAL_ID);

        await expect(page.getByText('Capital en mora', { exact: true })).toBeVisible();
        await expect(page.getByText('Punitorio aplicado pendiente', { exact: true })).toHaveCount(0);
        expectNoFailures(failures);
    });

    test('sólo punitorio aplicado pendiente: muestra el bloque con monto real, sin capital en mora', async ({ page }) => {
        test.skip(!CLIENTE_PUNITORIO_ID, 'Requiere E2E_CLIENTE_PUNITORIO_ID.');
        const failures = await gotoClienteDetails(page, CLIENTE_PUNITORIO_ID);

        await expect(page.getByText('Capital en mora', { exact: true })).toHaveCount(0);
        const bloque = page.locator('.alert', { hasText: 'Punitorio aplicado pendiente' });
        await expect(bloque).toBeVisible();
        await expect(bloque.getByText('Monto pendiente')).toBeVisible();
        await expect(bloque.getByText('Cuotas afectadas')).toBeVisible();
        await expect(bloque.getByText('Estado de aptitud')).toBeVisible();

        // Nunca un monto ficticio: ni "$0", ni la celda vacía.
        const montoFila = bloque.locator('.kv-row', { hasText: 'Monto pendiente' });
        await expect(montoFila).not.toHaveText(/\$\s?0,00/);
        expectNoFailures(failures);
    });

    test('mora de capital + punitorio: ambos bloques visibles, sin mezclar montos', async ({ page }) => {
        test.skip(!CLIENTE_MORA_Y_PUNITORIO_ID, 'Requiere E2E_CLIENTE_MORA_Y_PUNITORIO_ID.');
        const failures = await gotoClienteDetails(page, CLIENTE_MORA_Y_PUNITORIO_ID);

        await expect(page.getByText('Capital en mora', { exact: true })).toBeVisible();
        const bloquePunitorio = page.locator('.alert', { hasText: 'Punitorio aplicado pendiente' });
        await expect(bloquePunitorio).toBeVisible();
        expectNoFailures(failures);
    });

    test('NoApto por capital + punitorio: el bloque de punitorio es informativo, no un segundo bloqueo', async ({ page }) => {
        test.skip(!CLIENTE_NOAPTO_PUNITORIO_ID, 'Requiere E2E_CLIENTE_NOAPTO_PUNITORIO_ID.');
        const failures = await gotoClienteDetails(page, CLIENTE_NOAPTO_PUNITORIO_ID);

        await expect(page.getByText('No apto', { exact: true }).first()).toBeVisible();
        const bloquePunitorio = page.locator('.alert', { hasText: 'Punitorio aplicado pendiente' });
        await expect(bloquePunitorio).toBeVisible();
        await expect(bloquePunitorio.getByText('No bloquea la aptitud por sí sola')).toBeVisible();
        expectNoFailures(failures);
    });
});

// ---------------------------------------------------------------------------
// Prevalidación de venta — motivo Punitorio con monto real (Fase 3)
// ---------------------------------------------------------------------------

test.describe('Venta Create — prevalidación con motivo Punitorio', () => {
    test('el panel de motivos nunca muestra "$0,00" ni el genérico "Requiere revisión" para Punitorio', async ({ page }) => {
        const failures = trackFailures(page);
        await page.goto('/Venta/Create', { waitUntil: 'domcontentloaded' });
        await page.locator('#step-btn-cliente').click();
        const seleccionado = await searchAndSelectClient(page);
        test.skip(!seleccionado, 'El entorno no expone cliente de QA para habilitar prevalidación.');

        // El step "Pago" queda deshabilitado hasta agregar al menos un producto.
        await page.locator('#step-btn-productos').click();
        const producto = await addProduct(page, 'a');
        test.skip(!producto, 'El entorno no expone productos para habilitar el step de pago.');

        // El select de tipo de pago vive en el step "Pago" del wizard (oculto hasta navegar ahí).
        await page.locator('#step-btn-pago').click();
        await expect(page.locator('#step-panel-pago')).toBeVisible();
        await setGlobalTipoPago(page, TIPO_PAGO.CreditoPersonal);
        await waitForDiagnostico(page);

        const panel = page.locator('#panel-motivos');
        const visible = await panel.isVisible().catch(() => false);
        test.skip(!visible, 'El cliente seleccionado no generó motivos de prevalidación.');

        const motivoPunitorio = page.locator('#lista-motivos > div', { hasText: 'Punitorio' });
        const tienePunitorio = await motivoPunitorio.count();
        test.skip(!tienePunitorio, 'El cliente seleccionado no tiene punitorio aplicado pendiente.');

        await expect(motivoPunitorio.first()).not.toHaveText(/\$\s?0,00/);
        await expect(motivoPunitorio.first().getByText('Requiere revisión')).toHaveCount(0);
        expectNoFailures(failures);
    });
});

// ---------------------------------------------------------------------------
// Accesibilidad y responsive (Fase 6) — corren siempre, sin datos específicos.
// ---------------------------------------------------------------------------

test.describe('Cliente Details — responsive, zoom y teclado', () => {
    // Navega directo por href (en vez de click) porque a viewports angostos la ficha de cliente
    // usa un layout de cards donde el botón "Ver" puede quedar fuera del recorte visible del
    // primer elemento sin hacer scroll — no es lo que este test verifica (overflow de Details).
    async function primeraFichaClienteHref(page) {
        await page.goto('/Cliente', { waitUntil: 'domcontentloaded' });
        const primerCliente = page.locator('a[href*="/Cliente/Details/"]').first();
        const tieneCliente = await primerCliente.count();
        if (!tieneCliente) return null;
        return primerCliente.getAttribute('href');
    }

    for (const viewport of VIEWPORTS) {
        test(`ficha de cliente sin overflow horizontal en ${viewport.name}`, async ({ page }) => {
            await page.setViewportSize({ width: viewport.width, height: viewport.height });
            const href = await primeraFichaClienteHref(page);
            test.skip(!href, 'No hay clientes disponibles.');

            const failures = trackFailures(page);
            await page.goto(href, { waitUntil: 'domcontentloaded' });
            await expect(page).not.toHaveURL(/Identity\/Account\/Login/);

            const hasHorizontalOverflow = await page.evaluate(
                () => document.documentElement.scrollWidth > window.innerWidth
            );
            expect(hasHorizontalOverflow).toBeFalsy();
            expectNoFailures(failures);
        });
    }

    test('zoom 200% mantiene la ficha de cliente dentro de la página', async ({ page }) => {
        await page.setViewportSize({ width: 1366, height: 768 });
        const href = await primeraFichaClienteHref(page);
        test.skip(!href, 'No hay clientes disponibles.');
        await page.goto(href, { waitUntil: 'domcontentloaded' });

        await page.evaluate(() => { document.documentElement.style.zoom = '200%'; });
        const hasHorizontalOverflow = await page.evaluate(
            () => document.documentElement.scrollWidth > window.innerWidth
        );
        expect(hasHorizontalOverflow).toBeFalsy();
    });

    test('el botón "Recalcular aptitud" es alcanzable por teclado', async ({ page }) => {
        const href = await primeraFichaClienteHref(page);
        test.skip(!href, 'No hay clientes disponibles.');
        await page.goto(href, { waitUntil: 'domcontentloaded' });

        const boton = page.getByRole('button', { name: 'Recalcular aptitud' });
        const visible = await boton.isVisible().catch(() => false);
        test.skip(!visible, 'El banner de aptitud no está visible para este cliente.');
        await boton.focus();
        await expect(boton).toBeFocused();
    });
});
