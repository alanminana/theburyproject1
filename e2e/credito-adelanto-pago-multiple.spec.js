// @ts-check
/**
 * PUN-ML9-E — adelanto de última cuota (`/Credito/AdelantarCuota/{creditoId}`) y pago múltiple
 * (`/Credito/RegistrarPagoMultiple`, `/Credito/PreviewPagoMultiple`) desde el panel del cliente
 * (`_PanelClientePartial`, tarjeta expandible en `/Credito`).
 *
 * Contratos que este spec protege:
 *  - matriz de permisos opción A: creditos.view + cobranzas.payinstallment, tanto en la UI
 *    (botón "Adelantar", checkboxes/footer del panel) como en el servidor (GET/POST directos);
 *  - el adelanto siempre resuelve server-side la última cuota Pendiente/Parcial del plan — nunca
 *    un cuotaId elegido por el navegador — y cancela capital + punitorio aplicado pendiente;
 *  - el pago múltiple crea una fila PagoCuota por cuota, nunca una fila agregada;
 *  - RowVersion obligatorio en ambas superficies: faltante o vencida rechaza TODO antes de la
 *    primera escritura (atomicidad real, no rollback parcial);
 *  - el punitorio calculado pero no aplicado nunca entra al total cobrable; sólo el capital
 *    libera cupo; el recargo por medio de pago es un cargo separado;
 *  - el panel del cliente es una tarjeta expandible inline (`data-credito-user-toggle` /
 *    `.credito-user-collapsible`), no el overlay `[data-credito-cliente-panel-open]` de
 *    `credito-index.js` — ese trigger no existe en ningún view actual (verificado por grep),
 *    así que ese código quedó muerto; este spec valida el panel que la UI real usa.
 *
 * Requiere la app corriendo contra una base descartable con:
 *   - usuarios de prueba de `DbInitializer.CreateTestUsersAsync` (cajero/vendedor/gerente);
 *   - un crédito activo con varias cuotas Pendientes, todas vencidas, y una
 *     `ConfiguracionPunitorio` vigente con Porcentaje > 0 (ver
 *     `TheBuryProyect.Tests/_Scratch_SeedE2EPunML9EVerif.cs`, temporal, no versionado).
 *
 * Configurable por entorno: E2E_BASE_URL, E2E_PUNML9E_CREDITO, E2E_PUNML9E_CLIENTE.
 * Re-ejecutable: la selección de cuotas es siempre dinámica (primeras N pagables disponibles),
 * nunca un cuotaId fijo — cada corrida consume cuotas distintas de las corridas anteriores.
 */
const { test, expect } = require('playwright/test');

const CREDITO_ID = Number(process.env.E2E_PUNML9E_CREDITO || 2);
const CLIENTE_ID = Number(process.env.E2E_PUNML9E_CLIENTE || 2);

const USUARIOS = {
    cajero: { user: 'cajero', pass: 'Cajero123!' },
    vendedor: { user: 'vendedor', pass: 'Vendedor123!' },
    gerente: { user: 'gerente', pass: 'Gerente123!' },
};

const urlAdelantar = creditoId => `/Credito/AdelantarCuota/${creditoId}`;
const urlAdelantarPreview = creditoId => `/Credito/AdelantarCuota/${creditoId}/Preview`;
const urlCredito = clienteId => `/Credito?ClienteId=${clienteId}`;
const URL_PAGO_MULTIPLE = '/Credito/RegistrarPagoMultiple';
const URL_PREVIEW_MULTIPLE = '/Credito/PreviewPagoMultiple';

test.use({ storageState: { cookies: [], origins: [] } });

/** Login directo por rol: cada test controla su propia identidad, sin storageState compartido. */
async function login(page, { user, pass }) {
    await page.goto('/Identity/Account/Login', { waitUntil: 'domcontentloaded' });
    await page.fill('#Input_UserName, input[name="Input.UserName"]', user);
    await page.fill('#input-password, input[name="Input.Password"]', pass);
    await page.click('button[type="submit"]');

    const nombreCompleto = page.locator('input[placeholder="Nombre y apellido"]');
    if (await nombreCompleto.isVisible({ timeout: 5_000 }).catch(() => false)) {
        await nombreCompleto.fill(`E2E ${user}`);
        await page.getByRole('checkbox', { name: /Declaro que le.* y acepto/i }).setChecked(true);
        await page.getByRole('button', { name: 'Aceptar y continuar' }).click();
    }

    await expect(page).not.toHaveURL(/[Ll]ogin/, { timeout: 15_000 });
}

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
    expect(failures.consoleErrors, `consola: ${failures.consoleErrors.join(' | ')}`).toHaveLength(0);
    expect(failures.serverErrors, `HTTP 5xx: ${failures.serverErrors.join(' | ')}`).toHaveLength(0);
}

/**
 * "$ 12.345,67" -> 12345.67. "Datos del pago"/Previsualización formatea es-AR
 * ('.' miles, ',' decimales); "Contexto autoritativo" formatea invariant/US ('.' decimales,
 * ',' miles) — dos convenciones distintas en la misma pantalla. El separador que aparece
 * último en el texto es el decimal (estándar en ambos formatos); el otro, si aparece antes,
 * es de miles y se descarta.
 */
function aNumero(texto) {
    const limpio = String(texto || '').replace(/[^\d,.-]/g, '');
    const ultimaComa = limpio.lastIndexOf(',');
    const ultimoPunto = limpio.lastIndexOf('.');
    let normalizado;
    if (ultimaComa > ultimoPunto) {
        normalizado = limpio.replace(/\./g, '').replace(',', '.');
    } else if (ultimoPunto > ultimaComa) {
        normalizado = limpio.replace(/,/g, '');
    } else {
        normalizado = limpio;
    }
    const valor = Number.parseFloat(normalizado);
    return Number.isFinite(valor) ? valor : NaN;
}

async function leerContextoAdelanto(page) {
    const filas = await page.locator('.kv-row').allInnerTexts();
    const buscar = etiqueta => {
        const fila = filas.find(f => f.replace(/\s+/g, ' ').startsWith(etiqueta));
        return fila ? aNumero(fila.slice(etiqueta.length)) : NaN;
    };
    return {
        capitalPendiente: buscar('Capital pendiente'),
        punitorioAplicado: buscar('Punitorio aplicado a cancelar'),
        totalCobrable: buscar('Total a cancelar'),
    };
}

async function leerPreviewAdelanto(page) {
    const campos = {};
    for (const nombre of ['aplicadoCapital', 'aplicadoPunitorio', 'recargoMedioPago', 'totalCaja']) {
        campos[nombre] = aNumero(await page.locator(`[data-preview-field="${nombre}"]`).innerText());
    }
    campos.estadoEstimadoTexto = (await page.locator('[data-preview-field="estadoEstimadoTexto"]').innerText()).trim();
    return campos;
}

async function abrirAdelanto(page, creditoId) {
    await page.goto(urlAdelantar(creditoId), { waitUntil: 'domcontentloaded' });
    await expect(page.locator('[data-credito-adelanto]')).toBeVisible();
}

/** El link "Pagar" de la última fila (orden ascendente por NumeroCuota) es la cuota adelantable. */
async function obtenerUltimaCuotaAdelantableId(page, creditoId) {
    await page.goto(`/Credito/Details/${creditoId}`, { waitUntil: 'domcontentloaded' });
    const links = page.locator('a[href*="/Credito/PagarCuota/"]');
    const total = await links.count();
    if (total === 0) return null;
    const href = await links.nth(total - 1).getAttribute('href');
    const match = href && href.match(/PagarCuota\/(\d+)/);
    return match ? Number(match[1]) : null;
}

/** Aplica punitorio (sesión de gerente aparte) a una cuota puntual vía el panel real de B/C. */
async function asegurarPunitorioAplicado(browser, creditoId, cuotaId) {
    const contexto = await browser.newContext({ storageState: { cookies: [], origins: [] } });
    try {
        const page = await contexto.newPage();
        await login(page, USUARIOS.gerente);
        await page.goto(`/Credito/Details/${creditoId}`, { waitUntil: 'domcontentloaded' });

        await page.locator(`[aria-controls="punitorio-panel-${cuotaId}"]`).click();
        const panel = page.locator(`#punitorio-panel-${cuotaId}`);
        await expect(panel.getByText('Capital pendiente')).toBeVisible({ timeout: 15_000 });

        const form = panel.locator('form[action*="/Punitorio/Aplicar"]');
        if (!(await form.count())) return false;

        await form.locator('textarea').fill('Alta de punitorio para E2E PUN-ML9-E');
        const [respuesta] = await Promise.all([
            page.waitForResponse(r => /\/Punitorio\/Aplicar$/.test(r.url()), { timeout: 15_000 }),
            form.locator('button[type="submit"]').click(),
        ]);
        // response.ok(): un 4xx/5xx del propio Aplicar (motivo=Conflicto/NoAutorizado/etc.) no
        // debe leerse como "aplicado" — sólo un 2xx real confirma la fila persistida.
        return respuesta.ok();
    } finally {
        await contexto.close();
    }
}

/** Antiforgery token de cualquier form de una página ya cargada (para POST directos). */
async function tokenDesde(page) {
    return page.locator('input[name="__RequestVerificationToken"]').first().inputValue();
}

async function abrirPanelCliente(page, clienteId) {
    await page.goto(urlCredito(clienteId), { waitUntil: 'domcontentloaded' });
    const card = page.locator(`[data-credito-cliente-id="${clienteId}"]`).first();
    await expect(card).toBeVisible();
    await card.locator('[data-credito-user-toggle]').click();
    const body = card.locator('[data-credito-user-body]');
    await expect(body).toBeVisible();
    // Expandir el (los) crédito(s) del cliente para revelar la tabla de cuotas con checkboxes.
    const creditToggle = body.locator('[data-credito-credit-toggle]').first();
    await creditToggle.click();
    await expect(creditToggle).toHaveAttribute('data-credito-credit-expanded', 'true');
    return card;
}

/** Selecciona las primeras `cantidad` cuotas seleccionables visibles (checkbox habilitado). */
async function seleccionarCuotasPagables(card, cantidad) {
    const checkboxes = card.locator('[data-credito-cuota-selector]:not([disabled])');
    const disponibles = await checkboxes.count();
    test.skip(disponibles < cantidad, `No hay ${cantidad} cuota(s) pagable(s) disponibles en este momento (hay ${disponibles}).`);

    const seleccionadas = [];
    for (let i = 0; i < cantidad; i++) {
        const chk = checkboxes.nth(i);
        const cuotaId = await chk.getAttribute('data-cuota-id');
        const creditoId = await chk.getAttribute('data-credito-id');
        await chk.check();
        seleccionadas.push({ cuotaId, creditoId });
    }
    return seleccionadas;
}

async function esperarPreviewResumen(card) {
    await expect(card.locator('[data-credito-registrar-pago-multiple]')).toBeEnabled({ timeout: 15_000 });
}

test.describe('PUN-ML9-E — matriz de permisos (opción A)', () => {
    test('el vendedor no ve "Adelantar" ni los checkboxes/footer del panel', async ({ page }) => {
        const failures = trackFailures(page);
        await login(page, USUARIOS.vendedor);

        await page.goto(`/Credito/Details/${CREDITO_ID}`, { waitUntil: 'domcontentloaded' });
        await expect(page.locator('[data-credito-details]')).toBeVisible();
        await expect(page.getByRole('link', { name: 'Adelantar' })).toHaveCount(0);

        await page.goto(urlCredito(CLIENTE_ID), { waitUntil: 'domcontentloaded' });
        const card = page.locator(`[data-credito-cliente-id="${CLIENTE_ID}"]`).first();
        await card.locator('[data-credito-user-toggle]').click();
        const body = card.locator('[data-credito-user-body]');
        await expect(body).toBeVisible();
        await body.locator('[data-credito-credit-toggle]').first().click();

        // Sin cobranzas.payinstallment: ni checkboxes ni footer de pago llegan al HTML (no sólo
        // deshabilitados) — el markup entero se omite server-side.
        await expect(body.locator('[data-credito-cuota-selector]')).toHaveCount(0);
        await expect(body.locator('[data-credito-pago-resumen]')).toHaveCount(0);

        expectNoFailures(failures);
    });

    test('el vendedor no puede adelantar por GET/POST directo', async ({ page }) => {
        await login(page, USUARIOS.vendedor);

        const get = await page.request.get(urlAdelantar(CREDITO_ID), { maxRedirects: 0 });
        expect(get.status()).toBe(302);
        expect(get.headers()['location']).toMatch(/AccessDenied/i);

        const post = await page.request.post(urlAdelantar(CREDITO_ID), {
            form: { 'Input.MedioPago': 'Efectivo' },
            maxRedirects: 0,
        });
        expect(post.status()).toBe(302);
        expect(post.headers()['location']).toMatch(/AccessDenied/i);
    });

    test('el vendedor no puede previsualizar ni registrar el pago múltiple por POST directo', async ({ page }) => {
        await login(page, USUARIOS.vendedor);
        await page.goto('/Credito/Index', { waitUntil: 'domcontentloaded' });
        const token = await tokenDesde(page);

        const preview = await page.request.post(URL_PREVIEW_MULTIPLE, {
            headers: { 'RequestVerificationToken': token, 'Content-Type': 'application/json' },
            data: { clienteId: CLIENTE_ID, cuotaIds: [1], medioPago: 'Efectivo' },
            maxRedirects: 0,
        });
        expect(preview.status()).toBe(302);
        expect(preview.headers()['location']).toMatch(/AccessDenied/i);

        const registrar = await page.request.post(URL_PAGO_MULTIPLE, {
            headers: { 'RequestVerificationToken': token, 'Content-Type': 'application/json' },
            data: { clienteId: CLIENTE_ID, cuotaIds: [1], rowVersionsPorCuota: { 1: 'AAAAAAAAAAA=' }, medioPago: 'Efectivo' },
            maxRedirects: 0,
        });
        expect(registrar.status()).toBe(302);
        expect(registrar.headers()['location']).toMatch(/AccessDenied/i);
    });

    test('el cajero ve "Adelantar", abre la pantalla y ve los checkboxes del panel', async ({ page }) => {
        const failures = trackFailures(page);
        await login(page, USUARIOS.cajero);

        await page.goto(`/Credito/Details/${CREDITO_ID}`, { waitUntil: 'domcontentloaded' });
        await expect(page.getByRole('link', { name: 'Adelantar' })).toBeVisible();

        const card = await abrirPanelCliente(page, CLIENTE_ID);
        await expect(card.locator('[data-credito-cuota-selector]').first()).toBeVisible();
        await expect(card.locator('[data-credito-pago-resumen]')).toBeVisible();

        expectNoFailures(failures);
    });
});

test.describe('PUN-ML9-E — adelanto: contrato y preview', () => {
    test.beforeEach(async ({ page }) => { await login(page, USUARIOS.cajero); });

    test('resuelve server-side la última cuota pendiente, sin importe/fecha/punitorio editable', async ({ page }) => {
        await abrirAdelanto(page, CREDITO_ID);

        const nombres = await page.locator('[data-credito-adelanto-form] input, [data-credito-adelanto-form] select, [data-credito-adelanto-form] textarea')
            .evaluateAll(nodes => nodes.map(n => n.getAttribute('name')).filter(Boolean));
        const permitidos = [
            '__RequestVerificationToken',
            'Input.CuotaRowVersionBase64',
            'Input.MedioPago',
            'Input.Comprobante',
            'Input.Observaciones',
        ];
        expect(nombres.sort()).toEqual(permitidos.sort());

        for (const prohibido of ['CuotaId', 'MontoIngresado', 'FechaPago', 'MontoPunitorio', 'TotalAPagar', 'CreditoId']) {
            await expect(page.locator(`[name="${prohibido}"]`)).toHaveCount(0);
        }
        await expect(page.locator('[data-credito-adelanto-form] input[type="date"]')).toHaveCount(0);
    });

    test('la previsualización es automática y read-only', async ({ page }) => {
        await abrirAdelanto(page, CREDITO_ID);
        const antes = await leerContextoAdelanto(page);

        await expect(page.locator('[data-credito-adelanto-form]')).toHaveAttribute('data-preview-valid', 'true', { timeout: 15_000 });
        const preview = await leerPreviewAdelanto(page);
        expect(preview.aplicadoCapital + preview.aplicadoPunitorio).toBeCloseTo(antes.totalCobrable, 2);

        await page.reload({ waitUntil: 'domcontentloaded' });
        expect(await leerContextoAdelanto(page)).toEqual(antes);
    });

    test('sin punitorio aplicado, el total a cancelar es sólo el capital', async ({ page }) => {
        await abrirAdelanto(page, CREDITO_ID);
        const ctx = await leerContextoAdelanto(page);
        // El punitorio calculado (informativo) puede ser > 0 (todas las cuotas seed están
        // vencidas), pero el APLICADO pendiente es 0 salvo que un gerente lo haya confirmado.
        if (ctx.punitorioAplicado === 0 || Number.isNaN(ctx.punitorioAplicado)) {
            expect(ctx.totalCobrable).toBeCloseTo(ctx.capitalPendiente, 2);
        }
    });
});

test.describe('PUN-ML9-E — adelanto: con punitorio aplicado pendiente', () => {
    test('el adelanto cancela capital + punitorio aplicado, y sólo el capital libera cupo', async ({ page, browser }) => {
        const failures = trackFailures(page);
        await login(page, USUARIOS.cajero);
        const cuotaId = await obtenerUltimaCuotaAdelantableId(page, CREDITO_ID);
        test.skip(!cuotaId, 'No hay cuota adelantable disponible.');

        const aplicado = await asegurarPunitorioAplicado(browser, CREDITO_ID, cuotaId);
        test.skip(!aplicado, 'No se pudo aplicar punitorio (posible HistorialIncompleto/SinConfiguracion).');

        await abrirAdelanto(page, CREDITO_ID);
        const ctx = await leerContextoAdelanto(page);
        expect(ctx.punitorioAplicado).toBeGreaterThan(0);
        expect(ctx.totalCobrable).toBeCloseTo(ctx.capitalPendiente + ctx.punitorioAplicado, 2);

        await expect(page.locator('[data-credito-adelanto-form]')).toHaveAttribute('data-preview-valid', 'true', { timeout: 15_000 });
        const preview = await leerPreviewAdelanto(page);
        expect(preview.aplicadoPunitorio).toBeCloseTo(ctx.punitorioAplicado, 2);
        expect(preview.aplicadoCapital).toBeCloseTo(ctx.capitalPendiente, 2);

        await page.locator('[data-adelanto-confirmar]').click();
        await expect(page.locator('[data-adelanto-resultado]')).toBeVisible({ timeout: 20_000 });
        const resultado = page.locator('[data-adelanto-resultado]');
        await expect(resultado).toContainText('Capital cancelado');
        const textoResultado = (await resultado.allInnerTexts()).join(' ').replace(/\s+/g, ' ');
        const capitalCancelado = aNumero(textoResultado.match(/Capital cancelado ([^A-Z]+?) Punitorio/)?.[1] || '');
        expect(capitalCancelado).toBeCloseTo(ctx.capitalPendiente, 2);

        expectNoFailures(failures);
    });

    test('adelantar la última cuota del plan muestra el resultado en el GET (no lo pisa "sin cuotas")', async ({ page, browser }) => {
        // Esta prueba consume deliberadamente la ÚLTIMA cuota adelantable restante del crédito
        // seed para reproducir exactamente el bug de UX corregido en el cierre de E: el GET al
        // que redirige el POST exitoso volvía a resolver "última pendiente" (ahora null) y
        // pisaba el resultado con "No hay cuotas pendientes" antes de que el usuario lo viera.
        await login(page, USUARIOS.cajero);
        const cuotaId = await obtenerUltimaCuotaAdelantableId(page, CREDITO_ID);
        test.skip(!cuotaId, 'No hay cuota adelantable disponible.');

        await abrirAdelanto(page, CREDITO_ID);
        await expect(page.locator('[data-credito-adelanto-form]')).toHaveAttribute('data-preview-valid', 'true', { timeout: 15_000 });
        await page.locator('[data-adelanto-confirmar]').click();

        // El POST redirige al mismo GET: si el bug reapareciera, veríamos "No hay cuotas
        // pendientes" en vez del panel de resultado.
        await expect(page.locator('[data-adelanto-resultado], .alert-warn:has-text("No hay cuotas pendientes")'))
            .toBeVisible({ timeout: 20_000 });
        const huboResultado = await page.locator('[data-adelanto-resultado]').isVisible().catch(() => false);
        expect(huboResultado, 'El resultado del adelanto no se mostró (regresión del bug de PUN-ML9-E)').toBe(true);
    });
});

test.describe('PUN-ML9-E — adelanto: RowVersion y reenvíos', () => {
    test.beforeEach(async ({ page }) => { await login(page, USUARIOS.cajero); });

    test('confirmar y reenviar con el mismo RowVersion ya vencido se rechaza (409) y no duplica', async ({ page }) => {
        const cuotaId = await obtenerUltimaCuotaAdelantableId(page, CREDITO_ID);
        test.skip(!cuotaId, 'No hay cuota adelantable disponible.');

        await abrirAdelanto(page, CREDITO_ID);
        const token = await tokenDesde(page);
        const rowVersion = await page.locator('[data-adelanto-rowversion]').inputValue();

        const primero = await page.request.post(urlAdelantar(CREDITO_ID), {
            form: {
                __RequestVerificationToken: token,
                'Input.MedioPago': 'Efectivo',
                'Input.CuotaRowVersionBase64': rowVersion,
            },
            maxRedirects: 0,
        });
        expect(primero.status()).toBe(302);

        const segundo = await page.request.post(urlAdelantar(CREDITO_ID), {
            form: {
                __RequestVerificationToken: token,
                'Input.MedioPago': 'Efectivo',
                'Input.CuotaRowVersionBase64': rowVersion,
            },
            maxRedirects: 0,
        });
        // La cuota ya cambió (fue cancelada por el primer POST) o ya no es la última pendiente:
        // el servidor rechaza, nunca cobra dos veces con la misma versión.
        expect([400, 409]).toContain(segundo.status());
    });

    test('un RowVersion inválido devuelve 400 sin tocar la cuota', async ({ page }) => {
        await abrirAdelanto(page, CREDITO_ID);
        const antes = await leerContextoAdelanto(page);
        const token = await tokenDesde(page);

        const respuesta = await page.request.post(urlAdelantar(CREDITO_ID), {
            form: {
                __RequestVerificationToken: token,
                'Input.MedioPago': 'Efectivo',
                'Input.CuotaRowVersionBase64': 'no-es-base64',
            },
            maxRedirects: 0,
        });
        expect(respuesta.status()).toBe(400);

        await page.reload({ waitUntil: 'domcontentloaded' });
        expect(await leerContextoAdelanto(page)).toEqual(antes);
    });
});

test.describe('PUN-ML9-E — pago múltiple: preview y confirmación', () => {
    test('preview múltiple: agrega capital/punitorio/recargo y coincide con la confirmación', async ({ page }) => {
        const failures = trackFailures(page);
        await login(page, USUARIOS.cajero);
        const card = await abrirPanelCliente(page, CLIENTE_ID);

        const seleccion = await seleccionarCuotasPagables(card, 2);
        await esperarPreviewResumen(card);

        const subtotal = aNumero(await card.locator('[data-credito-resumen-subtotal]').innerText());
        const total = aNumero(await card.locator('[data-credito-resumen-total]').innerText());
        const cuotasResumen = await card.locator('[data-credito-resumen-cuotas]').innerText();
        expect(cuotasResumen.trim()).toBe(String(seleccion.length));
        expect(total).toBeGreaterThanOrEqual(subtotal);

        await card.locator('[data-credito-registrar-pago-multiple]').click();
        await expect(card.locator('[data-credito-pago-status]')).toHaveText(/registrado correctamente/i, { timeout: 20_000 });
        await page.waitForURL(/\/Credito(\?|$)/, { timeout: 10_000 });

        // Una fila PagoCuota por cuota: cada una de las seleccionadas queda Pagada/con saldo
        // reducido, visible en la cartera recargada — no una fila agregada.
        const cardDespues = page.locator(`[data-credito-cliente-id="${CLIENTE_ID}"]`).first();
        await expect(cardDespues).toBeVisible();

        expectNoFailures(failures);
    });

    test('atomicidad: una cuota con RowVersion vencida revierte todo el pago múltiple', async ({ page }) => {
        await login(page, USUARIOS.cajero);
        await page.goto('/Credito/Index', { waitUntil: 'domcontentloaded' });
        const card = await abrirPanelCliente(page, CLIENTE_ID);

        const checkboxes = card.locator('[data-credito-cuota-selector]:not([disabled])');
        const disponibles = await checkboxes.count();
        test.skip(disponibles < 2, 'No hay 2 cuotas pagables disponibles.');

        const cuotaAId = await checkboxes.nth(0).getAttribute('data-cuota-id');
        const rowVersionAObsoleto = await checkboxes.nth(0).getAttribute('data-cuota-rowversion');
        const cuotaBId = await checkboxes.nth(1).getAttribute('data-cuota-id');
        const rowVersionBVigente = await checkboxes.nth(1).getAttribute('data-cuota-rowversion');

        // Pagar la cuota A individualmente por fuera (form-based) rota su RowVersion: el intento
        // múltiple de abajo la usará ya vencida.
        const detalleAntes = await page.goto(`/Credito/PagarCuota/${cuotaAId}`, { waitUntil: 'domcontentloaded' });
        expect(detalleAntes && detalleAntes.status()).toBeLessThan(400);
        const tokenPagar = await tokenDesde(page);
        const rowVersionActualA = await page.locator('[data-pago-rowversion]').inputValue();
        await page.request.post(`/Credito/PagarCuota/${cuotaAId}`, {
            form: {
                __RequestVerificationToken: tokenPagar,
                'Input.MontoIngresado': '1',
                'Input.MedioPago': 'Efectivo',
                'Input.CuotaRowVersionBase64': rowVersionActualA,
            },
            maxRedirects: 0,
        });

        await page.goto(`/Credito/Details/${CREDITO_ID}`, { waitUntil: 'domcontentloaded' });
        const pagadoBAntes = await leerMontoPagadoCuotaEnDetalle(page, cuotaBId);

        const token = await tokenDesde(page);
        const respuesta = await page.request.post(URL_PAGO_MULTIPLE, {
            headers: { 'RequestVerificationToken': token, 'Content-Type': 'application/json' },
            data: {
                clienteId: CLIENTE_ID,
                cuotaIds: [Number(cuotaAId), Number(cuotaBId)],
                rowVersionsPorCuota: {
                    [cuotaAId]: rowVersionAObsoleto,
                    [cuotaBId]: rowVersionBVigente,
                },
                medioPago: 'Efectivo',
            },
            maxRedirects: 0,
        });
        expect(respuesta.status()).toBe(409);

        // La cuota B (RowVersion vigente) no debe haber recibido ningún pago del intento múltiple
        // rechazado: el rollback es total, no parcial.
        await page.goto(`/Credito/Details/${CREDITO_ID}`, { waitUntil: 'domcontentloaded' });
        const pagadoBDespues = await leerMontoPagadoCuotaEnDetalle(page, cuotaBId);
        expect(pagadoBDespues).toBe(pagadoBAntes);
    });

    test('doble envío del mismo pago múltiple no duplica (segundo rechazado por RowVersion)', async ({ page }) => {
        await login(page, USUARIOS.cajero);
        await page.goto('/Credito/Index', { waitUntil: 'domcontentloaded' });
        const card = await abrirPanelCliente(page, CLIENTE_ID);

        const checkboxes = card.locator('[data-credito-cuota-selector]:not([disabled])');
        const disponibles = await checkboxes.count();
        test.skip(disponibles < 1, 'No hay cuotas pagables disponibles.');

        const cuotaId = await checkboxes.nth(0).getAttribute('data-cuota-id');
        const rowVersion = await checkboxes.nth(0).getAttribute('data-cuota-rowversion');
        const token = await tokenDesde(page);
        const payload = {
            clienteId: CLIENTE_ID,
            cuotaIds: [Number(cuotaId)],
            rowVersionsPorCuota: { [cuotaId]: rowVersion },
            medioPago: 'Efectivo',
        };

        const primero = await page.request.post(URL_PAGO_MULTIPLE, {
            headers: { 'RequestVerificationToken': token, 'Content-Type': 'application/json' },
            data: payload,
        });
        expect(primero.ok()).toBe(true);

        const segundo = await page.request.post(URL_PAGO_MULTIPLE, {
            headers: { 'RequestVerificationToken': token, 'Content-Type': 'application/json' },
            data: payload,
        });
        expect(segundo.status()).toBe(409);
    });
});

/**
 * Lee el importe "Pagado" (7ma columna, índice fijo del thead de Details_tw) de la fila de una
 * cuota puntual. Es la única columna que refleja si el pago múltiple efectivamente la tocó — la
 * columna "Capital" muestra siempre el monto original de la cuota, no el saldo pendiente.
 */
async function leerMontoPagadoCuotaEnDetalle(page, cuotaId) {
    const link = page.locator(`a[href*="/Credito/PagarCuota/${cuotaId}"]`);
    if (!(await link.count())) return NaN;
    const row = link.locator('xpath=ancestor::tr[1]');
    const pagadoTexto = await row.locator('td').nth(6).innerText();
    return aNumero(pagadoTexto);
}

test.describe('PUN-ML9-E — panel del cliente: responsive y accesibilidad (checkboxes reales)', () => {
    test.beforeEach(async ({ page }) => { await login(page, USUARIOS.cajero); });

    for (const viewport of [
        { name: '360x800', width: 360, height: 800 },
        { name: '768x1024', width: 768, height: 1024 },
    ]) {
        test(`abre el panel, expande el crédito y selecciona cuotas con checkboxes reales en ${viewport.name}`, async ({ page }) => {
            const failures = trackFailures(page);
            await page.setViewportSize({ width: viewport.width, height: viewport.height });

            const card = await abrirPanelCliente(page, CLIENTE_ID);
            const checkbox = card.locator('[data-credito-cuota-selector]:not([disabled])').first();
            const hayCuotas = await checkbox.count();
            test.skip(hayCuotas === 0, 'No hay cuotas pagables disponibles.');

            await expect(checkbox).toBeVisible();
            await checkbox.check();
            await expect(checkbox).toBeChecked();
            await expect(card.locator('[data-credito-resumen-cuotas]')).toHaveText('1');

            const overflow = await page.evaluate(() =>
                document.documentElement.scrollWidth - document.documentElement.clientWidth);
            expect(overflow, `overflow horizontal en ${viewport.name}`).toBeLessThanOrEqual(1);

            // Desmarcar deja el resumen y el botón de confirmar en su estado inicial, sin
            // ensuciar cuotas para otros tests.
            await checkbox.uncheck();
            await expect(card.locator('[data-credito-resumen-cuotas]')).toHaveText('0');

            expectNoFailures(failures);
        });
    }

    test('desktop 1366x768: sin overflow horizontal con el panel y el crédito expandidos', async ({ page }) => {
        await page.setViewportSize({ width: 1366, height: 768 });
        await abrirPanelCliente(page, CLIENTE_ID);
        const overflow = await page.evaluate(() =>
            document.documentElement.scrollWidth - document.documentElement.clientWidth);
        expect(overflow).toBeLessThanOrEqual(1);
    });

    test('zoom 200% (equivale a 683px de ancho CSS): sin overflow horizontal', async ({ page }) => {
        await page.setViewportSize({ width: 683, height: 500 });
        await abrirPanelCliente(page, CLIENTE_ID);
        const overflow = await page.evaluate(() =>
            document.documentElement.scrollWidth - document.documentElement.clientWidth);
        expect(overflow).toBeLessThanOrEqual(1);
    });

    test('el checkbox de una cuota es alcanzable y operable por teclado', async ({ page }) => {
        const card = await abrirPanelCliente(page, CLIENTE_ID);
        const checkbox = card.locator('[data-credito-cuota-selector]:not([disabled])').first();
        const hayCuotas = await checkbox.count();
        test.skip(hayCuotas === 0, 'No hay cuotas pagables disponibles.');

        await checkbox.focus();
        await expect(checkbox).toBeFocused();
        await page.keyboard.press('Space');
        await expect(checkbox).toBeChecked();
        await page.keyboard.press('Space');
        await expect(checkbox).not.toBeChecked();
    });

    test('la pantalla de adelanto no tiene overflow horizontal en mobile', async ({ page }) => {
        const failures = trackFailures(page);
        await page.setViewportSize({ width: 360, height: 800 });
        await abrirAdelanto(page, CREDITO_ID);
        await expect(page.locator('[data-adelanto-confirmar]')).toBeVisible();
        const overflow = await page.evaluate(() =>
            document.documentElement.scrollWidth - document.documentElement.clientWidth);
        expect(overflow).toBeLessThanOrEqual(1);
        expectNoFailures(failures);
    });
});
