#!/usr/bin/env bash
# Rotacion de las passwords crypt (P1/P2) de la copia externa: MIGRA el contenido a un remoto crypt NUEVO y lo verifica.
# rclone crypt no re-cifra en sitio, asi que la rotacion es "copiar descifrando con la clave vieja y cifrando con la nueva".
#
#   scripts/backup/rotate-crypt.sh --from secure:erp --to secure2:erp [--verify-only]
#
# GARANTIAS (probadas en scripts/backup/test-key-rotation.sh):
#   * NUNCA destruye ni modifica el remoto de origen: solo lo LEE. Este script no usa sync/move/delete/purge/deletefile/cleanup;
#     el unico comando que escribe es `rclone copy --immutable` sobre el remoto NUEVO (agrega; jamas borra ni pisa lo que ya existe).
#   * Retirar el remoto viejo y las passwords viejas es una decision HUMANA posterior (docs/backup-custodia.md §8, pasos 6-7).
#   * Es idempotente y reanudable: si se interrumpe (corte, Ctrl-C, kill) se vuelve a correr y completa lo que falta.
#   * Un remoto nuevo con password equivocada o un origen ilegible aborta ANTES de copiar (exit 6) y no escribe nada util.
#   * Toma el lock de backups (no corre a la vez que backup.sh / restore-*.sh).
#   * Los passwords nunca se imprimen; los errores de rclone pasan por sanitize_text.
#
# --verify-only: no copia; solo compara descifrado vs descifrado (rclone check --one-way --download).
# Los dos remotos deben estar definidos en backup-offsite.env como `crypt` (RCLONE_CONFIG_<NOMBRE>_TYPE=crypt, ..._PASSWORD, ..._PASSWORD2).
# Codigos: 0 ok | 1 uso/config | 4 ocupado | 6 fallo de copia/verificacion | 8 prerequisito
# shellcheck source=lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

FROM="" TO="" VERIFY_ONLY=0
while (( $# )); do
  case "$1" in
    --from) FROM="${2:-}"; shift 2 ;;
    --to) TO="${2:-}"; shift 2 ;;
    --verify-only) VERIFY_ONLY=1; shift ;;
    -h|--help) sed -n '2,20p' "$0"; exit 0 ;;
    *) die $EX_USAGE "argumento desconocido: $1" ;;
  esac
done

re='^[A-Za-z0-9_-]+:[A-Za-z0-9_./-]+$'
[[ "$FROM" =~ $re && "$TO" =~ $re ]] || die $EX_USAGE "--from y --to deben tener la forma <remoto>:<ruta> (p. ej. secure:erp)"
[[ "$FROM" != *..* && "$TO" != *..* ]] || die $EX_USAGE "las rutas no pueden contener '..'"
FROM_NAME=${FROM%%:*}; TO_NAME=${TO%%:*}
[[ "${FROM_NAME,,}" != "${TO_NAME,,}" ]] || die $EX_USAGE "--from y --to usan el mismo remoto ($FROM_NAME): rotar exige un remoto NUEVO con otras passwords"

load_config
command -v docker >/dev/null 2>&1 || die $EX_PREREQ "docker no esta disponible"

# Ambos remotos deben ser crypt en backup-offsite.env (solo se lee la FORMA: nunca se imprime ningun valor).
OFFSITE_ENV="$REPO_DIR/backup-offsite.env"   # el MISMO archivo que monta docker-compose.yml en los contenedores rclone (no se admite otro)
[[ -f "$OFFSITE_ENV" ]] || die $EX_USAGE "no existe $OFFSITE_ENV (config de los remotos)"
envv() { local u=${1^^}; u=${u//-/_}; grep -E "^[[:space:]]*RCLONE_CONFIG_${u}_$2=" "$OFFSITE_ENV" | tail -n1 | cut -d= -f2- | tr -d '\r' || true; }
for n in "$FROM_NAME" "$TO_NAME"; do
  [[ "$(envv "$n" TYPE)" == crypt ]] || die $EX_USAGE "el remoto '$n' no esta definido como crypt en backup-offsite.env (los respaldos viajarian sin cifrar)"
done
[[ -n "$(envv "$TO_NAME" PASSWORD)" && -n "$(envv "$TO_NAME" PASSWORD2)" ]] || die $EX_USAGE "el remoto nuevo '$TO_NAME' debe tener PASSWORD y PASSWORD2 (las nuevas, ya guardadas en la custodia A)"

mkdir_new_mode 700 "$BACKUP_DIR" "$BACKUP_DIR/logs"
LOG_FILE="$BACKUP_DIR/logs/rotate-crypt.log"
lrc=0; acquire_lock || lrc=$?
(( lrc == 0 )) || { (( lrc == 1 )) && die $EX_BUSY "hay un backup o un restore en curso (lock): reintentar luego"; die $EX_PREREQ "no se pudo tomar el lock (permisos de BACKUP_DIR)"; }
trap release_lock EXIT

# Unico punto de contacto con rclone: lista blanca de subcomandos. Cualquier otro aborta (defensa en profundidad contra una edicion futura).
rc() { # rc <subcomando> args...
  case "$1" in lsf|copy|check) ;; *) die $EX_USAGE "subcomando rclone no permitido en una rotacion: $1" ;; esac
  dc run --rm -T --no-deps --quiet-pull backup-offsite "$@"
}

log INFO "=== rotate-crypt inicio: ${FROM_NAME}:* -> ${TO_NAME}:* (verify-only=$VERIFY_ONLY). El origen solo se LEE ==="

# 1) Origen legible con SUS passwords (si no, abortar antes de tocar nada).
if ! out=$(rc lsf "$FROM" --files-only 2>&1); then die $EX_OFFSITE "no se puede leer el remoto de origen $FROM_NAME (password de cifrado incorrecta, remoto inaccesible o ruta inexistente): $(rclone_errtext "$out")"; fi
SRC_N=$(rc lsf "$FROM" -R --files-only 2>/dev/null | tr -d '\r' | grep -c . || true)
(( SRC_N > 0 )) || die $EX_OFFSITE "el origen $FROM no tiene archivos descifrables: nada que migrar (¿password o ruta equivocada?)"
log INFO "origen legible: $SRC_N archivo(s)"

# 2) Destino alcanzable con las passwords NUEVAS (un destino con password equivocada a veces 'funciona' al escribir pero no al leer:
#    se prueba lectura/listado antes de copiar; un destino vacio es normal en la primera corrida).
if ! out=$(rc lsf "$TO" 2>&1); then
  if grep -qiE "directory not found|couldn't find|not found" <<<"$out"; then log INFO "destino $TO_NAME aun no existe: se creara al copiar"
  else die $EX_OFFSITE "no se puede acceder al remoto nuevo $TO_NAME (password nueva incorrecta o remoto inaccesible): $(rclone_errtext "$out")"; fi
fi

# 2b) Coherencia del destino: si el remoto subyacente ya tiene objetos, TODOS deben descifrarse con las passwords nuevas. Con una password
#     nueva equivocada, `copy` escribiria un segundo juego de objetos con otros nombres cifrados (basura indescifrable junto a lo bueno).
UNDER=$(envv "$TO_NAME" REMOTE)
if [[ "$UNDER" =~ ^[A-Za-z0-9_-]+:[A-Za-z0-9_./-]*$ ]]; then
  RAW_N=$(rc lsf "$UNDER" -R --files-only 2>/dev/null | grep -c . || true)
  DEC_N=$(rc lsf "$TO" -R --files-only 2>/dev/null | grep -c . || true)
  (( RAW_N == DEC_N )) || die $EX_OFFSITE "el destino $TO_NAME contiene $RAW_N objeto(s) de los que solo $DEC_N se descifran con las passwords nuevas (password nueva equivocada o destino ajeno): no se copia nada"
fi

# 3) Copia que solo AGREGA. --immutable: si un archivo ya existe en el destino con contenido distinto, FALLA en lugar de pisarlo.
if (( VERIFY_ONLY == 0 )); then
  if ! out=$(rc copy "$FROM" "$TO" --immutable --transfers 2 --checkers 4 2>&1 >/dev/null); then
    die $EX_OFFSITE "la migracion a $TO_NAME fallo (el origen NO se toco; reintentar: es reanudable): $(rclone_errtext "$out")"
  fi
  log INFO "copia completada (solo se agrego en $TO_NAME)"
fi

# 3b) Las passwords nuevas deben ser DISTINTAS de las viejas. Los valores `obscure` no son comparables (sal aleatoria), pero los nombres CIFRADOS de un
#     mismo archivo son identicos si las claves son identicas: si algun nombre crudo coincide entre ambos remotos subyacentes, "rotar" no roto nada.
UNDER_FROM=$(envv "$FROM_NAME" REMOTE)
if [[ "$UNDER_FROM" =~ ^[A-Za-z0-9_-]+:[A-Za-z0-9_./-]*$ && "$UNDER" =~ ^[A-Za-z0-9_-]+:[A-Za-z0-9_./-]*$ ]]; then
  rc lsf "$UNDER_FROM" -R --files-only 2>/dev/null | tr -d '\r' | sort >"$BACKUP_DIR/.rotate-raw-from.$$"
  rc lsf "$UNDER" -R --files-only 2>/dev/null | tr -d '\r' | sort >"$BACKUP_DIR/.rotate-raw-to.$$"
  SAME=$(comm -12 "$BACKUP_DIR/.rotate-raw-from.$$" "$BACKUP_DIR/.rotate-raw-to.$$" | grep -c . || true)
  rm -f "$BACKUP_DIR/.rotate-raw-from.$$" "$BACKUP_DIR/.rotate-raw-to.$$"
  (( SAME == 0 )) || die $EX_OFFSITE "$SAME nombre(s) cifrado(s) son IDENTICOS en ambos remotos: las passwords de $TO_NAME son equivalentes a las de $FROM_NAME (la clave NO roto). Generar passwords nuevas (docs/backup-custodia.md §6)"
else
  log WARN "no se pudo comprobar que las passwords nuevas difieran de las viejas (REMOTE de un remoto no es <remoto>:<ruta>)"
fi

# 4) Verificacion: cada archivo del origen existe en el destino y su contenido DESCIFRADO coincide byte a byte.
if ! out=$(rc check "$FROM" "$TO" --one-way --download 2>&1); then
  die $EX_OFFSITE "la verificacion origen->destino FALLO: $(rclone_errtext "$out")"
fi
DST_N=$(rc lsf "$TO" -R --files-only 2>/dev/null | tr -d '\r' | grep -c . || true)
(( DST_N >= SRC_N )) || die $EX_OFFSITE "el destino tiene $DST_N archivo(s) y el origen $SRC_N"
log INFO "verificado: $SRC_N archivo(s) del origen presentes e IDENTICOS (descifrados) en $TO_NAME ($DST_N en total)"
log INFO "=== rotate-crypt OK. El remoto viejo y sus passwords siguen intactos ==="
cat <<EOF
Siguientes pasos (MANUALES, ninguno lo hace este script):
  1. BACKUP_REMOTE=${TO} en .env; correr backup.sh all, restore-test.sh --offsite y check-custody.sh.
  2. Conservar ${FROM_NAME} y sus passwords en la custodia A durante >= 1 ciclo completo de retencion (>= 7 dias).
  3. Solo con aprobacion expresa del propietario: retirar los datos del remoto viejo y sus passwords.
  Rollback: volver a BACKUP_REMOTE=${FROM} (sigue integro).
EOF
