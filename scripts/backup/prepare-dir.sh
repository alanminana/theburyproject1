#!/usr/bin/env bash
# Prepara y VALIDA BACKUP_DIR *antes* de levantar los contenedores (db monta $BACKUP_DIR/sql).
#
#   scripts/backup/prepare-dir.sh [--check]
#
# Por que: si BACKUP_DIR no existe cuando se hace `docker compose up db`, Docker lo crea como root:root y despues los scripts (usuario
# normal) no pueden escribir en el (verificado en el ensayo del 2026-11-07). Este script lo crea ANTES, con el usuario actual.
#   - Si NO existe: lo crea (modo 700, dueño = usuario actual) con sql/, files/, logs/ y la marca .bury-backup-root.
#   - Si YA existe: NUNCA cambia modo ni dueño ni toca su contenido; solo valida y, si algo esta mal, dice el comando exacto de
#     remediacion (no lo ejecuta). Un directorio con backups reales no se "arregla" automaticamente.
#   - --check: solo valida (nunca crea nada).
# Codigos: 0 ok | 1 configuracion invalida | 8 el directorio existe pero no es utilizable (ver el mensaje)
# shellcheck source=lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

CHECK=0
while (( $# )); do
  case "$1" in
    --check) CHECK=1; shift ;;
    -h|--help) sed -n '2,13p' "$0"; exit 0 ;;
    *) die $EX_USAGE "argumento desconocido: $1" ;;
  esac
done

load_config
LOG_FILE=""   # solo valida: no escribe backup.log dentro de un BACKUP_DIR existente
me=$(id -u)
[[ "$BACKUP_DIR" == /* ]] || die $EX_USAGE "BACKUP_DIR debe resolverse a una ruta absoluta (es: $BACKUP_DIR)"

case "$BACKUP_DIR" in
  /mnt/[a-z]/*) log WARN "BACKUP_DIR=$BACKUP_DIR esta en un disco de Windows montado en WSL (drvfs): no admite dueño 10001 ni modos Linux, y SQL Server no puede escribir ahi. Usar una ruta del sistema de archivos de WSL (p. ej. /srv/bury-backups)" ;;
esac

if [[ -e "$BACKUP_DIR" || -L "$BACKUP_DIR" ]]; then
  [[ -d "$BACKUP_DIR" ]] || die $EX_PREREQ "BACKUP_DIR=$BACKUP_DIR existe pero NO es un directorio"
  backup_dir_usable
  owner=$(stat -c %u "$BACKUP_DIR" 2>/dev/null || echo "?")
  # Subdirectorio sql/: debe ser de este usuario, de root (si lo creo Docker bajo un BACKUP_DIR ya preparado: lo corrige
  # backup.sh/prepare_dirs a uid 10001 de forma acotada) o de 10001 (usuario mssql).
  if [[ -d "$BACKUP_DIR/sql" ]]; then
    so=$(stat -c %u "$BACKUP_DIR/sql" 2>/dev/null || echo "?")
    log INFO "BACKUP_DIR existente: dueño=$owner sql/=uid $so (no se modifica nada)"
  else
    if (( CHECK )); then die $EX_PREREQ "falta $BACKUP_DIR/sql (crearlo con: mkdir '$BACKUP_DIR/sql')"; fi
    mkdir "$BACKUP_DIR/sql" && log INFO "creado $BACKUP_DIR/sql (el dueño final lo fija backup.sh con el usuario mssql)"
  fi
  log INFO "OK  BACKUP_DIR validado: $BACKUP_DIR"
  exit 0
fi

(( CHECK )) && die $EX_PREREQ "BACKUP_DIR=$BACKUP_DIR no existe (ejecutar scripts/backup/prepare-dir.sh sin --check)"
mkdir -p "$BACKUP_DIR" 2>/dev/null \
  || die $EX_PREREQ "no se pudo crear BACKUP_DIR=$BACKUP_DIR. Crearlo con: sudo mkdir -p '$BACKUP_DIR' && sudo chown $(id -un): '$BACKUP_DIR' && chmod 700 '$BACKUP_DIR'  (o elegir otra ruta en .env) y repetir"
chmod 700 "$BACKUP_DIR"
mkdir "$BACKUP_DIR/sql" "$BACKUP_DIR/files" "$BACKUP_DIR/logs"
chmod 700 "$BACKUP_DIR/files" "$BACKUP_DIR/logs"
echo "Directorio administrado por scripts/backup. NO borrar este archivo." >"$BACKUP_DIR/.bury-backup-root"
log INFO "OK  BACKUP_DIR creado: $BACKUP_DIR (modo 700, dueño $(id -un))"
