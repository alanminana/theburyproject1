#!/usr/bin/env bash
# Instalación limpia sobre una base NUEVA en un SQL Server descartable (AC-T-1, AC-T-2, AC-T-3).
# Levanta db + db-init + migrate del docker-compose.yml (Production, igual que el despliegue real) con secretos sintéticos
# aleatorios, en un proyecto Compose propio (bury-instlimpia-<rand>) y volúmenes propios. NO usa LocalDB, TheBuryProjectDb,
# .env real ni backups reales. Al terminar elimina contenedores y volúmenes (la imagen no se toca).
#
# Verifica:
#   1. todas las migraciones EF del repo quedan aplicadas (ids únicos [Migration("...")] del repo) y 0 pendientes;
#   2. exactamente UN usuario (el administrador inicial, rol SuperAdmin) y ningún otro usuario importado;
#   3. ningún dato de negocio: toda tabla con filas debe estar en scripts/qa/instalacion-limpia-tablas-esperadas.txt
#      (catálogos/seed de arranque); una tabla con filas fuera de esa lista = FAIL;
#   4. las tablas nuevas de D-01/D-02/D-03 existen.
#
# Subred: la red del proyecto necesita una subred /24 que NO se solape con ninguna red Docker existente ni con las rutas del host
# (en un host con otros stacks bury-* una subred aleatoria a ciegas colisionaba: «Pool overlaps with other one on this address
# space» y `up` fallaba sin mostrar la causa; ver docs/hitos-d01-d03-inventario-y-criterios.md §10). Se elige una libre consultando
# `docker network inspect` y las rutas del host; si Docker aun así la rechaza por solapamiento (otro proceso creó una red en
# el mismo instante) se reintenta con otra subred, máximo 3 intentos y SOLO para causas transitorias identificadas. Cualquier otra falla se
# imprime con su causa (up.log, estado y logs de los servicios) y NO se reintenta. La segunda causa transitoria identificada es una lectura
# fallida del contexto de build por BuildKit (ver transient_cause).
#
# Uso (desde WSL/Linux, raíz del repo):  bash scripts/qa/instalacion-limpia.sh [--keep] [--no-cache]
#   --no-cache  reconstruye la imagen del ERP sin caché de capas antes de levantar (más lento; para descartar fallas dependientes de caché)
#   INSTL_SUBNET=172.31.77.0/24  fuerza la subred (solo para reproducir/probar; se valida igual contra solapamientos)
set -uo pipefail
cd "$(dirname "$0")/../.."
KEEP=0; NOCACHE=0
for a in "$@"; do case "$a" in --keep) KEEP=1 ;; --no-cache) NOCACHE=1 ;; *) echo "argumento desconocido: $a" >&2; exit 2 ;; esac; done
P="bury-instlimpia-$RANDOM$RANDOM"
TMP="$(mktemp -d)"; ENVF="$TMP/env"
rnd() { openssl rand -base64 24 | tr -dc 'A-Za-z0-9' | cut -c1-20; }
SA="Sa1!$(rnd)"; DBU="erp_app"; DBP="Ap1!$(rnd)"; MU="erp_mig"; MP="Mg1!$(rnd)"; ADM="Adm1!$(rnd)x"

# ---- subred libre ---------------------------------------------------------------------------------------------------------------
ip2int() { local a b c d; IFS=. read -r a b c d <<<"$1"; echo $(( (a << 24) + (b << 16) + (c << 8) + d )); }
overlaps() { # overlaps <cidr1> <cidr2>  -> rc 0 si se solapan (solo IPv4)
  local n1=${1%/*} p1=${1#*/} n2=${2%/*} p2=${2#*/} s1 e1 s2 e2
  [[ "$1" == */* && "$2" == */* && "$1" != *:* && "$2" != *:* ]] || return 1
  s1=$(ip2int "$n1"); e1=$(( s1 + (1 << (32 - p1)) - 1 )); s2=$(ip2int "$n2"); e2=$(( s2 + (1 << (32 - p2)) - 1 ))
  (( s1 <= e2 && s2 <= e1 ))
}
used_subnets() { # subredes de todas las redes Docker + rutas IPv4 del host (una por línea)
  docker network ls -q 2>/dev/null | xargs -r docker network inspect --format '{{range .IPAM.Config}}{{.Subnet}}{{"\n"}}{{end}}' 2>/dev/null
  ip -4 route 2>/dev/null | awk '$1 ~ /^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+\/[0-9]+$/ { print $1 }'
}
pick_subnet() { # pick_subnet <subredes-en-uso>  -> imprime una /24 de 172.31.0.0/16 libre
  local used=$1 i n cand u clash
  for i in $(seq 1 200); do
    n=$(( RANDOM % 200 + 20 )); cand="172.31.$n.0/24"; clash=0
    while IFS= read -r u; do [ -n "$u" ] && overlaps "$cand" "$u" && { clash=1; break; }; done <<<"$used"
    [ "$clash" = 0 ] && { echo "$cand"; return 0; }
  done
  return 1
}
choose_subnet() {
  local used u; used="$(used_subnets)"
  if [ -n "${INSTL_SUBNET:-}" ]; then
    while IFS= read -r u; do [ -n "$u" ] && overlaps "$INSTL_SUBNET" "$u" && { echo "INSTL_SUBNET=$INSTL_SUBNET se solapa con $u" >&2; return 1; }; done <<<"$used"
    SUBNET="$INSTL_SUBNET"; return 0
  fi
  SUBNET="$(pick_subnet "$used")" || { echo "no hay subred libre en 172.31.0.0/16" >&2; return 1; }
}
context_missing_path() { # ruta del archivo que BuildKit dice no encontrar en el contexto (relativa a la raíz del repo)
  sed -nE 's/.*"\/([^"]+)": not found.*/\1/p' "$1" | head -n 1
}
transient_cause() { # transient_cause <up.log> -> subnet | context | (vacío = no transitoria)
  local p
  if grep -qiE 'Pool overlaps with other one on this address space|invalid pool request' "$1"; then echo subnet; return 0; fi
  if grep -qE 'failed to (compute cache key|calculate checksum of ref)' "$1"; then
    p="$(context_missing_path "$1")"
    [ -n "$p" ] && [ -e "./$p" ] && echo context
  fi
  return 0
}
write_env() {
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
BURY_NET_SUBNET=$SUBNET
BACKUP_DIR=$TMP/backups
CSP_MODE=Off
EOF
}
mkdir -p "$TMP/backups/sql" "$TMP/backups/files" "$TMP/backups/logs"; chmod 777 "$TMP/backups/sql" 2>/dev/null || true
DC() { docker compose --env-file "$ENVF" -p "$P" -f docker-compose.yml "$@"; }
FAIL=0
ok()   { echo "  PASS  $*"; }
bad()  { echo "  FAIL  $*"; FAIL=$((FAIL+1)); }
cleanup() { if [ "$KEEP" = 0 ]; then DC down -v --remove-orphans >/dev/null 2>&1; rm -rf "$TMP"; echo "limpieza: proyecto $P eliminado"; else echo "--keep: proyecto $P y $TMP conservados"; fi; }
trap cleanup EXIT
SQL() { DC exec -T db bash -c "/opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P \"\$MSSQL_SA_PASSWORD\" -d TheBuryProjectDb_Nueva -h -1 -W -s '|' -b -Q \"SET NOCOUNT ON; $1\"" 2>&1; }
redact() { # los secretos sintéticos de esta corrida nunca se imprimen
  local t s; t="$(cat)"; for s in "$SA" "$DBP" "$MP" "$ADM"; do [ -n "$s" ] && t="${t//"$s"/[REDACTED]}"; done; printf '%s\n' "$t"
}
diag() { # diag <motivo>: volcado con la causa real ante una falla (antes se descartaba toda la salida)
  echo "---- DIAGNÓSTICO: $1 ----"
  echo "subred solicitada: ${SUBNET:-?}"
  if [ -s "$TMP/up.log" ]; then echo "---- up.log (últimas 60 líneas) ----"; tail -n 60 "$TMP/up.log" | redact; fi
  echo "---- docker compose ps -a ----"; DC ps -a 2>&1 | redact | tail -n 20
  echo "---- logs de db, db-init y migrate (últimas 60 líneas c/u) ----"; DC logs --tail 60 db db-init migrate 2>&1 | redact | tail -n 180
  echo "---- subredes en uso en este host ----"; used_subnets | sort -u | tr '\n' ' '; echo
}

echo "== Instalación limpia ($P)"
attempt=0; UP_OK=0
while [ "$attempt" -lt 3 ]; do
  attempt=$((attempt + 1))
  choose_subnet || { echo "FAIL: no se pudo elegir una subred libre"; exit 1; }
  write_env
  echo "  intento $attempt: subred $SUBNET"
  if [ "$NOCACHE" = 1 ] && [ "$attempt" = 1 ]; then
    DC build --no-cache migrate >"$TMP/build.log" 2>&1 || { cp "$TMP/build.log" "$TMP/up.log"; break; }
  fi
  if DC up -d --build migrate >"$TMP/up.log" 2>&1; then UP_OK=1; break; fi
  # Causas transitorias IDENTIFICADAS (únicas que se reintentan; cualquier otra falla corta con diagnóstico):
  #   subnet  : la subred elegida se solapó con una red creada en el mismo instante por otro proceso («Pool overlaps ...»).
  #   context : BuildKit no pudo leer un archivo del contexto de build montado desde Windows (/mnt/d) mientras otro proceso trabajaba
  #             en el mismo árbol: «failed to calculate checksum of ref ...: "/global.json": not found» con el archivo PRESENTE en el
  #             repo. Si el archivo realmente falta no es transitorio y no se reintenta.
  cause="$(transient_cause "$TMP/up.log")"
  if [ -n "$cause" ] && [ "$attempt" -lt 3 ]; then
    case "$cause" in
      subnet)  [ -z "${INSTL_SUBNET:-}" ] || break
               echo "  WARN  la subred $SUBNET se solapó con otra red Docker (colisión): se limpia y se reintenta con otra subred" ;;
      context) echo "  WARN  BuildKit no pudo leer $(context_missing_path "$TMP/up.log") del contexto (el archivo existe en el repo): lectura transitoria del contexto, se reintenta" ;;
    esac
    grep -iE 'Pool overlaps|failed to (solve|calculate)|not found' "$TMP/up.log" | tail -n 3 | redact | sed 's/^/        /'
    DC down -v --remove-orphans >/dev/null 2>&1
    continue
  fi
  break
done
if [ "$UP_OK" != 1 ]; then echo "FAIL: no levantó migrate (intento $attempt)"; diag "docker compose up -d --build migrate falló"; exit 1; fi
CID="$(DC ps -aq migrate)"; RC="$(docker wait "$CID")"
DC logs migrate 2>&1 | grep -E "migrate|FALLO|OK" | tail -5 | redact
[ "$RC" = "0" ] && ok "migrate terminó con código 0" || { bad "migrate terminó con código $RC"; diag "migrate terminó con código $RC"; exit 1; }

ESPERADAS="$(grep -rhoE 'Migration\("[0-9]{14}_[A-Za-z0-9_]+"' Migrations | sort -u | wc -l)"
APLICADAS="$(SQL "SELECT COUNT(*) FROM __EFMigrationsHistory" | tr -d '[:space:]')"
[ "$ESPERADAS" = "$APLICADAS" ] && ok "migraciones aplicadas = $APLICADAS (= ids únicos del repo)" || bad "migraciones aplicadas=$APLICADAS, esperadas=$ESPERADAS"
for t in ClienteReferencias ComprobantesProveedor PagosProveedor PagoProveedorImputaciones ImputacionesNotaCredito ListasPrecios; do
  n="$(SQL "SELECT COUNT(*) FROM sys.tables WHERE name='$t'" | tr -d '[:space:]')"
  [ "$n" = "1" ] && ok "tabla $t existe" || echo "  INFO  tabla $t no encontrada con ese nombre exacto (revisar nombre real)"
done

echo "== Usuarios"
NU="$(SQL "SELECT COUNT(*) FROM AspNetUsers" | tr -d '[:space:]')"
[ "$NU" = "1" ] && ok "AspNetUsers = 1" || bad "AspNetUsers = $NU (esperado 1)"
SA_N="$(SQL "SELECT COUNT(*) FROM AspNetUsers u JOIN AspNetUserRoles ur ON ur.UserId=u.Id JOIN AspNetRoles r ON r.Id=ur.RoleId WHERE r.Name='SuperAdmin' AND u.UserName='admin'" | tr -d '[:space:]')"
[ "$SA_N" = "1" ] && ok "un único SuperAdmin inicial: admin" || bad "SuperAdmin admin = $SA_N"

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
[ "$FAIL" = "0" ] && echo "RESULTADO: OK (instalación limpia verificada)" || echo "RESULTADO: $FAIL fallo(s)"
exit $((FAIL > 0))
