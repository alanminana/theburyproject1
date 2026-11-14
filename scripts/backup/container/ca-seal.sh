#!/bin/sh
# Corre DENTRO del contenedor ca-seal (imagen rclone, sin red, caddy-data en solo lectura, /tmp en memoria).
# Sella la clave privada de la CA de Caddy: tar de caddy/pki/authorities/*/*.key -> rclone crypt (NaCl secretbox: cifrado AUTENTICADO) ->
# /backups/files/<tar>.caseal. El tar en claro solo existe como tuberia en memoria. La clave de sellado llega por el entorno
# (RCLONE_CONFIG_CASEAL_PASSWORD / PASSWORD2, desde BACKUP_CA_SEAL_ENV, FUERA de BACKUP_DIR). Nunca imprime material de claves.
# Verifica releyendo el resultado: descifra, y compara el sha256 del flujo en claro con el del origen.
# Uso: ca-seal.sh <files_AAAA-MM-DD_HHMMSSZ.tar.gz>        Salida: ca_seal=ok entradas=N | ca_seal=none (no hay claves que sellar)
set -eu
umask 077

name="${1:?falta nombre del tar de archivos}"
case "$name" in
  files_[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]_[0-9][0-9][0-9][0-9][0-9][0-9]Z.tar.gz) ;;
  *) echo "nombre de archivo invalido: $name" >&2; exit 2 ;;
esac
[ -f /backups/.bury-backup-root ] || { echo "falta /backups/.bury-backup-root: directorio de backups no montado" >&2; exit 2; }
[ -d /backups/files ] || { echo "falta /backups/files" >&2; exit 2; }
[ -d /src/caddy-data ] || { echo "volumen /src/caddy-data no montado" >&2; exit 2; }
[ -n "${RCLONE_CONFIG_CASEAL_PASSWORD:-}" ] && [ -n "${RCLONE_CONFIG_CASEAL_PASSWORD2:-}" ] \
  || { echo "falta la clave de sellado (BACKUP_CA_SEAL_ENV no definido, ilegible o incompleto): ver scripts/backup/ca-seal-init.sh" >&2; exit 2; }

out="$name.caseal"
[ ! -e "/backups/files/$out" ] || { echo "ya existe $out: no se sobrescribe" >&2; exit 2; }

cd /src/caddy-data
find caddy/pki/authorities -type f -name '*.key' 2>/dev/null | sort >/tmp/lista || true
if [ ! -s /tmp/lista ]; then echo "ca_seal=none (sin caddy/pki/authorities/*/*.key)"; exit 0; fi
n=$(wc -l </tmp/lista)

tar -cf - -T /tmp/lista | rclone rcat "caseal:$out.partial"
want=$(tar -cf - -T /tmp/lista | sha256sum | cut -d' ' -f1)
got=$(rclone cat "caseal:$out.partial" | sha256sum | cut -d' ' -f1)
[ -n "$want" ] && [ "$want" = "$got" ] || { rm -f "/backups/files/$out.partial"; echo "verificacion del sellado fallo (el flujo descifrado no coincide con el origen)" >&2; exit 3; }
mv "/backups/files/$out.partial" "/backups/files/$out"
echo "ca_seal=ok archivo=$out entradas=$n bytes=$(stat -c %s "/backups/files/$out")"
