#!/bin/sh
# Corre DENTRO del contenedor ca-unseal (imagen rclone, sin red, caddy-data en escritura, backups en solo lectura, /tmp en memoria).
# Descifra <tar>.caseal, valida el contenido (SOLO archivos regulares caddy/pki/authorities/<ca>/<nombre>.key; nada de rutas con ..,
# enlaces ni otras entradas) y repone las claves en caddy-data con modo 0600. Una clave de sellado incorrecta falla en la autenticacion
# de rclone crypt (no se escribe nada). Si ya existe una clave DISTINTA en el destino, aborta salvo FORCE=1.
# Uso: ca-unseal.sh <files_*.tar.gz>     env: DRYRUN=1 (solo descifrar y validar, sin escribir), FORCE=1
# Salida: ca_unseal=ok|dryrun entradas=N repuestas=M    (jamas imprime material de claves)
set -eu
umask 077

name="${1:?falta nombre del tar de archivos}"
case "$name" in
  files_[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]_[0-9][0-9][0-9][0-9][0-9][0-9]Z.tar.gz) ;;
  *) echo "nombre de archivo invalido: $name" >&2; exit 2 ;;
esac
src="$name.caseal"
[ -s "/backups/files/$src" ] || { echo "no existe o esta vacio: $src" >&2; exit 2; }
[ -n "${RCLONE_CONFIG_CASEAL_PASSWORD:-}" ] && [ -n "${RCLONE_CONFIG_CASEAL_PASSWORD2:-}" ] \
  || { echo "falta la clave de sellado (BACKUP_CA_SEAL_ENV no definido, ilegible o incompleto)" >&2; exit 2; }
[ "${DRYRUN:-0}" = "1" ] || [ -d /dst/caddy-data ] || { echo "volumen /dst/caddy-data no montado" >&2; exit 2; }

rclone cat "caseal:$src" >/tmp/ca.tar 2>/tmp/err \
  || { echo "no se pudo descifrar $src: clave de sellado incorrecta o archivo corrupto/alterado" >&2; rm -f /tmp/ca.tar; exit 3; }
tar -tvf /tmp/ca.tar >/tmp/lista-v 2>/dev/null || { echo "el contenido descifrado no es un tar valido" >&2; exit 3; }
[ -s /tmp/lista-v ] || { echo "el sellado esta vacio" >&2; exit 3; }
# solo archivos regulares (primer caracter '-'), con ruta exacta caddy/pki/authorities/<ca>/<archivo>.key y sin '..'
if grep -vE '^-' /tmp/lista-v >/dev/null; then echo "el sellado contiene entradas que no son archivos regulares: rechazado" >&2; exit 3; fi
tar -tf /tmp/ca.tar >/tmp/lista
if grep -vE '^caddy/pki/authorities/[A-Za-z0-9._-]+/[A-Za-z0-9._-]+\.key$' /tmp/lista >/dev/null || grep -F '..' /tmp/lista >/dev/null; then
  echo "el sellado contiene rutas inesperadas: rechazado" >&2; exit 3
fi
n=$(wc -l </tmp/lista)

if [ "${DRYRUN:-0}" = "1" ]; then echo "ca_unseal=dryrun entradas=$n repuestas=0"; exit 0; fi

mkdir /tmp/x && tar -xpf /tmp/ca.tar -C /tmp/x
# Dos pasadas: primero se comprueba que NO se pisa una clave distinta; recien despues se escribe (todo o nada).
while IFS= read -r f; do
  if [ -e "/dst/caddy-data/$f" ] && ! cmp -s "/tmp/x/$f" "/dst/caddy-data/$f" && [ "${FORCE:-0}" != "1" ]; then
    echo "ABORTADO: /dst/caddy-data/$f ya existe y es DISTINTA de la sellada. Usar --force solo si se quiere reemplazarla." >&2; exit 4
  fi
done </tmp/lista
rep=0
while IFS= read -r f; do
  if [ -e "/dst/caddy-data/$f" ] && cmp -s "/tmp/x/$f" "/dst/caddy-data/$f"; then chmod 600 "/dst/caddy-data/$f"; continue; fi
  mkdir -p "/dst/caddy-data/$(dirname "$f")"
  cat "/tmp/x/$f" >"/dst/caddy-data/$f.tmp" && chmod 600 "/dst/caddy-data/$f.tmp" && mv -f "/dst/caddy-data/$f.tmp" "/dst/caddy-data/$f"
  rep=$((rep + 1))
done </tmp/lista
rm -rf /tmp/x /tmp/ca.tar
echo "ca_unseal=ok entradas=$n repuestas=$rep"
