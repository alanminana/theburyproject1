#!/bin/sh
# Corre DENTRO del contenedor backup-files (alpine, volumenes montados en solo lectura en /src).
# Genera /backups/files/<archivo>.tar.gz con keys/ uploads/ appdata/ (y caddy-data/ si BACKUP_INCLUDE_CADDY=true), lo verifica
# (gzip -t + listado del tar) y escribe <archivo>.sha256. Escribe primero a .partial y renombra al final: nunca queda un
# .tar.gz a medias. El llamador (backup.sh) detiene `app` antes para que los archivos no cambien durante la lectura.
# Uso: files-backup.sh <nombre-archivo.tar.gz>
set -eu

name="${1:?falta nombre de archivo}"
case "$name" in
  files_[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]_[0-9][0-9][0-9][0-9][0-9][0-9]Z.tar.gz) ;;
  *) echo "nombre de archivo invalido: $name" >&2; exit 2 ;;
esac

[ -f /backups/.bury-backup-root ] || { echo "falta /backups/.bury-backup-root: directorio de backups no montado" >&2; exit 2; }
mkdir -p /backups/files
umask 077

list="keys uploads appdata"
[ "${BACKUP_INCLUDE_CADDY:-true}" = "true" ] && list="$list caddy-data"

for d in $list; do
  [ -d "/src/$d" ] || { echo "volumen /src/$d no montado" >&2; exit 2; }
done

out="/backups/files/$name"
rm -f "$out.partial"
tar -czf "$out.partial" -C /src $list
gzip -t "$out.partial"
tar -tzf "$out.partial" >/dev/null
[ -s "$out.partial" ] || { echo "archivo vacio" >&2; rm -f "$out.partial"; exit 3; }
mv "$out.partial" "$out"
( cd /backups/files && sha256sum "$name" > "$name.sha256" )

size=$(stat -c %s "$out")
entries=$(tar -tzf "$out" | wc -l)
echo "archivo=$name bytes=$size entradas=$entries contenido=[$list]"
