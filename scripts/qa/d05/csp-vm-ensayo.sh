#!/bin/bash
# D-05 en el servidor Linux de ensayo (Docker + Caddy real; host por defecto https://tbp, cambiable con CSP_ENSAYO_URL): ReportOnly -> Enforce -> reversion, con humo de solo lectura y tiempos.
# SOLO servidor de ENSAYO con datos sinteticos: cambia CSP_MODE en el .env y reinicia `app`. Exige --confirmo-entorno-de-ensayo.
set -uo pipefail
[ "${1:-}" = "--confirmo-entorno-de-ensayo" ] || { echo "guarda: falta --confirmo-entorno-de-ensayo (modifica .env y reinicia la app)"; exit 2; }
cd ~/theburyproject1 || exit 2
C="docker compose -p theburyproject --env-file .env"
mkdir -p ~/csp && chmod 700 ~/csp
PREV=~/csp/env-previo-$(date +%F-%H%M%S)
cp .env "$PREV" && chmod 600 "$PREV" || { echo "no se pudo guardar el .env previo: abortado sin cambios"; exit 2; }
restore_env() { cp "$PREV" .env && $C up -d --no-deps app >/dev/null 2>&1; }
trap restore_env EXIT   # ante cualquier salida (error, Ctrl+C) vuelve al .env guardado EN ESTA corrida
export CSP_HUMO_URL=${CSP_ENSAYO_URL:-https://tbp} CSP_HUMO_USER=admin CSP_HUMO_PASS="$(grep ^ADMIN_PASSWORD= .env | cut -d= -f2-)"
hdr() { curl -s -D - -o /dev/null "$CSP_HUMO_URL"/Identity/Account/Login | grep -i '^content-security' | cut -d: -f1 | tr '\n' ' '; echo; }
setmode() { grep -q '^CSP_MODE=' .env && sed -i "s/^CSP_MODE=.*/CSP_MODE=$1/" .env || echo "CSP_MODE=$1" >> .env; }
apply() { local T0=$(date +%s); setmode "$1"; $C up -d --no-deps app >/dev/null 2>&1; until [ "$(docker inspect -f '{{.State.Health.Status}}' theburyproject-app-1)" = healthy ]; do [ $(( $(date +%s)-T0 )) -gt 200 ] && break; sleep 2; done; echo "  modo $1 aplicado y app healthy en $(( $(date +%s)-T0 ))s"; }
humo() { (cd ~/csp-tool && node scripts/ops/csp-humo-lectura.js --esperar "$1" --https-propio 2>&1 | tail -12); echo "  humo $1 exit=${PIPESTATUS[0]}"; }
echo "== modo actual (cabeceras):"; hdr
echo "== 1) ReportOnly"; apply ReportOnly; echo -n "  cabecera: "; hdr; humo reportonly
echo "== centinela (1 violacion inofensiva con navegador real) y circuito de reporte"
cat > /tmp/centinela.js <<'JS'
const { chromium } = require('/home/snow/csp-tool/node_modules/playwright');
(async () => { const b = await chromium.launch(); const c = await b.newContext({ ignoreHTTPSErrors: true }); const p = await c.newPage();
  await p.goto((process.env.CSP_HUMO_URL || 'https://tbp') + '/Identity/Account/Login'); await p.evaluate(() => { document.body.appendChild(Object.assign(document.createElement('img'), { src: 'https://csp-centinela.invalid/x.png' })); });
  await p.waitForTimeout(2500); await b.close(); })();
JS
node /tmp/centinela.js; sleep 2
echo "  lineas CSP en el log de la app:"; docker logs --since 2m theburyproject-app-1 2>&1 | grep -i "CSP violation" | tail -3 | cut -c1-200
echo "== 2) Enforce"; apply Enforce; echo -n "  cabecera: "; hdr; humo enforce
echo "== 3) REVERSION Enforce -> ReportOnly (objetivo <= 5 min)"; T0=$(date +%s); docker compose -p theburyproject --env-file .env logs --no-color -t app > ~/csp/app-incidente-$(date +%H%M).log 2>&1; apply ReportOnly; echo -n "  cabecera: "; hdr; humo reportonly; echo "  REVERSION TOTAL (archivar log + cambio + humo): $(( $(date +%s)-T0 ))s"
echo "== 4) estado final: restaurar el .env guardado al inicio de esta corrida"; restore_env; trap - EXIT; sleep 25; echo -n "  cabecera final: "; hdr; docker inspect -f '  app {{.State.Health.Status}}' theburyproject-app-1
