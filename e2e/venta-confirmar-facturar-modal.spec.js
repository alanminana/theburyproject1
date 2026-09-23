// @ts-check
/**
 * Regresión: el checkbox "Facturar al confirmar" (paso Revisión, Venta/Edit) abre el modal
 * "Confirmar y facturar" en vez de enviar el form directo. Bug reportado en staging
 * (2026-09-23): venta-page-wizard.js está registrado ANTES que venta-create.js y cancela el
 * submit con event.preventDefault() para abrir el modal — pero preventDefault() no detiene a
 * los demás listeners del mismo evento, así que el submit handler de venta-create.js seguía
 * de largo y deshabilitaba TODOS los button[type="submit"] del form (guarda anti-doble-envío),
 * incluido el propio botón del modal recién abierto, que quedaba inutilizable para siempre.
 *
 * Fix: venta-create.js ahora respeta `e.defaultPrevented` al principio de su handler de
 * submit — si otro listener ya canceló este submit puntual, no corre su lógica de "envío
 * final" (incluida la que deshabilita los botones).
 *
 * Nota: verificado en vivo contra el staging de esta sesión con Playwright MCP (no vía este
 * spec, que no se ejecutó en esta sesión — el storageState de e2e/.auth/user.json apunta a
 * otro entorno). Queda un segundo hallazgo relacionado, no resuelto en esta sesión: el campo
 * accionConfirmacion=confirmar-facturar no viaja en el POST final (el backend cae al branch
 * default y sólo guarda, sin confirmar ni facturar) — éste spec no lo cubre todavía.
 */
const { test, expect } = require('playwright/test');
const {
    searchAndSelectClient, activarFiltroStock, addProduct,
    ensureVendedorSeleccionado, TIPO_PAGO,
} = require('./helpers');

test.use({ storageState: 'e2e/.auth/user.json' });

test('checkbox Facturar al confirmar + Confirmar venta deja el botón del modal habilitado', async ({ page }) => {
    await page.goto('/Venta/Create', { waitUntil: 'domcontentloaded' });
    await expect(page.locator('#venta-form')).toBeVisible({ timeout: 15_000 });
    await page.locator('#step-btn-cliente').click();

    const client = await searchAndSelectClient(page);
    test.skip(!client, 'El entorno no expone cliente de QA para el autocompletado.');

    await page.locator('#step-btn-productos').click();
    await activarFiltroStock(page);
    const product = await addProduct(page, 'an');
    test.skip(!product, 'El entorno no expone producto con stock para QA.');

    await page.locator('#step-btn-pago').click();
    await page.locator('#select-tipo-pago').selectOption(TIPO_PAGO.Efectivo);
    await page.locator('#step-btn-revision').click();
    await ensureVendedorSeleccionado(page);
    await page.locator('#btn-confirmar').click();
    await page.waitForURL(/\/Venta\/(Details|Edit)\/\d+/, { timeout: 20_000 });
    const id = page.url().match(/\/(?:Details|Edit)\/(\d+)/)?.[1];
    expect(id).toBeTruthy();

    await page.goto(`/Venta/Edit/${id}`, { waitUntil: 'domcontentloaded' });
    await page.locator('[data-step="revision"]').click();

    const chkFacturar = page.locator('#chk-facturar');
    test.skip(await chkFacturar.count() === 0, 'Sin permiso ventas.invoice en este entorno: el checkbox no se renderiza.');

    await chkFacturar.check();
    await page.locator('#btn-confirmar').click();

    const modalBtn = page.locator('#modal-confirmar-facturar button[name="accionConfirmacion"][value="confirmar-facturar"]');
    await expect(modalBtn).toBeVisible({ timeout: 5_000 });
    // El bug real: este botón quedaba [disabled] para siempre apenas se abría el modal.
    await expect(modalBtn).toBeEnabled();
});
