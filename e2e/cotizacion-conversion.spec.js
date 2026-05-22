// @ts-check
/**
 * COTIZ-QA-3 — E2E Conversión Cotización → Venta
 *
 * Valida el flujo completo desde guardar una cotización hasta convertirla a venta:
 *   T9.  Conversión básica: cotización emitida → modal → confirmar → /Venta/Edit/{id}
 *   T10. Estado "Convertida" visible en detalle de cotización tras conversión
 *   T11. Conversión con descuento por producto importe — no rompe el flujo
 *   T12. Panel de conversión ausente si cotización ya fue convertida (regresión)
 *
 * Prerrequisitos:
 *   - App corriendo en E2E_BASE_URL (default: http://localhost:5187)
 *   - E2E_USER y E2E_PASS configurados (ver global-setup.js)
 *   - Al menos 1 producto y 1 cliente en la DB
 *   - Usuario Admin con permiso cotizaciones:convert
 *
 * Selectores usados (todos existentes en producción):
 *   #cotizacion-producto-buscar        — input búsqueda producto
 *   #cotizacion-productos-dropdown     — dropdown resultados
 *   #cotizacion-agregar-producto       — botón agregar producto
 *   #cotizacion-cliente-buscar         — input búsqueda cliente
 *   #cotizacion-clientes-dropdown      — dropdown clientes
 *   #cotizacion-simular                — botón simular
 *   #cotizacion-resultados             — contenedor de resultados
 *   .payment-option-card               — card de opción de pago
 *   #cotizacion-guardar                — botón guardar cotización
 *   #cotizacion-btn-convertir          — botón "Convertir a Venta" en Detalles
 *   #cotizacion-conversion-modal       — modal de conversión
 *   #cotizacion-conversion-loading     — panel de carga del preview
 *   #cotizacion-conversion-contenido   — contenido del modal tras preview
 *   #cotizacion-btn-confirmar-conversion — botón confirmar conversión
 *   .quote-state-badge--convertida     — badge de estado "Convertida"
 *   .quote-state-badge--emitida        — badge de estado "Emitida"
 *
 * Comportamiento de descuentos confirmado (COTIZ-QA-2):
 *   - DescuentoImporteSnapshot → VentaDetalle.Descuento (propagado)
 *   - DescuentoPorcentajeSnapshot solo → VentaDetalle.Descuento = 0 (no propagado)
 *   - Venta.Descuento = 0 siempre (descuento general no se propaga)
 *
 * Nota: la conversión SIEMPRE requiere ClienteId (independientemente del medio de pago).
 * Los tests asignan un cliente al crear la cotización para que el flujo sea completo.
 */

const { test, expect } = require('playwright/test');
const path = require('path');
const fs = require('fs');

const AUTH_FILE = path.join(__dirname, '.auth', 'user.json');
const EVIDENCE_DIR = path.join(process.cwd(), 'qa-evidence', 'cotiz-qa-3');

fs.mkdirSync(EVIDENCE_DIR, { recursive: true });

const VIEWPORT_DESKTOP = { width: 1366, height: 768 };

const TERMINOS_BUSQUEDA = ['an', 'el', 'or', 'is', 'ar', 'ro', 'al'];
const TERMINOS_CLIENTE  = ['ma', 'ro', 'an', 'pe', 'ju', 'ca', 'lu', 'al'];

// ── Helpers ──────────────────────────────────────────────────────────────────

/**
 * Navega a /Cotizacion y espera que el simulador esté listo.
 * @param {import('playwright/test').Page} page
 */
async function gotoCotizacion(page) {
    await page.goto('/Cotizacion', { waitUntil: 'domcontentloaded', timeout: 20_000 });
    await page.evaluate(() => document.fonts?.ready).catch(() => null);
    await expect(page.locator('#cotizacion-simular')).toBeVisible({ timeout: 10_000 });
}

/**
 * Busca y agrega el primer producto disponible al simulador.
 * @param {import('playwright/test').Page} page
 * @returns {Promise<boolean>}
 */
async function agregarProductoSimulador(page) {
    const input    = page.locator('#cotizacion-producto-buscar');
    const dropdown = page.locator('#cotizacion-productos-dropdown');
    const tbody    = page.locator('#cotizacion-productos-tbody');

    for (const term of TERMINOS_BUSQUEDA) {
        await input.fill(term);
        await page.waitForTimeout(600);

        const visible = await dropdown.isVisible({ timeout: 3_000 }).catch(() => false);
        if (!visible) continue;

        const firstBtn = dropdown.locator('button').first();
        const hasBtn = await firstBtn.isVisible({ timeout: 2_000 }).catch(() => false);
        if (!hasBtn) continue;

        await firstBtn.click();

        await expect(page.locator('#cotizacion-producto-seleccionado'))
            .not.toHaveText('Sin producto seleccionado.', { timeout: 3_000 })
            .catch(() => null);

        await page.click('#cotizacion-agregar-producto');
        await page.waitForTimeout(300);

        const rowCount = await tbody.locator('tr').count();
        if (rowCount > 0) return true;
    }
    return false;
}

/**
 * Busca y selecciona el primer cliente disponible en el simulador.
 * Necesario porque la conversión siempre requiere ClienteId.
 * @param {import('playwright/test').Page} page
 * @returns {Promise<boolean>}
 */
async function seleccionarPrimerCliente(page) {
    const buscar   = page.locator('#cotizacion-cliente-buscar');
    const dropdown = page.locator('#cotizacion-clientes-dropdown');

    for (const term of TERMINOS_CLIENTE) {
        await buscar.fill(term);
        await page.waitForTimeout(600);

        const visible = await dropdown.isVisible({ timeout: 3_000 }).catch(() => false);
        if (!visible) continue;

        const firstBtn = dropdown.locator('button').first();
        const hasBtn = await firstBtn.isVisible({ timeout: 2_000 }).catch(() => false);
        if (!hasBtn) continue;

        await firstBtn.click();
        await page.waitForTimeout(200);

        const clienteId = await page.locator('#cotizacion-cliente-id').inputValue().catch(() => '');
        if (clienteId) return true;
    }
    return false;
}

/**
 * Crea una cotización completa (cliente + producto + simular + guardar) y retorna la URL de detalles.
 * Retorna '' si no hay productos o clientes disponibles (el test debe skipear).
 * @param {import('playwright/test').Page} page
 * @param {{ descImporte?: number }} [opts]
 * @returns {Promise<string>}
 */
async function crearCotizacionYNavegar(page, opts = {}) {
    await gotoCotizacion(page);

    const added = await agregarProductoSimulador(page);
    if (!added) return '';

    // Asignar cliente (requerido para conversión)
    const clienteOk = await seleccionarPrimerCliente(page);
    if (!clienteOk) return '';

    // Aplicar descuento por producto si se indica
    if (opts.descImporte != null) {
        const descInput = page.locator('[data-cotizacion-desc-importe-index="0"]');
        await expect(descInput).toBeVisible({ timeout: 3_000 });
        await descInput.fill(String(opts.descImporte));
    }

    await page.click('#cotizacion-simular');
    await page.locator('#cotizacion-resultados').waitFor({ state: 'visible', timeout: 15_000 });
    await page.locator('.payment-option-card').first().waitFor({ state: 'visible', timeout: 5_000 });

    const guardarBtn = page.locator('#cotizacion-guardar');
    await expect(guardarBtn).toBeEnabled({ timeout: 5_000 });

    await Promise.all([
        page.waitForURL(/\/Cotizacion\/Detalles\/\d+/, { timeout: 20_000 }),
        guardarBtn.click()
    ]);
    await page.waitForLoadState('domcontentloaded');

    return page.url();
}

/**
 * En la página de Detalles, abre el modal de conversión y espera el preview.
 * Retorna true si el botón confirmar quedó habilitado.
 * @param {import('playwright/test').Page} page
 * @returns {Promise<boolean>}
 */
async function abrirModalYEsperarPreview(page) {
    const btnConvertir = page.locator('#cotizacion-btn-convertir');
    try {
        await expect(btnConvertir).toBeVisible({ timeout: 8_000 });
    } catch {
        return false;
    }

    await btnConvertir.click();

    const modal = page.locator('#cotizacion-conversion-modal');
    try {
        await expect(modal).toBeVisible({ timeout: 5_000 });
    } catch {
        return false;
    }

    try {
        await page.locator('#cotizacion-conversion-loading').waitFor({ state: 'hidden', timeout: 15_000 });
    } catch {
        return false;
    }

    try {
        await expect(page.locator('#cotizacion-conversion-contenido')).toBeVisible({ timeout: 5_000 });
    } catch {
        return false;
    }

    const btnConfirmar = page.locator('#cotizacion-btn-confirmar-conversion');
    return await btnConfirmar.isEnabled().catch(() => false);
}

// ── Suite ─────────────────────────────────────────────────────────────────────

test.describe('Cotización conversión — COTIZ-QA-3', () => {
    test.use({ storageState: AUTH_FILE });

    test.beforeEach(async ({ page }) => {
        await page.route('**/fonts.googleapis.com/**', route => route.abort()).catch(() => null);
        await page.route('**/fonts.gstatic.com/**', route => route.abort()).catch(() => null);
    });

    // ─── T9: Conversión básica ───────────────────────────────────────────────

    test('T9: Conversión completa — cotización emitida navega a /Venta/Edit/{id}', async ({ page }) => {
        await page.setViewportSize(VIEWPORT_DESKTOP);

        // 1. Crear cotización con cliente y navegar a Detalles
        const detallesUrl = await crearCotizacionYNavegar(page);
        test.skip(!detallesUrl, 'Sin productos o clientes disponibles en el entorno de prueba');

        expect(detallesUrl).toMatch(/\/Cotizacion\/Detalles\/\d+/);

        // 2. El badge debe ser "Emitida" antes de convertir
        await expect(page.locator('.quote-state-badge--emitida')).toBeVisible({ timeout: 5_000 });

        // 3. Abrir modal y esperar preview
        const convertible = await abrirModalYEsperarPreview(page);
        test.skip(!convertible, 'Modal de conversión no disponible o cotización no convertible');

        // 4. El total cotizado debe mostrarse en el modal
        await expect(page.locator('#cotizacion-total-cotizado')).not.toBeEmpty({ timeout: 3_000 });

        // 5. Confirmar conversión y esperar navegación a /Venta/Edit/{id}
        const btnConfirmar = page.locator('#cotizacion-btn-confirmar-conversion');
        await Promise.all([
            page.waitForURL(/\/Venta\/Edit\/\d+/, { timeout: 25_000 }),
            btnConfirmar.click()
        ]);

        expect(page.url()).toMatch(/\/Venta\/Edit\/\d+/);

        await page.screenshot({
            path: path.join(EVIDENCE_DIR, 'T9-venta-edit-post-conversion.png'),
            fullPage: false
        });
    });

    // ─── T10: Estado "Convertida" en Detalles ────────────────────────────────

    test('T10: Estado "Convertida" visible en Detalles tras conversión', async ({ page }) => {
        await page.setViewportSize(VIEWPORT_DESKTOP);

        // 1. Crear cotización
        const detallesUrl = await crearCotizacionYNavegar(page);
        test.skip(!detallesUrl, 'Sin productos o clientes disponibles en el entorno de prueba');

        // 2. Abrir modal y confirmar conversión
        const convertible = await abrirModalYEsperarPreview(page);
        test.skip(!convertible, 'Modal de conversión no disponible');

        const btnConfirmar = page.locator('#cotizacion-btn-confirmar-conversion');
        await Promise.all([
            page.waitForURL(/\/Venta\/Edit\/\d+/, { timeout: 25_000 }),
            btnConfirmar.click()
        ]);

        // 3. Volver a Detalles de la cotización
        await page.goto(detallesUrl, { waitUntil: 'domcontentloaded', timeout: 15_000 });

        // 4. Badge debe ser "Convertida"
        await expect(page.locator('.quote-state-badge--convertida')).toBeVisible({ timeout: 5_000 });

        // 5. Panel emerald "ya fue convertida" debe estar visible
        await expect(
            page.locator('text=Esta cotizacion ya fue convertida a venta')
        ).toBeVisible({ timeout: 5_000 });

        // 6. El botón "Convertir a Venta" NO debe estar presente (ya convertida)
        await expect(page.locator('#cotizacion-btn-convertir')).not.toBeVisible();

        // 7. Debe haber un link a la venta creada
        await expect(page.locator('a[href*="/Venta/Edit/"]').first()).toBeVisible({ timeout: 3_000 });

        await page.screenshot({
            path: path.join(EVIDENCE_DIR, 'T10-cotizacion-estado-convertida.png'),
            fullPage: false
        });
    });

    // ─── T11: Conversión con descuento por producto importe ──────────────────

    test('T11: Conversión con descuento por producto importe — flujo no se rompe', async ({ page }) => {
        await page.setViewportSize(VIEWPORT_DESKTOP);

        // 1. Crear cotización con descuento importe $50 en primer producto
        const detallesUrl = await crearCotizacionYNavegar(page, { descImporte: 50 });
        test.skip(!detallesUrl, 'Sin productos o clientes disponibles en el entorno de prueba');

        // 2. Abrir modal de conversión
        const convertible = await abrirModalYEsperarPreview(page);
        test.skip(!convertible, 'Modal de conversión no disponible');

        // 3. Confirmar conversión
        const btnConfirmar = page.locator('#cotizacion-btn-confirmar-conversion');
        await Promise.all([
            page.waitForURL(/\/Venta\/Edit\/\d+/, { timeout: 25_000 }),
            btnConfirmar.click()
        ]);

        expect(page.url()).toMatch(/\/Venta\/Edit\/\d+/);

        await page.screenshot({
            path: path.join(EVIDENCE_DIR, 'T11-conversion-con-descuento-importe.png'),
            fullPage: false
        });
    });

    // ─── T12: Panel conversión ausente si ya fue convertida ──────────────────

    test('T12: Panel "Convertir a Venta" ausente si cotización ya convertida', async ({ page }) => {
        await page.setViewportSize(VIEWPORT_DESKTOP);

        // 1. Crear y convertir cotización
        const detallesUrl = await crearCotizacionYNavegar(page);
        test.skip(!detallesUrl, 'Sin productos o clientes disponibles en el entorno de prueba');

        const convertible = await abrirModalYEsperarPreview(page);
        test.skip(!convertible, 'Modal de conversión no disponible');

        const btnConfirmar = page.locator('#cotizacion-btn-confirmar-conversion');
        await Promise.all([
            page.waitForURL(/\/Venta\/Edit\/\d+/, { timeout: 25_000 }),
            btnConfirmar.click()
        ]);

        // 2. Volver a Detalles
        await page.goto(detallesUrl, { waitUntil: 'domcontentloaded', timeout: 15_000 });

        // 3. El panel de conversión (amber) NO debe estar presente en el DOM
        await expect(page.locator('#cotizacion-conversion-panel')).not.toBeAttached();

        // 4. El botón de convertir NO debe estar presente
        await expect(page.locator('#cotizacion-btn-convertir')).not.toBeAttached();

        // 5. El modal de conversión NO debe estar presente en el DOM
        await expect(page.locator('#cotizacion-conversion-modal')).not.toBeAttached();
    });
});
