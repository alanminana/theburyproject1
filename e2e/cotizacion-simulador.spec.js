// @ts-check
/**
 * COTIZ-QA — E2E Simulador de Cotización
 *
 * Valida el flujo del simulador post-COTIZ-3C:
 *   T1. Carga del simulador — estructura inicial
 *   T2. Simulación genera cards .payment-option-card
 *   T3. Selección aplica .payment-option-card--selected y radio checked
 *   T4. Mobile 390px — sin scroll horizontal en resultados
 *   T5. Agrupación visual por medio de pago (COTIZ-3C)
 *
 * Prerrequisitos:
 *   - App corriendo en E2E_BASE_URL (default: http://localhost:5187)
 *   - E2E_USER y E2E_PASS configurados (ver global-setup.js)
 *   - Al menos 1 producto en la DB
 *
 * Selectores usados (todos existentes en producción, sin data-testid adicionales):
 *   #cotizacion-producto-buscar    — input de búsqueda de producto
 *   #cotizacion-productos-dropdown — dropdown con botones de resultado
 *   #cotizacion-agregar-producto   — botón agregar
 *   #cotizacion-simular            — botón simular
 *   #cotizacion-resultados         — contenedor de resultados (hidden → visible)
 *   #cotizacion-resultados-vacio   — mensaje vacío
 *   #cotizacion-resultados-tbody   — grid de cards
 *   .payment-option-card           — cada card de medio de pago
 *   .payment-option-card--selected — card seleccionada
 *   .payment-status-chip           — chip de estado
 *   input[name="cotizacion-opcion-pago"] — radios de selección
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
 * Retorna true si se agrega al menos un producto a la tabla.
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
 * Verifica ausencia de scroll horizontal con margen de 2px.
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

        // Contenedor de cards existe en el DOM desde el inicio
        await expect(page.locator('#cotizacion-resultados-tbody')).toBeAttached();

        // Estado inicial: mensaje vacío visible, resultados ocultos
        await expect(page.locator('#cotizacion-resultados-vacio')).toBeVisible();
        await expect(page.locator('#cotizacion-resultados')).not.toBeVisible();

        // Botón simular visible y habilitado
        await expect(page.locator('#cotizacion-simular')).toBeVisible();
        await expect(page.locator('#cotizacion-simular')).toBeEnabled();

        // No hay tabla de resultados (COTIZ-3B eliminó la tabla de 8 columnas)
        const tablesInResults = await page.locator('#cotizacion-resultados table').count();
        expect(tablesInResults).toBe(0);

        // No existe min-w-[980px] en el área de resultados
        const narrowTable = await page.locator('#cotizacion-resultados [class*="min-w-"]').count();
        expect(narrowTable).toBe(0);
    });

    // ─── T2: Cards de resultados ─────────────────────────────────────────────

    test('T2: Simulación genera cards .payment-option-card', async ({ page }) => {
        await page.setViewportSize(VIEWPORT_DESKTOP);
        await gotoCotizacion(page);

        const added = await agregarProductoSimulador(page);
        test.skip(!added, 'Sin productos disponibles en el entorno de prueba');

        // Ejecutar simulación
        await page.click('#cotizacion-simular');
        await page.locator('#cotizacion-resultados').waitFor({ state: 'visible', timeout: 15_000 });

        // Al menos una card visible
        const cards = page.locator('.payment-option-card');
        await expect(cards.first()).toBeVisible({ timeout: 5_000 });
        const cardCount = await cards.count();
        expect(cardCount).toBeGreaterThan(0);

        // Al menos un status chip visible
        await expect(page.locator('.payment-status-chip').first()).toBeVisible();

        // El contenedor es el grid div, no una tabla
        await expect(page.locator('#cotizacion-resultados-tbody')).toBeVisible();
        const tablesInResults = await page.locator('#cotizacion-resultados table').count();
        expect(tablesInResults).toBe(0);

        // Mensaje vacío se ocultó
        await expect(page.locator('#cotizacion-resultados-vacio')).not.toBeVisible();
    });

    // ─── T3: Selección de card ───────────────────────────────────────────────

    test('T3: Selección aplica .payment-option-card--selected y radio checked', async ({ page }) => {
        await page.setViewportSize(VIEWPORT_DESKTOP);
        await gotoCotizacion(page);

        const added = await agregarProductoSimulador(page);
        test.skip(!added, 'Sin productos disponibles en el entorno de prueba');

        await page.click('#cotizacion-simular');
        await page.locator('#cotizacion-resultados').waitFor({ state: 'visible', timeout: 15_000 });
        await page.locator('.payment-option-card').first().waitFor({ state: 'visible', timeout: 5_000 });

        const enabledRadios = page.locator('input[name="cotizacion-opcion-pago"]:not([disabled])');
        const radioCount = await enabledRadios.count();
        test.skip(radioCount === 0, 'Sin opciones de pago habilitadas (todos bloqueados)');

        // Buscar un radio no seleccionado actualmente para forzar un cambio de estado
        let targetRadio = null;
        for (let i = 0; i < radioCount; i++) {
            const r = enabledRadios.nth(i);
            const checked = await r.isChecked();
            if (!checked) { targetRadio = r; break; }
        }

        if (!targetRadio) {
            // Auto-selección ocupó el único radio disponible — verificar que está aplicada
            await expect(page.locator('.payment-option-card--selected')).toBeVisible({ timeout: 2_000 });
            await expect(page.locator('input[name="cotizacion-opcion-pago"]:checked')).toHaveCount(1);
            return;
        }

        // Seleccionar el radio no chequeado
        await targetRadio.click();

        // Exactamente una card debe tener la clase selected
        await expect(page.locator('.payment-option-card--selected')).toBeVisible({ timeout: 3_000 });
        await expect(page.locator('.payment-option-card--selected')).toHaveCount(1);

        // El radio target debe quedar chequeado
        await expect(targetRadio).toBeChecked();
    });

    // ─── T5: Agrupación visual por medio de pago (COTIZ-3C) ──────────────────

    test('T5: Agrupación visual — grupos de medio de pago con cards', async ({ page }) => {
        await page.setViewportSize(VIEWPORT_DESKTOP);
        await gotoCotizacion(page);

        const added = await agregarProductoSimulador(page);
        test.skip(!added, 'Sin productos disponibles en el entorno de prueba');

        await page.click('#cotizacion-simular');
        await page.locator('#cotizacion-resultados').waitFor({ state: 'visible', timeout: 15_000 });
        await page.locator('.payment-option-card').first().waitFor({ state: 'visible', timeout: 5_000 });

        // Existe al menos un grupo visual
        const groups = page.locator('.payment-option-group');
        const groupCount = await groups.count();
        expect(groupCount, 'Debe haber al menos un grupo de medio de pago').toBeGreaterThan(0);

        // Cada grupo contiene al menos una card
        for (let i = 0; i < groupCount; i++) {
            const cardsInGroup = await groups.nth(i).locator('.payment-option-card').count();
            expect(cardsInGroup, `Grupo ${i} debe tener al menos una card`).toBeGreaterThan(0);
        }

        // La auto-selección o selección manual sigue funcionando
        await expect(page.locator('.payment-option-card--selected')).toBeVisible({ timeout: 3_000 });
    });

    // ─── T6: Descuento por producto (COTIZ-1B) ───────────────────────────────

    test('T6: Descuento por producto — inputs presentes y simulacion funciona', async ({ page }) => {
        await page.setViewportSize(VIEWPORT_DESKTOP);
        await gotoCotizacion(page);

        const added = await agregarProductoSimulador(page);
        test.skip(!added, 'Sin productos disponibles en el entorno de prueba');

        // Los inputs de descuento por producto deben estar visibles en la primera fila
        const descPctInput    = page.locator('[data-cotizacion-desc-pct-index="0"]');
        const descImporteInput = page.locator('[data-cotizacion-desc-importe-index="0"]');
        await expect(descPctInput).toBeVisible({ timeout: 3_000 });
        await expect(descImporteInput).toBeVisible({ timeout: 3_000 });

        // Cargar descuento porcentaje 10%
        await descPctInput.fill('10');

        // Simular con descuento por producto cargado
        await page.click('#cotizacion-simular');
        await page.locator('#cotizacion-resultados').waitFor({ state: 'visible', timeout: 15_000 });

        // Cards deben seguir apareciendo — el descuento por producto no rompe la simulacion
        const cards = page.locator('.payment-option-card');
        await expect(cards.first()).toBeVisible({ timeout: 5_000 });
        expect(await cards.count()).toBeGreaterThan(0);

        // El descuento total debe ser mayor a cero (el backend aplico el descuento)
        const descuentoText = await page.locator('#cotizacion-descuento').textContent();
        expect(descuentoText).not.toBe('$ 0,00');
    });

    // ─── T4: Mobile 390px sin scroll horizontal ──────────────────────────────

    test('T4: Mobile 390px — sin scroll horizontal en resultados', async ({ page }) => {
        await page.setViewportSize(VIEWPORT_MOBILE);
        await gotoCotizacion(page);

        const added = await agregarProductoSimulador(page);

        if (added) {
            await page.click('#cotizacion-simular');
            await page.locator('#cotizacion-resultados').waitFor({ state: 'visible', timeout: 15_000 });

            // Hay cards en mobile
            const cardCount = await page.locator('.payment-option-card').count();
            expect(cardCount).toBeGreaterThan(0);
        }

        // Sin scroll horizontal con o sin resultados (COTIZ-3B eliminó min-w-[980px])
        const noOverflow = await noHorizontalOverflow(page);
        expect(noOverflow, 'Scroll horizontal detectado en mobile 390px').toBeTruthy();
    });
});
