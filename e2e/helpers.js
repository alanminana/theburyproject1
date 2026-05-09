// @ts-check
/**
 * Helpers compartidos para la suite E2E de pago por ítem — Fase 16.7.
 *
 * Selectores basados en venta-create.js y Create_tw.cshtml.
 */

/** Valores enteros del enum TipoPago (deben coincidir con TipoPago.cs) */
const TIPO_PAGO = {
    Efectivo:        '0',
    Transferencia:   '1',
    TarjetaDebito:   '2',
    TarjetaCredito:  '3',
    Cheque:          '4',
    CreditoPersonal: '5',
    MercadoPago:     '6',
    CuentaCorriente: '7',
    Tarjeta:         '8',
};

/** Labels que la UI muestra en el badge del ítem */
const TIPO_PAGO_BADGE = {
    '0': 'Efectivo',
    '1': 'Transferencia',
    '2': 'Débito',
    '3': 'Crédito',
    '4': 'Cheque',
    '5': 'Créd. Personal',
    '6': 'Mercado Pago',
    '7': 'Cta. Cte.',
    '8': 'Tarjeta',
};

/**
 * Busca y selecciona el primer cliente que coincida con el término.
 * Requiere al menos 2 caracteres para disparar el autocomplete.
 * @param {import('playwright/test').Page} page
 * @param {string} term - Término de búsqueda (mín. 2 chars)
 * @returns {Promise<string|null>} Nombre del cliente seleccionado, o null si no hay resultados
 */
async function searchAndSelectClient(page, term = 'an') {
    const input = page.locator('#input-buscar-cliente');
    await input.fill(term);

    // Debounce del JS es 300ms; esperar un poco más para la respuesta de red
    await page.waitForTimeout(700);

    const dropdown = page.locator('#dropdown-clientes');
    const visible = await dropdown.isVisible({ timeout: 5_000 }).catch(() => false);
    if (!visible) return null;

    const firstItem = dropdown.locator('[data-id]').first();
    const itemVisible = await firstItem.isVisible({ timeout: 3_000 }).catch(() => false);
    if (!itemVisible) return null;

    const nombre = await firstItem.locator('p').first().textContent();
    await firstItem.click();

    // Esperar que el panel de info del cliente sea visible
    await page.locator('#info-cliente').waitFor({ state: 'visible', timeout: 5_000 });

    return nombre?.trim() ?? null;
}

/**
 * Activa el filtro de solo-con-stock antes de buscar, para evitar
 * agregar productos sin stock que bloquean el formulario.
 * @param {import('playwright/test').Page} page
 */
async function activarFiltroStock(page) {
    const checkbox = page.locator('#filtro-solo-stock');
    const exists = await checkbox.count();
    if (exists && !(await checkbox.isChecked())) {
        await checkbox.check();
        await page.waitForTimeout(200);
    }
}

/**
 * Busca y agrega el primer producto disponible al carrito.
 * Devuelve el ID del producto agregado o null si no hay resultados.
 * @param {import('playwright/test').Page} page
 * @param {string} term - Término de búsqueda (mín. 2 chars)
 * @returns {Promise<{id:string, nombre:string}|null>}
 */
async function addProduct(page, term) {
    const inputBuscar = page.locator('#input-buscar-producto');
    await inputBuscar.fill(term);
    await page.waitForTimeout(700);

    const dropdown = page.locator('#dropdown-productos');
    const visible = await dropdown.isVisible({ timeout: 5_000 }).catch(() => false);
    if (!visible) return null;

    const firstItem = dropdown.locator('[data-id]').first();
    const itemVisible = await firstItem.isVisible({ timeout: 3_000 }).catch(() => false);
    if (!itemVisible) return null;

    const productId = await firstItem.getAttribute('data-id');
    const nombre = (await firstItem.locator('p').first().textContent())?.trim() ?? '';

    await firstItem.click();
    await page.locator('#panel-agregar-producto').waitFor({ state: 'visible', timeout: 3_000 });

    // Cantidad = 1 (ya tiene valor por defecto, pero lo asignamos explícitamente)
    await page.fill('#txt-cantidad', '1');

    const prevCount = await page.locator('#tbody-detalles tr').count();
    await page.click('#btn-agregar-producto');

    // Esperar que el panel se oculte (producto agregado exitosamente)
    await page.locator('#panel-agregar-producto').waitFor({ state: 'hidden', timeout: 3_000 })
        .catch(() => null);

    return { id: productId ?? '', nombre };
}

/**
 * Agrega dos productos diferentes al carrito.
 * Si el segundo término devuelve el mismo producto que el primero, intenta
 * con una variante del término. Devuelve los dos productos o null si no
 * hay suficientes productos distintos.
 * @param {import('playwright/test').Page} page
 * @returns {Promise<[{id:string, nombre:string}, {id:string, nombre:string}]|null>}
 */
async function addTwoDistinctProducts(page) {
    const terminos = ['an', 'el', 'or', 'is', 'ar', 'ro', 'al'];

    const prod1 = await addProduct(page, terminos[0]);
    if (!prod1) return null;

    // Buscar un segundo producto diferente
    for (let i = 1; i < terminos.length; i++) {
        await page.waitForTimeout(300);
        const inputBuscar = page.locator('#input-buscar-producto');
        await inputBuscar.fill(terminos[i]);
        await page.waitForTimeout(700);

        const dropdown = page.locator('#dropdown-productos');
        const visible = await dropdown.isVisible({ timeout: 3_000 }).catch(() => false);
        if (!visible) continue;

        const items = dropdown.locator('[data-id]');
        const count = await items.count();

        for (let j = 0; j < count; j++) {
            const item = items.nth(j);
            const candidateId = await item.getAttribute('data-id');
            if (candidateId && candidateId !== prod1.id) {
                const nombre = (await item.locator('p').first().textContent())?.trim() ?? '';
                await item.click();
                await page.locator('#panel-agregar-producto').waitFor({ state: 'visible', timeout: 3_000 });
                await page.fill('#txt-cantidad', '1');
                await page.click('#btn-agregar-producto');
                await page.locator('#panel-agregar-producto').waitFor({ state: 'hidden', timeout: 3_000 })
                    .catch(() => null);
                return [prod1, { id: candidateId, nombre }];
            }
        }
    }

    return null;
}

/**
 * Espera a que el diagnostico de condiciones de pago complete su ciclo.
 * El JS usa un debounce de 250ms + llamada a la API.
 * @param {import('playwright/test').Page} page
 */
async function waitForDiagnostico(page) {
    await page.waitForTimeout(1_200);
}

/**
 * Abre el modal de pago por ítem para el ítem en la posición indicada (0-based).
 * @param {import('playwright/test').Page} page
 * @param {number} itemIndex
 */
async function openPagoItemModal(page, itemIndex) {
    const btn = page.locator('.btn-configurar-pago-item').nth(itemIndex);
    await btn.scrollIntoViewIfNeeded();
    await btn.click();
    await page.locator('#modal-pago-item').waitFor({ state: 'visible', timeout: 5_000 });
}

/**
 * Configura el tipo de pago y opcionalmente un plan en el modal de pago por ítem.
 * Guarda y cierra el modal.
 * @param {import('playwright/test').Page} page
 * @param {string} tipoPagoValue - Valor entero como string (ej. '3' para TarjetaCredito)
 * @param {boolean} intentarSeleccionarPlan - Si true, selecciona el primer plan disponible
 * @returns {Promise<boolean>} true si se seleccionó un plan
 */
async function configurePagoItem(page, tipoPagoValue, intentarSeleccionarPlan = true) {
    await page.selectOption('#select-tipo-pago-item', tipoPagoValue);
    await page.waitForTimeout(350);

    let planSeleccionado = false;
    if (intentarSeleccionarPlan) {
        const planBtns = page.locator('.plan-pago-item-btn');
        const planCount = await planBtns.count();
        if (planCount > 0) {
            await planBtns.first().click();
            planSeleccionado = true;
        }
    }

    await page.click('#btn-guardar-pago-item');
    await page.locator('#modal-pago-item').waitFor({ state: 'hidden', timeout: 3_000 });

    return planSeleccionado;
}

/**
 * Establece el tipo de pago global en #select-tipo-pago y espera el diagnóstico.
 * @param {import('playwright/test').Page} page
 * @param {string} tipoPagoValue
 */
async function setGlobalTipoPago(page, tipoPagoValue) {
    await page.selectOption('#select-tipo-pago', tipoPagoValue);
    await waitForDiagnostico(page);
}

/**
 * Verifica que el btn-confirmar no esté deshabilitado.
 * Si está bloqueado por el diagnóstico, cambia a Efectivo y reintenta.
 * @param {import('playwright/test').Page} page
 * @returns {Promise<boolean>} true si quedó habilitado
 */
async function ensureConfirmarHabilitado(page) {
    const btn = page.locator('#btn-confirmar');

    const disabled = await btn.isDisabled();
    if (disabled) {
        await setGlobalTipoPago(page, TIPO_PAGO.Efectivo);
        await page.waitForTimeout(500);
        return !(await btn.isDisabled());
    }
    return true;
}

/**
 * Si el usuario tiene rol admin/gerente, el servidor requiere VendedorUserId.
 * Selecciona la primera opción válida del select si está presente y vacío.
 * @param {import('playwright/test').Page} page
 */
async function ensureVendedorSeleccionado(page) {
    const select = page.locator('#VendedorUserId');
    const visible = await select.isVisible().catch(() => false);
    if (!visible) return;
    const current = await select.inputValue().catch(() => '');
    if (current) return;
    await select.selectOption({ index: 1 });
}

module.exports = {
    TIPO_PAGO,
    TIPO_PAGO_BADGE,
    searchAndSelectClient,
    activarFiltroStock,
    addProduct,
    addTwoDistinctProducts,
    waitForDiagnostico,
    openPagoItemModal,
    configurePagoItem,
    setGlobalTipoPago,
    ensureConfirmarHabilitado,
    ensureVendedorSeleccionado,
};
