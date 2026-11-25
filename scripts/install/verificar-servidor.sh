#!/usr/bin/env bash
# VERIFICADOR DE UNA INSTALACION DEL ERP (provisional o definitiva). SOLO LECTURA: no cambia nada, nunca imprime secretos.
# Agrupa en una sola corrida lo que hoy se comprueba a mano: salud, migraciones, aislamiento de puertos, HTTPS con la CA propia,
# respaldos y su programacion, custodia, verificadores SQL, firewall y arranque automatico. Se repite tal cual en el servidor definitivo.
#
#   scripts/install/verificar-servidor.sh [--project theburyproject-prov] [--env-file .env] [--ca caddy-root-ca.crt]
#                                         [--lan-cidr 192.168.50.0/24] [--fw-chain BURY-ERP-PROV] [--cierre] [--sudo-cmd "sudo -n"]
#
#   --project    proyecto Compose de ESTA instalacion (default: COMPOSE_PROJECT_NAME o el de install.sh)
#   --ca         CA publica de Caddy (default: ./caddy-root-ca.crt, la deja install.sh)
#   --fw-chain   cadena de DOCKER-USER de esta instalacion (default BURY-ERP; una segunda instalacion usa otra)
#   --cierre     exige ademas p07-verificar-cuentas.sql @Cierre=1 (>= 2 SuperAdmin reales, 0 cuentas sinteticas): SOLO para produccion
#   --sudo-cmd   como obtener root para leer ufw/iptables (default "sudo -n"; si no hay permiso esas comprobaciones se marcan SKIP)
#
# Salida: PASS / FAIL / WARN / SKIP / INFO por comprobacion y un resumen. Exit 0 = ningun FAIL; 1 = hay FAIL; 2 = no se pudo empezar.
# WARN = condicion conocida y aceptada o pendiente de la persona responsable (p. ej. sin copia externa); no invalida la instalacion tecnica.
set -uo pipefail
REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
PROJECT="" ENV_FILE="$REPO_DIR/.env" CA="$REPO_DIR/caddy-root-ca.crt" LAN_CIDR="" FW_CHAIN="BURY-ERP" CIERRE=0 SUDO="sudo -n"
while [[ $# -gt 0 ]]; do
  case "$1" in
    --project) PROJECT="${2:-}"; shift 2 ;; --env-file) ENV_FILE="${2:-}"; shift 2 ;; --ca) CA="${2:-}"; shift 2 ;;
    --lan-cidr) LAN_CIDR="${2:-}"; shift 2 ;; --fw-chain) FW_CHAIN="${2:-}"; shift 2 ;; --cierre) CIERRE=1; shift ;;
    --sudo-cmd) SUDO="${2:-}"; shift 2 ;; -h|--help) sed -n '2,15p' "$0"; exit 0 ;;
    *) echo "argumento desconocido: $1" >&2; exit 2 ;;
  esac
done
[[ -f "$ENV_FILE" ]] || { echo "no existe $ENV_FILE" >&2; exit 2; }
command -v docker >/dev/null 2>&1 || { echo "falta docker" >&2; exit 2; }
envv() { grep -E "^$1=" "$ENV_FILE" | tail -1 | cut -d= -f2-; }          # lee una variable del .env (sin imprimirla salvo que se pida)
PROJECT="${PROJECT:-$(envv COMPOSE_PROJECT_NAME)}"; PROJECT="${PROJECT:-theburyproject}"
DOMAIN="$(envv ERP_DOMAIN)"; PORT="$(envv ERP_HTTPS_PORT)"; PORT="${PORT:-443}"; BDIR="$(envv BACKUP_DIR)"; BDIR="${BDIR:-/srv/bury-backups}"
dc() { docker compose --progress quiet -p "$PROJECT" --env-file "$ENV_FILE" -f "$REPO_DIR/docker-compose.yml" "$@"; }

NP=0 NF=0 NW=0 NS=0
pass() { NP=$((NP+1)); echo "PASS  $1"; }
fail() { NF=$((NF+1)); echo "FAIL  $1"; }
warn() { NW=$((NW+1)); echo "WARN  $1"; }
skip() { NS=$((NS+1)); echo "SKIP  $1"; }
info() { echo "INFO  $1"; }
check() { local n=$1; shift; if "$@" >/dev/null 2>&1; then pass "$n"; else fail "$n"; fi; }

echo "== verificar-servidor: proyecto=$PROJECT dominio=$DOMAIN https=$PORT respaldos=$BDIR =="
# --- 1. contenedores y salud
for s in db app caddy; do
  st="$(docker inspect -f '{{.State.Status}} {{if .State.Health}}{{.State.Health.Status}}{{end}}' "${PROJECT}-${s}-1" 2>/dev/null)"
  case "$s:$st" in db:"running healthy"|app:"running healthy") pass "V01 $s en ejecucion y healthy" ;; caddy:running*) pass "V01 caddy en ejecucion" ;; *) fail "V01 $s: estado '${st:-inexistente}'" ;; esac
done
# --- 2. HTTPS con la CA propia (y rechazo sin ella)
if [[ -f "$CA" ]]; then
  code="$(curl -s -o /dev/null -w '%{http_code}' --max-time 15 --cacert "$CA" --resolve "$DOMAIN:$PORT:127.0.0.1" "https://$DOMAIN:$PORT/health/ready" 2>/dev/null)"
  [[ "$code" = 200 ]] && pass "V02 HTTPS /health/ready = 200 validando con la CA propia" || fail "V02 HTTPS /health/ready = ${code:-sin respuesta} con la CA propia"
  sc="$(curl -s -o /dev/null -w '%{http_code}' --max-time 15 --resolve "$DOMAIN:$PORT:127.0.0.1" "https://$DOMAIN:$PORT/health/ready" 2>/dev/null)"
  [[ "$sc" != 200 ]] && pass "V03 sin la CA el certificado se rechaza (no es una CA publica)" || fail "V03 el sistema acepta el certificado sin la CA propia"
  lg="$(curl -s -o /dev/null -w '%{http_code}' --max-time 15 --cacert "$CA" --resolve "$DOMAIN:$PORT:127.0.0.1" "https://$DOMAIN:$PORT/Identity/Account/Login" 2>/dev/null)"
  [[ "$lg" = 200 ]] && pass "V04 pantalla de login por HTTPS = 200" || fail "V04 login por HTTPS = ${lg:-sin respuesta}"
else skip "V02-V04 no existe la CA ($CA): ejecutar install.sh o pasar --ca"; fi
# --- 3. migraciones y verificadores SQL
SA="$(envv MSSQL_SA_PASSWORD)"; DBN="$(envv MSSQL_DATABASE)"; DBN="${DBN:-TheBuryProjectDb}"
sqlq() { docker exec "${PROJECT}-db-1" /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$SA" -C -d "$DBN" -h -1 -W -Q "SET NOCOUNT ON; $1" 2>/dev/null | tr -d '\r' | head -1; }
mig="$(sqlq 'SELECT COUNT(*) FROM __EFMigrationsHistory')"; esperadas="$(ls "$REPO_DIR"/Migrations/20*.cs 2>/dev/null | grep -vc Designer)"
[[ -n "$mig" && "$mig" = "$esperadas" ]] && pass "V05 migraciones aplicadas = $mig (las del repositorio)" || fail "V05 migraciones aplicadas '${mig:-?}' vs repositorio $esperadas"
sqlfile() {  # sqlfile <archivo.sql> [cierre]: con "cierre" fija @Cierre = 1 en una copia temporal (el original no se modifica)
  local src="$REPO_DIR/scripts/sql/$1" tmp; tmp="$(mktemp)"
  if [[ "${2:-}" = cierre ]]; then sed 's/DECLARE @Cierre bit = 0/DECLARE @Cierre bit = 1/' "$src" >"$tmp"; else cp "$src" "$tmp"; fi
  chmod 644 "$tmp"   # mktemp crea 0600: el usuario del contenedor (mssql) debe poder leerlo
  docker cp "$tmp" "${PROJECT}-db-1:/tmp/v-$1" >/dev/null 2>&1; rm -f "$tmp"
  docker exec "${PROJECT}-db-1" /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$SA" -C -d "$DBN" -I -b -W -i "/tmp/v-$1" >/dev/null 2>&1
}
sqlfile p02-verificar-instalacion.sql && pass "V06 p02-verificar-instalacion.sql (exit 0)" || fail "V06 p02-verificar-instalacion.sql (exit != 0)"
sqlfile p07-verificar-credito-cerrado.sql && pass "V07 p07-verificar-credito-cerrado.sql (exit 0: credito cerrado)" || fail "V07 p07-verificar-credito-cerrado.sql (exit != 0)"
if [[ $CIERRE = 1 ]]; then
  sqlfile p07-verificar-cuentas.sql cierre && pass "V08 p07-verificar-cuentas.sql (>= 2 SuperAdmin reales, sin cuentas sinteticas)" || fail "V08 p07-verificar-cuentas.sql @Cierre=1 (exit != 0)"
else info "V08 p07-verificar-cuentas.sql no se exige (sin --cierre): solo aplica a produccion"; fi
fv="$(envv FISCAL_CRITERIO_V2_DESDE)"; [[ -n "$fv" ]] && info "V09 FISCAL_CRITERIO_V2_DESDE=$fv" || warn "V09 FISCAL_CRITERIO_V2_DESDE sin definir (criterio V1): el instante real lo decide el propietario (B9)"
# --- 4. aislamiento de puertos publicados
pub="$(docker ps --filter "name=${PROJECT}-" --format '{{.Names}} {{.Ports}}' 2>/dev/null)"
if echo "$pub" | grep -Eq "(:1433->|:8080->|:2019->)"; then fail "V10 un puerto interno esta publicado: $(echo "$pub" | grep -E ':1433->|:8080->|:2019->' | cut -c1-80)"; else pass "V10 db (1433), app (8080) y la API de Caddy (2019) no estan publicados"; fi
echo "$pub" | grep -q ":${PORT}->443" && pass "V11 solo se publica el HTTPS de Caddy (:$PORT)" || fail "V11 Caddy no publica :$PORT"
perm="$(stat -c %a "$ENV_FILE" 2>/dev/null)"; [[ "$perm" = 600 ]] && pass "V12 .env con permisos 600" || fail "V12 .env con permisos $perm (debe ser 600)"
# --- 5. firewall / acceso (requiere root)
if $SUDO true >/dev/null 2>&1; then
  $SUDO iptables -S "$FW_CHAIN" >/dev/null 2>&1 && pass "V13 existe la cadena $FW_CHAIN (limita :$PORT a la LAN)" || fail "V13 no existe la cadena $FW_CHAIN en DOCKER-USER"
  $SUDO iptables -S DOCKER-USER 2>/dev/null | grep -q -- "-j $FW_CHAIN" && pass "V14 $FW_CHAIN enganchada en DOCKER-USER" || fail "V14 $FW_CHAIN no esta enganchada en DOCKER-USER"
  if [[ -n "$LAN_CIDR" ]]; then $SUDO iptables -S "$FW_CHAIN" 2>/dev/null | grep -q "$LAN_CIDR" && pass "V15 la cadena permite $LAN_CIDR" || fail "V15 la cadena no menciona $LAN_CIDR"; fi
  $SUDO python3 "$REPO_DIR/scripts/install/access-model-audit.py" >/dev/null 2>&1 && pass "V16 modelo de acceso (ufw activo, SSH solo desde equipos concretos, 1433/8080 no expuestos)" || fail "V16 access-model-audit.py incumple"
  $SUDO systemctl is-enabled docker >/dev/null 2>&1 && pass "V17 Docker arranca con el sistema" || fail "V17 Docker no esta habilitado al arranque"
else skip "V13-V17 sin root ($SUDO): firewall/ufw/arranque no verificados (reintentar con --sudo-cmd 'sudo')"; fi
# --- 6. respaldos, programacion y custodia
suf="${PROJECT#theburyproject}"; suf="${suf#-}"; cron="/etc/cron.d/bury-backup${suf:+-$suf}"
[[ -f "$cron" ]] && grep -q "COMPOSE_PROJECT_NAME=$PROJECT\|backup.sh" "$cron" 2>/dev/null && pass "V18 cron de respaldos de esta instalacion ($cron)" || warn "V18 no se encontro $cron (o no es legible): programar con scripts/backup/install-schedule.sh --project $PROJECT"
if [[ -d "$BDIR/sql" ]] || $SUDO test -d "$BDIR/sql" 2>/dev/null; then
  lastfull="$($SUDO ls -1t "$BDIR/sql" 2>/dev/null | grep -E '_full_.*\.bak$' | head -1)"; [[ -z "$lastfull" ]] && lastfull="$(ls -1t "$BDIR/sql" 2>/dev/null | grep -E '_full_.*\.bak$' | head -1)"
  if [[ -n "$lastfull" ]]; then
    ts="$(stat -c %Y "$BDIR/sql/$lastfull" 2>/dev/null || $SUDO stat -c %Y "$BDIR/sql/$lastfull" 2>/dev/null)"; age=$(( ($(date +%s) - ${ts:-0}) / 3600 ))
    [[ $age -le 36 ]] && pass "V19 ultimo respaldo full hace ${age} h (<= 36 h)" || fail "V19 ultimo respaldo full hace ${age} h (> 36 h)"
  else warn "V19 todavia no hay un respaldo full en $BDIR/sql (esperable en una instalacion recien hecha; correr backup.sh all)"; fi
else warn "V19 $BDIR/sql no existe o no es legible"; fi
BACKUP_DIR="$BDIR" COMPOSE_PROJECT_NAME="$PROJECT" COMPOSE_ENV_FILES="$ENV_FILE" bash "$REPO_DIR/scripts/backup/check-custody.sh" --no-status --quiet >/dev/null 2>&1 \
  && pass "V20 check-custody.sh sin FAIL (claves fuera de los respaldos, .env 600)" || fail "V20 check-custody.sh reporta FAIL (correr sin --quiet para ver las reglas)"
rem="$(envv BACKUP_REMOVABLE_DIR)"; off="$(envv BACKUP_REMOTE)"
[[ -n "$rem" ]] && info "V21 copia local cifrada en medio extraible: $rem" || warn "V21 sin copia local cifrada (BACKUP_REMOVABLE_DIR vacio)"
[[ -n "$off" ]] && pass "V22 copia externa configurada (BACKUP_REMOTE)" || warn "V22 SIN copia externa independiente del equipo (B4: aceptarlo por escrito o mitigarlo)"
[[ -f /etc/systemd/system/bury-monitor.service || -d /etc/bury-monitor${suf:+-$suf} ]] && pass "V23 monitor de alertas instalado" || warn "V23 monitor de alertas no instalado (B6)"
echo "== resumen: $NP PASS, $NF FAIL, $NW WARN, $NS SKIP =="
[[ $NF -eq 0 ]]
