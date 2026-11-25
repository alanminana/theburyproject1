#!/usr/bin/env python3
"""Cierra el credito al inicio de una instalacion NUEVA, por la MISMA pantalla que usa una persona: Clientes -> «Limites por puntaje»
(POST /Cliente/LimitesPorPuntajeGuardar, con sesion de un usuario con `clientes.managecreditlimits` y token antifalsificacion).

Aplica la decision del propietario del 2026-11-22 (docs/cierre-credito-cupos-cero-20261122.md §1): la semilla de las migraciones deja el
puntaje 0 en $200.000 y una instalacion nueva debe llevar TODOS los cupos a $0 antes de operar. No cambia ninguna regla ni ningun otro parametro
(montos financiables, punitorios y planes ya vienen cerrados). Idempotente: si ya estan en $0 no escribe. No escribe por SQL.
Despues se verifica con scripts/sql/p07-verificar-credito-cerrado.sql (exit 0) o con scripts/install/verificar-servidor.sh.

  python3 scripts/install/cerrar-credito-inicial.py --base https://tbp-prov:8443 --ca caddy-root-ca.crt --resolve 127.0.0.1 \
      --user prov-admin --env-file .env        # la contrasena se lee de ADMIN_PASSWORD del .env (nunca se imprime)

Solo contra una instalacion propia, con --ca (CA de Caddy) o con http://127.0.0.1:PUERTO en una pila local. Exit 0 = cerrado; 1 = fallo.
"""
import argparse
import json
import os
import socket
import ssl
import sys
import urllib.parse
import urllib.request

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "qa", "p07"))
from erp_http import Erp  # noqa: E402  (cliente HTTP con cookies, login en dos fases y parser de formularios)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--base", required=True)
    ap.add_argument("--ca", help="CA publica de Caddy (caddy-root-ca.crt)")
    ap.add_argument("--resolve", help="IP a la que resolver el host de --base (como curl --resolve)")
    ap.add_argument("--user", required=True)
    ap.add_argument("--env-file", help="lee ADMIN_PASSWORD de este .env")
    a = ap.parse_args()
    pwd = os.environ.get("ADMIN_PASSWORD", "")
    if not pwd and a.env_file:
        for line in open(a.env_file, encoding="utf-8"):
            if line.startswith("ADMIN_PASSWORD="):
                pwd = line.rstrip("\r\n").split("=", 1)[1]
    if not pwd:
        sys.exit("falta la contrasena (ADMIN_PASSWORD en el entorno o --env-file)")
    u = urllib.parse.urlparse(a.base)
    if u.scheme == "https" and not a.ca:
        sys.exit("con https hace falta --ca (la CA propia de Caddy)")
    if a.resolve:
        orig = socket.getaddrinfo
        socket.getaddrinfo = lambda host, *r, **k: orig(a.resolve if host == u.hostname else host, *r, **k)
    e = Erp(a.base)
    if u.scheme == "https":
        ctx = ssl.create_default_context(cafile=a.ca)
        e.op = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(e.jar), urllib.request.HTTPSHandler(context=ctx))
        # misma politica de redirecciones que Erp (no seguirlas dentro del opener)
        from erp_http import _NoRedirect
        e.op = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(e.jar), urllib.request.HTTPSHandler(context=ctx), _NoRedirect)
    r = e.login(a.user, pwd)
    if "/Login" in r.url:
        sys.exit("el login fallo (usuario o contrasena)")
    tok = e.token(e.get("/Cliente").text)
    part = e.get("/Cliente/LimitesPorPuntajePartial")
    if part.status != 200:
        sys.exit(f"no se pudo abrir «Limites por puntaje» (HTTP {part.status}): el usuario necesita clientes.managecreditlimits")
    import re
    fields, montos = [], {}
    for m in re.finditer(r'<input[^>]*name="(Items\[\d+\]\.(?:Id|Puntaje|Activo|LimiteMonto))"[^>]*value="([^"]*)"', part.text):
        fields.append([m.group(1), m.group(2)])
    for k, v in fields:
        if k.endswith(".LimiteMonto"):
            montos[k] = v
    if not montos:
        sys.exit("no se encontraron limites en la pantalla")
    distintos = {k: v for k, v in montos.items() if str(v) not in ("0", "0.00")}
    print(f"puntajes con cupo: {len(montos)}; con monto > 0: {len(distintos)}")
    if not distintos:
        print("ya estan todos en $0: nada que hacer (idempotente)")
        return 0
    body = [[k, "0" if k.endswith(".LimiteMonto") else v] for k, v in fields]
    body.append(["__RequestVerificationToken", tok or ""])
    resp = e.post("/Cliente/LimitesPorPuntajeGuardar", body, follow=False, headers={"RequestVerificationToken": tok or "", "X-Requested-With": "XMLHttpRequest"})
    try:
        j = json.loads(resp.text)
    except Exception:
        sys.exit(f"respuesta inesperada (HTTP {resp.status})")
    if not j.get("ok"):
        sys.exit("la pantalla rechazo el guardado: " + "; ".join(j.get("errores") or []))
    print("guardado por la pantalla real:", j.get("mensaje"))
    return 0


if __name__ == "__main__":
    sys.exit(main())
