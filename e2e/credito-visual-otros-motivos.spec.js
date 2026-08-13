// @ts-check
/**
 * CREDITO-VISUAL-02A: contrato de "Otros motivos" (antes "Observaciones" automáticas,
 * ver mostrarMotivos() en venta-create.js) frente a los paneles dedicados de
 * Documentación (#panel-documentacion-faltante), Cupo (#panel-cupo-insuficiente) y
 * Mora (#panel-alerta-mora).
 *
 * Reglas protegidas:
 *  - Documentación/Cupo/Mora (CategoriaMotivo 1/2/3) NO se repiten en "Otros motivos"
 *    mientras su panel dedicado esté visible — evita redundancia cognitiva (un hecho,
 *    una explicación principal).
 *  - Si esa autoridad dedicada deja de estar visible (ej. al aplicar la excepción
 *    documental, que oculta Documentación/Cupo), el motivo debe reaparecer en "Otros
 *    motivos": es la única superficie que conserva el hecho. Este es el contrato de
 *    regresión obligatorio del audit — nunca perder información.
 *  - Categorías sin panel dedicado (ej. 5=Configuración) siempre se muestran ahí.
 *  - El heading pasó de "Observaciones" a "Otros motivos"; el textarea de notas libres
 *    del vendedor (#Observaciones) no cambió de nombre.
 *
 * Estrategia de datos: igual que venta-excepcion-documental-reload.spec.js — venta
 * descartable real (Efectivo) + GET de Edit reescrito para preseleccionar Crédito
 * Personal, y /api/ventas/PrevalidarCredito mockeado con un payload de motivos
 * controlado. Evita depender de un cliente de QA con mora/cupo/documentación reales.
 */
const { test, expect } = require('playwright/test');
const { searchAndSelectClient, activarFiltroStock, addProduct } = require('./helpers');

test.use({ storageState: 'e2e/.auth/user.json' });

// CategoriaMotivo (ver ViewModels/PrevalidacionResultViewModel.cs):
// 1=Documentacion, 2=Cupo, 3=Mora, 5=Configuracion. Se incluye Configuración (sin
// panel dedicado) para confirmar que esa categoría siempre se muestra, filtro aparte.
const NO_VIABLE_CON_TODAS_LAS_CATEGORIAS = {
    resultado: 2, colorBadge: 'danger', textoEstado: 'No viable para crédito',
    limiteCredito: 0, creditoUtilizado: 0, cupoDisponible: 0,
    documentacionCompleta: false,
    documentosFaltantes: ['DNI'], documentosVencidos: [],
    tieneMora: true, diasMora: 40, montoMora: 15000,
    motivos: [
        { categoria: 1, titulo: 'Documentación', descripcion: 'Faltan documentos: DNI', esBloqueante: true },
        { categoria: 2, titulo: 'Cupo', descripcion: 'Cupo insuficiente. Disponible: $ 0', esBloqueante: true },
        { categoria: 3, titulo: 'Mora activa', descripcion: 'Mora activa: 40 días', esBloqueante: false },
        { categoria: 5, titulo: 'Configuración', descripcion: 'Motivo sin panel dedicado (QA)', esBloqueante: false },
    ],
};

/** Crea una venta descartable real (cliente + producto de QA, Efectivo) y devuelve su Id. */
async function crearVentaThrowaway(page) {
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
    await page.locator('#select-tipo-pago').selectOption('0'); // Efectivo
    await page.locator('#step-btn-revision').click();

    const vendedor = page.locator('#VendedorUserId');
    if (await vendedor.isVisible().catch(() => false) && !(await vendedor.inputValue())) {
        await vendedor.selectOption({ index: 1 });
    }

    await page.locator('#btn-confirmar').click();
    await page.waitForURL(/\/Venta\/(Details|Edit)\/\d+/, { timeout: 20_000 });
    const id = page.url().match(/\/(?:Details|Edit)\/(\d+)/)?.[1];
    expect(id).toBeTruthy();
    return id;
}

/**
 * Reescribe el GET de /Venta/Edit/{id} para preseleccionar Crédito Personal (mismo
 * approach que mockEditConCreditoPersonal en venta-excepcion-documental-reload.spec.js,
 * sin la parte de excepción ya hidratada: acá se aplica en vivo desde la UI).
 */
function mockEditConCreditoPersonalPreseleccionado(page, editUrl) {
    const pattern = new RegExp(`${editUrl}$`);
    return page.route(pattern, async (route, request) => {
        if (request.method() !== 'GET') { await route.continue(); return; }
        const response = await route.fetch();
        let body = await response.text();
        body = body.replace(/<select id="select-tipo-pago"[\s\S]*?<\/select>/, (block) => {
            const sinSelected = block.replace(/\sselected="selected"/g, '');
            return sinSelected.replace('<option value="5"', '<option value="5" selected="selected"');
        });
        await route.fulfill({ response, body });
    });
}

test.describe('CREDITO-VISUAL-02A — Otros motivos vs. paneles dedicados', () => {
    test('Documentación/Cupo/Mora no se repiten cuando su panel dedicado está visible', async ({ page }) => {
        const id = await crearVentaThrowaway(page);
        const editUrl = `/Venta/Edit/${id}`;

        await mockEditConCreditoPersonalPreseleccionado(page, editUrl);
        await page.route('**/api/ventas/PrevalidarCredito*', (route) => route.fulfill({
            contentType: 'application/json',
            body: JSON.stringify(NO_VIABLE_CON_TODAS_LAS_CATEGORIAS),
        }));

        await page.goto(editUrl, { waitUntil: 'domcontentloaded' });
        await expect(page.locator('#venta-form')).toBeVisible({ timeout: 15_000 });
        await page.locator('#step-btn-credito').click();

        await expect(page.locator('#panel-documentacion-faltante')).toBeVisible({ timeout: 10_000 });
        await expect(page.locator('#panel-cupo-insuficiente')).toBeVisible();
        await expect(page.locator('#panel-alerta-mora')).toBeVisible();

        // "Otros motivos": microcopy nuevo + sólo la categoría sin panel dedicado (5).
        const panelMotivos = page.locator('#panel-motivos');
        await expect(panelMotivos).toBeVisible();
        await expect(page.locator('#panel-motivos > p')).toHaveText('Otros motivos');
        await expect(panelMotivos).toContainText('Configuración');
        await expect(panelMotivos).not.toContainText('Documentación');
        await expect(panelMotivos).not.toContainText('Cupo insuficiente');
        await expect(panelMotivos).not.toContainText('Mora activa');

        // Notas libres del vendedor: sigue llamándose "Observaciones", sin cambios.
        await expect(page.locator('#Observaciones')).toBeVisible();
    });

    test('Documentación/Cupo reaparecen en Otros motivos si la excepción oculta sus paneles dedicados (fallback obligatorio)', async ({ page }) => {
        const id = await crearVentaThrowaway(page);
        const editUrl = `/Venta/Edit/${id}`;

        await mockEditConCreditoPersonalPreseleccionado(page, editUrl);
        await page.route('**/api/ventas/PrevalidarCredito*', (route) => route.fulfill({
            contentType: 'application/json',
            body: JSON.stringify(NO_VIABLE_CON_TODAS_LAS_CATEGORIAS),
        }));

        await page.goto(editUrl, { waitUntil: 'domcontentloaded' });
        await expect(page.locator('#venta-form')).toBeVisible({ timeout: 15_000 });
        await page.locator('#step-btn-credito').click();
        await expect(page.locator('#panel-documentacion-faltante')).toBeVisible({ timeout: 10_000 });

        const panelMotivos = page.locator('#panel-motivos');
        await page.locator('#btn-aplicar-excepcion').click();

        await expect(page.locator('#panel-documentacion-faltante')).toBeHidden();
        await expect(page.locator('#panel-cupo-insuficiente')).toBeHidden();
        // Mora conserva su panel dedicado: sigue sin duplicarse.
        await expect(page.locator('#panel-alerta-mora')).toBeVisible();

        await expect(panelMotivos).toBeVisible();
        await expect(panelMotivos).toContainText('Documentación');
        await expect(panelMotivos).toContainText('Cupo');
        await expect(panelMotivos).not.toContainText('Mora activa');

        // Cancelar restaura la autoridad dedicada y vuelve a suprimir Documentación/Cupo.
        await page.locator('#btn-cancelar-excepcion').click();
        await expect(page.locator('#panel-documentacion-faltante')).toBeVisible();
        await expect(page.locator('#panel-cupo-insuficiente')).toBeVisible();
        await expect(panelMotivos).not.toContainText('Documentación');
        await expect(panelMotivos).not.toContainText('Cupo insuficiente');
    });
});
