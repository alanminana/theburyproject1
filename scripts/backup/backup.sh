#!/usr/bin/env bash
# Backup del ERP: SQL Server (full / log) + archivos persistentes + copia externa + retencion local.
#
#   scripts/backup/backup.sh [all|full|log|files]      (default: all)
#
#   all    full SQL + archivos (keys, uploads, App_Data, caddy-data), con `app` detenida unos segundos para que ambos
#          backups sean un par CONSISTENTE; luego copia externa y retencion.       -> diario
#   full   solo full SQL (online, sin detener nada).
#   log    solo backup del log de transacciones (online). Requiere recovery FULL y un full previo.   -> cada 15 min
#   files  solo archivos (detiene `app` unos segundos).
#
# Codigos de salida: 0 ok | 1 uso/config | 2 BACKUP SQL fallo | 3 verificacion fallo | 4 ocupado | 5 archivos fallo |
#                    6 copia externa fallo | 7 retencion fallo | 8 prerequisito (docker/SQL) | 10 app no volvio healthy
# Nunca imprime ni registra passwords ni credenciales del proveedor externo. Log: $BACKUP_DIR/logs/backup.log
# Documentacion: docs/backup-restore.md
# shellcheck source=lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

MODE="${1:-all}"
case "$MODE" in all|full|log|files) ;; -h|--help) sed -n '2,15p' "$0"; exit 0 ;; *) echo "modo invalido: $MODE (all|full|log|files)" >&2; exit $EX_USAGE ;; esac

load_config
START=$(date +%s)
mkdir -p "$BACKUP_DIR" 2>/dev/null || die $EX_USAGE "no se pudo crear BACKUP_DIR=$BACKUP_DIR"
mkdir -p "$BACKUP_DIR/logs"; rotate_log

RC=0
fail() { local code=$1; shift; log ERROR "$*"; if (( RC == 0 )); then RC=$code; fi; }

APP_STOPPED=0
RUN_SQL=()     # nombres generados en esta ejecucion (para la copia externa)
RUN_FILES=()

cleanup() {
  local ec=$?
  # Pase lo que pase, si detuvimos `app` se la vuelve a levantar.
  if (( APP_STOPPED == 1 )); then start_app || true; fi
  if (( ec != EX_BUSY )); then monitor_status "run-$MODE" "$ec" false; fi
  release_lock
  exit "$ec"
}
trap cleanup EXIT

log INFO "=== backup '$MODE' inicio (db=$DB_NAME dir=$BACKUP_DIR) ==="
check_prereqs
if ! acquire_lock; then
  log WARN "backup '$MODE' omitido: hay otra ejecucion en curso"
  trap - EXIT; exit $EX_BUSY
fi
prepare_dirs

# ---------------------------------------------------------------- app: detener / iniciar
stop_app() {
  if ! service_running app; then
    log INFO "app no estaba corriendo: no se detiene ni se reinicia"; return 0
  fi
  log INFO "deteniendo app (consistencia del par SQL+archivos)"
  dc stop -t 30 app >/dev/null 2>&1 || { log ERROR "no se pudo detener app"; return 1; }
  APP_STOPPED=1
}
start_app() {
  log INFO "iniciando app"
  dc start app >/dev/null 2>&1 || { fail $EX_APP "no se pudo iniciar app"; return 1; }
  local i st
  for i in $(seq 1 60); do
    st=$(dc ps --format '{{.Health}}' app 2>/dev/null | head -n1 || true)
    [[ "$st" == "healthy" ]] && { log INFO "app healthy"; APP_STOPPED=0; return 0; }
    sleep 3
  done
  fail $EX_APP "app no llego a healthy en 180 s"
  APP_STOPPED=0
  return 1
}

# ---------------------------------------------------------------- SQL
compression_clause() {
  case "$BACKUP_SQL_COMPRESSION" in
    on) echo "COMPRESSION," ;;
    off) echo "" ;;
    auto)
      local ed; ed=$(sql_scalar "SELECT CAST(SERVERPROPERTY('Edition') AS nvarchar(100))" || true)
      if [[ "$ed" == Express* ]]; then echo ""; else echo "COMPRESSION,"; fi ;;
    *) die $EX_USAGE "BACKUP_SQL_COMPRESSION debe ser auto|on|off" ;;
  esac
}

# Comprobaciones tras generar el archivo: existe, tamaño > 0, RESTORE VERIFYONLY (con CHECKSUM), sha256.
sql_post_checks() { # sql_post_checks <nombre>
  local name=$1 size
  size=$(tools sh -c 'test -f "/backups/sql/$0" && stat -c %s "/backups/sql/$0"' "$name" 2>/dev/null | tr -d '\r' || true)
  if ! is_uint "$size" || (( size == 0 )); then
    fail $EX_VERIFY "backup $name: no existe o tamaño 0"; return 1
  fi
  if ! sql_exec "RESTORE VERIFYONLY FROM DISK = N'/backups/sql/$name' WITH CHECKSUM" >/dev/null; then
    tools sh -c 'mv "/backups/sql/$0" "/backups/sql/$0.failed-verify"' "$name" || true
    fail $EX_VERIFY "backup $name: RESTORE VERIFYONLY FALLO (archivo puesto en cuarentena: $name.failed-verify)"; return 1
  fi
  tools sh -c 'cd /backups/sql && sha256sum "$0" > "$0.sha256"' "$name" || { fail $EX_VERIFY "no se pudo calcular sha256 de $name"; return 1; }
  log INFO "backup $name: bytes=$size verifyonly=OK sha256=OK"
}

sql_full() {
  local name="${DB_NAME}_full_$(utc_stamp).bak" comp
  comp=$(compression_clause)
  log INFO "BACKUP DATABASE -> $name (compresion: $([[ -n "$comp" ]] && echo si || echo no))"
  if ! sql_exec "BACKUP DATABASE [$DB_NAME] TO DISK = N'/backups/sql/$name' WITH INIT, CHECKSUM, ${comp} STATS = 25, NAME = N'$name'" >/dev/null; then
    tools sh -c 'rm -f "/backups/sql/$0"' "$name" || true       # nunca dejar un .bak parcial con nombre valido
    fail $EX_SQL "BACKUP DATABASE fallo ($name)"; return 1
  fi
  sql_post_checks "$name" || return 1
  RUN_SQL+=("$name")
  monitor_status full 0 true
}

sql_log() {
  local model; model=$(sql_scalar "SELECT recovery_model_desc FROM sys.databases WHERE name = N'$DB_NAME'" || true)
  if [[ "$model" != "FULL" && "$model" != "BULK_LOGGED" ]]; then
    log WARN "backup de log OMITIDO: recovery model de $DB_NAME es '${model:-?}' (solo FULL/BULK_LOGGED admiten log backups)"
    return 0
  fi
  local name="${DB_NAME}_log_$(utc_stamp).trn"
  log INFO "BACKUP LOG -> $name"
  if ! sql_exec "BACKUP LOG [$DB_NAME] TO DISK = N'/backups/sql/$name' WITH INIT, CHECKSUM, STATS = 100, NAME = N'$name'" >/dev/null; then
    tools sh -c 'rm -f "/backups/sql/$0"' "$name" || true
    fail $EX_SQL "BACKUP LOG fallo ($name). ¿Falta un full previo? (error 4214)"; return 1
  fi
  sql_post_checks "$name" || return 1
  RUN_SQL+=("$name")
  monitor_status log 0 true
}

# ---------------------------------------------------------------- archivos
files_backup() {
  local name="files_$(utc_stamp).tar.gz" out
  log INFO "backup de archivos -> $name (caddy-data: $BACKUP_INCLUDE_CADDY)"
  if ! out=$(tools sh /opt/bury-backup/files-backup.sh "$name" 2>&1); then
    log ERROR "$out"; fail $EX_FILES "backup de archivos fallo ($name)"; return 1
  fi
  log INFO "backup de archivos OK: $out"
  RUN_FILES+=("$name")
  monitor_status files 0 true
}

# ---------------------------------------------------------------- copia externa
offsite() {
  if [[ -z "$BACKUP_REMOTE" ]]; then
    if [[ "$BACKUP_REQUIRE_OFFSITE" == "true" ]]; then
      fail $EX_OFFSITE "copia externa REQUERIDA (BACKUP_REQUIRE_OFFSITE=true) pero BACKUP_REMOTE no esta configurado"
    else
      log WARN "copia externa NO configurada (BACKUP_REMOTE vacio): los backups existen SOLO en este servidor"
    fi
    return 0
  fi
  [[ "$BACKUP_REMOTE" =~ ^[A-Za-z0-9_-]+:.+ ]] || { fail $EX_OFFSITE "BACKUP_REMOTE debe tener la forma <remoto>:<ruta> (p.ej. offsite:bucket/erp)"; return 1; }

  # Centinela: con disco externo/NAS montado en el host, un punto de montaje vacio (disco desconectado) aceptaria la copia y la dejaria
  # en el disco del servidor. Si BACKUP_OFFSITE_MARKER esta definido, el remoto debe contener ese archivo (se crea una vez con
  # `rclone touch`); si no esta, se aborta ANTES de subir nada.
  if [[ -n "$BACKUP_OFFSITE_MARKER" ]]; then
    local mk
    mk=$(dc run --rm -T --no-deps --quiet-pull backup-offsite lsf "$BACKUP_REMOTE/$BACKUP_OFFSITE_MARKER" 2>/dev/null | tr -d '\r' || true)
    if [[ "$mk" != "$BACKUP_OFFSITE_MARKER" ]]; then
      monitor_status offsite-marker "$EX_OFFSITE" false
      fail $EX_OFFSITE "copia externa: falta el centinela '$BACKUP_OFFSITE_MARKER' en ${BACKUP_REMOTE%%:*}:* (¿disco/NAS desconectado o no montado?). No se sube nada"; return 1
    fi
  fi

  local sub names args up n
  for sub in sql files; do
    if [[ $sub == sql ]]; then names=("${RUN_SQL[@]}"); else names=("${RUN_FILES[@]}"); fi
    (( ${#names[@]} > 0 )) || continue
    args=()
    for n in "${names[@]}"; do args+=(--include "$n" --include "$n.sha256"); done
    # La SUBIDA cubre todos los backups locales con nombre valido (rclone copy omite lo que ya esta en el remoto): si una corrida
    # anterior no pudo subir un .trn (remoto caido), este lo repone y la cadena de logs remota no queda con un hueco permanente
    # (verificado 2026-10-29). La VERIFICACION sigue limitada a los archivos de esta corrida; el resto se comprueba con sha256 al descargar.
    if [[ $sub == sql ]]; then up=(--include "${DB_NAME}_full_*.bak" --include "${DB_NAME}_full_*.bak.sha256" --include "${DB_NAME}_log_*.trn" --include "${DB_NAME}_log_*.trn.sha256")
    else up=(--include "files_*.tar.gz" --include "files_*.tar.gz.sha256"); fi
    if (( REMOTE_RETENTION_DAYS > 0 )); then up+=(--max-age "${REMOTE_RETENTION_DAYS}d"); fi   # no resubir lo que la retencion remota borraria
    log INFO "copia externa: $sub -> ${BACKUP_REMOTE%%:*}:*/$sub (${#names[@]} archivo(s) de esta corrida + pendientes)"
    if ! dc run --rm -T --no-deps --quiet-pull backup-offsite copy "/backups/$sub" "$BACKUP_REMOTE/$sub" "${up[@]}" >/dev/null 2>"$BACKUP_DIR/logs/.offsite.err"; then
      monitor_status "offsite-$sub" "$EX_OFFSITE" false
      fail $EX_OFFSITE "copia externa FALLO ($sub): $(rclone_errtext "$(cat "$BACKUP_DIR/logs/.offsite.err")")"; return 1
    fi
    if ! rclone_verify backup-offsite "/backups/$sub" "$BACKUP_REMOTE/$sub" "${args[@]}"; then
      monitor_status "offsite-$sub" "$EX_OFFSITE" false
      fail $EX_OFFSITE "copia externa: la verificacion FALLO ($sub): $RCLONE_ERR"; return 1
    fi
    log INFO "copia externa $sub: subida y VERIFICADA"
    monitor_status "offsite-$sub" 0 true
  done
  rm -f "$BACKUP_DIR/logs/.offsite.err"

  if [[ "$MODE" != log ]] && (( REMOTE_RETENTION_DAYS > 0 )); then
    log INFO "retencion remota: se borran en el remoto backups con mas de ${REMOTE_RETENTION_DAYS} dias (solo nombres de este sistema)"
    local sub inc
    for sub in sql files; do
      if [[ $sub == sql ]]; then inc=(--include "${DB_NAME}_full_*.bak*" --include "${DB_NAME}_log_*.trn*"); else inc=(--include "files_*.tar.gz*"); fi
      # mkdir idempotente: en un remoto nuevo (p.ej. primera corrida = `full`) <sub>/ puede no existir y `delete` fallaria con
      # "directory not found" (falso fallo de la copia externa, verificado 2026-10-29).
      if ! rout=$(dc run --rm -T --no-deps --quiet-pull backup-offsite mkdir "$BACKUP_REMOTE/$sub" 2>&1 \
                  && dc run --rm -T --no-deps --quiet-pull backup-offsite delete "$BACKUP_REMOTE/$sub" --min-age "${REMOTE_RETENTION_DAYS}d" "${inc[@]}" 2>&1); then
        fail $EX_OFFSITE "retencion remota fallo ($sub): $(rclone_errtext "$rout")"; return 1
      fi
    done
  fi
}

# ---------------------------------------------------------------- retencion local
retention() {
  local out
  log INFO "retencion local: ${RETENTION_DAYS} dias, minimo ${RETENTION_MIN_KEEP} recientes por tipo"
  if ! out=$(dc run --rm -T --no-deps --quiet-pull -e DB_NAME="$DB_NAME" -e RETENTION_DAYS="$RETENTION_DAYS" -e MIN_KEEP="$RETENTION_MIN_KEEP" \
                 backup-files sh /opt/bury-backup/retention.sh 2>&1); then
    log ERROR "$out"; fail $EX_RETENTION "retencion local fallo"; return 1
  fi
  while IFS= read -r l; do log INFO "  $l"; done <<<"$out"
}

# ---------------------------------------------------------------- orquestacion
need_stop=0
if [[ "$BACKUP_STOP_APP" == "true" && ( "$MODE" == all || "$MODE" == files ) ]]; then need_stop=1; fi

if (( need_stop )); then stop_app || fail $EX_APP "no se pudo detener app; se aborta para no respaldar archivos en movimiento"; fi

if (( RC == 0 )); then
  case "$MODE" in
    all)   sql_full && files_backup ;;
    full)  sql_full ;;
    log)   sql_log ;;
    files) files_backup ;;
  esac || true
fi

if (( APP_STOPPED == 1 )); then start_app || true; fi

if (( RC == 0 )); then offsite || true; fi
if (( RC == 0 )) && [[ "$MODE" != log ]]; then retention || true; fi

DUR=$(( $(date +%s) - START ))
if (( RC == 0 )); then log INFO "=== backup '$MODE' OK (${DUR}s) ==="; else log ERROR "=== backup '$MODE' FALLO codigo=$RC (${DUR}s) ==="; fi
exit $RC
