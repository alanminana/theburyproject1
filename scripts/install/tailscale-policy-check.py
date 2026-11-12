#!/usr/bin/env python3
"""Valida y evalua OFFLINE la politica de Tailscale de TheBuryProject (deploy/tailscale/policy.hujson).

  python scripts/install/tailscale-policy-check.py [POLITICA] [--require-real-identities]
  python scripts/install/tailscale-policy-check.py --can USER DST PROTO PORT      (consulta puntual, exit 0 = permitido)

Solo biblioteca estandar. NO habla con Tailscale: es un analisis estatico + un evaluador del subconjunto de grants que usa el proyecto
(src = usuarios/grupos, dst = etiquetas o usuarios, ip = proto:puerto). La validacion real y definitiva la hace la consola de Tailscale
con la seccion `tests` de la propia politica al aplicarla.
Exit 0 = politica valida; 1 = incumple alguna regla; 2 = error de uso/lectura.
"""
import json
import re
import sys
from pathlib import Path

DEFAULT_POLICY = Path(__file__).resolve().parents[2] / "deploy" / "tailscale" / "policy.hujson"
ALLOWED_PORTS = {("tcp", 443), ("udp", 443)}  # HTTPS + HTTP/3 de Caddy. Nada mas.
SERVER_TAG = "tag:erp"
BROAD_SRC = {"*", "autogroup:member", "autogroup:admin", "autogroup:owner", "autogroup:tagged", "autogroup:shared",
             "autogroup:internet", "autogroup:self"}
FORBIDDEN_TOP = {
    "acls": "ACLs legacy (usar solo grants)",
    "autoApprovers": "autoApprovers (subnet routers / exit nodes)",
    "ssh": "reglas ssh de Tailscale",
    "sshTests": "sshTests",
    "postures": "postures sin disenar",
    "ipsets": "ipsets",
}


def strip_hujson(text: str) -> str:
    """Quita comentarios // y /* */ (fuera de strings) y comas finales."""
    out, i, n, in_str = [], 0, len(text), False
    while i < n:
        c = text[i]
        if in_str:
            out.append(c)
            if c == "\\" and i + 1 < n:
                out.append(text[i + 1])
                i += 2
                continue
            if c == '"':
                in_str = False
            i += 1
            continue
        if c == '"':
            in_str = True
            out.append(c)
            i += 1
            continue
        if text.startswith("//", i):
            while i < n and text[i] != "\n":
                i += 1
            continue
        if text.startswith("/*", i):
            j = text.find("*/", i + 2)
            i = n if j < 0 else j + 2
            continue
        out.append(c)
        i += 1
    s = "".join(out)
    prev = None
    while prev != s:  # comas finales antes de } o ]
        prev = s
        s = re.sub(r",(\s*[}\]])", r"\1", s)
    return s


def load(path: Path) -> dict:
    return json.loads(strip_hujson(path.read_text(encoding="utf-8")))


def parse_ip(entry: str):
    """'tcp:443' -> ('tcp', (443, 443)); '443' -> ('*', (443, 443)); '*' -> ('*', '*'); '1-1024' -> ('*', (1, 1024))."""
    if ":" in entry:
        proto, _, port = entry.partition(":")
    else:
        proto, port = "*", entry
    if port == "*":
        return proto, "*"
    m = re.fullmatch(r"(\d+)(?:-(\d+))?", port)
    if not m:
        raise ValueError(f"ip invalido: {entry!r}")
    lo = int(m.group(1))
    hi = int(m.group(2) or lo)
    return proto, (lo, hi)


def members_of(policy: dict, src: str) -> set:
    if src.startswith("group:"):
        return set(policy.get("groups", {}).get(src, []))
    return {src}


def can(policy: dict, user: str, dst: str, proto: str, port: int) -> bool:
    """Evalua grants (deny por defecto)."""
    for g in policy.get("grants", []):
        if not any(user in members_of(policy, s) for s in g.get("src", [])):
            continue
        if dst not in g.get("dst", []):
            continue
        for ip in g.get("ip", []):
            p, rng = parse_ip(ip)
            if p not in ("*", proto):
                continue
            if rng == "*" or rng[0] <= port <= rng[1]:
                return True
    return False


def parse_target(t: str):
    """'tag:erp:443' -> ('tag:erp', 443); 'persona@x.com:443' -> ('persona@x.com', 443)."""
    dst, _, port = t.rpartition(":")
    return dst, int(port)


def ip_allowed(ip: str):
    try:
        p, rng = parse_ip(ip)
    except ValueError:
        return False
    return rng != "*" and rng[0] == rng[1] and (p, rng[0]) in ALLOWED_PORTS


def validate(policy: dict, require_real: bool):
    errs = []
    for k, why in FORBIDDEN_TOP.items():
        if policy.get(k):
            errs.append(f"la politica define {k}: prohibido ({why})")

    members = set()
    for gi, g in enumerate(policy.get("grants", [])):
        where = f"grants[{gi}]"
        extra = set(g) - {"src", "dst", "ip"}
        if extra:
            errs.append(f"{where}: campos no permitidos {sorted(extra)}")
        if not g.get("src"):
            errs.append(f"{where}: sin src")
        for s in g.get("src", []):
            if s in BROAD_SRC or s.startswith("tag:"):
                errs.append(f"{where}: src demasiado amplio o de maquina: {s!r}")
            elif s.startswith("group:"):
                if s not in policy.get("groups", {}):
                    errs.append(f"{where}: grupo inexistente {s!r}")
                members |= set(policy.get("groups", {}).get(s, []))
            elif "@" in s:
                members.add(s)
            else:
                errs.append(f"{where}: src no reconocido {s!r}")
        if g.get("dst") != [SERVER_TAG]:
            errs.append(f"{where}: dst debe ser exactamente [{SERVER_TAG!r}], es {g.get('dst')!r}")
        if not g.get("ip"):
            errs.append(f"{where}: sin ip")
        for ip in g.get("ip", []):
            if not ip_allowed(ip):
                errs.append(f"{where}: ip {ip!r} fuera de lo permitido (solo tcp:443 y udp:443)")
    if not policy.get("grants"):
        errs.append("sin grants: nadie podria acceder")

    owners = policy.get("tagOwners", {})
    if set(owners) != {SERVER_TAG}:
        errs.append(f"tagOwners debe definir solo {SERVER_TAG} (hay {sorted(owners)})")
    for o in owners.get(SERVER_TAG, []):
        if o != "autogroup:admin":
            errs.append(f"{SERVER_TAG} solo debe pertenecer a autogroup:admin, no a {o!r}")
    if not owners.get(SERVER_TAG):
        errs.append(f"{SERVER_TAG} sin propietario")

    for na in policy.get("nodeAttrs", []):
        if "funnel" in json.dumps(na).lower():
            errs.append("nodeAttrs habilita Funnel (publicaria el servicio en Internet)")

    if len(members) < 2:
        errs.append(f"se esperan al menos 2 identidades independientes, hay {len(members)}")
    for m in members:
        if not re.fullmatch(r"[^@\s]+@[^@\s]+\.[^@\s]+", m):
            errs.append(f"identidad invalida: {m!r}")
    if require_real:
        for m in sorted(members):
            if m.rpartition("@")[2].lower() in ("example.com", "example.org", "example.net", "invalid", "test"):
                errs.append(f"identidad de ejemplo sin reemplazar: {m!r}")

    tests = policy.get("tests", [])
    if not tests:
        errs.append("sin tests en la politica (la consola de Tailscale los usa al aplicarla)")
    covered = {m: False for m in members}
    has_outsider = False
    for ti, t in enumerate(tests):
        src = t.get("src", "")
        acc, den = t.get("accept", []), t.get("deny", [])
        if src in members:
            if SERVER_TAG + ":443" in acc:
                covered[src] = True
            if not any(d.endswith(":22") for d in den) or not any(d.endswith(":1433") for d in den):
                errs.append(f"tests[{ti}] ({src}): debe negar al menos {SERVER_TAG}:22 y :1433")
            others = members - {src}
            if not any(d.rpartition(":")[0] in others for d in den):
                errs.append(f"tests[{ti}] ({src}): debe negar el acceso a otra persona (aislamiento)")
        else:
            has_outsider = has_outsider or (bool(den) and not acc)
        for target, expect in [(a, True) for a in acc] + [(d, False) for d in den]:
            try:
                dst, port = parse_target(target)
            except ValueError:
                errs.append(f"tests[{ti}]: destino invalido {target!r}")
                continue
            got = can(policy, src, dst, "tcp", port)
            if got != expect:
                errs.append(f"tests[{ti}]: {src} -> {target}: esperado {'permitido' if expect else 'denegado'}, "
                            f"la politica da {'permitido' if got else 'denegado'}")
    for m, ok in sorted(covered.items()):
        if not ok:
            errs.append(f"ninguna prueba cubre el acceso autorizado de {m}")
    if not has_outsider:
        errs.append("falta una prueba de una cuenta fuera del grupo (denegada)")

    # Acceso efectivo de cada miembro: solo tag:erp:443 y nada hacia otras identidades.
    for m in sorted(members):
        for port in (22, 80, 1433, 3389, 5432, 8080):
            if can(policy, m, SERVER_TAG, "tcp", port):
                errs.append(f"{m} alcanza {SERVER_TAG}:{port} (debe ser solo 443)")
        for o in sorted(members - {m}):
            if can(policy, m, o, "tcp", 443):
                errs.append(f"{m} alcanza a {o}: las personas deben estar aisladas entre si")
    return errs


def main(argv):
    rest = argv[1:]
    if "--can" in rest:
        rest = [a for a in rest if a != "--can"]
        if len(rest) != 4 or not rest[3].isdigit():
            print(__doc__)
            return 2
        ok = can(load(DEFAULT_POLICY), rest[0], rest[1], rest[2], int(rest[3]))
        print("permitido" if ok else "denegado")
        return 0 if ok else 1
    flags = {a for a in rest if a.startswith("--")}
    unknown = flags - {"--require-real-identities"}
    if unknown:
        print(f"opcion desconocida: {sorted(unknown)}", file=sys.stderr)
        return 2
    paths = [a for a in rest if not a.startswith("--")]
    path = Path(paths[0]) if paths else DEFAULT_POLICY
    try:
        policy = load(path)
    except (OSError, ValueError) as e:
        print(f"ERROR leyendo {path}: {e}", file=sys.stderr)
        return 2
    errs = validate(policy, "--require-real-identities" in flags)
    if errs:
        for e in errs:
            print(f"FAIL {e}")
        print(f"{len(errs)} incumplimiento(s) en {path}")
        return 1
    print(f"OK {path}: grants de minimo privilegio, aislamiento entre personas y pruebas coherentes")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
