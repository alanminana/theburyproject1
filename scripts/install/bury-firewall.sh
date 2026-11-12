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
# Solo IPv4 (el compose no publica IPv6). Idempotente: se puede ejecutar cuantas veces haga falta (lo hace bury-firewall.service).
set -Eeuo pipefail

CONF="${BURY_FW_CONF:-/etc/bury/firewall.conf}"
IPT="${BURY_IPTABLES:-iptables}"
CHAIN="BURY-ERP"
ACTION="${1:-}"

ALLOW_FROM=""
HTTPS_PORT=443
# shellcheck disable=SC1090
[[ -f "$CONF" ]] && source "$CONF"

die() { echo "bury-firewall: $*" >&2; exit 1; }

valid_src() { [[ "$1" =~ ^([0-9]{1,3}\.){3}[0-9]{1,3}(/([0-9]|[12][0-9]|3[0-2]))?$ ]]; }

validate() {
  [[ "$HTTPS_PORT" =~ ^[0-9]+$ ]] && (( HTTPS_PORT >= 1 && HTTPS_PORT <= 65535 )) || die "HTTPS_PORT invalido: '$HTTPS_PORT'"
  [[ -n "${ALLOW_FROM// /}" ]] || die "ALLOW_FROM esta vacio ($CONF): no se aplica nada para no dejar a todos sin acceso"
  local s
  for s in $ALLOW_FROM; do valid_src "$s" || die "ALLOW_FROM contiene un valor invalido: '$s' (use IPv4 o CIDR)"; done
}

# Emite, una por linea, los comandos de la cadena (sin el enganche a DOCKER-USER).
rules() {
  local s p
  for s in $ALLOW_FROM; do
    for p in tcp udp; do
      echo "-A $CHAIN -p $p -m conntrack --ctstate DNAT --ctorigdstport $HTTPS_PORT --ctdir ORIGINAL -s $s -j RETURN"
    done
  done
  for p in tcp udp; do
    echo "-A $CHAIN -p $p -m conntrack --ctstate DNAT --ctorigdstport $HTTPS_PORT --ctdir ORIGINAL -j DROP"
  done
  echo "-A $CHAIN -j RETURN"
}

need_docker_user() {
  $IPT -n -L DOCKER-USER >/dev/null 2>&1 || die "la cadena DOCKER-USER no existe: Docker no esta en ejecucion (systemctl start docker)"
}

do_apply() {
  validate
  need_docker_user
  $IPT -N "$CHAIN" 2>/dev/null || true
  $IPT -F "$CHAIN"
  local r
  while IFS= read -r r; do
    # shellcheck disable=SC2086
    $IPT $r
  done < <(rules)
  while $IPT -D DOCKER-USER -j "$CHAIN" 2>/dev/null; do :; done
  $IPT -I DOCKER-USER 1 -j "$CHAIN"
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
  print) validate; echo "$IPT -N $CHAIN"; echo "$IPT -F $CHAIN"; rules | sed "s|^|$IPT |"; echo "$IPT -I DOCKER-USER 1 -j $CHAIN" ;;
  status) $IPT -n -v -L "$CHAIN" 2>/dev/null || echo "bury-firewall: la cadena $CHAIN no existe (aplicar con: apply)"; $IPT -n -L DOCKER-USER --line-numbers ;;
  *) sed -n '2,8p' "$0"; exit 1 ;;
esac
