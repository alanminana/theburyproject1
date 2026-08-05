// @ts-check
/**
 * PUN-ML9-D.1 (riesgo 3) — returnUrl en `/Credito/PagarCuota/{cuotaId}`.
 *
 * Contratos que este spec protege en navegador real:
 *  - GET recibe returnUrl y lo conserva en el formulario y en el enlace "Volver";
 *  - una URL externa nunca se refleja: se ignora y usa el fallback canónico (Details);
 *  - al confirmar el pago, el enlace "Volver al crédito" sigue apuntando al origen válido;
 *  - un rechazo 400 (rowversion inválido) conserva returnUrl en el formulario re-renderizado;
 *  - los 3 puntos de entrada reales (Details, CuotasVencidas/Dashboard, Venta) enlazan con
 *    returnUrl (contrato de markup verificado en TheBuryProyect.Tests/Unit/*UiContractTests.cs;
 *    acá se ejercita en navegador el resultado real de esos asp-route-returnUrl).
 *
 * Requiere la app corriendo contra una base descartable con un crédito activo con 1 cuota
 * pendiente y caja abierta. Configurable por entorno:
 *   E2E_BASE_URL, E2E_RETURNURL_CREDITO, E2E_RETURNURL_CUOTA
 */
const { test, expect } = require('playwright/test');

const CREDITO_ID = Number(process.env.E2E_RETURNURL_CREDITO || 1);
const CUOTA_ID = Number(process.env.E2E_RETURNURL_CUOTA || 1);

const CAJERO = { user: 'cajero', pass: 'Cajero123!' };

test.use({ storageState: { cookies: [], origins: [] } });

async function login(page, { user, pass }) {
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

    await expect(page).not.toHaveURL(/[Ll]ogin/, { timeout: 15_000 });
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

function expectNoFailures(failures) {
    expect(failures.consoleErrors, `consola: ${failures.consoleErrors.join(' | ')}`).toHaveLength(0);
    expect(failures.serverErrors, `HTTP 5xx: ${failures.serverErrors.join(' | ')}`).toHaveLength(0);
}

const urlPago = (cuotaId, returnUrl) =>
    `/Credito/PagarCuota/${cuotaId}${returnUrl ? `?returnUrl=${encodeURIComponent(returnUrl)}` : ''}`;
const detailsHref = creditoId => `/Credito/Details/${creditoId}`;

test.describe('PUN-ML9-D.1 — returnUrl', () => {
    test.beforeEach(async ({ page }) => { await login(page, CAJERO); });

    test('ingresar desde Details real: el enlace "Pagar cuota" ya lleva returnUrl', async ({ page }) => {
        const failures = trackFailures(page);
        await page.goto(detailsHref(CREDITO_ID), { waitUntil: 'domcontentloaded' });

        const link = page.locator('a').filter({ hasText: /Pagar cuota/i }).first();
        const href = await link.getAttribute('href');
        expect(href).toContain(`/Credito/PagarCuota/${CUOTA_ID}`);
        expect(href).toContain(`returnUrl=${encodeURIComponent(detailsHref(CREDITO_ID))}`);

        await link.click();
        await expect(page.locator('[data-credito-pago]')).toBeVisible();
        await expect(page.locator('a[href="' + detailsHref(CREDITO_ID) + '"]').first()).toBeVisible();

        expectNoFailures(failures);
    });

    test('returnUrl local se conserva en el formulario y al volver', async ({ page }) => {
        const failures = trackFailures(page);
        const origen = detailsHref(CREDITO_ID);

        await page.goto(urlPago(CUOTA_ID, origen), { waitUntil: 'domcontentloaded' });
        await expect(page.locator('input[name="returnUrl"]')).toHaveValue(origen);
        await expect(page.locator('a[href="' + origen + '"]').first()).toBeVisible();

        await page.locator('a[href="' + origen + '"]').first().click();
        await expect(page).toHaveURL(new RegExp(origen.replace(/\//g, '\\/') + '$'));

        expectNoFailures(failures);
    });

    test('returnUrl externa nunca se refleja: cae al fallback canónico (Details)', async ({ page }) => {
        const failures = trackFailures(page);
        const externa = 'https://evil.example.com/robar';

        await page.goto(urlPago(CUOTA_ID, externa), { waitUntil: 'domcontentloaded' });

        await expect(page.locator('input[name="returnUrl"]')).toHaveCount(0);
        const html = await page.content();
        expect(html).not.toContain('evil.example.com');
        await expect(page.locator('a[href="' + detailsHref(CREDITO_ID) + '"]').first()).toBeVisible();

        expectNoFailures(failures);
    });

    test('un rowversion inválido (400) conserva returnUrl en el formulario', async ({ page }) => {
        const failures = trackFailures(page);
        const origen = '/Credito/CuotasVencidas';
        await page.goto(urlPago(CUOTA_ID, origen), { waitUntil: 'domcontentloaded' });

        const token = await page.locator('input[name="__RequestVerificationToken"]').first().inputValue();
        const response = await page.request.post(urlPago(CUOTA_ID), {
            form: {
                __RequestVerificationToken: token,
                'Input.MontoIngresado': '10',
                'Input.MedioPago': 'Efectivo',
                'Input.CuotaRowVersionBase64': 'no-es-base64',
                returnUrl: origen,
            },
            maxRedirects: 0,
        });
        expect(response.status()).toBe(400);
        const body = await response.text();
        expect(body).toContain(`name="returnUrl" value="${origen}"`);

        expectNoFailures(failures);
    });

    test('confirmar el pago con returnUrl local: el recibo sigue apuntando al origen', async ({ page }) => {
        const failures = trackFailures(page);
        const origen = detailsHref(CREDITO_ID);
        await page.goto(urlPago(CUOTA_ID, origen), { waitUntil: 'domcontentloaded' });

        await page.fill('[data-pago-monto]', '1');
        await page.locator('[data-pago-monto]').blur();
        await expect(page.locator('[data-credito-pago-form]'))
            .toHaveAttribute('data-preview-valid', 'true', { timeout: 15_000 });

        await page.locator('[data-pago-confirmar]').click();
        await expect(page.locator('[data-pago-resultado]')).toBeVisible({ timeout: 20_000 });

        // returnUrl sobrevivió al POST + redirect + GET: el recibo linkea de vuelta al origen real.
        await expect(page).toHaveURL(new RegExp(`returnUrl=${encodeURIComponent(origen)}`.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')));
        await expect(page.locator('a[href="' + origen + '"]').first()).toBeVisible();

        await page.locator('a[href="' + origen + '"]').first().click();
        await expect(page.locator('[data-credito-details]')).toBeVisible();

        expectNoFailures(failures);
    });
});
