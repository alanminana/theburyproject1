#!/usr/bin/env bash
# Prepara un Ubuntu Server 22.04/24.04 (la VM de Hyper-V de Windows Server, o cualquier Linux) como anfitrion de TheBuryProject:
# Docker Engine + Compose v2, firewall de los puertos publicados, cierre ordenado de SQL Server y, opcionalmente, Tailscale.
# NO instala el ERP (eso es scripts/install/install.sh) ni toca datos. Es idempotente.
#
#   sudo scripts/install/server-linux-bootstrap.sh --user bury --allow-from 192.168.1.0/24[,100.64.0.0/10]
#        [--admin-from 100.64.0.0/10] [--https-port 443] [--backup-dir /srv/bury-backups] [--tailscale]
#        [--allow-lan-admin] [--skip-docker] [--no-ufw] [--dry-run]
#
#   --user USER        usuario que administra el ERP; es el dueño de --backup-dir y queda en el grupo docker, que EQUIVALE A ROOT
#                      en el servidor (un miembro puede montar el disco o vaciar las reglas del firewall): usar solo la cuenta de administracion [obligatorio]
#   --allow-from LIST  redes con acceso HTTPS al ERP (CSV de IPv4/CIDR[@interfaz]; 100.64.0.0/10 se ata a tailscale0). Se aplica en DOCKER-USER (ver bury-firewall.sh)  [obligatorio]
#   --admin-from LIST  quien puede administrar por SSH (22). Si se omite NO se activa ufw (evita dejar la VM sin acceso por error).
#                      Preferir la IP /32 de Tailscale del administrador (p. ej. 100.101.2.3): con el rango 100.64.0.0/10 la capa del host NO distingue entre el
#                      administrador y el usuario remoto (solo la politica de Tailscale los separa).
#                      MINIMO PRIVILEGIO: solo (a) la red de Tailscale (100.64.0.0/10 o una IP/subred dentro de ella; se ata a tailscale0, igual que
#                      --allow-from) o (b) EQUIPOS concretos privados (IPv4 /32 en 10/8, 172.16/12, 192.168/16; p. ej. el host Hyper-V que instala la VM). Una red LAN completa se RECHAZA: los
#                      usuarios de la LAN solo tienen acceso a la web HTTPS, nunca a la administracion (ver docs/p05-permisos-acceso-remoto.md).
#   --allow-lan-admin  excepcion consciente: permite una red completa (no Tailscale, no /32) en --admin-from. No recomendado.
#   --backup-dir DIR   crea el directorio de backups (700, dueño --user) si no existe; nunca cambia uno existente
#   --tailscale        instala el paquete y habilita tailscaled (NO lo autentica: 'sudo tailscale up' lo hace el propietario)
#   --skip-docker      no instala Docker (ya esta instalado)       --no-ufw   no configura ufw       --dry-run   solo informa
set -Eeuo pipefail

RUN_USER="" ALLOW_FROM="" ADMIN_FROM="" HTTPS_PORT=443 BACKUP_DIR="" WANT_TS=0 SKIP_DOCKER=0 NO_UFW=0 DRY=0 LAN_ADMIN=0
REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

aptq() { run apt-get -o DPkg::Lock::Timeout=300 "$@"; }
log() { printf '%s %s\n' "[$(date -u +%H:%M:%S)]" "$*"; }
die() { printf 'ERROR: %s\n' "$*" >&2; exit 1; }
run() { if (( DRY )); then log "[dry-run] $*"; else "$@"; fi; }

while (( $# )); do
  case "$1" in
    --user|--allow-from|--admin-from|--https-port|--backup-dir) [[ $# -ge 2 ]] || die "falta el valor de $1" ;;&
    --user) RUN_USER="${2:-}"; shift 2 ;;
    --allow-from) ALLOW_FROM="${2:-}"; shift 2 ;;
    --admin-from) ADMIN_FROM="${2:-}"; shift 2 ;;
    --https-port) HTTPS_PORT="${2:-}"; shift 2 ;;
    --backup-dir) BACKUP_DIR="${2:-}"; shift 2 ;;
    --tailscale) WANT_TS=1; shift ;;
    --skip-docker) SKIP_DOCKER=1; shift ;;
    --no-ufw) NO_UFW=1; shift ;;
    --allow-lan-admin) LAN_ADMIN=1; shift ;;
    --dry-run) DRY=1; shift ;;
    -h|--help) sed -n '2,24p' "$0"; exit 0 ;;
    *) die "argumento desconocido: $1" ;;
  esac
done

# CSV de IPv4/CIDR: al menos UN elemento; octetos 0-255 sin ceros a la izquierda; prefijo 1-32 (/0 anularia el filtro). Igual que bury-firewall.sh.
valid_one() {
  [[ "$1" =~ ^([0-9]{1,3})\.([0-9]{1,3})\.([0-9]{1,3})\.([0-9]{1,3})(/([1-9]|[12][0-9]|3[0-2]))?$ ]] || return 1
  local o; for o in "${BASH_REMATCH[1]}" "${BASH_REMATCH[2]}" "${BASH_REMATCH[3]}" "${BASH_REMATCH[4]}"; do
    (( 10#$o <= 255 )) && [[ "$o" == 0 || "$o" != 0* ]] || return 1
  done
}
# --allow-from admite CIDR@interfaz (el origen solo se acepta si entra por esa interfaz; ver bury-firewall.sh).
valid_one_if() { [[ "$1" != *@* || "${1#*@}" =~ ^[A-Za-z0-9_.-]{1,15}$ ]] && valid_one "${1%%@*}"; }
valid_allow() { local s n=0; for s in ${1//,/ }; do valid_one_if "$s" || return 1; n=$((n+1)); done; (( n > 0 )); }
valid_list() { local s n=0; for s in ${1//,/ }; do valid_one "$s" || return 1; n=$((n+1)); done; (( n > 0 )); }
[[ "$RUN_USER" =~ ^[a-z_][a-z0-9_-]*$ ]] || die "--user es obligatorio (usuario Linux valido)"
id "$RUN_USER" >/dev/null 2>&1 || die "el usuario '$RUN_USER' no existe"
[[ -n "$ALLOW_FROM" ]] && valid_allow "$ALLOW_FROM" || die "--allow-from es obligatorio (CSV de IPv4/CIDR[@interfaz])"
# La red de Tailscale (100.64.0.0/10) solo se acepta si el paquete ENTRA por tailscale0 (evita suplantar un origen 100.x desde la LAN).
# Cualquier origen dentro de 100.64.0.0/10 (red, subred o IP suelta) se ata a tailscale0 si no trae interfaz propia.
FW_ALLOW=""; for s in ${ALLOW_FROM//,/ }; do
  if [[ "$s" != *@* && "$s" =~ ^100\.([0-9]{1,3})\. ]] && (( 10#${BASH_REMATCH[1]} >= 64 && 10#${BASH_REMATCH[1]} <= 127 )); then s="$s@tailscale0"; fi
  FW_ALLOW+="$s "
done; FW_ALLOW=${FW_ALLOW% }
[[ -z "$ADMIN_FROM" ]] || valid_list "$ADMIN_FROM" || die "--admin-from invalido (CSV de IPv4/CIDR)"
# Administracion (SSH 22) con minimo privilegio: la red de Tailscale (atada a tailscale0) o equipos concretos (/32). Una red LAN completa abriria
# la administracion a todos los usuarios de la LAN (que solo deben tener HTTPS): se rechaza salvo --allow-lan-admin.
in_tailnet() { # in_tailnet IP[/N]: dentro de 100.64.0.0/10 (segundo octeto 64-127 y prefijo >= 10 si lo trae)
  [[ "$1" =~ ^100\.([0-9]{1,3})\.[0-9]{1,3}\.[0-9]{1,3}(/([0-9]{1,2}))?$ ]] || return 1
  (( 10#${BASH_REMATCH[1]} >= 64 && 10#${BASH_REMATCH[1]} <= 127 )) && { [[ -z "${BASH_REMATCH[3]}" ]] || (( 10#${BASH_REMATCH[3]} >= 10 )); }
}
is_private_host() { # RFC 1918
  [[ "$1" =~ ^([0-9]{1,3})\.([0-9]{1,3})\.[0-9]{1,3}\.[0-9]{1,3}$ ]] || return 1
  local a=$((10#${BASH_REMATCH[1]})) b=$((10#${BASH_REMATCH[2]}))
  (( a == 10 )) || (( a == 172 && b >= 16 && b <= 31 )) || (( a == 192 && b == 168 ))
}
UFW_ADMIN=""; for s in ${ADMIN_FROM//,/ }; do
  if in_tailnet "$s"; then UFW_ADMIN+="$s@tailscale0 "
  elif [[ "$s" != */* || "$s" == */32 ]]; then
    # Equipo concreto: debe ser privado (RFC 1918). Un equipo publico abriria la administracion a Internet (o 0.0.0.0 a todos): se rechaza salvo --allow-lan-admin.
    if ! is_private_host "${s%/32}" && (( ! LAN_ADMIN )); then die "--admin-from: '$s' no es un equipo privado (10/8, 172.16/12, 192.168/16) ni de la tailnet: la administracion no se abre a Internet"; fi
    UFW_ADMIN+="${s%/32} "
  elif (( LAN_ADMIN )); then UFW_ADMIN+="$s "
  else die "--admin-from: '$s' es una red completa. La administracion (SSH) solo se permite desde la red de Tailscale (100.64.0.0/10) o desde equipos concretos (/32); los usuarios de la LAN solo deben tener HTTPS. Use una IP /32 o --allow-lan-admin (no recomendado)"
  fi
done; UFW_ADMIN=${UFW_ADMIN% }
[[ "$HTTPS_PORT" =~ ^[0-9]+$ ]] && (( HTTPS_PORT >= 1 && HTTPS_PORT <= 65535 )) || die "--https-port invalido"
[[ -z "$BACKUP_DIR" || "$BACKUP_DIR" =~ ^/[A-Za-z0-9_./-]+$ ]] || die "--backup-dir debe ser una ruta absoluta sin caracteres especiales"
(( DRY )) || [[ $EUID -eq 0 ]] || die "ejecutar con sudo/root (o --dry-run)"

# Antes de activar ufw: la sesion SSH actual debe estar dentro de --admin-from (si no, la VM queda sin acceso remoto: solo la consola de Hyper-V).
ip_in_list() { # ip_in_list IP CSV
  command -v python3 >/dev/null 2>&1 || return 0
  python3 - "$1" "${2//,/ }" <<'PY'
import ipaddress, sys
ip = ipaddress.ip_address(sys.argv[1])
sys.exit(0 if any(ip in ipaddress.ip_network(n, strict=False) for n in sys.argv[2].split()) else 1)
PY
}
# Origen de la sesion SSH actual. `sudo` suele descartar SSH_CLIENT (env_reset): se recurre a `who -m` ("(ip)" del tty).
ssh_ip=""
[[ -n "${SSH_CLIENT:-}" ]] && ssh_ip=${SSH_CLIENT%% *}
if [[ -z "$ssh_ip" ]] && command -v who >/dev/null 2>&1; then ssh_ip=$(who -m 2>/dev/null | sed -n 's/.*(\([0-9][0-9.]*\)).*/\1/p' | head -1); fi
if [[ -n "$ADMIN_FROM" && ( $NO_UFW -eq 0 ) && -n "$ssh_ip" ]]; then
  ip_in_list "$ssh_ip" "$ADMIN_FROM" || die "tu sesion SSH actual ($ssh_ip) no esta dentro de --admin-from ($ADMIN_FROM): ufw te dejaria sin acceso. Agregala o usa --no-ufw"
fi
[[ -f /etc/os-release ]] && . /etc/os-release
[[ "${ID:-}" == ubuntu ]] || log "AVISO: sistema no Ubuntu (${ID:-?}); el script esta pensado para Ubuntu 22.04/24.04"
export DEBIAN_FRONTEND=noninteractive

# ---------------------------------------------------------------- 1. paquetes base
log "1/6 paquetes base"
aptq update -qq
aptq install -y -qq ca-certificates curl git openssl gnupg iptables cron
if [[ "$(systemd-detect-virt 2>/dev/null || true)" == microsoft ]]; then
  # Integration services de Hyper-V (apagado ordenado desde el host, latido, kvp). Mejor esfuerzo: depende del kernel instalado.
  aptq install -y -qq linux-cloud-tools-virtual linux-tools-virtual || log "AVISO: no se pudieron instalar las herramientas de Hyper-V (el apagado ordenado desde el host puede no funcionar)"
fi

# ---------------------------------------------------------------- 2. Docker Engine
if (( ! SKIP_DOCKER )) && { dpkg -s docker.io >/dev/null 2>&1 || { command -v snap >/dev/null 2>&1 && snap list docker >/dev/null 2>&1; }; }; then
  die "hay un Docker que no es docker-ce (docker.io o snap): instalar docker-ce lo reemplazaria y podria cortar contenedores en marcha. Retirarlo a mano o usar --skip-docker"
fi
if (( SKIP_DOCKER )) || { [[ -z "${BURY_FORCE_DOCKER_INSTALL:-}" ]] && command -v docker >/dev/null 2>&1 && docker compose version >/dev/null 2>&1 && dpkg -s docker-ce >/dev/null 2>&1; }; then
  log "2/6 Docker ya presente o --skip-docker: no se instala"
else
  log "2/6 Docker Engine + Compose v2 (repositorio oficial de Docker)"
  run install -m 0755 -d /etc/apt/keyrings
  run curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
  run chmod a+r /etc/apt/keyrings/docker.asc
  if (( ! DRY )); then
    echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu ${VERSION_CODENAME:?sin VERSION_CODENAME} stable" >/etc/apt/sources.list.d/docker.list
  fi
  aptq update -qq
  aptq install -y -qq docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
fi
# Apagado: dockerd espera hasta 60 s a que SQL Server cierre limpio (el valor por defecto son 15 s). No se pisa un daemon.json existente.
if [[ ! -f /etc/docker/daemon.json ]]; then
  run install -m 0755 -d /etc/docker
  if (( DRY )); then log '[dry-run] escribir /etc/docker/daemon.json con "shutdown-timeout": 60 y recargar dockerd'
  else
    printf '{\n  "shutdown-timeout": 60\n}\n' >/etc/docker/daemon.json
    systemctl reload docker 2>/dev/null || systemctl restart docker || true   # shutdown-timeout es recargable (SIGHUP)
  fi
else
  log "AVISO: /etc/docker/daemon.json ya existe: no se modifica (verificar que incluya \"shutdown-timeout\": 60)"
fi
if getent group docker >/dev/null 2>&1 || (( DRY )); then run usermod -aG docker "$RUN_USER"; else log "AVISO: no existe el grupo docker (--skip-docker?): $RUN_USER no podra usar Docker sin sudo"; fi
run systemctl enable --now docker containerd cron

# ---------------------------------------------------------------- 3. directorio de backups
if [[ -n "$BACKUP_DIR" ]]; then
  if [[ -d "$BACKUP_DIR" ]]; then log "3/6 $BACKUP_DIR ya existe: no se modifica"
  else
    log "3/6 creando $BACKUP_DIR (700, dueño $RUN_USER)"
    run install -d -m 0700 -o "$RUN_USER" -g "$RUN_USER" "$BACKUP_DIR"
  fi
else log "3/6 sin --backup-dir: nada que crear"; fi

# ---------------------------------------------------------------- 4. firewall de puertos publicados (DOCKER-USER)
log "4/6 firewall de HTTPS ($HTTPS_PORT) para: $FW_ALLOW"
run install -m 0755 "$REPO_DIR/scripts/install/bury-firewall.sh" /usr/local/sbin/bury-firewall
run install -d -m 0755 /etc/bury
if (( ! DRY )); then
  printf 'ALLOW_FROM="%s"\nHTTPS_PORT=%s\n' "$FW_ALLOW" "$HTTPS_PORT" >/etc/bury/firewall.conf
  cat >/etc/systemd/system/bury-firewall.service <<'UNIT'
[Unit]
Description=TheBury: firewall de los puertos publicados por Docker (DOCKER-USER)
Requires=docker.service
After=docker.service network-online.target
PartOf=docker.service

[Service]
Type=oneshot
RemainAfterExit=yes
ExecStart=/usr/local/sbin/bury-firewall apply
ExecStop=/usr/local/sbin/bury-firewall remove

[Install]
WantedBy=multi-user.target
UNIT
  # Drop-in: la regla se aplica como parte del arranque de dockerd (ExecStartPost, el '-' tolera fallos para no impedir que Docker arranque),
  # lo que acorta al minimo la ventana en que los contenedores `unless-stopped` publican 443 antes del filtro y cubre la activacion por socket.
  install -d -m 0755 /etc/systemd/system/docker.service.d
  printf '[Service]\nExecStartPost=-/usr/local/sbin/bury-firewall apply\n' >/etc/systemd/system/docker.service.d/bury-firewall.conf
  systemctl daemon-reload
  systemctl enable --now bury-firewall.service
fi

# ---------------------------------------------------------------- 5. ufw (SSH)
if (( NO_UFW )); then
  log "5/6 ufw no configurado (--no-ufw)"
elif [[ -z "$ADMIN_FROM" ]]; then
  log "5/6 ufw no configurado: sin --admin-from se evita bloquear el acceso SSH"
else
  log "5/6 ufw: entrante denegado salvo SSH desde $UFW_ADMIN (el 443 de Caddy lo filtra bury-firewall.sh; la LAN no administra)"
  aptq install -y -qq ufw
  run ufw default deny incoming
  run ufw default allow outgoing
  for s in $UFW_ADMIN; do
    if [[ "$s" == *@* ]]; then run ufw allow in on "${s#*@}" from "${s%%@*}" to any port 22 proto tcp   # Tailscale: solo si entra por tailscale0 (sin suplantacion desde la LAN)
    else run ufw allow from "$s" to any port 22 proto tcp; fi
  done
  run ufw --force enable
fi

# ---------------------------------------------------------------- 6. Tailscale (opcional, sin autenticar)
if (( WANT_TS )); then
  log "6/6 Tailscale: paquete + servicio (autenticacion manual: sudo tailscale up)"
  (( DRY )) || [[ -n "${VERSION_CODENAME:-}" ]] || die "no se pudo determinar VERSION_CODENAME (/etc/os-release) para el repositorio de Tailscale"
  if (( ! DRY )) && ! command -v tailscale >/dev/null 2>&1; then
    curl -fsSL "https://pkgs.tailscale.com/stable/ubuntu/${VERSION_CODENAME}.noarmor.gpg" -o /usr/share/keyrings/tailscale-archive-keyring.gpg
    curl -fsSL "https://pkgs.tailscale.com/stable/ubuntu/${VERSION_CODENAME}.tailscale-keyring.list" -o /etc/apt/sources.list.d/tailscale.list
    apt-get -o DPkg::Lock::Timeout=300 update -qq && apt-get -o DPkg::Lock::Timeout=300 install -y -qq tailscale
  fi
  run systemctl enable --now tailscaled
else log "6/6 sin --tailscale"; fi

log "OK bootstrap completo. Siguiente: scripts/install/install.sh (como $RUN_USER, sesion nueva para tomar el grupo docker)."
