// @ts-check
/**
 * CREDITO-VISUAL-03: la excepción documental (#panel-excepcion-crediticia) se movió
 * de después de #panel-configuracion-credito (a 2000+px / 2.25-3.5 pantallas de
 * "Documentación crediticia incompleta") a inmediatamente después de
 * #panel-documentacion-faltante, dentro de #credito-zona-bloqueantes — mismo
 * panel/formulario/ids/JS, sin duplicar autoridad.
 *
 * Este spec protege lo que el audit y el micro-lote agregaron en la nueva posición:
 * distancia real reducida, aria-controls/aria-expanded sincronizado con el mismo
 * estado que ya gobernaba mostrar/ocultar el panel, y foco devuelto al disparador al
 * cancelar (mismo estándar que "foco vuelve al disparador al cerrar" de los modales).
 *
 * Estrategia de datos: igual que venta-excepcion-documental-reload.spec.js y
 * credito-visual-otros-motivos.spec.js — venta descartable real (Efectivo) + GET de
 * Edit reescrito para preseleccionar Crédito Personal, y
 * /api/ventas/PrevalidarCredito mockeado con documentación faltante controlada.
 */
const { test, expect } = require('playwright/test');
const { searchAndSelectClient, activarFiltroStock, addProduct } = require('./helpers');

test.use({ storageState: 'e2e/.auth/user.json' });

const DOCUMENTACION_FALTANTE = {
    resultado: 1, colorBadge: 'warning', textoEstado: 'Requiere autorización',
    limiteCredito: 100000, creditoUtilizado: 0, cupoDisponible: 100000,
    documentacionCompleta: false, documentosFaltantes: ['DNI'], documentosVencidos: [],
    motivos: [{ categoria: 1, titulo: 'Documentación', descripcion: 'Faltan documentos: DNI', esBloqueante: true }],
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

/** Preselecciona Crédito Personal en el GET de Edit (mismo patrón que los specs hermanos). */
function mockEditConCreditoPersonal(page, editUrl) {
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

/** Deja el wizard en el paso Crédito con documentación faltante + excepción disponible. */
async function irAPasoCreditoConDocumentacionFaltante(page) {
    const id = await crearVentaThrowaway(page);
    const editUrl = `/Venta/Edit/${id}`;

    await mockEditConCreditoPersonal(page, editUrl);
    await page.route('**/api/ventas/PrevalidarCredito*', (route) => route.fulfill({
        contentType: 'application/json',
        body: JSON.stringify(DOCUMENTACION_FALTANTE),
    }));

    await page.goto(editUrl, { waitUntil: 'domcontentloaded' });
    await expect(page.locator('#venta-form')).toBeVisible({ timeout: 15_000 });
    await page.locator('#step-btn-credito').click();
    await expect(page.locator('#panel-documentacion-faltante')).toBeVisible({ timeout: 10_000 });
    await expect(page.locator('#btn-aplicar-excepcion')).toBeVisible();
}

test.describe('CREDITO-VISUAL-03 — excepción documental junto al bloqueante', () => {
    test('estructura: distancia real entre Documentación y Aplicar Excepción queda mínima', async ({ page }) => {
        await irAPasoCreditoConDocumentacionFaltante(page);

        // Estado inicial: CTA visible, panel de edición (textarea) cerrado.
        await expect(page.locator('#panel-excepcion-activa')).toBeHidden();
        await expect(page.locator('#btn-aplicar-excepcion')).toHaveAttribute('aria-controls', 'panel-excepcion-activa');
        await expect(page.locator('#btn-aplicar-excepcion')).toHaveAttribute('aria-expanded', 'false');

        // Viewports obligatorios del micro-lote (1024x720 se fuerza acá porque no hay
        // project dedicado en playwright.config.js para ese ancho).
        const viewports = [
            { width: 1440, height: 900 },
            { width: 1024, height: 720 },
            { width: 390, height: 844 },
            { width: 360, height: 800 },
        ];

        for (const vp of viewports) {
            await page.setViewportSize(vp);
            const distancia = await page.evaluate(() => {
                const doc = document.getElementById('panel-documentacion-faltante');
                const btn = document.getElementById('btn-aplicar-excepcion');
                if (!doc || !btn) return null;
                const docRect = doc.getBoundingClientRect();
                const btnRect = btn.getBoundingClientRect();
                return Math.round(btnRect.top - docRect.bottom);
            });
            expect(distancia, `distancia Documentación→Aplicar Excepción en ${vp.width}x${vp.height}`).not.toBeNull();
            // Antes (CREDITO-VISUAL-03-AUDIT): 2022–2543px. Objetivo: 0–150px reales de
            // separación entre bloques; se tolera hasta 400px para no atar el test a un
            // valor de spacing arbitrario.
            expect(Math.abs(distancia ?? 9999), `${vp.width}x${vp.height}: ${distancia}px`).toBeLessThan(400);

            // Sin overflow horizontal en mobile (caso I).
            if (vp.width <= 412) {
                const overflow = await page.evaluate(() =>
                    document.documentElement.scrollWidth - document.documentElement.clientWidth);
                expect(overflow, `overflow horizontal en ${vp.width}px`).toBeLessThanOrEqual(1);
            }
        }
    });

    test('abrir: aria-expanded true y foco en el textarea (caso D)', async ({ page }) => {
        await irAPasoCreditoConDocumentacionFaltante(page);

        await page.locator('#btn-aplicar-excepcion').click();

        await expect(page.locator('#panel-excepcion-activa')).toBeVisible();
        await expect(page.locator('#btn-aplicar-excepcion')).toHaveAttribute('aria-expanded', 'true');
        await expect(page.locator('#txt-excepcion-documental')).toBeFocused();
    });

    test('cancelar: aria-expanded false y foco vuelve al disparador (caso E)', async ({ page }) => {
        await irAPasoCreditoConDocumentacionFaltante(page);

        await page.locator('#btn-aplicar-excepcion').click();
        await expect(page.locator('#panel-excepcion-activa')).toBeVisible();

        await page.locator('#btn-cancelar-excepcion').click();

        await expect(page.locator('#panel-excepcion-activa')).toBeHidden();
        await expect(page.locator('#btn-aplicar-excepcion')).toBeVisible();
        await expect(page.locator('#btn-aplicar-excepcion')).toHaveAttribute('aria-expanded', 'false');
        await expect(page.locator('#btn-aplicar-excepcion')).toBeFocused();
    });

    test('confirmar sin motivo: error inline, foco en textarea, panel sigue abierto (caso F)', async ({ page }) => {
        await irAPasoCreditoConDocumentacionFaltante(page);

        await page.locator('#btn-aplicar-excepcion').click();
        await page.locator('#btn-confirmar-excepcion').click();

        await expect(page.locator('#excepcion-motivo-error')).toBeVisible();
        await expect(page.locator('#excepcion-motivo-error')).toContainText('El motivo es obligatorio');
        await expect(page.locator('#txt-excepcion-documental')).toBeFocused();
        // No se cierra ni se confirma sin motivo.
        await expect(page.locator('#panel-excepcion-activa')).toBeVisible();
        await expect(page.locator('#hdn-aplicar-excepcion')).toHaveValue('false');
    });

    test('confirmar con motivo: hidden=true, confirmación visible, sin duplicados (caso G)', async ({ page }) => {
        await irAPasoCreditoConDocumentacionFaltante(page);

        await page.locator('#btn-aplicar-excepcion').click();
        await page.locator('#txt-excepcion-documental').fill('QA: aprobado por gerencia, legajo en trámite.');
        await page.locator('#btn-confirmar-excepcion').click();

        await expect(page.locator('#hdn-aplicar-excepcion')).toHaveValue('true');
        // VENTA-CREDITO-REDESIGN-VISUAL-IMPLEMENTACION-01 retiró #excepcion-aplicada-badge:
        // la fila "Documentación — exceptuada" de Otros motivos (mostrarMotivos(), categoría 1
        // emerald) es ahora la única confirmación visible del hecho, sin duplicarla.
        const confirmacion = page.getByText('Documentación — exceptuada');
        await expect(confirmacion).toBeVisible();
        await expect(confirmacion).toHaveCount(1);
        await expect(page.locator('#panel-excepcion-crediticia')).toHaveCount(1);
        await expect(page.locator('#txt-excepcion-documental')).toHaveAttribute('readonly', '');
    });
});
