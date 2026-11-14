// D-05 (2026-11-14): recorridos COMPLETOS DE ESCRITURA bajo CSP (ReportOnly y Enforce) que los barridos de solo lectura no ejecutan:
//   1. alta de plantilla de contrato (exige configuracion.update: solo SuperAdmin -> usuario "admin"),
//   2. facturación (modal de Venta/Details y página Venta/Facturar),
//   3. preparación y generación del contrato de venta a crédito (+ confirmar la operación),
//   4. devoluciones: alta desde el modal de la venta, aprobar, completar, rechazar y cancelar,
//   5. autorizaciones: los mismos endpoints con roles sin permiso no escriben nada.
// Cada escenario verifica funcionalidad, ausencia de errores de JS/violaciones CSP (lo hace el arnés) y la persistencia visible en la propia
// aplicación (la verificación contra la base se hace con sqlcmd sobre el stack descartable, ver docs/csp-enforce-pendientes.md).
// Los datos de apoyo (caja abierta, cliente, producto con stock, plan de cuotas, ventas) se crean por HTTP con la misma sesión del navegador
// (formularios reales con token antiforgery); lo que se prueba en el navegador es lo que el CSP puede romper: modales, JS de página, fetch.
// Corre al final (prefijo zz-) y solo en un stack QA aislado (assertEntorno): crea filas sintéticas.
'use strict';
const { serializarForm, conOverrides, extraerToken } = require('../../../../e2e-tx/financiero/lib/api');

const RUN = String(Date.now()).slice(-6);
const S = {}; // estado compartido entre escenarios (ids creados por la preparación)
const PRECIO = 100001;
const hoy = () => new Date().toISOString().slice(0, 10);
const r2 = (n) => Math.round(n * 100) / 100;

const api = (E) => ({
  async html(ruta) { const r = await E.ctx.request.get(E.opts.base + ruta); return { status: r.status(), html: await r.text() }; },
  async postForm(urlForm, urlPost, campos, opts = {}) {
    const g = await this.html(urlForm);
    return E.ctx.request.post(E.opts.base + urlPost, {
      form: { ...Object.fromEntries(Object.entries(campos).map(([k, v]) => [k, String(v)])), __RequestVerificationToken: extraerToken(g.html) || '' },
      maxRedirects: 0, headers: opts.headers,
    });
  },
  async postPares(urlPost, pares, opts = {}) {
    const body = new URLSearchParams();
    for (const [k, v] of pares) body.append(k, v);
    return E.ctx.request.post(E.opts.base + urlPost, { data: body.toString(), headers: { 'content-type': 'application/x-www-form-urlencoded', ...(opts.headers || {}) }, maxRedirects: 0 });
  },
});

const exigeSetup = (E) => { if (!E.check('la preparación D-05 dejó caja, cliente, producto y plan (corrida con --solo D-05 completo)', S.listo, S.error || 'sin preparación')) throw new Error('sin preparación D-05'); return api(E); };

// Venta de contado confirmada (y opcionalmente facturada) por HTTP: datos de apoyo para los escenarios de navegador.
async function crearVenta(A, { tipoPago = '0', qty = 1, precio = PRECIO } = {}) {
  const total = r2(precio * qty), sub = r2(total / 1.21), iva = r2(total - sub);
  const rv = await A.postForm('/Venta/Create', '/Venta/CreateAjax', {
    IdempotencyKey: require('crypto').randomUUID(), ClienteId: S.clienteId, FechaVenta: hoy(), TipoPago: tipoPago, Estado: '0', Subtotal: sub, IVA: iva, Total: total, Descuento: '0',
    'Detalles[0].ProductoId': S.productoId, 'Detalles[0].Cantidad': qty, 'Detalles[0].PrecioUnitario': precio, 'Detalles[0].Descuento': '0', 'Detalles[0].Subtotal': total, 'Detalles[0].PorcentajeIVA': '21',
  });
  const j = await rv.json().catch(() => ({}));
  if (!j.success) throw new Error('Error D-05: no se pudo crear la venta de apoyo: ' + JSON.stringify(j).slice(0, 200));
  j.ventaId = j.ventaId || Number(((j.redirectUrl || '').match(/Details\/(\d+)/) || [])[1] || 0);
  return j;
}
const confirmarVenta = async (A, id) => (await A.postForm(`/Venta/Details/${id}`, `/Venta/Confirmar/${id}`, { id })).status();
async function facturarPorHttp(A, id) {
  const g = await A.html(`/Venta/Facturar/${id}`);
  const r = await A.postPares('/Venta/Facturar', conOverrides(serializarForm(g.html, 'VentaId'), {}));
  return r.status();
}
// Rechazo por permisos: 403, o redirección a AccessDenied/Login (según el manejador de la app).
const denegado = (r) => r.status() === 403 || (r.status() === 302 && /AccessDenied|Login/i.test(r.headers()['location'] || ''));
// Clic que dispara un POST con redirección: la navegación se espera en el mismo Promise.all (en WebKit waitForLoadState resolvía antes de que empezara).
const enviar = async (E, page, sel) => { await Promise.all([page.waitForNavigation({ waitUntil: 'load', timeout: 30000 }), E.click(page, sel)]); await page.waitForLoadState('networkidle'); };
const cuerpo = (page) => page.evaluate(() => document.body.innerText);

async function crearDevolucionPorModal(E, page, ventaId, desc) {
  await E.ir(page, `/Venta/Details/${ventaId}`);
  await E.click(page, '[data-open-devolucion-modal]');
  const cargo = await E.visible(page, '#devolucion-modal-body', 8000);
  E.check(`venta ${ventaId}: el modal de devolución carga el contexto por AJAX (GetQuickCreateContext)`, cargo);
  if (!cargo) return null;
  await page.selectOption('#devolucion-motivo', { index: 1 });
  await page.selectOption('#devolucion-tipo-resolucion', { index: 1 });
  const fila = page.locator('#devolucion-items-body tr').first();
  await fila.locator('.devolucion-item-check').check(); E.interacciones++;
  await fila.locator('.devolucion-item-cantidad').fill('1');
  await page.fill('#devolucion-descripcion', desc);
  const [resp] = await Promise.all([
    page.waitForResponse((r) => /\/Devolucion\/QuickCreate/.test(r.url()) && r.request().method() === 'POST', { timeout: 15000 }),
    E.click(page, '#btn-submit-devolucion'),
  ]);
  const json = await resp.json().catch(() => ({}));
  E.check(`venta ${ventaId}: QuickCreate responde success`, json.success === true, JSON.stringify(json).slice(0, 160));
  const ocultoModal = await E.oculto(page, '#modal-devolucion-venta', 6000);
  E.check(`venta ${ventaId}: el modal se cierra tras crear`, ocultoModal);
  await E.ir(page, '/Devolucion');
  const hrefs = await page.evaluate(() => [...document.querySelectorAll('a[href*="/Devolucion/Detalles/"]')].map((a) => a.getAttribute('href')));
  const id = Math.max(0, ...hrefs.map((h) => Number((h.match(/Detalles\/(\d+)/) || [])[1] || 0)));
  E.check(`venta ${ventaId}: la devolución aparece en el listado`, id > 0, hrefs.length);
  return id || null;
}

module.exports = [
  {
    nombre: 'D-05 0: preparación (caja abierta, cliente, producto con stock, plan de cuotas, aptitud) por HTTP',
    usuario: 'admin', escribe: true, requiereInteraccion: false,
    async fn(E, page) {
      const A = api(E);
      try {
        const cc = await A.postForm('/Caja/Create', '/Caja/Create', { Codigo: 'D05-' + RUN, Nombre: 'Caja D05 ' + RUN, Sucursal: 'QA', Ubicacion: 'QA', Descripcion: 'caja de prueba D-05', Activa: 'true' });
        E.check('alta de caja', [200, 302].includes(cc.status()), cc.status());
        const ab = await A.html('/Caja/Abrir');
        const m = ab.html.match(new RegExp(`<option[^>]*value="(\\d+)"[^>]*>[^<]*Caja D05 ${RUN}`));
        S.cajaId = m ? m[1] : null;
        E.check('la caja nueva figura en el selector de Caja/Abrir', !!S.cajaId);
        const ra = await A.postForm('/Caja/Abrir', '/Caja/Abrir', { CajaId: S.cajaId, MontoInicial: '1000', ObservacionesApertura: 'D-05' });
        const yaAbierta = ra.status() === 200 && /ya tiene una apertura|ya tiene.*abiert|abierta/i.test(await ra.text());
        E.check('apertura de caja (302; en una reejecución sobre el mismo stack el SuperAdmin ya la tiene abierta)', ra.status() === 302 || yaAbierta, ra.status());

        const dni = '3' + RUN + '7';
        const rc = await A.postForm('/Cliente/ModalCreate', '/Cliente/CreateAjax', {
          TipoDocumento: 'DNI', NumeroDocumento: dni, Apellido: 'Qadcinco', Nombre: 'Escritura', FechaNacimiento: '1985-03-10', Domicilio: 'Calle Sintetica 123', Localidad: 'CABA',
          Provincia: 'Buenos Aires', CodigoPostal: '1000', Telefono: '1155550000', Email: `qa.d05.${RUN}@example.invalid`, TipoEmpleo: 'Relación de dependencia', Empleador: 'Empresa QA',
          Sueldo: '1500000', TieneReciboSueldo: 'true', Activo: 'true', EstadoCivil: 'Soltero/a',
        });
        const jc = await rc.json().catch(() => ({})); S.clienteId = jc.id;
        E.check('alta de cliente sintético', jc.success === true && !!S.clienteId, JSON.stringify(jc).slice(0, 140));

        const form = await A.html('/Catalogo');
        const primeraOpcion = (n) => { const sel = form.html.match(new RegExp(`<select[^>]*name="${n}"[^>]*>([\\s\\S]*?)</select>`)); const o = sel && sel[1].match(/<option[^>]*value="(\d+)"/); return o ? o[1] : '1'; };
        const cat = primeraOpcion('CategoriaId'), mar = primeraOpcion('MarcaId');
        const rp = await A.postForm('/Catalogo', '/Producto/CreateAjax', {
          Codigo: 'D05-' + RUN, Nombre: 'Producto QA D05 ' + RUN, Descripcion: 'sintetico', CategoriaId: cat, MarcaId: mar, PrecioCompra: '50000', PrecioVenta: String(PRECIO), PorcentajeIVA: '21',
          StockActual: '0', StockMinimo: '0', Activo: 'true', 'CreditoPersonal.AdmiteCreditoPersonal': 'true', 'CreditoPersonal.Modo': '0', 'CreditoPersonal.MaxCuotasCredito': '12',
        });
        const jp = await rp.json().catch(() => ({})); S.productoId = jp.entity && jp.entity.id;
        E.check('alta de producto sintético', jp.success === true && !!S.productoId, JSON.stringify(jp).slice(0, 140));
        await A.postForm('/MovimientoStock/Create', '/MovimientoStock/Create', { ProductoId: S.productoId, Tipo: 'Entrada', Cantidad: '30', Motivo: 'Stock D-05', Referencia: 'D05-' + RUN });

        await A.postForm(`/Cliente/Details/${S.clienteId}`, '/Cliente/AsignarLimiteCredito', { clienteId: S.clienteId, limiteCredito: '2000000', motivo: 'D-05: limite sintetico' });
        await A.postForm(`/Cliente/Details/${S.clienteId}`, '/Cliente/RecalcularAptitud', { clienteId: S.clienteId });

        const g = await A.html('/ConfiguracionPago/CreditoPersonal');
        const p = conOverrides(serializarForm(g.html, 'DefaultsGlobales.TasaMensual'), { 'DefaultsGlobales.TasaMensual': 12, nuevaCuotaCantidad: 6, nuevaCuotaTasaMensual: 12, nuevaCuotaActivo: 'true' });
        const rpl = await A.postPares('/ConfiguracionPago/CreditoPersonal', p);
        E.check('plan de 6 cuotas configurado', [200, 302].includes(rpl.status()), rpl.status());
        S.listo = !!(S.cajaId && S.clienteId && S.productoId);
        E.check('preparación completa', S.listo, JSON.stringify({ caja: S.cajaId, cli: S.clienteId, prod: S.productoId }));
      } catch (e) { S.error = String(e.message || e).slice(0, 300); E.check('preparación sin excepción', false, S.error); }
    },
  },

  {
    nombre: 'D-05 1: alta de plantilla de contrato con SuperAdmin (configuracion.update)',
    usuario: 'admin', escribe: true,
    async fn(E, page) {
      const nombre = `Plantilla D05 ${RUN}`;
      await E.ir(page, '/PlantillaContratoCredito');
      E.check('listado de plantillas accesible para SuperAdmin', /PlantillaContratoCredito/i.test(page.url()));
      const r = await E.ir(page, '/PlantillaContratoCredito/Create');
      E.check('Create responde 200 (no AccessDenied)', r.status() === 200 && /PlantillaContratoCredito\/Create/i.test(page.url()), `${r.status()} ${page.url()}`);
      await page.fill('input[name="Nombre"]', nombre); E.interacciones++;
      for (const t of ['vendedor', 'contrato', 'pagare']) { await E.click(page, `.pl-tab[data-tab="${t}"]`); }
      await E.click(page, '.pl-tab[data-tab="vendedor"]');
      await page.fill('input[name="NombreVendedor"]', 'Bury Hogar QA S.R.L.');
      await page.fill('input[name="DomicilioVendedor"]', 'Av. Sintetica 1234, CABA');
      await page.fill('input[name="CiudadFirma"]', 'CABA');
      await page.fill('input[name="Jurisdiccion"]', 'Tribunales de CABA');
      await page.fill('input[name="InteresMoraDiarioPorcentaje"]', '0.5');
      await E.click(page, '.pl-tab[data-tab="contrato"]');
      await page.fill('textarea[name="TextoContrato"]', 'Contrato D-05 entre {{COMPRADOR_NOMBRE}} y el vendedor. Productos: {{PRODUCTOS_DETALLE}}. Saldo: {{SALDO_FINANCIADO}}. Plan: {{PLAN_CUOTAS}}.');
      await E.click(page, '.pl-ins-btn[data-target="TextoContrato"]');
      const insertado = (await page.inputValue('textarea[name="TextoContrato"]')).includes('{{COMPRADOR_NOMBRE}}');
      E.check('el botón de inserción de variable (data-insert) escribe en el editor', insertado);
      await E.click(page, '.pl-tab[data-tab="pagare"]');
      await page.fill('textarea[name="TextoPagare"]', 'Pagaré D-05 N° {{NUMERO_PAGARE}} por {{SALDO_FINANCIADO}}. {{COMPRADOR_NOMBRE}}, {{FECHA_OPERACION}}.');
      await E.click(page, '.pl-paper-tab[data-ptab]');
      await Promise.all([page.waitForURL((u) => /\/PlantillaContratoCredito\/?(\?.*)?$/i.test(u.pathname + u.search), { timeout: 20000 }), E.click(page, 'button[type="submit"].btn-erp-primary')]);
      const txt = await cuerpo(page);
      E.check('tras guardar vuelve al listado con la plantilla creada', txt.includes(nombre), page.url());
      E.check('aviso de éxito visible', /creada exitosamente/i.test(txt));
      const rEdit = await E.ir(page, '/PlantillaContratoCredito');
      const href = await page.locator(`tr:has-text("${nombre}") a[href*="Edit"], a[href*="Edit"]:near(:text("${nombre}"))`).first().getAttribute('href').catch(() => null);
      E.check('la plantilla persistida tiene enlace de edición', !!href, rEdit.status());
      if (href) {
        const re = await E.ir(page, href);
        E.check('Edit abre con los datos persistidos', re.status() === 200 && (await page.inputValue('input[name="Nombre"]')) === nombre);
      }
    },
  },

  {
    nombre: 'D-05 2: facturación desde el modal de Venta/Details (confirmada -> factura)',
    usuario: 'admin', escribe: true,
    async fn(E, page) {
      const A = exigeSetup(E);
      const v = await crearVenta(A); S.ventaModal = v.ventaId;
      E.check('venta de apoyo confirmada', (await confirmarVenta(A, v.ventaId)) === 302);
      await E.ir(page, `/Venta/Details/${v.ventaId}`);
      E.check('Details expone la acción Facturar', await E.visible(page, '[data-venta-modal-target="facturar-modal"]'));
      await E.click(page, '[data-venta-modal-target="facturar-modal"]');
      E.check('el modal Emitir factura se abre (data-venta-modal-action=open)', await E.visible(page, '#facturar-modal #form-facturar'));
      const tipos = await page.locator('#form-facturar select[name="Tipo"] option').count();
      E.check('el selector de tipo de factura tiene opciones', tipos > 0, tipos);
      await E.click(page, '#facturar-modal [data-venta-modal-action="close"]');
      E.check('el modal se cierra con la X', await E.oculto(page, '#facturar-modal'));
      await E.click(page, '[data-venta-modal-target="facturar-modal"]');
      await E.visible(page, '#form-facturar button[type="submit"]');
      await enviar(E, page, '#form-facturar button[type="submit"]');
      const txt = await cuerpo(page);
      E.check('vuelve a Venta/Details de la misma venta', new RegExp(`/Venta/Details/${v.ventaId}`).test(page.url()), page.url());
      E.check('aviso "Factura generada exitosamente"', /Factura generada exitosamente/i.test(txt));
      E.check('la venta ya no ofrece Facturar (estado persistido)', (await page.locator('[data-venta-modal-target="facturar-modal"]').count()) === 0);
      E.check('Details muestra el estado Facturada', /Facturada/i.test(txt));
    },
  },

  {
    nombre: 'D-05 3: facturación desde la página Venta/Facturar (confirmada -> factura)',
    usuario: 'admin', escribe: true,
    async fn(E, page) {
      const A = exigeSetup(E);
      const v = await crearVenta(A); S.ventaPagina = v.ventaId;
      E.check('venta de apoyo confirmada', (await confirmarVenta(A, v.ventaId)) === 302);
      const r = await E.ir(page, `/Venta/Facturar/${v.ventaId}`);
      E.check('Venta/Facturar carga el formulario', r.status() === 200 && (await E.visible(page, 'form[data-facturar-form]')), page.url());
      await page.selectOption('form[data-facturar-form] select[name="Tipo"]', { index: 0 }); E.interacciones++;
      await Promise.all([page.waitForURL(new RegExp(`/Venta/Details/${v.ventaId}`), { timeout: 20000 }), E.click(page, 'form[data-facturar-form] button[type="submit"]')]);
      await page.waitForLoadState('networkidle');
      const txt = await cuerpo(page);
      E.check('aviso "Factura generada exitosamente"', /Factura generada exitosamente/i.test(txt));
      E.check('Details muestra el estado Facturada', /Facturada/i.test(txt));
      const again = await E.ir(page, `/Venta/Facturar/${v.ventaId}`);
      E.check('una venta ya facturada no vuelve a abrir el formulario', !(await page.locator('form[data-facturar-form]').count()), again.status());
    },
  },

  {
    nombre: 'D-05 4: contrato de venta a crédito: Preparar, Generar (fetch + PDF) y Confirmar operación',
    usuario: 'admin', escribe: true,
    async fn(E, page) {
      const A = exigeSetup(E);
      const v = await crearVenta(A, { tipoPago: '5' });
      S.ventaCredito = v.ventaId;
      E.check('venta financiada creada con crédito', !!v.creditoId, JSON.stringify(v).slice(0, 120));
      const aut = await A.html(`/Venta/Autorizar/${v.ventaId}`);
      if (aut.status === 200 && /name="motivo"/.test(aut.html)) await A.postPares(`/Venta/Autorizar/${v.ventaId}`, conOverrides(serializarForm(aut.html, 'motivo'), { motivo: 'D-05: BCRA/Veraz no validable en entorno aislado' }));
      const c = await A.html(`/Credito/ConfigurarVenta/${v.creditoId}?ventaId=${v.ventaId}&embedded=True`);
      const base = new Date(); const prim = new Date(Date.UTC(base.getUTCFullYear(), base.getUTCMonth() + 1, 15)).toISOString().slice(0, 10);
      const rc = await A.postPares('/Credito/ConfigurarVenta?embedded=True', conOverrides(serializarForm(c.html, 'CantidadCuotas'), { CantidadCuotas: 6, MetodoCalculo: 'Global', Anticipo: 0, GastosAdministrativos: 0, FechaPrimeraCuota: prim, CobrarPrimeraCuota: 'false' }));
      const jc = await rc.json().catch(() => ({}));
      E.check('plan de 6 cuotas configurado para la venta', jc.success === true, JSON.stringify(jc).slice(0, 160));

      await E.ir(page, `/ContratoVentaCredito/Preparar?ventaId=${v.ventaId}`);
      E.check('Preparar muestra el botón "Generar e imprimir contrato"', await E.visible(page, '[data-contrato-generar-btn]'));
      const txtAntes = await cuerpo(page);
      E.check('Preparar no lista datos faltantes (cliente, crédito, garante, plantilla)', !(await page.locator('a[href*="/Cliente/Edit"]').count()), txtAntes.slice(0, 80));
      // El JS abre el PDF con window.open: el popup es una descarga/visor, no una navegación de la página
      const popup = page.waitForEvent('popup', { timeout: 15000 }).catch(() => null);
      const [resp] = await Promise.all([
        page.waitForResponse((r) => /\/ContratoVentaCredito\/Generar/.test(r.url()) && r.request().method() === 'POST', { timeout: 30000 }),
        E.click(page, '[data-contrato-generar-btn]'),
      ]);
      const json = await resp.json().catch(() => ({}));
      E.check('Generar responde success por fetch (JSON)', json.success === true, JSON.stringify(json).slice(0, 200));
      const p2 = await popup; if (p2) await p2.close().catch(() => {});
      E.check('el JS pasa a estado "generado" (form Generar oculto, Confirmar visible)', (await E.oculto(page, '[data-contrato-generar-form]', 5000)) && (await E.visible(page, '[data-contrato-confirmar-form] button', 5000)));
      const ver = await E.ctx.request.get(E.opts.base + `/ContratoVentaCredito/Ver?ventaId=${v.ventaId}`);
      const buf = await ver.body();
      E.check('Ver devuelve un PDF real (%PDF) adjunto', ver.status() === 200 && buf.slice(0, 4).toString() === '%PDF' && buf.length > 2000, `${ver.status()} ${ver.headers()['content-type']} ${buf.length}b`);
      await Promise.all([page.waitForURL(new RegExp(`/Venta/`), { timeout: 20000 }), E.click(page, '[data-contrato-confirmar-form] button[type="submit"]')]);
      await page.waitForLoadState('networkidle');
      const det = await E.ir(page, `/Venta/Details/${v.ventaId}`);
      const txt = await cuerpo(page);
      E.check('la operación quedó Confirmada (persistida)', det.status() === 200 && /Confirmada/i.test(txt));
      await E.ir(page, `/ContratoVentaCredito/Preparar?ventaId=${v.ventaId}`);
      E.check('Preparar de una venta con contrato ya generado muestra el estado generado', await E.visible(page, '[data-contrato-state-generado]'));
    },
  },

  {
    nombre: 'D-05 5: devolución completa: alta por modal, Aprobar y Completar',
    usuario: 'admin', escribe: true,
    async fn(E, page) {
      const A = exigeSetup(E);
      E.aceptarDialogos = true;
      const v = await crearVenta(A, { qty: 2 });
      E.check('venta confirmada', (await confirmarVenta(A, v.ventaId)) === 302);
      E.check('venta facturada (requisito de devolución)', [302, 200].includes(await facturarPorHttp(A, v.ventaId)));
      const id = await crearDevolucionPorModal(E, page, v.ventaId, 'Producto con falla de fabricación verificada por QA D-05');
      if (!id) return;
      S.devCompleta = id;
      await E.ir(page, `/Devolucion/Detalles/${id}`);
      let txt = await cuerpo(page);
      E.check('Detalles: devolución en estado Pendiente', /Pendiente/i.test(txt));
      await enviar(E, page, 'form[action*="/Aprobar"] button[type="submit"]');
      txt = await cuerpo(page);
      E.check('Aprobar: pasa a Aprobada', /Aprobad/i.test(txt));
      await enviar(E, page, 'form[action*="/Completar"] button[type="submit"]');
      txt = await cuerpo(page);
      E.check('Completar: pasa a Completada', /Completad/i.test(txt));
      E.check('no quedan acciones pendientes (Aprobar/Completar) sobre la devolución completada', !(await page.locator('form[action*="/Aprobar"], form[action*="/Completar"]').count()));
    },
  },

  {
    nombre: 'D-05 6: devolución rechazada (panel toggle-hide + motivo) y cancelada',
    usuario: 'admin', escribe: true,
    async fn(E, page) {
      const A = exigeSetup(E);
      E.aceptarDialogos = true;
      const v = await crearVenta(A, { qty: 2 });
      await confirmarVenta(A, v.ventaId); await facturarPorHttp(A, v.ventaId);
      const idR = await crearDevolucionPorModal(E, page, v.ventaId, 'Cliente se arrepintió de la compra, devolución para rechazar D-05');
      if (!idR) return;
      await E.ir(page, `/Devolucion/Detalles/${idR}`);
      await E.click(page, '[data-action="toggle-hide"][data-toggle="rechazar-panel"]');
      E.check('el panel de rechazo se muestra (toggle-hide)', await E.visible(page, '#rechazar-panel input[name="motivo"]'));
      await page.fill('#rechazar-panel input[name="motivo"]', 'Fuera de plazo de devolución D-05');
      await enviar(E, page, '#rechazar-panel button[type="submit"]');
      E.check('Rechazar: pasa a Rechazada', /Rechazad/i.test(await cuerpo(page)));

      const idC = await crearDevolucionPorModal(E, page, v.ventaId, 'Carga por error del operador, se cancela antes de aprobar D-05');
      if (!idC) return;
      await E.ir(page, `/Devolucion/Detalles/${idC}`);
      await E.click(page, '[data-action="toggle-hide"][data-toggle="cancelar-panel"]');
      E.check('el panel de cancelación se muestra y el de rechazo se oculta', (await E.visible(page, '#devolucion-cancelar-motivo')) && (await E.oculto(page, '#rechazar-panel', 2000)));
      await page.fill('#devolucion-cancelar-motivo', 'Carga duplicada por error D-05');
      await enviar(E, page, '#cancelar-panel button[type="submit"]');
      E.check('Cancelar: pasa a Cancelada', /Cancelad/i.test(await cuerpo(page)));

      // Datos para los escenarios de autorización (D-05 7..9): una devolución que queda Pendiente y una venta confirmada sin facturar.
      S.devPendiente = await crearDevolucionPorModal(E, page, v.ventaId, 'Devolución que queda pendiente para las pruebas de autorización D-05');
      const vs = await crearVenta(A); await confirmarVenta(A, vs.ventaId); S.ventaSinFacturar = vs.ventaId;
      E.check('quedan una devolución Pendiente y una venta confirmada sin facturar para las pruebas de autorización', !!S.devPendiente && !!S.ventaSinFacturar);
    },
  },

  {
    nombre: 'D-05 7: autorización: Administrador (sin configuracion.update) no puede dar de alta plantillas de contrato',
    usuario: 'administrador', escribe: true, requiereInteraccion: false,
    async fn(E, page) {
      const A = api(E);
      const nombre = `Plantilla NO AUTORIZADA ${RUN}`;
      await E.ir(page, '/PlantillaContratoCredito/Create');
      E.check('GET Create no muestra el formulario (AccessDenied)', /AccessDenied/i.test(page.url()) || !(await page.locator('input[name="Nombre"]').count()), page.url());
      const r = await A.postForm('/Cliente', '/PlantillaContratoCredito/Create', {
        Nombre: nombre, Activa: 'true', NombreVendedor: 'X', DomicilioVendedor: 'X', CiudadFirma: 'X', Jurisdiccion: 'X', InteresMoraDiarioPorcentaje: '0.5',
        TextoContrato: 'x {{COMPRADOR_NOMBRE}}', TextoPagare: 'x', VigenteDesde: hoy(),
      });
      E.check('POST Create es rechazado por permisos', denegado(r), `${r.status()} ${r.headers()['location'] || ''}`);
      const l = await A.html('/PlantillaContratoCredito');
      E.check('la plantilla no autorizada no se persistió (o el listado ni se abre)', !l.html.includes(nombre), l.status);
    },
  },

  {
    nombre: 'D-05 8: autorización: Cajero no puede generar contratos ni facturar',
    usuario: 'cajero', escribe: true, requiereInteraccion: false,
    async fn(E, page) {
      const A = exigeSetup(E);
      const rg = await A.postForm('/Cliente', '/ContratoVentaCredito/Generar', { ventaId: S.ventaCredito });
      E.check('POST ContratoVentaCredito/Generar rechazado (creditos.configure)', denegado(rg), `${rg.status()} ${rg.headers()['location'] || ''}`);
      const rf = await A.postForm('/Cliente', '/Venta/Facturar', { VentaId: S.ventaSinFacturar, Tipo: '1', PuntoVenta: '1', FechaEmision: hoy() });
      E.check('POST Venta/Facturar rechazado (ventas.invoice)', denegado(rf), `${rf.status()} ${rf.headers()['location'] || ''}`);
    },
  },

  {
    nombre: 'D-05 9: autorización: Repositor (solo devoluciones.view) ve la devolución pero no puede aprobar, rechazar ni completar',
    usuario: 'repositor', escribe: true, requiereInteraccion: false,
    async fn(E, page) {
      const A = exigeSetup(E);
      const r = await E.ir(page, `/Devolucion/Detalles/${S.devPendiente}`);
      E.check('Detalles es visible para quien tiene devoluciones.view', r.status() === 200 && /Pendiente/i.test(await cuerpo(page)), r.status());
      E.check('no se renderizan los formularios Aprobar/Completar para este rol', !(await page.locator('form[action*="/Aprobar"], form[action*="/Completar"]').count()));
      for (const [acc, campos] of [['Aprobar', {}], ['Completar', {}], ['Rechazar', { motivo: 'no autorizado' }], ['Cancelar', { motivo: 'no autorizado' }]]) {
        const rr = await A.postForm('/Cliente', `/Devolucion/${acc}`, { id: S.devPendiente, ...campos });
        E.check(`POST Devolucion/${acc} rechazado por permisos`, denegado(rr), `${rr.status()} ${rr.headers()['location'] || ''}`);
      }
      await E.ir(page, `/Devolucion/Detalles/${S.devPendiente}`);
      E.check('la devolución sigue Pendiente', /Pendiente/i.test(await cuerpo(page)));
    },
  },

  {
    nombre: 'D-05 10: los POST rechazados por permisos no dejaron efectos (estado verificado como SuperAdmin)',
    usuario: 'admin', escribe: true,
    async fn(E, page) {
      exigeSetup(E);
      await E.ir(page, `/Devolucion/Detalles/${S.devPendiente}`);
      E.check('la devolución sigue Pendiente y con sus acciones', /Pendiente/i.test(await cuerpo(page)) && (await page.locator('form[action*="/Aprobar"]').count()) === 1);
      await E.click(page, '[data-action="toggle-hide"][data-toggle="rechazar-panel"]');
      await E.ir(page, `/Venta/Details/${S.ventaSinFacturar}`);
      E.check('la venta sigue Confirmada y facturable', (await page.locator('[data-venta-modal-target="facturar-modal"]').count()) === 1);
      const pl = await E.ir(page, '/PlantillaContratoCredito');
      E.check('el listado de plantillas no contiene la plantilla no autorizada', pl.status() === 200 && !(await cuerpo(page)).includes('NO AUTORIZADA'));
    },
  },

  {
    nombre: 'D-05 11: control negativo: un handler inline inyectado en Venta/Details y Devolucion/Detalles SE DETECTA como violación CSP',
    usuario: 'admin', escribe: true,
    violacionesEsperadas: [/script-src-attr|inline|unsafe-inline/i],
    async fn(E, page) {
      exigeSetup(E);
      for (const ruta of [`/Venta/Details/${S.ventaSinFacturar}`, `/Devolucion/Detalles/${S.devPendiente}`]) {
        await E.ir(page, ruta);
        await page.evaluate(() => { const b = document.createElement('button'); b.id = 'mutante-d05'; b.setAttribute('onclick', 'window.__mutante = 1'); b.textContent = 'x'; document.body.appendChild(b); });
        await E.click(page, '#mutante-d05');
        E.check(`${ruta}: el handler inline inyectado no se ejecutó (la política lo bloquea en Enforce)`, E.opts.modo === 'reportonly' || (await page.evaluate(() => window.__mutante)) !== 1);
      }
    },
  },
];
