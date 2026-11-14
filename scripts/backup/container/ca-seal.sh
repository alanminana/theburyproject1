#!/bin/sh
# Corre DENTRO del contenedor ca-seal (imagen rclone, sin red, caddy-data en solo lectura, /tmp en memoria).
# Sella la clave privada de la CA de Caddy: tar de caddy/pki/authorities/*/*.key -> rclone crypt (NaCl secretbox: cifrado AUTENTICADO) ->
# /backups/files/<tar>.caseal. El tar en claro solo existe como tuberia en memoria. La clave de sellado llega por el entorno
# (RCLONE_CONFIG_CASEAL_PASSWORD / PASSWORD2, desde BACKUP_CA_SEAL_ENV, FUERA de BACKUP_DIR). Nunca imprime material de claves.
# Verifica releyendo el resultado: descifra, y compara el sha256 del flujo en claro con el del origen.
# Fail-closed: una CA con certificados pero SIN sus claves (restore incompleto, volumen danado) o un error al enumerar NO es "nada que sellar":
# falla (rc 3) para que backup.sh descarte el tar en vez de registrar un respaldo del que la CA no se puede recuperar.
# Uso: ca-seal.sh <files_AAAA-MM-DD_HHMMSSZ.tar.gz>
# Salida: ca_seal=ok entradas=N | ca_seal=none (Caddy aun no inicializo su CA: no hay certificados ni claves)
# rc: 0 ok | 2 uso/precondicion | 3 CA incompleta, error al enumerar o verificacion fallida
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
if [ ! -d caddy/pki/authorities ]; then echo "ca_seal=none (Caddy aun no creo caddy/pki/authorities)"; exit 0; fi
find caddy/pki/authorities -type f \( -name '*.key' -o -name '*.crt' \) >/tmp/todo || { echo "no se pudo enumerar caddy/pki/authorities" >&2; exit 3; }
sort /tmp/todo | grep -E '\.key$' >/tmp/lista || true
sort /tmp/todo | grep -E '\.crt$' >/tmp/certs || true
if [ ! -s /tmp/lista ] && [ ! -s /tmp/certs ]; then echo "ca_seal=none (sin certificados ni claves de CA)"; exit 0; fi
# cada certificado de CA debe tener su clave (root.crt -> root.key, intermediate.crt -> intermediate.key)
while IFS= read -r c; do
  grep -qxF "${c%.crt}.key" /tmp/lista || { echo "CA incompleta: existe $c pero no su clave privada (${c%.crt}.key): no hay nada recuperable que sellar" >&2; exit 3; }
done </tmp/certs
[ -s /tmp/lista ] || { echo "CA incompleta: hay certificados pero ninguna clave privada" >&2; exit 3; }
n=$(wc -l </tmp/lista)

tar -cf - -T /tmp/lista | rclone rcat "caseal:$out.partial"
want=$(tar -cf - -T /tmp/lista | sha256sum | cut -d' ' -f1)
got=$(rclone cat "caseal:$out.partial" | sha256sum | cut -d' ' -f1)
[ -n "$want" ] && [ "$want" = "$got" ] || { rm -f "/backups/files/$out.partial"; echo "verificacion del sellado fallo (el flujo descifrado no coincide con el origen)" >&2; exit 3; }
mv "/backups/files/$out.partial" "/backups/files/$out"
echo "ca_seal=ok archivo=$out entradas=$n bytes=$(stat -c %s "/backups/files/$out")"
