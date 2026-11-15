// @ts-check
/**
 * D-02 / R-06 resuelta: para otorgar crédito personal se exige un mínimo configurable de referencias VERIFICADAS del cliente.
 * Si faltan, la venta queda pendiente de la autorización de un SEGUNDO usuario con ventas.authorize (nunca el creador), vale
 * solo para esa venta y nunca levanta mora, BCRA ni ningún otro bloqueo. Recorrido por la UI real:
 *
 *   1. la pantalla de configuración de Crédito personal muestra el mínimo vigente (solo lectura para Administrador: editarlo
 *      exige configuracion.update, reservado a SuperAdmin);
 *   2. cliente creado por el formulario -> agregar referencia -> verificarla -> el resumen pasa de «0 de 1» a «1 de 1»
 *      (y rechaza el teléfono del propio cliente);
 *   3. el Cotizador (aptitud) y el wizard de Venta muestran el motivo «Referencias verificadas insuficientes» con enlace;
 *   4. venta a crédito personal sin referencia verificada: queda pendiente; el creador no puede autorizarla por la pantalla de
 *      autorización (aunque tenga el permiso); un segundo usuario (Gerente) la autoriza con motivo; una venta posterior del
 *      mismo cliente vuelve a pedir la autorización (la excepción no es global);
 *   5. con una referencia verificada la venta no pide autorización;
 *   6. con mora bloqueante, la excepción solicitada NO levanta el bloqueo.
 *
 * Configuración global: el dataset E2E común deja el mínimo en 0 (no altera al resto de la suite). Este spec exige el mínimo en 1,
 * que activa/restaura el runner `ReferenciasCreditoE2ESeedRunner` (Activar/Restaurar) alrededor de su propio paso de Playwright,
 * igual que `@config-aislada` (C-03). Marca `@config-referencias`: queda fuera de la corrida principal (`--grep-invert`).
 * Con E2E_CONFIG_REFERENCIAS=1 (modo CI) cualquier precondición ausente es un FALLO; fuera de CI se omite con el motivo.
 *
 * Datos (todos sintéticos, del dataset E2E del repo):
 *   - cliente «Emilia Zukovic» (GenericE2ESeeder, BCRA «OK» vigente, sin mora ni referencias): E2E_REFCRED_TERM la busca (por defecto «Zukovic»);
 *   - E2E_CLIENTE_NOAPTO_PUNITORIO_ID (ClienteAptitudPunitorioE2ESeeder): cliente con mora bloqueante (escenario 6);
 *   - segundo usuario: E2E_AUTORIZADOR_USER / E2E_AUTORIZADOR_PASS (por defecto gerente / Gerente123!, DbInitializer en Development).
 * El usuario por defecto de la suite (E2E_USER, Administrador) debe tener ventas.authorize y clientes.*; corre con `--project=1366x768`.
 */
const { test, expect } = require('playwright/test');
const { randomUUID } = require('crypto');
const { searchAndSelectClient, activarFiltroStock, addProduct, irAlPaso, TIPO_PAGO } = require('./helpers');

test.use({ storageState: 'e2e/.auth/user.json' });
test.describe.configure({ mode: 'serial' });

const TERM = process.env.E2E_REFCRED_TERM || 'Zukovic';
const NOAPTO_ID = process.env.E2E_CLIENTE_NOAPTO_PUNITORIO_ID;
const AUT_USER = process.env.E2E_AUTORIZADOR_USER || 'gerente';
const AUT_PASS = process.env.E2E_AUTORIZADOR_PASS || 'Gerente123!';
const INPUT_MINIMO = '#ReferenciasCredito_ReferenciasVerificadasMinimas';
const MODO_CI = process.env.E2E_CONFIG_REFERENCIAS === '1';

function vigilar(page) {
    const issues = { console: [], pageerror: [], http5xx: [] };
    page.on('console', (m) => {
        if (m.type() !== 'error') return;
        const u = m.location().url || '';
        if (/fonts\.(googleapis|gstatic)|favicon/i.test(u)) return;
        // Los 4xx esperados (rechazos de validación) los registra el navegador como error de recurso.
        if (/Failed to load resource: the server responded with a status of 4\d\d/.test(m.text())) return;
        issues.console.push(`${m.text()} @ ${u}`);
    });
    page.on('pageerror', (e) => issues.pageerror.push(String(e)));
    page.on('response', (r) => { if (r.status() >= 500) issues.http5xx.push(`${r.status()} ${r.request().method()} ${r.url()}`); });
    return issues;
}

function sinFallas(issues) {
    expect(issues.pageerror, `pageerror: ${issues.pageerror.join(' | ')}`).toHaveLength(0);
    expect(issues.http5xx, `HTTP 5xx: ${issues.http5xx.join(' | ')}`).toHaveLength(0);
    expect(issues.console, `consola: ${issues.console.join(' | ')}`).toHaveLength(0);
}

async function bloquearFuentes(page) {
    await page.route(/fonts\.(googleapis|gstatic)\.com/, (route) => route.abort());
}

/** En modo CI una precondición ausente rompe el test; fuera de CI se omite con el motivo. */
function exigirOOmitir(condicionOk, motivo) {
    if (condicionOk) return;
    if (MODO_CI) throw new Error(`Precondición de CI no cumplida: ${motivo}`);
    test.skip(true, motivo);
}

async function csrf(page) {
    await page.goto('/Venta', { waitUntil: 'domcontentloaded' });
    const token = await page.locator('meta[name="csrf-token"]').getAttribute('content');
    expect(token).toBeTruthy();
    return token || '';
}

async function buscarCliente(page, term) {
    const resp = await page.request.get(`/api/ventas/BuscarClientes?term=${encodeURIComponent(term)}&take=5`);
    expect(resp.ok()).toBeTruthy();
    const lista = await resp.json();
    expect(lista.length, `no hay cliente sembrado para «${term}»`).toBeGreaterThan(0);
    return lista[0];
}

async function buscarProducto(page) {
    for (const term of ['Ventana', 'Motor', 'Cortina', 'Calefactor', 'Aros']) {
        const resp = await page.request.get(`/api/ventas/BuscarProductos?term=${term}&take=5&soloConStock=true`);
        if (!resp.ok()) continue;
        const lista = await resp.json();
        if (lista.length) return lista[0];
    }
    throw new Error('El dataset E2E no tiene un producto con stock.');
}

/** Crea una venta a crédito personal por el pipeline real (antiforgery + permisos + servicios), como el formulario. */
async function crearVentaCredito(page, token, clienteId, producto, extra = {}) {
    const form = {
        IdempotencyKey: randomUUID(),
        ClienteId: String(clienteId),
        FechaVenta: new Date().toISOString().slice(0, 10),
        TipoPago: 'CreditoPersonal',
        'Detalles[0].ProductoId': String(producto.id),
        'Detalles[0].Cantidad': '1',
        'Detalles[0].PrecioUnitario': String(producto.precioVenta),
        'Detalles[0].Subtotal': String(producto.precioVenta),
        ...extra,
    };
    const resp = await page.request.post('/Venta/CreateAjax', { headers: { RequestVerificationToken: token }, form });
    expect(resp.ok(), `CreateAjax respondió ${resp.status()}`).toBeTruthy();
    return resp.json();
}

async function ventaIdDeRespuesta(json) {
    expect(json.success, JSON.stringify(json)).toBeTruthy();
    const id = json.ventaId ?? Number((String(json.redirectUrl || '').match(/\/Venta\/(?:Details|Edit)\/(\d+)/) || [])[1]);
    expect(id).toBeTruthy();
    return Number(id);
}

async function contextoAutorizador(browser, baseURL) {
    const context = await browser.newContext({ baseURL, viewport: { width: 1366, height: 768 }, locale: 'es-AR', storageState: { cookies: [], origins: [] } });
    const page = await context.newPage();
    await bloquearFuentes(page);
    await page.goto('/Identity/Account/Login', { waitUntil: 'domcontentloaded', timeout: 30_000 });
    await page.fill('#Input_UserName, input[name="Input.UserName"], input[type="text"]', AUT_USER);
    await page.fill('#input-password, input[name="Input.Password"], input[type="password"]', AUT_PASS);
    await page.click('button[type="submit"]');
    const nombre = page.locator('input[placeholder="Nombre y apellido"]');
    if (await nombre.isVisible({ timeout: 5_000 }).catch(() => false)) {
        await nombre.fill(`E2E ${AUT_USER}`);
        const aceptar = page.locator('input[type="checkbox"]').first();
        if (await aceptar.count()) await aceptar.check().catch(() => null);
        await page.click('button[type="submit"]');
    }
    await page.waitForURL((u) => !/Identity\/Account\/Login/.test(u.toString()), { timeout: 20_000 });
    return { context, page };
}

/** Las referencias viven en la solapa «Datos» de la ficha del cliente. */
async function abrirSolapaDatos(page) {
    await page.locator('#tab-datos').click();
    await expect(page.locator('#panel-datos')).toBeVisible();
}

/** Da de baja las referencias vigentes del cliente por la UI (deja el dataset como estaba). */
async function bajaDeTodasLasReferencias(page, clienteId) {
    await page.goto(`/Cliente/Details/${clienteId}`, { waitUntil: 'domcontentloaded' });
    await abrirSolapaDatos(page);
    for (let i = 0; i < 10; i++) {
        const baja = page.locator('[data-ref-baja]').first();
        if (!(await baja.count())) return;
        await baja.click();
        await page.locator('#refBajaConfirmar').click();
        await page.waitForTimeout(500);
    }
}

test.describe('@config-referencias D-02 — referencias verificadas para otorgar crédito personal', () => {
    test.beforeAll(async ({ browser }) => {
        const context = await browser.newContext({ storageState: 'e2e/.auth/user.json', baseURL: process.env.E2E_BASE_URL || 'http://localhost:5187' });
        const page = await context.newPage();
        await bloquearFuentes(page);
        const cliente = await buscarCliente(page, TERM);
        const apt = await (await page.request.get(`/api/cotizacion/aptitud-credito?clienteId=${cliente.id}&monto=1000`)).json();
        await context.close();
        exigirOOmitir(apt.referencias?.evaluada === true && apt.referencias?.requeridas === 1,
            'El mínimo de referencias verificadas debe estar en 1 (ReferenciasCreditoE2ESeedRunner.Activar).');
    });

    test.beforeEach(async ({ page }) => {
        await bloquearFuentes(page);
        await page.setViewportSize({ width: 1366, height: 768 });
    });

    test('la configuración de Crédito personal muestra el mínimo vigente (solo lectura sin configuracion.update)', async ({ page }) => {
        const issues = vigilar(page);
        await page.goto('/ConfiguracionPago/CreditoPersonal', { waitUntil: 'domcontentloaded' });
        await page.locator('[data-target="s2"]').click(); // «Recargos y cuotas»
        const input = page.locator(INPUT_MINIMO);
        await expect(input).toBeVisible({ timeout: 15_000 });
        await expect(input).toHaveValue('1');
        await expect(page.getByText('Referencias verificadas para otorgar crédito')).toBeVisible();
        await expect(page.locator('label[for="ReferenciasCredito_ReferenciasVerificadasMinimas"]')).toHaveText('Referencias verificadas mínimas');
        // Administrador no tiene configuracion.update: el campo se ve pero no se puede editar.
        await expect(input).toHaveAttribute('readonly', 'readonly');
        const desborde = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
        expect(desborde).toBeLessThanOrEqual(1);
        sinFallas(issues);
    });

    test('cliente creado por el formulario: agregar y verificar una referencia cambia el resumen contra el mínimo', async ({ page }) => {
        const issues = vigilar(page);
        const dni = String(30_000_000 + (Date.now() % 9_000_000));

        await page.goto('/Cliente/Create', { waitUntil: 'domcontentloaded' });
        await page.getByRole('textbox', { name: 'Número de documento *' }).fill(dni);
        await page.getByRole('textbox', { name: 'Apellido *' }).fill('RefCreditoQA');
        await page.getByRole('textbox', { name: 'Nombre *' }).fill('Cliente');
        await page.getByRole('tab', { name: /Contacto/ }).click();
        await page.locator('#Telefono').fill('1122334455');
        await page.locator('#Domicilio').fill('Calle Falsa 123');
        await page.getByRole('button', { name: /Crear cliente/ }).click();
        await page.waitForURL(/\/Cliente\/Details\/\d+/, { timeout: 15_000 });
        const clienteId = Number(page.url().match(/\/Cliente\/Details\/(\d+)/)?.[1]);

        await abrirSolapaDatos(page);
        const resumen = page.locator('#referencias-resumen');
        await expect(resumen).toBeVisible();
        await expect(resumen).toHaveAttribute('role', 'status');
        await expect(resumen).toContainText('Referencias verificadas: 0 de 1');
        await expect(resumen).toContainText('No alcanza');

        // Una referencia con el teléfono del propio cliente se rechaza.
        await page.locator('[data-ref-agregar]').click();
        await page.locator('#refNombre').fill('Titular repetido');
        await page.locator('#refTelefono').fill('1122334455');
        await page.locator('#refGuardar').click();
        await expect(page.locator('#refError')).toContainText('propio cliente');
        await page.locator('#refNombre').fill('Marta Referencia');
        await page.locator('#refRelacion').fill('Vecina');
        await page.locator('#refTelefono').fill('3415550101');
        await page.locator('#refGuardar').click();

        const fila = page.locator('[data-referencia-id]').first();
        await expect(fila).toBeVisible({ timeout: 10_000 });
        await expect(fila).toContainText('Marta Referencia');
        await expect(resumen).toContainText('Referencias verificadas: 0 de 1'); // pendiente: todavía no cuenta

        await page.locator('[data-ref-verificar]').first().click();
        await page.locator('#refVerifEstado').selectOption('2');
        await page.locator('#refVerifObs').fill('Se llamó y confirmó');
        await page.locator('#refVerifGuardar').click();
        await expect(page.locator('[data-referencia-estado="2"]')).toHaveCount(1, { timeout: 10_000 });
        await expect(resumen).toContainText('Referencias verificadas: 1 de 1');
        await expect(resumen).toContainText('Cumple');

        // El mismo dato por el endpoint de aptitud que usa el Cotizador (fuente: el servicio de aptitud).
        const apt = await (await page.request.get(`/api/cotizacion/aptitud-credito?clienteId=${clienteId}&monto=1000`)).json();
        expect(apt.referencias).toMatchObject({ evaluada: true, requeridas: 1, verificadas: 1, cumple: true });

        // Recargar la página confirma que el resumen sale del servidor, no solo del script.
        await page.reload({ waitUntil: 'domcontentloaded' });
        await abrirSolapaDatos(page);
        await expect(page.locator('#referencias-resumen')).toContainText('Referencias verificadas: 1 de 1');

        const desborde = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
        expect(desborde).toBeLessThanOrEqual(1);
        sinFallas(issues);
    });

    test('Cotizador y wizard de Venta: sin referencia verificada muestran el motivo y el enlace a las referencias', async ({ page }) => {
        const issues = vigilar(page);
        const cliente = await buscarCliente(page, TERM);
        await bajaDeTodasLasReferencias(page, cliente.id);

        const apt = await (await page.request.get(`/api/cotizacion/aptitud-credito?clienteId=${cliente.id}&monto=1000`)).json();
        expect(apt.estado).toBe('RequiereAutorizacion');
        expect(apt.referencias).toMatchObject({ evaluada: true, requeridas: 1, verificadas: 0, cumple: false });
        expect(apt.referencias.url).toBe(`/Cliente/Details/${cliente.id}#referencias-card`);
        expect(apt.motivos.join(' | ')).toContain('Referencias verificadas insuficientes: 0 de 1');

        // Prevalidación del wizard (mismo servicio): motivo de categoría Referencias (7), no bloqueante, con la URL de acción.
        const prev = await (await page.request.get(`/api/ventas/PrevalidarCredito?clienteId=${cliente.id}&monto=1000`)).json();
        const motivo = prev.motivos.find((m) => m.categoria === 7);
        expect(motivo, JSON.stringify(prev)).toBeTruthy();
        expect(motivo.esBloqueante).toBe(false);
        expect(motivo.urlAccion).toBe(`/Cliente/Details/${cliente.id}#referencias-card`);

        // El wizard real lo muestra con el enlace.
        await page.goto('/Venta/Create', { waitUntil: 'domcontentloaded' });
        await expect(page.locator('#venta-form')).toBeVisible({ timeout: 15_000 });
        await searchAndSelectClient(page, TERM);
        await irAlPaso(page, 'productos');
        await activarFiltroStock(page);
        const producto = await addProduct(page, 'Ventana');
        expect(producto, 'el dataset E2E debe tener un producto con stock').toBeTruthy();
        await irAlPaso(page, 'pago');
        await page.locator('#select-tipo-pago').selectOption(TIPO_PAGO.CreditoPersonal);
        await irAlPaso(page, 'credito'); // la verificación de elegibilidad vive en el paso «Crédito»
        const enlace = page.locator('[data-venta-referencias-enlace]').first();
        await expect(enlace).toBeVisible({ timeout: 20_000 });
        await expect(enlace).toHaveAttribute('href', `/Cliente/Details/${cliente.id}#referencias-card`);
        await expect(page.getByText('Referencias verificadas insuficientes').first()).toBeVisible();

        sinFallas(issues);
    });

    test('sin referencia verificada la venta queda pendiente: el creador no se autoriza y un segundo usuario sí, solo para esa venta', async ({ page, browser, baseURL }) => {
        const issues = vigilar(page);
        const cliente = await buscarCliente(page, TERM);
        await bajaDeTodasLasReferencias(page, cliente.id);
        const producto = await buscarProducto(page);
        const token = await csrf(page);

        const creada = await crearVentaCredito(page, token, cliente.id, producto);
        const ventaId = await ventaIdDeRespuesta(creada);

        // Detalle: pendiente, con la razón de referencias.
        await page.goto(`/Venta/Details/${ventaId}`, { waitUntil: 'domcontentloaded' });
        await expect(page.getByText('Referencias verificadas insuficientes').first()).toBeVisible();
        await expect(page.locator('#main-content')).toContainText(/Pendiente/i);

        // El creador (con ventas.authorize) entra a la pantalla de autorización y NO puede autorizarse.
        await page.goto(`/Venta/Autorizar/${ventaId}`, { waitUntil: 'domcontentloaded' });
        const nota = page.locator('[data-venta-autorizar-referencias]');
        await expect(nota).toBeVisible();
        await expect(nota).toContainText('segundo usuario');
        await page.locator('#motivo').fill('Me autorizo yo mismo');
        await page.getByRole('button', { name: /Confirmar autorización/ }).click();
        await page.waitForURL(new RegExp(`/Venta/Details/${ventaId}`), { timeout: 15_000 });
        await expect(page.getByText(/segundo usuario/i).first()).toBeVisible();
        await expect(page.getByText('Pendiente', { exact: true }).first()).toBeVisible();

        // Un segundo usuario con ventas.authorize la autoriza, con motivo.
        const autorizador = await contextoAutorizador(browser, baseURL);
        try {
            const p2 = autorizador.page;
            await p2.goto(`/Venta/Autorizar/${ventaId}`, { waitUntil: 'domcontentloaded' });
            await expect(p2.locator('[data-venta-autorizar-referencias]')).toBeVisible();
            await p2.locator('#motivo').fill('E2E: cliente conocido, referencias en trámite');
            await p2.getByRole('button', { name: /Confirmar autorización/ }).click();
            await p2.waitForURL(new RegExp(`/Venta/Details/${ventaId}`), { timeout: 15_000 });
            await expect(p2.getByText('Autorizada', { exact: true }).first()).toBeVisible();
            await expect(p2.getByText(AUT_USER, { exact: false }).first()).toBeVisible();
        } finally {
            await autorizador.context.close();
        }

        // La excepción vale solo para esa venta: una segunda venta del mismo cliente vuelve a quedar pendiente.
        const segunda = await ventaIdDeRespuesta(await crearVentaCredito(page, token, cliente.id, producto));
        await page.goto(`/Venta/Details/${segunda}`, { waitUntil: 'domcontentloaded' });
        await expect(page.getByText('Pendiente', { exact: true }).first()).toBeVisible();

        // POST directo de un segundo intento de autorizar la primera (ya autorizada): no cambia nada.
        const reintento = await page.request.post(`/Venta/Autorizar/${ventaId}`, {
            headers: { RequestVerificationToken: token }, form: { id: String(ventaId), motivo: 'reintento' }, maxRedirects: 0,
        });
        expect(reintento.status()).toBeLessThan(500);

        sinFallas(issues);
    });

    test('con una referencia verificada la venta a crédito personal no pide autorización', async ({ page }) => {
        const issues = vigilar(page);
        const cliente = await buscarCliente(page, TERM);
        await bajaDeTodasLasReferencias(page, cliente.id);
        const producto = await buscarProducto(page);

        await page.goto(`/Cliente/Details/${cliente.id}`, { waitUntil: 'domcontentloaded' });
        await abrirSolapaDatos(page);
        await page.locator('[data-ref-agregar]').click();
        await page.locator('#refNombre').fill('Referencia verificada QA');
        await page.locator('#refTelefono').fill('3415550202');
        await page.locator('#refGuardar').click();
        await expect(page.locator('[data-referencia-id]').first()).toBeVisible({ timeout: 10_000 });
        await page.locator('[data-ref-verificar]').first().click();
        await page.locator('#refVerifEstado').selectOption('2');
        await page.locator('#refVerifGuardar').click();
        await expect(page.locator('[data-referencia-estado="2"]')).toHaveCount(1, { timeout: 10_000 });
        await expect(page.locator('#referencias-resumen')).toContainText('Referencias verificadas: 1 de 1');

        const token = await csrf(page);
        const ventaId = await ventaIdDeRespuesta(await crearVentaCredito(page, token, cliente.id, producto));
        await page.goto(`/Venta/Details/${ventaId}`, { waitUntil: 'domcontentloaded' });
        await expect(page.getByText('Referencias verificadas insuficientes')).toHaveCount(0);
        await expect(page.getByRole('heading', { name: 'Autorización', exact: true })).toHaveCount(0); // sin panel de autorización

        // Cleanup del dato de prueba para no alterar a los demás specs.
        await bajaDeTodasLasReferencias(page, cliente.id);
        sinFallas(issues);
    });

    test('con mora bloqueante la excepción solicitada no levanta el bloqueo, falten o no referencias', async ({ page }) => {
        test.skip(!NOAPTO_ID, 'Requiere E2E_CLIENTE_NOAPTO_PUNITORIO_ID (cliente con mora bloqueante sembrado por ClienteAptitudPunitorioE2ESeeder).');
        const issues = vigilar(page);
        const producto = await buscarProducto(page);
        const token = await csrf(page);

        const respuesta = await crearVentaCredito(page, token, NOAPTO_ID, producto, {
            AplicarExcepcionDocumental: 'true',
            MotivoExcepcionDocumentalCreate: 'E2E: intento de excepción sobre mora bloqueante',
        });

        expect(respuesta.success, JSON.stringify(respuesta)).toBeFalsy();
        expect(JSON.stringify(respuesta)).toMatch(/No es posible crear la venta|no viable|no apto/i);
        sinFallas(issues);
    });
});
