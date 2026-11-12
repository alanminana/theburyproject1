#!/usr/bin/env bash
# PRUEBA DE RESTORE (no destructiva): restaura el ultimo backup en una base temporal <db>_RestoreTest, la valida y la elimina.
# Es la unica forma de saber que los backups sirven (RESTORE VERIFYONLY NO reemplaza una restauracion real). Programarla (semanal).
#
#   scripts/backup/restore-test.sh [--backup <full.bak>] [--no-logs] [--keep] [--offsite]
#
# --offsite: prueba de restauracion DESDE LA COPIA EXTERNA (P-06/P-08). Descarga el ultimo full de BACKUP_REMOTE (a traves del crypt, o
#   sea que prueba tambien que las passwords crypt vigentes descifran), verifica su sha256 de origen, lo restaura en
#   <db>_OffsiteRestoreTest (base aislada), corre las mismas validaciones y BORRA la copia descargada y la base. Publica
#   monitor-status/offsite-restore-test.json (el monitor alerta por antiguedad/fallo). No toca la base viva ni el restore-test local.
#
# Valida: RESTORE OK, DBCC CHECKDB sin errores, __EFMigrationsHistory no vacia, tablas, y compara contra la base viva.
# NUNCA toca la base de produccion (el destino siempre es <db>_RestoreTest). Codigos: 0 ok | 9 validacion fallo | 8 prerequisito
# shellcheck source=lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

BACKUP="" LOGS=1 KEEP=0 OFFSITE=0
while (( $# )); do
  case "$1" in
    --backup) BACKUP="${2:-}"; shift 2 ;;
    --no-logs) LOGS=0; shift ;;
    --keep) KEEP=1; shift ;;
    --offsite) OFFSITE=1; shift ;;
    -h|--help) sed -n '2,15p' "$0"; exit 0 ;;
    *) die $EX_USAGE "argumento desconocido: $1" ;;
  esac
done

load_config
mkdir -p "$BACKUP_DIR/logs"

# Estado para el monitor: SIEMPRE se publica al salir (ok o fallo). verified=true solo si llego al final con exit 0.
RT_DONE=0 OFFSITE_LOCAL="" OFFSITE_SHA=".offsitert.sha256"
STATUS_KIND=restore-test; (( OFFSITE )) && STATUS_KIND=offsite-restore-test
finish() {
  local ec=$?
  if (( ec == 0 && RT_DONE == 1 )); then restore_test_status 0 true "$STATUS_KIND OK" "$STATUS_KIND"
  else restore_test_status "$ec" false "${LAST_ERROR:-fallo inesperado (exit $ec)}" "$STATUS_KIND"; fi
  (( KEEP )) || drop_test_safe
  cleanup_offsite_copy
}
# Borra SOLO la copia descargada por esta corrida (nombre validado offsitert_*): nunca un backup real.
cleanup_offsite_copy() {
  [[ -n "$OFFSITE_LOCAL" && "$OFFSITE_LOCAL" =~ ^offsitert_[A-Za-z0-9_.-]+_full_[0-9]{4}-[0-9]{2}-[0-9]{2}_[0-9]{6}Z\.bak$ ]] || return 0
  tools sh -c 'rm -f "/backups/sql/$0" "/backups/logs/$1"' "$OFFSITE_LOCAL" "$OFFSITE_SHA" >/dev/null 2>&1 || log WARN "no se pudo borrar la copia descargada $OFFSITE_LOCAL (borrar a mano)"
}
drop_test_safe() { declare -F drop_test >/dev/null && drop_test || true; }
trap finish EXIT
check_prereqs
TEST_DB="${DB_NAME}_RestoreTest"
(( OFFSITE )) && TEST_DB="${DB_NAME}_OffsiteRestoreTest"

if (( OFFSITE )); then
  [[ -z "$BACKUP" ]] || die $EX_USAGE "--offsite no se combina con --backup (siempre usa el ultimo full del remoto)"
  [[ -n "$BACKUP_REMOTE" && "$BACKUP_REMOTE" =~ ^[A-Za-z0-9_-]+:.+ ]] || die $EX_USAGE "--offsite requiere BACKUP_REMOTE=<remoto>:<ruta>"
  remote_full=$(dc run --rm -T --no-deps --quiet-pull backup-offsite lsf "$BACKUP_REMOTE/sql" --include "${DB_NAME}_full_*.bak" 2>/dev/null | tr -d '\r' \
    | grep -E "^${DB_NAME}_full_${BK_TS}\.bak$" | sort | tail -n1 || true)
  [[ -n "$remote_full" ]] || die $EX_OFFSITE "no hay backups full en ${BACKUP_REMOTE%%:*}:*/sql (o el remoto no responde / las passwords crypt no descifran)"
  OFFSITE_LOCAL="offsitert_$remote_full"
  log INFO "offsite-restore-test: descargando $remote_full desde ${BACKUP_REMOTE%%:*}:*"
  dc run --rm -T --no-deps --quiet-pull restore-offsite copyto "$BACKUP_REMOTE/sql/$remote_full" "/backups/sql/$OFFSITE_LOCAL" >/dev/null 2>&1     || die $EX_OFFSITE "la descarga del full desde la copia externa FALLO"
  dc run --rm -T --no-deps --quiet-pull restore-offsite copyto "$BACKUP_REMOTE/sql/$remote_full.sha256" "/backups/logs/$OFFSITE_SHA" >/dev/null 2>&1     || die $EX_OFFSITE "no se pudo descargar $remote_full.sha256 del remoto (sin hash de origen no hay verificacion de extremo a extremo)"
  want=$(tools sh -c 'cut -d" " -f1 "/backups/logs/$0" | head -n1' "$OFFSITE_SHA" | tr -d '\r')
  got=$(tools sh -c 'sha256sum "/backups/sql/$0" | cut -d" " -f1' "$OFFSITE_LOCAL" | tr -d '\r')
  [[ "$want" =~ ^[0-9a-f]{64}$ && "$want" == "$got" ]] || die $EX_VERIFY "sha256 de la copia descargada NO coincide con el de origen (corrupcion en el remoto o en transito)"
  log INFO "offsite-restore-test: descarga verificada (sha256 de origen)"
  tools sh -c 'chown 10001:0 "/backups/sql/$0" && chmod 640 "/backups/sql/$0"' "$OFFSITE_LOCAL" || die $EX_PREREQ "no se pudieron ajustar permisos de la copia descargada"
  BACKUP="$OFFSITE_LOCAL"
fi

if [[ -z "$BACKUP" ]]; then
  BACKUP=$(dc exec -T db bash -c 'ls -1 /backups/sql | grep -E "^${0}_full_[0-9]{4}-[0-9]{2}-[0-9]{2}_[0-9]{6}Z\.bak$" | sort | tail -n1' "$DB_NAME" | tr -d '\r')
  [[ -n "$BACKUP" ]] || die $EX_RESTORE "no hay backups full en $BACKUP_DIR/sql"
fi

drop_test() {
  [[ "$TEST_DB" == *_RestoreTest || "$TEST_DB" == *_OffsiteRestoreTest ]] || return 0   # defensa: solo se elimina una base temporal de prueba
  sql_exec "IF DB_ID(N'$TEST_DB') IS NOT NULL BEGIN ALTER DATABASE [$TEST_DB] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$TEST_DB]; END" >/dev/null 2>&1 || true
}

log INFO "=== restore-test inicio: $BACKUP -> [$TEST_DB] ==="
T0=$(date +%s)
drop_test
args=(--backup "$BACKUP" --target-db "$TEST_DB")
(( LOGS )) && args+=(--with-logs)
"$BK_SCRIPT_DIR/restore-sql.sh" "${args[@]}" || die $EX_RESTORE "el restore de prueba FALLO"
T1=$(date +%s)

log INFO "DBCC CHECKDB [$TEST_DB]"
sql_exec "DBCC CHECKDB (N'$TEST_DB') WITH NO_INFOMSGS, ALL_ERRORMSGS" >/dev/null || die $EX_RESTORE "DBCC CHECKDB REPORTO ERRORES en la base restaurada"
log INFO "DBCC CHECKDB OK"

mig=$(sql_scalar "SELECT COUNT(*) FROM [$TEST_DB].dbo.__EFMigrationsHistory")
tables=$(sql_scalar "SELECT COUNT(*) FROM [$TEST_DB].sys.tables")
is_uint "$mig" && (( mig > 0 )) || die $EX_RESTORE "la base restaurada no tiene historial de migraciones"
live_mig=$(sql_scalar "SELECT CASE WHEN DB_ID(N'$DB_NAME') IS NULL THEN -1 ELSE 1 END" )
live=""
if [[ "$live_mig" == "1" ]]; then live=$(sql_scalar "SELECT COUNT(*) FROM [$DB_NAME].dbo.__EFMigrationsHistory" || true); fi
log INFO "restaurada: migraciones=$mig tablas=$tables (base viva: migraciones=${live:-n/a})"
if [[ -n "$live" ]] && is_uint "$live" && (( mig > live )); then
  die $EX_RESTORE "la base restaurada tiene MAS migraciones ($mig) que la viva ($live): inconsistencia"
fi
if [[ -n "$live" ]] && is_uint "$live" && (( mig < live )); then
  log WARN "el backup tiene $mig migraciones y la base viva $live (normal si hubo migraciones nuevas despues del backup)"
fi
RT_DONE=1
log INFO "=== $STATUS_KIND OK: tiempo de restore $((T1 - T0)) s ==="
