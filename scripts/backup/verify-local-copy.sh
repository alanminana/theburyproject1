#!/usr/bin/env bash
# VERIFICA la copia secundaria de BACKUP_REMOVABLE_DIR (p. ej. el disco local cifrado de scripts/backup/setup-local-disk-copy.sh). Solo lectura.
#
#   scripts/backup/verify-local-copy.sh [--quiet]
#
# Comprueba y PUBLICA en monitor-status/removable-verify.json (el monitor lo alerta como CRITICAL de inmediato si falla):
#   V1 destino disponible (ruta definida, punto de montaje real, archivo-marca, escribible)    -> montaje
#   V2 si BACKUP_REMOVABLE_MAPPER esta definido: el montaje sale de /dev/mapper/<nombre>       -> cifrado activo (no el disco sin cifrar)
#   V3 sha256 de CADA .bak/.trn/.tar.gz del destino contra su .sha256 (y que cada dato tenga su .sha256)  -> integridad (bit-rot, truncado)
#   V4 el full mas nuevo del destino no es mas viejo que BACKUP_REMOVABLE_MAX_AGE_HOURS (default 48)      -> la copia sigue al dia
#   V5 espacio: libre >= BACKUP_REMOVABLE_MIN_FREE_GB, o >= 5 % del volumen si no hay limite; aviso a >= 85 % de uso      -> capacidad
# Nunca modifica el destino ni los backups. Lee dentro del contenedor backup-files (mismo camino que la copia), de modo que los permisos del
# destino no importan. Codigos: 0 todo OK | 1 uso/config | 13 algun control fallo (detalle en el log y en el estado del monitor)
# shellcheck source=lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
# shellcheck source=lib-capacity.sh
source "$BK_SCRIPT_DIR/lib-capacity.sh"
set +e

QUIET=0
while (( $# )); do
  case "$1" in
    --quiet) QUIET=1; shift ;;
    -h|--help) sed -n '2,15p' "$0"; exit 0 ;;
    *) echo "argumento desconocido: $1" >&2; exit $EX_USAGE ;;
  esac
done
EX_VERIFY=13
load_config
load_capacity_config
[[ -n "$BACKUP_REMOVABLE_DIR" ]] || die $EX_USAGE "BACKUP_REMOVABLE_DIR no esta definido"
[[ -d "$BACKUP_DIR" && -f "$BACKUP_DIR/.bury-backup-root" ]] || die $EX_USAGE "BACKUP_DIR=$BACKUP_DIR no es un directorio administrado por backup.sh"
LOG_FILE="$BACKUP_DIR/logs/backup.log"
MAPPER=$(cfg BACKUP_REMOVABLE_MAPPER "")
MAX_AGE_H=$(cfg BACKUP_REMOVABLE_MAX_AGE_HOURS 48)
[[ -z "$MAPPER" || "$MAPPER" =~ ^[A-Za-z0-9_.-]+$ ]] || die $EX_USAGE "BACKUP_REMOVABLE_MAPPER invalido"
is_uint "$MAX_AGE_H" && (( MAX_AGE_H >= 1 )) || die $EX_USAGE "BACKUP_REMOVABLE_MAX_AGE_HOURS debe ser un entero >= 1"

FAILS=0 MSGS=()
fail() { FAILS=$((FAILS+1)); MSGS+=("$1"); log ERROR "verificacion copia local: FALLO $1"; }
okm()  { (( QUIET )) || log INFO "verificacion copia local: OK $1"; }

finish() { # publica el estado para el monitor al salir (ok o fallo)
  local ec=$?
  if (( FAILS == 0 && ec == 0 )); then restore_test_status 0 true "copia local verificada: ${SUMMARY:-ok}" removable-verify
  else restore_test_status "${ec/#0/$EX_VERIFY}" false "${MSGS[*]:-fallo inesperado (exit $ec)}" removable-verify; fi
}
trap finish EXIT

# V1 montaje / marca
if removable_available; then okm "V1 destino montado y con marca ($BACKUP_REMOVABLE_DIR)"; else fail "V1 destino NO disponible: $REMOVABLE_WHY"; exit $EX_VERIFY; fi

# V2 cifrado activo
if [[ -n "$MAPPER" ]]; then
  src=$(findmnt -no SOURCE "$BACKUP_REMOVABLE_DIR" 2>/dev/null | head -n1)
  if [[ "$(readlink -f "$src" 2>/dev/null)" == "$(readlink -f "/dev/mapper/$MAPPER" 2>/dev/null)" && -e "/dev/mapper/$MAPPER" ]]; then okm "V2 montaje cifrado (/dev/mapper/$MAPPER)"
  else fail "V2 el montaje NO proviene de /dev/mapper/$MAPPER (origen='$src'): cifrado inactivo"; fi
fi

# V3/V4 integridad + antiguedad: dentro del contenedor, sin depender de permisos
VERIFY_SH='set -u
[ -f "$MARK" ] || { echo "ERR marca ausente" ; exit 2; }
cd "$ROOT" || { echo "ERR sin raiz"; exit 3; }
n=0; bad=0; nosha=0; newest=""
for f in sql/*.bak sql/*.trn files/*.tar.gz; do
  [ -f "$f" ] || continue
  case "$f" in *.partial) continue ;; esac
  n=$((n+1))
  if [ -f "$f.sha256" ]; then
    exp=$(cut -d" " -f1 "$f.sha256"); got=$(sha256sum "$f" | cut -d" " -f1)
    if [ -z "$exp" ] || [ "$exp" != "$got" ]; then bad=$((bad+1)); echo "BAD $f"; fi
  else nosha=$((nosha+1)); echo "NOSHA $f"; fi
  case "$f" in sql/*_full_*.bak) newest="$f" ;; esac
done
echo "COUNT $n"; echo "NEWEST ${newest:-}"; [ "$bad" = 0 ] && [ "$nosha" = 0 ]'
out=$(cap_run removable "$VERIFY_SH" 2>&1); rc=$?
if (( rc != 0 )) && grep -qE '^(BAD|NOSHA) ' <<<"$out"; then   # un archivo en pleno renombrado por una copia concurrente: se reintenta una vez
  sleep 20; out=$(cap_run removable "$VERIFY_SH" 2>&1); rc=$?
fi
count=$(sed -n 's/^COUNT //p' <<<"$out" | head -n1); count=${count:-0}
if (( rc == 0 )); then
  if (( count > 0 )); then okm "V3 $count archivo(s) con sha256 verificado"; else fail "V3 la copia no contiene ningun backup"; fi
else
  badl=$(grep -E '^(BAD|NOSHA) ' <<<"$out" | head -n5 | tr '\n' ';')
  if [[ -n "$badl" ]]; then fail "V3 integridad: $badl"; else fail "V3 no se pudo leer el destino: $(sanitize_text "$out" | tr '\n' ' ' | cut -c1-160)"; fi
fi
newest=$(sed -n 's/^NEWEST //p' <<<"$out" | head -n1)
if [[ "$newest" =~ _full_([0-9]{4})-([0-9]{2})-([0-9]{2})_([0-9]{2})([0-9]{2})([0-9]{2})Z\.bak$ ]]; then
  ts=$(date -u -d "${BASH_REMATCH[1]}-${BASH_REMATCH[2]}-${BASH_REMATCH[3]} ${BASH_REMATCH[4]}:${BASH_REMATCH[5]}:${BASH_REMATCH[6]}" +%s 2>/dev/null || echo 0)
  age_h=$(( ( $(date -u +%s) - ts ) / 3600 ))
  if (( ts > 0 && age_h <= MAX_AGE_H )); then okm "V4 full mas nuevo de hace ${age_h} h"; else fail "V4 el full mas nuevo de la copia tiene ${age_h} h (maximo ${MAX_AGE_H} h): la copia no esta al dia"; fi
else
  fail "V4 no hay ningun full en la copia"; age_h="?"
fi

# V5 capacidad
if df_line=$(df -PB1 "$BACKUP_REMOVABLE_DIR" 2>/dev/null | awk 'NR==2 {print $2, $3, $4}') && [[ -n "$df_line" ]]; then
  read -r tot used free <<<"$df_line"
  pct=$(( used * 100 / (used + free) ))
  if (( REM_MIN_FREE_BYTES > 0 )); then limit=$REM_MIN_FREE_BYTES; else limit=$(( tot / 20 )); fi
  if (( free >= limit )); then okm "V5 libre $((free/1048576)) MiB (minimo $((limit/1048576)) MiB), uso ${pct} %"; else fail "V5 espacio insuficiente: libre $((free/1048576)) MiB < minimo $((limit/1048576)) MiB (uso ${pct} %)"; fi
  (( pct >= 85 )) && log WARN "verificacion copia local: uso ${pct} % del volumen de la copia (>= 85 %)"
else
  fail "V5 no se pudo medir el espacio de $BACKUP_REMOVABLE_DIR"; pct="?"
fi
SUMMARY="${count} archivos sha256 OK, full de hace ${age_h:-?} h, uso ${pct:-?} %${MAPPER:+, cifrado $MAPPER}"
if (( FAILS )); then log ERROR "verificacion copia local: $FAILS control(es) fallaron"; exit $EX_VERIFY; fi
log INFO "verificacion copia local OK: $SUMMARY"
exit 0
