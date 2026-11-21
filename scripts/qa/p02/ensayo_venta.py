#!/usr/bin/env python3
"""P-02: conductor HTTP mínimo para el ensayo general de puesta en marcha (scripts/qa/p02/ensayo-puesta-en-marcha.sh).

Opera SOLO contra el stack efímero `tbp-p02ens-<hex>` (guardia estricta) y crea, por la interfaz real, lo necesario para vender
(cliente, producto con stock, caja abierta) y una venta contado facturada vía cotización → conversión. Imprime `VENTA_ID=<n>`.

  ensayo_venta.py preparar          crea cliente, producto, stock y caja abierta (una sola vez por stack)
  ensayo_venta.py vender            crea una venta contado facturada y la imprime
  ensayo_venta.py detalle <id>      imprime DETALLE_STATUS=<http> del detalle de la venta

Entorno: ENS_PROJECT, ENS_BASE (http://<ip-del-contenedor-app>:8080), ENS_DB_CONTAINER, ENS_SA_PASSWORD, ENS_DB_NAME,
         ENS_ADMIN_USER, ENS_ADMIN_PASS.
"""
import os
import re
import subprocess
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "p07"))
from erp_http import Erp  # noqa: E402
from synthetic import cuit_digit  # noqa: E402

PROJECT = os.environ.get("ENS_PROJECT", "")
BASE = os.environ.get("ENS_BASE", "")
DBC = os.environ.get("ENS_DB_CONTAINER", "")
SA = os.environ.get("ENS_SA_PASSWORD", "")
DB = os.environ.get("ENS_DB_NAME", "")
USER = os.environ.get("ENS_ADMIN_USER", "")
PASS = os.environ.get("ENS_ADMIN_PASS", "")


def guardia():
    if not re.fullmatch(r"tbp-p02ens-[0-9a-f]{6}", PROJECT):
        sys.exit("RECHAZADO: ENS_PROJECT debe ser el proyecto efímero tbp-p02ens-<hex>.")
    if DBC != PROJECT + "-db-1" or not re.fullmatch(r"http://\d+\.\d+\.\d+\.\d+:8080", BASE):
        sys.exit("RECHAZADO: contenedor o URL fuera del stack efímero.")
    o = subprocess.run(["docker", "inspect", "-f", '{{index .Config.Labels "com.docker.compose.project"}}', DBC],
                       capture_output=True, text=True, env=dict(os.environ, MSYS_NO_PATHCONV="1"))
    if o.returncode != 0 or o.stdout.strip() != PROJECT:
        sys.exit("RECHAZADO: %s no pertenece al proyecto %s." % (DBC, PROJECT))


def sql(q):
    out = subprocess.run(
        ["docker", "exec", "-i", DBC, "/opt/mssql-tools18/bin/sqlcmd", "-C", "-S", "localhost", "-U", "sa", "-P", SA,
         "-d", DB, "-h", "-1", "-W", "-s", "|", "-Q", "SET NOCOUNT ON; " + q],
        capture_output=True, text=True, encoding="utf-8", errors="replace", env=dict(os.environ, MSYS_NO_PATHCONV="1"))
    if out.returncode != 0 or "Msg " in out.stdout:
        raise RuntimeError("SQL falló: %s | %s" % (q, (out.stdout + out.stderr)[:300]))
    return [r.split("|") for r in out.stdout.strip().splitlines() if r.strip()]


def q1(q):
    r = sql(q)
    return r[0][0] if r and r[0] else None


def sesion():
    e = Erp(BASE)
    r = e.login(USER, PASS)
    assert "Login" not in r.url, "login falló (%s)" % r.url
    return e


def preparar():
    adm = sesion()
    dni = "11122233"
    ver = cuit_digit("20" + dni)
    cli = {"TipoDocumento": "DNI", "NumeroDocumento": dni, "CuilPrefijo": "20", "CuilVerificador": str(ver), "Apellido": "Ensayo",
           "Nombre": "Puesta", "FechaNacimiento": "1985-03-10", "EstadoCivil": "1", "Telefono": "1144445555",
           "Email": "ensayo.p02@example.invalid", "CodigoPostal": "1000", "Domicilio": "Calle Falsa 123", "Localidad": "CABA",
           "Provincia": "Buenos Aires", "TipoEmpleo": "1", "TiempoTrabajo": "5 años", "Empleador": "Empresa SA", "Sueldo": "1500000",
           "TelefonoLaboral": "1133334444", "TieneReciboSueldo": "true"}
    r, _ = adm.submit("/Cliente/Create", cli, action_contains="/Cliente/Create", follow=False)
    assert r.status == 302, "cliente no creado (%s)" % r.status
    prod = {"Codigo": "ENS-001", "Nombre": "Producto Ensayo", "Descripcion": "sintetico", "CategoriaId": "2", "MarcaId": "2",
            "PrecioCompraTotalConIva": "100000", "PrecioCompra": "82644.63", "MargenPorcentaje": "50", "PrecioVenta": "1000.51",
            "AlicuotaIVAId": "1", "PorcentajeIVA": "21", "StockActual": "0", "StockMinimo": "1"}
    r, _ = adm.submit("/Catalogo", prod, action_contains="/Producto/CreateAjax", follow=False)
    assert '"success":true' in r.text, "producto no creado: " + r.text[:160]
    pid = int(q1("select Id from Productos where Codigo='ENS-001'"))
    r, _ = adm.submit("/MovimientoStock/Create", {"ProductoId": str(pid), "Tipo": "0", "Cantidad": "50", "Motivo": "Ensayo P02"},
                      action_contains="/MovimientoStock/Create", follow=False)
    assert float(q1("select StockActual from Productos where Id=%d" % pid)) == 50, "stock no cargado"
    r, _ = adm.submit("/Caja/Create", {"Codigo": "CAJA-ENS", "Nombre": "Caja Ensayo", "Sucursal": "Central"}, fields_marker="Codigo", follow=False)
    caja = int(q1("select Id from Cajas where Codigo='CAJA-ENS'"))
    r, _ = adm.submit("/Caja/Abrir?cajaId=%d" % caja, {"MontoInicial": "1000"}, action_contains="/Caja/Abrir", follow=False)
    assert int(q1("select count(*) from AperturasCaja where CajaId=%d and Cerrada=0" % caja)) == 1, "caja no abierta"
    print("PREPARADO cliente=%s producto=%d caja=%d" % (dni, pid, caja))


def vender():
    adm = sesion()
    pid = int(q1("select Id from Productos where Codigo='ENS-001'"))
    cid = int(q1("select Id from Clientes where NumeroDocumento='11122233'"))
    sim = {"Productos": [{"ProductoId": pid, "Cantidad": 1}], "ClienteId": cid, "IncluirCreditoPersonal": False}
    r = adm.post_json("/api/cotizacion/simular", sim)
    assert r.status == 200, "simulación falló: " + r.text[:160]
    r = adm.post_json("/api/cotizacion/guardar", {"Simulacion": sim, "OpcionSeleccionada": {"MedioPago": 0, "Plan": "1 pago", "CantidadCuotas": 1},
                                                  "Observaciones": "Ensayo P02"})
    cot = r.json["id"]
    r = adm.post_json("/api/cotizacion/%d/conversion/convertir" % cot,
                      {"UsarPrecioCotizado": True, "ConfirmarVenta": True, "Facturar": True, "TipoFactura": "B"})
    assert r.json and r.json.get("exitoso") and r.json.get("facturada"), "conversión falló: " + r.text[:200]
    print("VENTA_ID=%s" % r.json["ventaId"])


def detalle():
    adm = sesion()
    r = adm.get("/Venta/Details/%s" % sys.argv[2], follow=False)
    print("DETALLE_STATUS=%s" % r.status)


def modulos():
    """Humo inicial de los modulos principales sobre una base nueva: cada pantalla abre (200) con el administrador inicial."""
    adm = sesion()
    rutas = ["/", "/Cliente", "/Catalogo", "/Venta", "/Cotizacion", "/Caja", "/Credito", "/Proveedor", "/OrdenCompra",
             "/MovimientoStock", "/Devolucion", "/Documento", "/Seguridad", "/ConfiguracionPago", "/Mora", "/Reporte",
             "/Tesoreria", "/CuentaPorPagar", "/ListaPrecio"]
    malas = []
    for ruta in rutas:
        r = adm.get(ruta, follow=True)
        if r.status != 200:
            malas.append("%s=%s" % (ruta, r.status))
    print("MODULOS_OK=%d MODULOS_MALOS=%s" % (len(rutas) - len(malas), ",".join(malas) or "-"))


def gracia():
    """Carga por la pantalla real (versionado, nunca sembrado) una configuracion de punitorios con 12 dias de gracia y comprueba
    con la vista previa del calculador real: dentro de la gracia no se cobra, el primer dia posterior si. La tasa es SINTETICA."""
    import datetime, json
    adm = sesion()
    hoy = datetime.date.today().isoformat()
    ov = {"Punitorios.CrearForm.VigenteDesde": hoy, "Punitorios.CrearForm.Porcentaje": "1", "Punitorios.CrearForm.PeriodoDias": "30",
          "Punitorios.CrearForm.DiasGracia": "12", "Punitorios.CrearForm.Activa": "true",
          "Punitorios.CrearForm.MotivoCambio": "Ensayo base nueva (tasa sintetica)"}
    r, _ = adm.submit("/ConfiguracionPago/CreditoPersonal", ov, action_contains="CrearVersionPunitorio", follow=False)
    n = int(q1("select count(*) from ConfiguracionesPunitorio where IsDeleted=0"))
    fila = sql("select DiasGracia, cast(Porcentaje as varchar(20)), PeriodoDias from ConfiguracionesPunitorio where IsDeleted=0")
    print("GRACIA_HTTP=%s VERSIONES=%d FILA=%s" % (r.status, n, fila[0] if fila else "-"))
    pv = adm.get("/ConfiguracionPago/PreviewPunitorio?porcentaje=1&periodoDias=30&diasGracia=12", follow=False)
    puntos = json.loads(pv.text).get("puntos") or json.loads(pv.text).get("Puntos") or []
    imp = [float(p.get("importe", p.get("Importe", -1))) for p in puntos]
    print("PREVIEW_IMPORTES=%s" % imp)


if __name__ == "__main__":
    guardia()
    {"preparar": preparar, "vender": vender, "detalle": detalle, "modulos": modulos, "gracia": gracia}[sys.argv[1]]()
