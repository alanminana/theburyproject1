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
for f in lib.sh backup.sh lib-capacity.sh retention-capacity.sh copy-removable.sh restore-test.sh restore-sql.sh restore-files.sh fetch-offsite.sh prepare-dir.sh install-schedule.sh check-custody.sh rotate-crypt.sh test-hardening.sh test-offsite-sftp.sh test-key-rotation.sh test-dr-integral.sh verify-local-copy.sh setup-local-disk-copy.sh restore-test-local-copy.sh test-verify-local-copy.sh; do
  check "bash -n $f" bash -n "$HERE/$f"
done

# --- install-schedule.sh --------------------------------------------------------------------------------------------------
# Algunos filesystems (NTFS bajo Git Bash) ignoran chmod: los chequeos de modo se omiten ahi (en Linux siempre corren).
: >"$T/probe"; chmod 0600 "$T/probe"; if [[ "$(stat -c %a "$T/probe")" == 600 ]]; then MODES=1; else MODES=0; echo "SKIP chequeos de modo: el filesystem no respeta chmod"; fi
modecheck() { if (( MODES )); then check "$@"; fi; }
USER_NOW=$(id -un); SB="$T/sb"; mkdir -p "$SB"
out=$(BURY_SCHEDULE_SANDBOX="$SB" bash "$HERE/install-schedule.sh" --print --user "$USER_NOW"); rc=$?
check "--print rc=0 y genera 4 jobs (full, log, restore-test, custodia)" bash -c '[[ '"$rc"' == 0 && $(grep -c "^[0-9*]" <<<"$1") == 4 ]]' _ "$out"
check "--print incluye la verificacion de custodia (check-custody.sh --quiet)" grep -q "check-custody.sh --quiet" <<<"$out"
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

# --- dos instalaciones en el mismo servidor (P-06 cierre tecnico): cron propio por proyecto y rechazo de pisar el ajeno -------------------
SB2="$T/sb2"; mkdir -p "$SB2"
BURY_SCHEDULE_SANDBOX="$SB2" bash "$HERE/install-schedule.sh" --install --user "$USER_NOW" >/dev/null
hlegacy=$(sha256sum "$SB2/etc/cron.d/bury-backup" | cut -d' ' -f1)
BURY_SCHEDULE_SANDBOX="$SB2" bash "$HERE/install-schedule.sh" --install --user "$USER_NOW" --project theburyproject-prod >/dev/null; rc=$?
check "--project theburyproject-prod: rc=0 y archivos propios (cron, logrotate, log de errores)" bash -c '[[ '"$rc"' == 0 && -f "$1/etc/cron.d/bury-backup-prod" && -f "$1/etc/logrotate.d/bury-backup-prod" && -f "$1/var/log/bury-backup-prod-errors.log" ]]' _ "$SB2"
check "--project: NO toca el cron de la otra instalacion" test "$(sha256sum "$SB2/etc/cron.d/bury-backup" | cut -d' ' -f1)" = "$hlegacy"
check "--project: los 4 jobs llevan COMPOSE_PROJECT_NAME y su propio log de errores" bash -c '[[ $(grep -c "COMPOSE_PROJECT_NAME=theburyproject-prod /bin/bash" "$1") == 4 && $(grep -c "2>>/var/log/bury-backup-prod-errors.log" "$1") == 4 && $(grep -c "bury-backup-errors.log" "$1") == 0 ]]' _ "$SB2/etc/cron.d/bury-backup-prod"
check "sin --project el cron legado NO lleva COMPOSE_PROJECT_NAME ni cambia de nombre" bash -c '! grep -q COMPOSE_PROJECT_NAME "$1" && grep -q "bury-backup-errors.log" "$1"' _ "$SB2/etc/cron.d/bury-backup"
check "--log-offset 7 con --log-every 15 -> minutos 7,22,37,52" bash -c 'bash "$1/install-schedule.sh" --print --user "$2" --log-offset 7 | grep -q "^7,22,37,52 \* \* \* \* "' _ "$HERE" "$USER_NOW"
check "--log-offset fuera de rango se rechaza" bash -c '! bash "$1/install-schedule.sh" --print --log-offset 15 >/dev/null 2>&1' _ "$HERE"
check "--project invalido se rechaza" bash -c '! bash "$1/install-schedule.sh" --print --project "Prod X" >/dev/null 2>&1' _ "$HERE"
# otro clon del repo no puede reemplazar el cron existente (mismo nombre de archivo)
CL2="$T/clone2"; mkdir -p "$CL2/scripts/backup"; cp "$HERE/install-schedule.sh" "$CL2/scripts/backup/"
err=$(BURY_SCHEDULE_SANDBOX="$SB2" bash "$CL2/scripts/backup/install-schedule.sh" --install --user "$USER_NOW" 2>&1 >/dev/null); rc=$?
check "otro clon sin --project: rechazado (rc!=0), mensaje claro y cron intacto" bash -c '[[ '"$rc"' != 0 && "$1" == *"pertenece a otro clon"* ]] && [[ "$(sha256sum "$2" | cut -d" " -f1)" == "$3" ]]' _ "$err" "$SB2/etc/cron.d/bury-backup" "$hlegacy"
BURY_SCHEDULE_SANDBOX="$SB2" bash "$CL2/scripts/backup/install-schedule.sh" --install --user "$USER_NOW" --project theburyproject-otro >/dev/null; rc=$?
check "otro clon con su --project: crea su archivo y deja los demas" bash -c '[[ '"$rc"' == 0 && -f "$1/etc/cron.d/bury-backup-otro" && -f "$1/etc/cron.d/bury-backup-prod" ]]' _ "$SB2"

hprod=$(sha256sum "$SB2/etc/cron.d/bury-backup-prod" | cut -d' ' -f1)
err=$(BURY_SCHEDULE_SANDBOX="$SB2" bash "$HERE/install-schedule.sh" --install --user "$USER_NOW" --project prod 2>&1 >/dev/null); rc=$?
check "mismo sufijo, otro proyecto (--project prod vs theburyproject-prod): rechazado y cron intacto" bash -c '[[ '"$rc"' != 0 && "$1" == *"pertenece al proyecto"* ]] && [[ "$(sha256sum "$2" | cut -d" " -f1)" == "$3" ]]' _ "$err" "$SB2/etc/cron.d/bury-backup-prod" "$hprod"
BURY_SCHEDULE_SANDBOX="$SB2" bash "$HERE/install-schedule.sh" --uninstall --project prod >/dev/null 2>&1; rc=$?
check "--uninstall con el proyecto equivocado: rechazado y NO borra el cron ajeno" bash -c '[[ '"$rc"' != 0 && -f "$1" ]]' _ "$SB2/etc/cron.d/bury-backup-prod"
BURY_SCHEDULE_SANDBOX="$SB2" bash "$CL2/scripts/backup/install-schedule.sh" --uninstall --project theburyproject-otro >/dev/null 2>&1; rc=$?
check "--uninstall con su proyecto: borra solo su archivo" bash -c '[[ '"$rc"' == 0 && ! -e "$1/etc/cron.d/bury-backup-otro" && -f "$1/etc/cron.d/bury-backup-prod" && -f "$1/etc/cron.d/bury-backup" ]]' _ "$SB2"
BURY_SCHEDULE_SANDBOX="$SB2" bash "$HERE/install-schedule.sh" --install --user "$USER_NOW" --project theburyproject-prod >/dev/null; rc=$?
check "reinstalar el mismo proyecto desde el mismo clon sigue permitido" test "$rc" = 0

# cron.d admite el valor entre comillas (cron manual): el guard no debe rechazar el mismo proyecto
sed 's/COMPOSE_PROJECT_NAME=theburyproject-prod /COMPOSE_PROJECT_NAME="theburyproject-prod" /' "$SB2/etc/cron.d/bury-backup-prod" >"$T/quoted.cron" && cp "$T/quoted.cron" "$SB2/etc/cron.d/bury-backup-prod"
BURY_SCHEDULE_SANDBOX="$SB2" bash "$HERE/install-schedule.sh" --install --user "$USER_NOW" --project theburyproject-prod >/dev/null 2>&1; rc=$?
check "cron con COMPOSE_PROJECT_NAME entre comillas: el mismo proyecto se reinstala sin falso rechazo" test "$rc" = 0

# assert_backup_dir_matches_project: docker falso (ps -aq db / inspect). Solo aborta ante discrepancia POSITIVA.
mkdir -p "$T/gbin" "$T/bkA/sql" "$T/bkB/sql"
cat >"$T/gbin/docker" <<'FAKE'
#!/bin/sh
case "$*" in
  *"ps -aq db"*) [ -n "${FAKE_CID-x}" ] && printf '%s\n' "${FAKE_CID-cid123}" ;;
  *"inspect -f"*"/backups/sql"*) printf '%s\n' "${FAKE_SRC-}" ;;
  *"inspect -f"*"compose.project"*) printf '%s\n' "${FAKE_PROJ-theburyproject}" ;;
  *) exit 0 ;;
esac
FAKE
chmod +x "$T/gbin/docker"
guard() { ( export PATH="$T/gbin:$PATH"; source "$HERE/lib.sh" >/dev/null 2>&1; BACKUP_DIR="$1"; assert_backup_dir_matches_project ) 2>&1; }
out=$(FAKE_SRC="$T/bkA/sql" guard "$T/bkA"); rc=$?
check "guard: db monta el sql/ de ESTE BACKUP_DIR -> pasa" test "$rc" = 0
out=$(FAKE_SRC="$T/bkB/sql" FAKE_PROJ=theburyproject guard "$T/bkA"); rc=$?
check "guard: db de OTRO BACKUP_DIR -> aborta (rc 8) nombrando proyecto y rutas" bash -c '[[ '"$rc"' == 8 && "$1" == *"PROYECTO COMPOSE EQUIVOCADO"* && "$1" == *"theburyproject"* && "$1" == *"bkB/sql"* ]]' _ "$out"
out=$(FAKE_CID="" guard "$T/bkA"); rc=$?
check "guard: sin contenedor db (servidor nuevo en recuperacion) -> no bloquea" test "$rc" = 0
out=$(FAKE_SRC="/run/desktop/mnt/host/c/x/sql" guard "$T/bkA"); rc=$?
check "guard: rutas de Docker Desktop (sin evidencia) -> no bloquea" test "$rc" = 0
out=$(FAKE_SRC="$T/bkB/sql" BURY_ALLOW_BACKUP_DIR_MISMATCH=1 guard "$T/bkA"); rc=$?
check "guard: BURY_ALLOW_BACKUP_DIR_MISMATCH=1 (staging explicito) -> no bloquea y avisa" bash -c '[[ '"$rc"' == 0 && "$1" == *"BURY_ALLOW_BACKUP_DIR_MISMATCH=1"* ]]' _ "$out"
if ln -sfn "$T/bkB" "$T/lnB" 2>/dev/null && [[ -L "$T/lnB" ]]; then
  out=$(FAKE_SRC="$T/bkB/sql" guard "$T/lnB"); rc=$?
  check "guard: BACKUP_DIR que es enlace al mismo directorio -> pasa" test "$rc" = 0
else echo "SKIP guard con enlace simbolico: el entorno no crea symlinks"; fi

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
BACKUP_DIR="$T/bk"; mkdir -p "$BACKUP_DIR/logs"; RUN_SQL=(x.bak); RUN_FILES=(); REMOTE_RETENTION_DAYS=0; BACKUP_OFFSITE_MARKER=""; DB_NAME=TheBuryProjectDb; MODE=full
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

# --- offsite(): centinela, subida pendiente y retencion remota (dc simulado) ----------------------------------------------
CALLS="$T/dc-calls"; : >"$CALLS"
FAILCODE="" FAILMSG=""; fail() { FAILCODE=$1; FAILMSG=$2; }
BACKUP_REMOTE="offsite:bucket/erp"; BACKUP_OFFSITE_MARKER=".bury-offsite-ok"
dc() { echo "$*" >>"$CALLS"; if [[ "$*" == *" lsf "* ]]; then return 0; fi; return 0; }   # lsf sin salida = centinela ausente
offsite
check "centinela ausente -> exit 6 y no se sube nada" bash -c '[[ "$1" == 6 && "$2" == *centinela* ]] && ! grep -q " copy " "$3"' _ "$FAILCODE" "$FAILMSG" "$CALLS"
FAILCODE=""; FAILMSG=""; : >"$CALLS"
dc() { echo "2026/01/01 CRITICAL: couldn't connect SSH: dial tcp: connection refused (password=$S1)" >&2; return 3; }   # remoto inaccesible
offsite
check "centinela: remoto inaccesible -> exit 6 con la CAUSA real y sin secreto" bash -c '[[ "$1" == 6 && "$2" == *"Causa:"*"connection refused"* && "$2" != *"$3"* ]]' _ "$FAILCODE" "$FAILMSG" "$S1"
FAILCODE=""; : >"$CALLS"
dc() { echo "$*" >>"$CALLS"; if [[ "$*" == *" lsf "* ]]; then echo ".bury-offsite-ok"; fi; return 0; }
rclone_verify() { return 0; }
remote_retention() { echo "remote_retention" >>"$CALLS"; }   # la logica real de borrado se prueba en test-retention-remote.sh
RUN_SQL=(TheBuryProjectDb_full_2026-01-01_000000Z.bak); DB_NAME=TheBuryProjectDb; REMOTE_RETENTION_DAYS=30; MODE=all
offsite
check "centinela presente -> sin fallo" test -z "$FAILCODE"
check "la subida incluye backups pendientes (full+log) y respeta --max-age" bash -c 'grep -q "_log_\*.trn" "$1" && grep -q "_full_\*.bak" "$1" && grep -q -- "--max-age 30d" "$1"' _ "$CALLS"
check "la retencion remota se ejecuta solo DESPUES de subir y verificar" bash -c 'grep -n " copy " "$1" | head -1 | cut -d: -f1 > "$1.c"; grep -n remote_retention "$1" | head -1 | cut -d: -f1 > "$1.r"; [[ $(cat "$1.c") -lt $(cat "$1.r") ]]' _ "$CALLS"
unset -f dc fail rclone_verify remote_retention
BACKUP_OFFSITE_MARKER=""; REMOTE_RETENTION_DAYS=0
source "$HERE/lib.sh" >/dev/null 2>&1; set +e   # restaura rclone_verify real

# --- rclone_errtext: la causa real no queda oculta por las lineas NOTICE ---------------------------------------------------
r=$(rclone_errtext "2026/01/01 NOTICE: Config file not found - using defaults
2026/01/01 NOTICE: sftpr: No host key validation is being performed
2026/01/01 CRITICAL: couldn't connect SSH: dial tcp: connection refused password=$S1")
check "rclone_errtext conserva la causa, descarta NOTICE y sanitiza" bash -c '[[ "$1" == *"connection refused"* && "$1" != *NOTICE* && "$1" != *"$2"* ]]' _ "$r" "$S1"
printf '2026/01/01 NOTICE: sftp: No host key validation is being performed\n' >"$T/hk.err"; : >"$T/hk.ok"
check "warn_hostkey_unpinned: avisa si rclone no valida la clave del servidor" bash -c '[[ "$(source "$1/lib.sh" >/dev/null 2>&1; warn_hostkey_unpinned "$2")" == *"SIN validar la clave del servidor"* ]]' _ "$HERE" "$T/hk.err"
check "warn_hostkey_unpinned: silencio cuando la clave esta fijada" bash -c '[[ -z "$(source "$1/lib.sh" >/dev/null 2>&1; warn_hostkey_unpinned "$2")" ]]' _ "$HERE" "$T/hk.ok"

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

# --- P-06: retencion remota por tipo, BACKUP_DIR, lock y recuperacion de app (archivo propio) ----------------------------------
out=$(bash "$HERE/test-retention-remote.sh" 2>&1); rc=$?
grep -v "^PASS" <<<"$out" | grep -E "^(FAIL|SKIP)" || true
check "test-retention-remote.sh ($(grep -c "^PASS" <<<"$out") casos)" test "$rc" = 0

# --- P-06/P-07: custodia de claves y restore desde la copia externa (archivos propios) ---------------------------------------
# --- retencion por capacidad y copia al disco extraible (cadenas sinteticas, sin docker; con --db usa SQL Server real: ver el archivo) ---
for t in test-check-custody.sh test-check-external-destination.sh test-recovery-package.sh test-exportar-custodia.sh test-offsite-restore-test.sh test-retention-capacity.sh test-verify-local-copy.sh; do
  out=$(bash "$HERE/$t" 2>&1); rc=$?
  grep -v "^PASS" <<<"$out" | grep -E "^(FAIL|SKIP)" || true
  check "$t ($(grep -c "^PASS" <<<"$out") casos)" test "$rc" = 0
done

echo; echo "resultado: $PASS OK, $FAIL fallos"; (( FAIL == 0 ))
