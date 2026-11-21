#!/usr/bin/env bash
# COPIA LOCAL CIFRADA EN UN SEGUNDO DISCO DE LA MISMA VM/SERVIDOR (P-06, solo respaldo LOCAL).
# Prepara, SIN formatear ni tocar el disco anfitrion, un contenedor LUKS2 (archivo) dentro de un volumen ya montado (p. ej. /mnt/tbp-pruebas)
# y lo deja montado en un punto propio (p. ej. /mnt/tbp-respaldo) para usarlo como BACKUP_REMOVABLE_DIR de scripts/backup/copy-removable.sh.
#
#   sudo scripts/backup/setup-local-disk-copy.sh --check   [opciones]   solo lectura: que haria y que falta
#   sudo scripts/backup/setup-local-disk-copy.sh --apply   [opciones]   crea clave, contenedor, ext4 interno, crypttab/fstab, marca y cron
#
# Opciones (valores por defecto entre parentesis):
#   --host-mount DIR   volumen ya montado que aloja el contenedor (/mnt/tbp-pruebas); DEBE ser un punto de montaje real distinto de /
#   --size-gb N        tamaño del contenedor en GB, TECHO DURO del uso real (3). Es un archivo disperso: no ocupa espacio hasta escribirse
#   --mount DIR        punto de montaje del volumen descifrado (/mnt/tbp-respaldo)
#   --owner USER       dueño del contenido (el usuario que corre el cron de backups; default: SUDO_USER)
#   --repo DIR         clon del repo en el servidor, para el cron de verificacion (default: este repo)
#   --verify-at HH:MM  hora local del verificador diario (03:15, despues del full de las 02:30)
# Seguridad:
#   - NUNCA formatea ni particiona un disco: solo crea UN archivo nuevo. Si el contenedor ya existe, se niega (no lo recrea ni lo borra).
#   - La clave (64 bytes aleatorios) va en /etc/bury-backup/local-disk.key (root, 0400), en el disco del sistema, FUERA del volumen cifrado.
#     Protege contra la lectura del VMDK/disco por separado (p. ej. un VMDK sincronizado o copiado), NO contra quien ya es root en la VM.
#     SIN esa clave el contenedor es irrecuperable: copiarla FUERA de la VM (custodia del propietario) y guardar tambien el backup del
#     encabezado LUKS (/etc/bury-backup/local-disk.luks-header). Ver docs/p06-respaldo-disco-local-vm.md.
#   - El punto de montaje sin montar queda inmutable (chattr +i): un backup nunca puede escribirse por error en el disco del sistema.
#   - Este disco es virtual y vive en el mismo equipo fisico que la VM: NO es una copia externa ni offsite.
# Codigos: 0 ok | 1 uso | 8 prerequisito/seguridad (no se cambio nada)
set -Eeuo pipefail

HOST_MOUNT=/mnt/tbp-pruebas SIZE_GB=3 MNT=/mnt/tbp-respaldo OWNER="${SUDO_USER:-}" ACTION="" VERIFY_AT="03:15"
REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
NAME=tbp-respaldo; KEYDIR=/etc/bury-backup; KEY=$KEYDIR/local-disk.key; HDR=$KEYDIR/local-disk.luks-header
LABEL=tbp-respaldo; MARKER=.bury-backup-removable
die() { echo "ERROR: $*" >&2; exit 8; }
usage() { sed -n '2,29p' "$0"; }
while (( $# )); do
  case "$1" in
    --check|--apply) ACTION="${1#--}"; shift ;;
    --host-mount) HOST_MOUNT="${2:-}"; shift 2 ;;
    --size-gb) SIZE_GB="${2:-}"; shift 2 ;;
    --mount) MNT="${2:-}"; shift 2 ;;
    --owner) OWNER="${2:-}"; shift 2 ;;
    --repo) REPO_DIR="${2:-}"; shift 2 ;;
    --verify-at) VERIFY_AT="${2:-}"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) echo "argumento desconocido: $1" >&2; exit 1 ;;
  esac
done
[[ -n "$ACTION" ]] || { usage; exit 1; }
[[ "$SIZE_GB" =~ ^[0-9]+$ ]] && (( SIZE_GB >= 1 && SIZE_GB <= 200 )) || { echo "--size-gb debe ser un entero 1-200" >&2; exit 1; }
[[ "$VERIFY_AT" =~ ^([01][0-9]|2[0-3]):([0-5][0-9])$ ]] || { echo "--verify-at debe ser HH:MM" >&2; exit 1; }
for p in "$HOST_MOUNT" "$MNT" "$REPO_DIR"; do [[ "$p" =~ ^/[A-Za-z0-9_./-]+$ && "$p" != *..* ]] || { echo "ruta invalida: $p" >&2; exit 1; }; done
[[ -n "$OWNER" ]] && id -u "$OWNER" >/dev/null 2>&1 || { echo "--owner invalido (usar --owner <usuario del cron de backups>)" >&2; exit 1; }
(( EUID == 0 )) || die "requiere root (sudo)"
CONT="$HOST_MOUNT/tbp-respaldo.luks"
[[ "$MNT" != "$HOST_MOUNT" && "$MNT" != "$HOST_MOUNT"/* && "$HOST_MOUNT" != "$MNT"/* ]] || die "--mount no puede estar dentro de --host-mount ni al reves"

# --- precondiciones (siempre, tambien en --check)
for c in cryptsetup mkfs.ext4 mountpoint findmnt truncate chattr; do command -v "$c" >/dev/null || die "falta '$c' (apt install cryptsetup-bin e2fsprogs)"; done
mountpoint -q "$HOST_MOUNT" || die "$HOST_MOUNT NO es un punto de montaje: no se escribe nada (evita llenar el disco del sistema)"
[[ "$(stat -c %d "$HOST_MOUNT")" != "$(stat -c %d /)" ]] || die "$HOST_MOUNT esta en el mismo dispositivo que /"
free_kb=$(df -Pk "$HOST_MOUNT" | awk 'NR==2{print $4}'); need_kb=$(( (SIZE_GB + 1) * 1024 * 1024 ))
(( free_kb > need_kb )) || die "$HOST_MOUNT tiene $((free_kb/1024)) MiB libres; hacen falta > $((need_kb/1024)) MiB"
[[ ! -e "$CONT" ]] || die "$CONT ya existe: no se recrea ni se borra (si es el contenedor correcto, ya esta preparado)"
if [[ -e "$MNT" ]] && mountpoint -q "$MNT"; then die "$MNT ya esta montado"; fi
if [[ -d "$MNT" ]] && [[ -n "$(ls -A "$MNT" 2>/dev/null)" ]]; then die "$MNT existe y NO esta vacio: no se monta encima de datos"; fi
[[ ! -e "/dev/mapper/$NAME" ]] || die "/dev/mapper/$NAME ya existe"
grep -qE "^\s*$NAME\s" /etc/crypttab 2>/dev/null && die "ya hay una entrada '$NAME' en /etc/crypttab"
echo "OK  precondiciones: $HOST_MOUNT montado y distinto de /, ${free_kb} KiB libres, contenedor $CONT nuevo, $MNT libre"
echo "PLAN contenedor LUKS2 ${SIZE_GB}G (disperso) en $CONT -> /dev/mapper/$NAME -> ext4 '$LABEL' -> $MNT (dueño $OWNER)"
echo "PLAN clave $KEY (0400 root), encabezado $HDR, /etc/crypttab + /etc/fstab (nofail), marca $MNT/$MARKER, cron /etc/cron.d/bury-backup-localcopy"
[[ "$ACTION" == apply ]] || { echo "(--check: no se cambio nada)"; exit 0; }

# --- aplicar (cada paso es aditivo; si falla, se detiene sin deshacer lo anterior: el estado queda a la vista)
umask 077
install -d -m 0700 -o root -g root "$KEYDIR"
[[ -e "$KEY" ]] || { head -c 64 /dev/urandom >"$KEY"; chmod 0400 "$KEY"; chown root:root "$KEY"; }
truncate -s "${SIZE_GB}G" "$CONT"; chmod 0600 "$CONT"; chown root:root "$CONT"
cryptsetup luksFormat --batch-mode --type luks2 --cipher aes-xts-plain64 --key-size 512 --hash sha256 --pbkdf pbkdf2 --pbkdf-force-iterations 1000 --key-file "$KEY" "$CONT"
cryptsetup luksHeaderBackup "$CONT" --header-backup-file "$HDR"; chmod 0400 "$HDR"
cryptsetup open --type luks2 --key-file "$KEY" "$CONT" "$NAME"
mkfs.ext4 -q -L "$LABEL" -m 0 -E lazy_itable_init=1,lazy_journal_init=1 "/dev/mapper/$NAME"
UUID_MAP=$(blkid -s UUID -o value "/dev/mapper/$NAME")
mkdir -p "$MNT"; chattr +i "$MNT"   # sin montar, nadie puede escribir en el disco del sistema
cp -a /etc/fstab "/etc/fstab.bak-$(date +%Y%m%d%H%M%S)"; [[ -e /etc/crypttab ]] && cp -a /etc/crypttab "/etc/crypttab.bak-$(date +%Y%m%d%H%M%S)"
echo "$NAME $CONT $KEY luks,nofail,x-systemd.device-timeout=15s" >>/etc/crypttab
echo "/dev/mapper/$NAME $MNT ext4 defaults,nofail,x-systemd.device-timeout=15s 0 2" >>/etc/fstab
systemctl daemon-reload
mount "$MNT"
chown "$OWNER:" "$MNT"; chmod 0750 "$MNT"
sudo -u "$OWNER" touch "$MNT/$MARKER"
VERIFY_H=${VERIFY_AT%%:*}; VERIFY_M=${VERIFY_AT##*:}
cat >/etc/cron.d/bury-backup-localcopy <<CRON
# Generado por scripts/backup/setup-local-disk-copy.sh. Verificacion diaria de la copia local cifrada (montaje, integridad sha256, espacio).
# Publica monitor-status/removable-verify.json; un fallo es CRITICAL en el monitor. No toca /etc/cron.d/bury-backup.
SHELL=/bin/bash
PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin
$((10#$VERIFY_M)) $((10#$VERIFY_H)) * * *  $OWNER  cd $REPO_DIR && /bin/bash $REPO_DIR/scripts/backup/verify-local-copy.sh --quiet >/dev/null 2>>/var/log/bury-backup-errors.log
CRON
chmod 0644 /etc/cron.d/bury-backup-localcopy
echo "OK  preparado: $MNT (UUID interno $UUID_MAP). Siguiente: BACKUP_REMOVABLE_DIR=$MNT en .env, BACKUP_REMOVABLE_MAX_TOTAL_GB y BACKUP_REMOVABLE_MIN_FREE_GB"
echo "AVISO: copiar FUERA de la VM $KEY y $HDR (sin la clave los respaldos de este disco no se pueden leer). Esto NO es una copia externa."
