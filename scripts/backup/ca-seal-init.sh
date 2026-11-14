#!/usr/bin/env bash
# Crea la CLAVE DE SELLADO de la clave privada de la CA de Caddy (BACKUP_CA_KEY=seal): dos secretos aleatorios de 256 bits en el formato que
# espera rclone crypt, en un archivo 0600 FUERA de BACKUP_DIR. NO sobrescribe nunca un archivo existente y NO imprime los secretos.
#
#   scripts/backup/ca-seal-init.sh --out /etc/bury/ca-seal.env
#
# DESPUES: (1) copiar ese archivo a una custodia independiente del servidor y de los respaldos (gestor de secretos / medio cifrado offline);
# sin el, los .caseal NO se pueden abrir y la CA solo se recupera rotandola. (2) definir BACKUP_CA_SEAL_ENV=<ruta> y BACKUP_CA_KEY=seal en .env.
# Es un modelo de custodia: elegirlo (y quien guarda la copia) es decision del propietario. Ver docs/backup-ca-key-custodia.md.
# shellcheck source=lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

OUT=""
while (( $# )); do
  case "$1" in
    --out) OUT="${2:-}"; shift 2 ;;
    -h|--help) sed -n '2,10p' "$0"; exit 0 ;;
    *) die $EX_USAGE "argumento desconocido: $1" ;;
  esac
done
load_config
[[ "$OUT" == /* ]] || die $EX_USAGE "--out debe ser una ruta absoluta"
[[ "$OUT" != "$BACKUP_DIR"/* ]] || die $EX_USAGE "--out no puede estar dentro de BACKUP_DIR ($BACKUP_DIR): la clave de sellado no debe viajar con los respaldos"
[[ -d "$(dirname "$OUT")" ]] || die $EX_USAGE "no existe el directorio $(dirname "$OUT")"
[[ ! -e "$OUT" && ! -L "$OUT" ]] || die $EX_USAGE "$OUT ya existe: no se sobrescribe (una clave de sellado perdida deja los .caseal ilegibles)"
command -v docker >/dev/null 2>&1 || die $EX_PREREQ "docker no esta disponible"

secret() { head -c 32 /dev/urandom | base64 | tr -d '\n=' | tr '+/' '-_'; }
# El secreto entra por stdin: no queda en argv ni en el historial.
obscure() { dc run --rm -T --no-deps --quiet-pull --entrypoint rclone ca-seal obscure - 2>/dev/null; }
p1=$(secret | obscure | tr -d '\r\n') || true
p2=$(secret | obscure | tr -d '\r\n') || true
[[ -n "$p1" && -n "$p2" && "$p1" != "$p2" ]] || die $EX_PREREQ "no se pudo generar la clave de sellado (rclone obscure fallo)"
umask 077
set -C
{ printf '# bury ca-seal v1 - generado %s. Clave de sellado de la clave privada de la CA de Caddy.\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  printf '# CUSTODIA: copia independiente fuera del servidor y de BACKUP_DIR. Si se pierde, los .caseal no se pueden abrir.\n'
  printf 'RCLONE_CONFIG_CASEAL_PASSWORD=%s\nRCLONE_CONFIG_CASEAL_PASSWORD2=%s\n' "$p1" "$p2"; } >"$OUT" || die $EX_USAGE "no se pudo crear $OUT (¿ya existe?)"
chmod 600 "$OUT"
log INFO "clave de sellado creada: $OUT (modo 600). Copiarla AHORA a una custodia independiente y definir BACKUP_CA_SEAL_ENV=$OUT y BACKUP_CA_KEY=seal"
