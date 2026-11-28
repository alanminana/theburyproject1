#!/usr/bin/env bash
# Prueba REAL de que el firewall FALLA CERRADO: lo instala con server-linux-bootstrap.sh sobre el systemd y el Docker del anfitrion,
# induce fallos y observa el puerto publicado desde dos clientes en espacios de red propios (no dependen de Docker):
# "autorizado" (198.18.77.20) y "ajeno" (198.19.77.20). Datos sinteticos; no usa el ERP ni ninguna base.
#
# DESTRUCTIVA: detiene y reinicia Docker muchas veces (corta TODOS los contenedores del anfitrion) y al terminar retira el firewall.
# Solo en una maquina descartable (runner de CI o VM de laboratorio). Se niega si no se pide explicitamente o si ya existe /etc/bury.
#
#   sudo BURY_FAILCLOSED_DESTRUCTIVO=1 bash scripts/install/test-firewall-failclosed.sh <usuario>
set -uo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

[[ "${BURY_FAILCLOSED_DESTRUCTIVO:-}" == 1 ]] || { echo "prueba destructiva: definir BURY_FAILCLOSED_DESTRUCTIVO=1 (solo en una maquina descartable)"; exit 2; }
[[ $EUID -eq 0 ]] || { echo "requiere root"; exit 2; }
RUN_USER="${1:?indicar el usuario para el bootstrap}"
[[ ! -e /etc/bury && ! -e /usr/local/sbin/bury-firewall ]] || { echo "ya hay un firewall instalado: parece un servidor real; no se toca"; exit 2; }
systemctl is-active -q docker || { echo "docker.service no esta activo bajo systemd"; exit 2; }

IMG="alpine:3@sha256:294b683cb724975bec92580e1e685676bd4b50bda910ddb8c51d4cabeaec77e6"
PORT=9443 NAME=buryfc-erp CONF=/etc/bury/firewall.conf DROPIN_TEST=/etc/systemd/system/docker.service.d/zz-prueba.conf
DJ=/etc/docker/daemon.json TMP="$(mktemp -d)" SRV_PID=""
PASS=0 FAIL=0
ok()  { PASS=$((PASS+1)); echo "PASS $1"; }
bad() { FAIL=$((FAIL+1)); echo "FAIL $1"; }
check() { local name=$1; shift; if "$@"; then ok "$name"; else bad "$name"; fi; }

HAD_DJ=0; [[ -f $DJ ]] && { HAD_DJ=1; cp -a $DJ "$TMP/daemon.json"; }
restore_dj() { if (( HAD_DJ )); then cp -a "$TMP/daemon.json" $DJ; else rm -f $DJ; fi; }

cleanup() {
  [[ -n "$SRV_PID" ]] && kill "$SRV_PID" 2>/dev/null
  rm -f $DROPIN_TEST /etc/systemd/system/docker.service.d/bury-firewall.conf
  systemctl disable -q bury-firewall.service 2>/dev/null
  rm -f /etc/systemd/system/bury-firewall.service
  restore_dj
  systemctl daemon-reload
  while iptables -w 5 -D DOCKER-USER -j ACCEPT 2>/dev/null; do :; done
  BURY_FW_CONF="$CONF" bash "$HERE/bury-firewall.sh" remove >/dev/null 2>&1
  rm -rf /etc/bury /usr/local/sbin/bury-firewall
  systemctl reset-failed docker.service 2>/dev/null
  systemctl restart docker
  docker rm -f $NAME >/dev/null 2>&1
  ip netns del buryfc-ok 2>/dev/null; ip netns del buryfc-no 2>/dev/null
  rm -rf "$TMP"
}
trap cleanup EXIT

# --- clientes en espacios de red propios (sobreviven a que Docker este caido) ---
client() {   # client NOMBRE IF_HOST RED
  ip netns add "$1" && ip link add "$2" type veth peer name eth0 netns "$1" || return 1
  ip addr add "$3.1/24" dev "$2"; ip link set "$2" up
  ip -n "$1" addr add "$3.20/24" dev eth0; ip -n "$1" link set eth0 up; ip -n "$1" link set lo up
  ip -n "$1" route add default via "$3.1"
}
client buryfc-ok buryfc-ok0 198.18.77 || { echo "no se pudo crear el cliente autorizado"; exit 2; }
client buryfc-no buryfc-no0 198.19.77 || { echo "no se pudo crear el cliente ajeno"; exit 2; }

reach_ok() { ip netns exec buryfc-ok curl -s -m 3 "http://198.18.77.1:$PORT/" 2>/dev/null | grep -q ERP-OK; }
reach_no() { ip netns exec buryfc-no curl -s -m 3 "http://198.19.77.1:$PORT/" 2>/dev/null | grep -q ERP-OK; }
denied_no() { ! reach_no; }
nothing_listens() { ! ss -Hltn "sport = :$PORT" | grep -q .; }
wait_ok() { local i; for i in $(seq 1 30); do reach_ok && return 0; sleep 1; done; return 1; }
docker_down() { ! systemctl is-active -q docker; }
# Caido y CERRADO: Docker no esta en marcha, nadie escucha el puerto y ningun cliente llega. Se espera a que systemd agote sus reintentos.
closed() { local i; for i in $(seq 1 40); do [[ "$(systemctl is-active docker)" == failed ]] && break; sleep 1; done; docker_down && nothing_listens && denied_no && ! reach_ok; }
start_docker() { systemctl reset-failed docker.service docker.socket 2>/dev/null; systemctl start docker; }
restart_docker() { systemctl reset-failed docker.service docker.socket 2>/dev/null; systemctl restart docker; }
# Sondea el cliente ajeno sin pausa mientras corre un comando: cualquier conexion lograda es una ventana abierta.
no_window() {   # no_window COMANDO...
  local flag="$TMP/watch" hits="$TMP/hits" n="$TMP/n"
  : >"$flag"; : >"$hits"; : >"$n"
  ip netns exec buryfc-no bash -c 'while [[ -e "$1" ]]; do echo . >>"$4"; timeout 0.3 bash -c "</dev/tcp/198.19.77.1/$3" 2>/dev/null && echo abierto >>"$2"; sleep 0.02; done' _ "$flag" "$hits" "$PORT" "$n" &
  local w=$!
  "$@" >/dev/null 2>&1; wait_ok; sleep 2
  rm -f "$flag"; wait $w
  echo "     (sondeos del cliente ajeno: $(wc -l <"$n"), conexiones logradas: $(wc -l <"$hits"))"
  [[ -s "$n" && ! -s "$hits" ]]
}
good_conf() { printf 'ALLOW_FROM="198.18.77.0/24"\nHTTPS_PORT=%s\n' "$PORT" >$CONF; }

docker pull -q "$IMG" >/dev/null || { echo "no se pudo bajar $IMG"; exit 2; }
docker rm -f $NAME >/dev/null 2>&1
docker run -d --name $NAME --restart unless-stopped -p "$PORT:443" "$IMG" \
  sh -c 'while :; do printf "HTTP/1.0 200 OK\r\n\r\nERP-OK\n" | nc -l -p 443 >/dev/null; done' >/dev/null || exit 2

echo "== Linea base sin firewall =="
check "base: el cliente autorizado llega" wait_ok
check "base: el cliente AJENO tambien llega (el riesgo que corrige el firewall)" reach_no

echo "== Instalacion con server-linux-bootstrap.sh (systemd real) =="
bash "$HERE/server-linux-bootstrap.sh" --user "$RUN_USER" --allow-from 198.18.77.0/24 --https-port $PORT --skip-docker --no-ufw >"$TMP/boot.log" 2>&1; rc=$?
check "bootstrap termina bien (rc=0)" test "$rc" = 0
(( rc == 0 )) || tail -20 "$TMP/boot.log"
check "bury-firewall.service activo y habilitado" bash -c 'systemctl is-active -q bury-firewall && systemctl is-enabled -q bury-firewall'
check "docker.service: guard antes y verify despues, SIN el prefijo que tolera fallos" bash -c 's=$(systemctl cat docker.service); grep -qx "ExecStartPre=/usr/local/sbin/bury-firewall guard" <<<"$s" && grep -qx "ExecStartPost=/usr/local/sbin/bury-firewall verify" <<<"$s" && ! grep -q "=-/usr/local/sbin/bury-firewall" <<<"$s"'
check "la unidad no define ExecStop (detenerla no abre el puerto)" bash -c '! grep -q "^ExecStop" /etc/systemd/system/bury-firewall.service'
check "AUTORIZADO llega" wait_ok
check "AJENO denegado" denied_no

echo "== Bootstrap que falla con Docker en marcha y sin filtro: no debe dejar el puerto publicado =="
BURY_FW_CONF="$CONF" bash "$HERE/bury-firewall.sh" remove >/dev/null 2>&1
check "linea base: sin la cadena el ajeno llega" reach_no
cp -a $DJ "$TMP/daemon.previo" 2>/dev/null
printf '{ "iptables": false }\n' >$DJ     # dockerd NO se reinicia: sigue en marcha y publicando
bash "$HERE/server-linux-bootstrap.sh" --user "$RUN_USER" --allow-from 198.18.77.0/24 --https-port $PORT --skip-docker --no-ufw >"$TMP/boot-falla.log" 2>&1; rc=$?
check "el bootstrap termina con error" test "$rc" != 0
check "CERRADO tras el bootstrap fallido" closed
if [[ -f "$TMP/daemon.previo" ]]; then cp -a "$TMP/daemon.previo" $DJ; else rm -f $DJ; fi
bash "$HERE/server-linux-bootstrap.sh" --user "$RUN_USER" --allow-from 198.18.77.0/24 --https-port $PORT --skip-docker --no-ufw >"$TMP/boot-ok.log" 2>&1; rc=$?
check "corregida la causa, el bootstrap termina bien" test "$rc" = 0
check "y el servicio vuelve" wait_ok
check "AJENO denegado" denied_no

echo "== verify detecta reglas en un orden que no filtra =="
iptables -w 5 -D BURY-ERP -j RETURN && iptables -w 5 -I BURY-ERP 1 -j RETURN
check "con el RETURN final movido al principio, el ajeno llega (el filtro quedo anulado)" reach_no
check "verify lo rechaza" bash -c '! bury-firewall verify >/dev/null 2>&1'
bury-firewall apply >/dev/null 2>&1
check "reaplicado: verify OK y ajeno denegado" bash -c 'bury-firewall verify >/dev/null'
check "AJENO denegado tras reaplicar" denied_no

echo "== Operaciones normales: sin ventana abierta =="
check "reinicio de Docker: el contenedor vuelve y el ajeno NUNCA conecta" no_window restart_docker
check "tras reiniciar Docker: autorizado llega, ajeno denegado, verify OK" bash -c 'bury-firewall verify >/dev/null'
check "AUTORIZADO tras el reinicio" reach_ok
check "AJENO denegado tras el reinicio" denied_no
systemctl stop bury-firewall
check "detener bury-firewall.service NO abre el puerto" denied_no
check "reiniciar bury-firewall.service no abre ventana" no_window systemctl restart bury-firewall

echo "== Salida de los contenedores =="
ip netns exec buryfc-no python3 -m http.server "$PORT" --bind 198.19.77.20 --directory "$TMP" >/dev/null 2>&1 &
SRV_PID=$!
echo SALIDA-OK >"$TMP/salida.txt"; sleep 2
check "un contenedor ABRE conexiones al puerto $PORT de otro equipo (no se filtra lo saliente)" bash -c 'docker exec "$1" wget -q -T 5 -O- "http://198.19.77.20:$2/salida.txt" | grep -q SALIDA-OK' _ $NAME $PORT
kill "$SRV_PID" 2>/dev/null; SRV_PID=""

echo "== Cambio de redes autorizadas repitiendo el bootstrap =="
bash "$HERE/server-linux-bootstrap.sh" --user "$RUN_USER" --allow-from 198.18.77.0/24,198.19.77.0/24 --https-port $PORT --skip-docker --no-ufw >"$TMP/boot2.log" 2>&1
check "la red agregada obtiene acceso sin reiniciar Docker" reach_no
bash "$HERE/server-linux-bootstrap.sh" --user "$RUN_USER" --allow-from 198.18.77.0/24 --https-port $PORT --skip-docker --no-ufw >"$TMP/boot3.log" 2>&1
check "la red retirada pierde el acceso sin reiniciar Docker" denied_no

echo "== Fallo 1: configuracion invalida con Docker en marcha =="
printf 'ALLOW_FROM=""\nHTTPS_PORT=%s\n' "$PORT" >$CONF
check "reaplicar falla" bash -c '! systemctl restart bury-firewall 2>/dev/null'
check "las reglas anteriores se conservan: ajeno denegado" denied_no
check "las reglas anteriores se conservan: autorizado llega" reach_ok

echo "== Fallo 2: Docker se reinicia con la configuracion invalida =="
check "el reinicio de Docker falla" bash -c '! systemctl restart docker 2>/dev/null'
check "CERRADO: Docker caido, nada escucha en $PORT y nadie llega" closed
timeout 20 docker ps >/dev/null 2>&1   # activacion por socket: un cliente docker no debe poder levantarlo sin filtro
check "CERRADO tambien tras un intento de activacion por docker.socket" closed
good_conf
check "recuperacion: corregir, reset-failed y start" start_docker
check "recuperacion: el contenedor vuelve solo y el autorizado llega" wait_ok
check "recuperacion: ajeno denegado" denied_no

echo "== Fallo 3: arranque en frio (sin la cadena DOCKER-USER) =="
systemctl stop docker.socket docker.service
iptables -w 5 -D FORWARD -j DOCKER-USER 2>/dev/null; iptables -w 5 -F DOCKER-USER 2>/dev/null; iptables -w 5 -X DOCKER-USER 2>/dev/null
iptables -w 5 -F BURY-ERP 2>/dev/null; iptables -w 5 -X BURY-ERP 2>/dev/null
check "en frio no existe DOCKER-USER" bash -c '! iptables -w 5 -n -L DOCKER-USER >/dev/null 2>&1'
check "arranque en frio sin ventana: guard crea la cadena antes de dockerd" no_window start_docker
check "en frio: verify OK" bash -c 'bury-firewall verify >/dev/null'
check "en frio: autorizado llega" reach_ok

echo "== Fallo 4: iptables no funciona =="
printf 'IPT=/bin/false\n' >>$CONF
check "el reinicio de Docker falla" bash -c '! systemctl restart docker 2>/dev/null'
check "CERRADO" closed
good_conf; start_docker; check "recuperado" wait_ok

echo "== Fallo 5: falta el script del firewall =="
mv /usr/local/sbin/bury-firewall "$TMP/bury-firewall.bak"
check "el reinicio de Docker falla" bash -c '! systemctl restart docker 2>/dev/null'
check "CERRADO" closed
install -m 0755 "$TMP/bury-firewall.bak" /usr/local/sbin/bury-firewall; start_docker; check "recuperado" wait_ok

echo "== Fallo 6: dockerd configurado sin iptables (DOCKER-USER no filtraria) =="
printf '{ "iptables": false }\n' >$DJ
check "el reinicio de Docker falla" bash -c '! systemctl restart docker 2>/dev/null'
check "CERRADO" closed
restore_dj; start_docker; check "recuperado" wait_ok

echo "== Fallo 7: el filtro queda anulado despues del guard (lo detecta verify) =="
printf '[Service]\nExecStartPre=/usr/bin/env iptables -w 5 -I DOCKER-USER 1 -j ACCEPT\n' >$DROPIN_TEST
systemctl daemon-reload
check "el reinicio de Docker falla en verify" bash -c '! systemctl restart docker 2>/dev/null'
check "CERRADO" closed
check "el journal explica el motivo" bash -c 'journalctl -u docker --since "-2 min" --no-pager | grep -q "verify: una regla anterior evita la cadena"'
rm -f $DROPIN_TEST; systemctl daemon-reload
while iptables -w 5 -D DOCKER-USER -j ACCEPT 2>/dev/null; do :; done
start_docker; check "recuperado" wait_ok
check "AJENO denegado al final" denied_no

echo "== Segunda instalacion en el mismo equipo (firewall-<nombre>.conf) =="
EXTRA=/etc/bury/firewall-prueba.conf
{ echo '[Service]'; echo 'Type=oneshot'; echo 'ExecStart=/usr/local/sbin/bury-firewall apply'; echo 'ExecStop=/usr/local/sbin/bury-firewall remove'; } >/etc/systemd/system/bury-firewall-prueba.service
extra_conf() { { echo "ALLOW_FROM=\"$1\""; echo 'HTTPS_PORT=9444'; } >$EXTRA; }
bury-firewall install-systemd >/dev/null; systemctl daemon-reload
check "install-systemd retira el ExecStop de una unidad copiada de la version anterior" bash -c '! grep -q "^ExecStop" /etc/systemd/system/bury-firewall-prueba.service'
rm -f /etc/systemd/system/bury-firewall-prueba.service; systemctl daemon-reload
extra_conf 198.18.77.0/24
check "el arranque de Docker aplica y verifica tambien la segunda cadena" bash -c 'systemctl restart docker && iptables -w 5 -C DOCKER-USER -j BURY-ERP-PRUEBA && iptables -w 5 -S BURY-ERP-PRUEBA | grep -q "ctorigdstport 9444 .*-j DROP"'
check "la instalacion principal sigue protegida" denied_no
extra_conf ''
check "con la segunda configuracion invalida el reinicio de Docker falla" bash -c '! systemctl restart docker 2>/dev/null'
check "CERRADO" closed
rm -f $EXTRA; start_docker; check "recuperado" wait_ok
while iptables -w 5 -D DOCKER-USER -j BURY-ERP-PRUEBA 2>/dev/null; do :; done
iptables -w 5 -F BURY-ERP-PRUEBA 2>/dev/null; iptables -w 5 -X BURY-ERP-PRUEBA 2>/dev/null

echo "== Reaplicacion interrumpida antes del renombrado =="
iptables -w 5 -E BURY-ERP BURY-ERP-NEW
check "reaplicar sin ventana cuando solo esta enganchada la cadena -NEW" no_window bury-firewall apply
check "queda una sola cadena y verify OK" bash -c 'bury-firewall verify >/dev/null && [[ $(iptables -w 5 -S DOCKER-USER | grep -c -- "-j BURY-ERP") == 1 ]]'

echo
echo "firewall fail-closed tests: $PASS PASS, $FAIL FAIL"
(( FAIL == 0 ))
