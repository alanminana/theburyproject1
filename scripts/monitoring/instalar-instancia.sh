#!/usr/bin/env bash
# P-06: genera (y, solo con --apply, instala) una SEGUNDA instancia del monitor para otra instalacion en el mismo host
# (p. ej. produccion: proyecto Compose theburyproject-prod, respaldos en /srv/bury-backups-prod).
# Automatiza el procedimiento manual de docs/p06-cierre-tecnico-dos-instalaciones.md §8, sin tocar la instancia existente.
#
#   scripts/monitoring/instalar-instancia.sh --name prod --project theburyproject-prod --backup-dir /srv/bury-backups-prod \
#       --base-url https://tbp-prod:8443 [--keep-offsite] [--out DIR]          (solo genera)
#   sudo scripts/monitoring/instalar-instancia.sh ... --apply                                              (instala y habilita)
#
# Por defecto NO toca el sistema: escribe config.json, la unidad systemd y un resumen en --out (directorio temporal si se omite) y sale.
# --apply (root): copia monitor.env de la instancia base (mismo relay/webhook; 0600), crea /etc/bury-monitor-<name>/, la unidad
#   bury-monitor-<name>.service y hace `systemctl enable --now`. No sobrescribe una instancia ya instalada (falla cerrado).
# Nunca lee ni imprime el contenido de monitor.env.
set -euo pipefail
SRC="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
NAME="" PROJECT="" BACKUP_DIR="" BASE_URL="" KEEP_OFFSITE=0 OUT="" APPLY=0
ETC_ROOT="${BURY_MONITOR_ETC_ROOT:-/etc}" UNIT_DIR="${BURY_MONITOR_UNIT_DIR:-/etc/systemd/system}" PY="${BURY_INSTALL_PYTHON:-python3}"
BASE_ETC="${BURY_MONITOR_BASE_ETC:-$ETC_ROOT/bury-monitor}" BASE_UNIT="${BURY_MONITOR_BASE_UNIT:-$SRC/bury-monitor.service}"
usage() { sed -n '2,13p' "${BASH_SOURCE[0]}" >&2; exit 2; }
while [ $# -gt 0 ]; do
  case "$1" in
    --name) NAME="${2:-}"; shift 2;; --project) PROJECT="${2:-}"; shift 2;; --backup-dir) BACKUP_DIR="${2:-}"; shift 2;;
    --base-url) BASE_URL="${2:-}"; shift 2;; --out) OUT="${2:-}"; shift 2;;
    --keep-offsite) KEEP_OFFSITE=1; shift;; --apply) APPLY=1; shift;; -h|--help) usage;; *) echo "argumento desconocido: $1" >&2; usage;;
  esac
done
[[ "$NAME" =~ ^[a-z][a-z0-9-]{0,20}$ ]] || { echo "FALLA: --name invalido (minusculas, digitos y guiones)" >&2; exit 2; }
[ "$NAME" != "base" ] || { echo "FALLA: --name no puede ser 'base'" >&2; exit 2; }
[[ "$PROJECT" =~ ^[a-z0-9][a-z0-9_-]*$ ]] || { echo "FALLA: --project invalido" >&2; exit 2; }
[[ "$BACKUP_DIR" =~ ^/[A-Za-z0-9._/-]+$ ]] && [[ "$BACKUP_DIR" != *..* ]] || { echo "FALLA: --backup-dir debe ser una ruta absoluta simple" >&2; exit 2; }
[[ "$BASE_URL" =~ ^https?://[A-Za-z0-9._:-]+(/[A-Za-z0-9._~/-]*)?$ ]] || { echo "FALLA: --base-url invalido" >&2; exit 2; }
[ -f "$SRC/config.example.json" ] && [ -f "$BASE_UNIT" ] || { echo "FALLA: arbol incompleto (config.example.json / bury-monitor.service)" >&2; exit 2; }

ETC_DIR="$ETC_ROOT/bury-monitor-$NAME"; SVC="bury-monitor-$NAME"; STATE="bury-monitor-$NAME"
if [ -z "$OUT" ]; then OUT="$(mktemp -d)"; else mkdir -p "$OUT"; fi

# 1) config.json: parte de config.example.json (la base real puede tener ajustes propios; aqui se usa el ejemplo versionado).
"$PY" - "$SRC/config.example.json" "$OUT/config.json" "$PROJECT" "$BACKUP_DIR" "/var/lib/$STATE" "$BASE_URL" "$KEEP_OFFSITE" <<'PYEOF'
import json, sys
src, dst, project, backup_dir, state_dir, base_url, keep = sys.argv[1:8]
with open(src, encoding="utf-8") as f:
    cfg = json.load(f)
cfg.update(project=project, backup_dir=backup_dir, state_dir=state_dir, base_url=base_url)
if keep != "1":
    for k in ("offsite-sql", "offsite-files"):
        cfg["backup_windows"].pop(k, None)
    cfg["offsite_restore_test"] = {"enabled": False}
with open(dst, "w", encoding="utf-8") as f:
    json.dump(cfg, f, indent=2); f.write("\n")
PYEOF

# 2) Unidad systemd: derivada de la de la base (mismas restricciones), con rutas y StateDirectory propios.
sed -e "s#/etc/bury-monitor/#/etc/bury-monitor-$NAME/#g" \
    -e "s#StateDirectory=bury-monitor#StateDirectory=$STATE#" \
    -e "s#/var/lib/bury-monitor#/var/lib/$STATE#g" \
    -e "s#TheBuryProject host monitoring#TheBuryProject $NAME host monitoring#" "$BASE_UNIT" > "$OUT/$SVC.service"
grep -q "bury-monitor-$NAME/config.json" "$OUT/$SVC.service" && grep -q "StateDirectory=$STATE" "$OUT/$SVC.service" \
  || { echo "FALLA: la unidad generada no quedo aislada de la instancia base" >&2; exit 1; }
if grep -E '/etc/bury-monitor/|/var/lib/bury-monitor([^-]|$)' "$OUT/$SVC.service" >/dev/null; then
  echo "FALLA: la unidad generada aun referencia la instancia base" >&2; exit 1; fi

echo "Generado en $OUT: config.json, $SVC.service"
echo "Instancia: $NAME | proyecto: $PROJECT | respaldos: $BACKUP_DIR | estado: /var/lib/$STATE | etc: $ETC_DIR"
[ "$KEEP_OFFSITE" = 1 ] || echo "Ventanas offsite-sql/offsite-files retiradas (sin copia externa). Usar --keep-offsite cuando exista."
[ "$APPLY" = 1 ] || { echo "Modo solo-generar: no se instalo nada. Para instalar: sudo $0 ... --apply"; exit 0; }

# 3) --apply
[ "$(id -u)" = 0 ] || [ -n "${BURY_INSTALL_ANY_USER:-}" ] || { echo "FALLA: --apply requiere root" >&2; exit 2; }
[ ! -e "$ETC_DIR" ] && [ ! -e "$UNIT_DIR/$SVC.service" ] || { echo "FALLA: la instancia '$NAME' ya existe; no se sobrescribe" >&2; exit 2; }
[ -f "$BASE_ETC/monitor.env" ] || { echo "FALLA: falta $BASE_ETC/monitor.env (canal de alertas de la instancia base)" >&2; exit 2; }
"$PY" -c 'import sys; compile(open(sys.argv[1], "rb").read(), sys.argv[1], "exec")' /opt/theburyproject/scripts/monitoring/monitor.py 2>/dev/null \
  || [ -n "${BURY_INSTALL_ANY_USER:-}" ] || { echo "FALLA: monitor.py no esta instalado en /opt/theburyproject/scripts/monitoring (correr instalar-agente.sh)" >&2; exit 2; }
install -d -m 0750 "$ETC_DIR"
install -m 0600 "$BASE_ETC/monitor.env" "$ETC_DIR/monitor.env"
install -m 0644 "$OUT/config.json" "$ETC_DIR/config.json"
install -m 0644 "$OUT/$SVC.service" "$UNIT_DIR/$SVC.service"
if [ -z "${BURY_INSTALL_ANY_USER:-}" ]; then
  systemctl daemon-reload
  systemctl enable --now "$SVC"
  systemctl is-active --quiet "$SVC" && echo "OK: $SVC activo" || { echo "FALLA: $SVC no quedo activo (journalctl -u $SVC)" >&2; exit 1; }
fi
echo "Verificar con una falla provocada en un directorio de PRUEBA antes de dar la alerta por probada."
