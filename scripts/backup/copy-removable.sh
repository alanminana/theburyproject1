#!/usr/bin/env bash
# Copia las cadenas de backup COMPLETAS de BACKUP_DIR a un disco EXTRAIBLE (ruta montada) cuando esta disponible. Nunca falla un backup:
# si el disco no esta, queda "atrasada" (monitor-status/removable-copy.json) y el monitor alerta por antiguedad del ultimo exito.
#
#   scripts/backup/copy-removable.sh              --dry-run (default): dice que copiaria; NO escribe en el destino
#   scripts/backup/copy-removable.sh --apply      copia y verifica (sha256) las cadenas que falten, de la mas nueva a la mas antigua
#   scripts/backup/copy-removable.sh --apply --strict   exit 1 si el destino no esta disponible o quedo atrasada (uso manual)
#
# Variables: BACKUP_REMOVABLE_DIR (ruta ABSOLUTA ya montada, p.ej. /mnt/respaldo o, en Windows via WSL, /mnt/e; sin letra de unidad fija),
# BACKUP_REMOVABLE_MARKER (archivo-marca en la raiz del destino; default .bury-backup-removable: crearlo UNA vez con touch en el disco correcto),
# BACKUP_REMOVABLE_REQUIRE_MOUNT (true: la ruta debe ser un punto de montaje real), BACKUP_REMOVABLE_MAX_TOTAL_GB / _MIN_FREE_GB (limites del
# destino; misma retencion por cadenas). Detalle: docs/backup-restore.md §8.2.
# Codigos de salida: 0 ok (o destino ausente sin --strict) | 1 uso/config o --strict atrasada | 4 ocupado | 8 prerequisito
# shellcheck source=lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
# shellcheck source=lib-capacity.sh
source "$BK_SCRIPT_DIR/lib-capacity.sh"

APPLY=0 REMOVABLE_STRICT=0
while (( $# )); do
  case "$1" in
    --apply) APPLY=1; shift ;;
    --dry-run) APPLY=0; shift ;;
    --strict) REMOVABLE_STRICT=1; shift ;;
    -h|--help) sed -n '2,13p' "$0"; exit 0 ;;
    *) echo "argumento desconocido: $1" >&2; exit $EX_USAGE ;;
  esac
done
export REMOVABLE_STRICT

load_config
load_capacity_config
[[ -n "$BACKUP_REMOVABLE_DIR" ]] || die $EX_USAGE "BACKUP_REMOVABLE_DIR no esta definido"
[[ -d "$BACKUP_DIR" && -f "$BACKUP_DIR/.bury-backup-root" ]] || die $EX_USAGE "BACKUP_DIR=$BACKUP_DIR no es un directorio administrado por backup.sh"
LOG_FILE="$BACKUP_DIR/logs/backup.log"
if (( APPLY )); then
  lrc=0; acquire_lock || lrc=$?
  if (( lrc == 1 )); then exit $EX_BUSY; elif (( lrc != 0 )); then die $EX_PREREQ "no se pudo tomar el lock de backup"; fi
  trap 'release_lock' EXIT
fi
rc=0; removable_sync "$APPLY" || rc=$?
exit $rc
