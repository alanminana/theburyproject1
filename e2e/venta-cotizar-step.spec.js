// @ts-check
/**
 * Paso "Cotizar" del wizard de Venta/Create.
 *
 * Regresión cubierta: el cotizador había desaparecido de Venta/Create y el único
 * acceso era un enlace que volvía a la misma pantalla. Ahora es el paso 1 del
 * wizard, embebido con el parcial Views/Cotizacion/_CotizadorForm.cshtml.
 *
 * En Edit la pestaña no debe existir: el parcial del wizard es compartido.
 */
const { test, expect } = require('playwright/test');
const { CTA_PRIMARIO_VISIBLE } = require('./helpers');

test.use({ storageState: 'e2e/.auth/user.json' });

const VIEWPORTS = [
    { name: '360x800', width: 360, height: 800 },
    { name: '390x844', width: 390, height: 844 },
    { name: '768x1024', width: 768, height: 1024 },
    { name: '1080x720', width: 1080, height: 720 },
    { name: '1280x720', width: 1280, height: 720 },
    { name: '1366x768', width: 1366, height: 768 },
    { name: '1600x900', width: 1600, height: 900 },
    { name: '1920x1080', width: 1920, height: 1080 },
    { name: '1920x1200', width: 1920, height: 1200 },
];

async function gotoCreate(page) {
    await page.goto('/Venta/Create', { waitUntil: 'domcontentloaded' });
    await expect(page).not.toHaveURL(/Identity\/Account\/Login/);
    await expect(page.locator('#venta-form')).toBeVisible({ timeout: 15_000 });
}

test.describe('Venta/Create — paso Cotizar', () => {
    test('Cotizar es el primer paso y arranca desplegado', async ({ page }) => {
        await gotoCreate(page);

        const cotizar = page.locator('#step-btn-cotizar');
        const cliente = page.locator('#step-btn-cliente');

        await expect(cotizar).toBeVisible();
        await expect(cotizar).toHaveAttribute('aria-selected', 'true');
        await expect(cotizar).toHaveAttribute('tabindex', '0');
        await expect(cotizar).toHaveAttribute('aria-controls', 'step-panel-cotizar');

        // Orden: Cotizar antes que Cliente dentro del mismo tablist.
        const orden = await page.locator('.venta-wizard-tablist [role="tab"]').evaluateAll(
            (tabs) => tabs.map((t) => t.id)
        );
        expect(orden.indexOf('step-btn-cotizar')).toBe(0);
        expect(orden.indexOf('step-btn-cotizar')).toBeLessThan(orden.indexOf('step-btn-cliente'));

        // Panel desplegado sin clic previo; el resto del wizard oculto.
        await expect(page.locator('#step-panel-cotizar')).toBeVisible();
        await expect(page.locator('#step-panel-cotizar [data-cotizacion-simulador]')).toBeVisible();
        await expect(page.locator('#step-panel-cliente')).toBeHidden();
        await expect(page.locator('#step-panel-productos')).toBeHidden();
        await expect(cliente).toHaveAttribute('aria-selected', 'false');

        // Numeración: Cotizar 1, Cliente 2.
        await expect(cotizar.locator('.vm-step-tab__num')).toHaveText('1');
        await expect(cliente.locator('.vm-step-tab__num')).toHaveText('2');
    });

    test('cambiar de pestaña alterna paneles sin navegar a otra pantalla', async ({ page }) => {
        await gotoCreate(page);

        await page.locator('#step-btn-cliente').click();
        await expect(page.locator('#step-panel-cliente')).toBeVisible();
        await expect(page.locator('#step-panel-cotizar')).toBeHidden();
        await expect(page).toHaveURL(/\/Venta\/Create/);

        await page.locator('#step-btn-cotizar').click();
        await expect(page.locator('#step-panel-cotizar')).toBeVisible();
        await expect(page.locator('#step-panel-cliente')).toBeHidden();
        await expect(page).toHaveURL(/\/Venta\/Create/);
    });

    test('el tablist navega con flechas, Home y End incluyendo Cotizar', async ({ page }) => {
        await gotoCreate(page);
        const cotizar = page.locator('#step-btn-cotizar');
        const cliente = page.locator('#step-btn-cliente');

        await cotizar.focus();
        await page.keyboard.press('ArrowRight');
        await expect(cliente).toBeFocused();
        await expect(cliente).toHaveAttribute('aria-selected', 'true');
        await expect(cotizar).toHaveAttribute('tabindex', '-1');

        await page.keyboard.press('ArrowLeft');
        await expect(cotizar).toBeFocused();
        await expect(cotizar).toHaveAttribute('aria-selected', 'true');

        // End = último paso habilitado. Sin cliente cargado, Productos en adelante
        // siguen deshabilitados, así que el último habilitado es Cliente.
        await page.keyboard.press('End');
        await expect(cliente).toBeFocused();

        await page.keyboard.press('Home');
        await expect(cotizar).toBeFocused();

        // Mover foco entre tabs no envía el formulario ni abandona la pantalla.
        await expect(page).toHaveURL(/\/Venta\/Create/);
    });

    test('la acción principal cotiza en vez de guardar la venta', async ({ page }) => {
        await gotoCreate(page);

        // COTIZACION-WORKSTATION-01 (§9): durante Cotizar el CTA del header queda
        // oculto — la acción primaria vive junto a la franja de Totales del cotizador,
        // al lado del resumen que la motiva. El botón global sigue existiendo (con su
        // data-wizard-action intacto, que es el contrato con venta-page-wizard.js); fuera de
        // Cotizar lo reemplaza el CTA único del paso (ver más abajo).
        const primary = page.locator('[data-wizard-primary]').first();
        await expect(primary).toHaveAttribute('data-wizard-action', 'simular-cotizacion');
        await expect(primary).toBeHidden();

        const ctaCotizador = page.locator('#cotizacion-simular');
        // La simulación es automática: el CTA sigue en el DOM (contrato) pero oculto.
        await expect(ctaCotizador).toBeHidden();
        await expect(ctaCotizador).toContainText(/Simular cotizaci.n/);
        await expect(ctaCotizador).not.toContainText(/Confirmar operaci.n|Guardar Operaci.n|Siguiente/);

        // El CTA de la venta no queda visible mientras se cotiza.
        await expect(page.locator('#btn-confirmar')).toBeHidden();

        // Fuera de Cotizar hay exactamente un CTA primario a la vista (a ≥1280px el del sidebar; el
        // del header queda oculto para no duplicarlo) y es el que avanza el wizard.
        await page.locator('#step-btn-cliente').click();
        const ctaVisible = page.locator(CTA_PRIMARIO_VISIBLE);
        await expect(ctaVisible).toHaveCount(1);
        await expect(ctaVisible).toBeVisible();
        await expect(ctaVisible).toContainText(/Siguiente/);
    });

    test('el cotizador simula desde su panel sin salir de Venta/Create', async ({ page }) => {
        const consoleErrors = [];
        const failedResponses = [];
        page.on('console', (msg) => { if (msg.type() === 'error') consoleErrors.push(msg.text()); });
        page.on('response', (res) => {
            if (res.status() >= 400 && !/favicon/i.test(res.url())) failedResponses.push(`${res.status()} ${res.url()}`);
        });

        await gotoCreate(page);

        await page.locator('#cotizacion-producto-buscar').fill('an');
        const opcion = page.locator('#cotizacion-productos-dropdown > *').first();
        await opcion.waitFor({ state: 'visible', timeout: 10_000 }).catch(() => { });
        test.skip(!(await opcion.isVisible().catch(() => false)), 'El entorno no expone producto de QA para cotizar.');
        await opcion.click();
        await expect(page.locator('#cotizacion-productos-tbody > *')).toHaveCount(1);

        // COTIZACION-WORKSTATION-01 (§9): se dispara desde el CTA contextual del
        // cotizador, junto a la franja de Totales (antes desde el CTA del header, que
        // delegaba en este mismo botón por id y ahora se oculta durante Cotizar para
        // no duplicar la misma intención a media pantalla de distancia).
        await page.evaluate(() => document.getElementById('cotizacion-simular').click());
        await expect(page.locator('#cotizacion-resultados')).toBeVisible({ timeout: 20_000 });
        await expect(page.locator('#cotizacion-resultados-tbody > *').first()).toBeVisible();
        await expect(page.locator('#estado-banner')).toContainText(/Simulada/);

        // No navegó ni cambió de paso.
        await expect(page).toHaveURL(/\/Venta\/Create/);
        await expect(page.locator('#step-btn-cotizar')).toHaveAttribute('aria-selected', 'true');

        expect(consoleErrors, `errores de consola: ${consoleErrors.join(' | ')}`).toHaveLength(0);
        expect(failedResponses, `requests fallidos: ${failedResponses.join(' | ')}`).toHaveLength(0);
    });

    test('no hay formularios anidados ni ids duplicados con el cotizador embebido', async ({ page }) => {
        await gotoCreate(page);

        const dom = await page.evaluate(() => {
            const ids = [...document.querySelectorAll('[id]')].map((e) => e.id);
            const ventaForm = document.getElementById('venta-form');
            return {
                nestedForms: document.querySelectorAll('form form').length,
                duplicatedIds: [...new Set(ids.filter((v, i) => ids.indexOf(v) !== i))],
                // Un solo antiforgery dentro del form de venta: dos romperían el POST.
                tokensEnVentaForm: ventaForm.querySelectorAll('input[name="__RequestVerificationToken"]').length,
                // El cotizador no aporta inputs con name, así que no altera el POST de la venta.
                inputsConNombreEnCotizador: [...document.querySelectorAll('[data-cotizacion-simulador] [name]')].map((e) => e.getAttribute('name')),
                scriptsSimulador: [...document.querySelectorAll('script[src*="cotizacion-simulador"]')].length,
            };
        });

        expect(dom.nestedForms).toBe(0);
        expect(dom.duplicatedIds).toEqual([]);
        expect(dom.tokensEnVentaForm).toBe(1);
        expect(dom.inputsConNombreEnCotizador).toEqual([]);
        // cotizacion-simulador-ui.js + cotizacion-simulador.js, una vez cada uno.
        expect(dom.scriptsSimulador).toBe(2);
    });

    for (const viewport of VIEWPORTS) {
        test(`el paso Cotizar entra sin overflow global en ${viewport.name}`, async ({ page }) => {
            await page.setViewportSize({ width: viewport.width, height: viewport.height });
            await gotoCreate(page);

            const medidas = await page.evaluate(() => {
                const tablist = document.querySelector('.venta-wizard-tablist');
                const tab = document.getElementById('step-btn-cotizar');
                const panel = document.getElementById('step-panel-cotizar');
                const tabRect = tab.getBoundingClientRect();
                const listRect = tablist.getBoundingClientRect();
                return {
                    overflowGlobal: document.documentElement.scrollWidth > document.documentElement.clientWidth + 1,
                    // El tablist puede scrollear localmente, pero Cotizar arranca visible.
                    cotizarDentroDelTablist: tabRect.left >= listRect.left - 1 && tabRect.right <= listRect.right + 1,
                    panelAncho: panel.getBoundingClientRect().width,
                    contenedorAncho: panel.parentElement.getBoundingClientRect().width,
                    // Con Cotizar activo el workspace de la venta no ocupa espacio.
                    workspaceOculto: [...document.querySelectorAll('[data-venta-workspace]')]
                        .every((n) => getComputedStyle(n).display === 'none'),
                };
            });

            expect(medidas.overflowGlobal, 'overflow horizontal de página').toBeFalsy();
            expect(medidas.cotizarDentroDelTablist, 'Cotizar visible al inicio del tablist').toBeTruthy();
            expect(medidas.panelAncho).toBeGreaterThan(medidas.contenedorAncho - 2);
            expect(medidas.workspaceOculto).toBeTruthy();
        });
    }
});

test.describe('Venta/Edit — sin paso Cotizar', () => {
    test('Edit no expone Cotizar y conserva Cliente como paso inicial', async ({ page }) => {
        // El listado no publica enlaces a Edit (sólo estados editables lo permiten),
        // así que se sondea sin mutar datos: el primer id cuyo GET devuelva 200.
        await page.goto('/Venta/Index', { waitUntil: 'domcontentloaded' });
        const editableId = await page.evaluate(async () => {
            const ids = [...new Set([...document.querySelectorAll('a[href*="/Venta/Details/"]')]
                .map((a) => a.getAttribute('href').match(/\/Details\/(\d+)/)?.[1])
                .filter(Boolean))].slice(0, 15);
            for (const id of ids) {
                const res = await fetch(`/Venta/Edit/${id}`, { redirect: 'manual' });
                if (res.status === 200) return id;
            }
            return null;
        });
        test.skip(!editableId, 'No hay venta en estado editable en el entorno de QA.');

        await page.goto(`/Venta/Edit/${editableId}`, { waitUntil: 'domcontentloaded' });
        await expect(page.locator('#venta-form')).toBeVisible({ timeout: 15_000 });

        await expect(page.locator('#step-btn-cotizar')).toHaveCount(0);
        await expect(page.locator('#step-panel-cotizar')).toHaveCount(0);
        await expect(page.locator('[data-cotizacion-simulador]')).toHaveCount(0);

        const cliente = page.locator('#step-btn-cliente');
        await expect(cliente).toHaveAttribute('aria-selected', 'true');
        await expect(cliente.locator('.vm-step-tab__num')).toHaveText('1');
        await expect(page.locator('#step-panel-cliente')).toBeVisible();

        // La precarga del wizard sigue en pie.
        await expect(page.locator('#hdn-cliente-id')).not.toHaveValue('');
        await page.locator('#step-btn-productos').click();
        expect(await page.locator('#tbody-detalles tr').count()).toBeGreaterThanOrEqual(1);
    });
});
