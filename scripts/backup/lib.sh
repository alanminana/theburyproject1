#!/usr/bin/env bash
# Libreria comun de scripts/backup/*.sh (se hace `source`, no se ejecuta). Requiere bash, docker compose v2.
#
# Configuracion: variable de entorno > valor en .env (o en COMPOSE_ENV_FILES) > default. Solo se leen claves NO secretas;
# la password de sa NUNCA sale del contenedor `db` (sqlcmd la toma de su propio entorno).
# Proyecto/archivos Compose: se respetan COMPOSE_PROJECT_NAME / COMPOSE_FILE / COMPOSE_ENV_FILES estandar de Compose.

set -Eeuo pipefail

BK_SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_DIR="$(cd "$BK_SCRIPT_DIR/../.." && pwd)"
cd "$REPO_DIR"

# Codigos de salida (contrato para alertas futuras).
EX_OK=0
EX_USAGE=1        # argumentos/configuracion invalidos
EX_SQL=2          # fallo BACKUP DATABASE / BACKUP LOG
EX_VERIFY=3       # fallo RESTORE VERIFYONLY / checksum / archivo ausente o vacio
EX_BUSY=4         # otra ejecucion en curso
EX_FILES=5        # fallo backup de archivos
EX_OFFSITE=6      # fallo copia externa (o requerida y no configurada)
EX_RETENTION=7    # fallo de retencion
EX_PREREQ=8       # docker/compose/SQL no disponible
EX_RESTORE=9      # fallo/rechazo de restore
EX_APP=10         # `app` no volvio a estar healthy tras detenerla para el backup

LOG_FILE=""

log() { # log NIVEL mensaje...
  local level=$1; shift
  local line; line="$(date -u +%Y-%m-%dT%H:%M:%SZ) [$level] $*"
  if [[ "$level" == ERROR ]]; then echo "$line" >&2; else echo "$line"; fi
  if [[ -n "$LOG_FILE" && -d "$(dirname "$LOG_FILE")" ]]; then echo "$line" >>"$LOG_FILE" 2>/dev/null || true; fi
}
die() { local code=$1; shift; log ERROR "$*"; exit "$code"; }

_envfiles() {
  if [[ -n "${COMPOSE_ENV_FILES:-}" ]]; then tr ',' '\n' <<<"$COMPOSE_ENV_FILES"; else echo "$REPO_DIR/.env"; fi
}

cfg() { # cfg CLAVE [DEFAULT]
  local key=$1 def=${2-} f line val
  if [[ -n "${!key:-}" ]]; then printf '%s' "${!key}"; return 0; fi
  while IFS= read -r f; do
    [[ -f "$f" ]] || continue
    line=$(grep -E "^[[:space:]]*${key}=" "$f" | tail -n1 || true)
    [[ -n "$line" ]] || continue
    val=${line#*=}; val=${val%$'\r'}
    if [[ "$val" =~ ^\"(.*)\"$ || "$val" =~ ^\'(.*)\'$ ]]; then val=${BASH_REMATCH[1]}; fi
    if [[ -n "$val" ]]; then printf '%s' "$val"; return 0; fi
  done < <(_envfiles)
  printf '%s' "$def"
}

is_uint() { [[ "${1:-}" =~ ^[0-9]+$ ]]; }

load_config() {
  DB_NAME=$(cfg MSSQL_DATABASE TheBuryProjectDb)
  [[ "$DB_NAME" =~ ^[A-Za-z_][A-Za-z0-9_]{0,63}$ ]] || die $EX_USAGE "MSSQL_DATABASE invalido (solo letras, digitos y _)"

  local d; d=$(cfg BACKUP_DIR ./backups)
  [[ -n "$d" ]] || die $EX_USAGE "BACKUP_DIR vacio"
  if [[ ! "$d" =~ ^(/|[A-Za-z]:[/\\]) ]]; then d="$REPO_DIR/${d#./}"; fi   # relativo -> respecto del repo (igual que Compose)
  d=${d%/}
  [[ -n "$d" && "$d" != "/" && "$d" != "$REPO_DIR" && "$d" != "${HOME:-/nonexistent}" ]] \
    || die $EX_USAGE "BACKUP_DIR='$d' no es un directorio de backups aceptable"
  BACKUP_DIR="$d"

  RETENTION_DAYS=$(cfg BACKUP_RETENTION_DAYS 14)
  RETENTION_MIN_KEEP=$(cfg BACKUP_RETENTION_MIN_KEEP 3)
  REMOTE_RETENTION_DAYS=$(cfg BACKUP_REMOTE_RETENTION_DAYS 30)
  is_uint "$RETENTION_DAYS" && (( RETENTION_DAYS >= 1 )) || die $EX_USAGE "BACKUP_RETENTION_DAYS debe ser entero >= 1"
  is_uint "$RETENTION_MIN_KEEP" && (( RETENTION_MIN_KEEP >= 1 )) || die $EX_USAGE "BACKUP_RETENTION_MIN_KEEP debe ser entero >= 1"
  is_uint "$REMOTE_RETENTION_DAYS" || die $EX_USAGE "BACKUP_REMOTE_RETENTION_DAYS debe ser entero (0 = no borrar en el remoto)"

  BACKUP_REMOTE=$(cfg BACKUP_REMOTE "")
  BACKUP_REQUIRE_OFFSITE=$(cfg BACKUP_REQUIRE_OFFSITE false)
  BACKUP_INCLUDE_CADDY=$(cfg BACKUP_INCLUDE_CADDY true)
  BACKUP_STOP_APP=$(cfg BACKUP_STOP_APP true)
  BACKUP_SQL_COMPRESSION=$(cfg BACKUP_SQL_COMPRESSION auto)   # auto | on | off
  export BACKUP_INCLUDE_CADDY

  LOG_FILE="$BACKUP_DIR/logs/backup.log"
}

dc() { docker compose --progress quiet "$@"; }

tools() { dc run --rm -T --no-deps --quiet-pull backup-files "$@"; }   # contenedor alpine de herramientas

# Ejecuta T-SQL como sa DENTRO del contenedor db. La password la lee sqlcmd del entorno del contenedor (no viaja por argumentos).
# El texto del statement pasa como $0 de bash -c (nunca interpolado en el comando).
sql_exec() { # sql_exec "T-SQL"
  dc exec -T db bash -c 'SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" exec /opt/mssql-tools18/bin/sqlcmd -C -b -S localhost -U sa -d master -W -w 400 -Q "$0"' "$1"
}
sql_scalar() { # sql_scalar "SELECT ..." -> primer valor, sin encabezados
  dc exec -T db bash -c 'SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" exec /opt/mssql-tools18/bin/sqlcmd -C -b -S localhost -U sa -d master -h -1 -W -Q "SET NOCOUNT ON; $0"' "$1" | tr -d '\r' | head -n1
}

sql_rows() { # sql_rows "T-SQL" -> filas separadas por '|', sin encabezados
  dc exec -T db bash -c 'SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" exec /opt/mssql-tools18/bin/sqlcmd -C -b -S localhost -U sa -d master -h -1 -W -s "|" -w 1000 -Q "SET NOCOUNT ON; $0"' "$1" | tr -d '\r'
}

# rclone check con el servicio dado. Si el remoto no tiene hash comun con el local (p.ej. SFTP sin shell), `check` solo compara
# TAMAÑO: en ese caso se repite con --download (compara byte a byte) para no declarar "verificado" algo que solo coincide en tamaño.
# rclone_verify <servicio> <origen> <destino> [args rclone...]   -> 0 ok, !=0 diferencias/error. Deja el detalle en $RCLONE_ERR.
RCLONE_ERR=""
rclone_verify() {
  local svc=$1 src=$2 dst=$3; shift 3
  local out rc=0
  out=$(dc run --rm -T --no-deps --quiet-pull "$svc" check "$src" "$dst" --one-way "$@" 2>&1) || rc=$?
  if (( rc == 0 )) && grep -qE "could not be checked|No common hash" <<<"$out"; then
    log INFO "  el remoto no expone hash comun: verificacion byte a byte (rclone check --download)"
    rc=0; out=$(dc run --rm -T --no-deps --quiet-pull "$svc" check "$src" "$dst" --one-way --download "$@" 2>&1) || rc=$?
  fi
  RCLONE_ERR=$(tr '\n' ' ' <<<"$out" | grep -v '^$' | cut -c1-300)
  return $rc
}

service_running() { # sin pipe directo a grep -q (SIGPIPE + pipefail)
  local svcs; svcs=$(dc ps --status running --services 2>/dev/null || true)
  grep -qx "$1" <<<"$svcs"
}

check_prereqs() {
  command -v docker >/dev/null 2>&1 || die $EX_PREREQ "docker no esta disponible"
  dc version >/dev/null 2>&1 || die $EX_PREREQ "docker compose v2 no esta disponible"
  service_running db || die $EX_PREREQ "el servicio db no esta corriendo"
  sql_exec "SELECT 1" >/dev/null 2>&1 || die $EX_PREREQ "SQL Server no responde"
}

prepare_dirs() {
  mkdir -p "$BACKUP_DIR/sql" "$BACKUP_DIR/files" "$BACKUP_DIR/logs"
  chmod 700 "$BACKUP_DIR" "$BACKUP_DIR/files" "$BACKUP_DIR/logs" 2>/dev/null || true
  [[ -f "$BACKUP_DIR/.bury-backup-root" ]] || echo "Directorio administrado por scripts/backup. NO borrar este archivo." >"$BACKUP_DIR/.bury-backup-root"
  # SQL Server corre como uid 10001 (mssql): backups/sql debe ser suyo. Se ajusta desde un contenedor root (idempotente).
  tools sh -c 'chown 10001:0 /backups/sql && chmod 750 /backups/sql' \
    || die $EX_PREREQ "no se pudo preparar $BACKUP_DIR/sql (permisos para el usuario mssql)"
}

# Bloqueo por directorio (atomico y portable). Un lock huerfano (pid muerto) se recupera solo.
acquire_lock() {
  local lock="$BACKUP_DIR/.lock" pid
  if ! mkdir "$lock" 2>/dev/null; then
    pid=$(cat "$lock/pid" 2>/dev/null || true)
    if [[ -n "$pid" ]] && kill -0 "$pid" 2>/dev/null; then
      log WARN "otra ejecucion en curso (pid $pid)"; return 1
    fi
    log WARN "lock huerfano (pid '${pid:-?}' inexistente): se recupera"
    rm -rf "$lock"; mkdir "$lock" || return 1
  fi
  echo $$ >"$lock/pid"
  BK_LOCK="$lock"
}
release_lock() { [[ -n "${BK_LOCK:-}" ]] && rm -rf "$BK_LOCK" || true; }

rotate_log() { # rota backup.log a 5 MB, conserva 3
  [[ -f "$LOG_FILE" ]] || return 0
  local size; size=$(wc -c <"$LOG_FILE" | tr -d ' ')
  if (( size > 5242880 )); then
    mv -f "$LOG_FILE.2" "$LOG_FILE.3" 2>/dev/null || true
    mv -f "$LOG_FILE.1" "$LOG_FILE.2" 2>/dev/null || true
    mv -f "$LOG_FILE" "$LOG_FILE.1"
  fi
}

utc_stamp() { date -u +%Y-%m-%d_%H%M%SZ; }

# Observabilidad exclusivamente: resultado atomico, sin credenciales ni nombres de archivos.
# Solo llamar verified=true DESPUES de las verificaciones existentes. Nunca cambia el exit del backup.
monitor_status() { # tipo exit verified
  local kind=$1 code=$2 verified=$3 dir="$BACKUP_DIR/monitor-status" tmp
  (
    umask 077
    mkdir -p "$dir" || exit 1
    tmp=$(mktemp "$dir/.${kind}.XXXXXX") || exit 1
    printf '{"time":%s,"exit":%s,"verified":%s}\n' "$(date +%s)" "$code" "$verified" >"$tmp" \
      && mv -f "$tmp" "$dir/$kind.json"
  ) || log WARN "no se pudo persistir estado de monitoreo: $kind"
  return 0
}
