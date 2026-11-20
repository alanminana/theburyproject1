#!/usr/bin/env bash
# VERIFICADOR DE DESTINO EXTERNO DE RESPALDO (P-06). Solo lectura: no escribe ni crea nada en el destino.
# Objetivo: que "otro disco" no se confunda con "respaldo externo". Una copia en otro disco virtual de la MISMA VM, en una carpeta
# compartida del anfitrion (vmhgfs/vboxsf/9p/virtiofs), en WSL2 del mismo PC o en otro volumen del mismo equipo NO protege contra la perdida del
# anfitrion (robo, incendio, falla de placa, ransomware, borrado del disco virtual). Este script descarta lo que se puede descartar por hechos
# comprobables y deja CLARO lo que solo una persona puede afirmar.
#
#   scripts/backup/check-external-destination.sh [--path DIR] [--quiet]
#
#   --path DIR   punto de montaje a evaluar (default: BACKUP_REMOVABLE_DIR del .env)
#
# Reglas (cada FAIL => exit 12):
#   E01 hay ruta y es un directorio existente
#   E02 es un PUNTO DE MONTAJE real (un directorio comun en el mismo disco no es un medio aparte)
#   E03 no esta en el mismo dispositivo (st_dev) que BACKUP_DIR ni que la raiz /
#   E04 el sistema de archivos no es volatil (tmpfs/overlay) ni una carpeta compartida del anfitrion (vmhgfs, vboxsf, 9p, virtiofs); cifs/nfs = WARN
#   E05 el disco que lo respalda no es un disco VIRTUAL (VMware/VirtualBox/QEMU/Hyper-V...): vive dentro de un archivo del anfitrion.
#       Un disco USB fisico (TRAN=usb o hotplug/extraible) pasa; un disco interno no virtual = WARN (confirmar que no es del anfitrion)
#   E06 existe el archivo-marca (BACKUP_REMOVABLE_MARKER, default .bury-backup-removable) creado UNA vez a mano en el disco correcto
#   E07 es escribible por el usuario actual (test -w, sin escribir)
# IMPORTANTE: pasar todas las reglas NO prueba que el medio este fuera del anfitrion. Un disco USB conectado al mismo PC que aloja la VM
# sigue en el mismo sitio. La independencia fisica (otro equipo/otra ubicacion) la confirma una persona y se registra en la matriz P-04/P-06.
# Variables solo para tests: BURY_EXT_PROBE (archivo con FSTYPE=, SOURCE=, VENDOR=, MODEL=, TRAN=, RM=, HOTPLUG=), BURY_EXT_ASSUME_MOUNT=1.
# Codigos: 0 sin FAIL | 1 uso/configuracion | 12 destino no valido como externo
# shellcheck source=lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
set +e

EX_NOT_EXTERNAL=12
TARGET="" QUIET=0
while (( $# )); do
  case "$1" in
    --path) TARGET="${2:-}"; shift 2 ;;
    --quiet) QUIET=1; shift ;;
    -h|--help) sed -n '2,28p' "$0"; exit 0 ;;
    *) echo "argumento desconocido: $1" >&2; exit $EX_USAGE ;;
  esac
done

FAILS=0 WARNS=0
say()  { (( QUIET )) || printf 'OK   %s\n' "$*"; }
fail() { FAILS=$((FAILS+1)); printf 'FAIL %s %s\n' "$1" "$2"; }
warn() { WARNS=$((WARNS+1)); printf 'WARN %s %s\n' "$1" "$2"; }

# ---- clasificadores puros (probados en test-check-external-destination.sh)
classify_fstype() { # -> volatile | host-share | network | ok
  case "${1,,}" in
    tmpfs|ramfs|overlay|overlayfs|squashfs|devtmpfs) echo volatile ;;
    vmhgfs|fuse.vmhgfs-fuse|vboxsf|9p|virtiofs|v9fs|drvfs|fuse.sshfs-host) echo host-share ;;
    cifs|smb3|smbfs|nfs|nfs4|fuse.sshfs|davfs) echo network ;;
    *) echo ok ;;
  esac
}
classify_disk() { # vendor model tran rm hotplug -> virtual | removable | internal | unknown
  local id="${1,,} ${2,,}" tran="${3,,}" rm="${4:-0}" hot="${5:-0}"
  if [[ -z "${1// /}${2// /}${3// /}" ]]; then echo unknown; return; fi
  case "$id" in *vmware*|*virtualbox*|*vbox*|*qemu*|*virtio*|*msft*virtual*|*"virtual disk"*|*"virtual s"*|*"virtual hd"*|*xen*|*hyper-v*) echo virtual; return ;; esac
  if [[ "$tran" == usb || "$rm" == 1 || "$hot" == 1 ]]; then echo removable; return; fi
  echo internal
}

TARGET="${TARGET:-}"
if [[ -z "$TARGET" ]]; then load_config; TARGET="${BACKUP_REMOVABLE_DIR:-}"; else load_config; fi
LOG_FILE=""
MARKER="${BACKUP_REMOVABLE_MARKER:-.bury-backup-removable}"

# E01
if [[ -z "$TARGET" ]]; then
  fail E01 "no hay destino externo configurado (BACKUP_REMOVABLE_DIR vacio y sin --path): hoy los respaldos existen SOLO en este servidor"
  echo "---"; echo "check-external-destination: NO HAY destino externo ($FAILS FAIL)"; exit $EX_NOT_EXTERNAL
fi
if [[ ! -d "$TARGET" ]]; then
  fail E01 "$TARGET no existe o no es un directorio (disco no conectado o no montado)"
  echo "---"; echo "check-external-destination: destino ausente ($FAILS FAIL)"; exit $EX_NOT_EXTERNAL
fi
REAL=$(readlink -f "$TARGET" 2>/dev/null || echo "$TARGET")
say "E01 destino existente: $REAL"

# sondeo del sistema (o archivo de prueba)
FSTYPE="" SOURCE="" VENDOR="" MODEL="" TRAN="" RMF="" HOT=""
if [[ -n "${BURY_EXT_PROBE:-}" && -f "$BURY_EXT_PROBE" ]]; then
  # shellcheck disable=SC1090
  . "$BURY_EXT_PROBE"; RMF="${RM:-}"; HOT="${HOTPLUG:-}"
else
  if command -v findmnt >/dev/null 2>&1; then
    FSTYPE=$(findmnt -no FSTYPE -T "$REAL" 2>/dev/null | head -n1); SOURCE=$(findmnt -no SOURCE -T "$REAL" 2>/dev/null | head -n1)
  fi
  if [[ "$SOURCE" == /dev/* ]] && command -v lsblk >/dev/null 2>&1; then
    disk=$(lsblk -no PKNAME "$SOURCE" 2>/dev/null | head -n1); disk=${disk:+/dev/$disk}; disk=${disk:-$SOURCE}
    VENDOR=$(lsblk -dno VENDOR "$disk" 2>/dev/null | head -n1 | xargs); MODEL=$(lsblk -dno MODEL "$disk" 2>/dev/null | head -n1 | xargs)
    TRAN=$(lsblk -dno TRAN "$disk" 2>/dev/null | head -n1 | xargs); RMF=$(lsblk -dno RM "$disk" 2>/dev/null | head -n1 | xargs); HOT=$(lsblk -dno HOTPLUG "$disk" 2>/dev/null | head -n1 | xargs)
  fi
fi

# E02
if [[ "${BURY_EXT_ASSUME_MOUNT:-0}" == 1 ]] || mountpoint -q "$REAL" 2>/dev/null; then
  say "E02 es un punto de montaje real"
else
  fail E02 "$REAL no es un punto de montaje: es una carpeta comun del mismo disco (no es un medio aparte)"
fi

# E03
dev_t=$(stat -c %d "$REAL" 2>/dev/null); dev_b=$(stat -c %d "${BACKUP_DIR:-/nonexistent}" 2>/dev/null); dev_r=$(stat -c %d / 2>/dev/null)
if [[ -n "$dev_t" && ( "$dev_t" == "$dev_b" || "$dev_t" == "$dev_r" ) ]]; then
  fail E03 "mismo dispositivo que BACKUP_DIR o que / (st_dev=$dev_t): no es independiente de los respaldos locales"
else
  say "E03 dispositivo distinto de BACKUP_DIR y de /"
fi

# E04
case "$(classify_fstype "$FSTYPE")" in
  volatile)   fail E04 "sistema de archivos volatil ($FSTYPE): se pierde al reiniciar" ;;
  host-share) fail E04 "carpeta compartida del anfitrion ($FSTYPE): vive en el mismo equipo fisico que la VM" ;;
  network)    warn E04 "sistema de archivos de red ($FSTYPE): solo es externo si el servidor remoto esta en OTRO equipo y ubicacion (confirmar a mano)" ;;
  *)          say "E04 sistema de archivos local ($FSTYPE)" ;;
esac

# E05
case "$(classify_disk "$VENDOR" "$MODEL" "$TRAN" "$RMF" "$HOT")" in
  virtual)   fail E05 "el disco que lo respalda es VIRTUAL (vendor='$VENDOR' model='$MODEL'): es un archivo dentro de un disco del anfitrion" ;;
  removable) say "E05 medio extraible/USB (tran='${TRAN:-?}' rm=${RMF:-?} hotplug=${HOT:-?}): candidato a copia externa" ;;
  internal)  warn E05 "disco interno no virtual (tran='${TRAN:-?}' model='$MODEL'): si es un disco fisico del MISMO anfitrion no cuenta como externo" ;;
  *)         warn E05 "no se pudo identificar el disco subyacente (lsblk sin datos): verificar a mano que sea fisico y externo" ;;
esac

# E06
if [[ -e "$REAL/$MARKER" ]]; then say "E06 archivo-marca presente ($MARKER)"; else fail E06 "falta el archivo-marca $MARKER en la raiz del destino (crearlo UNA vez con touch en el disco correcto)"; fi

# E07
if [[ -w "$REAL" ]]; then say "E07 escribible por $(id -un 2>/dev/null || echo usuario)"; else fail E07 "$REAL no es escribible por el usuario actual"; fi

echo "---"
echo "AVISO: pasar estas reglas NO prueba independencia fisica. Una persona debe confirmar que el medio esta FUERA del equipo que aloja la VM"
echo "       (otro equipo u otra ubicacion) y registrarlo; sin esa confirmacion P-06 no se declara finalizado."
if (( FAILS > 0 )); then echo "check-external-destination: destino NO valido como externo ($FAILS FAIL, $WARNS WARN)"; exit $EX_NOT_EXTERNAL; fi
echo "check-external-destination: sin hallazgos bloqueantes ($WARNS WARN) - candidato; falta la confirmacion humana de independencia fisica"
exit 0
