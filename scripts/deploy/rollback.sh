#!/usr/bin/env bash
# Rollback de APLICACION (imagen), no de base de datos. NUNCA reejecuta `migrate` hacia atras ni asume compatibilidad
# automatica. Sin --confirm-compatible el script se detiene en seco (exit 9) y solo explica que hacer.
#
#   scripts/deploy/rollback.sh --to-image theburyproject/erp:<release-id-anterior> --confirm-compatible \
#       [--env-file .env] [--project theburyproject] [--health-timeout 180]
#
# Requisito previo (responsabilidad del operador, ver docs/deploy-rollback.md#3-tipos-de-cambio-de-db):
#   el esquema de la release actual debe ser categoria A (backward-compatible) respecto de --to-image.
#   Si es categoria B o C: NO usar este script. Evaluar forward-fix / migracion correctiva / restore de backup.
#
# No hace: docker compose down -v, restore de base de datos, prune, eliminar la imagen reemplazada.
# Exit codes: ver scripts/deploy/lib.sh (EX_*).
# shellcheck source=lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

TO_IMAGE=""
CONFIRMED=0
HEALTH_TIMEOUT=180

while [[ $# -gt 0 ]]; do
  case "$1" in
    --to-image) TO_IMAGE="$2"; shift 2 ;;
    --env-file) DP_ENV_FILE="$2"; shift 2 ;;
    --project) DP_PROJECT="$2"; shift 2 ;;
    --confirm-compatible) CONFIRMED=1; shift ;;
    --health-timeout) HEALTH_TIMEOUT="$2"; shift 2 ;;
    -h|--help) sed -n '2,12p' "$0"; exit 0 ;;
    *) die $EX_USAGE "argumento desconocido: $1 (ver --help)" ;;
  esac
done
recompute_state_paths # --project pudo haber cambiado DP_PROJECT recien arriba; ver comentario en lib.sh
ensure_state_dir

if [[ -z "$TO_IMAGE" ]]; then
  [[ -f "$DP_PREVIOUS_FILE" ]] || die $EX_USAGE "--to-image no indicado y no hay previous-image registrada para '$DP_PROJECT'"
  TO_IMAGE=$(cat "$DP_PREVIOUS_FILE")
  log INFO "--to-image no indicado: se usa la previa registrada: $TO_IMAGE"
fi

CURRENT_IMAGE=""
[[ -f "$DP_CURRENT_FILE" ]] && CURRENT_IMAGE=$(cat "$DP_CURRENT_FILE")
[[ -z "$CURRENT_IMAGE" ]] && CURRENT_IMAGE=$(dc ps --format '{{.Image}}' app 2>/dev/null | head -n1 || true)

log INFO "=== rollback: proyecto=$DP_PROJECT actual=${CURRENT_IMAGE:-desconocida} -> destino=$TO_IMAGE ==="

if (( ! CONFIRMED )); then
  cat <<EOF >&2
STOP: rollback no ejecutado.

Este script no puede determinar por si mismo si el esquema de base de datos aplicado por
'${CURRENT_IMAGE:-la release actual}' es compatible con la imagen destino ($TO_IMAGE).

Antes de reintentar con --confirm-compatible, verificar en docs/deploy-rollback.md
(seccion "Tipos de cambio de DB"):
  A. backward-compatible  -> rollback de imagen es seguro. Reintentar con --confirm-compatible.
  B. compatibilidad limitada -> evaluar caso a caso antes de decidir.
  C. breaking/irreversible   -> NO hacer rollback de imagen. Ver forward-fix / restore de backup.

Este script JAMAS hace 'docker compose up' con la imagen anterior sin esta confirmacion explicita.
EOF
  exit $EX_ROLLBACK_REJECTED
fi

bash "$DP_SCRIPT_DIR/preflight.sh" --image "$TO_IMAGE" --env-file "$DP_ENV_FILE" --project "$DP_PROJECT" --require-db-up \
  || die $EX_USAGE "preflight fallo, rollback no iniciado"

acquire_lock || exit $EX_BUSY
cleanup() { release_lock; }
trap cleanup EXIT
trap 'trap_interrupt' INT TERM

export ERP_IMAGE="$TO_IMAGE"

log INFO "--- deteniendo app (imagen actual: ${CURRENT_IMAGE:-desconocida}) ---"
dc stop -t 30 app || true

log INFO "--- levantando app con imagen destino ($TO_IMAGE), sin re-ejecutar migrate ---"
dc up -d --no-deps app || die $EX_HEALTH "no se pudo levantar app con la imagen destino"

log INFO "--- health gate (timeout ${HEALTH_TIMEOUT}s) ---"
if ! wait_health app /health/live "$HEALTH_TIMEOUT" || ! wait_health app /health/ready "$HEALTH_TIMEOUT"; then
  log ERROR "health gate fallo tras el rollback a $TO_IMAGE"
  dc logs --no-color --tail 60 app | while IFS= read -r l; do log ERROR "  $l"; done
  append_release_record "resultado=FALLO" "etapa=rollback-health" "imagen=$TO_IMAGE" "reemplazada=${CURRENT_IMAGE:-desconocida}"
  die $EX_HEALTH "rollback fallo en el health gate. NO se reintenta automaticamente otra imagen; diagnosticar antes de continuar"
fi
log INFO "health gate OK"

LOGIN_CODE=$(http_code app /Identity/Account/Login)
if [[ "$LOGIN_CODE" != "200" ]]; then
  append_release_record "resultado=FALLO" "etapa=rollback-smoke" "imagen=$TO_IMAGE" "reemplazada=${CURRENT_IMAGE:-desconocida}"
  die $EX_SMOKE "rollback fallo en smoke: /Identity/Account/Login devolvio $LOGIN_CODE"
fi
log INFO "smoke OK"

echo "$TO_IMAGE" >"$DP_CURRENT_FILE"
[[ -n "$CURRENT_IMAGE" ]] && echo "$CURRENT_IMAGE" >"$DP_PREVIOUS_FILE"
append_release_record "resultado=OK" "etapa=rollback" "imagen=$TO_IMAGE" "reemplazada=${CURRENT_IMAGE:-desconocida}"
log INFO "=== rollback OK: $TO_IMAGE (proyecto=$DP_PROJECT) ==="
log WARN "recordatorio: esto es rollback de APLICACION. La base de datos NO fue tocada (no hubo restore ni downgrade de esquema)."
exit $EX_OK
