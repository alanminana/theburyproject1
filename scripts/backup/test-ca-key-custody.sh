#!/usr/bin/env bash
# ENSAYO de CUSTODIA de la clave privada de la CA de Caddy (root.key / intermediate.key), AISLADO y auto-limpiante. Usa el Caddy REAL del
# docker-compose.yml con el Caddyfile real (`tls internal`), SQL Server real (stack Compose propio) y los scripts REALES de scripts/backup/*.sh
# (backup.sh, fetch-offsite.sh, restore-files.sh, restore-ca-key.sh, ca-seal-init.sh, check-custody.sh). Copia externa = disco local (rclone
# crypt sobre backend local) para no depender de un SFTP. Datos 100% SINTETICOS. Detalle y resultados: docs/backup-ca-key-custodia.md
#
#   bash scripts/backup/test-ca-key-custody.sh [--keep]        (exit 0 = todo PASS)
#
# Escenarios: a riesgo base (modo include: root.key dentro del tar) | b modo seal: el tar y BACKUP_DIR NO contienen la clave; .caseal cifrado
# | c fail-closed (sin clave de sellado no hay backup degradado) | d PERDIDA TOTAL: volumenes y BACKUP_DIR eliminados, fetch-offsite + restore
# con la custodia correcta -> misma CA, mismas claves, Caddy sirve | e restauracion SIN la clave (sin .caseal) | f clave de sellado INCORRECTA,
# blob alterado, blob malicioso (ruta fuera de authorities, enlace) | g otras claves/certificados intactos y sin secretos en salidas/logs
# | h idempotencia y --force | i retencion local/remota del .caseal | j check-custody.
# NUNCA toca el stack real ni TheBuryProjectDb: proyecto Compose, red, volumenes y directorios con nombre unico (bury-cakey-<rand>), sin
# publicar puertos en el host. No genera ni usa credenciales reales: todo se crea y se destruye aqui.
set -uo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(cd "$HERE/../.." && pwd)"
cd "$REPO"

ALPINE_IMAGE='alpine:3@sha256:294b683cb724975bec92580e1e685676bd4b50bda910ddb8c51d4cabeaec77e6'
OFFSITE_ENV="$REPO/backup-offsite.env"
ENV_MARK='# generado por scripts/backup/test-ca-key-custody.sh (sintetico, se borra al terminar)'
DB=BuryCaKey DOMAIN=tbp CA_DIR=caddy/pki/authorities/local

KEEP=0
while (( $# )); do
  case "$1" in
    --keep) KEEP=1; shift ;;
    -h|--help) sed -n '2,15p' "$0"; exit 0 ;;
    *) echo "argumento desconocido: $1" >&2; exit 1 ;;
  esac
done
[[ "$(uname -s)" == Linux ]] || { echo "este arnes requiere Linux (en Windows: WSL2 con Docker)" >&2; exit 1; }
for c in docker od flock; do command -v "$c" >/dev/null 2>&1 || { echo "falta $c" >&2; exit 1; }; done
docker compose version >/dev/null 2>&1 || { echo "falta docker compose v2" >&2; exit 1; }
own_env() { [[ -f "$OFFSITE_ENV" && "$(head -n1 "$OFFSITE_ENV")" == "$ENV_MARK" ]]; }
if ! ( set -C; printf '%s\n' "$ENV_MARK" >"$OFFSITE_ENV" ) 2>/dev/null && ! own_env; then
  echo "ABORTADO: ya existe $OFFSITE_ENV y no es de este arnes (podria ser la configuracion real). No se toca." >&2; exit 1
fi

RUN=$(od -An -N4 -tx1 /dev/urandom | tr -d ' \n')
PROJ="bury-cakey-$RUN"
T=$(mktemp -d "${TMPDIR:-/tmp}/bury-cakey.XXXXXX") || { echo "mktemp fallo" >&2; exit 1; }
[[ -n "$T" && -d "$T" ]] || { echo "directorio temporal invalido" >&2; exit 1; }
chmod 700 "$T" || exit 1
CLIENT_C="$PROJ-client"
rnd() { od -An -N"${1:-12}" -tx1 /dev/urandom | tr -d ' \n'; }
free_subnet() {
  local used n i; used=$(docker network ls -q | xargs -r docker network inspect --format '{{range .IPAM.Config}}{{.Subnet}} {{end}}' 2>/dev/null)
  for i in $(seq 1 60); do n=$((100 + RANDOM % 120)); [[ " $used " == *" 172.28.$n.0/24 "* ]] || { echo "172.28.$n.0/24"; return; }; done
  echo "172.28.99.0/24"
}

SA_PASS="Aa1!$(rnd 10)"; CRYPT_P1=$(rnd 18); CRYPT_P2=$(rnd 18)
export COMPOSE_PROJECT_NAME="$PROJ" COMPOSE_PATH_SEPARATOR=':'
export COMPOSE_FILE="docker-compose.yml:docker-compose.offsite-disk.yml:$T/overlay.yml"
export BURY_NET_SUBNET; BURY_NET_SUBNET=$(free_subnet)
export MSSQL_SA_PASSWORD="$SA_PASS" MSSQL_DATABASE="$DB" MSSQL_PID=Express DB_MEMORY=2g DB_CPUS=2
export ADMIN_EMAIL=ca@example.invalid ERP_DB_USER=caapp ERP_DB_PASSWORD="Bb2!$(rnd 8)" ERP_MIGRATION_USER=camig ERP_MIGRATION_PASSWORD="Cc3!$(rnd 8)" ERP_DOMAIN="$DOMAIN"
export BACKUP_DIR="$T/backups" BACKUP_OFFSITE_DISK_DIR="$T/offsite"
export BACKUP_REMOTE='secure:erp' BACKUP_REQUIRE_OFFSITE=true BACKUP_OFFSITE_MARKER=centinela.txt
export BACKUP_REMOTE_RETENTION_DAYS=30 BACKUP_REMOTE_MIN_KEEP=3 BACKUP_RETENTION_DAYS=14 BACKUP_RETENTION_MIN_KEEP=3
export BACKUP_STOP_APP=true BACKUP_INCLUDE_CADDY=true
mkdir -p "$T/offsite" "$T/seal" && chmod 700 "$T/seal" || { echo "no se pudo preparar $T" >&2; exit 1; }
: >"$T/empty.env"; chmod 600 "$T/empty.env"; export COMPOSE_ENV_FILES="$T/empty.env" CUSTODY_RECOVERY_DIRS=""   # hermetico: ni el .env ni ~/secure-* reales   # blinda contra el .env REAL del repo
printf 'services:\n  caddy:\n    ports: !reset []\n' >"$T/overlay.yml"
SEAL_ENV="$T/seal/ca-seal.env"

PASS=0 FAIL=0 FAILED=()
ok()  { PASS=$((PASS+1)); echo "PASS $1"; }
bad() { FAIL=$((FAIL+1)); FAILED+=("$1"); echo "FAIL $1"; [[ -n "${2:-}" ]] && echo "     -> $2"; }
check() { local name=$1; shift; if "$@"; then ok "$name"; else bad "$name"; fi; }
info() { echo "INFO $*"; }
section() { echo; echo "=== $* ==="; }
not() { ! "$@"; }

cleanup() {
  local ec=$?
  if (( KEEP )); then echo "--keep: NO se limpio. Proyecto=$PROJ tmp=$T cliente=$CLIENT_C"; return; fi
  echo; echo "--- limpieza ($PROJ) ---"
  docker rm -f "$CLIENT_C" >/dev/null 2>&1 || true
  docker ps -aq --filter "label=com.docker.compose.project=$PROJ" | xargs -r docker rm -f >/dev/null 2>&1 || true
  docker compose --progress quiet down -v --remove-orphans >/dev/null 2>&1 || true
  docker volume ls -q --filter "label=com.docker.compose.project=$PROJ" | xargs -r docker volume rm >/dev/null 2>&1 || true
  docker network ls -q --filter "label=com.docker.compose.project=$PROJ" | xargs -r docker network rm >/dev/null 2>&1 || true
  docker run --rm --network none -v "$T:/t" "$ALPINE_IMAGE" find /t -mindepth 1 -delete >/dev/null 2>&1 || true
  rm -rf "$T" 2>/dev/null || true
  own_env && rm -f "$OFFSITE_ENV"
  echo "residuos: contenedores=$(docker ps -aq --filter "label=com.docker.compose.project=$PROJ" | wc -l) volumenes=$(docker volume ls -q --filter "label=com.docker.compose.project=$PROJ" | wc -l) tmp=$([[ -e $T ]] && echo si || echo no)"
  exit "$ec"
}
trap cleanup EXIT
trap 'exit 130' INT TERM

dcq()   { docker compose --progress quiet "$@"; }
rc()    { dcq run --rm -T --no-deps --quiet-pull backup-offsite "$@" </dev/null; }
tools() { dcq run --rm -T --no-deps --quiet-pull backup-files "$@" </dev/null; }
vols()  { dcq run --rm -T --no-deps --quiet-pull restore-files "$@" </dev/null; }
sqlq()  { dcq exec -T db bash -c 'SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" exec /opt/mssql-tools18/bin/sqlcmd -C -b -I -S localhost -U sa -d master -h -1 -W -Q "SET NOCOUNT ON; $0"' "$1" </dev/null | tr -d '\r'; }
N=0 OUT="" RCODE=0
run_script() { local s=$1; shift; N=$((N+1)); OUT="$T/run$N.out"; bash "$HERE/$s" "$@" >"$OUT" 2>&1; RCODE=$?; }
out_has() { grep -qE -- "$1" "$OUT"; }
show_out() { echo "     --- ultimas lineas de $OUT ---"; tail -n 8 "$OUT" | cut -c1-300 | sed 's/^/     | /'; }
expect_rc() { if [[ "$RCODE" == "$2" ]]; then ok "$1 (exit $2)"; else bad "$1" "exit=$RCODE, esperado $2"; show_out; fi; }

cl() { docker exec "$CLIENT_C" sh -c "$1"; }
cl_code() { cl "curl -sS --max-time 8 --connect-to $DOMAIN:443:caddy:443 --cacert $1 -o /dev/null -w '%{http_code}' https://$DOMAIN/ 2>/dev/null || true"; }   # 502 = TLS confiado (no hay app); 000 = sin TLS / no confiado
caddy_up() { dcq up -d --no-deps caddy >/dev/null 2>&1; }
wait_ca() { local i; for i in $(seq 1 60); do [[ -n "$(vols sh -c "test -s /dst/caddy-data/$CA_DIR/root.crt -a -s /dst/caddy-data/$CA_DIR/root.key && echo ok" | tr -d '\r')" ]] && return 0; sleep 1; done; return 1; }
vhash() { vols sh -c "sha256sum /dst/caddy-data/$1 2>/dev/null | cut -d' ' -f1" | tr -d '\r\n'; }
vmode() { vols sh -c "stat -c '%a %u' /dst/caddy-data/$1 2>/dev/null" | tr -d '\r\n'; }
LEAF=""
leaf_path() { vols sh -c 'cd /dst/caddy-data && find caddy/certificates -name "*.key" | head -n1' | tr -d '\r\n'; }
# Cuantas veces aparece un fragmento de la clave privada (2a linea del PEM) en un arbol: crudo, y dentro de tar.gz.
needle_hits() { # needle_hits <dir-del-host>: archivos del arbol que contienen el fragmento (crudo o dentro de un tar.gz)
  docker run --rm --network none -v "$1:/d:ro" "$ALPINE_IMAGE" sh -c 'for f in $(find /d -type f); do
      grep -qF -- "$0" "$f" 2>/dev/null && echo raw
      case "$f" in *.tar.gz) tar -xzOf "$f" 2>/dev/null | grep -qF -- "$0" && echo tar ;; esac
    done' "$NEEDLE" | wc -l | tr -d '\r\n '
}
no_key_in_new_backup() { # no_key_in_new_backup <tar>: ni el tar, ni el .caseal, ni backup.log, ni la ultima salida contienen la clave
  local hits
  hits=$(docker run --rm --network none -v "$BACKUP_DIR/files/$1:/f:ro" "$ALPINE_IMAGE" sh -c 'tar -xzOf /f 2>/dev/null' | grep -cF -- "$NEEDLE" || true)
  [[ "$hits" == 0 ]] || return 1
  ! grep -qF -- "$NEEDLE" "$BACKUP_DIR/files/$1.caseal" "$BACKUP_DIR/logs/backup.log" "$OUT"
}
tar_count() { tools sh -c 'ls -1 /backups/files/files_*.tar.gz 2>/dev/null | wc -l' | tr -d '\r\n '; }
partial_count() { tools sh -c 'ls -1 /backups/files | grep -c partial || true' | tr -d '\r\n '; }
local_lacks() { [[ -z "$(tools sh -c 'ls -1 /backups/files | grep -F -- "$0" || true' "$1" | tr -d '\r')" ]]; }
no_secrets_in_outputs() {
  cat "$T"/run*.out "$BACKUP_DIR/logs/backup.log" >"$T/todo.txt"
  [[ $(grep -cF -- "$NEEDLE" "$T/todo.txt") == 0 && $(grep -cE 'PRIVATE KEY' "$T/todo.txt") == 0 ]] || return 1
  ! grep -qF -f <(grep -h 'PASSWORD' "$SEAL_ENV" "$OFFSITE_ENV" | sed 's/^[A-Z0-9_]*=//' | grep -v '^$') "$T/todo.txt"
}
tar_list() { tools sh -c 'tar -tzf "/backups/files/$0"' "$1" | tr -d '\r'; }
local_has() { tools sh -c 'test -e "/backups/files/$0"' "$1" 2>/dev/null; }
stamp_ago() { date -u -d "-$1 days" +%Y-%m-%d_%H%M%SZ; }

export -f dcq rc tools vols vhash vmode cl cl_code needle_hits tar_list local_has tar_count partial_count

# ---------------------------------------------------------------------------------------------------------- setup
section "setup: stack aislado ($PROJ), Caddy real con CA interna, datos sinteticos"
docker image inspect "$ALPINE_IMAGE" >/dev/null 2>&1 || docker pull -q "$ALPINE_IMAGE" >/dev/null || { echo "no se pudo obtener alpine"; exit 1; }
bash "$HERE/prepare-dir.sh" >/dev/null || { echo "prepare-dir.sh fallo"; exit 1; }
dcq up -d --wait db >"$T/up-db.out" 2>&1 || { echo "no se pudo levantar db"; cat "$T/up-db.out"; exit 1; }
[[ "$(dcq config 2>/dev/null | sed -n 's/^name: //p')" == "$PROJ" ]] || { echo "ABORTADO: el proyecto Compose resuelto no es $PROJ"; exit 1; }
sqlq "CREATE DATABASE [$DB];" >/dev/null || { echo "no se pudo crear la base sintetica"; exit 1; }
vols sh -c 'for d in keys uploads appdata; do mkdir -p /dst/$d/sub; head -c 20000 /dev/urandom >/dst/$d/dato1.bin; echo "sintetico $d" >/dst/$d/leeme.txt; done' || { echo "no se pudieron sembrar los volumenes"; exit 1; }
# offsite: rclone crypt sobre el backend local (/offsite-disk), como en docs/backup-restore.md §6.1
OBS_P1=$(rc obscure "$CRYPT_P1" | tr -d '\r\n'); OBS_P2=$(rc obscure "$CRYPT_P2" | tr -d '\r\n')
{ echo "$ENV_MARK"; echo "RCLONE_CONFIG_OFFSITE_TYPE=local"; echo "RCLONE_CONFIG_SECURE_TYPE=crypt"; echo "RCLONE_CONFIG_SECURE_REMOTE=offsite:/offsite-disk/erp"
  echo "RCLONE_CONFIG_SECURE_PASSWORD=$OBS_P1"; echo "RCLONE_CONFIG_SECURE_PASSWORD2=$OBS_P2"; } >"$OFFSITE_ENV"; chmod 600 "$OFFSITE_ENV"
rc mkdir secure:erp >/dev/null && rc touch secure:erp/centinela.txt >/dev/null || { echo "no se pudo crear el centinela"; exit 1; }
caddy_up
docker network inspect "${PROJ}_bury-net" >/dev/null 2>&1 || { echo "falta la red del proyecto"; exit 1; }
docker run -d --name "$CLIENT_C" --network "${PROJ}_bury-net" --label "com.docker.compose.project=$PROJ" "$ALPINE_IMAGE" sleep 3000 >/dev/null || exit 1
cl 'apk add --no-cache curl >/dev/null 2>&1' || { echo "no se pudo instalar curl en el cliente"; exit 1; }
wait_ca && ok "setup: Caddy genero su CA interna (root.crt + root.key) en el volumen" || { bad "setup: Caddy no genero la CA"; exit 1; }
vols sh -c "cat /dst/caddy-data/$CA_DIR/root.crt" >"$T/ca-orig.crt"
docker cp "$T/ca-orig.crt" "$CLIENT_C:/ca-orig.crt" >/dev/null
sleep 3
LEAF=$(leaf_path)
H_ROOTKEY=$(vhash "$CA_DIR/root.key"); H_INTKEY=$(vhash "$CA_DIR/intermediate.key"); H_ROOTCRT=$(vhash "$CA_DIR/root.crt"); H_INTCRT=$(vhash "$CA_DIR/intermediate.crt"); H_LEAF=$(vhash "$LEAF")
NEEDLE=$(vols sh -c "sed -n 2p /dst/caddy-data/$CA_DIR/root.key" | tr -d '\r\n')
[[ ${#NEEDLE} -ge 20 && -n "$H_ROOTKEY" && -n "$H_INTKEY" && -n "$H_LEAF" ]] || { bad "setup: no se pudieron leer claves/huellas"; exit 1; }
check "setup: el cliente confia en la CA original (TLS ok, 502 por ausencia de app)" test "$(cl_code /ca-orig.crt)" = 502
check "setup: root.key/intermediate.key son 0600 root" bash -c '[[ "$1" == "600 0" && "$2" == "600 0" ]]' _ "$(vmode "$CA_DIR/root.key")" "$(vmode "$CA_DIR/intermediate.key")"
info "root.crt=${H_ROOTCRT:0:12} root.key=${H_ROOTKEY:0:12} hoja=$LEAF"

# ---------------------------------------------------------------------------------------------------------- a
section "a. riesgo base (BACKUP_CA_KEY=include, comportamiento historico): la clave privada viaja en el tar"
export ALPINE_IMAGE CLIENT_C DOMAIN CA_DIR NEEDLE T BACKUP_DIR SEAL_ENV OFFSITE_ENV
export BACKUP_CA_KEY=include
run_script backup.sh all; expect_rc "a: backup all en modo include" 0
TAR_A=$(tools sh -c 'ls -1 /backups/files/files_*.tar.gz | head -n1 | xargs basename' | tr -d '\r\n')
check "a: el tar CONTIENE root.key e intermediate.key (riesgo documentado)" bash -c 'grep -q "authorities/local/root.key" <<<"$1" && grep -q "authorities/local/intermediate.key" <<<"$1"' _ "$(tar_list "$TAR_A")"
check "a: la clave en claro aparece dentro del tar local sin cifrar (hits=$(needle_hits "$BACKUP_DIR"))" bash -c '(( $1 >= 1 ))' _ "$(needle_hits "$BACKUP_DIR")"
check "a: no hay .caseal en modo include" not local_has "$TAR_A.caseal"
run_script check-custody.sh --no-status; check "a: check-custody avisa C12 (WARN, no falla) en modo include" out_has '^WARN C12 .*root.key'

section "a2. BACKUP_INCLUDE_CADDY=false ahora SI excluye caddy-data (antes el valor no llegaba al contenedor)"
sleep 1
BACKUP_INCLUDE_CADDY=false run_script backup.sh files; expect_rc "a2: backup files con BACKUP_INCLUDE_CADDY=false" 0
TAR_A2=$(tools sh -c 'ls -1 /backups/files/files_*.tar.gz | tail -n1 | xargs basename' | tr -d '\r
')
check "a2: el tar NO contiene caddy-data (ni la clave ni los certificados)" bash -c '! grep -q "caddy-data" <<<"$1"' _ "$(tar_list "$TAR_A2")"

# ---------------------------------------------------------------------------------------------------------- b
section "b. modo seal: la clave NO viaja en el tar ni queda en BACKUP_DIR; se sella aparte"
run_script ca-seal-init.sh --out "$BACKUP_DIR/ca-seal.env"; expect_rc "b: ca-seal-init rechaza una ruta DENTRO de BACKUP_DIR" 1
run_script ca-seal-init.sh --out "$SEAL_ENV"; expect_rc "b: ca-seal-init crea la clave de sellado fuera de BACKUP_DIR" 0
check "b: la clave de sellado es 0600" bash -c '[[ "$(stat -c %a "$1")" == 600 ]]' _ "$SEAL_ENV"
check "b: ca-seal-init NO imprimio los secretos" bash -c '! grep -qF -f <(sed -n "s/^[A-Z0-9_]*=//p" "$2") "$1"' _ "$OUT" "$SEAL_ENV"
run_script ca-seal-init.sh --out "$SEAL_ENV"; expect_rc "b: ca-seal-init NO sobrescribe una clave de sellado existente" 1
check "b: las dos passwords de sellado difieren entre si y de las del offsite" bash -c 'a=$(sed -n "s/^RCLONE_CONFIG_CASEAL_PASSWORD=//p" "$1"); b=$(sed -n "s/^RCLONE_CONFIG_CASEAL_PASSWORD2=//p" "$1"); [[ -n "$a" && "$a" != "$b" && "$a" != "$2" && "$b" != "$3" ]]' _ "$SEAL_ENV" "$OBS_P1" "$OBS_P2"
export BACKUP_CA_KEY=seal BACKUP_CA_SEAL_ENV="$SEAL_ENV"
sleep 1
run_script backup.sh all; expect_rc "b: backup all en modo seal" 0
TAR_B=$(tools sh -c 'ls -1 /backups/files/files_*.tar.gz | tail -n1 | xargs basename' | tr -d '\r\n')
LB=$(tar_list "$TAR_B")
check "b: el tar nuevo NO contiene ningun *.key de la CA" bash -c '! grep -E "authorities/.*\.key$" <<<"$1"' _ "$LB"
check "b: el tar conserva root.crt e intermediate.crt (recuperacion rapida de la CA publica)" bash -c 'grep -q "authorities/local/root.crt" <<<"$1" && grep -q "authorities/local/intermediate.crt" <<<"$1"' _ "$LB"
check "b: existe <tar>.caseal" local_has "$TAR_B.caseal"
check "b: el log informa sellado ok con 2 entradas" out_has 'ca_seal=ok .*entradas=2'
check "b: el .caseal es 0600" bash -c '[[ "$(tools sh -c "stat -c %a /backups/files/$1.caseal" | tr -d "\r\n")" == 600 ]]' _ "$TAR_B"
check "b: la clave privada NO aparece en NINGUN archivo del backup nuevo (tar, caseal, logs, salida)" no_key_in_new_backup "$TAR_B"
check "b: el .caseal no es texto PEM ni tar legible (cifrado)" bash -c '! grep -aqE "PRIVATE KEY|authorities|root\.key" "$1/files/$2.caseal"' _ "$BACKUP_DIR" "$TAR_B"
check "b: el .caseal se subio al offsite (descifrado por crypt: aparece junto al tar)" bash -c 'rc lsf secure:erp/files | tr -d "\r" | grep -qxF "$1.caseal"' _ "$TAR_B"
check "b: la clave de sellado NO esta dentro de BACKUP_DIR ni del offsite" bash -c '[[ -z "$(find "$1" "$2" -name "ca-seal*" 2>/dev/null)" ]]' _ "$BACKUP_DIR" "$T/offsite"
check "b: el tar del modo include anterior sigue en BACKUP_DIR (riesgo heredado: migrar = purgar esos tars; hits=$(needle_hits "$BACKUP_DIR"))" bash -c '(( $1 >= 1 ))' _ "$(needle_hits "$BACKUP_DIR")"
run_script check-custody.sh --no-status; check "b: check-custody C12 ya no avisa del tar (sellado)" bash -c '! grep -qE "^WARN C12" "$1"' _ "$OUT"

# ---------------------------------------------------------------------------------------------------------- c
section "c. fail-closed: sin clave de sellado NO se degrada a include"
NBEFORE=$(tar_count)
BACKUP_CA_SEAL_ENV="$T/seal/no-existe.env" run_script backup.sh files; expect_rc "c: seal sin BACKUP_CA_SEAL_ENV legible falla" 5
check "c: mensaje explicito (BACKUP_CA_SEAL_ENV) y NO se creo ningun tar nuevo" bash -c 'grep -q "BACKUP_CA_SEAL_ENV" "$1" && [[ "$(tar_count)" == "$2" ]]' _ "$OUT" "$NBEFORE"
sleep 1
printf 'RCLONE_CONFIG_CASEAL_PASSWORD=\n' >"$T/seal/vacia.env"; chmod 600 "$T/seal/vacia.env"
BACKUP_CA_SEAL_ENV="$T/seal/vacia.env" run_script backup.sh files; expect_rc "c: clave de sellado incompleta: el backup falla y se descarta" 5
check "c: no quedo el tar sin su sellado (ni .partial)" bash -c '[[ "$(partial_count)" == 0 && "$(tar_count)" == "$1" ]]' _ "$NBEFORE"
BACKUP_CA_SEAL_ENV="$BACKUP_DIR/dentro.env" run_script backup.sh files; expect_rc "c: BACKUP_CA_SEAL_ENV dentro de BACKUP_DIR se rechaza (config invalida)" 1

# ---------------------------------------------------------------------------------------------------------- d
section "d. PERDIDA TOTAL del servidor: volumenes y BACKUP_DIR eliminados; recuperacion desde la copia externa + custodia"
dcq rm -sf caddy >/dev/null 2>&1
dcq down -v >/dev/null 2>&1 || true
docker volume ls -q --filter "label=com.docker.compose.project=$PROJ" | grep -E "caddy-data|bury-keys|bury-uploads|bury-appdata" | xargs -r docker volume rm >/dev/null 2>&1
docker run --rm --network none -v "$BACKUP_DIR:/b" "$ALPINE_IMAGE" sh -c 'find /b/files /b/sql /b/logs -mindepth 1 -delete' >/dev/null 2>&1
check "d: BACKUP_DIR local sin tars ni .caseal (servidor perdido)" bash -c '[[ -z "$(find "$1/files" -type f 2>/dev/null)" ]]' _ "$BACKUP_DIR"
dcq up -d --wait db >/dev/null 2>&1   # un servidor nuevo trae su SQL; los volumenes de archivos se recrean vacios
run_script fetch-offsite.sh; expect_rc "d: fetch-offsite descarga y verifica desde la copia externa" 0
check "d: el .caseal volvio con la descarga" local_has "$TAR_B.caseal"
dcq rm -sf caddy >/dev/null 2>&1
run_script restore-files.sh --archive "$TAR_B" --force; expect_rc "d: restore-files con la custodia correcta" 0
check "d: informa la reposicion de la clave de la CA" out_has 'clave de la CA repuesta'
check "d: root.key identica a la original" test "$(vhash "$CA_DIR/root.key")" = "$H_ROOTKEY"
check "d: intermediate.key identica a la original" test "$(vhash "$CA_DIR/intermediate.key")" = "$H_INTKEY"
check "d: root.key e intermediate.key 0600 root" bash -c '[[ "$1" == "600 0" && "$2" == "600 0" ]]' _ "$(vmode "$CA_DIR/root.key")" "$(vmode "$CA_DIR/intermediate.key")"
check "d: root.crt/intermediate.crt/hoja identicos a los originales" bash -c '[[ "$(vhash "$CA_DIR/root.crt")" == "$1" && "$(vhash "$CA_DIR/intermediate.crt")" == "$2" && "$(vhash "$3")" == "$4" ]]' _ "$H_ROOTCRT" "$H_INTCRT" "$LEAF" "$H_LEAF"
check "d: keys/uploads/appdata restaurados (3 volumenes con su contenido)" bash -c '[[ "$(vols sh -c "cat /dst/keys/leeme.txt /dst/uploads/leeme.txt /dst/appdata/leeme.txt" | tr -d "\r" | tr "\n" ";")" == "sintetico keys;sintetico uploads;sintetico appdata;" ]]'
caddy_up; sleep 6
check "d: Caddy arranca con la CA restaurada y el cliente sigue confiando en la CA ORIGINAL (sin tocar a los clientes)" test "$(cl_code /ca-orig.crt)" = 502
check "d: la clave restaurada no aparece en la salida del restore ni en backup.log" bash -c '[[ $(grep -cF -- "$3" "$1" "$2/logs/backup.log" | awk -F: "{s+=\$NF} END{print s+0}") == 0 ]]' _ "$OUT" "$BACKUP_DIR" "$NEEDLE"

# ---------------------------------------------------------------------------------------------------------- e
section "e. restauracion SIN la clave (sin .caseal): Caddy no sirve; con el .caseal repuesto vuelve"
dcq rm -sf caddy >/dev/null 2>&1
BLOB="$BACKUP_DIR/files/$TAR_B.caseal"
mv "$BLOB" "$T/blob.guardado"
run_script restore-files.sh --archive "$TAR_B" --force; expect_rc "e: restore-files sin .caseal restaura el resto pero FALLA (la CA quedo incompleta)" 9
check "e: explica root.crt SIN root.key y como seguir" out_has 'SIN root.key'
check "e: los datos SI se restauraron (keys/uploads/appdata)" bash -c '[[ "$(vols sh -c "cat /dst/keys/leeme.txt" | tr -d "\r")" == "sintetico keys" ]]'
run_script restore-files.sh --archive "$TAR_B" --force --skip-ca-key; expect_rc "e: con --skip-ca-key (intencional) el restore termina OK" 0
check "e: la clave NO esta en el volumen" test -z "$(vhash "$CA_DIR/root.key")"
caddy_up; sleep 8
check "e: sin root.key Caddy NO sirve TLS (000): excluir la clave sin custodia seria un outage" test "$(cl_code /ca-orig.crt)" = 000
dcq rm -sf caddy >/dev/null 2>&1
mv "$T/blob.guardado" "$BLOB"
run_script restore-ca-key.sh --archive "$TAR_B"; expect_rc "e: restore-ca-key con el .caseal y la custodia correcta" 0
check "e: root.key vuelve identica" test "$(vhash "$CA_DIR/root.key")" = "$H_ROOTKEY"
caddy_up; sleep 6
check "e: Caddy vuelve a servir con la CA original" test "$(cl_code /ca-orig.crt)" = 502

# ---------------------------------------------------------------------------------------------------------- f
section "f. clave de sellado incorrecta, blob alterado, blob malicioso, sin clave de sellado"
dcq rm -sf caddy >/dev/null 2>&1
vols sh -c "rm -f /dst/caddy-data/$CA_DIR/root.key /dst/caddy-data/$CA_DIR/intermediate.key"
bash "$HERE/ca-seal-init.sh" --out "$T/seal/otra.env" >/dev/null 2>&1
BACKUP_CA_SEAL_ENV="$T/seal/otra.env" run_script restore-ca-key.sh --archive "$TAR_B"; expect_rc "f: clave de sellado INCORRECTA" 9
check "f: mensaje: clave incorrecta o archivo alterado" out_has 'clave de sellado incorrecta o archivo corrupto'
check "f: NO se escribio ninguna clave" test -z "$(vhash "$CA_DIR/root.key")$(vhash "$CA_DIR/intermediate.key")"
check "f: las passwords de la clave incorrecta no aparecen en la salida" bash -c '! grep -qF -f <(sed -n "s/^[A-Z0-9_]*=//p" "$2") "$1"' _ "$OUT" "$T/seal/otra.env"
BACKUP_CA_SEAL_ENV="$T/seal/otra.env" run_script restore-files.sh --archive "$TAR_B" --force; expect_rc "f: restore-files con clave incorrecta: restaura archivos pero FALLA e informa" 9
check "f: indica como seguir (restore-ca-key o rotar la CA)" out_has 'restore-ca-key.sh|rotar la CA'
BACKUP_CA_SEAL_ENV= run_script restore-ca-key.sh --archive "$TAR_B"; expect_rc "f: sin BACKUP_CA_SEAL_ENV" 9
check "f: mensaje: falta la custodia" out_has 'sin la clave de sellado'
# blob alterado: un byte en el medio
docker run --rm --network none -v "$BACKUP_DIR/files:/f" "$ALPINE_IMAGE" sh -c "cp -p /f/$TAR_B.caseal /f/$TAR_B.caseal.orig"
# se cambia el byte 40 por OTRO valor (v+1): determinista (nunca queda igual) y dentro del primer bloque autenticado de rclone crypt
docker run --rm --network none -e F="/f/$TAR_B.caseal" -v "$BACKUP_DIR/files:/f" "$ALPINE_IMAGE" sh -c 'cur=$(od -An -tx1 -j40 -N1 "$F" | tr -d " "); c=A; [ "$cur" = 41 ] && c=B; printf %s "$c" | dd of="$F" bs=1 seek=40 conv=notrunc 2>/dev/null'
info "bytes 32..48 del .caseal tras alterar: $(tools sh -c "od -An -tx1 -j32 -N16 /backups/files/$TAR_B.caseal" | tr -d "
")"
run_script restore-ca-key.sh --archive "$TAR_B"; expect_rc "f: blob ALTERADO (1 byte) se rechaza (cifrado autenticado)" 9
check "f: nada escrito tras el blob alterado" test -z "$(vhash "$CA_DIR/root.key")"
docker run --rm --network none -v "$BACKUP_DIR/files:/f" "$ALPINE_IMAGE" sh -c "mv -f /f/$TAR_B.caseal.orig /f/$TAR_B.caseal"
# blobs maliciosos, sellados con la clave de sellado REAL (el atacante controla el contenido del tar, no la clave): ruta fuera, enlace
mk_evil() { # mk_evil <nombre> <comando shell que crea el contenido en /tmp/w>
  dcq run --rm -T --no-deps --quiet-pull ca-seal sh -c "mkdir /tmp/w && cd /tmp/w && $2 && tar -cf - * | rclone rcat caseal:$1.caseal" </dev/null
}
EV2=files_2099-01-01_000002Z.tar.gz; EV3=files_2099-01-01_000003Z.tar.gz; EV4=files_2099-01-01_000004Z.tar.gz
mk_evil "$EV2" 'mkdir -p caddy/pki/authorities/local && ln -s /etc/passwd caddy/pki/authorities/local/root.key' >/dev/null 2>&1
mk_evil "$EV3" 'mkdir -p caddy/pki/authorities/local && echo X >caddy/pki/authorities/local/otro.txt' >/dev/null 2>&1
mk_evil "$EV4" 'mkdir -p etc && echo X >etc/evil.key' >/dev/null 2>&1
check "f: los blobs maliciosos se crearon (3)" bash -c '[[ "$(tools sh -c "ls /backups/files/files_2099-*.caseal | wc -l" | tr -d "\r\n ")" == 3 ]]'
for ev in "$EV2" "$EV3" "$EV4"; do
  run_script restore-ca-key.sh --archive "$ev"; expect_rc "f: blob malicioso $ev (enlace / extension no .key / ruta ajena) se rechaza" 9
done
check "f: los rechazos explican que el contenido es inesperado" bash -c 'grep -lE "no son archivos regulares|rutas inesperadas" "$1"/run*.out >/dev/null' _ "$T"
check "f: el volumen no recibio nada de los blobs maliciosos" test -z "$(vhash "$CA_DIR/root.key")$(vhash "$CA_DIR/otro.txt")"
run_script restore-ca-key.sh --archive "$TAR_B" --dry-run; expect_rc "f: --dry-run con la custodia correcta valida sin escribir" 0
check "f: --dry-run no escribio nada" test -z "$(vhash "$CA_DIR/root.key")"
# CA incompleta (root.crt sin claves, como tras un restore incompleto): seal NO la acepta como respaldo valido
NB2=$(tar_count)
sleep 1
run_script backup.sh files; expect_rc "f: backup seal con CA INCOMPLETA (certificados sin claves) falla" 5
check "f: explica CA incompleta y descarta el tar" bash -c 'grep -q "CA incompleta" "$1" && [[ "$(tar_count)" == "$2" ]]' _ "$OUT" "$NB2"
# enlace simbolico plantado en el destino (tar de archivos manipulado): la clave NO debe salir del area excluida
vols sh -c "ln -s /dst/caddy-data/filtrada.txt /dst/caddy-data/$CA_DIR/root.key.tmp.key"
run_script restore-ca-key.sh --archive "$TAR_B"; expect_rc "f: enlace simbolico en el temporal del destino: se rechaza" 9
check "f: la clave NO se escribio en filtrada.txt ni en ninguna parte" bash -c '[[ -z "$(vhash filtrada.txt)" && -z "$(vhash "$1/root.key")" ]]' _ "$CA_DIR"
vols sh -c "rm -f /dst/caddy-data/$CA_DIR/root.key.tmp.key; mkdir -p /dst/caddy-data/real; rm -rf /dst/caddy-data/$CA_DIR; ln -s /dst/caddy-data/real /dst/caddy-data/$CA_DIR"
run_script restore-ca-key.sh --archive "$TAR_B"; expect_rc "f: directorio padre que es un enlace simbolico: se rechaza" 9
check "f: nada se escribio a traves del enlace" test -z "$(vols sh -c 'ls /dst/caddy-data/real' | tr -d '\r')"
vols sh -c "rm -f /dst/caddy-data/$CA_DIR; mkdir -p /dst/caddy-data/$CA_DIR; rmdir /dst/caddy-data/real"
docker run --rm --network none -v "$BACKUP_DIR/files:/f" "$ALPINE_IMAGE" sh -c "rm -f /f/files_2099-*" >/dev/null 2>&1

# ---------------------------------------------------------------------------------------------------------- h
section "h. idempotencia, --force y Caddy en marcha"
run_script restore-ca-key.sh --archive "$TAR_B"; expect_rc "h: reposicion normal" 0
check "h: claves identicas" test "$(vhash "$CA_DIR/root.key")$(vhash "$CA_DIR/intermediate.key")" = "$H_ROOTKEY$H_INTKEY"
run_script restore-ca-key.sh --archive "$TAR_B"; expect_rc "h: repetirla es idempotente (misma clave: sin cambios)" 0
check "h: repuestas=0 en la repeticion" out_has 'repuestas=0'
vols sh -c "echo clave-distinta >/dst/caddy-data/$CA_DIR/root.key"
run_script restore-ca-key.sh --archive "$TAR_B"; expect_rc "h: una clave DISTINTA presente no se pisa sin --force" 9
check "h: la clave distinta sigue ahi" test "$(vhash "$CA_DIR/root.key")" != "$H_ROOTKEY"
run_script restore-ca-key.sh --archive "$TAR_B" --force; expect_rc "h: --force la reemplaza" 0
check "h: root.key de nuevo identica" test "$(vhash "$CA_DIR/root.key")" = "$H_ROOTKEY"
caddy_up; sleep 6
run_script restore-ca-key.sh --archive "$TAR_B"; expect_rc "h: con Caddy corriendo solo se admite --dry-run" 9
run_script restore-ca-key.sh --archive "$TAR_B" --dry-run; expect_rc "h: --dry-run con Caddy corriendo" 0

# ---------------------------------------------------------------------------------------------------------- g
section "g. otras claves/certificados intactos; ningun secreto en salidas"
check "g: la clave de la hoja y su certificado siguen identicos (no se tocan)" test "$(vhash "$LEAF")" = "$H_LEAF"
check "g: el tar conserva la clave de la hoja (se regenera, no firma a nadie mas): documentado" bash -c 'grep -q "certificates/local/.*\.key" <<<"$1"' _ "$LB"
check "g: el cliente sigue confiando (TLS ok con la CA original)" test "$(cl_code /ca-orig.crt)" = 502
check "g: ninguna salida de scripts ni backup.log contiene la clave privada ni las passwords de sellado/offsite" no_secrets_in_outputs
check "g: ningun script pasa secretos de sellado por argv (los valores solo se escriben en el archivo 0600)" bash -c '! grep -nE "CASEAL_PASSWORD2?=[^\"\$ ]" "$1"/scripts/backup/*.sh "$1"/docker-compose.yml | grep -v "ca-seal-init.sh" | grep -v "^[^:]*:[0-9]*:\s*#" | grep -v "test-ca-key"' _ "$REPO"

# ---------------------------------------------------------------------------------------------------------- i
section "i. retencion: el .caseal se va con su tar (local y remoto)"
for d in 90 80 70 60; do tools sh -c 'cd /backups/files; n="$0"; head -c 2048 /dev/urandom >"$n"; sha256sum "$n" >"$n.sha256"; head -c 300 /dev/urandom >"$n.caseal"' "files_$(stamp_ago $d).tar.gz"; done
OLD=$(stamp_ago 90)
RMT=()   # tars sinteticos viejos EN EL REMOTO (principal + .sha256 + .caseal), para ejercitar la retencion remota con crypt
for d in 85 75; do
  n="files_$(stamp_ago $d).tar.gz"; RMT+=("$n")
  head -c 200 /dev/urandom | dcq run --rm -T --no-deps backup-offsite rcat "secure:erp/files/$n" >/dev/null 2>&1
  head -c 200 /dev/urandom | dcq run --rm -T --no-deps backup-offsite rcat "secure:erp/files/$n.sha256" >/dev/null 2>&1
  head -c 200 /dev/urandom | dcq run --rm -T --no-deps backup-offsite rcat "secure:erp/files/$n.caseal" >/dev/null 2>&1
done
check "i: el remoto tiene ahora los 2 tars sinteticos viejos con su .caseal" bash -c '[[ "$(rc lsf secure:erp/files | tr -d "\r" | grep -c "caseal$")" -ge 3 ]]'
run_script backup.sh files; expect_rc "i: backup con retencion local y remota" 0
DEL=$(rc lsf secure:erp/files | tr -d '\r')
for n in "${RMT[@]}"; do
  check "i: remoto: $n y su .sha256 y su .caseal fueron BORRADOS por la retencion" bash -c '! grep -qF -- "$1" <<<"$2"' _ "$n" "$DEL"
done
check "i: el tar sintetico de 90 dias y su .caseal fueron borrados localmente" local_lacks "$OLD"
check "i: los recientes y sus .caseal siguen" local_has "$TAR_B.caseal"
DEL=$(rc lsf secure:erp/files | tr -d '\r')
check "i: el remoto conserva el .caseal del backup real (hay $(grep -c caseal <<<"$DEL") .caseal)" bash -c 'grep -qxF "$1.caseal" <<<"$2"' _ "$TAR_B" "$DEL"

# ---------------------------------------------------------------------------------------------------------- j
section "j. check-custody"
run_script check-custody.sh --no-status --quiet; check "j: modo seal con BACKUP_CA_SEAL_ENV 0600 fuera de BACKUP_DIR: sin FAIL" bash -c '! grep -q "^FAIL" "$1"' _ "$OUT" || { grep "^FAIL" "$OUT" | cut -c1-250 | sed "s/^/     | /"; }
chmod 644 "$SEAL_ENV"; run_script check-custody.sh --no-status --quiet; check "j: C13 FALLA si la clave de sellado no es 0600" out_has '^FAIL C13'
chmod 600 "$SEAL_ENV"
BACKUP_CA_SEAL_ENV="$T/seal/no-existe.env" run_script check-custody.sh --no-status --quiet; check "j: C13 FALLA con seal sin clave de sellado legible" out_has '^FAIL C13'
sed "s/RCLONE_CONFIG_SECURE_/RCLONE_CONFIG_CASEAL_/" "$OFFSITE_ENV" >"$T/seal/mismo.env"; chmod 600 "$T/seal/mismo.env"
BACKUP_CA_SEAL_ENV="$T/seal/mismo.env" run_script check-custody.sh --no-status --quiet; check "j: C13 FALLA si la clave de sellado es el propio backup-offsite.env (no hay independencia)" out_has '^FAIL C13'

# el remoto activo NO se llama "secure": C13 debe comparar contra SU password (BACKUP_REMOTE=archivo:... -> RCLONE_CONFIG_ARCHIVO_*)
printf 'RCLONE_CONFIG_ARCHIVO_TYPE=crypt
RCLONE_CONFIG_ARCHIVO_PASSWORD=%s
RCLONE_CONFIG_ARCHIVO_PASSWORD2=%s
' "$OBS_P1" "$OBS_P2" >"$T/seal/otro-offsite.env"; chmod 600 "$T/seal/otro-offsite.env"
BURY_OFFSITE_ENV_FILE="$T/seal/otro-offsite.env" BACKUP_REMOTE='archivo:erp' BACKUP_CA_SEAL_ENV="$T/seal/mismo.env" run_script check-custody.sh --no-status --quiet
check "j: C13 compara contra el remoto crypt ACTIVO aunque no se llame secure" out_has '^FAIL C13 .*remoto crypt activo'

# ---------------------------------------------------------------------------------------------------------- k
section "k. restore de un tar SIN .caseal con una autoridad distinta de local y sin clave: tambien falla (no solo local/root.key)"
dcq rm -sf caddy >/dev/null 2>&1
EVK=files_2099-02-01_000001Z.tar.gz
docker run --rm --network none -v "$BACKUP_DIR/files:/f" "$ALPINE_IMAGE" sh -c 'mkdir -p /w/keys /w/uploads /w/appdata /w/caddy-data/caddy/pki/authorities/otra && echo x >/w/caddy-data/caddy/pki/authorities/otra/root.crt && tar -czf /f/'$EVK' -C /w keys uploads appdata caddy-data && cd /f && sha256sum '$EVK' >'$EVK'.sha256'
run_script restore-files.sh --archive "$EVK" --force; expect_rc "k: restore-files de un tar con la CA 'otra' (root.crt sin clave) y sin .caseal falla" 9
check "k: el mensaje explica la clave que falta" out_has 'SIN root.key'
run_script restore-files.sh --archive "$EVK" --force --skip-ca-key; expect_rc "k: con --skip-ca-key es una decision explicita y termina OK" 0

echo
echo "ca-key custody: $PASS PASS, $FAIL FAIL"
(( FAIL == 0 )) || { printf 'FALLARON:\n'; printf '  - %s\n' "${FAILED[@]}"; }
(( FAIL == 0 ))
