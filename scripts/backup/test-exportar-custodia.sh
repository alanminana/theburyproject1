#!/usr/bin/env bash
# Tests de scripts/backup/exportar-custodia.sh (B5). Claves SINTETICAS en un directorio temporal (BURY_KEYDIR); sin docker ni root. Linux/WSL (gpg 2.x, tmpfs).
#   bash scripts/backup/test-exportar-custodia.sh
set -uo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"; SUT="$HERE/exportar-custodia.sh"
T=$(mktemp -d); trap 'rm -rf "$T"' EXIT
PASS=0 FAIL=0
ok()  { PASS=$((PASS+1)); echo "PASS $1"; }
bad() { FAIL=$((FAIL+1)); echo "FAIL $1"; }
check() { local n=$1; shift; if "$@"; then ok "$n"; else bad "$n"; fi; }
command -v gpg >/dev/null 2>&1 || { echo "SKIP: sin gpg"; exit 0; }
[[ "$(stat -f -c %T /dev/shm 2>/dev/null)" == tmpfs ]] || { echo "SKIP: sin /dev/shm tmpfs"; exit 0; }
check "bash -n exportar-custodia.sh" bash -n "$SUT"
mkdir -p "$T/keys" "$T/out" "$T/pw" "$T/bk"; K="$T/keys"
head -c 64 /dev/urandom >"$K/local-disk-bury-prov.key"; head -c 4096 /dev/urandom >"$K/local-disk-bury-prov.luks-header"
head -c 64 /dev/urandom >"$K/local-disk-bury-notebook.key"; head -c 4096 /dev/urandom >"$K/local-disk-bury-notebook.luks-header"
chmod 400 "$K"/*
P1="$T/pw/pass1"; printf 'frase-de-prueba-larga-1' >"$P1"; chmod 600 "$P1"; P2="$T/pw/pass2"; printf 'otra-frase-distinta-22' >"$P2"; chmod 600 "$P2"
run() { BACKUP_DIR="${TBK-$T/bk}" BURY_KEYDIR="${TKD-$K}" bash "$SUT" "$@" >"$T/o" 2>&1; echo $?; }
# como bajo `sudo`: entorno vacio (sudo descarta BACKUP_DIR); las rutas solo pueden venir de --env-file
runenv() { env -i PATH="$PATH" BURY_KEYDIR="$K" bash "$SUT" "$@" >"$T/o" 2>&1; echo $?; }

rc=$(run create --out "$T/out" --passphrase-file "$P1"); check "create (todas las copias) -> exit 0" test "$rc" = 0
PKG=$(ls "$T/out"/bury-custodia-luks_*.gpg 2>/dev/null | head -1)
check "create: .gpg y .sha256 con modo 600" bash -c '[[ "$(stat -c %a "$1")" = 600 && "$(stat -c %a "$1.sha256")" = 600 ]]' _ "$PKG"
check "el paquete no contiene las claves en claro (32 bytes de la clave, en hex, no aparecen)" bash -c 'h=$(xxd -p -l 32 "$1" | tr -d "
"); [[ ${#h} = 64 ]] && ! xxd -p "$2" | tr -d "
" | grep -q "$h"' _ "$K/local-disk-bury-prov.key" "$PKG"
check "control positivo: la misma busqueda SI encuentra la clave en el archivo de claves" bash -c 'h=$(xxd -p -l 32 "$1" | tr -d "
"); xxd -p "$1" | tr -d "
" | grep -q "$h"' _ "$K/local-disk-bury-prov.key"
check "la salida no imprime la passphrase" bash -c '! grep -q frase-de-prueba "$1"' _ "$T/o"
rc=$(run verify "$PKG" --passphrase-file "$P1"); check "verify con la passphrase correcta -> exit 0" test "$rc" = 0
check "verify lista las 4 claves y el MANIFEST (solo nombres)" bash -c 'grep -q MANIFEST.txt "$1" && grep -q local-disk-bury-prov.key "$1" && grep -q local-disk-bury-notebook.luks-header "$1"' _ "$T/o"
rc=$(run verify "$PKG" --passphrase-file "$P2"); check "verify con passphrase incorrecta -> exit 3" test "$rc" = 3
rc=$(run create --out "$T/out2" --name bury-prov --passphrase-file "$P1"); check "create con --out inexistente -> exit 1" test "$rc" = 1
mkdir "$T/out2"; rc=$(run create --out "$T/out2" --name bury-prov --passphrase-file "$P1"); check "create --name bury-prov -> exit 0" test "$rc" = 0
rc=$(run verify "$(ls "$T/out2"/*.gpg | head -1)" --passphrase-file "$P1"); check "...contiene solo esa copia" bash -c '[[ "$1" = 0 ]] && grep -q bury-prov "$2" && ! grep -q notebook "$2"' _ "$rc" "$T/o"
rc=$(run create --out "$T/pw" --passphrase-file "$P1"); check "salida en el directorio de la passphrase -> rechazada (exit 1)" test "$rc" = 1
rc=$(BACKUP_DIR="$T/bk" run create --out "$T/bk" --passphrase-file "$P1"); check "salida dentro de BACKUP_DIR -> rechazada (exit 1)" test "$rc" = 1
rc=$(run create --out "$T/out" --name no-existe --passphrase-file "$P1"); check "clave inexistente -> exit 8" test "$rc" = 8
# separacion de los respaldos bajo sudo (entorno vacio): rutas desde --env-file, fallando CERRADO si no se pueden determinar
printf 'BACKUP_DIR=%s
BACKUP_REMOVABLE_DIR=%s
' "$T/bk" "$T/rem" >"$T/envf"; mkdir -p "$T/rem/sub"
rc=$(runenv create --out "$T/bk" --passphrase-file "$P1" --env-file "$T/envf"); check "sudo (sin entorno) + --env-file: salida dentro de BACKUP_DIR -> rechazada (exit 1)" test "$rc" = 1
check "...por estar dentro de BACKUP_DIR (no por otra causa)" grep -q "la clave no puede vivir junto a los respaldos" "$T/o"
rc=$(runenv create --out "$T/rem/sub" --passphrase-file "$P1" --env-file "$T/envf"); check "sudo + --env-file: salida dentro de BACKUP_REMOVABLE_DIR -> rechazada (exit 1)" test "$rc" = 1
check "...por estar dentro de BACKUP_REMOVABLE_DIR (no por otra causa)" grep -q "la clave no puede vivir junto a los respaldos" "$T/o"
rc=$(runenv create --out "$T/out" --passphrase-file "$P1" --env-file "$T/envf"); check "sudo + --env-file: salida valida -> exit 0" test "$rc" = 0
rc=$(runenv create --out "$T/out" --passphrase-file "$P1" --env-file "$T/no-existe.env"); check "rutas indeterminables (sin entorno ni .env) -> SE NIEGA (exit 1), no omite el control" test "$rc" = 1
check "...y el motivo es que no se pudo determinar BACKUP_DIR" grep -q "no se pudo determinar BACKUP_DIR" "$T/o"
printf 'BACKUP_DIR=%s
' "$T/no-existe" >"$T/envf2"; rc=$(runenv create --out "$T/out" --passphrase-file "$P1" --env-file "$T/envf2"); check "BACKUP_DIR inexistente -> SE NIEGA (exit 1)" test "$rc" = 1
# publicacion: si install falla (disco lleno / solo lectura), create NO puede terminar en exito ni dejar restos
mkdir -p "$T/fake" "$T/out3"; printf '#!/bin/sh
exit 1
' >"$T/fake/install"; chmod +x "$T/fake/install"
rc=$(PATH="$T/fake:$PATH" run create --out "$T/out3" --passphrase-file "$P1"); check "install falla -> create falla (exit != 0)" test "$rc" != 0
check "...sin mensaje de exito ni archivos parciales" bash -c '! grep -q "paquete creado" "$1" && [[ -z "$(ls -A "$2")" ]]' _ "$T/o" "$T/out3"
# staging: si copiar una clave falla o la clave esta vacia, NO se crea un paquete incompleto
mkdir -p "$T/out4"; printf '#!/bin/sh
exit 1
' >"$T/fake/cp"; chmod +x "$T/fake/cp"
rc=$(PATH="$T/fake:$PATH" run create --out "$T/out4" --passphrase-file "$P1"); check "cp falla al copiar una clave -> create falla (exit 8)" test "$rc" = 8
check "...sin paquete ni mensaje de exito" bash -c '! grep -q "paquete creado" "$1" && [[ -z "$(ls -A "$2")" ]] && grep -q "no se pudo copiar completo" "$1"' _ "$T/o" "$T/out4"
rm -f "$T/fake/cp"; mkdir -p "$T/keys2" "$T/out5"; : >"$T/keys2/local-disk-x.key"; head -c 100 /dev/urandom >"$T/keys2/local-disk-x.luks-header"
rc=$(TKD="$T/keys2" run create --out "$T/out5" --passphrase-file "$P1"); check "clave vacia -> create falla (exit 8) y no deja paquete" bash -c '[[ "$1" = 8 && -z "$(ls -A "$2")" ]]' _ "$rc" "$T/out5"

chmod 644 "$P1"; rc=$(run verify "$PKG" --passphrase-file "$P1"); check "passphrase con modo 644 -> rechazada (exit 1)" test "$rc" = 1; chmod 600 "$P1"

# extract
rc=$(run extract "$PKG" --to "$T/rest" --passphrase-file "$P1"); check "extract -> exit 0" test "$rc" = 0
check "extract: claves identicas, directorio 700 y archivos 600" bash -c 'cmp -s "$1/local-disk-bury-prov.key" "$2/local-disk-bury-prov.key" && cmp -s "$1/local-disk-bury-notebook.luks-header" "$2/local-disk-bury-notebook.luks-header" && [[ "$(stat -c %a "$2")" = 700 && "$(stat -c %a "$2/local-disk-bury-prov.key")" = 600 ]]' _ "$K" "$T/rest"
rc=$(run extract "$PKG" --to "$T/rest" --passphrase-file "$P1"); check "extract sobre un directorio existente -> rechazado (exit 1)" test "$rc" = 1

# autenticidad: alteracion DETERMINISTA (se invierte el byte: siempre cambia) y GnuPG la rechaza
cp "$PKG" "$T/alt.gpg"; orig=$(od -An -tu1 -j120 -N1 "$T/alt.gpg" | tr -d ' ')
printf "\x$(printf '%02x' $((orig ^ 255)))" | dd of="$T/alt.gpg" bs=1 seek=120 conv=notrunc 2>/dev/null
check "la alteracion cambio realmente el archivo" bash -c '! cmp -s "$1" "$2"' _ "$PKG" "$T/alt.gpg"
rc=$(run verify "$T/alt.gpg" --passphrase-file "$P1"); check "paquete alterado -> exit 3" test "$rc" = 3
check "...por el descifrado/autenticacion de GnuPG" grep -q "no se pudo descifrar/autenticar" "$T/o"

# paquete malicioso: enlace simbolico dentro del tar
mkdir -p "$T/evil/stage"; echo "bury-custodia-luks v1" >"$T/evil/stage/MANIFEST.txt"; ln -s /etc/passwd "$T/evil/stage/enlace"
tar -C "$T/evil/stage" -czf "$T/evil/p.tgz" MANIFEST.txt enlace; GH="$T/evil/gh"; mkdir -m 700 "$GH"
GNUPGHOME="$GH" gpg --batch --yes --quiet --no-tty --pinentry-mode loopback --passphrase-file "$P1" --symmetric --cipher-algo AES256 -o "$T/evil/malo.gpg" "$T/evil/p.tgz" 2>/dev/null
rc=$(run extract "$T/evil/malo.gpg" --to "$T/rest3" --passphrase-file "$P1"); check "paquete con enlace simbolico -> exit 3 y sin directorio" bash -c '[[ "$1" = 3 && ! -e "$2" ]]' _ "$rc" "$T/rest3"
check "no deja temporales en /dev/shm" bash -c '! ls /dev/shm | grep -q "^bury-xc\."'
echo; echo "resultado: PASS=$PASS FAIL=$FAIL"; [ "$FAIL" = 0 ]
