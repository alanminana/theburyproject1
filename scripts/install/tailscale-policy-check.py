#!/usr/bin/env python3
"""Valida y evalua OFFLINE la politica de Tailscale de TheBuryProject (deploy/tailscale/policy*.hujson).

  python scripts/install/tailscale-policy-check.py [POLITICA] [--profile linux-vm|windows] [--require-real-identities]
  python scripts/install/tailscale-policy-check.py [--policy POLITICA] --can USER DST PROTO PORT   (consulta puntual, exit 0 = permitido)

Roles (dos identidades independientes):
  group:erp-usuarios  usuario remoto: SOLO la web del ERP (tcp/udp 443).
  group:erp-admins    administrador remoto: la web (443) + UN puerto de administracion del servidor:
                      tcp:22 en el perfil linux-vm (SSH dentro del servidor/VM Linux), tcp:3389 en el perfil windows (RDP).
Los grupos son disjuntos y ninguna persona alcanza a otra. El perfil se fija con --profile (CI valida ambos archivos).

Solo biblioteca estandar. NO habla con Tailscale: es un analisis estatico + un evaluador del subconjunto de grants que usa el proyecto
(src = grupos, dst = etiquetas o usuarios, ip = proto:puerto). La validacion real y definitiva la hace la consola de Tailscale
con la seccion `tests` de la propia politica al aplicarla.
Exit 0 = politica valida; 1 = incumple alguna regla; 2 = error de uso/lectura.
"""
import json
import re
import sys
from pathlib import Path

DEFAULT_POLICY = Path(__file__).resolve().parents[2] / "deploy" / "tailscale" / "policy.hujson"
SERVER_TAG = "tag:erp"
WEB_GROUP = "group:erp-usuarios"
ADMIN_GROUP = "group:erp-admins"
WEB_IPS = {("tcp", 443), ("udp", 443)}  # HTTPS + HTTP/3 de Caddy
ADMIN_PORT_BY_PROFILE = {"linux-vm": 22, "windows": 3389}
# Puertos que se evaluan para el "acceso efectivo": administracion/archivos y servicios internos. Nadie los alcanza salvo el puerto
# de administracion del perfil, y solo el administrador.
ALL_PORTS = (22, 23, 80, 135, 445, 1433, 3389, 5432, 5985, 5986, 8080, 8443)
ALLOWED_TOP = {"groups", "tagOwners", "grants", "tests"}  # lista blanca: cualquier otra clave se rechaza
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


def _no_dupes(pairs):
    keys = [k for k, _ in pairs]
    dup = {k for k in keys if keys.count(k) > 1}
    if dup:
        raise ValueError(f"claves duplicadas en el JSON: {sorted(dup)}")
    return dict(pairs)


def load(path: Path) -> dict:
    return json.loads(strip_hujson(path.read_text(encoding="utf-8")), object_pairs_hook=_no_dupes)


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


def ip_set(ips):
    """['tcp:443', ...] -> {('tcp', 443), ...}; None si alguna entrada no es un puerto puntual con protocolo tcp/udp."""
    out = set()
    for ip in ips:
        try:
            p, rng = parse_ip(ip)
        except ValueError:
            return None
        if rng == "*" or rng[0] != rng[1] or p not in ("tcp", "udp"):
            return None
        out.add((p, rng[0]))
    return out


def validate(policy: dict, require_real: bool, profile: str = ""):
    errs = []
    unknown_top = set(policy) - ALLOWED_TOP
    if unknown_top:
        errs.append(f"claves de nivel superior no permitidas: {sorted(unknown_top)} (lista blanca: {sorted(ALLOWED_TOP)})")
    for k, why in FORBIDDEN_TOP.items():
        if policy.get(k):
            errs.append(f"la politica define {k}: prohibido ({why})")

    groups = policy.get("groups", {})
    if set(groups) != {WEB_GROUP, ADMIN_GROUP}:
        errs.append(f"groups debe definir exactamente {WEB_GROUP} y {ADMIN_GROUP} (hay {sorted(groups)})")
    web_m, adm_m = set(groups.get(WEB_GROUP, [])), set(groups.get(ADMIN_GROUP, []))
    if not web_m:
        errs.append(f"{WEB_GROUP} sin miembros: no habria usuario remoto")
    if not adm_m:
        errs.append(f"{ADMIN_GROUP} sin miembros: no habria administrador remoto")
    both = web_m & adm_m
    if both:
        errs.append(f"identidades en ambos grupos (los roles deben ser disjuntos): {sorted(both)}")
    members = web_m | adm_m
    lower = [m.lower() for m in web_m] + [m.lower() for m in adm_m]
    if len(lower) != len(set(lower)):
        errs.append("identidades repetidas que solo difieren en mayusculas (los roles deben ser disjuntos)")
    for m in sorted(members):
        if not re.fullmatch(r"[A-Za-z0-9._%+'-]+@[A-Za-z0-9-]+(\.[A-Za-z0-9-]+)+", m):
            errs.append(f"identidad invalida: {m!r}")
    if len(members) < 2:
        errs.append(f"se esperan al menos 2 identidades independientes, hay {len(members)}")
    if require_real:
        for m in sorted(members):
            if m.rpartition("@")[2].lower() in ("example.com", "example.org", "example.net", "invalid", "test"):
                errs.append(f"identidad de ejemplo sin reemplazar: {m!r}")

    # --- grants: exactamente uno por rol, con el conjunto de puertos exacto de ese rol ----------------------------------------------
    admin_port = None
    seen = {}
    for gi, g in enumerate(policy.get("grants", [])):
        where = f"grants[{gi}]"
        extra = set(g) - {"src", "dst", "ip"}
        if extra:
            errs.append(f"{where}: campos no permitidos {sorted(extra)}")
        src = g.get("src", [])
        if len(src) != 1 or src[0] not in (WEB_GROUP, ADMIN_GROUP):
            errs.append(f"{where}: src debe ser exactamente [{WEB_GROUP!r}] o [{ADMIN_GROUP!r}] "
                        f"(no individuos, comodines ni etiquetas), es {src!r}")
            continue
        role = src[0]
        seen[role] = seen.get(role, 0) + 1
        if g.get("dst") != [SERVER_TAG]:
            errs.append(f"{where}: dst debe ser exactamente [{SERVER_TAG!r}], es {g.get('dst')!r}")
        ips = ip_set(g.get("ip", []))
        if not g.get("ip") or ips is None:
            errs.append(f"{where}: ip debe ser una lista de puertos puntuales con protocolo (tcp:N / udp:N), es {g.get('ip')!r}")
            continue
        if role == WEB_GROUP:
            if ips != WEB_IPS:
                errs.append(f"{where} ({role}): el usuario remoto solo puede tener tcp:443 y udp:443, tiene {sorted(ips)}")
        else:
            extra_ips = ips - WEB_IPS
            if not WEB_IPS <= ips:
                errs.append(f"{where} ({role}): el administrador tambien debe tener la web (tcp:443 y udp:443)")
            allowed_mgmt = {("tcp", p) for p in ADMIN_PORT_BY_PROFILE.values()}
            if len(extra_ips) != 1 or not extra_ips <= allowed_mgmt:
                errs.append(f"{where} ({role}): el administrador debe sumar UN solo puerto de administracion "
                            f"({' o '.join('tcp:%d' % p for p in sorted(ADMIN_PORT_BY_PROFILE.values()))}), suma {sorted(extra_ips)}")
            else:
                admin_port = next(iter(extra_ips))[1]
    for role in (WEB_GROUP, ADMIN_GROUP):
        if seen.get(role, 0) != 1:
            errs.append(f"debe haber exactamente un grant para {role} (hay {seen.get(role, 0)})")
    if profile and admin_port != ADMIN_PORT_BY_PROFILE[profile]:
        errs.append(f"perfil {profile}: el puerto de administracion debe ser tcp:{ADMIN_PORT_BY_PROFILE[profile]}, es {admin_port}")

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

    # --- tests de la propia politica -------------------------------------------------------------------------------------------------
    tests = policy.get("tests", [])
    if not tests:
        errs.append("sin tests en la politica (la consola de Tailscale los usa al aplicarla)")
    covered = {m: False for m in members}
    admin_covered = {m: False for m in adm_m}
    has_outsider = False
    for ti, t in enumerate(tests):
        src = t.get("src", "")
        acc, den = t.get("accept", []), t.get("deny", [])
        if src in members:
            if SERVER_TAG + ":443" in acc:
                covered[src] = True
            if src in adm_m and admin_port and f"{SERVER_TAG}:{admin_port}" in acc:
                admin_covered[src] = True
            if not any(d.endswith(":1433") for d in den):
                errs.append(f"tests[{ti}] ({src}): debe negar al menos {SERVER_TAG}:1433")
            if src in web_m and not (any(d.endswith(":22") for d in den) and any(d.endswith(":3389") for d in den)):
                errs.append(f"tests[{ti}] ({src}): el usuario remoto debe tener negados {SERVER_TAG}:22 y {SERVER_TAG}:3389")
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
    for m, ok in sorted(admin_covered.items()):
        if not ok:
            errs.append(f"ninguna prueba cubre el acceso de administracion autorizado de {m}")
    if not has_outsider:
        errs.append("falta una prueba de una cuenta fuera de los grupos (denegada)")

    # --- acceso efectivo (evaluado, no declarado) ------------------------------------------------------------------------------------
    for m in sorted(members):
        is_admin = m in adm_m
        for port in ALL_PORTS:
            for proto in ("tcp", "udp"):
                if proto == "tcp" and is_admin and port == admin_port:
                    continue
                if can(policy, m, SERVER_TAG, proto, port):
                    errs.append(f"{m} alcanza {SERVER_TAG}:{proto}/{port} (rol {'administrador' if is_admin else 'usuario'}: no corresponde)")
        if is_admin and admin_port and not can(policy, m, SERVER_TAG, "tcp", admin_port):
            errs.append(f"{m} (administrador) no alcanza {SERVER_TAG}:{admin_port}")
        for proto in ("tcp", "udp"):
            if not can(policy, m, SERVER_TAG, proto, 443):
                errs.append(f"{m} no alcanza {SERVER_TAG}:{proto}/443 (la web debe estar disponible para ambos roles)")
        for o in sorted(members - {m}):
            if can(policy, m, o, "tcp", 443):
                errs.append(f"{m} alcanza a {o}: las personas deben estar aisladas entre si")
    return errs


def main(argv):
    rest = argv[1:]
    policy_path = DEFAULT_POLICY
    if "--policy" in rest:
        i = rest.index("--policy")
        if i + 1 >= len(rest):
            print(__doc__)
            return 2
        policy_path = Path(rest[i + 1])
        rest = rest[:i] + rest[i + 2:]
    if "--can" in rest:
        rest = [a for a in rest if a != "--can"]
        if len(rest) != 4 or not rest[3].isdigit():
            print(__doc__)
            return 2
        ok = can(load(policy_path), rest[0], rest[1], rest[2], int(rest[3]))
        print("permitido" if ok else "denegado")
        return 0 if ok else 1
    profile = ""
    if "--profile" in rest:
        i = rest.index("--profile")
        profile = rest[i + 1] if i + 1 < len(rest) else ""
        if profile not in ADMIN_PORT_BY_PROFILE:
            print(f"--profile debe ser uno de {sorted(ADMIN_PORT_BY_PROFILE)}", file=sys.stderr)
            return 2
        rest = rest[:i] + rest[i + 2:]
    flags = {a for a in rest if a.startswith("--")}
    unknown = flags - {"--require-real-identities"}
    if unknown:
        print(f"opcion desconocida: {sorted(unknown)}", file=sys.stderr)
        return 2
    paths = [a for a in rest if not a.startswith("--")]
    path = Path(paths[0]) if paths else policy_path
    try:
        policy = load(path)
    except (OSError, ValueError) as e:
        print(f"ERROR leyendo {path}: {e}", file=sys.stderr)
        return 2
    try:
        errs = validate(policy, "--require-real-identities" in flags, profile)
    except (TypeError, AttributeError, KeyError, ValueError) as e:
        print(f"ERROR: la politica tiene una estructura inesperada ({type(e).__name__}: {e})", file=sys.stderr)
        return 2
    if errs:
        for e in errs:
            print(f"FAIL {e}")
        print(f"{len(errs)} incumplimiento(s) en {path}")
        return 1
    print(f"OK {path}: usuario remoto solo web, administrador remoto web + un puerto de administracion, roles disjuntos, pruebas coherentes")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
