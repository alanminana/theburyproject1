#!/usr/bin/env python3
"""Contraste EXTERNO de las exportaciones (Excel/PDF) contra SQL Server.

Lee los archivos generados por ExportacionesReporteConsistenciaTests (QA_EXPORT_OUT) con herramientas ajenas a .NET
(openpyxl, pdftotext) y los compara con consultas SQL directas a la base QA (catalogo 'qa_export*').

Uso: python verificar-exportaciones.py <carpeta_salida> <catalogo_qa_export>
Requiere: sqlcmd, pdftotext (poppler), openpyxl. Solo opera sobre catalogos 'qa_export*' y solo lee.
"""
import re
import subprocess
import sys
from decimal import Decimal
from pathlib import Path

import openpyxl

out = Path(sys.argv[1])
catalogo = sys.argv[2]
assert catalogo.lower().startswith("qa_export"), "solo catalogos qa_export*"
FALLAS = []


def sql(consulta: str) -> Decimal:
    r = subprocess.run(
        ["sqlcmd", "-S", r"(localdb)\MSSQLLocalDB", "-E", "-C", "-d", catalogo, "-h", "-1", "-W", "-Q",
         f"SET NOCOUNT ON; {consulta}"],
        capture_output=True, text=True, check=True)
    return Decimal(r.stdout.strip().splitlines()[0])


def check(nombre, esperado, real):
    ok = real is not None and Decimal(str(esperado)) == Decimal(str(real))
    print(("OK   " if ok else "FALLA"), nombre, "| SQL/esperado =", esperado, "| archivo =", real)
    if not ok:
        FALLAS.append(nombre)


def hoja(nombre):
    return openpyxl.load_workbook(out / nombre, data_only=True).worksheets[0]


def texto_pdf(nombre):
    return subprocess.run(["pdftotext", "-layout", str(out / nombre), "-"], capture_output=True, text=True,
                          check=True).stdout


def d(v):
    return Decimal(str(round(float(v), 2)))


def monto(m):
    return Decimal(m.group(1).replace(".", "").replace(",", ".")) if m else None


COM = "Estado IN (2,3,4) AND IsDeleted = 0"


def rango(a, b):
    return f"FechaVenta >= '{a}' AND FechaVenta < '{b}'"


def devoluciones(a, b):
    # Estados que restan: Aprobada y Completada (se confirman contra el enum al correr las pruebas).
    return (f"SELECT COALESCE(SUM(d.TotalDevolucion),0) FROM Devoluciones d WHERE d.IsDeleted=0 AND d.Estado IN ({EST_DEV}) "
            f"AND d.FechaDevolucion >= '{a}' AND d.FechaDevolucion < '{b}' "
            f"AND EXISTS (SELECT 1 FROM Ventas v WHERE v.Id=d.VentaId AND v.{COM})")


# EstadoDevolucion (Models/Enums): Pendiente=0, EnRevision=1?, Aprobada=2?, ... se lee del enum para no suponer.
enum_src = (Path(__file__).resolve().parents[2] / "Models" / "Entities" / "Devolucion.cs").read_text(encoding="utf-8")
bloque = re.search(r"enum EstadoDevolucion\s*\{(.*?)\}", enum_src, re.S).group(1)
valores = {n: int(v) for n, v in re.findall(r"(\w+)\s*=\s*(\d+)", bloque)}
EST_DEV = f"{valores['Aprobada']},{valores['Completada']}"
print("Estados que restan (Aprobada,Completada):", EST_DEV)

# ---------------------------------------------------------------- Ventas Noviembre
a, b = "2026-11-01", "2026-12-01"
ws = hoja("ventas-2026-11.xlsx")
col = {c.value: i for i, c in enumerate(ws[1])}
todas = list(ws.iter_rows(min_row=2, values_only=True))
filas = [r for r in todas if r[0] and str(r[0]).startswith("V-")]
suma_total = sql(f"SELECT SUM(Total) FROM Ventas WHERE {COM} AND {rango(a, b)}")
check("ventas Nov: cantidad de filas", sql(f"SELECT COUNT(*) FROM Ventas WHERE {COM} AND {rango(a, b)}"), len(filas))
check("ventas Nov: sin duplicados", len(filas), len({str(r[0]) for r in filas}))
check("ventas Nov: SUM(Total)", suma_total, sum(d(r[col['Total']]) for r in filas))
check("ventas Nov: SUM(IVA)", sql(f"SELECT SUM(IVA) FROM Ventas WHERE {COM} AND {rango(a, b)}"),
      sum(d(r[col['IVA']]) for r in filas))
check("ventas Nov: SUM(Subtotal)", sql(f"SELECT SUM(Subtotal) FROM Ventas WHERE {COM} AND {rango(a, b)}"),
      sum(d(r[col['Subtotal Neto']]) for r in filas))
check("ventas Nov: SUM(Descuento)", sql(f"SELECT SUM(Descuento) FROM Ventas WHERE {COM} AND {rango(a, b)}"),
      sum(d(r[col['Descuento ($)']]) for r in filas))
check("ventas Nov: Total - IVA - Subtotal = 0 en cada fila", 0,
      sum(abs(d(r[col['Total']]) - d(r[col['IVA']]) - d(r[col['Subtotal Neto']])) for r in filas))
dev = sql(devoluciones(a, b))
tot = next(r for r in todas if r[4] == "TOTALES:")
dv = next(r for r in todas if r[4] and str(r[4]).startswith("Devoluciones del"))
neto = next(r for r in todas if r[4] and str(r[4]).startswith("NETO"))
check("ventas Nov: fila TOTALES", suma_total, d(tot[col['Total']]))
check("ventas Nov: devoluciones del periodo", -dev, d(dv[col['Total']]))
check("ventas Nov: neto = ventas - devoluciones", suma_total - dev, d(neto[col['Total']]))
presentes = {str(r[0]) for r in filas}
for n in ("V-CANC", "V-PRES", "V-ELIM", "V-LIM-DIC", "V-LIM-OCT"):
    check(f"ventas Nov: excluye {n}", 0, int(n in presentes))
for n in ("V-LIM-INI", "V-LIM-FIN", "V-BAJA", "V-DEB", "V-DESC", "V-CRED"):
    check(f"ventas Nov: incluye {n}", 1, int(n in presentes))

# ---------------------------------------------------------------- Ventas PDF vs Excel vs SQL
txt = texto_pdf("ventas-2026-11.pdf")
filas_pdf = len(re.findall(r"\bV-(?:FACT|BAJA|LIM-INI|LIM-FIN|DEB|DESC|CRED|REL-\d\d)\b", txt))
check("PDF ventas Nov: filas de detalle = filas del Excel", len(filas), filas_pdf)
check("PDF ventas Nov: 'Ventas brutas' = SUM(Total)", suma_total, monto(re.search(r"Ventas brutas:\s*\$([\d.,]+)", txt)))
check("PDF ventas Nov: 'Devoluciones' = SQL", dev, monto(re.search(r"Devoluciones:\s*\$([\d.,]+)", txt)))
check("PDF ventas Nov: 'Ventas netas' = neto del Excel", d(neto[col['Total']]),
      monto(re.search(r"Ventas netas:\s*\$([\d.,]+)", txt)))

# ---------------------------------------------------------------- Octubre, vacio y sin descuentos
ws = hoja("ventas-2026-10.xlsx")
f10 = [r for r in ws.iter_rows(min_row=2, values_only=True) if r[0] and str(r[0]).startswith("V-")]
check("ventas Oct: cantidad", sql(f"SELECT COUNT(*) FROM Ventas WHERE {COM} AND {rango('2026-10-01', '2026-11-01')}"), len(f10))
ws = hoja("ventas-vacio.xlsx")
check("ventas periodo vacio: sin filas", 0,
      len([r for r in ws.iter_rows(min_row=2, values_only=True) if r[0] and str(r[0]).startswith("V-")]))
w0, w1 = hoja("ventas-2026-11-desc0.xlsx"), hoja("ventas-2026-11-desc1.xlsx")
check("ventas sin permiso de descuentos: una columna menos", w1.max_column - 1, w0.max_column)
check("ventas sin permiso de descuentos: no expone 'Descuento ($)'", 0, int("Descuento ($)" in [c.value for c in w0[1]]))

# ---------------------------------------------------------------- Morosidad
HOY = "2026-11-20"
ws = hoja("morosidad.xlsx")
col = {c.value: i for i, c in enumerate(ws[1])}
filas = [r for r in ws.iter_rows(min_row=2, values_only=True) if r[0]]
cond = (f"c.IsDeleted=0 AND cr.IsDeleted=0 AND c.MontoPagado<c.MontoTotal AND c.FechaVencimiento < '{HOY}'")
deuda = sql(f"SELECT SUM(c.MontoTotal-c.MontoPagado) FROM Cuotas c JOIN Creditos cr ON cr.Id=c.CreditoId WHERE {cond}")
check("morosidad: SUM(deuda vencida)", deuda, sum(d(r[col['Deuda Vencida']]) for r in filas))
check("morosidad: clientes (incluye el dado de baja)",
      sql(f"SELECT COUNT(DISTINCT cr.ClienteId) FROM Cuotas c JOIN Creditos cr ON cr.Id=c.CreditoId WHERE {cond}"), len(filas))
check("morosidad: documento conserva ceros a la izquierda", 1, int(any(r[col['Documento']] == "00123456" for r in filas)))
pdf = texto_pdf("morosidad.pdf")
check("PDF morosidad: Deuda Vencida = SQL", deuda, monto(re.search(r"Deuda Vencida:\s*\$([\d.,]+)", pdf)))
check("PDF morosidad: lista a ambos clientes", 2, sum(1 for n in ("Perez", "Gomez") if n in pdf))

# ---------------------------------------------------------------- Margenes y movimientos
ws = hoja("margenes.xlsx")
check("margenes: una fila por producto no eliminado", sql("SELECT COUNT(*) FROM Productos WHERE IsDeleted=0"),
      len([r for r in ws.iter_rows(min_row=2, values_only=True) if r[0]]))
ws = hoja("movimientos-2026-11.xlsx")
filas = [r for r in ws.iter_rows(min_row=2, values_only=True) if r[1]]
per = "IsDeleted=0 AND CreatedAt >= '2026-11-01' AND CreatedAt < '2026-12-01'"
check("movimientos: filas del periodo", sql(f"SELECT COUNT(*) FROM MovimientosStock WHERE {per}"), len(filas))
check("movimientos: SUM(costo total)", sql(f"SELECT SUM(CostoTotalAlMomento) FROM MovimientosStock WHERE {per}"),
      sum(d(r[6]) for r in filas))

# ---------------------------------------------------------------- Devoluciones CSV
raw = (out / "devoluciones.csv").read_bytes()
check("devoluciones CSV: empieza con BOM UTF-8", 1, int(raw[:3] == b"\xef\xbb\xbf"))
lineas = [l for l in raw.decode("utf-8-sig").splitlines()[1:] if l]
check("devoluciones CSV: una fila por devolucion", sql("SELECT COUNT(*) FROM Devoluciones WHERE IsDeleted=0"), len(lineas))
check("devoluciones CSV: SUM(Monto)", sql("SELECT SUM(TotalDevolucion) FROM Devoluciones WHERE IsDeleted=0"),
      sum(Decimal(l.split(";")[-1].strip('"')) for l in lineas))
check("devoluciones CSV: DNI no repetido en la columna Cliente", 0, sum(1 for l in lineas if "DNI:" in l))

# ---------------------------------------------------------------- Cotizacion PDF (render del resultado)
def importes(txt_pdf, etiqueta):
    m = re.search(re.escape(etiqueta) + r"\s*\$?\s*([\d.]+,\d{2})", txt_pdf)
    return monto(m) if m else None


con, sin = texto_pdf("cotizacion-con-descuentos.pdf"), texto_pdf("cotizacion-sin-descuentos.pdf")
check("cotizacion PDF (con descuentos): Subtotal bruto", 2500, importes(con, "Subtotal"))
check("cotizacion PDF (con descuentos): Descuento total", 250, importes(con, "Descuento total"))
check("cotizacion PDF (con descuentos): Armados", 100, importes(con, "Armados"))
check("cotizacion PDF (con descuentos): Total base", 2650, importes(con, "Total base"))
check("cotizacion PDF (con descuentos): TOTAL", 2650, importes(con, "TOTAL"))
check("cotizacion PDF (sin permiso): Subtotal ya neto de descuento", 2250, importes(sin, "Subtotal"))
check("cotizacion PDF (sin permiso): no muestra 'Descuento total'", 0, int("Descuento total" in sin))
check("cotizacion PDF (sin permiso): no muestra la columna DESCUENTO", 0, int("DESCUENTO" in sin))
check("cotizacion PDF (sin permiso): TOTAL sin cambios", 2650, importes(sin, "TOTAL"))
check("cotizacion PDF: aritmetica Subtotal - Descuento + Armados + Envio = Total base",
      Decimal("2500") - Decimal("250") + Decimal("100") + Decimal("300"), importes(con, "Total base"))

print()
print("RESULTADO:", "TODO OK" if not FALLAS else f"{len(FALLAS)} FALLA(S): " + "; ".join(FALLAS))
sys.exit(1 if FALLAS else 0)
