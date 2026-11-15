// @ts-check
/**
 * D-03 / AC-03-9: cuentas por pagar como libro administrativo, recorrido completo por la UI real:
 * factura del proveedor -> pago parcial -> nota de credito aplicada -> saldo final (+ vencimiento y antiguedad,
 * anulacion de pago con restitucion de saldo, y estado de cuenta).
 *
 * El spec crea su propio proveedor (CreateAjax con un CUIT valido y unico) para no depender de datos sembrados
 * ni omitirse: no modifica datos existentes. El tramo "orden de compra -> factura" se prueba por HTTP
 * (CuentaPorPagarHttpTests.Recorrido_*) porque el dataset E2E no incluye ordenes de compra.
 *
 * Variables: E2E_USER / E2E_PASS (via e2e/global-setup.js); el usuario necesita los permisos cuentasporpagar.* (Administrador los tiene).
 */
const { test, expect } = require('playwright/test');

test.use({ storageState: 'e2e/.auth/user.json' });

test.beforeEach(async ({ page }) => {
    await page.route('**/fonts.googleapis.com/**', route =>
        route.fulfill({ status: 200, contentType: 'text/css', body: '' }));
    await page.route('**/fonts.gstatic.com/**', route =>
        route.fulfill({ status: 204, body: '' }));
});

/** CUIT de 11 digitos con digito verificador correcto, distinto en cada corrida. */
function cuitUnico() {
    const pesos = [5, 4, 3, 2, 7, 6, 5, 4, 3, 2];
    for (let intento = 0; intento < 50; intento++) {
        const base = '30' + String(Math.floor(Math.random() * 1e8)).padStart(8, '0');
        const suma = base.split('').reduce((acc, d, i) => acc + Number(d) * pesos[i], 0);
        let dv = 11 - (suma % 11);
        if (dv === 11) dv = 0;
        if (dv === 10) continue;
        return base + dv;
    }
    throw new Error('No se pudo generar un CUIT valido.');
}

async function crearProveedor(page, razonSocial) {
    await page.goto('/CuentaPorPagar', { waitUntil: 'domcontentloaded' });
    const token = await page.locator('meta[name="csrf-token"]').getAttribute('content');
    const resp = await page.request.post('/Proveedor/CreateAjax', {
        headers: { RequestVerificationToken: token || '' },
        form: { Cuit: cuitUnico(), RazonSocial: razonSocial, Activo: 'true' },
    });
    expect(resp.ok(), `CreateAjax respondio ${resp.status()}`).toBeTruthy();
    const json = await resp.json();
    expect(json.success, JSON.stringify(json)).toBeTruthy();
    return json.entity.id;
}

function trackFailures(page) {
    const consoleErrors = [];
    const serverErrors = [];
    page.on('console', message => {
        if (message.type() !== 'error') return;
        const url = message.location().url || '';
        if (url.includes('fonts.g')) return; // Google Fonts bloqueado a proposito
        consoleErrors.push(message.text());
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

/** Evita desborde horizontal de pagina (una tabla puede hacer scroll interno, la pagina no). */
async function expectSinDesbordeHorizontal(page) {
    const desborde = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    expect(desborde, 'la pagina no debe tener scroll horizontal').toBeLessThanOrEqual(1);
}

/** Envia el formulario y espera la respuesta del POST: evita leer el aviso de error de la pagina anterior. */
async function enviar(page, nombreBoton) {
    await Promise.all([
        page.waitForResponse(r => r.request().method() === 'POST'),
        page.getByRole('button', { name: nombreBoton }).click(),
    ]);
}

async function registrarComprobante(page, proveedorId, { tipo, numero, emision, vencimiento, neto, iva, total }) {
    await page.goto(`/CuentaPorPagar/Crear?proveedorId=${proveedorId}&tipo=${tipo}`, { waitUntil: 'domcontentloaded' });
    await page.locator('#NumeroComprobante').fill(numero);
    await page.locator('#FechaEmision').fill(emision);
    if (vencimiento) await page.locator('#FechaVencimiento').fill(vencimiento);
    await page.locator('#ImporteNeto').fill(neto);
    await page.locator('#ImporteIva').fill(iva);
    await page.locator('#ImporteTotal').fill(total);
    await page.getByRole('button', { name: 'Registrar comprobante' }).click();
    await expect(page).toHaveURL(/\/CuentaPorPagar\/Detalle\/\d+/);
    await expect(page.getByText('Comprobante registrado.')).toBeVisible();
    return Number(page.url().match(/Detalle\/(\d+)/)[1]);
}

test.describe('Cuentas por pagar - recorrido completo (AC-03-9)', () => {
    test('factura -> pago parcial -> nota de credito -> saldo final, con anulacion de pago y antiguedad', async ({ page }) => {
        test.setTimeout(120_000);
        const failures = trackFailures(page);
        const sufijo = Date.now().toString().slice(-7);
        const proveedorId = await crearProveedor(page, `E2E CxP ${sufijo}`);

        // 1) Factura de $ 1.210,00 vencida hace mas de 60 dias (emision y vencimiento fijos en el pasado).
        const facturaId = await registrarComprobante(page, proveedorId, {
            tipo: 'Factura', numero: `A-E2E-${sufijo}`, emision: '2020-01-10', vencimiento: '2020-02-10',
            neto: '1000.00', iva: '210.00', total: '1210.00',
        });
        await expect(page.getByRole('heading', { level: 1 })).toContainText(`A-E2E-${sufijo}`);
        await expect(page.getByText('Pendiente', { exact: true }).first()).toBeVisible();
        await expect(page.getByText(/1\.210,00/).first()).toBeVisible();

        // Unicidad: el mismo numero se rechaza con un mensaje claro y no duplica.
        await page.goto(`/CuentaPorPagar/Crear?proveedorId=${proveedorId}`, { waitUntil: 'domcontentloaded' });
        await page.locator('#NumeroComprobante').fill(`a-e2e-${sufijo}`);
        await page.locator('#FechaEmision').fill('2020-01-11');
        await page.locator('#ImporteNeto').fill('100');
        await page.locator('#ImporteIva').fill('0');
        await page.locator('#ImporteTotal').fill('100');
        await enviar(page, 'Registrar comprobante');
        await expect(page.locator('#cxp-error')).toContainText('Ya existe un comprobante');

        // Importes incoherentes (neto + IVA != total) se rechazan.
        await page.locator('#NumeroComprobante').fill(`A-E2E-OTRA-${sufijo}`);
        await page.locator('#ImporteTotal').fill('150');
        await enviar(page, 'Registrar comprobante');
        await expect(page.locator('#cxp-error')).toContainText('no coincide con el total');

        // 2) Pago parcial de $ 400,00: el helper visual muestra lo imputado y el servidor recalcula saldo y estado.
        await page.goto(`/CuentaPorPagar/Pagar?proveedorId=${proveedorId}&comprobanteId=${facturaId}`, { waitUntil: 'domcontentloaded' });
        await page.locator('#Monto').fill('400');
        await page.getByLabel(/Importe a imputar al comprobante/).fill('400');
        await expect(page.locator('[data-cxp-diferencia]')).toContainText('Coincide con el monto del pago');
        await page.getByLabel(/Importe a imputar al comprobante/).fill('450');
        await expect(page.locator('[data-cxp-diferencia]')).toContainText('de más');
        // El servidor rechaza sobreimputar aunque la UI deje enviar: monto 400 con imputacion 450.
        await enviar(page, 'Registrar pago');
        await expect(page.locator('#cxp-error')).toContainText('superan el monto del pago');
        await page.getByLabel(/Importe a imputar al comprobante/).fill('400');
        await page.locator('#Referencia').fill(`TRF-${sufijo}`);
        await page.getByRole('button', { name: 'Registrar pago' }).click();
        await expect(page).toHaveURL(/\/CuentaPorPagar\/Pago\/\d+/);
        await expect(page.getByText('Pago registrado.')).toBeVisible();
        const pagoUrl = page.url();

        await page.goto(`/CuentaPorPagar/Detalle/${facturaId}`, { waitUntil: 'domcontentloaded' });
        await expect(page.getByText('Pago parcial').first()).toBeVisible();
        await expect(page.getByText(/810,00/).first()).toBeVisible();

        // 3) Nota de credito de $ 100,00 aplicada a la factura.
        const ncId = await registrarComprobante(page, proveedorId, {
            tipo: 'NotaCredito', numero: `NC-E2E-${sufijo}`, emision: '2020-03-01', vencimiento: '',
            neto: '100.00', iva: '0', total: '100.00',
        });
        await page.getByRole('link', { name: /Aplicar a facturas/ }).click();
        await expect(page).toHaveURL(new RegExp(`/CuentaPorPagar/ImputarNotaCredito/${ncId}`));
        await page.getByRole('button', { name: new RegExp('Aplicar el saldo completo de A-E2E') }).click();
        // El saldo completo de la factura (810) supera a la nota (100): el servidor lo rechaza.
        await enviar(page, 'Aplicar nota de crédito');
        await expect(page.locator('#cxp-error')).toContainText('supera');
        await page.getByLabel(/Importe a aplicar a la factura/).fill('100');
        await page.getByRole('button', { name: 'Aplicar nota de crédito' }).click();
        await expect(page.getByText('Nota de crédito aplicada.')).toBeVisible();
        await expect(page.getByText('Aplicada', { exact: true }).first()).toBeVisible();

        // 4) Saldo final de la factura: 1.210 - 400 - 100 = 710.
        await page.goto(`/CuentaPorPagar/Detalle/${facturaId}`, { waitUntil: 'domcontentloaded' });
        await expect(page.getByText(/710,00/).first()).toBeVisible();
        await expect(page.getByText(/Vencido hace \d+ días/).first()).toBeVisible();

        // Estado de cuenta: saldo neto = 1.210 - 100 (NC) - 400 (pago) = 710 y movimientos presentes.
        await page.goto(`/CuentaPorPagar/EstadoCuenta?proveedorId=${proveedorId}`, { waitUntil: 'domcontentloaded' });
        await expect(page.getByRole('region', { name: 'Movimientos del estado de cuenta' })).toBeVisible();
        await expect(page.getByText(`Factura A-E2E-${sufijo}`).first()).toBeVisible();
        await expect(page.getByText(/Pago por transferencia/).first()).toBeVisible();
        await expect(page.locator('dl').getByText(/710,00/).first()).toBeVisible();

        // Antiguedad: el saldo de 710 esta en el tramo "Vencido +60 dias".
        await page.goto(`/CuentaPorPagar/Antiguedad?proveedorId=${proveedorId}`, { waitUntil: 'domcontentloaded' });
        const fila = page.locator('tbody tr').first();
        await expect(fila).toContainText(`E2E CxP ${sufijo}`);
        await expect(fila).toContainText(/710,00/);
        await expect(page.locator('tfoot')).toContainText(/710,00/);

        // 5) Anulacion del pago: contra-registro, saldo restituido (710 + 400 = 1.110) y trazado.
        await page.goto(pagoUrl, { waitUntil: 'domcontentloaded' });
        await page.locator('summary', { hasText: 'Anular pago' }).click();
        await page.getByLabel('Motivo de la anulación').fill('Transferencia rechazada por el banco');
        await page.getByRole('button', { name: 'Confirmar anulación' }).click();
        await expect(page.getByText(/Pago anulado: los saldos fueron restituidos/)).toBeVisible();
        await expect(page.getByText(/Transferencia rechazada por el banco/)).toBeVisible();
        await page.goto(`/CuentaPorPagar/Detalle/${facturaId}`, { waitUntil: 'domcontentloaded' });
        await expect(page.getByText(/1\.110,00/).first()).toBeVisible();
        await page.goto(`/CuentaPorPagar/EstadoCuenta?proveedorId=${proveedorId}`, { waitUntil: 'domcontentloaded' });
        await expect(page.getByText(/Anulación de pago/).first()).toBeVisible();

        // 6) Listado: el filtro por proveedor y "solo vencidos" encuentra la factura; ningun desborde de pagina.
        await page.goto(`/CuentaPorPagar?ProveedorId=${proveedorId}&SoloVencidos=true`, { waitUntil: 'domcontentloaded' });
        await expect(page.getByText(`A-E2E-${sufijo}`).first()).toBeVisible();
        await expectSinDesbordeHorizontal(page);

        expectNoFailures(failures);
    });

    test('las pantallas no desbordan la pagina y el listado vacio explica el estado', async ({ page }) => {
        const failures = trackFailures(page);
        const sufijo = Date.now().toString().slice(-7);
        const proveedorId = await crearProveedor(page, `E2E CxP vacio ${sufijo}`);

        await page.goto(`/CuentaPorPagar?ProveedorId=${proveedorId}`, { waitUntil: 'domcontentloaded' });
        await expect(page.locator('[data-cxp-vacio]')).toBeVisible();
        await expectSinDesbordeHorizontal(page);

        await page.goto(`/CuentaPorPagar/Pagar?proveedorId=${proveedorId}`, { waitUntil: 'domcontentloaded' });
        await expect(page.locator('[data-cxp-vacio]')).toContainText('no tiene comprobantes con saldo pendiente');
        await expectSinDesbordeHorizontal(page);

        await page.goto(`/CuentaPorPagar/EstadoCuenta?proveedorId=${proveedorId}`, { waitUntil: 'domcontentloaded' });
        await expect(page.locator('[data-cxp-vacio]')).toBeVisible();
        await expectSinDesbordeHorizontal(page);

        await page.goto('/CuentaPorPagar/Antiguedad', { waitUntil: 'domcontentloaded' });
        await expectSinDesbordeHorizontal(page);
        expectNoFailures(failures);
    });
});
