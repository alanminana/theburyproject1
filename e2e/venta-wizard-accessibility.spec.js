// @ts-check
const { test, expect } = require('playwright/test');
const { searchAndSelectClient } = require('./helpers');

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
            // El paso inicial de Create es Cotizar; Cliente queda a un clic.
            await expect(page.locator('#step-panel-cotizar')).toBeVisible();
            await page.locator('#step-btn-cliente').click();
            await expect(page.locator('#step-panel-cliente')).toBeVisible();

            const hasHorizontalOverflow = await page.evaluate(
                () => document.documentElement.scrollWidth > window.innerWidth
            );
            expect(hasHorizontalOverflow).toBeFalsy();
        });
    }

    test('la tab activa usa roving tabindex y el modal declara semántica de diálogo', async ({ page }) => {
        await page.goto('/Venta/Create', { waitUntil: 'domcontentloaded' });

        const selectedTab = page.locator('#venta-form [role="tab"][aria-selected="true"]');
        await expect(selectedTab).toHaveCount(1);
        await expect(selectedTab).toHaveAttribute('tabindex', '0');
        await expect(page.locator('#modal-documentacion')).toHaveAttribute('role', 'dialog');
        await expect(page.locator('#modal-documentacion')).toHaveAttribute('aria-modal', 'true');
    });

    test('el tablist mueve foco con flechas, Home y End sin usar las acciones principales', async ({ page }) => {
        await page.goto('/Venta/Create', { waitUntil: 'domcontentloaded' });
        const cliente = page.locator('#step-btn-cliente');
        const productos = page.locator('#step-btn-productos');
        // Create abre en Cotizar: hay que mostrar el panel Cliente para usar su buscador.
        await cliente.click();
        await expect(page.locator('#step-panel-cliente')).toBeVisible();
        const selectedClient = await searchAndSelectClient(page);
        test.skip(!selectedClient, 'El entorno no expone cliente de QA para habilitar el segundo paso.');

        await cliente.focus();
        await page.keyboard.press('ArrowRight');
        await expect(productos).toBeFocused();
        await expect(productos).toHaveAttribute('aria-selected', 'true');
        await expect(cliente).toHaveAttribute('tabindex', '-1');
        await expect(productos).toHaveAttribute('tabindex', '0');

        await page.keyboard.press('ArrowLeft');
        await expect(cliente).toBeFocused();
        await page.keyboard.press('End');
        await expect(productos).toBeFocused();
        // Home va al primer paso del wizard, que en Create es Cotizar.
        await page.keyboard.press('Home');
        await expect(page.locator('#step-btn-cotizar')).toBeFocused();
    });
});
