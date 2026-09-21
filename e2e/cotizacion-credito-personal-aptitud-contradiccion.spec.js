// @ts-check
/**
 * COTIZ-APTITUD-01 — regresión crítica reportada por el usuario, 2026-09-17.
 *
 * En Cotización (y en el paso "Cotizar" embebido de Venta/Create, que reusa el
 * mismo parcial/JS — ver Views/Venta/_VentaWizardForm.cshtml), la card de
 * aptitud del Cliente podía mostrar "Crédito personal → No apto" mientras la
 * tabla de comparación seguía pintando el grupo "Crédito personal" como
 * "Disponible" con sus planes en "Elegir" — la misma pantalla contradiciéndose
 * a sí misma.
 *
 * Causa raíz (wwwroot/js/cotizacion-simulador.js): simular() pinta la tabla
 * (renderResultado) de forma síncrona, ANTES de que evaluarAptitudCredito()
 * resuelva — si el cliente se elige antes de agregar productos, esa consulta
 * ni siquiera llega a dispararse en ese momento (monto=0). El resultado ya
 * llega después y actualiza la card de aptitud, pero nunca repintaba la tabla.
 * Fix: evaluarAptitudCredito() ahora llama a refrescarTablaPorAptitud(), que
 * vuelve a pintar la tabla con la aptitud fresca (preservando la selección
 * vigente si sigue siendo válida).
 *
 * Requiere un cliente cuya evaluación real (IClienteAptitudService, la misma
 * que usa Cliente/Details) resuelva en "No apto" (mora y/o documentación) —
 * en la LocalDB de desarrollo de este repo existe el cliente "alan miñana"
 * (DNI configurable via E2E_CLIENTE_NOAPTO_DNI, default 35996614). Si ese
 * cliente no existe en el entorno donde corre este spec, se salta con un
 * mensaje explícito en vez de fallar por datos ausentes.
 */
const { test, expect } = require('playwright/test');

const DNI_NO_APTO = process.env.E2E_CLIENTE_NOAPTO_DNI || '35996614';

test.use({ storageState: 'e2e/.auth/user.json' });

test.beforeEach(async ({ page }) => {
    await page.route('**/fonts.googleapis.com/**', route => route.abort()).catch(() => null);
    await page.route('**/fonts.gstatic.com/**', route => route.abort()).catch(() => null);
});

/** Selecciona el cliente NoApto ANTES de agregar productos (secuencia que dispara el bug: la
 *  consulta de aptitud dispara con monto=0 y no llega a evaluar en ese momento). Devuelve false
 *  si el cliente de prueba no existe en este entorno. */
async function seleccionarClienteNoApto(page) {
    const buscar = page.locator('#cotizacion-cliente-buscar');
    await buscar.fill(DNI_NO_APTO);
    await page.waitForTimeout(600); // debounce 220ms + margen de red (mismo criterio que agregarPrimerProducto)
    const opcion = page.locator('#cotizacion-clientes-dropdown button').first();
    const visible = await opcion.isVisible({ timeout: 3_000 }).catch(() => false);
    if (!visible) return false;
    const texto = await opcion.innerText().catch(() => '');
    if (!texto.includes(DNI_NO_APTO.replace(/(\d)(?=(\d{3})+(?!\d))/g, '$1.'))) return false;
    await opcion.click();
    return true;
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

test.describe('Cotización — aptitud crediticia vs. tabla de comparación (COTIZ-APTITUD-01)', () => {
    test('Cliente No apto: la tabla nunca ofrece "Elegir" en Crédito personal, ni auto-selecciona sus planes', async ({ page }) => {
        await page.setViewportSize({ width: 1366, height: 768 });
        await page.goto('/Cotizacion', { waitUntil: 'domcontentloaded' });
        await expect(page.locator('#cotizacion-simular')).toBeVisible({ timeout: 10_000 });

        const clienteEncontrado = await seleccionarClienteNoApto(page);
        test.skip(!clienteEncontrado, `Cliente de prueba con DNI ${DNI_NO_APTO} no existe en este entorno — ver E2E_CLIENTE_NOAPTO_DNI.`);

        const productoAgregado = await agregarPrimerProducto(page);
        expect(productoAgregado).toBeTruthy();

        await page.click('#cotizacion-simular');
        await expect(page.locator('#cotizacion-resultados')).toBeVisible({ timeout: 10_000 });

        // La card de aptitud (fuente real, IClienteAptitudService) debe resolver "No apto".
        const aptitudCard = page.locator('#cotizacion-aptitud-credito');
        await expect(aptitudCard).toContainText('No apto', { timeout: 10_000 });

        // La fila/grupo "Crédito personal" en la tabla de comparación tiene que reflejar el
        // MISMO veredicto — nunca "Disponible" con "Elegir" habilitado (la contradicción
        // reportada). Puede no tener fila (sin planes) o tenerla ya bloqueada.
        const filaCredito = page.locator('#cotizacion-resultados-tbody tr').filter({ hasText: 'Credito personal' });
        if (await filaCredito.count() > 0) {
            await expect(filaCredito.first().locator('.pill')).toHaveText(/No apto/);
            await expect(filaCredito.first().locator('[data-cotizacion-elegir]')).toHaveCount(0);
        }

        // Ninguna fila hija de Crédito personal puede ofrecer "Elegir": deben quedar como
        // "No disponible" (rt-accion-bloqueada), sin botón data-cotizacion-elegir.
        const filasHijasCredito = page.locator('#cotizacion-resultados-tbody tr.detail[data-g]').filter({
            has: page.locator('.plan-nombre')
        });
        const totalHijas = await filasHijasCredito.count();
        for (let i = 0; i < totalHijas; i++) {
            const fila = filasHijasCredito.nth(i);
            const grupo = await fila.getAttribute('data-g');
            const esCreditoPersonal = await page.locator(`tr.parent[data-group="${grupo}"]`).filter({ hasText: 'Credito personal' }).count() > 0;
            if (!esCreditoPersonal) continue;
            await expect(fila.locator('button[data-cotizacion-elegir]')).toHaveCount(0);
            await expect(fila.locator('.rt-accion-bloqueada')).toBeVisible();
        }

        // La selección vigente (auto-selección de "mejor precio" o manual) nunca puede ser un
        // plan de Crédito personal cuando el cliente es No apto.
        const seleccionResumen = await page.locator('#cotizacion-seleccion-resumen').innerText();
        expect(seleccionResumen).not.toMatch(/Crédito personal/i);
    });
});
