#!/usr/bin/env bash
# Prueba REAL (no simulada) de que bury-firewall.sh restringe un puerto PUBLICADO por Docker, con clientes en redes distintas:
# "tailnet" (100.64.88.0/24, bridge llamado tailscale0), "LAN" (192.168.77.0/24) y "ajeno" (10.99.77.0/24).
# Datos sinteticos, sin tocar el stack del ERP ni la base. Usa nombres/redes con prefijo buryfw- y los elimina al terminar.
#
#   sudo bash scripts/install/test-firewall-isolation.sh          en un host Linux con Docker Engine (p. ej. el runner de CI)
#   bash scripts/install/test-firewall-isolation.sh --in-dind     en una PC con Docker: levanta un Docker-in-Docker DESCARTABLE
#                                                                 (privilegiado, con su propio iptables; no toca el firewall ni la red del host)
# NO ejecutarlo con sudo directo en el servidor de produccion: aplica y quita la cadena BURY-ERP de DOCKER-USER.
set -uo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if [[ "${1:-}" == "--in-dind" ]]; then
  command -v docker >/dev/null || { echo "docker no disponible"; exit 2; }
  export MSYS_NO_PATHCONV=1                      # Git Bash en Windows: no reescribir "contenedor:/ruta"
  name="buryfw-dind-$$"
  trap 'docker rm -f "$name" >/dev/null 2>&1' EXIT
  docker run -d --privileged --name "$name" -e DOCKER_TLS_CERTDIR= docker:dind >/dev/null || exit 2
  for _ in $(seq 1 60); do docker exec "$name" docker info >/dev/null 2>&1 && break; sleep 1; done
  docker exec "$name" docker info >/dev/null 2>&1 || { echo "el dockerd interno no arranco"; exit 2; }
  docker exec "$name" apk add --no-cache bash >/dev/null 2>&1 || { echo "no se pudo instalar bash en dind"; exit 2; }
  docker exec "$name" mkdir -p /opt/bf/scripts/install || exit 2
  for f in bury-firewall.sh test-firewall-isolation.sh; do   # por stdin: evita `docker cp` y la conversion de rutas de Git Bash
    docker exec -i "$name" sh -c "cat > /opt/bf/scripts/install/$f" <"$HERE/$f" || exit 2
  done
  docker exec "$name" bash /opt/bf/scripts/install/test-firewall-isolation.sh
  exit $?
fi

[[ $EUID -eq 0 ]] || { echo "requiere root (o --in-dind)"; exit 2; }
FW="$HERE/bury-firewall.sh"
IMG="alpine:3@sha256:294b683cb724975bec92580e1e685676bd4b50bda910ddb8c51d4cabeaec77e6"   # el mismo que usa docker-compose.yml
PORT=9443                                                                                  # puerto PUBLICADO (equivale al 443 de Caddy)
P=buryfw
CONF=$(mktemp)
PASS=0 FAIL=0
ok()  { PASS=$((PASS+1)); echo "PASS $1"; }
bad() { FAIL=$((FAIL+1)); echo "FAIL $1"; }
check() { local name=$1; shift; if "$@"; then ok "$name"; else bad "$name"; fi; }

cleanup() {
  BURY_FW_CONF="$CONF" bash "$FW" remove >/dev/null 2>&1
  docker rm -f $P-erp $P-ts $P-lan $P-out $P-out2 $P-spoof >/dev/null 2>&1
  docker network rm $P-ts $P-lan $P-out >/dev/null 2>&1
  rm -f "$CONF"
}
trap cleanup EXIT
cleanup >/dev/null 2>&1; CONF=$(mktemp)

docker pull -q "$IMG" >/dev/null || { echo "no se pudo bajar $IMG"; exit 2; }
# Bridges con nombre fijo: el de la "tailnet" se llama tailscale0, como la interfaz real de Tailscale en el servidor.
docker network create --subnet 100.64.88.0/24 -o com.docker.network.bridge.name=tailscale0 $P-ts >/dev/null || exit 2
docker network create --subnet 192.168.77.0/24 -o com.docker.network.bridge.name=buryfw-lan0 $P-lan >/dev/null || exit 2
docker network create --subnet 10.99.77.0/24 -o com.docker.network.bridge.name=buryfw-out0 $P-out >/dev/null || exit 2

# "ERP": 443 interno PUBLICADO como $PORT; 8080 y 1433 escuchan DENTRO del contenedor pero NO se publican (como app y db).
SRV='srv() { while :; do printf "HTTP/1.0 200 OK\r\n\r\nERP-OK\n" | nc -l -p "$1" >/dev/null; done; }'
docker run -d --name $P-erp -p "$PORT:443" "$IMG" sh -c "$SRV; srv 8080 & srv 1433 & srv 443" >/dev/null || exit 2
docker run -d --name $P-ts  --network $P-ts  "$IMG" sleep 600 >/dev/null
docker run -d --name $P-lan --network $P-lan "$IMG" sleep 600 >/dev/null
docker run -d --name $P-out --network $P-out "$IMG" sleep 600 >/dev/null
# Cliente que suplanta un origen 100.x desde la LAN (entra por buryfw-lan0, NO por tailscale0)
docker run -d --name $P-spoof --network $P-lan --cap-add NET_ADMIN "$IMG" sh -c 'ip addr add 100.64.88.200/32 dev eth0; sleep 600' >/dev/null
sleep 2
gw() { docker network inspect "$1" --format '{{(index .IPAM.Config 0).Gateway}}'; }
GW_TS=$(gw $P-ts); GW_LAN=$(gw $P-lan); GW_OUT=$(gw $P-out)
# alcanza CLIENTE IP PUERTO -> 0 si responde el contenido esperado del ERP
reach() { docker exec "$1" wget -q -T 3 -O- "http://$2:$3/" 2>/dev/null | grep -q ERP-OK; }
refused_or_timeout() { ! reach "$@"; }
rule_pkts() { iptables -w 5 -nvx -L BURY-ERP 2>/dev/null | awk -v pat="$1" '$0 ~ pat {s+=$1} END {print s+0}'; }

echo "== Sin firewall (linea base: demuestra el riesgo que el firewall corrige) =="
check "base: el cliente AJENO llega al puerto publicado (Docker ignora a ufw/INPUT)" reach $P-out "$GW_OUT" $PORT

echo "== Con bury-firewall (LAN + tailnet atada a la interfaz tailscale0) =="
printf 'ALLOW_FROM="192.168.77.0/24 100.64.0.0/10@tailscale0"\nHTTPS_PORT=%s\n' "$PORT" >"$CONF"
BURY_FW_CONF="$CONF" bash "$FW" apply >/dev/null 2>&1; rc=$?
check "apply termina bien (rc=0)" test "$rc" = 0
check "AUTORIZADO: cliente de la tailnet (100.64.88.x por tailscale0) llega al ERP" reach $P-ts "$GW_TS" $PORT
check "AUTORIZADO: cliente de la LAN llega al ERP" reach $P-lan "$GW_LAN" $PORT
check "DENEGADO: cliente ajeno (10.99.77.x) NO llega al ERP" refused_or_timeout $P-out "$GW_OUT" $PORT
check "AISLAMIENTO: 8080 (app) no se alcanza desde la tailnet" refused_or_timeout $P-ts "$GW_TS" 8080
check "AISLAMIENTO: 1433 (SQL) no se alcanza desde la tailnet" refused_or_timeout $P-ts "$GW_TS" 1433
check "AISLAMIENTO: 8080 no se alcanza desde la LAN" refused_or_timeout $P-lan "$GW_LAN" 8080
check "AISLAMIENTO: 1433 no se alcanza desde la LAN" refused_or_timeout $P-lan "$GW_LAN" 1433
check "AISLAMIENTO: 8080 y 1433 no se alcanzan desde el cliente ajeno" bash -c '! docker exec $1 wget -q -T 3 -O- http://$2:8080/ >/dev/null 2>&1 && ! docker exec $1 nc -w 3 $2 1433 </dev/null >/dev/null 2>&1' _ $P-out "$GW_OUT"
check "el contenedor ERP solo publica el puerto $PORT (no 8080 ni 1433)" bash -c 'p=$(docker port $1); [[ "$p" == *"$2"* && "$p" != *8080* && "$p" != *1433* ]]' _ $P-erp $PORT

echo "== Suplantacion de origen 100.x desde la LAN =="
before=$(rule_pkts 'tailscale0'); dropped_before=$(rule_pkts 'DROP')
docker exec $P-spoof wget -q -T 3 -O- --header=x:y "http://$GW_LAN:$PORT/" >/dev/null 2>&1   # sale con una IP LAN: control
docker exec $P-spoof sh -c "nc -w 2 -s 100.64.88.200 $GW_LAN $PORT </dev/null" >/dev/null 2>&1
after=$(rule_pkts 'tailscale0'); dropped_after=$(rule_pkts 'DROP')
check "SPOOF: el SYN con origen 100.x que entra por la LAN NO coincide con la regla de tailscale0" test "$after" = "$before"
check "SPOOF: ese SYN cae en el DROP final" test "$dropped_after" -gt "$dropped_before"

echo "== Egreso no afectado (--ctstate DNAT) =="
docker run -d --name $P-out2 --network $P-out "$IMG" sh -c "$SRV; srv $PORT" >/dev/null
sleep 1
out2=$(docker inspect $P-out2 --format "{{(index .NetworkSettings.Networks \"$P-out\").IPAddress}}")
check "un contenedor puede ABRIR conexiones al puerto $PORT de otro servidor (solo se filtra lo entrante DNAT)" reach $P-out "$out2" $PORT

echo "== Revocacion: quitar la red de Tailscale del firewall =="
printf 'ALLOW_FROM="192.168.77.0/24"\nHTTPS_PORT=%s\n' "$PORT" >"$CONF"
BURY_FW_CONF="$CONF" bash "$FW" apply >/dev/null 2>&1
check "REVOCADO: el cliente de la tailnet ya NO llega" refused_or_timeout $P-ts "$GW_TS" $PORT
check "la LAN sigue con acceso tras reaplicar (idempotente)" reach $P-lan "$GW_LAN" $PORT
BURY_FW_CONF="$CONF" bash "$FW" apply >/dev/null 2>&1
check "reaplicar dos veces deja UNA sola cadena enganchada" test "$(iptables -w 5 -S DOCKER-USER | grep -c -- '-j BURY-ERP$')" = 1

echo "== Retirar el firewall =="
BURY_FW_CONF="$CONF" bash "$FW" remove >/dev/null 2>&1
check "sin la cadena, el ajeno vuelve a llegar (el firewall es lo que lo impedia)" reach $P-out "$GW_OUT" $PORT

echo
echo "firewall isolation tests: $PASS PASS, $FAIL FAIL"
(( FAIL == 0 ))
