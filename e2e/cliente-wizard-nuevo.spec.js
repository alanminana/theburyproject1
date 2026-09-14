// @ts-check
/**
 * CLIENTE-WIZARD-ML1/ML2 — wizard real de pasos en el drawer de alta de
 * Cliente (Views/Cliente/_ClienteFormPartial.cshtml + _ClienteFormCampos.cshtml
 * + wwwroot/js/cliente-modal.js).
 *
 * ML1 — contrato de navegación básico:
 *   1) abrir "Nuevo cliente" → arranca en Personales;
 *   2) intentar avanzar con obligatorios vacíos → no cambia de paso;
 *   3) completar los mínimos → avanzar funciona;
 *   4) "Crear cliente" no es visible fuera del último paso;
 *   5) volver con Anterior → los valores cargados se conservan.
 *
 * ML2 — UX contextual:
 *   6) Referencias queda fuera de los pasos navegables (5, no 6);
 *   7) Cónyuge se marca "No aplica" según Estado civil y Anterior/Siguiente
 *      lo saltean, sin borrar datos ya cargados en ese paso.
 *   8) Resumen lateral dinámico: identidad, documento y contacto se
 *      actualizan en vivo, sin placeholders ni datos ficticios, se
 *      conservan al navegar y no se activan fuera del drawer Create.
 *
 * Lote 3 — responsive y acabado final:
 *   9) Mobile (<=640px): la barra de 5 tabs se reemplaza por un stepper
 *      compacto ("Paso X de N" + ícono/nombre del paso actual + barra de
 *      progreso), sin inventar un flujo nuevo — Anterior/Siguiente y la
 *      validación por paso siguen exactamente iguales.
 *  10) Tablet/desktop angosto (1024px): el tab del paso actual se mantiene
 *      siempre visible dentro de la barra, aunque los 5 no entren completos
 *      sin comprimir texto — antes "Crédito" podía quedar totalmente fuera
 *      de vista al llegar al último paso con Siguiente.
 */
const { test, expect } = require('playwright/test');

test.use({ storageState: 'e2e/.auth/user.json' });

test.beforeEach(async ({ page }) => {
    await page.route('**/fonts.googleapis.com/**', route =>
        route.fulfill({ status: 200, contentType: 'text/css', body: '' }));
    await page.route('**/fonts.gstatic.com/**', route =>
        route.fulfill({ status: 204, body: '' }));
});

function trackFailures(page) {
    const consoleErrors = [];
    const serverErrors = [];
    page.on('console', message => {
        if (message.type() === 'error') consoleErrors.push(message.text());
    });
    page.on('response', response => {
        if (response.status() >= 500) serverErrors.push(`${response.status()} ${response.url()}`);
    });
    return { consoleErrors, serverErrors };
}

function expectNoFailures(failures) {
    expect(failures.consoleErrors, `errores de consola: ${failures.consoleErrors.join(' | ')}`).toHaveLength(0);
    expect(failures.serverErrors, `HTTP 5xx: ${failures.serverErrors.join(' | ')}`).toHaveLength(0);
}

test('wizard de alta: bloquea avance sin obligatorios, valida por paso y conserva datos al volver', async ({ page }) => {
    const failures = trackFailures(page);
    await page.goto('/Cliente', { waitUntil: 'domcontentloaded' });
    await expect(page).not.toHaveURL(/Identity\/Account\/Login/);

    await page.locator('[data-cliente-modal-open="create"]').first().click();

    const form = page.locator('#cliente-modal-form');
    await expect(form).toBeVisible();
    await expect(form).toHaveAttribute('data-cliente-wizard', 'create');

    const personalTab = form.locator('[data-cliente-tab="t-personal"]');
    const conyugeTab = form.locator('[data-cliente-tab="t-conyuge"]');

    // 1) Arranca en Personales.
    await expect(personalTab).toHaveAttribute('aria-current', 'step');

    // 2) Sin obligatorios (NumeroDocumento/Apellido/Nombre vacíos), Siguiente
    //    no debe avanzar de paso, y el paso queda marcado con error.
    await form.locator('#cliente-wizard-next').click();
    await expect(personalTab).toHaveAttribute('aria-current', 'step');
    await expect(personalTab).toHaveAttribute('data-step-state', 'error');
    await expect(conyugeTab).not.toHaveAttribute('aria-current', 'step');

    // 3) Completar los obligatorios mínimos de Personales y avanzar.
    await form.locator('#NumeroDocumento').fill('30111222');
    await form.locator('#Apellido').fill('Gomez');
    await form.locator('#Nombre').fill('Ana');

    await form.locator('#cliente-wizard-next').click();
    await expect(conyugeTab).toHaveAttribute('aria-current', 'step');
    await expect(personalTab).toHaveAttribute('data-step-state', 'complete');

    // 4) "Crear cliente" no es la acción visible fuera del último paso.
    await expect(form.locator('#cliente-wizard-submit')).toBeHidden();

    // 5) Anterior conserva los valores cargados.
    await form.locator('#cliente-wizard-prev').click();
    await expect(personalTab).toHaveAttribute('aria-current', 'step');
    await expect(form.locator('#Apellido')).toHaveValue('Gomez');
    await expect(form.locator('#Nombre')).toHaveValue('Ana');
    await expect(form.locator('#NumeroDocumento')).toHaveValue('30111222');

    expectNoFailures(failures);
});

test('wizard de alta: Referencias no es un paso navegable (Lote 2)', async ({ page }) => {
    const failures = trackFailures(page);
    await page.goto('/Cliente', { waitUntil: 'domcontentloaded' });
    await page.locator('[data-cliente-modal-open="create"]').first().click();

    const form = page.locator('#cliente-modal-form');
    await expect(form).toBeVisible();

    // La sección sigue en el DOM (no se borró), pero su tab queda oculta y
    // fuera de "Paso X de N" (5 pasos navegables, no 6).
    await expect(form.locator('[data-cliente-tab="t-refs"]')).toBeHidden();
    await expect(page.locator('#t-refs')).toContainText('SIN CAMPOS EN EL MODELO ACTUAL');
    await expect(form.locator('[data-wizard-progress]')).toHaveText('Paso 1 de 5');

    expectNoFailures(failures);
});

test('wizard de alta: Cónyuge se marca "No aplica" y se saltea según Estado civil, sin perder datos', async ({ page }) => {
    const failures = trackFailures(page);
    await page.goto('/Cliente', { waitUntil: 'domcontentloaded' });
    await page.locator('[data-cliente-modal-open="create"]').first().click();

    const form = page.locator('#cliente-modal-form');
    await expect(form).toBeVisible();

    const conyugeTab = form.locator('[data-cliente-tab="t-conyuge"]');
    const contactoTab = form.locator('[data-cliente-tab="t-contacto"]');
    const personalTab = form.locator('[data-cliente-tab="t-personal"]');

    await form.locator('#NumeroDocumento').fill('30111333');
    await form.locator('#Apellido').fill('Perez');
    await form.locator('#Nombre').fill('Luis');

    // Soltero/a → Cónyuge queda "No aplica" y Siguiente lo saltea directo a Contacto.
    await form.locator('#f-civil').selectOption('Soltero/a');
    await expect(conyugeTab).toHaveAttribute('data-step-state', 'skip');
    await form.locator('#cliente-wizard-next').click();
    await expect(contactoTab).toHaveAttribute('aria-current', 'step');
    await expect(form.locator('[data-wizard-progress]')).toHaveText('Paso 3 de 5');

    // Anterior también lo saltea de vuelta a Personales.
    await form.locator('#cliente-wizard-prev').click();
    await expect(personalTab).toHaveAttribute('aria-current', 'step');

    // Cargar un dato de Cónyuge a mano (click directo en la tab, permitido) y
    // cambiar el Estado civil no debe borrarlo en ningún sentido.
    await conyugeTab.click();
    await form.locator('#ConyugeNombreCompleto').fill('Marta Diaz');
    await personalTab.click();
    await form.locator('#f-civil').selectOption('Casado/a');
    await expect(conyugeTab).not.toHaveAttribute('data-step-state', 'skip');
    await conyugeTab.click();
    await expect(form.locator('#ConyugeNombreCompleto')).toHaveValue('Marta Diaz');

    // Volver a un estado "sin cónyuge" tampoco borra el dato ya cargado.
    await personalTab.click();
    await form.locator('#f-civil').selectOption('Viudo/a');
    await expect(conyugeTab).toHaveAttribute('data-step-state', 'skip');
    await conyugeTab.click();
    await expect(form.locator('#ConyugeNombreCompleto')).toHaveValue('Marta Diaz');

    expectNoFailures(failures);
});

test('resumen dinámico: identidad (iniciales y nombre) se actualiza en vivo sin inventar datos', async ({ page }) => {
    const failures = trackFailures(page);
    await page.goto('/Cliente', { waitUntil: 'domcontentloaded' });
    await page.locator('[data-cliente-modal-open="create"]').first().click();

    const form = page.locator('#cliente-modal-form');
    await expect(form).toBeVisible();

    const pvName = page.locator('#pv-name');
    const pvAvatar = page.locator('#pv-avatar');

    // Estado inicial: se mantiene tal cual (sin nombre/apellido cargados).
    await expect(pvName).toHaveText('Nuevo cliente');
    await expect(pvAvatar).toHaveText('');

    // Solo Nombre → inicial de un solo carácter, sin apellido inventado.
    await form.locator('#Nombre').fill('Alan');
    await expect(pvAvatar).toHaveText('A');
    await expect(pvName).toHaveText('Alan');

    // Nombre + Apellido → iniciales de ambos, nombre completo real.
    await form.locator('#Apellido').fill('Miñana');
    await expect(pvAvatar).toHaveText('AM');
    await expect(pvName).toHaveText('Alan Miñana');

    // Solo Apellido (se borra Nombre) → inicial de un solo carácter.
    await form.locator('#Nombre').fill('');
    await expect(pvAvatar).toHaveText('M');
    await expect(pvName).toHaveText('Miñana');

    expectNoFailures(failures);
});

test('resumen dinámico: documento se muestra formateado y solo cuando hay número real', async ({ page }) => {
    const failures = trackFailures(page);
    await page.goto('/Cliente', { waitUntil: 'domcontentloaded' });
    await page.locator('[data-cliente-modal-open="create"]').first().click();

    const form = page.locator('#cliente-modal-form');
    await expect(form).toBeVisible();

    const pvDoc = page.locator('#pv-doc');

    // Sin número cargado: no se muestra información ficticia.
    await expect(pvDoc).toHaveText('-');

    // Con tipo (DNI, valor por defecto) + número → aparece agrupado, no crudo.
    await form.locator('#NumeroDocumento').fill('35996614');
    await expect(pvDoc).toHaveText('DNI 35.996.614');

    // Modificar el número actualiza el resumen en vivo.
    await form.locator('#NumeroDocumento').fill('20123456');
    await expect(pvDoc).toHaveText('DNI 20.123.456');

    // Vaciar el número vuelve al estado sin datos ficticios.
    await form.locator('#NumeroDocumento').fill('');
    await expect(pvDoc).toHaveText('-');

    expectNoFailures(failures);
});

test('resumen dinámico: contacto solo muestra líneas con dato real, sin espacio vacío reservado', async ({ page }) => {
    const failures = trackFailures(page);
    await page.goto('/Cliente', { waitUntil: 'domcontentloaded' });
    await page.locator('[data-cliente-modal-open="create"]').first().click();

    const form = page.locator('#cliente-modal-form');
    await expect(form).toBeVisible();

    const pvContact = page.locator('#pv-contact');
    const pvPhone = page.locator('#pv-contact-phone');
    const pvEmail = page.locator('#pv-contact-email');

    // Ambos vacíos: el bloque completo queda oculto.
    await expect(pvContact).toBeHidden();

    // El teléfono está en el paso Contacto: hay que navegar hasta ahí (los
    // obligatorios de Personales ya están cubiertos en el primer test).
    await form.locator('#NumeroDocumento').fill('30111444');
    await form.locator('#Apellido').fill('Diaz');
    await form.locator('#Nombre').fill('Sofia');
    await form.locator('#cliente-wizard-next').click();
    await form.locator('#cliente-wizard-next').click();

    // Solo teléfono → aparece el bloque, solo esa línea.
    await form.locator('#Telefono').fill('1123456789');
    await expect(pvContact).toBeVisible();
    await expect(pvPhone).toBeVisible();
    await expect(pvEmail).toBeHidden();

    // Agregar email → aparecen ambas líneas.
    await form.locator('#Email').fill('alan@email.com');
    await expect(pvEmail).toBeVisible();

    // Limpiar teléfono → desaparece solo esa línea, el bloque sigue visible.
    await form.locator('#Telefono').fill('');
    await expect(pvPhone).toBeHidden();
    await expect(pvEmail).toBeVisible();
    await expect(pvContact).toBeVisible();

    // Limpiar también el email → el bloque completo vuelve a ocultarse.
    await form.locator('#Email').fill('');
    await expect(pvContact).toBeHidden();

    expectNoFailures(failures);
});

test('resumen dinámico: se conserva al navegar Anterior/Siguiente y al editar en un paso ya visitado', async ({ page }) => {
    const failures = trackFailures(page);
    await page.goto('/Cliente', { waitUntil: 'domcontentloaded' });
    await page.locator('[data-cliente-modal-open="create"]').first().click();

    const form = page.locator('#cliente-modal-form');
    await expect(form).toBeVisible();

    const pvName = page.locator('#pv-name');
    const pvDoc = page.locator('#pv-doc');
    const personalTab = form.locator('[data-cliente-tab="t-personal"]');
    const contactoTab = form.locator('[data-cliente-tab="t-contacto"]');

    await form.locator('#NumeroDocumento').fill('30111555');
    await form.locator('#Apellido').fill('Lopez');
    await form.locator('#Nombre').fill('Marcos');
    await expect(pvName).toHaveText('Marcos Lopez');
    await expect(pvDoc).toHaveText('DNI 30.111.555');

    // Avanzar no depende del paso visible: el resumen sigue igual.
    await form.locator('#cliente-wizard-next').click();
    await expect(pvName).toHaveText('Marcos Lopez');
    await expect(pvDoc).toHaveText('DNI 30.111.555');

    // Volver a Personales y editar el nombre actualiza el resumen en vivo.
    await personalTab.click();
    await form.locator('#Nombre').fill('Marcelo');
    await expect(pvName).toHaveText('Marcelo Lopez');

    // Abrir directamente un paso permitido (Contacto) conserva el resumen.
    await contactoTab.click();
    await expect(pvName).toHaveText('Marcelo Lopez');
    await expect(pvDoc).toHaveText('DNI 30.111.555');

    expectNoFailures(failures);
});

test('resumen dinámico: no se activa en drawer Edit, /Cliente/Create ni /Cliente/Edit/{id}', async ({ page }) => {
    const failures = trackFailures(page);

    // Drawer Edit: el bloque de Contacto del resumen dinámico no existe en
    // absoluto (se renderiza solo dentro de "@if (isWizard)").
    await page.goto('/Cliente', { waitUntil: 'domcontentloaded' });
    // La tabla desktop y la tarjeta mobile de Cliente/Index renderizan cada
    // una su propio botón "Editar" (uno queda oculto según el breakpoint):
    // ":visible" toma el que realmente se puede clickear en este viewport.
    const editButton = page.locator('[data-cliente-modal-open="edit"]:visible').first();
    const clienteId = await editButton.getAttribute('data-cliente-id');
    await editButton.click();
    const editForm = page.locator('#cliente-modal-form');
    await expect(editForm).toBeVisible();
    await expect(editForm).not.toHaveAttribute('data-cliente-wizard', 'create');
    await expect(page.locator('#pv-contact')).toHaveCount(0);
    await page.locator('#modal-cliente-cancel, #modal-cliente-cancel-bottom').first().click();

    // /Cliente/Create (full-page): tampoco existe el bloque.
    await page.goto('/Cliente/Create', { waitUntil: 'domcontentloaded' });
    await expect(page.locator('#pv-contact')).toHaveCount(0);

    // /Cliente/Edit/{id} (full-page): tampoco existe el bloque.
    if (clienteId) {
        await page.goto(`/Cliente/Edit/${clienteId}`, { waitUntil: 'domcontentloaded' });
        await expect(page.locator('#pv-contact')).toHaveCount(0);
    }

    expectNoFailures(failures);
});

test('responsive (Lote 3): en mobile se suma el stepper compacto y las tabs quedan icon-only, sin dejar de ser clickeables', async ({ page }) => {
    const failures = trackFailures(page);
    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto('/Cliente', { waitUntil: 'domcontentloaded' });
    await page.locator('[data-cliente-modal-open="create"]').first().click();

    const form = page.locator('#cliente-modal-form');
    await expect(form).toBeVisible();

    const conyugeTab = form.locator('[data-cliente-tab="t-conyuge"]');
    const conyugeLabel = conyugeTab.locator('.tab-label');
    const compact = form.locator('[data-wizard-compact]');
    const compactLabel = form.locator('[data-wizard-compact-label]');
    const compactIcon = form.locator('[data-wizard-compact-icon]');
    const compactFill = form.locator('[data-wizard-compact-fill]');

    // El resumen compacto se suma arriba de la barra (no la reemplaza): las
    // tabs siguen siendo botones reales, visibles y clickeables (icon-only,
    // el texto pasa a sr-only) — "abrir manualmente un paso permitido" no
    // puede depender de que la barra completa con texto esté visible.
    await expect(compact).toBeVisible();
    await expect(compactLabel).toHaveText('Personales');
    await expect(compactIcon).toHaveText('badge');
    await expect(compactFill).toHaveCSS('width', /.+/);
    await expect(conyugeTab).toBeVisible();
    await expect(conyugeLabel).toHaveCSS('position', 'absolute'); // sr-only, no display:none
    await expect(conyugeLabel).toHaveText('Cónyuge'); // el texto sigue en el DOM/accessible name, solo oculto visualmente

    // Completar Personales y avanzar con Siguiente actualiza ícono, nombre y
    // progreso del compacto — mismo dato que el tab real, nunca una copia
    // hardcodeada.
    await form.locator('#NumeroDocumento').fill('30111777');
    await form.locator('#Apellido').fill('Diaz');
    await form.locator('#Nombre').fill('Rocio');
    await form.locator('#cliente-wizard-next').click();
    await expect(compactLabel).toHaveText('Cónyuge');
    await expect(compactIcon).toHaveText('diversity_3');

    // Click directo en una tab ya permitida (Personales, atrás) sigue
    // funcionando igual que en desktop/tablet: no se rompió por quedar
    // icon-only en mobile.
    const personalTab = form.locator('[data-cliente-tab="t-personal"]');
    await personalTab.click();
    await expect(personalTab).toHaveAttribute('aria-current', 'step');
    await expect(compactLabel).toHaveText('Personales');

    expectNoFailures(failures);
});

test('responsive (Lote 3): el paso activo permanece visible en la barra aunque los 5 tabs no entren completos', async ({ page }) => {
    const failures = trackFailures(page);
    await page.setViewportSize({ width: 1024, height: 768 });
    await page.goto('/Cliente', { waitUntil: 'domcontentloaded' });
    await page.locator('[data-cliente-modal-open="create"]').first().click();

    const form = page.locator('#cliente-modal-form');
    await expect(form).toBeVisible();

    await form.locator('#NumeroDocumento').fill('30111888');
    await form.locator('#Apellido').fill('Paz');
    await form.locator('#Nombre').fill('Julian');
    await form.locator('#cliente-wizard-next').click(); // Personales -> Cónyuge (sin Estado civil elegido, no se saltea)
    await form.locator('#cliente-wizard-next').click(); // Cónyuge -> Contacto (sin obligatorios propios)

    await form.locator('#Telefono').fill('1123456789');
    await form.locator('#Domicilio').fill('Calle Falsa 123');

    // Avanza hasta el último paso (Crédito): antes del fix, la barra no
    // auto-scrolleaba y el tab activo quedaba totalmente fuera de vista.
    await form.locator('#cliente-wizard-next').click(); // Contacto -> Laboral
    await form.locator('#cliente-wizard-next').click(); // Laboral -> Crédito
    await expect(form.locator('[data-wizard-progress]')).toHaveText('Paso 5 de 5');

    const tabsBox = await form.locator('#form-tabs').boundingBox();
    const creditoBox = await form.locator('[data-cliente-tab="t-credito"]').boundingBox();
    expect(tabsBox).not.toBeNull();
    expect(creditoBox).not.toBeNull();
    expect(creditoBox.x).toBeGreaterThanOrEqual(tabsBox.x - 1);
    expect(creditoBox.x + creditoBox.width).toBeLessThanOrEqual(tabsBox.x + tabsBox.width + 1);

    expectNoFailures(failures);
});
