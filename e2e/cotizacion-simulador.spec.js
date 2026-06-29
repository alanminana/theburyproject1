// @ts-check
/**
 * COTIZ-QA — E2E Simulador de Cotización (rework visual: layout 3 columnas)
 *
 * Valida el flujo del simulador con el diseño de tabla comparativa (rtable):
 *   T1. Carga del simulador — estructura inicial
 *   T2. Simulación genera filas en la tabla de resultados
 *   T3. Selección de fila aplica .selected y abre el drawer de detalle
 *   T4. Mobile 390px — sin scroll horizontal de página
 *   T5. Agrupación expandible por medio de pago (parent/detail)
 *   T6. Descuento por producto
 *   T7/T8. Guardar (modal de confirmación) habilita "Pasar a venta"
 *
 * Prerrequisitos:
 *   - App corriendo en E2E_BASE_URL (default: http://localhost:5187)
 *   - E2E_USER y E2E_PASS configurados (ver global-setup.js)
 *   - Al menos 1 producto en la DB
 *
 * Selectores (contrato actual del simulador):
 *   #cotizacion-producto-buscar        — input de búsqueda de producto
 *   #cotizacion-productos-dropdown     — dropdown con botones de resultado
 *   #cotizacion-agregar-producto       — botón agregar
 *   #cotizacion-productos-tbody        — contenedor de productos (cards .cart-row)
 *   #cotizacion-simular                — botón simular
 *   #cotizacion-resultados             — contenedor de resultados (hidden → visible)
 *   #cotizacion-resultados-vacio       — mensaje vacío
 *   #cotizacion-resultados-tbody       — tbody de la tabla comparativa (rtable)
 *   #cotizacion-resultados-tbody tr[data-cotizacion-opcion-key] — fila seleccionable
 *   tr.selected                        — fila seleccionada
 *   tr.parent / tr.detail              — grupo expandible (varios planes por medio)
 *   #cotizacion-guardar                — abre el modal de confirmación
 *   #cotizacion-guardar-confirm        — confirma el guardado (POST)
 */

const { test, expect } = require('playwright/test');
const path = require('path');
const fs = require('fs');

const AUTH_FILE = path.join(__dirname, '.auth', 'user.json');
const EVIDENCE_DIR = path.join(process.cwd(), 'qa-evidence', 'cotiz-qa');

fs.mkdirSync(EVIDENCE_DIR, { recursive: true });

const VIEWPORT_DESKTOP = { width: 1366, height: 768 };
const VIEWPORT_MOBILE  = { width: 390,  height: 844 };

// Términos para buscar productos (mismo criterio que helpers.js de venta)
const TERMINOS_BUSQUEDA = ['an', 'el', 'or', 'is', 'ar', 'ro', 'al'];

// ── Helpers ─────────────────────────────────────────────────────────────────

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
 * Retorna true si se agrega al menos un producto (card .cart-row).
 * @param {import('playwright/test').Page} page
 * @returns {Promise<boolean>}
 */
async function agregarProductoSimulador(page) {
    const input    = page.locator('#cotizacion-producto-buscar');
    const dropdown = page.locator('#cotizacion-productos-dropdown');
    const tbody    = page.locator('#cotizacion-productos-tbody');

    for (const term of TERMINOS_BUSQUEDA) {
        await input.fill(term);
        await page.waitForTimeout(600); // debounce 220ms + margen de red

        const visible = await dropdown.isVisible({ timeout: 3_000 }).catch(() => false);
        if (!visible) continue;

        const firstBtn = dropdown.locator('button').first();
        const hasBtn = await firstBtn.isVisible({ timeout: 2_000 }).catch(() => false);
        if (!hasBtn) continue;

        await firstBtn.click();

        // Esperar a que el campo de estado muestre el producto
        await expect(page.locator('#cotizacion-producto-seleccionado'))
            .not.toContainText('Sin producto seleccionado.', { timeout: 3_000 })
            .catch(() => null);

        await page.click('#cotizacion-agregar-producto');
        await page.waitForTimeout(300);

        const rowCount = await tbody.locator('.cart-row').count();
        if (rowCount > 0) return true;
    }
    return false;
}

/**
 * Verifica ausencia de scroll horizontal de página con margen de 2px.
 * @param {import('playwright/test').Page} page
 * @returns {Promise<boolean>}
 */
async function noHorizontalOverflow(page) {
    return page.evaluate(() =>
        document.documentElement.scrollWidth <= window.innerWidth + 2
    );
}

// ── Suite ────────────────────────────────────────────────────────────────────

test.describe('Cotización simulador — COTIZ-QA', () => {
    test.use({ storageState: AUTH_FILE });

    test.beforeEach(async ({ page }) => {
        // Bloquear Google Fonts (igual que ui-4e-layout-visual.spec.js)
        await page.route('**/fonts.googleapis.com/**', route => route.abort()).catch(() => null);
        await page.route('**/fonts.gstatic.com/**', route => route.abort()).catch(() => null);
    });

    // ─── T1: Carga del simulador ─────────────────────────────────────────────

    test('T1: Carga del simulador — estructura inicial', async ({ page }) => {
        await page.setViewportSize(VIEWPORT_DESKTOP);
        await gotoCotizacion(page);

        // tbody de resultados existe en el DOM desde el inicio
        await expect(page.locator('#cotizacion-resultados-tbody')).toBeAttached();

        // Estado inicial: mensaje vacío visible, resultados ocultos
        await expect(page.locator('#cotizacion-resultados-vacio')).toBeVisible();
        await expect(page.locator('#cotizacion-resultados')).not.toBeVisible();

        // Botón simular visible y habilitado; guardar deshabilitado
        await expect(page.locator('#cotizacion-simular')).toBeVisible();
        await expect(page.locator('#cotizacion-simular')).toBeEnabled();
        await expect(page.locator('#cotizacion-guardar')).toBeDisabled();

        // Sin scroll horizontal de página
        expect(await noHorizontalOverflow(page)).toBeTruthy();
    });

    // ─── T2: Filas de resultados ─────────────────────────────────────────────

    test('T2: Simulación genera filas en la tabla de resultados', async ({ page }) => {
        await page.setViewportSize(VIEWPORT_DESKTOP);
        await gotoCotizacion(page);

        const added = await agregarProductoSimulador(page);
        test.skip(!added, 'Sin productos disponibles en el entorno de prueba');

        await page.click('#cotizacion-simular');
        await page.locator('#cotizacion-resultados').waitFor({ state: 'visible', timeout: 15_000 });

        // Al menos una fila de resultado
        const rows = page.locator('#cotizacion-resultados-tbody tr');
        await expect(rows.first()).toBeVisible({ timeout: 5_000 });
        expect(await rows.count()).toBeGreaterThan(0);

        // Al menos un pill de estado visible
        await expect(page.locator('#cotizacion-resultados-tbody .pill').first()).toBeVisible();

        // La tabla comparativa (rtable) está presente
        await expect(page.locator('#cotizacion-resultados table.rtable')).toBeVisible();

        // Mensaje vacío oculto
        await expect(page.locator('#cotizacion-resultados-vacio')).not.toBeVisible();
    });

    // ─── T3: Selección de fila ───────────────────────────────────────────────

    test('T3: Selección de fila aplica .selected y abre el drawer de detalle', async ({ page }) => {
        await page.setViewportSize(VIEWPORT_DESKTOP);
        await gotoCotizacion(page);

        const added = await agregarProductoSimulador(page);
        test.skip(!added, 'Sin productos disponibles en el entorno de prueba');

        await page.click('#cotizacion-simular');
        await page.locator('#cotizacion-resultados').waitFor({ state: 'visible', timeout: 15_000 });

        const selectable = page.locator('#cotizacion-resultados-tbody tr[data-cotizacion-opcion-key]');
        const count = await selectable.count();
        test.skip(count === 0, 'Sin opciones de pago con plan disponible');

        // Tras simular, el recomendado/mejor queda auto-seleccionado
        await expect(page.locator('#cotizacion-resultados-tbody tr.selected')).toHaveCount(1, { timeout: 3_000 });

        // Elegir una fila distinta a la ya seleccionada (si existe)
        const noSel = page.locator('#cotizacion-resultados-tbody tr[data-cotizacion-opcion-key]:not(.selected)');
        if (await noSel.count() > 0) {
            await noSel.first().click();
            // queda exactamente una fila seleccionada
            await expect(page.locator('#cotizacion-resultados-tbody tr.selected')).toHaveCount(1, { timeout: 3_000 });
        } else {
            await selectable.first().click();
        }

        // Click en fila abre el drawer de detalle del plan
        await expect(page.locator('#modal-plan')).toBeVisible({ timeout: 3_000 });
        await page.keyboard.press('Escape');
        await expect(page.locator('#modal-plan')).not.toBeVisible({ timeout: 3_000 });

        // La selección persiste tras cerrar el drawer
        await expect(page.locator('#cotizacion-resultados-tbody tr.selected')).toHaveCount(1);
    });

    // ─── T5: Agrupación expandible por medio de pago ─────────────────────────

    test('T5: Agrupación expandible — parent/detail togglean visibilidad', async ({ page }) => {
        await page.setViewportSize(VIEWPORT_DESKTOP);
        await gotoCotizacion(page);

        const added = await agregarProductoSimulador(page);
        test.skip(!added, 'Sin productos disponibles en el entorno de prueba');

        await page.click('#cotizacion-simular');
        await page.locator('#cotizacion-resultados').waitFor({ state: 'visible', timeout: 15_000 });
        await page.locator('#cotizacion-resultados-tbody tr').first().waitFor({ state: 'visible', timeout: 5_000 });

        const parent = page.locator('#cotizacion-resultados-tbody tr.parent').first();
        const hasParent = await parent.count() > 0;
        test.skip(!hasParent, 'Ningún medio con múltiples planes en este entorno');

        // Por defecto el grupo está expandido (detalles visibles)
        const gkey = await parent.getAttribute('data-group');
        const details = page.locator(`#cotizacion-resultados-tbody tr.detail[data-g="${gkey}"]`);
        expect(await details.count()).toBeGreaterThan(0);
        await expect(details.first()).toBeVisible();

        // Colapsar: click en parent oculta los detalles
        await parent.click();
        await expect(details.first()).toBeHidden({ timeout: 2_000 });

        // Expandir de nuevo
        await parent.click();
        await expect(details.first()).toBeVisible({ timeout: 2_000 });
    });

    // ─── T6: Descuento por producto (COTIZ-1B) ───────────────────────────────

    test('T6: Descuento por producto — inputs presentes y simulacion funciona', async ({ page }) => {
        await page.setViewportSize(VIEWPORT_DESKTOP);
        await gotoCotizacion(page);

        const added = await agregarProductoSimulador(page);
        test.skip(!added, 'Sin productos disponibles en el entorno de prueba');

        // Inputs de descuento por producto en la primera card
        const descPctInput     = page.locator('[data-cotizacion-desc-pct-index="0"]');
        const descImporteInput = page.locator('[data-cotizacion-desc-importe-index="0"]');
        await expect(descPctInput).toBeVisible({ timeout: 3_000 });
        await expect(descImporteInput).toBeVisible({ timeout: 3_000 });

        await descPctInput.fill('10');

        await page.click('#cotizacion-simular');
        await page.locator('#cotizacion-resultados').waitFor({ state: 'visible', timeout: 15_000 });

        // Filas siguen apareciendo
        const rows = page.locator('#cotizacion-resultados-tbody tr');
        await expect(rows.first()).toBeVisible({ timeout: 5_000 });
        expect(await rows.count()).toBeGreaterThan(0);

        // El descuento total aplicado por el backend es > 0
        const descuentoText = await page.locator('#cotizacion-descuento').textContent();
        expect(descuentoText).not.toMatch(/\$\s*0,00/);
    });

    // ─── T7: Guardar con descuento por producto (COTIZ-QA-2) ─────────────────

    test('T7: Guardar cotización (descuento por producto) — habilita Pasar a venta', async ({ page }) => {
        await page.setViewportSize(VIEWPORT_DESKTOP);
        await gotoCotizacion(page);

        const added = await agregarProductoSimulador(page);
        test.skip(!added, 'Sin productos disponibles en el entorno de prueba');

        const descPctInput = page.locator('[data-cotizacion-desc-pct-index="0"]');
        await expect(descPctInput).toBeVisible({ timeout: 3_000 });
        await descPctInput.fill('10');

        await page.click('#cotizacion-simular');
        await page.locator('#cotizacion-resultados').waitFor({ state: 'visible', timeout: 15_000 });
        await page.locator('#cotizacion-resultados-tbody tr').first().waitFor({ state: 'visible', timeout: 5_000 });

        // Guardar: abre modal de confirmación, luego confirmar
        const guardarBtn = page.locator('#cotizacion-guardar');
        await expect(guardarBtn).toBeEnabled({ timeout: 5_000 });
        await guardarBtn.click();

        const guardarConfirm = page.locator('#cotizacion-guardar-confirm');
        await expect(guardarConfirm).toBeVisible({ timeout: 5_000 });
        await guardarConfirm.click();

        // Tras guardar, la acción se transforma en "Pasar a venta"
        await expect(page.locator('#cotizacion-pasar-venta')).toBeVisible({ timeout: 20_000 });
        await expect(page.locator('#cotizacion-acciones-pre')).toBeHidden();
        await expect(page.locator('#cotizacion-ver-guardada')).toHaveAttribute('href', /\/Cotizacion\/Detalles\/\d+/);
    });

    // ─── T8: Guardar con descuento general (COTIZ-QA-2) ──────────────────────

    test('T8: Guardar cotización (descuento general) — habilita Pasar a venta', async ({ page }) => {
        await page.setViewportSize(VIEWPORT_DESKTOP);
        await gotoCotizacion(page);

        const added = await agregarProductoSimulador(page);
        test.skip(!added, 'Sin productos disponibles en el entorno de prueba');

        const descGralPct = page.locator('#cotizacion-descuento-gral-pct');
        await expect(descGralPct).toBeVisible({ timeout: 3_000 });
        await descGralPct.fill('5');

        await page.click('#cotizacion-simular');
        await page.locator('#cotizacion-resultados').waitFor({ state: 'visible', timeout: 15_000 });
        await page.locator('#cotizacion-resultados-tbody tr').first().waitFor({ state: 'visible', timeout: 5_000 });

        const guardarBtn = page.locator('#cotizacion-guardar');
        await expect(guardarBtn).toBeEnabled({ timeout: 5_000 });
        await guardarBtn.click();

        const guardarConfirm = page.locator('#cotizacion-guardar-confirm');
        await expect(guardarConfirm).toBeVisible({ timeout: 5_000 });
        await guardarConfirm.click();

        // Tras guardar, la acción se transforma en "Pasar a venta"
        await expect(page.locator('#cotizacion-pasar-venta')).toBeVisible({ timeout: 20_000 });
        await expect(page.locator('#cotizacion-acciones-pre')).toBeHidden();
    });

    // ─── T4: Mobile 390px sin scroll horizontal de página ─────────────────────

    test('T4: Mobile 390px — sin scroll horizontal de página', async ({ page }) => {
        await page.setViewportSize(VIEWPORT_MOBILE);
        await gotoCotizacion(page);

        const added = await agregarProductoSimulador(page);

        if (added) {
            await page.click('#cotizacion-simular');
            await page.locator('#cotizacion-resultados').waitFor({ state: 'visible', timeout: 15_000 });

            // Hay filas en mobile (la tabla scrollea horizontal DENTRO del panel)
            const rowCount = await page.locator('#cotizacion-resultados-tbody tr').count();
            expect(rowCount).toBeGreaterThan(0);
        }

        // Sin scroll horizontal a nivel de página (el scroll de la tabla es interno)
        const noOverflow = await noHorizontalOverflow(page);
        expect(noOverflow, 'Scroll horizontal de página detectado en mobile 390px').toBeTruthy();
    });
});
