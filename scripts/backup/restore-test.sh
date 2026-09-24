#!/usr/bin/env bash
# PRUEBA DE RESTORE (no destructiva): restaura el ultimo backup en una base temporal <db>_RestoreTest, la valida y la elimina.
# Es la unica forma de saber que los backups sirven (RESTORE VERIFYONLY NO reemplaza una restauracion real). Programarla (semanal).
#
#   scripts/backup/restore-test.sh [--backup <full.bak>] [--no-logs] [--keep]
#
# Valida: RESTORE OK, DBCC CHECKDB sin errores, __EFMigrationsHistory no vacia, tablas, y compara contra la base viva.
# NUNCA toca la base de produccion (el destino siempre es <db>_RestoreTest). Codigos: 0 ok | 9 validacion fallo | 8 prerequisito
# shellcheck source=lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

BACKUP="" LOGS=1 KEEP=0
while (( $# )); do
  case "$1" in
    --backup) BACKUP="${2:-}"; shift 2 ;;
    --no-logs) LOGS=0; shift ;;
    --keep) KEEP=1; shift ;;
    -h|--help) sed -n '2,9p' "$0"; exit 0 ;;
    *) die $EX_USAGE "argumento desconocido: $1" ;;
  esac
done

load_config
mkdir -p "$BACKUP_DIR/logs"

# Estado para el monitor: SIEMPRE se publica al salir (ok o fallo). verified=true solo si llego al final con exit 0.
RT_DONE=0
finish() {
  local ec=$?
  if (( ec == 0 && RT_DONE == 1 )); then restore_test_status 0 true "restore-test OK"
  else restore_test_status "$ec" false "${LAST_ERROR:-fallo inesperado (exit $ec)}"; fi
  (( KEEP )) || drop_test_safe
}
drop_test_safe() { declare -F drop_test >/dev/null && drop_test || true; }
trap finish EXIT
check_prereqs
TEST_DB="${DB_NAME}_RestoreTest"

if [[ -z "$BACKUP" ]]; then
  BACKUP=$(dc exec -T db bash -c 'ls -1 /backups/sql | grep -E "^${0}_full_[0-9]{4}-[0-9]{2}-[0-9]{2}_[0-9]{6}Z\.bak$" | sort | tail -n1' "$DB_NAME" | tr -d '\r')
  [[ -n "$BACKUP" ]] || die $EX_RESTORE "no hay backups full en $BACKUP_DIR/sql"
fi

drop_test() {
  [[ "$TEST_DB" == *_RestoreTest ]] || return 0   # defensa: solo se elimina una base temporal de prueba
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
log INFO "=== restore-test OK: tiempo de restore $((T1 - T0)) s ==="
