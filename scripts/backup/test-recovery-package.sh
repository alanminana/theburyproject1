#!/usr/bin/env bash
# Tests de scripts/backup/recovery-package.sh (P-06). Secretos SINTETICOS en un directorio temporal; sin docker. Linux/WSL (gpg 2.x, tmpfs).
#   bash scripts/backup/test-recovery-package.sh
set -uo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SUT="$HERE/recovery-package.sh"
T=$(mktemp -d); trap 'rm -rf "$T"' EXIT
PASS=0 FAIL=0
ok()  { PASS=$((PASS+1)); echo "PASS $1"; }
bad() { FAIL=$((FAIL+1)); echo "FAIL $1"; }
check() { local n=$1; shift; if "$@"; then ok "$n"; else bad "$n"; fi; }
command -v gpg >/dev/null 2>&1 || { echo "SKIP: sin gpg"; exit 0; }
[[ "$(stat -f -c %T /dev/shm 2>/dev/null)" == tmpfs ]] || { echo "SKIP: sin /dev/shm tmpfs"; exit 0; }

check "bash -n recovery-package.sh" bash -n "$SUT"

SECRET_SA="SA-sintetico-Zq81vXk-NOREAL"; SECRET_RC="rclone-obscure-sintetico-NOREAL"
mkdir -p "$T/repo" "$T/bk" "$T/out" "$T/keys" "$T/tmpdisk"
printf 'MSSQL_SA_PASSWORD=%s\nADMIN_PASSWORD=otro-NOREAL\n' "$SECRET_SA" >"$T/repo/.env"
printf 'RCLONE_CONFIG_SECURE_PASSWORD=%s\n' "$SECRET_RC" >"$T/repo/backup-offsite.env"
: >"$T/bk/.bury-backup-root"
P1="$T/keys/pass1"; printf 'frase-de-prueba-larga-1' >"$P1"; chmod 600 "$P1"
P2="$T/keys/pass2"; printf 'otra-frase-distinta-22' >"$P2"; chmod 600 "$P2"
run() { TMPDIR="$T/tmpdisk" COMPOSE_ENV_FILES="${ENVS:-$T/repo/.env}" BURY_OFFSITE_ENV_FILE="${OFFS:-$T/repo/backup-offsite.env}" BACKUP_DIR="$T/bk" bash "$SUT" "$@" >"$T/o" 2>&1; echo $?; }

rc=$(run create --out "$T/out" --passphrase-file "$P1"); check "create -> exit 0" test "$rc" = 0
PKG=$(ls "$T/out"/bury-recovery_*.gpg 2>/dev/null | head -1)
check "create: genera .gpg y .sha256 con modo 600" bash -c '[[ -f "$1" && -f "$1.sha256" && "$(stat -c %a "$1")" = 600 && "$(stat -c %a "$1.sha256")" = 600 ]]' _ "$PKG"
check "el paquete no contiene los secretos en claro" bash -c '! grep -aq -e "$1" -e "$2" "$3"' _ "$SECRET_SA" "$SECRET_RC" "$PKG"
check "la salida de create no imprime secretos ni la passphrase" bash -c '! grep -q -e "$1" -e "$2" -e frase-de-prueba "$3"' _ "$SECRET_SA" "$SECRET_RC" "$T/o"
check "create no dejo temporales con secretos en TMPDIR (disco)" bash -c '[[ -z "$(ls -A "$1")" ]]' _ "$T/tmpdisk"
check "create no dejo restos en /dev/shm" bash -c '! ls /dev/shm | grep -q "^bury-rp\."'

rc=$(run verify "$PKG" --passphrase-file "$P1"); check "verify con la passphrase correcta -> exit 0" test "$rc" = 0
check "verify lista nombres (MANIFEST, .env, backup-offsite.env)" bash -c 'grep -q MANIFEST.txt "$1" && grep -q "\.env" "$1" && grep -q backup-offsite.env "$1"' _ "$T/o"
check "verify no imprime contenidos" bash -c '! grep -q -e "$1" -e "$2" "$3"' _ "$SECRET_SA" "$SECRET_RC" "$T/o"
rc=$(run verify "$PKG" --passphrase-file "$P2"); check "verify con passphrase incorrecta -> exit 3" test "$rc" = 3

rc=$(run extract "$PKG" --to "$T/rest" --passphrase-file "$P1"); check "extract -> exit 0" test "$rc" = 0
check "extract: restaura los .env identicos, directorio 700 y archivos 600" bash -c 'cmp -s "$1/.env" "$2/.env" && cmp -s "$1/backup-offsite.env" "$2/backup-offsite.env" && [[ "$(stat -c %a "$1")" = 700 && "$(stat -c %a "$1/.env")" = 600 ]]' _ "$T/rest" "$T/repo"
rc=$(run extract "$PKG" --to "$T/rest" --passphrase-file "$P1"); check "extract sobre un directorio existente se rechaza (exit 1)" test "$rc" = 1
rc=$(run extract "$PKG" --to "$T/rest2" --passphrase-file "$P2"); check "extract con passphrase incorrecta -> exit 3 y no deja directorio" bash -c '[[ "$1" = 3 && ! -e "$2" ]]' _ "$rc" "$T/rest2"

# autenticidad: un byte alterado se detecta por el MDC de GnuPG AUNQUE no exista (o se reemplace) el .sha256
cp "$PKG" "$T/alt.gpg"; printf 'X' | dd of="$T/alt.gpg" bs=1 seek=120 conv=notrunc 2>/dev/null
rc=$(run verify "$T/alt.gpg" --passphrase-file "$P1"); check "paquete alterado SIN sidecar -> exit 3 (autenticado, no solo sha256)" test "$rc" = 3
sha256sum "$T/alt.gpg" | sed "s#$T/##" >"$T/alt.gpg.sha256"
rc=$(run verify "$T/alt.gpg" --passphrase-file "$P1"); check "paquete alterado con sidecar REEMPLAZADO -> exit 3 (el MDC lo detecta)" test "$rc" = 3
cp "$PKG" "$T/copia.gpg"; cp "$PKG.sha256" "$T/copia.gpg.sha256"; sed -i "s/$(basename "$PKG")/copia.gpg/" "$T/copia.gpg.sha256"
rc=$(run verify "$T/copia.gpg" --passphrase-file "$P1"); check "copia fiel con su sidecar -> exit 0" test "$rc" = 0

# paquete malicioso: enlace simbolico dentro del tar. No se extrae, y no se cambia el modo del archivo apuntado.
VICT="$T/victima"; echo dato >"$VICT"; chmod 644 "$VICT"
mkdir -p "$T/evil/stage"; echo "bury-recovery-package v2" >"$T/evil/stage/MANIFEST.txt"; ln -s "$VICT" "$T/evil/stage/enlace"
tar -C "$T/evil/stage" -czf "$T/evil/p.tgz" MANIFEST.txt enlace
GNUPGHOME="$T/evil/gh"; mkdir -m 700 "$GNUPGHOME"; export GNUPGHOME
gpg --batch --yes --quiet --no-tty --pinentry-mode loopback --passphrase-file "$P1" --symmetric --cipher-algo AES256 -o "$T/evil/malo.gpg" "$T/evil/p.tgz" 2>/dev/null
unset GNUPGHOME
rc=$(run extract "$T/evil/malo.gpg" --to "$T/rest3" --passphrase-file "$P1")
check "paquete con enlace simbolico -> exit 3, sin directorio y sin tocar el archivo apuntado" bash -c '[[ "$1" = 3 && ! -e "$2" && "$(stat -c %a "$3")" = 644 ]]' _ "$rc" "$T/rest3" "$VICT"
mkdir -p "$T/evil2/stage"; echo m >"$T/evil2/stage/MANIFEST.txt"; mkdir "$T/evil2/stage/sub"; echo x >"$T/evil2/stage/sub/f"
tar -C "$T/evil2/stage" -czf "$T/evil2/p.tgz" MANIFEST.txt sub/f
GNUPGHOME="$T/evil2/gh"; mkdir -m 700 "$GNUPGHOME"; export GNUPGHOME
gpg --batch --yes --quiet --no-tty --pinentry-mode loopback --passphrase-file "$P1" --symmetric --cipher-algo AES256 -o "$T/evil2/malo.gpg" "$T/evil2/p.tgz" 2>/dev/null
unset GNUPGHOME
rc=$(run extract "$T/evil2/malo.gpg" --to "$T/rest4" --passphrase-file "$P1"); check "paquete con subdirectorios/rutas -> exit 3" bash -c '[[ "$1" = 3 && ! -e "$2" ]]' _ "$rc" "$T/rest4"

# colision de nombres: dos .env con el mismo basename se rechazan (no se pierde uno en silencio)
mkdir -p "$T/otro"; printf 'OTRA=1\n' >"$T/otro/.env"
rc=$(ENVS="$T/repo/.env,$T/otro/.env" run create --out "$T/outc" --passphrase-file "$P1"); check "dos .env con el mismo nombre -> exit 1 (sin paquete)" bash -c '[[ "$1" = 1 && -z "$(ls "$2"/*.gpg 2>/dev/null)" ]]' _ "$rc" "$T/outc"

# reglas de ubicacion y passphrase
rc=$(run create --out "$T/bk/dentro" --passphrase-file "$P1"); check "--out dentro de BACKUP_DIR se rechaza (exit 1)" test "$rc" = 1
cp "$P1" "$T/out/pass-junto"; chmod 600 "$T/out/pass-junto"
rc=$(run create --out "$T/out" --passphrase-file "$T/out/pass-junto"); check "passphrase en el mismo directorio que el paquete se rechaza (exit 1)" test "$rc" = 1
printf 'corta' >"$T/keys/corta"; chmod 600 "$T/keys/corta"
rc=$(run create --out "$T/out2" --passphrase-file "$T/keys/corta"); check "passphrase corta (<12) se rechaza (exit 1)" test "$rc" = 1
cp "$P1" "$T/keys/abierta"; chmod 644 "$T/keys/abierta"
rc=$(run create --out "$T/out3" --passphrase-file "$T/keys/abierta"); check "archivo de passphrase legible por otros se rechaza (exit 1)" test "$rc" = 1
check "sin terminal ni --passphrase-file se rechaza (exit 1)" bash -c '[[ "$(COMPOSE_ENV_FILES="$1" BACKUP_DIR="$2" bash "$3" create --out "$4" </dev/null >/dev/null 2>&1; echo $?)" = 1 ]]' _ "$T/repo/.env" "$T/bk" "$SUT" "$T/out4"
rc=$(ENVS="$T/nada/.env" OFFS="$T/nada/off.env" run create --out "$T/out5" --passphrase-file "$P1"); check "sin .env que empaquetar -> exit 1" test "$rc" = 1
check "no quedaron restos en /dev/shm tras los casos de error" bash -c '! ls /dev/shm | grep -q "^bury-rp\."'

echo
echo "resultado test-recovery-package: $PASS OK, $FAIL fallos"
(( FAIL == 0 ))
