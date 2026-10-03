#!/usr/bin/env bash
# Descarga los backups desde la copia externa (BACKUP_REMOTE) al directorio local de backups. Es el primer paso de la
# recuperacion en un servidor NUEVO (donde los backups locales ya no existen).
#
#   scripts/backup/fetch-offsite.sh [--list]
#
# Copia <BACKUP_REMOTE>/sql -> $BACKUP_DIR/sql y <BACKUP_REMOTE>/files -> $BACKUP_DIR/files (solo agrega/actualiza; no borra nada
# local) y verifica con `rclone check`. Luego ajusta permisos para el usuario mssql. Codigos: 0 ok | 6 fallo copia externa | 1 config
# shellcheck source=lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

LIST=0
while (( $# )); do
  case "$1" in
    --list) LIST=1; shift ;;
    -h|--help) sed -n '2,8p' "$0"; exit 0 ;;
    *) die $EX_USAGE "argumento desconocido: $1" ;;
  esac
done

load_config
[[ -n "$BACKUP_REMOTE" ]] || die $EX_USAGE "BACKUP_REMOTE no esta configurado"
[[ "$BACKUP_REMOTE" =~ ^[A-Za-z0-9_-]+:.+ ]] || die $EX_USAGE "BACKUP_REMOTE debe tener la forma <remoto>:<ruta>"
command -v docker >/dev/null 2>&1 || die $EX_PREREQ "docker no esta disponible"

mkdir -p "$BACKUP_DIR/sql" "$BACKUP_DIR/files" "$BACKUP_DIR/logs"
chmod 700 "$BACKUP_DIR" 2>/dev/null || true
[[ -f "$BACKUP_DIR/.bury-backup-root" ]] || echo "Directorio administrado por scripts/backup. NO borrar este archivo." >"$BACKUP_DIR/.bury-backup-root"

if (( LIST )); then
  for sub in sql files; do
    echo "== $BACKUP_REMOTE/$sub"
    dc run --rm -T --no-deps --quiet-pull backup-offsite lsf "$BACKUP_REMOTE/$sub" || die $EX_OFFSITE "no se pudo listar $sub en el remoto"
  done
  exit 0
fi

log INFO "=== fetch-offsite inicio: ${BACKUP_REMOTE%%:*}:* -> $BACKUP_DIR ==="
for sub in sql files; do
  dc run --rm -T --no-deps --quiet-pull restore-offsite copy "$BACKUP_REMOTE/$sub" "/backups/$sub" >/dev/null || die $EX_OFFSITE "descarga fallo ($sub)"
  rclone_verify restore-offsite "$BACKUP_REMOTE/$sub" "/backups/$sub" || die $EX_OFFSITE "verificacion de la descarga fallo ($sub): $RCLONE_ERR"
  log INFO "descargado y verificado: $sub"
done
# Integridad de extremo a extremo: cada archivo contra el .sha256 calculado en ORIGEN (al crear el backup). Detecta corrupcion en el
# remoto o en transito aunque el transporte no exponga hashes.
if ! bad=$(tools sh -c 'cd /backups/sql && for f in *.sha256; do [ -e "$f" ] && { sha256sum -c "$f" >/dev/null 2>&1 || echo "$f"; }; done; cd /backups/files && for f in *.sha256; do [ -e "$f" ] && { sha256sum -c "$f" >/dev/null 2>&1 || echo "$f"; }; done; true' | tr -d '\r') || [[ -n "$bad" ]]; then
  die $EX_VERIFY "sha256 NO coincide (backup corrupto en el remoto o en transito): $bad"
fi
log INFO "sha256 de origen verificados"
tools sh -c 'chown -R 10001:0 /backups/sql && chmod 750 /backups/sql && chmod 640 /backups/sql/* 2>/dev/null; true'
log INFO "=== fetch-offsite OK ==="
