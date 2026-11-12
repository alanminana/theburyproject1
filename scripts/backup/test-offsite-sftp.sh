#!/usr/bin/env bash
# SIMULACRO de copia externa por SFTP: arnes AISLADO y auto-limpiante que prueba el pipeline REAL de scripts/backup/*.sh
# (backup.sh, fetch-offsite.sh, restore-sql.sh, restore-files.sh, restore-test.sh) contra un servidor SFTP DESCARTABLE
# (atmoz/sftp, imagen fijada por digest) con rclone crypt encima (BACKUP_REMOTE=secure:erp), SQL Server real en un stack
# Compose propio (COMPOSE_PROJECT_NAME unico) y datos 100% SINTETICOS. Detalle y resultados: docs/backup-sftp-simulacros.md
#
#   bash scripts/backup/test-offsite-sftp.sh [--keep] [--only a,b,...]        (exit 0 = todo PASS)
#
# Escenarios: a subida cifrada + verificacion, nombres opacos | b retencion remota (deletefile) sobre crypt+SFTP | c delete denegado
# | d fallo de conexion (servidor detenido, puerto cerrado, host key cambiada, credenciales invalidas) y recuperacion
# | g password crypt incorrecta | h autenticacion por clave privada | e corrupcion remota/local | f perdida total y restore completo.
#
# Requisitos: Linux (o WSL2 con Docker), docker compose v2, ssh-keygen. NUNCA toca el stack real: proyecto Compose, red, volumenes,
# contenedor SFTP y directorios temporales llevan un nombre unico (bury-p06sftp-<rand>) y se eliminan al terminar (salvo --keep).
# Escribe ./backup-offsite.env (sintetico, con marca) en la raiz del repo durante la prueba y lo borra al salir; si ya existe uno
# que no es de este arnes, ABORTA sin tocarlo.
set -uo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(cd "$HERE/../.." && pwd)"
cd "$REPO"

SFTP_IMAGE='atmoz/sftp@sha256:c5fb9e4beb8b7054a4c0b65c5e11df47cc20da95ad6002e88aecff62bab06372'   # atmoz/sftp:alpine
ALPINE_IMAGE='alpine:3@sha256:294b683cb724975bec92580e1e685676bd4b50bda910ddb8c51d4cabeaec77e6'   # el mismo que docker-compose.yml
OFFSITE_ENV="$REPO/backup-offsite.env"
ENV_MARK='# generado por scripts/backup/test-offsite-sftp.sh (sintetico, se borra al terminar)'
SFTP_PORT=2222
SFTP_HOST=sftp-test
SFTP_USER=burytest
DB=BuryP06Sim

KEEP=0 ONLY=""
while (( $# )); do
  case "$1" in
    --keep) KEEP=1; shift ;;
    --only) ONLY=",${2:-},"; shift 2 ;;
    -h|--help) sed -n '2,14p' "$0"; exit 0 ;;
    *) echo "argumento desconocido: $1" >&2; exit 1 ;;
  esac
done
want() { [[ -z "$ONLY" || "$ONLY" == *",$1,"* ]]; }

[[ "$(uname -s)" == Linux ]] || { echo "este arnes requiere Linux (en Windows: WSL2 con Docker)" >&2; exit 1; }
for c in docker ssh-keygen od; do command -v "$c" >/dev/null 2>&1 || { echo "falta $c" >&2; exit 1; }; done
docker compose version >/dev/null 2>&1 || { echo "falta docker compose v2" >&2; exit 1; }
if [[ -e "$OFFSITE_ENV" ]] && ! grep -qxF "$ENV_MARK" "$OFFSITE_ENV"; then
  echo "ABORTADO: ya existe $OFFSITE_ENV y no es de este arnes (podria ser la configuracion real). No se toca." >&2; exit 1
fi

# ---------------------------------------------------------------------------------------------------------- entorno aislado
RUN=$(od -An -N4 -tx1 /dev/urandom | tr -d ' \n')
PROJ="bury-p06sftp-$RUN"
T=$(mktemp -d "${TMPDIR:-/tmp}/bury-p06sftp.XXXXXX")
chmod 700 "$T"
SFTP_C="$PROJ-sftp"
rnd() { od -An -N"${1:-12}" -tx1 /dev/urandom | tr -d ' \n'; }

free_subnet() { # subred /24 que no use ninguna red Docker existente
  local used n i; used=$(docker network ls -q | xargs -r docker network inspect --format '{{range .IPAM.Config}}{{.Subnet}} {{end}}' 2>/dev/null)
  for i in $(seq 1 60); do n=$((100 + RANDOM % 120)); [[ " $used " == *" 172.28.$n.0/24 "* ]] || { echo "172.28.$n.0/24"; return; }; done
  echo "172.28.99.0/24"
}

SA_PASS="Aa1!$(rnd 10)"
SFTP_PASS=$(rnd 12)
CRYPT_P1=$(rnd 18); CRYPT_P2=$(rnd 18)
export COMPOSE_PROJECT_NAME="$PROJ"
export COMPOSE_PATH_SEPARATOR=':'
export COMPOSE_FILE="docker-compose.yml:docker-compose.offsite-sftp.yml"
export BURY_NET_SUBNET; BURY_NET_SUBNET=$(free_subnet)
export MSSQL_SA_PASSWORD="$SA_PASS" MSSQL_DATABASE="$DB" MSSQL_PID=Express DB_MEMORY=2g DB_CPUS=2
export ADMIN_EMAIL=p06@example.invalid ERP_DB_USER=p06app ERP_DB_PASSWORD="Bb2!$(rnd 8)" ERP_MIGRATION_USER=p06mig ERP_MIGRATION_PASSWORD="Cc3!$(rnd 8)" ERP_DOMAIN=p06.invalid
export BACKUP_DIR="$T/backups" BACKUP_OFFSITE_SSH_DIR="$T/ssh"
export BACKUP_REMOTE='secure:erp' BACKUP_REQUIRE_OFFSITE=true BACKUP_OFFSITE_MARKER=centinela.txt
export BACKUP_REMOTE_RETENTION_DAYS=30 BACKUP_REMOTE_MIN_KEEP=3 BACKUP_RETENTION_DAYS=14 BACKUP_RETENTION_MIN_KEEP=3
export BACKUP_STOP_APP=true BACKUP_INCLUDE_CADDY=true
mkdir -p "$T/ssh" "$T/sftp.d"; chmod 700 "$T/ssh"

# ---------------------------------------------------------------------------------------------------------- resultados
PASS=0 FAIL=0 FAILED=()
ok()  { PASS=$((PASS+1)); echo "PASS $1"; }
bad() { FAIL=$((FAIL+1)); FAILED+=("$1"); echo "FAIL $1"; [[ -n "${2:-}" ]] && echo "     -> $2"; }
check() { local name=$1; shift; if "$@"; then ok "$name"; else bad "$name"; fi; }
info() { echo "INFO $*"; }
section() { echo; echo "=== $* ==="; }

# ---------------------------------------------------------------------------------------------------------- limpieza
cleanup() {
  local ec=$?
  if (( KEEP )); then
    echo "--keep: NO se limpio. Proyecto=$PROJ tmp=$T contenedor=$SFTP_C"
    echo "Limpieza manual: docker rm -f $SFTP_C; docker compose -p $PROJ down -v --remove-orphans; docker run --rm -v $T:/t $ALPINE_IMAGE find /t -mindepth 1 -delete; rm -rf $T; rm -f $OFFSITE_ENV"
    return
  fi
  echo; echo "--- limpieza ($PROJ) ---"
  docker rm -f "$SFTP_C" >/dev/null 2>&1 || true
  docker compose --progress quiet down -v --remove-orphans >/dev/null 2>&1 || true
  # Solo recursos de ESTE proyecto (por etiqueta): nunca prune global.
  docker volume ls -q --filter "label=com.docker.compose.project=$PROJ" | xargs -r docker volume rm >/dev/null 2>&1 || true
  docker network ls -q --filter "label=com.docker.compose.project=$PROJ" | xargs -r docker network rm >/dev/null 2>&1 || true
  # SQL Server deja backups/ con dueño 10001: se vacia desde un contenedor root.
  docker run --rm --network none -v "$T:/t" "$ALPINE_IMAGE" find /t -mindepth 1 -delete >/dev/null 2>&1 || true
  rm -rf "$T" 2>/dev/null || true
  grep -qxF "$ENV_MARK" "$OFFSITE_ENV" 2>/dev/null && rm -f "$OFFSITE_ENV"
  local left; left=$(docker ps -aq --filter "label=com.docker.compose.project=$PROJ" | wc -l)
  echo "residuos: contenedores=$left volumenes=$(docker volume ls -q --filter "label=com.docker.compose.project=$PROJ" | wc -l) tmp=$([[ -e $T ]] && echo si || echo no)"
}
trap cleanup EXIT
trap 'exit 130' INT TERM

# ---------------------------------------------------------------------------------------------------------- helpers
dcq() { docker compose --progress quiet "$@"; }
rc()    { dcq run --rm -T --no-deps --quiet-pull backup-offsite "$@" </dev/null; }                 # rclone contra el remoto configurado
tools() { dcq run --rm -T --no-deps --quiet-pull backup-files "$@" </dev/null; }                    # alpine con /backups rw y volumenes ro
vols()  { dcq run --rm -T --no-deps --quiet-pull restore-files "$@" </dev/null; }                   # alpine con los 4 volumenes rw
srv()   { docker exec "$SFTP_C" sh -c "$@"; }
sqlq()  { dcq exec -T db bash -c 'SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" exec /opt/mssql-tools18/bin/sqlcmd -C -b -S localhost -U sa -d master -h -1 -W -Q "SET NOCOUNT ON; $0"' "$1" </dev/null | tr -d '\r'; }
obscure() { rc obscure "$1" | tr -d '\r\n'; }
stamp_ago() { date -u -d "-$1 days" +%Y-%m-%d_%H%M%SZ; }

N=0 OUT="" RCODE=0
run_script() { # run_script <script> [args...] -> $OUT (salida), $RCODE
  local s=$1; shift; N=$((N+1)); OUT="$T/run$N.out"
  bash "$HERE/$s" "$@" >"$OUT" 2>&1; RCODE=$?
}
run_backup() { run_script backup.sh "$@"; }
out_has() { grep -qE -- "$1" "$OUT"; }
show_out() { echo "     --- ultimas lineas de $OUT ---"; tail -n 8 "$OUT" | cut -c1-300 | sed 's/^/     | /'; }
expect_rc() { # expect_rc <nombre> <codigo>
  if [[ "$RCODE" == "$2" ]]; then ok "$1 (exit $2)"; else bad "$1" "exit=$RCODE, esperado $2"; show_out; fi
}

write_offsite_env() { # write_offsite_env [pass|key|nopin]  (+ overrides por variables PORT_ENV, PASS_ENV, P1_ENV)
  local auth=${1:-pass} port=${PORT_ENV:-$SFTP_PORT} pass=${PASS_ENV:-$OBS_SFTP} p1=${P1_ENV:-$OBS_P1}
  {
    echo "$ENV_MARK"
    echo "RCLONE_CONFIG_OFFSITE_TYPE=sftp"
    echo "RCLONE_CONFIG_OFFSITE_HOST=$SFTP_HOST"
    echo "RCLONE_CONFIG_OFFSITE_PORT=$port"
    echo "RCLONE_CONFIG_OFFSITE_USER=$SFTP_USER"
    if [[ $auth == key ]]; then echo "RCLONE_CONFIG_OFFSITE_KEY_FILE=/config/id_ed25519"; else echo "RCLONE_CONFIG_OFFSITE_PASS=$pass"; fi
    [[ $auth == nopin ]] || echo "RCLONE_CONFIG_OFFSITE_KNOWN_HOSTS_FILE=/config/known_hosts"
    echo "RCLONE_CONFIG_SECURE_TYPE=crypt"
    echo "RCLONE_CONFIG_SECURE_REMOTE=offsite:upload"
    echo "RCLONE_CONFIG_SECURE_PASSWORD=$p1"
    echo "RCLONE_CONFIG_SECURE_PASSWORD2=$OBS_P2"
  } >"$OFFSITE_ENV"
  chmod 600 "$OFFSITE_ENV"
}
reset_offsite_env() { unset PORT_ENV PASS_ENV P1_ENV; write_offsite_env pass; }
set_known_hosts() { local p; : >"$T/ssh/known_hosts"; for p in "$@"; do echo "[$SFTP_HOST]:$SFTP_PORT $(cut -d" " -f1,2 "$p")" >>"$T/ssh/known_hosts"; done; }   # una linea por tipo de clave (como ssh-keyscan): rclone exige la del tipo que negocia el servidor

dec_list() { rc lsf "secure:erp/$1" --files-only 2>/dev/null | tr -d '\r' | sort; }   # nombres DESCIFRADOS en el remoto
srv_files() { srv 'find /home/burytest/upload -type f | wc -l' | tr -d '\r\n'; }
seed_fake() { # seed_fake <sql|files> <nombre> <dias-mtime|0>  : archivo falso + .sha256 valido dentro de BACKUP_DIR
  local sub=$1 name=$2 age=${3:-0}
  tools sh -c 'cd "/backups/$0" && head -c 4096 /dev/urandom >"$1" && sha256sum "$1" >"$1.sha256"; if [ "$2" != 0 ]; then t=$(date -u -d "@$(( $(date +%s) - $2 * 86400 ))" +%Y%m%d%H%M.%S); touch -t "$t" "$1" "$1.sha256"; fi' "$sub" "$name" "$age"
}
not() { ! "$@"; }
local_has() { tools sh -c 'test -e "/backups/$0/$1"' "$1" "$2" 2>/dev/null; }
files_hash() { vols sh -c 'cd /dst && find . -type f -exec sha256sum {} + | sort | sha256sum' | tr -d '\r' | cut -d' ' -f1; }
db_fp() { sqlq "SELECT CAST(COUNT(*) AS varchar(20)) + ':' + CAST(CHECKSUM_AGG(CHECKSUM(Id, Payload)) AS varchar(20)) FROM [$DB].dbo.Datos" | head -n1; }

# ---------------------------------------------------------------------------------------------------------- setup
section "setup: stack SQL aislado ($PROJ), servidor SFTP descartable y credenciales sinteticas"
info "subred bury-net=$BURY_NET_SUBNET tmp=$T imagen SFTP=$SFTP_IMAGE"
docker image inspect "$SFTP_IMAGE" >/dev/null 2>&1 || docker pull -q "$SFTP_IMAGE" >/dev/null || { echo "no se pudo obtener $SFTP_IMAGE"; exit 1; }

ssh-keygen -q -t ed25519 -N '' -C p06-host -f "$T/ssh/host_ed25519"
ssh-keygen -q -t rsa -b 3072 -N "" -C p06-host-rsa -f "$T/ssh/host_rsa"
ssh-keygen -q -t ed25519 -N "" -C p06-host-otro -f "$T/ssh/host_otro_ed"
ssh-keygen -q -t rsa -b 3072 -N "" -C p06-host-otro-rsa -f "$T/ssh/host_otro_rsa"
ssh-keygen -q -t ed25519 -N '' -C p06-cliente -f "$T/ssh/id_ed25519"
set_known_hosts "$T/ssh/host_ed25519.pub" "$T/ssh/host_rsa.pub"
printf '#!/bin/sh\necho "Port %s" >> /etc/ssh/sshd_config\n' "$SFTP_PORT" >"$T/sftp.d/10-port.sh"; chmod 755 "$T/sftp.d/10-port.sh"

bash "$HERE/prepare-dir.sh" >/dev/null || { echo "prepare-dir.sh fallo"; exit 1; }   # si no, Docker crearia BACKUP_DIR/sql como root
dcq up -d --wait db >"$T/up-db.out" 2>&1 || { echo "no se pudo levantar db"; cat "$T/up-db.out"; exit 1; }
[[ "$(dcq config 2>/dev/null | sed -n 's/^name: //p')" == "$PROJ" ]] || { echo "ABORTADO: el proyecto Compose resuelto no es $PROJ"; exit 1; }
docker run -d --name "$SFTP_C" --network "${PROJ}_bury-net" --network-alias "$SFTP_HOST" --label "com.docker.compose.project=$PROJ" \
  -v "$T/ssh/host_ed25519:/etc/ssh/ssh_host_ed25519_key:ro" -v "$T/ssh/host_rsa:/etc/ssh/ssh_host_rsa_key:ro" \
  -v "$T/ssh/id_ed25519.pub:/home/$SFTP_USER/.ssh/keys/id_ed25519.pub:ro" \
  -v "$T/sftp.d:/etc/sftp.d:ro" \
  "$SFTP_IMAGE" "$SFTP_USER:$SFTP_PASS:::upload" >/dev/null || { echo "no se pudo iniciar el servidor SFTP"; exit 1; }
for i in $(seq 1 30); do docker logs "$SFTP_C" 2>&1 | grep -q "Server listening on 0.0.0.0 port $SFTP_PORT" && break; sleep 1; done
docker logs "$SFTP_C" 2>&1 | grep -q "Server listening on 0.0.0.0 port $SFTP_PORT" || { echo "el servidor SFTP no quedo escuchando en $SFTP_PORT"; docker logs "$SFTP_C" 2>&1 | tail -5; exit 1; }

OBS_SFTP=$(obscure "$SFTP_PASS"); OBS_P1=$(obscure "$CRYPT_P1"); OBS_P2=$(obscure "$CRYPT_P2")
[[ -n "$OBS_SFTP" && -n "$OBS_P1" && -n "$OBS_P2" ]] || { echo "rclone obscure fallo"; exit 1; }
write_offsite_env pass
SECRETS=("$SA_PASS" "$SFTP_PASS" "$CRYPT_P1" "$CRYPT_P2" "$OBS_SFTP" "$OBS_P1" "$OBS_P2")

# datos sinteticos: base con 5000 filas (recovery FULL) y 4 volumenes con archivos aleatorios
sqlq "CREATE DATABASE [$DB]; ALTER DATABASE [$DB] SET RECOVERY FULL;" >/dev/null
sqlq "CREATE TABLE [$DB].dbo.Datos (Id int NOT NULL PRIMARY KEY, Payload nvarchar(60) NOT NULL); CREATE TABLE [$DB].dbo.__EFMigrationsHistory (MigrationId nvarchar(150) NOT NULL PRIMARY KEY, ProductVersion nvarchar(32) NOT NULL); INSERT [$DB].dbo.__EFMigrationsHistory VALUES (N'20260101000000_Sintetica', N'8.0.0'); INSERT [$DB].dbo.Datos SELECT TOP 5000 ROW_NUMBER() OVER (ORDER BY (SELECT 1)), CONVERT(nvarchar(36), NEWID()) FROM sys.all_objects a CROSS JOIN sys.all_objects b;" >/dev/null
FP0=$(db_fp)
vols sh -c 'for d in keys uploads appdata caddy-data; do mkdir -p /dst/$d/sub; head -c 150000 /dev/urandom >/dst/$d/dato1.bin; head -c 3000 /dev/urandom >/dst/$d/sub/dato2.bin; echo "sintetico $d" >/dst/$d/leeme.txt; done' || { echo "no se pudieron sembrar los volumenes"; exit 1; }
FH0=$(files_hash)
info "huella base original=$FP0 ; huella archivos=${FH0:0:16}..."
[[ "$FP0" == 5000:* && -n "$FH0" ]] || { echo "datos sinteticos invalidos"; exit 1; }
rc mkdir secure:erp >/dev/null && rc touch secure:erp/centinela.txt >/dev/null || { echo "no se pudo crear el centinela"; exit 1; }
[[ "$(rc lsf secure:erp/centinela.txt | tr -d '\r\n')" == centinela.txt ]] && ok "setup: SFTP + crypt operativos, centinela creado (puerto $SFTP_PORT, host key fijada)" || bad "setup: crypt sobre SFTP no responde"

# ---------------------------------------------------------------------------------------------------------- a
if want a; then
section "a. subida cifrada + verificacion (backup.sh all, remoto crypt sobre SFTP real)"
run_backup all
expect_rc "a: backup all con copia externa" 0
check "a: SQL subido y VERIFICADO" out_has "copia externa sql: subida y VERIFICADA"
check "a: archivos subidos y VERIFICADOS" out_has "copia externa files: subida y VERIFICADA"
check "a: la verificacion uso lectura byte a byte (SFTP sin hash comun)" out_has "verificacion byte a byte"
NF=$(srv_files)
check "a: el servidor tiene los 6 objetos esperados (bak, sha, tar, sha, set, centinela) -> $NF" test "$NF" = 6
BADN=$(srv 'find /home/burytest/upload | sed "s#^/home/burytest/upload/\?##" | tr "/" "\n" | grep -v "^$" | grep -cvE "^[0-9a-v]+$"' | tr -d '\r\n')
check "a: en el servidor SOLO hay nombres opacos (base32 de crypt), 0 nombres legibles -> $BADN" test "$BADN" = 0
check "a: el servidor no contiene el nombre de la base ni texto de metadatos en claro" bash -c '[[ $(docker exec "$1" sh -c "grep -rlE \"$2|TAPE|BuryP06|sintetico|SQL Server\" /home/burytest/upload | wc -l" | tr -d "\r\n") == 0 ]]' _ "$SFTP_C" "$DB"
LS=$(dec_list sql)
check "a: via crypt se ven los nombres reales del full y su sha256" bash -c '[[ "$1" == *"_full_"*".bak"* && "$1" == *".bak.sha256"* ]]' _ "$LS"
A_FULL=$(grep -E '\.bak$' <<<"$LS" | head -n1); A_TAR=$(dec_list files | grep -E '\.tar\.gz$' | head -n1)
check "a: el manifiesto del par se subio" bash -c '[[ -n "$(docker compose --progress quiet run --rm -T --no-deps backup-offsite lsf secure:erp/files </dev/null | grep "^set_")" ]]'
fi

# ---------------------------------------------------------------------------------------------------------- b
if want b; then
section "b. retencion remota con deletefile sobre crypt+SFTP, respetando el minimo de copias"
for d in 70 60 50 40; do seed_fake sql "${DB}_full_$(stamp_ago $d).bak" 0; done
for d in 60 50 40; do seed_fake files "files_$(stamp_ago $d).tar.gz" 0; done
B0=$(srv_files)
run_backup all
expect_rc "b: backup all con retencion remota" 0
LSQ=$(dec_list sql); LFI=$(dec_list files)
check "b: fulls de 70/60/50 dias BORRADOS del remoto (principal y .sha256)" bash -c '! grep -qE "_full_($1|$2|$3)" <<<"$4"' _ "$(stamp_ago 70 | cut -c1-10)" "$(stamp_ago 60 | cut -c1-10)" "$(stamp_ago 50 | cut -c1-10)" "$LSQ"
check "b: el full de 40 dias se CONSERVA (minimo de 3 copias completas) con su .sha256" bash -c '[[ "$2" == *"_full_$1"*".bak"* && "$2" == *"_full_$1"*".bak.sha256"* ]]' _ "$(stamp_ago 40 | cut -c1-10)" "$LSQ"
check "b: tars de 60/50 dias BORRADOS, el de 40 conservado" bash -c '! grep -qE "files_($1|$2)" <<<"$4" && grep -q "files_$3" <<<"$4"' _ "$(stamp_ago 60 | cut -c1-10)" "$(stamp_ago 50 | cut -c1-10)" "$(stamp_ago 40 | cut -c1-10)" "$LFI"
check "b: quedan los 3 fulls completos (40d + 2 corridas) -> $(grep -c '\.bak$' <<<"$LSQ")" test "$(grep -c '\.bak$' <<<"$LSQ")" = 3
check "b: el log informa 5 archivos borrados (3 fulls + 2 tars)" out_has "retencion remota: 5 archivo\(s\) borrado\(s\)"
check "b: no quedan .sha256 huerfanos de lo borrado" bash -c '[[ $(grep -c "\.sha256$" <<<"$1") == $(grep -vc "\.sha256$" <<<"$1") ]]' _ "$LSQ"
fi

# ---------------------------------------------------------------------------------------------------------- c
if want c; then
section "c. permiso denegado al borrar en el remoto (directorios sticky de root, archivos de root): exit 6, no se borra nada mas"
CANARY_C="${DB}_full_$(stamp_ago 300).bak"; seed_fake sql "$CANARY_C" 300     # canario de la retencion LOCAL: debe sobrevivir a una corrida fallida
srv 'find /home/burytest/upload -type d -exec chown root {} + -exec chmod 1777 {} + ; find /home/burytest/upload -type f -exec chown root {} +'
LC0="$(dec_list sql; dec_list files)"; C0=$(srv_files)
run_backup all
expect_rc "c: delete denegado en el remoto" 6
check "c: mensaje legible: retencion INTERRUMPIDA + permiso denegado" bash -c 'grep -E "retencion remota INTERRUMPIDA" "$1" | grep -qiE "permission denied|failure|denied"' _ "$OUT"
check "c: se interrumpio ANTES de borrar nada (0 archivos)" out_has "INTERRUMPIDA tras borrar 0 archivo"
LC1="$(dec_list sql; dec_list files)"
check "c: todo lo que habia en el remoto sigue ahi (nada borrado)" bash -c '[[ -z "$(comm -23 <(sort <<<"$1") <(sort <<<"$2"))" ]]' _ "$LC0" "$LC1"
check "c: la corrida si subio su backup nuevo (fulls: $(grep -c '\.bak$' <<<"$LC1"))" bash -c '(( $(grep -c "\.bak$" <<<"$2") > $(grep -c "\.bak$" <<<"$1") ))' _ "$LC0" "$LC1"
check "c: la retencion LOCAL no corrio tras el fallo (canario intacto)" local_has sql "$CANARY_C"
check "c: el log no dice que la corrida termino OK" bash -c '! grep -q "backup .all. OK" "$1"' _ "$OUT"
# restituir permisos para seguir
srv 'find /home/burytest/upload -type d -exec chown burytest {} + -exec chmod 755 {} + ; find /home/burytest/upload -type f -exec chown burytest {} +'
fi

# ---------------------------------------------------------------------------------------------------------- d
if want d; then
section "d. fallo de conexion -> exit 6 con causa legible, sin retencion local; luego recuperacion"
CANARY="${DB}_full_$(stamp_ago 310).bak"; seed_fake sql "$CANARY" 310
FAILED_FULLS=0
d_fail() { # d_fail <nombre> <regex-de-causa>
  local name=$1 rx=$2
  run_backup full; FAILED_FULLS=$((FAILED_FULLS+1))
  expect_rc "d/$name: exit 6" 6
  check "d/$name: causa legible en el mensaje ($rx)" bash -c 'grep -E "\[ERROR\].*copia externa" "$1" | grep -qiE "$2"' _ "$OUT" "$rx"
  check "d/$name: sin retencion local (canario intacto)" local_has sql "$CANARY"
  check "d/$name: el log no declara OK" bash -c '! grep -q "backup .full. OK" "$1"' _ "$OUT"
}
docker stop "$SFTP_C" >/dev/null
d_fail "servidor detenido (con centinela)" 'no such host|lookup|refused|timeout|unreachable'
BACKUP_OFFSITE_MARKER="" d_fail "servidor detenido (sin centinela, falla la subida)" 'no such host|lookup|refused|timeout|unreachable'
docker start "$SFTP_C" >/dev/null
for i in $(seq 1 30); do docker logs "$SFTP_C" 2>&1 | grep -c "Server listening on 0.0.0.0 port $SFTP_PORT" | grep -q '^[2-9]' && break; sleep 1; done
PORT_ENV=2299 write_offsite_env pass; d_fail "puerto cerrado" 'refused|connect|timeout'; reset_offsite_env
cp "$T/ssh/known_hosts" "$T/ssh/known_hosts.ok"; set_known_hosts "$T/ssh/host_otro_ed.pub" "$T/ssh/host_otro_rsa.pub"
d_fail "host key CAMBIADA" 'host key|key mismatch|knownhosts|handshake'
PASS_ENV="$(obscure "incorrecta-$(rnd 4)")" write_offsite_env pass; d_fail "credenciales invalidas" 'unable to authenticate|handshake|auth'; reset_offsite_env
cp "$T/ssh/known_hosts.ok" "$T/ssh/known_hosts"
# recuperacion: la corrida siguiente repone lo no subido y vuelve a podar
LOCAL_FULLS=$(tools sh -c 'ls /backups/sql | grep "\.bak$" | grep -v "'"$CANARY"'" | tail -n '"$FAILED_FULLS" | tr -d '\r')
run_backup full
expect_rc "d/recuperacion: el servicio volvio y la corrida termina OK" 0
RL=$(dec_list sql)
MISSING=0; for f in $LOCAL_FULLS; do grep -qxF "$f" <<<"$RL" || MISSING=$((MISSING+1)); done
check "d/recuperacion: los $FAILED_FULLS fulls que no se pudieron subir quedaron repuestos en el remoto (faltan: $MISSING)" test "$MISSING" = 0
check "d/recuperacion: el canario antiguo (mtime > retencion) NO se subio (--max-age)" bash -c '! grep -qF "$1" <<<"$2"' _ "$CANARY" "$RL"
check "d/recuperacion: ahora si corrio la retencion local (canario borrado)" not local_has sql "$CANARY"
check "d/recuperacion: la retencion remota ya puede borrar (full de 40 dias eliminado)" bash -c '! grep -q "_full_$1" <<<"$2"' _ "$(stamp_ago 40 | cut -c1-10)" "$RL"
# Sin known_hosts rclone NO valida la identidad del servidor: se documenta tal cual (esto NO es un PASS de seguridad, es la
# demostracion de por que hay que fijarlo) y backup.sh lo advierte en el log.
set_known_hosts "$T/ssh/host_otro_ed.pub" "$T/ssh/host_otro_rsa.pub"; write_offsite_env nopin
run_backup full
check "d/sin known_hosts: rclone ACEPTA un servidor con otra clave (exit 0; riesgo documentado: fijar KNOWN_HOSTS_FILE)" test "$RCODE" = 0
check "d/sin known_hosts: backup.sh ADVIERTE en el log que la clave del servidor no se valida" out_has "SIN validar la clave del servidor"
reset_offsite_env; cp "$T/ssh/known_hosts.ok" "$T/ssh/known_hosts"
fi

# ---------------------------------------------------------------------------------------------------------- g
if want g; then
section "g. password de cifrado (crypt) incorrecta -> falla clara, sin escribir nada"
G0=$(srv_files)
P1_ENV="$(obscure "otra-$(rnd 6)")" write_offsite_env pass
run_backup full
expect_rc "g: backup con password crypt incorrecta" 6
check "g: el mensaje explica que no se pudo comprobar el centinela e incluye la causa" bash -c 'grep -E "\[ERROR\]" "$1" | grep -q "centinela.*Causa:"' _ "$OUT"
check "g: no se subio NADA al servidor (objetos antes=$G0, despues=$(srv_files))" test "$(srv_files)" = "$G0"
mkdir -p "$T/restore-g"
BACKUP_DIR="$T/restore-g" run_script fetch-offsite.sh
expect_rc "g: fetch-offsite con password crypt incorrecta" 6
check "g: fetch-offsite informa la causa (no solo 'descarga fallo')" bash -c 'grep -E "\[ERROR\].*descarga fallo \(sql\):" "$1" | grep -qE ": .{8,}"' _ "$OUT"
reset_offsite_env
fi

# ---------------------------------------------------------------------------------------------------------- h
if want h; then
section "h. autenticacion por clave privada (KEY_FILE montado desde BACKUP_OFFSITE_SSH_DIR), sin password"
write_offsite_env key
run_backup files
expect_rc "h: backup files autenticado SOLO con clave privada" 0
check "h: subida verificada con clave" out_has "copia externa files: subida y VERIFICADA"
check "h: el .env de la prueba no contiene password SFTP en este modo" bash -c '! grep -q "OFFSITE_PASS" "$1"' _ "$OFFSITE_ENV"
reset_offsite_env
fi

# ---------------------------------------------------------------------------------------------------------- e
if want e; then
section "e. corrupcion: remota (crypt autentica; sin crypt lo detecta sha256) y local"
# e1: crypt. Se altera un byte del full mas reciente EN EL SERVIDOR (copia de seguridad en el propio servidor y se repone despues)
E_FULL=$(dec_list sql | grep -E '\.bak$' | tail -n1)
ENC=$(rc cryptdecode --reverse secure: "erp/sql/$E_FULL" | tr -d '\r' | awk -F'\t' '{print $NF}' | tr -d ' ')
SRVPATH="/home/burytest/upload/$ENC"
if srv "test -s '$SRVPATH'"; then
  srv "cp -p '$SRVPATH' /tmp/e1.orig && sz=\$(wc -c <'$SRVPATH') && printf '\\377' | dd of='$SRVPATH' bs=1 seek=\$((sz/2)) conv=notrunc 2>/dev/null"
  mkdir -p "$T/restore-e1"
  BACKUP_DIR="$T/restore-e1" run_script fetch-offsite.sh
  check "e1/crypt: fetch-offsite con un respaldo remoto alterado FALLA (exit 6 o 3) -> $RCODE" bash -c '[[ "$1" == 6 || "$1" == 3 ]]' _ "$RCODE"
  check "e1/crypt: el mensaje identifica la corrupcion (autenticacion/descifrado)" bash -c 'grep -qiE "authenticate|bad password|corrupt|decrypt|sha256" "$1"' _ "$OUT"
  check "e1/crypt: el full alterado NO queda aceptado como valido en el destino" bash -c '! docker run --rm -v "$1:/b:ro" '"$ALPINE_IMAGE"' test -s "/b/sql/$2"' _ "$T/restore-e1" "$E_FULL"
  srv "cp -p /tmp/e1.orig '$SRVPATH'"
  check "e1/crypt: reparado el servidor, el respaldo vuelve a leerse (rclone cat de .sha256)" bash -c '[[ -n "$(docker compose --progress quiet run --rm -T --no-deps backup-offsite cat "secure:erp/sql/$0.sha256" </dev/null)" ]]' "$E_FULL"
else bad "e1/crypt: no se pudo ubicar el objeto cifrado de $E_FULL en el servidor ($SRVPATH)"; fi

# e1b: SIN crypt (remoto sftp plano): el transporte no autentica, lo detecta el sha256 de origen (exit 3)
BACKUP_REMOTE=offsite:upload/plano BACKUP_OFFSITE_MARKER="" run_backup all
expect_rc "e1b: backup all a un remoto SFTP plano (sin crypt)" 0
PTAR=$(srv 'ls /home/burytest/upload/plano/files | grep "\.tar\.gz$" | tail -n1' | tr -d '\r\n')
srv "printf '\\377' | dd of='/home/burytest/upload/plano/files/$PTAR' bs=1 seek=300 conv=notrunc 2>/dev/null"
mkdir -p "$T/restore-e1b"
BACKUP_DIR="$T/restore-e1b" BACKUP_REMOTE=offsite:upload/plano run_script fetch-offsite.sh
expect_rc "e1b/plano: fetch-offsite detecta el tar alterado por sha256 de origen" 3
check "e1b/plano: el mensaje nombra el .sha256 que no coincide" out_has "sha256 NO coincide.*files_.*\.tar\.gz\.sha256"
BACKUP_DIR="$T/restore-e1b" run_script restore-files.sh --archive "$PTAR" --force
expect_rc "e1b/plano: restore-files RECHAZA el tar alterado (no se acepta)" 9
check "e1b/plano: rechazo por sha256 antes de tocar los volumenes" bash -c 'grep -q "sha256 NO coincide" "$1"' _ "$OUT"
check "e1b/plano: los volumenes siguen intactos tras el rechazo (huella igual)" test "$(files_hash)" = "$FH0"

# e2: corrupcion LOCAL
L_FULL=$(tools sh -c 'ls /backups/sql | grep "\.bak$" | tail -n1' | tr -d '\r\n')
tools sh -c 'f="/backups/sql/$0"; cp -p "$f" /backups/sql/.e2.orig; sz=$(wc -c <"$f"); printf "\377" | dd of="$f" bs=1 seek=$((sz/2)) conv=notrunc 2>/dev/null' "$L_FULL"
run_script restore-sql.sh --backup "$L_FULL" --target-db BuryP06Corrupt
expect_rc "e2/local: restore-sql rechaza un full local alterado (sha256)" 3
check "e2/local: no se creo la base destino" test "$(sqlq "SELECT COUNT(*) FROM sys.databases WHERE name = N'BuryP06Corrupt'" | head -n1)" = 0
tools sh -c 'rm -f "/backups/sql/$0.sha256"' "$L_FULL"
run_script restore-sql.sh --backup "$L_FULL" --target-db BuryP06Corrupt
expect_rc "e2/local: sin .sha256, RESTORE VERIFYONLY ... CHECKSUM igualmente lo rechaza" 3
tools sh -c 'f="/backups/sql/$0"; mv -f /backups/sql/.e2.orig "$f"; cd /backups/sql && sha256sum "$0" >"$0.sha256"' "$L_FULL"
run_script restore-sql.sh --backup "$L_FULL" --target-db BuryP06Corrupt
expect_rc "e2/local: reparado el archivo, el restore a una base nueva funciona" 0
sqlq "DROP DATABASE [BuryP06Corrupt]" >/dev/null 2>&1 || true
fi

# ---------------------------------------------------------------------------------------------------------- f
if want f; then
section "f. perdida total: se borran BACKUP_DIR local y los volumenes; recuperacion desde el SFTP (fetch + restore SQL con logs + restore archivos)"
reset_offsite_env
sqlq "INSERT [$DB].dbo.Datos SELECT 5000 + ROW_NUMBER() OVER (ORDER BY (SELECT 1)), CONVERT(nvarchar(36), NEWID()) FROM (SELECT TOP 300 1 AS x FROM sys.all_objects) q" >/dev/null
run_backup all
expect_rc "f: backup all previo a la perdida (par full+tar)" 0
sqlq "INSERT [$DB].dbo.Datos SELECT 6000 + ROW_NUMBER() OVER (ORDER BY (SELECT 1)), CONVERT(nvarchar(36), NEWID()) FROM (SELECT TOP 200 1 AS x FROM sys.all_objects) q" >/dev/null
run_backup log
expect_rc "f: backup de log posterior (RPO)" 0
FP1=$(db_fp); FH1=$(files_hash)
check "f: la base cambio respecto del inicio (hay algo que recuperar): $FP0 -> $FP1" test "$FP0" != "$FP1"
# perdida: BACKUP_DIR (dueño mixto 10001) vaciado desde un contenedor, volumenes del proyecto y db eliminados; el servidor SFTP NO se toca
tools sh -c 'find /backups -mindepth 1 -delete'
dcq rm -sf db >/dev/null 2>&1
docker volume ls -q --filter "label=com.docker.compose.project=$PROJ" | xargs -r docker volume rm >/dev/null 2>&1
rmdir "$BACKUP_DIR" 2>/dev/null
check "f: perdida simulada: BACKUP_DIR local y volumenes inexistentes" bash -c '[[ ! -e "$1" ]] && [[ -z "$(docker volume ls -q --filter "label=com.docker.compose.project=$2")" ]]' _ "$BACKUP_DIR" "$PROJ"
run_script fetch-offsite.sh
expect_rc "f: fetch-offsite desde el SFTP a un servidor nuevo" 0
check "f: descargado y sha256 de origen verificados" out_has "sha256 de origen verificados"
F_FULL=$(tools sh -c 'ls /backups/sql' | tr -d '\r' | grep -E '\.bak$' | tail -n1); F_TAR=$(tools sh -c 'ls /backups/files' | tr -d '\r' | grep -E '\.tar\.gz$' | tail -n1)
info "restaurando full=$F_FULL tar=$F_TAR"
dcq up -d --wait db >"$T/up-db2.out" 2>&1 || { bad "f: db no levanto tras la perdida"; cat "$T/up-db2.out"; }
run_script restore-sql.sh --backup "$F_FULL" --target-db "$DB" --with-logs
expect_rc "f: restore-sql (full + logs) a una instancia nueva" 0
check "f: se aplico al menos 1 log" out_has "logs aplicados: [1-9]"
FP2=$(db_fp)
check "f: la base restaurada es IDENTICA a la original (filas:checksum $FP1 == $FP2)" test "$FP1" = "$FP2"
run_script restore-files.sh --archive "$F_TAR"
expect_rc "f: restore-files sobre volumenes vacios" 0
FH2=$(files_hash)
check "f: los archivos restaurados son IDENTICOS a los originales (sha256 de todo el arbol)" test "$FH1" = "$FH2"
run_script restore-test.sh
expect_rc "f: restore-test.sh (restore + DBCC CHECKDB) sobre lo recuperado" 0
fi

# ---------------------------------------------------------------------------------------------------------- transversal
section "transversal: ningun secreto en logs ni salidas"
LEAK=0
for s in "${SECRETS[@]}"; do
  [[ -n "$s" ]] || continue
  if grep -rqF -- "$s" "$T"/run*.out "$BACKUP_DIR/logs" 2>/dev/null; then LEAK=$((LEAK+1)); fi
done
check "ningun password/obscure/clave aparece en las salidas ni en backup.log (${#SECRETS[@]} valores buscados)" test "$LEAK" = 0

echo; echo "=========================================="
echo "resultado: $PASS PASS, $FAIL FAIL   (proyecto $PROJ)"
if (( FAIL )); then printf 'FALLARON:\n'; printf '  - %s\n' "${FAILED[@]}"; exit 1; fi
exit 0
