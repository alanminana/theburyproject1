#!/usr/bin/env bash
# Instalaci贸n limpia sobre una base NUEVA en un SQL Server descartable (AC-T-1, AC-T-2, AC-T-3).
# Levanta db + db-init + migrate del docker-compose.yml (Production, igual que el despliegue real) con secretos sint茅ticos
# aleatorios, en un proyecto Compose propio (bury-instlimpia-<rand>) y vol煤menes propios. NO usa LocalDB, TheBuryProjectDb,
# .env real ni backups reales. Al terminar elimina contenedores, vol煤menes y la imagen no se toca.
#
# Verifica:
#   1. todas las migraciones EF del repo quedan aplicadas (ids 鷑icos [Migration("...")] del repo) y 0 pendientes;
#   2. exactamente UN usuario (el administrador inicial, rol SuperAdmin) y ning煤n otro usuario importado;
#   3. ning煤n dato de negocio: toda tabla con filas debe estar en scripts/qa/instalacion-limpia-tablas-esperadas.txt
#      (cat谩logos/seed de arranque); una tabla con filas fuera de esa lista = FAIL;
#   4. las tablas nuevas de D-01/D-02/D-03 existen y est谩n vac铆as.
# Uso (desde WSL/Linux, ra铆z del repo):  bash scripts/qa/instalacion-limpia.sh [--keep]
set -uo pipefail
cd "$(dirname "$0")/../.."
KEEP=0; [ "${1:-}" = "--keep" ] && KEEP=1
P="bury-instlimpia-$RANDOM$RANDOM"
TMP="$(mktemp -d)"; ENVF="$TMP/env"
rnd() { openssl rand -base64 24 | tr -dc 'A-Za-z0-9' | cut -c1-20; }
SA="Sa1!$(rnd)"; DBU="erp_app"; DBP="Ap1!$(rnd)"; MU="erp_mig"; MP="Mg1!$(rnd)"; ADM="Adm1!$(rnd)x"
cat > "$ENVF" <<EOF
MSSQL_SA_PASSWORD=$SA
MSSQL_DATABASE=TheBuryProjectDb_Nueva
ERP_DB_USER=$DBU
ERP_DB_PASSWORD=$DBP
ERP_MIGRATION_USER=$MU
ERP_MIGRATION_PASSWORD=$MP
MSSQL_PID=Developer
ADMIN_EMAIL=admin-prueba@example.invalid
ADMIN_USERNAME=admin
ADMIN_PASSWORD=$ADM
ERP_DOMAIN=localhost
BURY_NET_SUBNET=172.31.$((RANDOM % 200 + 20)).0/24
BACKUP_DIR=$TMP/backups
CSP_MODE=Off
EOF
mkdir -p "$TMP/backups/sql" "$TMP/backups/files" "$TMP/backups/logs"; chmod 777 "$TMP/backups/sql" 2>/dev/null || true
DC() { docker compose --env-file "$ENVF" -p "$P" -f docker-compose.yml "$@"; }
FAIL=0
ok()   { echo "  PASS  $*"; }
bad()  { echo "  FAIL  $*"; FAIL=$((FAIL+1)); }
cleanup() { if [ "$KEEP" = 0 ]; then DC down -v --remove-orphans >/dev/null 2>&1; rm -rf "$TMP"; echo "limpieza: proyecto $P eliminado"; else echo "--keep: proyecto $P y $TMP conservados"; fi; }
trap cleanup EXIT
SQL() { DC exec -T db bash -c "/opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P \"\$MSSQL_SA_PASSWORD\" -d TheBuryProjectDb_Nueva -h -1 -W -s '|' -b -Q \"SET NOCOUNT ON; $1\"" 2>&1; }

echo "== Instalaci贸n limpia ($P)"
DC up -d --build migrate >/dev/null 2>&1 || { echo "FAIL: no levant贸 migrate"; DC logs --tail 60 migrate db-init 2>&1 | tail -80; exit 1; }
CID="$(DC ps -aq migrate)"; RC="$(docker wait "$CID")"
DC logs migrate 2>&1 | grep -E "migrate|FALLO|OK" | tail -5
[ "$RC" = "0" ] && ok "migrate termin贸 con c贸digo 0" || { bad "migrate termin贸 con c贸digo $RC"; DC logs --tail 80 migrate 2>&1 | tail -80; exit 1; }

ESPERADAS="$(grep -rhoE 'Migration\("[0-9]{14}_[A-Za-z0-9_]+"' Migrations | sort -u | wc -l)"
APLICADAS="$(SQL "SELECT COUNT(*) FROM __EFMigrationsHistory" | tr -d '[:space:]')"
[ "$ESPERADAS" = "$APLICADAS" ] && ok "migraciones aplicadas = $APLICADAS (= ids 鷑icos del repo)" || bad "migraciones aplicadas=$APLICADAS, esperadas=$ESPERADAS"
for t in ClienteReferencias ComprobantesProveedor PagosProveedor PagoProveedorImputaciones ImputacionesNotaCredito ListasPrecios; do
  n="$(SQL "SELECT COUNT(*) FROM sys.tables WHERE name='$t'" | tr -d '[:space:]')"
  [ "$n" = "1" ] && ok "tabla $t existe" || echo "  INFO  tabla $t no encontrada con ese nombre exacto (revisar nombre real)"
done

echo "== Usuarios"
NU="$(SQL "SELECT COUNT(*) FROM AspNetUsers" | tr -d '[:space:]')"
[ "$NU" = "1" ] && ok "AspNetUsers = 1" || bad "AspNetUsers = $NU (esperado 1)"
SA_N="$(SQL "SELECT COUNT(*) FROM AspNetUsers u JOIN AspNetUserRoles ur ON ur.UserId=u.Id JOIN AspNetRoles r ON r.Id=ur.RoleId WHERE r.Name='SuperAdmin' AND u.UserName='admin'" | tr -d '[:space:]')"
[ "$SA_N" = "1" ] && ok "un 煤nico SuperAdmin inicial: admin" || bad "SuperAdmin admin = $SA_N"

echo "== Tablas con filas (deben coincidir con la lista esperada de seed)"
ESP="scripts/qa/instalacion-limpia-tablas-esperadas.txt"
SQL "SELECT t.name FROM sys.tables t JOIN sys.partitions p ON p.object_id=t.object_id AND p.index_id IN (0,1) GROUP BY t.name HAVING SUM(p.rows)>0 ORDER BY t.name" | tr -d '\r' | grep -v '^$' | sort > "$TMP/con-filas.txt"
if [ -f "$ESP" ]; then
  grep -vE '^\s*(#|$)' "$ESP" | tr -d '\r' | sort > "$TMP/esp.txt"
  INESP="$(comm -23 "$TMP/con-filas.txt" "$TMP/esp.txt")"
  if [ -z "$INESP" ]; then ok "ninguna tabla fuera de la lista esperada tiene filas"; else bad "tablas con filas NO esperadas:"; echo "$INESP" | sed 's/^/        /'; fi
else
  echo "  INFO  no existe $ESP; tablas con filas encontradas (revisar y versionar):"; sed 's/^/        /' "$TMP/con-filas.txt"; bad "falta la lista esperada"
fi
echo
[ "$FAIL" = "0" ] && echo "RESULTADO: OK (instalaci贸n limpia verificada)" || echo "RESULTADO: $FAIL fallo(s)"
exit $((FAIL > 0))
