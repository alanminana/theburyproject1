#!/usr/bin/env python3
"""Audita en el servidor Linux/VM que el modelo de acceso de TheBuryProject se cumple: la LAN solo llega a la web HTTPS y la
administracion (SSH, etc.) solo se permite desde la red de Tailscale (por tailscale0) o desde equipos concretos (/32).

  python3 scripts/install/access-model-audit.py                       # en el servidor (root): lee `ufw status` y `ss -ltn`
  python3 scripts/install/access-model-audit.py --ufw FILE --listen FILE   # contra capturas guardadas (pruebas)

Comprueba:
  1. ufw esta activo (si no, el SSH queda sin restringir).
  2. Ninguna regla ufw permite puertos de administracion (22, 23, 135, 445, 3389, 5985, 5986) desde Anywhere, desde una red LAN completa
     ni desde un rango mas amplio que la tailnet; solo `100.64.0.0/10 (o dentro) por tailscale0` o un equipo /32 privado.
     Fallo cerrado: una linea que no se puede interpretar o un servicio no reconocido cuentan como incumplimiento.
  3. Ninguna regla ufw abre SQL (1433) ni Kestrel (8080), ni "todos los puertos" (el 443 de Caddy NO se filtra con ufw: va por DOCKER-USER).
  4. 1433 y 8080 no escuchan en una direccion no loopback (publicados por error).
Exit 0 = cumple; 1 = incumple; 2 = no se pudo leer la configuracion.
"""
import ipaddress
import re
import subprocess
import sys
from pathlib import Path

MGMT = {22, 23, 135, 445, 3389, 5985, 5986}
NEVER = {1433, 8080}
TAILNET = ipaddress.ip_network("100.64.0.0/10")
PRIVATE = [ipaddress.ip_network(n) for n in ("10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16")]
SERVICES = {"ssh": 22, "openssh": 22, "telnet": 23, "ms-wbt-server": 3389, "rdp": 3389, "microsoft-ds": 445, "smb": 445,
            "ms-sql-s": 1433, "winrm": 5985, "http-alt": 8080}
ACTION = re.compile(r"\s(ALLOW|LIMIT|DENY|REJECT)(?:\s+(IN|OUT|FWD))?(?:\s+|$)")
SKIP = re.compile(r"^(Status:|To\s|--|Default:|Logging:|New profiles:)")


def parse_to(to: str):
    """Columna 'To' -> (puertos|None=todos, interfaz, desconocido). Un nombre de servicio/perfil no reconocido es 'desconocido' (se trata como posible administracion)."""
    iface = None
    m = re.search(r"(?:^|\s)on\s+(\S+)", to)
    if m:
        iface = m.group(1)
        to = to[:m.start()] + to[m.end():]
    to = re.sub(r"\(v6\)", "", to).strip()
    to = re.sub(r"^\d+\.\d+\.\d+\.\d+(?:/\d+)?\s+", "", to)   # regla con IP de destino: "192.168.1.10 22/tcp"
    if not to or to.lower().startswith("anywhere"):
        return None, iface, False
    spec = to.split("/")[0]
    ports, unknown = set(), False
    for item in spec.split(","):
        item = item.strip()
        if re.fullmatch(r"\d+", item):
            ports.add(int(item))
        elif re.fullmatch(r"\d+:\d+", item):
            lo, hi = (int(x) for x in item.split(":"))
            ports |= set(range(lo, hi + 1))
        elif item.lower() in SERVICES:
            ports.add(SERVICES[item.lower()])
        else:
            unknown = True
    return ports, iface, unknown


def source_ok(frm: str, iface):
    """Origen aceptable para administrar: tailnet por tailscale0 (rango o IP), o un equipo concreto privado (/32). Devuelve (ok, motivo)."""
    frm = re.sub(r"\(v6\)", "", frm).strip()
    m = re.search(r"\s+on\s+(\S+)$", frm)
    if m:
        iface = iface or m.group(1)
        frm = frm[:m.start()].strip()
    if frm.lower().startswith("anywhere") or not frm:
        return False, "desde cualquier origen (Anywhere)"
    try:
        net = ipaddress.ip_network(frm, strict=False)
    except ValueError:
        return False, f"origen no interpretable: {frm!r}"
    if not isinstance(net, ipaddress.IPv4Network):
        return False, "origen IPv6"
    if net.subnet_of(TAILNET):
        if iface == "tailscale0":
            return True, ""
        return False, "origen de la tailnet sin atar a tailscale0 (un equipo de la LAN podria suplantar un origen 100.x)"
    if net.prefixlen == 32 and any(net.subnet_of(p) for p in PRIVATE):
        return True, ""
    if net.prefixlen == 32:
        return False, f"equipo {net.network_address} que no es privado ni de la tailnet (la administracion no se abre a Internet)"
    return False, f"red completa {net} (la LAN solo debe tener HTTPS)"


def audit_ufw(text: str):
    errs = []
    if not re.search(r"^Status:\s+active", text, re.M):
        errs.append("ufw no esta activo: el SSH (y cualquier servicio del host) queda sin restringir")
    for raw in text.splitlines():
        line = re.sub(r"^\[\s*\d+\]\s*", "", raw.strip())      # formato numbered: "[ 1] 22/tcp ..."
        if not line or SKIP.match(line):
            continue
        m = ACTION.search(" " + line)
        if not m:
            errs.append(f"linea de ufw no interpretable (fallo cerrado): '{raw.strip()}'")
            continue
        if m.group(1) in ("DENY", "REJECT") or m.group(2) == "OUT":
            continue
        pos = m.start() - 1 if m.start() > 0 else 0
        to, frm = line[:pos].strip(), line[m.end() - 1:].strip()
        ports, iface, unknown = parse_to(to)
        if ports is None:
            errs.append(f"regla que abre TODOS los puertos: '{raw.strip()}'")
            continue
        for p in sorted(ports & NEVER):
            errs.append(f"regla que abre {p} (SQL/Kestrel no deben abrirse en el host): '{raw.strip()}'")
        if ports & MGMT or unknown:
            ok, why = source_ok(frm, iface)
            if not ok:
                what = ", ".join(str(p) for p in sorted(ports & MGMT)) or "servicio/perfil no reconocido (se asume administracion)"
                errs.append(f"administracion ({what}) {why}: '{raw.strip()}'")
    return errs


def audit_listen(text: str):
    errs = []
    for line in text.splitlines():
        cols = line.split()
        loc = next((c for c in cols if re.search(r":\d+$", c) and not c.startswith("Local")), None)
        if not loc:
            continue
        addr, _, port = loc.rpartition(":")
        addr = addr.strip("[]")
        if port.isdigit() and int(port) in NEVER and addr not in ("127.0.0.1", "::1"):
            errs.append(f"el puerto {port} escucha en {addr} (no loopback): SQL/Kestrel no deben publicarse ('{line.strip()}')")
    return errs


def run(cmd):
    try:
        return subprocess.run(cmd, capture_output=True, text=True, check=True).stdout
    except (OSError, subprocess.CalledProcessError) as e:
        raise SystemExit(f"ERROR ejecutando {' '.join(cmd)}: {e} (ejecutar como root en el servidor)") from e


def main(argv):
    args = argv[1:]
    opts = {}
    while args:
        k = args.pop(0)
        if k not in ("--ufw", "--listen") or not args:
            print(__doc__)
            return 2
        opts[k] = args.pop(0)
    try:
        ufw = Path(opts["--ufw"]).read_text(encoding="utf-8") if "--ufw" in opts else run(["ufw", "status"])
        listen = Path(opts["--listen"]).read_text(encoding="utf-8") if "--listen" in opts else run(["ss", "-H", "-ltn"])
    except (OSError, SystemExit) as e:
        print(e, file=sys.stderr)
        return 2
    errs = audit_ufw(ufw) + audit_listen(listen)
    for e in errs:
        print(f"FAIL {e}")
    if errs:
        print(f"{len(errs)} incumplimiento(s) del modelo de acceso")
        return 1
    print("OK modelo de acceso: administracion solo desde tailscale0/equipos concretos; la LAN no administra; SQL/Kestrel no publicados")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
