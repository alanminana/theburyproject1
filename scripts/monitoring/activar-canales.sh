#!/usr/bin/env bash
# P-08: activación guiada de los canales REALES de alerta (Telegram + heartbeat a un observador externo) en el servidor Linux.
#
#   sudo bash scripts/monitoring/activar-canales.sh [--telegram] [--heartbeat] [--dry-run]
#   (sin flags: pregunta por ambos; Enter en un valor lo omite)
#
# Lo que hace (y solo eso): escribe /etc/bury-monitor/telegram-relay.env y actualiza /etc/bury-monitor/monitor.env (root, 0600, copia
# previa .previo), instala la unidad del relevo, valida (`telegram_relay.py --check`), envía UN mensaje de prueba real, habilita el
# relevo, prueba el canal completo (`monitor.py --test-channel`: firing + resolved + heartbeat) y reinicia el monitor.
# Los secretos se leen sin eco (read -s) o de variables BURY_ACT_TELEGRAM_TOKEN / BURY_ACT_TELEGRAM_CHAT_ID / BURY_ACT_HEARTBEAT_URL /
# BURY_ACT_HEARTBEAT_STYLE; nunca van por argumentos, nunca se imprimen y no se escriben en ningún log ni en Git.
# Variables de prueba (solo CI): BURY_ACT_ROOT (prefijo de /etc), BURY_ACT_NO_SYSTEMD=1.
set -uo pipefail
umask 077

DO_TG=0; DO_HB=0; DRY=0; ANY=0
for a in "$@"; do
  case "$a" in
    --telegram) DO_TG=1; ANY=1 ;;
    --heartbeat) DO_HB=1; ANY=1 ;;
    --dry-run) DRY=1 ;;
    -h|--help) sed -n '2,13p' "$0"; exit 0 ;;
    *) echo "opcion desconocida: $a" >&2; exit 2 ;;
  esac
done
[ "$ANY" = 0 ] && { DO_TG=1; DO_HB=1; }

ROOT="${BURY_ACT_ROOT:-}"
ETC="$ROOT/etc/bury-monitor"
SRC="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PY="${BURY_ACT_PYTHON:-python3}"
NOSD="${BURY_ACT_NO_SYSTEMD:-0}"
[ "$DRY" = 1 ] || [ -n "$ROOT" ] || [ "$(id -u)" = 0 ] || { echo "ejecutar como root (sudo)" >&2; exit 2; }

ask() { # ask <VAR> <prompt> [secret]  (toma BURY_ACT_<VAR> si existe; Enter = omitir)
  local var="$1" prompt="$2" secret="${3:-}" pre="BURY_ACT_$1" val
  val="${!pre:-}"
  if [ -z "$val" ]; then
    if [ -t 0 ]; then
      if [ -n "$secret" ]; then read -rsp "$prompt (Enter = omitir): " val; echo; else read -rp "$prompt (Enter = omitir): " val; fi
    fi
  fi
  printf -v "$var" '%s' "$val"
}

TG_TOKEN=""; TG_CHAT=""; HB_URL=""; HB_STYLE="healthchecks"
if [ "$DO_TG" = 1 ]; then
  ask TELEGRAM_TOKEN "Token del bot de Telegram (de @BotFather)" secret; TG_TOKEN="$TELEGRAM_TOKEN"
  ask TELEGRAM_CHAT_ID "chat_id numerico del chat/grupo destino"; TG_CHAT="$TELEGRAM_CHAT_ID"
  if [ -n "$TG_TOKEN$TG_CHAT" ]; then
    [[ "$TG_TOKEN" =~ ^[0-9]{6,12}:[A-Za-z0-9_-]{30,}$ ]] || { echo "FALLA: el token no tiene el formato de BotFather (<numero>:<texto>)" >&2; exit 2; }
    [[ "$TG_CHAT" =~ ^-?[0-9]{5,20}$ ]] || { echo "FALLA: el chat_id debe ser numerico (los grupos suelen empezar con -)" >&2; exit 2; }
  fi
fi
if [ "$DO_HB" = 1 ]; then
  ask HEARTBEAT_URL "URL de ping del observador externo (Healthchecks.io u otro)" secret; HB_URL="$HEARTBEAT_URL"
  if [ -n "$HB_URL" ]; then
    [[ "$HB_URL" =~ ^https://[A-Za-z0-9.-]+(:[0-9]+)?/[^[:space:]]+$ ]] || { echo "FALLA: la URL de heartbeat debe ser https://host/ruta (sin espacios)" >&2; exit 2; }
    ask HEARTBEAT_STYLE "Estilo del observador: healthchecks (defecto) o kuma"; HB_STYLE="${HEARTBEAT_STYLE:-healthchecks}"
    [[ "$HB_STYLE" == healthchecks || "$HB_STYLE" == kuma ]] || { echo "FALLA: estilo debe ser healthchecks o kuma" >&2; exit 2; }
  fi
fi

if [ -z "$TG_TOKEN$TG_CHAT$HB_URL" ]; then
  echo "Nada que activar: no se dieron valores. El canal queda SIN activar (instrucciones: docs/p08-activacion-propietario.md)."
  exit 3
fi
if [ "$DO_TG" = 1 ] && { [ -z "$TG_TOKEN" ] || [ -z "$TG_CHAT" ]; } && [ -n "$TG_TOKEN$TG_CHAT" ]; then
  echo "FALLA: Telegram necesita token Y chat_id" >&2; exit 2
fi
[ -n "$TG_TOKEN" ] && echo "Telegram: valores con formato valido (no se muestran)."
[ -n "$HB_URL" ] && echo "Heartbeat: URL https valida, estilo $HB_STYLE (no se muestra)."
if [ "$DRY" = 1 ]; then echo "--dry-run: no se escribio nada."; exit 0; fi

set_env_kv() { # set_env_kv <archivo> <CLAVE> <valor>  -> reemplaza o agrega, sin pasar el valor por argv de otro proceso
  local file="$1" key="$2" val="$3" tmp
  tmp="$(mktemp "$file.XXXXXX")"
  { grep -v -E "^${key}=" "$file" 2>/dev/null || true; printf '%s=%s\n' "$key" "$val"; } > "$tmp"
  chmod 600 "$tmp"; mv -f "$tmp" "$file"
}

mkdir -p "$ETC"; chmod 755 "$ETC" 2>/dev/null || true
MON="$ETC/monitor.env"; TGE="$ETC/telegram-relay.env"
[ -f "$MON" ] && cp -p "$MON" "$MON.previo" && chmod 600 "$MON.previo"
touch "$MON"; chmod 600 "$MON"

if [ -n "$TG_TOKEN" ]; then
  AUTH="$(openssl rand -hex 24)"
  touch "$TGE"; chmod 600 "$TGE"
  set_env_kv "$TGE" BURY_RELAY_TELEGRAM_TOKEN "$TG_TOKEN"
  set_env_kv "$TGE" BURY_RELAY_TELEGRAM_CHAT_ID "$TG_CHAT"
  set_env_kv "$TGE" BURY_RELAY_AUTH_TOKEN "$AUTH"
  set_env_kv "$MON" BURY_MONITOR_WEBHOOK "http://127.0.0.1:9098/alert"
  set_env_kv "$MON" BURY_MONITOR_TOKEN "$AUTH"
  unset AUTH
  if [ "$NOSD" != 1 ]; then
    install -m 0644 "$SRC/bury-telegram-relay.service" /etc/systemd/system/bury-telegram-relay.service
    systemd-analyze verify /etc/systemd/system/bury-telegram-relay.service 2>&1 | grep -v -E "^$" | head -5 || true
    systemctl daemon-reload
  fi
fi
if [ -n "$HB_URL" ]; then
  set_env_kv "$MON" BURY_MONITOR_HEARTBEAT "$HB_URL"
  set_env_kv "$MON" BURY_MONITOR_HEARTBEAT_STYLE "$HB_STYLE"
fi
echo "Configuracion escrita (root, 0600). Copia previa: $MON.previo"
[ "$NOSD" = 1 ] && { echo "BURY_ACT_NO_SYSTEMD=1: no se tocaron servicios."; exit 0; }

RC=0
if [ -n "$TG_TOKEN" ]; then
  ( set -a; . "$TGE"; set +a; "$PY" "$SRC/telegram_relay.py" --check ) || { echo "FALLA: telegram_relay.py --check" >&2; exit 1; }
  echo "Enviando UN mensaje de prueba real a Telegram..."
  ( set -a; . "$TGE"; set +a; "$PY" "$SRC/telegram_relay.py" --send-test ) || { echo "FALLA: Telegram no confirmo el mensaje (token/chat_id incorrectos, o el bot no tiene permiso en el chat). El relevo NO se habilito." >&2; exit 1; }
  echo ">>> Confirme que el mensaje llego al chat. <<<"
  systemctl enable --now bury-telegram-relay || RC=1
  sleep 2; systemctl is-active bury-telegram-relay >/dev/null || { echo "FALLA: el relevo no quedo activo" >&2; RC=1; }
fi
echo "Probando el canal completo (firing + resolved + heartbeat)..."
( set -a; . "$MON"; set +a; "$PY" "$SRC/monitor.py" --test-channel ) || { echo "FALLA: monitor.py --test-channel" >&2; RC=1; }
systemctl restart bury-monitor || RC=1
[ "$RC" = 0 ] && echo "LISTO: canales activos. Registre fecha, canal y quien recibio el aviso en docs/monitoreo-validacion.md." || echo "TERMINO CON FALLAS (ver arriba)." >&2
exit $RC
