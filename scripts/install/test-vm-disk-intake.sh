#!/usr/bin/env bash
# Prueba sintetica (sin Docker, sin root, sin VM real) de vm-disk-intake.py: cabeceras falsas de cada formato.
set -u
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
tmp="$(mktemp -d)"; trap 'rm -rf "$tmp"' EXIT
fail=0
pad() { head -c "$1" /dev/zero; }
mk() { # nombre, cabecera(printf), relleno
  { printf "$2"; pad "$3"; } > "$tmp/$1"; }
mk a.vhdx 'vhdxfile' 4096
mk b.vmdk 'KDMV\x01\x00\x00\x00' 4096
mk c.vdi '<<< Oracle VM VirtualBox Disk Image >>>\n' 4096
mk d.qcow2 'QFI\xfb\x00\x00\x00\x03' 4096
printf '# Disk DescriptorFile\nversion=1\n' > "$tmp/e.vmdk"
{ pad 510; printf '\x55\xaa'; pad 1536; } > "$tmp/f.img"
printf 'basura de texto sin formato' > "$tmp/g.bin"
pad 512 > "$tmp/z.bin"   # relleno de ceros alineado a 512: NO debe verse como disco
printf '# Disk DescriptorFile\nRW 100 FLAT "ok-flat.vmdk" 0\n' > "$tmp/i.vmdk"; pad 512 > "$tmp/ok-flat.vmdk"
printf '# Disk DescriptorFile\nRW 100 FLAT "no-existe.vmdk" 0\n' > "$tmp/j.vmdk"
mkdir "$tmp/ova"; printf 'x' > "$tmp/ova/disk1.vmdk"; ( cd "$tmp/ova" && tar cf "$tmp/h.ova" disk1.vmdk )
out="$(python3 -I "$here/vm-disk-intake.py" --json "$tmp" 2>/dev/null)" || { echo "FAIL: el script salio con error"; exit 1; }
chk() { # archivo, formato esperado
  python3 -I -c 'import json,sys; d={i["archivo"].rsplit("/",1)[-1]:i for i in json.load(sys.stdin)}; sys.exit(0 if d.get(sys.argv[1],{}).get("formato")==sys.argv[2] else 1)' "$1" "$2" <<<"$out" \
    && echo "PASS $1 -> $2" || { echo "FAIL $1 esperaba $2"; fail=1; }
}
chk a.vhdx vhdx; chk b.vmdk vmdk; chk c.vdi vdi; chk d.qcow2 qcow2; chk e.vmdk vmdk; chk f.img raw; chk g.bin desconocido; chk z.bin desconocido; chk h.ova ova
nota() { # archivo, texto que debe aparecer en las notas
  python3 -I -c 'import json,sys; d={i["archivo"].rsplit("/",1)[-1]:i for i in json.load(sys.stdin)}; sys.exit(0 if sys.argv[2] in " ".join(d[sys.argv[1]]["notas"]) else 1)' "$1" "$2" <<<"$out" \
    && echo "PASS $1 nota '$2'" || { echo "FAIL $1 sin nota '$2'"; fail=1; }
}
nota i.vmdk "presentes"; nota j.vmdk "FALTAN"; nota f.img "candidato"
# solo lectura: no aparecio ningun archivo nuevo
n="$(find "$tmp" -type f | wc -l)"; [ "$n" -eq 13 ] && echo "PASS solo lectura (13 archivos, ninguno nuevo)" || { echo "FAIL cantidad de archivos: $n"; fail=1; }
exit "$fail"
