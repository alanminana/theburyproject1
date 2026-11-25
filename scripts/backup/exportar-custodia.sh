#!/usr/bin/env bash
# EXPORTA LAS CLAVES DE LA COPIA LOCAL CIFRADA (LUKS) A UN ARCHIVO CIFRADO PARA LA CUSTODIA EXTERNA (B5). Complementa recovery-package.sh:
# el paquete de recuperacion lleva .env y backup-offsite.env; este lleva lo que SOLO existe en /etc/bury-backup (root, 0400): la clave y el
# encabezado LUKS de cada copia local (`setup-local-disk-copy.sh --name N`). Sin esa clave, el pendrive/disco de respaldos es ilegible.
#
#   sudo scripts/backup/exportar-custodia.sh create --out DIR --passphrase-file F [--name bury-prov]... [--env-file .env]   (sin --name: todas las copias)
#        scripts/backup/exportar-custodia.sh verify PAQUETE.gpg --passphrase-file F     descifra, valida y lista NOMBRES (nunca contenidos)
#        scripts/backup/exportar-custodia.sh extract PAQUETE.gpg --to DIR --passphrase-file F   restaura (DIR nuevo, 0700; usar tmpfs)
#
# Reglas (mismo modelo que recovery-package.sh, ver docs/backup-custodia.md): GnuPG simetrico AES-256 con MDC (cualquier alteracion falla el
# descifrado); passphrase solo desde archivo 0600 o terminal (nunca argv/entorno ni se imprime); temporales SOLO en tmpfs (/dev/shm), si no hay se
# aborta; la salida no puede estar dentro del directorio de la passphrase ni del BACKUP_DIR/BACKUP_REMOVABLE_DIR (la clave no puede vivir junto a
# los respaldos que descifra). BACKUP_DIR y BACKUP_REMOVABLE_DIR se leen del entorno o, si faltan (sudo los borra), de --env-file (default: .env del repo
# donde vive este script); si no se pueden determinar, `create` SE NIEGA (falla cerrado: nunca omite el control). Guardar la passphrase (custodia A) y este archivo (custodia B) en lugares DISTINTOS y FUERA del servidor.
# Codigos: 0 ok | 1 uso/configuracion | 3 integridad/descifrado | 8 falta gpg/tar o no hay tmpfs o no hay claves
set -uo pipefail
KEYDIR="${BURY_KEYDIR:-/etc/bury-backup}"
MODE="${1:-}"; shift || true
OUT="" PKG="" TO="" PASSFILE="" NAMES=() ENVF=""
REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
need() { [[ $# -ge 2 && -n "${2:-}" ]] || { echo "falta el valor de $1 (ver --help)" >&2; exit 1; }; }
while (( $# )); do
  case "$1" in
    --out) need "$@"; OUT="$2"; shift 2 ;; --to) need "$@"; TO="$2"; shift 2 ;; --passphrase-file) need "$@"; PASSFILE="$2"; shift 2 ;;
    --name) need "$@"; NAMES+=("$2"); shift 2 ;; --env-file) need "$@"; ENVF="$2"; shift 2 ;;
    -h|--help) sed -n '2,15p' "$0"; exit 0 ;; -*) echo "argumento desconocido: $1" >&2; exit 1 ;; *) PKG="$1"; shift ;;
  esac
done
die() { echo "ERROR: $1" >&2; exit "${2:-1}"; }
command -v gpg >/dev/null 2>&1 && command -v tar >/dev/null 2>&1 || die "falta gpg (2.x) o tar" 8
[[ "$(stat -f -c %T /dev/shm 2>/dev/null)" == tmpfs ]] || die "no hay /dev/shm en tmpfs: no se escriben claves en disco" 8
[[ "$MODE" =~ ^(create|verify|extract)$ ]] || { sed -n '2,8p' "$0" >&2; exit 1; }
if [[ -n "$PASSFILE" ]]; then
  [[ -f "$PASSFILE" && ! -L "$PASSFILE" && "$(stat -c %a "$PASSFILE")" = 600 && -s "$PASSFILE" ]] || die "--passphrase-file debe ser un archivo regular, no vacio, modo 600"
else die "falta --passphrase-file (modo 600)"; fi
WORK="$(mktemp -d /dev/shm/bury-xc.XXXXXX)" || die "no se pudo crear el temporal en tmpfs" 8
chmod 700 "$WORK"; export GNUPGHOME="$WORK/gh"; mkdir -m 700 "$GNUPGHOME"
trap 'gpgconf --kill gpg-agent >/dev/null 2>&1; rm -rf "$WORK"' EXIT
gpgx() { gpg --batch --yes --quiet --no-tty --pinentry-mode loopback --passphrase-file "$PASSFILE" "$@"; }
unpack() { gpgx -d -o "$WORK/p.tgz" "$1" 2>/dev/null && [[ -s "$WORK/p.tgz" ]] || die "no se pudo descifrar/autenticar (passphrase incorrecta o paquete alterado)" 3
  # solo archivos regulares de nombre simple (sin enlaces, rutas ni dispositivos)
  tar -tzvf "$WORK/p.tgz" 2>/dev/null | awk '{print substr($1,1,1), $NF}' | while read -r t n; do [[ "$t" = "-" && "$n" != */* && "$n" != .* ]] || exit 1; done \
    || die "el paquete contiene entradas no permitidas (enlaces, rutas o dispositivos)" 3; }

case "$MODE" in
create)
  [[ -n "$OUT" && -d "$OUT" ]] || die "--out debe ser un directorio existente"
  [[ "$(cd "$OUT" && pwd -P)" != "$(cd "$(dirname "$PASSFILE")" && pwd -P)" ]] || die "la salida no puede estar en el mismo directorio que la passphrase (custodias A y B separadas)"
  envv() { grep -E "^$1=" "${ENVF:-$REPO_DIR/.env}" 2>/dev/null | tail -1 | cut -d= -f2-; }
  BD="${BACKUP_DIR:-$(envv BACKUP_DIR)}"; RD="${BACKUP_REMOVABLE_DIR:-$(envv BACKUP_REMOVABLE_DIR)}"
  [[ -n "$BD" ]] || die "no se pudo determinar BACKUP_DIR (ni en el entorno ni en ${ENVF:-$REPO_DIR/.env}): pasar --env-file; el control de separacion no se omite" 1
  [[ -d "$BD" ]] || die "BACKUP_DIR=$BD no existe o no es un directorio: no se puede aplicar el control de separacion" 1
  OUTR="$(cd "$OUT" && pwd -P)"
  for d in "$BD" "$RD"; do [[ -z "$d" ]] && continue; DR="$(cd "$d" 2>/dev/null && pwd -P)" || continue
    case "$OUTR/" in "$DR"/*) die "la salida esta dentro de $d: la clave no puede vivir junto a los respaldos" 1 ;; esac; done
  mkdir -p "$WORK/stage"; : >"$WORK/stage/MANIFEST.txt"
  if ((${#NAMES[@]})); then files=(); for n in "${NAMES[@]}"; do files+=("$KEYDIR/local-disk-$n.key" "$KEYDIR/local-disk-$n.luks-header"); done
  else mapfile -t files < <(ls "$KEYDIR"/local-disk*.key "$KEYDIR"/local-disk*.luks-header 2>/dev/null); fi
  ((${#files[@]})) || die "no hay claves LUKS en $KEYDIR (setup-local-disk-copy.sh --apply las crea)" 8
  # cada copia necesita el PAR completo: sin la clave o sin el encabezado el disco no se puede reabrir tras perder el servidor
  declare -A __par=()
  for f in "${files[@]}"; do b="$(basename "$f")"; b="${b%.luks-header}"; b="${b%.key}"; __par["$b"]=1; done
  for b in "${!__par[@]}"; do for sfx in key luks-header; do
    [[ -f "$KEYDIR/$b.$sfx" && ! -L "$KEYDIR/$b.$sfx" && -s "$KEYDIR/$b.$sfx" ]] || die "la copia '$b' esta incompleta: falta o esta vacio $KEYDIR/$b.$sfx (se necesitan la clave Y el encabezado)" 8
  done; done
  files=(); for b in "${!__par[@]}"; do files+=("$KEYDIR/$b.key" "$KEYDIR/$b.luks-header"); done
  { echo "bury-custodia-luks v1"; echo "fecha=$(date -u +%FT%TZ)"; echo "host=$(hostname)"; } >>"$WORK/stage/MANIFEST.txt"
  for f in "${files[@]}"; do [[ -f "$f" && ! -L "$f" ]] || die "falta o es un enlace: $f" 8; [[ -s "$f" ]] || die "esta vacio: $f" 8
    cp -p -- "$f" "$WORK/stage/$(basename "$f")" && cmp -s -- "$f" "$WORK/stage/$(basename "$f")" || die "no se pudo copiar completo: $f (el paquete quedaria incompleto: NO se crea)" 8
    echo "$(basename "$f") sha256=$(sha256sum "$f" | cut -c1-16)..." >>"$WORK/stage/MANIFEST.txt"; done
  ( cd "$WORK/stage" && tar -czf "$WORK/p.tgz" -- * ) 2>/dev/null || die "no se pudo empaquetar" 1
  NAME="bury-custodia-luks_$(date -u +%Y%m%d-%H%M%SZ)_$(od -An -N4 -tx1 /dev/urandom | tr -d ' \n').gpg"
  gpgx --symmetric --cipher-algo AES256 --s2k-mode 3 --s2k-digest-algo SHA512 --s2k-count 65011712 -o "$WORK/$NAME" "$WORK/p.tgz" || die "fallo el cifrado" 3
  UG="${SUDO_UID:-$(id -u)}:${SUDO_GID:-$(id -g)}"
  install -m 600 -o "${UG%%:*}" -g "${UG##*:}" "$WORK/$NAME" "$OUT/$NAME" || { rm -f "$OUT/$NAME" "$OUT/$NAME.sha256"; die "no se pudo guardar el paquete en $OUT (disco lleno, solo lectura o sin permiso): NO se creo la custodia" 1; }
  ( cd "$OUT" && sha256sum "$NAME" >"$NAME.sha256" && chmod 600 "$NAME.sha256" ) || { rm -f "$OUT/$NAME" "$OUT/$NAME.sha256"; die "no se pudo escribir el .sha256: NO se creo la custodia" 1; }
  chown "$UG" "$OUT/$NAME.sha256" 2>/dev/null || true
  [[ "$(stat -c %s "$OUT/$NAME")" = "$(stat -c %s "$WORK/$NAME")" ]] && ( cd "$OUT" && sha256sum -c "$NAME.sha256" >/dev/null 2>&1 ) || { rm -f "$OUT/$NAME" "$OUT/$NAME.sha256"; die "el paquete guardado no coincide con el generado: NO se creo la custodia" 1; }
  echo "paquete creado: $OUT/$NAME ($(stat -c %s "$OUT/$NAME") bytes; ${#files[@]} archivo(s) de clave + MANIFEST)"
  echo "SIGUIENTE: guardar este archivo (custodia B) y la passphrase (custodia A) en lugares DISTINTOS y fuera del servidor." ;;
verify)
  [[ -f "$PKG" ]] || die "falta PAQUETE.gpg"; unpack "$PKG"
  echo "paquete valido (descifrado autenticado); contenido (solo nombres):"; tar -tzf "$WORK/p.tgz" | sed 's/^/  - /' ;;
extract)
  [[ -f "$PKG" && -n "$TO" ]] || die "uso: extract PAQUETE.gpg --to DIR"; [[ ! -e "$TO" ]] || die "$TO ya existe (no se sobrescribe)"
  unpack "$PKG"; mkdir -m 700 "$TO" && tar -C "$TO" -xzf "$WORK/p.tgz" --no-same-owner && chmod 600 "$TO"/* && echo "restaurado en $TO (0700); copiar las claves a /etc/bury-backup (root, 0400) solo para reabrir el disco" ;;
esac
