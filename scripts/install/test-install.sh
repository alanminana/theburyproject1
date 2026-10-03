#!/usr/bin/env bash
# Tests focalizados del instalador (scripts/install/install.sh). Sin docker ni root; todo en un temp dir.
#   bash scripts/install/test-install.sh        (exit 0 = todo OK)
# No imprime secretos generados: solo PASS/FAIL.
set -uo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
INSTALL="$HERE/install.sh"
T=$(mktemp -d); trap 'rm -rf "$T"' EXIT
PASS=0 FAIL=0
ok()   { PASS=$((PASS+1)); echo "PASS $1"; }
bad()  { FAIL=$((FAIL+1)); echo "FAIL $1"; }
check() { local name=$1; shift; if "$@"; then ok "$name"; else bad "$name"; fi; }
val() { grep -E "^$2=" "$1" | tail -n1 | cut -d= -f2-; }   # val ARCHIVO CLAVE

check "bash -n install.sh" bash -n "$INSTALL"
check "bash -n test-install.sh" bash -n "${BASH_SOURCE[0]}"

# --- generacion basica (community) ----------------------------------------------------------------------------------------
E="$T/a.env"
bash "$INSTALL" --env-only --env-file "$E" --domain tbp --admin-email soporte@example.com --backup-dir /srv/bury-test >/dev/null 2>&1; rc=$?
check "--env-only genera .env (rc=0)" test "$rc" = 0 -a -s "$E"
check "sin CHANGE_ME" bash -c '! grep -qi CHANGE_ME "$1"' _ "$E"
for k in MSSQL_SA_PASSWORD MSSQL_DATABASE ERP_DB_USER ERP_DB_PASSWORD ERP_MIGRATION_USER ERP_MIGRATION_PASSWORD ADMIN_EMAIL ERP_DOMAIN ADMIN_PASSWORD; do
  check "define $k" bash -c '[[ -n "$(grep -E "^$2=" "$1" | cut -d= -f2-)" ]]' _ "$E" "$k"
done
check "ERP_DOMAIN=tbp" test "$(val "$E" ERP_DOMAIN)" = tbp
check "ADMIN_EMAIL correcto" test "$(val "$E" ADMIN_EMAIL)" = soporte@example.com
check "MSSQL_PID=Express" test "$(val "$E" MSSQL_PID)" = Express
check "licencia community sin key" test "$(val "$E" AUTOMAPPER_LICENSE_MODE)" = community -a -z "$(val "$E" AUTOMAPPER_LICENSE_KEY)"
check "BACKUP_REQUIRE_OFFSITE=false" test "$(val "$E" BACKUP_REQUIRE_OFFSITE)" = false
check "BACKUP_DIR configurado" test "$(val "$E" BACKUP_DIR)" = /srv/bury-test
check "usuario de app != usuario de migracion" test "$(val "$E" ERP_DB_USER)" != "$(val "$E" ERP_MIGRATION_USER)"
check "las 3 passwords SQL son distintas entre si" bash -c '[[ "$1" != "$2" && "$2" != "$3" && "$1" != "$3" ]]' _ \
  "$(val "$E" MSSQL_SA_PASSWORD)" "$(val "$E" ERP_DB_PASSWORD)" "$(val "$E" ERP_MIGRATION_PASSWORD)"
strong() { [[ ${#1} -ge "$2" && "$1" =~ [A-Z] && "$1" =~ [a-z] && "$1" =~ [0-9] && "$1" =~ ^[A-Za-z0-9]+$ ]]; }
for k in MSSQL_SA_PASSWORD ERP_DB_PASSWORD ERP_MIGRATION_PASSWORD; do
  check "$k: >=28 alfanumericos con mayuscula, minuscula y digito" strong "$(val "$E" "$k")" 28
done
check "ADMIN_PASSWORD: >=20 con mayuscula, minuscula y digito" strong "$(val "$E" ADMIN_PASSWORD)" 20

# permisos 0600 (NTFS bajo Git Bash ignora chmod: se omite ahi, en Linux siempre corre)
: >"$T/probe"; chmod 0600 "$T/probe"
if [[ "$(stat -c %a "$T/probe")" == 600 ]]; then check ".env con modo 600" test "$(stat -c %a "$E")" = 600; else echo "SKIP modo 600: el filesystem no respeta chmod"; fi

# --- las claves del .env generado son exactamente las de .env.example (sin claves nuevas ni faltantes) ----------------------
keys() { grep -E '^[A-Z_][A-Z0-9_]*=' "$1" | cut -d= -f1 | sort; }
check "mismo conjunto de claves que .env.example" diff <(keys "$HERE/../../.env.example") <(keys "$E") >/dev/null

# --- idempotencia: no pisa un .env existente ------------------------------------------------------------------------------
before=$(cksum <"$E")
bash "$INSTALL" --env-only --env-file "$E" --admin-email otro@example.com >/dev/null 2>&1; rc=$?
check "--env-only con .env existente falla (rc!=0)" test "$rc" != 0
check "el .env existente no se modifico" test "$(cksum <"$E")" = "$before"

# --- compatibilidad con el preflight: modo community acepta key vacia ----------------------------------------------------
start=$(grep -n 'am_mode_line=' "$HERE/../deploy/preflight.sh" | head -1 | cut -d: -f1)
end=$(grep -n 'unset am_mode_line' "$HERE/../deploy/preflight.sh" | cut -d: -f1)
sed -n "${start},${end}p" "$HERE/../deploy/preflight.sh" >"$T/snippet.sh"
pf() { ( DP_ENV_FILE="$1"; FAIL=0; log(){ :; }; source "$T/snippet.sh"; exit "$FAIL" ); }
check "preflight acepta el .env community generado" pf "$E"

# --- licencia comercial ---------------------------------------------------------------------------------------------------
bash "$INSTALL" --env-only --env-file "$T/b.env" --admin-email a@example.com --license commercial >/dev/null 2>&1; rc=$?
check "--license commercial sin key falla" test "$rc" != 0 -a ! -e "$T/b.env"
bash "$INSTALL" --env-only --env-file "$T/c.env" --admin-email a@example.com --license commercial --automapper-key KEY.sintetica.123 >/dev/null 2>&1; rc=$?
check "--license commercial con key genera .env" test "$rc" = 0 -a "$(val "$T/c.env" AUTOMAPPER_LICENSE_KEY)" = KEY.sintetica.123 -a -z "$(val "$T/c.env" AUTOMAPPER_LICENSE_MODE)"
check "preflight acepta el .env comercial con key" pf "$T/c.env"

# --- validaciones de argumentos -------------------------------------------------------------------------------------------
rej() { bash "$INSTALL" --env-only --env-file "$T/x.env" "$@" >/dev/null 2>&1; local r=$?; [[ $r != 0 && ! -e "$T/x.env" ]]; }
check "rechaza correo invalido" rej --admin-email no-es-correo
check "rechaza falta de correo" rej --domain tbp
check "rechaza dominio invalido" rej --admin-email a@example.com --domain 'tb p;x'
check "rechaza licencia desconocida" rej --admin-email a@example.com --license gratis
check "rechaza backup-dir relativo" rej --admin-email a@example.com --backup-dir relativo/dir
check "rechaza backup-dir con caracteres raros" rej --admin-email a@example.com --backup-dir '/srv/a b;rm'
check "rechaza argumento desconocido" rej --admin-email a@example.com --foo

echo
echo "install tests: $PASS PASS, $FAIL FAIL"
(( FAIL == 0 ))
