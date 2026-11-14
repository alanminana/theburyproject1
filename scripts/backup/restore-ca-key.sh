#!/usr/bin/env bash
# Repone la clave privada de la CA de Caddy (root.key / intermediate.key) desde <tar>.caseal, SIN tocar el resto de caddy-data.
#
#   scripts/backup/restore-ca-key.sh --archive files_2026-09-21_230000Z.tar.gz [--dry-run] [--force]
#
# Requiere BACKUP_CA_SEAL_ENV (la clave de sellado, custodia INDEPENDIENTE de BACKUP_DIR). --dry-run descifra y valida sin escribir nada
# (prueba periodica de que la custodia sirve). Sin --force aborta si en caddy-data ya hay una clave DISTINTA. Con `caddy` corriendo solo
# se admite --dry-run. Codigos: 0 ok | 1 uso | 4 ocupado | 8 prerequisito | 9 rechazado/fallido (clave de sellado incorrecta, .caseal ausente o alterado)
# shellcheck source=lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

ARCHIVE="" FORCE=0 DRY=0
while (( $# )); do
  case "$1" in
    --archive) ARCHIVE="${2:-}"; shift 2 ;;
    --force) FORCE=1; shift ;;
    --dry-run) DRY=1; shift ;;
    -h|--help) sed -n '2,9p' "$0"; exit 0 ;;
    *) die $EX_USAGE "argumento desconocido: $1" ;;
  esac
done
load_config
[[ "$ARCHIVE" =~ ^files_[0-9]{4}-[0-9]{2}-[0-9]{2}_[0-9]{6}Z\.tar\.gz$ ]] || die $EX_USAGE "--archive debe ser el NOMBRE de un archivo files_AAAA-MM-DD_HHMMSSZ.tar.gz (sin ruta)"
command -v docker >/dev/null 2>&1 || die $EX_PREREQ "docker no esta disponible"
backup_dir_usable
lrc=0; acquire_lock || lrc=$?
(( lrc == 0 )) || { (( lrc == 1 )) && die $EX_BUSY "hay un backup o un restore en curso (lock)"; die $EX_PREREQ "no se pudo tomar el lock (permisos de BACKUP_DIR)"; }
trap release_lock EXIT
if (( ! DRY )) && service_running caddy; then die $EX_RESTORE "caddy esta corriendo: detenerlo antes (docker compose stop caddy) o usar --dry-run"; fi

log INFO "=== restore-ca-key inicio: $ARCHIVE (dry-run=$DRY force=$FORCE) ==="
rc=0; ca_unseal "$ARCHIVE" "$DRY" "$FORCE" || rc=$?
if (( rc != 0 )); then log ERROR "$CA_UNSEAL_OUT"; die $EX_RESTORE "restore-ca-key fallo (rc=$rc)"; fi
log INFO "$CA_UNSEAL_OUT"
log INFO "=== restore-ca-key OK ==="
