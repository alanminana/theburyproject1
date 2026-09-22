#!/bin/bash
# db-init (one-shot, credencial ADMINISTRATIVA): prepara la base y el login/usuario dedicado del ERP. Idempotente.
# Es el UNICO servicio (junto con `db`) que conoce MSSQL_SA_PASSWORD. `app` solo recibe ERP_DB_USER / ERP_DB_PASSWORD (sin DDL)
# y `migrate` solo ERP_MIGRATION_USER / ERP_MIGRATION_PASSWORD (con DDL, sin sysadmin).
# Nunca imprime passwords. La password de sa viaja por SQLCMDPASSWORD (no aparece en la lista de procesos).
set -euo pipefail

: "${MSSQL_SA_PASSWORD:?falta MSSQL_SA_PASSWORD}"
: "${MSSQL_DATABASE:?falta MSSQL_DATABASE}"
: "${ERP_DB_USER:?falta ERP_DB_USER}"
: "${ERP_DB_PASSWORD:?falta ERP_DB_PASSWORD}"
: "${ERP_MIGRATION_USER:?falta ERP_MIGRATION_USER}"
: "${ERP_MIGRATION_PASSWORD:?falta ERP_MIGRATION_PASSWORD}"

# Compose rechaza ausentes/vacios; tambien rechazar los marcadores de la plantilla.
for key in MSSQL_SA_PASSWORD ERP_DB_USER ERP_DB_PASSWORD ERP_MIGRATION_USER ERP_MIGRATION_PASSWORD; do
  value=${!key}
  if [[ "${value^^}" == *CHANGE_ME* ]]; then
    echo "ERROR: $key conserva un valor provisional." >&2; exit 1
  fi
done

# Los identificadores se incrustan entre corchetes en el SQL: se restringen a un formato seguro.
# (Las passwords NO tienen restriccion de caracteres: se escapan abajo.)
ident='^[A-Za-z_][A-Za-z0-9_]{0,127}$'
[[ "$MSSQL_DATABASE" =~ $ident ]] || { echo "ERROR: MSSQL_DATABASE invalido (solo letras, digitos y _)." >&2; exit 1; }
[[ "$ERP_DB_USER" =~ $ident ]]    || { echo "ERROR: ERP_DB_USER invalido (solo letras, digitos y _)." >&2; exit 1; }
[[ "$ERP_MIGRATION_USER" =~ $ident ]] || { echo "ERROR: ERP_MIGRATION_USER invalido (solo letras, digitos y _)." >&2; exit 1; }
if [[ "${ERP_DB_USER,,}" == "sa" || "${ERP_MIGRATION_USER,,}" == "sa" ]]; then
  echo "ERROR: ERP_DB_USER y ERP_MIGRATION_USER no pueden ser 'sa'." >&2; exit 1
fi
if [[ "${ERP_DB_USER,,}" == "${ERP_MIGRATION_USER,,}" ]]; then
  echo "ERROR: ERP_DB_USER y ERP_MIGRATION_USER deben ser usuarios distintos (la app no puede tener DDL)." >&2; exit 1
fi

# Password como literal T-SQL: se duplican las comillas simples. sqlcmd sustituye $(VAR) desde el entorno.
export SQLCMDPASSWORD="$MSSQL_SA_PASSWORD"
export ERP_PWD_SQL="${ERP_DB_PASSWORD//\'/\'\'}"
export MIG_PWD_SQL="${ERP_MIGRATION_PASSWORD//\'/\'\'}"
export ERP_LOGIN="$ERP_DB_USER" MIG_LOGIN="$ERP_MIGRATION_USER" ERP_DBNAME="$MSSQL_DATABASE"
SQLCMD=(/opt/mssql-tools18/bin/sqlcmd -C -b -S db -U sa)

# El healthcheck de `db` consulta `master`: puede dar "healthy" mientras la base del ERP aun arranca / hace crash recovery
# (tras cualquier arranque de SQL con datos existentes: error 904 "cannot be autostarted"). Espera acotada a que la base sea ACCESIBLE
# (si aun no existe, es el primer arranque y no hay nada que esperar); si no lo logra, falla (exit != 0) y `migrate`/`app` no arrancan.
echo "[db-init] Esperando a que '$MSSQL_DATABASE' este accesible..."
for attempt in $(seq 1 45); do
  "${SQLCMD[@]}" -Q "IF DB_ID(N'\$(ERP_DBNAME)') IS NOT NULL EXEC(N'SELECT 1 FROM [\$(ERP_DBNAME)].sys.objects WHERE 1 = 0')" >/dev/null 2>&1 && break
  if [[ "$attempt" == 45 ]]; then echo "ERROR: '$MSSQL_DATABASE' no quedo accesible a tiempo (90 s)." >&2; exit 1; fi
  sleep 2
done

echo "[db-init] Base '$MSSQL_DATABASE' y logins '$ERP_DB_USER' (app) / '$ERP_MIGRATION_USER' (migrate)..."
"${SQLCMD[@]}" -i /init/01-server.sql

echo "[db-init] Usuario y permisos dentro de '$MSSQL_DATABASE'..."
"${SQLCMD[@]}" -d "$MSSQL_DATABASE" -i /init/02-database.sql

echo "[db-init] OK"
