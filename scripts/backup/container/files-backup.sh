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

# Clave privada de la CA de Caddy (caddy/pki/authorities/*/*.key = root.key + intermediate.key). BACKUP_CA_KEY:
#   include (default, comportamiento historico)  las claves viajan DENTRO del tar, sin cifrar a nivel local.
#   seal     el tar NO las contiene; backup.sh las sella aparte en <tar>.caseal (ca-seal.sh) con una clave de sellado fuera de BACKUP_DIR.
# Las claves de las HOJAS (caddy/certificates/...) no se excluyen: Caddy las regenera y no firman a nadie mas.
ca_key_mode="${BACKUP_CA_KEY:-include}"
case "$ca_key_mode" in include|seal) ;; *) echo "BACKUP_CA_KEY invalido: $ca_key_mode (include|seal)" >&2; exit 2 ;; esac
excl=""
if [ "$ca_key_mode" = "seal" ] && [ "${BACKUP_INCLUDE_CADDY:-true}" = "true" ]; then
  excl="--exclude=caddy-data/caddy/pki/authorities/*/*.key*"
fi

out="/backups/files/$name"
rm -f "$out.partial"
# shellcheck disable=SC2086  # $excl es una unica opcion sin espacios
tar -czf "$out.partial" $excl -C /src $list
gzip -t "$out.partial"
tar -tzf "$out.partial" >/dev/null
# Fail-closed: en modo seal ningun *.key de una CA puede haber quedado en el tar (un cambio de busybox/patron no puede reintroducirlo en silencio).
if [ -n "$excl" ] && tar -tzf "$out.partial" | grep -E 'caddy/pki/authorities/.*\.key' >/dev/null; then
  echo "BACKUP_CA_KEY=seal pero el tar contiene claves privadas de la CA: se descarta" >&2; rm -f "$out.partial"; exit 3
fi
[ -s "$out.partial" ] || { echo "archivo vacio" >&2; rm -f "$out.partial"; exit 3; }
mv "$out.partial" "$out"
( cd /backups/files && sha256sum "$name" > "$name.sha256" )

size=$(stat -c %s "$out")
entries=$(tar -tzf "$out" | wc -l)
echo "archivo=$name bytes=$size entradas=$entries contenido=[$list] ca_key=$ca_key_mode"
