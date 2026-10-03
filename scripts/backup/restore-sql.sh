#!/usr/bin/env bash
# Restaura un backup SQL (full, y opcionalmente la cadena de logs) en una base, con parametros EXPLICITOS.
#
#   scripts/backup/restore-sql.sh --list
#   scripts/backup/restore-sql.sh --backup <full.bak> --target-db <nombre> [--with-logs] [--stopat 'YYYY-MM-DD HH:MM:SS']
#                                 [--overwrite --confirm-overwrite <nombre>]
#
#   --backup      nombre del archivo (dentro de $BACKUP_DIR/sql), p.ej. TheBuryProjectDb_full_2026-09-21_230000Z.bak
#   --target-db   base destino. Si NO existe se crea; si existe se ABORTA salvo --overwrite + --confirm-overwrite <mismo nombre>.
#   --with-logs   aplica los log backups (.trn) posteriores al full, en orden (recuperacion al ultimo log = RPO ~15 min).
#   --stopat      con --with-logs: recupera hasta ese instante (UTC, como los nombres de archivo) y detiene la cadena ahi.
#
# Restaurar sobre la base de PRODUCCION viva requiere --overwrite y detener `app` antes: este script lo exige.
# Los archivos de la base restaurada se relocalizan a /var/opt/mssql/data/<target>*.mdf|ndf|ldf (no pisa los de otra base).
# Codigos de salida: 0 ok | 1 uso | 8 prerequisito | 9 restore rechazado/fallido | 3 backup invalido
# shellcheck source=lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

BACKUP="" TARGET="" WITH_LOGS=0 STOPAT="" OVERWRITE=0 CONFIRM="" LIST=0
while (( $# )); do
  case "$1" in
    --backup) BACKUP="${2:-}"; shift 2 ;;
    --target-db) TARGET="${2:-}"; shift 2 ;;
    --with-logs) WITH_LOGS=1; shift ;;
    --stopat) STOPAT="${2:-}"; shift 2 ;;
    --overwrite) OVERWRITE=1; shift ;;
    --confirm-overwrite) CONFIRM="${2:-}"; shift 2 ;;
    --list) LIST=1; shift ;;
    -h|--help) sed -n '2,17p' "$0"; exit 0 ;;
    *) die $EX_USAGE "argumento desconocido: $1" ;;
  esac
done

load_config
mkdir -p "$BACKUP_DIR/logs"
check_prereqs

if (( LIST )); then
  echo "Backups en $BACKUP_DIR/sql (full y log):"
  dc exec -T db bash -c 'ls -1 /backups/sql | grep -E "\.(bak|trn)$" | sort'
  exit 0
fi

[[ -n "$BACKUP" ]] || die $EX_USAGE "falta --backup (usar --list para ver los disponibles)"
[[ -n "$TARGET" ]] || die $EX_USAGE "falta --target-db"
[[ "$BACKUP" =~ ^[A-Za-z0-9_.-]+_full_[0-9]{4}-[0-9]{2}-[0-9]{2}_[0-9]{6}Z\.bak$ ]] || die $EX_USAGE "--backup debe ser el NOMBRE de un full (sin ruta): ${DB_NAME}_full_AAAA-MM-DD_HHMMSSZ.bak"
[[ "$TARGET" =~ ^[A-Za-z_][A-Za-z0-9_]{0,63}$ ]] || die $EX_USAGE "--target-db invalido (letras, digitos y _)"
[[ -z "$STOPAT" || "$STOPAT" =~ ^[0-9]{4}-[0-9]{2}-[0-9]{2}\ [0-9]{2}:[0-9]{2}:[0-9]{2}$ ]] || die $EX_USAGE "--stopat con formato 'YYYY-MM-DD HH:MM:SS'"
[[ -z "$STOPAT" || $WITH_LOGS == 1 ]] || die $EX_USAGE "--stopat requiere --with-logs"
[[ "$TARGET" != "master" && "$TARGET" != "model" && "$TARGET" != "msdb" && "$TARGET" != "tempdb" ]] || die $EX_USAGE "--target-db no puede ser una base del sistema"

log INFO "=== restore-sql inicio: $BACKUP -> [$TARGET] logs=$WITH_LOGS stopat='${STOPAT:-}' ==="

# 1) el archivo existe, no esta vacio y coincide con su sha256
size=$(dc exec -T db bash -c 'test -s "/backups/sql/$0" && stat -c %s "/backups/sql/$0"' "$BACKUP" 2>/dev/null | tr -d '\r' || true)
is_uint "$size" && (( size > 0 )) || die $EX_VERIFY "el backup '$BACKUP' no existe o esta vacio en $BACKUP_DIR/sql"
if [[ "$(tools sh -c 'test -f "/backups/sql/$0.sha256" && echo yes' "$BACKUP" | tr -d '\r')" == yes ]]; then
  tools sh -c 'cd /backups/sql && sha256sum -c "$0.sha256"' "$BACKUP" >/dev/null 2>&1 || die $EX_VERIFY "sha256 de $BACKUP NO coincide: backup corrupto o modificado"
  log INFO "sha256 OK"
else
  log WARN "no hay $BACKUP.sha256: se omite esa verificacion"
fi

# 2) proteccion contra sobrescritura accidental
exists=$(sql_scalar "SELECT CASE WHEN DB_ID(N'$TARGET') IS NULL THEN 0 ELSE 1 END")
if [[ "$exists" == "1" ]]; then
  (( OVERWRITE )) || die $EX_RESTORE "la base [$TARGET] YA EXISTE: no se sobrescribe. Para reemplazarla: --overwrite --confirm-overwrite $TARGET"
  [[ "$CONFIRM" == "$TARGET" ]] || die $EX_RESTORE "--overwrite requiere --confirm-overwrite $TARGET (mismo nombre, escrito a mano)"
  if [[ "$TARGET" == "$DB_NAME" ]] && service_running app; then
    die $EX_RESTORE "app esta corriendo: detenerla antes de reemplazar [$TARGET] (docker compose stop app)"
  fi
  log WARN "SE REEMPLAZARA la base [$TARGET] con $BACKUP"
fi

# 3) verificar el backup y leer sus archivos logicos
sql_exec "RESTORE VERIFYONLY FROM DISK = N'/backups/sql/$BACKUP' WITH CHECKSUM" >/dev/null || die $EX_VERIFY "RESTORE VERIFYONLY fallo para $BACKUP"
log INFO "RESTORE VERIFYONLY OK"

filelist=$(sql_rows "RESTORE FILELISTONLY FROM DISK = N'/backups/sql/$BACKUP'") || die $EX_RESTORE "RESTORE FILELISTONLY fallo"
moves="" n=0
while IFS='|' read -r logical _phys type _rest; do
  [[ -n "$logical" ]] || continue
  [[ "$logical" =~ ^[A-Za-z0-9_.\ -]+$ ]] || die $EX_RESTORE "nombre logico inesperado: '$logical'"
  ext=mdf; [[ "$type" == "L" ]] && ext=ldf; [[ "$type" == "D" && $n -gt 0 ]] && ext=ndf
  suffix=""; (( n > 0 )) && suffix="_$n"
  moves+=", MOVE N'${logical}' TO N'/var/opt/mssql/data/${TARGET}${suffix}.${ext}'"
  n=$((n+1))
done <<<"$filelist"
(( n >= 2 )) || die $EX_RESTORE "no se pudo leer la lista de archivos del backup"

# 4) RESTORE
recovery="RECOVERY"; (( WITH_LOGS )) && recovery="NORECOVERY"
replace=""
if [[ "$exists" == "1" ]]; then
  replace=", REPLACE"
  sql_exec "ALTER DATABASE [$TARGET] SET SINGLE_USER WITH ROLLBACK IMMEDIATE" >/dev/null || die $EX_RESTORE "no se pudo pasar [$TARGET] a SINGLE_USER"
fi
log INFO "RESTORE DATABASE [$TARGET] FROM $BACKUP ($recovery)"
if ! sql_exec "RESTORE DATABASE [$TARGET] FROM DISK = N'/backups/sql/$BACKUP' WITH CHECKSUM${moves}${replace}, $recovery, STATS = 25" >/dev/null; then
  die $EX_RESTORE "RESTORE DATABASE fallo (la base [$TARGET] puede haber quedado en estado RESTORING/SINGLE_USER; revisar antes de reintentar)"
fi

# 5) cadena de logs
if (( WITH_LOGS )); then
  full_ts=$(grep -oE '[0-9]{4}-[0-9]{2}-[0-9]{2}_[0-9]{6}Z' <<<"$BACKUP")
  stop_ts=""
  if [[ -n "$STOPAT" ]]; then stop_ts="${STOPAT:0:10}_${STOPAT:11:2}${STOPAT:14:2}${STOPAT:17:2}Z"; fi
  logs=$(dc exec -T db bash -c 'ls -1 /backups/sql | grep -E "^${0}_log_[0-9]{4}-[0-9]{2}-[0-9]{2}_[0-9]{6}Z\.trn$" | sort' "$DB_NAME" | tr -d '\r' || true)
  applied=0
  for f in $logs; do
    t=$(grep -oE '[0-9]{4}-[0-9]{2}-[0-9]{2}_[0-9]{6}Z' <<<"$f")
    [[ "$t" > "$full_ts" ]] || continue
    stopclause=""
    [[ -n "$STOPAT" ]] && stopclause=", STOPAT = N'$STOPAT'"
    if ! sql_exec "RESTORE LOG [$TARGET] FROM DISK = N'/backups/sql/$f' WITH CHECKSUM, NORECOVERY${stopclause}" >/dev/null; then
      die $EX_RESTORE "RESTORE LOG fallo en $f (cadena rota o log inconsistente). [$TARGET] queda en RESTORING: NO usar."
    fi
    applied=$((applied+1))
    # con --stopat: el primer log cuyo nombre (instante en que se genero) es >= stopat ya cubre ese punto
    if [[ -n "$stop_ts" && ! "$t" < "$stop_ts" ]]; then break; fi
  done
  log INFO "logs aplicados: $applied"
  sql_exec "RESTORE DATABASE [$TARGET] WITH RECOVERY" >/dev/null || die $EX_RESTORE "RESTORE ... WITH RECOVERY fallo"
fi

if [[ "$exists" == "1" ]]; then sql_exec "ALTER DATABASE [$TARGET] SET MULTI_USER" >/dev/null || true; fi
state=$(sql_scalar "SELECT state_desc FROM sys.databases WHERE name = N'$TARGET'")
[[ "$state" == "ONLINE" ]] || die $EX_RESTORE "[$TARGET] no quedo ONLINE (estado: ${state:-?})"
log INFO "=== restore-sql OK: [$TARGET] ONLINE ==="
if [[ "$TARGET" == "$DB_NAME" ]]; then
  log INFO "Siguiente paso: docker compose up -d   (db-init re-vincula los usuarios de la app; migrate valida el esquema)"
fi
