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
 * COTIZACION-SIMULAR-GUARDAR-DIRECTO-01 (decisión confirmada: Guardar pasa directo a venta): con un cliente del sistema, Guardar guarda y
 * convierte en un solo paso, así que desde el simulador ya no se llega a una cotización «Emitida» sin convertir. Esa pantalla de
 * conversión manual (Detalles: #cotizacion-btn-convertir, #cotizacion-conversion-modal) sigue existiendo, por lo que estos 4 tests parten de
 * cotizaciones «Emitida» SEMBRADAS (OmitidosE2ESeeder → E2E_COTIZACION_CONV_IDS), una por test porque cada uno la convierte.
 * Son fixtures CONSUMIBLES (una conversión por cotización): valen para UNA corrida por base y por proyecto de viewport (CI corre solo
 * 1366x768 sobre una base efímera). Un fixture ya consumido hace FALLAR el test (no se omite en silencio).
 * Antes se omitían los 4 (C-03: ver docs/e2e-omitidos-clasificacion.md).
 *
 * Prerrequisitos:
 *   - App corriendo en E2E_BASE_URL (default: http://localhost:5187)
 *   - E2E_USER y E2E_PASS configurados (ver global-setup.js)
 *   - E2E_COTIZACION_CONV_IDS (4 ids de cotizaciones Emitidas sembradas); sin ella los tests se omiten con motivo
 *   - Usuario Admin con permiso cotizaciones:convert
 *
 * Selectores usados (todos existentes en producción):
 *   #cotizacion-producto-buscar        — input búsqueda producto
 *   #cotizacion-productos-dropdown     — dropdown resultados
 *   #cotizacion-cliente-buscar         — input búsqueda cliente
 *   #cotizacion-clientes-dropdown      — dropdown clientes
 *   #cotizacion-simular                — botón simular
 *   #cotizacion-resultados             — contenedor de resultados
 *   #cotizacion-resultados-tbody tr    — fila de opción de pago (tabla rtable)
 *   #cotizacion-guardar                — botón guardar (directo, sin modal; encadena
 *                                         a Pasar a venta si hay cliente de sistema)
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

// ── Helpers ──────────────────────────────────────────────────────────────────

/**
 * Abre el Detalles de una cotización «Emitida» sembrada (OmitidosE2ESeeder, E2E_COTIZACION_CONV_IDS = 4 ids separados por coma).
 * Desde el simulador ya no se puede dejar una cotización con cliente sin convertir (COTIZACION-SIMULAR-GUARDAR-DIRECTO-01: guardar
 * encadena a Venta/Edit), por eso este flujo manual de Detalles se ejercita con cotizaciones sembradas: una por test, porque cada
 * test la convierte (índice 2 = T11 lleva un descuento por importe de $50 en la línea). Sin la variable el test se omite con motivo.
 * Retorna la URL de Detalles, o '' si no hay cotización sembrada.
 * @param {import('playwright/test').Page} page
 * @param {number} indice 0..3
 * @returns {Promise<string>}
 */
async function irADetallesSembrada(page, indice) {
    const ids = (process.env.E2E_COTIZACION_CONV_IDS || '').split(',').map((x) => x.trim()).filter(Boolean);
    if (ids.length <= indice) return '';
    const url = `/Cotizacion/Detalles/${ids[indice]}`;
    await page.goto(url, { waitUntil: 'domcontentloaded', timeout: 15_000 });
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
        const detallesUrl = await irADetallesSembrada(page, 0);
        test.skip(!detallesUrl, 'Falta E2E_COTIZACION_CONV_IDS (cotizaciones Emitidas sembradas por OmitidosE2ESeeder) en este entorno.');

        expect(detallesUrl).toMatch(/\/Cotizacion\/Detalles\/\d+/);

        // 2. El badge debe ser "Emitida" antes de convertir
        await expect(page.locator('.quote-state-badge--emitida')).toBeVisible({ timeout: 5_000 });

        // 3. Abrir modal y esperar preview
        const convertible = await abrirModalYEsperarPreview(page);
        expect(convertible, 'La cotización sembrada no es convertible (¿fixture ya consumido por otro proyecto/intento? Estas cotizaciones se convierten una sola vez: usar base nueva por corrida).').toBeTruthy();

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
        const detallesUrl = await irADetallesSembrada(page, 1);
        test.skip(!detallesUrl, 'Falta E2E_COTIZACION_CONV_IDS (cotizaciones Emitidas sembradas por OmitidosE2ESeeder) en este entorno.');

        // 2. Abrir modal y confirmar conversión
        const convertible = await abrirModalYEsperarPreview(page);
        expect(convertible, 'La cotización sembrada no es convertible (¿fixture ya consumido por otro proyecto/intento? Estas cotizaciones se convierten una sola vez: usar base nueva por corrida).').toBeTruthy();

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
        const detallesUrl = await irADetallesSembrada(page, 2);
        test.skip(!detallesUrl, 'Falta E2E_COTIZACION_CONV_IDS (cotizaciones Emitidas sembradas por OmitidosE2ESeeder) en este entorno.');

        // 2. Abrir modal de conversión
        const convertible = await abrirModalYEsperarPreview(page);
        expect(convertible, 'La cotización sembrada no es convertible (¿fixture ya consumido por otro proyecto/intento? Estas cotizaciones se convierten una sola vez: usar base nueva por corrida).').toBeTruthy();

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
        const detallesUrl = await irADetallesSembrada(page, 3);
        test.skip(!detallesUrl, 'Falta E2E_COTIZACION_CONV_IDS (cotizaciones Emitidas sembradas por OmitidosE2ESeeder) en este entorno.');

        const convertible = await abrirModalYEsperarPreview(page);
        expect(convertible, 'La cotización sembrada no es convertible (¿fixture ya consumido por otro proyecto/intento? Estas cotizaciones se convierten una sola vez: usar base nueva por corrida).').toBeTruthy();

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
