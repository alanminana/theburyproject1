#!/usr/bin/env bash
# P-07/P-02: VALIDACION DE UNA BASE NUEVA e independiente (instalación limpia en Production, como el runbook de
# docs/p02-procedimiento-activacion.md §7), con datos sintéticos, en un stack DESCARTABLE (proyecto tbp-p02ens-<hex>).
# Verifica: todas las migraciones (incluida P3) y FISCAL_CRITERIO_V2_DESDE desde el primer arranque; base sin clientes/ventas/
# créditos/facturas/cajas ni cuentas de ensayos previos; humo de módulos; gracia de 12 días cargada por la pantalla real (versionada,
# nunca sembrada); V2 efectivo en la primera venta; backup + restore-test. El stack se destruye al salir. NO activa el neteo de IVA.
#   bash scripts/qa/p02/validacion-base-nueva.sh --image <imagen> [--out DIR] [--keep]
set -uo pipefail
export MSYS_NO_PATHCONV=1

IMG_B=""; OUT=""; KEEP=0
while [ $# -gt 0 ]; do
  case "$1" in
    --image) IMG_B="$2"; shift ;;
    --out) OUT="$2"; shift ;;
    --keep) KEEP=1 ;;
    *) echo "opcion desconocida: $1"; exit 2 ;;
  esac
  shift
done
[ -n "$IMG_B" ] || { echo "uso: --image <imagen> [--out DIR] [--keep]"; exit 2; }

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
for c in docker python3 openssl curl; do command -v "$c" >/dev/null 2>&1 || { echo "falta el comando: $c"; exit 2; }; done
[ -e "$REPO/.env" ] && { echo "RECHAZADO: $REPO/.env ya existe; este ensayo solo corre en una copia descartable del repo (git archive)."; exit 2; }
docker image inspect "$IMG_B" >/dev/null 2>&1 || { echo "falta la imagen B: $IMG_B"; exit 2; }

RAND="$(openssl rand -hex 3)"
P="tbp-p02ens-$RAND"
T="$(mktemp -d)"
OUT="${OUT:-$T/evidencia}"; mkdir -p "$OUT" "$T/backups"
LOG="$OUT/ensayo.log"
pw() { openssl rand -base64 24 | tr -dc 'A-Za-z0-9' | head -c 20; }
SUBNET=""
for i in $(seq 1 60); do
  cand="172.$((20 + RANDOM % 11)).$((RANDOM % 250)).0/24"
  net="${cand%.0/24}"
  if ! docker network ls -q | xargs -r docker network inspect -f '{{range .IPAM.Config}}{{.Subnet}} {{end}}' 2>/dev/null | grep -q "$net" \
     && ! ip route 2>/dev/null | grep -q "$net"; then SUBNET="$cand"; break; fi
done
[ -n "$SUBNET" ] || { echo "no se encontro subred libre"; exit 2; }
HTTPS_PORT="$(python3 -c "import socket;s=socket.socket();s.bind(('127.0.0.1',0));print(s.getsockname()[1]);s.close()")"
SAPW="Sa$(pw)#9a"; ADMPW="Adm$(pw)#3d"

write_env() {  # $1 = valor de FISCAL_CRITERIO_V2_DESDE
  cat > "$REPO/.env" <<EOF
MSSQL_SA_PASSWORD=$SAPW
MSSQL_DATABASE=BuryP02Ens
ERP_DB_USER=erp_app_ens
ERP_DB_PASSWORD=Ap$(pw)#7b
ERP_MIGRATION_USER=erp_mig_ens
ERP_MIGRATION_PASSWORD=Mg$(pw)#5c
MSSQL_PID=Developer
ADMIN_EMAIL=admin-ens@example.invalid
ADMIN_USERNAME=admin_ens
ADMIN_PASSWORD=$ADMPW
ERP_DOMAIN=ens-p02.example.invalid
ERP_HTTPS_PORT=$HTTPS_PORT
ERP_BIND_ADDRESS=127.0.0.1
BURY_NET_SUBNET=$SUBNET
APP_PORT=8080
MSSQL_HOST_PORT=1433
CSP_MODE=Off
FISCAL_CRITERIO_V2_DESDE=$1
AUTOMAPPER_LICENSE_MODE=community
BACKUP_DIR=$T/backups
BACKUP_RETENTION_DAYS=14
BACKUP_RETENTION_MIN_KEEP=3
BACKUP_REQUIRE_OFFSITE=false
BACKUP_INCLUDE_CADDY=true
BACKUP_CA_KEY=include
BACKUP_STOP_APP=true
EOF
  chmod 600 "$REPO/.env"
}

PASS=0; FAIL=0
say() { echo "$*" | tee -a "$LOG"; }
ok()   { PASS=$((PASS+1)); say "  [OK ] $*"; }
bad()  { FAIL=$((FAIL+1)); say "  [FALLA] $*"; }
chk()  { if eval "$2"; then ok "$1"; else bad "$1  -> $3"; fi; }
sec()  { say; say "== $* =="; }
dcp()  { docker compose --progress quiet -p "$P" --env-file "$REPO/.env" -f "$REPO/docker-compose.yml" "$@"; }
sqlq() { docker exec -i "$P-db-1" /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$SAPW" -d BuryP02Ens -h -1 -W -s'|' -Q "SET NOCOUNT ON; $1" 2>&1; }
sql1() { sqlq "$1" | head -n1; }
verif() {  # $1 = instante UTC de puesta en marcha (o vacío)
  local f="$T/verif.sql"
  if [ -n "$1" ]; then sed "s/DECLARE @PuestaEnMarcha datetime2 = NULL;/DECLARE @PuestaEnMarcha datetime2 = '$1';/" "$REPO/scripts/sql/p02-verificar-instalacion.sql" > "$f"
  else cp "$REPO/scripts/sql/p02-verificar-instalacion.sql" "$f"; fi
  docker cp "$f" "$P-db-1:/tmp/verif.sql" >/dev/null
  docker exec -i "$P-db-1" /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$SAPW" -d BuryP02Ens -W -s'|' -I -b -i /tmp/verif.sql 2>&1
}
APP_BASE() { echo "http://$(docker inspect -f '{{range .NetworkSettings.Networks}}{{.IPAddress}}{{end}}' "$P-app-1"):8080"; }
driver() {
  ENS_PROJECT="$P" ENS_BASE="$(APP_BASE)" ENS_DB_CONTAINER="$P-db-1" ENS_SA_PASSWORD="$SAPW" ENS_DB_NAME=BuryP02Ens \
  ENS_ADMIN_USER=admin_ens ENS_ADMIN_PASS="$ADMPW" python3 "$REPO/scripts/qa/p02/ensayo_venta.py" "$@"
}
deploy() { bash "$REPO/scripts/deploy/deploy.sh" --env-file "$REPO/.env" --project "$P" "$@"; }
cleanup() {
  if [ "$KEEP" = 1 ]; then say "Pila conservada: proyecto $P (destruir: docker compose -p $P down -v). Evidencia: $OUT"; return; fi
  dcp down -v >/dev/null 2>&1 || true
  LEFT="$(docker ps -aq --filter "label=com.docker.compose.project=$P" | wc -l)+$(docker volume ls -q --filter "label=com.docker.compose.project=$P" | wc -l)+$(docker network ls -q --filter "label=com.docker.compose.project=$P" | wc -l)"
  if [ "$LEFT" = "0+0+0" ]; then say "limpieza verificada: 0 contenedores, volúmenes y redes del proyecto $P"; else say "[FALLA] quedaron recursos del proyecto $P (contenedores+volúmenes+redes = $LEFT); .env conservado para destruirlos a mano"; EXIT_LEFT=1; fi
  [ "${EXIT_LEFT:-0}" = 1 ] || rm -f "$REPO/.env"
  # Los backups de SQL Server pertenecen al usuario del contenedor (uid 10001): se borran con un contenedor descartable.
  rm -rf "$T/backups" "$T/verif.sql" 2>/dev/null || docker run --rm -v "$T:/t" alpine sh -c 'rm -rf /t/backups /t/verif.sql' >/dev/null 2>&1 || true
  rmdir "$T" 2>/dev/null || true
}
trap cleanup EXIT

say "VALIDACION BASE NUEVA  proyecto=$P  imagen=$IMG_B  subred=$SUBNET  https=127.0.0.1:$HTTPS_PORT  fecha=$(date -u +%FT%TZ)"

sec "1. Instalación nueva en Production: V2 definida desde el primer arranque"
ACTIVACION="$(date -u +%FT%TZ)"
write_env "$ACTIVACION"
bash "$REPO/scripts/deploy/preflight.sh" --image "$IMG_B" --env-file "$REPO/.env" --project "$P" >>"$LOG" 2>&1; RC=$?
chk "preflight con FISCAL_CRITERIO_V2_DESDE válida termina 0" "[ $RC -eq 0 ]" "exit $RC (ver $LOG)"
deploy --image "$IMG_B" --backup skip >>"$LOG" 2>&1; RC=$?
chk "deploy.sh (db -> migrate -> app -> health -> smoke) termina 0" "[ $RC -eq 0 ]" "exit $RC (ver $LOG)"
[ $RC -eq 0 ] || { say "no se puede continuar sin la instalación"; exit 1; }

sec "2. La base es nueva: migraciones completas y sin datos de ningún ensayo anterior"
MIG_REPO="$(ls "$REPO"/Migrations | grep -E '^[0-9]{14}_[^.]*\.cs$' | wc -l | tr -d ' ')"
MIG_DB="$(sql1 'select count(*) from __EFMigrationsHistory')"
chk "todas las migraciones del repo aplicadas ($MIG_REPO) y ninguna de más" "[ \"$MIG_DB\" = \"$MIG_REPO\" ]" "repo=$MIG_REPO base=$MIG_DB"
V="$(verif "")"; RCV=$?; echo "$V" >>"$LOG"
chk "la verificación SQL se ejecutó (exit 0) y devolvió controles OK" "[ $RCV -eq 0 ] && echo \"\$V\" | grep -q '|OK'" "exit $RCV"
chk "P3 aplicada (8 columnas + índice, esquema coherente)" "echo \"\$V\" | grep -q 'esquema coherente.*|aplicada|'" "$(echo "$V" | grep -i 'P3')"
chk "verificación de instalación: ningún control en FALLA" "! echo \"\$V\" | grep -q '|FALLA'" "$(echo "$V" | grep FALLA)"
for T_ in Clientes Ventas Creditos Facturas Cajas AperturasCaja MovimientosCaja Productos Proveedores OrdenesCompra Cotizaciones Cuotas Cheques; do
  N="$(sql1 "select count(*) from $T_")"
  chk "tabla $T_ vacía" "[ \"$N\" = 0 ]" "filas=$N"
done
chk "exactamente 1 usuario (el administrador inicial) y ningún usuario importado" "[ \"\$(sql1 \"select cast(count(*) as varchar(5))+'|'+cast(sum(case when UserName='admin_ens' then 1 else 0 end) as varchar(5)) from AspNetUsers\")\" = '1|1' ]" "usuarios inesperados"
chk "ninguna cuenta de ensayo previo (p03/p05/tbp.local)" "[ \"\$(sql1 \"select count(*) from AspNetUsers where UserName like 'p0%' or Email like '%tbp.local'\")\" = 0 ]" "restos de otro ensayo"
chk "ConfiguracionesPunitorio vacía: la gracia NO está sembrada" "[ \"\$(sql1 'select count(*) from ConfiguracionesPunitorio')\" = 0 ]" "hay una versión sembrada"

sec "3. Arranque en V2 y humo inicial de los módulos principales"
chk "log de arranque informa V2 en este proceso" "docker logs \"$P-app-1\" 2>&1 | grep -q 'FISCAL.*= V2'" "falta [FISCAL] ... V2"
M="$(driver modulos 2>>"$LOG")"; say "  $M"
chk "módulos principales abren con el administrador inicial" "echo \"\$M\" | grep -q 'MODULOS_MALOS=-'" "$M"

sec "4. Gracia real de 12 días por versionado (sin valores en código)"
G="$(driver gracia 2>>"$LOG")"; say "$(echo "$G" | sed 's/^/  /')"
chk "una versión de punitorios con DiasGracia=12 creada por la pantalla" "echo \"\$G\" | grep -qF \"VERSIONES=1 FILA=['12'\"" "$G"
chk "vista previa del calculador real: día 12 = 0 y día 13 > 0" "echo \"\$G\" | grep -qE 'PREVIEW_IMPORTES=\[0\.0, [0-9.]*[1-9]'" "$G"

sec "5. V2 desde la primera venta (redondeo comercial) y facturación"
driver preparar >>"$LOG" 2>&1; chk "cliente, producto con stock y caja abierta creados" "[ $? -eq 0 ]" "ver $LOG"
S1="$(driver vender 2>>"$LOG" | sed -n 's/^VENTA_ID=//p')"
chk "primera venta creada y facturada" "[ -n \"$S1\" ]" "ver $LOG"
chk "versión fiscal 2" "[ \"\$(sql1 \"select CriterioFiscalVersion from Ventas where Id=$S1\")\" = 2 ]" "no es V2"
TOT="$(sql1 "select cast(Total as varchar(20)) from Ventas where Id=$S1")"
chk "V2 redondea comercialmente: total 1000,51 -> 1001.00" "[ \"$TOT\" = 1001.00 ]" "total=$TOT"
V="$(verif "$(date -u -d "$ACTIVACION" '+%Y-%m-%dT%H:%M:%S')")"; RCV=$?; echo "$V" >>"$LOG"
chk "la verificación posterior se ejecutó (exit 0) y devolvió controles OK" "[ $RCV -eq 0 ] && echo \"\$V\" | grep -q '|OK'" "exit $RCV"
chk "verificación posterior a la puesta en marcha: ningún control en FALLA" "! echo \"\$V\" | grep -q '|FALLA'" "$(echo "$V" | grep FALLA)"
chk "ninguna venta quedó en versión 1" "[ \"\$(sql1 'select count(*) from Ventas where CriterioFiscalVersion=1')\" = 0 ]" "hay ventas V1"

sec "6. Backup recuperable"
COMPOSE_PROJECT_NAME="$P" bash "$REPO/scripts/backup/backup.sh" all >>"$LOG" 2>&1; RC=$?
chk "backup.sh all termina 0" "[ $RC -eq 0 ]" "exit $RC (ver $LOG)"
COMPOSE_PROJECT_NAME="$P" bash "$REPO/scripts/backup/restore-test.sh" >>"$LOG" 2>&1; RC=$?
chk "restore-test.sh restaura y valida (exit 0)" "[ $RC -eq 0 ]" "exit $RC (ver $LOG)"

sec "RESULTADO"
say "OK=$PASS  FALLA=$FAIL   proyecto=$P   evidencia=$OUT   (el stack y sus datos sintéticos se destruyen al salir)"
cp "$LOG" "$OUT/resultado-$P.log" 2>/dev/null || true
[ "$FAIL" -eq 0 ]
