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
LAST_ERROR=""
die() { local code=$1; shift; LAST_ERROR="$*"; log ERROR "$*"; exit "$code"; }

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
  # Sin componentes '..': la ruta que ve Compose (bind mount) y la que valida este script deben ser la misma.
  [[ "$d" != *"/../"* && "$d" != */.. && "$d" != ../* && "$d" != *"/./"* ]] \
    || die $EX_USAGE "BACKUP_DIR='$d' contiene '.' o '..': usar una ruta absoluta normalizada"
  BACKUP_DIR="$d"

  RETENTION_DAYS=$(cfg BACKUP_RETENTION_DAYS 14)
  RETENTION_MIN_KEEP=$(cfg BACKUP_RETENTION_MIN_KEEP 3)
  REMOTE_RETENTION_DAYS=$(cfg BACKUP_REMOTE_RETENTION_DAYS 30)
  is_uint "$RETENTION_DAYS" && (( RETENTION_DAYS >= 1 )) || die $EX_USAGE "BACKUP_RETENTION_DAYS debe ser entero >= 1"
  is_uint "$RETENTION_MIN_KEEP" && (( RETENTION_MIN_KEEP >= 1 )) || die $EX_USAGE "BACKUP_RETENTION_MIN_KEEP debe ser entero >= 1"
  is_uint "$REMOTE_RETENTION_DAYS" || die $EX_USAGE "BACKUP_REMOTE_RETENTION_DAYS debe ser entero (0 = no borrar en el remoto)"
  REMOTE_MIN_KEEP=$(cfg BACKUP_REMOTE_MIN_KEEP 3)
  is_uint "$REMOTE_MIN_KEEP" && (( ${#REMOTE_MIN_KEEP} <= 3 && 10#$REMOTE_MIN_KEEP >= 1 )) || die $EX_USAGE "BACKUP_REMOTE_MIN_KEEP debe ser un entero >= 1 (copias completas que la retencion remota conserva SIEMPRE por tipo)"
  REMOTE_MIN_KEEP=$((10#$REMOTE_MIN_KEEP))

  BACKUP_REMOTE=$(cfg BACKUP_REMOTE "")
  BACKUP_REQUIRE_OFFSITE=$(cfg BACKUP_REQUIRE_OFFSITE false)
  BACKUP_OFFSITE_MARKER=$(cfg BACKUP_OFFSITE_MARKER "")   # archivo centinela en el remoto (ver docs/backup-restore.md §6.1)
  [[ -z "$BACKUP_OFFSITE_MARKER" || "$BACKUP_OFFSITE_MARKER" =~ ^[A-Za-z0-9._-]+$ ]] || die $EX_USAGE "BACKUP_OFFSITE_MARKER invalido (solo letras, digitos, . _ -)"
  BACKUP_INCLUDE_CADDY=$(cfg BACKUP_INCLUDE_CADDY true)
  BACKUP_STOP_APP=$(cfg BACKUP_STOP_APP true)
  BACKUP_SQL_COMPRESSION=$(cfg BACKUP_SQL_COMPRESSION auto)   # auto | on | off
  export BACKUP_INCLUDE_CADDY

  LOG_FILE="$BACKUP_DIR/logs/backup.log"
}

# `9>&-`: los hijos no heredan el descriptor del flock de acquire_lock (si el shell muere, el lock no queda retenido por docker).
dc() { docker compose --progress quiet "$@" 9>&-; }

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

# Redaccion de credenciales comunes en texto libre (stderr de rclone, mensajes de error) ANTES de loguearlo o persistirlo.
# Conserva el contexto del error; NO pretende detectar cualquier secreto posible. Lee stdin o los argumentos.
#   password/passwd/pass<N>, secret*, token, api/access/account/private key, credential, authorization, *_key(_id) (p.ej.
#   RCLONE_CONFIG_SECURE_PASSWORD2, aws_secret_access_key, AccountKey), Bearer/Basic, user:pass@host, AKIA..., JWT, SAS sig=.
sanitize_text() {
  local input; if (( $# )); then input="$*"; else input=$(cat); fi
  local key='[A-Za-z0-9_.-]*(pass(word|wd)?|secret|token|api[_-]?key|access[_-]?key|account[_-]?key|private[_-]?key|credential|authorization|sharedaccesssignature|[_-]key(_id)?)[A-Za-z0-9_-]*'
  local val='("[^"]*"|'"'"'[^'"'"']*'"'"'|[^[:space:]&,;"'"'"']+)'
  sed -E \
    -e 's#(://[^/:@[:space:]]+):[^@/[:space:]]+@#\1:[REDACTED]@#g' \
    -e 's#\b(bearer|basic)([[:space:]]+)[A-Za-z0-9._~+/=-]{6,}#\1\2[REDACTED]#Ig' \
    -e "s#(${key})([\"']?[[:space:]]*[=:][[:space:]]*)${val}#\1\5[REDACTED]#Ig" \
    -e 's#(^|[?&[:space:]])(sig)=[^&[:space:]]+#\1\2=[REDACTED]#Ig' \
    -e 's#\bAKIA[0-9A-Z]{16}\b#[REDACTED-AWS-KEY-ID]#g' \
    -e 's#\beyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]*#[REDACTED-JWT]#g' \
    <<<"$input"
}

# Causa legible de un fallo de rclone: descarta las lineas NOTICE (config no encontrada, host key, ...) que antes ocupaban los
# primeros 300 caracteres y ocultaban el error real (connection refused, permission denied, ...). Sanitiza y recorta.
rclone_errtext() { # rclone_errtext <salida de rclone>
  { grep -v ' NOTICE: ' <<<"$1" || true; } | tr '\n' ' ' | sanitize_text | cut -c1-300
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
  RCLONE_ERR=$(rclone_errtext "$out")
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

# Comprueba, SIN cambiar nada, que el usuario actual puede usar el BACKUP_DIR existente. Si no, falla con el comando de remediacion
# exacto (que NO se ejecuta: un directorio con backups reales nunca se "arregla" solo). Lo usan prepare-dir.sh, backup.sh y restore-files.sh.
backup_dir_usable() {
  local me owner; me=$(id -u)
  [[ -d "$BACKUP_DIR" ]] || die $EX_PREREQ "BACKUP_DIR=$BACKUP_DIR existe pero NO es un directorio (o no existe: ejecutar scripts/backup/prepare-dir.sh)"
  owner=$(stat -c %u "$BACKUP_DIR" 2>/dev/null || echo "?")
  if (( me != 0 )) && [[ "$owner" == "0" ]]; then
    die $EX_PREREQ "BACKUP_DIR=$BACKUP_DIR pertenece a root y este usuario ($(id -un)) no es root (tipico: lo creo Docker al hacer 'up' sin prepararlo). NO se cambia automaticamente. Si NO contiene backups que necesites conservar con ese dueño: sudo chown $(id -un): '$BACKUP_DIR' (sin -R) y repetir; si contiene datos, decidir antes que usuario debe ejecutar los backups (p. ej. correr los scripts con sudo/cron de root)"
  fi
  if (( me != 0 )) && [[ "$owner" != "$me" ]]; then
    die $EX_PREREQ "BACKUP_DIR=$BACKUP_DIR pertenece al uid $owner, no a $(id -un) (uid $me). NO se cambia automaticamente: ejecutar los scripts con ese usuario o corregir el dueño a mano (sin -R)"
  fi
  [[ -w "$BACKUP_DIR" ]] || die $EX_PREREQ "BACKUP_DIR=$BACKUP_DIR no es escribible por $(id -un). No se cambian permisos automaticamente"
}

# mkdir -p que aplica el modo SOLO a los directorios que crea (nunca cambia permisos de un directorio ya existente con datos).
mkdir_new_mode() { # mkdir_new_mode <modo> <dir>...
  local mode=$1 d; shift
  for d in "$@"; do
    if [[ ! -e "$d" ]]; then mkdir -p "$d" && chmod "$mode" "$d" 2>/dev/null || true; fi
  done
}

prepare_dirs() {
  # BACKUP_DIR ya debe existir (prepare-dir.sh / install.sh): si lo creara Docker al hacer `up db` seria root:root.
  [[ -d "$BACKUP_DIR" ]] || mkdir_new_mode 700 "$BACKUP_DIR"
  mkdir_new_mode 700 "$BACKUP_DIR/files" "$BACKUP_DIR/logs"
  [[ -d "$BACKUP_DIR/sql" ]] || mkdir -p "$BACKUP_DIR/sql"   # su modo/dueño los fija el chown de abajo (usuario mssql)
  [[ -f "$BACKUP_DIR/.bury-backup-root" ]] || echo "Directorio administrado por scripts/backup. NO borrar este archivo." >"$BACKUP_DIR/.bury-backup-root"
  # SQL Server corre como uid 10001 (mssql): backups/sql debe ser suyo. Se ajusta desde un contenedor root (idempotente).
  tools sh -c 'chown 10001:0 /backups/sql && chmod 750 /backups/sql' \
    || die $EX_PREREQ "no se pudo preparar $BACKUP_DIR/sql (permisos para el usuario mssql)"
}

# acquire_lock: rc 0 = tomado | 1 = otra ejecucion en curso | 2 = error (no se pudo abrir/crear el lock: permisos).
# Exclusion entre backups y restores destructivos.
# Con `flock` (util-linux: Linux/WSL, el camino de produccion) el kernel libera el lock aunque el proceso muera con SIGKILL y no hay
# "lock huerfano" ni carreras de recuperacion. `dc()` cierra el descriptor 9 en los hijos para que docker no lo herede.
# Sin `flock` (p. ej. Git Bash) las operaciones DESTRUCTIVAS no tienen exclusion fiable (la recuperacion de un lock huerfano por directorio
# admite dos recuperadores sucesivos): se rechaza con rc 2. Solo con BURY_ALLOW_DIR_LOCK=1 (tests/desarrollo, nunca produccion) se usa el
# lock por directorio, con las mitigaciones razonables (espera ~3 s a que el dueño escriba su pid; recuperacion por rename atomico).
acquire_lock() {
  if command -v flock >/dev/null 2>&1; then
    exec 9>>"$BACKUP_DIR/.lock.flock" || { log ERROR "no se pudo abrir $BACKUP_DIR/.lock.flock (permisos de BACKUP_DIR): no es 'otra ejecucion en curso'"; return 2; }
    if ! flock -n 9; then log WARN "otra ejecucion en curso (lock $BACKUP_DIR/.lock.flock)"; exec 9>&-; return 1; fi
    BK_LOCK_FD=1; return 0
  fi
  if [[ "${BURY_ALLOW_DIR_LOCK:-0}" != 1 ]]; then
    log ERROR "flock (util-linux) no esta disponible: sin el no hay exclusion fiable entre backup y restore. Instalar util-linux (en Linux/WSL ya viene)"; return 2
  fi
  local lock="$BACKUP_DIR/.lock" pid="" i
  if ! mkdir "$lock" 2>/dev/null; then
    for i in 1 2 3 4 5 6 7 8 9 10; do
      pid=$(cat "$lock/pid" 2>/dev/null || true)
      [[ -n "$pid" ]] && break
      sleep 0.3
    done
    if [[ -n "$pid" ]] && kill -0 "$pid" 2>/dev/null; then
      log WARN "otra ejecucion en curso (pid $pid)"; return 1
    fi
    local stale="$BACKUP_DIR/.lock.stale.$$"
    # mv atomico: si dos procesos recuperan a la vez, solo uno lo consigue.
    mv "$lock" "$stale" 2>/dev/null || { log WARN "otro proceso recupero el lock primero"; return 1; }
    rm -rf "$stale"
    log WARN "lock huerfano (pid '${pid:-?}' inexistente): se recupera"
    mkdir "$lock" 2>/dev/null || return 1
  fi
  echo $$ >"$lock/pid"
  BK_LOCK="$lock"
}
release_lock() {
  if [[ "${BK_LOCK_FD:-0}" == 1 ]]; then exec 9>&-; BK_LOCK_FD=0; fi
  [[ -n "${BK_LOCK:-}" ]] && rm -rf "$BK_LOCK" || true
  BK_LOCK=""
}

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

# Estado estructurado de la prueba de restore semanal: monitor-status/restore-test.json (mismo directorio y escritura atomica que
# monitor_status). A diferencia de los demas, conserva `last_success` entre corridas fallidas para que el monitor distinga
# "ultimo intento" de "ultimo exito verificado". El mensaje se sanitiza y se reduce a ASCII sin comillas/backslash (JSON seguro).
# restore_test_status <exit> <verified true|false> [mensaje]
restore_test_status() {
  local code=$1 verified=$2 msg=${3-} dir="$BACKUP_DIR/monitor-status" now prev last_ok=null tmp
  now=$(date +%s)
  msg=$(sanitize_text "$msg" | tr -c '[:print:]' ' ' | sed 's/["\]//g' | cut -c1-200)
  if [[ -f "$dir/restore-test.json" ]]; then
    prev=$(grep -oE '"last_success":[0-9]+' "$dir/restore-test.json" | head -n1 | cut -d: -f2 || true)
    [[ -n "$prev" ]] && last_ok=$prev
  fi
  if [[ "$code" == 0 && "$verified" == true ]]; then last_ok=$now; fi
  (
    umask 077
    mkdir -p "$dir" || exit 1
    tmp=$(mktemp "$dir/.restore-test.XXXXXX") || exit 1
    printf '{"time":%s,"exit":%s,"verified":%s,"last_success":%s,"message":"%s"}\n' "$now" "$code" "$verified" "$last_ok" "$msg" >"$tmp" \
      && mv -f "$tmp" "$dir/restore-test.json"
  ) || log WARN "no se pudo persistir estado de monitoreo: restore-test"
  return 0
}

# ---------------------------------------------------------------- retencion remota: planificador puro (sin I/O, testeable)
BK_TS='[0-9]{4}-[0-9]{2}-[0-9]{2}_[0-9]{6}Z'

# bk_ts_epoch <AAAA-MM-DD_HHMMSSZ> -> epoch UTC. rc 1 si no es una fecha real (rechaza 02-30, 24:00, 60 s, ...: exige ida y vuelta exacta).
bk_ts_epoch() {
  local ts=$1 e back
  [[ "$ts" =~ ^([0-9]{4})-([0-9]{2})-([0-9]{2})_([0-9]{2})([0-9]{2})([0-9]{2})Z$ ]] || return 1
  e=$(date -u -d "${BASH_REMATCH[1]}-${BASH_REMATCH[2]}-${BASH_REMATCH[3]} ${BASH_REMATCH[4]}:${BASH_REMATCH[5]}:${BASH_REMATCH[6]}" +%s 2>/dev/null) || return 1
  back=$(date -u -d "@$e" +%Y-%m-%d_%H%M%SZ 2>/dev/null) || return 1
  [[ "$back" == "$ts" ]] || return 1
  printf '%s' "$e"
}

# remote_prune_plan <full|files|log> <ahora_epoch> <dias> <min_keep> <db> [piso_ts]      < listado del remoto, "bytes|nombre" por linea
# Decide SIN tocar nada que se puede borrar del remoto. Solo reconoce basenames canonicos EXACTOS (ignora subdirectorios, sufijos
# extra, otras bases y archivos ajenos: nunca los propone). La antiguedad sale del TIMESTAMP DEL NOMBRE (UTC), no del mtime.
# Salida (una linea por decision):
#   DELETE <nombre>   el ejecutor borra el principal y despues su <nombre>.sha256
# Para `full`, el 6to argumento es una lista (separada por espacios) de timestamps de fulls PROTEGIDOS: los que referencia el manifiesto de un
# tar que se conserva (par recuperable, ver docs §10.2). Para `log`, el 6to argumento es el piso (FLOOR) calculado para los fulls.
#   FLOOR <ts>        (full) timestamp del full completo mas antiguo que SOBREVIVE: los .trn anteriores a el no sirven a ningun full retenido
#   GUARD <motivo>    no se borra NADA de este tipo
#   WARN <texto>      incompleto/huerfano/ignorado (informativo)
# "Completo" = principal y .sha256 presentes y no vacios (sha >= 64 bytes). NO prueba que el contenido se restaure: la verificacion
# fuerte es la de la corrida que lo subio (VERIFYONLY + rclone check) y el restore-test.
# Guardas: se conservan SIEMPRE los min_keep completos mas recientes; si hay menos que min_keep no se borra nada; cualquier nombre con
# fecha imposible o futura (> ahora + 10 min: reloj adelantado o nombres manipulados) anula el prune del tipo. rc 0 plan valido | 2 argumentos.
remote_prune_plan() {
  local kind=${1:-} now=${2:-} days=${3:-} keep=${4:-} db=${5:-} floor=${6-} prot=${6-}
  case "$kind" in full|files|log) ;; *) echo "GUARD tipo invalido"; return 2 ;; esac
  [[ "$now" =~ ^[0-9]{9,11}$ && "$days" =~ ^[0-9]{1,5}$ && "$keep" =~ ^[0-9]{1,3}$ ]] || { echo "GUARD argumentos numericos invalidos"; return 2; }
  (( 10#$days >= 1 && 10#$keep >= 1 )) || { echo "GUARD dias y min_keep deben ser >= 1"; return 2; }
  [[ "$db" =~ ^[A-Za-z_][A-Za-z0-9_]{0,63}$ ]] || { echo "GUARD nombre de base invalido"; return 2; }
  days=$((10#$days)); keep=$((10#$keep))
  local rx_main
  case "$kind" in
    full)  rx_main="^${db}_full_(${BK_TS})\.bak\$" ;;
    files) rx_main="^files_(${BK_TS})\.tar\.gz\$" ;;
    log)   rx_main="^${db}_log_(${BK_TS})\.trn\$" ;;
  esac
  local rx_sha="${rx_main%\$}\.sha256\$"
  local -A size=()
  local line sz name
  while IFS= read -r line; do
    line=${line%$'\r'}; [[ "$line" == *"|"* ]] || continue
    sz=${line%%|*}; name=${line#*|}
    [[ "$sz" =~ ^[0-9]+$ ]] || continue
    size["$name"]=$sz
  done

  local cutoff=$(( now - days * 86400 )) limit_future=$(( now + 600 ))
  local -a mains=()
  local ts ep
  for name in "${!size[@]}"; do
    if [[ "$name" =~ $rx_main || "$name" =~ $rx_sha ]]; then
      ts=${BASH_REMATCH[1]}
      if ! ep=$(bk_ts_epoch "$ts"); then echo "GUARD fecha imposible en el nombre '$name'"; return 0; fi
      if (( ep > limit_future )); then echo "GUARD nombre con fecha futura ('$name'): reloj adelantado o nombres anomalos"; return 0; fi
    fi
    if [[ "$name" =~ $rx_main ]]; then mains+=("${BASH_REMATCH[1]}|$name"); fi
  done
  local -a sorted=()
  if (( ${#mains[@]} )); then mapfile -t sorted < <(printf '%s\n' "${mains[@]}" | LC_ALL=C sort); fi

  if [[ "$kind" == log ]]; then
    local floor_ep lim
    if [[ -z "$floor" ]] || ! floor_ep=$(bk_ts_epoch "$floor"); then echo "GUARD sin full retenido de referencia: no se tocan los logs"; return 0; fi
    lim=$(( floor_ep < cutoff ? floor_ep : cutoff ))
    local m
    for m in "${sorted[@]}"; do
      ts=${m%%|*}; name=${m#*|}; ep=$(bk_ts_epoch "$ts")
      if (( ep < lim )); then echo "DELETE $name"; fi
    done
    return 0
  fi

  local -a complete=()
  local m shan
  for m in "${sorted[@]}"; do
    ts=${m%%|*}; name=${m#*|}; shan="$name.sha256"
    if (( ${size[$name]} > 0 )) && [[ -n "${size[$shan]+x}" ]] && (( ${size[$shan]} >= 64 )); then
      complete+=("$m")
    else
      echo "WARN copia incompleta o vacia (falta/0 bytes de principal o .sha256): $name; no cuenta como respaldo y no se borra"
    fi
  done
  local total=${#complete[@]}
  if (( total < keep )); then echo "GUARD solo $total copia(s) completa(s) < minimo $keep: no se borra nada"; return 0; fi

  local i oldest_kept=""
  for (( i = 0; i < total; i++ )); do
    m=${complete[$i]}; ts=${m%%|*}; name=${m#*|}; ep=$(bk_ts_epoch "$ts")
    if (( i < total - keep )) && (( ep < cutoff )) && [[ " $prot " != *" $ts "* ]]; then
      echo "DELETE $name"
    elif [[ -z "$oldest_kept" ]]; then
      oldest_kept=$ts
    fi
  done
  [[ -n "$oldest_kept" ]] && echo "FLOOR $oldest_kept"
  return 0
}
