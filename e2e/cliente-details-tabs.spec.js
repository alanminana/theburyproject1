// @ts-check
/**
 * E2E mínimo de la reorganización en solapas de Cliente/Details (2026-09-14):
 * resumen ejecutivo + alerta principal fijos arriba, resto de la ficha en 4
 * solapas fijas (Resumen/Crédito/Documentación/Datos; Historial se fusionó en Crédito). No depende de
 * datos sembrados especiales — usa el primer cliente real de /Cliente, igual
 * que el bloque "responsive, zoom y teclado" de cliente-aptitud-punitorio.spec.js.
 */
const { test, expect } = require('playwright/test');

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

async function primerClienteHref(page) {
    await page.goto('/Cliente', { waitUntil: 'domcontentloaded' });
    const primerCliente = page.locator('a[href*="/Cliente/Details/"]').first();
    const tieneCliente = await primerCliente.count();
    if (!tieneCliente) return null;
    return primerCliente.getAttribute('href');
}

test.describe('Cliente Details — solapas (Resumen/Crédito/Documentación/Datos)', () => {
    test('abre en Resumen por defecto, con las 4 solapas presentes', async ({ page }) => {
        const href = await primerClienteHref(page);
        test.skip(!href, 'No hay clientes cargados en esta base para ejercitar el spec.');

        const failures = trackFailures(page);
        await page.goto(href, { waitUntil: 'domcontentloaded' });

        const tabs = page.getByRole('tab');
        await expect(tabs).toHaveCount(4);

        const resumenTab = page.getByRole('tab', { name: /Resumen/ });
        await expect(resumenTab).toHaveAttribute('aria-selected', 'true');
        await expect(page.locator('#panel-resumen')).toBeVisible();
        await expect(page.locator('#panel-credito')).toBeHidden();
        expectNoFailures(failures);
    });

    test('cambia a Crédito, Documentación y Datos mostrando el panel correspondiente', async ({ page }) => {
        const href = await primerClienteHref(page);
        test.skip(!href, 'No hay clientes cargados en esta base para ejercitar el spec.');

        const failures = trackFailures(page);
        await page.goto(href, { waitUntil: 'domcontentloaded' });

        await page.getByRole('tab', { name: /Crédito/ }).click();
        await expect(page.locator('#panel-credito')).toBeVisible();
        await expect(page.locator('#panel-resumen')).toBeHidden();
        await expect(page.getByText('Últimos créditos del cliente')).toBeVisible();

        await page.getByRole('tab', { name: /Documentación/ }).click();
        await expect(page.locator('#panel-documentacion')).toBeVisible();
        await expect(page.locator('#panel-credito')).toBeHidden();

        await page.getByRole('tab', { name: /Datos/ }).click();
        await expect(page.locator('#panel-datos')).toBeVisible();
        await expect(page.getByText('Datos personales')).toBeVisible();
        await expect(page.getByText('Zona sensible')).toBeVisible();

        expectNoFailures(failures);
    });

    test('persistencia de solapa: recargar con #credito en la URL abre Crédito directamente', async ({ page }) => {
        const href = await primerClienteHref(page);
        test.skip(!href, 'No hay clientes cargados en esta base para ejercitar el spec.');

        const failures = trackFailures(page);
        await page.goto(href, { waitUntil: 'domcontentloaded' });

        await page.getByRole('tab', { name: /Crédito/ }).click();
        await expect(page).toHaveURL(/#credito$/);

        await page.reload({ waitUntil: 'domcontentloaded' });
        await expect(page.getByRole('tab', { name: /Crédito/ })).toHaveAttribute('aria-selected', 'true');
        await expect(page.locator('#panel-credito')).toBeVisible();
        await expect(page.locator('#panel-resumen')).toBeHidden();

        expectNoFailures(failures);
    });

    test('un hash de solapa invalido cae a Resumen en vez de una pantalla en blanco', async ({ page }) => {
        const href = await primerClienteHref(page);
        test.skip(!href, 'No hay clientes cargados en esta base para ejercitar el spec.');

        const failures = trackFailures(page);
        await page.goto(href + '#no-existe', { waitUntil: 'domcontentloaded' });

        await expect(page.getByRole('tab', { name: /Resumen/ })).toHaveAttribute('aria-selected', 'true');
        await expect(page.locator('#panel-resumen')).toBeVisible();

        expectNoFailures(failures);
    });

    test('un #historial guardado (solapa fusionada) abre Crédito, no Resumen', async ({ page }) => {
        const href = await primerClienteHref(page);
        test.skip(!href, 'No hay clientes cargados en esta base para ejercitar el spec.');

        const failures = trackFailures(page);
        await page.goto(href + '#historial', { waitUntil: 'domcontentloaded' });

        await expect(page.getByRole('tab', { name: /Crédito/ })).toHaveAttribute('aria-selected', 'true');
        await expect(page.locator('#panel-credito')).toBeVisible();
        await expect(page.getByRole('tab', { name: /Historial/ })).toHaveCount(0);

        expectNoFailures(failures);
    });

    test('navegación por teclado: ArrowRight/Home/End mueven foco y activan la solapa', async ({ page }) => {
        const href = await primerClienteHref(page);
        test.skip(!href, 'No hay clientes cargados en esta base para ejercitar el spec.');

        await page.goto(href, { waitUntil: 'domcontentloaded' });

        const resumenTab = page.getByRole('tab', { name: /Resumen/ });
        await resumenTab.focus();

        await page.keyboard.press('ArrowRight');
        await expect(page.getByRole('tab', { name: /Crédito/ })).toBeFocused();
        await expect(page.getByRole('tab', { name: /Crédito/ })).toHaveAttribute('aria-selected', 'true');
        await expect(page.locator('#panel-credito')).toBeVisible();

        await page.keyboard.press('End');
        await expect(page.getByRole('tab', { name: /Datos/ })).toBeFocused();
        await expect(page.locator('#panel-datos')).toBeVisible();

        await page.keyboard.press('Home');
        await expect(page.getByRole('tab', { name: /Resumen/ })).toBeFocused();
        await expect(page.locator('#panel-resumen')).toBeVisible();
    });
});
