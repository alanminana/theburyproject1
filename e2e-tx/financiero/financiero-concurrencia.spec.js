// @ts-check
/**
 * QA-RELEASE FIN — concurrencia sobre la versión corregida (369b77e).
 *  C1: dos cancelaciones simultáneas de la MISMA venta financiada sin pagos (el lock de fila debe serializar: 1 éxito, sin doble stock/caja).
 *  C2: cobro de cuota concurrente con cancelación (N rondas, venta nueva por ronda). Invariante: nunca queda venta Cancelada con cobro aplicado,
 *      ni cobro en caja sin contrapartida coherente.
 * Sincronización: los formularios (token antiforgery) se leen ANTES; los dos POST se disparan juntos con Promise.all.
 * Datos sintéticos; stack único http://127.0.0.1:18093 (bury-qa-fin / BuryQA_FIN). Requiere haber corrido financiero.spec.js antes
 * (permisos del rol Administrador y plan de 6 cuotas persisten en la base del stack). No cambia assertions de ningún otro spec.
 */
const { test, expect } = require('playwright/test');
const { Sesion, serializarForm, conOverrides } = require('./lib/api');
const db = require('./lib/sql');
const R = require('./lib/reglas');
const rep = require('./lib/reporte');

test.describe.configure({ mode: 'default' });

const RUN = Date.now().toString().slice(-6);
const LETRAS = RUN.replace(/\d/g, d => 'abcdefghij'[Number(d)]);
const PRECIO = 100001, RECARGO = 12, N = 6, RONDAS_C2 = 3;
const hoy = R.hoyIso();
const S = /** @type {any} */ ({});

async function ventaFinanciada(A, etiqueta) {
    const sub = R.r2(PRECIO / 1.21), iva = R.r2(PRECIO - sub);
    const rv = await A.postForm('/Venta/Create', '/Venta/CreateAjax', {
        ClienteId: S.clienteId, FechaVenta: hoy, TipoPago: '5', Estado: '0', Subtotal: sub, IVA: iva, Total: PRECIO, Descuento: '0',
        'Detalles[0].ProductoId': S.productoId, 'Detalles[0].Cantidad': '1', 'Detalles[0].PrecioUnitario': PRECIO, 'Detalles[0].Descuento': '0', 'Detalles[0].Subtotal': PRECIO, 'Detalles[0].PorcentajeIVA': '21',
    });
    const jv = await rv.json();
    if (!jv.success) throw new Error(`${etiqueta}: venta no creada ${JSON.stringify(jv).slice(0, 200)}`);
    const ventaId = jv.ventaId, creditoId = jv.creditoId;
    const v = db.uno(`SELECT RequiereAutorizacion FROM Ventas WHERE Id=${ventaId}`);
    if (v.RequiereAutorizacion === '1' || v.RequiereAutorizacion === 'True') {
        const g = await A.html(`/Venta/Autorizar/${ventaId}`);
        await A.postPares(`/Venta/Autorizar/${ventaId}`, conOverrides(serializarForm(g.html, 'motivo'), { motivo: 'QA concurrencia: BCRA/Veraz no validable en entorno aislado' }));
    }
    const c = await A.html(`/Credito/ConfigurarVenta/${creditoId}?ventaId=${ventaId}&embedded=True`);
    const base = new Date();
    const primera = R.fechaMensual(new Date(Date.UTC(base.getUTCFullYear(), base.getUTCMonth(), 15)), 1);
    const rc = await A.postPares('/Credito/ConfigurarVenta?embedded=True', conOverrides(serializarForm(c.html, 'CantidadCuotas'),
        { CantidadCuotas: N, MetodoCalculo: 'Global', Anticipo: 0, GastosAdministrativos: 0, FechaPrimeraCuota: primera, CobrarPrimeraCuota: 'false' }));
    expect((await rc.json()).success, `${etiqueta}: plan aceptado`).toBe(true);
    await A.postForm(`/ContratoVentaCredito/Preparar?ventaId=${ventaId}`, `/ContratoVentaCredito/Generar?ventaId=${ventaId}`, { ventaId });
    await A.postForm(`/Venta/Details/${ventaId}`, `/Venta/Confirmar/${ventaId}`, { id: ventaId });
    const estado = Number(db.num(`SELECT Estado FROM Ventas WHERE Id=${ventaId}`));
    expect(estado, `${etiqueta}: venta Confirmada(2) antes de la carrera`).toBe(2);
    const primeraCuota = db.uno(`SELECT TOP 1 Id,MontoTotal FROM Cuotas WHERE CreditoId=${creditoId} AND IsDeleted=0 ORDER BY NumeroCuota`);
    return { ventaId, creditoId, cuotaId: Number(primeraCuota.Id), montoCuota: Number(primeraCuota.MontoTotal) };
}

/** Estado persistido observable de una venta/crédito (todo lectura SQL). */
function foto(v) {
    return {
        venta: Number(db.num(`SELECT Estado FROM Ventas WHERE Id=${v.ventaId}`)),
        credito: Number(db.num(`SELECT Estado FROM Creditos WHERE Id=${v.creditoId}`)),
        saldo: db.num(`SELECT SaldoPendiente FROM Creditos WHERE Id=${v.creditoId}`),
        cuotasPagadas: db.num(`SELECT COUNT(*) c FROM Cuotas WHERE CreditoId=${v.creditoId} AND IsDeleted=0 AND (MontoPagado>0 OR Estado IN (2,3))`),
        ledgerAplicado: db.num(`SELECT COUNT(*) c FROM PagosCuota WHERE IsDeleted=0 AND Estado=1 AND CuotaId IN (SELECT Id FROM Cuotas WHERE CreditoId=${v.creditoId})`),
        ingresosCobro: db.num(`SELECT COUNT(*) c FROM MovimientosCaja WHERE IsDeleted=0 AND Tipo=0 AND Concepto=3 AND ReferenciaId IN (SELECT Id FROM Cuotas WHERE CreditoId=${v.creditoId})`),
        egresosVenta: db.num(`SELECT COUNT(*) c FROM MovimientosCaja WHERE IsDeleted=0 AND Tipo=1 AND VentaId=${v.ventaId}`),
        stock: db.num(`SELECT StockActual FROM Productos WHERE Id=${S.productoId}`),
    };
}

async function prepararCancelacion(ses, ventaId, tag) {
    const g = await ses.html(`/Venta/Cancelar/${ventaId}`);
    if (g.status !== 200 || !g.html.includes('name="motivo"')) return null;
    return conOverrides(serializarForm(g.html, 'motivo'), { motivo: `QA concurrencia ${tag}` });
}

async function prepararCobro(ses, cuotaId, monto, comp) {
    const pg = await ses.html(`/Credito/PagarCuota/${cuotaId}`);
    if (pg.status !== 200 || !pg.html.includes('Input.MontoIngresado')) return null;
    return conOverrides(serializarForm(pg.html, 'Input.MontoIngresado'), { 'Input.MontoIngresado': monto.toFixed(2), 'Input.MedioPago': 'Efectivo', 'Input.Comprobante': comp });
}

test.beforeAll(async () => {
    rep.op('Identidad SQL verificada', db.verificarIdentidad());
    S.A = await Sesion.login('admin');
    S.B = await Sesion.login('admin');
    // prerrequisito persistido por financiero.spec.js (FASE 0)
    S.planOk = !!db.uno('SELECT Id FROM ConfiguracionCreditoPersonalCuotas WHERE CantidadCuotas=6');
});

test.afterAll(async () => {
    console.log('RESUMEN-CONCURRENCIA', JSON.stringify(rep.escribir()));
    await S.A?.dispose(); await S.B?.dispose();
});

test('C0 preparación: cliente, producto con stock, aptitud y caja abierta', async () => {
    if (!S.planOk) { rep.bloqueado('C0', 'Preparación', 'Falta plan de 6 cuotas: ejecutar antes financiero.spec.js (FASE 0)'); test.skip(true, 'BLOCKED'); return; }
    const A = S.A;
    const dni = '4' + RUN + '9';
    const jc = await (await A.postForm('/Cliente/ModalCreate', '/Cliente/CreateAjax', {
        TipoDocumento: 'DNI', NumeroDocumento: dni, Apellido: 'Qc' + LETRAS, Nombre: 'Carrera', FechaNacimiento: '1985-03-10',
        Domicilio: 'Calle Sintetica 123', Localidad: 'CABA', Provincia: 'Buenos Aires', CodigoPostal: '1000', Telefono: '1155550001',
        Email: `qa.conc.${RUN}@example.invalid`, TipoEmpleo: 'Relación de dependencia', Empleador: 'Empresa QA', Sueldo: '1500000', TieneReciboSueldo: 'true', Activo: 'true', EstadoCivil: 'Soltero/a',
    })).json();
    S.clienteId = jc.id; rep.op("C0 alta cliente", JSON.stringify(jc).slice(0, 300)); expect(S.clienteId, "alta cliente: " + JSON.stringify(jc).slice(0, 400)).toBeTruthy();
    const jp = await (await A.postForm('/Catalogo', '/Producto/CreateAjax', {
        Codigo: 'QACONC-' + RUN, Nombre: 'Producto QA Concurrencia ' + RUN, Descripcion: 'sintetico', CategoriaId: 3, MarcaId: 4,
        PrecioCompra: '50000', PrecioVenta: String(PRECIO), PorcentajeIVA: '21', StockActual: '0', StockMinimo: '0', Activo: 'true',
        'CreditoPersonal.AdmiteCreditoPersonal': 'true', 'CreditoPersonal.Modo': '0', 'CreditoPersonal.MaxCuotasCredito': '12',
    })).json();
    S.productoId = jp.entity?.id; expect(S.productoId, 'alta producto: ' + JSON.stringify(jp).slice(0, 400)).toBeTruthy();
    await A.postForm('/MovimientoStock/Create', '/MovimientoStock/Create', { ProductoId: S.productoId, Tipo: 'Entrada', Cantidad: '20', Motivo: 'Alta stock QA concurrencia', Referencia: 'QA-CONC-' + RUN });
    await A.postForm(`/Cliente/Details/${S.clienteId}`, '/Cliente/AsignarLimiteCredito', { clienteId: S.clienteId, limiteCredito: '5000000', motivo: 'QA concurrencia: limite sintetico' });
    await A.postForm(`/Cliente/Details/${S.clienteId}`, '/Cliente/RecalcularAptitud', { clienteId: S.clienteId });
    let abierta = db.uno("SELECT Id FROM AperturasCaja WHERE Cerrada=0 AND UsuarioApertura='administrador' AND IsDeleted=0");
    if (!abierta) {
        await A.postForm('/Caja/Create', '/Caja/Create', { Codigo: 'QACONC-' + RUN, Nombre: 'Caja QA Concurrencia ' + RUN, Sucursal: 'QA', Ubicacion: 'QA', Descripcion: 'caja de prueba', Activa: 'true' });
        const caja = db.uno(`SELECT Id FROM Cajas WHERE Codigo='QACONC-${RUN}'`)?.Id;
        if (caja) await A.postForm('/Caja/Abrir', '/Caja/Abrir', { CajaId: caja, MontoInicial: '1000', ObservacionesApertura: 'QA concurrencia' });
        abierta = db.uno("SELECT Id FROM AperturasCaja WHERE Cerrada=0 AND UsuarioApertura='administrador' AND IsDeleted=0");
    }
    S.listo = !!(S.clienteId && S.productoId && abierta);
    rep.op("C0 ids", JSON.stringify({ cliente: S.clienteId, producto: S.productoId, apertura: abierta }));
    rep.chk('C0', 'Datos sintéticos listos (cliente, producto 20 u, caja abierta)', true, S.listo, { nota: JSON.stringify({ cliente: S.clienteId, producto: S.productoId, apertura: abierta?.Id }) });
    expect(S.listo, 'C0: preparación incompleta (ver operaciones C0 en el reporte)').toBe(true);
});

test('C1 dos cancelaciones simultáneas de la misma venta sin pagos', async () => {
    if (!S.listo) { rep.bloqueado('C1', 'Cancelación concurrente', 'Sin preparación'); test.skip(true, 'BLOCKED'); return; }
    const v = await ventaFinanciada(S.A, 'C1');
    const antes = foto(v);
    const [pa, pb] = [await prepararCancelacion(S.A, v.ventaId, 'A'), await prepararCancelacion(S.B, v.ventaId, 'B')];
    expect(pa && pb, 'ambos formularios de cancelación disponibles').toBeTruthy();
    const t0 = Date.now();
    const [ra, rb] = await Promise.all([S.A.postPares(`/Venta/Cancelar/${v.ventaId}`, pa), S.B.postPares(`/Venta/Cancelar/${v.ventaId}`, pb)]);
    const despues = foto(v);
    rep.op('C1 respuestas', { a: ra.status(), b: rb.status(), ms: Date.now() - t0, antes, despues });
    rep.chk('C1', 'Venta termina Cancelada(5) exactamente una vez', 5, despues.venta);
    rep.chk('C1', 'Stock devuelto UNA sola vez (+1, no +2)', antes.stock + 1, despues.stock);
    rep.chk('C1', 'Sin movimientos de caja duplicados (egresos de la venta ≤ 1 por ingreso original)', true, despues.egresosVenta <= antes.ingresosCobro + 1, { nota: JSON.stringify({ antes: antes.egresosVenta, despues: despues.egresosVenta }) });
    let cancelaciones = 'n/d'; try { cancelaciones = db.num(`SELECT COUNT(*) c FROM MovimientosStock WHERE ProductoId=${S.productoId}`); } catch { /* informativo */ }
    rep.op('C1 movimientos de stock de entrada que referencian la venta', cancelaciones);
    rep.chk('C1', 'No hay dos respuestas de "éxito" (302 a página de éxito) simultáneas con efecto doble', true, despues.stock - antes.stock === 1);
});

for (let ronda = 1; ronda <= RONDAS_C2; ronda++) {
    test(`C2.${ronda} cobro de cuota concurrente con cancelación`, async () => {
        if (!S.listo) { rep.bloqueado(`C2.${ronda}`, 'Cobro vs cancelación', 'Sin preparación'); test.skip(true, 'BLOCKED'); return; }
        const v = await ventaFinanciada(S.A, `C2.${ronda}`);
        const antes = foto(v);
        const cobro = await prepararCobro(S.A, v.cuotaId, v.montoCuota, `QA-CONC-${ronda}`);
        const canc = await prepararCancelacion(S.B, v.ventaId, `ronda ${ronda}`);
        if (!cobro) { rep.bloqueado(`C2.${ronda}`, 'Cobro vs cancelación', 'La cuota no es cobrable aún (sin formulario de pago): no se retrodató SQL'); test.skip(true, 'BLOCKED'); return; }
        expect(canc, 'formulario de cancelación disponible').toBeTruthy();
        const [rp, rc] = await Promise.all([S.A.postPares(`/Credito/PagarCuota/${v.cuotaId}`, cobro), S.B.postPares(`/Venta/Cancelar/${v.ventaId}`, canc)]);
        const d = foto(v);
        rep.op(`C2.${ronda} respuestas`, { cobro: rp.status(), cancelacion: rc.status(), antes, despues: d });
        const cancelada = d.venta === 5, cobrada = d.ledgerAplicado > 0 || d.cuotasPagadas > 0 || d.ingresosCobro > 0;
        // Invariante central: nunca Cancelada con cobro vigente (venta cancelada con dinero cobrado y sin reversión).
        rep.chk(`C2.${ronda}`, 'INVARIANTE: no existe venta Cancelada con cobro de cuota aplicado', false, cancelada && cobrada, { nota: JSON.stringify(d) });
        // Coherencia interna del cobro: ledger, cuota y caja cuentan lo mismo.
        rep.chk(`C2.${ronda}`, 'Cobro coherente: ledger aplicado == ingresos en caja == cuotas con pago (0 ó 1)', true,
            d.ledgerAplicado === d.ingresosCobro && d.ledgerAplicado === d.cuotasPagadas && d.ledgerAplicado <= 1, { nota: JSON.stringify(d) });
        // Resultado exclusivo: o cancelada sin cobro, o confirmada con cobro.
        rep.chk(`C2.${ronda}`, 'Resultado exclusivo: (Cancelada sin cobro) ó (Confirmada con cobro)', true, (cancelada && !cobrada) || (d.venta === 2 && cobrada), { nota: `cobro HTTP ${rp.status()}, cancelación HTTP ${rc.status()}` });
        if (cancelada) rep.chk(`C2.${ronda}`, 'Cancelada: stock devuelto una vez', antes.stock + 1, d.stock);
        else rep.chk(`C2.${ronda}`, 'Rechazo de cancelación no tocó stock', antes.stock, d.stock);
    });
}
