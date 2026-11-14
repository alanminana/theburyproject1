#!/usr/bin/env bash
# ENSAYO de RECUPERACION INTEGRAL ante PERDIDA TOTAL del servidor, AISLADO y auto-limpiante. Usa el stack REAL del ERP (db, db-init, migrate, app,
# caddy con `tls internal`), los scripts REALES de scripts/backup/*.sh y un servidor SFTP DESCARTABLE (atmoz/sftp fijado por digest) con rclone
# crypt encima. Datos 100% SINTETICOS. Detalle y resultados: docs/backup-dr-rotacion-claves.md
#
#   bash scripts/backup/test-dr-integral.sh [--keep]            (exit 0 = todo PASS)
#   ERP_IMAGE=<imagen ya construida> bash scripts/backup/test-dr-integral.sh   (si no se define, se construye desde ./Dockerfile y se elimina al final)
#
# Escenarios: d1 servidor original operativo (CA publica extraida, sesion iniciada, datos sinteticos) | d2 backup all + backup de log (RPO)
# | d3 PERDIDA TOTAL: stack, volumenes y BACKUP_DIR eliminados; el servidor original queda indisponible | d4 recuperacion SOLO desde el backup
# aislado (fetch-offsite -> restore-files -> db -> restore-sql con logs -> up) con tiempos por paso | d5 la aplicacion arranca y consulta los datos
# restaurados (login con la cookie VIEJA, datos y checksums identicos, archivos identicos, DBCC) | d6 CA de Caddy: la CA restaurada sigue siendo la
# que confian los clientes; root.key viaja en el tar (hallazgo); control negativo: sin caddy-data la CA nueva NO es la confiada | d7 rotacion de la CA
# (procedimiento documentado) | d8 fallos: sin la password crypt no hay recuperacion; sin las claves de Data Protection la cookie vieja se rechaza.
# NUNCA toca el stack real ni TheBuryProjectDb: proyecto Compose, red, volumenes, subred y contenedores con nombre unico (bury-p06dr-<rand>), sin
# publicar puertos en el host. Escribe ./backup-offsite.env (sintetico, con marca) y lo borra al salir; si ya existe uno que no es de este arnes, ABORTA.
set -uo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(cd "$HERE/../.." && pwd)"
cd "$REPO"

SFTP_IMAGE='atmoz/sftp@sha256:c5fb9e4beb8b7054a4c0b65c5e11df47cc20da95ad6002e88aecff62bab06372'
ALPINE_IMAGE='alpine:3@sha256:294b683cb724975bec92580e1e685676bd4b50bda910ddb8c51d4cabeaec77e6'
OFFSITE_ENV="$REPO/backup-offsite.env"
ENV_MARK='# generado por scripts/backup/test-dr-integral.sh (sintetico, se borra al terminar)'
SFTP_PORT=2222 SFTP_HOST=sftp-test SFTP_USER=burytest DB=BuryDrSim DOMAIN=tbp ADMIN_USER=admin

KEEP=0
while (( $# )); do
  case "$1" in
    --keep) KEEP=1; shift ;;
    -h|--help) sed -n '2,17p' "$0"; exit 0 ;;
    *) echo "argumento desconocido: $1" >&2; exit 1 ;;
  esac
done

[[ "$(uname -s)" == Linux ]] || { echo "este arnes requiere Linux (en Windows: WSL2 con Docker)" >&2; exit 1; }
for c in docker ssh-keygen od flock; do command -v "$c" >/dev/null 2>&1 || { echo "falta $c" >&2; exit 1; }; done
docker compose version >/dev/null 2>&1 || { echo "falta docker compose v2" >&2; exit 1; }
own_env() { [[ -f "$OFFSITE_ENV" && "$(head -n1 "$OFFSITE_ENV")" == "$ENV_MARK" ]]; }
# Reserva ATOMICA (noclobber): si aparece un archivo ajeno entre el control y la escritura, la creacion falla y se ABORTA sin tocarlo.
if ! ( set -C; printf '%s\n' "$ENV_MARK" >"$OFFSITE_ENV" ) 2>/dev/null && ! own_env; then
  echo "ABORTADO: ya existe $OFFSITE_ENV y no es de este arnes (podria ser la configuracion real). No se toca." >&2; exit 1
fi

RUN=$(od -An -N4 -tx1 /dev/urandom | tr -d ' \n')
PROJ="bury-p06dr-$RUN"
T=$(mktemp -d "${TMPDIR:-/tmp}/bury-p06dr.XXXXXX") || { echo "mktemp fallo" >&2; exit 1; }
[[ -n "$T" && -d "$T" ]] || { echo "directorio temporal invalido" >&2; exit 1; }
chmod 700 "$T" || exit 1
SFTP_C="$PROJ-sftp"; CLIENT_C="$PROJ-client"
rnd() { od -An -N"${1:-12}" -tx1 /dev/urandom | tr -d ' \n'; }
free_subnet() {
  local used n i; used=$(docker network ls -q | xargs -r docker network inspect --format '{{range .IPAM.Config}}{{.Subnet}} {{end}}' 2>/dev/null)
  for i in $(seq 1 60); do n=$((100 + RANDOM % 120)); [[ " $used " == *" 172.28.$n.0/24 "* ]] || { echo "172.28.$n.0/24"; return; }; done
  echo "172.28.99.0/24"
}

SA_PASS="Aa1!$(rnd 10)"; SFTP_PASS=$(rnd 12); CRYPT_P1=$(rnd 18); CRYPT_P2=$(rnd 18); ADMIN_PASS="Zz9!$(rnd 10)"
ERP_APP_PASS="Bb2!$(rnd 8)"; ERP_MIG_PASS="Cc3!$(rnd 8)"
BUILT=0
if [[ -z "${ERP_IMAGE:-}" ]]; then ERP_IMAGE="bury-p06dr-erp:$RUN"; BUILT=1; fi
export COMPOSE_PROJECT_NAME="$PROJ" COMPOSE_PATH_SEPARATOR=':'
export COMPOSE_FILE="docker-compose.yml:docker-compose.offsite-sftp.yml:$T/dr-overlay.yml"
export BURY_NET_SUBNET; BURY_NET_SUBNET=$(free_subnet)
export ERP_IMAGE MSSQL_SA_PASSWORD="$SA_PASS" MSSQL_DATABASE="$DB" MSSQL_PID=Express DB_MEMORY=2g DB_CPUS=2
export ADMIN_EMAIL=p06@example.invalid ADMIN_USERNAME="$ADMIN_USER" ADMIN_PASSWORD="$ADMIN_PASS" ERP_DB_USER=p06app ERP_DB_PASSWORD="$ERP_APP_PASS" ERP_MIGRATION_USER=p06mig ERP_MIGRATION_PASSWORD="$ERP_MIG_PASS" ERP_DOMAIN="$DOMAIN"
export BACKUP_DIR="$T/backups" BACKUP_OFFSITE_SSH_DIR="$T/ssh"
export BACKUP_REMOTE='secure:erp' BACKUP_REQUIRE_OFFSITE=true BACKUP_OFFSITE_MARKER=centinela.txt
export BACKUP_REMOTE_RETENTION_DAYS=30 BACKUP_REMOTE_MIN_KEEP=3 BACKUP_RETENTION_DAYS=14 BACKUP_RETENTION_MIN_KEEP=3
export BACKUP_STOP_APP=true BACKUP_INCLUDE_CADDY=true
mkdir -p "$T/ssh" "$T/sftp.d" "$T/client" && chmod 700 "$T/ssh" || { echo "no se pudo preparar $T" >&2; exit 1; }
: >"$T/empty.env"; export COMPOSE_ENV_FILES="$T/empty.env"   # blinda contra el .env REAL del repo
# Overlay: el ensayo NO publica puertos en el host (Caddy solo es alcanzable dentro de la red del proyecto).
printf 'services:\n  caddy:\n    ports: !reset []\n' >"$T/dr-overlay.yml"

PASS=0 FAIL=0 FAILED=()
ok()  { PASS=$((PASS+1)); echo "PASS $1"; }
bad() { FAIL=$((FAIL+1)); FAILED+=("$1"); echo "FAIL $1"; [[ -n "${2:-}" ]] && echo "     -> $2"; }
check() { local name=$1; shift; if "$@"; then ok "$name"; else bad "$name"; fi; }
info() { echo "INFO $*"; }
section() { echo; echo "=== $* ==="; }

cleanup() {
  local ec=$?
  if (( KEEP )); then
    echo "--keep: NO se limpio. Proyecto=$PROJ tmp=$T sftp=$SFTP_C cliente=$CLIENT_C imagen=$ERP_IMAGE"; return
  fi
  echo; echo "--- limpieza ($PROJ) ---"
  docker rm -f "$SFTP_C" "$CLIENT_C" >/dev/null 2>&1 || true
  docker ps -aq --filter "label=com.docker.compose.project=$PROJ" | xargs -r docker rm -f >/dev/null 2>&1 || true
  docker compose --progress quiet down -v --remove-orphans >/dev/null 2>&1 || true
  docker volume ls -q --filter "label=com.docker.compose.project=$PROJ" | xargs -r docker volume rm >/dev/null 2>&1 || true
  docker network ls -q --filter "label=com.docker.compose.project=$PROJ" | xargs -r docker network rm >/dev/null 2>&1 || true
  docker run --rm --network none -v "$T:/t" "$ALPINE_IMAGE" find /t -mindepth 1 -delete >/dev/null 2>&1 || true
  rm -rf "$T" 2>/dev/null || true
  own_env && rm -f "$OFFSITE_ENV"
  (( BUILT )) && docker rmi -f "$ERP_IMAGE" >/dev/null 2>&1
  echo "residuos: contenedores=$(docker ps -aq --filter "label=com.docker.compose.project=$PROJ" | wc -l) volumenes=$(docker volume ls -q --filter "label=com.docker.compose.project=$PROJ" | wc -l) tmp=$([[ -e $T ]] && echo si || echo no)"
  exit "$ec"
}
trap cleanup EXIT
trap 'exit 130' INT TERM

dcq() { docker compose --progress quiet "$@"; }
rc()    { dcq run --rm -T --no-deps --quiet-pull backup-offsite "$@" </dev/null; }
tools() { dcq run --rm -T --no-deps --quiet-pull backup-files "$@" </dev/null; }
vols()  { dcq run --rm -T --no-deps --quiet-pull restore-files "$@" </dev/null; }
sqlq()  { dcq exec -T db bash -c 'SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" exec /opt/mssql-tools18/bin/sqlcmd -C -b -I -S localhost -U sa -d master -h -1 -W -Q "SET NOCOUNT ON; $0"' "$1" </dev/null | tr -d '\r'; }
obscure() { rc obscure "$1" | tr -d '\r\n'; }
N=0 OUT="" RCODE=0
run_script() { local s=$1; shift; N=$((N+1)); OUT="$T/run$N.out"; bash "$HERE/$s" "$@" >"$OUT" 2>&1; RCODE=$?; }
out_has() { grep -qE -- "$1" "$OUT"; }
show_out() { echo "     --- ultimas lineas de $OUT ---"; tail -n 8 "$OUT" | cut -c1-300 | sed 's/^/     | /'; }
expect_rc() { if [[ "$RCODE" == "$2" ]]; then ok "$1 (exit $2)"; else bad "$1" "exit=$RCODE, esperado $2"; show_out; fi; }
now() { date +%s; }
TIMES=()
lap() { local name=$1 t0=$2; local d=$(( $(now) - t0 )); TIMES+=("$name:$d"); info "tiempo $name: ${d}s"; }

# --- cliente (alpine + curl) dentro de la red del proyecto: habla con Caddy por TLS como lo haria un navegador de la LAN, con `--connect-to`.
cl() { docker exec "$CLIENT_C" sh -c "$1"; }
CURL="curl -sS --max-time 20 --connect-to $DOMAIN:443:caddy:443"
cl_status() { # cl_status <cacert|-k> <jar|-> <path>  -> codigo HTTP
  local ca=$1 jar=$2 path=$3 opt
  if [[ $ca == "-k" ]]; then opt="-k"; else opt="--cacert $ca"; fi
  cl "$CURL $opt $( [[ $jar != - ]] && echo "-b $jar" ) -o /dev/null -w '%{http_code}' https://$DOMAIN$path" 2>/dev/null
}
cl_login() { # cl_login <cacert> <jar> -> codigo de la respuesta al POST (302 = autenticado)
  local ca=$1 jar=$2 tok
  tok=$(cl "$CURL --cacert $ca -c $jar https://$DOMAIN/Identity/Account/Login" | sed -n 's/.*__RequestVerificationToken[^>]*value="\([^"]*\)".*/\1/p' | head -n1)
  [[ -n "$tok" ]] || { echo "sin-token"; return 1; }
  local code
  code=$(cl "$CURL --cacert $ca -b $jar -c $jar -o /pagina.html -w '%{http_code}' --data-urlencode '__RequestVerificationToken=$tok' --data-urlencode 'Input.UserName=$ADMIN_USER' --data-urlencode 'Input.Password=$ADMIN_PASS' https://$DOMAIN/Identity/Account/Login")
  # Primer login de un usuario: la app valida la password y pide ACEPTAR los terminos (200 con el paso de terminos). Se completa ese paso.
  if [[ "$code" == 200 ]] && cl "grep -q 'Input_AceptaTerminos' /pagina.html"; then
    tok=$(cl "sed -n 's/.*__RequestVerificationToken[^>]*value=\"\\([^\"]*\\)\".*/\\1/p' /pagina.html | head -n1")
    code=$(cl "$CURL --cacert $ca -b $jar -c $jar -o /dev/null -w '%{http_code}' --data-urlencode '__RequestVerificationToken=$tok' --data-urlencode 'Input.UserName=$ADMIN_USER' --data-urlencode 'Input.Password=$ADMIN_PASS' --data-urlencode 'Input.NombreCompleto=Administrador Sintetico' --data-urlencode 'Input.AceptaTerminos=true' https://$DOMAIN/Identity/Account/Login")
  fi
  echo "$code"
}
marca_json() { cl "$CURL --cacert $1 -b $2 https://$DOMAIN/Marca/GetJson?id=$3" 2>/dev/null; }
wait_ready() { # wait_ready <cacert> [segundos]
  local i; for i in $(seq 1 "${2:-60}"); do [[ "$(cl_status "$1" - /health/ready)" == 200 ]] && return 0; sleep 1; done; return 1
}
ca_fp() { dcq run --rm -T --no-deps --quiet-pull restore-files sh -c 'sha256sum /dst/caddy-data/caddy/pki/authorities/local/root.crt | cut -d" " -f1' </dev/null | tr -d '\r'; }
files_hash() { vols sh -c 'cd /dst && find . -type f ! -path "./caddy-data/*" -exec sha256sum {} + | sort | sha256sum' | tr -d '\r' | cut -d' ' -f1; }
db_fp() { sqlq "SELECT CAST((SELECT COUNT(*) FROM [$DB].dbo.Marcas) AS varchar(10)) + ':' + CAST((SELECT CHECKSUM_AGG(CHECKSUM(Id, Codigo, Nombre)) FROM [$DB].dbo.Marcas) AS varchar(20)) + ':' + CAST((SELECT COUNT(*) FROM [$DB].dbo.AspNetUsers) AS varchar(10)) + ':' + CAST((SELECT COUNT(*) FROM [$DB].dbo.__EFMigrationsHistory) AS varchar(10))" | head -n1; }

write_offsite_env() { # +override P1_ENV
  {
    echo "$ENV_MARK"
    echo "RCLONE_CONFIG_OFFSITE_TYPE=sftp"; echo "RCLONE_CONFIG_OFFSITE_HOST=$SFTP_HOST"; echo "RCLONE_CONFIG_OFFSITE_PORT=$SFTP_PORT"
    echo "RCLONE_CONFIG_OFFSITE_USER=$SFTP_USER"; echo "RCLONE_CONFIG_OFFSITE_PASS=$OBS_SFTP"
    echo "RCLONE_CONFIG_OFFSITE_KNOWN_HOSTS_FILE=/config/known_hosts"
    echo "RCLONE_CONFIG_SECURE_TYPE=crypt"; echo "RCLONE_CONFIG_SECURE_REMOTE=offsite:upload"
    echo "RCLONE_CONFIG_SECURE_PASSWORD=${P1_ENV:-$OBS_P1}"; echo "RCLONE_CONFIG_SECURE_PASSWORD2=$OBS_P2"
  } >"$OFFSITE_ENV"
  chmod 600 "$OFFSITE_ENV"
}

# ---------------------------------------------------------------------------------------------------------- setup
section "setup: imagen del ERP, servidor SFTP descartable y credenciales sinteticas ($PROJ)"
if (( BUILT )); then
  info "construyendo la imagen del ERP desde ./Dockerfile (puede tardar unos minutos): $ERP_IMAGE"
  docker build -q -t "$ERP_IMAGE" "$REPO" >"$T/build.out" 2>&1 || { echo "docker build fallo"; tail -n 20 "$T/build.out"; exit 1; }
else
  docker image inspect "$ERP_IMAGE" >/dev/null 2>&1 || { echo "no existe la imagen ERP_IMAGE=$ERP_IMAGE"; exit 1; }
fi
docker image inspect "$SFTP_IMAGE" >/dev/null 2>&1 || docker pull -q "$SFTP_IMAGE" >/dev/null || { echo "no se pudo obtener $SFTP_IMAGE"; exit 1; }
ssh-keygen -q -t ed25519 -N '' -C p06dr -f "$T/ssh/host_ed25519"; ssh-keygen -q -t rsa -b 3072 -N "" -C p06dr-rsa -f "$T/ssh/host_rsa"
: >"$T/ssh/known_hosts"; for p in "$T/ssh/host_ed25519.pub" "$T/ssh/host_rsa.pub"; do echo "[$SFTP_HOST]:$SFTP_PORT $(cut -d" " -f1,2 "$p")" >>"$T/ssh/known_hosts"; done
printf '#!/bin/sh\necho "Port %s" >> /etc/ssh/sshd_config\n' "$SFTP_PORT" >"$T/sftp.d/10-port.sh"; chmod 755 "$T/sftp.d/10-port.sh"
bash "$HERE/prepare-dir.sh" >/dev/null || { echo "prepare-dir.sh fallo"; exit 1; }
rc version >/dev/null 2>&1   # crea la red del proyecto
[[ "$(dcq config 2>/dev/null | sed -n 's/^name: //p')" == "$PROJ" ]] || { echo "ABORTADO: el proyecto Compose resuelto no es $PROJ"; exit 1; }
[[ -z "$(dcq config 2>/dev/null | grep -E 'published:')" ]] || { echo "ABORTADO: el stack publicaria puertos en el host (Compose >= 2.24 requerido para '!reset')"; exit 1; }
docker run -d --name "$SFTP_C" --network "${PROJ}_bury-net" --network-alias "$SFTP_HOST" --label "com.docker.compose.project=$PROJ" \
  -v "$T/ssh/host_ed25519:/etc/ssh/ssh_host_ed25519_key:ro" -v "$T/ssh/host_rsa:/etc/ssh/ssh_host_rsa_key:ro" -v "$T/sftp.d:/etc/sftp.d:ro" \
  "$SFTP_IMAGE" "$SFTP_USER:$SFTP_PASS:::upload" >/dev/null || { echo "no se pudo iniciar el servidor SFTP"; exit 1; }
for i in $(seq 1 30); do docker logs "$SFTP_C" 2>&1 | grep -q "Server listening on 0.0.0.0 port $SFTP_PORT" && break; sleep 1; done
docker logs "$SFTP_C" 2>&1 | grep -q "Server listening on 0.0.0.0 port $SFTP_PORT" || { echo "el servidor SFTP no quedo escuchando"; exit 1; }
OBS_SFTP=$(obscure "$SFTP_PASS"); OBS_P1=$(obscure "$CRYPT_P1"); OBS_P2=$(obscure "$CRYPT_P2"); OBS_BAD=$(obscure "otra-$(rnd 8)")
[[ -n "$OBS_SFTP" && -n "$OBS_P1" && -n "$OBS_P2" && -n "$OBS_BAD" ]] || { echo "rclone obscure fallo"; exit 1; }
write_offsite_env
SECRETS=("$SA_PASS" "$SFTP_PASS" "$CRYPT_P1" "$CRYPT_P2" "$OBS_SFTP" "$OBS_P1" "$OBS_P2" "$ADMIN_PASS" "$ERP_APP_PASS" "$ERP_MIG_PASS")
docker run -d --name "$CLIENT_C" --network "${PROJ}_bury-net" --label "com.docker.compose.project=$PROJ" "$ALPINE_IMAGE" sleep 7200 >/dev/null || { echo "no se pudo iniciar el cliente"; exit 1; }
docker exec "$CLIENT_C" apk add --no-cache curl openssl >/dev/null 2>&1 || { echo "no se pudo instalar curl/openssl en el cliente"; exit 1; }
rc mkdir secure:erp >/dev/null && echo "centinela sintetico" | tools sh -c 'cat >/backups/logs/centinela.txt' && rc copyto /backups/logs/centinela.txt secure:erp/centinela.txt >/dev/null || { echo "no se pudo crear el centinela"; exit 1; }
ok "setup: SFTP + crypt operativos y cliente de pruebas listo"

# ---------------------------------------------------------------------------------------------------------- d1
section "d1. servidor ORIGINAL operativo: db + db-init + migrate + app + caddy (Docker Compose real)"
T0=$(now)
dcq up -d --wait db app caddy >"$T/up1.out" 2>&1; UPRC=$?
if (( UPRC != 0 )); then bad "d1: el stack original no levanto"; tail -n 20 "$T/up1.out"; exit 1; fi
lap stack-original-healthy "$T0"
for i in $(seq 1 30); do docker exec "$(dcq ps -q caddy)" test -s /data/caddy/pki/authorities/local/root.crt 2>/dev/null && break; sleep 1; done
docker cp "$(dcq ps -q caddy)":/data/caddy/pki/authorities/local/root.crt "$T/client/ca-orig.crt" || { echo "no se pudo extraer root.crt"; exit 1; }
docker cp "$T/client/ca-orig.crt" "$CLIENT_C":/ca-orig.crt
check "d1: la CA extraida es un certificado PUBLICO (sin clave privada)" bash -c 'grep -q "BEGIN CERTIFICATE" "$1" && ! grep -q "PRIVATE KEY" "$1"' _ "$T/client/ca-orig.crt"
check "d1: /health/ready = 200 por TLS confiando SOLO en la CA original" wait_ready /ca-orig.crt 60
CA_FP0=$(sha256sum "$T/client/ca-orig.crt" | cut -d' ' -f1)
check "d1: login del administrador (302) y sesion iniciada" bash -c '[[ "$1" == 302 ]]' _ "$(cl_login /ca-orig.crt /jar-orig.txt)"
sqlq "INSERT [$DB].dbo.Marcas (Codigo, Nombre, Activo, CreatedAt, IsDeleted) VALUES (N'DR1', N'MarcaDR-$RUN-alfa', 1, SYSUTCDATETIME(), 0), (N'DR2', N'MarcaDR-$RUN-beta', 1, SYSUTCDATETIME(), 0)" >/dev/null
ID1=$(sqlq "SELECT Id FROM [$DB].dbo.Marcas WHERE Codigo = N'DR1'" | head -n1)
check "d1: la app consulta el dato sintetico (GET /Marca/GetJson con la sesion) -> id $ID1" bash -c '[[ "$(echo "$1" | grep -c "MarcaDR-'"$RUN"'-alfa")" -ge 1 ]]' _ "$(marca_json /ca-orig.crt /jar-orig.txt "$ID1")"
vols sh -c 'mkdir -p /dst/uploads/sub && head -c 400000 /dev/urandom >/dst/uploads/dr-documento.bin && echo "sintetico" >/dst/uploads/sub/leeme.txt' || bad "d1: no se pudieron sembrar archivos en uploads"
FH0=$(files_hash); FP0=$(db_fp)
info "huella archivos=${FH0:0:16}... ; huella BD (marcas:checksum:usuarios:migraciones)=$FP0"
[[ -n "$FH0" && "$FP0" =~ ^[0-9]+: ]] || { echo "datos sinteticos invalidos"; exit 1; }
CK_COOKIE=$(cl "grep -c 'AspNetCore\|Identity\|\.AspNet' /jar-orig.txt" | tr -d '\r\n')
check "d1: la jarra de cookies tiene la cookie de sesion de Identity" bash -c '[[ "$1" -ge 1 ]]' _ "$CK_COOKIE"

# ---------------------------------------------------------------------------------------------------------- d2
section "d2. backup all (SQL + archivos con caddy-data, copia externa) y backup de log posterior (RPO)"
run_script backup.sh all
expect_rc "d2: backup all con copia externa" 0
check "d2: SQL y archivos subidos y VERIFICADOS" bash -c 'grep -q "copia externa sql: subida y VERIFICADA" "$1" && grep -q "copia externa files: subida y VERIFICADA" "$1"' _ "$OUT"
sqlq "INSERT [$DB].dbo.Marcas (Codigo, Nombre, Activo, CreatedAt, IsDeleted) VALUES (N'DR3', N'MarcaDR-$RUN-gamma-posterior', 1, SYSUTCDATETIME(), 0)" >/dev/null
run_script backup.sh log
expect_rc "d2: backup de log posterior al full" 0
FP1=$(db_fp)
check "d2: la base cambio tras el full (la fila del log existe): $FP0 -> $FP1" test "$FP0" != "$FP1"
# Hallazgo de custodia: caddy-data viaja en el tar de archivos. Se inspecciona el tar LOCAL (sin tocar la base).
TAR=$(tools sh -c 'ls /backups/files' | tr -d '\r' | grep -E '\.tar\.gz$' | tail -n1)
LIST=$(tools sh -c 'tar -tzf "/backups/files/$0"' "$TAR" | tr -d '\r')
check "d2/CA: el tar de archivos CONTIENE caddy/pki/authorities/local/root.crt (necesario para recuperar la CA)" bash -c 'grep -q "caddy-data/caddy/pki/authorities/local/root.crt" <<<"$1"' _ "$LIST"
check "d2/CA: el tar CONTIENE root.key e intermediate.key (la clave privada de la CA comparte custodia con los respaldos)" bash -c 'grep -q "authorities/local/root.key" <<<"$1" && grep -q "authorities/local/intermediate.key" <<<"$1"' _ "$LIST"
check "d2/CA: el tar local es 0600 (solo el dueño lo lee)" bash -c '[[ "$(docker run --rm --network none -v "$1:/b:ro" '"$ALPINE_IMAGE"' stat -c %a "/b/files/$2")" == 600 ]]' _ "$BACKUP_DIR" "$TAR"
BADN=$(docker exec "$SFTP_C" sh -c 'find /home/burytest/upload | sed "s#^/home/burytest/upload/\?##" | tr "/" "\n" | grep -v "^$" | grep -cvE "^[0-9a-v]+$"' | tr -d '\r\n')
check "d2/CA: en el servidor SFTP solo hay nombres opacos de crypt (base32), 0 legibles: root.key viaja dentro de un tar cifrado -> $BADN" test "$BADN" = 0

# ---------------------------------------------------------------------------------------------------------- d3
section "d3. PERDIDA TOTAL: stack, volumenes y BACKUP_DIR eliminados; el servidor original queda indisponible"
T_LOSS=$(now)
dcq down -v --remove-orphans >/dev/null 2>&1
docker ps -aq --filter "label=com.docker.compose.project=$PROJ" --filter "label=com.docker.compose.service" | xargs -r docker rm -f >/dev/null 2>&1
docker volume ls -q --filter "label=com.docker.compose.project=$PROJ" | xargs -r docker volume rm >/dev/null 2>&1
docker run --rm --network none -v "$T:/t" "$ALPINE_IMAGE" sh -c 'find /t/backups -mindepth 1 -delete; rmdir /t/backups'
check "d3: no queda NINGUN volumen del proyecto ni BACKUP_DIR local" bash -c '[[ -z "$(docker volume ls -q --filter "label=com.docker.compose.project=$1")" && ! -e "$2" ]]' _ "$PROJ" "$BACKUP_DIR"
check "d3: el servidor original esta indisponible (ni db, ni app, ni caddy en ejecucion)" bash -c '[[ -z "$(docker ps -q --filter "label=com.docker.compose.project=$1" --filter "label=com.docker.compose.service")" ]]' _ "$PROJ"
check "d3: el cliente ya no llega a la aplicacion (health != 200) -> $(cl_status /ca-orig.crt - /health/ready || true)" bash -c '[[ "$1" != 200 ]]' _ "$(cl_status /ca-orig.crt - /health/ready || true)"

# ---------------------------------------------------------------------------------------------------------- d4
section "d4. recuperacion SOLO desde el backup aislado (docs/backup-restore.md §10): prepare-dir -> fetch -> restore-files -> db -> restore-sql -> up"
T1=$(now)
run_script prepare-dir.sh;                   expect_rc "d4: prepare-dir.sh en el servidor nuevo" 0
TS=$(now); run_script fetch-offsite.sh;      expect_rc "d4: fetch-offsite desde el SFTP cifrado (BACKUP_DIR vacio)" 0; lap fetch-offsite "$TS"
check "d4: descargado y sha256 de origen verificados" out_has "sha256 de origen verificados"
F_FULL=$(tools sh -c 'ls /backups/sql' | tr -d '\r' | grep -E '\.bak$' | tail -n1); F_TAR=$(tools sh -c 'ls /backups/files' | tr -d '\r' | grep -E '\.tar\.gz$' | tail -n1)
info "restaurando full=$F_FULL tar=$F_TAR"
TS=$(now); run_script restore-files.sh --archive "$F_TAR"; expect_rc "d4: restore-files sobre volumenes vacios (app aun no existe)" 0; lap restore-files "$TS"
TS=$(now); dcq up -d --wait db >"$T/up-db2.out" 2>&1 || { bad "d4: db no levanto"; cat "$T/up-db2.out"; }; lap db-up "$TS"
TS=$(now); run_script restore-sql.sh --backup "$F_FULL" --target-db "$DB" --with-logs; expect_rc "d4: restore-sql (full + logs) en una instancia NUEVA" 0; lap restore-sql "$TS"
check "d4: se aplico al menos 1 log (RPO de minutos, no de un dia)" out_has "logs aplicados: [1-9]"
TS=$(now); dcq up -d --wait >"$T/up2.out" 2>&1; UP2=$?; lap stack-up "$TS"
check "d4: db-init + migrate + app + caddy quedan healthy (up --wait exit $UP2)" test "$UP2" = 0
(( UP2 == 0 )) || tail -n 15 "$T/up2.out"
TS=$(now); wait_ready /ca-orig.crt 90; WR=$?; lap health-ready "$TS"
check "d4: /health/ready = 200 por TLS confiando SOLO en la CA ORIGINAL de los clientes" test "$WR" = 0
RTO=$(( $(now) - T1 ))
TIMES+=("RTO-tecnico-total:$RTO"); info "RTO tecnico total (desde el servidor vacio hasta /health/ready=200): ${RTO}s; tiempo desde la perdida: $(( $(now) - T_LOSS ))s"

# ---------------------------------------------------------------------------------------------------------- d5
section "d5. la aplicacion consulta los datos restaurados"
check "d5: la cookie de sesion VIEJA sigue valida (claves de Data Protection restauradas): /Marca/GetJson responde con el dato" bash -c '[[ "$(echo "$1" | grep -c "MarcaDR-'"$RUN"'-alfa")" -ge 1 ]]' _ "$(marca_json /ca-orig.crt /jar-orig.txt "$ID1")"
check "d5: login NUEVO con la password del administrador (302)" bash -c '[[ "$1" == 302 ]]' _ "$(cl_login /ca-orig.crt /jar-nuevo.txt)"
ID3=$(sqlq "SELECT Id FROM [$DB].dbo.Marcas WHERE Codigo = N'DR3'" | head -n1)
check "d5: la fila posterior al full (recuperada por el log) es visible desde la app (id $ID3)" bash -c '[[ "$(echo "$1" | grep -c "gamma-posterior")" -ge 1 ]]' _ "$(marca_json /ca-orig.crt /jar-nuevo.txt "$ID3")"
FP2=$(db_fp)
check "d5: la base restaurada es IDENTICA a la original (marcas:checksum:usuarios:migraciones $FP1 == $FP2)" test "$FP1" = "$FP2"
FH2=$(files_hash)
check "d5: los archivos restaurados (keys, uploads, appdata) son IDENTICOS (sha256 del arbol)" test "$FH0" = "$FH2"
run_script restore-test.sh
expect_rc "d5: restore-test.sh (restore aislado + DBCC CHECKDB) sobre lo recuperado" 0
check "d5: el rol de la app sigue sin DDL (db-init re-vinculo los usuarios)" bash -c '[[ "$1" == 0 ]]' _ "$(sqlq "SELECT COUNT(*) FROM [$DB].sys.database_role_members rm JOIN [$DB].sys.database_principals r ON r.principal_id = rm.role_principal_id JOIN [$DB].sys.database_principals u ON u.principal_id = rm.member_principal_id WHERE u.name = N'p06app' AND r.name IN (N'db_owner', N'db_ddladmin')" | head -n1)"

# ---------------------------------------------------------------------------------------------------------- d6
section "d6. CA de Caddy: recuperada junto con caddy-data; control negativo sin ella"
CA_FP1=$(ca_fp)
check "d6: la CA raiz restaurada es la MISMA (sha256 de root.crt) -> los clientes no deben volver a confiar" test "$CA_FP0" = "$CA_FP1"
# control negativo 1: se pierde SOLO caddy-data (el resto sano): Caddy crea una CA NUEVA y la CA vieja deja de ser confiable.
dcq stop caddy >/dev/null 2>&1
vols sh -c 'find /dst/caddy-data -mindepth 1 -delete' || bad "d6: no se pudo vaciar caddy-data"
dcq up -d --wait caddy >/dev/null 2>&1
for i in $(seq 1 40); do [[ "$(cl_status -k - /health/ready)" == 200 ]] && break; sleep 1; done
CA_FP2=$(ca_fp)
check "d6/negativo: sin caddy-data Caddy genera una CA NUEVA (huella distinta)" bash -c '[[ -n "$2" && "$1" != "$2" ]]' _ "$CA_FP0" "$CA_FP2"
check "d6/negativo: los clientes que confian en la CA vieja RECHAZAN el certificado nuevo (hay que redistribuir la CA)" bash -c '[[ "$1" != 200 ]]' _ "$(cl_status /ca-orig.crt - /health/ready || true)"
check "d6/negativo: la aplicacion y los datos NO dependen de la CA (con -k todo responde: 200)" bash -c '[[ "$1" == 200 ]]' _ "$(cl_status -k /jar-orig.txt "/Marca/GetJson?id=$ID1" || true)"
# control negativo 2: root.key ausente con root.crt presente: Caddy NO arranca (por eso excluir root.key del backup NO es una opcion segura sin procedimiento).
dcq stop caddy app >/dev/null 2>&1
run_script restore-files.sh --archive "$F_TAR" --force
expect_rc "d6: restore-files --force restituye caddy-data del backup (CA original)" 0
vols sh -c 'rm -f /dst/caddy-data/caddy/pki/authorities/local/root.key' && dcq up -d caddy >/dev/null 2>&1; sleep 8
check "d6/negativo: con root.crt pero SIN root.key Caddy no sirve TLS (health != 200): excluir la clave del backup exige un procedimiento, no un simple filtro" bash -c '[[ "$1" != 200 ]]' _ "$(cl_status -k - /health/live || true)"
check "d6/negativo: el log de Caddy explica la causa (loading root key ... no such file)" bash -c 'docker logs "$(docker compose --progress quiet ps -aq caddy | head -n1)" 2>&1 | grep -q "loading root key"'
dcq stop caddy app >/dev/null 2>&1
run_script restore-files.sh --archive "$F_TAR" --force
expect_rc "d6: se restituye caddy-data completo (con root.key) y Caddy vuelve" 0
dcq up -d --wait app caddy >/dev/null 2>&1
check "d6: tras restituir caddy-data la CA ORIGINAL vuelve a ser la confiada por los clientes (/health/ready 200)" wait_ready /ca-orig.crt 60

# ---------------------------------------------------------------------------------------------------------- d7
section "d7. rotacion de la CA (procedimiento docs/backup-dr-rotacion-claves.md §4): CA nueva, app sin cambios, backup nuevo"
dcq stop caddy >/dev/null 2>&1
# 1) la CA vieja (incluida root.key) NO se borra: se mueve DENTRO del volumen a un directorio de retiro. (Custodia real: copiarla fuera del servidor y del BACKUP_DIR.)
RET=$(date -u +%Y%m%dT%H%M%SZ); vols sh -c 'cd /dst/caddy-data/caddy && mkdir -p "retiro/$0" && mv pki "retiro/$0/pki" && { [ ! -d certificates ] || mv certificates "retiro/$0/certificates"; }' "$RET" || bad "d7: no se pudo retirar la CA vieja"
dcq up -d --wait caddy >/dev/null 2>&1
for i in $(seq 1 40); do [[ "$(cl_status -k - /health/live)" == 200 ]] && break; sleep 1; done
dcq up -d --wait app >/dev/null 2>&1
CA_FP3=$(ca_fp)
check "d7: Caddy genero una CA nueva (huella distinta de la original y de la del control negativo)" bash -c '[[ -n "$1" && "$1" != "$2" && "$1" != "$3" ]]' _ "$CA_FP3" "$CA_FP0" "$CA_FP2"
docker cp "$(dcq ps -q caddy)":/data/caddy/pki/authorities/local/root.crt "$T/client/ca-nueva.crt" && docker cp "$T/client/ca-nueva.crt" "$CLIENT_C":/ca-nueva.crt
check "d7: el material retirado (root.key vieja) sigue en el volumen para la transicion, y NO se perdio" bash -c '[[ -n "$(docker run --rm --network none -v "$1_caddy-data:/d:ro" '"$ALPINE_IMAGE"' find /d/caddy/retiro -name root.key)" ]]' _ "$PROJ"
for i in $(seq 1 60); do [[ "$(cl_status /ca-nueva.crt - /health/ready)" == 200 ]] && break; sleep 1; done
check "d7: los clientes con la CA NUEVA instalada llegan a /health/ready = 200" bash -c '[[ "$1" == 200 ]]' _ "$(cl_status /ca-nueva.crt - /health/ready)"
check "d7: los clientes con la CA VIEJA ya no confian (esperado hasta redistribuir la nueva)" bash -c '[[ "$1" != 200 ]]' _ "$(cl_status /ca-orig.crt - /health/ready || true)"
check "d7: la sesion y los datos siguen intactos tras rotar la CA (login existente + dato)" bash -c '[[ "$(echo "$1" | grep -c "MarcaDR-'"$RUN"'-alfa")" -ge 1 ]]' _ "$(marca_json /ca-nueva.crt /jar-orig.txt "$ID1")"
run_script backup.sh files
expect_rc "d7: backup de archivos posterior a la rotacion (con la CA nueva) a la copia externa" 0
NEWTAR=$(tools sh -c 'ls -1 /backups/files' | tr -d '\r' | grep -E '\.tar\.gz$' | tail -n1)
NEWLIST=$(tools sh -c 'tar -tzf "/backups/files/$0"' "$NEWTAR" | tr -d '\r')
check "d7: el tar nuevo contiene la CA nueva Y la vieja retirada (retiro/<fecha>/pki viaja con los respaldos hasta que se la saque del volumen: ver procedimiento)" bash -c 'grep -q "caddy-data/caddy/pki/authorities/local/root.crt" <<<"$1" && grep -q "retiro/$2/pki/authorities/local/root.key" <<<"$1"' _ "$NEWLIST" "$RET"

# ---------------------------------------------------------------------------------------------------------- d8
section "d8. fallos: sin la password de crypt no hay recuperacion (y falla claro); sin las claves de Data Protection la cookie vieja se rechaza"
mkdir -p "$T/restore-d8"
P1_ENV="$OBS_BAD" write_offsite_env
BACKUP_DIR="$T/restore-d8" run_script fetch-offsite.sh
expect_rc "d8: fetch-offsite con la password crypt equivocada" 6
check "d8: el mensaje incluye la causa del fallo de descarga" bash -c 'grep -E "\[ERROR\].*descarga fallo" "$1" | grep -qE ": .{8,}"' _ "$OUT"
check "d8: nada util se descargo con la password equivocada" bash -c '[[ -z "$(find "$1" -type f ! -name .bury-backup-root ! -name "*.log" 2>/dev/null)" ]]' _ "$T/restore-d8"
write_offsite_env
# Control negativo de Data Protection: con las claves VACIAS, app genera claves nuevas y la cookie vieja se rechaza (302 al login).
dcq stop app >/dev/null 2>&1
vols sh -c 'mkdir -p /dst/keys-aparte && mv /dst/keys/* /dst/keys-aparte/ 2>/dev/null; true'
dcq up -d --wait app >/dev/null 2>&1
for i in $(seq 1 60); do [[ "$(cl_status /ca-nueva.crt - /health/ready)" == 200 ]] && break; sleep 1; done
check "d8/negativo: SIN las claves de Data Protection restauradas, la cookie VIEJA es rechazada (redirige al login: 302)" bash -c '[[ "$1" == 302 ]]' _ "$(cl_status /ca-nueva.crt /jar-orig.txt "/Marca/GetJson?id=$ID1")"
vols sh -c 'mv /dst/keys-aparte/* /dst/keys/ 2>/dev/null; rmdir /dst/keys-aparte'

# ---------------------------------------------------------------------------------------------------------- transversal
section "transversal: ningun secreto en logs ni salidas; resumen de tiempos"
LEAK=0
for s in "${SECRETS[@]}"; do
  [[ -n "$s" ]] || continue
  if grep -rqF -- "$s" "$T"/run*.out "$BACKUP_DIR/logs" 2>/dev/null; then LEAK=$((LEAK+1)); fi
done
check "ningun password/obscure aparece en las salidas ni en backup.log (${#SECRETS[@]} valores buscados)" test "$LEAK" = 0
echo "TIEMPOS (segundos): ${TIMES[*]}"

echo; echo "=========================================="
echo "resultado: $PASS PASS, $FAIL FAIL   (proyecto $PROJ)"
if (( FAIL )); then printf 'FALLARON:\n'; printf '  - %s\n' "${FAILED[@]}"; exit 1; fi
exit 0
