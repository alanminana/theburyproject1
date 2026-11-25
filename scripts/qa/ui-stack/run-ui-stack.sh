#!/usr/bin/env bash
# Ronda de interfaz (A2-A5, A8) sobre una pila Docker DESCARTABLE con Caddy + HTTPS (CA propia `tls internal`), SQL Server real y
# usuarios sinteticos (overlay docker-compose.ci.yml). Nunca toca la instalacion/base del laboratorio: todo recurso lleva el proyecto
# buryp07<aleatorio> (mismo prefijo que run-p07.sh, que exige el guardia de p07_recorridos.py).
# Los navegadores corren en el contenedor oficial de Playwright (network host), asi no se instala nada en el host salvo Docker.
#
#   bash scripts/qa/ui-stack/run-ui-stack.sh [--image IMG] [--csp ReportOnly|Enforce] [--out DIR] [--keep] [--suites "a11y,print,..."] [--browsers "chromium,firefox,webkit"]
set -uo pipefail
export MSYS_NO_PATHCONV=1
KEEP=0; IMAGE=""; OUT=""; CSP="Enforce"; SUITES="seed,https,crawl,global,cierre,print"; BROWSERS="chromium,firefox,webkit"; PWV="1.59.1"
while [ $# -gt 0 ]; do case "$1" in
  --keep) KEEP=1;; --image) IMAGE="$2"; shift;; --out) OUT="$2"; shift;; --csp) CSP="$2"; shift;;
  --suites) SUITES="$2"; shift;; --browsers) BROWSERS="$2"; shift;; *) echo "opcion desconocida: $1"; exit 2;; esac; shift; done
case "$CSP" in Off|ReportOnly|Enforce) ;; *) echo "--csp invalido"; exit 2;; esac
REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
for c in docker python3 openssl; do command -v "$c" >/dev/null 2>&1 || { echo "falta: $c"; exit 2; }; done
RAND="$(openssl rand -hex 3)"; P="buryp07$RAND"; T="$(mktemp -d)"
pw() { openssl rand -base64 24 | tr -dc 'A-Za-z0-9' | head -c 20; }
freeport() { python3 -c "import socket;s=socket.socket();s.bind(('127.0.0.1',0));print(s.getsockname()[1]);s.close()"; }
APP_PORT="$(freeport)"; DB_PORT="$(freeport)"; HTTPS_PORT="$(freeport)"
SUBNET="172.30.$((100 + 0x$RAND % 100)).0/24"; BUILT=0
if [ -z "$IMAGE" ]; then IMAGE="theburyproject/erp:ui-$RAND"; BUILT=1; fi
mkdir -p "$T/backups" "$T/out"
cat > "$T/.env" <<EOT
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
ERP_DOMAIN=localhost
ERP_BIND_ADDRESS=127.0.0.1
ERP_HTTPS_PORT=$HTTPS_PORT
ERP_IMAGE=$IMAGE
BURY_NET_SUBNET=$SUBNET
APP_PORT=$APP_PORT
MSSQL_HOST_PORT=$DB_PORT
BACKUP_DIR=$T/backups
CSP_MODE=$CSP
EOT
chmod 600 "$T/.env"
dc() { docker compose --progress quiet -p "$P" --env-file "$T/.env" -f "$REPO/docker-compose.yml" -f "$REPO/docker-compose.ci.yml" "$@"; }
cleanup() {
  if [ "$KEEP" = 1 ]; then echo "Pila conservada: $P (http 127.0.0.1:$APP_PORT, https 127.0.0.1:$HTTPS_PORT). Destruir: docker compose -p $P down -v"; return; fi
  dc down -v >/dev/null 2>&1 || true
  [ "$BUILT" = 1 ] && docker image rm "$IMAGE" >/dev/null 2>&1 || true
  rm -rf "$T"
}
trap cleanup EXIT
echo "== UI-STACK: $P app :$APP_PORT https :$HTTPS_PORT csp=$CSP imagen $IMAGE =="
if ! docker image inspect "$IMAGE" >/dev/null 2>&1; then docker build -q -t "$IMAGE" "$REPO" >/dev/null || { echo "FALLO el build"; exit 1; }; fi
# El overlay CI fija Csp__Mode=ReportOnly: se sobreescribe para probar tambien Enforce.
cat > "$T/csp.yml" <<EOT
services:
  app:
    environment:
      Csp__Mode: "$CSP"
EOT
dc() { docker compose --progress quiet -p "$P" --env-file "$T/.env" -f "$REPO/docker-compose.yml" -f "$REPO/docker-compose.ci.yml" -f "$T/csp.yml" "$@"; }
dc up -d --wait --wait-timeout 420 db app caddy || { echo "FALLO el arranque"; dc logs --tail 40 2>&1 | tail -40; exit 1; }
SA="$(grep ^MSSQL_SA_PASSWORD "$T/.env" | cut -d= -f2)"; APASS="$(grep ^ADMIN_PASSWORD "$T/.env" | cut -d= -f2)"
RC=0
if [[ ",$SUITES," == *,seed,* ]]; then
  echo "== Datos sinteticos (p07_recorridos como sembrador) =="
  P07_BASE_URL="http://127.0.0.1:$APP_PORT" P07_DB_CONTAINER="${P}-db-1" P07_SA_PASSWORD="$SA" P07_DB_NAME=BuryP07 P07_ADMIN_USER=admin_p07 \
  P07_ADMIN_PASS="$APASS" P07_REPORT="$T/out/seed.json" P07_APP_CONTAINER="${P}-app-1" P07_PROJECT="$P" python3 "$REPO/scripts/qa/p07/p07_recorridos.py" > "$T/out/seed.log" 2>&1 || { echo "FALLO el sembrado"; tail -15 "$T/out/seed.log"; RC=1; }
  tail -3 "$T/out/seed.log"
fi
echo "== Navegadores (contenedor Playwright $PWV) =="
docker run --rm --network host --ipc=host -v "$REPO:/repo:ro" -v "$T/out:/out" \
  -e E2E_BASE_URL="http://127.0.0.1:$APP_PORT" -e E2E_HTTPS_URL="https://localhost:$HTTPS_PORT" -e SUITES="$SUITES" -e BROWSERS="$BROWSERS" -e PWV="$PWV" \
  "mcr.microsoft.com/playwright:v$PWV-noble" bash /repo/scripts/qa/ui-stack/ronda.sh || RC=1
# El contenedor escribe como root: devolver la propiedad al usuario para poder copiar y borrar el directorio temporal.
docker run --rm -v "$T/out:/out" "mcr.microsoft.com/playwright:v$PWV-noble" chown -R "$(id -u):$(id -g)" /out >/dev/null 2>&1 || true
if [ -n "$OUT" ]; then mkdir -p "$OUT" && cp -r "$T/out/." "$OUT/" && echo "Resultados en $OUT"; fi
exit $RC
