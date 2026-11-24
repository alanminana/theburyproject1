#!/usr/bin/env bash
# Tests de la retencion POR CAPACIDAD y de la copia a destino EXTRAIBLE (scripts/backup/lib-capacity.sh, retention-capacity.sh, copy-removable.sh).
#   bash scripts/backup/test-retention-capacity.sh          cadenas SINTETICAS con los nombres reales, sin docker (corre en CI). exit 0 = todo OK.
#   bash scripts/backup/test-retention-capacity.sh --db     ademas: SQL Server REAL descartable (proyecto Compose bury-bkcap-<rand>) con restore real
#                                                           de lo que queda; requiere Linux/WSL con docker. Datos 100% sinteticos, se limpia solo.
# El borrado/copia corre sobre el filesystem del host (BK_CAP_HOSTFS=1: los mismos fragmentos sh que en produccion corren en el contenedor).
set -uo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(cd "$HERE/../.." && pwd)"
WITH_DB=0
for a in "$@"; do case "$a" in --db) WITH_DB=1 ;; -h|--help) sed -n '2,6p' "$0"; exit 0 ;; *) echo "argumento desconocido: $a" >&2; exit 1 ;; esac; done
export BURY_ALLOW_DIR_LOCK=1 BK_CAP_HOSTFS=1
T=$(mktemp -d); [[ -n "$T" && -d "$T" ]] || exit 1
PASS=0 FAIL=0
ok()   { PASS=$((PASS+1)); echo "PASS $1"; }
bad()  { FAIL=$((FAIL+1)); echo "FAIL $1"; [[ -n "${2:-}" ]] && echo "     -> $2"; }
check() { local name=$1; shift; if "$@"; then ok "$name"; else bad "$name"; fi; }
DBC_CLEAN=""
trap '[[ -n "$DBC_CLEAN" ]] && eval "$DBC_CLEAN"; rm -rf "$T"' EXIT

# shellcheck source=lib.sh
source "$HERE/lib.sh" >/dev/null 2>&1
# shellcheck source=lib-capacity.sh
source "$HERE/lib-capacity.sh"
set +e
for f in lib-capacity.sh retention-capacity.sh copy-removable.sh backup.sh test-retention-capacity.sh; do check "bash -n $f" bash -n "$HERE/$f"; done
check "scripts nuevos con bit ejecutable en el arbol de trabajo" bash -c 'for f in retention-capacity.sh copy-removable.sh test-retention-capacity.sh; do [[ -x "$1/$f" ]] || exit 1; done' _ "$HERE"
check "lib-capacity.sh con finales de linea LF" bash -c '! grep -q $'"'"'\r'"'"' "$1/lib-capacity.sh" "$1/retention-capacity.sh" "$1/copy-removable.sh" "$1/test-retention-capacity.sh"' _ "$HERE"

DB=TheBuryProjectDb
NOW=$(date -u +%s)
stamp() { date -u -d "@$((NOW - $1))" +%Y-%m-%d_%H%M%SZ; }   # stamp <segundos-atras>
D=86400

# ---------------------------------------------------------------------------------------------------------------- constructores
mkf() { mkdir -p "$1/$(dirname "$2")"; head -c "$3" /dev/zero | tr '\0' 'x' >"$1/$2"; }   # mkf <raiz> <rel> <bytes>
shaf() { ( cd "$1/$(dirname "$2")" && sha256sum "$(basename "$2")" >"$(basename "$2").sha256" ); }
# mkchain <raiz> <seg-atras-del-full> <n-logs> <bytes-full> <con-tar 0|1>: full + logs POSTERIORES (cada 1 h) + tar 30 s despues + manifiesto
mkchain() {
  local r=$1 ago=$2 nl=$3 fb=$4 tar=${5:-0} i f
  f="sql/${DB}_full_$(stamp "$ago").bak"; mkf "$r" "$f" "$fb"; shaf "$r" "$f"
  for (( i = 1; i <= nl; i++ )); do f="sql/${DB}_log_$(stamp $((ago - 3600 * i))).trn"; mkf "$r" "$f" 100; shaf "$r" "$f"; done
  if (( tar )); then
    f="files/files_$(stamp $((ago - 30))).tar.gz"; mkf "$r" "$f" 500; shaf "$r" "$f"
    printf 'bury-backup-set v1\nfull=%s\n' "x" >"$r/files/set_$(stamp $((ago - 30)))_$(stamp "$ago").txt"
  fi
}
newbk() { # newbk <nombre> -> crea $T/<nombre> con la marca y devuelve la ruta
  mkdir -p "$T/$1/sql" "$T/$1/files" "$T/$1/logs"; echo marca >"$T/$1/.bury-backup-root"; echo "$T/$1"
}
bytes_of() { # bytes_of <raiz> <patron-de-ts>: suma de bytes de archivos cuyo nombre contiene el ts
  find "$1/sql" "$1/files" -type f -name "*$2*" -printf '%s\n' 2>/dev/null | awk '{s+=$1} END {print s+0}'
}
total_of() { find "$1/sql" "$1/files" -type f -printf '%s\n' 2>/dev/null | awk '{s+=$1} END {print s+0}'; }
has_full() { [[ -f "$1/sql/${DB}_full_$(stamp "$2").bak" ]]; }
nfiles() { find "$1/sql" "$1/files" -type f | wc -l | tr -d ' '; }
list_of() { ( cd "$1" && for d in sql files; do for p in "$d"/*; do if [ -L "$p" ]; then echo "L|$p"; elif [ -f "$p" ]; then echo "$(stat -c %s "$p")|$p"; fi; done; done ); }
plan_of() { # plan_of <raiz> <min> <tope> <libre_min> <libre>
  bk_chains "$DB" "$NOW" < <(list_of "$1") | capacity_plan "$2" "$3" "$4" "$5"
}

# ================================================================================================================ 1. conversiones y modelo
check "gb_to_bytes 1 = 1073741824" test "$(gb_to_bytes 1)" = 1073741824
check "gb_to_bytes 0.5 = 536870912" test "$(gb_to_bytes 0.5)" = 536870912
check "gb_to_bytes rechaza negativo/texto/vacio" bash -c 'source "$1/lib.sh" >/dev/null 2>&1; source "$1/lib-capacity.sh"; ! gb_to_bytes -1 && ! gb_to_bytes abc && ! gb_to_bytes ""' _ "$HERE"

R=$(newbk m1)
mkchain "$R" $((4*D)) 2 1000 1; mkchain "$R" $((3*D)) 2 1000 1; mkchain "$R" $((2*D)) 2 1000 1; mkchain "$R" $((1*D)) 2 1000 1
mkf "$R" sql/notas.txt 77; mkf "$R" "sql/OtraBase_full_$(stamp $((9*D))).bak" 999; mkf "$R" "files/files_$(stamp $((9*D))).tar.gz.old" 55
rec=$(bk_chains "$DB" "$NOW" < <(list_of "$R"))
check "modelo: 4 cadenas (idx 1..4) y los archivos ajenos/otra base NO se modelan" bash -c '[[ $(cut -d"|" -f2 <<<"$1" | sort -u | tr "\n" " ") == "1 2 3 4 " ]] && ! grep -qE "notas|OtraBase|\.old" <<<"$1"' _ "$rec"
check "modelo: cadena 1 = full + sha + 2 logs + sus sha + tar + sha + manifiesto (9 archivos)" test "$(grep -c '^R|1|' <<<"$rec")" = 9
C1=$(bytes_of "$R" "$(stamp $((4*D)))"); C1=$((C1))
TOT=$(total_of "$R"); OTROS=$((77+999+55))
TOTCH=$((TOT - OTROS))   # bytes de archivos gestionados (los ajenos no cuentan)
CH_BYTES=$((TOTCH / 4))  # las 4 cadenas pesan igual
out=$(plan_of "$R" 2 $((TOTCH - CH_BYTES)) 0 999999999999)
check "plan: tope = total - 1 cadena -> borra SOLO la mas antigua (9 archivos) y OK" bash -c '[[ $(grep -c "^DELETE" <<<"$1") == 9 && $(grep -c "^CHAIN .* DEL" <<<"$1") == 1 && "$(tail -n1 <<<"$1")" == OK ]]' _ "$out"
check "plan: lo borrado es la cadena de hace 4 dias (full + logs + tar + manifiesto)" bash -c 'grep "^DELETE" <<<"$1" | grep -q "_full_$2" && grep "^DELETE" <<<"$1" | grep -q "set_" && grep "^DELETE" <<<"$1" | grep -q "files_" && grep "^DELETE" <<<"$1" | grep -q "_log_"' _ "$out" "$(stamp $((4*D)))"
check "plan: nunca propone borrar ajenos (notas, otra base, .old)" bash -c '! grep -E "notas|OtraBase|\.old" <<<"$1"' _ "$out"
out=$(plan_of "$R" 2 $((TOTCH - 2*CH_BYTES)) 0 999999999999)
check "plan: tope = total - 2 cadenas -> borra 2 (las 2 mas antiguas) y OK" bash -c '[[ $(grep -c "^CHAIN .* DEL" <<<"$1") == 2 && "$(tail -n1 <<<"$1")" == OK ]]' _ "$out"
out=$(plan_of "$R" 2 $((TOTCH - 3*CH_BYTES)) 0 999999999999); rc=$?
check "plan: pide borrar 3 pero el minimo es 2 -> borra 2, FAIL ruidoso rc 3 (disco insuficiente)" bash -c '[[ $(grep -c "^CHAIN .* DEL" <<<"$1") == 2 && "$(tail -n1 <<<"$1")" == FAIL* && "$2" == 3 ]]' _ "$out" "$rc"
check "plan: el FAIL dice 'DISCO INSUFICIENTE' y que la ultima cadena nunca se borra" bash -c 'tail -n1 <<<"$1" | grep -q "DISCO INSUFICIENTE" && tail -n1 <<<"$1" | grep -q "NUNCA"' _ "$out"
out=$(plan_of "$R" 1 1 0 999999999999); rc=$?
check "plan: min_cadenas=1 y tope absurdo (1 byte) -> borra 3, conserva SIEMPRE la mas nueva, rc 3" bash -c '[[ $(grep -c "^CHAIN .* DEL" <<<"$1") == 3 && $(grep -c "^CHAIN .* KEEP" <<<"$1") == 1 && "$2" == 3 ]] && grep "^CHAIN" <<<"$1" | tail -n1 | grep -q KEEP && ! grep "^DELETE" <<<"$1" | grep -q "$3"' _ "$out" "$rc" "$(stamp $((1*D)))"
out=$(plan_of "$R" 2 0 0 0); rc=$?
check "plan: sin limites (0/0) y libre 0 -> OK sin borrar" bash -c '[[ $(grep -c "^DELETE" <<<"$1") == 0 && "$(tail -n1 <<<"$1")" == OK ]]' _ "$out"
# libre minimo: libre actual 1000, minimo 1000+CH -> necesita 1 cadena
out=$(plan_of "$R" 2 0 $((1000 + CH_BYTES)) 1000)
check "plan: libre minimo -> borra la cadena mas antigua hasta liberar lo que falta" bash -c '[[ $(grep -c "^CHAIN .* DEL" <<<"$1") == 1 && "$(tail -n1 <<<"$1")" == OK ]]' _ "$out"
out=$(plan_of "$R" 2 0 999999999999999 1000); rc=$?
check "plan: libre minimo imposible -> FAIL rc 3 con las 2 cadenas minimas intactas" bash -c '[[ $(grep -c "^CHAIN .* KEEP" <<<"$1") == 2 && "$2" == 3 ]]' _ "$out" "$rc"
check "plan: argumentos invalidos -> rc 2" bash -c 'source "$1/lib.sh" >/dev/null 2>&1; source "$1/lib-capacity.sh"; set +e; echo | capacity_plan 0 1 1 1 >/dev/null; a=$?; echo | capacity_plan x 1 1 1 >/dev/null; b=$?; [[ $a == 2 && $b == 2 ]]' _ "$HERE"

# manifiesto manda: el tar de la corrida all del full1 pero con ts POSTERIOR al full2 -> sigue al full1 (se borra con la cadena 1)
R=$(newbk m2)
mkchain "$R" $((4*D)) 1 1000 0; mkchain "$R" $((3*D)) 1 1000 0; mkchain "$R" $((1*D)) 1 1000 0
tts=$(stamp $((2*D))); f="files/files_$tts.tar.gz"; mkf "$R" "$f" 500; shaf "$R" "$f"; printf 'set\n' >"$R/files/set_${tts}_$(stamp $((4*D))).txt"
rec=$(bk_chains "$DB" "$NOW" < <(list_of "$R"))
check "manifiesto: tar de hace 2d acreditado al full de hace 4d queda en la cadena 1 (y su manifiesto)" bash -c 'grep -q "^R|1|.*files_'"$tts"'.tar.gz$" <<<"$1" && grep -q "^R|1|.*set_'"$tts"'_" <<<"$1"' _ "$rec"
# sin manifiesto: por fecha (cadena del full anterior)
rm "$R/files/set_${tts}_"*.txt
rec=$(bk_chains "$DB" "$NOW" < <(list_of "$R"))
check "sin manifiesto: el tar sigue al full anterior por fecha (cadena 2)" bash -c 'grep -q "^R|2|.*files_'"$tts"'.tar.gz$" <<<"$1"' _ "$rec"

# logs huerfanos (anteriores a todo full) se borran PRIMERO y no cuentan como cadena
R=$(newbk m3)
mkf "$R" "sql/${DB}_log_$(stamp $((9*D))).trn" 100; shaf "$R" "sql/${DB}_log_$(stamp $((9*D))).trn"
mkchain "$R" $((2*D)) 1 1000 0; mkchain "$R" $((1*D)) 1 1000 0
out=$(plan_of "$R" 2 500 0 999999999999)
check "huerfanos: los logs anteriores a todo full se borran primero (cadena 'huerfanos'), sin tocar las 2 cadenas minimas" bash -c 'grep -q "^CHAIN huerfanos" <<<"$1" && [[ $(grep -c "^CHAIN .* KEEP" <<<"$1") == 2 ]]' _ "$out"

# full incompleto (sin .sha256): no es cadena, no se borra; sus logs siguen a la cadena anterior
R=$(newbk m4)
mkchain "$R" $((3*D)) 1 1000 0; mkchain "$R" $((1*D)) 1 1000 0
mkf "$R" "sql/${DB}_full_$(stamp $((2*D))).bak" 1000   # sin sha256
out=$(plan_of "$R" 1 1 0 999999999999)
check "full incompleto: WARN, no se propone borrarlo ni cuenta como cadena" bash -c 'grep -q "^WARN full incompleto" <<<"$1" && ! grep "^DELETE" <<<"$1" | grep -q "'"$(stamp $((2*D)))"'.bak$"' _ "$out"

# guardas de reloj
R=$(newbk m5); mkchain "$R" $((2*D)) 1 1000 0; mkchain "$R" $((1*D)) 1 1000 0; mkf "$R" "sql/${DB}_full_$(stamp -$((2*D))).bak" 1000; shaf "$R" "sql/${DB}_full_$(stamp -$((2*D))).bak"
out=$(plan_of "$R" 2 1 0 999999999999); rc=$?
check "reloj: nombre con fecha futura -> GUARD, 0 DELETE y FAIL ruidoso (limite excedido sin poder decidir)" bash -c '[[ $(grep -c "^GUARD" <<<"$1") -ge 1 && $(grep -c "^DELETE" <<<"$1") == 0 && "$2" == 3 ]]' _ "$out" "$rc"
R=$(newbk m6); mkchain "$R" $((2*D)) 1 1000 0; mkf "$R" "sql/${DB}_full_2026-02-30_101010Z.bak" 1000; shaf "$R" "sql/${DB}_full_2026-02-30_101010Z.bak"
out=$(plan_of "$R" 1 1 0 999999999999)
check "reloj: fecha imposible (02-30) -> GUARD y 0 DELETE" bash -c '[[ $(grep -c "^GUARD" <<<"$1") -ge 1 && $(grep -c "^DELETE" <<<"$1") == 0 ]]' _ "$out"
R=$(newbk m7); mkf "$R" "files/files_$(stamp $((2*D))).tar.gz" 500; shaf "$R" "files/files_$(stamp $((2*D))).tar.gz"
out=$(plan_of "$R" 1 1 0 999999999999); rc=$?
check "sin ninguna cadena completa y limite excedido -> FAIL rc 3 y 0 DELETE (solo tars: nada que 'cadena' proteja)" bash -c '[[ $(grep -c "^DELETE" <<<"$1") == 0 && "$2" == 3 ]]' _ "$out" "$rc"

# Hallazgo de la revision independiente: un backup completo (full + tar de archivos) seguido de DOS fulls SQL sueltos (los previos a un deploy)
# no deben permitir que el minimo de 2 cadenas borre el unico tar de archivos (uploads, claves): la ultima cadena con tar nunca se borra.
R=$(newbk m8); mkchain "$R" $((3*D)) 1 1000 1; mkchain "$R" $((2*D)) 1 1000 0; mkchain "$R" $((1*D)) 1 1000 0
out=$(plan_of "$R" 2 1 0 999999999999); rc=$?
check "tar protegido: all -> full -> full con minimo 2 y tope imposible -> 0 DELETE de la cadena con tar y FAIL rc 3" bash -c '[[ $(grep -c "^DELETE" <<<"$1") == 0 && "$2" == 3 ]] && ! grep "^DELETE" <<<"$1" | grep -q "files_"' _ "$out" "$rc"
check "tar protegido: la cadena con el tar figura como KEEP" bash -c 'grep -q "^CHAIN .* KEEP" <<<"$1" && [[ $(grep -c "^CHAIN .* DEL" <<<"$1") == 0 ]]' _ "$out"
R=$(newbk m9); mkchain "$R" $((3*D)) 1 1000 1; mkchain "$R" $((2*D)) 1 1000 1; mkchain "$R" $((1*D)) 1 1000 0
out=$(plan_of "$R" 2 $(( $(total_of "$R") - 100 )) 0 999999999999); rc=$?
check "tar protegido: si una cadena MAS NUEVA tambien tiene tar, la mas antigua si se puede borrar (rc 0)" bash -c '[[ $(grep -c "^CHAIN .* DEL" <<<"$1") == 1 && "$2" == 0 ]]' _ "$out" "$rc"

# Segunda revision de Codex: un tar INTERRUMPIDO (sin checksum, sin manifiesto) en una cadena posterior no debe desplazar la proteccion del ultimo
# conjunto acreditado (tar + checksum + manifiesto).
R=$(newbk m10); mkchain "$R" $((4*D)) 1 1000 1; mkchain "$R" $((3*D)) 1 1000 0; mkchain "$R" $((2*D)) 1 1000 0; mkchain "$R" $((1*D)) 1 1000 0
mkf "$R" "files/files_$(stamp $((3*D - 30))).tar.gz" 500   # tar posterior SIN .sha256 ni manifiesto (backup interrumpido)
out=$(plan_of "$R" 2 1 0 999999999999); rc=$?
check "tar incompleto (sin checksum ni manifiesto) en cadena posterior: el conjunto acreditado mas antiguo NO se borra y el FAIL sigue siendo ruidoso" bash -c '[[ "$2" == 3 ]] && ! grep "^DELETE" <<<"$1" | grep -q "_full_'"$(stamp $((4*D)))"'" && ! grep "^DELETE" <<<"$1" | grep -q "files_'"$(stamp $((4*D - 30)))"'" && tail -n1 <<<"$1" | grep -q "^FAIL"' _ "$out" "$rc"
R=$(newbk m11); mkchain "$R" $((4*D)) 1 1000 1; mkchain "$R" $((3*D)) 1 1000 0; mkchain "$R" $((2*D)) 1 1000 0; mkchain "$R" $((1*D)) 1 1000 0
mkf "$R" "files/files_$(stamp $((3*D - 30))).tar.gz" 500; shaf "$R" "files/files_$(stamp $((3*D - 30))).tar.gz"   # con checksum pero SIN manifiesto
out=$(plan_of "$R" 2 1 0 999999999999); rc=$?
check "tar con checksum pero sin manifiesto: tampoco acredita; el conjunto completo mas antiguo se conserva" bash -c '[[ "$2" == 3 ]] && ! grep "^DELETE" <<<"$1" | grep -q "_full_'"$(stamp $((4*D)))"'"' _ "$out" "$rc"
R=$(newbk m12); mkchain "$R" $((4*D)) 1 1000 1; mkchain "$R" $((3*D)) 1 1000 1; mkchain "$R" $((2*D)) 1 1000 0; mkchain "$R" $((1*D)) 1 1000 0
out=$(plan_of "$R" 2 1 0 999999999999); rc=$?
check "dos conjuntos acreditados: se conserva el MAS NUEVO (cadena 2) y la mas antigua si se puede borrar" bash -c '[[ "$2" == 3 ]] && grep "^DELETE" <<<"$1" | grep -q "_full_'"$(stamp $((4*D)))"'" && ! grep "^DELETE" <<<"$1" | grep -q "_full_'"$(stamp $((3*D)))"'"' _ "$out" "$rc"
# BACKUP_CA_KEY=seal: sin .caseal el tar no acredita
R=$(newbk m13); mkchain "$R" $((4*D)) 1 1000 1; mkchain "$R" $((3*D)) 1 1000 1; mkchain "$R" $((2*D)) 1 1000 0; mkchain "$R" $((1*D)) 1 1000 0
mkf "$R" "files/files_$(stamp $((4*D - 30))).tar.gz.caseal" 100   # solo el conjunto antiguo tiene .caseal
out=$(BACKUP_CA_KEY=seal plan_of "$R" 2 1 0 999999999999); rc=$?
check "seal: solo el conjunto con .caseal acredita; el posterior sin .caseal no desplaza la proteccion del anterior" bash -c '[[ "$2" == 3 ]] && ! grep "^DELETE" <<<"$1" | grep -q "_full_'"$(stamp $((4*D)))"'"' _ "$out" "$rc"

# Tercera revision de Codex: (a) un manifiesto que nombra un full BORRADO no acredita nada; (b) si BACKUP_CA_KEY no se puede determinar, no se
# asume "include" (relajaria la exigencia de .caseal): GUARD y no se borra nada.
R=$(newbk m14); mkchain "$R" $((4*D)) 1 1000 1; mkchain "$R" $((3*D)) 1 1000 0; mkchain "$R" $((2*D)) 1 1000 0; mkchain "$R" $((1*D)) 1 1000 0
tts=$(stamp $((3*D - 30))); dts=$(stamp $((3*D + 5)))   # el manifiesto apunta a un full inexistente (borrado)
mkf "$R" "files/files_${tts}.tar.gz" 500; shaf "$R" "files/files_${tts}.tar.gz"; printf 'bury-backup-set v1
full=x
' >"$R/files/set_${tts}_${dts}.txt"
out=$(plan_of "$R" 2 1 0 999999999999); rc=$?
check "manifiesto hacia un full borrado: no acredita; el ultimo conjunto valido (cadena 1) NO se borra y el FAIL es ruidoso" bash -c '[[ "$2" == 3 ]] && ! grep "^DELETE" <<<"$1" | grep -q "_full_'"$(stamp $((4*D)))"'" && ! grep "^DELETE" <<<"$1" | grep -q "files_'"$(stamp $((4*D - 30)))"'" && tail -n1 <<<"$1" | grep -q "^FAIL"' _ "$out" "$rc"
R=$(newbk m15); mkchain "$R" $((4*D)) 1 1000 1; mkchain "$R" $((3*D)) 1 1000 1; mkchain "$R" $((2*D)) 1 1000 0; mkchain "$R" $((1*D)) 1 1000 0
out=$( unset BACKUP_CA_KEY; cfg() { return 1; }; plan_of "$R" 2 1 0 999999999999 ); rc=$?
check "cfg falla y BACKUP_CA_KEY no esta definida: GUARD, 0 DELETE y FAIL (no se asume include)" bash -c '[[ $(grep -c "^GUARD" <<<"$1") -ge 1 && $(grep -c "^DELETE" <<<"$1") == 0 ]] && grep -q "^FAIL" <<<"$1" && [[ "$2" == 3 ]]' _ "$out" "$rc"
out=$( unset -f cfg; unset BACKUP_CA_KEY; plan_of "$R" 2 1 0 999999999999 ); rc=$?
check "cfg inexistente y BACKUP_CA_KEY no definida: GUARD, 0 DELETE y FAIL" bash -c '[[ $(grep -c "^GUARD" <<<"$1") -ge 1 && $(grep -c "^DELETE" <<<"$1") == 0 && "$2" == 3 ]]' _ "$out" "$rc"
out=$( BACKUP_CA_KEY=exclude plan_of "$R" 2 1 0 999999999999 ); rc=$?
check "BACKUP_CA_KEY con valor invalido: GUARD, 0 DELETE y FAIL" bash -c '[[ $(grep -c "^GUARD" <<<"$1") -ge 1 && $(grep -c "^DELETE" <<<"$1") == 0 && "$2" == 3 ]]' _ "$out" "$rc"
out=$( BACKUP_CA_KEY=include plan_of "$R" 2 1 0 999999999999 ); rc=$?
check "BACKUP_CA_KEY=include explicito: sin GUARD (la decision se toma con normalidad)" bash -c '[[ $(grep -c "^GUARD" <<<"$1") == 0 ]]' _ "$out" "$rc"

# Cuarta revision de Codex: dos manifiestos contradictorios para un mismo tar -> GUARD y 0 DELETE, sin depender del orden del listado.
for v in a b; do
  R=$(newbk "m16$v"); mkchain "$R" $((4*D)) 1 1000 1; mkchain "$R" $((3*D)) 1 1000 0; mkchain "$R" $((2*D)) 1 1000 0; mkchain "$R" $((1*D)) 1 1000 0
  tts=$(stamp $((4*D - 30)))
  # a: el segundo manifiesto nombra un full POSTERIOR (se ordena despues); b: uno ANTERIOR (se ordena antes): el resultado no depende del orden
  if [[ $v == a ]]; then f2=$(stamp $((3*D))); else f2=$(stamp $((5*D))); fi
  printf 'x
' >"$R/files/set_${tts}_${f2}.txt"
  out=$(plan_of "$R" 2 1 0 999999999999); rc=$?
  check "manifiestos contradictorios ($v): GUARD, 0 DELETE y FAIL ruidoso (el full coherente no se pierde)" bash -c '[[ $(grep -c "^GUARD.*contradictorios" <<<"$1") -ge 1 && $(grep -c "^DELETE" <<<"$1") == 0 && "$2" == 3 ]]' _ "$out" "$rc"
done

# ================================================================================================================ 2. scripts (hostfs)
cfgrun() { # cfgrun <raiz> [VAR=valor...] -- <script> [args]: ejecuta un script con entorno aislado del .env real
  local r=$1; shift; local -a ev=()
  while [[ "${1:-}" != -- ]]; do ev+=("$1"); shift; done; shift
  env -i PATH="$PATH" HOME="$T/home" BURY_ALLOW_DIR_LOCK=1 BK_CAP_HOSTFS=1 COMPOSE_ENV_FILES="$T/empty.env" BACKUP_DIR="$r" MSSQL_DATABASE="$DB" "${ev[@]}" bash "$HERE/$1" "${@:2}"
}
mkdir -p "$T/home"; : >"$T/empty.env"
RC="$HERE/retention-capacity.sh"; RM="$HERE/copy-removable.sh"
snap() { ( cd "$1" && find sql files -type f | sort | sha256sum | cut -d' ' -f1 ); }

R=$(newbk s1); mkchain "$R" $((4*D)) 2 1000 1; mkchain "$R" $((3*D)) 2 1000 1; mkchain "$R" $((2*D)) 2 1000 1; mkchain "$R" $((1*D)) 2 1000 1
mkf "$R" sql/notas.txt 77; mkf "$R" "sql/OtraBase_full_$(stamp $((9*D))).bak" 999
TOTCH=$(( $(total_of "$R") - 77 - 999 )); CH=$((TOTCH / 4))
LIM=$(awk -v b=$((TOTCH - CH)) 'BEGIN{printf "%.9f", b/1073741824}')
before=$(snap "$R")
out=$(cfgrun "$R" BACKUP_MAX_TOTAL_GB="$LIM" -- retention-capacity.sh 2>&1); rc=$?
check "script: por defecto es DRY-RUN (rc 0, no borra nada, muestra lo que se borraria)" bash -c '[[ "$1" == 0 && "$2" == "$3" ]] && grep -q "DRY-RUN" <<<"$4" && grep -q "DRY-RUN borraria" <<<"$4"' _ "$rc" "$before" "$(snap "$R")" "$out"
check "script: dry-run no escribe estado de monitor" test ! -e "$R/monitor-status/capacity.json"
out=$(cfgrun "$R" BACKUP_MAX_TOTAL_GB=0 BACKUP_MIN_FREE_GB=0 -- retention-capacity.sh --apply 2>&1); rc=$?
check "script: sin limites configurados (0/0) --apply no hace nada (rc 0)" bash -c '[[ "$1" == 0 && "$2" == "$3" ]] && grep -q "nada que hacer" <<<"$4"' _ "$rc" "$before" "$(snap "$R")" "$out"
out=$(cfgrun "$R" BACKUP_MAX_TOTAL_GB="$LIM" -- retention-capacity.sh --apply 2>&1); rc=$?
check "script --apply: rc 0, borra la cadena MAS ANTIGUA completa y deja las otras 3 intactas" bash -c '[[ "$1" == 0 ]]' _ "$rc"
check "apply: full de hace 4d y sus logs/tar/manifiesto ya no estan" bash -c '! compgen -G "$1/sql/*$2*" >/dev/null && ! compgen -G "$1/files/*$3*" >/dev/null' _ "$R" "$(stamp $((4*D)))" "$(stamp $((4*D+30)))"
check "apply: quedan 3 fulls (3d, 2d, 1d)" test "$(ls "$R/sql" | grep -c "^${DB}_full_.*\.bak\$")" = 3
check "apply: archivos ajenos intactos (notas.txt y OtraBase)" bash -c '[[ -f "$1/sql/notas.txt" && -f "$1/sql/OtraBase_full_'"$(stamp $((9*D)))"'.bak" ]]' _ "$R"
check "apply: escribio monitor-status/capacity.json con exit 0 y verified true" bash -c 'grep -q "\"exit\":0" "$1/monitor-status/capacity.json" && grep -q "\"verified\":true" "$1/monitor-status/capacity.json"' _ "$R"
check "apply: log claro en backup.log y sin secretos" bash -c 'grep -q "capacidad (local)" "$1/logs/backup.log" && ! grep -qiE "password|secret" "$1/logs/backup.log"' _ "$R"
# idempotente
before=$(snap "$R"); cfgrun "$R" BACKUP_MAX_TOTAL_GB="$LIM" -- retention-capacity.sh --apply >/dev/null 2>&1; rc=$?
check "apply repetido: idempotente (rc 0, nada mas se borra)" bash -c '[[ "$1" == 0 && "$2" == "$3" ]]' _ "$rc" "$before" "$(snap "$R")"

# imposible de cumplir: falla ruidosa y NO borra por debajo del minimo (2 cadenas) ni la ultima
before_full=$(ls "$R/sql" | grep -c '_full_.*\.bak$')
out=$(cfgrun "$R" BACKUP_MAX_TOTAL_GB=0.000001 -- retention-capacity.sh --apply 2>&1); rc=$?
check "imposible: rc 7 (EX_RETENTION) y mensaje DISCO INSUFICIENTE" bash -c '[[ "$1" == 7 ]] && grep -q "DISCO INSUFICIENTE" <<<"$2"' _ "$rc" "$out"
check "imposible: quedan las 2 cadenas minimas (2 fulls) y la mas nueva esta entera" bash -c '[[ $(ls "$1/sql" | grep -c "^TheBuryProjectDb_full_.*.bak$") == 2 ]] && [[ -f "$1/sql/'"${DB}_full_$(stamp $((1*D)))"'.bak" && -f "$1/sql/'"${DB}_full_$(stamp $((1*D)))"'.bak.sha256" ]] && [[ $(ls "$1/sql" | grep -c "_log_.*\.trn$") -ge 2 ]]' _ "$R"
check "imposible: monitor-status/capacity.json exit 7 y verified false (el monitor alerta)" bash -c 'grep -q "\"exit\":7" "$1/monitor-status/capacity.json" && grep -q "\"verified\":false" "$1/monitor-status/capacity.json"' _ "$R"
check "imposible: el estado conserva last_success de la corrida OK anterior" bash -c 'grep -qE "\"last_success\":[0-9]+" "$1/monitor-status/capacity.json"' _ "$R"
# minimo configurable: con BACKUP_CAPACITY_MIN_CHAINS=2 ya estan solo 2 -> no se borra mas
out=$(cfgrun "$R" BACKUP_MAX_TOTAL_GB=0.000001 BACKUP_CAPACITY_MIN_CHAINS=3 -- retention-capacity.sh --apply 2>&1); rc=$?
check "minimo: MIN_CHAINS=3 con 2 cadenas -> rc 7 y 0 borrados" bash -c '[[ "$1" == 7 && $(ls "$2/sql" | grep -c "^TheBuryProjectDb_full_.*.bak$") == 2 ]]' _ "$rc" "$R"
cfgrun "$R" BACKUP_MAX_TOTAL_GB=0.000001 BACKUP_CAPACITY_MIN_CHAINS=1 -- retention-capacity.sh --apply >/dev/null 2>&1; rc=$?
check "minimo: MIN_CHAINS=1 deja EXACTAMENTE la ultima cadena (rc 7 porque aun excede) y no la borra" bash -c '[[ "$1" == 7 && $(ls "$2/sql" | grep -c "^TheBuryProjectDb_full_.*.bak$") == 1 && -f "$2/sql/'"${DB}_full_$(stamp $((1*D)))"'.bak" ]]' _ "$rc" "$R"
cfgrun "$R" BACKUP_MAX_TOTAL_GB=0.000001 BACKUP_CAPACITY_MIN_CHAINS=0 -- retention-capacity.sh --apply >/dev/null 2>&1; rc=$?
check "config: BACKUP_CAPACITY_MIN_CHAINS=0 se rechaza (rc 1)" test "$rc" = 1
cfgrun "$R" BACKUP_MAX_TOTAL_GB=abc -- retention-capacity.sh >/dev/null 2>&1; rc=$?
check "config: BACKUP_MAX_TOTAL_GB invalido se rechaza (rc 1)" test "$rc" = 1

# rutas peligrosas
for bad_dir in "" "/" "$REPO" "$T/home"; do
  cfgrun "${bad_dir:-}" BACKUP_MAX_TOTAL_GB=0.000001 -- retention-capacity.sh --apply >/dev/null 2>&1; rc=$?
  check "ruta peligrosa '${bad_dir:-<vacia>}' -> rechazada (rc 1)" bash -c '[[ "$1" == 1 || "$1" == 8 ]]' _ "$rc"
done
cfgrun "$T/s1/../s1" BACKUP_MAX_TOTAL_GB=0.000001 -- retention-capacity.sh --apply >/dev/null 2>&1; rc=$?
check "ruta con '..' -> rechazada (rc 1)" test "$rc" = 1
R2=$(newbk nomarca); rm "$R2/.bury-backup-root"; mkchain "$R2" $((2*D)) 1 1000 0; mkchain "$R2" $((1*D)) 1 1000 0; b2=$(snap "$R2")
cfgrun "$R2" BACKUP_MAX_TOTAL_GB=0.000001 BACKUP_CAPACITY_MIN_CHAINS=1 -- retention-capacity.sh --apply >/dev/null 2>&1; rc=$?
check "directorio SIN marca .bury-backup-root: rechazado (rc 1) y 0 borrados" bash -c '[[ "$1" == 1 && "$2" == "$3" ]]' _ "$rc" "$b2" "$(snap "$R2")"
# BACKUP_DIR = symlink a un directorio valido: se resuelve y funciona sobre el destino real
R3=$(newbk real3); mkchain "$R3" $((2*D)) 1 1000 0; mkchain "$R3" $((1*D)) 1 1000 0; ln -s "$R3" "$T/link3"
cfgrun "$T/link3" BACKUP_MAX_TOTAL_GB=0.000001 BACKUP_CAPACITY_MIN_CHAINS=1 -- retention-capacity.sh --apply >/dev/null 2>&1; rc=$?
check "BACKUP_DIR symlink a directorio valido: se resuelve y opera dentro del destino real (deja 1 cadena)" bash -c '[[ $(ls "$1/sql" | grep -c "^TheBuryProjectDb_full_.*.bak$") == 1 ]]' _ "$R3"
# archivos que son symlinks a OTRO lugar: nunca se borran ni se siguen
R4=$(newbk sym4); mkchain "$R4" $((3*D)) 1 1000 0; mkchain "$R4" $((2*D)) 1 1000 0; mkchain "$R4" $((1*D)) 1 1000 0
echo "precioso" >"$T/fuera.bak"; ln -s "$T/fuera.bak" "$R4/sql/${DB}_full_$(stamp $((5*D))).bak"; ln -s "$T/fuera.bak" "$R4/sql/${DB}_full_$(stamp $((5*D))).bak.sha256"
out=$(cfgrun "$R4" BACKUP_MAX_TOTAL_GB=0.000001 BACKUP_CAPACITY_MIN_CHAINS=1 -- retention-capacity.sh --apply 2>&1)
check "symlink con nombre de backup: ignorado con WARN, el enlace y su destino siguen intactos" bash -c '[[ -L "$1/sql/'"${DB}_full_$(stamp $((5*D)))"'.bak" && "$(cat "$2/fuera.bak")" == precioso ]] && grep -q "enlace simbolico ignorado" <<<"$3"' _ "$R4" "$T" "$out"
# sql/ como symlink a un directorio con backups: no se sigue, no se borra nada fuera
R5=$(newbk sym5); rm -rf "$R5/sql"; mkdir -p "$T/otro-sql"; ln -s "$T/otro-sql" "$R5/sql"
mkchain "$T/ext" $((2*D)) 1 1000 0; cp -r "$T/ext/sql/." "$T/otro-sql/"; b5=$(ls "$T/otro-sql" | wc -l)
cfgrun "$R5" BACKUP_MAX_TOTAL_GB=0.000001 BACKUP_CAPACITY_MIN_CHAINS=1 -- retention-capacity.sh --apply >/dev/null 2>&1
check "sql/ es un symlink: no se sigue y NO se borra nada fuera de BACKUP_DIR" test "$(ls "$T/otro-sql" | wc -l)" = "$b5"
# el fragmento de borrado rechaza rutas fuera de sql/ files/ aunque el plan estuviera adulterado
out=$(ROOT="$R3" MARK="$R3/.bury-backup-root" sh -c "$CAP_DELETE_SH" cap "../s1/sql/x" "/etc/passwd" 2>&1); rc=$?
check "borrado en el contenedor/host rechaza rutas absolutas o con '..' (rc 3)" test "$rc" = 3
# regresion A10: un `rm` que falla NO puede quedar enmascarado por el echo final. Se simula con un `rm` falso (PATH) que falla en sql/*.bak
# (independiente de ser root: chattr/permisos no son fiables en CI).
RMSTUB="$T/rmstub"; mkdir -p "$RMSTUB"
cat >"$RMSTUB/rm" <<'RMEOF'
#!/bin/sh
for a in "$@"; do case "$a" in sql/*.bak) echo "rm: no se puede borrar '$a': Operation not permitted" >&2; exit 1 ;; esac; done
exec /bin/rm "$@"
RMEOF
chmod +x "$RMSTUB/rm"
R7=$(newbk rmfail7); mkchain "$R7" $((2*D)) 1 1000 0; mkchain "$R7" $((1*D)) 1 1000 0
f7=$(cd "$R7" && ls sql/*_full_*.bak | head -n1)
out=$(PATH="$RMSTUB:$PATH" ROOT="$R7" MARK="$R7/.bury-backup-root" sh -c "$CAP_DELETE_SH" cap "$f7" 2>&1); rc=$?
check "A10 fragmento de borrado: si rm falla sale con rc 4 (no 0) y lo informa" bash -c '[[ "$1" == 4 ]] && grep -q "no se pudo borrar" <<<"$2" && [[ -f "$3/$4" ]]' _ "$rc" "$out" "$R7" "$f7"
R8=$(newbk rmfail8); mkchain "$R8" $((2*D)) 1 1000 0; mkchain "$R8" $((1*D)) 1 1000 0
out=$(PATH="$RMSTUB:$PATH" cfgrun "$R8" BACKUP_MAX_TOTAL_GB=0.000001 BACKUP_CAPACITY_MIN_CHAINS=1 -- retention-capacity.sh --apply 2>&1); rc=$?
check "A10 capacity_run con rm que falla: exit 7 (no se declara exito) y mensaje claro" bash -c '[[ "$1" == 7 ]] && grep -q "el borrado fallo" <<<"$2" && ! grep -q "archivo(s) borrado(s)" <<<"$2"' _ "$rc" "$out"
check "A10 capacity.json refleja el fallo (exit 7, verified false)" bash -c 'grep -q "\"exit\":7" "$1/monitor-status/capacity.json" && grep -q "\"verified\":false" "$1/monitor-status/capacity.json"' _ "$R8"
# concurrencia: con el lock tomado, --apply sale con 4 y no borra
R6=$(newbk lock6); mkchain "$R6" $((2*D)) 1 1000 0; mkchain "$R6" $((1*D)) 1 1000 0; b6=$(snap "$R6")
( exec 9>>"$R6/.lock.flock"; flock -n 9 && sleep 6 ) & LKPID=$!; sleep 1
cfgrun "$R6" BACKUP_MAX_TOTAL_GB=0.000001 BACKUP_CAPACITY_MIN_CHAINS=1 -- retention-capacity.sh --apply >/dev/null 2>&1; rc=$?
check "concurrencia: con el lock de backups tomado --apply sale con 4 (ocupado) y no borra" bash -c '[[ "$1" == 4 && "$2" == "$3" ]]' _ "$rc" "$b6" "$(snap "$R6")"
kill "$LKPID" 2>/dev/null; wait "$LKPID" 2>/dev/null

# ================================================================================================================ 3. destino extraible
newrem() { mkdir -p "$T/$1"; echo "$T/$1"; }
S=$(newbk src); mkchain "$S" $((3*D)) 2 1000 1; mkchain "$S" $((2*D)) 2 1000 1; mkchain "$S" $((1*D)) 2 1000 1
REM="$T/rem-ausente"   # no existe (disco desconectado)
out=$(cfgrun "$S" BACKUP_REMOVABLE_DIR="$REM" BACKUP_REMOVABLE_REQUIRE_MOUNT=false -- copy-removable.sh --apply 2>&1); rc=$?
check "extraible AUSENTE: rc 0 (no falla), WARN 'atrasada', no crea nada" bash -c '[[ "$1" == 0 && ! -e "$2" ]] && grep -q "NO disponible" <<<"$3" && grep -q "ATRASADA" <<<"$3"' _ "$rc" "$REM" "$out"
check "extraible ausente: monitor-status/removable-copy.json exit 1, verified false, mensaje 'atrasada'" bash -c 'grep -q "\"exit\":1" "$1/monitor-status/removable-copy.json" && grep -q "\"verified\":false" "$1/monitor-status/removable-copy.json" && grep -q "atrasada" "$1/monitor-status/removable-copy.json"' _ "$S"
cfgrun "$S" BACKUP_REMOVABLE_DIR="$REM" BACKUP_REMOVABLE_REQUIRE_MOUNT=false -- copy-removable.sh --apply --strict >/dev/null 2>&1; rc=$?
check "extraible ausente con --strict: rc 1" test "$rc" = 1
REM=$(newrem rem1)   # existe pero SIN marca (podria ser un punto de montaje vacio en el disco del servidor)
out=$(cfgrun "$S" BACKUP_REMOVABLE_DIR="$REM" BACKUP_REMOVABLE_REQUIRE_MOUNT=false -- copy-removable.sh --apply 2>&1); rc=$?
check "extraible sin archivo-marca: no copia nada (rc 0, directorio vacio, WARN)" bash -c '[[ "$1" == 0 && -z "$(ls -A "$2")" ]] && grep -q "archivo-marca" <<<"$3"' _ "$rc" "$REM" "$out"
touch "$REM/.bury-backup-removable"
out=$(cfgrun "$S" BACKUP_REMOVABLE_DIR="$REM" BACKUP_REMOVABLE_REQUIRE_MOUNT=true -- copy-removable.sh --apply 2>&1); rc=$?
check "extraible con REQUIRE_MOUNT=true en un directorio que NO es punto de montaje: no copia" bash -c '[[ "$1" == 0 && ! -d "$2/bury-backup" ]] && grep -q "punto de montaje" <<<"$3"' _ "$rc" "$REM" "$out"
out=$(cfgrun "$S" BACKUP_REMOVABLE_DIR="$REM" BACKUP_REMOVABLE_REQUIRE_MOUNT=false -- copy-removable.sh 2>&1); rc=$?
check "extraible --dry-run (default): no escribe en el destino (sin bury-backup/)" bash -c '[[ "$1" == 0 && ! -d "$2/bury-backup" ]]' _ "$rc" "$REM"
out=$(cfgrun "$S" BACKUP_REMOVABLE_DIR="$REM" BACKUP_REMOVABLE_REQUIRE_MOUNT=false -- copy-removable.sh --apply 2>&1); rc=$?
check "extraible presente: --apply rc 0 y 'AL DIA'" bash -c '[[ "$1" == 0 ]] && grep -q "AL DIA" <<<"$2"' _ "$rc" "$out"
check "extraible: las 3 cadenas copiadas (27 archivos) e identicas byte a byte al origen" bash -c '[[ $(find "$1/bury-backup" -type f | wc -l) == 27 ]] && ( cd "$2" && for p in sql/* files/*; do cmp -s "$p" "$1/bury-backup/$p" || exit 1; done )' _ "$REM" "$S"
check "extraible: sha256 de cada .bak/.trn/.tar.gz copiado coincide con su .sha256" bash -c 'cd "$1/bury-backup/sql" && sha256sum -c --quiet *.bak.sha256 *.trn.sha256 && cd ../files && sha256sum -c --quiet *.tar.gz.sha256' _ "$REM"
check "extraible: sin restos .partial" bash -c '! find "$1" -name "*.partial" | grep -q .' _ "$REM"
check "extraible: removable-copy.json exit 0 verified true 'al dia'" bash -c 'grep -q "\"exit\":0" "$1/monitor-status/removable-copy.json" && grep -q "\"verified\":true" "$1/monitor-status/removable-copy.json" && grep -q "al dia" "$1/monitor-status/removable-copy.json" && grep -qE "\"last_success\":[0-9]+" "$1/monitor-status/removable-copy.json"' _ "$S"
check "extraible: el origen no se modifica (3 cadenas intactas)" bash -c '[[ $(ls "$1/sql" | grep -c "^TheBuryProjectDb_full_.*.bak$") == 3 ]]' _ "$S"
out=$(cfgrun "$S" BACKUP_REMOVABLE_DIR="$REM" BACKUP_REMOVABLE_REQUIRE_MOUNT=false -- copy-removable.sh --apply 2>&1)
check "extraible: segunda corrida copia 0 archivos (idempotente)" bash -c 'grep -q "AL DIA" <<<"$1" && grep -q "(cadena .*, 0 archivo(s) copiados)" <<<"$1"' _ "$out"
# un destino extraible es de UNA instalacion (P-06 cierre tecnico): la primera copia graba BACKUP_DIR en la marca y otra instalacion no puede usarlo
check "extraible: la primera copia graba el dueño (BACKUP_DIR) en la marca" test "$(head -n1 "$REM/.bury-backup-removable")" = "$S"
S3=$(newbk src3); mkchain "$S3" $((1*D)) 1 1000 1
hrem=$(cd "$REM" && find bury-backup -type f | sort | xargs sha256sum | sha256sum | cut -d' ' -f1)
out=$(cfgrun "$S3" BACKUP_REMOVABLE_DIR="$REM" BACKUP_REMOVABLE_REQUIRE_MOUNT=false -- copy-removable.sh --apply 2>&1); rc=$?
check "extraible de OTRA instalacion: rc 0 (no rompe el backup), no copia nada y avisa 'OTRA instalacion'" bash -c '[[ "$1" == 0 ]] && grep -q "OTRA instalacion" <<<"$2" && [[ "$(cd "$3" && find bury-backup -type f | sort | xargs sha256sum | sha256sum | cut -d" " -f1)" == "$4" ]]' _ "$rc" "$out" "$REM" "$hrem"
check "extraible de OTRA instalacion: estado 'atrasada' (exit 1, verified false) en el monitor de la intrusa" bash -c 'grep -q "\"exit\":1" "$1/monitor-status/removable-copy.json" && grep -q "\"verified\":false" "$1/monitor-status/removable-copy.json"' _ "$S3"
cfgrun "$S3" BACKUP_REMOVABLE_DIR="$REM" BACKUP_REMOVABLE_REQUIRE_MOUNT=false -- copy-removable.sh --apply --strict >/dev/null 2>&1; rc=$?
check "extraible de OTRA instalacion con --strict: rc 1" test "$rc" = 1
# marca no escribible (reclamo imposible): fail-closed, no copia nada y no la deja "libre"
S4=$(newbk src4); mkchain "$S4" $((1*D)) 1 1000 1; REM4=$(newrem rem4); : >"$REM4/.bury-backup-removable"
if [[ "$(id -u)" != 0 ]] && chmod 0444 "$REM4/.bury-backup-removable" 2>/dev/null && [[ ! -w "$REM4/.bury-backup-removable" ]]; then
  out=$(cfgrun "$S4" BACKUP_REMOVABLE_DIR="$REM4" BACKUP_REMOVABLE_REQUIRE_MOUNT=false -- copy-removable.sh --apply 2>&1); rc=$?
  check "marca no escribible: rc 0, NO copia nada (fail-closed) y avisa" bash -c '[[ "$1" == 0 && ! -d "$2/bury-backup/sql" ]] && grep -q "NO disponible" <<<"$3"' _ "$rc" "$REM4" "$out"
else echo "SKIP marca no escribible: el entorno no respeta chmod o corre como root"; fi
check "extraible de OTRA instalacion: la marca del dueño no se pisa" test "$(head -n1 "$REM/.bury-backup-removable")" = "$S"
# atrasada: llega un log nuevo al origen y el disco esta desconectado
mkf "$S" "sql/${DB}_log_$(stamp 60).trn" 100; shaf "$S" "sql/${DB}_log_$(stamp 60).trn"
mv "$REM" "$T/rem1-desconectado"
cfgrun "$S" BACKUP_REMOVABLE_DIR="$REM" BACKUP_REMOVABLE_REQUIRE_MOUNT=false -- copy-removable.sh --apply >/dev/null 2>&1; rc=$?
check "desconectado tras una copia al dia: rc 0, estado 'atrasada' (exit 1, verified false) y CONSERVA last_success" bash -c '[[ "$1" == 0 ]] && grep -q "\"exit\":1" "$2/monitor-status/removable-copy.json" && grep -qE "\"last_success\":[0-9]+" "$2/monitor-status/removable-copy.json" && grep -q atrasada "$2/monitor-status/removable-copy.json"' _ "$rc" "$S"
mv "$T/rem1-desconectado" "$REM"
cfgrun "$S" BACKUP_REMOVABLE_DIR="$REM" BACKUP_REMOVABLE_REQUIRE_MOUNT=false -- copy-removable.sh --apply >/dev/null 2>&1; rc=$?
check "reconectado: se pone al dia (el log nuevo se copia) y vuelve a verified true" bash -c '[[ "$1" == 0 && -f "$2/bury-backup/sql/'"${DB}_log_$(stamp 60)"'.trn" ]] && grep -q "\"verified\":true" "$3/monitor-status/removable-copy.json"' _ "$rc" "$REM" "$S"
# integridad: el origen del full mas nuevo esta CORRUPTO respecto de su .sha256 -> no se copia como valido
S2=$(newbk src2); mkchain "$S2" $((2*D)) 1 1000 0; mkchain "$S2" $((1*D)) 1 1000 0
nf="$S2/sql/${DB}_full_$(stamp $((1*D))).bak"; printf 'Z' | dd of="$nf" bs=1 seek=10 conv=notrunc 2>/dev/null
REM2=$(newrem rem2); touch "$REM2/.bury-backup-removable"
out=$(cfgrun "$S2" BACKUP_REMOVABLE_DIR="$REM2" BACKUP_REMOVABLE_REQUIRE_MOUNT=false -- copy-removable.sh --apply 2>&1); rc=$?
check "integridad: full de origen corrupto (no coincide con su sha256) -> NO queda copiado (ni .partial) y estado 'atrasada'" bash -c '[[ "$1" == 0 && ! -f "$2/bury-backup/sql/'"${DB}_full_$(stamp $((1*D)))"'.bak" ]] && ! find "$2" -name "*.partial" | grep -q . && grep -q "\"exit\":1" "$3/monitor-status/removable-copy.json"' _ "$rc" "$REM2" "$S2"
check "integridad: el mensaje indica el fallo de sha256 sin imprimir contenido" bash -c 'grep -q "sha256" "$1/monitor-status/removable-copy.json"' _ "$S2"
# capacidad del extraible: solo cabe 1 cadena -> copia la MAS NUEVA y las antiguas quedan pendientes (al dia en la mas nueva)
S3=$(newbk src3); mkchain "$S3" $((3*D)) 1 1000 0; mkchain "$S3" $((2*D)) 1 1000 0; mkchain "$S3" $((1*D)) 1 1000 0
CHB=$(( $(total_of "$S3") / 3 )); LIMR=$(awk -v b=$((CHB + CHB/2)) 'BEGIN{printf "%.9f", b/1073741824}')
REM3=$(newrem rem3); touch "$REM3/.bury-backup-removable"
cfgrun "$S3" BACKUP_REMOVABLE_DIR="$REM3" BACKUP_REMOVABLE_REQUIRE_MOUNT=false BACKUP_REMOVABLE_MAX_TOTAL_GB="$LIMR" -- copy-removable.sh --apply >/dev/null 2>&1; rc=$?
check "extraible chico (cabe 1 cadena): copia SOLO la mas nueva, completa, y queda 'al dia en la cadena mas nueva'" bash -c '[[ "$1" == 0 && $(ls "$2/bury-backup/sql" | grep -c "^TheBuryProjectDb_full_.*.bak$") == 1 && -f "$2/bury-backup/sql/'"${DB}_full_$(stamp $((1*D)))"'.bak" ]] && grep -q "pendientes" "$3/monitor-status/removable-copy.json" && grep -q "\"verified\":true" "$3/monitor-status/removable-copy.json"' _ "$rc" "$REM3" "$S3"
# la retencion por capacidad del ORIGEN no toca el extraible
before_rem=$(snap "$REM3/bury-backup")
cfgrun "$S3" BACKUP_MAX_TOTAL_GB=0.000001 BACKUP_CAPACITY_MIN_CHAINS=1 -- retention-capacity.sh --apply >/dev/null 2>&1
check "la capacidad del origen NO borra del extraible (copias independientes)" test "$(snap "$REM3/bury-backup")" = "$before_rem"
# config: extraible dentro de BACKUP_DIR o con espacios se rechaza
cfgrun "$S3" BACKUP_REMOVABLE_DIR="$S3/dentro" -- copy-removable.sh >/dev/null 2>&1; rc=$?
check "config: BACKUP_REMOVABLE_DIR dentro de BACKUP_DIR -> rc 1" test "$rc" = 1
cfgrun "$S3" BACKUP_REMOVABLE_DIR="relativo/x" -- copy-removable.sh >/dev/null 2>&1; rc=$?
check "config: BACKUP_REMOVABLE_DIR relativo -> rc 1 (no se asume letra de unidad ni ruta)" test "$rc" = 1
cfgrun "$S3" -- copy-removable.sh >/dev/null 2>&1; rc=$?
check "config: sin BACKUP_REMOVABLE_DIR el script avisa (rc 1) y backup.sh no lo invoca" test "$rc" = 1

# ================================================================================================================ 4. monitor
if command -v python3 >/dev/null 2>&1 && [[ -f "$REPO/scripts/monitoring/test_monitor.py" ]]; then
  out=$(cd "$REPO/scripts/monitoring" && python3 test_monitor.py 2>&1); rc=$?
  check "monitor: test_monitor.py (incluye capacity y removable-copy) rc 0" bash -c '[[ "$1" == 0 ]]' _ "$rc"
  [[ "$rc" == 0 ]] || echo "$out" | tail -n 15
else
  echo "SKIP monitor: python3 no disponible"
fi

# ================================================================================================================ 5. SQL Server REAL (--db)
if (( WITH_DB )); then
  echo; echo "=== SQL Server real descartable (restore real de lo que queda tras la retencion por capacidad) ==="
  [[ "$(uname -s)" == Linux ]] || { echo "--db requiere Linux/WSL"; exit 1; }
  for c in docker flock; do command -v "$c" >/dev/null 2>&1 || { echo "falta $c"; exit 1; }; done
  rnd() { od -An -N"${1:-6}" -tx1 /dev/urandom | tr -d ' \n'; }
  PROJ="bury-bkcap-$(rnd 4)"; DBN=BuryCapSim
  BKD="$T/dbbackups"; mkdir -p "$BKD"
  SA="Aa1!$(rnd 10)"
  used=$(docker network ls -q | xargs -r docker network inspect --format '{{range .IPAM.Config}}{{.Subnet}} {{end}}' 2>/dev/null)
  SUBNET=""; for i in $(seq 1 80); do n=$((100 + RANDOM % 120)); [[ " $used " == *" 172.29.$n.0/24 "* ]] || { SUBNET="172.29.$n.0/24"; break; }; done
  [[ -n "$SUBNET" ]] || { echo "sin subred libre"; exit 1; }
  export COMPOSE_PROJECT_NAME="$PROJ" COMPOSE_ENV_FILES="$T/empty.env" BURY_NET_SUBNET="$SUBNET" MSSQL_SA_PASSWORD="$SA" MSSQL_DATABASE="$DBN" MSSQL_PID=Express DB_MEMORY=2g DB_CPUS=2
  export BACKUP_DIR="$BKD" BACKUP_STOP_APP=false BACKUP_RETENTION_DAYS=14 BACKUP_RETENTION_MIN_KEEP=3
  export ERP_DOMAIN=tbp ADMIN_EMAIL=bk@example.invalid ADMIN_USERNAME=admin ADMIN_PASSWORD=Zz9!unused0 ERP_DB_USER=u ERP_DB_PASSWORD=x ERP_MIGRATION_USER=m ERP_MIGRATION_PASSWORD=y ERP_IMAGE=bury-bkcap-noexiste:0
  unset BK_CAP_HOSTFS   # aqui el borrado va por el contenedor backup-files real
  dcq() { docker compose --progress quiet "$@" </dev/null; }
  DBC_CLEAN='docker compose --progress quiet -p '"$PROJ"' down -v --remove-orphans >/dev/null 2>&1; docker volume ls -q --filter label=com.docker.compose.project='"$PROJ"' | xargs -r docker volume rm >/dev/null 2>&1; docker run --rm --network none -v '"$T"':/t alpine:3 find /t/dbbackups -mindepth 1 -delete >/dev/null 2>&1; echo "residuos: contenedores=$(docker ps -aq --filter label=com.docker.compose.project='"$PROJ"' | wc -l) volumenes=$(docker volume ls -q --filter label=com.docker.compose.project='"$PROJ"' | wc -l)"'
  [[ "$(cd "$REPO" && dcq config 2>/dev/null | sed -n 's/^name: //p')" == "$PROJ" ]] || { echo "ABORTADO: el proyecto Compose resuelto no es $PROJ"; exit 1; }
  cd "$REPO"
  bash "$HERE/prepare-dir.sh" >/dev/null 2>&1 || { echo "prepare-dir.sh fallo"; exit 1; }
  dcq up -d --no-deps db >/dev/null 2>&1 || { echo "no se pudo levantar db"; exit 1; }
  for i in $(seq 1 60); do [[ "$(dcq ps --format '{{.Health}}' db 2>/dev/null | head -n1)" == healthy ]] && break; sleep 3; done
  sqlq() { dcq exec -T db bash -c 'SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" exec /opt/mssql-tools18/bin/sqlcmd -C -b -I -S localhost -U sa -d master -h -1 -W -Q "SET NOCOUNT ON; $0"' "$1" | tr -d '\r'; }
  [[ "$(sqlq 'SELECT 1')" == 1 ]] || { echo "SQL Server no responde"; exit 1; }
  sqlq "CREATE DATABASE [$DBN]" >/dev/null; sqlq "ALTER DATABASE [$DBN] SET RECOVERY FULL" >/dev/null
  sqlq "CREATE TABLE [$DBN].dbo.t (id int PRIMARY KEY, v char(1500))" >/dev/null
  lsql() { dcq run --rm -T --no-deps --quiet-pull backup-files ls -1 /backups/sql | tr -d '\r'; }   # sql/ es de uid 10001 (750): se lista desde el contenedor
  lsql_sizes() { dcq run --rm -T --no-deps --quiet-pull backup-files sh -c 'for f in /backups/sql/*_full_*.bak; do stat -c %s "$f"; done' | tr -d '\r'; }
  ins() { sqlq "INSERT INTO [$DBN].dbo.t (id, v) SELECT $1 + n, REPLICATE('x',1500) FROM (SELECT TOP $2 ROW_NUMBER() OVER (ORDER BY (SELECT 1)) n FROM sys.all_columns) q"; }
  cnt() { sqlq "SELECT COUNT(*) FROM [$1].dbo.t" | head -n1; }
  bk() { bash "$HERE/backup.sh" "$@" >"$T/bk.out" 2>&1; return $?; }
  # 3 cadenas reales (cada una: full + log), sin limites
  for k in 1 2 3; do
    ins $((k*100000)) $((k*300)); bk full; r1=$?; sleep 1; ins $((k*100000+50000)) 50; bk log; r2=$?
    check "DB: cadena $k generada (full rc 0, log rc 0)" bash -c '[[ "$1" == 0 && "$2" == 0 ]]' _ "$r1" "$r2"
    sleep 1
  done
  NFULL=$(lsql | grep -c '_full_.*\.bak$'); check "DB: 3 fulls reales en BACKUP_DIR" test "$NFULL" = 3
  # 4a cadena SIN limites; despues se mide el tamaño real de cada cadena y se fija un tope que obliga a borrar SOLO la mas antigua
  ins 9000 50; sleep 1; bk full; sleep 1
  ROWS_FINAL=$(cnt "$DBN")
  dry=$(BACKUP_MAX_TOTAL_GB=0.000001 BACKUP_CAPACITY_MIN_CHAINS=1 bash "$HERE/retention-capacity.sh" 2>&1)
  TOTB=$(grep -oE ' total=[0-9]+' <<<"$dry" | head -n1 | tr -dc 0-9); C1B=$(grep -oE 'cadena [0-9_Z-]+ [0-9]+' <<<"$dry" | head -n1 | awk '{print $3}')
  check "DB: dry-run de la capacidad sobre archivos reales (4 cadenas, no borra nada)" bash -c '[[ -n "$1" && -n "$2" && $(grep -c "DRY-RUN borraria" <<<"$3") -ge 1 ]]' _ "$TOTB" "$C1B" "$dry"
  [[ $(lsql | grep -c '_full_.*\.bak$') == 4 ]] && ok "DB: el dry-run no borro nada (siguen los 4 fulls)" || bad "DB: el dry-run no borro nada (siguen los 4 fulls)"
  LIMDB=$(awk -v b=$((TOTB - C1B + 400000)) 'BEGIN{printf "%.9f", b/1073741824}')
  BACKUP_MAX_TOTAL_GB="$LIMDB" BACKUP_CAPACITY_MIN_CHAINS=2 bash "$HERE/backup.sh" log >"$T/bk.out" 2>&1; rc=$?
  check "DB: backup (log) con tope activo -> rc 0 y retencion por capacidad ejecutada (log)" bash -c '[[ "$1" == 0 ]] && grep -q "retencion por capacidad" "$2/logs/backup.log"' _ "$rc" "$BKD" || { echo "     rc=$rc"; tail -n 12 "$T/bk.out" | cut -c1-250 | sed 's/^/     | /'; }
  OLDEST_GONE=$(lsql | grep -c '_full_.*\.bak$')
  check "DB: se borro SOLO la cadena mas antigua (quedan exactamente 3 fulls reales)" test "$OLDEST_GONE" = 3
  NEWEST=$(lsql | grep '_full_.*\.bak$' | sort | tail -n1)
  OLDEST_KEPT=$(lsql | grep '_full_.*\.bak$' | sort | head -n1); OT=${OLDEST_KEPT#${DBN}_full_}; OT=${OT%.bak}
  check "DB: no quedan logs huerfanos (ningun .trn anterior al full mas antiguo que sobrevive)" bash -c 'for f in $(grep "_log_.*.trn$" <<<"$1"); do t=${f#*_log_}; t=${t%.trn}; [[ ! "$t" < "$2" ]] || exit 1; done' _ "$(lsql)" "$OT"
  # restore REAL del full mas antiguo que sobrevive CON logs, y del mas nuevo
  bash "$HERE/restore-sql.sh" --backup "$OLDEST_KEPT" --target-db "${DBN}_RO" --with-logs >"$T/r1.out" 2>&1; rc=$?
  check "DB: restore real del full mas antiguo que sobrevive + todos sus logs restantes -> rc 0" test "$rc" = 0
  check "DB: lo restaurado tiene los datos finales (filas = $ROWS_FINAL o mas con el ultimo full)" bash -c '[[ "$(cat "$1")" -ge "$2" ]]' _ <(cnt "${DBN}_RO") "$ROWS_FINAL"
  bash "$HERE/restore-sql.sh" --backup "$NEWEST" --target-db "${DBN}_RN" >"$T/r2.out" 2>&1; rc=$?
  check "DB: restore real del full mas nuevo -> rc 0 y datos completos" bash -c '[[ "$1" == 0 && "$(cat "$2")" -ge $3 ]]' _ "$rc" <(cnt "${DBN}_RN") "$ROWS_FINAL"
  # imposible: tope absurdo -> exit 7 y la ultima cadena (full + logs) sigue restaurable
  bk log; : # un log mas de la cadena mas nueva
  BACKUP_MAX_TOTAL_GB=0.000001 BACKUP_CAPACITY_MIN_CHAINS=1 bash "$HERE/backup.sh" log >"$T/bk.out" 2>&1; rc=$?
  check "DB: tope imposible -> backup sale con exit 7 y deja SOLO la ultima cadena" bash -c '[[ "$1" == 7 && $(grep -c "_full_.*.bak$" <<<"$2") == 1 ]]' _ "$rc" "$(lsql)"
  check "DB: monitor-status/capacity.json exit 7 verified false" bash -c 'grep -q "\"exit\":7" "$1/monitor-status/capacity.json" && grep -q "\"verified\":false" "$1/monitor-status/capacity.json"' _ "$BKD"
  LAST=$(lsql | grep '_full_.*\.bak$' | sort | tail -n1)
  bash "$HERE/restore-sql.sh" --backup "$LAST" --target-db "${DBN}_RL" --with-logs >"$T/r3.out" 2>&1; rc=$?
  check "DB: tras la falla ruidosa, la ULTIMA cadena restaura (rc 0) con todos sus datos" bash -c '[[ "$1" == 0 && "$(cat "$2")" -ge "$3" ]]' _ "$rc" <(cnt "${DBN}_RL") "$ROWS_FINAL"
  # extraible real (ruta del host) con el contenedor backup-files: copia cadenas y verifica sha256
  REMD="$T/dbremovable"; mkdir -p "$REMD"; touch "$REMD/.bury-backup-removable"
  BACKUP_REMOVABLE_DIR="$REMD" BACKUP_REMOVABLE_REQUIRE_MOUNT=false bash "$HERE/copy-removable.sh" --apply >"$T/rm.out" 2>&1; rc=$?
  check "DB: copia al extraible via contenedor backup-files -> rc 0 'AL DIA' y full+logs identicos" bash -c '[[ "$1" == 0 ]] && grep -q "AL DIA" "$2" && docker compose --progress quiet run --rm -T --no-deps --quiet-pull -v "$4:/removable:ro" backup-files sh -c "n=0; for f in /backups/sql/*; do cmp -s \"\$f\" \"/removable/bury-backup/sql/\${f##*/}\" || exit 1; n=\$((n+1)); done; [ \$n -ge 3 ]" </dev/null' _ "$rc" "$T/rm.out" "$BKD" "$REMD"
  docker run --rm --network none -v "$T:/t" alpine:3 chown -R "$(id -u):$(id -g)" /t/dbremovable >/dev/null 2>&1
fi

echo
echo "RESULTADO: PASS=$PASS FAIL=$FAIL"
[[ "$FAIL" == 0 ]]
