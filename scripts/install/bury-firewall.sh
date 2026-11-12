#!/usr/bin/env bash
# Firewall de los puertos que PUBLICA Docker (443 de Caddy) para el servidor Linux/VM de TheBuryProject.
#
#   bury-firewall.sh apply      reconstruye la cadena BURY-ERP y la engancha al inicio de DOCKER-USER
#   bury-firewall.sh remove     quita el enganche y la cadena (deja Docker intacto)
#   bury-firewall.sh print      muestra los comandos iptables que ejecutaria 'apply' (no cambia nada, no requiere root)
#   bury-firewall.sh status     muestra la cadena BURY-ERP actual
#
# POR QUE existe: ufw/INPUT NO filtra los puertos publicados por Docker (el trafico DNAT-eado pasa por FORWARD y Docker agrega
# sus propias reglas antes que las de ufw). La unica cadena que Docker garantiza respetar es DOCKER-USER. Sin esto, 443 quedaria
# abierto a cualquier red que alcance la VM aunque ufw diga "deny incoming".
#
# Configuracion (archivo de shell, por defecto /etc/bury/firewall.conf; sobrescribible con BURY_FW_CONF):
#   ALLOW_FROM="192.168.1.0/24 100.64.0.0/10"     redes/IPs con permiso a HTTPS (OBLIGATORIO; vacio = no se aplica nada)
#   HTTPS_PORT=443                                 puerto original publicado por Caddy (ERP_HTTPS_PORT)
# IMPORTANTE: las reglas llevan `--ctstate DNAT`: solo alcanzan a las conexiones ENTRANTES redirigidas a un contenedor (puerto publicado).
# Sin eso, `ctorigdstport 443` tambien descartaria el HTTPS SALIENTE de los contenedores (NuGet, ACME, SFTP...), porque su destino original es :443.
# Solo IPv4: el compose publica con ERP_BIND_ADDRESS=0.0.0.0 (IPv4 explicito), asi que Docker no escucha en IPv6 (verificar con `ss -ltn | grep :443`). Idempotente: se puede ejecutar cuantas veces haga falta (lo hace bury-firewall.service).
set -Eeuo pipefail

CONF="${BURY_FW_CONF:-/etc/bury/firewall.conf}"
IPT="${BURY_IPTABLES:-iptables -w 5}"   # -w: esperar el lock de xtables (Docker toca las mismas tablas al arrancar)
CHAIN="BURY-ERP"
ACTION="${1:-}"

ALLOW_FROM=""
HTTPS_PORT=443
# shellcheck disable=SC1090
[[ -f "$CONF" ]] && source "$CONF"

die() { echo "bury-firewall: $*" >&2; exit 1; }

# IPv4 o CIDR: octetos 0-255 sin ceros a la izquierda (iptables los leeria como octal) y prefijo 1-32 (/0 anularia el filtro).
valid_src() {
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
  local c=$1 s p
  for s in $ALLOW_FROM; do
    for p in tcp udp; do
      echo "-A $c -p $p -m conntrack --ctstate DNAT --ctorigdstport $HTTPS_PORT --ctdir ORIGINAL -s $s -j RETURN"
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

do_remove() {
  while $IPT -D DOCKER-USER -j "$CHAIN" 2>/dev/null; do :; done
  $IPT -F "$CHAIN" 2>/dev/null || true
  $IPT -X "$CHAIN" 2>/dev/null || true
  echo "bury-firewall: regla retirada (HTTPS abierto segun los valores por defecto de Docker)"
}

case "$ACTION" in
  apply) do_apply ;;
  remove) do_remove ;;
  print) validate; echo "$IPT -N $CHAIN-NEW"; rules "$CHAIN-NEW" | sed "s|^|$IPT |"; echo "$IPT -I DOCKER-USER 1 -j $CHAIN-NEW"; echo "(luego: retirar el enganche a $CHAIN, borrar $CHAIN y renombrar $CHAIN-NEW a $CHAIN)" ;;
  status) $IPT -n -v -L "$CHAIN" 2>/dev/null || echo "bury-firewall: la cadena $CHAIN no existe (aplicar con: apply)"; $IPT -n -L DOCKER-USER --line-numbers ;;
  *) sed -n '2,8p' "$0"; exit 1 ;;
esac
