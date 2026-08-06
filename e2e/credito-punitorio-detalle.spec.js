// @ts-check
/**
 * PUN-ML9-C — consulta, aplicación y anulación de punitorios en Credito/Details.
 *
 * Requiere E2E_USER/E2E_PASS con creditos.view y un crédito con cuotas. Para una base
 * aislada puede fijarse E2E_CREDITO_PUN_ML9_ID; si no, toma el primer Details visible.
 */
const { test, expect } = require('playwright/test');

const CREDIT_ID = process.env.E2E_CREDITO_PUN_ML9_ID;
const BASE_URL = process.env.E2E_BASE_URL || 'http://localhost:5187';
const OPERATION_CREDIT_ID = Number(process.env.E2E_CREDITO_PUN_ML9_C_ID || 0);
const OPERATION_QUOTAS = (process.env.E2E_CUOTAS_PUN_ML9_C || '')
    .split(',')
    .map(value => Number(value.trim()))
    .filter(Number.isInteger);
// PUN-ML10-G.1: cuota dedicada y determinista para el escenario "historial incompleto" —
// reemplaza el test.skip condicional que dependía de encontrarla "si existía" en datos ad hoc.
const CUOTA_HISTORIAL_INCOMPLETO = Number(process.env.E2E_CUOTA_HISTORIAL_INCOMPLETO || 0);
const VIEWPORTS = [
    { name: '1440x900', width: 1440, height: 900 },
    { name: '1280x720', width: 1280, height: 720 },
    { name: '1024x720', width: 1024, height: 720 },
    { name: '900x720', width: 900, height: 720 },
    { name: '360x800', width: 360, height: 800 },
    { name: '390x844', width: 390, height: 844 },
    { name: '768x1024', width: 768, height: 1024 },
    { name: '1366x768', width: 1366, height: 768 },
];

test.use({ storageState: 'e2e/.auth/user.json' });

async function gotoCreditoDetails(page) {
    if (CREDIT_ID) {
        await page.goto(`/Credito/Details/${CREDIT_ID}`, { waitUntil: 'domcontentloaded' });
    } else {
        await page.goto('/Credito', { waitUntil: 'domcontentloaded' });
        const first = page.locator('a[href*="/Credito/Details/"]').first();
        test.skip(!(await first.count()), 'No hay créditos con cuotas disponibles para PUN-ML9-B2.');
        await first.click();
        await page.waitForLoadState('domcontentloaded');
    }

    await expect(page).not.toHaveURL(/Identity\/Account\/Login/);
    await expect(page.locator('[data-credito-details]')).toBeVisible();
}

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

function expectNoFailures(failures, ignoredConsolePatterns = []) {
    const unexpectedConsoleErrors = failures.consoleErrors.filter(error =>
        !ignoredConsolePatterns.some(pattern => pattern.test(error)));
    expect(
        unexpectedConsoleErrors,
        `errores de consola: ${unexpectedConsoleErrors.join(' | ')}`
    ).toHaveLength(0);
    expect(failures.serverErrors, `HTTP 5xx: ${failures.serverErrors.join(' | ')}`).toHaveLength(0);
}

async function openQuotaPanel(page, cuotaId) {
    await gotoCreditoDetails(page);
    const toggle = page.locator(`[aria-controls="punitorio-panel-${cuotaId}"]`);
    await expect(toggle).toHaveCount(1);
    if (await toggle.getAttribute('aria-expanded') !== 'true') {
        await toggle.click();
    }
    const panel = page.locator(`#punitorio-panel-${cuotaId}`);
    await expect(panel.getByText('Capital pendiente')).toBeVisible();
    return { toggle, panel };
}

async function loginWith(page, user, pass) {
    await page.goto('/Identity/Account/Login', { waitUntil: 'domcontentloaded' });
    await page.fill('#Input_UserName, input[name="Input.UserName"]', user);
    await page.fill('#input-password, input[name="Input.Password"]', pass);
    await page.click('button[type="submit"]');

    const nombreCompleto = page.locator('input[placeholder="Nombre y apellido"]');
    if (await nombreCompleto.isVisible({ timeout: 5_000 }).catch(() => false)) {
        await nombreCompleto.fill(`E2E ${user}`);
        await page.getByRole('checkbox', { name: /Declaro que le.* y acepto/i }).setChecked(true);
        await page.getByRole('button', { name: 'Aceptar y continuar' }).click();
    }
    await expect(page).not.toHaveURL(/[Ll]ogin/);
}

test.beforeEach(async ({ page }) => {
    await page.route('**/fonts.googleapis.com/**', route =>
        route.fulfill({ status: 200, contentType: 'text/css', body: '' }));
    await page.route('**/fonts.gstatic.com/**', route =>
        route.fulfill({ status: 204, body: '' }));
});

test.describe('Crédito Details — punitorios read-only', () => {
    test('carga una vez, evita doble request y conserva navegación por teclado', async ({ page }) => {
        const failures = trackFailures(page);
        await gotoCreditoDetails(page);

        const toggle = page.locator('[data-punitorio-toggle]').first();
        test.skip(!(await toggle.count()), 'El crédito no tiene cuotas.');
        const panelLinks = await page.evaluate(() => {
            const controls = Array.from(document.querySelectorAll('[data-punitorio-toggle]'))
                .map(button => button.getAttribute('aria-controls'));
            return {
                total: controls.length,
                unique: new Set(controls).size,
                missing: controls.filter(id => !id || !document.getElementById(id)).length
            };
        });
        expect(panelLinks.unique).toBe(panelLinks.total);
        expect(panelLinks.missing).toBe(0);
        const endpointPattern = /\/Credito\/\d+\/Cuotas\/\d+\/Punitorio/;
        let requestCount = 0;
        page.on('request', request => {
            if (endpointPattern.test(new URL(request.url()).pathname)) requestCount += 1;
        });

        await toggle.evaluate(button => {
            button.click();
            button.click();
        });

        const panelId = await toggle.getAttribute('aria-controls');
        const panel = page.locator(`#${panelId}`);
        await expect(panel.getByText('Capital pendiente')).toBeVisible();
        await expect(panel.getByText('Punitorio calculado hoy')).toBeVisible();
        await expect(panel.getByText('Punitorio aplicado pendiente')).toBeVisible();
        await expect(panel.getByText('Total cobrable actual')).toBeVisible();
        await expect(panel.getByRole('heading', { name: 'Aplicaciones' })).toBeVisible();
        await expect(panel.getByRole('heading', { name: 'Pagos' })).toBeVisible();
        expect(requestCount).toBe(1);

        await toggle.focus();
        await page.keyboard.press('Space');
        await expect(toggle).toHaveAttribute('aria-expanded', 'false');
        await page.keyboard.press('Enter');
        await expect(toggle).toHaveAttribute('aria-expanded', 'true');
        await expect(panel.getByText('Capital pendiente')).toBeVisible();
        expect(requestCount).toBe(1);

        expectNoFailures(failures);
    });

    test('historial incompleto sigue respondiendo 200 y lo advierte sin convertir null en cero', async ({ page }) => {
        test.skip(!CUOTA_HISTORIAL_INCOMPLETO, 'Requiere E2E_CUOTA_HISTORIAL_INCOMPLETO (cuota sembrada con PagoCuota.HistorialCompleto=false).');
        const failures = trackFailures(page);
        await gotoCreditoDetails(page);

        const toggle = page.locator(`[aria-controls="punitorio-panel-${CUOTA_HISTORIAL_INCOMPLETO}"]`);
        await expect(toggle).toHaveCount(1);
        const responsePromise = page.waitForResponse(response =>
            /\/Credito\/\d+\/Cuotas\/\d+\/Punitorio/.test(new URL(response.url()).pathname));
        await toggle.click();
        const response = await responsePromise;
        expect(response.status()).toBe(200);

        const panel = page.locator(`#punitorio-panel-${CUOTA_HISTORIAL_INCOMPLETO}`);
        await expect(panel.getByText('Historial incompleto', { exact: true })).toBeVisible();
        await expect(panel.getByText(/No reconstruible|Sin información suficiente/).first()).toBeVisible();
        expectNoFailures(failures);
    });

    test('error visible y accesible permite retry', async ({ page }) => {
        let failFirst = true;
        await page.route('**/Credito/*/Cuotas/*/Punitorio', async route => {
            if (failFirst) {
                failFirst = false;
                await route.fulfill({ status: 500, contentType: 'text/plain', body: 'error controlado' });
                return;
            }
            await route.continue();
        });
        await gotoCreditoDetails(page);

        const toggle = page.locator('[data-punitorio-toggle]').first();
        test.skip(!(await toggle.count()), 'El crédito no tiene cuotas.');
        await toggle.click();
        const panelId = await toggle.getAttribute('aria-controls');
        const panel = page.locator(`#${panelId}`);
        await expect(panel.getByRole('alert')).toContainText('No se pudo cargar');

        await panel.getByRole('button', { name: /Reintentar/ }).click();
        await expect(panel.getByText('Capital pendiente')).toBeVisible();
        await expect(toggle).toHaveAttribute('aria-expanded', 'true');
    });
});

test.describe('Crédito Details — operaciones PUN-ML9-C', () => {
    test.describe.configure({ retries: 0 });

    test.beforeEach(() => {
        test.skip(
            !OPERATION_CREDIT_ID || OPERATION_QUOTAS.length < 5,
            'Requiere crédito y cinco cuotas aisladas para las operaciones PUN-ML9-C.'
        );
    });

    test('aplica, recarga el historial, anula sin borrar y conserva la fila', async ({ page }) => {
        const failures = trackFailures(page);
        const cuotaId = OPERATION_QUOTAS[0];
        const { panel } = await openQuotaPanel(page, cuotaId);

        const aplicar = panel.locator('form[data-punitorio-operation="aplicar"]');
        await expect(aplicar).toBeVisible();
        await aplicar.locator('textarea[name="Acciones.Aplicar.Motivo"]')
            .fill('Aplicación desde Playwright PUN-ML9-C');
        await aplicar.locator('[data-punitorio-submit]').click();

        await expect(panel.locator('[data-punitorio-feedback]'))
            .toContainText(/Punitorio aplicado por/);
        const filaAplicacion = panel.locator('.punitorio-table--applications tbody tr').first();
        await expect(filaAplicacion.getByText('Aplicación desde Playwright PUN-ML9-C', { exact: true }))
            .toBeVisible();
        await expect(filaAplicacion.getByRole('cell', { name: /Aplicado pendiente/ })).toBeVisible();

        const anular = panel.locator('form[data-punitorio-operation="anular"]');
        await expect(anular).toBeVisible();
        await anular.locator('textarea[name="Acciones.Anulacion.Form.Motivo"]')
            .fill('Anulación desde Playwright PUN-ML9-C');
        await anular.locator('[data-punitorio-submit]').click();

        await expect(panel.locator('[data-punitorio-feedback]'))
            .toContainText(/fue anulada/);
        const filaAnulada = panel.locator('.punitorio-table--applications tbody tr').first();
        await expect(filaAnulada.getByRole('cell', { name: /Anulado/ })).toBeVisible();
        await expect(filaAnulada.getByText('Aplicación desde Playwright PUN-ML9-C', { exact: true }))
            .toBeVisible();
        await expect(filaAnulada.getByText('Anulación desde Playwright PUN-ML9-C', { exact: true }))
            .toBeVisible();
        await expect(panel.locator('form[data-punitorio-operation="anular"]')).toHaveCount(0);
        expectNoFailures(failures);
    });

    test('doble click emite un solo POST y crea una sola aplicación', async ({ page }) => {
        const failures = trackFailures(page);
        const cuotaId = OPERATION_QUOTAS[1];
        const { panel } = await openQuotaPanel(page, cuotaId);
        const form = panel.locator('form[data-punitorio-operation="aplicar"]');
        await form.locator('textarea[name="Acciones.Aplicar.Motivo"]')
            .fill('Doble envío Playwright');

        let posts = 0;
        page.on('request', request => {
            const path = new URL(request.url()).pathname;
            if (request.method() === 'POST' && path.endsWith(`/Cuotas/${cuotaId}/Punitorio/Aplicar`)) {
                posts += 1;
            }
        });
        await form.locator('[data-punitorio-submit]').evaluate(button => {
            button.click();
            button.click();
        });

        await expect(panel.locator('[data-punitorio-feedback]'))
            .toContainText(/Punitorio aplicado por/);
        expect(posts).toBe(1);
        const filas = panel.locator('.punitorio-table--applications tbody tr');
        await expect(filas).toHaveCount(1);
        await expect(filas.first().getByText('Doble envío Playwright', { exact: true })).toBeVisible();
        expectNoFailures(failures);
    });

    test('conflicto de RowVersion queda visible y fuerza recarga del panel', async ({ page }) => {
        const failures = trackFailures(page);
        const cuotaId = OPERATION_QUOTAS[2];
        const { panel } = await openQuotaPanel(page, cuotaId);
        const form = panel.locator('form[data-punitorio-operation="aplicar"]');
        const concurrente = await form.evaluate(element => ({
            action: element.action,
            token: element.querySelector('input[name="__RequestVerificationToken"]').value,
            rowVersion: element.querySelector('input[name="Acciones.Aplicar.CuotaRowVersionBase64"]').value
        }));
        const primera = await page.request.post(concurrente.action, {
            headers: { Accept: 'application/json', 'X-Requested-With': 'XMLHttpRequest' },
            form: {
                __RequestVerificationToken: concurrente.token,
                'Acciones.Aplicar.Motivo': 'Aplicación concurrente Playwright',
                'Acciones.Aplicar.CuotaRowVersionBase64': concurrente.rowVersion
            }
        });
        expect(primera.status()).toBe(200);

        await form.locator('textarea[name="Acciones.Aplicar.Motivo"]')
            .fill('Intento con RowVersion vencida');
        const conflicto = page.waitForResponse(response =>
            response.status() === 409 &&
            new URL(response.url()).pathname.endsWith(`/Cuotas/${cuotaId}/Punitorio/Aplicar`));
        await form.locator('[data-punitorio-submit]').click();
        await conflicto;

        await expect(panel.locator('[data-punitorio-feedback].is-error')).toBeVisible();
        await expect(panel.locator('[data-punitorio-feedback]'))
            .toContainText(/Ya existe|cambió|cruzó|Recarg/);
        await expect(
            panel.locator('.punitorio-table--applications tbody tr').first()
                .getByText('Aplicación concurrente Playwright', { exact: true })
        )
            .toBeVisible();
        await expect(panel.locator('form[data-punitorio-operation="aplicar"]')).toHaveCount(0);
        expectNoFailures(failures, [/status of 409 \(Conflict\)/]);
    });

    test('motivo obligatorio evita el POST, informa error y conserva foco', async ({ page }) => {
        const failures = trackFailures(page);
        const cuotaId = OPERATION_QUOTAS[3];
        const { panel } = await openQuotaPanel(page, cuotaId);
        const form = panel.locator('form[data-punitorio-operation="aplicar"]');
        const motivo = form.locator('textarea[name="Acciones.Aplicar.Motivo"]');
        let posts = 0;
        page.on('request', request => {
            if (request.method() === 'POST' &&
                new URL(request.url()).pathname.endsWith(`/Cuotas/${cuotaId}/Punitorio/Aplicar`)) {
                posts += 1;
            }
        });

        await motivo.fill('   ');
        await form.locator('[data-punitorio-submit]').click();
        await expect(motivo).toBeFocused();
        await expect(motivo).toHaveAttribute('aria-invalid', 'true');
        await expect(panel.locator('[data-punitorio-feedback]')).toContainText('El motivo es obligatorio.');
        expect(posts).toBe(0);
        expectNoFailures(failures);
    });

    test('un 400 conserva motivo y devuelve el foco al campo', async ({ page }) => {
        const failures = trackFailures(page);
        const cuotaId = OPERATION_QUOTAS[4];
        await page.route(`**/Credito/${OPERATION_CREDIT_ID}/Cuotas/${cuotaId}/Punitorio/Aplicar`, route =>
            route.fulfill({
                status: 400,
                contentType: 'application/json',
                body: JSON.stringify({
                    success: false,
                    message: 'Validación controlada',
                    reloadPanel: false,
                    errors: { 'Acciones.Aplicar.Motivo': ['Revisá el motivo.'] }
                })
            }));
        const { panel } = await openQuotaPanel(page, cuotaId);
        const motivo = panel.locator('textarea[name="Acciones.Aplicar.Motivo"]');
        await motivo.fill('Este motivo debe conservarse');
        await panel.locator('form[data-punitorio-operation="aplicar"] [data-punitorio-submit]').click();

        await expect(motivo).toHaveValue('Este motivo debe conservarse');
        await expect(motivo).toBeFocused();
        await expect(panel.locator('[data-punitorio-feedback]')).toContainText('Validación controlada');
        expectNoFailures(failures, [/status of 400 \(Bad Request\)/]);
    });

    test('usuario sin permisos no ve acciones y el POST directo devuelve 403', async ({ browser }) => {
        const context = await browser.newContext({
            baseURL: BASE_URL,
            locale: 'es-AR',
            storageState: { cookies: [], origins: [] }
        });
        const page = await context.newPage();
        try {
            const failures = trackFailures(page);
            await loginWith(page, 'vendedor', 'Vendedor123!');
            const cuotaId = OPERATION_QUOTAS[4];
            const { panel } = await openQuotaPanel(page, cuotaId);
            await expect(panel.locator('[data-punitorio-operation-form]')).toHaveCount(0);
            await expect(panel.getByRole('heading', { name: 'Aplicar punitorio' })).toHaveCount(0);
            await expect(panel.getByRole('heading', { name: 'Anular aplicacion' })).toHaveCount(0);

            const token = await page.locator('input[name="__RequestVerificationToken"]').first().inputValue();
            const response = await page.request.post(
                `/Credito/${OPERATION_CREDIT_ID}/Cuotas/${cuotaId}/Punitorio/Aplicar`,
                {
                    headers: { Accept: 'application/json', 'X-Requested-With': 'XMLHttpRequest' },
                    form: {
                        __RequestVerificationToken: token,
                        'Acciones.Aplicar.Motivo': 'Intento sin permiso',
                        'Acciones.Aplicar.CuotaRowVersionBase64': 'AAAAAAAAAAA='
                    }
                }
            );
            expect(response.status()).toBe(403);
            expectNoFailures(failures);
        } finally {
            await context.close();
        }
    });

    test('operación completa por teclado mantiene nombres y foco utilizables', async ({ page }) => {
        const cuotaId = OPERATION_QUOTAS[4];
        await gotoCreditoDetails(page);
        const toggle = page.locator(`[aria-controls="punitorio-panel-${cuotaId}"]`);
        await toggle.focus();
        await page.keyboard.press('Enter');
        const panel = page.locator(`#punitorio-panel-${cuotaId}`);
        await expect(panel.getByText('Capital pendiente')).toBeVisible();
        const motivo = panel.locator('textarea[name="Acciones.Aplicar.Motivo"]');
        await motivo.focus();
        await page.keyboard.type('Navegación completa por teclado');
        await page.keyboard.press('Tab');
        await expect(panel.locator('form[data-punitorio-operation="aplicar"] [data-punitorio-submit]'))
            .toBeFocused();
        await page.keyboard.press('Shift+Tab');
        await expect(motivo).toBeFocused();
    });
});

test.describe('Crédito Details — responsive y zoom', () => {
    for (const viewport of VIEWPORTS) {
        test(`panel sin overflow de página en ${viewport.name}`, async ({ page }) => {
            await page.setViewportSize({ width: viewport.width, height: viewport.height });
            const failures = trackFailures(page);
            await gotoCreditoDetails(page);

            const toggle = page.locator('[data-punitorio-toggle]').first();
            test.skip(!(await toggle.count()), 'El crédito no tiene cuotas.');
            await toggle.click();
            const panelId = await toggle.getAttribute('aria-controls');
            await expect(page.locator(`#${panelId}`).getByText('Capital pendiente')).toBeVisible();

            const pageHasOverflow = await page.evaluate(
                () => document.documentElement.scrollWidth > window.innerWidth
            );
            expect(pageHasOverflow).toBeFalsy();
            expectNoFailures(failures);
        });
    }

    test('zoom 200% mantiene el panel dentro de la página', async ({ page }) => {
        await page.setViewportSize({ width: 1366, height: 768 });
        const failures = trackFailures(page);
        await gotoCreditoDetails(page);
        const toggle = page.locator('[data-punitorio-toggle]').first();
        test.skip(!(await toggle.count()), 'El crédito no tiene cuotas.');
        await toggle.click();
        const panelId = await toggle.getAttribute('aria-controls');
        await expect(page.locator(`#${panelId}`).getByText('Capital pendiente')).toBeVisible();

        await page.evaluate(() => { document.documentElement.style.zoom = '200%'; });
        const pageHasOverflow = await page.evaluate(
            () => document.documentElement.scrollWidth > window.innerWidth
        );
        expect(pageHasOverflow).toBeFalsy();
        expectNoFailures(failures);
    });
});
