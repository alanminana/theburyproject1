#!/bin/sh
# Corre DENTRO del contenedor ca-unseal (imagen rclone, sin red, caddy-data en escritura, backups en solo lectura, /tmp en memoria).
# Descifra <tar>.caseal, valida el contenido (SOLO archivos regulares caddy/pki/authorities/<ca>/<nombre>.key; nada de rutas con ..,
# enlaces ni otras entradas) y repone las claves en caddy-data con modo 0600. Una clave de sellado incorrecta falla en la autenticacion
# de rclone crypt (no se escribe nada). Si ya existe una clave DISTINTA en el destino, aborta salvo FORCE=1.
# El destino NO puede contener enlaces simbolicos (ni en el archivo, ni en su temporal, ni en los directorios padre): un tar de archivos
# manipulado podria haberlos plantado para desviar la clave descifrada fuera del area excluida del backup. Todo o nada: se preparan TODOS los
# temporales y recien despues se renombran; cualquier error de escritura aborta con rc 5 (jamas "ok" tras un fallo parcial).
# Uso: ca-unseal.sh <files_*.tar.gz>     env: DRYRUN=1 (solo descifrar y validar, sin escribir), FORCE=1
# Salida: ca_unseal=ok|dryrun entradas=N repuestas=M    (jamas imprime material de claves)
# rc: 0 ok | 2 uso/precondicion | 3 no descifra o contenido invalido | 4 clave distinta ya presente | 5 destino inseguro o fallo de escritura
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

# Ningun componente del destino (desde /dst/caddy-data) puede ser un enlace simbolico.
no_links() { # no_links <ruta relativa a /dst/caddy-data>
  p=/dst/caddy-data
  [ ! -L "$p" ] || return 1
  oldifs=$IFS; IFS=/
  # shellcheck disable=SC2086  # division por '/' intencional (los componentes ya se validaron con la lista blanca de arriba)
  set -- $1
  IFS=$oldifs
  for c in "$@"; do p="$p/$c"; [ ! -L "$p" ] || return 1; done
  return 0
}

# Pasada 1 (sin escribir): destino seguro y ninguna clave DISTINTA que pisar.
while IFS= read -r f; do
  no_links "$f" && no_links "$f.tmp.key" || { echo "ABORTADO: el destino de $f contiene un enlace simbolico (posible tar de archivos manipulado). No se escribio nada." >&2; exit 5; }
  if [ -e "/dst/caddy-data/$f" ]; then
    [ -f "/dst/caddy-data/$f" ] || { echo "ABORTADO: /dst/caddy-data/$f existe y no es un archivo regular." >&2; exit 5; }
    if ! cmp -s "/tmp/x/$f" "/dst/caddy-data/$f" && [ "${FORCE:-0}" != "1" ]; then
      echo "ABORTADO: /dst/caddy-data/$f ya existe y es DISTINTA de la sellada. Usar --force solo si se quiere reemplazarla." >&2; exit 4
    fi
  fi
done </tmp/lista

# Pasada 2: preparar TODOS los temporales (creacion exclusiva: nunca siguen ni reutilizan un enlace) y, solo si todos quedaron bien, renombrar.
: >/tmp/tmps
cleanup_tmps() { while IFS= read -r t; do rm -f "$t"; done </tmp/tmps; }
rep=0
while IFS= read -r f; do
  dest="/dst/caddy-data/$f"
  if [ -f "$dest" ] && cmp -s "/tmp/x/$f" "$dest"; then chmod 600 "$dest" || { cleanup_tmps; echo "no se pudo fijar el modo de $f" >&2; exit 5; }; continue; fi
  mkdir -p "$(dirname "$dest")" || { cleanup_tmps; echo "no se pudo crear el directorio de $f" >&2; exit 5; }
  tmp="$dest.tmp.key"
  rm -f "$tmp"
  echo "$tmp" >>/tmp/tmps
  if ! ( set -C; cat "/tmp/x/$f" >"$tmp" ) || ! chmod 600 "$tmp" || [ ! -f "$tmp" ] || [ -L "$tmp" ] || ! cmp -s "/tmp/x/$f" "$tmp"; then
    cleanup_tmps; echo "no se pudo escribir el temporal de $f (disco lleno o destino inseguro): no se repuso ninguna clave" >&2; exit 5
  fi
  echo "$f" >>/tmp/pend
  rep=$((rep + 1))
done </tmp/lista
if [ -s /tmp/pend ]; then
  while IFS= read -r f; do
    mv -f "/dst/caddy-data/$f.tmp.key" "/dst/caddy-data/$f" || { cleanup_tmps; echo "no se pudo renombrar $f: estado parcial, repetir con --force" >&2; exit 5; }
  done </tmp/pend
fi
rm -rf /tmp/x /tmp/ca.tar
echo "ca_unseal=ok entradas=$n repuestas=$rep"
