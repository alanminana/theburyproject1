#!/usr/bin/env bash
# Deploy de una imagen ya construida (build once, deploy same image). Gates obligatorios en orden:
# preflight -> backup (si corresponde) -> db-init -> migrate -> app -> health -> smoke.
# Cualquier gate que falla detiene el avance; la imagen anterior NO se toca (sigue corriendo hasta que este script
# reemplace `app`, y solo despues de que migrate haya salido 0).
#
#   scripts/deploy/deploy.sh --image theburyproject/erp:<release-id> [--env-file .env] [--project theburyproject]
#                            [--backup auto|skip] [--health-timeout 180] [--skip-smoke]
#
# No hace: docker compose down -v, system/volume prune, push/pull de registry, ssh, eliminar la imagen anterior.
# Exit codes: ver scripts/deploy/lib.sh (EX_*).
# shellcheck source=lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

IMAGE=""
BACKUP_MODE="auto"
HEALTH_TIMEOUT=180
SKIP_SMOKE=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    --image) IMAGE="$2"; shift 2 ;;
    --env-file) DP_ENV_FILE="$2"; shift 2 ;;
    --project) DP_PROJECT="$2"; shift 2 ;;
    --backup) BACKUP_MODE="$2"; shift 2 ;;
    --health-timeout) HEALTH_TIMEOUT="$2"; shift 2 ;;
    --skip-smoke) SKIP_SMOKE=1; shift ;;
    -h|--help) sed -n '2,10p' "$0"; exit 0 ;;
    *) die $EX_USAGE "argumento desconocido: $1 (ver --help)" ;;
  esac
done
[[ -n "$IMAGE" ]] || die $EX_USAGE "--image es obligatorio"
case "$BACKUP_MODE" in auto|skip) ;; *) die $EX_USAGE "--backup debe ser auto|skip" ;; esac

DP_ENV_FILE="$(cd "$(dirname "$DP_ENV_FILE")" && pwd)/$(basename "$DP_ENV_FILE")" 2>/dev/null || true
ensure_state_dir
trap 'trap_interrupt' INT TERM

REQUIRE_DB_UP=0
if dc ps --status running --services 2>/dev/null | grep -qx db; then REQUIRE_DB_UP=1; fi

log INFO "=== deploy: proyecto=$DP_PROJECT imagen=$IMAGE ==="
bash "$DP_SCRIPT_DIR/preflight.sh" --image "$IMAGE" --env-file "$DP_ENV_FILE" --project "$DP_PROJECT" \
  $([[ $REQUIRE_DB_UP == 1 ]] && echo --require-db-up) || die $EX_USAGE "preflight fallo, deploy no iniciado"

acquire_lock || exit $EX_BUSY
cleanup() { release_lock; }
trap cleanup EXIT

# --- identidad de la imagen (para el release record) ---
IDENTITY=$(image_identity "$IMAGE")
log INFO "identidad de imagen:"; while IFS= read -r l; do log INFO "  $l"; done <<<"$IDENTITY"

PREVIOUS_IMAGE=""
if [[ -f "$DP_CURRENT_FILE" ]]; then PREVIOUS_IMAGE=$(cat "$DP_CURRENT_FILE"); fi
if [[ -z "$PREVIOUS_IMAGE" ]]; then
  PREVIOUS_IMAGE=$(dc ps --format '{{.Image}}' app 2>/dev/null | head -n1 || true)
fi
log INFO "release anterior conocida: ${PREVIOUS_IMAGE:-<ninguna, bootstrap>}"

export ERP_IMAGE="$IMAGE"

# --- 1) levantar db ---
log INFO "--- db ---"
dc up -d db || die $EX_PREREQ "no se pudo levantar db"
for i in $(seq 1 30); do
  st=$(dc ps --format '{{.Health}}' db 2>/dev/null | head -n1 || true)
  [[ "$st" == "healthy" ]] && break
  sleep 3
  if [[ "$i" == 30 ]]; then die $EX_PREREQ "db no llego a healthy en 90s"; fi
done
log INFO "db healthy"

# --- 2) db-init (idempotente) ---
log INFO "--- db-init ---"
dc up -d db-init || true
DBINIT_EXIT=$(wait_container_exit db-init 120) || true
if [[ "$DBINIT_EXIT" != "0" ]]; then
  log ERROR "db-init exit=$DBINIT_EXIT. logs:"; dc logs --no-color --tail 60 db-init | while IFS= read -r l; do log ERROR "  $l"; done
  die $EX_PREREQ "db-init fallo"
fi
log INFO "db-init OK (exit 0, idempotente)"

# --- 3) backup pre-migracion (si corresponde) ---
DB_NAME=$(grep -E '^MSSQL_DATABASE=' "$DP_ENV_FILE" | tail -n1 | cut -d= -f2-)
DB_NAME=${DB_NAME:-TheBuryProjectDb}
if [[ "$BACKUP_MODE" == auto ]]; then
  DB_HAS_TABLES=$(dc exec -T db bash -c "SQLCMDPASSWORD=\"\$MSSQL_SA_PASSWORD\" exec /opt/mssql-tools18/bin/sqlcmd -C -b -S localhost -U sa -h -1 -W -Q \"SET NOCOUNT ON; IF DB_ID(N'$DB_NAME') IS NULL SELECT 0 ELSE SELECT COUNT(*) FROM [$DB_NAME].sys.tables\"" 2>/dev/null | tr -d '\r' | tail -n1 || echo 0)
  if is_uint "$DB_HAS_TABLES" && (( DB_HAS_TABLES > 0 )); then
    log INFO "--- backup pre-migracion (DB con $DB_HAS_TABLES tablas existentes) ---"
    if [[ -x "$REPO_DIR/scripts/backup/backup.sh" ]]; then
      if COMPOSE_PROJECT_NAME="$DP_PROJECT" COMPOSE_ENV_FILES="$DP_ENV_FILE" "$REPO_DIR/scripts/backup/backup.sh" full; then
        log INFO "backup full OK y verificado (RESTORE VERIFYONLY + sha256, ver scripts/backup/backup.sh)"
      else
        die $EX_BACKUP "backup pre-migracion fallo: no hay evidencia de backup reciente/verificado, se aborta antes de migrar"
      fi
    else
      die $EX_BACKUP "scripts/backup/backup.sh no encontrado o no ejecutable: no se puede garantizar backup pre-migracion"
    fi
  else
    log INFO "DB nueva/vacia: sin datos que respaldar, se omite el backup pre-migracion"
  fi
else
  log WARN "--backup skip: se omite el backup pre-migracion (uso no recomendado fuera de QA)"
fi

# --- 4) migrate (gate obligatorio) ---
log INFO "--- migrate ---"
dc up -d migrate || true
MIGRATE_EXIT=$(wait_container_exit migrate 600) || true
MIGRATE_LOG=$(dc logs --no-color --tail 200 migrate 2>/dev/null || true)
if [[ "$MIGRATE_EXIT" != "0" ]]; then
  log ERROR "migrate exit=$MIGRATE_EXIT. logs:"; echo "$MIGRATE_LOG" | tail -60 | while IFS= read -r l; do log ERROR "  $l"; done
  append_release_record "resultado=FALLO" "etapa=migrate" "imagen=$IMAGE" "previa=${PREVIOUS_IMAGE:-ninguna}" "migrate_exit=$MIGRATE_EXIT"
  die $EX_MIGRATE "migrate fallo: la app nueva NO se declara exitosa, app anterior no se toca"
fi
if ! grep -q '\[migrate\] OK: .* 0 pendientes' <<<"$MIGRATE_LOG"; then
  log ERROR "migrate salio 0 pero no confirmo '0 pendientes' en su log (ver arriba)"
  append_release_record "resultado=FALLO" "etapa=migrate-pending" "imagen=$IMAGE" "previa=${PREVIOUS_IMAGE:-ninguna}"
  die $EX_MIGRATE "migrate no confirmo 0 migraciones pendientes"
fi
log INFO "migrate OK: $(grep '\[migrate\] OK' <<<"$MIGRATE_LOG" | tail -1)"

# --- 5) app ---
log INFO "--- app ---"
dc up -d app || die $EX_HEALTH "no se pudo levantar app"

# --- 6) health gate ---
log INFO "--- health gate (timeout ${HEALTH_TIMEOUT}s) ---"
if ! wait_health app /health/live "$HEALTH_TIMEOUT"; then
  log ERROR "/health/live no respondio 200 dentro de ${HEALTH_TIMEOUT}s"
  dc logs --no-color --tail 60 app | while IFS= read -r l; do log ERROR "  $l"; done
  append_release_record "resultado=FALLO" "etapa=health-live" "imagen=$IMAGE" "previa=${PREVIOUS_IMAGE:-ninguna}" "migrate_exit=0"
  die $EX_HEALTH "health gate (live) fallo: deploy FAILED. La DB pudo haber cambiado (migrate ya corrio); ver docs/deploy-rollback.md#fallo-despues-de-migrar antes de cualquier rollback de imagen"
fi
if ! wait_health app /health/ready "$HEALTH_TIMEOUT"; then
  log ERROR "/health/ready no respondio 200 dentro de ${HEALTH_TIMEOUT}s"
  dc logs --no-color --tail 60 app | while IFS= read -r l; do log ERROR "  $l"; done
  append_release_record "resultado=FALLO" "etapa=health-ready" "imagen=$IMAGE" "previa=${PREVIOUS_IMAGE:-ninguna}" "migrate_exit=0"
  die $EX_HEALTH "health gate (ready) fallo: deploy FAILED. La DB pudo haber cambiado (migrate ya corrio); ver docs/deploy-rollback.md#fallo-despues-de-migrar antes de cualquier rollback de imagen"
fi
log INFO "health gate OK (live y ready = 200)"

# --- 7) smoke minimo ---
if (( ! SKIP_SMOKE )); then
  log INFO "--- smoke ---"
  LOGIN_CODE=$(http_code app /Identity/Account/Login)
  ROOT_CODE=$(http_code app /)
  USERS_COUNT=$(dc exec -T db bash -c "SQLCMDPASSWORD=\"\$MSSQL_SA_PASSWORD\" exec /opt/mssql-tools18/bin/sqlcmd -C -b -S localhost -U sa -h -1 -W -Q \"SET NOCOUNT ON; SELECT COUNT(*) FROM [$DB_NAME].dbo.AspNetUsers\"" 2>/dev/null | tr -d '\r' | tail -1 || echo "")
  log INFO "smoke: /Account/Login=$LOGIN_CODE / /=$ROOT_CODE AspNetUsers=count:${USERS_COUNT:-?}"
  if [[ "$LOGIN_CODE" != "200" ]]; then
    append_release_record "resultado=FALLO" "etapa=smoke-login" "imagen=$IMAGE" "previa=${PREVIOUS_IMAGE:-ninguna}"
    die $EX_SMOKE "smoke fallo: /Identity/Account/Login devolvio $LOGIN_CODE (esperado 200)"
  fi
  if [[ -z "$USERS_COUNT" ]] || ! is_uint "$USERS_COUNT"; then
    append_release_record "resultado=FALLO" "etapa=smoke-sql" "imagen=$IMAGE" "previa=${PREVIOUS_IMAGE:-ninguna}"
    die $EX_SMOKE "smoke fallo: consulta SQL de humo (COUNT AspNetUsers) no devolvio un numero"
  fi
  log INFO "smoke OK"
else
  log WARN "--skip-smoke: smoke omitido (uso solo para reproducir escenarios de fallo aislados)"
fi

# --- 8) exito: registrar release ---
echo "$IMAGE" >"$DP_CURRENT_FILE"
if [[ -n "$PREVIOUS_IMAGE" && "$PREVIOUS_IMAGE" != "$IMAGE" ]]; then echo "$PREVIOUS_IMAGE" >"$DP_PREVIOUS_FILE"; fi
append_release_record "resultado=OK" "imagen=$IMAGE" "previa=${PREVIOUS_IMAGE:-ninguna}" "migrate_exit=0" "health=OK" "smoke=$([[ $SKIP_SMOKE == 1 ]] && echo omitido || echo OK)"
log INFO "=== deploy OK: $IMAGE (proyecto=$DP_PROJECT) ==="
exit $EX_OK
