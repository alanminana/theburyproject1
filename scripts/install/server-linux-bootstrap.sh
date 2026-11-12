#!/usr/bin/env bash
# Prepara un Ubuntu Server 22.04/24.04 (la VM de Hyper-V de Windows Server, o cualquier Linux) como anfitrion de TheBuryProject:
# Docker Engine + Compose v2, firewall de los puertos publicados, cierre ordenado de SQL Server y, opcionalmente, Tailscale.
# NO instala el ERP (eso es scripts/install/install.sh) ni toca datos. Es idempotente.
#
#   sudo scripts/install/server-linux-bootstrap.sh --user bury --allow-from 192.168.1.0/24[,100.64.0.0/10]
#        [--admin-from 192.168.1.0/24] [--https-port 443] [--backup-dir /srv/bury-backups] [--tailscale]
#        [--skip-docker] [--no-ufw] [--dry-run]
#
#   --user USER        usuario que administra el ERP (queda en el grupo docker; es el dueño de --backup-dir)   [obligatorio]
#   --allow-from LIST  redes con acceso HTTPS al ERP (CSV de IPv4/CIDR). Se aplica en DOCKER-USER (ver bury-firewall.sh)  [obligatorio]
#   --admin-from LIST  redes con acceso SSH (22). Si se omite NO se activa ufw (evita dejar la VM sin acceso por error)
#   --backup-dir DIR   crea el directorio de backups (700, dueño --user) si no existe; nunca cambia uno existente
#   --tailscale        instala el paquete y habilita tailscaled (NO lo autentica: 'sudo tailscale up' lo hace el propietario)
#   --skip-docker      no instala Docker (ya esta instalado)       --no-ufw   no configura ufw       --dry-run   solo informa
set -Eeuo pipefail

RUN_USER="" ALLOW_FROM="" ADMIN_FROM="" HTTPS_PORT=443 BACKUP_DIR="" WANT_TS=0 SKIP_DOCKER=0 NO_UFW=0 DRY=0
REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

log() { printf '%s %s\n' "[$(date -u +%H:%M:%S)]" "$*"; }
die() { printf 'ERROR: %s\n' "$*" >&2; exit 1; }
run() { if (( DRY )); then log "[dry-run] $*"; else "$@"; fi; }

while (( $# )); do
  case "$1" in
    --user) RUN_USER="${2:-}"; shift 2 ;;
    --allow-from) ALLOW_FROM="${2:-}"; shift 2 ;;
    --admin-from) ADMIN_FROM="${2:-}"; shift 2 ;;
    --https-port) HTTPS_PORT="${2:-}"; shift 2 ;;
    --backup-dir) BACKUP_DIR="${2:-}"; shift 2 ;;
    --tailscale) WANT_TS=1; shift ;;
    --skip-docker) SKIP_DOCKER=1; shift ;;
    --no-ufw) NO_UFW=1; shift ;;
    --dry-run) DRY=1; shift ;;
    -h|--help) sed -n '2,19p' "$0"; exit 0 ;;
    *) die "argumento desconocido: $1" ;;
  esac
done

valid_list() { local s; for s in ${1//,/ }; do [[ "$s" =~ ^([0-9]{1,3}\.){3}[0-9]{1,3}(/([0-9]|[12][0-9]|3[0-2]))?$ ]] || return 1; done; }
[[ "$RUN_USER" =~ ^[a-z_][a-z0-9_-]*$ ]] || die "--user es obligatorio (usuario Linux valido)"
id "$RUN_USER" >/dev/null 2>&1 || die "el usuario '$RUN_USER' no existe"
[[ -n "$ALLOW_FROM" ]] && valid_list "$ALLOW_FROM" || die "--allow-from es obligatorio (CSV de IPv4/CIDR)"
[[ -z "$ADMIN_FROM" ]] || valid_list "$ADMIN_FROM" || die "--admin-from invalido (CSV de IPv4/CIDR)"
[[ "$HTTPS_PORT" =~ ^[0-9]+$ ]] || die "--https-port invalido"
[[ -z "$BACKUP_DIR" || "$BACKUP_DIR" =~ ^/[A-Za-z0-9_./-]+$ ]] || die "--backup-dir debe ser una ruta absoluta sin caracteres especiales"
(( DRY )) || [[ $EUID -eq 0 ]] || die "ejecutar con sudo/root (o --dry-run)"
[[ -f /etc/os-release ]] && . /etc/os-release
[[ "${ID:-}" == ubuntu ]] || log "AVISO: sistema no Ubuntu (${ID:-?}); el script esta pensado para Ubuntu 22.04/24.04"
export DEBIAN_FRONTEND=noninteractive

# ---------------------------------------------------------------- 1. paquetes base
log "1/6 paquetes base"
run apt-get update -qq
run apt-get install -y -qq ca-certificates curl git openssl gnupg iptables cron
if [[ "$(systemd-detect-virt 2>/dev/null || true)" == microsoft ]]; then
  # Integration services de Hyper-V (apagado ordenado desde el host, latido, kvp). Mejor esfuerzo: depende del kernel instalado.
  run apt-get install -y -qq linux-cloud-tools-virtual linux-tools-virtual || log "AVISO: no se pudieron instalar las herramientas de Hyper-V (el apagado ordenado desde el host puede no funcionar)"
fi

# ---------------------------------------------------------------- 2. Docker Engine
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
  run apt-get update -qq
  run apt-get install -y -qq docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
fi
# Apagado: dockerd espera hasta 60 s a que SQL Server cierre limpio (el valor por defecto son 15 s). No se pisa un daemon.json existente.
if [[ ! -f /etc/docker/daemon.json ]]; then
  run install -m 0755 -d /etc/docker
  if (( DRY )); then log '[dry-run] escribir /etc/docker/daemon.json con "shutdown-timeout": 60'; else printf '{\n  "shutdown-timeout": 60\n}\n' >/etc/docker/daemon.json; fi
else
  log "AVISO: /etc/docker/daemon.json ya existe: no se modifica (verificar que incluya \"shutdown-timeout\": 60)"
fi
run usermod -aG docker "$RUN_USER" 2>/dev/null || true
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
log "4/6 firewall de HTTPS ($HTTPS_PORT) para: ${ALLOW_FROM//,/ }"
run install -m 0755 "$REPO_DIR/scripts/install/bury-firewall.sh" /usr/local/sbin/bury-firewall
run install -d -m 0755 /etc/bury
if (( ! DRY )); then
  printf 'ALLOW_FROM="%s"\nHTTPS_PORT=%s\n' "${ALLOW_FROM//,/ }" "$HTTPS_PORT" >/etc/bury/firewall.conf
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
  systemctl daemon-reload
  systemctl enable --now bury-firewall.service
fi

# ---------------------------------------------------------------- 5. ufw (SSH)
if (( NO_UFW )); then
  log "5/6 ufw no configurado (--no-ufw)"
elif [[ -z "$ADMIN_FROM" ]]; then
  log "5/6 ufw no configurado: sin --admin-from se evita bloquear el acceso SSH"
else
  log "5/6 ufw: entrante denegado salvo SSH desde ${ADMIN_FROM//,/ }"
  run apt-get install -y -qq ufw
  run ufw default deny incoming
  run ufw default allow outgoing
  for s in ${ADMIN_FROM//,/ }; do run ufw allow from "$s" to any port 22 proto tcp; done
  run ufw --force enable
fi

# ---------------------------------------------------------------- 6. Tailscale (opcional, sin autenticar)
if (( WANT_TS )); then
  log "6/6 Tailscale: paquete + servicio (autenticacion manual: sudo tailscale up)"
  if (( ! DRY )) && ! command -v tailscale >/dev/null 2>&1; then
    curl -fsSL "https://pkgs.tailscale.com/stable/ubuntu/${VERSION_CODENAME}.noarmor.gpg" -o /usr/share/keyrings/tailscale-archive-keyring.gpg
    curl -fsSL "https://pkgs.tailscale.com/stable/ubuntu/${VERSION_CODENAME}.tailscale-keyring.list" -o /etc/apt/sources.list.d/tailscale.list
    apt-get update -qq && apt-get install -y -qq tailscale
  fi
  run systemctl enable --now tailscaled
else log "6/6 sin --tailscale"; fi

log "OK bootstrap completo. Siguiente: scripts/install/install.sh (como $RUN_USER, sesion nueva para tomar el grupo docker)."
