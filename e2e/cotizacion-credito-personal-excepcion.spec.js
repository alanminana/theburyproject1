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
 * Cliente de prueba: se crea uno nuevo en cada corrida (DNI único derivado de
 * Date.now()) sin documentación cargada — self-contained, no depende de un
 * fixture externo ni de otro spec.
 */
const { test, expect } = require('playwright/test');

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
    await page.getByRole('textbox', { name: 'Teléfono *' }).fill('1122334455');
    await page.getByRole('textbox', { name: 'Domicilio *' }).fill('Calle Falsa 123');
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
    test('Cliente No apto por documentación (sin mora): Solicitar excepción → Continuar con excepción crea la venta autorizada', async ({ page }) => {
        await page.setViewportSize({ width: 1366, height: 768 });

        const dni = String(Date.now()).slice(-8);
        const clienteId = await crearClienteSinDocumentacion(page, dni);
        expect(clienteId).toBeTruthy();

        await page.goto('/Cotizacion', { waitUntil: 'domcontentloaded' });
        await expect(page.locator('#cotizacion-simular')).toBeAttached({ timeout: 10_000 });

        // El botón sólo debe ofrecerse si el usuario actual tiene ventas.authorize — mismo gate
        // que Venta/Create (@if (User.TienePermiso("ventas","authorize")) en _VentaWizardForm).
        const puedeExcepcion = await page.locator('[data-cotizacion-simulador]').getAttribute('data-puede-excepcion-documental');
        test.skip(puedeExcepcion !== 'true', 'El usuario E2E actual no tiene ventas.authorize en este entorno.');

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
        test.skip(sinAccesoABcra, 'BCRA no es alcanzable desde la red Docker CI aislada (sin egreso a internet); ver comentario arriba.');
        await expect(aptitudTexto).toContainText('No apto', { timeout: 10_000 });

        const filaCredito = page.locator('#cotizacion-resultados-tbody tr').filter({ hasText: 'Credito personal' }).first();
        // "No apto" sigue viéndose como "No apto" (§5 del pedido) — la excepción no lo cambia.
        await expect(filaCredito.locator('.pill')).toHaveText(/No apto/);

        const btnExcepcion = filaCredito.locator('[data-cotizacion-excepcion]');
        await expect(btnExcepcion).toBeVisible();
        await expect(filaCredito.locator('[data-cotizacion-elegir]')).toHaveCount(0);
        await btnExcepcion.click();

        // El formulario vive en el drawer (§4 del pedido), no en un modal nuevo.
        await expect(page.locator('#plan-excepcion-formulario')).toBeVisible({ timeout: 5_000 });
        await page.fill('#plan-excepcion-motivo', 'E2E COTIZ-EXCEPCION-01: motivo de prueba automatizada.');
        await page.click('[data-cotizacion-excepcion-confirmar]');

        // La fila vuelve a "Excepción solicitada" (nunca "Disponible"/"Elegir") y la barra de
        // cierre ofrece "Continuar con excepción" — no "Continuar con esta opción" (§6: nunca
        // se trata como una selección normal).
        await expect(filaCredito.locator('[data-cotizacion-ver-excepcion]')).toBeVisible();
        const continuar = page.locator('#cotizacion-continuar');
        await expect(continuar).toBeEnabled();
        await expect(page.locator('[data-continuar-label]')).toHaveText('Continuar con excepción');
        await expect(page.locator('#cotizacion-seleccion-resumen')).toContainText('Excepción solicitada');

        // COTIZACION-MIVENTA-02: "Confirmar Mi Venta"/"Continuar con excepción" corre primero
        // el preflight (§NUEVA REGLA FUNDAMENTAL) — Crédito personal SIEMPRE se reporta como
        // bloqueo real (nunca se auto-confirma en el mismo paso; necesita configurar el plan
        // primero, igual que Venta/Create), así que acá nunca se abre el modal de confirmación:
        // se queda en Mi Venta mostrando el bloqueo con "Continuar con wizard" como elección
        // explícita del usuario (§NO HACER del pedido: nunca derivar sola al wizard).
        await continuar.click();
        await expect(page.locator('#cotizacion-bloqueos')).toBeVisible({ timeout: 10_000 });
        await expect(page.locator('#modal-confirmar-venta')).toBeHidden();
        const btnContinuarWizard = page.locator('#cotizacion-bloqueos-lista button', { hasText: 'Continuar con wizard' });
        await expect(btnContinuarWizard).toBeVisible();
        await btnContinuarWizard.click();

        // El backend es la autoridad real (§9 del pedido: nunca autorizar desde el frontend) —
        // se verifica el resultado real navegando a Venta/Details de la venta creada. Crédito
        // personal nunca se auto-confirma en el mismo paso (necesita configurar el plan primero,
        // igual que Venta/Create), así que el destino sigue siendo Venta/Edit.
        await page.waitForURL(/\/Venta\/Edit\/\d+/, { timeout: 15_000 });
        const ventaId = Number(page.url().match(/\/Venta\/Edit\/(\d+)/)?.[1]);
        expect(ventaId).toBeGreaterThan(0);

        await page.goto(`/Venta/Details/${ventaId}`, { waitUntil: 'domcontentloaded' });
        const panelAutorizacion = page.locator('section').filter({ hasText: 'Autorización' }).first();
        await expect(panelAutorizacion).toContainText('Autorizada');
        await expect(panelAutorizacion).toContainText('Excepción documental');
        await expect(panelAutorizacion).toContainText('E2E COTIZ-EXCEPCION-01: motivo de prueba automatizada.');
    });
});
