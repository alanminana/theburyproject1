#!/usr/bin/env bash
# Tests de scripts/backup/check-external-destination.sh (P-06). Sin docker ni discos reales: clasificadores puros + sondeo simulado
# (BURY_EXT_PROBE) sobre directorios temporales. Linux/WSL.   bash scripts/backup/test-check-external-destination.sh
set -uo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SUT="$HERE/check-external-destination.sh"
T=$(mktemp -d); trap 'rm -rf "$T"' EXIT
PASS=0 FAIL=0
ok()  { PASS=$((PASS+1)); echo "PASS $1"; }
bad() { FAIL=$((FAIL+1)); echo "FAIL $1"; }
check() { local n=$1; shift; if "$@"; then ok "$n"; else bad "$n"; fi; }

check "bash -n check-external-destination.sh" bash -n "$SUT"

# --- clasificadores puros: se extraen las funciones del propio script
eval "$(sed -n '/^classify_fstype() {/,/^}/p;/^classify_disk() {/,/^}/p' "$SUT")"
for fs in tmpfs overlay ramfs; do check "fstype $fs = volatile" test "$(classify_fstype $fs)" = volatile; done
for fs in vmhgfs fuse.vmhgfs-fuse vboxsf 9p virtiofs drvfs; do check "fstype $fs = carpeta compartida del anfitrion" test "$(classify_fstype $fs)" = host-share; done
for fs in cifs nfs nfs4 fuse.sshfs; do check "fstype $fs = red (WARN, a confirmar)" test "$(classify_fstype $fs)" = network; done
for fs in ext4 xfs btrfs ntfs exfat vfat; do check "fstype $fs = local" test "$(classify_fstype $fs)" = ok; done
check "disco VMware = virtual"            test "$(classify_disk 'VMware,' 'VMware Virtual S' spi 0 0)" = virtual
check "disco VirtualBox = virtual"        test "$(classify_disk 'ATA' 'VBOX HARDDISK' sata 0 0)" = virtual
check "disco QEMU/virtio = virtual"       test "$(classify_disk 'QEMU' 'QEMU HARDDISK' sata 0 0)" = virtual
check "disco Hyper-V = virtual"           test "$(classify_disk 'Msft' 'Virtual Disk' sas 0 0)" = virtual
check "pendrive USB = extraible"          test "$(classify_disk 'Kingston' 'DataTraveler' usb 1 1)" = removable
check "HDD externo USB = extraible"       test "$(classify_disk 'WD' 'Elements 25A2' usb 0 0)" = removable
check "SSD SATA interno = interno"        test "$(classify_disk 'ATA' 'Samsung SSD 870' sata 0 0)" = internal
check "sin datos de lsblk = desconocido"  test "$(classify_disk '' '' '' '' '')" = unknown

# --- integracion: el script completo con sondeo simulado
BK="$T/backup"; mkdir -p "$BK"; : >"$BK/.bury-backup-root"
run() { # run <ruta-o-vacio> -> exit y salida en $T/out
  BACKUP_DIR="$BK" BACKUP_REMOVABLE_DIR="${1:-}" BURY_EXT_PROBE="${PROBE:-}" BURY_EXT_ASSUME_MOUNT="${ASSUME:-0}" bash "$SUT" >"$T/out" 2>&1; echo $?
}
probe() { printf 'FSTYPE=%s\nSOURCE=%s\nVENDOR=%s\nMODEL=%s\nTRAN=%s\nRM=%s\nHOTPLUG=%s\n' "$@" >"$T/probe"; PROBE="$T/probe"; }

check "sin destino configurado -> exit 12 y E01" bash -c '[[ "$(PROBE= ASSUME=0 BACKUP_DIR="$1" BACKUP_REMOVABLE_DIR= bash "$2" 2>&1; echo "rc=$?")" == *"E01"*"rc=12" ]]' _ "$BK" "$SUT"
check "ruta inexistente -> exit 12" bash -c '[[ "$(BACKUP_DIR="$1" BACKUP_REMOVABLE_DIR=/no/existe bash "$2" >/dev/null 2>&1; echo $?)" == 12 ]]' _ "$BK" "$SUT"

D="$T/usb"; mkdir -p "$D"
# directorio comun dentro del mismo disco que BACKUP_DIR: no es punto de montaje ni otro dispositivo
probe ext4 /dev/sdb1 Kingston DataTraveler usb 1 1
rc=$(run "$D"); check "directorio comun (no montaje, mismo st_dev) -> exit 12" test "$rc" = 12
check "  ...marca E02" grep -q "^FAIL E02" "$T/out"
check "  ...marca E03" grep -q "^FAIL E03" "$T/out"
check "  ...el AVISO de independencia fisica siempre se imprime" grep -q "NO prueba independencia fisica" "$T/out"

# destino simulado perfecto, pero en el mismo st_dev (temp dir): E03 solo se puede satisfacer con otro dispositivo real
if [[ -d /dev/shm ]] && [[ "$(stat -c %d /dev/shm)" != "$(stat -c %d "$BK")" ]] && [[ "$(stat -c %d /dev/shm)" != "$(stat -c %d /)" ]]; then
  S=$(mktemp -d /dev/shm/bury-ext-XXXXXX); : >"$S/.bury-backup-removable"
  probe ext4 /dev/sdb1 Kingston DataTraveler usb 1 1; ASSUME=1
  rc=$(run "$S"); check "USB simulado en otro dispositivo, con marca -> exit 0 (candidato)" test "$rc" = 0
  check "  ...sin FAIL" bash -c '! grep -q "^FAIL" "$1"' _ "$T/out"
  probe ext4 /dev/sdb1 'VMware,' 'VMware Virtual S' spi 0 0
  rc=$(run "$S"); check "disco VIRTUAL de VMware -> exit 12 (E05)" test "$rc" = 12
  check "  ...marca E05" grep -q "^FAIL E05" "$T/out"
  probe vmhgfs .host:/share '' '' '' '' ''
  rc=$(run "$S"); check "carpeta compartida del anfitrion (vmhgfs) -> exit 12 (E04)" test "$rc" = 12
  check "  ...marca E04" grep -q "^FAIL E04" "$T/out"
  probe cifs //nas/respaldo '' '' '' '' ''
  rc=$(run "$S"); check "cifs -> WARN E04 y E05 desconocido, sin FAIL (exit 0)" test "$rc" = 0
  check "  ...WARN E04" grep -q "^WARN E04" "$T/out"
  probe ext4 /dev/sdc1 ATA 'Samsung SSD 870' sata 0 0
  rc=$(run "$S"); check "disco interno no virtual -> WARN E05 (a confirmar), exit 0" test "$rc" = 0
  check "  ...WARN E05" grep -q "^WARN E05" "$T/out"
  rm -f "$S/.bury-backup-removable"; probe ext4 /dev/sdb1 Kingston DataTraveler usb 1 1
  rc=$(run "$S"); check "falta la marca -> exit 12 (E06)" test "$rc" = 12
  check "  ...marca E06" grep -q "^FAIL E06" "$T/out"
  rm -rf "$S"; ASSUME=0
else
  echo "SKIP casos de otro dispositivo: /dev/shm no es un dispositivo distinto en este entorno"
fi

# el script no escribe en el destino (solo lectura)
D2="$T/ro"; mkdir -p "$D2"; before=$(find "$D2" | wc -l); PROBE="$T/probe" run "$D2" >/dev/null; after=$(find "$D2" | wc -l)
check "solo lectura: el destino no recibe archivos" test "$before" = "$after"

echo
echo "resultado test-check-external-destination: $PASS OK, $FAIL fallos"
(( FAIL == 0 ))
