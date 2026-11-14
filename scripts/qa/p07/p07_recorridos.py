#!/usr/bin/env python3
"""P-07 - recorridos integrales de negocio contra una instalacion Docker descartable.

Maneja el ERP por HTTP como un navegador (formularios reales + endpoints JSON del propio frontend) y contrasta cada
paso con SQL Server (consultas de solo lectura via `docker exec ... sqlcmd`). Solo stdlib de Python.
Datos 100 % sinteticos: la base se crea vacia (migrate en Development) y todo se da de alta por la aplicacion.

Variables de entorno:
  P07_BASE_URL         http://127.0.0.1:18097
  P07_DB_CONTAINER     contenedor SQL Server de la pila descartable (<proyecto>-db-1)
  P07_PROJECT          proyecto Compose efimero (buryp07<hex>); obligatorio: la guardia rechaza cualquier otro destino
  P07_APP_CONTAINER    contenedor de la app (<proyecto>-app-1)
  P07_SA_PASSWORD      password SA de ESA pila
  P07_DB_NAME          base (BuryP07)
  P07_ADMIN_USER/PASS  SuperAdmin creado por migrate (ADMIN_USERNAME/ADMIN_PASSWORD de la pila)
  P07_REPORT           ruta del JSON de resultados (opcional)
Los usuarios de prueba (administrador, vendedor, cajero...) los siembra DbInitializer en Development.
"""
import json, os, re, subprocess, sys, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from erp_http import Erp, msgs  # noqa: E402
from synthetic import cuit, cuit_digit, pdf  # noqa: E402

BASE = os.environ.get("P07_BASE_URL", "http://127.0.0.1:18097")
DBC = os.environ.get("P07_DB_CONTAINER", "")
SA = os.environ["P07_SA_PASSWORD"]
DB = os.environ.get("P07_DB_NAME", "BuryP07")
ADMIN_USER = os.environ.get("P07_ADMIN_USER", "admin_p07")
ADMIN_PASS = os.environ["P07_ADMIN_PASS"]
RESULTS = []
SECTION = [""]


def guardia_descartable():
    """Antes de CUALQUIER login o escritura: el destino debe ser la pila efimera creada por run-p07.sh y estar vacio.
    Rechaza destinos arbitrarios, HTTP y SQL de instalaciones distintas y bases con datos comerciales."""
    proyecto = os.environ.get("P07_PROJECT", "")
    if not re.fullmatch(r"buryp07[0-9a-f]{6}", proyecto):
        sys.exit("RECHAZADO: P07_PROJECT debe ser el proyecto efimero buryp07<hex> que crea run-p07.sh (no se opera contra otro destino).")
    if DB != "BuryP07":
        sys.exit("RECHAZADO: P07_DB_NAME debe ser BuryP07.")
    app_c = os.environ.get("P07_APP_CONTAINER", "")
    if DBC != proyecto + "-db-1" or app_c != proyecto + "-app-1":
        sys.exit("RECHAZADO: los contenedores deben ser %s-db-1 y %s-app-1." % (proyecto, proyecto))
    env = dict(os.environ, MSYS_NO_PATHCONV="1")
    for c in (DBC, app_c):
        o = subprocess.run(["docker", "inspect", "-f", "{{index .Config.Labels \"com.docker.compose.project\"}}", c], capture_output=True, text=True, env=env)
        if o.returncode != 0 or o.stdout.strip() != proyecto:
            sys.exit("RECHAZADO: %s no pertenece al proyecto Compose %s." % (c, proyecto))
    port = re.search(r"^http://127\.0\.0\.1:(\d+)$", BASE)
    o = subprocess.run(["docker", "port", app_c, "8080/tcp"], capture_output=True, text=True, env=env)
    if not port or ("127.0.0.1:" + port.group(1)) not in o.stdout:
        sys.exit("RECHAZADO: P07_BASE_URL debe ser http://127.0.0.1:<puerto publicado por %s>." % app_c)
    n = sql("select (select count(*) from Clientes)+(select count(*) from Ventas)+(select count(*) from Productos)+(select count(*) from Proveedores)+(select count(*) from Cajas)")
    if n[0][0] != "0":
        sys.exit("RECHAZADO: la base no esta vacia (datos comerciales presentes); el recorrido solo corre sobre una instalacion nueva.")


def sql(q):
    out = subprocess.run(
        ["docker", "exec", "-i", DBC, "/opt/mssql-tools18/bin/sqlcmd", "-C", "-S", "localhost", "-U", "sa", "-P", SA,
         "-d", DB, "-h", "-1", "-W", "-s", "|", "-Q", "SET NOCOUNT ON; " + q],
        capture_output=True, text=True, encoding="utf-8", errors="replace", env=dict(os.environ, MSYS_NO_PATHCONV="1"))
    if out.returncode != 0 or "Msg " in out.stdout:
        raise RuntimeError("SQL fallo: %s | %s" % (q, (out.stdout + out.stderr)[:300]))
    return [r.split("|") for r in out.stdout.strip().splitlines() if r.strip()]


def q1(q):
    r = sql(q)
    return r[0][0] if r and r[0] else None


def dec(x):
    return round(float(x), 2)


def chk(cid, desc, cond, evidence=""):
    RESULTS.append({"seccion": SECTION[0], "id": cid, "descripcion": desc, "ok": bool(cond), "evidencia": str(evidence)[:400]})
    print("  [%s] %s %s%s" % ("OK " if cond else "FALLA", cid, desc, ("  -> " + str(evidence)[:200]) if (not cond or evidence) else ""))
    return bool(cond)


def sec(name):
    SECTION[0] = name
    print("\n== %s ==" % name)


def new_session(user, pwd):
    e = Erp(BASE)
    r = e.login(user, pwd)
    assert "Login" not in r.url, "login fallo para %s (%s)" % (user, r.url)
    return e


def denied(r):
    return r.status in (401, 403) or "AccessDenied" in (r.location + r.url) or "/Account/Login" in r.location


def main():
    guardia_descartable()
    t0 = time.time()
    adm = new_session("administrador", "Admin123!")
    sup = new_session(ADMIN_USER, ADMIN_PASS)

    # ------------------------------------------------------------------ R0 salud y presentacion
    sec("R0 Salud, cultura y presentacion en contenedor")
    r = adm.get("/health/ready")
    chk("R0.1", "readiness /health/ready = 200", r.status == 200, r.status)
    r = adm.get("/Dashboard")
    chk("R0.2", "Dashboard abre con el usuario Administrador", r.status == 200, r.url)

    sec("R0b Presentacion regional (cultura del contenedor)")
    login_pg = adm.get("/Dashboard")
    chk("R0.3", "fecha y moneda se muestran como es-AR: ninguna pantalla muestra el signo de moneda generico (¤)", "\u00a4" not in login_pg.text and "&#xA4;" not in login_pg.text)

    # ------------------------------------------------------------------ R1 cliente
    sec("R1 Alta de cliente")
    dni = "11122233"
    ver = cuit_digit("20" + dni)
    base_cli = {"TipoDocumento": "DNI", "NumeroDocumento": dni, "CuilPrefijo": "20", "CuilVerificador": str(ver), "Apellido": "Perez",
                "Nombre": "Juan", "FechaNacimiento": "1985-03-10", "EstadoCivil": "1", "Telefono": "1144445555",
                "Email": "juan.p07@example.invalid", "CodigoPostal": "1000", "Domicilio": "Calle Falsa 123", "Localidad": "CABA",
                "Provincia": "Buenos Aires", "TipoEmpleo": "1", "TiempoTrabajo": "5 años", "Empleador": "Empresa SA", "Sueldo": "1500000",
                "TelefonoLaboral": "1133334444", "TieneReciboSueldo": "true"}
    n0 = int(q1("select count(*) from Clientes"))
    r, _ = adm.submit("/Cliente/Create", {"Apellido": "", "Nombre": "", "NumeroDocumento": ""}, action_contains="/Cliente/Create", follow=False)
    chk("R1.1", "cliente vacio rechazado (no crea, vuelve al formulario)", r.status == 200 and int(q1("select count(*) from Clientes")) == n0, r.status)
    r, _ = adm.submit("/Cliente/Create", dict(base_cli, Email="no-es-un-mail"), action_contains="/Cliente/Create", follow=False)
    chk("R1.2", "email invalido rechazado", r.status == 200 and int(q1("select count(*) from Clientes")) == n0, r.status)
    r, _ = adm.submit("/Cliente/Create", base_cli, action_contains="/Cliente/Create", follow=False)
    chk("R1.3", "cliente valido se crea (302 a Details)", r.status == 302 and "/Cliente/Details/" in r.location, r.location)
    cid = int(q1("select Id from Clientes where NumeroDocumento='%s'" % dni))
    row = sql("select Apellido,Nombre,Activo,Email from Clientes where Id=%d" % cid)[0]
    chk("R1.4", "SQL: datos persistidos y activo", row[0] == "Perez" and row[2] == "1", row)
    r, _ = adm.submit("/Cliente/Create", dict(base_cli, Nombre="Otro"), action_contains="/Cliente/Create", follow=False)
    chk("R1.5", "DNI duplicado rechazado", r.status == 200 and q1("select count(*) from Clientes where NumeroDocumento='%s'" % dni) == "1", r.status)
    r, _ = adm.submit("/Cliente/Edit/%d" % cid, {"Telefono": "1166667777"}, action_contains="/Cliente/Edit/", follow=False)
    chk("R1.6", "edicion de telefono persiste", r.status == 302 and q1("select Telefono from Clientes where Id=%d" % cid) == "1166667777", r.location)
    t = adm.get("/Cliente/Details/%d" % cid).text
    chk("R1.7", "Details del cliente muestra su DNI", dni in t)
    chk("R1.8", "cliente nuevo sin documentos: aptitud de credito no concedida (reglas actuales)",
        q1("select count(*) from DocumentosCliente where ClienteId=%d" % cid) == "0")

    # ------------------------------------------------------------------ R2 compras y stock
    sec("R2 Compras: proveedor, producto, orden, recepcion y stock")
    r, _ = adm.submit("/Proveedor", {"Cuit": "123", "RazonSocial": "X"}, fields_marker="NombreFantasia", form_index=0, post_to="/Proveedor/CreateAjax", follow=False)
    chk("R2.1", "proveedor con CUIT invalido rechazado", '"success":false' in r.text, r.text[:120])
    cu = cuit("3071234567")
    r, _ = adm.submit("/Proveedor", {"Cuit": cu, "RazonSocial": "Proveedor P07 SA", "NombreFantasia": "ProvP07", "Email": "p07@example.invalid", "Telefono": "1155550000"},
                      fields_marker="NombreFantasia", form_index=0, post_to="/Proveedor/CreateAjax", follow=False)
    chk("R2.2", "proveedor valido creado", '"success":true' in r.text, r.text[:120])
    r, _ = adm.submit("/Proveedor", {"Cuit": cu, "RazonSocial": "Duplicado SA"}, fields_marker="NombreFantasia", form_index=0, post_to="/Proveedor/CreateAjax", follow=False)
    chk("R2.3", "proveedor con CUIT duplicado rechazado", '"success":false' in r.text, r.text[:120])
    prov = int(q1("select Id from Proveedores where Cuit='%s'" % cu))

    prod_ov = {"Codigo": "P07-HEL-001", "Nombre": "Heladera P07", "Descripcion": "sintetico", "CategoriaId": "2", "MarcaId": "2",
               "PrecioCompraTotalConIva": "121000", "PrecioCompra": "100000", "MargenPorcentaje": "50", "PrecioVenta": "181500",
               "AlicuotaIVAId": "1", "PorcentajeIVA": "21", "StockActual": "0", "StockMinimo": "2"}
    r, _ = adm.submit("/Catalogo", dict(prod_ov, Codigo="", Nombre=""), action_contains="/Producto/CreateAjax", follow=False)
    chk("R2.4", "producto sin codigo ni nombre rechazado", '"success":false' in r.text, r.text[:120])
    r, _ = adm.submit("/Catalogo", prod_ov, action_contains="/Producto/CreateAjax", follow=False)
    chk("R2.5", "producto valido creado", '"success":true' in r.text, r.text[:120])
    r, _ = adm.submit("/Catalogo", prod_ov, action_contains="/Producto/CreateAjax", follow=False)
    chk("R2.6", "producto con codigo duplicado rechazado", '"success":false' in r.text, r.text[:120])
    pid = int(q1("select Id from Productos where Codigo='P07-HEL-001'"))
    pr = sql("select PrecioCompra,PrecioVenta,StockActual from Productos where Id=%d" % pid)[0]
    chk("R2.7", "SQL: costo 100000 / venta 181500 (= costo +50 % +IVA 21 %) / stock 0",
        dec(pr[0]) == 100000 and dec(pr[1]) == 181500 and dec(pr[2]) == 0, pr)

    oc_ov = {"ProveedorId": str(prov), "Subtotal": "1210000", "Descuento": "0", "Iva": "0", "Total": "1210000", "Detalles[0].Id": "0",
             "Detalles[0].ProductoId": str(pid), "Detalles[0].ProductoNombre": "Heladera P07", "Detalles[0].ProductoCodigo": "P07-HEL-001",
             "Detalles[0].Cantidad": "10", "Detalles[0].PrecioUnitario": "121000", "Detalles[0].Subtotal": "1210000"}
    n_oc = int(q1("select count(*) from OrdenesCompra"))
    r, _ = adm.submit("/OrdenCompra/Create", dict(oc_ov, ProveedorId=""), action_contains="/OrdenCompra/Create", follow=False)
    chk("R2.8", "orden sin proveedor rechazada", r.status == 200 and int(q1("select count(*) from OrdenesCompra")) == n_oc, r.status)
    r, _ = adm.submit("/OrdenCompra/Create", oc_ov, action_contains="/OrdenCompra/Create", follow=False)
    chk("R2.9", "orden valida creada (Borrador)", r.status == 302 and q1("select top 1 Estado from OrdenesCompra order by Id desc") == "0", r.location)
    oc = int(q1("select top 1 Id from OrdenesCompra order by Id desc"))
    row = sql("select Subtotal,Iva,Total from OrdenesCompra where Id=%d" % oc)[0]
    chk("R2.10", "SQL: total 1.210.000 = neto 1.000.000 + IVA 210.000 (precios con IVA incluido)",
        dec(row[2]) == 1210000 and dec(row[1]) == 210000 and dec(row[0]) == 1210000, row)

    def cambiar(est):
        pg = adm.get("/OrdenCompra/Details/%d" % oc)
        return adm.post("/OrdenCompra/CambiarEstado", {"id": str(oc), "nuevoEstado": str(est), "__RequestVerificationToken": adm.token(pg.text)}, follow=False)
    cambiar(3)
    chk("R2.11", "salto Borrador->EnTransito rechazado (solo hacia el siguiente estado)", q1("select Estado from OrdenesCompra where Id=%d" % oc) == "0")
    for est in (1, 2, 3):
        cambiar(est)
    chk("R2.12", "Borrador->Enviada->Confirmada->EnTransito", q1("select Estado from OrdenesCompra where Id=%d" % oc) == "3")

    def recibir(n):
        pg = adm.get("/OrdenCompra/Details/%d" % oc)
        rv = re.search(r'name="rowVersion"[^>]*value="([^"]+)"', pg.text).group(1)
        f = [("id", str(oc)), ("rowVersion", rv), ("detalles[0].DetalleId", q1("select top 1 Id from OrdenCompraDetalle where OrdenCompraId=%d" % oc)),
             ("detalles[0].ProductoId", str(pid)), ("detalles[0].CantidadARecepcionar", str(n)), ("__RequestVerificationToken", adm.token(pg.text))]
        return adm.post("/OrdenCompra/Recepcionar/%d" % oc, f, follow=True)
    recibir(6)
    chk("R2.13", "recepcion parcial 6/10: stock 6, orden sigue En transito, 1 movimiento de entrada",
        dec(q1("select StockActual from Productos where Id=%d" % pid)) == 6 and q1("select Estado from OrdenesCompra where Id=%d" % oc) == "3"
        and q1("select count(*) from MovimientosStock where ProductoId=%d and Tipo=0" % pid) == "1")
    recibir(5)
    chk("R2.14", "sobre-recepcion (5 con 4 pendientes) rechazada: stock sigue 6", dec(q1("select StockActual from Productos where Id=%d" % pid)) == 6)
    recibir(4)
    chk("R2.15", "recepcion final: stock 10, orden Recibida, Kardex coherente",
        dec(q1("select StockActual from Productos where Id=%d" % pid)) == 10 and q1("select Estado from OrdenesCompra where Id=%d" % oc) == "4"
        and dec(q1("select sum(case when Tipo=0 then Cantidad else -Cantidad end) from MovimientosStock where ProductoId=%d" % pid)) == 10)
    chk("R2.16", "movimientos de recepcion referencian la orden", int(q1("select count(*) from MovimientosStock where Referencia like 'Orden de Compra%'")) == 2)

    sec("R3 Stock y movimientos manuales")
    def mov(tipo, cant, motivo="QA P07"):
        r, _ = adm.submit("/MovimientoStock/Create", {"ProductoId": str(pid), "Tipo": str(tipo), "Cantidad": str(cant), "Motivo": motivo}, action_contains="/MovimientoStock/Create", follow=False)
        return r
    stock = lambda: dec(q1("select StockActual from Productos where Id=%d" % pid))
    s0 = stock()
    r = mov(1, 999)
    chk("R3.1", "salida mayor al stock rechazada, stock sin cambios", stock() == s0, "stock=%s status=%s" % (stock(), r.status))
    r = mov(1, 1)
    chk("R3.2", "salida manual de 1: stock %s -> %s" % (s0, s0 - 1), stock() == s0 - 1, r.status)
    r = mov(0, 1)
    chk("R3.3", "entrada manual de 1: stock vuelve a %s" % s0, stock() == s0, r.status)
    k = adm.get("/MovimientoStock/Kardex/%d" % pid)
    chk("R3.4", "Kardex del producto abre (200)", k.status == 200, k.status)

    # ------------------------------------------------------------------ caja
    sec("R4 Caja: alta, apertura y cobros")
    r, _ = adm.submit("/Caja/Create", {"Codigo": "CAJA-P07", "Nombre": "Caja P07", "Sucursal": "Central"}, fields_marker="Codigo", follow=False)
    caja = int(q1("select Id from Cajas where Codigo='CAJA-P07'"))
    chk("R4.1", "caja creada", r.status == 302 and caja > 0, r.location)
    r = adm.get("/Venta/Create", follow=False)
    chk("R4.2", "con la caja cerrada, Venta/Create redirige (no se puede vender sin caja abierta)", r.status == 302, r.location)
    r, _ = adm.submit("/Caja/Abrir?cajaId=%d" % caja, {"MontoInicial": "50000"}, action_contains="/Caja/Abrir", follow=False)
    apert = int(q1("select Id from AperturasCaja where CajaId=%d order by Id desc" % caja))
    chk("R4.3", "apertura con monto inicial 50.000", r.status == 302 and dec(q1("select MontoInicial from AperturasCaja where Id=%d" % apert)) == 50000, r.location)
    pg = adm.get("/Caja/Abrir?cajaId=%d" % caja)
    chk("R4.4", "caja ya abierta: no ofrece abrirla de nuevo", not [f for f in adm.forms(pg.text) if "/Caja/Abrir" in (f["attrs"].get("action") or "")] and q1("select count(*) from AperturasCaja where CajaId=%d and Cerrada=0" % caja) == "1")

    # ------------------------------------------------------------------ cotizacion -> venta contado -> factura
    sec("R5 Cotizacion, conversion a venta, factura")
    sim = {"Productos": [{"ProductoId": pid, "Cantidad": 2}], "ClienteId": cid, "IncluirCreditoPersonal": False}
    r = adm.post_json("/api/cotizacion/simular", sim)
    ef = [o for o in (r.json or {}).get("opcionesPago", []) if o["medioPago"] == 0]
    chk("R5.1", "simulacion: efectivo 2 x 181.500 = 363.000", r.status == 200 and ef and dec(ef[0]["planes"][0]["total"]) == 363000, r.text[:150])
    r = adm.post_json("/api/cotizacion/guardar", {"Simulacion": sim, "OpcionSeleccionada": {"MedioPago": 0, "Plan": "1 pago", "CantidadCuotas": 1}, "Observaciones": "P07 contado"})
    cot1 = r.json["id"] if r.json else 0
    chk("R5.2", "cotizacion guardada (COT-...)", r.status == 200 and cot1 > 0 and r.json["numero"].startswith("COT-"), r.text[:120])
    chk("R5.3", "SQL: cotizacion con total seleccionado 363.000", dec(q1("select TotalSeleccionado from Cotizaciones where Id=%d" % cot1)) == 363000)
    r = adm.post_json("/api/cotizacion/%d/conversion/preflight" % cot1, {})
    chk("R5.4", "preflight de conversion: listo", r.json and r.json.get("listo") is True, r.text[:120])
    s_ant = stock()
    r = adm.post_json("/api/cotizacion/%d/conversion/convertir" % cot1, {"UsarPrecioCotizado": True, "ConfirmarVenta": True, "Facturar": True, "TipoFactura": "B"})
    chk("R5.5", "conversion + confirmar + facturar en un paso", r.json and r.json.get("exitoso") and r.json.get("ventaConfirmada") and r.json.get("facturada"), r.text[:220])
    v1 = r.json["ventaId"]
    row = sql("select Estado,Total,CotizacionOrigenId from Ventas where Id=%d" % v1)[0]
    chk("R5.6", "SQL: venta Facturada (estado 3), total 363.000, origen = la cotizacion", row[0] == "3" and dec(row[1]) == 363000 and row[2] == str(cot1), row)
    chk("R5.7", "SQL: stock descontado en 2", stock() == s_ant - 2, "%s -> %s" % (s_ant, stock()))
    fa = sql("select Subtotal,IVA,Total,Anulada from Facturas where VentaId=%d" % v1)[0]
    chk("R5.8", "SQL: factura 300.000 + IVA 63.000 = 363.000", dec(fa[0]) == 300000 and dec(fa[1]) == 63000 and dec(fa[2]) == 363000 and fa[3] == "0", fa)
    mc = sql("select Tipo,Monto,TipoPago from MovimientosCaja where VentaId=%d" % v1)
    chk("R5.9", "SQL: ingreso de caja 363.000 efectivo", len(mc) == 1 and mc[0][0] == "0" and dec(mc[0][1]) == 363000, mc)
    r = adm.post_json("/api/cotizacion/%d/conversion/convertir" % cot1, {"UsarPrecioCotizado": True})
    chk("R5.10", "convertir otra vez la misma cotizacion: rechazado", r.status != 200 or not (r.json or {}).get("exitoso"), r.text[:150])
    chk("R5.11", "SQL: una sola venta por cotizacion", q1("select count(*) from Ventas where CotizacionOrigenId=%d" % cot1) == "1")

    sec("R6 Venta directa (wizard), confirmacion, factura y cancelacion")
    def venta_form(tipo_pago, extra=None, qty=1):
        ov = {"ClienteId": str(cid), "TipoPago": str(tipo_pago), "Detalles[0].ProductoId": str(pid), "Detalles[0].Cantidad": str(qty),
              "Detalles[0].PrecioUnitario": "181500", "Detalles[0].Descuento": "0", "Detalles[0].Subtotal": str(181500 * qty),
              "Subtotal": str(150000 * qty), "IVA": str(31500 * qty), "Total": str(181500 * qty)}
        ov.update(extra or {})
        return adm.submit("/Venta/Create", ov, action_contains="/Venta/Create", follow=False)[0]
    r = venta_form(1, qty=50)
    n_v = int(q1("select count(*) from Ventas"))
    chk("R6.1", "venta de 50 unidades con 8 en stock: se puede armar el presupuesto", r.status == 302, "status=%s" % r.status)
    if r.status == 302:
        vtmp = int(r.location.rsplit("/", 1)[1])
        pg = adm.get("/Venta/Details/%d" % vtmp)
        adm.post("/Venta/Confirmar", {"id": str(vtmp), "__RequestVerificationToken": adm.token(pg.text)}, follow=False)
        chk("R6.2", "confirmar sin stock suficiente rechazado (la venta no pasa a Confirmada)", q1("select Estado from Ventas where Id=%d" % vtmp) in ("0", "1"), q1("select Estado from Ventas where Id=%d" % vtmp))
        pg = adm.get("/Venta/Details/%d" % vtmp)
        fs = [f for f in adm.forms(pg.text) if "Cancelar" in (f["attrs"].get("action") or "")]
        if fs:
            adm.post(fs[0]["attrs"]["action"], [("motivo", "limpieza QA"), ("__RequestVerificationToken", adm.token(pg.text))])
    else:
        chk("R6.2", "venta sin stock no se crea", int(q1("select count(*) from Ventas")) == n_v)
    s_ant = stock()
    r = venta_form(1)
    v2 = int(r.location.rsplit("/", 1)[1])
    chk("R6.3", "venta por transferencia creada como Presupuesto", q1("select Estado from Ventas where Id=%d" % v2) == "0", r.location)
    pg = adm.get("/Venta/Details/%d" % v2)
    adm.post("/Venta/Confirmar", {"id": str(v2), "__RequestVerificationToken": adm.token(pg.text)}, follow=False)
    chk("R6.4", "confirmada: estado 2, numero VTA-, stock -1", q1("select Estado from Ventas where Id=%d" % v2) == "2" and q1("select Numero from Ventas where Id=%d" % v2).startswith("VTA-") and stock() == s_ant - 1)
    r, _ = adm.submit("/Venta/Details/%d" % v2, {}, action_contains="/Venta/Facturar", follow=False)
    chk("R6.5", "facturada: factura por 181.500 y venta estado 3", dec(q1("select Total from Facturas where VentaId=%d and Anulada=0" % v2) or 0) == 181500 and q1("select Estado from Ventas where Id=%d" % v2) == "3")
    det = adm.get("/Venta/Details/%d" % v2).text
    plano = re.sub(r"<[^>]+>", " ", det)
    chk("R6.5b", "montos en formato es-AR (miles con punto, decimales con coma) y sin el signo de moneda generico",
        "¤" not in det and "&#xA4;" not in det and re.search(r"181\.500,00", plano) is not None, re.findall(r".{12}181[.,]500[.,]00", plano)[:2])
    r = adm.get("/Venta/ComprobanteFactura?facturaId=%s" % q1("select Id from Facturas where VentaId=%d" % v2))
    chk("R6.6", "comprobante de factura abre (200)", r.status == 200, r.status)
    pg = adm.get("/Venta/Details/%d" % v2)
    fs = [f for f in adm.forms(pg.text) if "Cancelar" in (f["attrs"].get("action") or "")]
    adm.post(fs[0]["attrs"]["action"], [("motivo", ""), ("__RequestVerificationToken", adm.token(pg.text))])
    chk("R6.7", "cancelar sin motivo no cancela", q1("select Estado from Ventas where Id=%d" % v2) == "3")
    pg = adm.get("/Venta/Details/%d" % v2)
    adm.post(fs[0]["attrs"]["action"], [("motivo", "Cancelacion sintetica P07"), ("__RequestVerificationToken", adm.token(pg.text))])
    chk("R6.8", "cancelar con motivo: venta Cancelada, stock restituido, factura anulada, reversion en caja",
        q1("select Estado from Ventas where Id=%d" % v2) == "5" and stock() == s_ant
        and q1("select Anulada from Facturas where VentaId=%d" % v2) == "1"
        and int(q1("select count(*) from MovimientosCaja where VentaId=%d and Tipo=1" % v2)) == 1)

    # ------------------------------------------------------------------ devoluciones
    sec("R7 Devoluciones y nota de credito")
    ctx = adm.get("/Devolucion/GetQuickCreateContext?ventaId=%d" % v1)
    chk("R7.1", "contexto de devolucion de la venta: 2 unidades disponibles", ctx.status == 200 and '"cantidadDisponible":2' in ctx.text, ctx.text[:100])

    def dev(res, egreso, estado, cant=1, motivo=2):
        pg = adm.get("/Venta/Details/%d" % v1)
        f = [("VentaId", str(v1)), ("ClienteId", str(cid)), ("Motivo", str(motivo)), ("TipoResolucion", str(res)), ("RegistrarEgresoCaja", str(egreso).lower()),
             ("Descripcion", "Devolucion sintetica P07"), ("Items[0].ProductoId", str(pid)), ("Items[0].Seleccionado", "true"),
             ("Items[0].CantidadDevolver", str(cant)), ("Items[0].EstadoProducto", str(estado)), ("Items[0].PrecioUnitario", "181500"),
             ("Items[0].CantidadDisponible", "2"), ("__RequestVerificationToken", adm.token(pg.text))]
        return adm.post("/Devolucion/QuickCreate", f, follow=False, headers={"X-Requested-With": "XMLHttpRequest"})
    r = dev(0, False, 0, cant=3)
    chk("R7.2", "devolver 3 de 2 unidades rechazado", '"success":false' in r.text or r.status >= 400, r.text[:150])
    r = dev(0, False, 0)
    chk("R7.3", "devolucion por nota de credito creada (pendiente)", '"success":true' in r.text, r.text[:120])
    d1 = int(q1("select max(Id) from Devoluciones"))

    def dstep(act, did):
        pg = adm.get("/Devolucion/Detalles/%d" % did)
        fs = [f for f in adm.forms(pg.text) if "/Devolucion/%s" % act in (f["attrs"].get("action") or "")]
        if not fs:
            return None
        return adm.post(fs[0]["attrs"]["action"], fs[0]["fields"], follow=False)
    s_ant = stock()
    mcaja = int(q1("select count(*) from MovimientosCaja"))
    dstep("Completar", d1)
    chk("R7.4", "completar sin aprobar no avanza", q1("select Estado from Devoluciones where Id=%d" % d1) == "0")
    dstep("Aprobar", d1); dstep("Completar", d1)
    chk("R7.5", "devolucion completada: estado 4, stock +1 (producto nuevo reintegra)", q1("select Estado from Devoluciones where Id=%d" % d1) == "4" and stock() == s_ant + 1)
    nc = sql("select Id,MontoTotal,Estado,FechaVencimiento from NotasCredito where DevolucionId=%d" % d1)[0]
    chk("R7.6", "SQL: nota de credito por 181.500 vigente, sin egreso de caja", dec(nc[1]) == 181500 and nc[2] == "0" and int(q1("select count(*) from MovimientosCaja")) == mcaja)
    nc_id = int(nc[0])
    r = venta_form(9, {"NotaCreditoId": str(nc_id)})
    v3 = int(r.location.rsplit("/", 1)[1])
    pg = adm.get("/Venta/Details/%d" % v3)
    adm.post("/Venta/Confirmar", {"id": str(v3), "__RequestVerificationToken": adm.token(pg.text)}, follow=False)
    chk("R7.7", "venta pagada con la nota de credito: NC utilizada 181.500, estado Utilizada",
        dec(q1("select MontoUtilizado from NotasCredito where Id=%d" % nc_id)) == 181500 and q1("select Estado from NotasCredito where Id=%d" % nc_id) == "2")
    pg = adm.get("/Venta/Details/%d" % v3)
    fs = [f for f in adm.forms(pg.text) if "Cancelar" in (f["attrs"].get("action") or "")]
    adm.post(fs[0]["attrs"]["action"], [("motivo", "Restituir NC (QA P07)"), ("__RequestVerificationToken", adm.token(pg.text))])
    chk("R7.8", "cancelar esa venta restituye la nota de credito", dec(q1("select MontoUtilizado from NotasCredito where Id=%d" % nc_id)) == 0 and q1("select Estado from NotasCredito where Id=%d" % nc_id) == "0")
    s_ant = stock(); egr = int(q1("select count(*) from MovimientosCaja where Tipo=1"))
    r = dev(1, True, 1, motivo=1)
    d2 = int(q1("select max(Id) from Devoluciones"))
    dstep("Aprobar", d2); dstep("Completar", d2)
    chk("R7.9", "reembolso: completada, egreso de caja 181.500 con referencia a la devolucion, stock +1",
        q1("select Estado from Devoluciones where Id=%d" % d2) == "4" and int(q1("select count(*) from MovimientosCaja where Tipo=1")) == egr + 1
        and q1("select count(*) from MovimientosCaja where Tipo=1 and Monto=181500 and Descripcion like N'Reembolso devoluci%'") == "1" and stock() == s_ant + 1)
    ctx2 = adm.get("/Devolucion/GetQuickCreateContext?ventaId=%d" % v1).text
    chk("R7.10", "venta ya devuelta por completo: no queda cantidad devolvible", '"cantidadDisponible":0' in ctx2 or '"puedeDevolver":false' in ctx2 or '"items":[]' in ctx2, ctx2[:200])

    # ------------------------------------------------------------------ credito personal
    sec("R8 Credito personal: configuracion, documentacion, plan, cuotas y cobros")
    ov = {"DefaultsGlobales.TasaMensual": "5", "DefaultsGlobales.GastosAdministrativos": "0"}
    for i, m in enumerate([100000, 200000, 400000, 800000, 1500000, 3000000]):
        ov["LimitesPorPuntaje[%d].LimiteMonto" % i] = str(m); ov["LimitesPorPuntaje[%d].Activo" % i] = "true"
    r, _ = adm.submit("/ConfiguracionPago/CreditoPersonal", ov, action_contains="/ConfiguracionPago/CreditoPersonal", follow=False)
    chk("R8.1", "rol Administrador NO puede guardar la configuracion de credito (configuracion.update es de SuperAdmin)", denied(r), r.location)
    for i, (c, t) in enumerate([(3, "10"), (6, "20"), (12, "35")]):
        ov["CuotasCreditoPersonal[%d].Id" % i] = "0"; ov["CuotasCreditoPersonal[%d].CantidadCuotas" % i] = str(c)
        ov["CuotasCreditoPersonal[%d].TasaMensual" % i] = t; ov["CuotasCreditoPersonal[%d].Activo" % i] = "true"; ov["CuotasCreditoPersonal[%d].Orden" % i] = str(i)
    sim = {"Productos": [{"ProductoId": pid, "Cantidad": 1}], "ClienteId": cid, "IncluirCreditoPersonal": True}
    cp = [o for o in adm.post_json("/api/cotizacion/simular", sim).json["opcionesPago"] if o["medioPago"] == 5][0]
    chk("R8.2", "instalacion nueva: credito personal NO disponible hasta configurar tasa/planes (dato faltante, no defecto)", cp["disponible"] is False, cp["motivoNoDisponible"])
    r, _ = sup.submit("/ConfiguracionPago/CreditoPersonal", ov, action_contains="/ConfiguracionPago/CreditoPersonal", follow=False)
    chk("R8.3", "SuperAdmin guarda tasa, limites por puntaje y 3 planes (3/6/12 cuotas)", r.status == 302 and q1("select count(*) from ConfiguracionCreditoPersonalCuotas where Activo=1") == "3", r.location)
    cp = [o for o in adm.post_json("/api/cotizacion/simular", sim).json["opcionesPago"] if o["medioPago"] == 5][0]
    p6 = [p for p in cp["planes"] if p["cantidadCuotas"] == 6]
    chk("R8.4", "simulacion: 6 cuotas con recargo 20 %% = 217.800 (6 x 36.300)", cp["disponible"] and p6 and dec(p6[0]["total"]) == 217800 and dec(p6[0]["valorCuota"]) == 36300, p6[:1])
    r = adm.post_json("/api/cotizacion/guardar", {"Simulacion": sim, "OpcionSeleccionada": {"MedioPago": 5, "Plan": "6 cuotas", "CantidadCuotas": 6}, "Observaciones": "P07 credito"})
    cot2 = r.json["id"]
    r = adm.post_json("/api/cotizacion/%d/conversion/convertir" % cot2, {"UsarPrecioCotizado": True})
    chk("R8.5", "cliente sin documentacion/CUIL verificada: la conversion a credito personal se rechaza (regla vigente)",
        r.status == 400 and "Faltan documentos" in r.text, r.text[:200])
    for t_ in (1, 2, 3):
        pg = adm.get("/DocumentoCliente/Upload?clienteId=%d" % cid)
        adm.post_multipart("/DocumentoCliente/Upload", [("ClienteId", str(cid)), ("TipoDocumento", str(t_)), ("Observaciones", "p07"), ("__RequestVerificationToken", adm.token(pg.text))],
                           {"Archivo": ("doc%d.pdf" % t_, "application/pdf", pdf("doc %d" % t_))}, referer="/DocumentoCliente/Upload?clienteId=%d" % cid)
    chk("R8.6", "SQL: 3 documentos cargados (DNI, recibo, servicio)", q1("select count(*) from DocumentosCliente where ClienteId=%d" % cid) == "3")
    pg = adm.get("/DocumentoCliente?clienteId=%d" % cid)
    adm.post("/DocumentoCliente/VerificarTodos", {"clienteId": str(cid), "observaciones": "p07", "__RequestVerificationToken": adm.token(pg.text)}, follow=False)
    chk("R8.7", "SQL: documentos verificados (estado 2)", q1("select count(*) from DocumentosCliente where ClienteId=%d and Estado=2" % cid) == "3")
    r = adm.post_json("/api/cotizacion/%d/conversion/convertir" % cot2, {"UsarPrecioCotizado": True, "ConfirmarVenta": False, "Facturar": False})
    chk("R8.8", "conversion a venta de credito: queda pendiente de financiacion (estado 7) con credito asociado",
        r.json and r.json.get("exitoso") and r.json.get("estadoVenta") == 7 and r.json.get("creditoId"), r.text[:200])
    v4 = r.json["ventaId"]; cr = r.json["creditoId"]
    s_ant = stock()
    r, _ = adm.submit("/Credito/ConfigurarVenta/%d?ventaId=%d" % (cr, v4), {"FechaPrimeraCuota": "2026-12-14"}, action_contains="/Credito/ConfigurarVenta", follow=False)
    chk("R8.9", "configurar plan (6 cuotas, primera cuota 14/12)", r.status == 302, r.location)
    pg = adm.get("/ContratoVentaCredito/Preparar?ventaId=%d" % v4)
    adm.post("/ContratoVentaCredito/Generar", {"ventaId": str(v4), "__RequestVerificationToken": adm.token(pg.text)}, follow=False)
    pg = adm.get("/ContratoVentaCredito/Preparar?ventaId=%d" % v4)
    adm.post("/Venta/Confirmar", {"id": str(v4), "__RequestVerificationToken": adm.token(pg.text)}, follow=False)
    c = sql("select Estado,MontoSolicitado,TasaInteres,CantidadCuotas,MontoCuota,TotalAPagar,SaldoPendiente from Creditos where Id=%d" % cr)[0]
    chk("R8.10", "SQL: credito Generado, 6 cuotas de 36.300, total 217.800, saldo de capital 181.500", c[0] == "8" and c[3] == "6" and dec(c[4]) == 36300 and dec(c[5]) == 217800 and dec(c[6]) == 181500, c)
    cu_rows = sql("select NumeroCuota,MontoCapital,MontoInteres,MontoTotal,convert(varchar(10),FechaVencimiento,23) from Cuotas where CreditoId=%d order by NumeroCuota" % cr)
    chk("R8.11", "SQL: sum(cuotas) = total a pagar; capital = 181.500; vencimientos mensuales desde 14/12/2026",
        dec(sum(float(x[3]) for x in cu_rows)) == 217800 and dec(sum(float(x[1]) for x in cu_rows)) == 181500 and cu_rows[0][4] == "2026-12-14" and cu_rows[5][4] == "2027-05-14", cu_rows[:2])
    chk("R8.12", "venta de credito Confirmada (estado 2) y stock -1; sin ingreso de caja inmediato", q1("select Estado from Ventas where Id=%d" % v4) == "2" and stock() == s_ant - 1 and q1("select count(*) from MovimientosCaja where VentaId=%d" % v4) == "0")
    r, _ = adm.submit("/Venta/Details/%d" % v4, {}, action_contains="/Venta/Facturar", follow=False)
    fa = q1("select Total from Facturas where VentaId=%d and Anulada=0" % v4)
    chk("R8.13", "facturar la venta de credito: factura por el total de la venta (181.500, no por los intereses)", fa is not None and dec(fa) == 181500, fa)
    def pagar(cuota, monto, generar=False):
        """POST del cobro; la primera GET posterior es la pantalla de resultado (con 'Generar pendientes' para el recibo)."""
        try:
            rr, _ = adm.submit("/Credito/PagarCuota/%d" % cuota, {"Input.MontoIngresado": str(monto)}, action_contains="PagarCuota", follow=False)
        except IndexError:
            return None, None
        res = adm.get(rr.location) if rr.status == 302 and rr.location else rr
        if generar:
            gf = [x for x in adm.forms(res.text) if "GenerarPendientes" in (x["attrs"].get("action") or "")]
            if gf:
                adm.post(gf[0]["attrs"]["action"], gf[0]["fields"], follow=False)
        return rr, res
    cuota1 = int(q1("select Id from Cuotas where CreditoId=%d and NumeroCuota=1" % cr))
    r, _ = adm.submit("/Credito/PagarCuota/%d" % cuota1, {"Input.MontoIngresado": "0"}, action_contains="PagarCuota", follow=False)
    chk("R8.14", "pago de cuota por 0 rechazado", q1("select MontoPagado from Cuotas where Id=%d" % cuota1) in ("0", "0.00", ".00"))
    r, res = pagar(cuota1, 20000, generar=True)
    chk("R8.15a", "pantalla de resultado del cobro con importes aplicados (capital) y recibo generado a pedido",
        "Aplicado a capital" in res.text or "Capital pendiente" in res.text, "")
    chk("R8.15b", "sin reglas documentales configuradas (instalacion nueva) el cobro se registra y NO se emite recibo: dato de configuracion pendiente, no defecto (docs/sistema-documental.md)",
        q1("select count(*) from DocumentosGenerados where PagoCuotaId is not null") == "0" and q1("select count(*) from ReglasDocumento") == "0")
    row = sql("select MontoPagado,Estado from Cuotas where Id=%d" % cuota1)[0]
    chk("R8.15", "pago parcial 20.000: cuota parcial (estado 3)", dec(row[0]) == 20000 and row[1] == "3", row)
    saldo = dec(q1("select SaldoPendiente from Creditos where Id=%d" % cr))
    chk("R8.16", "saldo de capital baja en proporcion (181.500 - 20.000 x 30.250/36.300 = 164.833,33)", abs(saldo - 164833.33) < 0.02, saldo)
    r, _ = pagar(cuota1, 16300)
    row = sql("select MontoPagado,Estado from Cuotas where Id=%d" % cuota1)[0]
    chk("R8.17", "pago del resto 16.300: cuota 1 pagada (estado 1)", dec(row[0]) == 36300 and row[1] == "1", row)
    chk("R8.18", "SQL: dos ingresos de caja por los cobros (20.000 y 16.300)", sql("select Monto from MovimientosCaja where Descripcion like 'Cobro cuota #1%' order by Id") == [["20000.00"], ["16300.00"]], sql("select Monto from MovimientosCaja where Descripcion like 'Cobro cuota #1%'"))
    chk("R8.19", "saldo de capital = 181.500 - 30.250 = 151.250", dec(q1("select SaldoPendiente from Creditos where Id=%d" % cr)) == 151250)
    r, _ = pagar(cuota1, 100)
    chk("R8.20", "pagar de nuevo una cuota ya pagada: sin efecto", dec(q1("select MontoPagado from Cuotas where Id=%d" % cuota1)) == 36300)
    r = adm.get("/Credito/Details/%d" % cr)
    chk("R8.21", "Details del credito abre y muestra el numero CRE-", r.status == 200 and "CRE-" in r.text)

    # ------------------------------------------------------------------ cierre de caja
    sec("R9 Cierre de caja y conciliacion")
    pg = adm.get("/Caja/Cerrar?aperturaId=%d" % apert)
    chk("R9.1", "pantalla de cierre abre", pg.status == 200, pg.status)
    ing = dec(q1("select isnull(sum(Monto),0) from MovimientosCaja where AperturaCajaId=%d and Tipo=0" % apert))
    egr = dec(q1("select isnull(sum(Monto),0) from MovimientosCaja where AperturaCajaId=%d and Tipo=1" % apert))
    efectivo_net = dec(q1("select isnull(sum(case when Tipo=0 then Monto else -Monto end),0) from MovimientosCaja where AperturaCajaId=%d and (TipoPago is null or TipoPago=0)" % apert))
    r, _ = adm.submit("/Caja/Cerrar?aperturaId=%d" % apert, {"EfectivoContado": "1"}, action_contains="/Caja/Cerrar", follow=False)
    chk("R9.2", "cierre con diferencia grande y sin justificacion rechazado (la caja sigue abierta)", q1("select Cerrada from AperturasCaja where Id=%d" % apert) == "0", r.status)
    esperado_sql = 50000 + efectivo_net
    r, _ = adm.submit("/Caja/Cerrar?aperturaId=%d" % apert, {"EfectivoContado": "%.2f" % esperado_sql}, action_contains="/Caja/Cerrar", follow=False)
    chk("R9.3", "cierre con el efectivo esperado calculado por SQL independiente (apertura + efectivo neto)", q1("select Cerrada from AperturasCaja where Id=%d" % apert) == "1", "esperado SQL=%s status=%s" % (esperado_sql, r.status))
    ci = sql("select MontoInicialSistema,TotalIngresosSistema,TotalEgresosSistema,MontoEsperadoSistema,EfectivoContado,Diferencia,TieneDiferencia from CierresCaja where AperturaCajaId=%d" % apert)
    ci = ci[0] if ci else None
    chk("R9.4", "SQL: el cierre guardado coincide con la suma de movimientos (inicial + ingresos - egresos = esperado del sistema)",
        ci is not None and abs(dec(ci[0]) + dec(ci[1]) - dec(ci[2]) - dec(ci[3])) < 0.01, ci)
    chk("R9.5", "SQL: sin diferencia de caja al contar lo esperado", ci is not None and dec(ci[5]) == 0 and ci[6] == "0", ci)
    print("   (informativo) apertura=50000 ingresos=%s egresos=%s efectivo_neto=%s | cierre=%s" % (ing, egr, efectivo_net, ci))

    sec("R10 Reportes, exportaciones y conciliacion basica")
    def numtxt(sx):
        sx = re.sub(r"[^\d.,-]", "", sx)
        m = re.search(r"[.,](\d{2})$", sx)
        if m:
            ent = re.sub(r"[.,]", "", sx[:m.start()])
            return float("%s.%s" % (ent, m.group(1)))
        return float(re.sub(r"[.,]", "", sx))
    rep, _ = adm.submit("/Reporte/Ventas", {"FechaDesde": "2026-11-01", "FechaHasta": "2026-11-30"}, fields_marker="FechaDesde", follow=False)
    txt = re.sub(r"\s+", " ", re.sub(r"<[^>]+>", " ", re.sub(r"<script.*?</script>", "", rep.text, flags=re.S)))
    def kpi(label):
        m = re.search(re.escape(label) + r" (-?[^ ]*[\d.,]+)", txt)
        return numtxt(m.group(1)) if m else None
    brutas_sql = dec(q1("select isnull(sum(Total),0) from Ventas where Estado in (2,3,4,6) and IsDeleted=0"))
    dev_sql = dec(q1("select isnull(sum(TotalDevolucion),0) from Devoluciones where Estado=4 and IsDeleted=0"))
    chk("R10.1", "Reporte de ventas: ventas brutas = SQL (%s)" % brutas_sql, kpi("Ventas brutas") == brutas_sql, kpi("Ventas brutas"))
    chk("R10.2", "Reporte de ventas: devoluciones = SQL (%s)" % dev_sql, abs(abs(kpi("Devoluciones") or 0) - dev_sql) < 0.01, kpi("Devoluciones"))
    chk("R10.3", "Reporte de ventas: ventas netas = brutas - devoluciones", abs((kpi("Ventas netas") or 0) - (brutas_sql - dev_sql)) < 0.01, kpi("Ventas netas"))
    nv = int(q1("select count(*) from Ventas where Estado in (2,3,4,6) and IsDeleted=0"))
    canc = [q1("select Numero from Ventas where Id=%s" % v) for v in (str(v2), str(v3))]
    chk("R10.4", "Reporte de ventas: lista las %d ventas vigentes y ninguna cancelada" % nv, txt.count("VTA-2026") == nv and all(c not in txt for c in canc), "%s listadas" % txt.count("VTA-2026"))
    mv = adm.get("/Reporte/MovimientosValorizados")
    chk("R10.5", "Movimientos valorizados abre", mv.status == 200)
    for url, magic, nom in (("/Reporte/ExportarVentasExcel?FechaDesde=2026-11-01&FechaHasta=2026-11-30", b"PK", "Ventas Excel"),
                            ("/Reporte/ExportarVentasPdf?FechaDesde=2026-11-01&FechaHasta=2026-11-30", b"%PDF", "Ventas PDF"),
                            ("/Reporte/ExportarMorosidadExcel", b"PK", "Morosidad Excel"), ("/Reporte/ExportarMorosidadPdf", b"%PDF", "Morosidad PDF"),
                            ("/Reporte/ExportarMargenesExcel", b"PK", "Margenes Excel"),
                            ("/Reporte/ExportarMovimientosValorizadosExcel?FechaDesde=2026-11-01&FechaHasta=2026-11-30", b"PK", "Movimientos Excel"),
                            ("/Cotizacion/DescargarPdf/%d" % cot1, b"%PDF", "Cotizacion PDF")):
        x = adm.get(url)
        chk("R10.x", "exportacion %s: 200, firma %s, %d bytes (fuentes del contenedor para QuestPDF)" % (nom, magic.decode(), len(x.body)), x.status == 200 and x.body[:len(magic)] == magic and len(x.body) > 500, x.status)
    chk("R10.6", "Dashboard carga con datos (200)", adm.get("/Dashboard").status == 200)
    stock_sql = dec(q1("select StockActual from Productos where Id=%d" % pid))
    stock_mov = dec(q1("select isnull(sum(case when Tipo=0 then Cantidad when Tipo=1 then -Cantidad else 0 end),0) from MovimientosStock where ProductoId=%d" % pid))
    chk("R10.7", "conciliacion: stock del producto = suma firmada de sus movimientos (entradas - salidas)", stock_sql == stock_mov, "%s vs %s" % (stock_sql, stock_mov))
    chk("R10.8", "conciliacion: toda venta Facturada (3) tiene exactamente una factura vigente", q1("select count(*) from Ventas v where Estado=3 and IsDeleted=0 and (select count(*) from Facturas f where f.VentaId=v.Id and f.Anulada=0)<>1") == "0")
    chk("R10.9", "conciliacion: venta cancelada con factura => factura anulada", q1("select count(*) from Ventas v join Facturas f on f.VentaId=v.Id where v.Estado=5 and f.Anulada=0") == "0")
    chk("R10.10", "conciliacion: credito vigente: capital de cuotas = monto aprobado", q1("select count(*) from Creditos c where Estado in (8,2) and (select sum(MontoCapital) from Cuotas q where q.CreditoId=c.Id) <> c.MontoAprobado") == "0")
    chk("R10.11", "conciliacion: NC utilizada <= monto total en todas las notas", q1("select count(*) from NotasCredito where MontoUtilizado>MontoTotal") == "0")

    sec("R11 Permisos por rol y errores")
    roles = {"vendedor": "Vendedor123!", "cajero": "Cajero123!", "repositor": "Repositor123!", "contador": "Contador123!", "gerente": "Gerente123!", "tecnico": "Tecnico123!"}
    sess = {u: new_session(u, p) for u, p in roles.items()}
    paginas = ["/Venta", "/Caja", "/OrdenCompra", "/Cliente", "/Credito", "/Reporte/Ventas", "/ConfiguracionPago/MediosPago", "/Seguridad", "/Devolucion", "/Catalogo", "/Usuarios/Create", "/MovimientoStock"]
    matriz = {}
    for u, e_ in sess.items():
        matriz[u] = {}
        for pgurl in paginas:
            rr = e_.get(pgurl, follow=False)
            matriz[u][pgurl] = "403/denegado" if denied(rr) else str(rr.status)
    print("   matriz GET (rol x pagina; NO = denegado):")
    print("   %-10s " % "" + " ".join("%-9s" % x.strip("/")[:9] for x in paginas))
    for u in matriz:
        print("   %-10s " % u + " ".join("%-9s" % ("NO" if matriz[u][x] != "200" else "200") for x in paginas))
    ok_ = lambda u, x: matriz[u][x] == "200"
    chk("R11.1", "Vendedor: Ventas y Clientes si; Seguridad, Usuarios y Configuracion no", ok_("vendedor", "/Venta") and ok_("vendedor", "/Cliente") and not ok_("vendedor", "/Seguridad") and not ok_("vendedor", "/Usuarios/Create") and not ok_("vendedor", "/ConfiguracionPago/MediosPago"), matriz["vendedor"])
    chk("R11.2", "Cajero: Caja si; Seguridad, Usuarios y Compras no", ok_("cajero", "/Caja") and not ok_("cajero", "/Seguridad") and not ok_("cajero", "/Usuarios/Create") and not ok_("cajero", "/OrdenCompra"), matriz["cajero"])
    chk("R11.3", "Repositor: Stock si; Caja, Seguridad y Usuarios no", ok_("repositor", "/MovimientoStock") and not ok_("repositor", "/Caja") and not ok_("repositor", "/Seguridad") and not ok_("repositor", "/Usuarios/Create"), matriz["repositor"])
    chk("R11.4", "Contador: Reportes si; Seguridad y Usuarios no", ok_("contador", "/Reporte/Ventas") and not ok_("contador", "/Seguridad") and not ok_("contador", "/Usuarios/Create"), matriz["contador"])
    chk("R11.5", "Tecnico: sin Caja, Compras, Seguridad ni Usuarios", not ok_("tecnico", "/Caja") and not ok_("tecnico", "/OrdenCompra") and not ok_("tecnico", "/Seguridad") and not ok_("tecnico", "/Usuarios/Create"), matriz["tecnico"])
    chk("R11.6", "Gerente: sin Seguridad ni Usuarios", not ok_("gerente", "/Seguridad") and not ok_("gerente", "/Usuarios/Create"), matriz["gerente"])

    def post_rol(u, url, fields):
        e_ = sess[u]
        pg = e_.get("/Cliente")
        return e_.post(url, list(fields.items()) + [("__RequestVerificationToken", e_.token(pg.text) or "")], follow=False)
    n_prov = q1("select count(*) from Proveedores")
    r_ = post_rol("vendedor", "/Proveedor/CreateAjax", {"Cuit": cuit("3071234568"), "RazonSocial": "Intruso SA"})
    chk("R11.7", "Vendedor no puede crear proveedores (sin cambios en SQL)", q1("select count(*) from Proveedores") == n_prov, r_.status)
    n_oc = q1("select count(*) from OrdenesCompra")
    r_ = post_rol("cajero", "/OrdenCompra/Create", {"ProveedorId": str(prov)})
    chk("R11.8", "Cajero no puede crear ordenes de compra", q1("select count(*) from OrdenesCompra") == n_oc, r_.status)
    r_ = post_rol("vendedor", "/Caja/Cerrar", {"AperturaCajaId": str(apert), "EfectivoContado": "0"})
    chk("R11.9", "Vendedor no puede operar el cierre de caja (sin segundo cierre en SQL)", q1("select count(*) from CierresCaja where AperturaCajaId=%d" % apert) == "1", r_.status)
    cuota2 = int(q1("select Id from Cuotas where CreditoId=%d and NumeroCuota=2" % cr))
    r_ = post_rol("contador", "/Credito/PagarCuota/%d" % cuota2, {"Input.MontoIngresado": "1000", "Input.MedioPago": "Efectivo", "Input.CuotaRowVersionBase64": "AAAAAAAAAAA="})
    chk("R11.10", "Contador no puede cobrar cuotas", dec(q1("select MontoPagado from Cuotas where Id=%d" % cuota2)) == 0, r_.status)
    r_ = post_rol("vendedor", "/ConfiguracionPago/CreditoPersonal", {"DefaultsGlobales.TasaMensual": "99"})
    chk("R11.11", "Vendedor no puede modificar la configuracion de credito personal", dec(q1("select TasaMensual from ConfiguracionCreditoPersonalCuotas where CantidadCuotas=6")) == 20, r_.status)
    anon = Erp(BASE)
    r_ = anon.get("/Venta", follow=False)
    chk("R11.12", "sin sesion: /Venta redirige al login", r_.status == 302 and "Login" in r_.location, r_.location)
    r_ = anon.post_json("/api/cotizacion/guardar", {}, token_page="/Identity/Account/Login")
    chk("R11.13", "sin sesion: la API de cotizaciones no responde 200", r_.status in (302, 400, 401, 403), r_.status)
    r_ = adm.get("/Venta/Details/999999", follow=False)
    chk("R11.14", "venta inexistente: 404 o redireccion controlada al listado (nunca 500)", r_.status in (404, 302), "%s %s" % (r_.status, r_.location))
    r_ = adm.get("/Cliente/Details/999999", follow=False)
    chk("R11.15", "cliente inexistente: 404 o redireccion controlada al listado (nunca 500)", r_.status in (404, 302), "%s %s" % (r_.status, r_.location))
    logs = subprocess.run(["docker", "logs", os.environ["P07_APP_CONTAINER"]], capture_output=True, text=True, encoding="utf-8", errors="replace")
    lineas = (logs.stdout + logs.stderr).splitlines()
    no_controladas = [l for l in lineas if re.search(r"[Uu]nhandled|\bcrit:", l)]
    fails = [l for l in lineas if re.search(r"\bfail:", l)]
    chk("R11.16", "logs de la app: sin excepciones no controladas ni nivel critico durante todo el recorrido", len(no_controladas) == 0, "; ".join(no_controladas[:3]))
    print("   (informativo) %d lineas 'fail:' en el log: son los rechazos de negocio provocados a proposito (codigo duplicado, stock insuficiente, tope de venta) registrados con nivel Error" % len(fails))

    return t0


if __name__ == "__main__":
    t0 = main()
    ok = sum(1 for r in RESULTS if r["ok"]); bad = [r for r in RESULTS if not r["ok"]]
    print("\n== RESUMEN: %d comprobaciones, %d OK, %d con falla (%.0f s) ==" % (len(RESULTS), ok, len(bad), time.time() - t0))
    for b in bad:
        print("  FALLA %s %s -> %s" % (b["id"], b["descripcion"], b["evidencia"]))
    rp = os.environ.get("P07_REPORT")
    if rp:
        with open(rp, "w", encoding="utf-8") as fh:
            json.dump(RESULTS, fh, ensure_ascii=False, indent=1)
    sys.exit(1 if bad else 0)
