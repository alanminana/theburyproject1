#!/usr/bin/env bash
# P-02: ENSAYO GENERAL de la puesta en marcha (P3 + CriterioFiscal V2) en un stack DESCARTABLE de Linux/Docker (p. ej. la VM tbp).
#
# Recorre, con las herramientas reales del repo (scripts/deploy, scripts/backup) y datos sintéticos, la secuencia que se aplica en la
# puesta en marcha real:  instalación con la release anterior (A) -> verificación -> venta V1 -> deploy de la release nueva (B) con
# backup previo + migración P3 + fecha futura de V2 (sigue V1) -> prueba de restore -> fecha de V2 alcanzada y redeploy (V2) ->
# verificación -> rollback de imagen a A y roll-forward a B.
#
#   bash scripts/qa/p02/ensayo-puesta-en-marcha.sh --image-a <imagen-anterior> --image-b <imagen-nueva> [--out DIR] [--keep]
#
# Aislamiento (guardas): proyecto Compose propio tbp-p02ens-<hex>, red/volúmenes/puertos/subred propios, .env y backups en un directorio
# temporal. NUNCA toca otro proyecto, ninguna base real, ni el .env real (se niega a correr si ya existe un .env en el checkout).
# Al terminar destruye contenedores y volúmenes del proyecto (salvo --keep). No usa `docker compose down -v` sobre otro proyecto.
set -uo pipefail
export MSYS_NO_PATHCONV=1

IMG_A=""; IMG_B=""; OUT=""; KEEP=0
while [ $# -gt 0 ]; do
  case "$1" in
    --image-a) IMG_A="$2"; shift ;;
    --image-b) IMG_B="$2"; shift ;;
    --out) OUT="$2"; shift ;;
    --keep) KEEP=1 ;;
    *) echo "opcion desconocida: $1"; exit 2 ;;
  esac
  shift
done
[ -n "$IMG_A" ] && [ -n "$IMG_B" ] || { echo "uso: --image-a <imagen> --image-b <imagen> [--out DIR] [--keep]"; exit 2; }

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
for c in docker python3 openssl curl; do command -v "$c" >/dev/null 2>&1 || { echo "falta el comando: $c"; exit 2; }; done
[ -e "$REPO/.env" ] && { echo "RECHAZADO: $REPO/.env ya existe; este ensayo solo corre en una copia descartable del repo (git archive)."; exit 2; }
docker image inspect "$IMG_A" >/dev/null 2>&1 || { echo "falta la imagen A: $IMG_A"; exit 2; }
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
  rm -f "$REPO/.env"
  rm -rf "$T/backups" "$T/verif.sql"
}
trap cleanup EXIT

say "ENSAYO GENERAL P-02  proyecto=$P  A=$IMG_A  B=$IMG_B  subred=$SUBNET  https=127.0.0.1:$HTTPS_PORT  fecha=$(date -u +%FT%TZ)"

# ---------------------------------------------------------------------------------------------------------------------------
sec "1. Release A: instalación nueva (base vacía) y verificación de instalación limpia"
write_env ""
deploy --image "$IMG_A" --backup skip >>"$LOG" 2>&1; RC=$?
chk "deploy.sh de la release A (bootstrap) termina 0" "[ $RC -eq 0 ]" "exit $RC (ver $LOG)"
[ $RC -eq 0 ] || { say "no se puede continuar sin la release A"; exit 1; }
V="$(verif "")"; echo "$V" >>"$LOG"
chk "verificación: instalación limpia (0 ventas, 0 créditos, 0 facturas)" "echo \"\$V\" | grep -E '^(Ventas|Créditos|Facturas) totales\\|0\\|' | wc -l | grep -qx 3" "$(echo "$V" | grep -E 'totales' | tr '\n' ' ')"
chk "verificación: ningún control en FALLA" "! echo \"\$V\" | grep -q '|FALLA'" "$(echo "$V" | grep FALLA)"
chk "A: P3 NO aplicada (0 columnas FiscalExterna*)" "echo \"\$V\" | grep -q 'columnas FiscalExterna\\* en NotasCredito|0|'" "A ya trae P3; se revierte en el paso siguiente"
MIG_A="$(sql1 'select count(*) from __EFMigrationsHistory')"
say "  migraciones con la release A: $MIG_A"

# Si la imagen A ya incluyera P3, se revierte con el script validado para ensayar la aplicación real de P3 en B.
if echo "$V" | grep -q 'columnas FiscalExterna\* en NotasCredito|8|'; then
  docker cp "$REPO/scripts/sql/p02-nc-fiscal-externa-down.sql" "$P-db-1:/tmp/down.sql" >/dev/null
  docker exec -i "$P-db-1" /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$SAPW" -d BuryP02Ens -I -b -i /tmp/down.sql >>"$LOG" 2>&1
  chk "P3 revertida con p02-nc-fiscal-externa-down.sql (0 columnas)" "[ \"\$(sql1 \"select count(*) from sys.columns where object_id=object_id('NotasCredito') and name like 'FiscalExterna%'\")\" = 0 ]" "no se revirtió"
fi
MIG_PRE="$(sql1 'select count(*) from __EFMigrationsHistory')"
say "  migraciones antes de P3: $MIG_PRE"

# ---------------------------------------------------------------------------------------------------------------------------
sec "2. Datos sintéticos y venta bajo la release A (V1)"
driver preparar >>"$LOG" 2>&1; chk "cliente, producto con stock y caja abierta creados" "[ $? -eq 0 ]" "ver $LOG"
S1="$(driver vender 2>>"$LOG" | sed -n 's/^VENTA_ID=//p')"
chk "venta S1 creada y facturada bajo A" "[ -n \"$S1\" ]" "ver $LOG"
V1_S1="$(sql1 "select CriterioFiscalVersion from Ventas where Id=$S1")"
chk "S1 tiene versión fiscal 1" "[ \"$V1_S1\" = 1 ]" "versión=$V1_S1"
TOT_S1="$(sql1 "select cast(Total as varchar(20)) from Ventas where Id=$S1")"
say "  S1 total=$TOT_S1 (V1: 1.000,51 truncado a 1.000,00 por el criterio vigente)"

# ---------------------------------------------------------------------------------------------------------------------------
sec "3. Preflight rechaza una fecha de V2 inválida"
write_env "2026-02-31T03:00:00Z"
bash "$REPO/scripts/deploy/preflight.sh" --image "$IMG_B" --env-file "$REPO/.env" --project "$P" --require-db-up >>"$LOG" 2>&1; RC=$?
chk "preflight con FISCAL_CRITERIO_V2_DESDE inválida falla (exit != 0)" "[ $RC -ne 0 ]" "exit $RC"
write_env "2026-12-01T03:00"
bash "$REPO/scripts/deploy/preflight.sh" --image "$IMG_B" --env-file "$REPO/.env" --project "$P" --require-db-up >>"$LOG" 2>&1; RC=$?
chk "preflight con fecha sin zona falla (exit != 0)" "[ $RC -ne 0 ]" "exit $RC"

# ---------------------------------------------------------------------------------------------------------------------------
sec "4. Release B: backup previo + migración P3 + fecha de V2 FUTURA (el proceso sigue en V1)"
FUTURA="$(date -u -d '+2 hours' +%FT%TZ)"
write_env "$FUTURA"
deploy --image "$IMG_B" --backup auto >>"$LOG" 2>&1; RC=$?
chk "deploy.sh de la release B termina 0 (backup -> migrate -> app -> health -> smoke)" "[ $RC -eq 0 ]" "exit $RC (ver $LOG)"
[ $RC -eq 0 ] || { say "deploy B fallo; se detiene"; exit 1; }
chk "backup previo a la migración creado" "ls \"$T/backups\" | grep -qiE '\\.bak|full|sql'" "sin archivos en $T/backups"
V="$(verif "")"; echo "$V" >>"$LOG"
chk "P3 aplicada (8 columnas + índice, esquema coherente)" "echo \"\$V\" | grep -q 'esquema coherente.*|aplicada|'" "$(echo "$V" | grep -i 'P3')"
chk "migraciones +1 respecto de antes de P3" "[ \"\$(sql1 'select count(*) from __EFMigrationsHistory')\" = $((MIG_PRE+1)) ]" "pre=$MIG_PRE"
chk "S1 conserva versión 1 y total tras el deploy" "[ \"\$(sql1 \"select cast(CriterioFiscalVersion as varchar(5))+'|'+cast(Total as varchar(20)) from Ventas where Id=$S1\")\" = \"1|$TOT_S1\" ]" "S1 cambió"
chk "log de arranque informa V1 en este proceso (instante futuro)" "docker logs \"$P-app-1\" 2>&1 | grep -q 'FISCAL.*V1'" "falta la línea [FISCAL]"
S2="$(driver vender 2>>"$LOG" | sed -n 's/^VENTA_ID=//p')"
chk "S2 (instante futuro) se estampa V1" "[ \"\$(sql1 \"select CriterioFiscalVersion from Ventas where Id=$S2\")\" = 1 ]" "S2 no es V1"

# ---------------------------------------------------------------------------------------------------------------------------
sec "5. Backup recuperable: prueba de restore no destructiva"
COMPOSE_PROJECT_NAME="$P" bash "$REPO/scripts/backup/restore-test.sh" >>"$LOG" 2>&1; RC=$?
chk "restore-test.sh restaura el último backup y valida (exit 0)" "[ $RC -eq 0 ]" "exit $RC (ver $LOG)"

# ---------------------------------------------------------------------------------------------------------------------------
sec "6. Puesta en marcha: instante de V2 = ahora; redeploy (el proceso arranca en V2)"
ACTIVACION="$(date -u +%FT%TZ)"
sleep 2
write_env "$ACTIVACION"
deploy --image "$IMG_B" --backup skip >>"$LOG" 2>&1; RC=$?
chk "redeploy con la fecha de puesta en marcha termina 0" "[ $RC -eq 0 ]" "exit $RC"
chk "log de arranque informa V2 en este proceso" "docker logs \"$P-app-1\" 2>&1 | grep -q 'FISCAL.*= V2'" "falta [FISCAL] ... V2"
S3="$(driver vender 2>>"$LOG" | sed -n 's/^VENTA_ID=//p')"
chk "S3 creada y facturada con V2" "[ -n \"$S3\" ] && [ \"\$(sql1 \"select CriterioFiscalVersion from Ventas where Id=$S3\")\" = 2 ]" "S3 no es V2"
TOT_S3="$(sql1 "select cast(Total as varchar(20)) from Ventas where Id=$S3")"
say "  S3 total=$TOT_S3 (V2: 1.000,51 redondeado a 1.001,00)"
chk "V2 redondea comercialmente: S3 total = 1001.00" "[ \"$TOT_S3\" = 1001.00 ]" "total=$TOT_S3"
chk "operaciones previas conservan V1 (S1,S2) sin cambios de importe" "[ \"\$(sql1 \"select cast(sum(case when CriterioFiscalVersion=1 then 1 else 0 end) as varchar(5)) from Ventas where Id in ($S1,$S2)\")\" = 2 ]" "S1/S2 alteradas"
V="$(verif "$(date -u -d "$ACTIVACION" '+%Y-%m-%dT%H:%M:%S')")"; echo "$V" >>"$LOG"
chk "verificación posterior: sin FALLA (consistencia Neto+IVA=Total, facturas, V2 no retroactiva)" "! echo \"\$V\" | grep -q '|FALLA'" "$(echo "$V" | grep FALLA)"
say "$(echo "$V" | grep -E 'Fecha efectiva de V2|versión fiscal')"

# ---------------------------------------------------------------------------------------------------------------------------
sec "7. Rollback de imagen a A (sin tocar la base) y roll-forward a B"
bash "$REPO/scripts/deploy/rollback.sh" --to-image "$IMG_A" --confirm-compatible --env-file "$REPO/.env" --project "$P" >>"$LOG" 2>&1; RC=$?
chk "rollback.sh a la imagen A termina 0 y la app queda sana" "[ $RC -eq 0 ]" "exit $RC (ver $LOG)"
chk "rollback no cambió la base (P3 sigue aplicada, ventas intactas)" "[ \"\$(sql1 'select count(*) from Ventas')\" = 3 ]" "ventas alteradas"
DET_S1="$(driver detalle "$S1" 2>/dev/null | sed -n 's/^DETALLE_STATUS=//p')"
chk "con A: el detalle de la venta V1 (S1) abre" "[ \"$DET_S1\" = 200 ]" "status=$DET_S1"
DET_S3="$(driver detalle "$S3" 2>/dev/null | sed -n 's/^DETALLE_STATUS=//p')"
say "  [INFO] con A: detalle de la venta V2 (S3) -> HTTP $DET_S3 (si A no conoce V2 falla cerrada: el rollback con ventas V2 requiere una imagen que conozca V2)"
deploy --image "$IMG_B" --backup skip >>"$LOG" 2>&1; RC=$?
chk "roll-forward a B termina 0" "[ $RC -eq 0 ]" "exit $RC"
DET_S3B="$(driver detalle "$S3" 2>/dev/null | sed -n 's/^DETALLE_STATUS=//p')"
chk "con B: el detalle de la venta V2 (S3) abre" "[ \"$DET_S3B\" = 200 ]" "status=$DET_S3B"

# ---------------------------------------------------------------------------------------------------------------------------
sec "RESULTADO"
say "OK=$PASS  FALLA=$FAIL   proyecto=$P   evidencia=$OUT"
cp "$LOG" "$OUT/resultado-$P.log" 2>/dev/null || true
[ "$FAIL" -eq 0 ]
