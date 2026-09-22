#!/bin/sh
# Corre DENTRO del contenedor restore-files (volumenes con estado montados en escritura en /dst; backups en solo lectura).
# Restaura un archivo files_*.tar.gz. Verifica sha256 y la integridad del tar ANTES de tocar nada.
# Si algun destino ya tiene contenido, aborta salvo FORCE=1 (en cuyo caso lo VACIA antes de extraer).
# Uso: files-restore.sh <nombre-archivo.tar.gz>     (env: FORCE=1)
set -eu

name="${1:?falta nombre de archivo}"
case "$name" in
  files_[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]_[0-9][0-9][0-9][0-9][0-9][0-9]Z.tar.gz) ;;
  *) echo "nombre de archivo invalido: $name" >&2; exit 2 ;;
esac

src="/backups/files/$name"
[ -s "$src" ] || { echo "no existe o esta vacio: $src" >&2; exit 2; }

if [ -f "$src.sha256" ]; then
  ( cd /backups/files && sha256sum -c "$name.sha256" >/dev/null ) || { echo "sha256 NO coincide: backup corrupto" >&2; exit 3; }
  echo "sha256 OK"
else
  echo "ADVERTENCIA: sin $name.sha256; solo se verifica el tar" >&2
fi
gzip -t "$src"
tar -tzf "$src" >/dev/null

# Volumenes contenidos en el archivo (primer componente de cada ruta).
dirs=$(tar -tzf "$src" | cut -d/ -f1 | sort -u)
for d in $dirs; do
  case "$d" in keys|uploads|appdata|caddy-data) ;; *) echo "entrada inesperada en el archivo: $d" >&2; exit 3 ;; esac
  [ -d "/dst/$d" ] || { echo "volumen /dst/$d no montado" >&2; exit 2; }
done

for d in $dirs; do
  if [ -n "$(ls -A "/dst/$d")" ]; then
    if [ "${FORCE:-0}" != "1" ]; then
      echo "ABORTADO: /dst/$d (volumen $d) ya tiene contenido. Usar --force para vaciarlo y restaurar." >&2
      exit 4
    fi
    find "/dst/$d" -mindepth 1 -delete
  fi
done

tar -xzpf "$src" -C /dst
echo "restaurado: $name -> [$(echo $dirs)]"
for d in $dirs; do
  echo "  $d: $(find "/dst/$d" -type f | wc -l) archivos, dueño $(stat -c %u:%g "/dst/$d")"
done
