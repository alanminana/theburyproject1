#!/usr/bin/env bash
# RECUPERACION COMPLETA ESTRICTA ("perdi el servidor"): restaura archivos persistentes + base SQL desde BACKUP_DIR y recien despues levanta el stack.
# Reemplaza la secuencia manual de docs/backup-restore.md §10 para que NINGUN fallo de restauracion pueda terminar en una base VACIA:
# si `docker compose up` corriera tras un restore fallido, `migrate` crearia el esquema desde cero y el sistema arrancaria "sano" y sin datos.
#
#   scripts/backup/recover.sh --plan                        muestra el par elegido y los pasos; NO cambia nada
#   scripts/backup/recover.sh --apply [opciones]            ejecuta la recuperacion
# Opciones:
#   --archive files_<ts>.tar.gz   tar de archivos (default: el del manifiesto set_*.txt mas nuevo, que acredita el PAR full+tar)
#   --full <full>.bak             full de SQL (default: el del manifiesto). Con --archive sin manifiesto hay que indicarlo
#   --with-logs                   aplica los logs posteriores al full (opcion B de §10.2: menos perdida SQL, pero puede referenciar documentos posteriores al tar)
#   --force-files                 permite reponer archivos aunque los volumenes ya tengan contenido (reintento tras un fallo posterior a restore-files)
#   --skip-checkdb                omite DBCC CHECKDB de la compuerta (bases grandes; el resto de la compuerta se mantiene)
#   --min-users N                 usuarios minimos que debe tener la base restaurada (default 1: una base operativa nunca tiene 0)
#   --no-start                    restaura y verifica, pero NO levanta app/migrate/caddy (para inspeccionar o medir antes de arrancar)
#
# Garantias:
#   1. ANTES de tocar nada: verifica que existan el tar y el full, y su sha256 (y el de cada log con --with-logs). Falla => no se cambia NADA.
#   2. Cualquier paso que falle detiene la recuperacion con su codigo; NUNCA se levanta app/migrate tras un fallo. Queda la marca
#      BACKUP_DIR/.recovery-incomplete (scripts/deploy/preflight.sh la rechaza) hasta que una corrida completa termine bien.
#   3. COMPUERTA previa al arranque: la base restaurada debe estar ONLINE, tener migraciones y >= --min-users usuarios y pasar DBCC CHECKDB.
#   4. Tras arrancar: migrate no pudo recrear la base (usuarios y migraciones identicos a los de la compuerta).
#   5. SQL Server debe poder leer los .bak/.trn (restore-sql.sh corrige dueño/modo y, si no puede, aborta).
# Requiere `app` detenida y que la base destino NO exista (restore-sql.sh no sobrescribe). Respeta COMPOSE_PROJECT_NAME / COMPOSE_ENV_FILES.
# Codigos: 0 ok | 1 uso | 3 respaldo ausente o corrupto | 8 prerequisito | 9 restore/compuerta fallida | 10 el stack no volvio a estar sano
# shellcheck source=lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

ACTION="" ARCHIVE="" FULL="" WITH_LOGS=0 FORCE_FILES=0 SKIP_CHECKDB=0 MIN_USERS=1 NO_START=0
while (( $# )); do
  case "$1" in
    --plan|--apply) ACTION="${1#--}"; shift ;;
    --archive) ARCHIVE="${2:-}"; shift 2 ;;
    --full) FULL="${2:-}"; shift 2 ;;
    --with-logs) WITH_LOGS=1; shift ;;
    --force-files) FORCE_FILES=1; shift ;;
    --skip-checkdb) SKIP_CHECKDB=1; shift ;;
    --min-users) MIN_USERS="${2:-}"; shift 2 ;;
    --no-start) NO_START=1; shift ;;
    -h|--help) sed -n '2,29p' "$0"; exit 0 ;;
    *) echo "argumento desconocido: $1" >&2; exit "$EX_USAGE" ;;
  esac
done
[[ -n "$ACTION" ]] || { sed -n '2,29p' "$0"; exit "$EX_USAGE"; }
is_uint "$MIN_USERS" && (( MIN_USERS >= 1 )) || { echo "--min-users debe ser un entero >= 1 (una base operativa nunca tiene 0 usuarios)" >&2; exit "$EX_USAGE"; }

load_config
BK_SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
[[ -d "$BACKUP_DIR/sql" && -d "$BACKUP_DIR/files" ]] || die "$EX_VERIFY" "BACKUP_DIR=$BACKUP_DIR no tiene sql/ y files/: traer primero los respaldos (fetch-offsite.sh o copia del extraible)"
MARK="$BACKUP_DIR/.recovery-incomplete"
RE_TS='[0-9]{4}-[0-9]{2}-[0-9]{2}_[0-9]{6}Z'

# --- 1. elegir el par (full + tar)
if [[ -z "$ARCHIVE" ]]; then
  set_file=$(find "$BACKUP_DIR/files" -maxdepth 1 -type f -name 'set_*.txt' 2>/dev/null | sort | tail -n1 || true)
  [[ -n "$set_file" ]] || die "$EX_VERIFY" "no hay manifiesto set_*.txt (par full+tar acreditado) en $BACKUP_DIR/files: indicar --archive y --full explicitamente (opcion B de §10.2)"
  if [[ "$(basename "$set_file")" =~ ^set_(${RE_TS})_(${RE_TS})\.txt$ ]]; then
    ARCHIVE="files_${BASH_REMATCH[1]}.tar.gz"; [[ -n "$FULL" ]] || FULL="${DB_NAME}_full_${BASH_REMATCH[2]}.bak"
  else die "$EX_VERIFY" "nombre de manifiesto inesperado: $set_file"; fi
fi
[[ -n "$FULL" ]] || die "$EX_USAGE" "con --archive hay que indicar tambien --full (el manifiesto no se pudo usar)"
[[ "$ARCHIVE" =~ ^files_${RE_TS}\.tar\.gz$ ]] || die "$EX_USAGE" "--archive invalido: $ARCHIVE"
[[ "$FULL" =~ ^[A-Za-z0-9_.-]+_full_${RE_TS}\.bak$ ]] || die "$EX_USAGE" "--full invalido: $FULL"
log INFO "=== recover: par elegido -> archivos=$ARCHIVE | sql=$FULL | logs=$WITH_LOGS | base=$DB_NAME ==="

# --- 2. verificacion previa de TODO (no se cambia nada si algo falta o esta corrupto)
verificar_sha() { # verificar_sha <ruta relativa a BACKUP_DIR>
  local rel=$1
  [[ -s "$BACKUP_DIR/$rel" || "$(tools sh -c 'test -s "/backups/$0" && echo yes' "$rel" | tr -d '\r')" == yes ]] || die "$EX_VERIFY" "falta o esta vacio: $BACKUP_DIR/$rel"
  [[ "$(tools sh -c 'test -f "/backups/$0.sha256" && echo yes' "$rel" | tr -d '\r')" == yes ]] || die "$EX_VERIFY" "falta $rel.sha256: no se puede acreditar la integridad"
  tools sh -c 'cd "/backups/$(dirname "$0")" && sha256sum -c "$(basename "$0").sha256"' "$rel" >/dev/null 2>&1 || die "$EX_VERIFY" "sha256 de $rel NO coincide: respaldo corrupto o modificado (no se cambio nada)"
}
verificar_sha "files/$ARCHIVE"
verificar_sha "sql/$FULL"
if (( WITH_LOGS )); then
  SQL_LS=$(tools sh -c 'ls -1 /backups/sql' | tr -d '\r') || die "$EX_VERIFY" "no se pudo listar $BACKUP_DIR/sql para leer la cadena de logs (no se cambio nada)"
  fts=${FULL##*_full_}; fts=${fts%.bak}   # solo el timestamp que sigue a _full_
  while IFS= read -r lg; do
    [[ -n "$lg" ]] || continue
    lts=$(grep -oE "$RE_TS" <<<"$lg")
    if [[ "$lts" > "$fts" ]]; then verificar_sha "sql/$lg"; fi
  done < <(grep -E "^${DB_NAME}_log_${RE_TS}\.trn$" <<<"$SQL_LS" | sort || true)
fi
log INFO "OK  respaldos presentes y con sha256 correcto"
service_running app && die "$EX_PREREQ" "app esta corriendo: detenerla antes de recuperar (docker compose stop app)"

if [[ "$ACTION" == plan ]]; then
  log INFO "PLAN: 1) restore-files.sh --archive $ARCHIVE$( ((FORCE_FILES)) && echo ' --force')  2) up db  3) restore-sql.sh --backup $FULL --target-db $DB_NAME$( ((WITH_LOGS)) && echo ' --with-logs')  4) compuerta (ONLINE, migraciones, >= $MIN_USERS usuarios$( ((SKIP_CHECKDB)) || echo ', DBCC CHECKDB'))  5) $( ((NO_START)) && echo 'FIN (--no-start)' || echo 'up -d --wait y verificacion posterior')"
  exit 0
fi

# --- 3. ejecucion estricta: cualquier fallo sale SIN levantar app/migrate
# La marca .recovery-incomplete se escribe recien cuando los archivos YA se repusieron (primer punto destructivo): si restore-files se niega
# (volumenes con contenido = instalacion viva, error de uso) no cambia nada y NO debe bloquear los despliegues de una instalacion sana.
marcar() { echo "recuperacion iniciada $(date -u +%Y-%m-%dT%H:%M:%SZ) archivos=$ARCHIVE sql=$FULL" >"$MARK" 2>/dev/null || die "$EX_PREREQ" "no se pudo escribir la marca $MARK"; }
falla() { # falla <codigo> <mensaje>
  log ERROR "RECUPERACION DETENIDA: $2"
  if [[ -e "$MARK" ]]; then log ERROR "NO se levanto app/migrate (migrate crearia una base VACIA). Corregir la causa y reintentar (con --force-files si los archivos ya se repusieron). Marca: $MARK"
  else log ERROR "No se cambio nada (no se levanto app/migrate)."; fi
  exit "$1"
}

args=(--archive "$ARCHIVE"); (( FORCE_FILES )) && args+=(--force)
RFOUT=$(mktemp); trap 'rm -f "$RFOUT"' EXIT
frc=0; bash "$BK_SCRIPT_DIR/restore-files.sh" "${args[@]}" >"$RFOUT" 2>&1 || frc=$?   # sin tuberia: con pipefail un `| tee || true` enmascararia el codigo
cat "$RFOUT" || true   # solo informativo: un fallo de impresion no debe cortar antes de decidir la marca
if (( frc != 0 )); then
  # Solo el rechazo explicito "ya tiene contenido" (sin --force) garantiza que NO se cambio nada; cualquier otro fallo puede haber extraido
  # parte de los archivos (p. ej. al desellar la clave de la CA, que ocurre despues de extraer): estado incierto => marca.
  if (( ! FORCE_FILES )) && grep -q 'ya tiene contenido' "$RFOUT"; then
    falla "$EX_RESTORE" "restore-files.sh se nego: los volumenes ya tienen contenido (esta instalacion NO esta vacia). Si se quiere REEMPLAZARLOS: --force-files"
  fi
  marcar
  falla "$EX_RESTORE" "restore-files.sh fallo (codigo $frc)"
fi
marcar

urc=0; dc up -d --wait db >/dev/null 2>&1 || urc=$?
(( urc == 0 )) || falla "$EX_PREREQ" "no se pudo levantar el servicio db (codigo $urc)"

rargs=(--backup "$FULL" --target-db "$DB_NAME"); (( WITH_LOGS )) && rargs+=(--with-logs)
src=0; bash "$BK_SCRIPT_DIR/restore-sql.sh" "${rargs[@]}" || src=$?
(( src == 0 )) || falla "$src" "restore-sql.sh fallo (codigo $src)"

# --- 4. compuerta: la base restaurada debe ser una base OPERATIVA, no un cascaron
estado=$(sql_scalar "SELECT state_desc FROM sys.databases WHERE name = N'$DB_NAME'" || true)
[[ "$estado" == ONLINE ]] || falla "$EX_RESTORE" "compuerta: [$DB_NAME] no esta ONLINE (estado: ${estado:-inexistente})"
mig=$(sql_scalar "SELECT COUNT(*) FROM [$DB_NAME].dbo.__EFMigrationsHistory" || true)
is_uint "$mig" && (( mig >= 1 )) || falla "$EX_RESTORE" "compuerta: la base restaurada no tiene migraciones (__EFMigrationsHistory=${mig:-?})"
usr=$(sql_scalar "SELECT COUNT(*) FROM [$DB_NAME].dbo.AspNetUsers" || true)
is_uint "$usr" && (( usr >= MIN_USERS )) || falla "$EX_RESTORE" "compuerta: usuarios=${usr:-?} < $MIN_USERS: base vacia o incompleta"
if (( SKIP_CHECKDB )); then log WARN "compuerta: DBCC CHECKDB omitido (--skip-checkdb)"
else
  sql_exec "DBCC CHECKDB (N'$DB_NAME') WITH NO_INFOMSGS" >/dev/null || falla "$EX_RESTORE" "compuerta: DBCC CHECKDB reporto errores en [$DB_NAME]"
  log INFO "compuerta: DBCC CHECKDB OK"
fi
log INFO "OK  compuerta superada: ONLINE, migraciones=$mig, usuarios=$usr"

if (( NO_START )); then
  log INFO "--no-start: base restaurada y verificada; stack NO levantado. Marca $MARK se conserva hasta completar la recuperacion (rm tras 'docker compose up -d --wait' y verificar)."
  exit 0
fi

# --- 5. resto del stack: db-init re-vincula usuarios, migrate valida el esquema (no-op), app, caddy
crc=0; dc up -d --wait >/dev/null 2>&1 || crc=$?
(( crc == 0 )) || { log ERROR "RECUPERACION INCOMPLETA: el stack no quedo sano (codigo $crc); la base SI fue restaurada y verificada. Ver: docker compose ps / logs"; exit "$EX_APP"; }
mig2=$(sql_scalar "SELECT COUNT(*) FROM [$DB_NAME].dbo.__EFMigrationsHistory" || true)
usr2=$(sql_scalar "SELECT COUNT(*) FROM [$DB_NAME].dbo.AspNetUsers" || true)
[[ "$mig2" == "$mig" && "$usr2" == "$usr" ]] || { log ERROR "tras arrancar, migraciones/usuarios cambiaron ($mig/$usr -> ${mig2:-?}/${usr2:-?}): la base fue modificada o recreada"; exit "$EX_RESTORE"; }
rm -f "$MARK"
log INFO "=== recover OK: stack sano; base restaurada con migraciones=$mig usuarios=$usr ==="
