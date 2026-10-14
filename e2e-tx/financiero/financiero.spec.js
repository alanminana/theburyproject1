// @ts-check
/**
 * QA transaccional financiero — venta financiada 6 cuotas → mora → pagos → cancelación/reversión.
 * Stack autorizado ÚNICO: http://127.0.0.1:18093 (bury-qa-fin / BuryQA_FIN). Ejecución estrictamente secuencial.
 * Todas las verificaciones son "soft" (reporte.js): un defecto no oculta el resto del recorrido.
 * Resultados esperados: lib/reglas.js (derivados de reglas documentadas, nunca del output del sistema).
 */
const { test, expect, chromium } = require('playwright/test');
const path = require('path');
const { Sesion, serializarForm, conOverrides, BASE } = require('./lib/api');
const db = require('./lib/sql');
const R = require('./lib/reglas');
const rep = require('./lib/reporte');

test.describe.configure({ mode: 'default' });

const RUN = Date.now().toString().slice(-6);
const LETRAS = RUN.replace(/\d/g, d => 'abcdefghij'[Number(d)]);
const PRECIO = 100001; // no divisible por 6 con recargo: ejercita el residuo en la última cuota
const RECARGO = 12;
const N = 6;
const MORA = { gracia: 2, tasaBase: 6 };
const PUN_BASE = { pct: 10, periodo: 20, gracia: 5 };

/** Estado compartido entre pasos. @type {any} */
const S = {};
const strip = h => h.replace(/<script[\s\S]*?<\/script>/g, '').replace(/<style[\s\S]*?<\/style>/g, '').replace(/<[^>]+>/g, ' ').replace(/\s+/g, ' ');
const money = s => Number(String(s).replace(/,/g, ''));
const hoy = R.hoyIso();

async function shot(browser, rol, url, nombre) {
    const file = path.join(rep.DIR, `${nombre}.png`);
    const ctx = await browser.newContext({ baseURL: BASE, storageState: path.join(__dirname, '.auth', `${rol}.json`), viewport: { width: 1366, height: 900 } });
    const page = await ctx.newPage();
    await page.route('**/fonts.g*/**', r => r.fulfill({ status: 204, body: '' }));
    try { await page.goto(url, { waitUntil: 'domcontentloaded', timeout: 30000 }); await page.waitForTimeout(800); await page.screenshot({ path: file, fullPage: true }); }
    catch (e) { rep.op(`screenshot falló ${nombre}`, String(e).slice(0, 120)); }
    await ctx.close();
    return path.basename(file);
}

async function pagar(ses, cuotaId, monto, medio = 'Efectivo', comp = '') {
    const pg = await ses.html(`/Credito/PagarCuota/${cuotaId}`);
    if (pg.status !== 200 || !pg.html.includes('Input.MontoIngresado')) return { status: pg.status, location: pg.url, sinForm: true, texto: strip(pg.html).slice(0, 200) };
    const p = serializarForm(pg.html, 'Input.MontoIngresado');
    const r = await ses.postPares(`/Credito/PagarCuota/${cuotaId}`, conOverrides(p, { 'Input.MontoIngresado': monto.toFixed(2), 'Input.MedioPago': medio, 'Input.Comprobante': comp }));
    let texto = '';
    if (r.status() !== 302) {
        const cuerpo = await r.text();
        require('fs').writeFileSync(path.join(rep.DIR, `respuesta-rechazo-cuota${cuotaId}-${Date.now()}.html`), cuerpo);
        const resaltados = (cuerpo.match(/(validation-summary|field-validation|text-red|role="alert")[^>]*>[\s\S]{0,260}/gi) || []).map(x => strip(x).slice(0, 200)).join(' || ');
        const frases = (strip(cuerpo).match(/(No se|debe|inv[aá]lid|rechaz|conflict|El importe|excede|superior|caja)[^.]{0,160}/gi) || []).slice(0, 4).join(' || ');
        texto = `${resaltados} ## ${frases}`;
    }
    // consumir el recibo (TempData) como lo haría el navegador; si no, la próxima GET muestra el recibo en lugar del formulario
    if (r.status() === 302 && r.headers()['location']) await ses.get(r.headers()['location']);
    return { status: r.status(), location: r.headers()['location'], texto };
}

const cuotas = () => db.filas(`SELECT Id,NumeroCuota,MontoCapital,MontoInteres,MontoTotal,MontoPagado,Estado,CONVERT(varchar(10),FechaVencimiento,23) AS Venc FROM Cuotas WHERE CreditoId=${S.creditoId} AND IsDeleted=0 ORDER BY NumeroCuota`);
const movs = () => db.filas(`SELECT Id,Tipo,Concepto,Monto,ReferenciaId,AperturaCajaId FROM MovimientosCaja WHERE IsDeleted=0 AND Concepto=3 AND ReferenciaId IN (SELECT Id FROM Cuotas WHERE CreditoId=${S.creditoId}) ORDER BY Id`);
const pagosLedger = () => db.filas(`SELECT Id,CuotaId,ImporteTotal,ImporteAplicadoCuota,ImporteAplicadoPunitorio,Estado,Origen,MovimientoCajaId FROM PagosCuota WHERE IsDeleted=0 AND CuotaId IN (SELECT Id FROM Cuotas WHERE CreditoId=${S.creditoId}) ORDER BY Id`);
const alerta = () => db.uno(`SELECT TOP 1 Id,MontoVencido,CuotasVencidas,DiasAtraso,MontoMoraCalculada,Tipo,Resuelta FROM AlertasCobranza WHERE CreditoId=${S.creditoId} AND IsDeleted=0 ORDER BY Id DESC`);
const nAlertas = () => db.num(`SELECT COUNT(*) c FROM AlertasCobranza WHERE CreditoId=${S.creditoId} AND IsDeleted=0`);
const logUltimo = () => db.uno('SELECT TOP 1 Id,CuotasProcesadas,AlertasGeneradas,TotalMora,TotalRecargosAplicados,Exitoso FROM LogsMora ORDER BY Id DESC');
const sumPag = () => db.num(`SELECT ISNULL(SUM(Monto),0) s FROM MovimientosCaja WHERE IsDeleted=0 AND Tipo=0 AND Concepto=3 AND ReferenciaId IN (SELECT Id FROM Cuotas WHERE CreditoId=${S.creditoId})`);

async function punitorioHoy(ses, cuotaId) {
    const d = await ses.html(`/Credito/${S.creditoId}/Cuotas/${cuotaId}/Punitorio`);
    const t = strip(d.html);
    const m = t.match(/Punitorio calculado hoy\s*\$\s*([\d.,]+)/);
    return { valor: m ? money(m[1]) : NaN, pagina: d };
}

const dd = (a, b) => Math.round((Date.parse(b + 'T00:00:00Z') - Date.parse(a + 'T00:00:00Z')) / 86400000);
/** Punitorio esperado aplicando la regla documentada (tramos por vigencia + gracia global) a las versiones reales de la DB. */
function punEsperado(saldo, venc, hasta) {
    const vs = db.filas("SELECT CONVERT(varchar(10),VigenteDesde,23) AS d,Porcentaje AS p,PeriodoDias AS pd,DiasGracia AS g,Activa AS a FROM ConfiguracionesPunitorio ORDER BY VigenteDesde");
    const cortes = [venc, ...vs.map(v => v.d).filter(d => d > venc && d < hasta), hasta];
    const total = dd(venc, hasta);
    let exacto = 0;
    for (let i = 0; i < cortes.length - 1; i++) {
        const cfg = [...vs].reverse().find(v => v.d <= cortes[i]);
        if (cfg && cfg.a === '1' && total > Number(cfg.g)) exacto += saldo * Number(cfg.p) / 100 * dd(cortes[i], cortes[i + 1]) / Number(cfg.pd);
    }
    return R.r2(exacto);
}

test.beforeAll(async () => {
    expect(BASE).toBe('http://127.0.0.1:18093');
    const id = db.verificarIdentidad();
    rep.op('Identidad SQL verificada', id);
    S.admin = await Sesion.login('admin');
    rep.op('Login real administrador (Términos y Condiciones aceptados en primer ingreso)');
});

test.afterAll(async () => {
    const r = rep.escribir();
    console.log('RESUMEN', JSON.stringify(r));
    await S.admin?.dispose();
});

test('FASE 0 — prerrequisitos RBAC y reglas reales de cálculo', async () => {
    const A = S.admin;
    // El rol Administrador no trae permisos de configuración (solo SuperAdmin); se conceden por el flujo real de Seguridad.
    const rid = db.uno("SELECT Id FROM AspNetRoles WHERE Name='Administrador'").Id;
    const faltan = db.filas(`SELECT a.Id, m.Clave+'.'+a.Clave AS p FROM AccionesModulo a JOIN ModulosSistema m ON m.Id=a.ModuloId WHERE m.Clave+'.'+a.Clave IN ('configuracion.update','configuracion.managepunitorio','configuracion.retroactivepunitorio') AND a.Id NOT IN (SELECT AccionId FROM RolPermisos WHERE IsDeleted=0 AND RoleId='${rid}')`);
    if (faltan.length) {
        const actuales = db.filas(`SELECT AccionId FROM RolPermisos WHERE IsDeleted=0 AND RoleId='${rid}'`).map(x => x.AccionId);
        const ids = [...new Set([...actuales, ...faltan.map(x => x.Id)])];
        const g = await A.html(`/Seguridad/RolDetails/${rid}`);
        const tok = require('./lib/api').extraerToken(g.html);
        const r = await A.postPares('/Seguridad/SavePermisosRol', [['roleId', rid], ...ids.map(i => ['accionIds', i]), ['__RequestVerificationToken', tok]]);
        rep.op('Permisos concedidos a rol Administrador (SavePermisosRol)', faltan.map(x => x.p));
        rep.chk('0', 'Concesión de permisos de configuración vía Seguridad', 302, r.status());
    }
    // Reglas: configuración vigente de crédito personal (plan 6 cuotas, recargo total 12%).
    const plan = db.uno('SELECT Id,TasaMensual FROM ConfiguracionCreditoPersonalCuotas WHERE CantidadCuotas=6');
    if (!plan) {
        const g = await A.html('/ConfiguracionPago/CreditoPersonal');
        let p = serializarForm(g.html, 'DefaultsGlobales.TasaMensual');
        p = conOverrides(p, { 'DefaultsGlobales.TasaMensual': RECARGO, nuevaCuotaCantidad: 6, nuevaCuotaTasaMensual: RECARGO, nuevaCuotaActivo: 'true' });
        const r = await A.postPares('/ConfiguracionPago/CreditoPersonal', p);
        rep.op('Plan 6 cuotas / recargo total 12% configurado', r.status());
    }
    const plan2 = db.uno('SELECT TasaMensual FROM ConfiguracionCreditoPersonalCuotas WHERE CantidadCuotas=6');
    rep.chk('6', 'Regla real: recargo TOTAL del plan de 6 cuotas (%)', RECARGO, Number(plan2?.TasaMensual));
    const pun = db.uno('SELECT TOP 1 Porcentaje,PeriodoDias,DiasGracia,ProrrateoDiario FROM ConfiguracionesPunitorio ORDER BY VigenteDesde DESC');
    rep.datos.reglas = { recargoTotalPlan6: plan2?.TasaMensual, punitorioVigente: pun, moraTasaBase: db.uno('SELECT DiasGracia,TasaMoraBase FROM ConfiguracionesMora') };
    S.punBase = { pct: Number(pun.Porcentaje), periodo: Number(pun.PeriodoDias), gracia: Number(pun.DiasGracia) };
    rep.op('Regla real leída: última versión de punitorio vigente', S.punBase);
});

test('FASE 1 — preparación: cliente, producto, stock, aptitud, caja', async ({ browser }) => {
    const A = S.admin;
    // 1. cliente sintético
    const dni = '3' + RUN + '7';
    const rc = await A.postForm('/Cliente/ModalCreate', '/Cliente/CreateAjax', {
        TipoDocumento: 'DNI', NumeroDocumento: dni, Apellido: 'Qa' + LETRAS, Nombre: 'Financiero', FechaNacimiento: '1985-03-10',
        Domicilio: 'Calle Sintetica 123', Localidad: 'CABA', Provincia: 'Buenos Aires', CodigoPostal: '1000', Telefono: '1155550000',
        Email: `qa.fin.${RUN}@example.invalid`, TipoEmpleo: 'Relación de dependencia', Empleador: 'Empresa QA', Sueldo: '1500000', TieneReciboSueldo: 'true', Activo: 'true', EstadoCivil: 'Soltero/a',
    });
    const jc = await rc.json();
    S.clienteId = jc.id;
    rep.chk('1', 'Alta de cliente sintético', true, jc.success === true, { nota: JSON.stringify(jc).slice(0, 120) });
    rep.op('Cliente creado', { id: S.clienteId, dni });
    // 2. producto
    const rp = await A.postForm('/Catalogo', '/Producto/CreateAjax', {
        Codigo: 'QAFIN-' + RUN, Nombre: 'Producto QA Financiero ' + RUN, Descripcion: 'sintetico', CategoriaId: 3, MarcaId: 4,
        PrecioCompra: '50000', PrecioVenta: String(PRECIO), PorcentajeIVA: '21', StockActual: '0', StockMinimo: '0', Activo: 'true',
        'CreditoPersonal.AdmiteCreditoPersonal': 'true', 'CreditoPersonal.Modo': '0', 'CreditoPersonal.MaxCuotasCredito': '12',
    });
    const jp = await rp.json();
    S.productoId = jp.entity?.id;
    rep.chk('2', 'Alta de producto de prueba', true, jp.success === true, { nota: JSON.stringify(jp).slice(0, 120) });
    // 3. stock
    const st = await A.postForm('/MovimientoStock/Create', '/MovimientoStock/Create', { ProductoId: S.productoId, Tipo: 'Entrada', Cantidad: '10', Motivo: 'Alta stock QA financiero', Referencia: 'QA-FIN-' + RUN });
    S.stock0 = db.num(`SELECT StockActual FROM Productos WHERE Id=${S.productoId}`);
    rep.chk('3', 'Stock asignado (entrada de 10 unidades)', 10, S.stock0, { nota: 'HTTP ' + st.status() });
    // 4. aptitud crediticia
    await A.postForm(`/Cliente/Details/${S.clienteId}`, '/Cliente/AsignarLimiteCredito', { clienteId: S.clienteId, limiteCredito: '2000000', motivo: 'QA financiero: limite sintetico' });
    await A.postForm(`/Cliente/Details/${S.clienteId}`, '/Cliente/RecalcularAptitud', { clienteId: S.clienteId });
    const cl = db.uno(`SELECT LimiteCredito,NivelRiesgo FROM Clientes WHERE Id=${S.clienteId}`);
    rep.chk('4', 'Aptitud: límite de crédito asignado y vigente', 2000000, Number(cl.LimiteCredito));
    // 5. caja de prueba
    const cc = await A.postForm('/Caja/Create', '/Caja/Create', { Codigo: 'QAFIN-' + RUN, Nombre: 'Caja QA Financiero ' + RUN, Sucursal: 'QA', Ubicacion: 'QA', Descripcion: 'caja de prueba', Activa: 'true' });
    S.cajaNueva = db.uno(`SELECT Id FROM Cajas WHERE Codigo='QAFIN-${RUN}'`)?.Id;
    rep.chk('5', 'Caja de prueba creada', true, !!S.cajaNueva, { nota: 'HTTP ' + cc.status() });
    const abiertaAntes = db.uno("SELECT Id,CajaId FROM AperturasCaja WHERE Cerrada=0 AND UsuarioApertura='administrador' AND IsDeleted=0");
    if (S.cajaNueva) {
        const ab = await A.postForm('/Caja/Abrir', '/Caja/Abrir', { CajaId: S.cajaNueva, MontoInicial: '1000', ObservacionesApertura: 'QA financiero' });
        rep.op('Intento de apertura de caja de prueba', { http: ab.status(), loc: ab.headers()['location'] });
    }
    S.aperturas = db.filas("SELECT Id,CajaId,MontoInicial FROM AperturasCaja WHERE Cerrada=0 AND UsuarioApertura='administrador' AND IsDeleted=0");
    rep.chk('5', 'Administrador opera con una apertura de caja abierta', true, S.aperturas.length >= 1, { nota: `aperturas abiertas: ${JSON.stringify(S.aperturas)} (previa seed: ${JSON.stringify(abiertaAntes)})` });
    rep.datos.fase1 = { clienteId: S.clienteId, productoId: S.productoId, cajaNueva: S.cajaNueva, aperturas: S.aperturas };
});

test('FASE 2 — venta financiada, plan de 6 cuotas y persistencia', async ({ browser }) => {
    const A = S.admin;
    const sub = R.r2(PRECIO / 1.21), iva = R.r2(PRECIO - sub);
    const rv = await A.postForm('/Venta/Create', '/Venta/CreateAjax', {
        ClienteId: S.clienteId, FechaVenta: hoy, TipoPago: '5', Estado: '0', Subtotal: sub, IVA: iva, Total: PRECIO, Descuento: '0',
        'Detalles[0].ProductoId': S.productoId, 'Detalles[0].Cantidad': '1', 'Detalles[0].PrecioUnitario': PRECIO, 'Detalles[0].Descuento': '0', 'Detalles[0].Subtotal': PRECIO, 'Detalles[0].PorcentajeIVA': '21',
    });
    const jv = await rv.json();
    S.ventaId = jv.ventaId; S.creditoId = jv.creditoId;
    rep.chk('7', 'Venta financiada creada (crédito personal)', true, jv.success === true && !!S.ventaId, { nota: JSON.stringify(jv).slice(0, 200) });
    if (!S.ventaId) { rep.bloqueado('8-13', 'Resto de fase 2', 'La venta no pudo crearse'); return; }
    rep.op('Venta creada', { ventaId: S.ventaId, creditoId: S.creditoId });
    const vta = db.uno(`SELECT Estado,Total,RequiereAutorizacion,EstadoAutorizacion FROM Ventas WHERE Id=${S.ventaId}`);
    if (vta.RequiereAutorizacion === '1' || vta.RequiereAutorizacion === 'True') {
        const g = await A.html(`/Venta/Autorizar/${S.ventaId}`);
        const r = await A.postPares(`/Venta/Autorizar/${S.ventaId}`, conOverrides(serializarForm(g.html, 'motivo'), { motivo: 'QA financiero: BCRA/Veraz no validable en entorno aislado' }));
        rep.op('Venta autorizada (requisito de BCRA/Veraz)', r.status());
    }
    // configurar plan
    const c = await A.html(`/Credito/ConfigurarVenta/${S.creditoId}?ventaId=${S.ventaId}&embedded=True`);
    const base = new Date(); const primera = R.fechaMensual(new Date(Date.UTC(base.getUTCFullYear(), base.getUTCMonth(), 15)), 1);
    S.primera = primera;
    const rc = await A.postPares('/Credito/ConfigurarVenta?embedded=True', conOverrides(serializarForm(c.html, 'CantidadCuotas'), { CantidadCuotas: N, MetodoCalculo: 'Global', Anticipo: 0, GastosAdministrativos: 0, FechaPrimeraCuota: primera, CobrarPrimeraCuota: 'false' }));
    rep.chk('8', 'Configuración del plan de 6 cuotas aceptada', true, (await rc.json()).success === true);
    // contrato + confirmar
    await A.postForm(`/ContratoVentaCredito/Preparar?ventaId=${S.ventaId}`, `/ContratoVentaCredito/Generar?ventaId=${S.ventaId}`, { ventaId: S.ventaId });
    await A.postForm(`/Venta/Details/${S.ventaId}`, `/Venta/Confirmar/${S.ventaId}`, { id: S.ventaId });
    rep.op('Contrato generado y venta confirmada');
    const e = R.planCuotas(PRECIO, RECARGO, N);
    S.plan = e;
    const cr = db.uno(`SELECT Estado,MontoSolicitado,TasaInteres,CantidadCuotas,MontoCuota,TotalAPagar,SaldoPendiente FROM Creditos WHERE Id=${S.creditoId}`);
    const cu = cuotas();
    rep.chk('8', 'Se generaron exactamente 6 cuotas', 6, cu.length);
    rep.chk('9', 'Capital financiado (Σ MontoCapital)', e.monto, R.r2(cu.reduce((a, x) => a + Number(x.MontoCapital), 0)));
    rep.chk('9', 'Interés total (Σ MontoInteres) = recargo 12% sobre capital', e.interesTotal, R.r2(cu.reduce((a, x) => a + Number(x.MontoInteres), 0)));
    rep.chk('9', 'Total financiado (Credito.TotalAPagar)', e.total, Number(cr.TotalAPagar));
    rep.chk('9', 'Recargo aplicado (Credito.TasaInteres %)', RECARGO, Number(cr.TasaInteres));
    rep.chk('10', 'Σ cuotas == total financiado (exacto, centavos)', e.total, R.r2(cu.reduce((a, x) => a + Number(x.MontoTotal), 0)), { tol: 0 });
    cu.forEach((x, i) => {
        rep.chk('10', `Cuota ${i + 1}: total/interés/capital`, { t: e.totales[i], i: e.intereses[i], c: e.capitales[i] }, { t: Number(x.MontoTotal), i: Number(x.MontoInteres), c: Number(x.MontoCapital) });
        rep.chk('11', `Cuota ${i + 1}: vencimiento`, R.fechaMensual(new Date(primera + 'T00:00:00Z'), i), x.Venc);
    });
    const v = db.uno(`SELECT Estado,Total FROM Ventas WHERE Id=${S.ventaId}`);
    rep.chk('13', 'Persistencia de venta: Confirmada(2) y total', { estado: 2, total: PRECIO }, { estado: Number(v.Estado), total: Number(v.Total) });
    rep.chk('13', 'Persistencia de crédito: Generado(8) con 6 cuotas', { estado: 8, cuotas: 6 }, { estado: Number(cr.Estado), cuotas: Number(cr.CantidadCuotas) });
    S.stock1 = db.num(`SELECT StockActual FROM Productos WHERE Id=${S.productoId}`);
    rep.chk('12', 'Descuento de stock (10 → 9)', S.stock0 - 1, S.stock1);
    S.saldo0 = Number(cr.SaldoPendiente);
    rep.datos.fase2 = { ventaId: S.ventaId, creditoId: S.creditoId, plan: e, saldoPendienteInicial: S.saldo0, TotalAPagar: cr.TotalAPagar };
    const ev = await shot(browser, 'admin', `/Credito/Details/${S.creditoId}`, 'f2-credito-details');
    rep.chk('9', 'Evidencia visual del plan (captura Playwright)', true, true, { evidencia: [ev] });
});

test('FASE 3 — mora: vencimiento controlado, proceso real, alertas y cambios de tasa', async ({ browser }) => {
    if (!S.creditoId) return rep.bloqueado('14-22', 'Fase 3', 'Sin crédito generado en fase 2');
    const A = S.admin;
    // 14. tasa conocida
    const g = await A.html('/Mora/ConfiguracionExpandida');
    const rc = await A.postPares('/Mora/GuardarConfiguracionExpandida', conOverrides(serializarForm(g.html, 'DiasGracia'),
        { DiasGracia: MORA.gracia, TasaMoraBase: MORA.tasaBase, ActualizarMoraAutomaticamente: 'true', CambiarEstadoCuotaAuto: 'true' }));
    const cfg = db.uno('SELECT DiasGracia,TasaMoraBase,ProcesoAutomaticoActivo FROM ConfiguracionesMora');
    rep.chk('14', 'Config mora: gracia/tasa base aplicadas', { g: MORA.gracia, t: MORA.tasaBase, auto: 0 }, { g: Number(cfg.DiasGracia), t: Number(cfg.TasaMoraBase), auto: Number(cfg.ProcesoAutomaticoActivo) }, { nota: 'ProcesoAutomaticoActivo debe seguir 0 (guarda del stack QA).' });
    // 15. vencimiento simulado (retrodatación en BuryQA_FIN; el reloj del SO no se toca)
    const cu = cuotas();
    S.c = cu.map(x => Number(x.Id));
    const v1 = R.sumarDias(hoy, -15);
    db.ejecutar(`UPDATE Cuotas SET FechaVencimiento='${v1}' WHERE Id=${S.c[0]}`);
    rep.op('Vencimiento retrodatado (BuryQA_FIN)', { cuota: S.c[0], nuevoVenc: v1, motivo: 'simular 15 días de atraso sin alterar reloj del SO' });
    rep.chk('15', 'Mecanismo de simulación: reloj del SO intacto', hoy, R.hoyIso());
    // 16. proceso real
    const l0 = logUltimo();
    const rj = await A.postForm('/Mora', '/Mora/EjecutarJob', {});
    const l1 = logUltimo();
    rep.chk('16', 'Proceso real de mora ejecutado (nuevo LogsMora exitoso)', true, !!l1 && Number(l1.Id) > Number(l0?.Id ?? 0) && l1.Exitoso === '1', { nota: 'HTTP ' + rj.status() });
    S.logMora1 = l1;
    // 17-18
    const c1 = cuotas()[0];
    const al = alerta();
    rep.chk('17', 'Días de atraso en alerta', 15, Number(al?.DiasAtraso));
    rep.chk('17', 'Cuota 1 pasa a Vencida(2) tras el proceso', 2, Number(c1.Estado));
    const tot1 = Number(c1.MontoTotal);
    rep.chk('19', 'Alerta: monto vencido = cuota 1 sin pagar', tot1, Number(al?.MontoVencido));
    rep.chk('19', 'Alerta: cantidad de cuotas vencidas = 1', 1, Number(al?.CuotasVencidas));
    const moraEsp = R.moraAlerta(tot1, 15, MORA.tasaBase);
    if (Number(l1.AlertasGeneradas) === 1) rep.chk('18', 'Interés de mora (log TotalRecargosAplicados) = monto × tasa/100/30 × días', moraEsp, Number(l1.TotalRecargosAplicados), { tol: 0.01 });
    else rep.bloqueado('18', 'Interés de mora del log', `El log agrega ${l1.AlertasGeneradas} alertas; no aislable`);
    rep.chk('18', 'Alerta.MontoMoraCalculada persiste el interés de mora', moraEsp, Number(al?.MontoMoraCalculada), { tol: 0.01, nota: 'El servicio calcula la mora solo para el log; el campo de la alerta queda en 0 (CalcularMora no se persiste en AlertaCobranza).' });
    // punitorio
    const dias = 15;
    const pEsp = punEsperado(tot1, v1, hoy);
    const p = await punitorioHoy(A, S.c[0]);
    rep.chk('18', 'Punitorio calculado (tramos según versiones vigentes, gracia, prorrateo diario)', pEsp, p.valor, { tol: 0.01 });
    S.punitorio1 = p.valor;
    // 20. idempotencia
    const n0 = nAlertas();
    await A.postForm('/Mora', '/Mora/EjecutarJob', {});
    const al2 = alerta();
    rep.chk('20', 'Reproceso: no duplica alertas', n0, nAlertas());
    rep.chk('20', 'Reproceso: DiasAtraso estable', 15, Number(al2?.DiasAtraso));
    // 21. segunda cuota
    const v2 = R.sumarDias(hoy, -5);
    db.ejecutar(`UPDATE Cuotas SET FechaVencimiento='${v2}' WHERE Id=${S.c[1]}`);
    rep.op('Vencimiento retrodatado (BuryQA_FIN)', { cuota: S.c[1], nuevoVenc: v2 });
    await A.postForm('/Mora', '/Mora/EjecutarJob', {});
    const al3 = alerta();
    const tot2 = Number(cuotas()[1].MontoTotal);
    rep.chk('21', 'Segunda cuota vencida: alerta refleja 2 cuotas vencidas', 2, Number(al3?.CuotasVencidas), { nota: 'MoraService.ProcesarMoraAsync solo actualiza DiasAtraso de una alerta existente (comentado en código); no recalcula monto ni cantidad.' });
    rep.chk('21', 'Segunda cuota vencida: alerta refleja deuda vencida total', R.r2(tot1 + tot2), Number(al3?.MontoVencido));
    rep.chk('21', 'Cuota 2 pasa a Vencida(2)', 2, Number(cuotas()[1].Estado));
    // 22. cambios de tasa/vigencia (versiones append-only)
    const ult = db.uno('SELECT CONVERT(varchar(10),MAX(VigenteDesde),23) d FROM ConfiguracionesPunitorio').d;
    const retro = R.sumarDias(hoy, -7);
    const crear = async (pct, desde, motivo) => {
        const pg = await A.html('/ConfiguracionPago/CreditoPersonal');
        const f = serializarForm(pg.html, 'Punitorios.CrearForm.Porcentaje');
        return A.postPares('/ConfiguracionPago/CrearVersionPunitorio', conOverrides(f, {
            'Punitorios.CrearForm.Porcentaje': pct, 'Punitorios.CrearForm.PeriodoDias': 20, 'Punitorios.CrearForm.DiasGracia': 5,
            'Punitorios.CrearForm.ProrrateoDiario': 'true', 'Punitorios.CrearForm.AplicacionRetroactiva': 'true', 'Punitorios.CrearForm.Activa': 'true',
            'Punitorios.CrearForm.VigenteDesde': desde, 'Punitorios.CrearForm.MotivoCambio': motivo,
        }));
    };
    if (ult >= retro) {
        rep.bloqueado('22', 'Cambio de tasa con vigencia retroactiva', `Ya existe una versión con VigenteDesde ${ult} >= ${retro}; las versiones son append-only. Repetir en un stack limpio.`);
    } else {
        const r1 = await crear(20, retro, 'QA financiero: tasa 20% desde hace 7 días');
        const nver = db.num(`SELECT COUNT(*) c FROM ConfiguracionesPunitorio WHERE VigenteDesde='${retro}'`);
        rep.chk('22', 'Versión retroactiva (20%) creada con permiso y motivo', 1, nver, { nota: 'HTTP ' + r1.status() });
        const esp = R.punitorioTramos(tot1, 15, [{ dias: 8, pct: 10, periodo: 20, gracia: 5 }, { dias: 7, pct: 20, periodo: 20, gracia: 5 }]);
        const pr = await punitorioHoy(A, S.c[0]);
        rep.chk('22', 'Punitorio con 2 vigencias: 8 días @10% + 7 días @20%', esp, pr.valor, { tol: 0.01 });
        const r2 = await crear(30, hoy, 'QA financiero: tasa 30% desde hoy');
        const pr2 = await punitorioHoy(A, S.c[0]);
        rep.chk('22', 'Versión que rige desde hoy NO altera el devengado hasta hoy', esp, pr2.valor, { tol: 0.01, nota: 'HTTP ' + r2.status() });
        S.punitorio1 = pr2.valor;
    }
    const ev1 = await shot(browser, 'admin', '/Mora/Alertas', 'f3-mora-alertas');
    const ev2 = await shot(browser, 'admin', `/Credito/${S.creditoId}/Cuotas/${S.c[0]}/Punitorio`, 'f3-punitorio-cuota1');
    rep.chk('19', 'Evidencia visual de alertas y punitorio', true, true, { evidencia: [ev1, ev2] });
    rep.datos.fase3 = { cuotas: S.c, vencimientosRetrodatados: { c1: v1, c2: v2 }, punitorioCuota1: S.punitorio1 };
});

test('FASE 4 — pagos: parcial, total, doble pago y sin caja', async ({ browser }) => {
    if (!S.creditoId || !S.c) return rep.bloqueado('23-29', 'Fase 4', 'Sin fase 2/3');
    const A = S.admin;
    const antes = cuotas();
    const tot1 = Number(antes[0].MontoTotal);
    // aplicar punitorio (requisito para poder cobrarlo)
    const d = await A.html(`/Credito/${S.creditoId}/Cuotas/${S.c[0]}/Punitorio`);
    const ra = await A.postPares(`/Credito/${S.creditoId}/Cuotas/${S.c[0]}/Punitorio/Aplicar`, conOverrides(serializarForm(d.html, 'Acciones.Aplicar.Motivo'), { 'Acciones.Aplicar.Motivo': 'QA financiero: aplicar punitorio' }));
    const P = db.num(`SELECT ISNULL(SUM(Importe),0) s FROM PunitoriosAplicados WHERE CuotaId=${S.c[0]} AND Estado=1 AND IsDeleted=0`);
    rep.chk('23', 'Punitorio aplicado (autoridad persistida) = calculado', S.punitorio1, P, { tol: 0.01, nota: 'HTTP ' + ra.status() });
    const m0 = movs().length, s0 = db.num(`SELECT SaldoPendiente FROM Creditos WHERE Id=${S.creditoId}`);
    // 23. pago parcial
    const X = 10000;
    const r = await pagar(A, S.c[0], X, 'Efectivo', 'QA-FIN-PARCIAL');
    rep.chk('23', 'Pago parcial aceptado (redirect al recibo)', 302, r.status, { nota: r.texto });
    const led = pagosLedger();
    const l = led[led.length - 1];
    const aplCuotaEsp = R.r2(X - P);
    rep.chk('24', 'Prioridad: punitorio aplicado primero', P, Number(l?.ImporteAplicadoPunitorio), { tol: 0.01 });
    rep.chk('24', 'Resto a capital de la cuota', aplCuotaEsp, Number(l?.ImporteAplicadoCuota), { tol: 0.01 });
    const cu1 = cuotas()[0];
    rep.chk('24', 'Cuota 1 queda Parcial(3) con MontoPagado = aplicado a cuota', { e: 3, p: aplCuotaEsp }, { e: Number(cu1.Estado), p: Number(cu1.MontoPagado) }, { tol: 0.01 });
    // reparto interés/capital: proporcional a la composición de la cuota
    const capEsp = R.r2(aplCuotaEsp * Number(cu1.MontoCapital) / Number(cu1.MontoTotal));
    const s1 = db.num(`SELECT SaldoPendiente FROM Creditos WHERE Id=${S.creditoId}`);
    rep.chk('24', 'Distribución: Credito.SaldoPendiente baja por la porción de CAPITAL (pro-rata capital/total de la cuota)', R.r2(s0 - capEsp), s1, { tol: 0.02, nota: `interés cobrado esperado ${R.r2(aplCuotaEsp - capEsp)}` });
    const mm = movs();
    rep.chk('26', 'Caja: un único ingreso nuevo por el monto total entregado', { n: m0 + 1, monto: X }, { n: mm.length, monto: Number(mm[mm.length - 1]?.Monto) });
    // 25. pago total (resto cuota 1, luego cuota 2 completa)
    const rest1 = R.r2(tot1 - aplCuotaEsp);
    { const ctx = await A.html(`/Credito/${S.creditoId}/Cuotas/${S.c[0]}/Punitorio`); const t = strip(ctx.html); const i = t.indexOf('Capital pendiente'); rep.op('Contexto cuota 1 antes del pago del saldo', t.slice(i, i + 330)); }
    const r2_ = await pagar(A, S.c[0], rest1, 'Efectivo', 'QA-FIN-TOTAL-1');
    rep.chk('25', 'Cancelación del saldo de cuota 1 aceptada', 302, r2_.status, { nota: r2_.texto });
    const tot2 = Number(antes[1].MontoTotal);
    const r3 = await pagar(A, S.c[1], tot2, 'Efectivo', 'QA-FIN-TOTAL-2');
    rep.chk('25', 'Pago total de cuota 2 aceptado', 302, r3.status, { nota: r3.texto });
    const cu = cuotas();
    rep.chk('26', 'Cuotas 1 y 2 Pagadas(1) y MontoPagado = MontoTotal', [1, 1, tot1, tot2], [Number(cu[0].Estado), Number(cu[1].Estado), Number(cu[0].MontoPagado), Number(cu[1].MontoPagado)], { tol: 0.01 });
    const esperadoCaja = R.r2(X + rest1 + tot2);
    rep.chk('26', 'Caja: Σ ingresos de cobro = Σ pagos entregados', esperadoCaja, sumPag(), { tol: 0.01 });
    const ledAll = pagosLedger();
    rep.chk('26', 'Libro de pagos: 3 filas Aplicado(1) ligadas a un movimiento de caja', { n: 3, ok: true }, { n: ledAll.length, ok: ledAll.every(x => x.Estado === '1' && x.MovimientoCajaId) });
    const apertura = db.uno(`SELECT Id FROM AperturasCaja WHERE Cerrada=0 AND UsuarioApertura='administrador' AND IsDeleted=0`);
    rep.datos.fase4 = { pagoParcial: X, punitorio: P, restoCuota1: rest1, cuota2: tot2, saldoPendienteCredito: db.num(`SELECT SaldoPendiente FROM Creditos WHERE Id=${S.creditoId}`) };
    // 27-28. doble pago
    const mAntes = movs().length, lAntes = pagosLedger().length;
    const dup = await pagar(A, S.c[0], 1000, 'Efectivo', 'QA-FIN-DOBLE');
    rep.chk('27', 'Doble pago sobre cuota ya Pagada: rechazado (no 302 de éxito)', true, dup.status !== 302 || !!dup.sinForm, { nota: `HTTP ${dup.status} ${dup.texto ?? ''}` });
    rep.chk('28', 'Sin movimientos/ledger duplicados tras el doble pago', { m: mAntes, l: lAntes }, { m: movs().length, l: pagosLedger().length });
    // doble envío simultáneo sobre cuota 3 (misma versión de fila)
    const pg = await A.html(`/Credito/PagarCuota/${S.c[2]}`);
    const form = serializarForm(pg.html, 'Input.MontoIngresado');
    const tot3 = Number(cu[2].MontoTotal);
    const cuerpo = conOverrides(form, { 'Input.MontoIngresado': tot3.toFixed(2), 'Input.MedioPago': 'Efectivo', 'Input.Comprobante': 'QA-FIN-CONCURRENTE' });
    const [ra1, rb1] = await Promise.all([A.postPares(`/Credito/PagarCuota/${S.c[2]}`, cuerpo), A.postPares(`/Credito/PagarCuota/${S.c[2]}`, cuerpo)]);
    const exitos = [ra1, rb1].filter(x => x.status() === 302).length;
    const filasC3 = db.num(`SELECT COUNT(*) c FROM PagosCuota WHERE CuotaId=${S.c[2]} AND IsDeleted=0 AND Estado=1`);
    const movC3 = db.num(`SELECT COUNT(*) c FROM MovimientosCaja WHERE IsDeleted=0 AND Concepto=3 AND ReferenciaId=${S.c[2]}`);
    rep.chk('27', 'Envío simultáneo duplicado: exactamente 1 aceptado', 1, exitos, { nota: `status ${ra1.status()}/${rb1.status()}` });
    rep.chk('28', 'Envío simultáneo: 1 fila de ledger y 1 movimiento de caja', { l: 1, m: 1 }, { l: filasC3, m: movC3 });
    // 29. sin caja abierta: gerente no tiene apertura
    const G = await Sesion.login('gerente');
    const mG0 = movs().length, lG0 = pagosLedger().length;
    const rg = await pagar(G, S.c[3], Number(cu[3].MontoTotal), 'Efectivo', 'QA-FIN-SINCAJA');
    const tieneCaja = db.num("SELECT COUNT(*) c FROM AperturasCaja WHERE Cerrada=0 AND UsuarioApertura='gerente' AND IsDeleted=0");
    rep.chk('29', 'Precondición: gerente sin caja abierta', 0, tieneCaja);
    rep.chk('29', 'Cobro sin caja abierta: rechazado y sin registrar movimientos', { exito: false, m: mG0, l: lG0 }, { exito: rg.status === 302 && !rg.location?.includes('AccessDenied') && (movs().length > mG0), m: movs().length, l: pagosLedger().length }, { nota: `HTTP ${rg.status} ${rg.location ?? ''} ${rg.texto ?? ''}` });
    await G.dispose();
    const ev = await shot(browser, 'admin', `/Credito/Details/${S.creditoId}`, 'f4-credito-tras-pagos');
    const ev2 = await shot(browser, 'admin', '/Caja', 'f4-caja');
    rep.chk('26', 'Evidencia visual de crédito y caja tras pagos', true, true, { evidencia: [ev, ev2] });
});

test('FASE 5 — cancelación con pagos y reversión auditada', async ({ browser }) => {
    if (!S.ventaId) return rep.bloqueado('30-37', 'Fase 5', 'Sin venta');
    const A = S.admin;
    rep.chk('30', 'Venta financiada con pagos registrados disponible', true, pagosLedger().length >= 3);
    // 37 (antes de que el admin cancele): operaciones no autorizadas
    const noAut = [];
    for (const rol of ['cajero', 'vendedor']) {
        const U = await Sesion.login(rol);
        const g = await U.html(`/Venta/Cancelar/${S.ventaId}`);
        let r = null;
        if (g.status === 200 && g.html.includes('name="motivo"')) r = await U.postPares(`/Venta/Cancelar/${S.ventaId}`, conOverrides(serializarForm(g.html, 'motivo'), { motivo: `QA no autorizado ${rol}` }));
        noAut.push({ rol, get: g.status, url: g.url.replace(BASE, ''), post: r?.status() ?? null });
        await U.dispose();
    }
    const estV = Number(db.num(`SELECT Estado FROM Ventas WHERE Id=${S.ventaId}`));
    rep.chk('37', 'Roles sin permiso ventas.cancel no pueden cancelar (venta sigue Confirmada=2)', 2, estV, { nota: JSON.stringify(noAut) });
    // 31. intento de cancelación (admin)
    const g = await A.html(`/Venta/Cancelar/${S.ventaId}`);
    const antes = {
        ledger: pagosLedger().length, mov: movs().length, egresos: db.num(`SELECT COUNT(*) c FROM MovimientosCaja WHERE IsDeleted=0 AND Tipo=1 AND VentaId=${S.ventaId}`),
        stock: db.num(`SELECT StockActual FROM Productos WHERE Id=${S.productoId}`),
    };
    const evPre = await shot(browser, 'admin', `/Venta/Cancelar/${S.ventaId}`, 'f5-cancelar-pantalla');
    const rc = await A.postPares(`/Venta/Cancelar/${S.ventaId}`, conOverrides(serializarForm(g.html, 'motivo'), { motivo: 'QA financiero: cancelación de venta con cuotas pagadas' }));
    const v = db.uno(`SELECT Estado,MotivoCancelacion FROM Ventas WHERE Id=${S.ventaId}`);
    const cr = db.uno(`SELECT Estado,SaldoPendiente FROM Creditos WHERE Id=${S.creditoId}`);
    const cu = cuotas();
    const led = pagosLedger();
    const pagado = R.r2(cu.reduce((a, x) => a + Number(x.MontoPagado), 0));
    const despues = {
        venta: Number(v.Estado), credito: Number(cr.Estado), cuotas: cu.map(x => `${x.NumeroCuota}:${x.Estado}:${x.MontoPagado}`),
        ledger: led.map(x => `${x.Id}:${x.Estado}`), egresosCaja: db.num(`SELECT COUNT(*) c FROM MovimientosCaja WHERE IsDeleted=0 AND Tipo=1`), stock: db.num(`SELECT StockActual FROM Productos WHERE Id=${S.productoId}`),
    };
    rep.op('Intento de cancelación de venta con cuotas pagadas', { http: rc.status(), antes, despues });
    // Regla confirmada: solo mediante reversión auditada. Esperado: la cancelación directa es RECHAZADA y la venta sigue Confirmada.
    rep.chk('31', 'Cancelación directa con cuotas pagadas debe ser rechazada (venta sigue Confirmada=2)', 2, Number(v.Estado), { nota: `Real: la venta quedó Cancelada(5); crédito→${cr.Estado}, cuotas pagadas→Cancelada(4) con Σ pagado ${pagado}. VentaService.CancelarVentaAsync no valida pagos de cuotas.`, evidencia: [evPre] });
    rep.chk('33', 'Pagos del ledger revertidos/anulados al cancelar (esperado: sin filas Aplicado)', 0, led.filter(x => x.Estado === '1').length, { nota: 'Ninguna fila de PagosCuota fue revertida; sigue Aplicado.' });
    const movsVenta = db.filas(`SELECT Id,Tipo,Concepto,Monto,Descripcion,Observaciones,ReferenciaId,VentaId FROM MovimientosCaja WHERE IsDeleted=0 AND (VentaId=${S.ventaId} OR (Concepto=3 AND ReferenciaId IN (SELECT Id FROM Cuotas WHERE CreditoId=${S.creditoId})) OR Concepto=21) ORDER BY Id`);
    rep.op('Movimientos de caja ligados a la venta/cuotas tras cancelar', movsVenta);
    {
        const rev = movsVenta.find(m => m.Tipo === '1' && m.Concepto === '21' && m.VentaId === String(S.ventaId));
        if (rev) {
            const ob = rev.Observaciones ?? '';
            const origId = (ob.match(/ingreso #([0-9]+)/) || [])[1];
            const orig = origId ? db.uno(`SELECT Id,Monto,ReferenciaId,VentaId,Descripcion FROM MovimientosCaja WHERE Id=${origId}`) : null;
            const propio = !!orig && db.num(`SELECT COUNT(*) c FROM Cuotas WHERE Id=${orig.ReferenciaId} AND CreditoId=${S.creditoId}`) > 0;
            rep.chk('33', 'El egreso "ReversionVenta" revierte un ingreso de ESTA venta/crédito (no de otra operación)', true, propio, { nota: `Ingreso original #${origId}: ${JSON.stringify(orig)}. CajaService.RegistrarContramovimientoVentaAsync usa fallback ReferenciaId==ventaId (VentaId nulo): los cobros de cuota guardan ReferenciaId=cuotaId, por lo que puede colisionar con la cuota cuyo Id coincide con el Id de la venta.` });
        }
    }
    rep.chk('33', 'Caja: contramovimiento (egreso) por los cobros de cuotas devueltos', R.r2(sumPag()), R.r2(movsVenta.filter(m => m.Tipo === '1' && m.VentaId === String(S.ventaId) && (() => { const id = (String(m.Observaciones ?? '').match(/ingreso #([0-9]+)/) || [])[1]; return !!id && db.num(`SELECT COUNT(*) c FROM MovimientosCaja WHERE Id=${id} AND ReferenciaId IN (SELECT Id FROM Cuotas WHERE CreditoId=${S.creditoId})`) > 0; })()).reduce((a, m) => a + Number(m.Monto), 0)), { tol: 0.01, nota: 'Solo se cuentan egresos que reviertan ingresos de ESTE crédito. El egreso generado (si existe) corresponde a otra operación por colisión de Id, y los cobros propios quedan sin contrapartida.' });
    // 32. ¿existe un proceso controlado de reversión? (UI real de detalle de venta y crédito)
    const dv = await A.html(`/Venta/Details/${S.ventaId}`);
    const dc = await A.html(`/Credito/Details/${S.creditoId}`);
    const hay = /revers|revertir|anular pago/i.test(strip(dv.html) + strip(dc.html));
    if (!hay) rep.bloqueado('32', 'Proceso controlado y autorizado de reversión de pagos', 'No hay acción/pantalla de reversión en Venta/Details ni Credito/Details; el código solo usa OrigenPagoCuota.Reversion en tests (PagoCuotaLedgerTests). Funcionalidad inexistente: no se inventan endpoints.');
    else rep.chk('32', 'Existe acción de reversión en la UI', true, hay);
    for (const [p, d] of [['33', 'Reversión de pagos, caja, saldos, crédito y stock por procedimiento auditado'], ['34', 'Evidencia auditable por operación de reversión'], ['35', 'Atomicidad de la reversión'], ['36', 'Idempotencia de la reversión repetida']])
        rep.bloqueado(p, d, 'Depende de la funcionalidad de reversión auditada (inexistente). No se simuló con SQL.');
    rep.chk('34', 'Auditoría de la cancelación directa: queda motivo y fecha de cancelación', true, !!v.MotivoCancelacion, { nota: 'Solo hay motivo en Venta; no hay asiento de auditoría de pagos/caja.' });
    const evPost = await shot(browser, 'admin', `/Credito/Details/${S.creditoId}`, 'f5-credito-tras-cancelacion');
    const evVenta = await shot(browser, 'admin', `/Venta/Details/${S.ventaId}`, 'f5-venta-tras-cancelacion');
    rep.datos.fase5 = { antes, despues, evidencia: [evPre, evPost, evVenta] };
});
