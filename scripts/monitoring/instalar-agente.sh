#!/usr/bin/env bash
# P-08: instala/actualiza el conjunto COMPLETO de archivos del agente de monitoreo en el servidor Linux.
#
#   sudo bash scripts/monitoring/instalar-agente.sh [DESTINO]      (defecto: /opt/theburyproject/scripts/monitoring)
#
# Copia desde el checkout/arbol donde se ejecuta (monitor.py, telegram_relay.py, activar-canales.sh, unidades systemd y ejemplos).
# Motivo: ese directorio del servidor no es un checkout; copiar a mano solo "lo que cambio" dejo activar-canales.sh sin
# telegram_relay.py ni bury-telegram-relay.service.
# Garantias: valida TODO (archivos presentes, .py compilan) antes de tocar el destino; prepara los archivos nuevos en un directorio
# temporal DENTRO del destino y los publica con rename; si algo falla a mitad, REVIERTE los ya publicados; conserva `<archivo>.bak`
# de los .py reemplazados (sin seguir enlaces simbolicos); corrige permisos aunque el contenido coincida.
# NO toca /etc/bury-monitor (configuracion y secretos) y NO reinicia ni habilita servicios (lo informa al final).
set -uo pipefail
SRC="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DEST="${1:-/opt/theburyproject/scripts/monitoring}"
PY="${BURY_INSTALL_PYTHON:-python3}"
REQ=(monitor.py:0644 telegram_relay.py:0644 activar-canales.sh:0755 bury-monitor.service:0644 bury-telegram-relay.service:0644)
OPT=(config.example.json:0644 monitor.env.example:0644 telegram-relay.env.example:0644 isolated_smoke.py:0644)

FILES=()
for e in "${REQ[@]}"; do f="${e%%:*}"; [ -f "$SRC/$f" ] || { echo "FALLA: falta $SRC/$f (arbol incompleto); no se instalo nada" >&2; exit 2; }; FILES+=("$e"); done
for e in "${OPT[@]}"; do f="${e%%:*}"; [ -f "$SRC/$f" ] && FILES+=("$e"); done
for f in monitor.py telegram_relay.py; do
  "$PY" -c 'import sys; compile(open(sys.argv[1], "rb").read(), sys.argv[1], "exec")' "$SRC/$f" 2>/dev/null || { echo "FALLA: $f no compila; no se instalo nada" >&2; exit 2; }
done
[ "$(id -u)" = 0 ] || [ -n "${BURY_INSTALL_ANY_USER:-}" ] || { echo "ejecutar como root (sudo)" >&2; exit 2; }

mkdir -p "$DEST" || exit 1
[ -L "$DEST" ] && { echo "FALLA: el destino es un enlace simbolico; no se instalo nada" >&2; exit 2; }
STAGE="$(mktemp -d "$DEST/.instalacion.XXXXXX")" || { echo "FALLA: no se pudo crear el directorio temporal en $DEST" >&2; exit 1; }
trap 'rm -rf "$STAGE"' EXIT
mkdir "$STAGE/new" "$STAGE/old" || exit 1

# 1) Preparar todo (nada del destino cambia todavia) y respaldar lo que se va a reemplazar.
CHANGED=(); PUBLISHED=()
for e in "${FILES[@]}"; do
  f="${e%%:*}"; mode="${e##*:}"
  install -m "$mode" "$SRC/$f" "$STAGE/new/$f" || { echo "FALLA: no se pudo preparar $f; no se cambio nada" >&2; exit 1; }
  if [ -e "$DEST/$f" ] && [ ! -L "$DEST/$f" ]; then
    cp -p "$DEST/$f" "$STAGE/old/$f" || { echo "FALLA: no se pudo respaldar $f; no se cambio nada" >&2; exit 1; }
  elif [ -L "$DEST/$f" ]; then
    echo "FALLA: $DEST/$f es un enlace simbolico; no se instalo nada" >&2; exit 2
  fi
  if [ ! -e "$DEST/$f" ] || ! cmp -s "$SRC/$f" "$DEST/$f"; then CHANGED+=("$f"); fi
done

# 2) Publicar con rename (mismo filesystem); ante cualquier error, revertir lo publicado.
revert() {
  echo "Revirtiendo lo ya publicado..." >&2
  for f in "${PUBLISHED[@]}"; do
    if [ -e "$STAGE/old/$f" ]; then mv -f "$STAGE/old/$f" "$DEST/$f"; else rm -f "$DEST/$f"; fi
  done
}
for f in "${CHANGED[@]}"; do
  if ! mv -f "$STAGE/new/$f" "$DEST/$f"; then echo "FALLA: no se pudo publicar $f" >&2; revert; exit 1; fi
  PUBLISHED+=("$f")
done
# 3) Permisos correctos aunque el contenido ya coincidiera.
for e in "${FILES[@]}"; do chmod "${e##*:}" "$DEST/${e%%:*}" 2>/dev/null || echo "AVISO: no se pudieron fijar permisos de ${e%%:*}" >&2; done
# 4) .bak de los .py reemplazados (rm previo: nunca se sigue un enlace simbolico preexistente).
for f in "${CHANGED[@]}"; do
  if [[ "$f" == *.py ]] && [ -e "$STAGE/old/$f" ]; then rm -f "$DEST/$f.bak"; cp -p "$STAGE/old/$f" "$DEST/$f.bak" || echo "AVISO: no se pudo crear $f.bak" >&2; fi
  echo "instalado: $f"
done
if [ "${#CHANGED[@]}" = 0 ]; then echo "Sin cambios de contenido: $DEST ya esta al dia (permisos verificados)."; exit 0; fi
echo "Listo (${#CHANGED[@]} archivo(s)). No se reinicio ningun servicio ni se toco /etc/bury-monitor."
echo "Para que el monitor use el monitor.py nuevo: sudo systemctl restart bury-monitor (los servicios ya activos siguen con el codigo cargado)."
