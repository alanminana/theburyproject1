#!/usr/bin/env python3
"""Segunda pasada de auditoria funcional (software, sin servidor fisico).

Reutiliza el cliente HTTP y los recorridos P-07 (R0-R11) como regresion de circuitos completos contra la
instalacion DESCARTABLE local (SQL Server en Docker + app en Development) y agrega casos negativos, limites,
reversiones, auditoria y concurrencia (secciones S*).

Variables: P08_BASE_URL, P08_DB_CONTAINER (bury-sN-db), P08_DB_NAME (sNqa), P08_APP_LOG, P08_SA_PASSWORD,
P08_ADMIN_USER/PASS (administrador sembrado en Development), P08_REPORT.
Guardia: contenedor con nombre bury-s<d>-db, base s<d>qa, URL 127.0.0.1 y base SIN datos comerciales.
"""
import os, re, subprocess, sys, json, time, threading
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
os.environ.setdefault("P07_SA_PASSWORD", os.environ["P08_SA_PASSWORD"])
os.environ.setdefault("P07_ADMIN_PASS", os.environ.get("P08_ADMIN_PASS", "Admin123!"))
os.environ["P07_BASE_URL"] = os.environ["P08_BASE_URL"]
os.environ["P07_DB_CONTAINER"] = os.environ["P08_DB_CONTAINER"]
os.environ["P07_DB_NAME"] = os.environ["P08_DB_NAME"]
import p07_recorridos as m  # noqa: E402
from erp_http import Erp  # noqa: E402

chk, sql, q1, dec, sec = m.chk, m.sql, m.q1, m.dec, m.sec
LOG = os.environ.get("P08_APP_LOG", "")


def guardia():
    c, d, b = os.environ["P08_DB_CONTAINER"], os.environ["P08_DB_NAME"], os.environ["P08_BASE_URL"]
    if not re.fullmatch(r"bury-s\d-db", c) or not re.fullmatch(r"s\dqa", d) or not re.fullmatch(r"http://127\.0\.0\.1:\d+", b):
        sys.exit("RECHAZADO: solo contra la instalacion descartable local (bury-sN-db / sNqa / 127.0.0.1).")
    n = sql("select (select count(*) from Clientes)+(select count(*) from Ventas)+(select count(*) from Productos)+(select count(*) from Proveedores)+(select count(*) from Cajas)")
    if n[0][0] != "0":
        sys.exit("RECHAZADO: la base no esta vacia.")


m.guardia_descartable = guardia
m.DBC, m.DB = os.environ["P08_DB_CONTAINER"], os.environ["P08_DB_NAME"]
_real_run = subprocess.run


def _run(args, *a, **k):
    if isinstance(args, list) and args[:2] == ["docker", "logs"]:
        txt = open(LOG, encoding="utf-8", errors="replace").read() if LOG else ""
        return subprocess.CompletedProcess(args, 0, stdout=txt, stderr="")
    return _real_run(args, *a, **k)


m.subprocess.run = _run
os.environ["P07_APP_CONTAINER"] = "n/a"


def extra(ctx):
    import p08_extra
    p08_extra.correr(m)


if __name__ == "__main__":
    t0 = m.main()
    extra(None)
    ok = sum(1 for r in m.RESULTS if r["ok"]); bad = [r for r in m.RESULTS if not r["ok"]]
    print("\n== RESUMEN: %d comprobaciones, %d OK, %d con falla (%.0f s) ==" % (len(m.RESULTS), ok, len(bad), time.time() - t0))
    for b in bad:
        print("  FALLA %s %s -> %s" % (b["id"], b["descripcion"], b["evidencia"]))
    rp = os.environ.get("P08_REPORT")
    if rp:
        json.dump(m.RESULTS, open(rp, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    sys.exit(1 if bad else 0)
