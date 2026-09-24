// @ts-check
/**
 * Regresión BLOCKER staging 2026-09-23: `accionConfirmacion` no viajaba en el POST final de
 * Venta/Edit sin importar qué botón lo disparara ("Confirmar venta" simple o "Confirmar y
 * facturar" del modal). El servidor caía siempre al branch `default` de
 * ProcesarAccionPostGuardadoAsync ("Venta actualizada exitosamente", sin cambiar Estado) —
 * ninguna venta podía confirmarse, facturarse ni descontar stock desde el wizard.
 *
 * Causa real: venta-create.js deshabilitaba TODOS los button[type="submit"] del form dentro de
 * su propio handler de "submit" (guarda anti-doble-envío), incluido el botón que disparó ese
 * mismo submit (event.submitter). Deshabilitar el submitter dentro del handler de "submit" hace
 * que el navegador lo excluya del form-data al serializar la petición real — el campo
 * `name="accionConfirmacion"` de ese botón nunca llegaba al servidor.
 *
 * Fix: venta-create.js excluye a e.submitter de la deshabilitación.
 *
 * Este spec intercepta el POST real a /Venta/Edit/{id} y verifica que el body incluya
 * accionConfirmacion, y que el servidor efectivamente confirme la venta (no el branch
 * default de "sólo guardado").
 */
const { test, expect } = require('playwright/test');
const {
    searchAndSelectClient, activarFiltroStock, addProduct,
    ensureVendedorSeleccionado, TIPO_PAGO,
} = require('./helpers');

test.use({ storageState: 'e2e/.auth/user.json' });

async function crearVentaBorrador(page) {
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

    // Create no tiene accionConfirmacion: "Confirmar Transacción" crea la venta directo
    // en Presupuesto (nunca confirmada desde acá), que es justo el estado de partida que
    // necesitamos para reproducir el POST de Edit.
    await page.locator('#btn-confirmar').click();
    await page.waitForURL(/\/Venta\/(Details|Edit)\/\d+/, { timeout: 20_000 });
    const id = page.url().match(/\/(?:Details|Edit)\/(\d+)/)?.[1];
    expect(id).toBeTruthy();
    return id;
}

test('Venta/Edit "Confirmar venta" envía accionConfirmacion y la venta queda confirmada', async ({ page }) => {
    const id = await crearVentaBorrador(page);

    await page.goto(`/Venta/Edit/${id}`, { waitUntil: 'domcontentloaded' });
    await page.locator('[data-step="revision"]').click();

    const postRequest = page.waitForRequest((req) =>
        req.url().includes(`/Venta/Edit/${id}`) && req.method() === 'POST');

    await page.locator('#btn-confirmar').click();

    const request = await postRequest;
    const body = request.postData() || '';
    // El bug real: este campo faltaba en el body sin importar qué botón se clickeara.
    expect(body).toMatch(/accionConfirmacion=confirmar(?!-)/);

    await page.waitForURL(/\/Venta\/Details\/\d+/, { timeout: 20_000 });
    // exact:true: la página muestra toast + título + descripción con este texto (strict mode).
    await expect(page.getByText('Venta confirmada', { exact: true })).toBeVisible({ timeout: 10_000 });

    // Idempotencia: una venta ya Confirmada no es editable (ValidarEstadoParaEdicion),
    // así que no hay forma de volver a disparar accionConfirmacion=confirmar sobre ella
    // ni de descontar stock una segunda vez por este camino.
    await page.goto(`/Venta/Edit/${id}`, { waitUntil: 'domcontentloaded' });
    await page.waitForURL(/\/Venta\/Details\/\d+/, { timeout: 10_000 });
});
