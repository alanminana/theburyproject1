#!/usr/bin/env bash
# Tests de `restore-test.sh --offsite` con un `docker` FALSO (sin SQL Server, sin red, sin remoto real). Verifica el flujo de la
# prueba de restauracion desde la copia externa: descarga del ultimo full, verificacion sha256 de origen, base aislada, limpieza de la
# copia descargada y publicacion de monitor-status/offsite-restore-test.json SIN tocar restore-test.json.
#   bash scripts/backup/test-offsite-restore-test.sh        (exit 0 = todo OK)
set -uo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
T=$(mktemp -d); trap 'rm -rf "$T"' EXIT
PASS=0 FAIL=0
ok()  { PASS=$((PASS+1)); echo "PASS $1"; }
bad() { FAIL=$((FAIL+1)); echo "FAIL $1"; }
check() { local name=$1; shift; if "$@"; then ok "$name"; else bad "$name"; fi; }

bash -n "$HERE/restore-test.sh" && ok "bash -n restore-test.sh" || bad "bash -n restore-test.sh"

HASH_OK=$(printf 'a%.0s' $(seq 1 64))
HASH_BAD=$(printf 'b%.0s' $(seq 1 64))

mkdir -p "$T/bin"
cat >"$T/bin/docker" <<'FAKE'
#!/usr/bin/env bash
# docker falso: registra cada llamada en $FAKE_LOG y simula SQL/rclone segun variables FAKE_*.
echo "CALL $*" >>"$FAKE_LOG"
all="$*"; last="${*: -1}"
case "$all" in
  *" version"*) exit 0 ;;
  *"ps --status running --services"*) echo db; exit 0 ;;
  *"backup-offsite lsf"*) [[ -n "${FAKE_REMOTE_LS:-}" ]] && cat "$FAKE_REMOTE_LS"; exit 0 ;;
  *"restore-offsite copyto"*) [[ -n "${FAKE_DL_FAIL:-}" ]] && exit 1; exit 0 ;;
  *"backup-files sh -c"*)
    case "$all" in
      *'sha256sum "/backups'*) echo "$FAKE_GOT_SHA" ;;
      *'cut -d" " -f1'*) echo "$FAKE_WANT_SHA" ;;
      *"rm -f"*) echo "RM $*" >>"$FAKE_LOG" ;;
    esac
    exit 0 ;;
  *"exec -T db bash -c"*)
    if [[ -n "${FAKE_FAIL_ON:-}" && "$last" == *"$FAKE_FAIL_ON"* ]]; then exit 1; fi
    case "$all" in
      *"stat -c %s"*) echo 1000; exit 0 ;;
      *"ls -1 /backups/sql"*) exit 0 ;;
      *"test -f"*) exit 0 ;;
    esac
    case "$last" in
      "SELECT 1") echo 1 ;;
      *__EFMigrationsHistory*) echo 5 ;;
      *sys.tables*) echo 10 ;;
      *"THEN -1 ELSE 1"*) echo 1 ;;
      *"THEN 0 ELSE 1"*) echo 0 ;;
      *"RESTORE FILELISTONLY"*) printf 'Data|x|D|\nLog|y|L|\n' ;;
      *state_desc*) echo ONLINE ;;
    esac
    exit 0 ;;
esac
exit 0
FAKE
chmod +x "$T/bin/docker"

printf 'TheBuryProjectDb_full_2026-01-01_000000Z.bak\nTheBuryProjectDb_full_2026-01-02_000000Z.bak\nTheBuryProjectDb_log_2026-01-02_010000Z.trn\n' >"$T/ls.txt"
: >"$T/empty.txt"; : >"$T/.env"

# run_rt [VAR=valor...] -- args de restore-test.sh ; deja $RC, $BD y $LOG
n=0
run_rt() {
  n=$((n+1)); BD="$T/bk$n"; LOG="$T/log$n"; : >"$LOG"; mkdir -p "$BD"
  local envs=() a
  while [[ "${1:-}" != "--" ]]; do envs+=("$1"); shift; done; shift
  env -i PATH="$T/bin:$PATH" HOME="$T/home" FAKE_LOG="$LOG" BACKUP_DIR="$BD" COMPOSE_ENV_FILES="$T/.env" BACKUP_REMOTE="secure:erp" \
      FAKE_REMOTE_LS="$T/ls.txt" FAKE_WANT_SHA="$HASH_OK" FAKE_GOT_SHA="$HASH_OK" "${envs[@]}" \
      bash "$HERE/restore-test.sh" "$@" >"$T/out$n" 2>&1; RC=$?
}
status() { grep -o "\"$1\":[^,}]*" "$BD/monitor-status/offsite-restore-test.json" | head -n1 | cut -d: -f2; }

# --- camino feliz --------------------------------------------------------------------------------------------------------
run_rt -- --offsite
check "ok: exit 0" test "$RC" = 0
check "ok: estado offsite-restore-test.json exit 0 / verified true" bash -c '[[ "$(grep -c "\"exit\":0,\"verified\":true" "$1")" == 1 ]]' _ "$BD/monitor-status/offsite-restore-test.json"
check "ok: NO crea restore-test.json (no contamina el restore-test local)" test ! -e "$BD/monitor-status/restore-test.json"
check "ok: descarga el ULTIMO full (2026-01-02) con prefijo offsitert_" grep -q 'copyto secure:erp/sql/TheBuryProjectDb_full_2026-01-02_000000Z.bak /backups/sql/offsitert_TheBuryProjectDb_full_2026-01-02_000000Z.bak' "$LOG"
check "ok: descarga tambien el .sha256 de origen (fuera de backups/sql)" grep -q 'copyto secure:erp/sql/TheBuryProjectDb_full_2026-01-02_000000Z.bak.sha256 /backups/logs/.offsitert.sha256' "$LOG"
check "ok: restaura en base aislada <db>_OffsiteRestoreTest" grep -q 'RESTORE DATABASE \[TheBuryProjectDb_OffsiteRestoreTest\]' "$LOG"
check "ok: nunca restaura sobre la base viva ni sobre _RestoreTest" bash -c '! grep -qE "RESTORE DATABASE \[TheBuryProjectDb(_RestoreTest)?\]" "$1"' _ "$LOG"
check "ok: borra la copia descargada y la base temporal" bash -c 'grep -q "^RM .*offsitert_TheBuryProjectDb_full_2026-01-02" "$1" && grep -q "DROP DATABASE \[TheBuryProjectDb_OffsiteRestoreTest\]" "$1"' _ "$LOG"
check "ok: el mensaje del estado no contiene rutas del remoto ni secretos" bash -c '! grep -qiE "password|secret|secure:erp" "$1"' _ "$BD/monitor-status/offsite-restore-test.json"

# --- fallos: cada uno deja estado failed y limpia -------------------------------------------------------------------------
run_rt FAKE_GOT_SHA="$HASH_BAD" -- --offsite
check "sha256 distinto: exit 3" test "$RC" = 3
check "sha256 distinto: estado exit 3 / verified false" bash -c 'grep -q "\"exit\":3,\"verified\":false" "$1"' _ "$BD/monitor-status/offsite-restore-test.json"
check "sha256 distinto: no llega a restaurar" bash -c '! grep -q "RESTORE DATABASE" "$1"' _ "$LOG"
check "sha256 distinto: igual borra la copia descargada" grep -q '^RM .*offsitert_' "$LOG"

run_rt FAKE_DL_FAIL=1 -- --offsite
check "descarga falla: exit 6 y estado failed" bash -c '[[ "$1" == 6 ]] && grep -q "\"exit\":6,\"verified\":false" "$2"' _ "$RC" "$BD/monitor-status/offsite-restore-test.json"

run_rt FAKE_REMOTE_LS="$T/empty.txt" -- --offsite
check "remoto sin fulls (o crypt que no descifra): exit 6 y estado failed" bash -c '[[ "$1" == 6 ]] && grep -q "\"exit\":6,\"verified\":false" "$2"' _ "$RC" "$BD/monitor-status/offsite-restore-test.json"
check "remoto sin fulls: el mensaje menciona la causa" grep -q 'crypt' "$BD/monitor-status/offsite-restore-test.json"

run_rt FAKE_FAIL_ON="DBCC CHECKDB" -- --offsite
check "DBCC falla: exit 9 y estado failed" bash -c '[[ "$1" == 9 ]] && grep -q "\"exit\":9,\"verified\":false" "$2"' _ "$RC" "$BD/monitor-status/offsite-restore-test.json"
check "DBCC falla: borra copia y base temporal" bash -c 'grep -q "^RM .*offsitert_" "$1" && grep -q "DROP DATABASE" "$1"' _ "$LOG"

run_rt BACKUP_REMOTE="" -- --offsite
check "sin BACKUP_REMOTE: exit 1 y estado failed" bash -c '[[ "$1" == 1 ]] && grep -q "\"exit\":1,\"verified\":false" "$2"' _ "$RC" "$BD/monitor-status/offsite-restore-test.json"

run_rt -- --offsite --backup TheBuryProjectDb_full_2026-01-01_000000Z.bak
check "--offsite + --backup: rechazado (exit 1)" test "$RC" = 1

# --- antiguedad del ultimo full remoto -----------------------------------------------------------------------------------------
FRESH=$(date -u +%Y-%m-%d_%H%M%SZ)
printf 'TheBuryProjectDb_full_%s.bak
' "$FRESH" >"$T/ls-fresh.txt"
run_rt FAKE_REMOTE_LS="$T/ls-fresh.txt" -- --offsite
check "full remoto reciente: exit 0 y sin aviso de antiguedad" bash -c '[[ "$1" == 0 ]] && ! grep -q "AVISO" "$2" && ! grep -q "AVISO" "$3"' _ "$RC" "$T/out$n" "$BD/monitor-status/offsite-restore-test.json"
run_rt -- --offsite
check "full remoto viejo (default warn): exit 0 con WARN en la salida" bash -c '[[ "$1" == 0 ]] && grep -q "WARN.*AVISO: el ultimo full remoto tiene" "$2"' _ "$RC" "$T/out$n"
check "full remoto viejo: el aviso queda en el mensaje del estado" grep -q 'AVISO: el ultimo full remoto' "$BD/monitor-status/offsite-restore-test.json"
run_rt BACKUP_OFFSITE_RESTORE_STALE=fail -- --offsite
check "full remoto viejo con STALE=fail: exit 6, estado failed y sin descargar" bash -c '[[ "$1" == 6 ]] && grep -q "\"exit\":6,\"verified\":false" "$2" && ! grep -q "copyto" "$3"' _ "$RC" "$BD/monitor-status/offsite-restore-test.json" "$LOG"
run_rt BACKUP_OFFSITE_RESTORE_MAX_AGE_DAYS=99999 -- --offsite
check "MAX_AGE_DAYS alto: sin aviso" bash -c '[[ "$1" == 0 ]] && ! grep -q "AVISO" "$2"' _ "$RC" "$T/out$n"
run_rt BACKUP_OFFSITE_RESTORE_STALE=otro -- --offsite
check "STALE invalido: exit 1" test "$RC" = 1

# --- el modo local sigue igual ---------------------------------------------------------------------------------------------
run_rt -- --backup TheBuryProjectDb_full_2026-01-01_000000Z.bak
check "modo local: exit 0 y escribe restore-test.json (no offsite)" bash -c '[[ "$1" == 0 && -f "$2/monitor-status/restore-test.json" && ! -e "$2/monitor-status/offsite-restore-test.json" ]]' _ "$RC" "$BD"
check "modo local: base <db>_RestoreTest" grep -q 'RESTORE DATABASE \[TheBuryProjectDb_RestoreTest\]' "$LOG"
check "modo local: no usa el remoto" bash -c '! grep -q "restore-offsite" "$1"' _ "$LOG"

echo; echo "resultado: $PASS OK, $FAIL fallos"; (( FAIL == 0 ))
