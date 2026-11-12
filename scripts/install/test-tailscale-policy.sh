#!/usr/bin/env bash
# Pruebas de la politica de Tailscale (deploy/tailscale/policy.hujson): acceso autorizado, acceso denegado, aislamiento entre personas,
# revocacion individual y rechazo de politicas demasiado permisivas. Offline, sin Tailscale ni red; solo Python 3 (biblioteca estandar).
#   bash scripts/install/test-tailscale-policy.sh        (exit 0 = todo OK)
set -uo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CHK="$HERE/tailscale-policy-check.py"
POL="$HERE/../../deploy/tailscale/policy.hujson"
PY="${PYTHON:-python3}"; command -v "$PY" >/dev/null 2>&1 || PY=python
T=$(mktemp -d); trap 'rm -rf "$T"' EXIT
PASS=0 FAIL=0
ok()  { PASS=$((PASS+1)); echo "PASS $1"; }
bad() { FAIL=$((FAIL+1)); echo "FAIL $1"; }
check() { local name=$1; shift; if "$@"; then ok "$name"; else bad "$name"; fi; }
chk() { "$PY" -I "$CHK" "$@" >/dev/null 2>&1; }
can() { "$PY" -I "$CHK" --can "$@" >/dev/null 2>&1; }

# mut 'EXPRESION PYTHON sobre p' -> escribe $T/m.json (JSON valido = HuJSON valido) a partir de la politica versionada
mut() {
  "$PY" -I - "$CHK" "$POL" "$T/m.json" "$1" <<'PYEOF'
import importlib.util, json, sys
spec = importlib.util.spec_from_file_location("chk", sys.argv[1]); m = importlib.util.module_from_spec(spec); spec.loader.exec_module(m)
from pathlib import Path
p = m.load(Path(sys.argv[2]))
exec(sys.argv[4])
Path(sys.argv[3]).write_text(json.dumps(p), encoding="utf-8")
PYEOF
}
rejects() { mut "$1" && ! chk "$T/m.json"; }   # la mutacion debe producir una politica RECHAZADA
accepts() { mut "$1" && chk "$T/m.json"; }

check "python disponible" command -v "$PY"
check "la politica versionada es valida" chk
check "la politica versionada NO pasa --require-real-identities (correos de ejemplo)" bash -c '! "$1" -I "$2" --require-real-identities >/dev/null 2>&1' _ "$PY" "$CHK"

# --- acceso autorizado / denegado / aislamiento (evaluador sobre la politica real) -----------------------------------------------------
for u in persona1@example.com persona2@example.com; do
  check "$u -> tag:erp:443/tcp permitido" can "$u" tag:erp tcp 443
  check "$u -> tag:erp:443/udp (HTTP/3) permitido" can "$u" tag:erp udp 443
  for port in 22 80 1433 3389 5432 8080; do check "$u -> tag:erp:$port denegado" bash -c '! "$1" -I "$2" --can "$3" tag:erp tcp "$4" >/dev/null 2>&1' _ "$PY" "$CHK" "$u" "$port"; done
done
check "persona1 NO alcanza a persona2 (aislamiento)" bash -c '! "$1" -I "$2" --can persona1@example.com persona2@example.com tcp 443 >/dev/null 2>&1' _ "$PY" "$CHK"
check "persona2 NO alcanza a persona1 (aislamiento)" bash -c '! "$1" -I "$2" --can persona2@example.com persona1@example.com tcp 443 >/dev/null 2>&1' _ "$PY" "$CHK"
check "cuenta fuera del grupo: denegada en 443" bash -c '! "$1" -I "$2" --can intruso@example.org tag:erp tcp 443 >/dev/null 2>&1' _ "$PY" "$CHK"
check "dispositivo etiquetado (tag:erp) no origina trafico hacia el ERP" bash -c '! "$1" -I "$2" --can tag:erp tag:erp tcp 443 >/dev/null 2>&1' _ "$PY" "$CHK"

# --- revocacion individual -------------------------------------------------------------------------------------------------------
mut 'p["groups"]["group:erp-usuarios"].remove("persona1@example.com")' || bad "mutacion de revocacion"
cat >"$T/eval.py" <<'PYEOF'
import importlib.util, sys
from pathlib import Path
spec = importlib.util.spec_from_file_location("chk", sys.argv[1]); m = importlib.util.module_from_spec(spec); spec.loader.exec_module(m)
p = m.load(Path(sys.argv[2]))
sys.exit(0 if m.can(p, sys.argv[3], "tag:erp", "tcp", 443) else 1)
PYEOF
ev() { "$PY" -I "$T/eval.py" "$CHK" "$1" "$2" >/dev/null 2>&1; }   # ev POLITICA USUARIO (exit 0 = permitido)
check "revocada persona1: ya NO alcanza tag:erp:443" bash -c '! "$1" -I "$2" "$3" "$4" "$5" >/dev/null 2>&1' _ "$PY" "$T/eval.py" "$CHK" "$T/m.json" persona1@example.com
check "revocada persona1: persona2 SIGUE con acceso (revocacion individual)" ev "$T/m.json" persona2@example.com
mut 'p["groups"]["group:erp-usuarios"] = []'
check "grupo vacio: nadie alcanza el ERP" bash -c '! "$1" -I "$2" "$3" "$4" "$5" >/dev/null 2>&1' _ "$PY" "$T/eval.py" "$CHK" "$T/m.json" persona2@example.com

# --- politicas demasiado permisivas: deben ser RECHAZADAS ------------------------------------------------------------------------
check "rechaza grant con dst * " rejects 'p["grants"][0]["dst"]=["*"]'
check "rechaza grant con src *" rejects 'p["grants"][0]["src"]=["*"]'
check "rechaza grant con src autogroup:member" rejects 'p["grants"][0]["src"]=["autogroup:member"]'
check "rechaza grant con src tag:erp (maquina)" rejects 'p["grants"][0]["src"]=["tag:erp"]'
check "rechaza ip * (todos los puertos)" rejects 'p["grants"][0]["ip"]=["*"]'
check "rechaza SSH 22" rejects 'p["grants"][0]["ip"]=["tcp:443","tcp:22"]'
check "rechaza SQL 1433" rejects 'p["grants"][0]["ip"]=["tcp:1433"]'
check "rechaza rango de puertos" rejects 'p["grants"][0]["ip"]=["tcp:1-1024"]'
check "rechaza puerto sin protocolo (443 = tcp+udp+icmp)" rejects 'p["grants"][0]["ip"]=["443"]'
check "rechaza ACL legacy *:*" rejects 'p["acls"]=[{"action":"accept","src":["*"],"dst":["*:*"]}]'
check "rechaza autoApprovers (subnet router / exit node)" rejects 'p["autoApprovers"]={"exitNode":["tag:erp"],"routes":{"192.168.0.0/16":["tag:erp"]}}'
check "rechaza reglas ssh" rejects 'p["ssh"]=[{"action":"accept","src":["*"],"dst":["tag:erp"],"users":["root"]}]'
check "rechaza Funnel" rejects 'p["nodeAttrs"]=[{"target":["tag:erp"],"attr":["funnel"]}]'
check "rechaza grant usuario->usuario" rejects 'p["grants"].append({"src":["persona1@example.com"],"dst":["persona2@example.com"],"ip":["tcp:443"]})'
check "rechaza tag:erp propiedad de los usuarios del ERP" rejects 'p["tagOwners"]["tag:erp"]=["group:erp-usuarios"]'
check "rechaza una sola identidad (se exigen 2 independientes)" rejects 'p["groups"]["group:erp-usuarios"]=["persona1@example.com"]'
check "rechaza politica sin tests" rejects 'p["tests"]=[]'
check "rechaza test que contradice la politica" rejects 'p["tests"][0]["accept"].append("tag:erp:22")'
check "rechaza sin prueba de cuenta ajena al grupo" rejects 'p["tests"]=p["tests"][:2]'
check "rechaza grants vacios" rejects 'p["grants"]=[]'
check "rechaza clave de nivel superior no prevista (nodeAttrs)" rejects 'p["nodeAttrs"]=[{"target":["*"],"attr":["mullvad"]}]'
check "rechaza clave de nivel superior no prevista (hosts)" rejects 'p["hosts"]={"x":"1.2.3.4"}'
check "rechaza identidad con comodin (*@dominio)" rejects 'p["groups"]["group:erp-usuarios"]=["*@empresa.com","luis@empresa.com"]'
check "rechaza claves duplicadas en el JSON" bash -c 'printf "{\"grants\":[],\"grants\":[]}" >"$1"; "$2" -I "$3" "$1" >/dev/null 2>&1; [[ $? == 2 ]]' _ "$T/dup.json" "$PY" "$CHK"
check "rechaza JSON roto (exit 2)" bash -c 'echo "{ roto" >"$1"; "$2" -I "$3" "$1" >/dev/null 2>&1; [[ $? == 2 ]]' _ "$T/roto.json" "$PY" "$CHK"
# control: una mutacion inocua sigue siendo valida (el validador no rechaza todo)
check "acepta una politica equivalente con otros correos" accepts 'import json as _j; s=_j.dumps(p).replace("persona1@example.com","ana@empresa.com.ar").replace("persona2@example.com","luis@empresa.com.ar"); p.clear(); p.update(_j.loads(s))'
mut 'import json as _j; s=_j.dumps(p).replace("persona1@example.com","ana@empresa.com.ar").replace("persona2@example.com","luis@empresa.com.ar"); p.clear(); p.update(_j.loads(s))'
check "identidades reales pasan --require-real-identities" bash -c '"$1" -I "$2" "$3" --require-real-identities >/dev/null 2>&1' _ "$PY" "$CHK" "$T/m.json"

echo
echo "tailscale policy tests: $PASS PASS, $FAIL FAIL"
(( FAIL == 0 ))
