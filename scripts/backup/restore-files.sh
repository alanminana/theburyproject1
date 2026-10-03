#!/usr/bin/env bash
# Restaura los archivos persistentes (keys, uploads, App_Data, caddy-data) desde un files_*.tar.gz.
#
#   scripts/backup/restore-files.sh --list
#   scripts/backup/restore-files.sh --archive files_2026-09-21_230000Z.tar.gz [--force]
#
# Requiere `app` DETENIDA (si no, podria escribir mientras se restaura y generar claves nuevas).
# Sin --force ABORTA si algun volumen destino ya tiene contenido; con --force lo VACIA y restaura (borra lo que hubiera).
# Verifica sha256 y la integridad del tar antes de tocar nada. Codigos: 0 ok | 1 uso | 8 prerequisito | 9 restore rechazado/fallido
# shellcheck source=lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

ARCHIVE="" FORCE=0 LIST=0
while (( $# )); do
  case "$1" in
    --archive) ARCHIVE="${2:-}"; shift 2 ;;
    --force) FORCE=1; shift ;;
    --list) LIST=1; shift ;;
    -h|--help) sed -n '2,9p' "$0"; exit 0 ;;
    *) die $EX_USAGE "argumento desconocido: $1" ;;
  esac
done

load_config
command -v docker >/dev/null 2>&1 || die $EX_PREREQ "docker no esta disponible"
mkdir -p "$BACKUP_DIR/logs"

if (( LIST )); then
  echo "Archivos en $BACKUP_DIR/files:"; ls -1 "$BACKUP_DIR/files" 2>/dev/null | grep -E '\.tar\.gz$' | sort || true
  exit 0
fi

[[ -n "$ARCHIVE" ]] || die $EX_USAGE "falta --archive (usar --list)"
[[ "$ARCHIVE" =~ ^files_[0-9]{4}-[0-9]{2}-[0-9]{2}_[0-9]{6}Z\.tar\.gz$ ]] || die $EX_USAGE "--archive debe ser el NOMBRE de un archivo files_AAAA-MM-DD_HHMMSSZ.tar.gz (sin ruta)"
service_running app && die $EX_RESTORE "app esta corriendo: detenerla antes (docker compose stop app)"

log INFO "=== restore-files inicio: $ARCHIVE (force=$FORCE) ==="
if ! out=$(dc run --rm -T --no-deps --quiet-pull -e FORCE="$FORCE" restore-files sh /opt/bury-backup/files-restore.sh "$ARCHIVE" 2>&1); then
  log ERROR "$out"; die $EX_RESTORE "restore-files fallo"
fi
while IFS= read -r l; do log INFO "  $l"; done <<<"$out"
log INFO "=== restore-files OK ==="
