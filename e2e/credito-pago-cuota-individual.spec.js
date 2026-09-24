// @ts-check
/**
 * PUN-ML9-D — cobro individual de una cuota (`/Credito/PagarCuota/{cuotaId}`).
 *
 * Contratos que este spec protege:
 *  - matriz de permisos opción A: creditos.view + cobranzas.payinstallment;
 *  - el formulario sólo postea intención (importe, medio, comprobante, observaciones,
 *    rowversion) — nunca fecha ni derivados financieros;
 *  - la preview es read-only y la calcula el servidor;
 *  - prioridad punitorio aplicado → capital, y sólo el capital libera cupo;
 *  - el punitorio calculado pero NO aplicado nunca se cobra;
 *  - el recargo por medio de pago es un cargo separado que no paga deuda.
 *
 * Requiere la app corriendo contra una base descartable con los usuarios de prueba de
 * `DbInitializer.CreateTestUsersAsync` (cajero/vendedor/gerente) y un crédito activo con
 * cuotas. Configurable por entorno:
 *   E2E_BASE_URL, E2E_PUNML9D_CREDITO,
 *   E2E_PUNML9D_CUOTA_CON_APLICACION, E2E_PUNML9D_CUOTA_SIN_APLICACION
 */
const { test, expect } = require('playwright/test');

const CREDITO_ID = Number(process.env.E2E_PUNML9D_CREDITO || 1);
const CUOTA_CON_APLICACION = Number(process.env.E2E_PUNML9D_CUOTA_CON_APLICACION || 3);
const CUOTA_SIN_APLICACION = Number(process.env.E2E_PUNML9D_CUOTA_SIN_APLICACION || 4);

const USUARIOS = {
    cajero: { user: 'cajero', pass: 'Cajero123!' },
    vendedor: { user: 'vendedor', pass: 'Vendedor123!' },
    // Aplica punitorios (cobranzas.applyfine); el cajero deliberadamente no puede.
    gerente: { user: 'gerente', pass: 'Gerente123!' },
};

const VIEWPORTS = [
    { name: '360x800', width: 360, height: 800 },
    { name: '768x1024', width: 768, height: 1024 },
    { name: '1366x768', width: 1366, height: 768 },
];

const urlPago = cuotaId => `/Credito/PagarCuota/${cuotaId}`;

// Cada test elige su rol: se ignora el storageState compartido de la suite.
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
        if (message.type() !== 'error') return;
        const text = message.text();
        // Tras el login el layout abre la negociacion SignalR (notificaciones); el page.goto
        // inmediato del test aborta ese fetch en vuelo y el cliente lo loguea como error. Es un
        // artefacto de navegacion (TypeError: Failed to fetch), no un fallo del servidor: un
        // hub roto real sigue detectandose por las respuestas 5xx (serverErrors) y por cualquier
        // otro error de consola.
        if (/negotiation with the server: TypeError: Failed to fetch/.test(text)) return;
        consoleErrors.push(text);
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
 * "$ 12.345,67" -> 12345.67. El panel "Datos del pago"/Previsualización formatea es-AR
 * ('.' miles, ',' decimales: "10,00"); "Contexto autoritativo" formatea invariant/US
 * ('.' decimales, ',' miles: "1,000.00" o, sin miles, "999.00") — dos convenciones
 * distintas conviven en la misma pantalla. En vez de asumir una, el separador que
 * aparece último en el texto es el decimal (estándar en ambos formatos); el otro,
 * si aparece antes, es de miles y se descarta.
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

async function leerContexto(page) {
    const filas = await page.locator('.kv-row').allInnerTexts();
    const buscar = etiqueta => {
        const fila = filas.find(f => f.replace(/\s+/g, ' ').startsWith(etiqueta));
        return fila ? aNumero(fila.slice(etiqueta.length)) : NaN;
    };
    return {
        capitalPendiente: buscar('Capital pendiente'),
        punitorioCalculado: buscar('Punitorio calculado'),
        punitorioAplicado: buscar('Punitorio aplicado pendiente'),
        totalCobrable: buscar('Total cobrable actual'),
    };
}

async function leerPreview(page) {
    const campos = {};
    for (const nombre of ['importeIngresado', 'aplicadoPunitorio', 'aplicadoCapital', 'excedente',
        'recargoMedioPago', 'totalCaja', 'punitorioRestante', 'capitalRestante']) {
        campos[nombre] = aNumero(await page.locator(`[data-preview-field="${nombre}"]`).innerText());
    }
    campos.estadoEstimado = (await page.locator('[data-preview-field="estadoEstimadoTexto"]').innerText()).trim();
    return campos;
}

/** Escribe el importe y espera a que el servidor devuelva una preview válida. */
async function pedirPreview(page, importe, medioPago) {
    const form = page.locator('[data-credito-pago-form]');
    if (medioPago) await page.selectOption('[data-pago-medio]', medioPago);
    await page.fill('[data-pago-monto]', importe);
    await page.locator('[data-pago-monto]').blur();
    await expect(form).toHaveAttribute('data-preview-valid', 'true', { timeout: 15_000 });
    await expect(page.locator('[data-pago-preview-status]'))
        .toHaveText(/calculada por el servidor/i, { timeout: 15_000 });
}

async function confirmar(page) {
    await page.locator('[data-pago-confirmar]').click();
    await expect(page.locator('[data-pago-resultado]')).toBeVisible({ timeout: 20_000 });
}

/**
 * Deja la cuota con punitorio aplicado pendiente usando el panel real de PUN-ML9-C, en una
 * sesión aparte de gerente. Hace el spec re-ejecutable: no depende de datos preexistentes.
 */
async function asegurarPunitorioAplicado(browser, cuotaId) {
    const contexto = await browser.newContext({ storageState: { cookies: [], origins: [] } });
    try {
        const page = await contexto.newPage();
        await login(page, USUARIOS.gerente);
        await page.goto(`/Credito/Details/${CREDITO_ID}`, { waitUntil: 'domcontentloaded' });

        await page.locator(`[aria-controls="punitorio-panel-${cuotaId}"]`).click();
        const panel = page.locator(`#punitorio-panel-${cuotaId}`);
        await expect(panel.getByText('Capital pendiente')).toBeVisible({ timeout: 15_000 });

        const form = panel.locator('form[action*="/Punitorio/Aplicar"]');
        if (!(await form.count())) return;

        await form.locator('textarea').fill('Alta de punitorio para E2E PUN-ML9-D');
        await Promise.all([
            page.waitForResponse(r => /\/Punitorio\/Aplicar$/.test(r.url()), { timeout: 15_000 }),
            form.locator('button[type="submit"]').click(),
        ]);
    } finally {
        await contexto.close();
    }
}

async function abrirPago(page, cuotaId) {
    await page.goto(urlPago(cuotaId), { waitUntil: 'domcontentloaded' });
    await expect(page.locator('[data-credito-pago]')).toBeVisible();
}

test.describe('PUN-ML9-D — matriz de permisos (opción A)', () => {
    test('el vendedor consulta el crédito pero no puede cobrar', async ({ page }) => {
        const failures = trackFailures(page);
        await login(page, USUARIOS.vendedor);

        await page.goto(`/Credito/Details/${CREDITO_ID}`, { waitUntil: 'domcontentloaded' });
        await expect(page.locator('[data-credito-details]')).toBeVisible();
        // Conserva creditos.view: ve el crédito…
        await expect(page.locator('a[href*="/Credito/PagarCuota/"]')).toHaveCount(0);

        // …pero el GET y el POST del cobro están cerrados por permiso, no sólo ocultos.
        // Con cookie auth, ForbidResult se materializa como 302 a AccessDenied en el navegador
        // (la capa de servicio/filtro sí devuelve 403; ver CreditoPagoCuotaIndividualHttpTests).
        const get = await page.request.get(urlPago(CUOTA_SIN_APLICACION), { maxRedirects: 0 });
        expect(get.status()).toBe(302);
        expect(get.headers()['location']).toMatch(/AccessDenied/i);

        const post = await page.request.post(urlPago(CUOTA_SIN_APLICACION), {
            form: { 'Input.MontoIngresado': '1', 'Input.MedioPago': 'Efectivo' },
            maxRedirects: 0,
        });
        expect(post.status()).toBe(302);
        expect(post.headers()['location']).toMatch(/AccessDenied/i);

        expectNoFailures(failures);
    });

    test('el cajero abre la pantalla de cobro', async ({ page }) => {
        const failures = trackFailures(page);
        await login(page, USUARIOS.cajero);
        await abrirPago(page, CUOTA_SIN_APLICACION);
        await expect(page.getByRole('heading', { level: 1, name: 'Registrar pago de cuota' })).toBeVisible();
        await expect(page.locator('[data-pago-confirmar]')).toBeEnabled();
        expectNoFailures(failures);
    });
});

test.describe('PUN-ML9-D — contrato del formulario', () => {
    test.beforeEach(async ({ page }) => { await login(page, USUARIOS.cajero); });

    test('sólo postea intención: sin fecha editable ni derivados financieros', async ({ page }) => {
        await abrirPago(page, CUOTA_SIN_APLICACION);

        const nombres = await page.locator('[data-credito-pago-form] input, [data-credito-pago-form] select, [data-credito-pago-form] textarea')
            .evaluateAll(nodes => nodes.map(n => n.getAttribute('name')).filter(Boolean));

        const permitidos = [
            '__RequestVerificationToken',
            'Input.CuotaRowVersionBase64',
            'Input.MontoIngresado',
            'Input.MedioPago',
            'Input.Comprobante',
            'Input.Observaciones',
        ];
        expect(nombres.sort()).toEqual(permitidos.sort());

        for (const prohibido of ['FechaPago', 'MontoCuota', 'MontoPunitorio', 'TotalAPagar',
            'CapitalPendiente', 'AplicadoPunitorio', 'AplicadoCuota', 'Excedente', 'Recargo']) {
            await expect(page.locator(`[name="${prohibido}"]`)).toHaveCount(0);
        }

        // La fecha comercial se muestra, pero como dato del servidor, no como input.
        await expect(page.getByText(/\(autom[aá]tica\)/i)).toBeVisible();
        await expect(page.locator('[data-credito-pago-form] input[type="date"]')).toHaveCount(0);
    });

    test('el punitorio calculado no aplicado no entra en el total cobrable', async ({ page }) => {
        await abrirPago(page, CUOTA_SIN_APLICACION);
        const ctx = await leerContexto(page);
        expect(ctx.punitorioAplicado).toBe(0);
        expect(ctx.totalCobrable).toBeCloseTo(ctx.capitalPendiente, 2);
    });
});

test.describe('PUN-ML9-D — preview read-only', () => {
    test.beforeEach(async ({ page }) => { await login(page, USUARIOS.cajero); });

    test('no persiste nada y es repetible', async ({ page }) => {
        await abrirPago(page, CUOTA_SIN_APLICACION);
        const antes = await leerContexto(page);

        await pedirPreview(page, '1');
        await pedirPreview(page, '2');
        await pedirPreview(page, '3');

        await page.reload({ waitUntil: 'domcontentloaded' });
        expect(await leerContexto(page)).toEqual(antes);
    });

    test('un pago sin aplicación va 100% a capital', async ({ page }) => {
        await abrirPago(page, CUOTA_SIN_APLICACION);
        const ctx = await leerContexto(page);
        test.skip(!(ctx.capitalPendiente > 10), 'La cuota sin aplicación no tiene capital suficiente.');

        await pedirPreview(page, '10');
        const preview = await leerPreview(page);
        expect(preview.aplicadoPunitorio).toBe(0);
        expect(preview.aplicadoCapital).toBeCloseTo(10, 2);
        expect(preview.capitalRestante).toBeCloseTo(ctx.capitalPendiente - 10, 2);
    });

    test('con aplicación activa el punitorio cobra primero', async ({ page, browser }) => {
        await asegurarPunitorioAplicado(browser, CUOTA_CON_APLICACION);
        await abrirPago(page, CUOTA_CON_APLICACION);
        const ctx = await leerContexto(page);
        expect(ctx.punitorioAplicado).toBeGreaterThan(0);

        // Importe menor al punitorio pendiente: nada debe tocar el capital.
        const parcial = Math.max(1, Math.round(ctx.punitorioAplicado / 2));
        await pedirPreview(page, String(parcial));
        const soloPunitorio = await leerPreview(page);
        expect(soloPunitorio.aplicadoPunitorio).toBeCloseTo(parcial, 2);
        expect(soloPunitorio.aplicadoCapital).toBe(0);
        expect(soloPunitorio.capitalRestante).toBeCloseTo(ctx.capitalPendiente, 2);

        // Importe mayor: punitorio completo y el resto a capital.
        const mixto = ctx.punitorioAplicado + 100;
        await pedirPreview(page, String(mixto));
        const conCapital = await leerPreview(page);
        expect(conCapital.aplicadoPunitorio).toBeCloseTo(ctx.punitorioAplicado, 2);
        expect(conCapital.aplicadoCapital).toBeCloseTo(100, 2);
        expect(conCapital.punitorioRestante).toBe(0);
    });

    test('pagar total cancela punitorio aplicado y capital', async ({ page, browser }) => {
        await asegurarPunitorioAplicado(browser, CUOTA_CON_APLICACION);
        await abrirPago(page, CUOTA_CON_APLICACION);
        const ctx = await leerContexto(page);

        await page.locator('[data-pago-total]').click();
        await expect(page.locator('[data-credito-pago-form]'))
            .toHaveAttribute('data-preview-valid', 'true', { timeout: 15_000 });

        const preview = await leerPreview(page);
        expect(preview.importeIngresado).toBeCloseTo(ctx.totalCobrable, 2);
        expect(preview.punitorioRestante).toBe(0);
        expect(preview.capitalRestante).toBe(0);
        expect(preview.excedente).toBe(0);
        expect(preview.estadoEstimado).toBe('Pagada');
    });

    test('el recargo por medio de pago es un cargo separado', async ({ page }) => {
        await abrirPago(page, CUOTA_SIN_APLICACION);

        await pedirPreview(page, '1000', 'Efectivo');
        const efectivo = await leerPreview(page);

        await pedirPreview(page, '1000', 'Transferencia');
        const transferencia = await leerPreview(page);

        // El recargo cambia el total de caja, nunca lo aplicado a la deuda.
        expect(transferencia.aplicadoPunitorio).toBe(efectivo.aplicadoPunitorio);
        expect(transferencia.aplicadoCapital).toBe(efectivo.aplicadoCapital);
        expect(transferencia.capitalRestante).toBe(efectivo.capitalRestante);
        expect(transferencia.recargoMedioPago).toBeGreaterThan(0);
        expect(transferencia.totalCaja).toBeCloseTo(
            transferencia.importeIngresado + transferencia.recargoMedioPago, 2);
    });
});

test.describe('PUN-ML9-D — confirmación', () => {
    test('registra el pago, muestra el resultado persistido y actualiza el panel B2', async ({ page, browser }) => {
        const failures = trackFailures(page);
        await asegurarPunitorioAplicado(browser, CUOTA_CON_APLICACION);
        await login(page, USUARIOS.cajero);
        await abrirPago(page, CUOTA_CON_APLICACION);

        const ctx = await leerContexto(page);
        expect(ctx.totalCobrable).toBeGreaterThan(0);

        const importe = Math.max(1, Math.round(Math.min(ctx.totalCobrable, ctx.punitorioAplicado || 100)));
        await pedirPreview(page, String(importe));
        const preview = await leerPreview(page);

        await confirmar(page);

        const resultado = page.locator('[data-pago-resultado]');
        await expect(resultado).toContainText('Importe recibido');
        await expect(resultado).toContainText(/Pago #\d+/);
        await expect(resultado).toContainText(/Caja #\d+/);

        const filas = (await resultado.allInnerTexts()).join(' ').replace(/\s+/g, ' ');
        expect(aNumero(filas.match(/Importe recibido ([^A]+?) Aplicado/)?.[1] || '')).toBeCloseTo(importe, 2);

        // El contexto recargado refleja lo persistido: el cobrable bajó exactamente lo aplicado a deuda.
        const despues = await leerContexto(page);
        expect(despues.totalCobrable)
            .toBeCloseTo(ctx.totalCobrable - preview.aplicadoPunitorio - preview.aplicadoCapital, 2);

        // El panel de punitorios (PUN-ML9-B2) ve el mismo pago.
        await page.goto(`/Credito/Details/${CREDITO_ID}`, { waitUntil: 'domcontentloaded' });
        const toggle = page.locator(`[aria-controls="punitorio-panel-${CUOTA_CON_APLICACION}"]`);
        await toggle.click();
        const panel = page.locator(`#punitorio-panel-${CUOTA_CON_APLICACION}`);
        await expect(panel.getByText('Capital pendiente')).toBeVisible({ timeout: 15_000 });
        await expect(panel).toContainText('Total cobrable actual');

        expectNoFailures(failures);
    });

    test('el reenvío con la versión vieja de la cuota se rechaza y no duplica', async ({ page }) => {
        await login(page, USUARIOS.cajero);
        await abrirPago(page, CUOTA_SIN_APLICACION);

        const ctx = await leerContexto(page);
        test.skip(!(ctx.capitalPendiente > 20), 'La cuota no tiene capital suficiente.');

        const token = await page.locator('input[name="__RequestVerificationToken"]').first().inputValue();
        const rowVersion = await page.locator('[data-pago-rowversion]').inputValue();
        const form = {
            __RequestVerificationToken: token,
            'Input.MontoIngresado': '10',
            'Input.MedioPago': 'Efectivo',
            'Input.CuotaRowVersionBase64': rowVersion,
        };

        const primero = await page.request.post(urlPago(CUOTA_SIN_APLICACION), { form, maxRedirects: 0 });
        expect(primero.status()).toBe(302);

        // Mismo rowversion: la cuota ya cambió, el servidor debe rechazarlo.
        const segundo = await page.request.post(urlPago(CUOTA_SIN_APLICACION), { form, maxRedirects: 0 });
        expect(segundo.status()).toBe(409);

        await page.reload({ waitUntil: 'domcontentloaded' });
        const despues = await leerContexto(page);
        expect(despues.capitalPendiente).toBeCloseTo(ctx.capitalPendiente - 10, 2);
    });

    test('un rowversion inválido devuelve 400 sin tocar la cuota', async ({ page }) => {
        await login(page, USUARIOS.cajero);
        await abrirPago(page, CUOTA_SIN_APLICACION);
        const antes = await leerContexto(page);

        const token = await page.locator('input[name="__RequestVerificationToken"]').first().inputValue();
        const respuesta = await page.request.post(urlPago(CUOTA_SIN_APLICACION), {
            form: {
                __RequestVerificationToken: token,
                'Input.MontoIngresado': '10',
                'Input.MedioPago': 'Efectivo',
                'Input.CuotaRowVersionBase64': 'no-es-base64',
            },
            maxRedirects: 0,
        });
        expect(respuesta.status()).toBe(400);

        await page.reload({ waitUntil: 'domcontentloaded' });
        expect(await leerContexto(page)).toEqual(antes);
    });
});

test.describe('PUN-ML9-D — accesibilidad y responsive', () => {
    test.beforeEach(async ({ page }) => { await login(page, USUARIOS.cajero); });

    test('se puede confirmar desde el teclado', async ({ page }) => {
        await abrirPago(page, CUOTA_SIN_APLICACION);
        await pedirPreview(page, '1');

        const boton = page.locator('[data-pago-confirmar]');
        await boton.focus();
        await expect(boton).toBeFocused();
        await page.keyboard.press('Enter');
        await expect(page.locator('[data-pago-resultado]')).toBeVisible({ timeout: 20_000 });
    });

    for (const viewport of VIEWPORTS) {
        test(`sin overflow horizontal en ${viewport.name}`, async ({ page }) => {
            const failures = trackFailures(page);
            await page.setViewportSize({ width: viewport.width, height: viewport.height });
            await abrirPago(page, CUOTA_SIN_APLICACION);

            await expect(page.locator('[data-pago-monto]')).toBeVisible();
            await expect(page.locator('[data-pago-confirmar]')).toBeVisible();

            const overflow = await page.evaluate(() =>
                document.documentElement.scrollWidth - document.documentElement.clientWidth);
            expect(overflow, `overflow horizontal en ${viewport.name}`).toBeLessThanOrEqual(1);

            expectNoFailures(failures);
        });
    }

    test('sin overflow horizontal con zoom 200% (equivale a 683px de ancho CSS)', async ({ page }) => {
        await page.setViewportSize({ width: 683, height: 400 });
        await abrirPago(page, CUOTA_SIN_APLICACION);
        const overflow = await page.evaluate(() =>
            document.documentElement.scrollWidth - document.documentElement.clientWidth);
        expect(overflow).toBeLessThanOrEqual(1);
    });
});
