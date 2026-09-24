// @ts-check
/** Auditoría E2E focalizada de Venta/Create y Venta/Edit. */
const { test, expect } = require('playwright/test');
const path = require('path');
const fs = require('fs');
const {
    searchAndSelectClient, activarFiltroStock, addProduct, addTwoDistinctProducts,
    setGlobalTipoPago, ensureVendedorSeleccionado, TIPO_PAGO, CTA_PRIMARIO_VISIBLE,
} = require('./helpers');

test.use({ storageState: 'e2e/.auth/user.json' });

const evidenceDir = path.join('qa-evidence', 'e2e', 'venta-functional-audit');
const matrix = [
    ['360x800', 360, 800], ['390x844', 390, 844], ['768x1024', 768, 1024],
    ['1080x720', 1080, 720], ['1280x720', 1280, 720], ['1366x768', 1366, 768],
    ['1600x900', 1600, 900], ['1920x1080', 1920, 1080], ['1920x1200', 1920, 1200],
];
let editUrl = '';
const findings = [];

function ensureEvidence() { fs.mkdirSync(evidenceDir, { recursive: true }); }
async function capture(page, name) {
    ensureEvidence();
    const safe = name.replace(/[^a-z0-9_-]+/gi, '-').toLowerCase();
    const file = path.join(evidenceDir, `${safe}.png`);
    await page.screenshot({ path: file, fullPage: true });
    return file;
}
async function issue(page, title, severity, probableFile, detail) {
    const screenshot = await capture(page, `failure-${findings.length + 1}-${title}`);
    findings.push({ title, severity, probableFile, detail, screenshot });
}
async function observe(page, tag) {
    const result = await page.evaluate(() => ({
        overflow: document.documentElement.scrollWidth > document.documentElement.clientWidth + 1,
        scrollWidth: document.documentElement.scrollWidth,
        clientWidth: document.documentElement.clientWidth,
        active: document.activeElement?.id || document.activeElement?.getAttribute('aria-label') || document.activeElement?.tagName,
        duplicateConfirm: document.querySelectorAll('#btn-confirmar').length,
        sticky: [...document.querySelectorAll('*')].filter((el) => {
            const style = getComputedStyle(el);
            return (style.position === 'sticky' || style.position === 'fixed') && style.display !== 'none' && style.visibility !== 'hidden';
        }).map((el) => ({ id: el.id, cls: el.className?.toString().slice(0, 120), rect: el.getBoundingClientRect().toJSON() })),
    }));
    if (result.overflow) await issue(page, `overflow-${tag}`, 'Alta', 'wwwroot/css/venta-page-wizard.css', `scrollWidth ${result.scrollWidth} > viewport ${result.clientWidth}`);
    if (result.duplicateConfirm !== 1) await issue(page, `duplicated-confirm-${tag}`, 'Media', 'Views/Venta/_VentaWizardForm.cshtml', `#btn-confirmar aparece ${result.duplicateConfirm} veces.`);
    return result;
}
async function gotoCreate(page) {
    await page.goto('/Venta/Create', { waitUntil: 'domcontentloaded' });
    await expect(page.locator('#venta-form')).toBeVisible({ timeout: 15_000 });
    // Create abre en el paso Cotizar (ver venta-cotizar-step.spec.js). Esta auditoría
    // cubre el flujo de la venta, así que entra por Cliente, que es donde arrancaba
    // el wizard antes de que el cotizador volviera a ser el paso 1.
    await page.locator('#step-btn-cliente').click();
    await expect(page.locator('#step-panel-cliente')).toBeVisible();
}
async function addProducts(page) {
    await page.locator('#step-btn-productos').click();
    await activarFiltroStock(page);
    const products = await addTwoDistinctProducts(page);
    if (products) return products;
    const one = await addProduct(page, 'an');
    return one ? [one] : [];
}

test.afterAll(() => {
    ensureEvidence();
    fs.writeFileSync(path.join(evidenceDir, 'findings.json'), JSON.stringify(findings, null, 2));
});

test('Create: validaciones, cliente, productos, descuentos, pago, crédito, modal y guardado', async ({ page }) => {
    const consoleErrors = [];
    const failedResponses = [];
    page.on('console', (msg) => { if (msg.type() === 'error') consoleErrors.push(msg.text()); });
    page.on('response', (res) => { if (res.status() >= 400) failedResponses.push(`${res.status()} ${res.url()}`); });
    await gotoCreate(page);

    // Validación sin datos: la UI no debe enviar ni avanzar hacia una operación inválida.
    await page.locator('#btn-confirmar').click();
    await expect(page).toHaveURL(/\/Venta\/Create/);
    await capture(page, 'create-validation-empty');

    const client = await searchAndSelectClient(page);
    test.skip(!client, 'El entorno no expone cliente de QA para el autocompletado.');
    await expect(page.locator('#hdn-cliente-id')).not.toHaveValue('');
    await expect(page.locator('#info-cliente')).toBeVisible();

    const products = await addProducts(page);
    test.skip(products.length === 0, 'El entorno no expone producto con stock para QA.');
    await expect(page.locator('#tbody-detalles tr')).toHaveCount(products.length);

    const qty = page.locator('#tbody-detalles [data-quantity-input]').first();
    await expect(qty).toBeVisible();
    await qty.fill('2');
    await qty.press('Tab');
    await expect(qty).toHaveValue('2');
    await qty.fill('0');
    await qty.press('Tab');
    await expect(qty).toHaveValue('2');
    // Reagregar el mismo producto actualiza cantidad/descuento de esa fila sin crear una tercera venta.
    await page.locator('#input-buscar-producto').fill(products[0].nombre.slice(0, Math.max(2, Math.min(12, products[0].nombre.length))));
    await page.waitForTimeout(700);
    const same = page.locator('#dropdown-productos [data-id]').first();
    if (await same.isVisible().catch(() => false)) {
        await same.click();
        await page.locator('#panel-agregar-producto').waitFor({ state: 'visible' });
        await page.locator('#txt-cantidad').fill('2');
        await page.locator('#txt-descuento-item').fill('10');
        await page.locator('#btn-agregar-producto').click();
        await expect(page.locator('#total-descuento')).not.toContainText('$0,00');
    }

    // Eliminar una fila y volver a dejar al menos una para el guardado.
    if (products.length > 1) {
        const before = await page.locator('#tbody-detalles tr').count();
        await page.locator('.btn-eliminar-detalle').last().click();
        await expect(page.locator('#tbody-detalles tr')).toHaveCount(before - 1);
    }

    // El medio principal y sus campos dependientes viven juntos en Pago.
    await page.locator('#step-btn-pago').click();
    await expect(page.locator('#select-tipo-pago')).toBeVisible();
    await setGlobalTipoPago(page, TIPO_PAGO.Efectivo);
    await expect(page.locator('#select-tipo-pago')).toHaveValue(TIPO_PAGO.Efectivo);
    // Crédito se activa sólo al elegir Crédito Personal; se desactiva al volver a efectivo.
    await page.route('**/api/ventas/PrevalidarCredito*', (route) => route.fulfill({
        contentType: 'application/json',
        body: JSON.stringify({
            resultado: 2, colorBadge: 'danger', textoEstado: 'Documentación pendiente',
            limiteCredito: 100000, creditoUtilizado: 0, cupoDisponible: 100000,
            documentacionCompleta: false, documentosFaltantes: ['DNI'], documentosVencidos: [], motivos: []
        })
    }));
    await page.locator('#select-tipo-pago').selectOption(TIPO_PAGO.CreditoPersonal);
    await expect(page.locator('#step-btn-credito')).toBeVisible();
    // Hay tres presentaciones de la misma acción primaria (header, sidebar y barra sticky mobile) y
    // el layout deja visible exactamente una según el viewport: a 1366x768 es el botón del sidebar
    // (el del header se oculta ≥1280px para no duplicarlo). Se opera la que está a la vista.
    const wizardPrimary = page.locator(CTA_PRIMARIO_VISIBLE);
    await expect(wizardPrimary).toHaveCount(1);
    await expect(wizardPrimary).toContainText(/Continuar a cr.dito/);
    await wizardPrimary.click();
    await expect(page.locator('#step-btn-credito')).toHaveAttribute('aria-selected', 'true');
    // VENTA-CREDITO-REDESIGN-VISUAL-IMPLEMENTACION-01: entrar al paso Crédito ya dispara la
    // verificación automática (antes hacía falta un segundo click en "Verificar crédito" para
    // recién ahí mostrar el resultado) — el panel de resultado aparece directo, sin ese paso
    // intermedio ni ese texto en el CTA.
    await expect(page.locator('#panel-resultado-verificacion')).toBeVisible();
    await expect(page.locator('#btn-cargar-documentacion')).toBeVisible();

    // Modal de documentación: foco inicial, Escape y retorno al disparador.
    const docTrigger = page.locator('#btn-cargar-documentacion');
    await page.setViewportSize({ width: 390, height: 844 });
    await page.evaluate(() => { document.body.style.zoom = '2'; });
    await docTrigger.click();
    const modal = page.locator('#modal-documentacion');
    const closeModal = page.locator('#btn-cerrar-modal-doc');
    await expect(modal).toBeVisible();
    await expect(modal).toHaveAttribute('role', 'dialog');
    await expect(modal).toHaveAttribute('aria-modal', 'true');
    await expect(modal).toHaveAttribute('aria-labelledby', 'modal-documentacion-titulo');
    await expect(closeModal).toBeFocused();
    expect(await page.evaluate(() => document.body.style.overflow)).toBe('hidden');
    await expect(page.locator('#input-buscar-producto').click({ timeout: 500 })).rejects.toThrow();
    const focusables = modal.locator('button:not([disabled]), input:not([disabled]), select:not([disabled]), [tabindex]:not([tabindex="-1"])');
    await focusables.last().focus();
    await page.keyboard.press('Tab');
    await expect(closeModal).toBeFocused();
    await page.keyboard.press('Shift+Tab');
    await expect(focusables.last()).toBeFocused();
    const stacking = await page.evaluate(() => Number(getComputedStyle(document.querySelector('#modal-documentacion')).zIndex) > Number(getComputedStyle(document.querySelector('#dropdown-productos')).zIndex || 0));
    expect(stacking).toBeTruthy();
    await closeModal.click();
    await expect(modal).toBeHidden();
    await expect(docTrigger).toBeFocused();
    await docTrigger.click();
    await page.keyboard.press('Escape');
    await expect(modal).toBeHidden();
    await expect(docTrigger).toBeFocused();
    await page.evaluate(() => { document.body.style.zoom = ''; });
    // El bloque de arriba puso el viewport en mobile (390x844) para probar el stacking del
    // modal; el resto del flujo (navegación por tabs, CTA de Revisión) se auditó en desktop.
    await page.setViewportSize({ width: 1366, height: 768 });
    await page.unroute('**/api/ventas/PrevalidarCredito*');
    await page.locator('#step-btn-pago').click();
    await expect(page.locator('#step-panel-pago #select-tipo-pago')).toBeVisible();
    await page.locator('#select-tipo-pago').selectOption(TIPO_PAGO.Efectivo);
    await expect(page.locator('#step-btn-credito')).toBeHidden();
    await expect(page.locator('#step-btn-pago')).toBeEnabled({ timeout: 8_000 });

    // Navegación roving de tabs por teclado.
    await page.locator('#step-btn-cliente').focus();
    await page.keyboard.press('ArrowRight');
    if (!(await page.locator('#step-btn-productos').evaluate((el) => el === document.activeElement))) {
        await issue(page, 'wizard-tab-arrow-navigation-inoperative', 'Alta', 'wwwroot/js/venta-page-wizard.js',
            'ArrowRight sobre #step-btn-cliente no desplaza foco ni activa #step-btn-productos, pese a estar habilitado.');
    } else {
        await page.keyboard.press('End');
        if (!(await page.locator('#step-btn-revision').evaluate((el) => el === document.activeElement))) {
            await issue(page, 'wizard-tab-end-navigation-inoperative', 'Alta', 'wwwroot/js/venta-page-wizard.js',
                'End no lleva el foco al último tab visible del wizard.');
        }
    }
    await observe(page, 'create-before-save');

    // El wizard gatea el avance por pasos: saltar directo al tab "Revisión" con un click no
    // lo activa si los pasos previos (Pago, Envío) no se "cierran" primero con su CTA
    // contextual — mismo patrón de estado progresivo que "End = último paso habilitado"
    // (venta-cotizar-step.spec.js). El CTA es el mismo elemento re-etiquetado por paso
    // (Pago→"Revisar operación", Envío→su propio "Continuar"), así que se re-consulta y
    // clickea hasta llegar a Revisión en vez de asumir un único click.
    // No se usa aria-selected como condición de corte: la navegación por teclado de más
    // arriba (ArrowRight/End) ya lo deja en "true" sobre el roving tabindex sin cambiar el
    // panel visible (foco ≠ activación en este tablist) — el panel real es la única señal
    // confiable de que la navegación por click efectivamente ocurrió.
    const revisionPanel = page.locator('#step-panel-revision');
    for (let intentos = 0; intentos < 4; intentos++) {
        if (await revisionPanel.isVisible().catch(() => false)) break;
        await wizardPrimary.click();
        await page.waitForTimeout(300);
    }
    await expect(revisionPanel).toBeVisible();
    await ensureVendedorSeleccionado(page);

    const btnConfirmar = page.locator('#btn-confirmar');
    // venta-page-wizard.js delega en #btn-confirmar.click() cuando el CTA contextual ya está
    // parado en el último paso (Revisión): el click que activó el panel puede haber disparado
    // la confirmación real en el mismo gesto. Si ya quedó deshabilitado (submit en curso) no
    // hace falta un segundo click — sólo esperar la navegación; si sigue habilitado, confirmar
    // explícitamente acá.
    if (await btnConfirmar.isEnabled()) {
        await btnConfirmar.click();
    }
    await page.waitForURL(/\/Venta\/(Details|Edit)\/\d+/, { timeout: 20_000 });
    const detailsUrl = page.url();
    const id = detailsUrl.match(/\/(?:Details|Edit)\/(\d+)/)?.[1];
    expect(id).toBeTruthy();
    editUrl = `/Venta/Edit/${id}`;
    await capture(page, 'create-saved');

    const unexpectedResponses = failedResponses.filter((x) => !/404 .*favicon/i.test(x));
    if (consoleErrors.length) await issue(page, 'create-console-errors', 'Media', 'wwwroot/js/venta-create.js', consoleErrors.join('\n'));
    if (unexpectedResponses.length) await issue(page, 'create-failed-requests', 'Media', 'wwwroot/js/venta-create.js', unexpectedResponses.join('\n'));
});

test('Edit: carga, modificación, guardado y matriz responsive/zoom', async ({ page }) => {
    test.skip(!editUrl, 'No se creó una venta QA editable en el escenario anterior.');
    const consoleErrors = [];
    const failedResponses = [];
    page.on('console', (msg) => { if (msg.type() === 'error') consoleErrors.push(msg.text()); });
    page.on('response', (res) => { if (res.status() >= 400) failedResponses.push(`${res.status()} ${res.url()}`); });

    await page.goto(editUrl, { waitUntil: 'domcontentloaded' });
    await expect(page.locator('#venta-form')).toBeVisible({ timeout: 15_000 });
    // La matriz responsive va ANTES de confirmar: en Edit `#btn-confirmar` ahora CONFIRMA la venta
    // (accionConfirmacion), y una venta Confirmada ya no es editable (Edit redirige a Details).
    for (const [name, width, height] of matrix) {
        await page.setViewportSize({ width, height });
        await page.goto(editUrl, { waitUntil: 'domcontentloaded' });
        await expect(page.locator('#venta-form')).toBeVisible({ timeout: 15_000 });
        await observe(page, `edit-${name}`);
        await capture(page, `edit-${name}`);
        for (const zoom of [1.25, 1.5, 2]) {
            await page.evaluate((factor) => { document.body.style.zoom = String(factor); }, zoom);
            await observe(page, `edit-${name}-zoom-${zoom * 100}`);
            await capture(page, `edit-${name}-zoom-${zoom * 100}`);
            await page.evaluate(() => { document.body.style.zoom = ''; });
        }
    }
    await page.setViewportSize({ width: 1366, height: 768 });
    await page.goto(editUrl, { waitUntil: 'domcontentloaded' });
    await expect(page.locator('#venta-form')).toBeVisible({ timeout: 15_000 });
    expect(await page.locator('#tbody-detalles tr').count()).toBeGreaterThanOrEqual(1);
    await page.locator('#step-btn-productos').click();
    await expect(page.locator('#tbody-detalles [data-quantity-input]').first()).toBeVisible();
    // Modificación reversible del borrador QA: quitar la línea y volver a agregar un producto existente.
    const rowsBeforeRemoval = await page.locator('#tbody-detalles tr').count();
    await page.locator('.btn-eliminar-detalle').first().click();
    await expect(page.locator('#tbody-detalles tr')).toHaveCount(rowsBeforeRemoval - 1);
    await activarFiltroStock(page);
    const replacement = await addProduct(page, 'an');
    test.skip(!replacement, 'No hay producto de QA con stock para completar la modificación de Edit.');
    await page.locator('#step-btn-pago').click();
    await expect(page.locator('#select-tipo-pago')).toBeVisible();
    await setGlobalTipoPago(page, TIPO_PAGO.Efectivo);
    await ensureVendedorSeleccionado(page);
    await page.locator('#step-btn-revision').click();
    await page.locator('#btn-confirmar').click();
    await page.waitForURL(/\/Venta\/Details\/\d+/, { timeout: 20_000 });
    await capture(page, 'edit-saved');

    if (consoleErrors.length) await issue(page, 'edit-console-errors', 'Media', 'wwwroot/js/venta-create.js', consoleErrors.join('\n'));
    if (failedResponses.length) await issue(page, 'edit-failed-requests', 'Media', 'wwwroot/js/venta-create.js', failedResponses.join('\n'));
});

test('Create: matriz responsive y recuperación ante error de API de búsqueda', async ({ page }) => {
    for (const [name, width, height] of matrix) {
        await page.setViewportSize({ width, height });
        await gotoCreate(page);
        await observe(page, `create-${name}`);
        await capture(page, `create-${name}`);
        for (const zoom of [1.25, 1.5, 2]) {
            await page.evaluate((factor) => { document.body.style.zoom = String(factor); }, zoom);
            await observe(page, `create-${name}-zoom-${zoom * 100}`);
            await capture(page, `create-${name}-zoom-${zoom * 100}`);
            await page.evaluate(() => { document.body.style.zoom = ''; });
        }
    }

    await page.route(/.*(Buscar|buscar).*(Cliente|cliente|Producto|producto).*/, (route) => route.fulfill({ status: 503, contentType: 'application/json', body: '{"error":"QA simulated outage"}' }));
    await gotoCreate(page);
    await page.locator('#input-buscar-cliente').fill('an');
    await page.waitForTimeout(900);
    // La UI debe permanecer operable tras el error, sin dropdown vacío bloqueante.
    await expect(page.locator('#input-buscar-cliente')).toBeEnabled();
    await observe(page, 'create-api-error');
    await capture(page, 'create-api-error');
    await page.unrouteAll({ behavior: 'ignoreErrors' });
});
