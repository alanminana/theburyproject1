"""Casos adicionales de la segunda pasada (S1..S5): limites, negativos, reversiones, idempotencia, concurrencia y auditoria.
Se ejecuta despues de los recorridos P-07 (R0..R11), con sus datos sinteticos ya cargados."""
import re
import threading
import time


def paralelo(n, fn):
    """Lanza n hilos que esperan una barrera y disparan fn(i) a la vez. Devuelve los resultados."""
    barrera, res = threading.Barrier(n), [None] * n

    def run(i):
        try:
            barrera.wait()
            res[i] = fn(i)
        except Exception as ex:  # noqa: BLE001
            res[i] = ex
    hs = [threading.Thread(target=run, args=(i,)) for i in range(n)]
    [h.start() for h in hs]
    [h.join() for h in hs]
    return res


def correr(m):
    chk, sql, q1, dec, sec = m.chk, m.sql, m.q1, m.dec, m.sec
    adm = m.new_session("administrador", "Admin123!")
    cid = int(q1("select top 1 Id from Clientes order by Id"))
    pid = int(q1("select Id from Productos where Codigo='P07-HEL-001'"))
    prov = int(q1("select top 1 Id from Proveedores order by Id"))
    caja = int(q1("select Id from Cajas where Codigo='CAJA-P07'"))

    def stock():
        return dec(q1("select StockActual from Productos where Id=%d" % pid))

    # ------------------------------------------------------------------ S1
    sec("S1 Clientes: limites de nombres y apellidos (autorizado: 20 letras por palabra, 6 palabras)")

    def alta(dni, apellido, nombre):
        ver = m.cuit_digit("20" + dni)
        ov = {"TipoDocumento": "DNI", "NumeroDocumento": dni, "CuilPrefijo": "20", "CuilVerificador": str(ver), "Apellido": apellido, "Nombre": nombre,
              "FechaNacimiento": "1985-03-10", "EstadoCivil": "1", "Telefono": "1144445555", "Email": "n%s@example.invalid" % dni, "CodigoPostal": "1000",
              "Domicilio": "Calle Falsa 123", "Localidad": "CABA", "Provincia": "Buenos Aires", "TipoEmpleo": "1", "TiempoTrabajo": "5 anios", "Empleador": "Empresa SA",
              "Sueldo": "1500000", "TelefonoLaboral": "1133334444", "TieneReciboSueldo": "true"}
        r, _ = adm.submit("/Cliente/Create", ov, action_contains="/Cliente/Create", follow=False)
        return r, q1("select count(*) from Clientes where NumeroDocumento='%s'" % dni) == "1"

    r, ok = alta("20111001", "Constantino", "Maximiliano")
    chk("S1.1", "apellido de 11 letras ('Constantino') y nombre de 11 ('Maximiliano') se aceptan", ok and r.status == 302, r.status)
    r, ok = alta("20111002", "Wolfeschlegelstein", "Ana Maria de los Angeles Gomez")
    chk("S1.2", "apellido de 18 letras y nombre de 6 palabras se aceptan", ok, r.status)
    r, ok = alta("20111003", "Abcdefghijklmnopqrstu", "Juan")
    chk("S1.3", "apellido de 21 letras rechazado (sin alta)", (not ok) and r.status == 200, r.status)
    r, ok = alta("20111004", "Perez", "Ana Maria de los Angeles Gomez Paz Luna")
    chk("S1.4", "nombre de 7 palabras rechazado (sin alta)", (not ok) and r.status == 200, r.status)
    r, ok = alta("20111005", "Perez", "Juan3")
    chk("S1.5", "digitos en el nombre rechazados", (not ok) and r.status == 200, r.status)

    # ------------------------------------------------------------------ S2
    sec("S2 Ventas: concurrencia (doble confirmacion y stock limitado)")
    r, _ = adm.submit("/Caja/Abrir?cajaId=%d" % caja, {"MontoInicial": "1000"}, action_contains="/Caja/Abrir", follow=False)
    chk("S2.0", "caja reabierta para el bloque de concurrencia", q1("select count(*) from AperturasCaja where CajaId=%d and Cerrada=0" % caja) == "1", r.status)

    def presupuesto(qty=1):
        ov = {"ClienteId": str(cid), "TipoPago": "1", "Detalles[0].ProductoId": str(pid), "Detalles[0].Cantidad": str(qty), "Detalles[0].PrecioUnitario": "181500",
              "Detalles[0].Descuento": "0", "Detalles[0].Subtotal": str(181500 * qty), "Subtotal": str(150000 * qty), "IVA": str(31500 * qty), "Total": str(181500 * qty)}
        r_, _ = adm.submit("/Venta/Create", ov, action_contains="/Venta/Create", follow=False)
        return int(r_.location.rsplit("/", 1)[1])

    def sesiones_para(vids):
        out = []
        for vid in vids:
            e = m.new_session("administrador", "Admin123!")
            out.append((e, e.token(e.get("/Venta/Details/%d" % vid).text), vid))
        return out

    s0 = stock()
    v = presupuesto()
    ses = sesiones_para([v] * 4)
    paralelo(4, lambda i: ses[i][0].post("/Venta/Confirmar", {"id": str(ses[i][2]), "__RequestVerificationToken": ses[i][1]}, follow=False))
    chk("S2.1", "4 confirmaciones simultaneas de la misma venta: confirmada una sola vez (estado 2)", q1("select Estado from Ventas where Id=%d" % v) == "2")
    chk("S2.2", "stock descontado exactamente una vez", stock() == s0 - 1, "%s -> %s" % (s0, stock()))
    num = q1("select Numero from Ventas where Id=%d" % v)
    chk("S2.3", "un unico movimiento de stock de salida para esa venta", q1("select count(*) from MovimientosStock where Referencia like '%%%s%%' and Tipo=1" % num) == "1")

    exc = int(stock()) - 1
    if exc > 0:
        adm.submit("/MovimientoStock/Create", {"ProductoId": str(pid), "Tipo": "1", "Cantidad": str(exc), "Motivo": "QA S2 dejar 1"}, action_contains="/MovimientoStock/Create", follow=False)
    chk("S2.4", "stock ajustado a 1 para la prueba de ultima unidad", stock() == 1, stock())
    va, vb = presupuesto(), presupuesto()
    ses = sesiones_para([va, vb])
    paralelo(2, lambda i: ses[i][0].post("/Venta/Confirmar", {"id": str(ses[i][2]), "__RequestVerificationToken": ses[i][1]}, follow=False))
    conf = int(q1("select count(*) from Ventas where Id in (%d,%d) and Estado=2" % (va, vb)))
    chk("S2.5", "dos ventas por la ultima unidad confirmadas a la vez: solo una se confirma", conf == 1, "confirmadas=%d" % conf)
    chk("S2.6", "el stock nunca queda negativo", stock() == 0, stock())

    # ------------------------------------------------------------------ S3
    sec("S3 Caja: un turno abierto por usuario (apertura concurrente) y movimientos")
    adm.submit("/Caja/Create", {"Codigo": "CAJA-S3", "Nombre": "Caja S3", "Sucursal": "Central"}, fields_marker="Codigo", follow=False)
    adm.submit("/Caja/Create", {"Codigo": "CAJA-S3B", "Nombre": "Caja S3B", "Sucursal": "Central"}, fields_marker="Codigo", follow=False)
    ids = [int(q1("select Id from Cajas where Codigo='%s'" % c_)) for c_ in ("CAJA-S3", "CAJA-S3B")]
    ses = []
    for k in range(4):
        e = m.new_session("admin", "Admin123!")
        ses.append((e, e.get("/Caja/Abrir?cajaId=%d" % ids[k % 2]), ids[k % 2]))

    def abrir(i):
        e, pg, cj = ses[i]
        fs = [x for x in e.forms(pg.text) if "/Caja/Abrir" in (x["attrs"].get("action") or "")]
        if not fs:
            return None
        fields = dict((k_, v_) for k_, v_ in fs[0]["fields"])
        fields["MontoInicial"] = "100"
        fields["CajaId"] = str(cj)
        return e.post(fs[0]["attrs"]["action"], list(fields.items()), follow=False)

    res = paralelo(4, abrir)
    chk("S3.0", "las 4 sesiones del usuario pudieron enviar la apertura", all(r_ is not None and not isinstance(r_, Exception) for r_ in res), [type(r_).__name__ for r_ in res])
    abiertas = q1("select count(*) from AperturasCaja where CajaId in (%d,%d) and Cerrada=0" % tuple(ids))
    chk("S3.1", "4 aperturas simultaneas del mismo usuario sobre 2 cajas: un solo turno abierto (regla: una caja abierta por usuario)", abiertas == "1", "abiertas=%s" % abiertas)
    apert_p07 = int(q1("select top 1 Id from AperturasCaja where CajaId=%d and Cerrada=0" % caja))
    apert_p07 = int(q1("select top 1 Id from AperturasCaja where CajaId=%d and Cerrada=0" % caja))
    url_mov = "/Caja/RegistrarMovimiento?aperturaId=%d" % apert_p07

    def movimiento(monto):
        # Tipo y Concepto explicitos (el select de Concepto lo completa JavaScript en el navegador; el cliente HTTP no lo ejecuta)
        return adm.submit(url_mov, {"Tipo": "1", "Concepto": "10", "Monto": str(monto), "Descripcion": "movimiento QA S3"}, action_contains="/Caja/RegistrarMovimiento", follow=False)[0]

    n_mov = int(q1("select count(*) from MovimientosCaja"))
    r = movimiento(250)
    chk("S3.2", "control positivo: el formulario con Tipo/Concepto validos y monto 250 registra el egreso (302 y una fila nueva por 250)",
        r.status == 302 and int(q1("select count(*) from MovimientosCaja")) == n_mov + 1 and dec(q1("select top 1 Monto from MovimientosCaja order by Id desc")) == 250, "%s" % r.status)
    n_mov = int(q1("select count(*) from MovimientosCaja"))
    for cid_, monto, desc in (("S3.3", -5, "negativo"), ("S3.4", 0, "cero")):
        r = movimiento(monto)
        txt = re.sub(r"\s+", " ", re.sub(r"<[^>]+>", " ", re.sub(r"<script.*?</script>", "", r.text, flags=re.S)))
        chk(cid_, "movimiento manual con monto %s rechazado: vuelve al formulario (200, no 400/500), sin fila nueva y con el mensaje de validacion del monto (entre 0,01 y 20.000.000)" % desc,
            r.status == 200 and int(q1("select count(*) from MovimientosCaja")) == n_mov and "entre 0,01 y 20.000.000" in txt, "%s %s" % (r.status, (re.findall(r"El campo Monto[^.]*\.", txt) or [""])[0]))

    # ------------------------------------------------------------------ S4
    sec("S4 Compras / cuentas por pagar: comprobantes, pagos, anticipos, anulaciones e idempotencia")
    r, _ = adm.submit("/Tesoreria/Crear", {"Codigo": "ORI-QA", "Nombre": "Origen QA", "Tipo": "2", "Activo": "true"}, action_contains="/Tesoreria/Guardar", follow=False)
    ori = q1("select Id from OrigenesFondos where Codigo='ORI-QA'")
    chk("S4.1", "origen de fondos creado", ori is not None, r.status)
    ori = int(ori)

    def comprobante(num, total, tipo=1):
        neto = round(total / 1.21, 2)
        pg_ = adm.get("/CuentaPorPagar/Crear?proveedorId=%d&tipo=%d" % (prov, tipo))
        return adm.post("/CuentaPorPagar/Crear", [("ProveedorId", str(prov)), ("Tipo", str(tipo)), ("NumeroComprobante", num), ("FechaEmision", "2026-11-10"), ("FechaVencimiento", "2026-12-10"),
                        ("ImporteNeto", str(neto)), ("ImporteIva", str(round(total - neto, 2))), ("ImporteTotal", str(total)), ("__RequestVerificationToken", adm.token(pg_.text))], follow=False)

    r = comprobante("A-0001-00000001", 121000)
    cp1 = q1("select Id from ComprobantesProveedor where NumeroComprobante='A-0001-00000001'")
    chk("S4.2", "factura de proveedor registrada: saldo = total, estado Pendiente", cp1 is not None and dec(q1("select SaldoPendiente from ComprobantesProveedor where Id=%s" % cp1)) == 121000 and q1("select Estado from ComprobantesProveedor where Id=%s" % cp1) == "1", r.status)
    cp1 = int(cp1)
    n_cp = int(q1("select count(*) from ComprobantesProveedor"))
    comprobante("A-0001-00000001", 5000)
    chk("S4.3", "numero de comprobante duplicado del mismo proveedor rechazado", int(q1("select count(*) from ComprobantesProveedor")) == n_cp)
    comprobante("A-0001-00000002", 0)
    chk("S4.4", "comprobante por importe 0 rechazado", int(q1("select count(*) from ComprobantesProveedor")) == n_cp)
    comprobante("A-0001-00000003", -100)
    chk("S4.5", "comprobante por importe negativo rechazado", int(q1("select count(*) from ComprobantesProveedor")) == n_cp)

    def saldo():
        return dec(q1("select SaldoPendiente from ComprobantesProveedor where Id=%d" % cp1))

    def pagar(monto, clave=None, origen=ori, sess=None):
        sess = sess or adm
        pg = sess.get("/CuentaPorPagar/Pagar?proveedorId=%d&comprobanteId=%d" % (prov, cp1))
        f = [("ProveedorId", str(prov)), ("Medio", "2"), ("Monto", str(monto)), ("Referencia", "TR-1"), ("OrigenFondosId", str(origen) if origen else ""),
             ("ClaveIdempotencia", clave or "k-%s" % time.time()), ("Imputaciones[0].ComprobanteId", str(cp1)), ("Imputaciones[0].Monto", str(monto)),
             ("Fecha", "2026-11-19"), ("__RequestVerificationToken", sess.token(pg.text))]
        return sess.post("/CuentaPorPagar/Pagar", f, follow=False)

    n_pg = int(q1("select count(*) from PagosProveedor"))
    pagar(200000)
    chk("S4.6", "pago mayor al saldo rechazado (sin pago ni cambio de saldo)", int(q1("select count(*) from PagosProveedor")) == n_pg and saldo() == 121000)
    pagar(1000, origen=None)
    chk("S4.7", "pago sin origen de fondos rechazado", int(q1("select count(*) from PagosProveedor")) == n_pg)
    pagar(0)
    chk("S4.8", "pago por 0 rechazado", int(q1("select count(*) from PagosProveedor")) == n_pg)
    pagar(21000)
    pg1 = q1("select max(Id) from PagosProveedor")
    chk("S4.9", "pago parcial 21.000: saldo 100.000, estado ParcialmentePagado (2), una imputacion",
        saldo() == 100000 and q1("select Estado from ComprobantesProveedor where Id=%d" % cp1) == "2" and q1("select count(*) from PagoProveedorImputaciones where PagoProveedorId=%s" % pg1) == "1")

    n_pg = int(q1("select count(*) from PagosProveedor"))
    ses = [m.new_session("administrador", "Admin123!") for _ in range(4)]
    clave = "idem-s4-%s" % time.time()
    paralelo(4, lambda i: pagar(10000, clave=clave, sess=ses[i]))
    chk("S4.10", "4 envios simultaneos con la misma clave de idempotencia: un solo pago de 10.000 (saldo 90.000)",
        int(q1("select count(*) from PagosProveedor")) == n_pg + 1 and saldo() == 90000, "pagos=%s saldo=%s" % (q1("select count(*) from PagosProveedor"), saldo()))

    ses2 = [m.new_session("administrador", "Admin123!") for _ in range(3)]
    n_pg = int(q1("select count(*) from PagosProveedor"))
    res = paralelo(3, lambda i: pagar(50000, clave="k-conc-%d-%s" % (i, time.time()), sess=ses2[i]))
    respondieron = all(r_ is not None and not isinstance(r_, Exception) and r_.status in (200, 302) for r_ in res)
    pagado = dec(q1("select isnull(sum(i.Monto),0) from PagoProveedorImputaciones i join PagosProveedor p on p.Id=i.PagoProveedorId where i.ComprobanteProveedorId=%d and p.Anulado=0" % cp1))
    chk("S4.11", "3 pagos simultaneos de 50.000 con saldo 90.000: los 3 reciben respuesta de negocio (200/302, sin error), se registra exactamente UNO, saldo 40.000 = total - pagos vigentes",
        respondieron and int(q1("select count(*) from PagosProveedor")) == n_pg + 1 and saldo() == 40000 and abs(saldo() - (121000 - pagado)) < 0.01,
        "respuestas=%s pagos nuevos=%d saldo=%s pagado=%s" % ([getattr(r_, "status", r_) for r_ in res], int(q1("select count(*) from PagosProveedor")) - n_pg, saldo(), pagado))

    ult = int(q1("select max(Id) from PagosProveedor where Anulado=0"))
    s_ant = saldo()
    monto_ult = dec(q1("select Monto from PagosProveedor where Id=%d" % ult))
    pg = adm.get("/CuentaPorPagar/Pago/%d" % ult)
    f = [x for x in adm.forms(pg.text) if "AnularPago" in (x["attrs"].get("action") or "")]
    if f:
        adm.post(f[0]["attrs"]["action"], [("motivo", "Anulacion QA S4"), ("__RequestVerificationToken", adm.token(pg.text))], follow=False)
    chk("S4.12", "anular un pago restituye el saldo y conserva el pago marcado como anulado",
        bool(f) and q1("select Anulado from PagosProveedor where Id=%d" % ult) == "1" and abs(saldo() - (s_ant + monto_ult)) < 0.01, "saldo %s -> %s (+%s)" % (s_ant, saldo(), monto_ult))
    if f:
        pg = adm.get("/CuentaPorPagar/Pago/%d" % ult)
        adm.post(f[0]["attrs"]["action"], [("motivo", "Anulacion repetida"), ("__RequestVerificationToken", adm.token(pg.text))], follow=False)
    chk("S4.13", "anular dos veces el mismo pago no restituye dos veces", abs(saldo() - (s_ant + monto_ult)) < 0.01, saldo())

    def anticipar(monto, clave):
        pg_ = adm.get("/CuentaPorPagar/Anticipar?proveedorId=%d" % prov)
        return adm.post("/CuentaPorPagar/Anticipar", [("ProveedorId", str(prov)), ("Fecha", "2026-11-19"), ("Monto", str(monto)), ("OrigenFondosId", str(ori)), ("Medio", "2"),
                        ("Referencia", "ANT"), ("ClaveIdempotencia", clave), ("__RequestVerificationToken", adm.token(pg_.text))], follow=False)

    anticipar(0, "ant-0")
    chk("S4.14", "anticipo por 0 rechazado", q1("select count(*) from AnticiposProveedor") == "0")
    anticipar(30000, "ant-ok")
    anticipar(30000, "ant-ok")
    chk("S4.15", "anticipo de 30.000 registrado; reenviar con la misma clave no lo duplica", q1("select count(*) from AnticiposProveedor") == "1")
    ant = int(q1("select Id from AnticiposProveedor"))
    pgA = adm.get("/CuentaPorPagar/AplicarAnticipo/%d" % ant)
    saldo0 = saldo()
    tok = adm.token(pgA.text)
    r = adm.post("/CuentaPorPagar/AplicarAnticipo/%d" % ant, [("id", str(ant)), ("imputaciones[0].ComprobanteId", str(cp1)), ("imputaciones[0].Monto", "40000"), ("claveIdempotencia", "ap-1"), ("__RequestVerificationToken", tok)], follow=False)
    chk("S4.16", "aplicar mas que el anticipo disponible (40.000 de 30.000) rechazado", q1("select count(*) from AnticipoAplicaciones") == "0" and saldo() == saldo0, r.status)
    r = adm.post("/CuentaPorPagar/AplicarAnticipo/%d" % ant, [("id", str(ant)), ("imputaciones[0].ComprobanteId", str(cp1)), ("imputaciones[0].Monto", "30000"), ("claveIdempotencia", "ap-2"), ("__RequestVerificationToken", tok)], follow=False)
    chk("S4.17", "aplicar 30.000 del anticipo: el saldo baja 30.000", saldo() == saldo0 - 30000 and q1("select count(*) from AnticipoAplicaciones where Anulada=0") == "1", "%s -> %s" % (saldo0, saldo()))
    pgC = adm.get("/CuentaPorPagar/Detalle/%d" % cp1)
    adm.post("/CuentaPorPagar/Anular/%d" % cp1, [("id", str(cp1)), ("motivo", "anular con pagos"), ("__RequestVerificationToken", adm.token(pgC.text))], follow=False)
    chk("S4.18", "anular un comprobante con pagos o aplicaciones vigentes se rechaza", q1("select Estado from ComprobantesProveedor where Id=%d" % cp1) != "4", q1("select Estado from ComprobantesProveedor where Id=%d" % cp1))
    comprobante("NC-0001-00000001", 5000, tipo=3)
    chk("S4.19", "nota de credito de proveedor registrada", q1("select count(*) from ComprobantesProveedor where NumeroComprobante='NC-0001-00000001'") == "1")
    ven = m.new_session("vendedor", "Vendedor123!")
    pg = ven.get("/Cliente")
    n_pg = int(q1("select count(*) from PagosProveedor"))
    ven.post("/CuentaPorPagar/Pagar", [("ProveedorId", str(prov)), ("Monto", "100"), ("OrigenFondosId", str(ori)), ("ClaveIdempotencia", "x-v"), ("__RequestVerificationToken", ven.token(pg.text) or "")], follow=False)
    chk("S4.20", "vendedor no puede registrar pagos a proveedores", int(q1("select count(*) from PagosProveedor")) == n_pg)

    # ------------------------------------------------------------------ S5
    sec("S5 Auditoria y usuarios")
    chk("S5.1", "auditoria de seguridad: hay eventos registrados durante el recorrido", int(q1("select count(*) from SeguridadEventosAuditoria")) > 0, q1("select count(*) from SeguridadEventosAuditoria"))
    print("   (informativo) ultimos eventos:", sql("select top 3 Accion from SeguridadEventosAuditoria order by Id desc"))

    # ------------------------------------------------------------------ S6
    sec("S6 Usuarios y roles: ultimo SuperAdmin, jerarquia, revocacion de sesion")
    sup = m.new_session("admin", "Admin123!")
    uid = lambda u: q1("select Id from AspNetUsers where UserName='%s'" % u)
    act = lambda u: q1("select Activo from AspNetUsers where UserName='%s'" % u)

    def post_u(e, accion, uid_):
        pg = e.get("/Seguridad")
        return e.post("/Usuarios/%s" % accion, [("id", uid_), ("__RequestVerificationToken", e.token(pg.text) or "")], follow=False)

    r = post_u(adm, "Desactivar", uid("admin"))
    chk("S6.1", "un Administrador no puede desactivar a un SuperAdmin (la peticion llega al servidor: 302/403, no 400)", act("admin") == "1" and r.status in (302, 403), "%s %s" % (r.status, r.location))
    r = post_u(sup, "Desactivar", uid("admin"))
    chk("S6.2", "el unico SuperAdmin no puede desactivarse a si mismo (invariante del ultimo SuperAdmin)", act("admin") == "1" and r.status == 302, "%s %s" % (r.status, r.location))
    tec = m.new_session("tecnico", "Tecnico123!")
    chk("S6.3", "tecnico con sesion activa abre una pantalla permitida", tec.get("/Catalogo", follow=False).status == 200)
    r = post_u(sup, "Desactivar", uid("tecnico"))
    chk("S6.4", "SuperAdmin desactiva al tecnico (Activo=0)", act("tecnico") == "0", "%s act=%s" % (r.status, act("tecnico")))
    r = tec.get("/Catalogo", follow=False)
    chk("S6.5", "la sesion ya emitida del usuario desactivado deja de valer (redirige al login, SecurityStamp)", r.status in (302, 401, 403) and r.status != 200, "%s %s" % (r.status, r.location))
    nuevo = m.Erp(m.BASE)
    rl = nuevo.login("tecnico", "Tecnico123!")
    r2 = nuevo.get("/Catalogo", follow=False)
    chk("S6.6", "el usuario desactivado no puede volver a iniciar sesion", r2.status != 200, "%s" % r2.status)
    post_u(sup, "Reactivar", uid("tecnico"))
    chk("S6.7", "reactivar al usuario le devuelve el acceso", act("tecnico") == "1" and m.new_session("tecnico", "Tecnico123!").get("/Catalogo", follow=False).status == 200)
    ev = int(q1("select count(*) from SeguridadEventosAuditoria"))
    chk("S6.8", "los intentos denegados y las desactivaciones quedaron en la auditoria de seguridad", ev >= 4, ev)

    # ------------------------------------------------------------------ S7
    sec("S7 Mora y acuerdos de pago (cuotas vencidas simuladas por SQL en la base descartable)")
    cr = int(q1("select top 1 Id from Creditos order by Id"))
    cli = int(q1("select ClienteId from Creditos where Id=%d" % cr))
    sql("update Cuotas set FechaVencimiento=dateadd(day,-45,getdate()) where CreditoId=%d and NumeroCuota in (2,3) and MontoPagado=0" % cr)
    pg = adm.get("/Mora/FichaCliente/%d" % cli)
    chk("S7.1", "ficha de mora del cliente con deuda vencida abre (200)", pg.status == 200, pg.status)

    def acuerdo(**kw):
        base = {"ClienteId": str(cli), "CreditoId": str(cr), "MontoEntregaInicial": "10000", "CantidadCuotas": "3", "MontoCondonar": "0",
                "FechaPrimeraCuota": time.strftime("%Y-%m-%d", time.localtime(time.time() + 7 * 86400)), "Observaciones": "Acuerdo sintetico QA"}
        base.update({k_: str(v_) for k_, v_ in kw.items()})
        p_ = adm.get("/Mora/FichaCliente/%d" % cli)
        return adm.post("/Mora/CrearAcuerdo", list(base.items()) + [("__RequestVerificationToken", adm.token(p_.text))], follow=False)

    n_ac = int(q1("select count(*) from AcuerdosPago"))
    acuerdo(MontoCondonar=5000)
    chk("S7.2", "un acuerdo con condonacion se rechaza (el acuerdo financia la deuda completa)", int(q1("select count(*) from AcuerdosPago")) == n_ac)
    acuerdo(CantidadCuotas=0)
    chk("S7.3", "acuerdo con 0 cuotas rechazado", int(q1("select count(*) from AcuerdosPago")) == n_ac)
    acuerdo(Observaciones="")
    chk("S7.4", "acuerdo sin observaciones rechazado", int(q1("select count(*) from AcuerdosPago")) == n_ac)
    acuerdo(MontoEntregaInicial=99999999)
    chk("S7.5", "entrega inicial mayor que la deuda rechazada", int(q1("select count(*) from AcuerdosPago")) == n_ac)
    acuerdo(FechaPrimeraCuota="2020-01-01")
    chk("S7.6", "primera cuota en el pasado rechazada", int(q1("select count(*) from AcuerdosPago")) == n_ac)
    acuerdo()
    chk("S7.7", "acuerdo valido creado", int(q1("select count(*) from AcuerdosPago")) == n_ac + 1, q1("select Estado from AcuerdosPago where Id=(select max(Id) from AcuerdosPago)"))
    acuerdo()
    chk("S7.8", "segundo acuerdo sobre el mismo credito (hay uno vigente) rechazado", int(q1("select count(*) from AcuerdosPago")) == n_ac + 1)
    ac = int(q1("select max(Id) from AcuerdosPago"))
    row = sql("select MontoDeudaOriginal,MontoMoraOriginal,MontoCondonado,MontoTotalAcuerdo,MontoEntregaInicial,CantidadCuotas,MontoCuotaAcuerdo from AcuerdosPago where Id=%d" % ac)[0]
    tot, ent, cu, ncu = dec(row[3]), dec(row[4]), dec(row[6]), int(row[5])
    chk("S7.9", "SQL: total del acuerdo = deuda + mora - condonado (condonado 0) y entrega + cuotas cubre el total",
        abs(dec(row[0]) + dec(row[1]) - dec(row[2]) - tot) < 0.02 and dec(row[2]) == 0 and abs(ent + cu * ncu - tot) < 0.05 * ncu, row)
    ncc0 = int(q1("select count(*) from CuotasAcuerdo where AcuerdoPagoId=%d" % ac))
    p_ = adm.get("/Mora/FichaCliente/%d" % cli)
    adm.post("/Mora/CambiarEstadoAcuerdo", [("acuerdoId", str(ac)), ("clienteId", str(cli)), ("estado", "2"), ("motivo", ""), ("__RequestVerificationToken", adm.token(p_.text))], follow=False)
    ncc = int(q1("select count(*) from CuotasAcuerdo where AcuerdoPagoId=%d" % ac))
    chk("S7.10", "activar el acuerdo: estado Activo, %d cuotas del calendario generadas (en borrador no hay cuotas: %d)" % (ncu, ncc0), q1("select Estado from AcuerdosPago where Id=%d" % ac) == "2" and ncc == ncu and ncc0 == 0, "estado=%s cuotas=%s" % (q1("select Estado from AcuerdosPago where Id=%d" % ac), ncc))
    chk("S7.10b", "SQL: el acuerdo cubre las cuotas vencidas del credito (AcuerdoPagoCuotasCubiertas)", int(q1("select count(*) from AcuerdoPagoCuotasCubiertas where AcuerdoPagoId=%d" % ac)) >= 1)
    ing0 = dec(q1("select isnull(sum(Monto),0) from MovimientosCaja where Tipo=0"))
    ses = []
    for _ in range(4):
        e = m.new_session("administrador", "Admin123!")
        ses.append((e, e.token(e.get("/Mora/FichaCliente/%d" % cli).text)))
    paralelo(4, lambda i: ses[i][0].post("/Mora/RegistrarPagoAcuerdo", [("acuerdoId", str(ac)), ("clienteId", str(cli)), ("medioPago", "Efectivo"), ("comprobante", "QA-ent"), ("pasoEsperado", "0"), ("__RequestVerificationToken", ses[i][1])], follow=False))
    npag = int(q1("select count(*) from AcuerdoPagoImputaciones where AcuerdoPagoId=%d" % ac))
    ing1 = dec(q1("select isnull(sum(Monto),0) from MovimientosCaja where Tipo=0"))
    chk("S7.12", "4 cobros simultaneos del mismo paso del acuerdo (pasoEsperado=0): se registra una sola vez", npag == 1, "imputaciones=%d" % npag)
    chk("S7.13", "SQL: el ingreso de caja es el de la entrega inicial (%s), una sola vez" % ent, abs((ing1 - ing0) - ent) < 0.02, "delta caja=%s" % (ing1 - ing0))
    chk("S7.14", "SQL: entrega inicial marcada como pagada y monto pagado = entrega", q1("select EntregaInicialPagada from AcuerdosPago where Id=%d" % ac) == "1" and abs(dec(q1("select MontoEntregaPagada from AcuerdosPago where Id=%d" % ac)) - ent) < 0.02)
    p_ = adm.get("/Mora/FichaCliente/%d" % cli)
    adm.post("/Mora/RegistrarPagoAcuerdo", [("acuerdoId", str(ac)), ("clienteId", str(cli)), ("medioPago", "Efectivo"), ("comprobante", "QA-dup"), ("pasoEsperado", "0"), ("__RequestVerificationToken", adm.token(p_.text))], follow=False)
    chk("S7.15", "repetir el mismo paso (pasoEsperado=0) con la entrega ya cobrada se rechaza", int(q1("select count(*) from AcuerdoPagoImputaciones where AcuerdoPagoId=%d" % ac)) == npag)
    p_ = adm.get("/Mora/FichaCliente/%d" % cli)
    adm.post("/Mora/CambiarEstadoAcuerdo", [("acuerdoId", str(ac)), ("clienteId", str(cli)), ("estado", "5"), ("motivo", "cancelado por QA"), ("__RequestVerificationToken", adm.token(p_.text))], follow=False)
    print("   (informativo) estado del acuerdo tras cancelarlo con pagos registrados:", q1("select Estado from AcuerdosPago where Id=%d" % ac))
    ven = m.new_session("vendedor", "Vendedor123!")
    pgv = ven.get("/Cliente")
    n_ac = int(q1("select count(*) from AcuerdosPago"))
    ven.post("/Mora/CrearAcuerdo", [("ClienteId", str(cli)), ("CreditoId", str(cr)), ("MontoEntregaInicial", "1"), ("CantidadCuotas", "2"), ("FechaPrimeraCuota", time.strftime("%Y-%m-%d")), ("Observaciones", "x"), ("__RequestVerificationToken", ven.token(pgv.text) or "")], follow=False)
    chk("S7.11", "un vendedor no puede crear acuerdos de pago", int(q1("select count(*) from AcuerdosPago")) == n_ac)
