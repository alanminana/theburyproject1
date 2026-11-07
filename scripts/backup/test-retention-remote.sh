#!/usr/bin/env bash
# Tests focalizados de P-06: retencion remota por tipo, BACKUP_DIR, lock y recuperacion de `app` tras `app-stop`.
# Sin docker, rclone, cron ni root reales: todo en un temp dir, con un remoto SIMULADO (un directorio) y datos sinteticos.
#   bash scripts/backup/test-retention-remote.sh        (exit 0 = todo OK; lo invoca test-hardening.sh, que corre en CI)
set -uo pipefail
export BURY_ALLOW_DIR_LOCK=1   # el lock por directorio (sin flock) solo existe para desarrollo/tests; ver acquire_lock
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
T=$(mktemp -d); trap 'rm -rf "$T"' EXIT
PASS=0 FAIL=0
ok()   { PASS=$((PASS+1)); echo "PASS $1"; }
bad()  { FAIL=$((FAIL+1)); echo "FAIL $1"; }
check() { local name=$1; shift; if "$@"; then ok "$name"; else bad "$name"; fi; }

# shellcheck source=lib.sh
source "$HERE/lib.sh" >/dev/null 2>&1
set +e
for f in lib.sh backup.sh prepare-dir.sh restore-files.sh fetch-offsite.sh test-retention-remote.sh; do check "bash -n $f" bash -n "$HERE/$f"; done

DB=TheBuryProjectDb
NOW=$(date -u +%s)
stamp() { date -u -d "@$((NOW - $1 * 86400 - ${2:-0}))" +%Y-%m-%d_%H%M%SZ; }   # stamp <dias-atras> [segundos-extra]
SHA64=$(printf 'a%.0s' $(seq 1 64))

# ------------------------------------------------------------------------------------------------------ 1. planificador puro
lst() { # lst "bytes|nombre"...  -> listado
  printf '%s\n' "$@"
}
full()  { echo "5000|${DB}_full_$(stamp "$1").bak"; echo "100|${DB}_full_$(stamp "$1").bak.sha256"; }
tar_()  { echo "5000|files_$(stamp "$1").tar.gz"; echo "100|files_$(stamp "$1").tar.gz.sha256"; }
plan() { remote_prune_plan "$@"; }   # remote_prune_plan <kind> <now> <dias> <min_keep> <db> [piso]   < stdin
export -f bk_ts_epoch remote_prune_plan
dels() { grep '^DELETE ' | sed 's/^DELETE //' | sed -E 's/_[0-9-]+_[0-9]{6}Z.*//' | sort | uniq -c | tr -s ' ' | tr '\n' ';'; }

L=$( { for d in 1 5 20 31 40 60; do full $d; done; } )
out=$(plan full "$NOW" 30 3 "$DB" <<<"$L")
check "full: borra solo los >30d y deja >=3 (31,40,60 dias)" test "$(grep -c '^DELETE ' <<<"$out")" = 3
check "full: no borra los recientes ni el de 20d" bash -c '! grep "^DELETE" <<<"$2" | grep -q "$1"' _ "$(stamp 20)" "$out"
check "full: FLOOR = full mas antiguo que sobrevive (20d)" grep -qx "FLOOR $(stamp 20)" <<<"$out"

L=$( { for d in 40 50 60; do full $d; done; } )   # todos viejos pero son exactamente min_keep
out=$(plan full "$NOW" 30 3 "$DB" <<<"$L")
check "full: 3 completos todos viejos -> no se borra ninguno (min_keep)" test "$(grep -c '^DELETE ' <<<"$out")" = 0
L=$( { for d in 40 50; do full $d; done; } )
out=$(plan full "$NOW" 30 3 "$DB" <<<"$L")
check "full: menos de min_keep completos -> GUARD y 0 DELETE" bash -c 'grep -q "^GUARD" <<<"$1" && ! grep -q "^DELETE" <<<"$1"' _ "$out"

# 3 pares "completos" corruptos de tamaño 0 no desplazan a las copias buenas
L=$( { full 5; full 40; full 60; echo "0|${DB}_full_$(stamp 1).bak"; echo "100|${DB}_full_$(stamp 1).bak.sha256"; echo "5000|${DB}_full_$(stamp 2).bak"; echo "0|${DB}_full_$(stamp 2).bak.sha256"; echo "5000|${DB}_full_$(stamp 3).bak"; } )
out=$(plan full "$NOW" 30 3 "$DB" <<<"$L")
check "full: principal vacio / sha vacio / sin sha = incompletos (WARN), no cuentan" test "$(grep -c '^WARN' <<<"$out")" = 3
check "full: con solo 3 completos (5,40,60d) no se borra nada" test "$(grep -c '^DELETE' <<<"$out")" = 0

# nombres ajenos / sufijos / otras bases / subdirectorios: jamas se proponen
L=$( { for d in 1 2 3 40 60; do full $d; done; echo "5000|OtraBase_full_$(stamp 90).bak"; echo "5|notas.txt"; echo "5000|${DB}_full_$(stamp 90).bak.old"; echo "5000|sub/${DB}_full_$(stamp 90).bak"; echo "5000|${DB}_full_$(stamp 90).bak.failed-verify"; echo "5000|files_$(stamp 90).tar.gz"; } )
out=$(plan full "$NOW" 30 3 "$DB" <<<"$L")
check "full: ajenos (otra base, .old, subruta, failed-verify, tar) NO se proponen" bash -c '! grep -E "OtraBase|notas|\.old|sub/|failed|files_" <<<"$1"' _ "$out"
DUP=$( { full 90; full 90; full 80; full 80; full 70; full 70; } )
out=$(plan full "$NOW" 30 3 "$DB" <<<"$DUP")
check "full: nombres duplicados en el listado no cuentan doble (3 completos exactos -> 0 DELETE)" test "$(grep -c '^DELETE' <<<"$out")" = 0

# reloj / fechas
L=$( { for d in 1 2 3 40; do full $d; done; echo "5000|${DB}_full_$(stamp -2).bak"; echo "100|${DB}_full_$(stamp -2).bak.sha256"; } )   # 2 dias EN EL FUTURO
out=$(plan full "$NOW" 30 3 "$DB" <<<"$L")
check "reloj: un nombre con fecha futura -> GUARD y 0 DELETE" bash -c 'grep -q "^GUARD.*futura" <<<"$1" && ! grep -q "^DELETE" <<<"$1"' _ "$out"
L=$( { for d in 1 2 3 40; do full $d; done; echo "5000|${DB}_full_2026-02-30_000000Z.bak"; echo "100|${DB}_full_2026-02-30_000000Z.bak.sha256"; } )
out=$(plan full "$NOW" 30 3 "$DB" <<<"$L")
check "fecha imposible (02-30) -> GUARD y 0 DELETE" bash -c 'grep -q "^GUARD.*imposible" <<<"$1" && ! grep -q "^DELETE" <<<"$1"' _ "$out"
for bad_ts in 2026-13-01_000000Z 2026-01-01_246000Z 2026-01-01_000060Z 2026-00-10_000000Z; do
  check "bk_ts_epoch rechaza $bad_ts" bash -c '! bk_ts_epoch "$1" >/dev/null' _ "$bad_ts"
done
check "bk_ts_epoch acepta 29-feb de año bisiesto" bash -c 'bk_ts_epoch 2028-02-29_120000Z >/dev/null'
check "bk_ts_epoch rechaza 29-feb de año no bisiesto" bash -c '! bk_ts_epoch 2027-02-29_120000Z >/dev/null'
# frontera exacta de la retencion: un full con exactamente 30d (a la segunda) NO es mas viejo que el corte
L=$( { full 1; full 2; full 3; echo "5000|${DB}_full_$(stamp 30).bak"; echo "100|${DB}_full_$(stamp 30).bak.sha256"; } )
out=$(plan full "$NOW" 30 3 "$DB" <<<"$L")
check "frontera: exactamente 30d no se borra" test "$(grep -c '^DELETE' <<<"$out")" = 0
L=$( { full 1; full 2; full 3; echo "5000|${DB}_full_$(stamp 30 1).bak"; echo "100|${DB}_full_$(stamp 30 1).bak.sha256"; } )
out=$(plan full "$NOW" 30 3 "$DB" <<<"$L")
check "frontera: 30d + 1s si se borra (hay 3 mas nuevos)" test "$(grep -c '^DELETE' <<<"$out")" = 1
# argumentos invalidos
for args in "full $NOW 0 3 $DB" "full $NOW 30 0 $DB" "full abc 30 3 $DB" "full $NOW 30 3 bad;db" "full $NOW 99999999999 3 $DB" "full $NOW 30 99999 $DB" "tipo $NOW 30 3 $DB"; do
  # shellcheck disable=SC2086
  r=$(remote_prune_plan $args </dev/null 2>&1); rc=$?
  check "argumentos invalidos rechazados (rc=2): $args" test "$rc" = 2
done
out=$(plan full "$NOW" 30 007 "$DB" <<<"$(for d in 1 2 3 40 60 70 80 90; do full $d; done)")
check "min_keep con ceros iniciales se interpreta decimal (007=7)" test "$(grep -c '^DELETE' <<<"$out")" = 1   # 8 completos, 7 protegidos, solo el mas viejo borrable

# archivos
L=$( { for d in 1 10 31 45; do tar_ $d; done; } )
out=$(plan files "$NOW" 30 3 "$DB" <<<"$L")
check "files: 4 tars, 2 >30d, min_keep=3 -> borra solo 1 (deja 3)" test "$(grep -c '^DELETE' <<<"$out")" = 1
check "files: nunca propone nada con nombre de full" bash -c '! grep -q "_full_" <<<"$1"' _ "$out"
# logs: solo anteriores al piso y al corte
LL=$( { for d in 5 20 29 35 50; do echo "300|${DB}_log_$(stamp $d).trn"; echo "100|${DB}_log_$(stamp $d).trn.sha256"; done; } )
out=$(plan log "$NOW" 30 3 "$DB" "$(stamp 25)" <<<"$LL")
check "log: borra los < piso(25d) Y < corte(30d): 35 y 50 dias" bash -c 'test "$(grep -c "^DELETE" <<<"$1")" = 2 && grep -q "$2" <<<"$1" && grep -q "$3" <<<"$1"' _ "$out" "$(stamp 35)" "$(stamp 50)"
out=$(plan log "$NOW" 30 3 "$DB" "$(stamp 40)" <<<"$LL")
check "log: piso(40d) mas antiguo que el corte -> solo borra los < piso (50d)" test "$(grep -c '^DELETE' <<<"$out")" = 1
out=$(plan log "$NOW" 30 3 "$DB" "" <<<"$LL")
check "log: sin piso (sin full de referencia) -> GUARD y 0 DELETE" bash -c 'grep -q "^GUARD" <<<"$1" && ! grep -q "^DELETE" <<<"$1"' _ "$out"

# ------------------------------------------------------------------------------------------------------ 2. ejecutor con remoto simulado
# Se extrae de backup.sh el codigo REAL de remote_list / remote_list_has / remote_retention.
eval "declare -A REMOTE_LIST=()"
eval "$(sed -n '/^remote_list() {/,/^}/p' "$HERE/backup.sh")"
eval "$(sed -n '/^remote_list_has() /p' "$HERE/backup.sh")"
eval "$(sed -n '/^remote_retention() {/,/^}/p' "$HERE/backup.sh")"
REM="$T/remote"; CALLS="$T/calls"
BACKUP_DIR="$T/bk"; mkdir -p "$BACKUP_DIR/logs"
BACKUP_REMOTE="offsite:erp"; REMOTE_RETENTION_DAYS=30; REMOTE_MIN_KEEP=3; DB_NAME=$DB
FAILCODE="" FAILMSG=""; fail() { FAILCODE=$1; FAILMSG=$2; }
log() { :; }
rclone_errtext() { echo "$1"; }
LSF_FAIL="" LSF_EMPTY="" LSF_HIDE="" DEL_FAIL_N=0
dc() { # simula backup-offsite: lsf / deletefile contra $REM
  echo "$*" >>"$CALLS"
  local sub f
  if [[ "$*" == *" lsf "* ]]; then
    sub=$(sed -E 's#.*lsf offsite:erp/([a-z]+) .*#\1#' <<<"$*")
    [[ "$LSF_FAIL" == "$sub" ]] && { echo "boom: connection refused" >&2; return 1; }
    [[ "$LSF_EMPTY" == "$sub" ]] && return 0
    for f in "$REM/$sub"/*; do [[ -f "$f" ]] || continue; [[ -n "$LSF_HIDE" && "$(basename "$f")" == *"$LSF_HIDE"* ]] && continue; echo "$(stat -c %s "$f")|$(basename "$f")"; done
    return 0
  fi
  if [[ "$*" == *" deletefile "* ]]; then
    local n; n=$(grep -c deletefile "$CALLS")
    if (( DEL_FAIL_N > 0 && n == DEL_FAIL_N )); then echo "permission denied" >&2; return 1; fi
    f=$(sed -E 's#.*deletefile offsite:erp/##' <<<"$*"); rm -f "$REM/$f"; return 0
  fi
  return 0
}
mk() { # mk <sub> <nombre> <bytes>
  mkdir -p "$REM/$1"; head -c "$3" /dev/zero | tr '\0' 'x' >"$REM/$1/$2"
}
mkfull() { mk sql "${DB}_full_$(stamp "$1").bak" 5000; mk sql "${DB}_full_$(stamp "$1").bak.sha256" 100; }
mktar()  { mk files "files_$(stamp "$1").tar.gz" 5000; mk files "files_$(stamp "$1").tar.gz.sha256" 100; }
mklog()  { mk sql "${DB}_log_$(stamp "$1").trn" 300; mk sql "${DB}_log_$(stamp "$1").trn.sha256" 100; }
reset() { rm -rf "$REM"; : >"$CALLS"; FAILCODE="" FAILMSG=""; LSF_FAIL="" LSF_EMPTY="" LSF_HIDE="" DEL_FAIL_N=0; REMOTE_LIST=(); OFF_FULL_OK=0 OFF_FILES_OK=0; RUN_SQL=() RUN_FILES=(); }
nsql() { find "$REM/sql" -type f 2>/dev/null | wc -l; }
nfiles() { find "$REM/files" -type f 2>/dev/null | wc -l; }
ndel() { grep -c deletefile "$CALLS"; }
seed_big() { mkfull 1; mkfull 5; mkfull 31; mkfull 40; mkfull 60; mktar 1; mktar 5; mktar 31; mktar 40; mktar 60; mklog 2; mklog 33; mklog 45; mklog 70; }

# 2.1 modo `files`: NO toca SQL (este era el defecto: un `files` borraba tambien los fulls)
reset; seed_big; MODE=files; RUN_FILES=("files_$(stamp 1).tar.gz"); OFF_FILES_OK=1
b_sql=$(nsql); remote_retention
check "modo files: ningun archivo de sql/ borrado" test "$(nsql)" = "$b_sql"
check "modo files: poda los tars viejos (31,40,60 -> quedan 3)" test "$(nfiles)" = 6
check "modo files: ningun deletefile apunta a sql/" bash -c '! grep deletefile "$1" | grep -q "/sql/"' _ "$CALLS"
# 2.2 modo `full`: no toca archivos
reset; seed_big; MODE=full; RUN_SQL=("${DB}_full_$(stamp 1).bak"); OFF_FULL_OK=1
remote_retention
check "modo full: files/ intacto (10 archivos)" test "$(nfiles)" = 10
check "modo full: quedan 3 fulls completos (1,5,31... min_keep protege) o mas" test "$(ls "$REM/sql" | grep -c '_full_.*\.bak$')" -ge 3
check "modo full: se borraron los fulls de 40 y 60 dias" bash -c '[[ ! -e "$1/sql/${2}_full_$3.bak" && ! -e "$1/sql/${2}_full_$4.bak" ]]' _ "$REM" "$DB" "$(stamp 40)" "$(stamp 60)"
check "modo full: se borro el .sha256 junto con el principal" bash -c '[[ ! -e "$1/sql/${2}_full_$3.bak.sha256" ]]' _ "$REM" "$DB" "$(stamp 60)"
check "modo full: los logs anteriores al piso (33,45,70 d) se borran; el de 2d queda" bash -c '[[ -e "$1/sql/${2}_log_$3.trn" && ! -e "$1/sql/${2}_log_$4.trn" && ! -e "$1/sql/${2}_log_$5.trn" ]]' _ "$REM" "$DB" "$(stamp 2)" "$(stamp 45)" "$(stamp 70)"
# 2.3 modo log: nunca borra
reset; seed_big; MODE=log; RUN_SQL=("${DB}_log_$(stamp 0).trn"); OFF_FULL_OK=1
remote_retention
check "modo log: cero borrados" test "$(ndel)" = 0
# 2.4 modo all: ambos tipos
reset; seed_big; MODE=all; RUN_SQL=("${DB}_full_$(stamp 1).bak"); RUN_FILES=("files_$(stamp 1).tar.gz"); OFF_FULL_OK=1 OFF_FILES_OK=1
remote_retention
check "modo all: poda fulls y tars (quedan 3 de cada uno)" bash -c 'test "$(ls "$1/sql" | grep -c "_full_.*\.bak$")" = 3 -a "$(ls "$1/files" | grep -c "\.tar\.gz$")" = 3' _ "$REM"
# 2.5 sin subida/verificacion de ESTA corrida: nada
reset; seed_big; MODE=all; RUN_SQL=(); RUN_FILES=(); OFF_FULL_OK=0 OFF_FILES_OK=0
remote_retention
check "sin subida verificada en esta corrida: cero llamadas rclone" test "$(wc -l <"$CALLS")" = 0
# 2.6 fallo de listado SQL
reset; seed_big; MODE=all; RUN_SQL=("${DB}_full_$(stamp 1).bak"); RUN_FILES=("files_$(stamp 1).tar.gz"); OFF_FULL_OK=1 OFF_FILES_OK=1; LSF_FAIL=sql
remote_retention; rc=$?
check "listado sql falla: rc!=0, EX_OFFSITE y 0 deletefile" bash -c 'test "$1" != 0 -a "$2" = 6 -a "$3" = 0' _ "$rc" "$FAILCODE" "$(ndel)"
# 2.7 SQL planificado OK pero el listado de files falla: NO se borra el SQL tampoco
reset; seed_big; MODE=all; RUN_SQL=("${DB}_full_$(stamp 1).bak"); RUN_FILES=("files_$(stamp 1).tar.gz"); OFF_FULL_OK=1 OFF_FILES_OK=1; LSF_FAIL=files
b_sql=$(nsql); remote_retention; rc=$?
check "listado files falla despues del plan SQL: 0 deletefile y SQL intacto (dos fases)" bash -c 'test "$1" != 0 -a "$2" = 0 -a "$3" = "$4"' _ "$rc" "$(ndel)" "$(nsql)" "$b_sql"
# 2.8 listado vacio con rc=0 (remoto desconectado/vacio): falta lo de esta corrida
reset; seed_big; MODE=full; RUN_SQL=("${DB}_full_$(stamp 1).bak"); OFF_FULL_OK=1; LSF_EMPTY=sql
remote_retention; rc=$?
check "listado vacio (rc=0): se niega a borrar (no contiene lo de esta corrida)" bash -c 'test "$1" != 0 -a "$2" = 0' _ "$rc" "$(ndel)"
# 2.9 listado parcial que NO contiene el .sha256 de esta corrida
reset; seed_big; MODE=full; RUN_SQL=("${DB}_full_$(stamp 1).bak"); OFF_FULL_OK=1; LSF_HIDE="$(stamp 1).bak.sha256"
remote_retention; rc=$?
check "falta el .sha256 de esta corrida en el listado: 0 borrados" bash -c 'test "$1" != 0 -a "$2" = 0' _ "$rc" "$(ndel)"
# 2.10 menos de min_keep: nada
reset; mkfull 1; mkfull 40; mkfull 60; MODE=full; RUN_SQL=("${DB}_full_$(stamp 1).bak"); OFF_FULL_OK=1; REMOTE_MIN_KEEP=4
remote_retention
check "3 completos < min_keep=4: 0 borrados" test "$(ndel)" = 0
REMOTE_MIN_KEEP=3
# 2.11 borrado interrumpido: se detiene y avisa
reset; seed_big; MODE=full; RUN_SQL=("${DB}_full_$(stamp 1).bak"); OFF_FULL_OK=1; DEL_FAIL_N=2
remote_retention; rc=$?
check "deletefile falla en el 2do: rc!=0, EX_OFFSITE y mensaje INTERRUMPIDA" bash -c 'test "$1" != 0 -a "$2" = 6 && grep -q INTERRUMPIDA <<<"$3"' _ "$rc" "$FAILCODE" "$FAILMSG"
check "deletefile falla: no sigue borrando (2 llamadas)" test "$(ndel)" = 2
# 2.12 archivos ajenos y subdirectorios no se tocan
reset; seed_big; mk sql "notas.txt" 10; mk sql "OtraBase_full_$(stamp 90).bak" 5000; mkdir -p "$REM/sql/sub"; mk sql/sub "${DB}_full_$(stamp 99).bak" 5000
MODE=full; RUN_SQL=("${DB}_full_$(stamp 1).bak"); OFF_FULL_OK=1; remote_retention
check "ajenos (notas, otra base, subdir) siguen intactos" bash -c '[[ -e "$1/sql/notas.txt" && -e "$1/sql/OtraBase_full_$2.bak" && -e "$1/sql/sub/${3}_full_$4.bak" ]]' _ "$REM" "$(stamp 90)" "$DB" "$(stamp 99)"
# 2.13 reloj adelantado en un nombre existente: GUARD, 0 borrados
reset; seed_big; mkfull -3; MODE=full; RUN_SQL=("${DB}_full_$(stamp 1).bak"); OFF_FULL_OK=1; remote_retention
check "nombre con fecha futura en el remoto: 0 borrados" test "$(ndel)" = 0

# 2.14 subida fallida: offsite() sale antes y NO llega a la retencion remota
eval "$(sed -n '/^offsite() {/,/^}/p' "$HERE/backup.sh")"
reset; seed_big; MODE=all; RUN_SQL=("${DB}_full_$(stamp 1).bak"); RUN_FILES=(); BACKUP_OFFSITE_MARKER=""; BACKUP_REQUIRE_OFFSITE=false
dc() { echo "$*" >>"$CALLS"; if [[ "$*" == *" copy "* ]]; then echo "upload boom" >&2; return 1; fi; return 0; }
offsite
check "subida fallida: EX_OFFSITE y 0 deletefile" bash -c 'test "$1" = 6 -a "$(grep -c deletefile "$2")" = 0' _ "$FAILCODE" "$CALLS"
rclone_verify() { return 1; }
dc() { echo "$*" >>"$CALLS"; return 0; }
reset; seed_big; MODE=all; RUN_SQL=("${DB}_full_$(stamp 1).bak"); BACKUP_REMOTE="offsite:erp"
offsite
check "verificacion fallida: EX_OFFSITE y 0 deletefile" bash -c 'test "$1" = 6 -a "$(grep -c deletefile "$2")" = 0' _ "$FAILCODE" "$CALLS"
unset -f dc rclone_verify fail log rclone_errtext
source "$HERE/lib.sh" >/dev/null 2>&1; set +e

# ------------------------------------------------------------------------------------------------------ 3. BACKUP_DIR (prepare-dir.sh)
PD="$HERE/prepare-dir.sh"
modeof() { stat -c %a "$1"; }
: >"$T/probe"; chmod 0600 "$T/probe"; MODES=0; [[ "$(stat -c %a "$T/probe")" == 600 ]] && MODES=1
modecheck() { if (( MODES )); then check "$@"; fi; }
D1="$T/new/bk"
BACKUP_DIR="$D1" bash "$PD" >/dev/null 2>&1; rc=$?
check "prepare-dir: directorio inexistente -> se crea (rc 0) con sql/ files/ logs/ y marca" bash -c 'test "$1" = 0 && [[ -d "$2/sql" && -d "$2/files" && -d "$2/logs" && -f "$2/.bury-backup-root" ]]' _ "$rc" "$D1"
modecheck "prepare-dir: directorio nuevo en modo 700" test "$(modeof "$D1")" = 700
# existente con datos: NO se tocan modo, contenido ni dueño
D2="$T/existing"; mkdir -p "$D2/sql" "$D2/files"; echo dato >"$D2/sql/${DB}_full_$(stamp 1).bak"; chmod 0755 "$D2"; chmod 0711 "$D2/sql"
before=$(find "$D2" -printf '%p %m %u %s\n' | sort | sha256sum)
BACKUP_DIR="$D2" bash "$PD" >/dev/null 2>&1; rc=$?
after=$(find "$D2" -printf '%p %m %u %s\n' | sort | sha256sum)
check "prepare-dir: existente con datos -> rc 0 y NADA cambia (modos, dueños, tamaños, archivos)" bash -c 'test "$1" = 0 -a "$2" = "$3"' _ "$rc" "$before" "$after"
modecheck "prepare-dir: existente 0755 sigue 0755 (sin chmod)" test "$(modeof "$D2")" = 755
# --check no crea
BACKUP_DIR="$T/nuevo-check" bash "$PD" --check >/dev/null 2>&1; rc=$?
check "prepare-dir --check: inexistente -> rc 8 y no crea nada" bash -c 'test "$1" = 8 && [[ ! -e "$2" ]]' _ "$rc" "$T/nuevo-check"
# archivo en lugar de directorio
: >"$T/archivo"; BACKUP_DIR="$T/archivo" bash "$PD" >/dev/null 2>&1; rc=$?
check "prepare-dir: ruta existente que es un archivo -> rc 8" test "$rc" = 8
# '..' y relativas
BACKUP_DIR="$T/a/../b" bash "$PD" >/dev/null 2>&1; rc=$?
check "prepare-dir: ruta con '..' rechazada (rc 1) y no se crea" bash -c 'test "$1" = 1 && [[ ! -e "$2/b" ]]' _ "$rc" "$T"
# ruta con espacios
BACKUP_DIR="$T/con espacios/bk" bash "$PD" >/dev/null 2>&1; rc=$?
check "prepare-dir: ruta con espacios funciona" bash -c 'test "$1" = 0 && [[ -d "$2/sql" ]]' _ "$rc" "$T/con espacios/bk"
# no escribible (solo si no somos root y el filesystem respeta modos)
if (( MODES )) && [[ $(id -u) != 0 ]]; then
  D3="$T/ro"; mkdir -p "$D3/sql"; chmod 0555 "$D3"
  BACKUP_DIR="$D3" bash "$PD" >/dev/null 2>&1; rc=$?
  check "prepare-dir: existente no escribible -> rc 8 sin cambiar permisos" bash -c 'test "$1" = 8 -a "$(stat -c %a "$2")" = 555' _ "$rc" "$D3"
  chmod 0755 "$D3"
fi
# BACKUP_DIR de root + usuario normal (lo que deja Docker al hacer `up` sin preparar): error claro, rc 8, NO cambia nada
if [[ $(id -u) != 0 ]] && [[ -d /usr && "$(stat -c %u /usr)" = 0 ]]; then
  msg=$(BACKUP_DIR=/usr bash "$PD" --check 2>&1); rc=$?
  check "prepare-dir: BACKUP_DIR de root con usuario normal -> rc 8 y mensaje con la remediacion (sin ejecutarla)" bash -c 'test "$1" = 8 && grep -q "pertenece a root" <<<"$2" && grep -q "sin -R" <<<"$2"' _ "$rc" "$msg"
  msg=$(BACKUP_DIR=/usr bash "$HERE/backup.sh" log 2>&1); rc=$?
  check "backup.sh sobre BACKUP_DIR de root: rc 8 con el error real (no 'otra ejecucion en curso')" bash -c 'test "$1" = 8 && grep -q "pertenece a root" <<<"$2" && ! grep -q "otra ejecucion" <<<"$2"' _ "$rc" "$msg"
fi
# symlink a directorio valido
mkdir -p "$T/real-target"; ln -s "$T/real-target" "$T/link" 2>/dev/null && { BACKUP_DIR="$T/link" bash "$PD" >/dev/null 2>&1; rc=$?; check "prepare-dir: symlink a directorio valido -> rc 0 (el destino se valida)" test "$rc" = 0; }
# backup.sh / prepare_dirs no cambian el modo de un BACKUP_DIR existente
eval "$(sed -n '/^mkdir_new_mode() {/,/^}/p' "$HERE/lib.sh")"
D4="$T/e4"; mkdir -p "$D4"; chmod 0750 "$D4"
mkdir_new_mode 700 "$D4" "$D4/logs"
modecheck "mkdir_new_mode: el existente conserva 750; el nuevo sale 700" bash -c '[[ "$(stat -c %a "$1")" = 750 && "$(stat -c %a "$1/logs")" = 700 ]]' _ "$D4"
# el fuente ya no tiene chmod incondicional sobre BACKUP_DIR
check "lib.sh prepare_dirs: sin chmod 700 incondicional de BACKUP_DIR" bash -c '! sed -n "/^prepare_dirs() {/,/^}/p" "$1" | grep -qE "^[[:space:]]*chmod 700 \"\\\$BACKUP_DIR\""' _ "$HERE/lib.sh"
check "fetch-offsite.sh: sin chmod 700 incondicional de BACKUP_DIR" bash -c '! grep -qE "^chmod 700 \"\\\$BACKUP_DIR\"" "$1"' _ "$HERE/fetch-offsite.sh"

# ------------------------------------------------------------------------------------------------------ 4. lock
LK="$T/lock"; mkdir -p "$LK"
cat >"$T/lockproc.sh" <<EOS
source "$HERE/lib.sh" >/dev/null 2>&1; set +e
BACKUP_DIR="$LK"; log() { :; }
if acquire_lock; then echo "\$\$" >>"$LK/winners"; sleep 1.2; release_lock; fi
EOS
rm -f "$LK/winners"
for i in 1 2 3 4 5 6; do bash "$T/lockproc.sh" & done; wait
check "lock: 6 procesos simultaneos -> exactamente 1 gana" test "$(wc -l <"$LK/winners" 2>/dev/null || echo 0)" = 1
# un proceso muerto con SIGKILL no deja el lock tomado
cat >"$T/lockkill.sh" <<EOS
source "$HERE/lib.sh" >/dev/null 2>&1; set +e
BACKUP_DIR="$LK"; log() { :; }
acquire_lock && { sleep 30 9>&- & wait; }   # el hijo largo cierra fd 9 igual que dc()
EOS
bash "$T/lockkill.sh" & kp=$!; sleep 1; kill -9 "$kp" 2>/dev/null; wait "$kp" 2>/dev/null
# los hijos (sleep) del proceso muerto pueden seguir vivos: no deben retener el lock
BACKUP_DIR="$LK"; log() { :; }
if command -v flock >/dev/null 2>&1; then
  acquire_lock; rc=$?; release_lock
  check "lock (flock): tras SIGKILL del dueño el lock se puede tomar (rc 0)" test "$rc" = 0
else
  echo "SKIP lock SIGKILL: sin flock el lock es por directorio (recuperacion por pid muerto)"
  acquire_lock; rc=$?; release_lock
  check "lock (mkdir): lock huerfano tras SIGKILL se recupera (rc 0)" test "$rc" = 0
fi
unset -f log; source "$HERE/lib.sh" >/dev/null 2>&1; set +e

# ------------------------------------------------------------------------------------------------------ 5. recuperacion de app (marca persistente)
eval "$(sed -n '/^app_healthy() /p' "$HERE/backup.sh")"
eval "$(sed -n '/^app_wait_healthy() {/,/^}/p' "$HERE/backup.sh")"
eval "$(sed -n '/^stop_app() {/,/^}/p' "$HERE/backup.sh")"
eval "$(sed -n '/^start_app() {/,/^}/p' "$HERE/backup.sh")"
eval "$(sed -n '/^recover_app() {/,/^}/p' "$HERE/backup.sh")"
BACKUP_DIR="$T/app"; mkdir -p "$BACKUP_DIR"; APP_MARK="$BACKUP_DIR/.app-stopped-by-backup"; MODE=all
FAILCODE=""; fail() { FAILCODE=$1; }; log() { :; }; monitor_status() { echo "$1 $2" >>"$T/ms"; }
sleep() { :; }   # no esperar 3 s por reintento
APP_STATE=running; APP_HEALTH=healthy; START_FAILS=0; EX_APP=10
service_running() { [[ "$APP_STATE" == running ]]; }
dc() {
  case "$*" in
    *"stop"*"app"*) APP_STATE=stopped; APP_HEALTH="" ;;
    *"start app"*) if (( START_FAILS > 0 )); then START_FAILS=$((START_FAILS-1)); return 1; fi; APP_STATE=running; APP_HEALTH=${NEXT_HEALTH:-healthy} ;;
    *"ps --format"*) echo "$APP_HEALTH" ;;
  esac
  return 0
}
# 5.1 flujo normal: la marca existe mientras app esta detenida y desaparece al volver healthy
APP_STATE=running; APP_HEALTH=healthy; rm -f "$APP_MARK"; APP_STOPPED=0
stop_app
check "stop_app: escribe la marca ANTES/al detener y APP_STOPPED=1" bash -c '[[ -f "$1" && "$2" = 1 && "$3" = stopped ]]' _ "$APP_MARK" "$APP_STOPPED" "$APP_STATE"
start_app
check "start_app: app healthy -> borra la marca y APP_STOPPED=0" bash -c '[[ ! -e "$1" && "$2" = 0 && "$3" = running ]]' _ "$APP_MARK" "$APP_STOPPED" "$APP_STATE"
# 5.2 crash (SIGKILL tras el stop): la marca queda, app detenida -> la proxima corrida la recupera (en cualquier modo)
APP_STATE=running; APP_HEALTH=healthy; stop_app; APP_STOPPED=0     # simula que el proceso murio aqui (sin start_app)
MODE=log; APP_RECOVERY_FAILED=0; NEXT_HEALTH=healthy
recover_app
check "recover_app (modo log): marca + app detenida -> app arrancada, marca borrada" bash -c '[[ ! -e "$1" && "$2" = running && "$3" = 0 ]]' _ "$APP_MARK" "$APP_STATE" "$APP_RECOVERY_FAILED"
# 5.3 sin marca: NO toca app (operador la detuvo a mano)
APP_STATE=stopped; APP_HEALTH=""; rm -f "$APP_MARK"; recover_app
check "recover_app: sin marca no arranca una app detenida a mano" test "$APP_STATE" = stopped
# 5.4 marca + app ya healthy: solo limpia la marca
APP_STATE=running; APP_HEALTH=healthy; echo x >"$APP_MARK"; recover_app
check "recover_app: app ya healthy -> limpia la marca sin reiniciar" bash -c '[[ ! -e "$1" ]]' _ "$APP_MARK"
# 5.5 arranque fallido: la marca PERSISTE y se reintenta en la proxima corrida
APP_STATE=stopped; APP_HEALTH=""; echo x >"$APP_MARK"; START_FAILS=1; APP_RECOVERY_FAILED=0; recover_app
check "recover_app: arranque falla -> marca persiste y APP_RECOVERY_FAILED=1" bash -c '[[ -e "$1" && "$2" = 1 ]]' _ "$APP_MARK" "$APP_RECOVERY_FAILED"
START_FAILS=0; APP_RECOVERY_FAILED=0; recover_app
check "recover_app: reintento posterior tiene exito y borra la marca" bash -c '[[ ! -e "$1" && "$2" = running ]]' _ "$APP_MARK" "$APP_STATE"
# 5.6 arrancada pero nunca healthy (unhealthy): la marca no queda ignorada
APP_STATE=stopped; APP_HEALTH=""; echo x >"$APP_MARK"; NEXT_HEALTH=unhealthy; APP_RECOVERY_FAILED=0; recover_app
check "recover_app: app arranca pero queda unhealthy -> marca persiste" bash -c '[[ -e "$1" && "$2" = 1 ]]' _ "$APP_MARK" "$APP_RECOVERY_FAILED"
NEXT_HEALTH=healthy; rm -f "$APP_MARK"
# 5.7 start_app falla: la marca queda
APP_STATE=running; APP_HEALTH=healthy; stop_app; START_FAILS=1; start_app
check "start_app falla: la marca queda para el reintento" bash -c '[[ -e "$1" ]]' _ "$APP_MARK"
START_FAILS=0; rm -f "$APP_MARK"
# 5.8 restore-files borra la marca solo despues del lock y de comprobar app detenida; --list no la toca
check "restore-files.sh: borra la marca DESPUES de acquire_lock y service_running" bash -c 'awk "/acquire_lock/{a=NR} /service_running app/{s=NR} /\\.app-stopped-by-backup/{m=NR} END{exit !(a&&s&&m&&a<s&&s<m)}" "$1"' _ "$HERE/restore-files.sh"
check "restore-files.sh: --list sale antes de tocar la marca" bash -c 'awk "/if \\(\\( LIST \\)\\)/{l=NR} /\\.app-stopped-by-backup/{m=NR} END{exit !(l&&m&&l<m)}" "$1"' _ "$HERE/restore-files.sh"
# 5.9 el lock se toma antes que los prerequisitos y recover_app corre antes de check_prereqs
check "backup.sh: acquire_lock -> recover_app -> check_prereqs (la recuperacion no depende de SQL)" bash -c 'awk "/^lrc=0; acquire_lock/{a=NR} /^recover_app\$/{r=NR} /^check_prereqs\$/{c=NR} END{exit !(a&&r&&c&&a<r&&r<c)}" "$1"' _ "$HERE/backup.sh"


# ------------------------------------------------------------------------------------------------------ 6. manifiesto del par (modo all)
eval "$(sed -n '/^write_set() {/,/^}/p' "$HERE/backup.sh")"
H1=$(printf 'b%.0s' $(seq 1 64)); H2=$(printf 'c%.0s' $(seq 1 64))
tools() { # tools sh -c <script> <a0> [a1]: lee sha (a0 = nombre) o escribe el manifiesto
  local script=$3 a0=$4 a1=${5-}
  case "$script" in
    *"cut -d"*) if [[ "$script" == */backups/sql/* ]]; then echo "$H1"; else echo "$H2"; fi ;;
    *printf*)  printf '%s\n' "$a1" >"$T/set_out"; echo "$a0" >"$T/set_name" ;;
  esac
}
log() { :; }
rm -f "$T/set_out" "$T/set_name"; RUN_SQL=("${DB}_full_2026-11-07_200000Z.bak"); RUN_FILES=("files_2026-11-07_200003Z.tar.gz"); SET_APP_STOPPED=true; RUN_SET=""
write_set
check "write_set: nombre set_<ts-tar>_<ts-full>.txt" test "$(cat "$T/set_name" 2>/dev/null)" = "set_2026-11-07_200003Z_2026-11-07_200000Z.txt"
check "write_set: acredita full y tar con sus sha256 y app_stopped" bash -c 'grep -q "^full=$1 sha256=$3\$" "$5" && grep -q "^files=$2 sha256=$4\$" "$5" && grep -q "^app_stopped=true\$" "$5" && grep -q "^bury-backup-set v1\$" "$5"' _ "${DB}_full_2026-11-07_200000Z.bak" "files_2026-11-07_200003Z.tar.gz" "$H1" "$H2" "$T/set_out"
check "write_set: RUN_SET queda definido (se sube con los archivos)" test "$RUN_SET" = "set_2026-11-07_200003Z_2026-11-07_200000Z.txt"
rm -f "$T/set_out"; RUN_SQL=(); RUN_FILES=("files_2026-11-07_200003Z.tar.gz"); RUN_SET=""; write_set
check "write_set: sin full en la corrida no escribe nada" bash -c '[[ ! -e "$1" && -z "$2" ]]' _ "$T/set_out" "$RUN_SET"
check "backup.sh: solo el modo all escribe el manifiesto" bash -c 'grep -q "all)   sql_full && files_backup && write_set ;;" "$1" && ! grep -E "^    (full|files|log)\).*write_set" "$1"' _ "$HERE/backup.sh"
unset -f tools log

# el manifiesto viaja y se poda con su tar (retencion remota)
eval "$(sed -n '/^remote_retention() {/,/^}/p' "$HERE/backup.sh")"
log() { :; }; fail() { FAILCODE=$1; FAILMSG=$2; }
dc() {
  echo "$*" >>"$CALLS"
  local sub f
  if [[ "$*" == *" lsf "* ]]; then sub=$(sed -E 's#.*lsf offsite:erp/([a-z]+) .*#\1#' <<<"$*"); for f in "$REM/$sub"/*; do [[ -f "$f" ]] && echo "$(stat -c %s "$f")|$(basename "$f")"; done; return 0; fi
  if [[ "$*" == *" deletefile "* ]]; then f=$(sed -E 's#.*deletefile offsite:erp/##' <<<"$*"); rm -f "$REM/$f"; return 0; fi
  return 0
}
BACKUP_DIR="$T/bk"; mkdir -p "$BACKUP_DIR/logs"; BACKUP_REMOTE="offsite:erp"; REMOTE_RETENTION_DAYS=30; REMOTE_MIN_KEEP=3; DB_NAME=$DB; rclone_errtext() { echo "$1"; }
reset; for d in 1 5 31 40 60; do mktar $d; mk files "set_$(stamp $d)_$(stamp $d 3).txt" 300; done
MODE=files; RUN_FILES=("files_$(stamp 1).tar.gz"); OFF_FILES_OK=1; remote_retention
check "retencion remota: al borrar un tar se borra tambien su set_<ts>.txt (y los que sobreviven conservan el suyo)" bash -c '[[ ! -e "$1/files/set_$2_$4.txt" && -e "$1/files/set_$3_$5.txt" ]]' _ "$REM" "$(stamp 60)" "$(stamp 1)" "$(stamp 60 3)" "$(stamp 1 3)"
unset -f dc log fail


# ------------------------------------------------------------------------------------------------------ 7. hallazgos de la revision adversarial (Codex)
# 7.1 un par recuperable no pierde su full aunque lleguen fulls independientes mas nuevos (planificador + ejecutor remoto)
PL=$( { for d in 1 2 3 4 40 45 50; do full $d; done; } )
out=$(plan full "$NOW" 30 3 "$DB" "$(stamp 40) $(stamp 45) $(stamp 50)" <<<"$PL")
check "pares: los fulls protegidos (40,45,50 d) NO se borran aunque haya 4 fulls independientes mas nuevos" test "$(grep -c '^DELETE' <<<"$out")" = 0
check "pares: FLOOR = full protegido mas antiguo (los logs de ese rango se conservan)" grep -qx "FLOOR $(stamp 50)" <<<"$out"
out=$(plan full "$NOW" 30 3 "$DB" "" <<<"$PL")
check "pares: sin proteccion, esos mismos fulls antiguos si se borrarian (regresion que se cierra)" test "$(grep -c '^DELETE' <<<"$out")" = 3

eval "$(sed -n '/^remote_retention() {/,/^}/p' "$HERE/backup.sh")"
BACKUP_DIR="$T/bk"; mkdir -p "$BACKUP_DIR/logs"; BACKUP_REMOTE="offsite:erp"; REMOTE_RETENTION_DAYS=30; REMOTE_MIN_KEEP=3; DB_NAME=$DB
log() { :; }; fail() { FAILCODE=$1; FAILMSG=$2; }; rclone_errtext() { echo "$1"; }
dc() {
  echo "$*" >>"$CALLS"
  local sub f
  if [[ "$*" == *" lsf "* ]]; then sub=$(sed -E 's#.*lsf offsite:erp/([a-z]+) .*#\1#' <<<"$*"); for f in "$REM/$sub"/*; do [[ -f "$f" ]] && echo "$(stat -c %s "$f")|$(basename "$f")"; done; return 0; fi
  if [[ "$*" == *" deletefile "* ]]; then f=$(sed -E 's#.*deletefile offsite:erp/##' <<<"$*"); rm -f "$REM/$f"; return 0; fi
  return 0
}
# escenario de Codex: 3 pares `all` de >30 d (tar + set + su full) y despues solo fulls independientes diarios
reset
for d in 40 45 50; do mktar $d; mk files "set_$(stamp $d)_$(stamp $d 3).txt" 300; mk sql "${DB}_full_$(stamp $d 3).bak" 5000; mk sql "${DB}_full_$(stamp $d 3).bak.sha256" 100; done
for d in 1 2 3 4; do mkfull $d; done
MODE=full; RUN_SQL=("${DB}_full_$(stamp 1).bak"); OFF_FULL_OK=1; remote_retention
n_pair_fulls=0; for d in 40 45 50; do [[ -e "$REM/sql/${DB}_full_$(stamp $d 3).bak" ]] && n_pair_fulls=$((n_pair_fulls+1)); done
check "remoto: sobreviven los 3 fulls de los 3 pares (tars y sets intactos)" test "$n_pair_fulls" = 3 -a "$(nfiles)" = 9
# si el tar del par SE BORRA en esta misma corrida, su full deja de estar protegido
reset
for d in 40 45 50 60; do mktar $d; mk files "set_$(stamp $d)_$(stamp $d 3).txt" 300; mk sql "${DB}_full_$(stamp $d 3).bak" 5000; mk sql "${DB}_full_$(stamp $d 3).bak.sha256" 100; done
for d in 1 2 3; do mkfull $d; mktar $d; done
MODE=all; RUN_SQL=("${DB}_full_$(stamp 1).bak"); RUN_FILES=("files_$(stamp 1).tar.gz"); OFF_FULL_OK=1 OFF_FILES_OK=1; remote_retention
check "remoto: el par cuyo tar se poda en ESTA corrida pierde tambien su full (ya no esta protegido)" bash -c '[[ ! -e "$1/files/files_$2.tar.gz" && ! -e "$1/files/set_$2_$3.txt" && ! -e "$1/sql/${4}_full_$3.bak" ]]' _ "$REM" "$(stamp 60)" "$(stamp 60 3)" "$DB"

# 7.2 sin flock no hay lock para operaciones destructivas (salvo opt-in explicito de desarrollo)
(
  BACKUP_DIR="$T/nolock"; mkdir -p "$BACKUP_DIR"; log() { :; }
  command() { if [[ "${1-}" == "-v" && "${2-}" == "flock" ]]; then return 1; fi; builtin command "$@"; }
  unset BURY_ALLOW_DIR_LOCK; acquire_lock; echo "sin_optin=$?" >"$T/nolock.out"
  BURY_ALLOW_DIR_LOCK=1 acquire_lock; echo "con_optin=$?" >>"$T/nolock.out"; release_lock
)
check "lock sin flock: rc 2 (rechazado) sin opt-in explicito" grep -qx "sin_optin=2" "$T/nolock.out"
check "lock sin flock: con BURY_ALLOW_DIR_LOCK=1 (solo desarrollo/tests) funciona" grep -qx "con_optin=0" "$T/nolock.out"

# 7.3 restore-sql sobre la base de produccion toma el lock y borra la marca bajo el, antes de restaurar nada
check "restore-sql.sh: acquire_lock -> borrado de marca -> RESTORE (en ese orden)" bash -c 'awk "/acquire_lock \\|\\| lrc/{a=NR} /app-stopped-by-backup/{m=NR} /RESTORE DATABASE \\[/{r=NR} END{exit !(a&&m&&r&&a<m&&m<r)}" "$1"' _ "$HERE/restore-sql.sh"
check "restore-sql.sh: el lock solo aplica cuando el destino ES la base de produccion (restore-test no lo toma)" grep -qE 'if \[\[ "\$TARGET" == "\$DB_NAME" \]\]; then' "$HERE/restore-sql.sh"
unset -f dc log fail rclone_errtext

echo; echo "resultado test-retention-remote: $PASS OK, $FAIL fallos"; (( FAIL == 0 ))
