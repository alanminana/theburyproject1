#!/usr/bin/env bash
# Retencion POR CAPACIDAD y copia a destino EXTRAIBLE (se hace `source` DESPUES de lib.sh; no se ejecuta). Documentacion: docs/backup-restore.md §8.2.
#
# Unidad de borrado = CADENA recuperable: un full completo (<db>_full_<ts>.bak + .sha256) + los logs <db>_log_<ts>.trn con ts en [este full, el
# siguiente full) + el tar de archivos (files_<ts>.tar.gz + .sha256 + .caseal) y el manifiesto set_<ts-tar>_<ts-full>.txt que el par acredita
# (el manifiesto manda: un tar sigue al full que nombra su manifiesto; sin manifiesto, al full anterior por fecha). Se borra de la cadena MAS
# ANTIGUA a la mas nueva y NUNCA por debajo de BACKUP_CAPACITY_MIN_CHAINS (>= 1: la ultima cadena completa jamas se borra); si aun asi no
# alcanza, FALLA RUIDOSAMENTE (exit 7, monitor-status/capacity.json, alerta del monitor) en vez de borrar lo irrecuperable.
#
# Capas: bk_chains y capacity_plan son PURAS (sin I/O; testeables). cap_run ejecuta el listado/borrado/copia dentro del contenedor
# `backup-files` (root: los .bak son de uid 10001 y el host no siempre puede listarlos), con las mismas guardas que retention.sh.
# BK_CAP_HOSTFS=1 (solo tests/desarrollo) ejecuta esos mismos fragmentos sh directamente sobre el filesystem del host.

# ------------------------------------------------------------------------------------------------------------------ configuracion
# gb_to_bytes <decimal>  -> bytes enteros (acepta hasta 12 decimales); rc 1 si no es un numero no negativo
gb_to_bytes() {
  [[ "${1:-}" =~ ^[0-9]{1,6}(\.[0-9]{1,12})?$ ]] || return 1
  awk -v g="$1" 'BEGIN { printf "%.0f", g * 1073741824 }'
}

load_capacity_config() {
  local v
  v=$(cfg BACKUP_MAX_TOTAL_GB 0);          MAX_TOTAL_BYTES=$(gb_to_bytes "$v")   || die $EX_USAGE "BACKUP_MAX_TOTAL_GB invalido (numero >= 0; 0 = sin tope)"
  v=$(cfg BACKUP_MIN_FREE_GB 0);           MIN_FREE_BYTES=$(gb_to_bytes "$v")    || die $EX_USAGE "BACKUP_MIN_FREE_GB invalido (numero >= 0; 0 = sin minimo)"
  CAPACITY_MIN_CHAINS=$(cfg BACKUP_CAPACITY_MIN_CHAINS 2)
  is_uint "$CAPACITY_MIN_CHAINS" && (( ${#CAPACITY_MIN_CHAINS} <= 3 && 10#$CAPACITY_MIN_CHAINS >= 1 )) \
    || die $EX_USAGE "BACKUP_CAPACITY_MIN_CHAINS debe ser un entero >= 1 (cadenas recuperables que la capacidad NUNCA borra)"
  CAPACITY_MIN_CHAINS=$((10#$CAPACITY_MIN_CHAINS))
  BACKUP_REMOVABLE_DIR=$(cfg BACKUP_REMOVABLE_DIR "")
  BACKUP_REMOVABLE_DIR=${BACKUP_REMOVABLE_DIR%/}
  BACKUP_REMOVABLE_MARKER=$(cfg BACKUP_REMOVABLE_MARKER .bury-backup-removable)
  [[ "$BACKUP_REMOVABLE_MARKER" =~ ^[A-Za-z0-9._-]+$ && "$BACKUP_REMOVABLE_MARKER" != . && "$BACKUP_REMOVABLE_MARKER" != .. ]] \
    || die $EX_USAGE "BACKUP_REMOVABLE_MARKER invalido (solo letras, digitos, . _ -)"
  BACKUP_REMOVABLE_REQUIRE_MOUNT=$(cfg BACKUP_REMOVABLE_REQUIRE_MOUNT true)
  [[ "$BACKUP_REMOVABLE_REQUIRE_MOUNT" == true || "$BACKUP_REMOVABLE_REQUIRE_MOUNT" == false ]] || die $EX_USAGE "BACKUP_REMOVABLE_REQUIRE_MOUNT debe ser true|false"
  v=$(cfg BACKUP_REMOVABLE_MAX_TOTAL_GB 0); REM_MAX_TOTAL_BYTES=$(gb_to_bytes "$v") || die $EX_USAGE "BACKUP_REMOVABLE_MAX_TOTAL_GB invalido"
  v=$(cfg BACKUP_REMOVABLE_MIN_FREE_GB 0);  REM_MIN_FREE_BYTES=$(gb_to_bytes "$v")  || die $EX_USAGE "BACKUP_REMOVABLE_MIN_FREE_GB invalido"
  if [[ -n "$BACKUP_REMOVABLE_DIR" ]]; then
    # Sin espacios ni ':' ni ',' (viaja a `docker compose run -v host:cont`); absoluta, normalizada y fuera de BACKUP_DIR.
    [[ "$BACKUP_REMOVABLE_DIR" =~ ^/[A-Za-z0-9_./+-]+$ ]] || die $EX_USAGE "BACKUP_REMOVABLE_DIR debe ser una ruta absoluta (p.ej. /mnt/respaldo) sin espacios ni ':'"
    [[ "$BACKUP_REMOVABLE_DIR" != *"/../"* && "$BACKUP_REMOVABLE_DIR" != */.. && "$BACKUP_REMOVABLE_DIR" != *"/./"* && "$BACKUP_REMOVABLE_DIR" != *"//"* ]] \
      || die $EX_USAGE "BACKUP_REMOVABLE_DIR contiene '.', '..' o '//': usar una ruta normalizada"
    [[ "$BACKUP_REMOVABLE_DIR" != "$BACKUP_DIR" && "$BACKUP_REMOVABLE_DIR" != "$BACKUP_DIR"/* && "$BACKUP_DIR" != "$BACKUP_REMOVABLE_DIR"/* ]] \
      || die $EX_USAGE "BACKUP_REMOVABLE_DIR no puede contener ni estar dentro de BACKUP_DIR"
  fi
  # `retention-capacity` solo actua si hay algun limite; el destino extraible es opcional.
  CAPACITY_ENABLED=0; if (( MAX_TOTAL_BYTES > 0 || MIN_FREE_BYTES > 0 )); then CAPACITY_ENABLED=1; fi
}

# ------------------------------------------------------------------------------------------------------------------ modelo de cadenas (puro)
# bk_chains <db> <ahora_epoch>      < "bytes|relpath" por linea (relpath = sql/<nombre> | files/<nombre>; "L|<ruta>" = enlace simbolico)
# Salida: "R|<idx>|<ts-full-de-la-cadena>|<bytes>|<relpath>" por archivo gestionado, con idx = 1..N (cadenas completas, de la mas antigua a la
# mas nueva) | 0 (logs huerfanos: anteriores a todo full completo, inutiles) | K (se cuenta pero NUNCA se borra: full incompleto, manifiesto sin
# tar ni full, tar sin ninguna cadena). Mas lineas "WARN ..." / "GUARD ..." (GUARD: nombre con fecha imposible/futura -> no se decide nada).
# "Completo" = principal > 0 bytes y .sha256 >= 64 bytes (como la retencion remota; no prueba que restaure: eso lo hace restore-test).
bk_chains() {
  local db=${1:-} now=${2:-}
  [[ "$db" =~ ^[A-Za-z_][A-Za-z0-9_]{0,63}$ ]] || { echo "GUARD nombre de base invalido"; return 2; }
  [[ "$now" =~ ^[0-9]{9,11}$ ]] || { echo "GUARD ahora invalido"; return 2; }
  # Modo de custodia de la CA (include|seal): condiciona que un conjunto de archivos se considere acreditado. Si no se puede determinar
  # con certeza NO se asume "include" (relajaria la exigencia de .caseal): GUARD y no se decide ningun borrado.
  local seal=${BACKUP_CA_KEY:-}
  if [[ -z "$seal" ]] && declare -F cfg >/dev/null 2>&1; then seal=$(cfg BACKUP_CA_KEY include 2>/dev/null) || seal=""; fi
  if [[ "$seal" != include && "$seal" != seal ]]; then
    cat >/dev/null
    echo "GUARD BACKUP_CA_KEY ausente o invalido (debe ser include|seal): no se puede acreditar ningun conjunto de archivos y no se decide nada"; return 0
  fi
  local LC_ALL=C
  local rx_full="^sql/${db}_full_(${BK_TS})\.bak(\.sha256)?\$" rx_log="^sql/${db}_log_(${BK_TS})\.trn(\.sha256)?\$"
  local rx_tar="^files/files_(${BK_TS})\.tar\.gz(\.sha256|\.caseal)?\$" rx_set="^files/set_(${BK_TS})_(${BK_TS})\.txt\$"
  local limit; limit=$(date -u -d "@$((now + 600))" +%Y-%m-%d_%H%M%SZ) || { echo "GUARD no se pudo calcular el limite de reloj"; return 2; }
  local -A size=() typ=() t1=() t2=() suf=() fidx=() settar=()
  local -a names=() fl=()
  local line sz rel ts
  while IFS= read -r line; do
    line=${line%$'\r'}; [[ "$line" == *"|"* ]] || continue
    sz=${line%%|*}; rel=${line#*|}
    if [[ "$sz" == L ]]; then echo "WARN enlace simbolico ignorado (nunca se borra ni se copia): $rel"; continue; fi
    [[ "$sz" =~ ^[0-9]+$ ]] || continue
    if   [[ "$rel" =~ $rx_full ]]; then typ[$rel]=full; t1[$rel]=${BASH_REMATCH[1]}; suf[$rel]=${BASH_REMATCH[2]:+sha}
    elif [[ "$rel" =~ $rx_log  ]]; then typ[$rel]=log;  t1[$rel]=${BASH_REMATCH[1]}; suf[$rel]=${BASH_REMATCH[2]:+sha}
    elif [[ "$rel" =~ $rx_tar  ]]; then typ[$rel]=tar;  t1[$rel]=${BASH_REMATCH[1]}; suf[$rel]=${BASH_REMATCH[2]#.}
    elif [[ "$rel" =~ $rx_set  ]]; then typ[$rel]=set;  t1[$rel]=${BASH_REMATCH[1]}; t2[$rel]=${BASH_REMATCH[2]}; suf[$rel]=""
    else continue; fi
    size[$rel]=$sz; names+=("$rel")
  done
  # Fechas: exactas (ida y vuelta) en fulls/tars/manifiestos; rango + no-futuro en todos (los logs son miles: sin un `date` por archivo).
  local r t ok rx_quick='^[0-9]{4}-(0[1-9]|1[0-2])-(0[1-9]|[12][0-9]|3[01])_([01][0-9]|2[0-3])[0-5][0-9][0-5][0-9]Z$'
  for r in "${names[@]}"; do
    for t in "${t1[$r]}" "${t2[$r]:-}"; do
      [[ -n "$t" ]] || continue
      if [[ "$t" > "$limit" ]]; then echo "GUARD nombre con fecha futura ('${r#*/}'): reloj adelantado o nombres anomalos; no se decide nada"; return 0; fi
      ok=0
      if [[ "${typ[$r]}" == log ]]; then [[ "$t" =~ $rx_quick ]] && ok=1; else bk_ts_epoch "$t" >/dev/null && ok=1; fi
      (( ok )) || { echo "GUARD fecha imposible en el nombre '${r#*/}'; no se decide nada"; return 0; }
    done
  done
  # Fulls completos, ordenados.
  local -A comp=()
  for r in "${names[@]}"; do
    [[ "${typ[$r]}" == full && -z "${suf[$r]}" ]] || continue
    if (( ${size[$r]} > 0 )) && [[ -n "${size[$r.sha256]+x}" ]] && (( ${size[$r.sha256]} >= 64 )); then comp[${t1[$r]}]=1
    else echo "WARN full incompleto o vacio (falta/0 bytes de principal o .sha256): ${r#*/}; no cuenta como cadena y no se borra"; fi
  done
  if (( ${#comp[@]} )); then mapfile -t fl < <(printf '%s\n' "${!comp[@]}" | sort); fi
  local n=${#fl[@]} i
  for (( i = 0; i < n; i++ )); do fidx[${fl[$i]}]=$((i + 1)); done
  chain_of() { local x=$1 j; for (( j = n - 1; j >= 0; j-- )); do [[ "${fl[$j]}" > "$x" ]] || { echo $((j + 1)); return; }; done; echo 0; }
  for r in "${names[@]}"; do
    [[ "${typ[$r]}" == set ]] || continue
    # Un tar tiene UN manifiesto. Dos manifiestos que nombran fulls distintos para el mismo tar son contradictorios: no se elige "el ultimo"
    # (dependeria del orden del listado); GUARD y no se decide ningun borrado ni copia.
    if [[ -n "${settar[${t1[$r]}]:-}" && "${settar[${t1[$r]}]}" != "${t2[$r]}" ]]; then
      echo "GUARD manifiestos contradictorios para el tar ${t1[$r]} (full ${settar[${t1[$r]}]} y ${t2[$r]}): no se decide nada"; return 0
    fi
    settar[${t1[$r]}]=${t2[$r]}
  done
  tar_idx() { # tar_idx <ts-tar>: el manifiesto manda; sin el, el full anterior por fecha; sin ninguno, la cadena mas antigua; sin cadenas, K
    local x=$1 c
    if [[ -n "${settar[$x]:-}" && -n "${fidx[${settar[$x]}]:-}" ]]; then echo "${fidx[${settar[$x]}]}"; return; fi
    c=$(chain_of "$x"); if [[ "$c" == 0 ]]; then if (( n > 0 )); then c=1; else c=K; fi; fi
    echo "$c"
  }
  local idx
  for r in "${names[@]}"; do
    case "${typ[$r]}" in
      full) if [[ -n "${fidx[${t1[$r]}]:-}" ]]; then idx=${fidx[${t1[$r]}]}; else idx=K; fi ;;
      log)  idx=$(chain_of "${t1[$r]}") ;;
      tar)  idx=$(tar_idx "${t1[$r]}") ;;
      set)  if [[ -n "${size[files/files_${t1[$r]}.tar.gz]+x}" ]]; then idx=$(tar_idx "${t1[$r]}")
            elif [[ -n "${fidx[${t2[$r]}]:-}" ]]; then idx=${fidx[${t2[$r]}]}; else idx=K; fi ;;
    esac
    if [[ "$idx" =~ ^[0-9]+$ && "$idx" != 0 ]]; then ts=${fl[$((idx - 1))]}; else ts=-; fi
    echo "R|$idx|$ts|${size[$r]}|$r"
  done
  # Conjuntos de archivos ACREDITADOS (uploads, claves): tar con bytes, su .sha256 completo y su manifiesto set_<tar>_<full>.txt; con
  # BACKUP_CA_KEY=seal ademas su .caseal (sin el, Caddy podria no arrancar tras restaurar). Un tar interrumpido (sin checksum, manifiesto o
  # .caseal) NO acredita nada. capacity_plan nunca borra la ultima cadena con un conjunto acreditado.
  for r in "${names[@]}"; do
    [[ "${typ[$r]}" == tar && -z "${suf[$r]}" ]] || continue
    (( ${size[$r]} > 0 )) || continue
    [[ -n "${size[$r.sha256]+x}" ]] && (( ${size[$r.sha256]} >= 64 )) || continue
    local mfull=${settar[${t1[$r]}]:-}
    [[ -n "$mfull" && -n "${fidx[$mfull]:-}" ]] || continue   # el manifiesto debe nombrar un full COMPLETO que exista (la fecha solo agrupa, no acredita)
    if [[ "$seal" == seal ]]; then [[ -n "${size[$r.caseal]+x}" ]] && (( ${size[$r.caseal]} > 0 )) || continue; fi
    echo "P|${fidx[$mfull]}"
  done
}

# capacity_plan <min_cadenas> <tope_total_bytes|0> <libre_minimo_bytes|0> <libre_actual_bytes>      < salida de bk_chains
# Decide SIN tocar nada. Necesidad = max(total - tope, minimo_libre - libre_actual). Se borra primero lo huerfano (idx 0), despues cadenas de la mas
# antigua a la mas nueva, SIN bajar de <min_cadenas> (las mas nuevas se conservan SIEMPRE) y SIN borrar la ultima cadena que contiene el tar de archivos. Salida: SUMMARY, CHAIN <ts> <bytes> DEL|KEEP,
# DELETE <relpath>, y al final OK | FAIL <motivo> (rc 3: disco insuficiente; igual se aplican los DELETE ya decididos: lo que se borro excedia
# el limite y NO era necesario para el minimo). rc 2 = argumentos invalidos.
capacity_plan() {
  local minc=${1:-} maxt=${2:-} minf=${3:-} free=${4:-}
  [[ "$minc" =~ ^[0-9]{1,3}$ && "$maxt" =~ ^[0-9]{1,15}$ && "$minf" =~ ^[0-9]{1,15}$ && "$free" =~ ^[0-9]{1,15}$ ]] || { echo "FAIL argumentos numericos invalidos"; return 2; }
  minc=$((10#$minc)); maxt=$((10#$maxt)); minf=$((10#$minf)); free=$((10#$free))
  (( minc >= 1 )) || { echo "FAIL min_cadenas debe ser >= 1"; return 2; }
  local -A cb=() cf=() cts=()
  local tag idx ts bytes rel total=0 guard=0 maxi=0 keeptar=0 line
  while IFS= read -r line; do
    case "$line" in
      R\|*) IFS='|' read -r tag idx ts bytes rel <<<"$line"
            [[ "$bytes" =~ ^[0-9]+$ ]] || continue
            cb[$idx]=$(( ${cb[$idx]:-0} + bytes )); cf[$idx]+="$rel"$'\n'; cts[$idx]=$ts; total=$((total + bytes))
            if [[ "$idx" =~ ^[1-9][0-9]*$ ]] && (( idx > maxi )); then maxi=$idx; fi ;;
      P\|*) IFS='|' read -r tag idx <<<"$line"
            # La ultima cadena con un conjunto de archivos ACREDITADO (tar + checksum + manifiesto [+ .caseal]) es el ULTIMO par recuperable
            # full+archivos: nunca se borra, aunque despues haya fulls SQL sueltos (p. ej. los previos a un deploy) o un tar interrumpido.
            if [[ "$idx" =~ ^[1-9][0-9]*$ ]] && (( idx > keeptar )); then keeptar=$idx; fi ;;
      GUARD\ *) guard=1; echo "$line" ;;
      WARN\ *) echo "$line" ;;
    esac
  done
  local need_t=0 need_f=0 need
  (( maxt > 0 && total > maxt )) && need_t=$((total - maxt))
  (( minf > 0 && free < minf )) && need_f=$((minf - free))
  need=$(( need_t > need_f ? need_t : need_f ))
  echo "SUMMARY cadenas=$maxi total=$total tope=$maxt libre=$free libre_minimo=$minf necesidad=$need cadenas_minimas=$minc"
  if (( guard )) && (( maxt > 0 || need_f > 0 )); then echo "FAIL hay una GUARD (nombres con fechas anomalas): no se puede evaluar el limite y no se borra nada; revisar el reloj/los nombres"; return 3; fi
  if (( need == 0 )); then echo OK; return 0; fi
  local freed=0 i del_upto=$(( maxi - minc )) f
  if (( ${cb[0]:-0} > 0 )); then
    echo "CHAIN huerfanos ${cb[0]} DEL"
    while IFS= read -r f; do [[ -n "$f" ]] && echo "DELETE $f"; done <<<"${cf[0]}"
    freed=$((freed + cb[0]))
  fi
  for (( i = 1; i <= maxi; i++ )); do
    if (( freed < need && i <= del_upto && i != keeptar )); then
      echo "CHAIN ${cts[$i]} ${cb[$i]:-0} DEL"
      while IFS= read -r f; do [[ -n "$f" ]] && echo "DELETE $f"; done <<<"${cf[$i]:-}"
      freed=$((freed + ${cb[$i]:-0}))
    else
      echo "CHAIN ${cts[$i]} ${cb[$i]:-0} KEEP"
    fi
  done
  if (( freed >= need )); then echo "OK"; return 0; fi
  if (( maxi == 0 )); then echo "FAIL limite excedido y no hay ninguna cadena completa que respaldar: revisar los backups (disco insuficiente o backups incompletos)"; return 3; fi
  echo "FAIL DISCO INSUFICIENTE: faltan $((need - freed)) bytes tras conservar las $((maxi < minc ? maxi : minc)) cadena(s) mas nueva(s) (minimo $minc; la ultima cadena completa NUNCA se borra). Ampliar el disco o bajar BACKUP_RETENTION_*/BACKUP_CAPACITY_MIN_CHAINS"
  return 3
}

# ------------------------------------------------------------------------------------------------------------------ ejecucion
# Fragmentos sh (POSIX, busybox/dash) que corren dentro del contenedor backup-files o, con BK_CAP_HOSTFS=1, en el host. Entorno: ROOT (raiz de la
# arbol sql/ + files/), MARK (archivo-marca que debe existir en la raiz de montaje), LOCAL_ROOT (origen de copias).
CAP_LIST_SH='cd "$ROOT" || exit 2
[ -f "$MARK" ] || { echo "marca ausente ($MARK): destino no montado; no se hace nada" >&2; exit 2; }
for d in sql files; do
  if [ -L "$d" ]; then echo "L|$d"; continue; fi
  [ -d "$d" ] || continue
  for p in "$d"/*; do
    if [ -L "$p" ]; then echo "L|$p"; elif [ -f "$p" ]; then echo "$(stat -c %s "$p")|$p"; fi
  done
done'
CAP_DELETE_SH='cd "$ROOT" || exit 2
[ -f "$MARK" ] || { echo "marca ausente ($MARK): no se borra nada" >&2; exit 2; }
[ -L sql ] || [ -L files ] && { echo "sql/ o files/ es un enlace simbolico: no se borra nada" >&2; exit 3; }
n=0
for p in "$@"; do
  case "$p" in sql/*|files/*) ;; *) echo "ruta rechazada: $p" >&2; exit 3 ;; esac
  case "$p" in */../*|*/./*|*//*|*/) echo "ruta rechazada: $p" >&2; exit 3 ;; esac
  if [ -L "$p" ]; then echo "enlace simbolico: no se borra $p" >&2; continue; fi
  [ -f "$p" ] || continue
  rm -f -- "$p" && n=$((n + 1))
done
echo "$n"'
CAP_PREP_SH='[ -f "$MARK" ] || { echo "marca ausente ($MARK): destino no montado" >&2; exit 2; }
mkdir -p "$ROOT/sql" "$ROOT/files"'
CAP_COPY_SH='set -eu
[ -f "$MARK" ] || { echo "marca ausente ($MARK): destino no montado" >&2; exit 2; }
[ -d "$ROOT/sql" ] && [ -d "$ROOT/files" ] && [ ! -L "$ROOT/sql" ] && [ ! -L "$ROOT/files" ] || { echo "destino sin sql/ y files/ (o enlaces)" >&2; exit 3; }
cd "$LOCAL_ROOT"
n=0
for p in "$@"; do
  case "$p" in sql/*|files/*) ;; *) echo "ruta rechazada: $p" >&2; exit 3 ;; esac
  case "$p" in */../*|*/./*|*//*|*/) echo "ruta rechazada: $p" >&2; exit 3 ;; esac
  { [ -f "$p" ] && [ ! -L "$p" ]; } || { echo "falta el origen $p" >&2; exit 4; }
  dst="$ROOT/$p"; tmp="$dst.partial"
  cp -- "$p" "$tmp" || { rm -f -- "$tmp"; echo "la copia fallo (espacio?): $p" >&2; exit 5; }
  ok=0
  case "$p" in
    *.sha256|*.caseal|*.txt) cmp -s -- "$p" "$tmp" && ok=1 ;;
    *) if [ -f "$p.sha256" ]; then
         exp=$(cut -d" " -f1 "$p.sha256"); got=$(sha256sum "$tmp" | cut -d" " -f1)
         [ -n "$exp" ] && [ "$exp" = "$got" ] && ok=1
       else cmp -s -- "$p" "$tmp" && ok=1; fi ;;
  esac
  [ "$ok" = 1 ] || { rm -f -- "$tmp"; echo "la verificacion de integridad (sha256) FALLO: $p" >&2; exit 6; }
  mv -f -- "$tmp" "$dst"; n=$((n + 1))
done
echo "$n"'

# cap_run <local|removable> <fragmento sh> [args...]   (stdout del fragmento; rc del fragmento)
cap_run() {
  local which=$1 script=$2; shift 2
  local root mark lroot=$BACKUP_DIR
  if [[ "$which" == local ]]; then root=$BACKUP_DIR; mark=$BACKUP_DIR/.bury-backup-root
  else root=$BACKUP_REMOVABLE_DIR/bury-backup; mark=$BACKUP_REMOVABLE_DIR/$BACKUP_REMOVABLE_MARKER; fi
  if [[ "${BK_CAP_HOSTFS:-0}" == 1 ]]; then
    ROOT="$root" MARK="$mark" LOCAL_ROOT="$lroot" sh -c "$script" cap "$@" </dev/null
  elif [[ "$which" == local ]]; then
    dc run --rm -T --no-deps --quiet-pull -e ROOT=/backups -e MARK=/backups/.bury-backup-root -e LOCAL_ROOT=/backups backup-files sh -c "$script" cap "$@" </dev/null
  else
    dc run --rm -T --no-deps --quiet-pull -v "$BACKUP_REMOVABLE_DIR:/removable" -e ROOT=/removable/bury-backup -e "MARK=/removable/$BACKUP_REMOVABLE_MARKER" -e LOCAL_ROOT=/backups \
       backup-files sh -c "$script" cap "$@" </dev/null
  fi
}

CAP_REL_RX="^(sql/[A-Za-z_][A-Za-z0-9_]{0,63}_(full_${BK_TS}\.bak|log_${BK_TS}\.trn)(\.sha256)?|files/(files_${BK_TS}\.tar\.gz(\.sha256|\.caseal)?|set_${BK_TS}_${BK_TS}\.txt))\$"

fs_free_bytes() { # fs_free_bytes <dir> -> bytes libres del filesystem que contiene <dir>
  local v; v=$(df -PB1 "$1" 2>/dev/null | awk 'NR==2 { print $4 }')
  is_uint "$v" || return 1
  printf '%s' "$v"
}

# capacity_run <local|removable> <apply 0|1> <tope_bytes> <libre_min_bytes> <min_cadenas>  -> rc 0 ok | 7 limite no cumplible / fallo (falla ruidosa)
# Deja en CAP_FREED_FILES la cantidad de archivos borrados (o que se borrarian con apply=0) y en CAP_LAST el resumen.
CAP_FREED_FILES=0 CAP_LAST=""
capacity_run() {
  local which=$1 apply=$2 maxb=$3 minf=$4 minc=$5 dir list free plan prc=0 line n=0 rel
  local -a dels=()
  CAP_FREED_FILES=0 CAP_LAST=""
  if [[ "$which" == local ]]; then dir=$BACKUP_DIR; else dir=$BACKUP_REMOVABLE_DIR; fi
  [[ -n "$dir" && "$dir" != "/" ]] || { log ERROR "retencion por capacidad: directorio destino vacio o '/': se rechaza"; return 7; }
  if ! list=$(cap_run "$which" "$CAP_LIST_SH" 2>&1); then
    log ERROR "retencion por capacidad ($which): no se pudo listar el destino: $(sanitize_text "$list" | tr '\n' ' ' | cut -c1-300)"; return 7
  fi
  if ! free=$(fs_free_bytes "$dir"); then log ERROR "retencion por capacidad ($which): no se pudo medir el espacio libre de $dir"; return 7; fi
  plan=$(bk_chains "$DB_NAME" "$(date -u +%s)" <<<"$list" | capacity_plan "$minc" "$maxb" "$minf" "$free") || prc=$?
  while IFS= read -r line; do
    case "$line" in
      DELETE\ *) rel=${line#DELETE }
                 [[ "$rel" =~ $CAP_REL_RX ]] || { log ERROR "retencion por capacidad: ruta no canonica en el plan, se aborta sin borrar: $rel"; return 7; }
                 dels+=("$rel") ;;
      SUMMARY\ *) CAP_LAST=${line#SUMMARY }; log INFO "  capacidad ($which): $CAP_LAST" ;;
      CHAIN\ *)  log INFO "  cadena ${line#CHAIN }" ;;
      WARN\ *|GUARD\ *) log WARN "  capacidad ($which): ${line#* }" ;;
      FAIL\ *)   log ERROR "retencion por capacidad ($which): ${line#FAIL }" ;;
      OK)        ;;
    esac
  done <<<"$plan"
  if (( ${#dels[@]} )); then
    if (( apply )); then
      local i=0 batch out
      while (( i < ${#dels[@]} )); do
        batch=("${dels[@]:i:100}")
        if ! out=$(cap_run "$which" "$CAP_DELETE_SH" "${batch[@]}" 2>&1); then
          log ERROR "retencion por capacidad ($which): el borrado fallo tras $n archivo(s): $(sanitize_text "$out" | tr '\n' ' ' | cut -c1-300)"; CAP_FREED_FILES=$n; return 7
        fi
        n=$((n + $(tail -n1 <<<"$out" | tr -dc '0-9' | sed 's/^$/0/')))
        i=$((i + 100))
      done
      CAP_FREED_FILES=$n
      log INFO "  capacidad ($which): $n archivo(s) borrado(s) (cadenas mas antiguas primero)"
    else
      CAP_FREED_FILES=${#dels[@]}
      local d; for d in "${dels[@]}"; do log INFO "  DRY-RUN borraria $d"; done
      log INFO "  capacidad ($which): DRY-RUN, ${#dels[@]} archivo(s) se borrarian; no se toco nada"
    fi
  fi
  if (( prc != 0 )); then return 7; fi
  return 0
}

# capacity_status <exit> <verified> <mensaje>: monitor-status/capacity.json (mismo formato y monitor que restore-test/custody)
capacity_status() { restore_test_status "$1" "$2" "${3:-}" capacity; }

# ------------------------------------------------------------------------------------------------------------------ destino extraible
# Disponibilidad: ruta definida + existe + (montaje real si BACKUP_REMOVABLE_REQUIRE_MOUNT=true y hay `mountpoint`) + archivo-marca en su raiz
# (se crea UNA vez a mano con `touch <dir>/<marca>` en el disco ya conectado: asi un punto de montaje vacio -disco desconectado- no recibe
# copias en el disco del servidor). No se asume ninguna letra de unidad ni ruta: Ubuntu (/mnt/respaldo, /media/..) o Windows via WSL (/mnt/e).
removable_available() { # -> rc 0 disponible | 1 no (REMOVABLE_WHY = motivo)
  REMOVABLE_WHY=""
  [[ -n "$BACKUP_REMOVABLE_DIR" ]] || { REMOVABLE_WHY="BACKUP_REMOVABLE_DIR no esta definido"; return 1; }
  [[ -d "$BACKUP_REMOVABLE_DIR" ]] || { REMOVABLE_WHY="la ruta no existe (disco desconectado o no montado)"; return 1; }
  if [[ "$BACKUP_REMOVABLE_REQUIRE_MOUNT" == true ]] && command -v mountpoint >/dev/null 2>&1 && ! mountpoint -q "$BACKUP_REMOVABLE_DIR"; then
    REMOVABLE_WHY="la ruta existe pero NO es un punto de montaje (disco desconectado: no se copia al disco del servidor)"; return 1
  fi
  [[ -f "$BACKUP_REMOVABLE_DIR/$BACKUP_REMOVABLE_MARKER" ]] || { REMOVABLE_WHY="falta el archivo-marca '$BACKUP_REMOVABLE_MARKER' en la raiz del destino (crearlo una vez con touch en el disco correcto)"; return 1; }
  [[ -w "$BACKUP_REMOVABLE_DIR" ]] || { REMOVABLE_WHY="el destino no es escribible por $(id -un)"; return 1; }
  # Un destino extraible es de UNA instalacion: el nombre de archivo de los respaldos (<db>_full_<ts>.bak) es identico entre instalaciones y la
  # retencion por cadenas las trataria como una sola (una podaria las copias de la otra). La primera copia graba BACKUP_DIR en la marca
  # (removable_sync); aqui se rechaza un destino cuya marca nombra a OTRA instalacion. Marca vacia (legado) = libre, se reclama al copiar.
  local owner; owner=$(head -c 1024 "$BACKUP_REMOVABLE_DIR/$BACKUP_REMOVABLE_MARKER" 2>/dev/null | tr -d '\r' | head -n1 || true)
  if [[ -n "$owner" && "$owner" != "$BACKUP_DIR" ]]; then
    REMOVABLE_WHY="el destino pertenece a OTRA instalacion (su marca nombra BACKUP_DIR=$owner, esta es $BACKUP_DIR): usar un destino extraible propio por instalacion. Si se movio BACKUP_DIR a proposito, corregir el contenido de '$BACKUP_REMOVABLE_MARKER'"
    return 1
  fi
  return 0
}

removable_status() { restore_test_status "$1" "$2" "${3:-}" removable-copy; }

# removable_sync: copia al destino extraible las cadenas COMPLETAS que le falten (de la mas nueva a la mas antigua, solo los archivos ausentes o de
# tamaño distinto), verifica sha256 de cada copia y marca el estado en monitor-status/removable-copy.json (verified=true solo si la cadena mas
# nueva quedo completa y verificada en el destino = "al dia"; si no, "atrasada" y el monitor alerta por antiguedad del ultimo exito).
# NUNCA falla el backup principal: devuelve 0 salvo REMOVABLE_STRICT=1 (modo manual). Requiere el lock tomado por quien lo llama.
removable_sync() { # removable_sync <apply 0|1>
  local apply=${1:-1}
  [[ -n "$BACKUP_REMOVABLE_DIR" ]] || return 0
  local rc=0
  if ! removable_available; then
    log WARN "destino extraible NO disponible: $REMOVABLE_WHY. Los backups siguen en BACKUP_DIR; la copia extraible queda ATRASADA"
    (( apply )) && removable_status 1 false "atrasada: destino no disponible: $REMOVABLE_WHY"
    [[ "${REMOVABLE_STRICT:-0}" == 1 ]] && return 1
    return 0
  fi
  local llist rlist now; now=$(date -u +%s)
  llist=$(cap_run local "$CAP_LIST_SH" 2>&1) || { log WARN "copia extraible: no se pudo listar BACKUP_DIR: $(sanitize_text "$llist" | tr '\n' ' ' | cut -c1-200)"; (( apply )) && removable_status 1 false "atrasada: no se pudo listar el origen"; return 0; }
  if (( apply )) && [[ ! -s "$BACKUP_REMOVABLE_DIR/$BACKUP_REMOVABLE_MARKER" ]]; then
    printf '%s
' "$BACKUP_DIR" >"$BACKUP_REMOVABLE_DIR/$BACKUP_REMOVABLE_MARKER" 2>/dev/null       && log INFO "destino extraible reclamado por esta instalacion (marca = $BACKUP_DIR)"       || log WARN "no se pudo grabar el dueño del destino extraible en la marca (se sigue sin exclusividad)"
  fi
  if (( apply )); then
    cap_run removable "$CAP_PREP_SH" >/dev/null 2>&1 || { log WARN "copia extraible: no se pudo preparar el destino"; removable_status 1 false "atrasada: no se pudo preparar el destino"; [[ "${REMOVABLE_STRICT:-0}" == 1 ]] && return 1; return 0; }
  fi
  rlist=$(cap_run removable "$CAP_LIST_SH" 2>/dev/null || true)
  local recs; recs=$(bk_chains "$DB_NAME" "$now" <<<"$llist")
  if grep -q '^GUARD' <<<"$recs"; then
    log WARN "copia extraible: $(grep -m1 '^GUARD' <<<"$recs"): no se copia nada"; (( apply )) && removable_status 1 false "atrasada: nombres con fechas anomalas en el origen"; return 0
  fi
  local -A rsize=() cfiles=() cbytes=() cts=()
  local line sz rel tag idx ts bytes maxi=0
  while IFS= read -r line; do
    sz=${line%%|*}; rel=${line#*|}; [[ "$sz" =~ ^[0-9]+$ ]] && rsize[$rel]=$sz
  done <<<"$rlist"
  while IFS='|' read -r tag idx ts bytes rel; do
    [[ "$tag" == R && "$idx" =~ ^[1-9][0-9]*$ ]] || continue
    cfiles[$idx]+="$bytes|$rel"$'\n'; cbytes[$idx]=$(( ${cbytes[$idx]:-0} + bytes )); cts[$idx]=$ts
    (( idx > maxi )) && maxi=$idx
  done <<<"$recs"
  if (( maxi == 0 )); then
    log WARN "copia extraible: no hay ninguna cadena completa en BACKUP_DIR que copiar"; (( apply )) && removable_status 1 false "atrasada: sin cadena completa en el origen"; return 0
  fi
  local i f need_bytes missing newest_ok=0 copied=0 stopped="" rtotal=0 free
  for rel in "${!rsize[@]}"; do [[ "$rel" =~ $CAP_REL_RX ]] && rtotal=$((rtotal + rsize[$rel])); done
  for (( i = maxi; i >= 1; i-- )); do
    local -a todo=(); need_bytes=0
    while IFS= read -r f; do
      [[ -n "$f" ]] || continue
      bytes=${f%%|*}; rel=${f#*|}
      if [[ "${rsize[$rel]:-x}" != "$bytes" ]]; then todo+=("$rel"); need_bytes=$((need_bytes + bytes)); fi
    done <<<"${cfiles[$i]}"
    if (( ${#todo[@]} == 0 )); then (( i == maxi )) && newest_ok=1; continue; fi
    if ! free=$(fs_free_bytes "$BACKUP_REMOVABLE_DIR"); then stopped="no se pudo medir el espacio del destino"; break; fi
    if (( free < need_bytes + REM_MIN_FREE_BYTES )) || (( REM_MAX_TOTAL_BYTES > 0 && rtotal + need_bytes > REM_MAX_TOTAL_BYTES )); then
      stopped="sin espacio para la cadena ${cts[$i]} (necesita $need_bytes bytes; libres $free)"; break
    fi
    if (( ! apply )); then log INFO "  DRY-RUN copiaria cadena ${cts[$i]}: ${#todo[@]} archivo(s), $need_bytes bytes"; continue; fi
    local out j=0 batch
    while (( j < ${#todo[@]} )); do
      batch=("${todo[@]:j:50}")
      if ! out=$(cap_run removable "$CAP_COPY_SH" "${batch[@]}" 2>&1); then
        stopped="la copia de la cadena ${cts[$i]} fallo: $(sanitize_text "$out" | tr '\n' ' ' | cut -c1-200)"; break 2
      fi
      j=$((j + 50))
    done
    copied=$((copied + ${#todo[@]})); rtotal=$((rtotal + need_bytes))
    log INFO "copia extraible: cadena ${cts[$i]} copiada y verificada (${#todo[@]} archivo(s), $need_bytes bytes)"
    (( i == maxi )) && newest_ok=1
  done
  (( apply )) || { log INFO "copia extraible: DRY-RUN, no se copio nada"; return 0; }
  # Poda del destino por capacidad (mismas reglas: cadenas completas, nunca la ultima).
  local prune_msg=""
  if (( REM_MAX_TOTAL_BYTES > 0 || REM_MIN_FREE_BYTES > 0 )); then
    capacity_run removable 1 "$REM_MAX_TOTAL_BYTES" "$REM_MIN_FREE_BYTES" "$CAPACITY_MIN_CHAINS" || { rc=1; prune_msg=" | poda del destino: disco insuficiente"; }
  fi
  if [[ -n "$stopped" ]]; then log WARN "copia extraible incompleta: $stopped"; fi
  if (( newest_ok && rc == 0 )); then
    if [[ -z "$stopped" ]]; then
      removable_status 0 true "al dia: cadena ${cts[$maxi]} verificada en el destino extraible ($copied archivo(s) copiados)"
      log INFO "copia extraible AL DIA (cadena ${cts[$maxi]}, $copied archivo(s) copiados)"
    else
      removable_status 0 true "al dia en la cadena mas nueva (${cts[$maxi]}); cadenas antiguas pendientes: $stopped"
    fi
  else
    removable_status 1 false "atrasada: ${stopped:-la cadena mas nueva no quedo completa}$prune_msg"
    [[ "${REMOVABLE_STRICT:-0}" == 1 ]] && return 1
  fi
  return 0
}
