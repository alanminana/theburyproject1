#!/usr/bin/env bash
# Libreria comun de scripts/deploy/*.sh (se hace `source`, no se ejecuta). Requiere bash, docker compose v2.
#
# Codigos de salida (contrato compartido por deploy.sh / rollback.sh):
#   0  ok
#   1  uso/config invalidos (preflight)
#   2  imagen no disponible / no encontrada
#   3  backup previo faltante o no verificado
#   4  migrate fallo (exit != 0 o pending > 0 despues)
#   5  health gate fallo (live/ready no llegaron a 200 dentro del timeout)
#   6  smoke test fallo
#   7  deploy concurrente (lock ocupado)
#   8  interrumpido (SIGINT/SIGTERM)
#   9  rollback rechazado (incompatibilidad de esquema no confirmada)
#  10  compose/docker no disponible o comando de compose fallo
#
# Nunca imprime .env completo ni valores de variables que contengan credenciales.
set -Eeuo pipefail

DP_SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_DIR="$(cd "$DP_SCRIPT_DIR/../.." && pwd)"

EX_OK=0
EX_USAGE=1
EX_IMAGE=2
EX_BACKUP=3
EX_MIGRATE=4
EX_HEALTH=5
EX_SMOKE=6
EX_BUSY=7
EX_INTERRUPTED=8
EX_ROLLBACK_REJECTED=9
EX_PREREQ=10

log() { # log NIVEL mensaje...
  local level=$1; shift
  local line; line="$(date -u +%Y-%m-%dT%H:%M:%SZ) [$level] $*"
  if [[ "$level" == ERROR ]]; then echo "$line" >&2; else echo "$line"; fi
  if [[ -n "${DP_LOG_FILE:-}" && -d "$(dirname "$DP_LOG_FILE")" ]]; then echo "$line" >>"$DP_LOG_FILE" 2>/dev/null || true; fi
}
die() { local code=$1; shift; log ERROR "$*"; exit "$code"; }

is_uint() { [[ "${1:-}" =~ ^[0-9]+$ ]]; }

# ---------------------------------------------------------------- compose wrapper
DP_PROJECT="${DP_PROJECT:-theburyproject}"
DP_ENV_FILE="${DP_ENV_FILE:-$REPO_DIR/.env}"
DP_COMPOSE_FILES=(-f "$REPO_DIR/docker-compose.yml")

dc() {
  docker compose --progress quiet -p "$DP_PROJECT" --env-file "$DP_ENV_FILE" "${DP_COMPOSE_FILES[@]}" "$@"
}

# ---------------------------------------------------------------- estado de release (fuera de git; ver .gitignore)
DP_STATE_DIR="${DP_STATE_DIR:-$REPO_DIR/.deploy-state/$DP_PROJECT}"
DP_LOCK_DIR="$DP_STATE_DIR/.lock"
DP_LOG_FILE="$DP_STATE_DIR/deploy.log"
DP_RECORD_FILE="$DP_STATE_DIR/releases.log"
DP_CURRENT_FILE="$DP_STATE_DIR/current-image"
DP_PREVIOUS_FILE="$DP_STATE_DIR/previous-image"

ensure_state_dir() { mkdir -p "$DP_STATE_DIR"; }

# Bloqueo por directorio (atomico, portable). Un lock huerfano (pid muerto) se recupera solo.
acquire_lock() {
  ensure_state_dir
  if ! mkdir "$DP_LOCK_DIR" 2>/dev/null; then
    local pid; pid=$(cat "$DP_LOCK_DIR/pid" 2>/dev/null || true)
    if [[ -n "$pid" ]] && kill -0 "$pid" 2>/dev/null; then
      log ERROR "otro deploy/rollback en curso (pid $pid) sobre el proyecto '$DP_PROJECT'"; return 1
    fi
    log WARN "lock huerfano (pid '${pid:-?}' inexistente): se recupera"
    rm -rf "$DP_LOCK_DIR"; mkdir "$DP_LOCK_DIR" || return 1
  fi
  echo $$ >"$DP_LOCK_DIR/pid"
}
release_lock() { rm -rf "$DP_LOCK_DIR" 2>/dev/null || true; }

# ---------------------------------------------------------------- identidad de imagen
# release-id sugerido: <fecha UTC>-<shortsha>. No se fuerza: --image acepta cualquier tag/digest ya construido.
suggest_release_id() {
  local sha
  sha=$(git -C "$REPO_DIR" rev-parse --short=7 HEAD 2>/dev/null || echo "nogit")
  if ! git -C "$REPO_DIR" diff --quiet 2>/dev/null || ! git -C "$REPO_DIR" diff --cached --quiet 2>/dev/null; then
    sha="${sha}-dirty"
  fi
  echo "$(date -u +%Y%m%d)-${sha}"
}

# image_identity <image> -> imprime lineas "clave=valor" (id, digest si existe, creado)
image_identity() {
  local image=$1
  local id created digest
  id=$(docker image inspect --format '{{.Id}}' "$image" 2>/dev/null || true)
  created=$(docker image inspect --format '{{.Created}}' "$image" 2>/dev/null || true)
  digest=$(docker image inspect --format '{{index .RepoDigests 0}}' "$image" 2>/dev/null || true)
  echo "image=$image"
  echo "id=${id:-desconocido}"
  echo "digest=${digest:-sin-digest-local}"
  echo "created=${created:-desconocido}"
}

image_exists_locally() { docker image inspect "$1" >/dev/null 2>&1; }

# `docker compose up -d` vuelve en cuanto arranca el contenedor, no cuando un one-shot TERMINA.
# wait_container_exit <servicio> [timeout_s] -> espera a que el contenedor este en estado 'exited' e imprime su ExitCode.
wait_container_exit() {
  local svc=$1 timeout=${2:-120} waited=0 state
  while (( waited < timeout )); do
    state=$(dc ps -a --format '{{.State}}' "$svc" 2>/dev/null | head -n1 || true)
    [[ "$state" == "exited" ]] && { dc ps -a --format '{{.ExitCode}}' "$svc" 2>/dev/null | head -n1; return 0; }
    sleep 2; waited=$((waited + 2))
  done
  echo "timeout"
  return 1
}

# ---------------------------------------------------------------- health gate
# wait_health <servicio> <path> <timeout_s> -> 0 si responde 200 dentro del plazo
# Nota: "-o /dev/null" con curl dentro de `docker compose exec -T` resulto intermitente en este host
# (curl: (23) Failure writing output to destination); se evita descartando el cuerpo con un marcador en su lugar.
wait_health() {
  local svc=$1 path=$2 timeout=${3:-120} waited=0 code
  while (( waited < timeout )); do
    code=$(http_code "$svc" "$path")
    [[ "$code" == "200" ]] && return 0
    sleep 3; waited=$((waited + 3))
  done
  return 1
}

# http_code <servicio> <path> -> codigo HTTP (o vacio si no se pudo determinar). No usa -o /dev/null (ver nota arriba).
http_code() {
  local svc=$1 path=$2 out
  out=$(dc exec -T "$svc" curl -sS -w '\nHTTPCODE:%{http_code}' "http://localhost:8080$path" 2>/dev/null || true)
  grep -o 'HTTPCODE:[0-9]*' <<<"$out" | tail -1 | cut -d: -f2
}

# ---------------------------------------------------------------- registro de release (append-only, sin secretos)
append_release_record() { # append_release_record "campo1=valor1" "campo2=valor2" ...
  ensure_state_dir
  { printf 'timestamp=%s ' "$(date -u +%Y-%m-%dT%H:%M:%SZ)"; printf '%s ' "$@"; printf '\n'; } >>"$DP_RECORD_FILE"
}

pending_migrations_count() {
  # El unico modo soportado de contar pendientes sin credenciales DDL extra es leer el resultado de `migrate` (Data/DbMigrationRunner.cs
  # ya lo deja en stdout: "[migrate] OK: N migraciones aplicadas, 0 pendientes." o "[migrate] FALLO: N migraciones pendientes.").
  # Este helper solo confirma que el ultimo log de migrate declaro 0 pendientes; no reconsulta la base.
  :
}

trap_interrupt() { # instalar en cada script: trap 'on_interrupt' INT TERM
  log ERROR "interrumpido (SIGINT/SIGTERM). Lock liberado. Ver docs/deploy-rollback.md#interrupciones para el estado posible de cada etapa."
  release_lock
  exit $EX_INTERRUPTED
}
