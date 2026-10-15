// @ts-check
const { test, expect } = require('playwright/test');
const { searchAndSelectClient, irAlPaso, CTA_PRIMARIO_VISIBLE } = require('./helpers');

const VIEWPORTS = [
    { name: '360x800', width: 360, height: 800 },
    { name: '390x844', width: 390, height: 844 },
    { name: '768x1024', width: 768, height: 1024 },
    { name: '1080x720', width: 1080, height: 720 },
    { name: '1280x720', width: 1280, height: 720 },
    { name: '1366x768', width: 1366, height: 768 },
    { name: '1600x900', width: 1600, height: 900 },
    { name: '1920x1080', width: 1920, height: 1080 },
    { name: '1920x1200', width: 1920, height: 1200 },
];

test.use({ storageState: 'e2e/.auth/user.json' });

test.describe('Venta/Create — accesibilidad y responsive', () => {
    for (const viewport of VIEWPORTS) {
        test(`abre el wizard sin overflow en ${viewport.name}`, async ({ page }) => {
            await page.setViewportSize({ width: viewport.width, height: viewport.height });
            await page.goto('/Venta/Create', { waitUntil: 'domcontentloaded' });

            await expect(page).not.toHaveURL(/Identity\/Account\/Login/);
            await expect(page.locator('#venta-form')).toBeVisible();
            // El paso inicial de Create es Cotizar; Cliente queda a un clic de "Siguiente: Cliente".
            await expect(page.locator('#step-panel-cotizar')).toBeVisible();
            await expect(page.locator('.vm-hero__steps')).toBeHidden();
            await expect(page.getByRole('button', { name: /Siguiente: Cliente/ })).toBeVisible();
            await irAlPaso(page, 'cliente');
            await expect(page.locator('#step-panel-cliente')).toBeVisible();
            // En Cliente ya hay "Paso anterior" y un CTA para seguir, ambos utilizables.
            await expect(page.getByRole('button', { name: /Paso anterior/ })).toBeVisible();
            await expect(page.locator(CTA_PRIMARIO_VISIBLE).first()).toBeVisible();

            const hasHorizontalOverflow = await page.evaluate(
                () => document.documentElement.scrollWidth > window.innerWidth
            );
            expect(hasHorizontalOverflow).toBeFalsy();
        });
    }

    test('el estado de los pasos sigue sincronizado (roving tabindex) y el modal declara semántica de diálogo', async ({ page }) => {
        await page.goto('/Venta/Create', { waitUntil: 'domcontentloaded' });

        const selectedTab = page.locator('#venta-form [role="tab"][aria-selected="true"]');
        await expect(selectedTab).toHaveCount(1);
        await expect(selectedTab).toHaveAttribute('tabindex', '0');
        await expect(page.locator('#modal-documentacion')).toHaveAttribute('role', 'dialog');
        await expect(page.locator('#modal-documentacion')).toHaveAttribute('aria-modal', 'true');
    });

    test('el teclado recorre Cotizar, Cliente y Productos con los controles visibles', async ({ page }) => {
        await page.goto('/Venta/Create', { waitUntil: 'domcontentloaded' });
        await expect(page.locator('#venta-form')).toBeVisible();

        // Cotizar -> Cliente con el teclado; el foco acompaña al panel.
        await page.getByRole('button', { name: /Siguiente: Cliente/ }).focus();
        await page.keyboard.press('Enter');
        await expect(page.locator('#step-panel-cliente')).toBeVisible();
        await expect(page.locator('#step-panel-cliente').locator('h2, h3').first()).toBeFocused();

        const selectedClient = await searchAndSelectClient(page);
        test.skip(!selectedClient, 'El entorno no expone cliente de QA para habilitar el segundo paso.');

        // Cliente -> Productos con el CTA del paso (Enter), ya con cliente seleccionado.
        await page.locator(CTA_PRIMARIO_VISIBLE).first().focus();
        await page.keyboard.press('Enter');
        await expect(page.locator('#step-panel-productos')).toBeVisible();
        await expect(page.locator('#step-panel-productos').locator('h2, h3').first()).toBeFocused();

        // Productos -> Cliente -> Cotizar con "Paso anterior" por teclado, conservando el cliente elegido.
        await page.getByRole('button', { name: /Paso anterior/ }).focus();
        await page.keyboard.press('Enter');
        await expect(page.locator('#step-panel-cliente')).toBeVisible();
        await expect(page.locator('#hdn-cliente-id')).not.toHaveValue('');
        await page.getByRole('button', { name: /Paso anterior/ }).focus();
        await page.keyboard.press('Enter');
        await expect(page.locator('#step-panel-cotizar')).toBeVisible();
        await expect(page.locator('[data-wizard-back]')).toBeHidden();
    });
});
