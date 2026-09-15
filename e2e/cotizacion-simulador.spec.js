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
 *   T7/T8. Guardar (directo, sin modal) habilita "Pasar a venta"
 *   T9. Mobile 390px — Resultados primero, Productos/Config colapsables,
 *       CTA siempre visible, tabla sin overflow interno (CIERRE-01)
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
 *   #cotizacion-guardar                — guarda directo (POST); encadena a Pasar a
 *                                         venta si hay un cliente de sistema
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

    // COTIZACION-SIMULAR-REDESIGN-VISUAL-CIERRE-01: en la banda angosta (<40rem
    // de contenedor, mobile real) Productos arranca colapsado — el buscador
    // queda oculto (mobile-collapsible.is-collapsed) hasta expandirlo. En
    // desktop el toggle es display:none y este click no hace nada.
    const toggle = page.locator('#cotizacion-productos-toggle');
    if (await toggle.isVisible().catch(() => false) && await toggle.getAttribute('aria-expanded') !== 'true') {
        await toggle.click();
    }

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

        // COTIZACION-SIMULAR-GUARDAR-DIRECTO-01: Guardar ya no abre un modal de
        // confirmación — guarda directo (y encadenaría a Pasar a venta si hubiera
        // un cliente de sistema seleccionado, que este flujo no selecciona).
        const guardarBtn = page.locator('#cotizacion-guardar');
        await expect(guardarBtn).toBeEnabled({ timeout: 5_000 });
        await guardarBtn.click();

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

            // Hay filas en mobile — COTIZACION-SIMULAR-REDESIGN-VISUAL-CIERRE-01:
            // la tabla ya no scrollea horizontal, reflow a filas compactas (ver T9).
            const rowCount = await page.locator('#cotizacion-resultados-tbody tr').count();
            expect(rowCount).toBeGreaterThan(0);
        }

        // Sin scroll horizontal a nivel de página (el scroll de la tabla es interno)
        const noOverflow = await noHorizontalOverflow(page);
        expect(noOverflow, 'Scroll horizontal de página detectado en mobile 390px').toBeTruthy();
    });

    // ─── T9: Mobile 390px — Resultados primero + paneles colapsables (CIERRE-01) ─

    test('T9: Mobile 390px — Resultados primero, Productos/Config colapsables, CTA siempre visible', async ({ page }) => {
        await page.setViewportSize(VIEWPORT_MOBILE);
        await gotoCotizacion(page);

        // Resultados aparece visualmente antes que Productos y Config — el DOM
        // sigue en orden "productos, resultados, config" (mismo markup que
        // desktop/tablet, sin duplicar nodos); el reorden es sólo CSS grid-area
        // (mismo mecanismo ya usado en la banda ≥40rem desde IMPLEMENTACION-01).
        const resultadosY = await page.locator('[data-zone="resultados"]').boundingBox().then(b => b.y);
        const productosY  = await page.locator('[data-zone="productos"]').boundingBox().then(b => b.y);
        const configY     = await page.locator('[data-zone="config"]').boundingBox().then(b => b.y);
        expect(resultadosY).toBeLessThan(productosY);
        expect(resultadosY).toBeLessThan(configY);

        // Productos y Config arrancan colapsados: el toggle existe, aria-expanded=false,
        // y el contenido real queda oculto (mismo DOM, no una segunda representación).
        const prodToggle = page.locator('#cotizacion-productos-toggle');
        const configToggle = page.locator('#cotizacion-config-toggle');
        await expect(prodToggle).toBeVisible();
        await expect(configToggle).toBeVisible();
        await expect(prodToggle).toHaveAttribute('aria-expanded', 'false');
        await expect(configToggle).toHaveAttribute('aria-expanded', 'false');
        await expect(page.locator('#cotizacion-producto-buscar')).toBeHidden();
        await expect(page.locator('#cotizacion-cliente-buscar')).toBeHidden();

        // El CTA (Total + Simular/Guardar) nunca se colapsa, aunque ambos paneles
        // secundarios estén cerrados — es lo único que debe verse sin expandir nada.
        await expect(page.locator('#cotizacion-simular')).toBeVisible();
        await expect(page.locator('#cotizacion-guardar')).toBeVisible();

        // Abrir Productos: el buscador y el carrito quedan accesibles (item 7 del lote).
        await prodToggle.click();
        await expect(prodToggle).toHaveAttribute('aria-expanded', 'true');
        await expect(page.locator('#cotizacion-producto-buscar')).toBeVisible();

        const added = await agregarProductoSimulador(page); // ya expandido, no vuelve a togglear
        test.skip(!added, 'Sin productos disponibles en el entorno de prueba');

        // Editar cantidad del producto agregado (input real dentro del carrito).
        const cantidadInput = page.locator('[data-cotizacion-cantidad-index="0"]');
        await expect(cantidadInput).toBeVisible({ timeout: 3_000 });
        await cantidadInput.fill('2');
        await cantidadInput.dispatchEvent('input');

        // Colapsar Productos de nuevo antes de simular (no debe romper el estado).
        await prodToggle.click();
        await expect(prodToggle).toHaveAttribute('aria-expanded', 'false');

        // Abrir Configuración para cambiar el cliente (best-effort: puede no haber
        // clientes cargados en el entorno de prueba, no bloquea el resto del test).
        await configToggle.click();
        await expect(configToggle).toHaveAttribute('aria-expanded', 'true');
        await expect(page.locator('#cotizacion-cliente-buscar')).toBeVisible();

        const clienteInput = page.locator('#cotizacion-cliente-buscar');
        await clienteInput.fill('a');
        await page.waitForTimeout(600);
        const clienteBtn = page.locator('#cotizacion-clientes-dropdown button').first();
        if (await clienteBtn.isVisible({ timeout: 2_000 }).catch(() => false)) {
            await clienteBtn.click();
        }

        // El estado pending (setQuoteState) ya está cubierto en desktop por T6/T7/T8
        // con el mismo cableado — acá se valida el flujo Simular → Guardar en mobile.
        await page.click('#cotizacion-simular');
        await page.locator('#cotizacion-resultados').waitFor({ state: 'visible', timeout: 15_000 });
        await page.locator('#cotizacion-resultados-tbody tr').first().waitFor({ state: 'visible', timeout: 5_000 });

        // Sin scroll horizontal de página NI de la tabla interna (antes desbordaba
        // ~266px — reflow a filas compactas en vez de columnas de tabla).
        expect(await noHorizontalOverflow(page)).toBeTruthy();
        const tablaOverflow = await page.evaluate(() => {
            const el = document.getElementById('cotizacion-resultados');
            return el.scrollWidth - el.clientWidth;
        });
        expect(tablaOverflow).toBeLessThanOrEqual(2);

        // Abrir/cerrar un grupo expandible (Tarjeta de crédito o Crédito personal,
        // el que exista en este entorno) sigue funcionando sin cambios de JS.
        const parent = page.locator('#cotizacion-resultados-tbody tr.parent').first();
        if (await parent.count() > 0) {
            const gkey = await parent.getAttribute('data-group');
            const details = page.locator(`#cotizacion-resultados-tbody tr.detail[data-g="${gkey}"]`);
            await expect(details.first()).toBeVisible();
            await parent.click();
            await expect(details.first()).toBeHidden({ timeout: 2_000 });
            await parent.click();
            await expect(details.first()).toBeVisible({ timeout: 2_000 });
        }

        // Seleccionar una opción abre el drawer de detalle (mismo comportamiento
        // que desktop — ahí vive el Recargo, oculto de la fila compacta).
        const selectable = page.locator('#cotizacion-resultados-tbody tr[data-cotizacion-opcion-key]').first();
        if (await selectable.count() > 0) {
            await selectable.click();
            await expect(page.locator('#modal-plan')).toBeVisible({ timeout: 3_000 });
            await page.keyboard.press('Escape');
            await expect(page.locator('#modal-plan')).not.toBeVisible({ timeout: 3_000 });
        }

        // Guardar sigue disponible con ambos paneles colapsados. COTIZACION-SIMULAR-
        // GUARDAR-DIRECTO-01: ya no abre un modal — guarda directo (sin cliente de
        // sistema en este flujo, así que no encadena a Pasar a venta automáticamente).
        const guardarBtn = page.locator('#cotizacion-guardar');
        await expect(guardarBtn).toBeVisible();
        if (await guardarBtn.isEnabled()) {
            await guardarBtn.click();
            await expect(page.locator('#cotizacion-pasar-venta')).toBeVisible({ timeout: 20_000 });
        }
    });
});
