#!/usr/bin/env bash
# Pruebas de la politica de Tailscale (deploy/tailscale/policy.hujson = perfil linux-vm; policy.windows.hujson = perfil windows).
# Cubre: acceso autorizado por rol (usuario remoto = solo web; administrador remoto = web + administracion), acceso denegado,
# aislamiento entre personas, revocacion individual de cada rol y rechazo de politicas demasiado permisivas.
# Offline, sin Tailscale ni red; solo Python 3 (biblioteca estandar).
#   bash scripts/install/test-tailscale-policy.sh        (exit 0 = todo OK)
set -uo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CHK="$HERE/tailscale-policy-check.py"
POL="$HERE/../../deploy/tailscale/policy.hujson"
POLW="$HERE/../../deploy/tailscale/policy.windows.hujson"
PY="${PYTHON:-python3}"; command -v "$PY" >/dev/null 2>&1 || PY=python
T=$(mktemp -d); trap 'rm -rf "$T"' EXIT
PASS=0 FAIL=0
ok()  { PASS=$((PASS+1)); echo "PASS $1"; }
bad() { FAIL=$((FAIL+1)); echo "FAIL $1"; }
check() { local name=$1; shift; if "$@"; then ok "$name"; else bad "$name"; fi; }
chk() { "$PY" -I "$CHK" "$@" >/dev/null 2>&1; }
# can POLITICA USUARIO DESTINO PROTO PUERTO  (exit 0 = permitido)
can() { "$PY" -I "$CHK" --policy "$1" --can "${@:2}" >/dev/null 2>&1; }
cannot() { ! can "$@"; }

U=usuario-remoto@example.com   # usuario remoto (solo web)
A=admin-remoto@example.com     # administrador remoto (web + administracion)

# mut POLITICA 'EXPRESION PYTHON sobre p' -> escribe $T/m.json (JSON valido = HuJSON valido) a partir de la politica indicada
mut() {
  "$PY" -I - "$CHK" "$1" "$T/m.json" "$2" <<'PYEOF'
import importlib.util, json, sys
spec = importlib.util.spec_from_file_location("chk", sys.argv[1]); m = importlib.util.module_from_spec(spec); spec.loader.exec_module(m)
from pathlib import Path
p = m.load(Path(sys.argv[2]))
exec(sys.argv[4])
Path(sys.argv[3]).write_text(json.dumps(p), encoding="utf-8")
PYEOF
}
rejects()  { mut "$POL"  "$1" && ! chk "$T/m.json"; }   # la mutacion (perfil linux-vm) debe producir una politica RECHAZADA
rejectsw() { mut "$POLW" "$1" && ! chk "$T/m.json"; }   # idem sobre el perfil windows
accepts()  { mut "$POL"  "$1" && chk "$T/m.json"; }

check "python disponible" command -v "$PY"
check "perfil linux-vm: la politica versionada es valida" chk "$POL" --profile linux-vm
check "perfil windows: la politica versionada es valida" chk "$POLW" --profile windows
check "politica por defecto (sin argumentos) valida" chk
check "perfil linux-vm NO acepta el archivo del perfil windows" bash -c '! "$1" -I "$2" "$3" --profile linux-vm >/dev/null 2>&1' _ "$PY" "$CHK" "$POLW"
check "perfil windows NO acepta el archivo del perfil linux-vm" bash -c '! "$1" -I "$2" "$3" --profile windows >/dev/null 2>&1' _ "$PY" "$CHK" "$POL"
check "las politicas versionadas NO pasan --require-real-identities (correos de ejemplo)" bash -c '! "$1" -I "$2" --require-real-identities >/dev/null 2>&1 && ! "$1" -I "$2" "$3" --require-real-identities >/dev/null 2>&1' _ "$PY" "$CHK" "$POLW"

# Los dos perfiles solo pueden diferir en el puerto de administracion: se intercambian 22<->3389 en el perfil windows y se compara (listas ordenadas).
check "los dos perfiles solo difieren en el puerto de administracion (sin deriva)" "$PY" -I - "$CHK" "$POL" "$POLW" <<'PYEOF'
import importlib.util, json, re, sys
from pathlib import Path
spec = importlib.util.spec_from_file_location("chk", sys.argv[1]); m = importlib.util.module_from_spec(spec); spec.loader.exec_module(m)
def norm(path, swap):
    s = json.dumps(m.load(Path(path)))
    if swap:
        s = re.sub(r"(tcp:|tag:erp:)(22|3389)\b", lambda x: x.group(1) + ("3389" if x.group(2) == "22" else "22"), s)
    p = json.loads(s)
    for g in p["grants"]: g["ip"].sort()
    for t in p["tests"]:
        t["accept"] = sorted(t.get("accept", [])); t["deny"] = sorted(t.get("deny", []))
    return p
sys.exit(0 if norm(sys.argv[2], False) == norm(sys.argv[3], True) else 1)
PYEOF

# --- acceso autorizado / denegado por rol (evaluador sobre la politica real; ambos perfiles) -------------------------------------------
for prof in linux-vm windows; do
  if [[ $prof == linux-vm ]]; then P=$POL MG=22 NOMG=3389; else P=$POLW MG=3389 NOMG=22; fi
  for proto in tcp udp; do
    check "[$prof] usuario remoto -> tag:erp:443/$proto permitido" can "$P" "$U" tag:erp "$proto" 443
    check "[$prof] administrador remoto -> tag:erp:443/$proto permitido" can "$P" "$A" tag:erp "$proto" 443
  done
  check "[$prof] administrador remoto -> tag:erp:$MG/tcp (administracion) permitido" can "$P" "$A" tag:erp tcp "$MG"
  check "[$prof] administrador remoto -> tag:erp:$MG/udp denegado (la administracion es solo TCP)" cannot "$P" "$A" tag:erp udp "$MG"
  check "[$prof] administrador remoto -> tag:erp:$NOMG denegado (un solo puerto de administracion por perfil)" cannot "$P" "$A" tag:erp tcp "$NOMG"
  for port in 22 23 80 135 445 1433 3389 5432 5985 5986 8080; do
    check "[$prof] usuario remoto -> tag:erp:$port denegado (solo web)" cannot "$P" "$U" tag:erp tcp "$port"
  done
  for port in 80 135 445 1433 5432 5985 5986 8080; do
    check "[$prof] administrador remoto -> tag:erp:$port denegado" cannot "$P" "$A" tag:erp tcp "$port"
  done
  check "[$prof] usuario NO alcanza al administrador (aislamiento)" cannot "$P" "$U" "$A" tcp 443
  check "[$prof] administrador NO alcanza al usuario (aislamiento)" cannot "$P" "$A" "$U" tcp 443
  check "[$prof] cuenta fuera de los grupos: denegada en 443 y en administracion" bash -c '! "$1" -I "$2" --policy "$3" --can intruso@example.org tag:erp tcp 443 >/dev/null 2>&1 && ! "$1" -I "$2" --policy "$3" --can intruso@example.org tag:erp tcp "$4" >/dev/null 2>&1' _ "$PY" "$CHK" "$P" "$MG"
  check "[$prof] dispositivo etiquetado (tag:erp) no origina trafico hacia el ERP" cannot "$P" tag:erp tag:erp tcp 443
done

# --- revocacion individual (cada rol por separado) --------------------------------------------------------------------------------------
cat >"$T/eval.py" <<'PYEOF'
import importlib.util, sys
from pathlib import Path
spec = importlib.util.spec_from_file_location("chk", sys.argv[1]); m = importlib.util.module_from_spec(spec); spec.loader.exec_module(m)
p = m.load(Path(sys.argv[2]))
sys.exit(0 if m.can(p, sys.argv[3], "tag:erp", "tcp", int(sys.argv[4])) else 1)
PYEOF
ev() { "$PY" -I "$T/eval.py" "$CHK" "$T/m.json" "$1" "$2" >/dev/null 2>&1; }   # ev USUARIO PUERTO sobre $T/m.json (exit 0 = permitido)
nev() { ! ev "$@"; }

mut "$POL" 'p["groups"]["group:erp-usuarios"].remove("usuario-remoto@example.com")' || bad "mutacion: revocar usuario remoto"
check "revocado el usuario remoto: ya NO alcanza tag:erp:443" nev "$U" 443
check "revocado el usuario remoto: el administrador SIGUE con web y administracion" bash -c 'ev() { "$1" -I "$2" "$3" "$4" "$5" "$6" >/dev/null 2>&1; }; ev "$@" admin-remoto@example.com 443 && ev "$@" admin-remoto@example.com 22' _ "$PY" "$T/eval.py" "$CHK" "$T/m.json"
mut "$POL" 'p["groups"]["group:erp-admins"].remove("admin-remoto@example.com")' || bad "mutacion: revocar administrador remoto"
check "revocado el administrador: pierde la web" nev "$A" 443
check "revocado el administrador: pierde la administracion (22)" nev "$A" 22
check "revocado el administrador: el usuario remoto SIGUE con la web" ev "$U" 443
mut "$POL" 'p["groups"]["group:erp-admins"] = []'
check "grupo de administradores vacio: nadie administra" nev "$A" 22
mut "$POL" 'p["groups"]["group:erp-usuarios"] = []; p["groups"]["group:erp-admins"] = []'
check "ambos grupos vacios: nadie alcanza el ERP" bash -c '! "$1" -I "$2" "$3" "$4" "$5" "$6" >/dev/null 2>&1 && ! "$1" -I "$2" "$3" "$4" "$7" "$6" >/dev/null 2>&1' _ "$PY" "$T/eval.py" "$CHK" "$T/m.json" "$U" 443 "$A"
# degradar (administrador -> usuario): deja de administrar pero conserva la web
mut "$POL" 'p["groups"]["group:erp-admins"].remove("admin-remoto@example.com"); p["groups"]["group:erp-usuarios"].append("admin-remoto@example.com")'
check "administrador movido a usuarios: conserva la web" ev "$A" 443
check "administrador movido a usuarios: pierde la administracion" nev "$A" 22

# --- politicas demasiado permisivas: deben ser RECHAZADAS ------------------------------------------------------------------------
check "rechaza al usuario remoto con SSH 22" rejects 'p["grants"][0]["ip"]=["tcp:443","udp:443","tcp:22"]'
check "rechaza al usuario remoto con RDP 3389" rejects 'p["grants"][0]["ip"]=["tcp:443","udp:443","tcp:3389"]'
check "rechaza al usuario remoto sin HTTP/3 (udp:443)" rejects 'p["grants"][0]["ip"]=["tcp:443"]'
check "rechaza al administrador con SQL 1433" rejects 'p["grants"][1]["ip"]=["tcp:443","udp:443","tcp:22","tcp:1433"]'
check "rechaza al administrador con Kestrel 8080" rejects 'p["grants"][1]["ip"]=["tcp:443","udp:443","tcp:22","tcp:8080"]'
check "rechaza al administrador con SSH y RDP a la vez (un solo puerto)" rejects 'p["grants"][1]["ip"]=["tcp:443","udp:443","tcp:22","tcp:3389"]'
check "rechaza al administrador sin puerto de administracion" rejects 'p["grants"][1]["ip"]=["tcp:443","udp:443"]'
check "rechaza al administrador sin la web" rejects 'p["grants"][1]["ip"]=["tcp:22"]'
check "rechaza administracion por udp" rejects 'p["grants"][1]["ip"]=["tcp:443","udp:443","udp:22"]'
check "rechaza ip * (todos los puertos)" rejects 'p["grants"][1]["ip"]=["*"]'
check "rechaza rango de puertos" rejects 'p["grants"][1]["ip"]=["tcp:443","udp:443","tcp:1-1024"]'
check "rechaza puerto sin protocolo (22 = tcp+udp+icmp)" rejects 'p["grants"][1]["ip"]=["tcp:443","udp:443","22"]'
check "rechaza grant con dst *" rejects 'p["grants"][0]["dst"]=["*"]'
check "rechaza grant del administrador con dst *" rejects 'p["grants"][1]["dst"]=["*"]'
check "rechaza grant con src *" rejects 'p["grants"][0]["src"]=["*"]'
check "rechaza grant con src autogroup:member" rejects 'p["grants"][0]["src"]=["autogroup:member"]'
check "rechaza grant con src autogroup:admin (administrador de la tailnet != administrador del ERP)" rejects 'p["grants"][1]["src"]=["autogroup:admin"]'
check "rechaza grant con src tag:erp (maquina)" rejects 'p["grants"][0]["src"]=["tag:erp"]'
check "rechaza grant con src individual (usar grupos)" rejects 'p["grants"][1]["src"]=["admin-remoto@example.com"]'
check "rechaza grant con dos grupos en src" rejects 'p["grants"][0]["src"]=["group:erp-usuarios","group:erp-admins"]'
check "rechaza grant extra para el grupo de usuarios" rejects 'p["grants"].append({"src":["group:erp-usuarios"],"dst":["tag:erp"],"ip":["tcp:22"]})'
check "rechaza grant extra para el grupo de administradores" rejects 'p["grants"].append({"src":["group:erp-admins"],"dst":["tag:erp"],"ip":["tcp:3389"]})'
check "rechaza ACL legacy *:*" rejects 'p["acls"]=[{"action":"accept","src":["*"],"dst":["*:*"]}]'
check "rechaza autoApprovers (subnet router / exit node)" rejects 'p["autoApprovers"]={"exitNode":["tag:erp"],"routes":{"192.168.0.0/16":["tag:erp"]}}'
check "rechaza reglas ssh de Tailscale" rejects 'p["ssh"]=[{"action":"accept","src":["*"],"dst":["tag:erp"],"users":["root"]}]'
check "rechaza Funnel" rejects 'p["nodeAttrs"]=[{"target":["tag:erp"],"attr":["funnel"]}]'
check "rechaza grant usuario->usuario" rejects 'p["grants"].append({"src":["group:erp-admins"],"dst":["usuario-remoto@example.com"],"ip":["tcp:443"]})'
check "rechaza tag:erp propiedad de los usuarios del ERP" rejects 'p["tagOwners"]["tag:erp"]=["group:erp-usuarios"]'
check "rechaza tag:erp propiedad de los administradores del ERP" rejects 'p["tagOwners"]["tag:erp"]=["group:erp-admins"]'
check "rechaza tag adicional en tagOwners" rejects 'p["tagOwners"]["tag:admin"]=["autogroup:admin"]'
check "rechaza una misma identidad en usuarios y administradores (roles no disjuntos)" rejects 'p["groups"]["group:erp-usuarios"].append("admin-remoto@example.com")'
check "rechaza una sola identidad (se exigen 2 independientes)" rejects 'p["groups"]["group:erp-usuarios"]=["admin-remoto@example.com"]; p["groups"]["group:erp-admins"]=["admin-remoto@example.com"]'
check "rechaza la misma identidad en ambos grupos distinguida solo por mayusculas" rejects 'p["groups"]["group:erp-usuarios"].append("Admin-Remoto@example.com")'
check "estructura inesperada (grants como objeto): error controlado (exit 2), sin traceback" bash -c 'printf "{\"groups\":{},\"grants\":{\"a\":1},\"tests\":[],\"tagOwners\":{}}" >"$1"; out=$("$2" -I "$3" "$1" 2>&1); rc=$?; [[ $rc == 2 || $rc == 1 ]] && ! grep -q Traceback <<<"$out"' _ "$T/tipos.json" "$PY" "$CHK"
check "rechaza sin administrador remoto" rejects 'p["groups"]["group:erp-admins"]=[]'
check "rechaza sin usuario remoto" rejects 'p["groups"]["group:erp-usuarios"]=[]'
check "rechaza un grupo adicional" rejects 'p["groups"]["group:todos"]=["usuario-remoto@example.com"]'
check "rechaza politica sin tests" rejects 'p["tests"]=[]'
check "rechaza test que contradice la politica (usuario con 22)" rejects 'p["tests"][0]["accept"].append("tag:erp:22")'
check "rechaza test que contradice la politica (administrador sin 22)" rejects 'p["tests"][1]["accept"]=["tag:erp:443"]'
check "rechaza sin prueba de cuenta ajena a los grupos" rejects 'p["tests"]=p["tests"][:2]'
check "rechaza sin prueba del acceso de administracion" rejects 'p["tests"][1]["accept"]=["tag:erp:443"]; p["grants"][1]["ip"]=["tcp:443","udp:443","tcp:22"]'
check "rechaza grants vacios" rejects 'p["grants"]=[]'
check "rechaza clave de nivel superior no prevista (nodeAttrs)" rejects 'p["nodeAttrs"]=[{"target":["*"],"attr":["mullvad"]}]'
check "rechaza clave de nivel superior no prevista (hosts)" rejects 'p["hosts"]={"x":"1.2.3.4"}'
check "rechaza identidad con comodin (*@dominio)" rejects 'p["groups"]["group:erp-usuarios"]=["*@empresa.com"]'
check "[windows] rechaza al usuario remoto con RDP" rejectsw 'p["grants"][0]["ip"]=["tcp:443","udp:443","tcp:3389"]'
check "[windows] rechaza al administrador con SQL 1433" rejectsw 'p["grants"][1]["ip"]=["tcp:443","udp:443","tcp:3389","tcp:1433"]'
check "[windows] rechaza al administrador con SMB 445" rejectsw 'p["grants"][1]["ip"]=["tcp:443","udp:443","tcp:3389","tcp:445"]'
check "[windows] rechaza al administrador con WinRM 5985" rejectsw 'p["grants"][1]["ip"]=["tcp:443","udp:443","tcp:3389","tcp:5985"]'
check "rechaza claves duplicadas en el JSON" bash -c 'printf "{\"grants\":[],\"grants\":[]}" >"$1"; "$2" -I "$3" "$1" >/dev/null 2>&1; [[ $? == 2 ]]' _ "$T/dup.json" "$PY" "$CHK"
check "rechaza JSON roto (exit 2)" bash -c 'echo "{ roto" >"$1"; "$2" -I "$3" "$1" >/dev/null 2>&1; [[ $? == 2 ]]' _ "$T/roto.json" "$PY" "$CHK"
# control: una mutacion inocua sigue siendo valida (el validador no rechaza todo)
REAL='import json as _j; s=_j.dumps(p).replace("usuario-remoto@example.com","ana@empresa.com.ar").replace("admin-remoto@example.com","luis@empresa.com.ar"); p.clear(); p.update(_j.loads(s))'
check "acepta una politica equivalente con otros correos" accepts "$REAL"
mut "$POL" "$REAL"
check "identidades reales pasan --require-real-identities" chk "$T/m.json" --require-real-identities --profile linux-vm

echo
echo "tailscale policy tests: $PASS PASS, $FAIL FAIL"
(( FAIL == 0 ))
