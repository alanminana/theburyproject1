#!/usr/bin/env python3
"""P-08: observador EXTERNO de ensayo con Uptime Kuma 2.x (otro host que el ERP). Configura y consulta por API (uptime-kuma-api 1.2.1).

  docker run -d --name bury-kuma -p 3001:3001 --add-host tbp:<IP_ERP> -e UPTIME_KUMA_DB_TYPE=sqlite \
      -e NODE_EXTRA_CA_CERTS=/ca.crt -v caddy-root-ca.crt:/ca.crt:ro -v bury-kuma-data:/app/data louislam/uptime-kuma:2.5.5
  python3 kuma_observador.py setup  secreto.json     # crea el admin (clave aleatoria, guardada en secreto.json, 0600 a cargo de quien lo use)
  python3 kuma_observador.py monitores secreto.json  # /health/live y /health/ready por HTTPS con la CA, cada 20 s, palabra clave Healthy
  python3 kuma_observador.py push secreto.json       # monitor PUSH (heartbeat del agente monitor.py); imprime el token en stdout
  python3 kuma_observador.py estado secreto.json     # ultimo latido de cada monitor
  (KUMA_URL=http://host:3001 para apuntar a otra instancia; por defecto 127.0.0.1)

Trampas de Kuma 2.x verificadas: (1) sin UPTIME_KUMA_DB_TYPE el primer arranque espera un asistente web de base de datos y el socket no responde;
(2) uptime-kuma-api 1.2.1 no envia `conditions` y Kuma 2.x lo exige (NOT NULL): se agrega a mano; (3) la libreria no conoce `pushToken`.
El monitor Push (heartbeat del agente) exige que el ERP alcance a Kuma: en el ensayo el firewall de Windows lo impedia, por eso se usan monitores
HTTP (el observador consulta al ERP). En produccion Kuma va en OTRO sitio (p. ej. por Tailscale). Sin dependencias del ERP; solo ensayo.
"""
import json, os, secrets, sys, time
from uptime_kuma_api import UptimeKumaApi, MonitorType

URL = os.environ.get("KUMA_URL", "http://127.0.0.1:3001")
STATE = {1: "UP", 0: "DOWN", 2: "PENDING", 3: "MAINTENANCE"}


def api_login(path):
    c = json.load(open(path)); api = UptimeKumaApi(URL, timeout=30); api.login(c["user"], c["password"]); time.sleep(2); return api


def main():
    cmd, path = sys.argv[1], sys.argv[2]
    if cmd == "setup":
        pw = secrets.token_urlsafe(16)
        api = UptimeKumaApi(URL, timeout=30); r = api.setup("observador", pw); api.disconnect()
        if r.get("msg") != "successAdded":
            print("setup fallo:", r.get("msg")); sys.exit(1)
        fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)   # exclusivo y 0600: no pisa credenciales existentes
        with os.fdopen(fd, "w") as f: json.dump({"user": "observador", "password": pw}, f)
        print("setup: ok (credenciales en", path + ")"); return
    api = api_login(path)
    if cmd == "monitores":
        for m in api.get_monitors():
            if m["name"].startswith("ERP tbp "): api.delete_monitor(m["id"])   # solo los propios; no borra monitores ajenos
        for name, p in (("ERP tbp /health/live", "/health/live"), ("ERP tbp /health/ready", "/health/ready")):
            d = api._build_monitor_data(type=MonitorType.KEYWORD, name=name, url="https://tbp" + p, keyword="Healthy", interval=20, retryInterval=20, maxretries=1, method="GET")
            d["conditions"] = "[]"; print("monitor", api._call("add", d)["monitorID"], name)
    elif cmd == "push":
        for m in api.get_monitors():
            if m["name"].startswith("bury-monitor-heartbeat"): api.delete_monitor(m["id"])
        tok = secrets.token_hex(16)
        d = api._build_monitor_data(type=MonitorType.PUSH, name="bury-monitor-heartbeat (agente)", interval=180, retryInterval=60, maxretries=1)
        d["conditions"] = "[]"; d["pushToken"] = tok
        api._call("add", d); print(tok)    # BURY_MONITOR_HEARTBEAT=http://<kuma>/api/push/<token>
    elif cmd == "estado":
        for m in api.get_monitors():
            b = api.get_monitor_beats(m["id"], 1); last = b[-1] if b else None
            if not last:
                print(m["name"], "sin latidos"); continue
            s = last["status"]; print(m["name"], STATE.get(getattr(s, "value", s), "?"), last.get("msg", "")[:60])
    api.disconnect()


if __name__ == "__main__":
    main()
