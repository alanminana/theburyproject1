#!/usr/bin/env bash
# Prueba de instalar-instancia.sh con rutas sinteticas (sin root, sin systemd, sin tocar el sistema real).
set -uo pipefail
D="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"; S="$D/instalar-instancia.sh"
T="$(mktemp -d)"; trap 'rm -rf "$T"' EXIT
fail=0; ok() { echo "OK   $1"; }; ko() { echo "FALLA $1"; fail=1; }
A=(--name prod --project theburyproject-prod --backup-dir /srv/bury-backups-prod --base-url https://tbp-prod:8443)

# 1) solo generar: no crea nada fuera de --out
"$S" "${A[@]}" --out "$T/out" >/dev/null 2>&1 && [ -f "$T/out/config.json" ] && [ -f "$T/out/bury-monitor-prod.service" ] && ok generar || ko generar
python3 - "$T/out/config.json" <<'PY' && ok config || ko config
import json,sys; c=json.load(open(sys.argv[1]))
assert c["project"]=="theburyproject-prod" and c["backup_dir"]=="/srv/bury-backups-prod" and c["state_dir"]=="/var/lib/bury-monitor-prod"
assert "offsite-sql" not in c["backup_windows"] and "offsite-files" not in c["backup_windows"] and c["offsite_restore_test"]["enabled"] is False
PY
U="$T/out/bury-monitor-prod.service"
grep -q -- '--config /etc/bury-monitor-prod/config.json' "$U" && grep -q 'EnvironmentFile=/etc/bury-monitor-prod/monitor.env' "$U" \
  && grep -q 'StateDirectory=bury-monitor-prod' "$U" && grep -q 'ReadWritePaths=/var/lib/bury-monitor-prod' "$U" && ok unidad || ko unidad
grep -E '/etc/bury-monitor/|/var/lib/bury-monitor([^-]|$)' "$U" >/dev/null && ko aislamiento || ok aislamiento
"$S" "${A[@]}" --keep-offsite --out "$T/o2" >/dev/null 2>&1 && grep -q offsite-sql "$T/o2/config.json" && ok keep-offsite || ko keep-offsite

# 2) validaciones de entrada
for bad in "--name Prod" "--name base" "--project A/B" "--backup-dir rel/dir" "--backup-dir /srv/../etc" "--base-url ftp://x"; do
  args=("${A[@]}"); k="${bad%% *}"; v="${bad#* }"
  for i in "${!args[@]}"; do [ "${args[$i]}" = "$k" ] && args[$((i+1))]="$v"; done
  "$S" "${args[@]}" --out "$T/bad" >/dev/null 2>&1 && ko "rechaza '$bad'" || ok "rechaza '$bad'"
done

# 3) --apply sobre un arbol sintetico; no pisa una instancia existente; no filtra el contenido de monitor.env
mkdir -p "$T/etc/bury-monitor" "$T/units"; echo 'BURY_MONITOR_TOKEN=SECRETO-SINTETICO' > "$T/etc/bury-monitor/monitor.env"
export BURY_INSTALL_ANY_USER=1 BURY_MONITOR_ETC_ROOT="$T/etc" BURY_MONITOR_UNIT_DIR="$T/units"
OUTP="$("$S" "${A[@]}" --apply --out "$T/a1" 2>&1)"; rc=$?
[ $rc = 0 ] && [ -f "$T/etc/bury-monitor-prod/config.json" ] && [ -f "$T/units/bury-monitor-prod.service" ] && ok apply || { ko "apply (rc=$rc)"; echo "$OUTP"; }
[ "$(stat -c %a "$T/etc/bury-monitor-prod/monitor.env")" = 600 ] && ok "monitor.env 0600" || ko "monitor.env 0600"
echo "$OUTP" | grep -q SECRETO && ko "no filtra secreto" || ok "no filtra secreto"
"$S" "${A[@]}" --apply --out "$T/a2" >/dev/null 2>&1 && ko "no sobrescribe" || ok "no sobrescribe"
[ -f "$T/etc/bury-monitor/monitor.env" ] && ok "base intacta" || ko "base intacta"
exit $fail
