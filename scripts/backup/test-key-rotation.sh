#!/usr/bin/env bash
# ENSAYO de rotacion de las passwords crypt (P1/P2) de la copia externa, AISLADO y auto-limpiante: usa el rotate-crypt.sh y el
# fetch-offsite.sh REALES contra un servidor SFTP DESCARTABLE (atmoz/sftp fijado por digest) con rclone crypt encima, datos 100% SINTETICOS.
# No necesita SQL Server (la rotacion solo concierne al remoto cifrado). Detalle y resultados: docs/backup-dr-rotacion-claves.md
#
#   bash scripts/backup/test-key-rotation.sh [--keep]        (exit 0 = todo PASS)
#
# Escenarios: r1 respaldo cifrado con la clave anterior y recuperable | r2 origen con password incorrecta: aborta sin escribir
# | r3 rotacion INTERRUMPIDA (docker kill a mitad de la copia): el origen intacto, el destino parcial, reanudable
# | r4 rotacion completa + verificacion; los respaldos viejos NO se destruyen (arbol del servidor identico byte a byte)
# | r5 recuperacion de respaldos viejos y NUEVOS con la clave nueva; un respaldo posterior a la rotacion
# | r6 password nueva incorrecta: la verificacion y la recuperacion fallan claro, sin ensuciar el destino local
# | r7 rollback a la clave anterior | r8 validaciones de uso y garantia estatica "nunca borra" | transversal: ningun secreto en las salidas.
# NUNCA toca el stack real: proyecto Compose, red, volumenes y contenedor SFTP con nombre unico (bury-p06rot-<rand>); todo se elimina al terminar.
# Escribe ./backup-offsite.env (sintetico, con marca) y lo borra al salir; si ya existe uno que no es de este arnes, ABORTA sin tocarlo.
set -uo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(cd "$HERE/../.." && pwd)"
cd "$REPO"

SFTP_IMAGE='atmoz/sftp@sha256:c5fb9e4beb8b7054a4c0b65c5e11df47cc20da95ad6002e88aecff62bab06372'
ALPINE_IMAGE='alpine:3@sha256:294b683cb724975bec92580e1e685676bd4b50bda910ddb8c51d4cabeaec77e6'
OFFSITE_ENV="$REPO/backup-offsite.env"
ENV_MARK='# generado por scripts/backup/test-key-rotation.sh (sintetico, se borra al terminar)'
SFTP_PORT=2222 SFTP_HOST=sftp-test SFTP_USER=burytest DB=BuryRot

KEEP=0
while (( $# )); do
  case "$1" in
    --keep) KEEP=1; shift ;;
    -h|--help) sed -n '2,15p' "$0"; exit 0 ;;
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
PROJ="bury-p06rot-$RUN"
T=$(mktemp -d "${TMPDIR:-/tmp}/bury-p06rot.XXXXXX") || { echo "mktemp fallo" >&2; exit 1; }
[[ -n "$T" && -d "$T" ]] || { echo "directorio temporal invalido" >&2; exit 1; }
chmod 700 "$T" || exit 1
SFTP_C="$PROJ-sftp"
rnd() { od -An -N"${1:-12}" -tx1 /dev/urandom | tr -d ' \n'; }
free_subnet() {
  local used n i; used=$(docker network ls -q | xargs -r docker network inspect --format '{{range .IPAM.Config}}{{.Subnet}} {{end}}' 2>/dev/null)
  for i in $(seq 1 60); do n=$((100 + RANDOM % 120)); [[ " $used " == *" 172.28.$n.0/24 "* ]] || { echo "172.28.$n.0/24"; return; }; done
  echo "172.28.99.0/24"
}

SFTP_PASS=$(rnd 12)
P1_OLD=$(rnd 18); P2_OLD=$(rnd 18); P1_NEW=$(rnd 18); P2_NEW=$(rnd 18); P1_BAD=$(rnd 18)
export COMPOSE_PROJECT_NAME="$PROJ" COMPOSE_PATH_SEPARATOR=':' COMPOSE_FILE="docker-compose.yml:docker-compose.offsite-sftp.yml"
export BURY_NET_SUBNET; BURY_NET_SUBNET=$(free_subnet)
# Variables obligatorias de Compose (sinteticas; no hay SQL Server en este ensayo, pero Compose interpola el archivo entero).
export MSSQL_SA_PASSWORD="Aa1!$(rnd 10)" MSSQL_DATABASE="$DB" ADMIN_EMAIL=p06@example.invalid ERP_DB_USER=p06app ERP_DB_PASSWORD="Bb2!$(rnd 8)" ERP_MIGRATION_USER=p06mig ERP_MIGRATION_PASSWORD="Cc3!$(rnd 8)" ERP_DOMAIN=p06.invalid
export BACKUP_DIR="$T/backups" BACKUP_OFFSITE_SSH_DIR="$T/ssh"
export BACKUP_REMOTE='secure:erp' BACKUP_OFFSITE_MARKER=centinela.txt
mkdir -p "$T/ssh" "$T/sftp.d" "$BACKUP_DIR/sql" "$BACKUP_DIR/files" "$BACKUP_DIR/logs" && chmod 700 "$T/ssh" "$BACKUP_DIR" || { echo "no se pudo preparar $T" >&2; exit 1; }
echo "Directorio administrado por scripts/backup. NO borrar este archivo." >"$BACKUP_DIR/.bury-backup-root"
: >"$T/empty.env"; export COMPOSE_ENV_FILES="$T/empty.env"

PASS=0 FAIL=0 FAILED=()
ok()  { PASS=$((PASS+1)); echo "PASS $1"; }
bad() { FAIL=$((FAIL+1)); FAILED+=("$1"); echo "FAIL $1"; [[ -n "${2:-}" ]] && echo "     -> $2"; }
check() { local name=$1; shift; if "$@"; then ok "$name"; else bad "$name"; fi; }
info() { echo "INFO $*"; }
section() { echo; echo "=== $* ==="; }

cleanup() {
  local ec=$?
  if (( KEEP )); then
    echo "--keep: NO se limpio. Proyecto=$PROJ tmp=$T contenedor=$SFTP_C"; return
  fi
  echo; echo "--- limpieza ($PROJ) ---"
  docker rm -f "$SFTP_C" >/dev/null 2>&1 || true
  docker ps -aq --filter "label=com.docker.compose.project=$PROJ" | xargs -r docker rm -f >/dev/null 2>&1 || true
  docker compose --progress quiet down -v --remove-orphans >/dev/null 2>&1 || true
  docker volume ls -q --filter "label=com.docker.compose.project=$PROJ" | xargs -r docker volume rm >/dev/null 2>&1 || true
  docker network ls -q --filter "label=com.docker.compose.project=$PROJ" | xargs -r docker network rm >/dev/null 2>&1 || true
  docker run --rm --network none -v "$T:/t" "$ALPINE_IMAGE" find /t -mindepth 1 -delete >/dev/null 2>&1 || true
  rm -rf "$T" 2>/dev/null || true
  own_env && rm -f "$OFFSITE_ENV"
  echo "residuos: contenedores=$(docker ps -aq --filter "label=com.docker.compose.project=$PROJ" | wc -l) volumenes=$(docker volume ls -q --filter "label=com.docker.compose.project=$PROJ" | wc -l) tmp=$([[ -e $T ]] && echo si || echo no)"
  exit "$ec"   # conservar el codigo de salida del arnes (un FAIL debe verse en CI)
}
trap cleanup EXIT
trap 'exit 130' INT TERM

dcq() { docker compose --progress quiet "$@"; }
[[ "$(dcq config 2>/dev/null | sed -n 's/^name: //p')" == "$PROJ" ]] || { echo "ABORTADO: el proyecto Compose resuelto no es $PROJ"; exit 1; }
rc()    { dcq run --rm -T --no-deps --quiet-pull backup-offsite "$@" </dev/null; }
tools() { dcq run --rm -T --no-deps --quiet-pull backup-files "$@" </dev/null; }
srv()   { docker exec "$SFTP_C" sh -c "$@"; }
obscure() { rc obscure "$1" | tr -d '\r\n'; }
N=0 OUT="" RCODE=0
run_script() { local s=$1; shift; N=$((N+1)); OUT="$T/run$N.out"; bash "$HERE/$s" "$@" >"$OUT" 2>&1; RCODE=$?; }
out_has() { grep -qE -- "$1" "$OUT"; }
show_out() { echo "     --- ultimas lineas de $OUT ---"; tail -n 8 "$OUT" | cut -c1-300 | sed 's/^/     | /'; }
expect_rc() { if [[ "$RCODE" == "$2" ]]; then ok "$1 (exit $2)"; else bad "$1" "exit=$RCODE, esperado $2"; show_out; fi; }
tree_hash() { srv "cd /home/burytest/$1 && find . -type f -exec sha256sum {} + | sort | sha256sum" | tr -d '\r' | cut -d' ' -f1; }   # arbol del servidor (nombres CIFRADOS + contenido); $1 = upload (viejo) | upload2 (nuevo)
tree_files() { srv "find /home/burytest/$1 -type f 2>/dev/null | wc -l" | tr -d '\r\n'; }
local_hash() { tools sh -c 'cd /backups && find sql files -type f ! -name "*.sha256" -exec sha256sum {} + | sort | sha256sum' | tr -d '\r' | cut -d' ' -f1; }
wipe_local() { tools sh -c 'find /backups/sql /backups/files -mindepth 1 -delete'; }

# remotos: secure (viejo, raiz upload) y secure2 (nuevo, raiz upload2: otro arbol en el mismo servidor). Overrides: P1_NEW_ENV, BWLIMIT.
write_env() {
  {
    echo "$ENV_MARK"
    echo "RCLONE_CONFIG_OFFSITE_TYPE=sftp"; echo "RCLONE_CONFIG_OFFSITE_HOST=$SFTP_HOST"; echo "RCLONE_CONFIG_OFFSITE_PORT=$SFTP_PORT"
    echo "RCLONE_CONFIG_OFFSITE_USER=$SFTP_USER"; echo "RCLONE_CONFIG_OFFSITE_PASS=$OBS_SFTP"
    echo "RCLONE_CONFIG_OFFSITE_KNOWN_HOSTS_FILE=/config/known_hosts"
    echo "RCLONE_CONFIG_SECURE_TYPE=crypt"; echo "RCLONE_CONFIG_SECURE_REMOTE=offsite:upload"
    echo "RCLONE_CONFIG_SECURE_PASSWORD=${P1_OLD_ENV:-$OBS_P1_OLD}"; echo "RCLONE_CONFIG_SECURE_PASSWORD2=$OBS_P2_OLD"
    echo "RCLONE_CONFIG_SECURE2_TYPE=crypt"; echo "RCLONE_CONFIG_SECURE2_REMOTE=offsite:upload2"
    echo "RCLONE_CONFIG_SECURE2_PASSWORD=${P1_NEW_ENV:-$OBS_P1_NEW}"; echo "RCLONE_CONFIG_SECURE2_PASSWORD2=$OBS_P2_NEW"
    echo "RCLONE_CONFIG_SECURE3_TYPE=crypt"; echo "RCLONE_CONFIG_SECURE3_REMOTE=offsite:upload3"   # "rotacion" con las MISMAS passwords que el viejo (error de operador)
    echo "RCLONE_CONFIG_SECURE3_PASSWORD=${P1_OLD_ENV:-$OBS_P1_OLD}"; echo "RCLONE_CONFIG_SECURE3_PASSWORD2=$OBS_P2_OLD"
    [[ -z "${BWLIMIT:-}" ]] || echo "RCLONE_BWLIMIT=$BWLIMIT"   # rclone lee RCLONE_* como flags: ralentiza la copia para interrumpirla de forma determinista
  } >"$OFFSITE_ENV"
  chmod 600 "$OFFSITE_ENV"
}
reset_env() { unset P1_OLD_ENV P1_NEW_ENV BWLIMIT; write_env; }

# ---------------------------------------------------------------------------------------------------------- setup
section "setup: servidor SFTP descartable ($PROJ), dos juegos de passwords crypt sinteticos y respaldos 'viejos'"
docker image inspect "$SFTP_IMAGE" >/dev/null 2>&1 || docker pull -q "$SFTP_IMAGE" >/dev/null || { echo "no se pudo obtener $SFTP_IMAGE"; exit 1; }
ssh-keygen -q -t ed25519 -N '' -C p06rot -f "$T/ssh/host_ed25519"; ssh-keygen -q -t rsa -b 3072 -N "" -C p06rot-rsa -f "$T/ssh/host_rsa"
: >"$T/ssh/known_hosts"; for p in "$T/ssh/host_ed25519.pub" "$T/ssh/host_rsa.pub"; do echo "[$SFTP_HOST]:$SFTP_PORT $(cut -d" " -f1,2 "$p")" >>"$T/ssh/known_hosts"; done
printf '#!/bin/sh\necho "Port %s" >> /etc/ssh/sshd_config\n' "$SFTP_PORT" >"$T/sftp.d/10-port.sh"; chmod 755 "$T/sftp.d/10-port.sh"
rc version >/dev/null 2>&1   # crea la red del proyecto
docker run -d --name "$SFTP_C" --network "${PROJ}_bury-net" --network-alias "$SFTP_HOST" --label "com.docker.compose.project=$PROJ" \
  -v "$T/ssh/host_ed25519:/etc/ssh/ssh_host_ed25519_key:ro" -v "$T/ssh/host_rsa:/etc/ssh/ssh_host_rsa_key:ro" -v "$T/sftp.d:/etc/sftp.d:ro" \
  "$SFTP_IMAGE" "$SFTP_USER:$SFTP_PASS:::upload,upload2,upload3" >/dev/null || { echo "no se pudo iniciar el servidor SFTP"; exit 1; }
for i in $(seq 1 30); do docker logs "$SFTP_C" 2>&1 | grep -q "Server listening on 0.0.0.0 port $SFTP_PORT" && break; sleep 1; done
docker logs "$SFTP_C" 2>&1 | grep -q "Server listening on 0.0.0.0 port $SFTP_PORT" || { echo "el servidor SFTP no quedo escuchando"; exit 1; }
OBS_SFTP=$(obscure "$SFTP_PASS"); OBS_P1_OLD=$(obscure "$P1_OLD"); OBS_P2_OLD=$(obscure "$P2_OLD"); OBS_P1_NEW=$(obscure "$P1_NEW"); OBS_P2_NEW=$(obscure "$P2_NEW"); OBS_P1_BAD=$(obscure "$P1_BAD")
[[ -n "$OBS_SFTP" && -n "$OBS_P1_OLD" && -n "$OBS_P1_NEW" && -n "$OBS_P1_BAD" ]] || { echo "rclone obscure fallo"; exit 1; }
SECRETS=("$SFTP_PASS" "$P1_OLD" "$P2_OLD" "$P1_NEW" "$P2_NEW" "$P1_BAD" "$OBS_SFTP" "$OBS_P1_OLD" "$OBS_P2_OLD" "$OBS_P1_NEW" "$OBS_P2_NEW" "$OBS_P1_BAD")
reset_env

# respaldos SINTETICOS "anteriores a la rotacion": full (12 MB), tar de archivos (12 MB), sus .sha256 y el manifiesto del par
TS_OLD=2026-01-01_000000Z
tools sh -c 'cd /backups/sql && head -c 12000000 /dev/urandom >"$0_full_$1.bak" && sha256sum "$0_full_$1.bak" >"$0_full_$1.bak.sha256"
  cd /backups/files && head -c 12000000 /dev/urandom >"files_$1.tar.gz" && sha256sum "files_$1.tar.gz" >"files_$1.tar.gz.sha256"
  echo "full=$0_full_$1.bak files=files_$1.tar.gz" >"set_$1_x.txt"' "$DB" "$TS_OLD" || { echo "no se pudieron crear los respaldos sinteticos"; exit 1; }
rc mkdir secure:erp >/dev/null && echo "centinela sintetico" | tools sh -c 'cat >/backups/logs/centinela.txt' && rc copyto /backups/logs/centinela.txt secure:erp/centinela.txt >/dev/null || { echo "no se pudo crear el centinela"; exit 1; }
rc copy /backups/sql secure:erp/sql >/dev/null && rc copy /backups/files secure:erp/files >/dev/null || { echo "no se pudo subir con la clave vieja"; exit 1; }
H_LOCAL0=$(local_hash)
[[ -n "$H_LOCAL0" ]] || { echo "huella local invalida"; exit 1; }

# ---------------------------------------------------------------------------------------------------------- r1
section "r1. respaldo cifrado con la clave ANTERIOR: opaco en el servidor y recuperable desde un servidor nuevo"
OLD_TREE0=$(tree_hash upload); OLD_N0=$(tree_files upload)
check "r1: el servidor tiene 6 objetos (bak, sha, tar, sha, set, centinela) -> $OLD_N0" test "$OLD_N0" = 6
check "r1: el servidor solo tiene nombres opacos (sin 'full', 'files', 'set_', base)" bash -c '[[ $(docker exec "$1" sh -c "find /home/burytest/upload | grep -ciE \"full|files_|set_|$2|centinela\"" | tr -d "\r\n") == 0 ]]' _ "$SFTP_C" "$DB"
wipe_local
run_script fetch-offsite.sh
expect_rc "r1: fetch-offsite con la clave vieja (servidor nuevo, BACKUP_DIR vacio)" 0
check "r1: lo recuperado es IDENTICO a lo respaldado (sha256 de todo)" test "$(local_hash)" = "$H_LOCAL0"

# ---------------------------------------------------------------------------------------------------------- r2
section "r2. origen con password INCORRECTA (P1 vieja equivocada): la rotacion aborta ANTES de copiar y no escribe nada"
NEW_N=$(tree_files upload2)
P1_OLD_ENV="$OBS_P1_BAD" write_env
run_script rotate-crypt.sh --from secure:erp --to secure2:erp
expect_rc "r2: rotate-crypt con la password del origen incorrecta" 6
check "r2: el mensaje dice que el origen no se pudo leer / no hay archivos descifrables" out_has "origen|descifrable|password"
check "r2: el remoto nuevo no recibio NADA (objetos antes=$NEW_N, despues=$(tree_files upload2))" test "$(tree_files upload2)" = "$NEW_N"
check "r2: el arbol del origen sigue IDENTICO" test "$(tree_hash upload)" = "$OLD_TREE0"
reset_env

# ---------------------------------------------------------------------------------------------------------- r3
section "r3. rotacion INTERRUMPIDA a mitad (docker kill del rclone con la copia a 4 MB/s): origen intacto, destino parcial, reanudable"
BWLIMIT=4M write_env
OUT="$T/run-r3.out"
( bash "$HERE/rotate-crypt.sh" --from secure:erp --to secure2:erp >"$OUT" 2>&1; echo $? >"$T/r3.rc" ) &
BGPID=$!
for i in $(seq 1 120); do [[ "$(tree_files upload2)" -ge 1 ]] && break; sleep 0.5; done   # esperar a que el destino reciba el primer objeto
docker ps -q --filter "label=com.docker.compose.project=$PROJ" --filter "label=com.docker.compose.service=backup-offsite" | xargs -r docker kill >/dev/null 2>&1
wait "$BGPID" 2>/dev/null
R3RC=$(cat "$T/r3.rc" 2>/dev/null || echo ?)
RCODE=$R3RC
check "r3: la rotacion interrumpida termina con error (exit $R3RC), no con exito falso" bash -c '[[ "$1" != 0 && "$1" != "?" ]]' _ "$R3RC"
PART_N=$(tree_files upload2)
check "r3: el destino quedo PARCIAL ($PART_N de 6 objetos): la interrupcion fue real" bash -c '(( $1 < 6 ))' _ "$PART_N"
check "r3: el origen quedo IDENTICO byte a byte tras la interrupcion" test "$(tree_hash upload)" = "$OLD_TREE0"
reset_env
run_script rotate-crypt.sh --from secure:erp --to secure2:erp --verify-only
expect_rc "r3: --verify-only sobre el destino parcial FALLA" 6
check "r3: el lock se libero (el proceso interrumpido no dejo el BACKUP_DIR bloqueado)" bash -c 'flock -n "$1/.lock.flock" true' _ "$BACKUP_DIR" 
run_script rotate-crypt.sh --from secure:erp --to secure2:erp
expect_rc "r3: al volver a ejecutar, la rotacion se REANUDA y completa" 0

# ---------------------------------------------------------------------------------------------------------- r4
section "r4. rotacion completa y verificada: los respaldos anteriores NO se destruyen"
check "r4: verificacion descifrado-contra-descifrado OK" out_has "verificado: 6 archivo\(s\) del origen presentes e IDENTICOS"
check "r4: el arbol del servidor del origen es IDENTICO al previo a la rotacion (nada borrado ni modificado)" test "$(tree_hash upload)" = "$OLD_TREE0"
check "r4: el remoto nuevo tiene los 6 objetos con nombres opacos" test "$(tree_files upload2)" = 6
run_script rotate-crypt.sh --from secure:erp --to secure2:erp
expect_rc "r4: idempotente: repetir la rotacion ya completada no cambia nada" 0
check "r4: el origen sigue intacto tras repetirla" test "$(tree_hash upload)" = "$OLD_TREE0"
check "r4: el log cierra con 'El remoto viejo y sus passwords siguen intactos'" out_has "siguen intactos"

# ---------------------------------------------------------------------------------------------------------- r5
section "r5. recuperacion de respaldos VIEJOS y NUEVOS con la clave nueva (BACKUP_REMOTE=secure2:erp)"
export BACKUP_REMOTE='secure2:erp'
wipe_local
run_script fetch-offsite.sh
expect_rc "r5: fetch-offsite desde el remoto rotado" 0
check "r5: los respaldos ANTERIORES a la rotacion se recuperan identicos con la clave nueva" test "$(local_hash)" = "$H_LOCAL0"
TS_NEW=2026-02-02_000000Z
tools sh -c 'cd /backups/sql && head -c 3000000 /dev/urandom >"$0_full_$1.bak" && sha256sum "$0_full_$1.bak" >"$0_full_$1.bak.sha256"
  cd /backups/files && head -c 3000000 /dev/urandom >"files_$1.tar.gz" && sha256sum "files_$1.tar.gz" >"files_$1.tar.gz.sha256"' "$DB" "$TS_NEW"
H_BOTH=$(local_hash)
rc copy /backups/sql secure2:erp/sql --immutable >/dev/null && rc copy /backups/files secure2:erp/files --immutable >/dev/null
check "r5: un respaldo POSTERIOR a la rotacion se sube cifrado con la clave nueva" test "$(tree_files upload2)" = 10
wipe_local
run_script fetch-offsite.sh
expect_rc "r5: segundo servidor nuevo: fetch-offsite recupera viejos + nuevos" 0
check "r5: viejos + nuevos IDENTICOS a lo respaldado" test "$(local_hash)" = "$H_BOTH"
check "r5: con la clave vieja se ve el respaldo anterior pero NO el posterior a la rotacion" bash -c 'l=$(docker compose --progress quiet run --rm -T --no-deps backup-offsite lsf "secure:erp/sql" </dev/null); grep -q "$1" <<<"$l" && ! grep -q "$2" <<<"$l"' _ "$TS_OLD" "$TS_NEW"

# ---------------------------------------------------------------------------------------------------------- r6
section "r6. password NUEVA incorrecta: falla clara, sin ensuciar el destino local ni el remoto"
P1_NEW_ENV="$OBS_P1_BAD" write_env
wipe_local; H_EMPTY=$(local_hash)
run_script fetch-offsite.sh
expect_rc "r6: fetch-offsite con la password nueva equivocada" 6
check "r6: el mensaje incluye la causa (no solo 'descarga fallo')" bash -c 'grep -E "\[ERROR\].*descarga fallo|verificacion" "$1" | grep -qE ": .{8,}"' _ "$OUT"
check "r6: no se escribio ningun respaldo en el BACKUP_DIR local" test "$(local_hash)" = "$H_EMPTY"
BEFORE_N=$(tree_files upload2); BEFORE_T=$(tree_hash upload2)
run_script rotate-crypt.sh --from secure:erp --to secure2:erp --verify-only
expect_rc "r6: --verify-only con la password nueva equivocada FALLA" 6
run_script rotate-crypt.sh --from secure:erp --to secure2:erp
expect_rc "r6: la rotacion con la password nueva equivocada se NIEGA (no escribe un 2o juego indescifrable)" 6
check "r6: explica que el destino tiene objetos que no se descifran con las passwords nuevas" out_has "solo [0-9]+ se descifran"
reset_env
check "r6: el destino bueno quedo sin cambios (objetos y hash)" bash -c '[[ "$1" == "$3" && "$2" == "$4" ]]' _ "$(tree_files upload2)" "$(tree_hash upload2)" "$BEFORE_N" "$BEFORE_T"

# ---------------------------------------------------------------------------------------------------------- r7
section "r7. rollback: volver a la clave anterior (BACKUP_REMOTE=secure:erp) sigue funcionando"
export BACKUP_REMOTE='secure:erp'
wipe_local
run_script fetch-offsite.sh
expect_rc "r7: fetch-offsite con la clave VIEJA tras haber rotado" 0
check "r7: los respaldos anteriores se recuperan identicos (el origen no se destruyo)" test "$(local_hash)" = "$H_LOCAL0"

# ---------------------------------------------------------------------------------------------------------- r8
section "r8. validaciones de uso y garantia estatica 'nunca borra'"
run_script rotate-crypt.sh --from secure:erp --to secure3:erp
expect_rc "r8: 'rotar' con las MISMAS passwords (secure3 = viejas) se detecta y falla" 6
check "r8: el mensaje dice que las passwords son equivalentes (la clave no roto)" out_has "equivalentes"
exec 8>>"$BACKUP_DIR/.lock.flock"; flock -x 8   # este shell retiene el lock (como un backup en curso)
run_script rotate-crypt.sh --from secure:erp --to secure2:erp --verify-only
expect_rc "r8: con un backup/restore en curso (lock tomado) la rotacion se NIEGA" 4
exec 8>&-   # libera el lock
run_script rotate-crypt.sh --from secure:erp --to secure:erp
expect_rc "r8: --from igual a --to se rechaza" 1
run_script rotate-crypt.sh --from secure:erp --to offsite:upload
expect_rc "r8: un destino que no es crypt se rechaza (viajaria sin cifrar)" 1
run_script rotate-crypt.sh --from secure:../x --to secure2:erp
expect_rc "r8: rutas con '..' se rechazan" 1
run_script rotate-crypt.sh --from secure:erp
expect_rc "r8: falta --to" 1
check "r8: el script solo invoca los subcomandos rclone lsf/copy/check (lista blanca)" bash -c '! grep -vE "^[[:space:]]*#" "$1" | grep -E "\b(rc|rclone)[[:space:]]+(sync|move|moveto|delete|deletefile|purge|cleanup|rmdir|rmdirs|dedupe|bisync|touch|mkdir)\b"' _ "$HERE/rotate-crypt.sh"
check "r8: la copia usa --immutable (nunca pisa un archivo existente del destino)" bash -c 'grep -E "rc copy .*--immutable" "$1" >/dev/null' _ "$HERE/rotate-crypt.sh"
check "r8: backup.sh/retencion remota solo operan sobre BACKUP_REMOTE: ningun script de rotacion llama a retention" bash -c '! grep -vE "^[[:space:]]*#" "$1" | grep -E "retention|retencion_remota|deletefile" >/dev/null' _ "$HERE/rotate-crypt.sh"
run_script rotate-crypt.sh --from secure:erp --to secure2:erp --verify-only
expect_rc "r8: estado final: la verificacion vieja->nueva sigue OK" 0

# ---------------------------------------------------------------------------------------------------------- transversal
section "transversal: ningun secreto en logs ni salidas"
LEAK=0
for s in "${SECRETS[@]}"; do
  [[ -n "$s" ]] || continue
  if grep -rqF -- "$s" "$T"/run*.out "$BACKUP_DIR/logs" 2>/dev/null; then LEAK=$((LEAK+1)); fi
done
check "ningun password/obscure aparece en las salidas ni en rotate-crypt.log (${#SECRETS[@]} valores buscados)" test "$LEAK" = 0

echo; echo "=========================================="
echo "resultado: $PASS PASS, $FAIL FAIL   (proyecto $PROJ)"
if (( FAIL )); then printf 'FALLARON:\n'; printf '  - %s\n' "${FAILED[@]}"; exit 1; fi
exit 0
