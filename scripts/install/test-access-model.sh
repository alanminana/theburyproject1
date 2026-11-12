#!/usr/bin/env bash
# Pruebas del modelo de acceso del host Linux: la LAN solo llega a la web HTTPS; la administracion solo desde la tailnet (tailscale0) o
# equipos /32. Evalua access-model-audit.py contra capturas SINTETICAS de `ufw status` y `ss -ltn`, y las reglas que genera
# bury-firewall.sh para la LAN. No toca el equipo (sin root, sin red, sin Docker).
#   bash scripts/install/test-access-model.sh        (exit 0 = todo OK)
set -uo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
AUD="$HERE/access-model-audit.py"; FW="$HERE/bury-firewall.sh"
PY="${PYTHON:-python3}"; command -v "$PY" >/dev/null 2>&1 || PY=python
T=$(mktemp -d); trap 'rm -rf "$T"' EXIT
PASS=0 FAIL=0
check() { local n=$1; shift; if "$@"; then PASS=$((PASS+1)); echo "PASS $n"; else FAIL=$((FAIL+1)); echo "FAIL $n"; fi; }
HDR=$'Status: active\n\nTo                         Action      From\n--                         ------      ----'
LISTEN_OK=$'LISTEN 0 4096 0.0.0.0:443 0.0.0.0:*\nLISTEN 0 128 0.0.0.0:22 0.0.0.0:*\nLISTEN 0 4096 127.0.0.1:8080 0.0.0.0:*'
# ufw FILE_CONTENT [LISTEN] -> exit del auditor
aud() { printf '%s\n' "$1" >"$T/ufw.txt"; printf '%s\n' "${2:-$LISTEN_OK}" >"$T/ss.txt"; "$PY" -I "$AUD" --ufw "$T/ufw.txt" --listen "$T/ss.txt" >"$T/out.txt" 2>&1; }
ok()  { aud "$@"; }                 # debe cumplir
ko()  { ! aud "$@"; }               # debe incumplir
said() { grep -q -- "$1" "$T/out.txt"; }

check "python disponible" command -v "$PY"
# --- configuraciones correctas ----------------------------------------------------------------------------------------------------------
check "OK: administrador por tailscale0 (rango de la tailnet)" ok "$HDR"$'\n22/tcp on tailscale0       ALLOW       100.64.0.0/10'
check "OK: administrador por una IP de Tailscale concreta en tailscale0" ok "$HDR"$'\n22/tcp on tailscale0       ALLOW       100.101.2.3'
check "OK: un equipo concreto (el host Hyper-V) por IP /32" ok "$HDR"$'\n22/tcp                     ALLOW       192.168.1.5'
check "OK: sin ninguna regla de administracion (ufw activo, nadie administra por red)" ok "$HDR"
check "OK: la reglas de salida no cuentan" ok "$HDR"$'\n22/tcp                     ALLOW OUT   Anywhere'
# --- la LAN NO puede administrar ----------------------------------------------------------------------------------------------------------
check "FALLA: SSH desde la LAN completa 192.168.1.0/24" ko "$HDR"$'\n22/tcp                     ALLOW       192.168.1.0/24'
check "FALLA: SSH desde 10.0.0.0/8" ko "$HDR"$'\n22/tcp                     ALLOW       10.0.0.0/8'
check "FALLA: SSH desde cualquier origen (Anywhere)" ko "$HDR"$'\n22/tcp                     ALLOW       Anywhere'
check "FALLA: SSH desde Anywhere por IPv6" ko "$HDR"$'\n22/tcp (v6)                ALLOW       Anywhere (v6)'
check "FALLA: RDP 3389 desde la LAN" ko "$HDR"$'\n3389/tcp                   ALLOW       192.168.1.0/24'
check "FALLA: SMB 445 desde la LAN" ko "$HDR"$'\n445/tcp                    ALLOW       192.168.1.0/24'
check "FALLA: WinRM 5985 desde Anywhere" ko "$HDR"$'\n5985/tcp                   ALLOW       Anywhere'
check "FALLA: la tailnet SIN atar a tailscale0 (suplantacion de un origen 100.x desde la LAN)" ko "$HDR"$'\n22/tcp                     ALLOW       100.64.0.0/10'
check "FALLA: rango de puertos que incluye el 22 desde la LAN" ko "$HDR"$'\n20:30/tcp                  ALLOW       192.168.1.0/24'
check "FALLA: todos los puertos permitidos (Anywhere ALLOW Anywhere)" ko "$HDR"$'\nAnywhere                   ALLOW       192.168.1.0/24'
check "FALLA: todos los puertos permitidos desde la tailnet" ko "$HDR"$'\nAnywhere on tailscale0     ALLOW       100.64.0.0/10'
check "FALLA: SQL 1433 abierto aunque sea a un equipo" ko "$HDR"$'\n1433/tcp                   ALLOW       192.168.1.5'
check "FALLA: Kestrel 8080 abierto desde la tailnet" ko "$HDR"$'\n8080/tcp on tailscale0       ALLOW       100.64.0.0/10'
check "FALLA: una regla mala entre varias buenas" ko "$HDR"$'\n22/tcp on tailscale0       ALLOW       100.64.0.0/10\n22/tcp                     ALLOW       192.168.1.0/24'
check "FALLA: ufw inactivo" ko $'Status: inactive'
aud "$HDR"$'\n22/tcp                     ALLOW       192.168.1.0/24'; check "el informe nombra la red completa y la LAN" said "red completa 192.168.1.0/24"
# --- fallo cerrado: formatos reales de ufw que una lectura ingenua dejaria pasar (hallazgos de la revision independiente) ------------------
check "FALLA: perfil OpenSSH desde Anywhere (ufw allow OpenSSH)" ko "$HDR"$'
OpenSSH                    ALLOW       Anywhere'
check "FALLA: perfil OpenSSH (v6) desde Anywhere" ko "$HDR"$'
OpenSSH (v6)               ALLOW       Anywhere (v6)'
check "FALLA: ufw limit ssh (LIMIT) desde Anywhere" ko "$HDR"$'
22/tcp                     LIMIT       Anywhere'
check "FALLA: LIMIT desde la LAN" ko "$HDR"$'
22/tcp                     LIMIT       192.168.1.0/24'
check "FALLA: regla con IP de destino desde la LAN" ko "$HDR"$'
192.168.1.10 22/tcp          ALLOW       192.168.1.0/24'
check "FALLA: columna To larga separada por un solo espacio" ko "$HDR"$'
22,3389,5985/tcp on tailscale0 ALLOW 192.168.1.0/24'
check "FALLA: formato numbered" ko "$HDR"$'
[ 1] 22/tcp                 ALLOW IN    Anywhere'
check "FALLA: lista de puertos con rango mezclado (sin romper el auditor)" ko "$HDR"$'
22,8000:8100/tcp         ALLOW       192.168.1.0/24'
check "FALLA: IP de Tailscale concreta SIN atar a tailscale0 (suplantacion)" ko "$HDR"$'
22/tcp                     ALLOW       100.101.2.3'
check "FALLA: equipo /32 publico (no privado ni de la tailnet)" ko "$HDR"$'
22/tcp                     ALLOW       8.8.8.8'
check "FALLA: perfil/servicio no reconocido desde la LAN (se asume administracion)" ko "$HDR"$'
miservicio                 ALLOW       192.168.1.0/24'
check "FALLA: linea que no se puede interpretar (fallo cerrado)" ko "$HDR"$'
algo raro sin accion'
check "OK: OpenSSH solo por tailscale0" ok "$HDR"$'
OpenSSH on tailscale0       ALLOW       100.64.0.0/10'
check "OK: reglas DENY no cuentan" ok "$HDR"$'
22/tcp                     DENY        192.168.1.0/24'
check "el auditor no produce traceback con entradas raras" bash -c '! grep -q Traceback "$1"' _ "$T/out.txt"
# --- puertos que escuchan -------------------------------------------------------------------------------------------------------------------
check "FALLA: SQL 1433 escucha en 0.0.0.0" ko "$HDR" $'LISTEN 0 4096 0.0.0.0:1433 0.0.0.0:*'
check "FALLA: Kestrel 8080 escucha en todas las interfaces ([::])" ko "$HDR" $'LISTEN 0 4096 [::]:8080 [::]:*'
check "OK: 8080 solo en loopback (override de desarrollo)" ok "$HDR" $'LISTEN 0 4096 127.0.0.1:8080 0.0.0.0:*'
# --- el firewall de puertos publicados: la LAN solo recibe el 443 ----------------------------------------------------------------------
cat >"$T/fw.conf" <<'CONF'
ALLOW_FROM="192.168.1.0/24 100.64.0.0/10@tailscale0"
HTTPS_PORT=443
CONF
fw=$(BURY_FW_CONF="$T/fw.conf" bash "$FW" print 2>&1)
check "bury-firewall: toda regla de la LAN es solo del puerto 443 (DNAT)" bash -c 'grep -- "-s 192.168.1.0/24" <<<"$1" | grep -q -- "--ctorigdstport 443" && ! grep -- "-s 192.168.1.0/24" <<<"$1" | grep -v -- "--ctorigdstport 443"' _ "$fw"
check "bury-firewall: ninguna regla menciona 22, 3389, 1433 ni 8080" bash -c '! grep -E -- "(dport|ctorigdstport) (22|3389|1433|8080|445|5985)\b" <<<"$1"' _ "$fw"
check "bury-firewall: la regla de la LAN no esta atada a tailscale0 y la de la tailnet si" bash -c 'grep -- "-s 192.168.1.0/24" <<<"$1" | grep -vq -- "-i tailscale0" && grep -- "-s 100.64.0.0/10" <<<"$1" | grep -q -- "-i tailscale0"' _ "$fw"
check "bury-firewall: lo demas se descarta (DROP del 443 para origenes no listados)" bash -c 'grep -q -- "-j DROP" <<<"$1"' _ "$fw"
echo; echo "access model tests: $PASS PASS, $FAIL FAIL"; (( FAIL == 0 ))
