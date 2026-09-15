// @ts-check
/**
 * PUN-ML10-G — mora de capital vs. punitorio aplicado pendiente, separados en la ficha de cliente
 * (Views/Cliente/Details_tw.cshtml, PUN-ML10-F), en el panel de crédito del cliente
 * (Views/Credito/_PanelClientePartial.cshtml, PUN-ML10-G) y en la prevalidación de venta.
 *
 * Cierra el carryover explícito dejado por PUN-ML10-F: los 9 escenarios con datos reales ya NO
 * dependen de que alguien haya sembrado clientes a mano en la base de desarrollo — se generan de
 * forma determinística y re-ejecutable con
 * `TheBuryProyect.Tests/E2ESeeding/ClienteAptitudPunitorioE2ESeeder.cs` contra una LocalDB
 * descartable (ver ese archivo para el procedimiento completo). Cero `test.skip` por falta de
 * datos: si las variables de entorno no están seteadas, el spec falla fuerte y explica cómo
 * sembrarlas — nunca se salta en silencio.
 *
 *   E2E_CLIENTE_SIN_DEUDA_ID                          sin mora ni punitorio
 *   E2E_CLIENTE_MORA_CAPITAL_ID                        sólo mora de capital (también prueba
 *                                                       "punitorio calculado no aplicado": la
 *                                                       config de punitorio está activa y esta
 *                                                       cuota está vencida más allá de la gracia,
 *                                                       pero nunca se aplicó ninguno)
 *   E2E_CLIENTE_PUNITORIO_ID                           sólo punitorio aplicado pendiente
 *   E2E_CLIENTE_MORA_Y_PUNITORIO_ID                    mora de capital + punitorio pendiente
 *   E2E_CLIENTE_NOAPTO_PUNITORIO_ID                    capital NoApto + punitorio pendiente
 *   E2E_CLIENTE_PUNITORIO_PARCIAL_ID                   punitorio parcialmente pagado (pendiente neto)
 *   E2E_CLIENTE_PUNITORIO_PAGADO_ID                    punitorio totalmente pagado (no pendiente)
 *   E2E_CLIENTE_PUNITORIO_ANULADO_ID                   punitorio anulado (no pendiente)
 *   E2E_CLIENTE_PUNITORIO_CALCULADO_NO_APLICADO_ID     alias de MORA_CAPITAL, ver arriba
 *   E2E_CLIENTE_MORA_Y_PUNITORIO_DOCUMENTO             documento del cliente de MORA_Y_PUNITORIO,
 *                                                       usado para buscarlo en Venta/Create
 */
const { test, expect } = require('playwright/test');
const { addProduct, setGlobalTipoPago, TIPO_PAGO } = require('./helpers');

const VIEWPORTS = [
    { name: '360x800', width: 360, height: 800 },
    { name: '768x1024', width: 768, height: 1024 },
    { name: '1366x768', width: 1366, height: 768 },
];

const REQUIRED_ENV_VARS = [
    'E2E_CLIENTE_SIN_DEUDA_ID',
    'E2E_CLIENTE_MORA_CAPITAL_ID',
    'E2E_CLIENTE_PUNITORIO_ID',
    'E2E_CLIENTE_MORA_Y_PUNITORIO_ID',
    'E2E_CLIENTE_NOAPTO_PUNITORIO_ID',
    'E2E_CLIENTE_PUNITORIO_PARCIAL_ID',
    'E2E_CLIENTE_PUNITORIO_PAGADO_ID',
    'E2E_CLIENTE_PUNITORIO_ANULADO_ID',
    'E2E_CLIENTE_PUNITORIO_CALCULADO_NO_APLICADO_ID',
    'E2E_CLIENTE_MORA_Y_PUNITORIO_DOCUMENTO',
];

const faltantes = REQUIRED_ENV_VARS.filter(v => !process.env[v]);
if (faltantes.length > 0) {
    throw new Error(
        '\n[PUN-ML10-G] Faltan variables de entorno para cliente-aptitud-punitorio.spec.js:\n' +
        faltantes.map(v => `  - ${v}`).join('\n') +
        '\n\nEste spec no se saltea escenarios por falta de datos (regla congelada del cierre PUN-ML10-G).\n' +
        'Sembrar contra una LocalDB descartable:\n' +
        '  $env:E2E_SEED_CONNECTION = "Server=(localdb)\\MSSQLLocalDB;Database=<db_descartable>;Trusted_Connection=True;TrustServerCertificate=True"\n' +
        '  dotnet test TheBuryProyect.Tests --filter "FullyQualifiedName~ClienteAptitudPunitorioE2ESeedRunner.Sembrar"\n' +
        'y exportar las variables desde e2e/.auth/pun-ml10g-seed-ids.json antes de correr Playwright.\n'
    );
}

const CLIENTE_SIN_DEUDA_ID = process.env.E2E_CLIENTE_SIN_DEUDA_ID;
const CLIENTE_MORA_CAPITAL_ID = process.env.E2E_CLIENTE_MORA_CAPITAL_ID;
const CLIENTE_PUNITORIO_ID = process.env.E2E_CLIENTE_PUNITORIO_ID;
const CLIENTE_MORA_Y_PUNITORIO_ID = process.env.E2E_CLIENTE_MORA_Y_PUNITORIO_ID;
const CLIENTE_NOAPTO_PUNITORIO_ID = process.env.E2E_CLIENTE_NOAPTO_PUNITORIO_ID;
const CLIENTE_PUNITORIO_PARCIAL_ID = process.env.E2E_CLIENTE_PUNITORIO_PARCIAL_ID;
const CLIENTE_PUNITORIO_PAGADO_ID = process.env.E2E_CLIENTE_PUNITORIO_PAGADO_ID;
const CLIENTE_PUNITORIO_ANULADO_ID = process.env.E2E_CLIENTE_PUNITORIO_ANULADO_ID;
const CLIENTE_PUNITORIO_CALCULADO_NO_APLICADO_ID = process.env.E2E_CLIENTE_PUNITORIO_CALCULADO_NO_APLICADO_ID;
const CLIENTE_MORA_Y_PUNITORIO_DOCUMENTO = process.env.E2E_CLIENTE_MORA_Y_PUNITORIO_DOCUMENTO;

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

// El bloque de mora de capital/punitorio vive dentro de la card "Informacion
// crediticia", que la reorganizacion en solapas (2026-09-14) movio a la solapa
// "Credito" de Cliente/Details (Resumen, la solapa por defecto, solo muestra un
// resumen de 4 metricas). Los tests que verifican ese bloque en detalle navegan
// primero a esa solapa; los que solo verifican ausencia (toHaveCount(0)) no lo
// necesitan, porque un elemento server-side ausente lo esta sin importar la
// solapa activa.
async function irATabCredito(page) {
    await page.locator('[data-cliente-tab="credito"]').click();
    await expect(page.locator('#panel-credito')).toBeVisible();
}

async function gotoPanelCredito(page, clienteId) {
    const failures = trackFailures(page);
    await page.goto(`/Credito?ClienteId=${clienteId}`, { waitUntil: 'domcontentloaded' });
    await expect(page).not.toHaveURL(/Identity\/Account\/Login/);
    const card = page.locator(`[data-credito-cliente-id="${clienteId}"]`).first();
    await expect(card).toBeVisible();
    await card.locator('[data-credito-user-toggle]').click();
    const body = card.locator('[data-credito-user-body]');
    await expect(body).toBeVisible();
    return { card, body, failures };
}

// ---------------------------------------------------------------------------
// Ficha de cliente — separación mora de capital vs. punitorio aplicado pendiente
// ---------------------------------------------------------------------------

test.describe('Cliente Details — mora de capital vs. punitorio aplicado pendiente', () => {
    test('cliente sin deuda: no muestra bloque de capital en mora ni de punitorio', async ({ page }) => {
        const failures = await gotoClienteDetails(page, CLIENTE_SIN_DEUDA_ID);

        await expect(page.getByText('Capital en mora', { exact: true })).toHaveCount(0);
        await expect(page.getByText('Punitorio aplicado pendiente', { exact: true })).toHaveCount(0);
        expectNoFailures(failures);
    });

    test('sólo mora de capital: muestra capital pero no el bloque de punitorio', async ({ page }) => {
        const failures = await gotoClienteDetails(page, CLIENTE_MORA_CAPITAL_ID);

        await expect(page.getByText('Capital en mora', { exact: true })).toBeVisible();
        await expect(page.getByText('Punitorio aplicado pendiente', { exact: true })).toHaveCount(0);
        expectNoFailures(failures);
    });

    test('punitorio calculado pero nunca aplicado: nunca genera un bloque de punitorio', async ({ page }) => {
        // Mismo cliente que "sólo mora de capital" — la ConfiguracionPunitorio del seed está activa
        // y esta cuota está vencida más allá de los días de gracia, así que un punitorio SÍ sería
        // calculable ahora mismo (PunitorioService.CalcularCuotaAsync devolvería un importe > 0).
        // Nunca se aplicó ninguno: el bloque de punitorio no debe aparecer bajo ninguna circunstancia.
        const failures = await gotoClienteDetails(page, CLIENTE_PUNITORIO_CALCULADO_NO_APLICADO_ID);

        await expect(page.getByText('Punitorio aplicado pendiente', { exact: true })).toHaveCount(0);
        expectNoFailures(failures);
    });

    test('sólo punitorio aplicado pendiente: muestra el bloque con monto real, sin capital en mora', async ({ page }) => {
        const failures = await gotoClienteDetails(page, CLIENTE_PUNITORIO_ID);
        await irATabCredito(page);

        await expect(page.getByText('Capital en mora', { exact: true })).toHaveCount(0);
        const bloque = page.locator('.alert', { hasText: 'Punitorio aplicado pendiente' });
        await expect(bloque).toBeVisible();
        await expect(bloque.getByText('Monto pendiente')).toBeVisible();
        await expect(bloque.getByText('Cuotas afectadas')).toBeVisible();
        await expect(bloque.getByText('Estado de aptitud')).toBeVisible();

        // Nunca un monto ficticio: ni "$0", ni la celda vacía. El seed aplicó $300.
        const montoFila = bloque.locator('.kv-row', { hasText: 'Monto pendiente' });
        await expect(montoFila).not.toHaveText(/\$\s?0,00/);
        await expect(montoFila).toContainText('300');
        expectNoFailures(failures);
    });

    test('mora de capital + punitorio: ambos bloques visibles, sin mezclar montos', async ({ page }) => {
        const failures = await gotoClienteDetails(page, CLIENTE_MORA_Y_PUNITORIO_ID);

        // "Capital en mora" ya es visible en el resumen de la solapa Resumen (default);
        // el bloque completo de punitorio vive en el detalle de la solapa Credito.
        await expect(page.getByText('Capital en mora', { exact: true })).toBeVisible();
        await irATabCredito(page);
        const bloquePunitorio = page.locator('.alert', { hasText: 'Punitorio aplicado pendiente' });
        await expect(bloquePunitorio).toBeVisible();
        expectNoFailures(failures);
    });

    test('NoApto por capital + punitorio: el bloque de punitorio es informativo, no un segundo bloqueo', async ({ page }) => {
        const failures = await gotoClienteDetails(page, CLIENTE_NOAPTO_PUNITORIO_ID);

        await expect(page.getByText('No apto', { exact: true }).first()).toBeVisible();
        await irATabCredito(page);
        const bloquePunitorio = page.locator('.alert', { hasText: 'Punitorio aplicado pendiente' });
        await expect(bloquePunitorio).toBeVisible();
        await expect(bloquePunitorio.getByText('No bloquea la aptitud por sí sola')).toBeVisible();
        expectNoFailures(failures);
    });

    test('punitorio parcialmente pagado: el monto pendiente es el neto, no el importe original', async ({ page }) => {
        // Seed: Importe aplicado $500, pago parcial $200 => pendiente neto $300 (nunca $500).
        const failures = await gotoClienteDetails(page, CLIENTE_PUNITORIO_PARCIAL_ID);
        await irATabCredito(page);

        const bloque = page.locator('.alert', { hasText: 'Punitorio aplicado pendiente' });
        await expect(bloque).toBeVisible();
        const montoFila = bloque.locator('.kv-row', { hasText: 'Monto pendiente' });
        await expect(montoFila).toContainText('300');
        await expect(montoFila).not.toContainText('500');
        expectNoFailures(failures);
    });

    test('punitorio totalmente pagado: no muestra bloque de punitorio pendiente', async ({ page }) => {
        const failures = await gotoClienteDetails(page, CLIENTE_PUNITORIO_PAGADO_ID);

        await expect(page.getByText('Punitorio aplicado pendiente', { exact: true })).toHaveCount(0);
        expectNoFailures(failures);
    });

    test('punitorio anulado: no muestra bloque de punitorio pendiente', async ({ page }) => {
        const failures = await gotoClienteDetails(page, CLIENTE_PUNITORIO_ANULADO_ID);

        await expect(page.getByText('Punitorio aplicado pendiente', { exact: true })).toHaveCount(0);
        expectNoFailures(failures);
    });
});

// ---------------------------------------------------------------------------
// Panel de crédito del cliente (Views/Credito/_PanelClientePartial.cshtml, PUN-ML10-G)
// ---------------------------------------------------------------------------

test.describe('Panel Crédito — mora de capital vs. punitorio aplicado pendiente', () => {
    test('sin deuda: minimetric de mora en $0 y sin bloque de punitorio', async ({ page }) => {
        const { body, failures } = await gotoPanelCredito(page, CLIENTE_SIN_DEUDA_ID);

        await expect(body.getByText('Mora de capital')).toBeVisible();
        await expect(body.locator('.alert', { hasText: 'Punitorio aplicado pendiente' })).toHaveCount(0);
        expectNoFailures(failures);
    });

    test('mora + punitorio: panel muestra ambos, rótulo "Mora de capital" (no ambiguo)', async ({ page }) => {
        const { body, failures } = await gotoPanelCredito(page, CLIENTE_MORA_Y_PUNITORIO_ID);

        await expect(body.getByText('Mora de capital')).toBeVisible();
        // Regla congelada: nunca el rótulo ambiguo legacy "En mora".
        await expect(body.getByText('En mora', { exact: true })).toHaveCount(0);
        const bloquePunitorio = body.locator('.alert', { hasText: 'Punitorio aplicado pendiente' });
        await expect(bloquePunitorio).toBeVisible();
        await expect(bloquePunitorio).not.toHaveText(/\$\s?0,00/);
        expectNoFailures(failures);
    });

    test('punitorio pagado: panel no muestra bloque de punitorio pendiente', async ({ page }) => {
        const { body, failures } = await gotoPanelCredito(page, CLIENTE_PUNITORIO_PAGADO_ID);

        await expect(body.locator('.alert', { hasText: 'Punitorio aplicado pendiente' })).toHaveCount(0);
        expectNoFailures(failures);
    });
});

// ---------------------------------------------------------------------------
// Prevalidación de venta — motivo Punitorio con monto real
// ---------------------------------------------------------------------------

test.describe('Venta Create — prevalidación con motivo Punitorio', () => {
    test('el panel de motivos muestra el motivo Punitorio con monto real, nunca "$0,00" ni el genérico', async ({ page }) => {
        const failures = trackFailures(page);
        await page.goto('/Venta/Create', { waitUntil: 'domcontentloaded' });
        await page.locator('#step-btn-cliente').click();

        const input = page.locator('#input-buscar-cliente');
        await input.fill(CLIENTE_MORA_Y_PUNITORIO_DOCUMENTO);
        await page.waitForTimeout(700);
        const dropdown = page.locator('#dropdown-clientes');
        await expect(dropdown).toBeVisible({ timeout: 5_000 });
        const item = dropdown.locator(`[data-id="${CLIENTE_MORA_Y_PUNITORIO_ID}"]`).first();
        await expect(item).toBeVisible({ timeout: 5_000 });
        await item.click();

        // El step "Pago" queda deshabilitado hasta agregar al menos un producto. El seed garantiza
        // al menos un producto vendible (marcador E2EPUNML10G-PRODUCTO) — sin skip por falta de catálogo.
        await page.locator('#step-btn-productos').click();
        const producto = await addProduct(page, 'PUN-ML10-G');
        expect(producto, 'El seed debe garantizar al menos un producto vendible.').toBeTruthy();

        await page.locator('#step-btn-pago').click();
        await expect(page.locator('#step-panel-pago')).toBeVisible();

        // El panel de verificación crediticia (#panel-motivos) vive dentro del step 5 "Crédito"
        // (#step-panel-credito), no del step 4 "Pago" — elegir Crédito Personal en el step "Pago"
        // sólo habilita la pestaña "Crédito" (queda con `hidden` hasta entonces), hay que navegar
        // ahí para verla. Hallazgo real de esta sesión: un intento anterior de este spec revisaba
        // #panel-motivos sin haber entrado al step correcto — el contenido ya estaba en el DOM
        // (por eso `#lista-motivos` tenía hijos) pero un ancestro (`#step-panel-credito`) seguía
        // con `display:none`, así que Playwright correctamente lo reportaba como no-visible.
        const prevalidacion = page.waitForResponse(
            resp => resp.url().includes('/api/ventas/PrevalidarCredito') && resp.status() === 200,
            { timeout: 15_000 });
        await setGlobalTipoPago(page, TIPO_PAGO.CreditoPersonal);
        await prevalidacion;

        const tabCredito = page.locator('#step-btn-credito');
        await expect(tabCredito).toBeEnabled({ timeout: 10_000 });
        await tabCredito.click();
        await expect(page.locator('#step-panel-credito')).toBeVisible();

        const panel = page.locator('#panel-motivos');
        await expect(panel).toBeVisible({ timeout: 10_000 });

        const motivoPunitorio = page.locator('#lista-motivos > div', { hasText: 'Punitorio' });
        await expect(motivoPunitorio.first()).toBeVisible({ timeout: 10_000 });
        await expect(motivoPunitorio.first()).not.toHaveText(/\$\s?0,00/);
        await expect(motivoPunitorio.first().getByText('Requiere revisión')).toHaveCount(0);
        expectNoFailures(failures);
    });
});

// ---------------------------------------------------------------------------
// Accesibilidad y responsive — corren siempre, sin datos específicos.
// ---------------------------------------------------------------------------

test.describe('Cliente Details — responsive, zoom y teclado', () => {
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
            const failures = await gotoClienteDetails(page, CLIENTE_MORA_Y_PUNITORIO_ID);

            const hasHorizontalOverflow = await page.evaluate(
                () => document.documentElement.scrollWidth > window.innerWidth
            );
            expect(hasHorizontalOverflow).toBeFalsy();
            expectNoFailures(failures);
        });
    }

    test('zoom 200% mantiene la ficha de cliente dentro de la página', async ({ page }) => {
        await page.setViewportSize({ width: 1366, height: 768 });
        await gotoClienteDetails(page, CLIENTE_MORA_Y_PUNITORIO_ID);

        await page.evaluate(() => { document.documentElement.style.zoom = '200%'; });
        const hasHorizontalOverflow = await page.evaluate(
            () => document.documentElement.scrollWidth > window.innerWidth
        );
        expect(hasHorizontalOverflow).toBeFalsy();
    });

    test('el botón "Recalcular aptitud" es alcanzable por teclado', async ({ page }) => {
        await gotoClienteDetails(page, CLIENTE_MORA_Y_PUNITORIO_ID);

        const boton = page.getByRole('button', { name: 'Recalcular aptitud' });
        await expect(boton).toBeVisible();
        await boton.focus();
        await expect(boton).toBeFocused();
    });

    test('el href de la primera ficha de la lista sigue siendo navegable (smoke adicional)', async ({ page }) => {
        const href = await primeraFichaClienteHref(page);
        expect(href).toBeTruthy();
    });
});
