// @ts-check
/**
 * COTIZACION-MIVENTA-02 — segunda iteración de la POC "Mi Venta" (pedido explícito del
 * usuario, 2026-09-17): corrige 3 problemas de UX de la primera iteración.
 *
 *   1. "Confirmar Mi Venta" corre un preflight de sólo lectura (/conversion/preflight,
 *      reutiliza IVentaValidator.ValidarStock + ICajaService.ObtenerAperturaActivaParaUsuarioAsync)
 *      ANTES de crear la Venta — si no puede terminar, se queda en Mi Venta mostrando el
 *      bloqueo (#cotizacion-bloqueos) en vez de derivar silenciosamente a Venta/Edit.
 *   2. "Facturar al confirmar" abre el modal real de configuración (mismo partial que
 *      Venta/Details, Views/Venta/_FacturaCamposEmision.cshtml) en vez de un select A/B/C
 *      inline — el preview de Subtotal/IVA/alícuotas viene de /conversion/factura-preview
 *      (mismo cálculo que usará la Venta real, nunca recalculado en JS).
 *   3. Botón "Continuar con wizard" — cubierto en cotizacion-continuar-wizard.spec.js.
 *
 * Este spec cubre el CAMINO MI VENTA completo: cliente nuevo, producto, envío (modal real),
 * facturación (modal real reutilizado de Details), Confirmar Mi Venta. El resultado final
 * depende de una precondición ajena a este módulo (caja abierta a nombre del usuario E2E) —
 * se aceptan AMBOS desenlaces reales como válidos:
 *   - Caja abierta: preflight Listo → modal de confirmación → Venta Confirmada/Facturada en
 *     Venta/Details (nunca Venta/Edit).
 *   - Sin caja: preflight bloqueado → #cotizacion-bloqueos con "Abrir caja" — la Venta NO se
 *     crea (§NUEVA REGLA FUNDAMENTAL del pedido: nunca crear a ciegas lo que ya se sabe que
 *     no puede confirmarse).
 * Lo que nunca puede pasar: que "Confirmar Mi Venta" abra el modal de confirmación y termine
 * en Venta/Edit (eso es exactamente el bug que esta iteración corrige).
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
    await page.getByRole('textbox', { name: 'Apellido *' }).fill('MiVentaQA');
    await page.getByRole('textbox', { name: 'Nombre *' }).fill('Cliente');
    await page.getByRole('tab', { name: /Contacto/ }).click();
    await page.getByRole('textbox', { name: 'Teléfono *' }).fill('1133445566');
    await page.getByRole('textbox', { name: 'Domicilio *' }).fill('Av. Siempreviva 742');
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
        await page.click('#cotizacion-agregar-producto');
        await page.waitForTimeout(300);
        if (await page.locator('#cotizacion-productos-tbody .cart-row').count() > 0) return true;
    }
    return false;
}

test.describe('Cotización — "Mi Venta" v2: preflight real + modal de facturación reutilizado (COTIZACION-MIVENTA-02)', () => {
    test('Efectivo + envío + facturar: preflight nunca deja crear una Venta que ya sabe que no puede confirmar', async ({ page }) => {
        await page.setViewportSize({ width: 1366, height: 768 });

        const dni = String(Date.now()).slice(-8);
        const clienteId = await crearCliente(page, dni);
        expect(clienteId).toBeTruthy();

        await page.goto('/Cotizacion', { waitUntil: 'domcontentloaded' });
        await expect(page.locator('#cotizacion-simular')).toBeVisible({ timeout: 10_000 });

        await seleccionarCliente(page, dni);
        expect(await agregarPrimerProducto(page)).toBeTruthy();

        // Envío: modal real (sin cambios respecto a la primera iteración).
        await page.click('#cotizacion-tiene-envio');
        await expect(page.locator('#modal-envio')).toBeVisible({ timeout: 5_000 });
        await expect(page.locator('#cotizacion-envio-destinatario')).toHaveValue(/MiVentaQA/);
        await page.click('#cotizacion-envio-guardar');
        await expect(page.locator('#modal-envio')).toBeHidden();
        await expect(page.locator('#cotizacion-envio-resumen')).toContainText('Av. Siempreviva 742');

        const root = page.locator('[data-cotizacion-simulador]');
        const puedeFacturar = await root.getAttribute('data-puede-facturar');

        // El preview de IVA/alícuotas de "Facturar al confirmar" necesita una cotización
        // simulada+guardada (mismos snapshots que usará la Venta real) — por eso se activa
        // DESPUÉS de simular, nunca antes (el propio modal fuerza el guardado si hiciera
        // falta, pero simular primero es el orden natural del flujo real).
        await page.click('#cotizacion-simular');
        await expect(page.locator('#cotizacion-resultados')).toBeVisible({ timeout: 10_000 });

        if (puedeFacturar === 'true') {
            // §10/§11/§12 del pedido: activar el checkbox abre el MISMO modal que ya usa
            // Venta/Details (partial _FacturaCamposEmision) — no un select A/B/C inline.
            await page.click('#cotizacion-facturar');
            await expect(page.locator('#modal-facturar-config')).toBeVisible({ timeout: 10_000 });

            // El preview de Subtotal/IVA viene del backend (/conversion/factura-preview,
            // mismo cálculo que Venta/Details) — nunca "$0,00" fijo ni recalculado en JS.
            await expect(page.locator('#cotizacion-factura-total')).not.toHaveText('$0,00', { timeout: 10_000 });
            await expect(page.locator('#cotizacion-factura-total')).not.toHaveText('—');

            await page.selectOption('#cotizacion-factura-tipo-factura', 'A');
            await page.fill('#cotizacion-factura-punto-venta', '0001');
            await page.click('#cotizacion-facturar-guardar');
            await expect(page.locator('#modal-facturar-config')).toBeHidden();

            // §13 del pedido: resumen compacto, nunca el select siempre visible.
            await expect(page.locator('#cotizacion-facturar-resumen')).toContainText('Factura A');
            await expect(page.locator('#cotizacion-facturar-resumen')).toContainText('PV 0001');
        }

        const continuar = page.locator('#cotizacion-continuar');
        await expect(continuar).toBeEnabled({ timeout: 10_000 });
        const labelEsperado = puedeFacturar === 'true' ? 'Confirmar y facturar' : 'Confirmar Mi Venta';
        await expect(page.locator('[data-continuar-label]')).toHaveText(labelEsperado);

        await continuar.click();

        // §NUEVA REGLA FUNDAMENTAL del pedido: el click corre el preflight ANTES de abrir
        // ningún modal — el resultado depende de si el usuario E2E tiene caja abierta, pero
        // NUNCA puede terminar creando una Venta y mandando silenciosamente a Venta/Edit.
        const modalConfirmar = page.locator('#modal-confirmar-venta');
        const bloqueos = page.locator('#cotizacion-bloqueos');
        await Promise.race([
            modalConfirmar.waitFor({ state: 'visible', timeout: 10_000 }),
            bloqueos.waitFor({ state: 'visible', timeout: 10_000 })
        ]);

        if (await bloqueos.isVisible().catch(() => false)) {
            // El preflight detectó ANTES de crear nada que no podía terminar (sin caja
            // abierta, u otra precondición real como stock — el motivo exacto depende del
            // estado del entorno compartido, no es lo que este test verifica) — nunca se
            // abrió el modal de confirmación, y por lo tanto nunca se creó una Venta a ciegas.
            await expect(modalConfirmar).toBeHidden();
            await expect(bloqueos.locator('li')).not.toHaveCount(0);
            return;
        }

        // Camino feliz: preflight Listo → resumen de confirmación → aceptar.
        await expect(page.locator('#cotizacion-confirmar-resumen')).toContainText('Envío');
        if (puedeFacturar === 'true') {
            await expect(page.locator('#cotizacion-confirmar-resumen')).toContainText('Factura A');
        }
        await page.click('#cotizacion-confirmar-aceptar');

        // §DOBLE SUBMIT: un segundo click inmediato nunca debe crear una segunda venta.
        await page.locator('#cotizacion-continuar').click({ force: true }).catch(() => null);

        // §5/§NO HACER del pedido: "Confirmar Mi Venta" que llegó hasta acá (preflight Listo)
        // termina SIEMPRE en Venta/Details — nunca en Venta/Edit.
        await page.waitForURL(/\/Venta\/Details\/\d+/, { timeout: 15_000 });
        const ventaId = Number(page.url().match(/\/Venta\/Details\/(\d+)/)?.[1]);
        expect(ventaId).toBeGreaterThan(0);

        const main = page.locator('main');
        await expect(main).toContainText('MiVentaQA');
        await expect(main).toContainText('Av. Siempreviva 742');
        await expect(main).toContainText(/CONFIRMADA|FACTURADA/i);

        if (puedeFacturar === 'true') {
            await expect(main).toContainText(/FACTURADA/i);
            await expect(main).toContainText(/FA-A-\d+-\d+/);
        }
    });
});
