#!/usr/bin/env bash
# PRUEBA DE RESTAURACION DESDE LA COPIA SECUNDARIA (BACKUP_REMOVABLE_DIR, p. ej. el disco local cifrado) en un SQL Server DESCARTABLE.
#
#   scripts/backup/restore-test-local-copy.sh [--keep-evidence DIR] [--from-backup-dir DIR]
#
# --from-backup-dir DIR: en vez de la copia secundaria, toma el full mas nuevo y sus logs de <DIR>/sql (un BACKUP_DIR real, p. ej. el de la
#   instalacion de produccion; DIR/.bury-backup-root debe existir). Solo LECTURA sobre DIR (se monta :ro); la restauracion corre en el SQL
#   Server descartable. Se mide y registra el tiempo real de cada etapa (RTO observado, no una garantia).
#
# Toma el full MAS NUEVO de <BACKUP_REMOVABLE_DIR>/bury-backup/sql y los logs posteriores, los copia (verificando sha256) a un directorio
# temporal, levanta un proyecto Compose aleatorio con su propio SQL Server, restaura full + logs con restore-sql.sh y valida: RESTORE OK,
# DBCC CHECKDB sin errores, __EFMigrationsHistory no vacia, cantidad de tablas y filas por tabla principal. Despues BORRA todo (contenedores,
# volumenes, temporal). NO toca la base viva, el stack de produccion ni BACKUP_DIR, y no escribe en el destino de la copia (solo lectura).
# Requiere Linux con docker. Usa la imagen de SQL Server ya presente (sin descargas si ya esta). Tarda ~1-2 min.
# Con --keep-evidence DIR deja un resumen (resultado.txt, sin datos de negocio) para el informe.
# Codigos: 0 ok | 1 uso/config | 8 prerequisito | 9 la restauracion o una validacion fallo
# shellcheck source=lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
# shellcheck source=lib-capacity.sh
source "$BK_SCRIPT_DIR/lib-capacity.sh"

EVID="" FROMDIR=""
while (( $# )); do
  case "$1" in
    --keep-evidence) EVID="${2:-}"; shift 2 ;;
    --from-backup-dir) FROMDIR="${2:-}"; shift 2 ;;
    -h|--help) sed -n '2,18p' "$0"; exit 0 ;;
    *) die $EX_USAGE "argumento desconocido: $1" ;;
  esac
done
load_config; load_capacity_config
LOG_FILE=""
[[ "$(uname -s)" == Linux ]] || die $EX_PREREQ "requiere Linux"
command -v docker >/dev/null || die $EX_PREREQ "falta docker"
if [[ -n "$FROMDIR" ]]; then
  [[ "$FROMDIR" == /* && "$FROMDIR" != *..* && -f "$FROMDIR/.bury-backup-root" ]] || die $EX_USAGE "--from-backup-dir debe ser la ruta absoluta de un BACKUP_DIR administrado por backup.sh (falta .bury-backup-root)"
  SRC="${FROMDIR%/}/sql"
else
  [[ -n "$BACKUP_REMOVABLE_DIR" ]] || die $EX_USAGE "BACKUP_REMOVABLE_DIR no esta definido"
  removable_available || die $EX_PREREQ "copia secundaria no disponible: $REMOVABLE_WHY"
  SRC="$BACKUP_REMOVABLE_DIR/bury-backup/sql"
fi
[[ -d "$SRC" ]] || die $EX_PREREQ "no existe $SRC"
# El sql/ de un BACKUP_DIR real es del usuario mssql (750): se lista como root desde un contenedor, solo lectura.
src_ls() { docker run --rm --network none -v "$SRC":/src:ro alpine:3 ls -1 /src 2>/dev/null | tr -d '\r'; }
SRC_LIST=$(src_ls) || true
REPO="$(cd "$BK_SCRIPT_DIR/../.." && pwd)"

full=$(grep -E "^${DB_NAME}_full_${BK_TS}\.bak$" <<<"$SRC_LIST" | sort | tail -n1 || true)
[[ -n "$full" ]] || die $EX_PREREQ "la copia no tiene ningun full de $DB_NAME"
full_ts=$(grep -oE "$BK_TS" <<<"$full")
mapfile -t logs < <(grep -E "^${DB_NAME}_log_${BK_TS}\.trn$" <<<"$SRC_LIST" | sort | while read -r f; do t=$(grep -oE "$BK_TS" <<<"$f"); [[ "$t" > "$full_ts" ]] && echo "$f"; done)

rnd() { od -An -N"${1:-6}" -tx1 /dev/urandom | tr -d ' \n'; }
PROJ="bury-lcrt-$(rnd 4)"; DBN=LocalCopyRestoreTest   # DBN = base DESTINO; el nombre de la base ORIGEN (DB_NAME) se conserva para que restore-sql.sh encuentre los logs
TMPBASE="${TMPDIR:-/tmp}"; T=$(mktemp -d "$TMPBASE/bury-lcrt.XXXXXX"); BKD="$T/backups"; mkdir -p "$BKD"; : >"$T/empty.env"
SA="Aa1!$(rnd 10)"
used=$(docker network ls -q | xargs -r docker network inspect --format '{{range .IPAM.Config}}{{.Subnet}} {{end}}' 2>/dev/null)
SUBNET=""; for i in $(seq 1 80); do n=$((100 + RANDOM % 120)); [[ " $used " == *" 172.29.$n.0/24 "* ]] || { SUBNET="172.29.$n.0/24"; break; }; done
[[ -n "$SUBNET" ]] || { rm -rf "$T"; die $EX_PREREQ "sin subred libre"; }
export COMPOSE_PROJECT_NAME="$PROJ" COMPOSE_ENV_FILES="$T/empty.env" BURY_NET_SUBNET="$SUBNET" MSSQL_SA_PASSWORD="$SA" MSSQL_DATABASE="$DB_NAME" MSSQL_PID=Express DB_MEMORY=2g DB_CPUS=2
export BACKUP_DIR="$BKD" BACKUP_STOP_APP=false BACKUP_REMOVABLE_DIR="" BACKUP_REMOTE=""
export ERP_DOMAIN=tbp ADMIN_EMAIL=bk@example.invalid ADMIN_USERNAME=admin ADMIN_PASSWORD=Zz9!unused0 ERP_DB_USER=u ERP_DB_PASSWORD=x ERP_MIGRATION_USER=m ERP_MIGRATION_PASSWORD=y ERP_IMAGE=bury-lcrt-noexiste:0
dcq() { docker compose --progress quiet "$@" </dev/null; }
RESULT=1
cleanup() {
  cd "$REPO" 2>/dev/null || true
  dcq -p "$PROJ" down -v --remove-orphans >/dev/null 2>&1
  docker volume ls -q --filter "label=com.docker.compose.project=$PROJ" | xargs -r docker volume rm >/dev/null 2>&1
  docker run --rm --network none -v "$T":/t alpine:3 find /t -mindepth 1 -delete >/dev/null 2>&1
  rmdir "$T" 2>/dev/null || rm -rf "$T"
  echo "residuos: contenedores=$(docker ps -aq --filter "label=com.docker.compose.project=$PROJ" | wc -l) volumenes=$(docker volume ls -q --filter "label=com.docker.compose.project=$PROJ" | wc -l) temporal=$([[ -e "$T" ]] && echo si || echo no)"
}
trap cleanup EXIT
[[ "$(cd "$REPO" && dcq config 2>/dev/null | sed -n 's/^name: //p')" == "$PROJ" ]] || die $EX_PREREQ "el proyecto Compose resuelto no es $PROJ: se aborta sin levantar nada"

echo "origen (solo lectura): $SRC"; echo "full: $full  logs posteriores: ${#logs[@]}  proyecto descartable: $PROJ"
T0=$SECONDS
cd "$REPO"
bash "$BK_SCRIPT_DIR/prepare-dir.sh" >/dev/null 2>&1 || die $EX_PREREQ "prepare-dir.sh fallo"
# copia de lo que se va a restaurar, verificando sha256 (la copia es la que se prueba, no el origen). Se lee desde un contenedor como root: los
# backups copiados por backup-files conservan el modo 0640 del origen y el usuario normal no puede leerlos.
docker run --rm --network none -v "$SRC":/src:ro -v "$BKD/sql":/dst alpine:3 sh -c '
  set -eu
  for f in "$@"; do
    [ -f "/src/$f.sha256" ] || { echo "falta $f.sha256 en la copia" >&2; exit 3; }
    cp -- "/src/$f" "/dst/$f"
    exp=$(cut -d" " -f1 "/src/$f.sha256"); got=$(sha256sum "/dst/$f" | cut -d" " -f1)
    [ -n "$exp" ] && [ "$exp" = "$got" ] || { echo "sha256 de $f NO coincide con su .sha256: copia corrupta" >&2; exit 4; }
  done
  chmod 0644 /dst/*; chmod 0755 /dst' _ "$full" "${logs[@]}" || die $EX_RESTORE "no se pudo copiar/verificar la cadena desde la copia secundaria"
echo "OK  full + ${#logs[@]} log(s) copiados con sha256 verificado"
T_COPY=$((SECONDS - T0)); T1=$SECONDS

dcq up -d --no-deps db >/dev/null 2>&1 || die $EX_PREREQ "no se pudo levantar el SQL Server descartable"
for i in $(seq 1 60); do [[ "$(dcq ps --format '{{.Health}}' db 2>/dev/null | head -n1)" == healthy ]] && break; sleep 3; done
sqlq() { dcq exec -T db bash -c 'SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" exec /opt/mssql-tools18/bin/sqlcmd -C -b -I -S localhost -U sa -d master -h -1 -W -Q "SET NOCOUNT ON; $0"' "$1" | tr -d '\r'; }
[[ "$(sqlq 'SELECT 1')" == 1 ]] || die $EX_PREREQ "el SQL Server descartable no responde"

T_SQLUP=$((SECONDS - T1)); T2=$SECONDS
OUT="$T/resultado.txt"; : >"$OUT"
say() { echo "$*" | tee -a "$OUT"; }
bash "$BK_SCRIPT_DIR/restore-sql.sh" --backup "$full" --target-db "$DBN" --with-logs >"$T/restore.out" 2>&1; rc=$?
if (( rc != 0 )); then tail -n 15 "$T/restore.out"; die $EX_RESTORE "restore-sql.sh fallo (rc $rc)"; fi
T_RESTORE=$((SECONDS - T2)); T3=$SECONDS
applied=$(grep -oE 'logs aplicados: [0-9]+' "$T/restore.out" | tail -n1 | grep -oE '[0-9]+$')
[[ "$applied" =~ ^[0-9]+$ && "$applied" == "${#logs[@]}" ]] || die $EX_RESTORE "se copiaron ${#logs[@]} log(s) pero restore-sql.sh aplico '${applied:-?}': la cadena NO se restauro completa"
say "OK  RESTORE de full + $applied log(s) -> [$DBN] ONLINE (logs aplicados = logs copiados)"
dbcc=$(sqlq "DBCC CHECKDB ([$DBN]) WITH NO_INFOMSGS" 2>&1) || { echo "$dbcc"; die $EX_RESTORE "DBCC CHECKDB reporto errores"; }
[[ -z "$dbcc" ]] || { echo "$dbcc"; die $EX_RESTORE "DBCC CHECKDB devolvio mensajes"; }
say "OK  DBCC CHECKDB sin errores"
T_DBCC=$((SECONDS - T3))
mig=$(sqlq "SELECT COUNT(*) FROM [$DBN].dbo.__EFMigrationsHistory" | head -n1); tbl=$(sqlq "SELECT COUNT(*) FROM [$DBN].sys.tables" | head -n1)
[[ "$mig" =~ ^[0-9]+$ ]] && (( mig > 0 )) || die $EX_RESTORE "__EFMigrationsHistory vacia o ilegible ($mig)"
[[ "$tbl" =~ ^[0-9]+$ ]] && (( tbl > 0 )) || die $EX_RESTORE "la base restaurada no tiene tablas ($tbl)"
say "OK  __EFMigrationsHistory=$mig migraciones, $tbl tablas"
for t in Clientes Ventas Creditos Cuotas Productos; do
  c=$(sqlq "IF OBJECT_ID(N'[$DBN].dbo.$t') IS NOT NULL SELECT COUNT(*) FROM [$DBN].dbo.$t ELSE SELECT -1" | head -n1); say "    filas $t: $c"
done
say "TIEMPOS (s, medidos en esta corrida; no son garantia): copia+sha256=$T_COPY levantar-SQL=$T_SQLUP restore=$T_RESTORE dbcc=$T_DBCC total=$((SECONDS - T0))"
say "ORIGEN: $SRC  full=$full  logs=${#logs[@]}"
say "RESULTADO: restauracion desde la copia secundaria OK ($(date -u +%Y-%m-%dT%H:%M:%SZ))"
[[ -n "$EVID" ]] && { mkdir -p "$EVID" && cp "$OUT" "$EVID/resultado.txt"; echo "evidencia: $EVID/resultado.txt"; }
RESULT=0
exit 0
