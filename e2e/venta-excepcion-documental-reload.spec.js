// @ts-check
/**
 * Regresión: excepción documental ya autorizada por el servidor debe sobrevivir
 * una recarga de la página del wizard.
 *
 * Bug reportado: el operador aplicaba la excepción documental (motivo cargado,
 * crédito configurado, contrato generado), pero si la página se recargaba —o el
 * operador volvía más tarde a un borrador ya guardado— el estado en memoria
 * `excepcionActiva` de venta-create.js arrancaba en false. La primera verificación
 * automática de elegibilidad volvía a marcar "No viable" y el guard de submit
 * bloqueaba el envío final sin mandar ningún POST, aunque el servidor ya tuviera
 * la excepción auditada (Venta.MotivoAutorizacion "EXCEPCION_DOC|...", ver
 * VentaViewModel.TieneExcepcionDocumentalRegistrada).
 *
 * Fix: el form emite data-excepcion-documental-registrada/-motivo (ver
 * _VentaWizardForm.cshtml) y venta-create.js hidrata excepcionActiva desde ahí
 * antes de la primera verificación automática.
 *
 * Estrategia de datos: se crea una venta descartable real (Efectivo, sin crédito)
 * para tener un Id de Edit válido, y se reescribe el HTML de su GET /Venta/Edit/{id}
 * para simular Crédito Personal ya seleccionado + excepción ya registrada por el
 * servidor — tal como se vería al recargar un Edit real con crédito ya autorizado.
 * Así el test no depende de un cliente de QA con mora/cupo insuficiente real, y
 * al reutilizar el detalle/total ya persistidos de Edit (sembrados server-side en
 * window.ventaInicial) evita dañar la hidratación agregando productos en vivo:
 * cualquier cambio de carrito after-hidratación invalida legítimamente la
 * excepción (mismo comportamiento que un cambio real de cliente/monto, ver
 * comentario "BUG reportado" en verificarElegibilidadAuto de venta-create.js).
 */
const { test, expect } = require('playwright/test');
const {
    searchAndSelectClient, activarFiltroStock, addProduct,
    ensureVendedorSeleccionado, TIPO_PAGO,
} = require('./helpers');

test.use({ storageState: 'e2e/.auth/user.json' });

// mostrarMotivos() (venta-create.js) espera objetos {categoria,titulo,descripcion,esBloqueante},
// no strings sueltos: con motivos de texto plano nunca se arma la fila "Documentación —
// exceptuada" (categoria 1) que confirma la hidratación de la excepción.
const NO_VIABLE_RESPONSE = {
    resultado: 2, colorBadge: 'danger', textoEstado: 'No viable',
    limiteCredito: 0, creditoUtilizado: 0, cupoDisponible: 0,
    documentacionCompleta: false, documentosFaltantes: ['DNI'], documentosVencidos: [],
    motivos: [
        { categoria: 1, titulo: 'Documentación', descripcion: 'Faltan documentos: DNI', esBloqueante: true },
        { categoria: 3, titulo: 'Mora vigente', descripcion: 'Mora vigente', esBloqueante: true },
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
    await page.locator('#select-tipo-pago').selectOption(TIPO_PAGO.Efectivo);
    await page.locator('#step-btn-revision').click();
    await ensureVendedorSeleccionado(page);
    await page.locator('#btn-confirmar').click();
    await page.waitForURL(/\/Venta\/(Details|Edit)\/\d+/, { timeout: 20_000 });
    const id = page.url().match(/\/(?:Details|Edit)\/(\d+)/)?.[1];
    expect(id).toBeTruthy();
    return id;
}

/**
 * Reescribe el GET de /Venta/Edit/{id} servido para simular una venta que ya
 * carga con Crédito Personal preseleccionado, crédito "configurado" y —cuando
 * `conExcepcion` es true— una excepción documental ya autorizada por el
 * servidor. La venta detrás sigue siendo la descartable creada en Efectivo: acá
 * sólo importa lo que el navegador ve al cargar la página, que es exactamente lo
 * que hidrata/guarda venta-create.js.
 *
 * Preseleccionar el tipo de pago en el HTML (en vez de elegirlo con selectOption()
 * ya en el navegador) es a propósito: elegir el <select> en vivo dispara un
 * 'change' real, y onTipoPagoChange() invalida cualquier excepción ya confirmada
 * cuando NO es la inicialización de la página (ver venta-create.js). Un Edit real
 * recargado con TipoPago=CreditoPersonal ya guardado nunca dispara ese 'change' —
 * sólo corre la rama de inicialización, que preserva la excepción hidratada.
 */
function mockEditConCreditoPersonal(page, editUrl, { conExcepcion }) {
    const pattern = new RegExp(`${editUrl}$`);
    return page.route(pattern, async (route, request) => {
        if (request.method() !== 'GET') { await route.continue(); return; }
        const response = await route.fetch();
        let body = await response.text();
        body = body.replace(/<form id="venta-form"[^>]*>/, (tag) => {
            let t = tag.replace(/data-credito-configurado="[^"]*"/, 'data-credito-configurado="true"');
            if (conExcepcion) {
                t = t
                    .replace(/data-excepcion-documental-registrada="[^"]*"/, 'data-excepcion-documental-registrada="true"')
                    .replace(/data-excepcion-documental-motivo="[^"]*"/, 'data-excepcion-documental-motivo="QA: excepción ya auditada por el servidor"');
            }
            return t;
        });
        body = body.replace(/<select id="select-tipo-pago"[\s\S]*?<\/select>/, (block) => {
            const sinSelected = block.replace(/\sselected="selected"/g, '');
            return sinSelected.replace('<option value="5"', '<option value="5" selected="selected"');
        });
        await route.fulfill({ response, body });
    });
}

/**
 * Dispara el 'submit' nativo del form. Se usa en lugar de clickear el botón real
 * porque llegar al paso "Revisión" habilitado por UI requiere además que el
 * configurador embebido de crédito confirme un plan (creditoValidado/"configurado",
 * gating de venta-page-wizard.js) — una precondición de UX totalmente aparte del
 * guard bajo prueba acá.
 *
 * venta-page-wizard.js también engancha su propio listener 'submit' en el mismo
 * form: si el paso activo no es "revision" convierte el submit en un simple
 * "avanzar paso" (event.preventDefault + avanzar()), lo que enmascararía el guard
 * de venta-create.js bajo prueba. window.VentaWizard.setActiveStep('revision') es
 * la API pública que el propio wizard expone (venta-page-wizard.js) para mover el
 * paso activo; se usa acá para posicionar el wizard sin depender del gating de
 * creditoValidado, que es una precondición de UX distinta del guard que se prueba.
 */
async function submitVentaForm(page) {
    await page.evaluate(() => {
        window.VentaWizard?.setActiveStep?.('revision');
        document.getElementById('venta-form')?.requestSubmit();
    });
}

test('excepción documental hidratada al recargar Edit: no vuelve a bloquear el submit final', async ({ page }) => {
    const id = await crearVentaThrowaway(page);
    const editUrl = `/Venta/Edit/${id}`;

    await mockEditConCreditoPersonal(page, editUrl, { conExcepcion: true });
    await page.route('**/api/ventas/PrevalidarCredito*', (route) => route.fulfill({
        contentType: 'application/json',
        body: JSON.stringify(NO_VIABLE_RESPONSE),
    }));

    await page.goto(editUrl, { waitUntil: 'domcontentloaded' });
    await expect(page.locator('#venta-form')).toBeVisible({ timeout: 15_000 });
    await expect(page.locator('#select-tipo-pago')).toHaveValue(TIPO_PAGO.CreditoPersonal);

    await page.locator('#step-btn-credito').click();
    await expect(page.locator('#step-panel-credito')).toBeVisible();

    // Hidratación: la confirmación de "excepción aplicada" debe aparecer sola, sin que
    // el operador haya tocado "Aplicar Excepción" ni "Aplicar y continuar".
    // VENTA-CREDITO-REDESIGN-VISUAL-IMPLEMENTACION-01 retiró #excepcion-aplicada-badge:
    // la fila "Documentación — exceptuada" de Otros motivos (mostrarMotivos(), categoría 1
    // emerald) es ahora la única confirmación visible del hecho.
    const badge = page.getByText('Documentación — exceptuada');
    await expect(badge).toBeVisible({ timeout: 10_000 });

    const txtMotivo = page.locator('#txt-excepcion-documental');
    await expect(txtMotivo).toHaveValue('QA: excepción ya auditada por el servidor');
    await expect(txtMotivo).toHaveAttribute('readonly', '');
    await expect(page.locator('#hdn-aplicar-excepcion')).toHaveValue('true');

    // Verificación automática re-confirma "No viable" (mock), pero al preservar la
    // excepción hidratada no debe reaparecer el panel de "aplicar excepción" vacío.
    await expect(page.locator('#panel-excepcion-inactiva')).toBeHidden();

    await ensureVendedorSeleccionado(page);

    const postSubmit = page.waitForRequest(
        (req) => req.method() === 'POST' && new RegExp(`${editUrl}$`).test(new URL(req.url()).pathname),
        { timeout: 10_000 }
    );
    await submitVentaForm(page);

    // La aserción central del fix: el guard de submit ya no debe interceptar el
    // envío (antes del fix, ningún POST llegaba a salir y el wizard rebotaba al
    // paso "Crédito" sin enviar nada).
    await expect(postSubmit).resolves.toBeTruthy();
});

test('sin excepción documental registrada: el submit sigue bloqueado si la prevalidación es No viable (regresión inversa)', async ({ page }) => {
    const id = await crearVentaThrowaway(page);
    const editUrl = `/Venta/Edit/${id}`;

    await mockEditConCreditoPersonal(page, editUrl, { conExcepcion: false });
    await page.route('**/api/ventas/PrevalidarCredito*', (route) => route.fulfill({
        contentType: 'application/json',
        body: JSON.stringify(NO_VIABLE_RESPONSE),
    }));

    await page.goto(editUrl, { waitUntil: 'domcontentloaded' });
    await expect(page.locator('#venta-form')).toBeVisible({ timeout: 15_000 });
    await expect(page.locator('#select-tipo-pago')).toHaveValue(TIPO_PAGO.CreditoPersonal);

    await page.locator('#step-btn-credito').click();
    await expect(page.locator('#panel-resultado-verificacion')).toBeVisible({ timeout: 10_000 });
    await expect(page.locator('#excepcion-aplicada-badge')).toHaveCount(0);
    await ensureVendedorSeleccionado(page);

    let posteo = false;
    const onRequest = (req) => {
        if (req.method() === 'POST' && new RegExp(`${editUrl}$`).test(new URL(req.url()).pathname)) posteo = true;
    };
    page.on('request', onRequest);

    await submitVentaForm(page);

    // El guard original sigue vigente: sin excepción, el submit se cancela
    // client-side (mismo mensaje que emite venta-create.js) y no sale ningún POST;
    // el wizard vuelve a mostrar el paso "Crédito" en vez de avanzar.
    await expect(page.locator('#venta-create-feedback-slot')).toContainText('Revisá la verificación crediticia antes de continuar.', { timeout: 5_000 });
    await expect(page.locator('#step-btn-credito')).toHaveAttribute('aria-selected', 'true');
    expect(posteo).toBe(false);

    page.off('request', onRequest);
});
