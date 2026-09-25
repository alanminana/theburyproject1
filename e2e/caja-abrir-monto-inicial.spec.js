// @ts-check
/**
 * Regresión staging 2026-09-23: el fondo inicial ingresado (30000) llegaba como 0.00 al servidor.
 * Causa: caja-abrir.js prellena el fondo con el efectivo del último cierre al elegir la caja, y esa
 * respuesta (0 en una caja sin cierres) pisaba el monto que el usuario ya había cargado.
 * Fix: el prefill solo aplica si el usuario no editó el monto.
 *
 * El endpoint UltimoEfectivoCierre se intercepta para no depender del estado de la DB; el POST
 * de Abrir se aborta tras capturar el body (no abre ninguna caja real).
 */
const { test, expect } = require('playwright/test');

test.use({ storageState: 'e2e/.auth/user.json' });

async function prepararForm(page, montoUltimoCierre) {
    await page.route('**/Caja/UltimoEfectivoCierre*', r =>
        r.fulfill({ contentType: 'application/json', body: JSON.stringify({ monto: montoUltimoCierre }) }));
    await page.goto('/Caja/Abrir', { waitUntil: 'domcontentloaded' });
    await page.evaluate(() => {
        const s = document.querySelector('[data-caja-abrir-select]');
        if (!s.querySelector('option[value="999999"]')) s.add(new Option('CAJA-TEST', '999999'));
    });
}

test('el monto cargado antes de elegir la caja no se pisa con el último cierre', async ({ page }) => {
    await prepararForm(page, 0);
    const monto = page.locator('[data-caja-abrir-monto]');
    await monto.fill('30000');
    const resp = page.waitForResponse('**/Caja/UltimoEfectivoCierre*');
    await page.selectOption('[data-caja-abrir-select]', '999999');
    await resp;
    await page.waitForTimeout(200);
    await expect(monto).toHaveValue('30000');

    let body = '';
    await page.route('**/Caja/Abrir', r => { body = r.request().postData() || ''; r.abort(); });
    await page.locator('form button[type=submit].btn-primary').click();
    await expect.poll(() => body).toContain('MontoInicial=30000');
});

test('sin monto cargado, elegir la caja sigue prellenando el último cierre', async ({ page }) => {
    await prepararForm(page, 12500);
    const resp = page.waitForResponse('**/Caja/UltimoEfectivoCierre*');
    await page.selectOption('[data-caja-abrir-select]', '999999');
    await resp;
    await expect(page.locator('[data-caja-abrir-monto]')).toHaveValue('12500');
});

test('el atajo $30.000 tampoco se pisa al cambiar de caja', async ({ page }) => {
    await prepararForm(page, 0);
    await page.getByRole('button', { name: '$ 30.000' }).click();
    const resp = page.waitForResponse('**/Caja/UltimoEfectivoCierre*');
    await page.selectOption('[data-caja-abrir-select]', '999999');
    await resp;
    await page.waitForTimeout(200);
    await expect(page.locator('[data-caja-abrir-monto]')).toHaveValue('30000');
});
