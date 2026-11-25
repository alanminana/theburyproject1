#!/usr/bin/env bash
# Corre dentro del contenedor Playwright. Variables: E2E_BASE_URL (http app), E2E_HTTPS_URL (Caddy), SUITES, BROWSERS, PWV.
set -uo pipefail
W=/tmp/w; mkdir -p $W && cd $W
npm init -y >/dev/null && npm i --silent playwright@"$PWV" axe-core >/dev/null 2>&1 || { echo "FALLO npm i"; exit 1; }
export NODE_PATH=$W/node_modules AXE_PATH=$W/node_modules/axe-core/axe.min.js
R=/repo/scripts/qa; RC=0
run() { local n="$1"; shift; echo "--- $n"; "$@" > "/out/$n.log" 2>&1; local c=$?; tail -4 "/out/$n.log"; echo "[$n] exit=$c"; [ $c -ne 0 ] && RC=1; return 0; }
for b in ${BROWSERS//,/ }; do
  [[ ",$SUITES," == *,https,* ]] && run "https-signalr-$b" env E2E_HTTPS_URL="$E2E_HTTPS_URL" node $R/csp-roles/https-signalr.js --browser $b
  if [[ ",$SUITES," == *,crawl,* ]]; then
    run "crawl-http-$b" node $R/a11y-responsive/crawl.js --browser $b --roles administrador,gerente,vendedor,cajero,repositor,tecnico,contador --salida /out/crawl-http-$b.json
    run "crawl-https-$b" env E2E_BASE_URL="$E2E_HTTPS_URL" node $R/a11y-responsive/crawl.js --browser $b --roles administrador --salida /out/crawl-https-$b.json
  fi
  [[ ",$SUITES," == *,global,* ]] && run "global-$b" node $R/ui-stack/a11y-global.js --browser $b --salida /out/global-$b.json
  [[ ",$SUITES," == *,cierre,* ]] && run "cierre-$b" node $R/ui-stack/cierre-pantallas.js --browser $b --salida /out/cierre-$b
  [[ ",$SUITES," == *,print,* ]] && run "print-$b" node $R/ui-stack/impresion-render.js --browser $b --salida /out/print-$b
done
exit $RC
