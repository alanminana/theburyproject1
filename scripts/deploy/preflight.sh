#!/usr/bin/env bash
# Preflight de deploy/rollback: valida TODO lo posible antes de tocar la aplicacion. Falla rapido (exit 1) sin efectos secundarios.
#
#   scripts/deploy/preflight.sh --image <tag> [--env-file .env] [--project theburyproject] [--require-db-up]
#
# No imprime secretos. No modifica nada: es seguro de ejecutar en cualquier momento.
# shellcheck source=lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

IMAGE=""
REQUIRE_DB_UP=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    --image) IMAGE="$2"; shift 2 ;;
    --env-file) DP_ENV_FILE="$2"; shift 2 ;;
    --project) DP_PROJECT="$2"; shift 2 ;;
    --require-db-up) REQUIRE_DB_UP=1; shift ;;
    -h|--help) sed -n '2,6p' "$0"; exit 0 ;;
    *) die $EX_USAGE "argumento desconocido: $1" ;;
  esac
done

FAIL=0
check() { # check "descripcion" comando...
  local desc=$1; shift
  if "$@" >/dev/null 2>&1; then
    log INFO "OK   $desc"
  else
    log ERROR "FAIL $desc"
    FAIL=1
  fi
}

log INFO "=== preflight (proyecto=$DP_PROJECT imagen=${IMAGE:-<sin especificar>}) ==="

# --- imagen ---
if [[ -z "$IMAGE" ]]; then
  log ERROR "FAIL --image es obligatorio"; FAIL=1
else
  case "$IMAGE" in
    *:latest|*:LATEST) log ERROR "FAIL --image no puede usar el tag 'latest' (sin identidad de release)"; FAIL=1 ;;
  esac
  case "$IMAGE" in
    *:local|*:LOCAL) log ERROR "FAIL --image no puede usar el tag 'local' (reservado para build de desarrollo)"; FAIL=1 ;;
  esac
  if [[ "${IMAGE^^}" == *CHANGE_ME* ]]; then
    log ERROR "FAIL --image contiene un marcador de plantilla (CHANGE_ME)"; FAIL=1
  fi
  if image_exists_locally "$IMAGE"; then
    log INFO "OK   imagen disponible localmente: $IMAGE"
  else
    log ERROR "FAIL imagen no encontrada localmente: $IMAGE (build once / docker image load antes de deploy)"; FAIL=1
  fi
fi

# --- entorno ---
if [[ -f "$DP_ENV_FILE" ]]; then
  log INFO "OK   env-file existe: $DP_ENV_FILE"
  if grep -qi 'CHANGE_ME' "$DP_ENV_FILE"; then
    log ERROR "FAIL $DP_ENV_FILE conserva valores CHANGE_ME sin reemplazar"; FAIL=1
  else
    log INFO "OK   sin marcadores CHANGE_ME en $DP_ENV_FILE"
  fi
  for key in MSSQL_SA_PASSWORD MSSQL_DATABASE ERP_DB_USER ERP_DB_PASSWORD ERP_MIGRATION_USER ERP_MIGRATION_PASSWORD ADMIN_EMAIL ERP_DOMAIN; do
    if grep -qE "^[[:space:]]*${key}=.+" "$DP_ENV_FILE"; then
      log INFO "OK   variable presente: $key"
    else
      log ERROR "FAIL falta variable obligatoria en env-file: $key"; FAIL=1
    fi
  done
else
  log ERROR "FAIL env-file no existe: $DP_ENV_FILE"; FAIL=1
fi

# --- compose ---
check "docker disponible" command -v docker
check "docker compose v2 disponible" bash -c 'docker compose version'
if [[ -f "$DP_ENV_FILE" ]]; then
  if dc config --quiet 2>/tmp/.preflight-compose-err; then
    log INFO "OK   docker compose config valido (proyecto=$DP_PROJECT)"
  else
    log ERROR "FAIL docker compose config invalido: $(tr '\n' ' ' </tmp/.preflight-compose-err | cut -c1-300)"; FAIL=1
  fi
  rm -f /tmp/.preflight-compose-err
fi

# --- DB disponible (si se pide, p.ej. redeploy sobre stack ya levantado) ---
if (( REQUIRE_DB_UP )); then
  if dc ps --status running --services 2>/dev/null | grep -qx db; then
    log INFO "OK   servicio db corriendo"
  else
    log ERROR "FAIL servicio db no esta corriendo (requerido con --require-db-up)"; FAIL=1
  fi
fi

# --- espacio libre ---
FREE_KB=$(df -Pk "$REPO_DIR" 2>/dev/null | awk 'NR==2{print $4}')
if is_uint "$FREE_KB" && (( FREE_KB > 5 * 1024 * 1024 )); then
  log INFO "OK   espacio libre suficiente (~$(( FREE_KB / 1024 / 1024 )) GiB en $REPO_DIR)"
else
  log ERROR "FAIL espacio libre insuficiente o no determinable en $REPO_DIR (se requieren >5 GiB libres)"; FAIL=1
fi

# --- deploy concurrente ---
ensure_state_dir
if [[ -d "$DP_LOCK_DIR" ]]; then
  pid=$(cat "$DP_LOCK_DIR/pid" 2>/dev/null || true)
  if [[ -n "$pid" ]] && kill -0 "$pid" 2>/dev/null; then
    log ERROR "FAIL hay un deploy/rollback en curso sobre '$DP_PROJECT' (pid $pid)"; FAIL=1
  else
    log INFO "OK   lock presente pero huerfano (se recuperara automaticamente)"
  fi
else
  log INFO "OK   sin deploy/rollback concurrente"
fi

if (( FAIL )); then
  log ERROR "=== preflight FALLO ==="
  exit $EX_USAGE
fi
log INFO "=== preflight OK ==="
exit $EX_OK
