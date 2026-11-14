#!/usr/bin/env bash
# Restaura los archivos persistentes (keys, uploads, App_Data, caddy-data) desde un files_*.tar.gz.
#
#   scripts/backup/restore-files.sh --list
#   scripts/backup/restore-files.sh --archive files_2026-09-21_230000Z.tar.gz [--force]
#
# Con BACKUP_CA_KEY=seal repone ademas la clave de la CA desde <tar>.caseal (BACKUP_CA_SEAL_ENV); --skip-ca-key lo omite.
# Requiere `app` DETENIDA (si no, podria escribir mientras se restaura y generar claves nuevas).
# Sin --force ABORTA si algun volumen destino ya tiene contenido; con --force lo VACIA y restaura (borra lo que hubiera).
# Verifica sha256 y la integridad del tar antes de tocar nada. Codigos: 0 ok | 1 uso | 8 prerequisito | 9 restore rechazado/fallido
# shellcheck source=lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

ARCHIVE="" FORCE=0 LIST=0 SKIP_CA=0
while (( $# )); do
  case "$1" in
    --archive) ARCHIVE="${2:-}"; shift 2 ;;
    --force) FORCE=1; shift ;;
    --skip-ca-key) SKIP_CA=1; shift ;;
    --list) LIST=1; shift ;;
    -h|--help) sed -n '2,9p' "$0"; exit 0 ;;
    *) die $EX_USAGE "argumento desconocido: $1" ;;
  esac
done

load_config
command -v docker >/dev/null 2>&1 || die $EX_PREREQ "docker no esta disponible"
mkdir_new_mode 700 "$BACKUP_DIR" "$BACKUP_DIR/logs"
backup_dir_usable

if (( LIST )); then
  echo "Archivos en $BACKUP_DIR/files (el manifiesto set_<ts>.txt acredita el PAR full+tar de una corrida 'all'):"
  tools sh -c 'cd /backups/files 2>/dev/null || exit 0
    for t in $(ls -1 files_*.tar.gz 2>/dev/null | sort); do
      ts="${t#files_}"; ts="${ts%.tar.gz}"; s=$(ls -1 "set_${ts}"_*.txt 2>/dev/null | head -n1)
      if [ -n "$s" ] && [ -f "$s" ]; then echo "  $t   <- PAR acreditado con $(sed -n "s/^full=\([^ ]*\) .*/\1/p" "$s")"; else echo "  $t   (sin manifiesto: su par con un full NO esta acreditado)"; fi
    done' || true
  exit 0
fi

[[ -n "$ARCHIVE" ]] || die $EX_USAGE "falta --archive (usar --list)"
[[ "$ARCHIVE" =~ ^files_[0-9]{4}-[0-9]{2}-[0-9]{2}_[0-9]{6}Z\.tar\.gz$ ]] || die $EX_USAGE "--archive debe ser el NOMBRE de un archivo files_AAAA-MM-DD_HHMMSSZ.tar.gz (sin ruta)"
# Un restore destructivo no puede correr a la vez que un backup (que detiene/arranca `app`) ni que otro restore.
lrc=0; acquire_lock || lrc=$?
(( lrc == 0 )) || { (( lrc == 1 )) && die $EX_BUSY "hay un backup o un restore en curso (lock): esperar a que termine"; die $EX_PREREQ "no se pudo tomar el lock (permisos de BACKUP_DIR)"; }
trap release_lock EXIT
service_running app && die $EX_RESTORE "app esta corriendo: detenerla antes (docker compose stop app)"
# El operador toma el control de `app`: si un backup anterior murio con ella detenida, esa marca ya no debe provocar un arranque
# automatico mientras se restaura. Se borra recien aqui (no en --list) y con el lock tomado.
rm -f "$BACKUP_DIR/.app-stopped-by-backup"

log INFO "=== restore-files inicio: $ARCHIVE (force=$FORCE) ==="
pair=$(tools sh -c 'ts="${0#files_}"; ts="${ts%.tar.gz}"; s=$(ls -1 /backups/files/set_"${ts}"_*.txt 2>/dev/null | head -n1); [ -n "$s" ] && sed -n "s/^full=\([^ ]*\) .*/\1/p" "$s"' "$ARCHIVE" 2>/dev/null | tr -d '\r' || true)
if [[ -n "$pair" ]]; then log INFO "par acreditado de este tar: $pair (restaurarlo SIN logs da el estado coherente de esa corrida; con logs la base avanza mas que los archivos)"; else log WARN "este tar no tiene manifiesto de par: nada garantiza que corresponda al full que se restaure"; fi
if ! out=$(dc run --rm -T --no-deps --quiet-pull -e FORCE="$FORCE" restore-files sh /opt/bury-backup/files-restore.sh "$ARCHIVE" 2>&1); then
  log ERROR "$out"; die $EX_RESTORE "restore-files fallo"
fi
while IFS= read -r l; do log INFO "  $l"; done <<<"$out"
# Clave privada de la CA de Caddy: con BACKUP_CA_KEY=seal viaja aparte en <tar>.caseal (el tar NO la contiene). Se repone con la clave de
# sellado (custodia independiente). Sin root.key, Caddy NO arranca aunque exista root.crt (probado: test-dr-integral d6).
has_blob=$(tools sh -c 'test -s "/backups/files/$0.caseal" && echo si || echo no' "$ARCHIVE" 2>/dev/null | tr -d '\r' || true)
if (( SKIP_CA )); then
  log WARN "--skip-ca-key: no se repone la clave de la CA (Caddy no arrancara con la CA restaurada: rotarla o reponer la clave con restore-ca-key.sh)"
elif [[ "$has_blob" == si ]]; then
  carc=0; ca_unseal "$ARCHIVE" 0 "$FORCE" || carc=$?
  if (( carc != 0 )); then
    log ERROR "$CA_UNSEAL_OUT"
    die $EX_RESTORE "archivos restaurados, pero la clave privada de la CA NO se repuso (rc=$carc). Caddy no arrancara hasta resolverlo: restore-ca-key.sh --archive $ARCHIVE con la clave de sellado correcta, o rotar la CA (docs/backup-ca-key-custodia.md)"
  fi
  log INFO "clave de la CA repuesta desde $ARCHIVE.caseal: $CA_UNSEAL_OUT"
else
  miss=""; miss=$(dc run --rm -T --no-deps --quiet-pull restore-files sh -c 'bad=0; for c in /dst/caddy-data/caddy/pki/authorities/*/*.crt; do [ -e "$c" ] || continue; [ -s "${c%.crt}.key" ] || bad=1; done; if [ "$bad" = 1 ]; then echo falta; else echo ok; fi' </dev/null 2>/dev/null | tr -d '\r') || miss=error
  # Un restore "OK" que deja a Caddy sin su clave NO es un restore OK: un proceso de recuperacion que avance daria por recuperado un servicio sin TLS.
  if [[ "$miss" == falta ]]; then
    die $EX_RESTORE "archivos restaurados, pero caddy-data quedo con root.crt SIN root.key y no hay $ARCHIVE.caseal: Caddy no arrancara. Traer el .caseal del remoto y usar restore-ca-key.sh, rotar la CA (docs/backup-ca-key-custodia.md), o repetir con --skip-ca-key si es intencional"
  elif [[ "$miss" != ok ]]; then
    die $EX_RESTORE "archivos restaurados, pero no se pudo comprobar el estado de la clave de la CA (docker fallo): verificar a mano antes de arrancar Caddy"
  fi
fi
log INFO "=== restore-files OK ==="
