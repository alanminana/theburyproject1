#!/usr/bin/env bash
# Instala/desinstala la programacion automatica de backups en un servidor Linux (cron del sistema, /etc/cron.d/bury-backup).
# No requiere SQL Server Agent (Express no lo trae) ni nada dentro de ASP.NET.
#
#   scripts/backup/install-schedule.sh --print                 muestra el archivo cron que se instalaria (no cambia nada)
#   sudo scripts/backup/install-schedule.sh --install          lo instala en /etc/cron.d/bury-backup
#   scripts/backup/install-schedule.sh --print-logrotate       muestra la config de logrotate que se instalaria (no cambia nada)
#   sudo scripts/backup/install-schedule.sh --uninstall        elimina cron y logrotate (deja el log)
# --install tambien instala /etc/logrotate.d/bury-backup y deja /var/log/bury-backup-errors.log con owner = usuario del cron, 0600.
# Test: BURY_SCHEDULE_SANDBOX=<dir> redirige TODAS las rutas del sistema a <dir> (sin root; ver scripts/backup/test-hardening.sh).
#
# Opciones: --full-at HH:MM (default 02:30, HORA LOCAL del servidor)  --log-every MIN (default 15, divisor de 60)
#           --restore-test-day 0-6 (default 0 = domingo, 04:30)       --user USUARIO (default root; debe poder usar docker)
#           --log-offset MIN  minuto de inicio del log de transacciones (0..LOG_EVERY-1; escalona dos instalaciones: 7 -> 7,22,37,52)
#           --project PROYECTO  proyecto Compose de ESTA instalacion. Con un proyecto distinto del default (theburyproject) el cron lleva
#           COMPOSE_PROJECT_NAME=PROYECTO y los archivos se llaman bury-backup-<sufijo> (theburyproject-prod -> /etc/cron.d/bury-backup-prod,
#           /etc/logrotate.d/bury-backup-prod, /var/log/bury-backup-prod-errors.log): una segunda instalacion NO pisa el cron de la primera.
#           --install se NIEGA a reemplazar un cron que apunta a OTRO clon del repo.
# Programa:  full+archivos diario | log de transacciones cada N min | prueba de restore semanal | verificacion de custodia semanal
#            (check-custody.sh, solo lectura; la prueba de restore DESDE la copia externa -restore-test.sh --offsite- NO se programa
#            sola: es pesada, se agrega a mano mensualmente cuando haya copia externa, ver docs/backup-custodia.md).
# Los nombres de archivo de backup usan siempre UTC; solo los HORARIOS de cron usan la hora local del servidor.
set -Eeuo pipefail
REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
ACTION="" FULL_AT="02:30" LOG_EVERY=15 RT_DAY=0 RUN_USER=root PROJECT="" LOG_OFFSET=0
while (( $# )); do
  case "$1" in
    --print|--print-logrotate|--install|--uninstall) ACTION="${1#--}"; shift ;;
    --full-at) FULL_AT="${2:-}"; shift 2 ;;
    --log-every) LOG_EVERY="${2:-}"; shift 2 ;;
    --restore-test-day) RT_DAY="${2:-}"; shift 2 ;;
    --user) RUN_USER="${2:-}"; shift 2 ;;
    --project) PROJECT="${2:-}"; shift 2 ;;
    --log-offset) LOG_OFFSET="${2:-}"; shift 2 ;;
    -h|--help) sed -n '2,24p' "$0"; exit 0 ;;
    *) echo "argumento desconocido: $1" >&2; exit 1 ;;
  esac
done
[[ -n "$ACTION" ]] || { sed -n '2,24p' "$0"; exit 1; }
[[ "$FULL_AT" =~ ^([01][0-9]|2[0-3]):([0-5][0-9])$ ]] || { echo "--full-at debe ser HH:MM" >&2; exit 1; }
[[ "$LOG_EVERY" =~ ^[0-9]+$ ]] && (( LOG_EVERY >= 1 && LOG_EVERY <= 60 && 60 % LOG_EVERY == 0 )) || { echo "--log-every debe dividir 60 (1,2,3,5,10,15,20,30,60)" >&2; exit 1; }
[[ "$LOG_OFFSET" =~ ^[0-9]+$ ]] && (( LOG_OFFSET < LOG_EVERY )) || { echo "--log-offset debe ser un entero entre 0 y --log-every menos 1" >&2; exit 1; }
[[ "$RT_DAY" =~ ^[0-6]$ ]] || { echo "--restore-test-day debe ser 0-6" >&2; exit 1; }
[[ "$RUN_USER" =~ ^[a-z_][a-z0-9_-]*$ ]] || { echo "--user invalido" >&2; exit 1; }
[[ -z "$PROJECT" || "$PROJECT" =~ ^[a-z0-9][a-z0-9_-]{0,62}$ ]] || { echo "--project invalido (minusculas, digitos, - y _)" >&2; exit 1; }
[[ "$REPO_DIR" =~ ^/[A-Za-z0-9_./-]+$ ]] || { echo "la ruta del repo ($REPO_DIR) tiene caracteres no soportados en cron.d" >&2; exit 1; }

ROOT_PFX="${BURY_SCHEDULE_SANDBOX:-}"   # vacio en produccion
# Sufijo de instancia: vacio para el proyecto por defecto (compatibilidad con las instalaciones existentes).
SUF=""; PROJ_ENV=""
if [[ -n "$PROJECT" && "$PROJECT" != theburyproject ]]; then
  SUF="-${PROJECT#theburyproject-}"
  [[ "$SUF" =~ ^-[a-z0-9][a-z0-9_-]*$ ]] || { echo "--project no produce un sufijo valido" >&2; exit 1; }
fi
[[ -n "$PROJECT" ]] && PROJ_ENV="COMPOSE_PROJECT_NAME=$PROJECT "
FILE="$ROOT_PFX/etc/cron.d/bury-backup$SUF"
LOGROTATE_FILE="$ROOT_PFX/etc/logrotate.d/bury-backup$SUF"
ERR_LOG=/var/log/bury-backup$SUF-errors.log   # ruta que va en el cron (literal); el archivo real se prepara bajo $ROOT_PFX
ERR_LOG_REAL="$ROOT_PFX$ERR_LOG"
LOG_SPEC="*/$LOG_EVERY"
if (( LOG_OFFSET > 0 )); then LOG_SPEC=$LOG_OFFSET; for (( m = LOG_OFFSET + LOG_EVERY; m < 60; m += LOG_EVERY )); do LOG_SPEC+=",$m"; done; fi
HH=${FULL_AT%%:*}; MM=${FULL_AT##*:}; HH=$((10#$HH)); MM=$((10#$MM))
RT_MM=$(( (MM + 60) % 60 )); RT_HH=$(( (HH + 2) % 24 ))
CU_HH=$(( (HH + 3) % 24 ))   # custodia: una hora despues del restore-test
S="$REPO_DIR/scripts/backup"
render() {
cat <<CRON
# Generado por scripts/backup/install-schedule.sh - NO editar a mano (reinstalar para cambiar).
# Codigos de salida de los scripts: ver docs/backup-restore.md. stdout se descarta (ya va a \$BACKUP_DIR/logs/backup.log); stderr
# (solo errores) queda en $ERR_LOG (rotado por /etc/logrotate.d/bury-backup$SUF): un archivo no vacio o un exit != 0 indica un backup fallido.
SHELL=/bin/bash
PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin

# full SQL + archivos (detiene app unos segundos) + copia externa + retencion, diario
$MM $HH * * *  $RUN_USER  cd $REPO_DIR && $PROJ_ENV/bin/bash $S/backup.sh all >/dev/null 2>>$ERR_LOG
# backup del log de transacciones (RPO ~ $LOG_EVERY min)
$LOG_SPEC * * * *  $RUN_USER  cd $REPO_DIR && $PROJ_ENV/bin/bash $S/backup.sh log >/dev/null 2>>$ERR_LOG
# prueba de restore (base temporal <db>_RestoreTest; no toca la base real), semanal
$RT_MM $RT_HH * * $RT_DAY  $RUN_USER  cd $REPO_DIR && $PROJ_ENV/bin/bash $S/restore-test.sh >/dev/null 2>>$ERR_LOG
# verificacion de custodia de claves (solo lectura; publica monitor-status/custody.json; exit 11 = custodia insegura), semanal
$RT_MM $CU_HH * * $RT_DAY  $RUN_USER  cd $REPO_DIR && $PROJ_ENV/bin/bash $S/check-custody.sh --quiet >/dev/null 2>>$ERR_LOG
CRON
}

render_logrotate() {
cat <<LR
# Generado por scripts/backup/install-schedule.sh. cron abre el log en modo append en cada corrida (no hace falta copytruncate).
$ERR_LOG {
    weekly
    maxsize 10M
    rotate 8
    compress
    delaycompress
    missingok
    notifempty
    create 0600 $RUN_USER $RUN_GROUP
}
LR
}

run_group() { if [[ -n "$ROOT_PFX" || $EUID -eq 0 ]] && id -gn "$RUN_USER" >/dev/null 2>&1; then id -gn "$RUN_USER"; else echo "$RUN_USER"; fi; }
RUN_GROUP=$(run_group)

# Deja el log de errores escribible por el usuario del cron y por nadie mas. Falla claro si no puede: un log root:600 hace que
# la redireccion `2>>` del cron falle y el job NO se ejecute.
prepare_err_log() {
  id -u "$RUN_USER" >/dev/null 2>&1 || { echo "el usuario '$RUN_USER' no existe en este sistema" >&2; exit 1; }
  local dir; dir=$(dirname "$ERR_LOG_REAL")
  mkdir -p "$dir" || { echo "no se pudo crear $dir" >&2; exit 1; }
  { [[ -e "$ERR_LOG_REAL" ]] || : >"$ERR_LOG_REAL"; }     && chown "$RUN_USER:" "$ERR_LOG_REAL" && chmod 0600 "$ERR_LOG_REAL"     || { echo "no se pudo preparar $ERR_LOG_REAL para el usuario $RUN_USER (owner/permisos)" >&2; exit 1; }
  [[ "$(stat -c %U "$ERR_LOG_REAL")" == "$RUN_USER" ]] || { echo "$ERR_LOG_REAL no quedo con owner $RUN_USER" >&2; exit 1; }
}

# No tocar el cron de OTRA instalacion: un archivo existente debe apuntar a ESTE clon y a ESTE proyecto Compose. Dos --project distintos pueden dar el mismo
# sufijo de archivo (p. ej. "prod" y "theburyproject-prod"): la identidad real es el proyecto grabado en el cron. Sin COMPOSE_PROJECT_NAME = theburyproject.
guard_not_foreign() {
  [[ -f "$FILE" ]] || return 0
  local prev_repo prev_proj want_proj=${PROJECT:-theburyproject}
  prev_repo=$(grep -vE '^[[:space:]]*#' "$FILE" | sed -nE 's;.*[[:space:]]cd (/[^ ]+) &&.*;\1;p' | head -n1)
  prev_proj=$(grep -vE '^[[:space:]]*#' "$FILE" | sed -nE 's;^(.*[[:space:]])?COMPOSE_PROJECT_NAME=([^[:space:]]+).*;\2;p' | head -n1)
  prev_proj=$(printf '%s' "$prev_proj" | tr -d "\"'")   # cron.d admite COMPOSE_PROJECT_NAME="x" o 'x'
  if [[ -n "$prev_repo" && "$prev_repo" != "$REPO_DIR" ]]; then
    echo "$FILE ya existe y pertenece a otro clon ($prev_repo), no a $REPO_DIR: NO se modifica. Para una segunda instalacion usar --project <proyecto> (archivo propio); si es el mismo servicio movido de carpeta, eliminarlo antes con --uninstall desde el clon anterior" >&2; exit 1
  fi
  if [[ "${prev_proj:-theburyproject}" != "$want_proj" ]]; then
    echo "$FILE ya existe y pertenece al proyecto '${prev_proj:-theburyproject}', no a '$want_proj': NO se modifica (dos nombres de --project pueden dar el mismo archivo). Usar el --project original o eliminar ese cron a mano" >&2; exit 1
  fi
}

case "$ACTION" in
  print) render ;;
  print-logrotate) render_logrotate ;;
  install)
    [[ -n "$ROOT_PFX" || $EUID -eq 0 ]] || { echo "--install requiere root (sudo)" >&2; exit 1; }
    guard_not_foreign
    prepare_err_log
    mkdir -p "$(dirname "$FILE")" "$(dirname "$LOGROTATE_FILE")"
    tmp=$(mktemp); render >"$tmp"
    if [[ -n "$ROOT_PFX" ]]; then install -m 0644 "$tmp" "$FILE"; else install -m 0644 -o root -g root "$tmp" "$FILE"; fi
    render_logrotate >"$tmp"
    if [[ -n "$ROOT_PFX" ]]; then install -m 0644 "$tmp" "$LOGROTATE_FILE"; else install -m 0644 -o root -g root "$tmp" "$LOGROTATE_FILE"; fi
    rm -f "$tmp"
    echo "instalado: $FILE y $LOGROTATE_FILE (log: $ERR_LOG_REAL, owner $RUN_USER:$RUN_GROUP, 0600)"; echo "programacion:"; grep -vE '^(#|$|SHELL|PATH)' "$FILE" ;;
  uninstall)
    [[ -n "$ROOT_PFX" || $EUID -eq 0 ]] || { echo "--uninstall requiere root (sudo)" >&2; exit 1; }
    guard_not_foreign
    rm -f "$FILE" "$LOGROTATE_FILE"; echo "eliminado: $FILE y $LOGROTATE_FILE (el log de errores se conserva)" ;;
esac
