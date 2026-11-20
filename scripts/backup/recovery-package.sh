#!/usr/bin/env bash
# PAQUETE DE RECUPERACION CIFRADO (P-06). Reune la configuracion y los secretos que NO viajan en los respaldos (.env, backup-offsite.env,
# que contienen SA, admin, passwords crypt y credenciales de transporte) en UN archivo cifrado que se guarda FUERA del servidor.
# Sin este paquete, perder el servidor deja los respaldos de SQL/archivos sin los secretos para volver a levantarlos.
#
#   scripts/backup/recovery-package.sh create  --out DIR [--passphrase-file F]   crea DIR/bury-recovery_<UTC>.gpg (+ .sha256)
#   scripts/backup/recovery-package.sh verify  PAQUETE.gpg [--passphrase-file F]  descifra, valida el contenido y lista NOMBRES (nunca contenidos)
#   scripts/backup/recovery-package.sh extract PAQUETE.gpg --to DIR [--passphrase-file F]   restaura los archivos (DIR nuevo, 0700; usar tmpfs)
#
# Seguridad (docs/backup-custodia.md):
#  - Cifrado AUTENTICADO: GnuPG simetrico AES-256 con MDC (cualquier alteracion hace fallar el descifrado) y S2K SHA-512 iterado. Requiere gpg 2.x.
#  - La passphrase se pide por terminal (read -s, dos veces) o se lee de un archivo modo 600 (--passphrase-file); JAMAS por argv/entorno ni se imprime.
#  - Temporales (passphrase, staging, GNUPGHOME) SOLO en un directorio privado sobre tmpfs verificado; si no hay, se aborta (nunca cae a disco).
#  - La salida no puede estar dentro de BACKUP_DIR ni en el mismo directorio que el archivo de passphrase (C09).
#  - extract solo acepta archivos REGULARES de nombre simple (sin enlaces, rutas ni dispositivos) y nunca sobrescribe.
#  - create rechaza dos archivos de configuracion con el mismo nombre (se perderia uno). El .sha256 es solo una comodidad: la autenticidad la da el MDC.
# Custodia A (passphrase) y B (paquete) deben ser distintas y estar fuera del servidor. Contenido: .env (COMPOSE_ENV_FILES), backup-offsite.env si
# existe y MANIFEST.txt (nombres, fecha, commit; sin valores).
# Codigos: 0 ok | 1 uso/configuracion | 3 integridad/descifrado | 8 falta gpg/tar o no hay tmpfs
# shellcheck source=lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
set +e

EX_PKG=3
MODE="${1:-}"; shift || true
OUT="" PKG="" TO="" PASSFILE=""
while (( $# )); do
  case "$1" in
    --out) OUT="${2:-}"; shift 2 ;;
    --to) TO="${2:-}"; shift 2 ;;
    --passphrase-file) PASSFILE="${2:-}"; shift 2 ;;
    -h|--help) sed -n '2,22p' "$0"; exit 0 ;;
    -*) echo "argumento desconocido: $1" >&2; exit $EX_USAGE ;;
    *) PKG="$1"; shift ;;
  esac
done
command -v gpg >/dev/null 2>&1 && command -v tar >/dev/null 2>&1 || { echo "falta gpg (GnuPG 2.x) o tar" >&2; exit $EX_PREREQ; }
load_config
LOG_FILE=""
umask 077

abspath() { readlink -f "$1" 2>/dev/null || echo "$1"; }

# Directorio privado SOLO sobre tmpfs/ramfs: ahi viven la passphrase, el staging y GNUPGHOME. Sin tmpfs no se continua.
WORK=""
make_work() {
  local base t
  for base in "${XDG_RUNTIME_DIR:-}" /dev/shm; do
    [[ -n "$base" && -d "$base" && -w "$base" ]] || continue
    t=$(stat -f -c %T "$base" 2>/dev/null)
    [[ "$t" == tmpfs || "$t" == ramfs ]] || continue
    WORK=$(mktemp -d "$base/bury-rp.XXXXXX" 2>/dev/null) && [[ -n "$WORK" && -d "$WORK" ]] && break
    WORK=""
  done
  [[ -n "$WORK" ]] || { echo "no hay un directorio tmpfs escribible (XDG_RUNTIME_DIR o /dev/shm): se aborta para no dejar secretos en disco" >&2; exit $EX_PREREQ; }
  chmod 700 "$WORK"
}
cleanup() {
  [[ -n "$WORK" && -d "$WORK" ]] || return 0
  GNUPGHOME="$WORK/gnupg" gpgconf --kill gpg-agent >/dev/null 2>&1
  rm -rf "$WORK"
}
trap cleanup EXIT
trap 'cleanup; exit 130' INT TERM HUP

read_pass() { # deja la passphrase en $WORK/pass (0600, tmpfs); nunca en argv/entorno
  PASS="$WORK/pass"
  if [[ -n "$PASSFILE" ]]; then
    [[ -f "$PASSFILE" ]] || { echo "no existe el archivo de passphrase" >&2; exit $EX_USAGE; }
    local m; m=$(stat -c %a "$PASSFILE" 2>/dev/null || echo 600)
    [[ "$m" =~ ^[0-7]00$ ]] || { echo "el archivo de passphrase debe tener modo 600 (tiene $m)" >&2; exit $EX_USAGE; }
    [[ -s "$PASSFILE" ]] || { echo "el archivo de passphrase esta vacio" >&2; exit $EX_USAGE; }
    head -n1 "$PASSFILE" | tr -d '\r\n' >"$PASS"
  else
    [[ -t 0 ]] || { echo "sin terminal: usar --passphrase-file" >&2; exit $EX_USAGE; }
    local p1 p2
    read -rsp "Passphrase del paquete: " p1; echo >&2
    if [[ "$MODE" == create ]]; then read -rsp "Repetir passphrase: " p2; echo >&2; [[ "$p1" == "$p2" ]] || { echo "no coinciden" >&2; exit $EX_USAGE; }; fi
    printf '%s' "$p1" >"$PASS"; p1="" p2=""
  fi
  (( $(wc -c <"$PASS") >= 12 )) || { echo "passphrase demasiado corta (minimo 12 caracteres)" >&2; exit $EX_USAGE; }
  mkdir -p "$WORK/gnupg" && chmod 700 "$WORK/gnupg"
}
gpgx() { GNUPGHOME="$WORK/gnupg" gpg --batch --yes --quiet --no-tty --pinentry-mode loopback --passphrase-file "$PASS" "$@"; }

# Nombres aceptados dentro del paquete: archivos regulares simples (sin '/', sin '..').
entries_ok() { # entries_ok <listado tar tvf> -> 0 si todas son archivos regulares con nombre simple
  local line type name
  while IFS= read -r line; do
    [[ -n "$line" ]] || continue
    type=${line:0:1}
    name=${line##* }
    [[ "$type" == "-" && "$name" =~ ^[A-Za-z0-9._-]+$ && "$name" != "." && "$name" != ".." ]] || return 1
  done <<<"$1"
}

case "$MODE" in
  create)
    [[ -n "$OUT" ]] || { echo "falta --out DIR" >&2; exit $EX_USAGE; }
    mkdir -p "$OUT" && chmod 700 "$OUT" 2>/dev/null
    OUT=$(abspath "$OUT"); BD=$(abspath "${BACKUP_DIR:-/nonexistent}")
    [[ "$OUT" == "$BD" || "$OUT" == "$BD"/* ]] && { echo "--out no puede estar dentro de BACKUP_DIR (la clave de los respaldos no vive junto a ellos)" >&2; exit $EX_USAGE; }
    [[ -n "$PASSFILE" && "$(dirname "$(abspath "$PASSFILE")")" == "$OUT" ]] && { echo "el archivo de passphrase no puede estar en el mismo directorio que el paquete" >&2; exit $EX_USAGE; }
    make_work; read_pass
    STAGE="$WORK/stage"; mkdir -p "$STAGE"
    files=()
    add_file() { # add_file RUTA -> rechaza nombres repetidos y comprueba la copia
      local f=$1 b; b=$(basename "$f")
      [[ "$b" =~ ^[A-Za-z0-9._-]+$ ]] || { echo "nombre de archivo no soportado: $b" >&2; exit $EX_USAGE; }
      if [[ -e "$STAGE/$b" ]]; then echo "dos archivos de configuracion con el mismo nombre ($b): se perderia uno; renombrar o empaquetar por separado" >&2; exit $EX_USAGE; fi
      cp "$f" "$STAGE/$b" || { echo "no se pudo copiar $b" >&2; exit $EX_USAGE; }
      files+=("$b")
    }
    while IFS= read -r f; do [[ -f "$f" ]] && add_file "$f"; done < <(_envfiles)
    OFF="${BURY_OFFSITE_ENV_FILE:-$REPO_DIR/backup-offsite.env}"
    [[ -f "$OFF" ]] && add_file "$OFF"
    (( ${#files[@]} > 0 )) || { echo "no hay .env que empaquetar" >&2; exit $EX_USAGE; }
    { echo "bury-recovery-package v2"; echo "created=$(date -u +%Y-%m-%dT%H:%M:%SZ)"; echo "commit=$(git -C "$REPO_DIR" rev-parse --short HEAD 2>/dev/null || echo desconocido)"; printf 'file=%s\n' "${files[@]}"; } >"$STAGE/MANIFEST.txt"
    NAME="bury-recovery_$(date -u +%Y%m%d-%H%M%SZ).gpg"
    tar -C "$STAGE" -czf - MANIFEST.txt "${files[@]}" \
      | gpgx --symmetric --cipher-algo AES256 --s2k-mode 3 --s2k-digest-algo SHA512 --s2k-count 65011712 -o "$OUT/$NAME" \
      || { rm -f "$OUT/$NAME"; echo "fallo el cifrado" >&2; exit $EX_PKG; }
    [[ -s "$OUT/$NAME" ]] || { rm -f "$OUT/$NAME"; echo "el paquete quedo vacio" >&2; exit $EX_PKG; }
    chmod 600 "$OUT/$NAME"
    ( cd "$OUT" && sha256sum "$NAME" >"$NAME.sha256" && chmod 600 "$NAME.sha256" )
    echo "paquete creado: $OUT/$NAME ($(wc -c <"$OUT/$NAME") bytes; $((${#files[@]})) archivo(s) de configuracion + MANIFEST)"
    echo "SIGUIENTE: guardar el paquete (custodia B) y la passphrase (custodia A) en lugares DISTINTOS y fuera del servidor."
    ;;
  verify|extract)
    [[ -n "$PKG" && -f "$PKG" ]] || { echo "falta el paquete" >&2; exit $EX_USAGE; }
    if [[ -f "$PKG.sha256" ]]; then
      ( cd "$(dirname "$PKG")" && sha256sum -c "$(basename "$PKG").sha256" >/dev/null 2>&1 ) || { echo "el SHA-256 no coincide (copia danada o paquete reemplazado)" >&2; exit $EX_PKG; }
      echo "sha256 OK"
    fi
    make_work; read_pass
    # El descifrado autenticado (MDC) falla ante cualquier alteracion; el contenido se valida ANTES de extraer.
    gpgx -d -o "$WORK/payload.tgz" "$PKG" 2>/dev/null && [[ -s "$WORK/payload.tgz" ]] || { echo "no se pudo descifrar/autenticar (passphrase incorrecta o paquete alterado)" >&2; exit $EX_PKG; }
    listing=$(tar tvzf "$WORK/payload.tgz" 2>/dev/null) || { echo "el contenido del paquete no es un tar valido" >&2; exit $EX_PKG; }
    entries_ok "$listing" || { echo "el paquete contiene entradas no permitidas (enlaces, rutas o tipos especiales): se rechaza" >&2; exit $EX_PKG; }
    names=$(tar tzf "$WORK/payload.tgz" 2>/dev/null)
    grep -qx MANIFEST.txt <<<"$names" || { echo "el paquete no tiene MANIFEST.txt" >&2; exit $EX_PKG; }
    if [[ "$MODE" == verify ]]; then
      echo "paquete valido (descifrado autenticado); contenido (solo nombres):"; sed 's/^/  - /' <<<"$names"
    else
      [[ -n "$TO" ]] || { echo "falta --to DIR" >&2; exit $EX_USAGE; }
      [[ ! -e "$TO" ]] || { echo "--to debe ser un directorio NUEVO (no se sobrescribe nada)" >&2; exit $EX_USAGE; }
      mkdir -p "$TO" && chmod 700 "$TO"
      tar xzf "$WORK/payload.tgz" -C "$TO" --no-same-owner --no-same-permissions 2>/dev/null || { rm -rf "$TO"; echo "fallo la extraccion" >&2; exit $EX_PKG; }
      find "$TO" -maxdepth 1 -type f -exec chmod 600 {} +
      echo "restaurado en $TO (modo 700). Copiar los .env a su lugar, y BORRAR este directorio al terminar."
    fi
    ;;
  *) sed -n '2,22p' "$0"; exit $EX_USAGE ;;
esac
