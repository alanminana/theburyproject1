#!/usr/bin/env bash
# Instala/desinstala la programacion automatica de backups en un servidor Linux (cron del sistema, /etc/cron.d/bury-backup).
# No requiere SQL Server Agent (Express no lo trae) ni nada dentro de ASP.NET.
#
#   scripts/backup/install-schedule.sh --print                 muestra el archivo cron que se instalaria (no cambia nada)
#   sudo scripts/backup/install-schedule.sh --install          lo instala en /etc/cron.d/bury-backup
#   sudo scripts/backup/install-schedule.sh --uninstall        lo elimina
#
# Opciones: --full-at HH:MM (default 02:30, HORA LOCAL del servidor)  --log-every MIN (default 15, divisor de 60)
#           --restore-test-day 0-6 (default 0 = domingo, 04:30)       --user USUARIO (default root; debe poder usar docker)
# Programa:  full+archivos diario | log de transacciones cada N min | prueba de restore semanal.
# Los nombres de archivo de backup usan siempre UTC; solo los HORARIOS de cron usan la hora local del servidor.
set -Eeuo pipefail
REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
ACTION="" FULL_AT="02:30" LOG_EVERY=15 RT_DAY=0 RUN_USER=root
while (( $# )); do
  case "$1" in
    --print|--install|--uninstall) ACTION="${1#--}"; shift ;;
    --full-at) FULL_AT="${2:-}"; shift 2 ;;
    --log-every) LOG_EVERY="${2:-}"; shift 2 ;;
    --restore-test-day) RT_DAY="${2:-}"; shift 2 ;;
    --user) RUN_USER="${2:-}"; shift 2 ;;
    -h|--help) sed -n '2,13p' "$0"; exit 0 ;;
    *) echo "argumento desconocido: $1" >&2; exit 1 ;;
  esac
done
[[ -n "$ACTION" ]] || { sed -n '2,13p' "$0"; exit 1; }
[[ "$FULL_AT" =~ ^([01][0-9]|2[0-3]):([0-5][0-9])$ ]] || { echo "--full-at debe ser HH:MM" >&2; exit 1; }
[[ "$LOG_EVERY" =~ ^[0-9]+$ ]] && (( LOG_EVERY >= 1 && LOG_EVERY <= 60 && 60 % LOG_EVERY == 0 )) || { echo "--log-every debe dividir 60 (1,2,3,5,10,15,20,30,60)" >&2; exit 1; }
[[ "$RT_DAY" =~ ^[0-6]$ ]] || { echo "--restore-test-day debe ser 0-6" >&2; exit 1; }
[[ "$RUN_USER" =~ ^[a-z_][a-z0-9_-]*$ ]] || { echo "--user invalido" >&2; exit 1; }
[[ "$REPO_DIR" =~ ^/[A-Za-z0-9_./-]+$ ]] || { echo "la ruta del repo ($REPO_DIR) tiene caracteres no soportados en cron.d" >&2; exit 1; }

FILE=/etc/cron.d/bury-backup
HH=${FULL_AT%%:*}; MM=${FULL_AT##*:}; HH=$((10#$HH)); MM=$((10#$MM))
RT_MM=$(( (MM + 60) % 60 )); RT_HH=$(( (HH + 2) % 24 ))
S="$REPO_DIR/scripts/backup"
render() {
cat <<CRON
# Generado por scripts/backup/install-schedule.sh - NO editar a mano (reinstalar para cambiar).
# Codigos de salida de los scripts: ver docs/backup-restore.md. stdout se descarta (ya va a \$BACKUP_DIR/logs/backup.log); stderr
# (solo errores) queda en /var/log/bury-backup-errors.log: un archivo no vacio o un exit != 0 indica un backup fallido.
SHELL=/bin/bash
PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin

# full SQL + archivos (detiene app unos segundos) + copia externa + retencion, diario
$MM $HH * * *  $RUN_USER  cd $REPO_DIR && /bin/bash $S/backup.sh all >/dev/null 2>>/var/log/bury-backup-errors.log
# backup del log de transacciones (RPO ~ $LOG_EVERY min)
*/$LOG_EVERY * * * *  $RUN_USER  cd $REPO_DIR && /bin/bash $S/backup.sh log >/dev/null 2>>/var/log/bury-backup-errors.log
# prueba de restore (base temporal <db>_RestoreTest; no toca la base real), semanal
$RT_MM $RT_HH * * $RT_DAY  $RUN_USER  cd $REPO_DIR && /bin/bash $S/restore-test.sh >/dev/null 2>>/var/log/bury-backup-errors.log
CRON
}

case "$ACTION" in
  print) render ;;
  install)
    [[ $EUID -eq 0 ]] || { echo "--install requiere root (sudo)" >&2; exit 1; }
    tmp=$(mktemp); render >"$tmp"; install -m 0644 -o root -g root "$tmp" "$FILE"; rm -f "$tmp"
    touch /var/log/bury-backup-errors.log; chmod 600 /var/log/bury-backup-errors.log
    echo "instalado: $FILE"; echo "programacion:"; grep -vE '^(#|$|SHELL|PATH)' "$FILE" ;;
  uninstall)
    [[ $EUID -eq 0 ]] || { echo "--uninstall requiere root (sudo)" >&2; exit 1; }
    rm -f "$FILE"; echo "eliminado: $FILE" ;;
esac
