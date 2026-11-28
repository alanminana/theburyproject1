#!/usr/bin/env bash
# Estado de energia del host en UNA linea, solo lectura, sin root. Pensado como comando FORZADO de una clave SSH dedicada del
# observador externo (authorized_keys: restrict,from="<IP de la PC>",command="/usr/local/bin/bury-estado-energia" ssh-ed25519 ...):
# esa clave no abre shell ni reenvia puertos; solo puede leer esta linea. No acepta argumentos ni usa SSH_ORIGINAL_COMMAND.
#
#   ac=<1|0|na> cap=<0-100|na> status=<Charging|Discharging|...|na> monitor=<estado systemd> uptime_s=<n> fw=<estado|vencido|na>
#   fw: vigilancia del firewall (bury-firewall-watch). Con un incidente abierto se informa 'incidente' aunque las reglas ya esten bien.
set -u
ROOT=${BURY_POWER_SYSFS:-/sys/class/power_supply}
ac=na cap=na status=na
for d in "$ROOT"/*; do
  [ -r "$d/type" ] || continue
  case "$(cat "$d/type")" in
    Mains)
      v=$(cat "$d/online" 2>/dev/null)
      if [ "$v" = 1 ]; then ac=1; elif [ "$v" = 0 ] && [ "$ac" != 1 ]; then ac=0; fi ;;
    Battery)
      [ "$(cat "$d/scope" 2>/dev/null)" = Device ] && continue
      c=$(cat "$d/capacity" 2>/dev/null)
      case "$c" in ''|*[!0-9]*) continue ;; esac
      if [ "$cap" = na ] || [ "$c" -lt "$cap" ]; then cap=$c; fi
      s=$(tr -d ' ' < "$d/status" 2>/dev/null)
      if [ "$status" != Discharging ]; then status=${s:-na}; fi ;;
  esac
done
monitor=$(systemctl is-active bury-monitor 2>/dev/null) || true
up=$(cut -d. -f1 /proc/uptime 2>/dev/null)
fw=na
FWF=${BURY_FW_STATE:-/var/lib/bury-firewall/estado}
if [ -r "$FWF" ]; then
  beat=$(sed -n 's/^latido=\([0-9]*\) .*/\1/p' "$FWF")
  fw=$(sed -n 's/.* estado=\([a-z-]*\) .*/\1/p' "$FWF"); fw=${fw:-na}
  inc=$(sed -n 's/.* incidente=\([0-9][0-9]*\).*/\1/p' "$FWF")
  if [ -z "$beat" ] || [ $(( $(date +%s) - beat )) -gt 60 ]; then fw=vencido
  elif [ -n "$inc" ] && [ "$fw" = ok ]; then fw=incidente; fi
fi
printf 'ac=%s cap=%s status=%s monitor=%s uptime_s=%s fw=%s\n' "$ac" "$cap" "$status" "${monitor:-na}" "${up:-na}" "$fw"
