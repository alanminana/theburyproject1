#!/usr/bin/env bash
# Retencion POR CAPACIDAD de BACKUP_DIR (disco de backups). Complementa la retencion por dias (backup.sh la corre sola tras cada backup).
#
#   scripts/backup/retention-capacity.sh              --dry-run (default): muestra que cadenas/archivos se borrarian; NO toca nada
#   scripts/backup/retention-capacity.sh --apply      aplica (toma el lock de backups; borra de la cadena mas antigua a la mas nueva)
#
# Limites (variables de .env o del entorno; 0 = sin limite; si ambos son 0 no hay nada que hacer):
#   BACKUP_MAX_TOTAL_GB   tope de GB que ocupan los backups gestionados (sql/ + files/)
#   BACKUP_MIN_FREE_GB    GB libres minimos que debe conservar el filesystem de BACKUP_DIR
#   BACKUP_CAPACITY_MIN_CHAINS  cadenas recuperables (full + sus logs + tar + manifiesto) que NUNCA se borran (default 2, >= 1)
# La unidad de borrado es la cadena; la ultima cadena completa jamas se borra: si aun asi no se cumple el limite, FALLA (exit 7) y el
# monitor alerta (monitor-status/capacity.json). Detalle: docs/backup-restore.md §8.2.
# Codigos de salida: 0 ok | 1 uso/config | 4 ocupado | 7 limite imposible de cumplir / fallo de borrado | 8 prerequisito
# shellcheck source=lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
# shellcheck source=lib-capacity.sh
source "$BK_SCRIPT_DIR/lib-capacity.sh"

APPLY=0
while (( $# )); do
  case "$1" in
    --apply) APPLY=1; shift ;;
    --dry-run) APPLY=0; shift ;;
    -h|--help) sed -n '2,15p' "$0"; exit 0 ;;
    *) echo "argumento desconocido: $1" >&2; exit $EX_USAGE ;;
  esac
done

load_config
load_capacity_config
[[ -d "$BACKUP_DIR" ]] || die $EX_USAGE "BACKUP_DIR=$BACKUP_DIR no existe"
BACKUP_DIR=$(cd "$BACKUP_DIR" && pwd -P)   # symlinks resueltos: el borrado ocurre solo dentro de la ruta real
[[ -n "$BACKUP_DIR" && "$BACKUP_DIR" != "/" ]] || die $EX_USAGE "BACKUP_DIR resuelve a '/' o vacio: se rechaza"
[[ -f "$BACKUP_DIR/.bury-backup-root" ]] || die $EX_USAGE "falta $BACKUP_DIR/.bury-backup-root: no es un directorio administrado por backup.sh; no se borra nada"
LOG_FILE="$BACKUP_DIR/logs/backup.log"
if (( ! CAPACITY_ENABLED )); then log INFO "retencion por capacidad: sin limites configurados (BACKUP_MAX_TOTAL_GB y BACKUP_MIN_FREE_GB = 0): nada que hacer"; exit 0; fi

if (( APPLY )); then
  lrc=0; acquire_lock || lrc=$?
  if (( lrc == 1 )); then exit $EX_BUSY; elif (( lrc != 0 )); then die $EX_PREREQ "no se pudo tomar el lock de backup"; fi
  trap 'release_lock' EXIT
fi
log INFO "retencion por capacidad ($([[ $APPLY == 1 ]] && echo APLICAR || echo DRY-RUN)): tope=${MAX_TOTAL_BYTES}B libre_min=${MIN_FREE_BYTES}B min_cadenas=$CAPACITY_MIN_CHAINS"
rc=0; capacity_run local "$APPLY" "$MAX_TOTAL_BYTES" "$MIN_FREE_BYTES" "$CAPACITY_MIN_CHAINS" || rc=$?
if (( APPLY )); then
  if (( rc == 0 )); then capacity_status 0 true "ok: $CAP_LAST"; else capacity_status "$EX_RETENTION" false "disco insuficiente o fallo: $CAP_LAST"; fi
fi
exit $rc
