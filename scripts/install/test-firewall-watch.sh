#!/usr/bin/env bash
# Prueba REAL de la vigilancia del firewall (bury-firewall-watch.service) sobre el systemd y el Docker del anfitrion: borra reglas con Docker
# en marcha y observa el puerto publicado desde un cliente "autorizado" (198.18.77.20) y uno "ajeno" (198.19.77.20) en espacios de red propios.
#
# DESTRUCTIVA, igual que test-firewall-failclosed.sh: detiene Docker y borra reglas. Solo en una maquina descartable.
#   sudo BURY_FAILCLOSED_DESTRUCTIVO=1 bash scripts/install/test-firewall-watch.sh <usuario>
set -uo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
[[ "${BURY_FAILCLOSED_DESTRUCTIVO:-}" == 1 ]] || { echo "prueba destructiva: definir BURY_FAILCLOSED_DESTRUCTIVO=1 (solo en una maquina descartable)"; exit 2; }
[[ $EUID -eq 0 ]] || { echo "requiere root"; exit 2; }
RUN_USER="${1:?indicar el usuario para el bootstrap}"
[[ ! -e /etc/bury && ! -e /usr/local/sbin/bury-firewall ]] || { echo "ya hay un firewall instalado: parece un servidor real; no se toca"; exit 2; }
systemctl is-active -q docker || { echo "docker.service no esta activo bajo systemd"; exit 2; }

IMG="alpine:3@sha256:294b683cb724975bec92580e1e685676bd4b50bda910ddb8c51d4cabeaec77e6"
PORT=9443 NAME=buryfw-vig CONF=/etc/bury/firewall.conf ST=/var/lib/bury-firewall
TUNE=/etc/systemd/system/bury-firewall-watch.service.d/prueba.conf
PASS=0 FAIL=0
ok()  { PASS=$((PASS+1)); echo "PASS $1"; }
bad() { FAIL=$((FAIL+1)); echo "FAIL $1"; }
check() { local name=$1; shift; if "$@"; then ok "$name"; else bad "$name"; fi; }

cleanup() {
  systemctl disable -q --now bury-firewall-watch.service bury-firewall.service 2>/dev/null
  rm -rf "$(dirname $TUNE)" /etc/systemd/system/bury-firewall-watch.service /etc/systemd/system/bury-firewall.service /etc/systemd/system/docker.service.d/bury-firewall.conf
  systemctl daemon-reload
  while iptables -w 5 -D DOCKER-USER -m conntrack --ctstate DNAT -m comment --comment bury-barrera -j DROP 2>/dev/null; do :; done
  BURY_FW_CONF="$CONF" bash "$HERE/bury-firewall.sh" remove >/dev/null 2>&1
  rm -rf /etc/bury /usr/local/sbin/bury-firewall "$ST"
  systemctl reset-failed docker.service docker.socket 2>/dev/null
  systemctl restart docker
  docker rm -f $NAME >/dev/null 2>&1; docker network rm buryfw-extra >/dev/null 2>&1
  ip netns del buryfc-ok 2>/dev/null; ip netns del buryfc-no 2>/dev/null
}
trap cleanup EXIT

client() {
  ip netns add "$1" && ip link add "$2" type veth peer name eth0 netns "$1" || return 1
  ip addr add "$3.1/24" dev "$2"; ip link set "$2" up
  ip -n "$1" addr add "$3.20/24" dev eth0; ip -n "$1" link set eth0 up; ip -n "$1" link set lo up
  ip -n "$1" route add default via "$3.1"
}
client buryfc-ok buryfc-ok0 198.18.77 || exit 2
client buryfc-no buryfc-no0 198.19.77 || exit 2
reach_ok() { ip netns exec buryfc-ok curl -s -m 3 "http://198.18.77.1:$PORT/" 2>/dev/null | grep -q ERP-OK; }
reach_no() { ip netns exec buryfc-no curl -s -m 2 "http://198.19.77.1:$PORT/" 2>/dev/null | grep -q ERP-OK; }
denied_no() { ! reach_no; }
wait_ok() { local i; for i in $(seq 1 40); do reach_ok && return 0; sleep 1; done; return 1; }
nothing_listens() { ! ss -Hltn "sport = :$PORT" | grep -q .; }
st() { sed -n "s/.*\b$1=\([^ ]*\).*/\1/p" $ST/estado 2>/dev/null | head -1; }
wait_state() { local i; for i in $(seq 1 "${2:-60}"); do [[ "$(st estado)" == "$1" ]] && return 0; sleep 1; done; return 1; }
incident_id() { sed -n 's/^id=//p' $ST/incidente 2>/dev/null; }
no_incident() { [[ ! -e $ST/incidente ]]; }
pid_docker() { systemctl show docker -p MainPID --value; }
good_conf() { printf 'ALLOW_FROM="198.18.77.0/24"\nHTTPS_PORT=%s\n' "$PORT" >$CONF; }
fresh_ok() { wait_state ok 40 && wait_ok && denied_no; }
tune() {   # tune INTERVALO VENTANA_DE_REINICIO
  mkdir -p "$(dirname $TUNE)"; { echo '[Service]'; echo "Environment=BURY_WATCH_INTERVAL=$1"; echo "Environment=BURY_WATCH_RESTART_WINDOW=$2"; } >$TUNE
  rm -f $ST/ultimo-reinicio; systemctl daemon-reload; systemctl restart bury-firewall-watch.service
}
ack_all() { local id; id=$(incident_id); [[ -z "$id" ]] || bury-firewall ack "$id" >/dev/null; }

# Recuperaciones por reinicio registradas; se espera una NUEVA (una linea anterior no sirve como evidencia).
recoveries() { grep -c "recuperado reiniciando Docker" $ST/incidentes.log 2>/dev/null || true; }
wait_new_recovery() { local i; for i in $(seq 1 90); do (( $(recoveries) > $1 )) && return 0; sleep 1; done; return 1; }

docker pull -q "$IMG" >/dev/null || exit 2
docker rm -f $NAME >/dev/null 2>&1
docker run -d --name $NAME --restart unless-stopped -p "$PORT:443" "$IMG" \
  sh -c 'while :; do printf "HTTP/1.0 200 OK\r\n\r\nERP-OK\n" | nc -l -p 443 >/dev/null; done' >/dev/null || exit 2
bash "$HERE/server-linux-bootstrap.sh" --user "$RUN_USER" --allow-from 198.18.77.0/24 --https-port $PORT --skip-docker --no-ufw >/tmp/buryfw-vig-boot.log 2>&1
check "bootstrap instala el firewall y la vigilancia (activa y habilitada)" bash -c 'systemctl is-active -q bury-firewall-watch && systemctl is-enabled -q bury-firewall-watch'
check "estado inicial ok, autorizado llega y ajeno denegado" fresh_ok
check "la unidad de vigilancia no depende de docker.service (sigue informando si Docker cae)" bash -c '! grep -qE "^(PartOf|Requires|BindsTo)=" /etc/systemd/system/bury-firewall-watch.service'

echo "== Sin falsos positivos (intervalo 1 s para forzar carreras) =="
tune 1 600
pid0=$(pid_docker)
for _ in 1 2 3; do systemctl reset-failed docker.service docker.socket 2>/dev/null; systemctl restart docker; sleep 4; done
check "tras 3 reinicios de Docker: sin incidentes y Docker activo" bash -c 'sleep 5; [[ ! -e /var/lib/bury-firewall/incidente ]] && systemctl is-active -q docker'
pid0=$(pid_docker)
for i in 1 2 3 4 5; do docker network create buryfw-extra >/dev/null; docker run --rm --network buryfw-extra -p 9555:80 "$IMG" sleep 1 >/dev/null; docker network rm buryfw-extra >/dev/null; done
sleep 5
check "crear/borrar redes y puertos publicados: sin incidentes y sin reiniciar Docker" bash -c '[[ ! -e /var/lib/bury-firewall/incidente ]] && [[ "$(systemctl show docker -p MainPID --value)" == "$1" ]]' _ "$pid0"
bash "$HERE/server-linux-bootstrap.sh" --user "$RUN_USER" --allow-from 198.18.77.0/24 --https-port $PORT --skip-docker --no-ufw >/dev/null 2>&1
check "repetir el bootstrap no abre incidentes" bash -c 'sleep 5; [[ ! -e /var/lib/bury-firewall/incidente ]]'
systemctl stop docker.socket docker.service
sleep 8
check "parada manual de Docker: la vigilancia NO lo arranca" bash -c '! systemctl is-active -q docker'
check "con Docker parado: estado docker-inactivo, latido al dia, sin incidente" bash -c 'grep -q "estado=docker-inactivo" /var/lib/bury-firewall/estado && [[ ! -e /var/lib/bury-firewall/incidente ]] && (( $(date +%s) - $(sed -n "s/^latido=\([0-9]*\).*/\1/p" /var/lib/bury-firewall/estado) < 10 ))'
systemctl start docker
check "al volver Docker: estado ok" fresh_ok

tune 10 600
fresh_ok >/dev/null

# Rompe las reglas, mide cuanto sigue entrando el ajeno y comprueba la reposicion sin reiniciar Docker.
repair_case() {   # repair_case "descripcion" comando...
  local name=$1; shift
  echo "== $name =="
  local pid t0 last=0 i
  pid=$(pid_docker); t0=$(date +%s)
  "$@"
  for i in $(seq 1 60); do
    if reach_no; then last=$(( $(date +%s) - t0 )); fi
    [[ "$(st estado)" == incidente ]] && break
    sleep 0.5
  done
  echo "     exposicion observada: ultimo acceso del ajeno a los ${last} s; incidente a los $(( $(date +%s) - t0 )) s"
  check "$name: detectado y repuesto (estado incidente)" wait_state incidente 30
  check "$name: ajeno denegado" denied_no
  check "$name: autorizado llega" reach_ok
  check "$name: sin reiniciar Docker" test "$(pid_docker)" = "$pid"
  check "$name: verify OK y sin barrera residual" bash -c 'bury-firewall verify >/dev/null && ! iptables -w 5 -S DOCKER-USER | grep -q bury-barrera'
  ack_all; wait_state ok 25 >/dev/null
}
repair_case "salto DOCKER-USER -> BURY-ERP borrado" iptables -w 5 -D DOCKER-USER -j BURY-ERP
repair_case "cadena BURY-ERP vaciada" iptables -w 5 -F BURY-ERP
repair_case "bury-firewall remove" bury-firewall remove
repair_case "salto FORWARD -> DOCKER-USER borrado" iptables -w 5 -D FORWARD -j DOCKER-USER
repair_case "RETURN final movido al principio" bash -c 'iptables -w 5 -D BURY-ERP -j RETURN && iptables -w 5 -I BURY-ERP 1 -j RETURN'

echo "== El incidente queda abierto hasta reconocerlo =="
iptables -w 5 -F BURY-ERP; wait_state incidente 30; sleep 14
check "sigue 'abierto' aunque las reglas ya esten bien (el estado vuelve a ok y conserva el id)" bash -c 'grep -qE "estado=ok .*incidente=[0-9]+" /var/lib/bury-firewall/estado'
check "ack con un id incorrecto se rechaza" bash -c '! bury-firewall ack 1 >/dev/null 2>&1'
check "ack con el id correcto termina con codigo 0" bury-firewall ack "$(incident_id)"
check "ack con el id correcto lo cierra" no_incident
check "ack deja el estado publicado sin incidente en el acto (sin esperar otro ciclo)" grep -q "estado=ok .*incidente=-" $ST/estado
check "el historial conserva el incidente" grep -q "INCIDENTE" $ST/incidentes.log

echo "== Perdida de las reglas de Docker: un reinicio de Docker =="
tune 10 1
for caso in "iptables -w 5 -F" "iptables -w 5 -t nat -F"; do
  fresh_ok >/dev/null; pid=$(pid_docker); n=$(recoveries)
  $caso
  check "($caso): se recupera reiniciando Docker" wait_new_recovery "$n"
  check "($caso): Docker se reinicio" bash -c '[[ "$(systemctl show docker -p MainPID --value)" != "$1" ]]' _ "$pid"
  check "($caso): el contenedor vuelve" wait_ok
  check "($caso): ajeno denegado" denied_no
  ack_all; sleep 3
done

echo "== Segunda perdida dentro de la ventana de reinicio: no hay bucle, se detiene Docker =="
tune 10 3600
fresh_ok >/dev/null
n=$(recoveries); iptables -w 5 -t nat -F
check "primera: recuperada con un reinicio" wait_new_recovery "$n"
ack_all; wait_ok >/dev/null; wait_state ok 30 >/dev/null
iptables -w 5 -t nat -F
check "segunda: se detiene Docker" wait_state detenido 60
check "CERRADO: Docker inactivo, nada escucha y nadie llega" bash -c '! systemctl is-active -q docker'
check "nada escucha en el puerto" nothing_listens
check "el ajeno no llega" denied_no
sleep 25
check "la vigilancia no vuelve a arrancar Docker y conserva 'detenido'" bash -c '! systemctl is-active -q docker && grep -q "estado=detenido" /var/lib/bury-firewall/estado'
ack_all; systemctl reset-failed docker.service docker.socket 2>/dev/null; systemctl start docker
check "recuperacion manual: start; el ERP vuelve y la barrera se retira" fresh_ok

echo "== Configuracion mal editada con las reglas intactas: NO se corta el servicio =="
tune 10 600; fresh_ok >/dev/null; pid=$(pid_docker)
printf 'ALLOW_FROM=""\nHTTPS_PORT=%s\n' "$PORT" >$CONF
check "se informa config-invalida" wait_state config-invalida 40
sleep 14
check "Docker sigue en marcha, sin reinicios" test "$(pid_docker)" = "$pid"
check "autorizado llega" reach_ok
check "ajeno denegado" denied_no
check "un solo incidente registrado por esta causa" bash -c '(( $(grep -c "INCIDENTE: configuracion invalida" /var/lib/bury-firewall/incidentes.log) == 1 ))'
good_conf; ack_all
check "corregida: vuelve a ok" wait_state ok 30

echo "== Configuracion invalida Y reglas borradas: falla cerrado =="
printf 'ALLOW_FROM=""\nHTTPS_PORT=%s\n' "$PORT" >$CONF
sleep 12
iptables -w 5 -F BURY-ERP
check "se detiene Docker" wait_state detenido 60
check "Docker inactivo" bash -c '! systemctl is-active -q docker'
check "nada escucha en el puerto" nothing_listens
good_conf; ack_all; systemctl reset-failed docker.service docker.socket 2>/dev/null; systemctl start docker
check "recuperado" fresh_ok

# Con la configuracion rota solo se mantiene el servicio si las reglas son EXACTAMENTE las ultimas comprobadas.
tamper_case() {   # tamper_case "descripcion" comando...
  local name=$1; shift
  echo "== Configuracion invalida Y $name: falla cerrado =="
  tune 10 600; fresh_ok >/dev/null
  printf 'ALLOW_FROM=""\nHTTPS_PORT=%s\n' "$PORT" >$CONF
  wait_state config-invalida 40 >/dev/null
  "$@"
  check "$name: precondicion, el ajeno entra" reach_no
  check "$name: se detiene Docker" wait_state detenido 60
  check "$name: el ajeno ya no entra y Docker esta inactivo" bash -c '! systemctl is-active -q docker'
  check "$name: nada escucha en el puerto" nothing_listens
  good_conf; ack_all; systemctl reset-failed docker.service docker.socket 2>/dev/null; systemctl start docker
  while iptables -w 5 -D DOCKER-USER -j ACCEPT 2>/dev/null; do :; done
  check "$name: recuperado" fresh_ok
}
tamper_case "ACCEPT antes del salto" iptables -w 5 -I DOCKER-USER 1 -j ACCEPT
# Cambia el ORIGEN permitido conservando cantidad y orden de las reglas (A, D, E).
tamper_case "origen permitido alterado" bash -c 'iptables -w 5 -R BURY-ERP 1 -p tcp -m conntrack --ctstate DNAT --ctorigdstport 9443 --ctdir ORIGINAL -s 198.19.77.0/24 -j RETURN'

echo "== Mantenimiento: con la vigilancia detenida no se actua y el latido envejece =="
systemctl stop bury-firewall-watch.service
b=$(st latido); iptables -w 5 -F BURY-ERP; sleep 14
check "sin vigilancia nadie repone (el ajeno llega)" reach_no
check "el latido deja de avanzar (lo detecta el monitor)" test "$(st latido)" = "$b"
systemctl start bury-firewall-watch.service
check "al reanudarla repone" wait_state incidente 30
check "ajeno denegado al final" denied_no

echo
echo "firewall watch tests: $PASS PASS, $FAIL FAIL"
(( FAIL == 0 ))
