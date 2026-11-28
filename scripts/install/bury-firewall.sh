#!/usr/bin/env bash
# Firewall de los puertos que PUBLICA Docker (443 de Caddy) para el servidor Linux/VM de TheBuryProject.
#
#   bury-firewall.sh apply      reconstruye la cadena BURY-ERP y la engancha al inicio de DOCKER-USER
#   bury-firewall.sh remove     quita el enganche y la cadena (deja Docker intacto)
#   bury-firewall.sh print      muestra los comandos iptables que ejecutaria 'apply' (no cambia nada, no requiere root)
#   bury-firewall.sh status     muestra la cadena BURY-ERP actual
#   bury-firewall.sh guard      ANTES de arrancar dockerd (ExecStartPre): crea DOCKER-USER si falta y aplica; si falla, Docker NO arranca
#   bury-firewall.sh verify     DESPUES de arrancar dockerd (ExecStartPost): comprueba que el trafico pasa por la cadena; si no, systemd detiene Docker
#   bury-firewall.sh install-systemd   instala las unidades (firewall y vigilancia) y el drop-in de docker.service (lo usa el bootstrap)
#   bury-firewall.sh watch      vigilancia continua con Docker en marcha (bury-firewall-watch.service); 'watch-once' hace un solo ciclo
#   bury-firewall.sh ack ID     reconoce (cierra) el incidente abierto por la vigilancia
#
# FALLA CERRADO: si el filtro no se puede instalar o comprobar, docker.service no queda en marcha y no se publica ningun puerto. SSH no depende de
# Docker. Recuperacion: corregir la causa, 'sudo bury-firewall guard', 'sudo systemctl reset-failed docker' y 'sudo systemctl start docker'.
# Instalaciones adicionales en el mismo equipo: guard y verify tambien cubren cada firewall-<nombre>.conf junto al archivo principal, con la
# cadena BURY-ERP-<NOMBRE> (p. ej. firewall-prov.conf -> BURY-ERP-PROV). Si cualquiera falla, Docker no queda en marcha.
# Limite: protege el arranque y la reaplicacion. Quien borre las reglas con Docker en marcha (iptables -F, 'remove') vuelve a exponer el puerto.
#
# POR QUE existe: ufw/INPUT NO filtra los puertos publicados por Docker (el trafico DNAT-eado pasa por FORWARD y Docker agrega
# sus propias reglas antes que las de ufw). La unica cadena que Docker garantiza respetar es DOCKER-USER. Sin esto, 443 quedaria
# abierto a cualquier red que alcance la VM aunque ufw diga "deny incoming".
#
# Configuracion (archivo de shell, por defecto /etc/bury/firewall.conf; sobrescribible con BURY_FW_CONF):
#   ALLOW_FROM="192.168.1.0/24 100.64.0.0/10@tailscale0"   redes/IPs con permiso a HTTPS (OBLIGATORIO; vacio = no se aplica nada)
#                       Cada entrada admite el sufijo @INTERFAZ: el origen solo se acepta si el paquete ENTRA por esa interfaz. Para la
#                       red de Tailscale (100.64.0.0/10) usar @tailscale0: sin eso, un equipo de la LAN podria suplantar un origen 100.x
#                       (Tailscale protege INPUT con su propia cadena, pero NO los puertos publicados por Docker, que van por FORWARD).
#   BURY_FW_CHAIN=BURY-ERP-PROD (variable de entorno)  nombre de la cadena; por defecto BURY-ERP. Una segunda pila usa su cadena, su BURY_FW_CONF y su puerto.
#   HTTPS_PORT=443                                 puerto original publicado por Caddy (ERP_HTTPS_PORT)
# IMPORTANTE: las reglas llevan `--ctstate DNAT`: solo alcanzan a las conexiones ENTRANTES redirigidas a un contenedor (puerto publicado).
# Sin eso, `ctorigdstport 443` tambien descartaria el HTTPS SALIENTE de los contenedores (NuGet, ACME, SFTP...), porque su destino original es :443.
# Solo IPv4: el compose publica con ERP_BIND_ADDRESS=0.0.0.0 (IPv4 explicito), asi que Docker no escucha en IPv6 (verificar con `ss -ltn | grep :443`). Idempotente: se puede ejecutar cuantas veces haga falta (lo hace bury-firewall.service).
set -Eeuo pipefail

CONF="${BURY_FW_CONF:-/etc/bury/firewall.conf}"
IPT="${BURY_IPTABLES:-iptables -w 5}"   # -w: esperar el lock de xtables (Docker toca las mismas tablas al arrancar)
CHAIN="${BURY_FW_CHAIN:-BURY-ERP}"   # otra cadena (p. ej. BURY-ERP-PROD) permite una segunda pila con su propio puerto sin tocar la primera
ACTION="${1:-}"

ALLOW_FROM=""
HTTPS_PORT=443
# shellcheck disable=SC1090
[[ -f "$CONF" ]] && source "$CONF"

die() { echo "bury-firewall: $*" >&2; exit 1; }

# IPv4 o CIDR con interfaz opcional (CIDR@iface): octetos 0-255 sin ceros a la izquierda (iptables los leeria como octal) y prefijo 1-32 (/0 anularia el filtro).
valid_src() {
  local v=${1%%@*}
  if [[ "$1" == *@* ]]; then [[ "${1#*@}" =~ ^[A-Za-z0-9_.-]{1,15}$ ]] || return 1; fi
  set -- "$v"
  [[ "$1" =~ ^([0-9]{1,3})\.([0-9]{1,3})\.([0-9]{1,3})\.([0-9]{1,3})(/([1-9]|[12][0-9]|3[0-2]))?$ ]] || return 1
  local o; for o in "${BASH_REMATCH[1]}" "${BASH_REMATCH[2]}" "${BASH_REMATCH[3]}" "${BASH_REMATCH[4]}"; do
    (( 10#$o <= 255 )) && [[ "$o" == 0 || "$o" != 0* ]] || return 1
  done
}

validate() {
  [[ "$HTTPS_PORT" =~ ^[0-9]+$ ]] && (( HTTPS_PORT >= 1 && HTTPS_PORT <= 65535 )) || die "HTTPS_PORT invalido: '$HTTPS_PORT'"
  [[ -n "${ALLOW_FROM// /}" ]] || die "ALLOW_FROM esta vacio ($CONF): no se aplica nada para no dejar a todos sin acceso"
  local s
  for s in $ALLOW_FROM; do valid_src "$s" || die "ALLOW_FROM contiene un valor invalido: '$s' (use IPv4 o CIDR)"; done
}

# Emite, una por linea, los comandos de la cadena (sin el enganche a DOCKER-USER).
rules() {   # rules NOMBRE_CADENA
  local c=$1 s p src ifc
  for s in $ALLOW_FROM; do
    src=${s%%@*}; ifc=""
    [[ "$s" == *@* ]] && ifc="-i ${s#*@} "
    for p in tcp udp; do
      echo "-A $c -p $p ${ifc}-m conntrack --ctstate DNAT --ctorigdstport $HTTPS_PORT --ctdir ORIGINAL -s $src -j RETURN"
    done
  done
  for p in tcp udp; do
    echo "-A $c -p $p -m conntrack --ctstate DNAT --ctorigdstport $HTTPS_PORT --ctdir ORIGINAL -j DROP"
  done
  echo "-A $c -j RETURN"
}

need_docker_user() {
  $IPT -n -L DOCKER-USER >/dev/null 2>&1 || die "la cadena DOCKER-USER no existe: Docker no esta en ejecucion (systemctl start docker)"
}

# Reemplazo ATOMICO: se arma BURY-ERP-NEW completa, se engancha primero, se retira el enganche viejo y recien entonces se renombra.
# En ningun momento DOCKER-USER queda sin cadena (no hay ventana abierta al reaplicar) ni con una cadena a medio llenar.
do_apply() {
  validate
  need_docker_user
  local new="$CHAIN-NEW" r
  # Si una ejecucion anterior se corto antes de renombrar, la cadena -NEW es la UNICA enganchada: se renombra en vez de retirarla.
  if $IPT -C DOCKER-USER -j "$new" 2>/dev/null && ! $IPT -C DOCKER-USER -j "$CHAIN" 2>/dev/null; then
    $IPT -F "$CHAIN" 2>/dev/null || true
    $IPT -X "$CHAIN" 2>/dev/null || true
    $IPT -E "$new" "$CHAIN"
  fi
  while $IPT -D DOCKER-USER -j "$new" 2>/dev/null; do :; done
  $IPT -F "$new" 2>/dev/null || true
  $IPT -X "$new" 2>/dev/null || true
  $IPT -N "$new"
  while IFS= read -r r; do
    # shellcheck disable=SC2086
    $IPT $r
  done < <(rules "$new")
  $IPT -I DOCKER-USER 1 -j "$new"
  while $IPT -D DOCKER-USER -j "$CHAIN" 2>/dev/null; do :; done
  $IPT -F "$CHAIN" 2>/dev/null || true
  $IPT -X "$CHAIN" 2>/dev/null || true
  $IPT -E "$new" "$CHAIN"
  echo "bury-firewall: HTTPS $HTTPS_PORT permitido solo desde: $ALLOW_FROM"
}

# Configuraciones de dockerd que dejan a DOCKER-USER sin efecto: sin iptables, el puerto lo atiende docker-proxy por INPUT; con el backend
# nftables Docker no usa DOCKER-USER. En ambos casos este filtro no protege y no se debe arrancar.
check_daemon_conf() {
  local f="${BURY_DAEMON_JSON:-/etc/docker/daemon.json}" flat
  [[ -f "$f" ]] || return 0
  flat=$(tr -d '[:space:]' <"$f")
  [[ "$flat" != *'"iptables":false'* ]] || die "$f define \"iptables\": false: DOCKER-USER no filtraria el puerto publicado"
  [[ "$flat" != *'"firewall-backend":"nftables"'* ]] || die "$f usa firewall-backend nftables: Docker no consulta DOCKER-USER"
}

do_guard() {
  validate
  check_daemon_conf
  # En un arranque en frio dockerd todavia no creo DOCKER-USER: se crea aqui para que el filtro exista ANTES de que publique puertos.
  # dockerd conserva el contenido de esa cadena y agrega el salto desde FORWARD.
  $IPT -n -L DOCKER-USER >/dev/null 2>&1 || $IPT -N DOCKER-USER || die "no se pudo crear la cadena DOCKER-USER"
  do_apply
}

# El ORDEN decide si filtra: primero los RETURN de los origenes (A), despues los DROP (D) y recien al final el RETURN sin condiciones (E).
chain_order_ok() {
  local c=$1 line orden=""
  while IFS= read -r line; do
    case "$line" in
      "-A $c -j RETURN") orden+=E ;;
      "-A $c "*" -j RETURN") orden+=A ;;
      "-A $c "*" -j DROP") orden+=D ;;
      "-A $c "*) orden+=X ;;
    esac
  done < <($IPT -S "$c" 2>/dev/null)
  [[ "$orden" =~ ^A+D+E$ ]]
}

# Solo lectura. Comprueba el recorrido completo: FORWARD -> DOCKER-USER (antes de las reglas de Docker) -> cadena propia con sus reglas exactas.
do_verify() {
  validate
  check_daemon_conf
  local line hooked=0 r total=0
  while IFS= read -r line; do
    [[ "$line" == "-A FORWARD "* ]] || continue
    [[ "$line" == "-A FORWARD -j DOCKER-USER" ]] && { hooked=1; break; }
    case "$line" in
      *" -j ACCEPT"*|*" -j DOCKER"|*" -j DOCKER-"*) die "verify: hay una regla de FORWARD anterior al salto a DOCKER-USER: $line" ;;
    esac
  done < <($IPT -S FORWARD)
  (( hooked )) || die "verify: FORWARD no salta a DOCKER-USER: el puerto publicado no pasa por el filtro"
  hooked=0
  while IFS= read -r line; do
    [[ "$line" == "-A DOCKER-USER "* ]] || continue
    [[ "$line" == "-A DOCKER-USER -j $CHAIN" ]] && { hooked=1; break; }
    case "$line" in
      *"-j ACCEPT"*|"-A DOCKER-USER -j RETURN") die "verify: una regla anterior evita la cadena $CHAIN: $line" ;;
    esac
  done < <($IPT -S DOCKER-USER)
  (( hooked )) || die "verify: DOCKER-USER no salta a $CHAIN"
  while IFS= read -r r; do
    total=$((total+1))
    # shellcheck disable=SC2086
    $IPT ${r/#-A /-C } 2>/dev/null || die "verify: falta la regla: $r"
  done < <(rules "$CHAIN")
  [[ "$($IPT -S "$CHAIN" | grep -c "^-A ")" == "$total" ]] || die "verify: la cadena $CHAIN tiene reglas de mas o de menos (se esperaban $total)"
  chain_order_ok "$CHAIN" || die "verify: las reglas de $CHAIN estan en un orden que no filtra"
  echo "bury-firewall: verificado ($CHAIN, puerto $HTTPS_PORT)"
}

# guard/verify de las instalaciones adicionales. Con BURY_FW_CONF explicito se opera solo sobre ese archivo.
for_extras() {   # for_extras guard|verify
  [[ -z "${BURY_FW_CONF:-}" ]] || return 0
  local f n
  for f in "$(dirname "$CONF")"/firewall-*.conf; do
    [[ -f "$f" ]] || continue
    n=${f##*/firewall-}; n=${n%.conf}
    [[ "$n" =~ ^[A-Za-z0-9_-]{1,15}$ ]] || die "nombre de instalacion invalido en $f (letras, numeros, _ o -; maximo 15)"
    BURY_FW_CONF="$f" BURY_FW_CHAIN="BURY-ERP-${n^^}" bash "${BASH_SOURCE[0]}" "$1" || die "fallo '$1' para $f"
  done
}

do_install_systemd() {
  local dir="${BURY_SYSTEMD_DIR:-/etc/systemd/system}" bin="${BURY_BIN:-/usr/local/sbin/bury-firewall}"
  install -d -m 0755 "$dir/docker.service.d"
  # Sin ExecStop: detener o reiniciar la unidad NUNCA debe abrir el puerto.
  cat >"$dir/bury-firewall.service" <<UNIT
[Unit]
Description=TheBury: firewall de los puertos publicados por Docker (DOCKER-USER)
Requires=docker.service
After=docker.service network-online.target
PartOf=docker.service

[Service]
Type=oneshot
RemainAfterExit=yes
ExecStart=$bin apply

[Install]
WantedBy=multi-user.target
UNIT
  # Independiente de docker.service (sin PartOf/Requires): debe seguir informando aunque Docker este caido.
  cat >"$dir/bury-firewall-watch.service" <<UNIT
[Unit]
Description=TheBury: vigilancia del firewall con Docker en marcha
After=docker.service

[Service]
Type=simple
ExecStart=$bin watch
Restart=always
RestartSec=5

[Install]
WantedBy=multi-user.target
UNIT
  # Sin el prefijo '-': si cualquiera de los dos falla, docker.service no queda en marcha.
  {
    echo '[Service]'
    echo "ExecStartPre=$bin guard"
    echo "ExecStartPost=$bin verify"
  } >"$dir/docker.service.d/bury-firewall.conf"
  # Unidades copiadas de la version anterior (segunda instalacion): se les quita el ExecStop que abria el puerto al detenerlas.
  local u
  for u in "$dir"/bury-firewall-*.service; do
    [[ -f "$u" ]] && grep -q '^ExecStop=.*bury-firewall.* remove' "$u" || continue
    sed -i '/^ExecStop=.*bury-firewall.* remove/d' "$u"
    echo "bury-firewall: retirado ExecStop=remove de $u"
  done
  echo "bury-firewall: unidad y drop-in instalados en $dir"
}

# ---------------------------------------------------------------- vigilancia en ejecucion (bury-firewall-watch.service)
# Detecta reglas borradas o alteradas con Docker en marcha. Secuencia ante un fallo confirmado: 1) BARRERA (descarta todo lo entrante hacia
# contenedores) para cerrar la exposicion; 2) reponer; 3) si se perdieron las reglas propias de Docker, UN reinicio de Docker (pasa por guard y
# verify); 4) si aun asi no se puede comprobar, detener Docker. Cada fallo abre un INCIDENTE que queda abierto hasta 'ack <id>'.
# Nunca arranca un Docker detenido. Ventana de exposicion: hasta un intervalo (BURY_WATCH_INTERVAL, 10 s) mas la comprobacion repetida.
STATE_DIR="${BURY_STATE_DIR:-/var/lib/bury-firewall}"
LOCK_FILE="${BURY_LOCK:-/run/bury-firewall.lock}"
SYSTEMCTL="${BURY_SYSTEMCTL:-systemctl}"
BARRIER=(-m conntrack --ctstate DNAT -m comment --comment bury-barrera -j DROP)

# Bloqueo comun de apply/guard/verify/vigilancia. Los hijos (for_extras) heredan el del padre. Sin flock o sin permiso sobre /run se sigue.
lock_fw() {
  [[ -z "${BURY_FW_LOCKED:-}" ]] || return 0
  command -v flock >/dev/null 2>&1 && [[ -w "$(dirname "$LOCK_FILE")" ]] || return 0
  exec 9>"$LOCK_FILE"
  flock -w 60 9 || die "no se pudo tomar el bloqueo $LOCK_FILE"
  export BURY_FW_LOCKED=1
}
# Se libera ANTES de pedirle algo a systemd sobre docker.service: sus ExecStartPre/Post necesitan este mismo bloqueo.
unlock_fw() { { exec 9>&-; } 2>/dev/null || true; unset BURY_FW_LOCKED; }

barrier_off() { while $IPT -D DOCKER-USER "${BARRIER[@]}" 2>/dev/null; do :; done; }
forward_jump_ok() {
  local line
  while IFS= read -r line; do
    [[ "$line" == "-A FORWARD -j DOCKER-USER" ]] && return 0
    case "$line" in "-A FORWARD "*" -j ACCEPT"*|"-A FORWARD "*" -j DOCKER"|"-A FORWARD "*" -j DOCKER-"*) return 1 ;; esac
  done < <($IPT -S FORWARD 2>/dev/null)
  return 1
}
ensure_forward_jump() {
  forward_jump_ok && return 0
  while $IPT -D FORWARD -j DOCKER-USER 2>/dev/null; do :; done
  $IPT -I FORWARD 1 -j DOCKER-USER
}
barrier_on() {
  $IPT -n -L DOCKER-USER >/dev/null 2>&1 || $IPT -N DOCKER-USER 2>/dev/null || true
  $IPT -C DOCKER-USER "${BARRIER[@]}" 2>/dev/null || $IPT -I DOCKER-USER 1 "${BARRIER[@]}" || true
  ensure_forward_jump || true
}
# Reglas que pone el propio Docker: sin ellas el puerto lo atiende docker-proxy por INPUT (sin pasar por DOCKER-USER) o los contenedores
# quedan incomunicados. No se pueden reponer a mano: requieren reiniciar Docker.
docker_rules_ok() {
  grep -Eq -- '-j DOCKER(-FORWARD)?( |$)' <<<"$($IPT -S FORWARD 2>/dev/null)" &&
  grep -Eq -- '-j DOCKER( |$)' <<<"$($IPT -t nat -S PREROUTING 2>/dev/null)"
}
# La proteccion vigente, sin depender de la configuracion (para no cortar el servicio solo porque alguien edito mal firewall.conf).
structure_ok() {
  forward_jump_ok || return 1
  local c f n chains=("$CHAIN")
  for f in "$(dirname "$CONF")"/firewall-*.conf; do [[ -f "$f" ]] || continue; n=${f##*/firewall-}; n=${n%.conf}; chains+=("BURY-ERP-${n^^}"); done
  for c in "${chains[@]}"; do $IPT -C DOCKER-USER -j "$c" 2>/dev/null && chain_order_ok "$c" || return 1; done
}
self() { bash "${BASH_SOURCE[0]}" "$@"; }
rules_ok() { VERIFY_MSG=$(self verify 2>&1) || return 1; docker_rules_ok || { VERIFY_MSG="faltan las reglas propias de Docker (FORWARD o nat PREROUTING)"; return 1; }; }
docker_active() { [[ "$($SYSTEMCTL is-active docker.service 2>/dev/null)" == active ]]; }
docker_identity() { $SYSTEMCTL show docker.service -p MainPID -p ActiveEnterTimestampMonotonic 2>/dev/null | tr '\n' ' '; }
same_docker() { docker_active && [[ "$(docker_identity)" == "$1" ]]; }

now() { date +%s; }
# Con 'set -e' y pipefail, un archivo inexistente no puede abortar a quien los llama (abortaba 'ack' despues de borrar el incidente).
state_field() { sed -n "s/.*\b$1=\([^ ]*\).*/\1/p" "$STATE_DIR/estado" 2>/dev/null | head -1 || true; }
open_incident_id() { sed -n 's/^id=//p' "$STATE_DIR/incidente" 2>/dev/null | head -1 || true; }
write_state() {   # write_state ESTADO  (publicacion atomica; 'latido' avanza siempre, 'ultima_ok' solo cuando todo se comprobo)
  local ok id tmp
  ok=$(state_field ultima_ok); [[ "$1" == ok ]] && ok=$(now)
  id=$(open_incident_id)
  mkdir -p "$STATE_DIR" && chmod 755 "$STATE_DIR"
  tmp=$(mktemp "$STATE_DIR/.estado.XXXXXX") || return 1
  printf 'latido=%s estado=%s ultima_ok=%s incidente=%s\n' "$(now)" "$1" "${ok:-0}" "${id:--}" >"$tmp"
  chmod 644 "$tmp"; mv -f "$tmp" "$STATE_DIR/estado"
}
note() {   # note MENSAJE : journal con prioridad critica + historial persistente
  echo "<2>bury-firewall: $*"
  mkdir -p "$STATE_DIR"; printf '%s %s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$*" >>"$STATE_DIR/incidentes.log"
}
open_incident() {
  mkdir -p "$STATE_DIR" && chmod 755 "$STATE_DIR"
  [[ -f "$STATE_DIR/incidente" ]] || { printf 'id=%s\nmotivo=%s\n' "$(now)" "$1" >"$STATE_DIR/incidente"; chmod 644 "$STATE_DIR/incidente"; }
  note "INCIDENTE: $1"
}
stop_docker() {
  note "no se puede garantizar el filtro ($1): se DETIENE Docker (el ERP y los respaldos quedan detenidos)"
  $SYSTEMCTL stop docker.socket docker.service || true
  if docker_active; then note "CIERRE NO CONFIRMADO: Docker sigue activo; la barrera queda puesta; intervenir ya"; else note "Docker detenido"; fi
  write_state detenido
}

watch_once() {
  if ! docker_active; then
    # No se actua sobre un Docker detenido o en transicion. 'detenido' se conserva hasta que alguien lo recupere.
    if [[ "$(state_field estado)" == detenido ]]; then write_state detenido; else write_state docker-inactivo; fi
    return 0
  fi
  local id0 reason last
  id0=$(docker_identity)
  lock_fw
  if ! $IPT -S >/dev/null 2>&1 || ! $IPT -t nat -S >/dev/null 2>&1; then unlock_fw; echo "bury-firewall: no se pudieron leer las reglas; no se actua"; write_state ilegible; return 0; fi
  if rules_ok; then barrier_off; unlock_fw; write_state ok; return 0; fi
  # Segunda lectura: Docker reacomoda sus cadenas al crear redes o contenedores.
  unlock_fw; sleep "${BURY_WATCH_RECHECK:-1}"; lock_fw
  if ! same_docker "$id0"; then unlock_fw; write_state docker-inactivo; return 0; fi
  if rules_ok; then barrier_off; unlock_fw; write_state ok; return 0; fi
  reason=$(tail -n1 <<<"$VERIFY_MSG")

  if ! self check-config >/dev/null 2>&1; then
    if structure_ok && docker_rules_ok; then
      unlock_fw
      [[ "$(state_field estado)" == config-invalida ]] || open_incident "configuracion invalida con las reglas anteriores vigentes: corregirla antes de reiniciar Docker ($reason)"
      write_state config-invalida; return 0
    fi
    open_incident "$reason"; barrier_on; unlock_fw; stop_docker "configuracion invalida y reglas incompletas"; return 0
  fi

  open_incident "$reason"
  barrier_on
  if self rearm >/dev/null 2>&1 && ensure_forward_jump && rules_ok; then
    barrier_off; unlock_fw; note "reglas repuestas sin reiniciar Docker"; write_state incidente; return 0
  fi
  last=$(cat "$STATE_DIR/ultimo-reinicio" 2>/dev/null || echo 0)
  if ! docker_rules_ok && (( $(now) - last >= ${BURY_WATCH_RESTART_WINDOW:-600} )); then
    unlock_fw
    same_docker "$id0" || { write_state docker-inactivo; return 0; }
    now >"$STATE_DIR/ultimo-reinicio"
    note "faltan reglas propias de Docker: se reinicia Docker una vez"
    $SYSTEMCTL restart docker.service || true
    if docker_active; then
      lock_fw
      if rules_ok; then barrier_off; unlock_fw; note "recuperado reiniciando Docker"; write_state incidente; return 0; fi
      barrier_on; unlock_fw
    fi
    stop_docker "no se recupero tras reiniciar"; return 0
  fi
  unlock_fw
  same_docker "$id0" || { write_state docker-inactivo; return 0; }
  stop_docker "la reposicion fallo"
}

do_watch() {
  local every="${BURY_WATCH_INTERVAL:-10}"
  echo "bury-firewall: vigilancia activa cada ${every}s"
  while :; do ( watch_once ) || echo "bury-firewall: un ciclo de vigilancia termino con error"; sleep "$every"; done
}
do_ack() {
  local id; id=$(open_incident_id)
  [[ -n "$id" ]] || die "no hay un incidente abierto"
  [[ "${1:-}" == "$id" ]] || die "indicar el id del incidente abierto: ack $id (revisar antes $STATE_DIR/incidentes.log)"
  rm -f "$STATE_DIR/incidente"; note "incidente $id reconocido"
  write_state "$([[ "$(state_field estado)" == incidente ]] && echo ok || state_field estado)"
}

do_remove() {
  while $IPT -D DOCKER-USER -j "$CHAIN" 2>/dev/null; do :; done
  $IPT -F "$CHAIN" 2>/dev/null || true
  $IPT -X "$CHAIN" 2>/dev/null || true
  echo "bury-firewall: regla retirada (HTTPS abierto segun los valores por defecto de Docker)"
}

case "$ACTION" in apply|remove|guard|rearm|verify) lock_fw ;; esac
case "$ACTION" in
  apply) do_apply ;;
  remove) do_remove ;;
  # La barrera que pudo dejar la vigilancia se retira solo en el nivel principal y despues de aplicar todas las cadenas.
  guard) do_guard; for_extras guard; [[ -n "${BURY_FW_CONF:-}" ]] || barrier_off ;;
  rearm) do_guard; for_extras guard ;;     # interno de la vigilancia: igual que guard, SIN retirar la barrera
  check-config) validate; check_daemon_conf; for_extras check-config ;;
  watch) do_watch ;;
  watch-once) watch_once ;;
  ack) do_ack "${2:-}" ;;
  verify) do_verify; for_extras verify ;;
  install-systemd) do_install_systemd ;;
  print) validate; echo "$IPT -N $CHAIN-NEW"; rules "$CHAIN-NEW" | sed "s|^|$IPT |"; echo "$IPT -I DOCKER-USER 1 -j $CHAIN-NEW"; echo "(luego: retirar el enganche a $CHAIN, borrar $CHAIN y renombrar $CHAIN-NEW a $CHAIN)" ;;
  status) $IPT -n -v -L "$CHAIN" 2>/dev/null || echo "bury-firewall: la cadena $CHAIN no existe (aplicar con: apply)"; $IPT -n -L DOCKER-USER --line-numbers ;;
  *) sed -n '2,13p' "$0"; exit 1 ;;
esac
