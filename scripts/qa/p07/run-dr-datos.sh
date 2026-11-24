#!/usr/bin/env bash
# P-06/P-07 - RECUPERACION con DATOS COMERCIALES SINTETICOS CONOCIDOS (focalizada). Linux / WSL (usa chown/stat de root en contenedores).
#
# Pila A (descartable): los recorridos de P-07 cargan por la aplicacion clientes, compras, stock, cotizaciones, ventas, facturas, devoluciones, notas de
# credito, credito personal con cuotas, documentos subidos y caja. Con `app` detenida (la base queda quieta) se toma una HUELLA de TODAS las tablas
# de usuario (filas + CHECKSUM_AGG(BINARY_CHECKSUM(*))) y del arbol de uploads, y se hace `backup.sh all`.
# Pila B (descartable, volumenes vacios): los respaldos se copian como root:root 600 (como llegan de un extraible) y se recupera con
# scripts/backup/recover.sh. Casos negativos ANTES del positivo: sha256 alterado, tar ausente, .bak corrupto con sha256 recalculado (falla de SQL)
# => la recuperacion se detiene, NO levanta app/migrate, deja la marca .recovery-incomplete y preflight.sh la rechaza.
# Positivo: huella de B (antes de arrancar app) == huella de A; uploads identicos; sin violaciones de FK/CHECK; luego el stack completo y sano.
#
#   bash scripts/qa/p07/run-dr-datos.sh --image <imagen ya construida> [--keep]
# No toca ninguna otra pila ni base: proyectos buryp07dr<hex>a / b, redes, volumenes y puertos propios; todo se destruye al salir (salvo --keep).
set -uo pipefail

IMAGE=""; KEEP=0
while [ $# -gt 0 ]; do
  case "$1" in
    --image) IMAGE="$2"; shift ;;
    --keep) KEEP=1 ;;
    *) echo "opcion desconocida: $1"; exit 2 ;;
  esac
  shift
done
[ -n "$IMAGE" ] || { echo "uso: run-dr-datos.sh --image <imagen>"; exit 2; }
REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
cd "$REPO"
for c in docker python3 openssl; do command -v "$c" >/dev/null 2>&1 || { echo "falta el comando: $c"; exit 2; }; done
docker image inspect "$IMAGE" >/dev/null 2>&1 || { echo "la imagen $IMAGE no existe localmente"; exit 2; }

RAND="$(openssl rand -hex 3)"; PA="buryp07${RAND}"; PB="buryp07$(openssl rand -hex 3)"   # la guardia de p07_recorridos exige buryp07<6 hex> para la pila A
T="$(mktemp -d)"; chmod 700 "$T"
pw() { openssl rand -base64 24 | tr -dc 'A-Za-z0-9' | head -c 20; }
freeport() { python3 -c "import socket;s=socket.socket();s.bind(('127.0.0.1',0));print(s.getsockname()[1]);s.close()"; }
ALPINE='alpine:3@sha256:294b683cb724975bec92580e1e685676bd4b50bda910ddb8c51d4cabeaec77e6'
SA="Sa$(pw)#9a"; ADM="Adm$(pw)#3d"
mkenv() { # mkenv <archivo> <backup_dir> <subred>
  cat > "$1" <<EOF
MSSQL_SA_PASSWORD=$SA
MSSQL_DATABASE=BuryP07
ERP_DB_USER=erp_app_p07
ERP_DB_PASSWORD=Ap$(pw)#7b
ERP_MIGRATION_USER=erp_mig_p07
ERP_MIGRATION_PASSWORD=Mg$(pw)#5c
MSSQL_PID=Developer
ADMIN_EMAIL=admin-p07@example.invalid
ADMIN_USERNAME=admin_p07
ADMIN_PASSWORD=$ADM
ERP_DOMAIN=p07.example.invalid
ERP_IMAGE=$IMAGE
BURY_NET_SUBNET=$3
APP_PORT=$(freeport)
MSSQL_HOST_PORT=$(freeport)
ERP_HTTPS_PORT=$(freeport)
ERP_BIND_ADDRESS=127.0.0.1
BACKUP_DIR=$2
CSP_MODE=Off
EOF
  chmod 600 "$1"
}
mkdir -p "$T/b1" "$T/b2"
mkenv "$T/envA" "$T/b1" "172.30.$((100 + 0x${RAND:0:2} % 50)).0/24"
mkenv "$T/envB" "$T/b2" "172.30.$((160 + 0x${RAND:2:2} % 50)).0/24"
dcA() { docker compose --progress quiet -p "$PA" --env-file "$T/envA" -f "$REPO/docker-compose.yml" -f "$REPO/docker-compose.ci.yml" "$@"; }
dcB() { docker compose --progress quiet -p "$PB" --env-file "$T/envB" -f "$REPO/docker-compose.yml" -f "$REPO/docker-compose.ci.yml" "$@"; }
envA() { env COMPOSE_PROJECT_NAME="$PA" COMPOSE_ENV_FILES="$T/envA" COMPOSE_FILE="docker-compose.yml:docker-compose.ci.yml" "$@"; }
envB() { env COMPOSE_PROJECT_NAME="$PB" COMPOSE_ENV_FILES="$T/envB" COMPOSE_FILE="docker-compose.yml:docker-compose.ci.yml" "$@"; }
cleanup() {
  if [ "$KEEP" = 1 ]; then echo "--keep: proyectos $PA y $PB, tmp=$T"; return; fi
  dcA down -v >/dev/null 2>&1 || true; dcB down -v >/dev/null 2>&1 || true
  docker run --rm -v "$T:/t" "$ALPINE" sh -c 'rm -rf /t/b1 /t/b2' >/dev/null 2>&1 || true
  rm -rf "$T"
}
trap cleanup EXIT

PASS=0; FAIL=0
ok()  { PASS=$((PASS+1)); echo "  [OK ] $*"; }
bad() { FAIL=$((FAIL+1)); echo "  [FALLA] $*"; }
check() { local d=$1; shift; if "$@" >/dev/null 2>&1; then ok "$d"; else bad "$d"; fi; }

sqlq() { # sqlq <proyecto> <archivo.sql>  -> filas sin cabecera
  local c="${1}-db-1"
  docker cp "$2" "$c:/tmp/q.sql" >/dev/null && docker exec -e SA="$SA" -e QDB="${SQLDB:-BuryP07}" "$c" bash -c '/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$SA" -C -d "$QDB" -I -h -1 -W -s"|" -b -i /tmp/q.sql' 2>&1 | tr -d '\r'
}
cat > "$T/snap.sql" <<'SQL'
SET NOCOUNT ON;
DECLARE @sql nvarchar(max) = N'';
SELECT @sql += N'SELECT N''' + t.name + N''' AS tabla, COUNT_BIG(*) AS filas, ISNULL(CHECKSUM_AGG(BINARY_CHECKSUM(*)),0) AS chk FROM ' + QUOTENAME(s.name) + N'.' + QUOTENAME(t.name) + N' UNION ALL '
FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id WHERE t.is_ms_shipped = 0;
SET @sql = LEFT(@sql, LEN(@sql) - 10) + N' ORDER BY tabla';
EXEC(@sql);
SQL
echo "SET NOCOUNT ON; DBCC CHECKCONSTRAINTS WITH ALL_CONSTRAINTS;" > "$T/fk.sql"
echo "SELECT CAST(DB_ID(N'BuryP07') AS varchar(10));" > "$T/dbid.sql"
uploads_hash() { docker run --rm -v "${1}_bury-uploads:/d:ro" "$ALPINE" sh -c 'cd /d && n=$(find . -type f | wc -l) && h=$(find . -type f -exec sha256sum {} + | sort | sha256sum | cut -d" " -f1) && echo "$n:$h"' | tr -d '\r'; }

echo "== DR con datos sinteticos conocidos: pila A=$PA, pila B=$PB, imagen $IMAGE =="
echo "-- A: cargar datos comerciales por la aplicacion (recorridos P-07)"
dcA up -d --wait --wait-timeout 420 db app || { echo "FALLO el arranque de la pila A"; exit 1; }
APP_PORT_A="$(grep ^APP_PORT "$T/envA" | cut -d= -f2)"
P07_BASE_URL="http://127.0.0.1:$APP_PORT_A" P07_DB_CONTAINER="${PA}-db-1" P07_SA_PASSWORD="$SA" P07_DB_NAME=BuryP07 P07_ADMIN_USER=admin_p07 \
  P07_ADMIN_PASS="$ADM" P07_REPORT="$T/resultado.json" P07_APP_CONTAINER="${PA}-app-1" P07_PROJECT="$PA" python3 "$REPO/scripts/qa/p07/p07_recorridos.py" > "$T/p07.log" 2>&1
RC=$?; tail -n 2 "$T/p07.log"
[ "$RC" = 0 ] && ok "recorridos P-07 en la pila A: $(grep -c '\[OK \]' "$T/p07.log") comprobaciones sin falla" || { bad "los recorridos de P-07 fallaron: no hay datos conocidos"; exit 1; }

echo "-- A: respaldo con la aplicacion detenida (base quieta) y huella"
dcA stop app >/dev/null 2>&1
envA bash scripts/backup/backup.sh all > "$T/backup.log" 2>&1; BRC=$?
[ "$BRC" = 0 ] && ok "backup.sh all (pila A) rc 0" || { bad "backup.sh all fallo (rc $BRC)"; tail -5 "$T/backup.log"; exit 1; }
check "la aplicacion de A sigue detenida tras el backup (la huella es la del respaldo)" bash -c "! docker ps --format '{{.Names}}' | grep -q '^${PA}-app-1$'"
sqlq "$PA" "$T/snap.sql" > "$T/snapA.txt"
sqlq "$PA" "$T/fk.sql" > "$T/fkA.txt"
UPA="$(uploads_hash "$PA")"
TBL=$(wc -l < "$T/snapA.txt")
echo "   huella A: $TBL tablas, uploads=$UPA"
for tb in Clientes Productos Proveedores OrdenesCompra Ventas Facturas MovimientosCaja Devoluciones NotasCredito Creditos Cuotas DocumentosCliente MovimientosStock; do
  n=$(grep -E "^${tb}\|" "$T/snapA.txt" | cut -d'|' -f2)
  if [ -n "$n" ] && [ "$n" -gt 0 ] 2>/dev/null; then ok "datos conocidos presentes en A: $tb = $n fila(s)"; else bad "A no tiene datos en $tb (la prueba seria vacia)"; fi
done
[ "${UPA%%:*}" -ge 3 ] 2>/dev/null && ok "documentos subidos en A: ${UPA%%:*} archivo(s) en uploads" || bad "A sin documentos en uploads ($UPA)"

echo "-- B: simular la llegada de los respaldos desde un extraible (root:root 600) en una instalacion limpia"
envB bash scripts/backup/prepare-dir.sh >/dev/null 2>&1 || { bad "prepare-dir.sh de B fallo"; exit 1; }
docker run --rm -e OU="$(id -u):$(id -g)" -v "$T/b1:/src:ro" -v "$T/b2:/dst" "$ALPINE" sh -c 'mkdir -p /dst/files && cp -r /src/sql/. /dst/sql/ && cp -r /src/files/. /dst/files/ && chown -R 0:0 /dst/sql && chmod -R go-rwx /dst/sql && chown "$OU" /dst/files && chmod 700 /dst/files' || { bad "no se pudo copiar a B"; exit 1; }
[ -n "$(ls "$T/b2/files"/set_*.txt 2>/dev/null)" ] || { bad "B no puede listar el manifiesto del par (permisos de files/)"; exit 1; }
SETF=$(ls "$T/b2/files"/set_*.txt | sort | tail -n1)
[ -n "$SETF" ] || { bad "sin manifiesto set_*.txt"; exit 1; }
# solo por el NOMBRE del manifiesto (set_<ts-tar>_<ts-full>.txt): el contenido puede ser root:root 600, como llega de un extraible
TARN=$(basename "$SETF" | sed -E 's/^set_([^_]+_[^_]+)_([^_]+_[^_]+)\.txt$/files_\1.tar.gz/')
FULLN=$(basename "$SETF" | sed -E 's/^set_([^_]+_[^_]+)_([^_]+_[^_]+)\.txt$/BuryP07_full_\2.bak/')
echo "   par: $TARN + $FULLN"
rtry() { envB bash scripts/backup/recover.sh "$@" > "$T/rec.log" 2>&1; echo $?; }
sin_efectos() { # B sin NINGUN contenedor del proyecto (ni db, ni app, ni migrate) y sin marca: la recuperacion no llego a tocar nada
  [ -z "$(docker ps -aq --filter "label=com.docker.compose.project=$PB")" ] && [ ! -e "$T/b2/.recovery-incomplete" ]
}
sin_app() { ! docker ps -a --format '{{.Names}}' | grep -q "^${PB}-\(app\|migrate\)-1$"; }

echo "-- B negativo 1: .bak alterado (sha256 no coincide) => no se cambia NADA"
docker run --rm -v "$T/b2:/b" "$ALPINE" sh -c "printf X | dd of=/b/sql/$FULLN bs=1 seek=4096 conv=notrunc 2>/dev/null"
RCN=$(rtry --apply); [ "$RCN" = 3 ] && ok "recover.sh se detiene con codigo 3 ante sha256 alterado" || bad "esperaba codigo 3, obtuvo $RCN"
check "no se creo ningun contenedor de B ni la marca (no se cambio nada)" sin_efectos || docker ps -a --filter "label=com.docker.compose.project=$PB" --format '   residuo: {{.Names}} {{.Status}}' 
docker run --rm -v "$T/b1:/s:ro" -v "$T/b2:/b" "$ALPINE" sh -c "cp /s/sql/$FULLN /b/sql/$FULLN && chown 0:0 /b/sql/$FULLN && chmod 600 /b/sql/$FULLN"

echo "-- B negativo 2: tar ausente => no se cambia NADA"
docker run --rm -v "$T/b2:/b" "$ALPINE" sh -c "mv /b/files/$TARN /b/files/$TARN.perdido"
RCN=$(rtry --apply); [ "$RCN" = 3 ] && ok "recover.sh se detiene con codigo 3 ante tar ausente" || bad "esperaba codigo 3, obtuvo $RCN"
check "no se creo ningun contenedor de B ni la marca (no se cambio nada)" sin_efectos || docker ps -a --filter "label=com.docker.compose.project=$PB" --format '   residuo: {{.Names}} {{.Status}}' 
docker run --rm -v "$T/b2:/b" "$ALPINE" sh -c "mv /b/files/$TARN.perdido /b/files/$TARN"

echo "-- B negativo 3: .bak corrupto con sha256 RECALCULADO (falla de SQL tras restaurar archivos)"
docker run --rm -v "$T/b2:/b" "$ALPINE" sh -c "cp /b/sql/$FULLN /b/$FULLN.bueno && head -c 65536 /dev/urandom > /b/sql/$FULLN && (cd /b/sql && sha256sum $FULLN > $FULLN.sha256)"
RCN=$(rtry --apply); [ "$RCN" != 0 ] && ok "recover.sh se detiene (codigo $RCN) ante un .bak ilegible para SQL" || bad "recover.sh devolvio 0 con un .bak corrupto"
check "NO se levantaron app ni migrate (no puede nacer una base vacia)" sin_app
check "queda la marca .recovery-incomplete" test -e "$T/b2/.recovery-incomplete"
DBID="$(SQLDB=master sqlq "$PB" "$T/dbid.sql")"
echo "$DBID" | grep -q 'NULL' && ok "la base BuryP07 NO existe en B tras el fallo" || bad "la base existe o no se pudo consultar tras un restore fallido: $(echo "$DBID" | head -2)"
PF=$(bash scripts/deploy/preflight.sh --image "$IMAGE" --env-file "$T/envB" --project "$PB" 2>&1 || true)
echo "$PF" | grep -q 'recuperacion INCOMPLETA' && ok "preflight.sh rechaza desplegar sobre una recuperacion incompleta" || bad "preflight.sh no detecto la marca"
docker run --rm -v "$T/b2:/b" "$ALPINE" sh -c "mv /b/$FULLN.bueno /b/sql/$FULLN && chown 0:0 /b/sql/$FULLN && chmod 600 /b/sql/$FULLN && cd /b/sql && sha256sum -c $FULLN.sha256 >/dev/null"
docker run --rm -v "$T/b1:/s:ro" -v "$T/b2:/b" "$ALPINE" sh -c "cp /s/sql/$FULLN.sha256 /b/sql/$FULLN.sha256 && chown 0:0 /b/sql/$FULLN.sha256"

echo "-- B positivo (fase 1): reintento con la copia buena (--force-files), SIN arrancar app"
RCP=$(rtry --apply --force-files --no-start); tail -n 3 "$T/rec.log" | cut -c1-200
[ "$RCP" = 0 ] && ok "recover.sh --no-start termino bien (rc 0): archivos + SQL restaurados y compuerta superada" || bad "recover.sh fallo en el reintento (rc $RCP)"
grep -q 'corrige a 10001' "$T/rec.log" && ok "restore-sql.sh corrigio el dueño/modo root:root 600 de los respaldos (SQL Server pudo leerlos)" || bad "no se vio la correccion de permisos"
sqlq "$PB" "$T/snap.sql" > "$T/snapB.txt"; sqlq "$PB" "$T/fk.sql" > "$T/fkB.txt"; UPB="$(uploads_hash "$PB")"
if diff -q "$T/snapA.txt" "$T/snapB.txt" >/dev/null; then ok "huella de TODAS las tablas ($TBL) identica entre A y B: registros, importes y relaciones recuperados"; else bad "la huella difiere:"; diff "$T/snapA.txt" "$T/snapB.txt" | head -10; fi
[ "$UPA" = "$UPB" ] && ok "documentos subidos identicos (uploads: $UPB)" || bad "uploads difiere: A=$UPA B=$UPB"
VA=$(grep -vc 'DBCC execution completed' "$T/fkA.txt"); VB=$(grep -vc 'DBCC execution completed' "$T/fkB.txt")
[ "$VA" = 0 ] && [ "$VB" = 0 ] && ok "DBCC CHECKCONSTRAINTS WITH ALL_CONSTRAINTS: 0 violaciones de FK/CHECK en el original y en la base recuperada" || bad "violaciones de FK/CHECK: A=$VA B=$VB"

echo "-- B positivo (fase 2): recuperacion COMPLETA (sin --no-start) sobre una pila B limpia"
dcB down -v >/dev/null 2>&1
docker run --rm -v "$T/b2:/b" "$ALPINE" sh -c 'rm -f /b/.recovery-incomplete'
RCP=$(rtry --apply); tail -n 2 "$T/rec.log" | cut -c1-200
[ "$RCP" = 0 ] && ok "recover.sh completo rc 0: stack sano y sin recreacion de la base" || bad "recover.sh completo fallo (rc $RCP)"
check "la marca .recovery-incomplete se elimino al terminar bien" bash -c "! test -e '$T/b2/.recovery-incomplete'"
APP_PORT_B="$(grep ^APP_PORT "$T/envB" | cut -d= -f2)"
check "la aplicacion restaurada responde (login 200)" bash -c "curl -fs -o /dev/null http://127.0.0.1:$APP_PORT_B/Identity/Account/Login"
sqlq "$PB" "$T/snap.sql" > "$T/snapB2.txt"
for tb in Clientes Ventas Facturas DocumentosCliente; do
  a=$(grep -E "^${tb}\|" "$T/snapA.txt" | cut -d'|' -f2-); b=$(grep -E "^${tb}\|" "$T/snapB2.txt" | cut -d'|' -f2-)
  [ "$a" = "$b" ] && ok "tras arrancar: $tb conserva filas+checksum ($a)" || bad "tras arrancar $tb difiere: A=$a B=$b"
done
echo; echo "== RESUMEN DR con datos conocidos: $PASS OK, $FAIL con falla =="
[ "$FAIL" = 0 ]
