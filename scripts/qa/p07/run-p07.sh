#!/usr/bin/env bash
# P-07 - recorridos integrales en una instalacion Docker DESCARTABLE (SQL Server real + app en contenedor).
#
# Crea una pila nueva e independiente (proyecto Compose propio, red/volumenes/puertos/subred propios, secretos aleatorios en un
# directorio temporal), construye (o reutiliza) la imagen, corre scripts/qa/p07/p07_recorridos.py y la smoke de UI, y la destruye.
# NO toca TheBuryProjectDb, LocalDB, ni ninguna otra pila: todo recurso lleva el proyecto buryp07<aleatorio>.
#
#   bash scripts/qa/p07/run-p07.sh                 # construye la imagen, corre todo, destruye la pila
#   bash scripts/qa/p07/run-p07.sh --image X       # reutiliza una imagen ya construida
#   bash scripts/qa/p07/run-p07.sh --keep          # deja la pila arriba para inspeccionarla (destruir: docker compose -p <proyecto> down -v)
#   bash scripts/qa/p07/run-p07.sh --no-ui         # omite la smoke de interfaz (Playwright + Chrome del sistema)
#   bash scripts/qa/p07/run-p07.sh --out DIR       # copia resultado.json y las capturas de UI a DIR antes de destruir la pila
#
# Requisitos: docker (con compose v2), python3, openssl; para la smoke de UI: node y Chrome (P07_NODE_MODULES con playwright-core).
# En Windows se ejecuta desde Git Bash o WSL.
set -uo pipefail
export MSYS_NO_PATHCONV=1

KEEP=0; UI=1; IMAGE=""; OUT=""
while [ $# -gt 0 ]; do
  case "$1" in
    --keep) KEEP=1 ;;
    --no-ui) UI=0 ;;
    --image) IMAGE="$2"; shift ;;
    --out) OUT="$2"; shift ;;
    *) echo "opcion desconocida: $1"; exit 2 ;;
  esac
  shift
done

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
for c in docker python3 openssl; do command -v "$c" >/dev/null 2>&1 || { echo "falta el comando: $c"; exit 2; }; done

RAND="$(openssl rand -hex 3)"
P="buryp07$RAND"
T="$(mktemp -d)"
win() { if command -v cygpath >/dev/null 2>&1; then cygpath -m "$1"; else echo "$1"; fi; }
pw() { openssl rand -base64 24 | tr -dc 'A-Za-z0-9' | head -c 20; }
freeport() { python3 -c "import socket;s=socket.socket();s.bind(('127.0.0.1',0));print(s.getsockname()[1]);s.close()"; }

APP_PORT="$(freeport)"; DB_PORT="$(freeport)"
SUBNET="172.30.$((100 + 0x$RAND % 100)).0/24"
BUILT=0
if [ -z "$IMAGE" ]; then IMAGE="theburyproject/erp:p07-$RAND"; BUILT=1; fi
mkdir -p "$T/backups"
cat > "$T/.env" <<EOF
MSSQL_SA_PASSWORD=Sa$(pw)#9a
MSSQL_DATABASE=BuryP07
ERP_DB_USER=erp_app_p07
ERP_DB_PASSWORD=Ap$(pw)#7b
ERP_MIGRATION_USER=erp_mig_p07
ERP_MIGRATION_PASSWORD=Mg$(pw)#5c
MSSQL_PID=Developer
ADMIN_EMAIL=admin-p07@example.invalid
ADMIN_USERNAME=admin_p07
ADMIN_PASSWORD=Adm$(pw)#3d
ERP_DOMAIN=p07.example.invalid
ERP_IMAGE=$IMAGE
BURY_NET_SUBNET=$SUBNET
APP_PORT=$APP_PORT
MSSQL_HOST_PORT=$DB_PORT
BACKUP_DIR=$(win "$T/backups")
CSP_MODE=Off
EOF
chmod 600 "$T/.env" 2>/dev/null || true

dc() { docker compose --progress quiet -p "$P" --env-file "$(win "$T/.env")" -f "$(win "$REPO/docker-compose.yml")" -f "$(win "$REPO/docker-compose.ci.yml")" "$@"; }
cleanup() {
  if [ "$KEEP" = 1 ]; then echo "Pila conservada: proyecto $P (app http://127.0.0.1:$APP_PORT). Destruir: docker compose -p $P down -v ; imagen $IMAGE"; return; fi
  dc down -v >/dev/null 2>&1 || true
  [ "$BUILT" = 1 ] && docker image rm "$IMAGE" >/dev/null 2>&1 || true
  rm -rf "$T"
}
trap cleanup EXIT

echo "== P-07: proyecto $P, app :$APP_PORT, sql :$DB_PORT, subred $SUBNET, imagen $IMAGE =="
if ! docker image inspect "$IMAGE" >/dev/null 2>&1; then
  echo "construyendo la imagen (puede tardar unos minutos)..."
  docker build -q -t "$IMAGE" "$(win "$REPO")" >/dev/null || { echo "FALLO el build de la imagen"; exit 1; }
fi
dc up -d --wait --wait-timeout 420 db app || { echo "FALLO el arranque de la pila"; dc logs --tail 40 2>&1 | tail -40; exit 1; }

SA="$(grep ^MSSQL_SA_PASSWORD "$T/.env" | cut -d= -f2)"
APASS="$(grep ^ADMIN_PASSWORD "$T/.env" | cut -d= -f2)"
export P07_BASE_URL="http://127.0.0.1:$APP_PORT" P07_DB_CONTAINER="${P}-db-1" P07_SA_PASSWORD="$SA" P07_DB_NAME=BuryP07 \
       P07_ADMIN_USER=admin_p07 P07_ADMIN_PASS="$APASS" P07_REPORT="$(win "$T/resultado.json")" P07_APP_CONTAINER="${P}-app-1" P07_PROJECT="$P"
python3 "$(win "$REPO/scripts/qa/p07/p07_recorridos.py")"; RC=$?

if [ "$UI" = 1 ] && command -v node >/dev/null 2>&1; then
  echo; echo "== Smoke de interfaz (Chrome, 1366 y 390) =="
  node "$(win "$REPO/scripts/qa/p07/p07-ui-smoke.mjs")" "$P07_BASE_URL" "$(win "$T/ui")" || RC=1
fi
if [ -n "$OUT" ]; then mkdir -p "$OUT" && cp -r "$T/resultado.json" "$T/ui" "$OUT/" 2>/dev/null && echo "Resultados copiados a $OUT"; fi
exit $RC
