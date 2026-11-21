#!/usr/bin/env bash
# P-05: pruebas DINAMICAS de red desde un origen NO autorizado contra el servidor Linux/VM de ENSAYO. No cambia reglas de firewall, ni la NIC, ni Docker.
# Crea un namespace de red temporal con un veth (10.77.0.0/30) y desde el intenta conectar al servidor: la fuente 10.77.0.2 no esta autorizada para
# nada (ni 22 ni 443). Lee los contadores de ufw/BURY-ERP/DOCKER-USER antes y despues para demostrar QUE REGLA descarto el paquete (no solo "timeout").
# Luego (opcional --reinicios) reinicia SERVICIOS individuales (caddy, app y `ufw reload`) y comprueba que las restricciones persisten.
#
#   sudo bash p05_red_vm.sh --confirmo-entorno-de-ensayo [--reinicios]
#
# Garantias: todo lo creado (namespace, veth, IP) se elimina con `trap` aunque falle; el SSH actual no se toca (no hay cambios de reglas);
# `ufw reload` conserva las conexiones establecidas. Salida: 0 todo OK | 1 hay FALLA | 2 uso/guarda.
set -uo pipefail
[[ "${1:-}" == "--confirmo-entorno-de-ensayo" ]] || { echo "guarda: falta --confirmo-entorno-de-ensayo"; exit 2; }
REINICIOS=0; [[ "${2:-}" == "--reinicios" ]] && REINICIOS=1
[[ $EUID -eq 0 ]] || { echo "ejecutar con sudo"; exit 2; }
command -v ip >/dev/null && command -v iptables >/dev/null || { echo "faltan ip/iptables"; exit 2; }

NS=p05out$$; V0=p05a$$; V1=p05b$$; SRV=10.77.0.1; CLI=10.77.0.2
OK=0; FAIL=0
chk() { if [[ "$2" == ok ]]; then OK=$((OK+1)); echo "[OK] $1 :: $3"; else FAIL=$((FAIL+1)); echo "[FALLA] $1 :: $3"; fi; }
cleanup() { ip netns del "$NS" >/dev/null 2>&1; ip link del "$V0" >/dev/null 2>&1; }
trap cleanup EXIT INT TERM
ip netns add "$NS" && ip link add "$V0" type veth peer name "$V1" && ip link set "$V1" netns "$NS" \
  && ip addr add "$SRV/30" dev "$V0" && ip link set "$V0" up \
  && ip netns exec "$NS" ip addr add "$CLI/30" dev "$V1" && ip netns exec "$NS" ip link set "$V1" up && ip netns exec "$NS" ip link set lo up \
  || { echo "no se pudo preparar el namespace"; exit 2; }

probe() { # probe PUERTO -> open|closed|silent  (desde la fuente no autorizada)
  local out rc
  out=$(ip netns exec "$NS" timeout 5 bash -c "exec 3<>/dev/tcp/$SRV/$1" 2>&1); rc=$?
  if (( rc == 0 )); then echo open; elif [[ "$out" == *"refused"* || "$out" == *"rechaz"* ]]; then echo closed; else echo silent; fi
}
pkts() { iptables -vnxL "$1" 2>/dev/null | awk -v pat="$2" '$0 ~ pat {print $1; exit}'; }
bury_drop_443() { iptables -vnxL BURY-ERP 2>/dev/null | awk '/DROP/ && /ctorigdstport 443/ && /tcp/ {print $1; exit}'; }
ufw_blocks() { journalctl -k --since "-2min" --no-pager 2>/dev/null | grep -c "UFW BLOCK.*SRC=$CLI" || true; }

echo "origen no autorizado: $CLI (namespace $NS) -> servidor $SRV"
D0=$(bury_drop_443); B0=$(ufw_blocks)
declare -A R
for port in 22 80 443 1433 8080 2019 3389 445; do R[$port]=$(probe "$port"); done
D1=$(bury_drop_443); B1=$(ufw_blocks)
for port in 22 80 1433 8080 2019 3389 445; do
  chk "origen ajeno -> $port/tcp sin servicio" "$([[ ${R[$port]} != open ]] && echo ok || echo fail)" "resultado=${R[$port]}"
done
chk "origen ajeno -> 443/tcp (web) NO responde (BURY-ERP)" "$([[ ${R[443]} != open ]] && echo ok || echo fail)" "resultado=${R[443]}"
chk "BURY-ERP descarto paquetes de 443 del origen ajeno (contador DROP ${D0:-?} -> ${D1:-?})" "$([[ -n "$D0" && -n "$D1" && "$D1" -gt "$D0" ]] && echo ok || echo fail)" "regla DROP tcp dpt:443"
chk "ufw registro bloqueos del origen ajeno ($B0 -> $B1; 22/80/1433/8080/...)" "$([[ "$B1" -gt "$B0" ]] && echo ok || echo fail)" "[UFW BLOCK] SRC=$CLI"

# reglas efectivas (sin cambios): politica por defecto y reglas de administracion
POL=$(iptables -S INPUT | head -1)
chk "politica INPUT por defecto" "$([[ "$POL" == "-P INPUT DROP" ]] && echo ok || echo fail)" "$POL"
chk "ufw: 22/tcp solo desde equipos /32 concretos (sin 'Anywhere' ni rangos)" "$([[ -z "$(ufw status | grep -E '^22' | grep -E 'Anywhere|/[0-9]+ ' | grep -v '/32')" ]] && echo ok || echo fail)" "$(ufw status | grep -E '^22' | tr -s ' ' | tr '\n' ';')"

if (( REINICIOS )); then
  echo "--- reinicios de servicios individuales (las restricciones deben persistir)"
  cd "$(getent passwd "${SUDO_USER:-root}" | cut -d: -f6)/theburyproject1" 2>/dev/null || { echo "no encuentro el repo"; exit 2; }
  snap() { { iptables -S DOCKER-USER; iptables -S BURY-ERP; iptables -S INPUT | head -1; ufw status | sed -n '/^22/p'; } | sha256sum | cut -c1-16; }
  S0=$(snap)
  web_ok() { # live y ready por el host virtual correcto (SNI tbp) a traves de Caddy
    local u
    for u in live ready; do
      [[ "$(curl -sk -o /dev/null -w '%{http_code}' --max-time 4 --resolve tbp:443:127.0.0.1 "https://tbp/health/$u" 2>/dev/null)" == 200 ]] || return 1
    done
  }
  espera_web() { local i; for i in $(seq 1 60); do web_ok && return 0; sleep 3; done; return 1; }
  for svc in caddy app; do
    sudo -u "${SUDO_USER:-root}" docker compose restart "$svc" >/dev/null 2>&1; rc=$?
    chk "reinicio de $svc: docker compose restart termino sin error" "$([[ $rc -eq 0 ]] && echo ok || echo fail)" "rc=$rc"
    espera_web; rcw=$?
    chk "reinicio de $svc: el ERP vuelve a responder live y ready (200 por https://tbp)" "$([[ $rcw -eq 0 ]] && echo ok || echo fail)" "espera hasta 180 s"
    S1=$(snap)
    chk "reinicio de $svc: reglas (DOCKER-USER, BURY-ERP, INPUT, ufw 22) identicas" "$([[ "$S0" == "$S1" ]] && echo ok || echo fail)" "huella $S0 -> $S1"
    [[ "$(probe 443)" != open ]] && r443=ok || r443=fail
    chk "reinicio de $svc: origen ajeno sigue SIN llegar a 443" "$r443" ""
    [[ "$(probe 1433)" != open && "$(probe 8080)" != open ]] && rr=ok || rr=fail
    chk "reinicio de $svc: 1433 y 8080 siguen inalcanzables" "$rr" ""
  done
  ufw reload >/dev/null 2>&1; rcu=$?
  chk "ufw reload termino sin error" "$([[ $rcu -eq 0 ]] && echo ok || echo fail)" "rc=$rcu"
  S2=$(snap)
  chk "ufw reload: reglas identicas y la sesion SSH actual sigue viva" "$([[ "$S0" == "$S2" ]] && echo ok || echo fail)" "huella $S0 -> $S2"
  web_ok; chk "tras ufw reload el ERP sigue respondiendo (live y ready 200)" "$([[ $? -eq 0 ]] && echo ok || echo fail)" ""
fi
echo "---"
echo "P-05 red (origen no autorizado): $OK OK, $FAIL FALLA"
(( FAIL == 0 ))
