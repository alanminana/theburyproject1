#!/usr/bin/env bash
# Tests de scripts/backup/recovery-package.sh (P-06). Secretos SINTETICOS en un directorio temporal; sin docker. Linux/WSL.
#   bash scripts/backup/test-recovery-package.sh
set -uo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SUT="$HERE/recovery-package.sh"
T=$(mktemp -d); trap 'rm -rf "$T"' EXIT
PASS=0 FAIL=0
ok()  { PASS=$((PASS+1)); echo "PASS $1"; }
bad() { FAIL=$((FAIL+1)); echo "FAIL $1"; }
check() { local n=$1; shift; if "$@"; then ok "$n"; else bad "$n"; fi; }
command -v openssl >/dev/null 2>&1 || { echo "SKIP: sin openssl"; exit 0; }

check "bash -n recovery-package.sh" bash -n "$SUT"

SECRET_SA="SA-sintetico-Zq81vXk-NOREAL"; SECRET_RC="rclone-obscure-sintetico-NOREAL"
mkdir -p "$T/repo" "$T/bk" "$T/out" "$T/keys"
printf 'MSSQL_SA_PASSWORD=%s\nADMIN_PASSWORD=otro-NOREAL\n' "$SECRET_SA" >"$T/repo/.env"
printf 'RCLONE_CONFIG_SECURE_PASSWORD=%s\n' "$SECRET_RC" >"$T/repo/backup-offsite.env"
: >"$T/bk/.bury-backup-root"
P1="$T/keys/pass1"; printf 'frase-de-prueba-larga-1' >"$P1"; chmod 600 "$P1"
P2="$T/keys/pass2"; printf 'otra-frase-distinta-22' >"$P2"; chmod 600 "$P2"
run() { COMPOSE_ENV_FILES="$T/repo/.env" BURY_OFFSITE_ENV_FILE="$T/repo/backup-offsite.env" BACKUP_DIR="$T/bk" bash "$SUT" "$@" >"$T/o" 2>&1; echo $?; }

rc=$(run create --out "$T/out" --passphrase-file "$P1"); check "create -> exit 0" test "$rc" = 0
PKG=$(ls "$T/out"/bury-recovery_*.enc 2>/dev/null | head -1)
check "create: genera .enc y .sha256 con modo 600" bash -c '[[ -f "$1" && -f "$1.sha256" && "$(stat -c %a "$1")" = 600 && "$(stat -c %a "$1.sha256")" = 600 ]]' _ "$PKG"
check "el .enc no contiene los secretos en claro" bash -c '! grep -aq -e "$1" -e "$2" "$3"' _ "$SECRET_SA" "$SECRET_RC" "$PKG"
check "la salida de create no imprime secretos ni la passphrase" bash -c '! grep -q -e "$1" -e "$2" -e frase-de-prueba "$3"' _ "$SECRET_SA" "$SECRET_RC" "$T/o"

rc=$(run verify "$PKG" --passphrase-file "$P1"); check "verify con la passphrase correcta -> exit 0" test "$rc" = 0
check "verify lista nombres (MANIFEST, .env, backup-offsite.env)" bash -c 'grep -q MANIFEST.txt "$1" && grep -q "\.env" "$1" && grep -q backup-offsite.env "$1"' _ "$T/o"
check "verify no imprime contenidos" bash -c '! grep -q -e "$1" -e "$2" "$3"' _ "$SECRET_SA" "$SECRET_RC" "$T/o"
rc=$(run verify "$PKG" --passphrase-file "$P2"); check "verify con passphrase incorrecta -> exit 3" test "$rc" = 3

rc=$(run extract "$PKG" --to "$T/rest" --passphrase-file "$P1"); check "extract -> exit 0" test "$rc" = 0
check "extract: restaura los .env identicos y con modo 600/700" bash -c 'cmp -s "$1/.env" "$2/.env" && cmp -s "$1/backup-offsite.env" "$2/backup-offsite.env" && [[ "$(stat -c %a "$1")" = 700 && "$(stat -c %a "$1/.env")" = 600 ]]' _ "$T/rest" "$T/repo"
rc=$(run extract "$PKG" --to "$T/rest" --passphrase-file "$P1"); check "extract sobre un directorio existente se rechaza (exit 1)" test "$rc" = 1
rc=$(run extract "$PKG" --to "$T/rest2" --passphrase-file "$P2"); check "extract con passphrase incorrecta -> exit 3 y no deja directorio" bash -c '[[ "$1" = 3 && ! -e "$2" ]]' _ "$rc" "$T/rest2"

# integridad: un byte alterado se detecta por el sha256
cp "$PKG" "$T/alt.enc"; cp "$PKG.sha256" "$T/alt.enc.sha256"; sed -i "s/$(basename "$PKG")/alt.enc/" "$T/alt.enc.sha256"
printf 'X' | dd of="$T/alt.enc" bs=1 seek=100 conv=notrunc 2>/dev/null
rc=$(run verify "$T/alt.enc" --passphrase-file "$P1"); check "paquete alterado -> exit 3 (sha256)" test "$rc" = 3

# reglas de ubicacion y passphrase
rc=$(run create --out "$T/bk/dentro" --passphrase-file "$P1"); check "--out dentro de BACKUP_DIR se rechaza (exit 1)" test "$rc" = 1
cp "$P1" "$T/out/pass-junto"; chmod 600 "$T/out/pass-junto"
rc=$(run create --out "$T/out" --passphrase-file "$T/out/pass-junto"); check "passphrase en el mismo directorio que el paquete se rechaza (exit 1)" test "$rc" = 1
printf 'corta' >"$T/keys/corta"; chmod 600 "$T/keys/corta"
rc=$(run create --out "$T/out2" --passphrase-file "$T/keys/corta"); check "passphrase corta (<12) se rechaza (exit 1)" test "$rc" = 1
cp "$P1" "$T/keys/abierta"; chmod 644 "$T/keys/abierta"
rc=$(run create --out "$T/out3" --passphrase-file "$T/keys/abierta"); check "archivo de passphrase legible por otros se rechaza (exit 1)" test "$rc" = 1
check "sin terminal ni --passphrase-file se rechaza (exit 1)" bash -c '[[ "$(COMPOSE_ENV_FILES="$1" BACKUP_DIR="$2" bash "$3" create --out "$4" </dev/null >/dev/null 2>&1; echo $?)" = 1 ]]' _ "$T/repo/.env" "$T/bk" "$SUT" "$T/out4"
rc=$(COMPOSE_ENV_FILES="$T/nada/.env" BACKUP_DIR="$T/bk" bash "$SUT" create --out "$T/out5" --passphrase-file "$P1" >/dev/null 2>&1; echo $?); check "sin .env que empaquetar -> exit 1" test "$rc" = 1

echo
echo "resultado test-recovery-package: $PASS OK, $FAIL fallos"
(( FAIL == 0 ))
