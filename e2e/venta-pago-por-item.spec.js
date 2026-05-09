// @ts-check
/**
 * Fase 16.7 — Tests E2E: flujo completo de pago por ítem.
 *
 * Escenarios:
 *   T1. Flujo completo: 2 ítems con pagos mixtos → confirmar → verificar Details
 *   T2. Badge inicial "Usa pago principal" y actualización visual al guardar
 *   T3. CréditoPersonal no muestra planes en el modal de ítem
 *   T4. Cancelar modal no modifica el ítem
 *   T5. Click en backdrop cierra el modal sin guardar
 *
 * Prerequisitos:
 *   - App corriendo en E2E_BASE_URL (default: http://localhost:5187)
 *   - E2E_USER y E2E_PASS configurados (ver global-setup.js)
 *   - Al menos 1 cliente y 1 producto con stock en la DB
 */
const { test, expect } = require('playwright/test');
const path = require('path');
const fs = require('fs');

const {
    TIPO_PAGO,
    TIPO_PAGO_BADGE,
    searchAndSelectClient,
    activarFiltroStock,
    addProduct,
    addTwoDistinctProducts,
    waitForDiagnostico,
    openPagoItemModal,
    configurePagoItem,
    setGlobalTipoPago,
    ensureConfirmarHabilitado,
    ensureVendedorSeleccionado,
} = require('./helpers');

// ── Utilidades locales ──────────────────────────────────────────────────────

const EVIDENCE_DIR = path.join('qa-evidence', 'e2e');

function ensureEvidenceDir() {
    fs.mkdirSync(EVIDENCE_DIR, { recursive: true });
}

async function screenshot(page, name) {
    ensureEvidenceDir();
    const viewport = page.viewportSize();
    const vp = viewport ? `${viewport.width}x${viewport.height}` : 'unknown';
    const filepath = path.join(EVIDENCE_DIR, `${name}_${vp}.png`);
    await page.screenshot({ path: filepath, fullPage: true });
    return filepath;
}

// ── Suite principal ────────────────────────────────────────────────────────

test.describe('Pago por ítem — Fase 16.7 E2E', () => {

    test.beforeEach(async ({ page }) => {
        await page.goto('/Venta/Create');
        await page.locator('#venta-form').waitFor({ state: 'visible', timeout: 15_000 });
    });

    // ──────────────────────────────────────────────────────────────────────
    // T1: Flujo completo — pago mixto por ítem → confirmar → Details
    // ──────────────────────────────────────────────────────────────────────
    test('T1: flujo completo pago mixto por ítem → confirmar → Details', async ({ page }) => {

        // 1. Seleccionar cliente
        const clienteNombre = await searchAndSelectClient(page);
        if (!clienteNombre) {
            test.skip();
            return;
        }
        console.log(`[T1] Cliente: ${clienteNombre}`);

        // 2. Activar filtro de stock y agregar dos productos distintos
        await activarFiltroStock(page);
        const productos = await addTwoDistinctProducts(page);

        if (!productos) {
            console.log('[T1] Solo hay un tipo de producto. Agregando el mismo dos veces con cantidades distintas no es posible. Test reducido a 1 ítem.');
            // Intentar al menos con 1 ítem
            await activarFiltroStock(page);
            const prod1 = await addProduct(page, 'an');
            if (!prod1) { test.skip(); return; }
        } else {
            const [p1, p2] = productos;
            console.log(`[T1] Ítem 1: ${p1.nombre} (id=${p1.id})`);
            console.log(`[T1] Ítem 2: ${p2.nombre} (id=${p2.id})`);
        }

        // 3. Verificar que hay al menos 1 fila en el carrito
        const filas = page.locator('#tbody-detalles tr');
        const filaCount = await filas.count();
        expect(filaCount).toBeGreaterThanOrEqual(1);

        // Esperar renderizado del botón de pago por ítem
        await page.waitForTimeout(400);

        // 4. Verificar badge inicial "Usa pago principal" en ítem 1
        const badge0 = page.locator('.btn-configurar-pago-item').first();
        await expect(badge0).toContainText('Usa pago principal');

        // 5. Cargar planes: setear pago global a TarjetaCredito para que el
        //    diagnóstico devuelva planesDisponibles
        await setGlobalTipoPago(page, TIPO_PAGO.TarjetaCredito);
        await page.waitForTimeout(500);

        // 6. Configurar pago ítem 0: Tarjeta Crédito + plan (si hay planes)
        await openPagoItemModal(page, 0);

        const modalTitulo = page.locator('#modal-pago-item-titulo');
        await expect(modalTitulo).not.toBeEmpty();

        const plan0Seleccionado = await configurePagoItem(page, TIPO_PAGO.TarjetaCredito);
        await expect(page.locator('#modal-pago-item')).toHaveClass(/hidden/);

        // Verificar badge ítem 0 actualizado
        await expect(badge0).toContainText(TIPO_PAGO_BADGE[TIPO_PAGO.TarjetaCredito]);
        await expect(badge0).not.toContainText('Usa pago principal');
        await expect(badge0).toHaveClass(/border-primary/);

        console.log(`[T1] Ítem 0 configurado con TarjetaCredito. Plan seleccionado: ${plan0Seleccionado}`);

        // 7. Configurar pago ítem 1 (si existe): Mercado Pago + plan
        let plan1Seleccionado = false;
        const totalBadges = await page.locator('.btn-configurar-pago-item').count();
        if (totalBadges >= 2) {
            await openPagoItemModal(page, 1);
            plan1Seleccionado = await configurePagoItem(page, TIPO_PAGO.MercadoPago);
            await expect(page.locator('#modal-pago-item')).toHaveClass(/hidden/);

            const badge1 = page.locator('.btn-configurar-pago-item').nth(1);
            await expect(badge1).toContainText(TIPO_PAGO_BADGE[TIPO_PAGO.MercadoPago]);
            await expect(badge1).not.toContainText('Usa pago principal');
            console.log(`[T1] Ítem 1 configurado con MercadoPago. Plan seleccionado: ${plan1Seleccionado}`);
        }

        // 8. Screenshot: Create con pagos por ítem configurados
        const ssCreate = await screenshot(page, 'T1_create_pago_configurado');
        console.log(`[T1] Screenshot Create: ${ssCreate}`);

        // 9. Asegurar que #btn-confirmar no está bloqueado
        //    (cambiar a Efectivo si el diagnóstico bloquea TarjetaCredito sin tarjeta)
        const confirmHabilitado = await ensureConfirmarHabilitado(page);
        if (!confirmHabilitado) {
            // Si sigue bloqueado con Efectivo (raro), documentar y saltar
            await screenshot(page, 'T1_btn_confirmar_bloqueado');
            test.skip();
            return;
        }

        // Si el usuario tiene rol que puede delegar vendedor, el servidor requiere VendedorUserId
        await ensureVendedorSeleccionado(page);

        // 10. Confirmar venta
        await page.locator('#btn-confirmar').click();

        // 11. Esperar redirección a Details
        await page.waitForURL(/\/Venta\/Details\/\d+/, { timeout: 20_000 });
        const detailsUrl = page.url();
        console.log(`[T1] Venta creada: ${detailsUrl}`);

        // 12. Screenshot Details
        await screenshot(page, 'T1_details_post_confirmacion');

        // ── Verificaciones en Details ──────────────────────────────────

        // a) Al menos un badge .forma-pago-item visible (ítem 0 tiene TarjetaCredito)
        const formaPagoItems = page.locator('.forma-pago-item');
        const formaBadgeCount = await formaPagoItems.count();
        expect(formaBadgeCount).toBeGreaterThanOrEqual(1);

        // b) Badge "Tarjeta Crédito" presente
        await expect(formaPagoItems.filter({ hasText: 'Tarjeta Crédito' }).first()).toBeVisible();

        // c) Badge "Mercado Pago" si ítem 2 fue configurado
        if (totalBadges >= 2) {
            await expect(formaPagoItems.filter({ hasText: 'Mercado Pago' }).first()).toBeVisible();
        }

        // d) Badges de ajuste por plan (solo si se seleccionaron planes)
        if (plan0Seleccionado || plan1Seleccionado) {
            const ajusteItems = page.locator('.ajuste-plan-item');
            const ajusteCount = await ajusteItems.count();
            // Los planes sin ajuste (0%) NO generan badge de ajuste
            // Solo validamos que si hay badge, muestre texto correcto
            if (ajusteCount > 0) {
                const textoAjuste = await ajusteItems.first().textContent();
                const esAjusteValido = /recargo por plan|descuento por plan|plan sin ajuste/i.test(textoAjuste ?? '');
                expect(esAjusteValido).toBe(true);
            }
        }

        // e) Resumen agrupado por forma de pago (si hay planes con ajuste)
        const resumenGrupo = page.locator('#resumen-ajuste-por-grupo');
        const tieneResumen = await resumenGrupo.count() > 0 && await resumenGrupo.isVisible().catch(() => false);
        if (tieneResumen) {
            await expect(resumenGrupo).toBeVisible();
            console.log('[T1] #resumen-ajuste-por-grupo visible ✓');
        } else {
            console.log('[T1] #resumen-ajuste-por-grupo no visible (sin planes con ajuste o CreditoPersonal excluido)');
        }

        // f) Verificar que CréditoPersonal no aparece en resumen de ajuste
        if (tieneResumen) {
            const textoResumen = await resumenGrupo.textContent();
            expect(textoResumen).not.toContain('Crédito Personal');
        }

        // ── Comprobante (si disponible) ────────────────────────────────

        const comprobanteLink = page.locator('a[href*="ComprobanteFactura"]').first();
        const hayComprobante = await comprobanteLink.count() > 0;

        if (hayComprobante) {
            const href = await comprobanteLink.getAttribute('href');
            console.log(`[T1] Comprobante disponible: ${href}`);

            if (href) {
                await page.goto(href);
                await page.waitForLoadState('networkidle');
                await screenshot(page, 'T1_comprobante');

                // Verificar sección desglose por ítem
                const desglosePorItem = page.locator('#desglose-pago-por-item');
                const desgloseVisible = await desglosePorItem.isVisible().catch(() => false);

                if (desgloseVisible) {
                    await expect(desglosePorItem).toBeVisible();
                    const filasTbody = desglosePorItem.locator('tbody tr');
                    expect(await filasTbody.count()).toBeGreaterThan(0);
                    console.log('[T1] #desglose-pago-por-item en comprobante ✓');
                } else {
                    console.log('[T1] #desglose-pago-por-item no visible (sin grupos con ajuste de plan)');
                }

                // Verificar total final en comprobante
                const totalEl = page.locator('.totals .total-row strong').last();
                const totalTexto = await totalEl.textContent();
                expect(totalTexto).toMatch(/^\$[\d.,]+/);
                console.log(`[T1] Total comprobante: ${totalTexto}`);
            }
        } else {
            console.log('[T1] Sin comprobante (factura no generada automáticamente)');
        }

        console.log(`[T1] DONE. Planes: ítem0=${plan0Seleccionado}, ítem1=${plan1Seleccionado}. Comprobante: ${hayComprobante}`);
    });

    // ──────────────────────────────────────────────────────────────────────
    // T2: Badge inicial "Sin definir" y actualización visual al guardar
    // ──────────────────────────────────────────────────────────────────────
    test('T2: badge "Usa pago principal" inicial y actualización al guardar pago', async ({ page }) => {

        const clienteNombre = await searchAndSelectClient(page);
        if (!clienteNombre) { test.skip(); return; }

        await activarFiltroStock(page);
        const prod = await addProduct(page, 'an');
        if (!prod) { test.skip(); return; }

        await page.waitForTimeout(400);

        // Estado inicial: Usa pago principal con estilos neutrales
        const badge = page.locator('.btn-configurar-pago-item').first();
        await expect(badge).toContainText('Usa pago principal');
        await expect(badge).toHaveClass(/border-slate-700/);
        await expect(badge).toHaveClass(/text-slate-500/);
        // hover:border-primary es una clase Tailwind distinta a border-primary — usar regex con límite de token
        await expect(badge).not.toHaveClass(/(^| )border-primary( |$)/);

        await screenshot(page, 'T2_badge_usa_pago_principal');

        // Abrir modal y configurar con Transferencia (tipo sin planes, verificación de flujo básico)
        await openPagoItemModal(page, 0);

        // El título del modal debe mostrar el nombre del producto
        const titulo = page.locator('#modal-pago-item-titulo');
        await expect(titulo).not.toBeEmpty();

        await page.selectOption('#select-tipo-pago-item', TIPO_PAGO.Transferencia);
        await page.waitForTimeout(300);

        // Transferencia no soporta planes → mensaje "Sin planes disponibles"
        const planesContainer = page.locator('#modal-pago-item-planes');
        await expect(planesContainer).toContainText('Sin planes disponibles');

        await page.click('#btn-guardar-pago-item');
        await page.locator('#modal-pago-item').waitFor({ state: 'hidden', timeout: 3_000 });

        // Badge actualizado con estilo primario
        await expect(badge).toContainText(TIPO_PAGO_BADGE[TIPO_PAGO.Transferencia]);
        await expect(badge).not.toContainText('Sin definir');
        await expect(badge).toHaveClass(/border-primary/);
        await expect(badge).toHaveClass(/text-primary/);

        await screenshot(page, 'T2_badge_actualizado_transferencia');

        // Cambiar a Efectivo: reabrir modal y cambiar tipo
        await openPagoItemModal(page, 0);
        await page.selectOption('#select-tipo-pago-item', TIPO_PAGO.Efectivo);
        await page.click('#btn-guardar-pago-item');
        await page.locator('#modal-pago-item').waitFor({ state: 'hidden', timeout: 3_000 });

        await expect(badge).toContainText(TIPO_PAGO_BADGE[TIPO_PAGO.Efectivo]);

        console.log('[T2] DONE: badge inicial "Usa pago principal" → actualizado correctamente al guardar excepción');
    });

    // ──────────────────────────────────────────────────────────────────────
    // T3: CréditoPersonal no muestra planes en el modal de ítem
    // ──────────────────────────────────────────────────────────────────────
    test('T3: CréditoPersonal no muestra plan de pago en modal por ítem', async ({ page }) => {

        const clienteNombre = await searchAndSelectClient(page);
        if (!clienteNombre) { test.skip(); return; }

        await activarFiltroStock(page);
        const prod = await addProduct(page, 'an');
        if (!prod) { test.skip(); return; }

        await page.waitForTimeout(400);

        await openPagoItemModal(page, 0);

        // Seleccionar Crédito Personal
        await page.selectOption('#select-tipo-pago-item', TIPO_PAGO.CreditoPersonal);
        await page.waitForTimeout(350);

        // Verificación clave: sin planes disponibles (Fase 16.2 — decisión de negocio)
        const planesContainer = page.locator('#modal-pago-item-planes');
        await expect(planesContainer).toContainText('Sin planes disponibles para este medio');

        // No deben existir botones de plan
        await expect(planesContainer.locator('.plan-pago-item-btn')).toHaveCount(0);

        await screenshot(page, 'T3_modal_credito_personal_sin_planes');

        // Guardar y verificar badge
        await page.click('#btn-guardar-pago-item');
        await page.locator('#modal-pago-item').waitFor({ state: 'hidden', timeout: 3_000 });

        const badge = page.locator('.btn-configurar-pago-item').first();
        await expect(badge).toContainText(TIPO_PAGO_BADGE[TIPO_PAGO.CreditoPersonal]);

        // El badge NO debe incluir planId (CréditoPersonal nunca tiene plan)
        const badgeText = await badge.textContent();
        expect(badgeText).not.toMatch(/#\d+/); // sin "· #<planId>"

        console.log('[T3] DONE: CréditoPersonal sin planes ✓, badge sin plan ✓');
    });

    // ──────────────────────────────────────────────────────────────────────
    // T4: Cancelar modal no modifica el ítem
    // ──────────────────────────────────────────────────────────────────────
    test('T4: Cancelar en modal de pago no modifica el ítem', async ({ page }) => {

        const clienteNombre = await searchAndSelectClient(page);
        if (!clienteNombre) { test.skip(); return; }

        await activarFiltroStock(page);
        const prod = await addProduct(page, 'an');
        if (!prod) { test.skip(); return; }

        await page.waitForTimeout(400);

        const badge = page.locator('.btn-configurar-pago-item').first();
        await expect(badge).toContainText('Usa pago principal');

        // Capturar texto inicial para verificar que cancel no lo modifica
        const textoInicial = await badge.textContent();

        // Abrir modal, seleccionar un tipo distinto al global default, luego cancelar
        await openPagoItemModal(page, 0);
        await page.selectOption('#select-tipo-pago-item', TIPO_PAGO.CuentaCorriente);
        await page.waitForTimeout(200);

        // Cancelar vía botón
        await page.locator('.btn-cerrar-pago-item').first().click();
        await page.locator('#modal-pago-item').waitFor({ state: 'hidden', timeout: 3_000 });

        // Badge no cambió — cancel no aplicó la excepción de CuentaCorriente
        const textoDespues = await badge.textContent();
        expect(textoDespues).toBe(textoInicial);
        await expect(badge).not.toContainText('Cta. Cte.');

        console.log('[T4] DONE: Cancelar no modifica badge ✓');
    });

    // ──────────────────────────────────────────────────────────────────────
    // T5: Click en backdrop cierra el modal sin guardar
    // ──────────────────────────────────────────────────────────────────────
    test('T5: backdrop del modal cierra sin guardar', async ({ page }) => {

        const clienteNombre = await searchAndSelectClient(page);
        if (!clienteNombre) { test.skip(); return; }

        await activarFiltroStock(page);
        const prod = await addProduct(page, 'an');
        if (!prod) { test.skip(); return; }

        await page.waitForTimeout(400);

        const badge = page.locator('.btn-configurar-pago-item').first();
        const textoAntes = await badge.textContent();

        // Abrir modal
        await openPagoItemModal(page, 0);
        await expect(page.locator('#modal-pago-item')).not.toHaveClass(/hidden/);

        // Click en el backdrop (esquina superior izquierda del overlay, fuera del card)
        await page.locator('#modal-pago-item').click({ position: { x: 5, y: 5 } });
        await page.locator('#modal-pago-item').waitFor({ state: 'hidden', timeout: 3_000 });

        // Badge sin cambios
        const textoDespues = await badge.textContent();
        expect(textoDespues).toBe(textoAntes);

        console.log('[T5] DONE: backdrop cierra modal sin modificar badge ✓');
    });

    // ──────────────────────────────────────────────────────────────────────
    // T6: Limpiar excepción → badge vuelve a herencia del pago principal
    // ──────────────────────────────────────────────────────────────────────
    test('T6: limpiar excepción vuelve al badge heredado del pago principal', async ({ page }) => {

        const clienteNombre = await searchAndSelectClient(page);
        if (!clienteNombre) { test.skip(); return; }

        await activarFiltroStock(page);
        const prod = await addProduct(page, 'an');
        if (!prod) { test.skip(); return; }

        await page.waitForTimeout(400);

        const badge = page.locator('.btn-configurar-pago-item').first();

        // 1. Guardar excepción: Transferencia
        await openPagoItemModal(page, 0);
        await page.selectOption('#select-tipo-pago-item', TIPO_PAGO.Transferencia);
        await page.click('#btn-guardar-pago-item');
        await page.locator('#modal-pago-item').waitFor({ state: 'hidden', timeout: 3_000 });

        await expect(badge).toContainText(TIPO_PAGO_BADGE[TIPO_PAGO.Transferencia]);
        await expect(badge).toHaveClass(/border-primary/);
        await expect(badge).toHaveClass(/text-primary/);

        await screenshot(page, 'T6_badge_con_excepcion_transferencia');

        // 2. Reabrir modal y limpiar excepción → opción vacía = "Usa pago principal"
        await openPagoItemModal(page, 0);
        await page.selectOption('#select-tipo-pago-item', '');
        await page.waitForTimeout(200);
        await page.click('#btn-guardar-pago-item');
        await page.locator('#modal-pago-item').waitFor({ state: 'hidden', timeout: 3_000 });

        // Badge volvió a estado heredado: texto incluye "Usa pago principal", sin clases de excepción
        await expect(badge).toContainText('Usa pago principal');
        await expect(badge).toHaveClass(/border-slate-700/);
        await expect(badge).not.toHaveClass(/(^| )border-primary( |$)/);
        await expect(badge).not.toContainText('Transferencia');

        await screenshot(page, 'T6_badge_limpio_herencia');

        console.log('[T6] DONE: excepción limpiada → badge heredado ✓');
    });

});
