// @ts-check
/**
 * COTIZ-EXCEPCION-01 — integración del mecanismo de excepción documental de
 * Venta/Create dentro del Cotizador (pedido explícito del usuario, 2026-09-17).
 *
 * Reutiliza EXACTAMENTE el mismo mecanismo que ya existe en Venta/Create (ver
 * VentaService.PuedeAplicarExcepcionDocumental/AplicarExcepcionDocumentalComoAutorizada,
 * ahora expuesto como IVentaService.AplicarExcepcionDocumentalSiCorresponde y
 * reutilizado por CotizacionConversionService.ConvertirAVentaAsync) — no hay una
 * segunda regla de negocio para esto. Alcance exceptuable: documentación/cupo
 * insuficiente, NUNCA mora (mismo criterio que Venta/Create).
 *
 * CASO 1/2/3 (medios sin condición — Efectivo/Tarjeta) están cubiertos porque
 * accionCellHtml/esCreditoPersonalMedio sólo tocan la rama de Crédito personal;
 * el resto de los medios nunca pasa por esExcepcionDisponible.
 *
 * Este spec cubre el CASO positivo completo (documentación faltante, sin mora,
 * cliente recién creado sin legajo cargado — nunca tiene mora porque nunca tuvo
 * crédito): aparece "Solicitar excepción", el formulario vive en el drawer,
 * confirmar fija el "plan objetivo para excepción" (nunca una selección normal:
 * "No apto" sigue mostrándose, ver estadoCellHtml sin cambios) y "Continuar con
 * excepción" guarda la cotización + convierte a venta con la MISMA autoridad de
 * Venta/Create — verificado contra el resultado real (Venta/Details muestra
 * "Autorizada"/"Excepción documental" vía _VentaAutorizacionPanel.cshtml, el
 * mismo parcial que ya usa Venta/Create, sin código nuevo ahí).
 *
 * Cliente de prueba:
 *  - Modo CI (E2E_CONFIG_AISLADA=1, C-03 2026-11-11): cliente sembrado por OmitidosE2ESeeder
 *    (E2E_CLIENTE_EXCEPCION_DNI): sin documentación, sin créditos y con consulta BCRA «OK» vigente (cache
 *    de 7 días del servicio real, que no llama a internet). La aptitud es la REAL (IClienteAptitudService); solo
 *    hace falta ValidarDocumentacion=true y Crédito personal configurado, que el dataset compartido no tiene. El
 *    workflow los activa SOLO para los tests @config-aislada (ConfiguracionAisladaE2ESeedRunner.Activar, Playwright
 *    con 1 worker) y los restaura con `if: always()`. En este modo NO hay skips: cualquier precondición ausente es un fallo.
 *  - Modo local/legacy: se crea uno nuevo en cada corrida (DNI único derivado de Date.now()) sin documentación
 *    cargada; depende de que la base tenga ValidarDocumentacion=true y BCRA alcanzable, si no se omite.
 */
const { test, expect } = require('playwright/test');

const MODO_CI = process.env.E2E_CONFIG_AISLADA === '1';
const DNI_FIXTURE = process.env.E2E_CLIENTE_EXCEPCION_DNI || '';

/** En modo CI una precondición ausente rompe el test; fuera de CI se omite con el motivo. */
function exigirOOmitir(condicionOk, motivo) {
    if (condicionOk) return;
    if (MODO_CI) throw new Error(`Precondición de CI no cumplida: ${motivo}`);
    test.skip(true, motivo);
}

test.use({ storageState: 'e2e/.auth/user.json' });

test.beforeEach(async ({ page }) => {
    await page.route('**/fonts.googleapis.com/**', route => route.abort()).catch(() => null);
    await page.route('**/fonts.gstatic.com/**', route => route.abort()).catch(() => null);
});

async function crearClienteSinDocumentacion(page, dni) {
    await page.goto('/Cliente/Create', { waitUntil: 'domcontentloaded' });
    await page.getByRole('textbox', { name: 'Número de documento *' }).fill(dni);
    await page.getByRole('textbox', { name: 'Apellido *' }).fill('ExcepcionQA');
    await page.getByRole('textbox', { name: 'Nombre *' }).fill('Cliente');
    await page.getByRole('tab', { name: /Contacto/ }).click();
    await page.locator('#Telefono').fill('1122334455');
    await page.locator('#Domicilio').fill('Calle Falsa 123');
    await page.getByRole('button', { name: /Crear cliente/ }).click();
    await page.waitForURL(/\/Cliente\/Details\/\d+/, { timeout: 15_000 });
    const match = page.url().match(/\/Cliente\/Details\/(\d+)/);
    return match ? Number(match[1]) : null;
}

async function seleccionarCliente(page, dni) {
    const buscar = page.locator('#cotizacion-cliente-buscar');
    await buscar.fill(dni);
    await page.waitForTimeout(600);
    const opcion = page.locator('#cotizacion-clientes-dropdown button').first();
    await expect(opcion).toBeVisible({ timeout: 5_000 });
    await opcion.click();
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
        await page.waitForTimeout(300);
        if (await page.locator('#cotizacion-productos-tbody .cart-row').count() > 0) return true;
    }
    return false;
}

/** Deja tildado únicamente Crédito personal en "Filtrar medios", para que no haya ninguna
 *  alternativa normal disponible que gane la auto-selección de "mejor precio" — así se puede
 *  observar "Continuar con excepción" en la barra de cierre en vez de una opción normal. */
async function dejarSoloCreditoPersonal(page) {
    const summary = page.getByText('Filtrar medios');
    await summary.click();
    for (const label of ['Efectivo', 'Transferencia', 'T. crédito', 'T. débito', 'MercadoPago']) {
        const checkbox = page.getByRole('checkbox', { name: label });
        if (await checkbox.isChecked()) await checkbox.click({ force: true });
    }
    await summary.click();
}

test.describe('Cotización — excepción documental de Crédito personal (COTIZ-EXCEPCION-01)', () => {
    test('@config-aislada Cliente No apto por documentación (sin mora): Solicitar excepción → Continuar con excepción crea la venta autorizada', async ({ page }) => {
        await page.setViewportSize({ width: 1366, height: 768 });

        let dni;
        if (MODO_CI) {
            expect(DNI_FIXTURE, 'Falta E2E_CLIENTE_EXCEPCION_DNI (OmitidosE2ESeeder)').toBeTruthy();
            dni = DNI_FIXTURE;
        } else {
            dni = String(Date.now()).slice(-8);
            const clienteId = await crearClienteSinDocumentacion(page, dni);
            expect(clienteId).toBeTruthy();
        }

        await page.goto('/Cotizacion', { waitUntil: 'domcontentloaded' });
        await expect(page.locator('#cotizacion-simular')).toBeAttached({ timeout: 10_000 });

        // El botón sólo debe ofrecerse si el usuario actual tiene ventas.authorize — mismo gate
        // que Venta/Create (@if (User.TienePermiso("ventas","authorize")) en _VentaWizardForm).
        const puedeExcepcion = await page.locator('[data-cotizacion-simulador]').getAttribute('data-puede-excepcion-documental');
        exigirOOmitir(puedeExcepcion === 'true', 'El usuario E2E actual no tiene ventas.authorize en este entorno.');

        await seleccionarCliente(page, dni);
        expect(await agregarPrimerProducto(page)).toBeTruthy();
        await dejarSoloCreditoPersonal(page);

        await page.evaluate(() => document.getElementById('cotizacion-simular').click());
        await expect(page.locator('#cotizacion-resultados')).toBeVisible({ timeout: 10_000 });

        // Aptitud real (IClienteAptitudService, la misma que Cliente/Details): No apto por
        // documentación, cliente recién creado nunca tuvo crédito → nunca hay mora.
        // SituacionCrediticiaBcraService llama a la API real de BCRA (api.bcra.gob.ar); el
        // stack CI self-contained no tiene egreso a internet desde la red Docker efímera, así
        // que para un cliente recién creado (sin consulta BCRA cacheada) esa llamada falla y
        // el estado observable pasa a ser "Requiere autorización / No se pudo validar BCRA"
        // en vez de "No apto" — no es drift de este spec ni un bug de la app, es una
        // limitación real de ese entorno de red aislado.
        const aptitudTexto = page.locator('#cotizacion-aptitud-credito');
        await expect(aptitudTexto).not.toBeEmpty({ timeout: 10_000 });
        const sinAccesoABcra = await aptitudTexto.locator('text=/No se pudo validar BCRA/').count() > 0;
        exigirOOmitir(!sinAccesoABcra, 'BCRA no es alcanzable desde la red Docker CI aislada (sin egreso a internet) y el cliente no tiene consulta BCRA vigente; ver comentario arriba.');
        await expect(aptitudTexto).toContainText('No apto', { timeout: 10_000 });

        const filaCredito = page.locator('#cotizacion-resultados-tbody tr').filter({ hasText: /Cr[eé]dito personal/ }).first();
        // "No apto" sigue viéndose como "No apto" (§5 del pedido) — la excepción no lo cambia.
        await expect(filaCredito.locator('.pill')).toHaveText(/No apto/);

        // Con varios planes (Crédito personal configurado) la fila de Crédito personal es el GRUPO («Ver planes») y la acción vive en
        // cada plan hijo (tr.detail[data-g]); con un solo plan la acción está en la misma fila.
        const grupo = await filaCredito.getAttribute('data-group');
        const filasPlan = grupo
            ? page.locator(`#cotizacion-resultados-tbody tr.detail[data-g="${grupo}"]`)
            : filaCredito;
        const filaPlan = filasPlan.first();
        await expect(filaPlan).toBeVisible();

        const btnExcepcion = filaPlan.locator('[data-cotizacion-excepcion]');
        await expect(btnExcepcion).toBeVisible();
        await expect(filasPlan.locator('[data-cotizacion-elegir]')).toHaveCount(0);
        await btnExcepcion.click();

        // El formulario vive en el drawer (§4 del pedido), no en un modal nuevo.
        await expect(page.locator('#plan-excepcion-formulario')).toBeVisible({ timeout: 5_000 });
        await page.fill('#plan-excepcion-motivo', 'E2E COTIZ-EXCEPCION-01: motivo de prueba automatizada.');
        await page.click('[data-cotizacion-excepcion-confirmar]');

        // La fila vuelve a "Excepción solicitada" (nunca "Disponible"/"Elegir") y la barra de
        // cierre ofrece "Continuar con excepción" — no "Continuar con esta opción" (§6: nunca
        // se trata como una selección normal).
        await expect(filaPlan.locator('[data-cotizacion-ver-excepcion]')).toBeVisible();
        const continuar = page.locator('#cotizacion-continuar');
        await expect(continuar).toBeEnabled();
        await expect(page.locator('[data-continuar-label]')).toHaveText('Continuar con excepción');
        await expect(page.locator('#cotizacion-seleccion-resumen')).toContainText('Excepción solicitada');

        // Crédito personal desde "Mi Venta" (cotizacion-simulador.js, «Crédito personal desde Mi Venta»): "Continuar con excepción"
        // abre el modal «Confirmar venta a crédito» (fecha de la 1ª cuota → «Generar contrato» → «Confirmar venta»), con la excepción
        // documental declarada en el resumen y aplicada por el backend en Venta/Confirmar. Nunca se autoriza desde el frontend (§9 del
        // pedido): se verifica el resultado real en Venta/Details.
        await continuar.click();
        const modalCredito = page.locator('#modal-confirmar-credito');
        await expect(modalCredito).toBeVisible({ timeout: 15_000 });
        await expect(page.locator('#cotizacion-credito-resumen')).toContainText('Documental — se aplica al confirmar');
        await expect(page.locator('#modal-confirmar-venta')).toBeHidden();

        // «Generar contrato» crea la venta, configura el crédito y abre el contrato en otra pestaña (se descarta: no se evalúa acá).
        await page.locator('#cotizacion-credito-generar').click();
        const btnConfirmarCredito = page.locator('#cotizacion-credito-confirmar');
        await expect(btnConfirmarCredito).toBeVisible({ timeout: 30_000 });
        await expect(page.locator('#cotizacion-credito-error')).toBeHidden();
        for (const extra of page.context().pages()) {
            if (extra !== page) await extra.close().catch(() => null);
        }
        await btnConfirmarCredito.click();

        await page.waitForURL(/\/Venta\/Details\/\d+/, { timeout: 30_000 });
        const ventaId = Number(page.url().match(/\/Venta\/Details\/(\d+)/)?.[1]);
        expect(ventaId).toBeGreaterThan(0);

        await page.goto(`/Venta/Details/${ventaId}`, { waitUntil: 'domcontentloaded' });
        const panelAutorizacion = page.locator('section').filter({ has: page.getByRole('heading', { name: 'Autorización', exact: true }) });
        await expect(panelAutorizacion).toContainText('Autorizada');
        await expect(panelAutorizacion).toContainText('Excepción documental');
        await expect(panelAutorizacion).toContainText('E2E COTIZ-EXCEPCION-01: motivo de prueba automatizada.');
    });
});
