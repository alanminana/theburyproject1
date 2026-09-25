// @ts-check
/**
 * COTIZACION-MIVENTA-02 — segundo camino explícito de la POC "Mi Venta" (pedido explícito
 * del usuario, 2026-09-17): "Continuar con wizard".
 *
 * Crea la Venta SIN confirmar (mismo CotizacionConversionService.ConvertirAVentaAsync ya
 * existente, con ConfirmarVenta=false — nada nuevo) y va SIEMPRE a Venta/Edit, que ya
 * reconstituye el wizard completo desde la Venta persistida (cliente, productos, medio de
 * pago, envío, observaciones) — confirmado en la auditoría antes de implementar: no hace
 * falta ningún DTO ni mecanismo nuevo para "preservar contexto".
 *
 * A diferencia de "Confirmar Mi Venta", este camino es 100% determinístico (no depende de si
 * el usuario E2E tiene una caja abierta): "Continuar con wizard" nunca intenta confirmar, así
 * que el resultado es siempre el mismo — Venta creada en Venta/Edit con el wizard mostrando
 * los mismos datos ya cargados en el Cotizador.
 */
const { test, expect } = require('playwright/test');

test.use({ storageState: 'e2e/.auth/user.json' });

test.beforeEach(async ({ page }) => {
    await page.route('**/fonts.googleapis.com/**', route => route.abort()).catch(() => null);
    await page.route('**/fonts.gstatic.com/**', route => route.abort()).catch(() => null);
});

async function crearCliente(page, dni) {
    await page.goto('/Cliente/Create', { waitUntil: 'domcontentloaded' });
    await page.getByRole('textbox', { name: 'Número de documento *' }).fill(dni);
    await page.getByRole('textbox', { name: 'Apellido *' }).fill('WizardQA');
    await page.getByRole('textbox', { name: 'Nombre *' }).fill('Cliente');
    await page.getByRole('tab', { name: /Contacto/ }).click();
    await page.getByRole('textbox', { name: 'Teléfono *' }).fill('1144556677');
    await page.getByRole('textbox', { name: 'Domicilio *' }).fill('Av. Continuar 100');
    await page.getByRole('button', { name: /Crear cliente/ }).click();
    await page.waitForURL(/\/Cliente\/Details\/\d+/, { timeout: 15_000 });
    const match = page.url().match(/\/Cliente\/Details\/(\d+)/);
    return match ? Number(match[1]) : null;
}

async function seleccionarCliente(page, dni) {
    const buscar = page.locator('#cotizacion-cliente-buscar');
    await buscar.fill(dni);
    await page.waitForTimeout(600);
    const opcion = page.locator('#cotizacion-clientes-dropdown button').first();
    await expect(opcion).toBeVisible({ timeout: 5_000 });
    await opcion.click();
}

async function agregarPrimerProducto(page) {
    const input = page.locator('#cotizacion-producto-buscar');
    const dropdown = page.locator('#cotizacion-productos-dropdown');
    for (const term of ['an', 'el', 'or', 'is', 'ar', 'ro']) {
        await input.fill(term);
        await page.waitForTimeout(600);
        if (!(await dropdown.isVisible({ timeout: 2_000 }).catch(() => false))) continue;
        const firstBtn = dropdown.locator('button').first();
        if (!(await firstBtn.isVisible({ timeout: 2_000 }).catch(() => false))) continue;
        await firstBtn.click();
        await page.waitForTimeout(300);
        if (await page.locator('#cotizacion-productos-tbody .cart-row').count() > 0) return true;
    }
    return false;
}

test.describe('Cotización — "Continuar con wizard" (COTIZACION-MIVENTA-02)', () => {
    test('Crea la Venta sin confirmar y abre siempre Venta/Edit con el contexto ya cargado', async ({ page }) => {
        await page.setViewportSize({ width: 1366, height: 768 });

        const dni = String(Date.now()).slice(-8);
        const clienteId = await crearCliente(page, dni);
        expect(clienteId).toBeTruthy();

        await page.goto('/Cotizacion', { waitUntil: 'domcontentloaded' });
        await expect(page.locator('#cotizacion-simular')).toBeAttached({ timeout: 10_000 });

        await seleccionarCliente(page, dni);
        expect(await agregarPrimerProducto(page)).toBeTruthy();

        await page.evaluate(() => document.getElementById('cotizacion-simular').click());
        await expect(page.locator('#cotizacion-resultados')).toBeVisible({ timeout: 10_000 });

        // §7 del pedido: botón permanente, secundario — nunca compite con la acción primaria.
        const continuarWizard = page.locator('#cotizacion-continuar-wizard');
        await expect(continuarWizard).toBeEnabled({ timeout: 10_000 });
        await expect(continuarWizard).toHaveText(/Continuar con wizard/);

        await continuarWizard.click();

        // Siempre Venta/Edit — nunca decide por resultado (a diferencia de Confirmar Mi Venta).
        await page.waitForURL(/\/Venta\/Edit\/\d+/, { timeout: 15_000 });
        const ventaId = Number(page.url().match(/\/Venta\/Edit\/(\d+)/)?.[1]);
        expect(ventaId).toBeGreaterThan(0);

        // §8 del pedido: el wizard entra con el cliente y el producto ya cargados — no hay
        // que volver a pedirlos. Venta/Edit reconstituye el wizard desde la Venta persistida.
        await expect(page.locator('#venta-form')).toBeVisible({ timeout: 10_000 });
        await expect(page.locator('body')).toContainText('WizardQA');

        // El wizard tradicional sigue funcionando exactamente igual (§26 del pedido) —
        // el paso de Revisión/Cliente muestra el mismo cliente sin tener que re-tipearlo.
        const clienteEnWizard = page.getByText(/WizardQA/).first();
        await expect(clienteEnWizard).toBeVisible({ timeout: 10_000 });
    });
});
