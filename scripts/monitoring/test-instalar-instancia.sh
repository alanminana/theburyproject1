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
mkdir -p "$T/etc/bury-monitor" "$T/units"; printf 'BURY_MONITOR_TOKEN=SECRETO-SINTETICO
BURY_MONITOR_HEARTBEAT=https://hb.invalid/push/HBSECRETO
  BURY_MONITOR_HEARTBEAT_STYLE=kuma
	export BURY_MONITOR_HEARTBEAT_X=1
' > "$T/etc/bury-monitor/monitor.env"
printf -- '-----BEGIN CERTIFICATE-----
QUJD
-----END CERTIFICATE-----
' > "$T/ca.crt"
export BURY_INSTALL_ANY_USER=1 BURY_MONITOR_ETC_ROOT="$T/etc" BURY_MONITOR_UNIT_DIR="$T/units"
OUTP="$("$S" "${A[@]}" --ca-file "$T/ca.crt" --apply --out "$T/a1" 2>&1)"; rc=$?
[ $rc = 0 ] && [ -f "$T/etc/bury-monitor-prod/config.json" ] && [ -f "$T/units/bury-monitor-prod.service" ] && ok apply || { ko "apply (rc=$rc)"; echo "$OUTP"; }
[ "$(stat -c %a "$T/etc/bury-monitor-prod/monitor.env")" = 600 ] && ok "monitor.env 0600" || ko "monitor.env 0600"
! grep -q HEARTBEAT "$T/etc/bury-monitor-prod/monitor.env" && grep -q SECRETO "$T/etc/bury-monitor-prod/monitor.env" && ok "heartbeat no copiado, token si" || ko "heartbeat no copiado, token si"
cmp -s "$T/ca.crt" "$T/etc/bury-monitor-prod/ca.crt" && grep -q 'Environment=SSL_CERT_FILE=/etc/bury-monitor-prod/ca.crt' "$T/units/bury-monitor-prod.service" && ok "CA y SSL_CERT_FILE" || ko "CA y SSL_CERT_FILE"
echo "$OUTP" | grep -q -e SECRETO -e HBSECRETO && ko "no filtra secreto" || ok "no filtra secreto"
"$S" "${A[@]}" --apply --out "$T/a2" >/dev/null 2>&1 && ko "no sobrescribe" || ok "no sobrescribe"
grep -q HEARTBEAT "$T/etc/bury-monitor/monitor.env" && ok "base intacta" || ko "base intacta"
"$S" "${A[@]}" --ca-file "$T/no-existe.crt" --out "$T/b2" >/dev/null 2>&1 && ko "rechaza CA invalida" || ok "rechaza CA invalida"

# 4) Codex #364: rutas de salida peligrosas, symlinks, carrera/limpieza y lectura fallida (cada caso con rutas propias: sin falsos positivos)
CFG0="$(md5sum "$T/etc/bury-monitor-prod/config.json")"
"$S" "${A[@]}" --apply --out "$T/etc/bury-monitor-prod" >/dev/null 2>&1 && ko "out=instancia existente" || ok "out=instancia existente rechazado"
[ "$(md5sum "$T/etc/bury-monitor-prod/config.json")" = "$CFG0" ] && ok "config existente intacta" || ko "config existente intacta"
mkdir -p "$T/etc4/bury-monitor" "$T/units4"; echo 'BURY_MONITOR_TOKEN=X' > "$T/etc4/bury-monitor/monitor.env"
BURY_MONITOR_ETC_ROOT="$T/etc4" BURY_MONITOR_UNIT_DIR="$T/units4" "$S" "${A[@]}" --apply --out "$T/units4" >/dev/null 2>&1 && ko "out=directorio de unidades" || ok "out=directorio de unidades rechazado"
[ ! -e "$T/etc4/bury-monitor-prod" ] && ok "rechazo antes de crear nada" || ko "rechazo antes de crear nada"
# symlink preexistente en --out: no se sigue ni se pisa el destino
mkdir -p "$T/out5"; echo 'AJENO' > "$T/ajeno.txt"; ln -s "$T/ajeno.txt" "$T/out5/config.json"
"$S" "${A[@]}" --out "$T/out5" >/dev/null 2>&1 && ko "symlink en --out" || ok "symlink en --out rechazado"
[ "$(cat "$T/ajeno.txt")" = AJENO ] && ok "destino del symlink intacto" || ko "destino del symlink intacto"
# --out con una ruta relativa que apunta a la instancia existente (..)
( cd "$T/etc" && "$S" "${A[@]}" --apply --out "bury-monitor-prod/../bury-monitor-prod" >/dev/null 2>&1 ) && ko "out relativo a instancia" || ok "out relativo a instancia rechazado"
# lectura fallida de monitor.env: aborta y no deja instancia a medias (solo como usuario no root)
mkdir -p "$T/etc2/bury-monitor" "$T/units2"; echo 'BURY_MONITOR_TOKEN=X' > "$T/etc2/bury-monitor/monitor.env"; chmod 000 "$T/etc2/bury-monitor/monitor.env"
if [ "$(id -u)" != 0 ]; then
  BURY_MONITOR_ETC_ROOT="$T/etc2" BURY_MONITOR_UNIT_DIR="$T/units2" "$S" "${A[@]}" --apply --out "$T/a3" >/dev/null 2>&1 && ko "monitor.env ilegible aborta" || ok "monitor.env ilegible aborta"
  [ ! -e "$T/etc2/bury-monitor-prod" ] && [ ! -e "$T/units2/bury-monitor-prod.service" ] && ok "sin instancia a medias" || ko "sin instancia a medias"
  BURY_MONITOR_ETC_ROOT="$T/etc2" BURY_MONITOR_UNIT_DIR="$T/units2" "$S" "${A[@]}" --keep-heartbeat --apply --out "$T/a4" >/dev/null 2>&1 && ko "keep-heartbeat ilegible aborta" || ok "keep-heartbeat ilegible aborta"
  [ ! -e "$T/etc2/bury-monitor-prod" ] && ok "keep-heartbeat: sin instancia a medias" || ko "keep-heartbeat: sin instancia a medias"
fi
chmod 600 "$T/etc2/bury-monitor/monitor.env"
exit $fail
