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

# deploy.sh exige backup.sh ejecutable: todos los .sh versionados deben tener modo 100755 en Git (si no, un clone nuevo falla al actualizar)
if git -C "$HERE" rev-parse --is-inside-work-tree >/dev/null 2>&1; then
  check "todos los .sh versionados son ejecutables en Git (100755)" bash -c '[[ -z "$(git -C "$1" ls-files -s "*.sh" | awk "\$1 != \"100755\" {print \$4}")" ]]' _ "$HERE"
fi

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

# --- Windows Server (VM Hyper-V): bootstrap Linux y firewall de puertos publicados ----------------------------------------------
BOOT="$HERE/server-linux-bootstrap.sh"; FW="$HERE/bury-firewall.sh"
check "bash -n server-linux-bootstrap.sh" bash -n "$BOOT"
check "bash -n bury-firewall.sh" bash -n "$FW"
ME=$(id -un)
bootdry() { BURY_FORCE_DOCKER_INSTALL=1 bash "$BOOT" --dry-run --user "$ME" "$@" 2>&1; }
out=$(bootdry --allow-from 192.168.1.0/24,100.64.0.0/10 --admin-from 192.168.1.0/24 --backup-dir /srv/bury-test --tailscale); rc=$?
check "bootstrap --dry-run completo (rc=0)" test "$rc" = 0
check "bootstrap: Docker Engine desde el repo oficial" grep -q 'download.docker.com/linux/ubuntu/gpg' <<<"$out"
check "bootstrap: shutdown-timeout 60 en daemon.json" grep -q 'shutdown-timeout": 60' <<<"$out"
check "bootstrap: drop-in que aplica el firewall con dockerd" grep -q 'bury-firewall' <<<"$out"
check "bootstrap: firewall con las dos redes" grep -q '192.168.1.0/24 100.64.0.0/10' <<<"$out"
check "bootstrap: ufw solo SSH desde --admin-from" grep -q 'ufw allow from 192.168.1.0/24 to any port 22 proto tcp' <<<"$out"
check "bootstrap: Tailscale solo si se pide" grep -q 'tailscaled' <<<"$out"
out=$(bootdry --allow-from 192.168.1.0/24)
check "bootstrap sin --tailscale no instala Tailscale" bash -c '! grep -q tailscaled <<<"$1"' _ "$out"
check "bootstrap sin --admin-from NO activa ufw" bash -c '! grep -q "ufw --force enable" <<<"$1" && grep -q "sin --admin-from" <<<"$1"' _ "$out"
brej() { bash "$BOOT" --dry-run "$@" >/dev/null 2>&1; [[ $? != 0 ]]; }
check "bootstrap rechaza falta de --allow-from" brej --user "$ME"
check "bootstrap rechaza CIDR invalido" brej --user "$ME" --allow-from '10.0.0.0/33'
check "bootstrap rechaza inyeccion en --allow-from" brej --user "$ME" --allow-from '10.0.0.0/8;id'
check "bootstrap rechaza usuario inexistente" brej --user no_existe_zzz --allow-from 10.0.0.0/8
check "bootstrap rechaza --backup-dir con caracteres raros" brej --user "$ME" --allow-from 10.0.0.0/8 --backup-dir '/srv/a b'
check "bootstrap rechaza argumento desconocido" brej --user "$ME" --allow-from 10.0.0.0/8 --foo
check "bootstrap rechaza lista vacia (--allow-from ',')" brej --user "$ME" --allow-from ','
check "bootstrap rechaza --admin-from vacio ',' (dejaria ufw sin SSH)" brej --user "$ME" --allow-from 10.0.0.0/8 --admin-from ','
check "bootstrap rechaza /0 (anula el filtro)" brej --user "$ME" --allow-from 0.0.0.0/0
check "bootstrap rechaza octeto 256 y ceros a la izquierda" bash -c 'for v in 256.1.1.1/24 010.0.0.1; do bash "$1" --dry-run --user "$2" --allow-from "$v" >/dev/null 2>&1 && exit 1; done; exit 0' _ "$BOOT" "$ME"
check "bootstrap rechaza opcion sin valor (--allow-from al final)" brej --user "$ME" --allow-from
check "bootstrap rechaza puerto fuera de rango" brej --user "$ME" --allow-from 10.0.0.0/8 --https-port 70000

# bury-firewall.sh con un iptables simulado (registra los argumentos; -D falla para cortar el bucle de limpieza)
cat >"$T/ipt" <<'STUB'
#!/usr/bin/env bash
echo "$*" >>"$IPT_LOG"
for a in "$@"; do [[ "$a" == -D ]] && exit 1; done
exit 0
STUB
chmod +x "$T/ipt"
printf 'ALLOW_FROM="192.168.1.0/24 100.64.0.0/10"\nHTTPS_PORT=443\n' >"$T/fw.conf"
IPT_LOG="$T/ipt.log" BURY_IPTABLES="$T/ipt" BURY_FW_CONF="$T/fw.conf" bash "$FW" apply >/dev/null 2>&1; rc=$?
check "bury-firewall apply (rc=0)" test "$rc" = 0
check "firewall: cadena nueva enganchada al inicio de DOCKER-USER" grep -qx -- '-I DOCKER-USER 1 -j BURY-ERP-NEW' "$T/ipt.log"
check "firewall: reemplazo atomico (se retira la vieja y se renombra la nueva)" bash -c 'grep -q -- "-D DOCKER-USER -j BURY-ERP$" "$1" && grep -qx -- "-E BURY-ERP-NEW BURY-ERP" "$1"' _ "$T/ipt.log"
check "firewall: el enganche nuevo se inserta ANTES de retirar el viejo" bash -c 'a=$(grep -n -- "-I DOCKER-USER 1 -j BURY-ERP-NEW" "$1" | head -1 | cut -d: -f1); b=$(grep -n -- "-D DOCKER-USER -j BURY-ERP$" "$1" | head -1 | cut -d: -f1); [[ $a -lt $b ]]' _ "$T/ipt.log"
check "firewall: RETURN para cada red y protocolo (2 redes x tcp/udp + final)" test "$(grep -c -- '-j RETURN' "$T/ipt.log")" = 5
check "firewall: DROP del puerto original 443 para el resto (tcp y udp)" test "$(grep -c -- 'ctorigdstport 443 --ctdir ORIGINAL -j DROP' "$T/ipt.log")" = 2
check "firewall: los DROP van DESPUES de los RETURN" bash -c 'a=$(grep -n -- "-s 100.64.0.0/10 -j RETURN" "$1" | tail -1 | cut -d: -f1); b=$(grep -n -- "-j DROP" "$1" | head -1 | cut -d: -f1); [[ $a -lt $b ]]' _ "$T/ipt.log"
printf 'ALLOW_FROM=""\n' >"$T/fw-empty.conf"
check "firewall: ALLOW_FROM vacio no aplica nada" bash -c '! IPT_LOG=/dev/null BURY_IPTABLES="$1" BURY_FW_CONF="$2" bash "$3" apply >/dev/null 2>&1' _ "$T/ipt" "$T/fw-empty.conf" "$FW"
printf 'ALLOW_FROM="10.0.0.0/8; id"\n' >"$T/fw-bad.conf"
check "firewall: valor invalido en ALLOW_FROM se rechaza" bash -c '! IPT_LOG=/dev/null BURY_IPTABLES="$1" BURY_FW_CONF="$2" bash "$3" print >/dev/null 2>&1' _ "$T/ipt" "$T/fw-bad.conf" "$FW"
printf 'ALLOW_FROM="10.0.0.0/8"\nHTTPS_PORT=99999\n' >"$T/fw-port.conf"
check "firewall: /0 y octetos invalidos se rechazan" bash -c 'for v in 0.0.0.0/0 10.0.0.256 010.1.1.1; do printf "ALLOW_FROM=\"%s\"\n" "$v" >"$1/fw-x.conf"; BURY_FW_CONF="$1/fw-x.conf" bash "$2" print >/dev/null 2>&1 && exit 1; done; exit 0' _ "$T" "$FW"
check "firewall: puerto fuera de rango se rechaza" bash -c '! BURY_FW_CONF="$1" bash "$2" print >/dev/null 2>&1' _ "$T/fw-port.conf" "$FW"
: >"$T/ipt.log"
IPT_LOG="$T/ipt.log" BURY_IPTABLES="$T/ipt" BURY_FW_CONF="$T/fw.conf" bash "$FW" print >"$T/print.out" 2>&1
check "firewall print no ejecuta iptables" test ! -s "$T/ipt.log"

echo
echo "install tests: $PASS PASS, $FAIL FAIL"
(( FAIL == 0 ))
