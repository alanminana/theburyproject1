#!/usr/bin/env bash
# P-06: genera (y, solo con --apply, instala) una SEGUNDA instancia del monitor para otra instalacion en el mismo host
# (p. ej. produccion: proyecto Compose theburyproject-prod, respaldos en /srv/bury-backups-prod).
# Automatiza el procedimiento manual de docs/p06-cierre-tecnico-dos-instalaciones.md §8, sin tocar la instancia existente.
#
#   scripts/monitoring/instalar-instancia.sh --name prod --project theburyproject-prod --backup-dir /srv/bury-backups-prod \
#       --base-url https://tbp-prod:8443 [--keep-offsite] [--keep-heartbeat] [--ca-file CA.crt] [--out DIR]   (solo genera)
#   sudo scripts/monitoring/instalar-instancia.sh ... --apply                                              (instala y habilita)
#
# Por defecto NO toca el sistema: escribe config.json, la unidad systemd y un resumen en --out (directorio temporal si se omite) y sale.
# --apply (root): copia monitor.env de la instancia base (mismo relay/webhook; 0600) SIN las lineas BURY_MONITOR_HEARTBEAT* (un heartbeat
#   compartido haria que el latido de una instancia tape la caida de la otra; --keep-heartbeat lo conserva), crea /etc/bury-monitor-<name>/, la unidad
#   bury-monitor-<name>.service y hace `systemctl enable --now`. No sobrescribe una instancia ya instalada (falla cerrado).
# --ca-file: CA publica de la instancia (p. ej. la raiz de Caddy de produccion); se instala en /etc/bury-monitor-<name>/ y la unidad recibe
#   Environment=SSL_CERT_FILE=... (sin eso el monitor marca collector.tls CRITICAL si el sistema no confia en esa CA).
# Nunca imprime el contenido de monitor.env (solo se filtran por nombre las lineas de heartbeat).
set -euo pipefail
SRC="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
NAME="" PROJECT="" BACKUP_DIR="" BASE_URL="" KEEP_OFFSITE=0 KEEP_HEARTBEAT=0 CA_FILE="" OUT="" APPLY=0
ETC_ROOT="${BURY_MONITOR_ETC_ROOT:-/etc}" UNIT_DIR="${BURY_MONITOR_UNIT_DIR:-/etc/systemd/system}" PY="${BURY_INSTALL_PYTHON:-python3}"
BASE_ETC="${BURY_MONITOR_BASE_ETC:-$ETC_ROOT/bury-monitor}" BASE_UNIT="${BURY_MONITOR_BASE_UNIT:-$SRC/bury-monitor.service}"
usage() { sed -n '2,13p' "${BASH_SOURCE[0]}" >&2; exit 2; }
while [ $# -gt 0 ]; do
  case "$1" in
    --name) NAME="${2:-}"; shift 2;; --project) PROJECT="${2:-}"; shift 2;; --backup-dir) BACKUP_DIR="${2:-}"; shift 2;;
    --base-url) BASE_URL="${2:-}"; shift 2;; --out) OUT="${2:-}"; shift 2;;
    --keep-offsite) KEEP_OFFSITE=1; shift;; --keep-heartbeat) KEEP_HEARTBEAT=1; shift;; --ca-file) CA_FILE="${2:-}"; shift 2;; --apply) APPLY=1; shift;; -h|--help) usage;; *) echo "argumento desconocido: $1" >&2; usage;;
  esac
done
[[ "$NAME" =~ ^[a-z][a-z0-9-]{0,20}$ ]] || { echo "FALLA: --name invalido (minusculas, digitos y guiones)" >&2; exit 2; }
[ "$NAME" != "base" ] || { echo "FALLA: --name no puede ser 'base'" >&2; exit 2; }
[[ "$PROJECT" =~ ^[a-z0-9][a-z0-9_-]*$ ]] || { echo "FALLA: --project invalido" >&2; exit 2; }
[[ "$BACKUP_DIR" =~ ^/[A-Za-z0-9._/-]+$ ]] && [[ "$BACKUP_DIR" != *..* ]] || { echo "FALLA: --backup-dir debe ser una ruta absoluta simple" >&2; exit 2; }
[[ "$BASE_URL" =~ ^https?://[A-Za-z0-9._:-]+(/[A-Za-z0-9._~/-]*)?$ ]] || { echo "FALLA: --base-url invalido" >&2; exit 2; }
[ -f "$SRC/config.example.json" ] && [ -f "$BASE_UNIT" ] || { echo "FALLA: arbol incompleto (config.example.json / bury-monitor.service)" >&2; exit 2; }

[ -z "$CA_FILE" ] || grep -q 'BEGIN CERTIFICATE' "$CA_FILE" 2>/dev/null || { echo "FALLA: --ca-file no es un certificado PEM legible" >&2; exit 2; }

ETC_DIR="$ETC_ROOT/bury-monitor-$NAME"; SVC="bury-monitor-$NAME"; STATE="bury-monitor-$NAME"
# --apply: fallar cerrado ANTES de generar nada (si --out apuntara a /etc/bury-monitor-<name> o al directorio de unidades, generar ya sobrescribiria).
if [ "$APPLY" = 1 ]; then
  [ ! -e "$ETC_DIR" ] && [ ! -e "$UNIT_DIR/$SVC.service" ] || { echo "FALLA: la instancia '$NAME' ya existe; no se sobrescribe" >&2; exit 2; }
fi
if [ -z "$OUT" ]; then OUT="$(mktemp -d)"; else mkdir -p "$OUT"; fi
for d in "$ETC_DIR" "$UNIT_DIR"; do
  case "$(readlink -f "$OUT")/" in "$(readlink -f "$d")/"*) echo "FALLA: --out no puede estar dentro de $d" >&2; exit 2;; esac
done
# Los archivos de salida no pueden existir de antemano (ni como symlink): la escritura seguiria el enlace y pisaria otro archivo.
for f in "$OUT/config.json" "$OUT/$SVC.service"; do
  if [ -e "$f" ] || [ -L "$f" ]; then echo "FALLA: $f ya existe; usar un --out vacio" >&2; exit 2; fi
done

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
if [ -n "$CA_FILE" ]; then
  sed -i "s#^EnvironmentFile=.*#&\\nEnvironment=SSL_CERT_FILE=/etc/bury-monitor-$NAME/ca.crt#" "$OUT/$SVC.service"
fi
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
# Creacion atomica: mkdir falla si ya existe (carrera con otro proceso); solo se limpia lo que ESTE script creo.
CREATED=0 UNIT_CREATED=0
cleanup_apply() { rc=$?; if [ "$rc" != 0 ]; then [ "$CREATED" = 1 ] && rm -rf "$ETC_DIR"; [ "$UNIT_CREATED" = 1 ] && rm -f "$UNIT_DIR/$SVC.service"; fi; return 0; }
trap cleanup_apply EXIT
mkdir -m 0750 "$ETC_DIR" 2>/dev/null || { echo "FALLA: no se pudo crear $ETC_DIR (existe o sin permisos); no se sobrescribe" >&2; exit 2; }
CREATED=1
if [ "$KEEP_HEARTBEAT" = 1 ]; then install -m 0600 "$BASE_ETC/monitor.env" "$ETC_DIR/monitor.env"
else rc=0; ( umask 077; grep -v -E '^[[:space:]]*(export[[:space:]]+)?BURY_MONITOR_HEARTBEAT' "$BASE_ETC/monitor.env" > "$ETC_DIR/monitor.env" ) || rc=$?
  # grep: 0 = quedaron lineas, 1 = no quedo ninguna (aceptable); >=2 = error de lectura/escritura: no continuar con un canal vacio.
  [ "$rc" -le 1 ] && [ -s "$ETC_DIR/monitor.env" ] || { echo "FALLA: no se pudo copiar monitor.env (o quedo vacio); instalacion abortada" >&2; exit 1; }
  chmod 0600 "$ETC_DIR/monitor.env"; fi
[ -z "$CA_FILE" ] || install -m 0644 "$CA_FILE" "$ETC_DIR/ca.crt"
install -m 0644 "$OUT/config.json" "$ETC_DIR/config.json"
[ -e "$UNIT_DIR/$SVC.service" ] || [ -L "$UNIT_DIR/$SVC.service" ] && { echo "FALLA: la unidad ya existe; no se sobrescribe" >&2; exit 2; }
install -m 0644 "$OUT/$SVC.service" "$UNIT_DIR/$SVC.service" && UNIT_CREATED=1
if [ -z "${BURY_INSTALL_ANY_USER:-}" ]; then
  systemctl daemon-reload
  systemctl enable --now "$SVC"
  systemctl is-active --quiet "$SVC" && echo "OK: $SVC activo" || { echo "FALLA: $SVC no quedo activo (journalctl -u $SVC)" >&2; exit 1; }
fi
echo "Verificar con una falla provocada en un directorio de PRUEBA antes de dar la alerta por probada."
