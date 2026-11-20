#!/usr/bin/env bash
# PAQUETE DE RECUPERACION CIFRADO (P-06). Reune la configuracion y los secretos que NO viajan en los respaldos (.env, backup-offsite.env,
# que contienen SA, admin, passwords crypt y credenciales de transporte) en UN archivo cifrado que se guarda FUERA del servidor.
# Sin este paquete, perder el servidor deja los respaldos de SQL/archivos sin los secretos para volver a levantarlos.
#
#   scripts/backup/recovery-package.sh create  --out DIR [--passphrase-file F]   crea DIR/bury-recovery_<UTC>.enc (+ .sha256)
#   scripts/backup/recovery-package.sh verify  PAQUETE.enc [--passphrase-file F]  integridad + descifra y lista NOMBRES (nunca contenidos)
#   scripts/backup/recovery-package.sh extract PAQUETE.enc --to DIR [--passphrase-file F]   restaura los archivos (DIR nuevo, 0700; usar tmpfs)
#
# Reglas de seguridad (docs/backup-custodia.md): la passphrase se pide por terminal (read -s, dos veces) o se lee de un archivo modo 600 con
# --passphrase-file; JAMAS por argv/variable de entorno ni se imprime. La salida NO puede estar dentro de BACKUP_DIR ni en el mismo directorio
# que el archivo de passphrase (C09). Cifrado: AES-256-CBC + PBKDF2 (600000 iteraciones); CBC no autentica, por eso se escribe SHA-256 que debe
# custodiarse APARTE del paquete. Custodia A (passphrase) y B (paquete) deben ser distintas. Contenido: .env (COMPOSE_ENV_FILES), backup-offsite.env
# si existe y MANIFEST.txt (nombres, fecha, commit; sin valores).
# Codigos: 0 ok | 1 uso/configuracion | 3 integridad (sha256/descifrado/contrasena) | 8 falta openssl/tar
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
    -h|--help) sed -n '2,19p' "$0"; exit 0 ;;
    -*) echo "argumento desconocido: $1" >&2; exit $EX_USAGE ;;
    *) PKG="$1"; shift ;;
  esac
done
command -v openssl >/dev/null 2>&1 && command -v tar >/dev/null 2>&1 || { echo "falta openssl o tar" >&2; exit $EX_PREREQ; }
load_config
LOG_FILE=""
ITER=600000
umask 077

abspath() { readlink -f "$1" 2>/dev/null || echo "$1"; }
read_pass() { # -> deja la passphrase en el descriptor de un archivo temporal 0600 (tmpfs si hay); nunca en argv/entorno
  PASS_TMP=$(mktemp "${XDG_RUNTIME_DIR:-/dev/shm}/bury-rp.XXXXXX" 2>/dev/null || mktemp)
  trap 'rm -f "$PASS_TMP"' EXIT
  if [[ -n "$PASSFILE" ]]; then
    [[ -f "$PASSFILE" ]] || { echo "no existe el archivo de passphrase" >&2; exit $EX_USAGE; }
    local m; m=$(stat -c %a "$PASSFILE" 2>/dev/null || echo 600)
    [[ "$m" =~ ^[0-7]00$ ]] || { echo "el archivo de passphrase debe tener modo 600 (tiene $m)" >&2; exit $EX_USAGE; }
    [[ -s "$PASSFILE" ]] || { echo "el archivo de passphrase esta vacio" >&2; exit $EX_USAGE; }
    head -n1 "$PASSFILE" | tr -d '\r\n' >"$PASS_TMP"
  else
    [[ -t 0 ]] || { echo "sin terminal: usar --passphrase-file" >&2; exit $EX_USAGE; }
    local p1 p2
    read -rsp "Passphrase del paquete: " p1; echo >&2
    if [[ "$MODE" == create ]]; then read -rsp "Repetir passphrase: " p2; echo >&2; [[ "$p1" == "$p2" ]] || { echo "no coinciden" >&2; exit $EX_USAGE; }; fi
    printf '%s' "$p1" >"$PASS_TMP"; p1="" p2=""
  fi
  (( $(wc -c <"$PASS_TMP") >= 12 )) || { echo "passphrase demasiado corta (minimo 12 caracteres)" >&2; exit $EX_USAGE; }
}
enc() { openssl enc -aes-256-cbc -pbkdf2 -iter $ITER -salt -pass "file:$PASS_TMP" "$@"; }

case "$MODE" in
  create)
    [[ -n "$OUT" ]] || { echo "falta --out DIR" >&2; exit $EX_USAGE; }
    mkdir -p "$OUT" && chmod 700 "$OUT" 2>/dev/null
    OUT=$(abspath "$OUT"); BD=$(abspath "${BACKUP_DIR:-/nonexistent}")
    [[ "$OUT" == "$BD" || "$OUT" == "$BD"/* ]] && { echo "--out no puede estar dentro de BACKUP_DIR (la clave de los respaldos no vive junto a ellos)" >&2; exit $EX_USAGE; }
    [[ -n "$PASSFILE" && "$(dirname "$(abspath "$PASSFILE")")" == "$OUT" ]] && { echo "el archivo de passphrase no puede estar en el mismo directorio que el paquete" >&2; exit $EX_USAGE; }
    read_pass
    STAGE=$(mktemp -d "${XDG_RUNTIME_DIR:-/dev/shm}/bury-rp-stage.XXXXXX" 2>/dev/null || mktemp -d); trap 'rm -rf "$STAGE"; rm -f "$PASS_TMP"' EXIT
    files=()
    while IFS= read -r f; do [[ -f "$f" ]] && { cp "$f" "$STAGE/$(basename "$f")"; files+=("$(basename "$f")"); }; done < <(_envfiles)
    OFF="${BURY_OFFSITE_ENV_FILE:-$REPO_DIR/backup-offsite.env}"
    [[ -f "$OFF" ]] && { cp "$OFF" "$STAGE/backup-offsite.env"; files+=("backup-offsite.env"); }
    (( ${#files[@]} > 0 )) || { echo "no hay .env que empaquetar" >&2; exit $EX_USAGE; }
    { echo "bury-recovery-package v1"; echo "created=$(date -u +%Y-%m-%dT%H:%M:%SZ)"; echo "commit=$(git -C "$REPO_DIR" rev-parse --short HEAD 2>/dev/null || echo desconocido)"; printf 'file=%s\n' "${files[@]}"; } >"$STAGE/MANIFEST.txt"
    NAME="bury-recovery_$(date -u +%Y%m%d-%H%M%SZ).enc"
    tar -C "$STAGE" -czf - MANIFEST.txt "${files[@]}" | enc -e -out "$OUT/$NAME" || { rm -f "$OUT/$NAME"; echo "fallo el cifrado" >&2; exit $EX_PKG; }
    chmod 600 "$OUT/$NAME"
    ( cd "$OUT" && sha256sum "$NAME" >"$NAME.sha256" && chmod 600 "$NAME.sha256" )
    echo "paquete creado: $OUT/$NAME ($(wc -c <"$OUT/$NAME") bytes; contiene $((${#files[@]})) archivo(s) de configuracion + MANIFEST)"
    echo "SIGUIENTE: guardar el .enc (custodia B) y la passphrase (custodia A) en lugares DISTINTOS y fuera del servidor; el .sha256 aparte del .enc."
    ;;
  verify|extract)
    [[ -n "$PKG" && -f "$PKG" ]] || { echo "falta el paquete .enc" >&2; exit $EX_USAGE; }
    SUM="$PKG.sha256"
    if [[ -f "$SUM" ]]; then
      ( cd "$(dirname "$PKG")" && sha256sum -c "$(basename "$SUM")" >/dev/null 2>&1 ) || { echo "INTEGRIDAD: el SHA-256 no coincide (paquete corrupto o alterado)" >&2; exit $EX_PKG; }
      echo "sha256 OK"
    else
      echo "AVISO: sin .sha256 junto al paquete (CBC no autentica): verificar su integridad por otro medio" >&2
    fi
    read_pass
    if [[ "$MODE" == verify ]]; then
      names=$(enc -d -in "$PKG" 2>/dev/null | tar tzf - 2>/dev/null) || { echo "no se pudo descifrar (passphrase incorrecta o paquete danado)" >&2; exit $EX_PKG; }
      grep -qx MANIFEST.txt <<<"$names" || { echo "el paquete no tiene MANIFEST.txt" >&2; exit $EX_PKG; }
      echo "paquete valido; contenido (solo nombres):"; sed 's/^/  - /' <<<"$names"
    else
      [[ -n "$TO" ]] || { echo "falta --to DIR" >&2; exit $EX_USAGE; }
      [[ ! -e "$TO" ]] || { echo "--to debe ser un directorio NUEVO (no se sobrescribe nada)" >&2; exit $EX_USAGE; }
      mkdir -p "$TO" && chmod 700 "$TO"
      enc -d -in "$PKG" 2>/dev/null | tar xzf - -C "$TO" 2>/dev/null || { rm -rf "$TO"; echo "no se pudo descifrar (passphrase incorrecta o paquete danado)" >&2; exit $EX_PKG; }
      chmod 600 "$TO"/* 2>/dev/null
      echo "restaurado en $TO (modo 700). Copiar los .env a su lugar, y BORRAR este directorio al terminar."
    fi
    ;;
  *) sed -n '2,19p' "$0"; exit $EX_USAGE ;;
esac
