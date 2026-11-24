#!/usr/bin/env bash
# Tests de scripts/backup/verify-local-copy.sh (copia local cifrada, P-06) con datos 100% SINTETICOS, sin docker ni root (corre en CI).
#   bash scripts/backup/test-verify-local-copy.sh        exit 0 = todo OK
# El destino es un directorio comun (BACKUP_REMOVABLE_REQUIRE_MOUNT=false) y el contenedor backup-files se sustituye por el host (BK_CAP_HOSTFS=1).
set -uo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
export BURY_ALLOW_DIR_LOCK=1 BK_CAP_HOSTFS=1
T=$(mktemp -d); [[ -n "$T" && -d "$T" ]] || exit 1
trap 'rm -rf "$T"' EXIT
PASS=0 FAIL=0
ok()  { PASS=$((PASS+1)); echo "PASS $1"; }
bad() { FAIL=$((FAIL+1)); echo "FAIL $1"; }
check() { local n=$1; shift; if "$@"; then ok "$n"; else bad "$n"; fi; }
DB=TheBuryProjectDb
stamp() { date -u -d "@$(( $(date -u +%s) - $1 ))" +%Y-%m-%d_%H%M%SZ; }

check "bash -n verify-local-copy.sh / setup-local-disk-copy.sh" bash -c 'bash -n "$1/verify-local-copy.sh" && bash -n "$1/setup-local-disk-copy.sh"' _ "$HERE"
check "finales de linea LF" bash -c '! grep -q $'"'"'\r'"'"' "$1/verify-local-copy.sh" "$1/setup-local-disk-copy.sh" "$1/test-verify-local-copy.sh"' _ "$HERE"

mkbak() { # mkbak <raiz> <rel> <bytes>
  mkdir -p "$1/$(dirname "$2")"; head -c "$3" /dev/urandom >"$1/$2"; ( cd "$1/$(dirname "$2")" && sha256sum "$(basename "$2")" >"$(basename "$2").sha256" )
}
setup() { # setup <nombre> <antiguedad-del-full-en-segundos> -> BACKUP_DIR y destino listos
  BK="$T/$1/bk"; RM="$T/$1/rm"
  mkdir -p "$BK/sql" "$BK/files" "$BK/logs" "$RM/bury-backup/sql" "$RM/bury-backup/files"; echo marca >"$BK/.bury-backup-root"; touch "$RM/.bury-backup-removable"
  mkbak "$RM/bury-backup" "sql/${DB}_full_$(stamp "$2").bak" 20000
  mkbak "$RM/bury-backup" "sql/${DB}_log_$(stamp $(( $2 - 900 ))).trn" 3000
  mkbak "$RM/bury-backup" "files/files_$(stamp $(( $2 - 30 ))).tar.gz" 5000
}
run() { # run [VAR=valor ...] -> rc; deja el estado en $BK/monitor-status/removable-verify.json
  env BACKUP_DIR="$BK" BACKUP_REMOVABLE_DIR="$RM" BACKUP_REMOVABLE_REQUIRE_MOUNT=false BACKUP_REMOVABLE_MIN_FREE_GB=0.001 COMPOSE_ENV_FILES=/dev/null "$@" bash "$HERE/verify-local-copy.sh" --quiet >"$T/out.txt" 2>&1
}
status_has() { grep -q "$1" "$BK/monitor-status/removable-verify.json"; }

setup ok 3600
run; rc=$?
check "copia sana y al dia -> rc 0" test "$rc" = 0
check "estado del monitor: exit 0, verified true" bash -c 'grep -q "\"exit\":0" "$1" && grep -q "\"verified\":true" "$1"' _ "$BK/monitor-status/removable-verify.json"

setup corrupt 3600
f=$(ls "$RM"/bury-backup/sql/*_full_*.bak); printf 'Z' | dd of="$f" bs=1 seek=100 conv=notrunc 2>/dev/null
run; rc=$?
check "1 byte alterado en el .bak -> rc 13" test "$rc" = 13
check "estado: exit 13, verified false, menciona integridad y el archivo" bash -c 'grep -q "\"exit\":13" "$1" && grep -q "\"verified\":false" "$1" && grep -q "integridad" "$1" && grep -q "_full_" "$1"' _ "$BK/monitor-status/removable-verify.json"

setup trunc 3600
f=$(ls "$RM"/bury-backup/sql/*_log_*.trn); truncate -s 1000 "$f"
run; rc=$?; check "archivo truncado -> rc 13" test "$rc" = 13

setup nosha 3600
rm -f "$RM"/bury-backup/files/*.sha256
run; rc=$?; check "dato sin su .sha256 -> rc 13" test "$rc" = 13

setup old $((5*86400))
run; rc=$?
check "full mas nuevo de hace 5 dias (max 48 h) -> rc 13 'no esta al dia'" bash -c '[[ "$1" == 13 ]] && grep -q "no esta al dia" "$2"' _ "$rc" "$BK/monitor-status/removable-verify.json"
run BACKUP_REMOVABLE_MAX_AGE_HOURS=200; rc=$?; check "con BACKUP_REMOVABLE_MAX_AGE_HOURS=200 la misma copia pasa" test "$rc" = 0

setup nomark 3600
rm -f "$RM/.bury-backup-removable"
run; rc=$?
check "sin archivo-marca (disco no montado / equivocado) -> rc 13 'NO disponible'" bash -c '[[ "$1" == 13 ]] && grep -q "NO disponible" "$2"' _ "$rc" "$BK/monitor-status/removable-verify.json"

setup nodir 3600
rm -rf "$RM"
run; rc=$?; check "destino inexistente (disco ausente) -> rc 13" test "$rc" = 13

setup space 3600
run BACKUP_REMOVABLE_MIN_FREE_GB=900000; rc=$?
check "espacio libre bajo el minimo configurado -> rc 13 'espacio insuficiente'" bash -c '[[ "$1" == 13 ]] && grep -q "espacio insuficiente" "$2"' _ "$rc" "$BK/monitor-status/removable-verify.json"

setup mapper 3600
run BACKUP_REMOVABLE_MAPPER=no-existe-xyz; rc=$?
check "montaje que no sale de /dev/mapper/<nombre> -> rc 13 'cifrado inactivo'" bash -c '[[ "$1" == 13 ]] && grep -q "cifrado inactivo" "$2"' _ "$rc" "$BK/monitor-status/removable-verify.json"

setup empty 3600
rm -f "$RM"/bury-backup/sql/* "$RM"/bury-backup/files/*
run; rc=$?; check "copia sin ningun backup -> rc 13" test "$rc" = 13

setup recov 3600
f=$(ls "$RM"/bury-backup/sql/*_full_*.bak); cp "$f" "$T/good.bak"; printf 'Z' | dd of="$f" bs=1 seek=7 conv=notrunc 2>/dev/null
run; r1=$?; cp "$T/good.bak" "$f"; run; r2=$?
check "tras corregir la copia, la siguiente verificacion vuelve a rc 0 y conserva last_success" bash -c '[[ "$1" == 13 && "$2" == 0 ]] && grep -q "\"verified\":true" "$3"' _ "$r1" "$r2" "$BK/monitor-status/removable-verify.json"

# el setup no debe poder ejecutarse sin root ni sobre un volumen que no es punto de montaje (no cambia nada)
check "setup --check sin root se niega (exit 8) y no crea nada" bash -c 'out=$(bash "$1/setup-local-disk-copy.sh" --check --owner "$(id -un)" --host-mount "$2/x" --mount "$2/y" 2>&1); rc=$?; if [[ $EUID == 0 ]]; then [[ $rc == 8 ]] || exit 1; else [[ $rc == 8 && "$out" == *"requiere root"* ]] || exit 1; fi; [[ ! -e "$2/x" && ! -e "$2/y" ]]' _ "$HERE" "$T"

# dos copias independientes (--name/--project): entradas invalidas se rechazan (exit 1) antes de cualquier otra comprobacion; no cambia nada
for bad in "--name Prod" "--name x" "--name tbp_respaldo" "--name tbp-respaldo-prod --project Mayus" "--name tbp-respaldo-prod --project a/b"; do
  check "setup rechaza argumentos invalidos ($bad) con exit 1" bash -c 'bash "$1/setup-local-disk-copy.sh" --check --owner "$(id -un)" $3 >/dev/null 2>&1; [[ $? == 1 ]]' _ "$HERE" "$T" "$bad"
done
check "setup con --name/--project validos llega a la comprobacion de root (exit 8 sin root), sin tocar nada" bash -c 'if [[ $EUID == 0 ]]; then exit 0; fi; out=$(bash "$1/setup-local-disk-copy.sh" --check --owner "$(id -un)" --name tbp-respaldo-prod --project theburyproject-prod --host-mount "$2/x" --mount "$2/y" 2>&1); [[ $? == 8 && "$out" == *"requiere root"* && ! -e "$2/x" ]]' _ "$HERE" "$T"

echo; echo "RESULTADO: PASS=$PASS FAIL=$FAIL"
[[ "$FAIL" == 0 ]]
