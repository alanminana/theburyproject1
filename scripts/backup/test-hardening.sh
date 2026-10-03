#!/usr/bin/env bash
# Tests focalizados de hardening operativo de backup/monitoring. Sin docker, cron, systemd ni root reales; todo en un temp dir.
#   bash scripts/backup/test-hardening.sh        (exit 0 = todo OK)
# Los "secretos" son valores sinteticos; los tests no los imprimen (solo reportan PASS/FAIL).
set -uo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
T=$(mktemp -d); trap 'rm -rf "$T"' EXIT
PASS=0 FAIL=0
ok()   { PASS=$((PASS+1)); echo "PASS $1"; }
bad()  { FAIL=$((FAIL+1)); echo "FAIL $1"; }
check() { local name=$1; shift; if "$@"; then ok "$name"; else bad "$name"; fi; }

# --- sintaxis -------------------------------------------------------------------------------------------------------------
for f in lib.sh backup.sh restore-test.sh restore-sql.sh install-schedule.sh test-hardening.sh; do
  check "bash -n $f" bash -n "$HERE/$f"
done

# --- install-schedule.sh --------------------------------------------------------------------------------------------------
# Algunos filesystems (NTFS bajo Git Bash) ignoran chmod: los chequeos de modo se omiten ahi (en Linux siempre corren).
: >"$T/probe"; chmod 0600 "$T/probe"; if [[ "$(stat -c %a "$T/probe")" == 600 ]]; then MODES=1; else MODES=0; echo "SKIP chequeos de modo: el filesystem no respeta chmod"; fi
modecheck() { if (( MODES )); then check "$@"; fi; }
USER_NOW=$(id -un); SB="$T/sb"; mkdir -p "$SB"
out=$(BURY_SCHEDULE_SANDBOX="$SB" bash "$HERE/install-schedule.sh" --print --user "$USER_NOW"); rc=$?
check "--print rc=0 y genera 3 jobs" bash -c '[[ '"$rc"' == 0 && $(grep -c "^[0-9*]" <<<"$1") == 3 ]]' _ "$out"
check "--print no toca el filesystem" bash -c '[[ -z "$(find "$1" -mindepth 1)" ]]' _ "$SB"
check "--print-logrotate no toca el filesystem" bash -c 'BURY_SCHEDULE_SANDBOX="$1" bash "$2/install-schedule.sh" --print-logrotate --user "$3" >/dev/null && [[ -z "$(find "$1" -mindepth 1)" ]]' _ "$SB" "$HERE" "$USER_NOW"

# usuario no-root: el log queda de su propiedad, 0600 (root:600 dejaba el cron sin poder abrir la redireccion)
BURY_SCHEDULE_SANDBOX="$SB" bash "$HERE/install-schedule.sh" --install --user "$USER_NOW" >/dev/null; rc=$?
LOG="$SB/var/log/bury-backup-errors.log"
check "--install rc=0" test "$rc" = 0
check "log owner = usuario del cron" test "$(stat -c %U "$LOG")" = "$USER_NOW"
modecheck "log modo 0600 (sin abrir permisos)" test "$(stat -c %a "$LOG")" = 600
modecheck "cron file 0644" test "$(stat -c %a "$SB/etc/cron.d/bury-backup")" = 644
check "logrotate instalado con create 0600 user" grep -q "create 0600 $USER_NOW " "$SB/etc/logrotate.d/bury-backup"
check "el usuario del cron puede abrir el log en append (2>>)" bash -c ': 2>>"$1"' _ "$LOG"
# un log preexistente con otro modo se corrige, el contenido se conserva; idempotencia
echo "linea previa" >"$LOG"; chmod 0666 "$LOG"
h1=$(cat "$SB/etc/cron.d/bury-backup" "$SB/etc/logrotate.d/bury-backup" | sha256sum)
BURY_SCHEDULE_SANDBOX="$SB" bash "$HERE/install-schedule.sh" --install --user "$USER_NOW" >/dev/null
h2=$(cat "$SB/etc/cron.d/bury-backup" "$SB/etc/logrotate.d/bury-backup" | sha256sum)
check "reinstalar es idempotente" test "$h1" = "$h2"
modecheck "reinstalar corrige 0666 -> 0600" test "$(stat -c %a "$LOG")" = 600
check "reinstalar conserva el contenido del log" grep -q "linea previa" "$LOG"
# falla clara
err=$(BURY_SCHEDULE_SANDBOX="$SB" bash "$HERE/install-schedule.sh" --install --user no_existe_xyz 2>&1 >/dev/null); rc=$?
check "usuario inexistente: falla con mensaje claro" bash -c '[[ '"$rc"' != 0 && "$1" == *"no existe"* ]]' _ "$err"
: >"$T/blocker"
err=$(BURY_SCHEDULE_SANDBOX="$T/blocker" bash "$HERE/install-schedule.sh" --install --user "$USER_NOW" 2>&1 >/dev/null); rc=$?
check "no se puede preparar el log: falla != 0 con mensaje" bash -c '[[ '"$rc"' != 0 && -n "$1" ]]' _ "$err"
check "sin sandbox y sin root --install se rechaza" bash -c '[[ $EUID -eq 0 ]] || ! bash "$1/install-schedule.sh" --install >/dev/null 2>&1' _ "$HERE"
BURY_SCHEDULE_SANDBOX="$SB" bash "$HERE/install-schedule.sh" --uninstall >/dev/null
check "--uninstall quita cron+logrotate y conserva el log" bash -c '[[ ! -e "$1/etc/cron.d/bury-backup" && ! -e "$1/etc/logrotate.d/bury-backup" && -e "$2" ]]' _ "$SB" "$LOG"
if command -v logrotate >/dev/null; then
  BURY_SCHEDULE_SANDBOX="$SB" bash "$HERE/install-schedule.sh" --install --user "$USER_NOW" >/dev/null
  check "logrotate -d acepta la config" bash -c 'logrotate -d "$1" >/dev/null 2>&1' _ "$SB/etc/logrotate.d/bury-backup"
fi

# --- sanitize_text --------------------------------------------------------------------------------------------------------
# shellcheck source=lib.sh
source "$HERE/lib.sh" >/dev/null 2>&1
set +e   # lib.sh activa `set -e`; los tests manejan sus propios codigos
S1="Zq9SyntheticSecret123"
assert_redacted() { # nombre entrada  -> el secreto sintetico no debe sobrevivir
  local r; r=$(sanitize_text "$2"); if [[ "$r" != *"$S1"* && "$r" == *REDACTED* ]]; then ok "sanitize: $1"; else bad "sanitize: $1"; fi
}
assert_redacted "password="                 "rclone: auth failed password=$S1 host=x"
assert_redacted "passwd:"                   "login passwd: $S1"
assert_redacted "token="                    "Failed to copy: token=$S1: 401"
assert_redacted "Authorization Bearer"      "Authorization: Bearer $S1"
assert_redacted "bearer suelto"             "sent bearer $S1 to server"
assert_redacted "access_key/secret_key"     "access_key=$S1 secret_key=$S1"
assert_redacted "AWS secret"                "aws_secret_access_key = $S1"
assert_redacted "AWS RCLONE_..._SECRET_ACCESS_KEY" "RCLONE_CONFIG_OFFSITE_SECRET_ACCESS_KEY=$S1"
AWS_ID_PFX="AK"; AWS_ID_MID="IAABCDEF"; AWS_ID_SFX="GHIJKLMNOP"   # se arma en runtime: el fuente no contiene un access key id completo (gitleaks)
AWS_ID="${AWS_ID_PFX}${AWS_ID_MID}${AWS_ID_SFX}"
assert_redacted "AWS key id"                "key $AWS_ID denied"
assert_redacted "Azure AccountKey"          "DefaultEndpointsProtocol=https;AccountKey=$S1;EndpointSuffix=core.windows.net"
assert_redacted "Azure SAS sig"             "GET https://acct.blob.core.windows.net/c?sv=2022&sig=$S1&se=2030 failed"
assert_redacted "rclone password"           "config: password=$S1"
assert_redacted "rclone password2 (digito)" "RCLONE_CONFIG_SECURE_PASSWORD2=$S1"
assert_redacted "JSON password"             "{\"password\":\"$S1\",\"user\":\"bob\"}"
assert_redacted "URL user:pass@host"        "Get sftp://bob:$S1@host.example/path: dial failed"
assert_redacted "JWT"                       "auth eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0In0.abcDEF123"
r=$(sanitize_text "password=$S1 pero el resto: connection refused to host.example:22")
check "sanitize conserva contexto del error" bash -c '[[ "$1" == *"connection refused to host.example:22"* && "$1" == "password=[REDACTED]"* ]]' _ "$r"
n="Failed to copy: cannot list directory /sql: 403 Forbidden (signal received, token bucket ok) at 2026-09-23T10:00:00Z"
check "sanitize no altera texto normal" test "$(sanitize_text "$n")" = "$n"
check "sanitize acepta stdin" bash -c 'source "$1/lib.sh" >/dev/null 2>&1; [[ "$(echo "secret=abc12345" | sanitize_text)" == "secret=[REDACTED]" ]]' _ "$HERE"

# --- rclone_verify: RCLONE_ERR sanitizado --------------------------------------------------------------------------------
dc() { echo "ERROR : failed: password=$S1 unexpected EOF"; return 1; }
rclone_verify x /a b >/dev/null 2>&1
check "RCLONE_ERR sin secreto y con contexto" bash -c '[[ "$1" != *"$2"* && "$1" == *"unexpected EOF"* ]]' _ "$RCLONE_ERR" "$S1"
unset -f dc

# --- offsite(): REQUIRE_OFFSITE sin remoto y upload fallido (funcion extraida de backup.sh) ------------------------------
eval "$(sed -n '/^offsite() {/,/^}/p' "$HERE/backup.sh")"
BACKUP_DIR="$T/bk"; mkdir -p "$BACKUP_DIR/logs"; RUN_SQL=(x.bak); RUN_FILES=(); REMOTE_RETENTION_DAYS=0
FAILCODE="" FAILMSG=""; fail() { FAILCODE=$1; FAILMSG=$2; }
BACKUP_REMOTE="" BACKUP_REQUIRE_OFFSITE=true; offsite
check "REQUIRE_OFFSITE=true + remoto vacio -> exit 6" test "$FAILCODE" = 6
FAILCODE=""; BACKUP_REQUIRE_OFFSITE=false; offsite
check "REQUIRE_OFFSITE=false + remoto vacio -> sin fallo" test -z "$FAILCODE"
FAILCODE=""; BACKUP_REMOTE="offsite:bucket/erp"
dc() { echo "Failed to copy: access_key=$S1 timeout" >&2; return 1; }
offsite
check "upload fallido -> exit 6" test "$FAILCODE" = 6
check "upload fallido: mensaje sin secreto, con contexto" bash -c '[[ "$1" != *"$2"* && "$1" == *timeout* ]]' _ "$FAILMSG" "$S1"
check "upload fallido: monitor-status offsite-sql exit=6 verified=false" grep -q '"exit":6,"verified":false' "$BACKUP_DIR/monitor-status/offsite-sql.json"
unset -f dc fail

# --- restore_test_status --------------------------------------------------------------------------------------------------
BACKUP_DIR="$T/rt"; F="$BACKUP_DIR/monitor-status/restore-test.json"
PYBIN=$(command -v python3 || command -v python); export PYBIN
pyget() { "$PYBIN" - "$F" "$1" <<'PY'
import json,sys; d=json.load(open(sys.argv[1])); v=d[sys.argv[2]]; print("null" if v is None else v)
PY
}
restore_test_status 0 true "restore-test OK"
export -f pyget; export F
check "restore ok: exit 0 / verified true / last_success = time" bash -c '[[ "$(pyget exit)" == 0 && "$(pyget verified)" == True && "$(pyget last_success)" == "$(pyget time)" ]]'
ok_ts=$(pyget last_success); sleep 1
restore_test_status 9 false "restore FALLO password=$S1 \"comillas\" y \\ barra"
check "restore failed: JSON valido, exit 9, verified false" test "$(pyget exit)/$(pyget verified)" = "9/False"
check "restore failed: conserva last_success del ultimo exito" test "$(pyget last_success)" = "$ok_ts"
m=$(pyget message)
check "restore failed: mensaje sanitizado y con contexto" bash -c '[[ "$1" != *"$2"* && "$1" == *"restore FALLO"* ]]' _ "$m" "$S1"
modecheck "restore: archivo 0600" test "$(stat -c %a "$F")" = 600
rm -rf "$BACKUP_DIR"; restore_test_status 9 false "primer intento fallido"
check "restore failed sin exito previo: last_success null" test "$(pyget last_success)" = null

# --- restore-test.sh end-to-end de fallo (docker falso: prerequisito no disponible) ---------------------------------------
mkdir -p "$T/fakebin"; printf '#!/bin/sh\nexit 1\n' >"$T/fakebin/docker"; chmod +x "$T/fakebin/docker"
BD="$T/e2e"; PATH="$T/fakebin:$PATH" BACKUP_DIR="$BD" bash "$HERE/restore-test.sh" >/dev/null 2>&1; rc=$?
check "restore-test.sh sin docker: exit 8" test "$rc" = 8
check "restore-test.sh publica estado failed (exit 8, verified false)" grep -q '"exit":8,"verified":false' "$BD/monitor-status/restore-test.json"
check "restore-test.sh: mensaje del fallo en el estado" grep -q 'docker' "$BD/monitor-status/restore-test.json"

echo; echo "resultado: $PASS OK, $FAIL fallos"; (( FAIL == 0 ))
