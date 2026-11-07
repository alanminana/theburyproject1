#!/usr/bin/env bash
# Backup del ERP: SQL Server (full / log) + archivos persistentes + copia externa + retencion local.
#
#   scripts/backup/backup.sh [all|full|log|files]      (default: all)
#
#   all    full SQL + archivos (keys, uploads, App_Data, caddy-data), con `app` detenida unos segundos para que ambos
#          backups sean un par CONSISTENTE; luego copia externa y retencion.       -> diario
#   full   solo full SQL (online, sin detener nada).
#   log    solo backup del log de transacciones (online). Requiere recovery FULL y un full previo.   -> cada 15 min
#   files  solo archivos (detiene `app` unos segundos).
#
# Codigos de salida: 0 ok | 1 uso/config | 2 BACKUP SQL fallo | 3 verificacion fallo | 4 ocupado | 5 archivos fallo |
#                    6 copia externa fallo (incluye retencion remota) | 7 retencion local fallo | 8 prerequisito (docker/SQL) | 10 app no volvio healthy
# Nunca imprime ni registra passwords ni credenciales del proveedor externo. Log: $BACKUP_DIR/logs/backup.log
# Documentacion: docs/backup-restore.md
# shellcheck source=lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

MODE="${1:-all}"
case "$MODE" in all|full|log|files) ;; -h|--help) sed -n '2,15p' "$0"; exit 0 ;; *) echo "modo invalido: $MODE (all|full|log|files)" >&2; exit $EX_USAGE ;; esac

load_config
START=$(date +%s)
# BACKUP_DIR debe existir antes de levantar `db` (prepare-dir.sh / install.sh). Si falta, se crea con el usuario actual y modo 700;
# nunca se cambia el modo de un directorio que ya existe.
mkdir_new_mode 700 "$BACKUP_DIR"
[[ -d "$BACKUP_DIR" ]] || die $EX_USAGE "no se pudo crear BACKUP_DIR=$BACKUP_DIR"
backup_dir_usable
mkdir_new_mode 700 "$BACKUP_DIR/logs"; rotate_log

RC=0
fail() { local code=$1; shift; log ERROR "$*"; if (( RC == 0 )); then RC=$code; fi; }

APP_STOPPED=0
APP_MARK="$BACKUP_DIR/.app-stopped-by-backup"   # persistente: sobrevive a un SIGKILL / corte del host mientras `app` esta detenida
APP_RECOVERY_FAILED=0
SET_APP_STOPPED=false   # el manifiesto del par registra si app estuvo detenida durante el backup
RUN_SET=""
RUN_SQL=()     # nombres generados en esta ejecucion (para la copia externa)
RUN_FILES=()
OFF_FULL_OK=0  # 1 solo si ESTA corrida subio y verifico un full en el remoto (habilita podar fulls y logs remotos)
OFF_FILES_OK=0 # idem para el tar de archivos

cleanup() {
  local ec=$?
  # Pase lo que pase, si detuvimos `app` se la vuelve a levantar.
  if (( APP_STOPPED == 1 )); then start_app || true; fi
  if (( ec != EX_BUSY )); then monitor_status "run-$MODE" "$ec" false; fi
  release_lock
  exit "$ec"
}
trap cleanup EXIT

log INFO "=== backup '$MODE' inicio (db=$DB_NAME dir=$BACKUP_DIR) ==="
# El lock se toma ANTES de los prerequisitos: asi la recuperacion de `app` (abajo) corre en cualquier modo aunque SQL no responda.
lrc=0; acquire_lock || lrc=$?
if (( lrc == 1 )); then
  log WARN "backup '$MODE' omitido: hay otra ejecucion en curso"
  trap - EXIT; exit $EX_BUSY
elif (( lrc != 0 )); then
  die $EX_PREREQ "no se pudo tomar el lock de backup (ver el mensaje anterior)"
fi

# ---------------------------------------------------------------- app: detener / iniciar
app_healthy() { [[ "$(dc ps --format '{{.Health}}' app 2>/dev/null | head -n1 || true)" == "healthy" ]]; }
# Espera hasta 180 s a que `app` este healthy. rc 0 = healthy.
app_wait_healthy() {
  local i
  for i in $(seq 1 60); do
    if app_healthy; then return 0; fi
    sleep 3
  done
  return 1
}
stop_app() {
  if ! service_running app; then
    log INFO "app no estaba corriendo: no se detiene ni se reinicia"; return 0
  fi
  log INFO "deteniendo app (consistencia del par SQL+archivos)"
  # La marca se escribe ANTES de detener: si el proceso muere (SIGKILL, corte de luz), la proxima corrida sabe que fue el backup y
  # vuelve a levantar `app` (con `restart: unless-stopped`, Docker NO reinicia un contenedor detenido a mano tras reiniciar el host).
  printf '%s pid=%s modo=%s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$$" "$MODE" >"$APP_MARK" || { log ERROR "no se pudo escribir $APP_MARK: no se detiene app"; return 1; }
  APP_STOPPED=1
  dc stop -t 30 app >/dev/null 2>&1 || { log ERROR "no se pudo detener app"; return 1; }
  SET_APP_STOPPED=true
}
start_app() {
  log INFO "iniciando app"
  dc start app >/dev/null 2>&1 || { fail $EX_APP "no se pudo iniciar app (la marca $APP_MARK queda: la proxima corrida reintenta)"; APP_STOPPED=0; return 1; }
  if app_wait_healthy; then log INFO "app healthy"; rm -f "$APP_MARK"; APP_STOPPED=0; return 0; fi
  fail $EX_APP "app no llego a healthy en 180 s (la marca $APP_MARK queda: la proxima corrida reintenta)"
  APP_STOPPED=0
  return 1
}
# Si una corrida anterior murio con `app` detenida (marca presente y NINGUNA corrida viva: tenemos el lock), la levanta.
# No toca `app` si no hay marca (p. ej. detenida a mano por el operador para un restore).
recover_app() {
  [[ -f "$APP_MARK" ]] || return 0
  log WARN "marca $APP_MARK presente ($(tr -d '\n' <"$APP_MARK" 2>/dev/null || true)): una corrida anterior no devolvio app a servicio"
  if app_healthy; then log INFO "app ya esta healthy: se limpia la marca"; rm -f "$APP_MARK"; return 0; fi
  log WARN "recuperando app (docker compose start app)"
  if dc start app >/dev/null 2>&1 && app_wait_healthy; then
    log INFO "app recuperada y healthy"; rm -f "$APP_MARK"; monitor_status app-recovery 0 true
  else
    APP_RECOVERY_FAILED=1; monitor_status app-recovery "$EX_APP" false
    log ERROR "no se pudo recuperar app: reintento en la proxima corrida (revisar docker compose logs app)"
  fi
}

recover_app
check_prereqs
prepare_dirs

# ---------------------------------------------------------------- SQL
compression_clause() {
  case "$BACKUP_SQL_COMPRESSION" in
    on) echo "COMPRESSION," ;;
    off) echo "" ;;
    auto)
      local ed; ed=$(sql_scalar "SELECT CAST(SERVERPROPERTY('Edition') AS nvarchar(100))" || true)
      if [[ "$ed" == Express* ]]; then echo ""; else echo "COMPRESSION,"; fi ;;
    *) die $EX_USAGE "BACKUP_SQL_COMPRESSION debe ser auto|on|off" ;;
  esac
}

# Comprobaciones tras generar el archivo: existe, tamaño > 0, RESTORE VERIFYONLY (con CHECKSUM), sha256.
sql_post_checks() { # sql_post_checks <nombre>
  local name=$1 size
  size=$(tools sh -c 'test -f "/backups/sql/$0" && stat -c %s "/backups/sql/$0"' "$name" 2>/dev/null | tr -d '\r' || true)
  if ! is_uint "$size" || (( size == 0 )); then
    fail $EX_VERIFY "backup $name: no existe o tamaño 0"; return 1
  fi
  if ! sql_exec "RESTORE VERIFYONLY FROM DISK = N'/backups/sql/$name' WITH CHECKSUM" >/dev/null; then
    tools sh -c 'mv "/backups/sql/$0" "/backups/sql/$0.failed-verify"' "$name" || true
    fail $EX_VERIFY "backup $name: RESTORE VERIFYONLY FALLO (archivo puesto en cuarentena: $name.failed-verify)"; return 1
  fi
  tools sh -c 'cd /backups/sql && sha256sum "$0" > "$0.sha256"' "$name" || { fail $EX_VERIFY "no se pudo calcular sha256 de $name"; return 1; }
  log INFO "backup $name: bytes=$size verifyonly=OK sha256=OK"
}

# Nunca reutilizar un nombre: BACKUP ... WITH INIT sobrescribiria un backup valido (dos corridas en el mismo segundo, reloj atrasado).
name_free() { # name_free <sql|files> <nombre> <codigo-de-error>
  if tools sh -c 'test -e "/backups/$0/$1"' "$1" "$2" >/dev/null 2>&1; then
    fail "$3" "el archivo $2 ya existe en $1/: no se sobrescribe (reloj retrocedido o dos corridas en el mismo segundo)"; return 1
  fi
}

sql_full() {
  local name="${DB_NAME}_full_$(utc_stamp).bak" comp
  name_free sql "$name" $EX_SQL || return 1
  comp=$(compression_clause)
  log INFO "BACKUP DATABASE -> $name (compresion: $([[ -n "$comp" ]] && echo si || echo no))"
  if ! sql_exec "BACKUP DATABASE [$DB_NAME] TO DISK = N'/backups/sql/$name' WITH INIT, CHECKSUM, ${comp} STATS = 25, NAME = N'$name'" >/dev/null; then
    tools sh -c 'rm -f "/backups/sql/$0"' "$name" || true       # nunca dejar un .bak parcial con nombre valido
    fail $EX_SQL "BACKUP DATABASE fallo ($name)"; return 1
  fi
  sql_post_checks "$name" || return 1
  RUN_SQL+=("$name")
  monitor_status full 0 true
}

sql_log() {
  local model; model=$(sql_scalar "SELECT recovery_model_desc FROM sys.databases WHERE name = N'$DB_NAME'" || true)
  if [[ "$model" != "FULL" && "$model" != "BULK_LOGGED" ]]; then
    log WARN "backup de log OMITIDO: recovery model de $DB_NAME es '${model:-?}' (solo FULL/BULK_LOGGED admiten log backups)"
    return 0
  fi
  local name="${DB_NAME}_log_$(utc_stamp).trn"
  name_free sql "$name" $EX_SQL || return 1
  log INFO "BACKUP LOG -> $name"
  if ! sql_exec "BACKUP LOG [$DB_NAME] TO DISK = N'/backups/sql/$name' WITH INIT, CHECKSUM, STATS = 100, NAME = N'$name'" >/dev/null; then
    tools sh -c 'rm -f "/backups/sql/$0"' "$name" || true
    fail $EX_SQL "BACKUP LOG fallo ($name). ¿Falta un full previo? (error 4214)"; return 1
  fi
  sql_post_checks "$name" || return 1
  RUN_SQL+=("$name")
  monitor_status log 0 true
}

# ---------------------------------------------------------------- archivos
files_backup() {
  local name="files_$(utc_stamp).tar.gz" out
  name_free files "$name" $EX_FILES || return 1
  log INFO "backup de archivos -> $name (caddy-data: $BACKUP_INCLUDE_CADDY)"
  if ! out=$(tools sh /opt/bury-backup/files-backup.sh "$name" 2>&1); then
    log ERROR "$out"; fail $EX_FILES "backup de archivos fallo ($name)"; return 1
  fi
  log INFO "backup de archivos OK: $out"
  RUN_FILES+=("$name")
  monitor_status files 0 true
}

# ---------------------------------------------------------------- manifiesto del par (solo modo all)
# Un `full` y un tar de archivos solo forman un PAR coherente si salen de la misma corrida `all` (app detenida entre ambos). Los
# nombres llevan timestamps distintos y `backup.sh full` (p. ej. el backup pre-migracion de deploy.sh) crea fulls mas nuevos que el
# ultimo tar: elegir "el ultimo de cada uno" a ojo NO da un par. Este manifiesto (set_<ts-del-tar>.txt, junto al tar, se sube y se
# conserva con el) acredita el par: nombres exactos, sha256 y si app estuvo detenida. Se escribe solo si AMBOS backups se verificaron.
write_set() {
  local full tar fsha tsha name content
  full=$(printf '%s\n' "${RUN_SQL[@]}" | grep '_full_' | head -n1 || true); tar=${RUN_FILES[0]:-}
  [[ -n "$full" && -n "$tar" ]] || return 0
  fsha=$(tools sh -c 'cut -d" " -f1 "/backups/sql/$0.sha256"' "$full" 2>/dev/null | tr -d '\r' || true)
  tsha=$(tools sh -c 'cut -d" " -f1 "/backups/files/$0.sha256"' "$tar" 2>/dev/null | tr -d '\r' || true)
  [[ "$fsha" =~ ^[0-9a-f]{64}$ && "$tsha" =~ ^[0-9a-f]{64}$ ]] || { log WARN "manifiesto del par NO escrito: no se pudieron leer los sha256"; return 0; }
  local tts fts; tts=${tar#files_}; tts=${tts%.tar.gz}; fts=${full#${DB_NAME}_full_}; fts=${fts%.bak}
  name="set_${tts}_${fts}.txt"   # lleva AMBOS timestamps: la retencion sabe que full protege cada tar sin leer el contenido
  content=$(printf 'bury-backup-set v1\ncreated=%s\nfull=%s sha256=%s\nfiles=%s sha256=%s\napp_stopped=%s\n' \
            "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$full" "$fsha" "$tar" "$tsha" "$SET_APP_STOPPED")
  if tools sh -c 'umask 077; printf "%s\n" "$1" > "/backups/files/$0.partial" && mv -f "/backups/files/$0.partial" "/backups/files/$0"' "$name" "$content" >/dev/null 2>&1; then
    RUN_SET="$name"; log INFO "manifiesto del par escrito: $name (full + tar de esta corrida)"
  else
    log WARN "no se pudo escribir el manifiesto del par $name (el backup sigue siendo valido)"
  fi
}

# ---------------------------------------------------------------- copia externa
offsite() {
  if [[ -z "$BACKUP_REMOTE" ]]; then
    if [[ "$BACKUP_REQUIRE_OFFSITE" == "true" ]]; then
      fail $EX_OFFSITE "copia externa REQUERIDA (BACKUP_REQUIRE_OFFSITE=true) pero BACKUP_REMOTE no esta configurado"
    else
      log WARN "copia externa NO configurada (BACKUP_REMOTE vacio): los backups existen SOLO en este servidor"
    fi
    return 0
  fi
  [[ "$BACKUP_REMOTE" =~ ^[A-Za-z0-9_-]+:.+ ]] || { fail $EX_OFFSITE "BACKUP_REMOTE debe tener la forma <remoto>:<ruta> (p.ej. offsite:bucket/erp)"; return 1; }

  # Centinela: con disco externo/NAS montado en el host, un punto de montaje vacio (disco desconectado) aceptaria la copia y la dejaria
  # en el disco del servidor. Si BACKUP_OFFSITE_MARKER esta definido, el remoto debe contener ese archivo (se crea una vez con
  # `rclone touch`); si no esta, se aborta ANTES de subir nada.
  if [[ -n "$BACKUP_OFFSITE_MARKER" ]]; then
    local mk
    mk=$(dc run --rm -T --no-deps --quiet-pull backup-offsite lsf "$BACKUP_REMOTE/$BACKUP_OFFSITE_MARKER" 2>/dev/null | tr -d '\r' || true)
    if [[ "$mk" != "$BACKUP_OFFSITE_MARKER" ]]; then
      monitor_status offsite-marker "$EX_OFFSITE" false
      fail $EX_OFFSITE "copia externa: falta el centinela '$BACKUP_OFFSITE_MARKER' en ${BACKUP_REMOTE%%:*}:* (¿disco/NAS desconectado o no montado?). No se sube nada"; return 1
    fi
  fi

  local sub names args up n
  for sub in sql files; do
    if [[ $sub == sql ]]; then names=("${RUN_SQL[@]}"); else names=("${RUN_FILES[@]}"); fi
    (( ${#names[@]} > 0 )) || continue
    args=()
    for n in "${names[@]}"; do args+=(--include "$n" --include "$n.sha256"); done
    if [[ $sub == files && -n "$RUN_SET" ]]; then args+=(--include "$RUN_SET"); fi
    # La SUBIDA cubre todos los backups locales con nombre valido (rclone copy omite lo que ya esta en el remoto): si una corrida
    # anterior no pudo subir un .trn (remoto caido), este lo repone y la cadena de logs remota no queda con un hueco permanente
    # (verificado 2026-10-29). La VERIFICACION sigue limitada a los archivos de esta corrida; el resto se comprueba con sha256 al descargar.
    if [[ $sub == sql ]]; then up=(--include "${DB_NAME}_full_*.bak" --include "${DB_NAME}_full_*.bak.sha256" --include "${DB_NAME}_log_*.trn" --include "${DB_NAME}_log_*.trn.sha256")
    else up=(--include "files_*.tar.gz" --include "files_*.tar.gz.sha256" --include "set_*.txt"); fi
    if (( REMOTE_RETENTION_DAYS > 0 )); then up+=(--max-age "${REMOTE_RETENTION_DAYS}d"); fi   # no resubir lo que la retencion remota borraria
    log INFO "copia externa: $sub -> ${BACKUP_REMOTE%%:*}:*/$sub (${#names[@]} archivo(s) de esta corrida + pendientes)"
    if ! dc run --rm -T --no-deps --quiet-pull backup-offsite copy "/backups/$sub" "$BACKUP_REMOTE/$sub" "${up[@]}" >/dev/null 2>"$BACKUP_DIR/logs/.offsite.err"; then
      monitor_status "offsite-$sub" "$EX_OFFSITE" false
      fail $EX_OFFSITE "copia externa FALLO ($sub): $(rclone_errtext "$(cat "$BACKUP_DIR/logs/.offsite.err")")"; return 1
    fi
    if ! rclone_verify backup-offsite "/backups/$sub" "$BACKUP_REMOTE/$sub" "${args[@]}"; then
      monitor_status "offsite-$sub" "$EX_OFFSITE" false
      fail $EX_OFFSITE "copia externa: la verificacion FALLO ($sub): $RCLONE_ERR"; return 1
    fi
    log INFO "copia externa $sub: subida y VERIFICADA"
    if [[ $sub == sql ]]; then for n in "${names[@]}"; do [[ "$n" == *_full_* ]] && OFF_FULL_OK=1; done; else OFF_FILES_OK=1; fi
    monitor_status "offsite-$sub" 0 true
  done
  rm -f "$BACKUP_DIR/logs/.offsite.err"

  remote_retention || return 1
}

# ---------------------------------------------------------------- retencion remota (dos fases, por tipo)
# Reglas (ver docs/backup-restore.md §8): solo se poda el TIPO que ESTA corrida subio Y verifico (full <- modos all|full; tar de
# archivos <- all|files; los .trn solo junto con los fulls y solo los anteriores al full retenido mas antiguo); el modo `log` nunca
# borra. La antiguedad sale del timestamp del NOMBRE. Se conservan SIEMPRE BACKUP_REMOTE_MIN_KEEP copias completas por tipo.
# Fase 1: listar y planificar TODO (si algo falla, se borra NADA). Fase 2: ejecutar `deletefile` por nombre exacto.
declare -A REMOTE_LIST=()
remote_list() { # remote_list <sql|files>  -> llena REMOTE_LIST[<sub>] con "bytes|nombre"; rc != 0 si el listado falla
  local out rc=0
  out=$(dc run --rm -T --no-deps --quiet-pull backup-offsite lsf "$BACKUP_REMOTE/$1" --files-only --format sp --separator '|' 2>"$BACKUP_DIR/logs/.offsite.err") || rc=$?
  if (( rc != 0 )); then RCLONE_ERR=$(rclone_errtext "$(cat "$BACKUP_DIR/logs/.offsite.err" 2>/dev/null)"); return 1; fi
  REMOTE_LIST[$1]=$(tr -d '\r' <<<"$out")
}
remote_list_has() { grep -qxF -- "$3" <<<"$(cut -d'|' -f2- <<<"${REMOTE_LIST[$1]:-}")" ; }   # remote_list_has <sub> _ <nombre>

remote_retention() {
  (( REMOTE_RETENTION_DAYS > 0 )) || return 0
  case "$MODE" in all|full|files) ;; *) return 0 ;; esac
  if (( ! OFF_FULL_OK && ! OFF_FILES_OK )); then
    log INFO "retencion remota: omitida (esta corrida no subio ni verifico ningun full ni tar)"; return 0
  fi
  log INFO "retencion remota: >${REMOTE_RETENTION_DAYS} dias por nombre, minimo ${REMOTE_MIN_KEEP} copias completas por tipo (solo nombres de este sistema)"
  local now plan sub n line floor="" fguard=0 tts setn pts prot=""
  local -a dels=()        # "sub|nombre"
  local -a gone_tars=()   # timestamps de los tars que ESTA corrida va a borrar
  now=$(date -u +%s)

  # ---- fase 1: listados + planes (sin borrar nada). Primero los tars: los fulls que referencian los manifiestos de tars que
  # SOBREVIVEN no se pueden borrar (si no, un par recuperable quedaria sin su full).
  if (( OFF_FILES_OK || OFF_FULL_OK )); then
    remote_list files || { fail $EX_OFFSITE "retencion remota OMITIDA, no se borro nada: no se pudo listar files/ ($RCLONE_ERR)"; return 1; }
  fi
  if (( OFF_FILES_OK )); then
    for n in "${RUN_FILES[@]}"; do
      remote_list_has files _ "$n" && remote_list_has files _ "$n.sha256" \
        || { fail $EX_OFFSITE "retencion remota OMITIDA, no se borro nada: el listado de files/ no contiene $n y su .sha256"; return 1; }
    done
    plan=$(remote_prune_plan files "$now" "$REMOTE_RETENTION_DAYS" "$REMOTE_MIN_KEEP" "$DB_NAME" <<<"${REMOTE_LIST[files]}") \
      || { fail $EX_OFFSITE "retencion remota OMITIDA, no se borro nada: plan de archivos invalido"; return 1; }
    while IFS= read -r line; do
      case "$line" in
        DELETE\ *)
          n=${line#DELETE }; dels+=("files|$n")
          tts=${n#files_}; tts=${tts%.tar.gz}; gone_tars+=("$tts")
          # el manifiesto del par se va con su tar (set_<ts-tar>_<ts-full>.txt)
          while IFS= read -r setn; do [[ -n "$setn" ]] && dels+=("files|$setn"); done < <(cut -d'|' -f2- <<<"${REMOTE_LIST[files]}" | grep -E "^set_${tts}_${BK_TS}\.txt\$" || true) ;;
        GUARD\ *)  log WARN "retencion remota (archivos): ${line#GUARD }" ;;
        WARN\ *)   log WARN "retencion remota (archivos): ${line#WARN }" ;;
      esac
    done <<<"$plan"
  fi
  if (( OFF_FULL_OK )); then
    remote_list sql || { fail $EX_OFFSITE "retencion remota OMITIDA, no se borro nada: no se pudo listar sql/ ($RCLONE_ERR)"; return 1; }
    for n in "${RUN_SQL[@]}"; do
      [[ "$n" == *_full_* ]] || continue
      remote_list_has sql _ "$n" && remote_list_has sql _ "$n.sha256" \
        || { fail $EX_OFFSITE "retencion remota OMITIDA, no se borro nada: el listado de sql/ no contiene $n y su .sha256 (listado incompleto o remoto distinto)"; return 1; }
    done
    # fulls protegidos = referenciados por un manifiesto cuyo tar (completo) sobrevive a esta corrida
    while IFS= read -r setn; do
      [[ "$setn" =~ ^set_(${BK_TS})_(${BK_TS})\.txt$ ]] || continue
      tts=${BASH_REMATCH[1]}; pts=${BASH_REMATCH[2]}
      remote_list_has files _ "files_${tts}.tar.gz" && remote_list_has files _ "files_${tts}.tar.gz.sha256" || continue
      [[ " ${gone_tars[*]-} " == *" $tts "* ]] && continue
      prot+="$pts "
    done < <(cut -d'|' -f2- <<<"${REMOTE_LIST[files]}" | grep -E "^set_${BK_TS}_${BK_TS}\.txt\$" || true)
    plan=$(remote_prune_plan full "$now" "$REMOTE_RETENTION_DAYS" "$REMOTE_MIN_KEEP" "$DB_NAME" "$prot" <<<"${REMOTE_LIST[sql]}") \
      || { fail $EX_OFFSITE "retencion remota OMITIDA, no se borro nada: plan de fulls invalido"; return 1; }
    while IFS= read -r line; do
      case "$line" in
        DELETE\ *) dels+=("sql|${line#DELETE }") ;;
        FLOOR\ *)  floor=${line#FLOOR } ;;
        GUARD\ *)  fguard=1; log WARN "retencion remota (fulls): ${line#GUARD }" ;;
        WARN\ *)   log WARN "retencion remota (fulls): ${line#WARN }" ;;
      esac
    done <<<"$plan"
    [[ -z "$prot" ]] || log INFO "retencion remota: fulls protegidos por pares conservados: ${prot}"
    if (( ! fguard )); then
      plan=$(remote_prune_plan log "$now" "$REMOTE_RETENTION_DAYS" "$REMOTE_MIN_KEEP" "$DB_NAME" "$floor" <<<"${REMOTE_LIST[sql]}") \
        || { fail $EX_OFFSITE "retencion remota OMITIDA, no se borro nada: plan de logs invalido"; return 1; }
      while IFS= read -r line; do
        case "$line" in
          DELETE\ *) dels+=("sql|${line#DELETE }") ;;
          GUARD\ *)  log WARN "retencion remota (logs): ${line#GUARD }" ;;
        esac
      done <<<"$plan"
    fi
  fi

  # ---- fase 2: ejecutar (principal primero; si se interrumpe queda, como mucho, un .sha256 huerfano e inofensivo)
  if (( ${#dels[@]} == 0 )); then log INFO "retencion remota: nada que borrar"; return 0; fi
  local d done_n=0 rout target
  for d in "${dels[@]}"; do
    sub=${d%%|*}; n=${d#*|}
    for target in "$n" "$n.sha256"; do
      [[ "$target" == "$n" ]] || remote_list_has "$sub" _ "$target" || continue   # el .sha256 puede no existir (logs, manifiestos)
      if ! rout=$(dc run --rm -T --no-deps --quiet-pull backup-offsite deletefile "$BACKUP_REMOTE/$sub/$target" 2>&1); then
        fail $EX_OFFSITE "retencion remota INTERRUMPIDA tras borrar $done_n archivo(s); fallo en $sub/$target: $(rclone_errtext "$rout")"; return 1
      fi
    done
    log INFO "  remoto: borrado $sub/$n"
    done_n=$((done_n + 1))
  done
  log INFO "retencion remota: $done_n archivo(s) borrado(s)"
}

# ---------------------------------------------------------------- retencion local
retention() {
  local out
  log INFO "retencion local: ${RETENTION_DAYS} dias, minimo ${RETENTION_MIN_KEEP} recientes por tipo"
  if ! out=$(dc run --rm -T --no-deps --quiet-pull -e DB_NAME="$DB_NAME" -e RETENTION_DAYS="$RETENTION_DAYS" -e MIN_KEEP="$RETENTION_MIN_KEEP" \
                 backup-files sh /opt/bury-backup/retention.sh 2>&1); then
    log ERROR "$out"; fail $EX_RETENTION "retencion local fallo"; return 1
  fi
  while IFS= read -r l; do log INFO "  $l"; done <<<"$out"
}

# ---------------------------------------------------------------- orquestacion
need_stop=0
if [[ "$BACKUP_STOP_APP" == "true" && ( "$MODE" == all || "$MODE" == files ) ]]; then need_stop=1; fi

if (( need_stop )); then stop_app || fail $EX_APP "no se pudo detener app; se aborta para no respaldar archivos en movimiento"; fi

if (( RC == 0 )); then
  case "$MODE" in
    all)   sql_full && files_backup && write_set ;;
    full)  sql_full ;;
    log)   sql_log ;;
    files) files_backup ;;
  esac || true
fi

if (( APP_STOPPED == 1 )); then start_app || true; fi

if (( RC == 0 )); then offsite || true; fi
if (( RC == 0 )) && [[ "$MODE" != log ]]; then retention || true; fi

DUR=$(( $(date +%s) - START ))
if (( RC == 0 )); then log INFO "=== backup '$MODE' OK (${DUR}s) ==="; else log ERROR "=== backup '$MODE' FALLO codigo=$RC (${DUR}s) ==="; fi
exit $RC
