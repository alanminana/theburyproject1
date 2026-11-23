#!/usr/bin/env bash
# Tests focalizados de los verificadores SQL de la puesta en marcha (scripts/sql/p02-verificar-instalacion.sql,
# p07-verificar-cuentas.sql, p07-verificar-credito-cerrado.sql). Sin docker por defecto (comprobaciones estáticas, corren en CI):
#   bash scripts/install/test-verificadores-sql.sh
# Modo vivo opcional (base DESCARTABLE ya migrada; todo dentro de transacciones que se revierten):
#   P07_DB_CONTAINER=<contenedor> P07_SA_PASSWORD=<pwd> [P07_DB_NAME=TheBuryProjectDb] bash scripts/install/test-verificadores-sql.sh
set -uo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"; SQLD="$HERE/../sql"
PASS=0 FAIL=0
ok()  { PASS=$((PASS+1)); echo "PASS $1"; }
bad() { FAIL=$((FAIL+1)); echo "FAIL $1"; }
check() { local n=$1; shift; if "$@"; then ok "$n"; else bad "$n"; fi; }

VERIF=(p02-verificar-instalacion.sql p07-verificar-cuentas.sql p07-verificar-credito-cerrado.sql)
# sqlcmd -b solo devuelve código != 0 si el script emite un error: toda FALLA debe terminar en RAISERROR severidad 16.
for f in "${VERIF[@]}"; do
  check "$f: una FALLA termina con RAISERROR (sqlcmd -b => exit != 0)" grep -Eq "IF @fallas > 0 RAISERROR\(.*, 16, 1, @fallas\)" "$SQLD/$f"
  check "$f: sin FALLA informa «sin FALLA»" grep -q "PRINT N'RESULTADO: sin FALLA.'" "$SQLD/$f"
  # Solo lectura: ninguna sentencia que escriba fuera de las tablas temporales #c/#r/#u (comentarios y SQL dinámico incluidos en la búsqueda)
  check "$f: solo escribe en tablas temporales (#...)" bash -c '
    ! sed "s/--.*$//" "$1" | grep -Eiv "^\s*$" | grep -Ei "\b(update|delete|truncate|merge|drop\s+(table|index|database)|alter\s+(table|database)|create\s+(index|database|login|user)|backup\s+database|sp_executesql)\b" | grep -Ev "DROP TABLE #[a-z]+" &&
    ! sed "s/--.*$//" "$1" | grep -Ei "insert\s+(into\s+)?[^#[:space:]]" ' _ "$SQLD/$f"
done

if [[ -n "${P07_DB_CONTAINER:-}" && -n "${P07_SA_PASSWORD:-}" ]]; then
  DB="${P07_DB_NAME:-TheBuryProjectDb}"; T=$(mktemp -d); trap 'rm -rf "$T"' EXIT
  run() {  # run <bloque SQL previo> <verificador>  -> imprime la salida; devuelve el exit de sqlcmd
    { echo "SET NOCOUNT ON; BEGIN TRAN;"; echo "$1"; echo "GO"; echo ":r /tmp/chk.sql"; echo "GO"; echo "ROLLBACK;"; } > "$T/drv.sql"
    docker cp "$SQLD/$2" "$P07_DB_CONTAINER:/tmp/chk.sql" >/dev/null; docker cp "$T/drv.sql" "$P07_DB_CONTAINER:/tmp/drv.sql" >/dev/null
    docker exec "$P07_DB_CONTAINER" /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$P07_SA_PASSWORD" -C -d "$DB" -I -b -W -s'|' -i /tmp/drv.sql 2>&1
  }
  # inserta una fila en $1 con valores neutros por tipo (FKs sin chequear dentro de la transacción) y $2 = "Col=valor;Col=valor" (literales T-SQL)
  ins() {
    local cases="" incl="N'IsDeleted'" kv
    IFS=';' read -ra KV <<< "$2"
    for kv in "${KV[@]}"; do cases+=" WHEN N'${kv%%=*}' THEN '${kv#*=}'"; incl+=",N'${kv%%=*}'"; done
    cat <<SQL
DECLARE @cols nvarchar(max), @vals nvarchar(max);
SELECT @cols = STRING_AGG(QUOTENAME(c.name), ','), @vals = STRING_AGG(CASE c.name $cases
  ELSE CASE WHEN t.name IN ('bit','int','bigint','smallint','tinyint','decimal','numeric','money','float') THEN '0'
       WHEN t.name IN ('datetime','datetime2','date','datetimeoffset','smalldatetime') THEN '''2026-01-01'''
       WHEN t.name = 'uniqueidentifier' THEN 'NEWID()' ELSE '''x''' END END, ',')
FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
WHERE c.object_id=OBJECT_ID(N'$1') AND c.is_identity=0 AND c.is_computed=0 AND t.name NOT IN ('rowversion','timestamp') AND (c.is_nullable=0 OR c.name IN ($incl));
EXEC(N'ALTER TABLE $1 NOCHECK CONSTRAINT ALL');
EXEC(N'INSERT INTO $1 (' + @cols + N') VALUES (' + @vals + N')');
SQL
  }
  expect() {  # expect <nombre> <exit esperado> <patrón esperado en la salida> <bloque previo> <verificador>
    local out rc; out="$(run "$4" "$5")"; rc=$?
    if [[ $rc -eq $2 ]] && grep -Eq "$3" <<< "$out"; then ok "vivo: $1"; else bad "vivo: $1 (exit $rc)"; echo "$out" | grep -E "FALLA|Msg" | head -5; fi
  }
  C=p07-verificar-credito-cerrado.sql
  expect "crédito: base con cupos en 0 => sin FALLA (exit 0)" 0 "sin FALLA" "UPDATE PuntajeCreditoLimites SET LimiteMonto = 0;" $C
  expect "crédito: cupo > 0 activo => FALLA (exit != 0)" 1 "Cupos por puntaje con monto > 0.*FALLA" "UPDATE PuntajeCreditoLimites SET LimiteMonto = 1 WHERE Puntaje = 0;" $C
  expect "crédito: cupo > 0 en fila inactiva no da cupo" 0 "sin FALLA" "UPDATE PuntajeCreditoLimites SET LimiteMonto = 0; UPDATE PuntajeCreditoLimites SET LimiteMonto = 5, Activo = 0 WHERE Puntaje = 3;" $C
  expect "crédito: límite manual de cliente => FALLA" 1 "Clientes con límite manual.*FALLA" "UPDATE PuntajeCreditoLimites SET LimiteMonto = 0;$(ins Clientes 'LimiteCredito=50000')" $C
  expect "crédito: override de cupo => FALLA" 1 "override.*FALLA" "UPDATE PuntajeCreditoLimites SET LimiteMonto = 0;$(ins ClientesCreditoConfiguraciones 'LimiteOverride=1000')" $C
  expect "crédito: crédito preexistente => FALLA" 1 "Créditos existentes.*FALLA" "UPDATE PuntajeCreditoLimites SET LimiteMonto = 0;$(ins Creditos 'IsDeleted=0')" $C
  expect "crédito: crédito eliminado (borrado lógico) no cuenta" 0 "sin FALLA" "UPDATE PuntajeCreditoLimites SET LimiteMonto = 0;$(ins Creditos 'IsDeleted=1')" $C
  expect "cuentas: cuenta sintética activa => FALLA (exit != 0)" 1 "sintéticas/demo.*FALLA" "UPDATE AspNetUsers SET Email = N'x@example.invalid';" p07-verificar-cuentas.sql
  expect "instalación: venta con versión fiscal inválida => exit != 0" 1 "Ventas con versión fiscal.*FALLA" "$(ins Ventas 'IsDeleted=0')" p02-verificar-instalacion.sql
fi

echo; echo "verificadores SQL: $PASS PASS, $FAIL FAIL"; [[ $FAIL -eq 0 ]]
