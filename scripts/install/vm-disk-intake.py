#!/usr/bin/env python3
"""Inventario de SOLO LECTURA de los archivos de una VM (ERP antiguo).

Lee unos pocos bytes de cabecera de cada archivo indicado (nunca escribe, no monta, no arranca nada)
y dice que formato es y que conversion hace falta para Hyper-V (Windows 11) o KVM/QEMU (Ubuntu).
No decide el sistema operativo del servidor: solo ordena la evidencia que falta.

Uso:  python3 vm-disk-intake.py <archivo-o-carpeta> [...]   [--json]
"""
import json
import os
import sys
import tarfile

HEADER = 4096
# formato -> (hyper-v, kvm)
COMPAT = {
    "vhdx":  ("nativo", "qemu-img convert -O qcow2 (o raw)"),
    "vhd":   ("nativo (VHD fijo/dinamico; convertir a VHDX con Convert-VHD)", "qemu-img convert -O qcow2"),
    "vmdk":  ("convertir: qemu-img convert -O vhdx (o StarWind V2V)", "qemu-img convert -O qcow2 (lo lee directo)"),
    "vdi":   ("convertir: qemu-img convert -O vhdx", "qemu-img convert -O qcow2 (lo lee directo)"),
    "qcow2": ("convertir: qemu-img convert -O vhdx", "nativo"),
    "raw":   ("convertir: qemu-img convert -O vhdx (confirmar que es un disco y no otra cosa)", "nativo"),
}


def sniff(path):
    size = os.path.getsize(path)
    with open(path, "rb") as f:
        head = f.read(HEADER)
        tail = b""
        if size >= 512:
            f.seek(max(0, size - 512))
            tail = f.read(512)
    info = {"archivo": path, "bytes": size, "formato": "desconocido", "notas": []}
    if head.startswith(b"vhdxfile"):
        info["formato"] = "vhdx"
    elif head.startswith(b"conectix") or tail.startswith(b"conectix"):
        info["formato"] = "vhd"
    elif head.startswith(b"KDMV"):
        info["formato"] = "vmdk"
        info["notas"].append("VMDK monolitico/sparse")
    elif head.startswith(b"# Disk DescriptorFile"):
        info["formato"] = "vmdk"
        base = os.path.dirname(path)
        faltan, vistos = [], 0
        for linea in head.decode("utf-8", "replace").splitlines():
            partes = linea.split('"')
            if linea.split(" ", 1)[0] in ("RW", "RDONLY", "NOACCESS") and len(partes) >= 3:
                vistos += 1
                if not os.path.isfile(os.path.join(base, partes[1])):
                    faltan.append(partes[1])
        if faltan:
            info["notas"].append("descriptor VMDK: FALTAN extents referenciados: " + ", ".join(faltan))
        elif vistos:
            info["notas"].append(f"descriptor VMDK: {vistos} extent(s) presentes junto al descriptor (no se verifico su contenido)")
        else:
            info["notas"].append("descriptor VMDK sin extents legibles en los primeros 4 KiB: no verificado")
    elif b"<<< Oracle VM VirtualBox Disk Image >>>" in head[:80] or b"<<< VirtualBox Disk Image >>>" in head[:80]:
        info["formato"] = "vdi"
    elif head.startswith(b"QFI\xfb"):
        info["formato"] = "qcow2"
        info["notas"].append("QCOW2; si tiene backing file o snapshots internos hay que consolidarlos antes de migrar")
    elif head.startswith(b"MZ") or (head.startswith(b"<?xml") and b"<Envelope" in head):
        info["formato"] = "otro"
        info["notas"].append("XML OVF" if head.startswith(b"<?xml") else "ejecutable, no es disco")
    elif size >= 1024 and size % 512 == 0 and (head[510:512] == b"\x55\xaa" or head[512:520] == b"EFI PART"):
        # raw no tiene magia propia: solo se propone como CANDIDATO si hay firma MBR o GPT
        info["formato"] = "raw"
        info["notas"].append("candidato a disco raw (firma MBR 0x55AA o GPT); no confirmado")
    if path.lower().endswith((".ova", ".tar")):
        try:
            with tarfile.open(path) as t:
                names = t.getnames()
            info["formato"] = "ova"
            info["notas"].append("OVA con: " + ", ".join(names[:8]))
        except tarfile.TarError:
            info["notas"].append("extension .ova pero no es un tar legible")
    if info["formato"] in COMPAT:
        info["hyper_v"], info["kvm"] = COMPAT[info["formato"]]
    elif info["formato"] == "ova":
        info["hyper_v"] = info["kvm"] = "extraer el .vmdk/.ovf del OVA y tratarlo como VMDK"
    else:
        info["hyper_v"] = info["kvm"] = "sin evidencia: no asumir compatibilidad"
    return info


def collect(args):
    for a in args:
        if os.path.isdir(a):
            for root, _, files in os.walk(a):
                for n in sorted(files):
                    yield os.path.join(root, n)
        else:
            yield a


def main(argv):
    as_json = "--json" in argv
    paths = [a for a in argv if a != "--json"]
    if not paths:
        print(__doc__)
        return 2
    out = []
    for p in collect(paths):
        try:
            out.append(sniff(p))
        except OSError as e:
            out.append({"archivo": p, "error": str(e)})
    if as_json:
        print(json.dumps(out, indent=2, ensure_ascii=False))
    else:
        for i in out:
            if "error" in i:
                print(f"ERROR {i['archivo']}: {i['error']}")
                continue
            print(f"{i['archivo']}  {i['bytes'] / 2**30:.2f} GiB  formato={i['formato']}")
            for n in i["notas"]:
                print(f"    nota: {n}")
            print(f"    Hyper-V (Windows 11): {i['hyper_v']}")
            print(f"    KVM/QEMU (Ubuntu):    {i['kvm']}")
    print("\nEste informe NO cubre: SO invitado, drivers/HAL, UEFI vs BIOS, licencia del SO invitado,"
          " dependencias de hardware (llaves USB, puertos COM) ni si el disco arranca. Eso exige la VM real.",
          file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
