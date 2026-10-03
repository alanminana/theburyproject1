#!/usr/bin/env bash
# Instalador de TheBuryProject en un servidor (Linux, o WSL2 dentro de Windows). Deja el ERP corriendo con Docker.
#
#   scripts/install/install.sh --admin-email correo@dominio [--domain tbp] [--license community|commercial]
#                              [--automapper-key KEY] [--backup-dir /srv/bury-backups] [--admin-user admin]
#                              [--image repo:tag] [--env-file RUTA] [--project theburyproject]
#                              [--schedule] [--env-only] [--no-deploy] [--yes]
#
# Pasos (cada uno es idempotente; se puede volver a ejecutar):
#   1. verifica prerequisitos (bash, docker, docker compose v2, openssl, git)
#   2. genera .env a partir de .env.example con secretos aleatorios (0600) SOLO si no existe; nunca lo pisa
#   3. construye la imagen con tag inmutable <fecha>-<commit>[-dirty] (o usa --image)
#   4. scripts/deploy/deploy.sh (preflight -> backup -> db-init -> migrate -> app -> health -> smoke)
#   5. levanta Caddy (deploy.sh no lo gestiona) y extrae su CA publica a ./caddy-root-ca.crt para instalarla en los clientes
#   6. con --schedule (solo Linux, requiere sudo) instala cron de backups; en Windows lo hace install-windows.ps1
#
#   --env-only   solo genera .env (sin docker; lo usa test-install.sh)      --no-deploy  deja todo listo pero no despliega
#   --yes        no pide confirmacion
# Nunca imprime secretos salvo la password inicial del administrador, UNA vez, justo cuando este run genera el .env.
# Documentacion: docs/instalacion-servidor.md
# shellcheck source=../deploy/lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/../deploy/lib.sh"

DOMAIN="tbp"
ADMIN_EMAIL_ARG=""
ADMIN_USER="admin"
LICENSE_MODE="community"
AUTOMAPPER_KEY=""
BACKUP_DIR_ARG="/srv/bury-backups"
IMAGE=""
SCHEDULE=0
ENV_ONLY=0
NO_DEPLOY=0
ASSUME_YES=0

usage() { sed -n '2,19p' "$0"; }
while [[ $# -gt 0 ]]; do
  case "$1" in
    --domain) DOMAIN="${2:-}"; shift 2 ;;
    --admin-email) ADMIN_EMAIL_ARG="${2:-}"; shift 2 ;;
    --admin-user) ADMIN_USER="${2:-}"; shift 2 ;;
    --license) LICENSE_MODE="${2:-}"; shift 2 ;;
    --automapper-key) AUTOMAPPER_KEY="${2:-}"; shift 2 ;;
    --backup-dir) BACKUP_DIR_ARG="${2:-}"; shift 2 ;;
    --image) IMAGE="${2:-}"; shift 2 ;;
    --env-file) DP_ENV_FILE="${2:-}"; shift 2 ;;
    --project) DP_PROJECT="${2:-}"; shift 2 ;;
    --schedule) SCHEDULE=1; shift ;;
    --env-only) ENV_ONLY=1; shift ;;
    --no-deploy) NO_DEPLOY=1; shift ;;
    --yes|-y) ASSUME_YES=1; shift ;;
    -h|--help) usage; exit 0 ;;
    *) die $EX_USAGE "argumento desconocido: $1 (ver --help)" ;;
  esac
done
recompute_state_paths

# ---------------------------------------------------------------- validaciones de argumentos
[[ "$DOMAIN" =~ ^[A-Za-z0-9]([A-Za-z0-9.-]*[A-Za-z0-9])?$ ]] || die $EX_USAGE "--domain invalido: '$DOMAIN' (hostname, p. ej. tbp)"
[[ "$ADMIN_USER" =~ ^[A-Za-z0-9_.-]{3,40}$ ]] || die $EX_USAGE "--admin-user invalido (3-40 caracteres: letras, digitos, _ . -)"
case "$LICENSE_MODE" in
  community) ;;
  commercial) [[ -n "$AUTOMAPPER_KEY" ]] || die $EX_USAGE "--license commercial requiere --automapper-key" ;;
  *) die $EX_USAGE "--license debe ser community|commercial" ;;
esac
[[ "$BACKUP_DIR_ARG" == /* ]] || die $EX_USAGE "--backup-dir debe ser una ruta absoluta"
[[ "$BACKUP_DIR_ARG" =~ ^/[A-Za-z0-9_./-]+$ ]] || die $EX_USAGE "--backup-dir tiene caracteres no soportados (usar letras, digitos, _ . / -)"

ENV_GENERATED=0
ADMIN_PASSWORD_GENERATED=""

# ---------------------------------------------------------------- generacion de .env
rand_alnum() { # rand_alnum N -> N caracteres [A-Za-z0-9] (el `|| true` evita SIGPIPE bajo pipefail)
  local n=$1 out=""
  while (( ${#out} < n )); do out+=$(openssl rand -base64 48 | tr -dc 'A-Za-z0-9' || true); done
  printf '%s' "${out:0:n}"
}
rand_password() { # rand_password N -> garantiza mayuscula, minuscula y digito (reglas de SQL Server e Identity)
  local n=$1 p
  while :; do
    p=$(rand_alnum "$n")
    [[ "$p" =~ [A-Z] && "$p" =~ [a-z] && "$p" =~ [0-9] ]] && { printf '%s' "$p"; return; }
  done
}

set_env_value() { # set_env_value ARCHIVO CLAVE VALOR  (reemplaza la linea CLAVE=...; valor por ENVIRON, sin interpretar nada)
  local file=$1 key=$2 value=$3 tmp
  grep -qE "^${key}=" "$file" || die $EX_USAGE "la plantilla .env.example no define ${key}"
  tmp=$(mktemp)
  KEY="$key" VAL="$value" awk 'BEGIN{k=ENVIRON["KEY"]; v=ENVIRON["VAL"]} index($0, k "=")==1 {print k "=" v; next} {print}' "$file" >"$tmp"
  cat "$tmp" >"$file"; rm -f "$tmp"
}

generate_env() {
  local target=$1 template="$REPO_DIR/.env.example"
  [[ -f "$template" ]] || die $EX_USAGE "no existe $template"
  [[ -n "$ADMIN_EMAIL_ARG" ]] || die $EX_USAGE "--admin-email es obligatorio para generar el .env"
  [[ "$ADMIN_EMAIL_ARG" =~ ^[^[:space:]@]+@[^[:space:]@]+\.[^[:space:]@]+$ ]] || die $EX_USAGE "--admin-email no parece un correo valido"
  command -v openssl >/dev/null 2>&1 || die $EX_PREREQ "openssl no esta disponible"

  local old_umask; old_umask=$(umask); umask 077
  local tmp; tmp=$(mktemp "${target}.XXXXXX")
  cp "$template" "$tmp"
  ADMIN_PASSWORD_GENERATED=$(rand_password 20)
  set_env_value "$tmp" MSSQL_SA_PASSWORD "$(rand_password 28)"
  set_env_value "$tmp" ERP_DB_USER "erp_app"
  set_env_value "$tmp" ERP_DB_PASSWORD "$(rand_password 28)"
  set_env_value "$tmp" ERP_MIGRATION_USER "erp_migrator"
  set_env_value "$tmp" ERP_MIGRATION_PASSWORD "$(rand_password 28)"
  set_env_value "$tmp" MSSQL_PID "Express"
  set_env_value "$tmp" ADMIN_EMAIL "$ADMIN_EMAIL_ARG"
  set_env_value "$tmp" ADMIN_USERNAME "$ADMIN_USER"
  set_env_value "$tmp" ADMIN_PASSWORD "$ADMIN_PASSWORD_GENERATED"
  set_env_value "$tmp" ERP_DOMAIN "$DOMAIN"
  set_env_value "$tmp" AUTOMAPPER_LICENSE_MODE "$([[ $LICENSE_MODE == community ]] && echo community || echo '')"
  set_env_value "$tmp" AUTOMAPPER_LICENSE_KEY "$AUTOMAPPER_KEY"
  set_env_value "$tmp" BACKUP_DIR "$BACKUP_DIR_ARG"
  # Solo copias locales (decision de despliegue): sin destino externo no se exige copia offsite. Para activarla despues:
  # BACKUP_REMOTE=secure:erp y BACKUP_REQUIRE_OFFSITE=true (docs/offsite-monitoring-activacion.md).
  set_env_value "$tmp" BACKUP_REQUIRE_OFFSITE "false"
  # preflight.sh rechaza cualquier .env que contenga la palabra CHANGE_ME, tambien en comentarios: la plantilla la usa
  # en sus textos de ayuda, asi que esas lineas de comentario no se copian al .env generado.
  grep -viE '^[[:space:]]*#.*CHANGE_ME' "$tmp" >"${tmp}.clean" && cat "${tmp}.clean" >"$tmp"; rm -f "${tmp}.clean"
  mv -f "$tmp" "$target"; chmod 600 "$target" 2>/dev/null || true
  umask "$old_umask"
  ENV_GENERATED=1
  log INFO "OK   .env generado en $target (permisos 600, secretos aleatorios)"
  # Se muestra aqui, una sola vez y a stdout (no al log), para que no se pierda si un paso posterior falla.
  if (( ! ENV_ONLY )); then
    echo
    echo "=============================================================================="
    echo " Administrador inicial   usuario: $ADMIN_USER   password: $ADMIN_PASSWORD_GENERATED"
    echo " Se muestra UNA sola vez (tambien queda en $target hasta que la cambies)."
    echo "=============================================================================="
    echo
  fi
}

ensure_env() {
  if [[ -f "$DP_ENV_FILE" ]]; then
    log INFO "OK   .env ya existe ($DP_ENV_FILE): se conserva sin cambios"
    if grep -qi 'CHANGE_ME' "$DP_ENV_FILE"; then die $EX_USAGE "$DP_ENV_FILE conserva valores CHANGE_ME sin reemplazar"; fi
  else
    generate_env "$DP_ENV_FILE"
  fi
}

# ---------------------------------------------------------------- prerequisitos
check_prereqs() {
  local missing=0 c
  for c in docker openssl git; do
    if ! command -v "$c" >/dev/null 2>&1; then log ERROR "FAIL falta el comando: $c"; missing=1; fi
  done
  (( missing == 0 )) || die $EX_PREREQ "instalar los prerequisitos faltantes (ver docs/instalacion-servidor.md)"
  docker info >/dev/null 2>&1 || die $EX_PREREQ "docker no responde (daemon detenido o sin permisos: grupo docker / integracion WSL de Docker Desktop)"
  docker compose version >/dev/null 2>&1 || die $EX_PREREQ "docker compose v2 no esta disponible"
  log INFO "OK   prerequisitos: docker $(docker version --format '{{.Server.Version}}' 2>/dev/null || echo '?'), compose v2, openssl, git"
}

# deploy.sh exige que scripts/backup/backup.sh sea ejecutable. Clones antiguos (o hechos desde Windows) pueden traer los .sh en modo 644.
ensure_scripts_executable() {
  local f
  for f in "$REPO_DIR"/scripts/*/*.sh "$REPO_DIR"/scripts/backup/container/*.sh "$REPO_DIR"/docker/db-init/*.sh; do
    [[ -f "$f" && ! -x "$f" ]] && chmod +x "$f" 2>/dev/null
  done
  return 0
}

prepare_backup_dir() {
  local dir; dir=$(grep -E '^BACKUP_DIR=' "$DP_ENV_FILE" | tail -n1 | cut -d= -f2-)
  [[ -n "$dir" && "$dir" == /* ]] || { log WARN "BACKUP_DIR no es una ruta absoluta ($dir): se omite su preparacion"; return 0; }
  if mkdir -p "$dir" 2>/dev/null; then
    chmod 700 "$dir" 2>/dev/null || true
    log INFO "OK   BACKUP_DIR listo: $dir"
  else
    die $EX_PREREQ "no se pudo crear BACKUP_DIR=$dir. Crearlo con: sudo mkdir -p $dir && sudo chown $(id -un): $dir && chmod 700 $dir (o usar --backup-dir con una ruta propia) y repetir"
  fi
}

# ---------------------------------------------------------------- imagen
resolve_image() {
  if [[ -n "$IMAGE" ]]; then return 0; fi
  local sha dirty="" stamp
  sha=$(git -C "$REPO_DIR" rev-parse --short HEAD 2>/dev/null || echo nogit)
  if [[ -n "$(git -C "$REPO_DIR" -c core.fileMode=false status --porcelain 2>/dev/null || true)" ]]; then dirty="-dirty"; fi
  stamp=$(date -u +%Y%m%d)
  IMAGE="theburyproject/erp:${stamp}-${sha}${dirty}"
}
build_image() {
  if image_exists_locally "$IMAGE"; then
    log INFO "OK   la imagen $IMAGE ya existe localmente: no se reconstruye"
    return 0
  fi
  log INFO "construyendo imagen $IMAGE (la primera vez tarda varios minutos)"
  docker build -t "$IMAGE" "$REPO_DIR" || die $EX_IMAGE "docker build fallo"
}

# ---------------------------------------------------------------- Caddy
# deploy.sh no gestiona Caddy (en un host con Caddy ya corriendo no lo toca): en una instalacion nueva hay que levantarlo aqui.
# `up -d` es idempotente: si ya corre con la misma configuracion no lo recrea.
start_caddy() {
  dc up -d caddy >/dev/null 2>&1 || die $EX_PREREQ "no se pudo levantar caddy (docker compose -p $DP_PROJECT logs caddy); si el puerto 443 esta ocupado definir ERP_HTTPS_PORT en .env"
  log INFO "OK   caddy en ejecucion (unico servicio publicado: HTTPS)"
}

# ---------------------------------------------------------------- CA de Caddy
export_caddy_ca() {
  local out="$REPO_DIR/caddy-root-ca.crt" i
  for i in $(seq 1 30); do
    if dc cp caddy:/data/caddy/pki/authorities/local/root.crt "$out" >/dev/null 2>&1 && [[ -s "$out" ]]; then
      log INFO "OK   CA publica de Caddy en $out (solo certificado publico; distribuir a los clientes)"
      return 0
    fi
    sleep 2
  done
  log WARN "no se pudo extraer la CA de Caddy todavia; repetir luego: docker compose -p $DP_PROJECT cp caddy:/data/caddy/pki/authorities/local/root.crt ./caddy-root-ca.crt"
}

# ---------------------------------------------------------------- main
log INFO "=== instalacion: dominio=$DOMAIN licencia=$LICENSE_MODE proyecto=$DP_PROJECT ==="

if (( ENV_ONLY )); then
  if [[ -f "$DP_ENV_FILE" ]]; then die $EX_USAGE "--env-only no pisa un .env existente ($DP_ENV_FILE)"; fi
  generate_env "$DP_ENV_FILE"
  exit 0
fi

check_prereqs
ensure_scripts_executable
ensure_env
prepare_backup_dir

if (( NO_DEPLOY )); then
  log INFO "--no-deploy: .env y BACKUP_DIR listos; no se construye ni despliega"
  exit 0
fi

if (( ! ASSUME_YES )) && [[ -t 0 ]]; then
  read -r -p "Construir y desplegar el ERP ahora (proyecto '$DP_PROJECT')? [s/N] " ans
  [[ "$ans" =~ ^[sSyY]$ ]] || { log INFO "cancelado por el usuario"; exit 0; }
fi

resolve_image
build_image
bash "$DP_SCRIPT_DIR/deploy.sh" --image "$IMAGE" --env-file "$DP_ENV_FILE" --project "$DP_PROJECT" || die $? "deploy fallo (ver mensajes anteriores y docs/deploy-rollback.md)"
start_caddy
export_caddy_ca

if (( SCHEDULE )); then
  if [[ "$(uname -s)" == Linux* ]] && ! grep -qi microsoft /proc/version 2>/dev/null; then
    sudo bash "$REPO_DIR/scripts/backup/install-schedule.sh" --install --user "$(id -un)" || log WARN "no se pudo instalar el cron de backups"
  else
    log WARN "--schedule solo aplica a Linux; en Windows/WSL usar scripts/install/install-windows.ps1"
  fi
fi

log INFO "=== instalacion completa ==="
echo
echo "ERP:      https://$DOMAIN   (los clientes deben resolver '$DOMAIN' y confiar en caddy-root-ca.crt)"
echo "Imagen:   $IMAGE"
if (( ENV_GENERATED )); then
  echo
  echo "Ahora: 1) guardar el archivo $DP_ENV_FILE completo en el gestor de contrasenas (sin el no se puede recuperar el sistema)."
  echo "       2) entrar, cambiar la password, y luego quitar ADMIN_PASSWORD del .env y ejecutar: docker compose -p $DP_PROJECT rm -f migrate"
fi
echo "Siguiente: docs/instalacion-servidor.md (clientes, DNS/VPN, backups programados)."
