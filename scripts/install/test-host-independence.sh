#!/usr/bin/env bash
# P-04 / P-07: prueba de INDEPENDENCIA DEL HOST. Instala el ERP con el instalador REAL (install.sh) sobre un stack Docker 100% sintetico
# y descartable, y comprueba lo que no debe depender del sistema operativo anfitrion: configuracion inicial, persistencia, reinicio y
# restauracion. Corre igual sobre Docker Desktop (Windows 11 + WSL2) que sobre Docker Engine (Ubuntu): lo que cambia entre hosts se
# registra en el perfil (host-profile.json), no se asume.
#
#   bash scripts/install/test-host-independence.sh [--image repo:tag] [--rebuild] [--keep] [--skip-restore] [--out DIR]
#
# Debe ejecutarse en Linux o dentro de WSL2 (como install.sh). En Git Bash/MSYS se rechaza: las rutas se reescriben y los bind mounts
# fallan; usar `wsl -d Ubuntu -- bash scripts/install/test-host-independence.sh`.
#
# Garantias: proyecto Compose propio (buryhi<rand>), red/volumenes/puerto/subred propios, .env generado por el instalador con secretos
# aleatorios en un directorio temporal, HTTPS solo en 127.0.0.1. NO toca TheBuryProjectDb, ni otros proyectos, ni credenciales/respaldos
# reales, ni aplica migraciones a ninguna base real (las aplica a la base sintetica del propio stack). Al terminar elimina sus
# contenedores, volumenes y red (--keep los conserva para inspeccion). La imagen construida se conserva (cache) salvo --rebuild.
# Salida: 0 todo OK | 1 hay FAIL | 2 prerequisito/uso. No imprime secretos.
set -uo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(cd "$HERE/../.." && pwd)"

IMAGE="" REBUILD=0 KEEP=0 SKIP_RESTORE=0 OUT=""
while (( $# )); do
  case "$1" in
    --image) IMAGE="${2:-}"; shift 2 ;;
    --rebuild) REBUILD=1; shift ;;
    --keep) KEEP=1; shift ;;
    --skip-restore) SKIP_RESTORE=1; shift ;;
    --out) OUT="${2:-}"; shift 2 ;;
    -h|--help) sed -n '2,17p' "$0"; exit 0 ;;
    *) echo "argumento desconocido: $1"; exit 2 ;;
  esac
done

case "$(uname -s)" in MINGW*|MSYS*|CYGWIN*) echo "Ejecutar en Linux o WSL2 (wsl -d Ubuntu -- bash $0): Git Bash reescribe rutas y rompe los bind mounts."; exit 2 ;; esac
for c in docker openssl curl git python3; do command -v "$c" >/dev/null 2>&1 || { echo "falta el comando: $c"; exit 2; }; done
docker info >/dev/null 2>&1 || { echo "docker no responde"; exit 2; }

# Entorno controlado: scripts/deploy y scripts/backup dan prioridad a variables exportadas (BACKUP_DIR, BACKUP_REMOTE, COMPOSE_FILE, DP_*...).
# Si el arnés corre desde una sesion operativa, esas variables apuntarian a recursos REALES: se descartan todas antes de empezar y los valores
# que si hacen falta se fijan explicitamente mas abajo. Tambien se rechaza un daemon remoto (DOCKER_HOST tcp:// o ssh://).
for v in $(compgen -e | grep -E '^(COMPOSE_|DP_|BACKUP_|ERP_|MSSQL_|ADMIN_|AUTOMAPPER_|CSP_|BURY_|DOCKER_CONTEXT$|DOCKER_CONFIG$)'); do unset "$v"; done
case "${DOCKER_HOST:-}" in ""|unix://*) ;; *) echo "DOCKER_HOST=${DOCKER_HOST%%:*}://... es un daemon remoto: el arnés solo corre contra el daemon local"; exit 2 ;; esac
[[ "$(docker context show 2>/dev/null)" =~ ^(default|desktop-linux)$ ]] || { echo "contexto de docker no local ($(docker context show)): usar el contexto default"; exit 2; }

PASS=0 FAIL=0 SKIP=0
ok()   { PASS=$((PASS+1)); echo "PASS $1"; }
bad()  { FAIL=$((FAIL+1)); echo "FAIL $1"; }
skip() { SKIP=$((SKIP+1)); echo "SKIP $1"; }
check() { local n=$1; shift; if "$@" >/dev/null 2>&1; then ok "$n"; else bad "$n"; fi; }

ALPINE="$(grep -oE 'alpine:3@sha256:[a-f0-9]+' "$REPO/docker-compose.yml" | head -n1)"
P="buryhi$RANDOM$RANDOM"
T="$(mktemp -d "/tmp/${P}.XXXX")"
ENVF="$T/.env"; BK="$T/backups"; ART="${OUT:-$T/out}"; mkdir -p "$BK" "$ART"
LOG="$ART/install.log"
# install.sh exporta la CA publica de Caddy SIEMPRE a $REPO/caddy-root-ca.crt: si ya existe (instalacion real en este checkout) se rechaza en vez de pisarla.
CA_FILE="$REPO/caddy-root-ca.crt"
[[ -e "$CA_FILE" ]] && { echo "Existe $CA_FILE (CA de una instalacion en este checkout): el instalador la sobrescribiria. Correr el arnés desde otro checkout/worktree."; exit 2; }
dc() { docker compose --progress quiet -p "$P" --env-file "$ENVF" -f "$REPO/docker-compose.yml" "$@"; }
envval() { grep -E "^$1=" "$ENVF" | tail -n1 | cut -d= -f2-; }
now() { date +%s; }

cleanup() {
  local ec=$?
  if (( KEEP )); then echo "--keep: proyecto $P y $T conservados"; exit $ec; fi
  dc down -v --remove-orphans >/dev/null 2>&1
  docker ps -aq --filter "label=com.docker.compose.project=$P" 2>/dev/null | xargs -r docker rm -f >/dev/null 2>&1
  docker volume ls -q --filter "label=com.docker.compose.project=$P" 2>/dev/null | xargs -r docker volume rm -f >/dev/null 2>&1
  docker network ls -q --filter "label=com.docker.compose.project=$P" 2>/dev/null | xargs -r docker network rm >/dev/null 2>&1
  rm -f "$CA_FILE"
  # backups/sql pertenece al usuario de SQL Server (uid 10001, 750): un usuario normal no puede borrarlo; se borra con un contenedor auxiliar
  # limitado al directorio temporal propio (ruta validada) y se comprueba que no queda nada.
  if [[ "$T" == /tmp/buryhi*.* && -d "$T" && -n "${ALPINE:-}" ]]; then docker run --rm --network none -v "$T:/t" "$ALPINE" sh -c 'rm -rf /t/backups /t/out' >/dev/null 2>&1; fi
  rm -rf "$T" 2>/dev/null
  if [[ -e "$T" ]]; then echo "ERROR: no se pudo eliminar $T (quedan backups sinteticos)"; (( ec == 0 )) && ec=1; fi
  exit $ec
}
trap cleanup EXIT
trap 'exit 143' TERM INT HUP

wait_for() { # wait_for SEGUNDOS comando...   (reintenta cada 3 s)
  local t=$1 end; shift; end=$(( $(now) + t ))
  while (( $(now) < end )); do "$@" >/dev/null 2>&1 && return 0; sleep 3; done
  return 1
}
cstate() { docker inspect -f '{{.State.Status}}/{{if .State.Health}}{{.State.Health.Status}}{{else}}-{{end}}' "$(dc ps -q "$1" 2>/dev/null | head -n1)" 2>/dev/null; }
healthy() { [[ "$(cstate "$1")" == "running/healthy" ]]; }
https_code() { curl -sk --max-time 8 -o /dev/null -w '%{http_code}' --resolve "$DOMAIN:$PORT:127.0.0.1" "https://$DOMAIN:$PORT$1"; }
ready200() { [[ "$(https_code /health/ready)" == 200 ]]; }
sql() { # sql "T-SQL" -> stdout (sa dentro del contenedor; la password no sale del contenedor)
  dc exec -T db bash -c "SQLCMD_PW=\"\$MSSQL_SA_PASSWORD\"; /opt/mssql-tools18/bin/sqlcmd -C -b -S localhost -U sa -P \"\$SQLCMD_PW\" -h -1 -W -Q \"SET NOCOUNT ON; $1\"" 2>/dev/null | tr -d '\r'
}

# ------------------------------------------------------------------ 0. perfil del host (lo que cambia entre Windows 11 y Ubuntu)
echo "== 0. Perfil del host =="
KERNEL="$(uname -r)"; WSL=0; grep -qi microsoft /proc/version 2>/dev/null && WSL=1
D_OS="$(docker info --format '{{.OperatingSystem}}' 2>/dev/null)"; D_CTX="$(docker context show 2>/dev/null)"
D_VER="$(docker version --format '{{.Server.Version}}' 2>/dev/null)"; D_SD="$(docker info --format '{{.Driver}}' 2>/dev/null)"
D_CG="$(docker info --format '{{.CgroupVersion}}' 2>/dev/null)"; D_MEM="$(docker info --format '{{.MemTotal}}' 2>/dev/null)"
D_CPU="$(docker info --format '{{.NCPU}}' 2>/dev/null)"; D_RT="$(docker info --format '{{.DefaultRuntime}}' 2>/dev/null)"
D_LR="$(docker info --format '{{.LiveRestoreEnabled}}' 2>/dev/null)"
REPO_FS="$(stat -f -c %T "$REPO" 2>/dev/null)"; TMP_FS="$(stat -f -c %T "$T" 2>/dev/null)"
FAMILY="linux-engine"; [[ "$D_OS" == *"Docker Desktop"* || "$D_CTX" == desktop-* ]] && FAMILY="docker-desktop"
python3 - "$ART/host-profile.json" <<PY
import json,sys
json.dump({"kernel":"$KERNEL","wsl2":bool($WSL),"docker_server":"$D_VER","docker_os":"$D_OS","docker_context":"$D_CTX","family":"$FAMILY",
 "storage_driver":"$D_SD","cgroup":"$D_CG","mem_total_bytes":int("${D_MEM:-0}" or 0),"ncpu":int("${D_CPU:-0}" or 0),"default_runtime":"$D_RT",
 "live_restore":"$D_LR","repo_filesystem":"$REPO_FS","tmp_filesystem":"$TMP_FS","repo_path":"$REPO"},open(sys.argv[1],"w"),indent=2)
PY
echo "  host: familia=$FAMILY wsl2=$WSL docker=$D_VER ($D_OS) cgroup=v$D_CG fs-repo=$REPO_FS fs-tmp=$TMP_FS cpu=$D_CPU mem=$(( ${D_MEM:-0} / 1073741824 ))GiB"
(( ${D_MEM:-0} >= 6*1073741824 )) && ok "memoria del motor Docker >= 6 GiB (SQL Server + app)" || bad "memoria del motor Docker < 6 GiB (en Windows 11 ajustar .wslconfig memory=6GB o mas)"
[[ "$REPO_FS" == "9p" || "$REPO_FS" == "drvfs" || "$REPO_FS" == "fuseblk" || "$REPO_FS" == "v9fs" ]] \
  && echo "  AVISO: el repo esta en un montaje de Windows ($REPO_FS): lento y sin permisos POSIX; en el servidor clonar dentro del sistema de archivos de Linux (~/...)." \
  || ok "repo en sistema de archivos nativo Linux ($REPO_FS)"
[[ "$TMP_FS" == "9p" || "$TMP_FS" == "drvfs" ]] && bad "directorio temporal/backups en montaje de Windows ($TMP_FS): SQL Server y el backup requieren fs Linux" || ok "backups y estado en sistema de archivos Linux ($TMP_FS)"

# ------------------------------------------------------------------ 1. contrato estatico del compose (igual en todo host)
echo "== 1. Contrato del compose =="
python3 - "$REPO" <<'PY' && ok "compose: db/app/caddy con restart=unless-stopped, db y app con healthcheck, sin puertos en db/app, datos de SQL en volumen con nombre, sin docker.sock" || bad "compose: contrato (ver detalle arriba)"
import json,subprocess,sys,os
repo=sys.argv[1]
env=dict(os.environ,ERP_DOMAIN="tbp",MSSQL_SA_PASSWORD="x",ERP_DB_USER="u",ERP_DB_PASSWORD="x",ERP_MIGRATION_USER="m",ERP_MIGRATION_PASSWORD="x",ADMIN_EMAIL="a@b.c",ADMIN_PASSWORD="x",ADMIN_USERNAME="a")
out=subprocess.run(["docker","compose","-f",repo+"/docker-compose.yml","--profile","tools","config","--format","json"],capture_output=True,text=True,env=env)
if out.returncode: print("  config fallo:",out.stderr[:200]); sys.exit(1)
c=json.loads(out.stdout); s=c["services"]; bad=[]
for n in ("db","app","caddy"):
    if s[n].get("restart")!="unless-stopped": bad.append(f"{n}: restart={s[n].get('restart')}")
dockerfile=open(repo+"/Dockerfile").read()
if "healthcheck" not in s["db"]: bad.append("db: sin healthcheck")
if "healthcheck" not in s["app"] and "HEALTHCHECK" not in dockerfile: bad.append("app: sin healthcheck (ni en compose ni en la imagen)")
for n in ("db","app"):
    if s[n].get("ports"): bad.append(f"{n}: publica puertos")
dbvols={v.get("target"):v for v in s["db"].get("volumes",[])}
if dbvols.get("/var/opt/mssql",{}).get("type")!="volume": bad.append("db: los datos de SQL Server (/var/opt/mssql) no estan en un volumen con nombre (en Windows un bind a NTFS corrompe/ralentiza SQL Server)")
for n,sv in s.items():
    for v in sv.get("volumes",[]):
        if v.get("type")=="bind" and n in ("db","app") and not str(v.get("target","")).startswith("/backups"): bad.append(f"{n}: bind mount inesperado {v.get('source')} -> {v.get('target')}")
        if "docker.sock" in str(v.get("source","")): bad.append(f"{n}: monta docker.sock")
for b in bad: print("  ",b)
sys.exit(1 if bad else 0)
PY

# ------------------------------------------------------------------ 2. P-07: instalacion inicial con el instalador real
echo "== 2. P-07: configuracion inicial sintetica (install.sh) =="
ADMIN_MAIL="qa-$P@example.invalid"; DOMAIN="tbp-$P.test"
bash "$REPO/scripts/install/install.sh" --env-only --admin-email "$ADMIN_MAIL" --domain "$DOMAIN" --env-file "$ENVF" --project "$P" --backup-dir "$BK" --yes >"$LOG" 2>&1 \
  && ok "install.sh --env-only genera el .env" || { bad "install.sh --env-only"; tail -5 "$LOG"; exit 1; }
check ".env con permisos 600" test "$(stat -c %a "$ENVF")" = 600
check ".env sin CHANGE_ME ni CRLF" bash -c "! grep -q 'CHANGE_ME' '$ENVF' && ! grep -q \$'\r' '$ENVF'"
check "secretos distintos entre si (SA, app, migraciones, admin)" python3 - "$ENVF" <<'PY'
import sys
d=dict(l.rstrip("\n").split("=",1) for l in open(sys.argv[1]) if "=" in l and not l.startswith("#"))
v=[d[k] for k in("MSSQL_SA_PASSWORD","ERP_DB_PASSWORD","ERP_MIGRATION_PASSWORD","ADMIN_PASSWORD")]
sys.exit(0 if len(set(v))==4 and all(len(x)>=20 for x in v) else 1)
PY
# puerto y subred propios, solo loopback
PORT=0; for _ in $(seq 1 50); do c=$(( 20000 + RANDOM % 30000 )); ss -ltn 2>/dev/null | grep -q ":$c " || { PORT=$c; break; }; done
USED="$(docker network inspect $(docker network ls -q) --format '{{range .IPAM.Config}}{{.Subnet}} {{end}}' 2>/dev/null)"
SUBNET=""; for _ in $(seq 1 50); do n=$(( 100 + RANDOM % 150 )); [[ "$USED" == *"172.29.$n.0/24"* ]] || { SUBNET="172.29.$n.0/24"; break; }; done
[[ "$PORT" != 0 && -n "$SUBNET" ]] || { echo "sin puerto/subred libre"; exit 2; }
sed -i -e "s|^ERP_HTTPS_PORT=.*|ERP_HTTPS_PORT=$PORT|" -e "s|^ERP_BIND_ADDRESS=.*|ERP_BIND_ADDRESS=127.0.0.1|" -e "s|^BURY_NET_SUBNET=.*|BURY_NET_SUBNET=$SUBNET|" "$ENVF"

if [[ -z "$IMAGE" ]]; then IMAGE="theburyproject/erp:p04hi-$(git -C "$REPO" rev-parse --short HEAD 2>/dev/null || echo nogit)"; fi
(( REBUILD )) && docker rmi "$IMAGE" >/dev/null 2>&1
T0=$(now)
bash "$REPO/scripts/install/install.sh" --admin-email "$ADMIN_MAIL" --domain "$DOMAIN" --env-file "$ENVF" --project "$P" --image "$IMAGE" --backup-dir "$BK" --yes >>"$LOG" 2>&1
IEC=$?
echo "  install.sh: exit=$IEC en $(( $(now) - T0 )) s (log: $LOG)"
(( IEC == 0 )) && ok "install.sh completo (preflight, backup previo, db-init, migrate, app, health, smoke, Caddy)" || { bad "install.sh fallo (exit $IEC)"; tail -15 "$LOG"; exit 1; }

check "db, app y caddy corriendo y sanos" bash -c "true"
for s in db app; do healthy "$s" && ok "$s healthy" || bad "$s healthy ($(cstate $s))"; done
[[ "$(cstate caddy)" == running/* ]] && ok "caddy running" || bad "caddy running ($(cstate caddy))"
for s in db-init migrate; do
  ec="$(docker inspect -f '{{.State.ExitCode}}' "$(dc ps -aq "$s" | head -n1)" 2>/dev/null)"; [[ "$ec" == 0 ]] && ok "$s termino con exit 0" || bad "$s exit=$ec"
done
[[ "$(https_code /health/live)" == 200 && "$(https_code /health/ready)" == 200 ]] && ok "HTTPS por Caddy: /health/live y /health/ready = 200" || bad "HTTPS /health ($(https_code /health/live)/$(https_code /health/ready))"
# solo 443 publicado, db/app sin puertos al host
PUB="$(docker ps --filter "label=com.docker.compose.project=$P" --format '{{.Names}} {{.Ports}}')"
echo "$PUB" | grep -E '^[^ ]*-(db|app)-' | grep -q -- '->' && bad "db/app publican puertos: $(echo "$PUB" | grep -E '(db|app)-')" || ok "db y app sin puertos publicados al host"
echo "$PUB" | grep -E -- '-caddy-' | grep -q "127.0.0.1:$PORT->443" && ok "Caddy publica HTTPS solo en 127.0.0.1:$PORT" || bad "publicacion de Caddy inesperada: $(echo "$PUB" | grep caddy)"
# base y usuarios de la aplicacion
DB="$(envval MSSQL_DATABASE)"; DB="${DB:-TheBuryProjectDb}"
[[ "$(sql "SELECT COUNT(*) FROM [$DB].dbo.__EFMigrationsHistory")" -gt 0 ]] 2>/dev/null && ok "migraciones aplicadas a la base SINTETICA del stack" || bad "sin migraciones aplicadas"
[[ "$(sql "SELECT COUNT(*) FROM [$DB].dbo.AspNetUsers WHERE NormalizedUserName=UPPER('$(envval ADMIN_USERNAME)')")" == 1 ]] && ok "administrador inicial creado (solo 1)" || bad "administrador inicial ausente o duplicado"
[[ "$(sql "SELECT COUNT(*) FROM sys.server_principals WHERE name IN ('$(envval ERP_DB_USER)','$(envval ERP_MIGRATION_USER)')")" == 2 ]] && ok "logins de aplicacion y de migraciones creados por db-init" || bad "logins de db-init"
# login con la password generada (cookie de auth + antiforgery)
CJ="$T/cj.txt"
login() { # login ARCHIVO_COOKIES -> imprime el codigo HTTP del POST
  local jar=$1 tok
  tok="$(curl -sk --max-time 15 -c "$jar" --resolve "$DOMAIN:$PORT:127.0.0.1" "https://$DOMAIN:$PORT/Identity/Account/Login" | grep -o 'name="__RequestVerificationToken"[^>]*value="[^"]*"' | head -n1 | sed 's/.*value="//;s/"$//')"
  [[ -n "$tok" ]] || { echo no-token; return; }
  curl -sk --max-time 20 -b "$jar" -c "$jar" -o /dev/null -w '%{http_code}' --resolve "$DOMAIN:$PORT:127.0.0.1" \
    --data-urlencode "Input.UserName=$(envval ADMIN_USERNAME)" --data-urlencode "Input.Password=$(envval ADMIN_PASSWORD)" --data-urlencode "__RequestVerificationToken=$tok" \
    "https://$DOMAIN:$PORT/Identity/Account/Login"
}
authed() { # authed ARCHIVO_COOKIES -> 0 si "/" no redirige al login
  local loc; loc="$(curl -sk --max-time 15 -b "$1" -o /dev/null -w '%{http_code} %{redirect_url}' --resolve "$DOMAIN:$PORT:127.0.0.1" "https://$DOMAIN:$PORT/")"
  [[ "$loc" != *"/Identity/Account/Login"* && "${loc%% *}" =~ ^(200|302)$ ]]
}
LC="$(login "$CJ")"; echo "  login (fase 1: credenciales) POST -> $LC"
# El primer ingreso del administrador exige aceptar los Terminos y Condiciones en la misma pantalla (fase 2). Es parte de la configuracion inicial.
terms_page="$(curl -sk --max-time 15 -b "$CJ" -c "$CJ" --resolve "$DOMAIN:$PORT:127.0.0.1" "https://$DOMAIN:$PORT/Identity/Account/Login")"
if [[ "$LC" == 200 ]] && grep -q 'Input.AceptaTerminos' <<<"$terms_page"; then
  ok "primer ingreso: credenciales validas y pantalla de Terminos y Condiciones (fase 2)"
  tok="$(grep -o 'name="__RequestVerificationToken"[^>]*value="[^"]*"' <<<"$terms_page" | head -n1 | sed 's/.*value="//;s/"$//')"
  TC="$(curl -sk --max-time 20 -b "$CJ" -c "$CJ" -o /dev/null -w '%{http_code}' --resolve "$DOMAIN:$PORT:127.0.0.1"     --data-urlencode "Input.NombreCompleto=Administrador Sintetico QA" --data-urlencode "Input.AceptaTerminos=true" --data-urlencode "__RequestVerificationToken=$tok"     "https://$DOMAIN:$PORT/Identity/Account/Login")"
  [[ "$TC" == 302 ]] && authed "$CJ" && ok "aceptacion de Terminos y Condiciones y sesion valida por HTTPS" || bad "aceptacion de Terminos (POST=$TC)"
else bad "login del administrador inicial (POST=$LC): no llego a la fase de Terminos y Condiciones"; fi
grep -q "#HttpOnly_" "$CJ" && ok "cookie de sesion HttpOnly" || bad "cookie de sesion sin HttpOnly"
# pantallas principales con el administrador recien creado: ninguna debe dar 5xx ni mandar al login
PANT_BAD=()
for path in / /Dashboard /Cliente /Producto /Catalogo /Proveedor /Venta /Caja /Cotizacion /Credito /Mora /Reporte /Seguridad /ConfiguracionPago /ConfiguracionCliente /ConfiguracionRentabilidad /PrefijosNumeracion /DocumentosConfig /ServiciosVenta /Documento /Ticket /OrdenCompra /MovimientoStock /AlertaStock; do
  r="$(curl -sk --max-time 30 -L --max-redirs 3 -b "$CJ" -o /dev/null -w '%{http_code} %{url_effective}' --resolve "$DOMAIN:$PORT:127.0.0.1" "https://$DOMAIN:$PORT$path")"
  [[ "${r%% *}" == 200 && "$r" != *"/Identity/Account/Login"* ]] || PANT_BAD+=("$path=${r%% *}")
done
(( ${#PANT_BAD[@]} == 0 )) && ok "pantallas principales y de configuracion: 200 con el administrador inicial (base vacia, sin 5xx)" || bad "pantallas con error en base vacia: ${PANT_BAD[*]}"

# ------------------------------------------------------------------ 3. persistencia
echo "== 3. Persistencia (down/up sin borrar volumenes) =="
sql "IF OBJECT_ID('[$DB].dbo.HostIndependenceMarker') IS NULL CREATE TABLE [$DB].dbo.HostIndependenceMarker(Id int PRIMARY KEY, Val nvarchar(64)); DELETE FROM [$DB].dbo.HostIndependenceMarker; INSERT [$DB].dbo.HostIndependenceMarker VALUES (1, N'$P');" >/dev/null
[[ "$(sql "SELECT Val FROM [$DB].dbo.HostIndependenceMarker WHERE Id=1")" == "$P" ]] && ok "marcador escrito en SQL Server" || bad "no se pudo escribir el marcador"
dc down >>"$LOG" 2>&1 && ok "compose down (sin -v)" || bad "compose down"
[[ -z "$(dc ps -q 2>/dev/null)" ]] && ok "sin contenedores tras down" || bad "quedaron contenedores"
docker volume ls -q --filter "label=com.docker.compose.project=$P" | grep -q sqldata && ok "volumenes con estado conservados" || bad "volumenes perdidos tras down"
T0=$(now); dc up -d --wait --wait-timeout 240 db app caddy >>"$LOG" 2>&1 && ok "compose up -d --wait tras down ($(( $(now) - T0 )) s)" || bad "compose up tras down"
[[ "$(sql "SELECT Val FROM [$DB].dbo.HostIndependenceMarker WHERE Id=1")" == "$P" ]] && ok "el marcador sobrevive a down/up (volumen de SQL Server)" || bad "marcador perdido"
wait_for 90 ready200 && ok "/health/ready = 200 tras down/up" || bad "ready tras down/up"
authed "$CJ" && ok "la cookie de sesion previa sigue valida (claves de proteccion de datos persistidas en el volumen)" || bad "sesion previa invalidada: las claves de Data Protection no persisten"

# ------------------------------------------------------------------ 4. reinicio (politica restart: unless-stopped)
echo "== 4. Reinicio ante caida (restart: unless-stopped) =="
crash() { # crash CONTENEDOR: SIGKILL al proceso 1 DESDE FUERA (como un OOM kill o un crash). `docker kill` cuenta como parada manual y NO reinicia.
  local pid; pid="$(docker inspect -f '{{.State.Pid}}' "$1" 2>/dev/null)"
  [[ "${pid:-0}" -gt 0 ]] && docker run --rm --pid=host --network none "$ALPINE" kill -9 "$pid" >/dev/null 2>&1
}
restarted() { # restarted CONTENEDOR CONTADOR_PREVIO
  [[ "$(docker inspect -f '{{.State.Status}}' "$1")" == running && "$(docker inspect -f '{{.RestartCount}}' "$1")" -gt "$2" ]]
}
for s in app db caddy; do
  cid="$(dc ps -q $s | head -n1)"; rc0="$(docker inspect -f '{{.RestartCount}}' "$cid")"; T0=$(now)
  if crash "$cid" && wait_for 240 restarted "$cid" "$rc0"; then
    if [[ "$s" == caddy ]] || wait_for 240 healthy "$s"; then ok "$s: SIGKILL del proceso 1 -> Docker lo reinicia solo y queda sano ($(( $(now) - T0 )) s)"; else bad "$s: reinicio sin volver a healthy"; fi
  else bad "$s: no se reinicio tras SIGKILL del proceso 1 (estado $(docker inspect -f '{{.State.Status}}' "$cid"))"; fi
done
wait_for 240 ready200 && ok "tras caer app, db y caddy el ERP vuelve a responder /health/ready" || bad "ERP no se recupero tras las caidas"
[[ "$(sql "SELECT Val FROM [$DB].dbo.HostIndependenceMarker WHERE Id=1")" == "$P" ]] && ok "el marcador sobrevive a la caida abrupta de SQL Server (recuperacion de la base)" || bad "marcador perdido tras la caida de db"
authed "$CJ" && ok "la sesion del administrador sigue valida tras las caidas" || bad "sesion invalidada tras las caidas"
cid="$(dc ps -q app | head -n1)"; docker kill "$cid" >/dev/null 2>&1; sleep 12
[[ "$(docker inspect -f '{{.State.Status}}' "$cid")" == exited ]] && ok "docker kill MANUAL no se reinicia (comportamiento de Docker: el monitor debe alertar docker.app, el operador debe levantarlo)" || bad "docker kill manual se reinicio (revisar supuestos de la politica)"
dc up -d --wait --wait-timeout 180 app >>"$LOG" 2>&1 && ok "compose up -d recupera la app detenida a mano" || bad "compose up -d app tras parada manual"
dc restart app >>"$LOG" 2>&1 && wait_for 120 healthy app && ok "docker compose restart app -> healthy" || bad "restart app"

# ------------------------------------------------------------------ 5. restauracion
echo "== 5. Respaldo y restauracion =="
if (( SKIP_RESTORE )); then skip "restauracion (--skip-restore)"; else
  export COMPOSE_PROJECT_NAME="$P" COMPOSE_ENV_FILES="$ENVF" COMPOSE_FILE="$REPO/docker-compose.yml" BACKUP_DIR="$BK" BACKUP_REMOTE="" BACKUP_REQUIRE_OFFSITE=false
  # antes de escribir nada: el db que veran los scripts de backup es el del proyecto propio y su /backups/sql es el directorio temporal propio
  DBC="$(dc ps -q db | head -n1)"
  # (el Source de un bind mount que reporta `docker inspect` cambia en Docker Desktop; se compara contra la configuracion resuelta de Compose)
  [[ "$(docker inspect -f '{{index .Config.Labels "com.docker.compose.project"}}' "$DBC" 2>/dev/null)" == "$P" ]]     && dc config --format json 2>/dev/null | python3 -c 'import json,sys; c=json.load(sys.stdin); v=[x for x in c["services"]["db"]["volumes"] if x.get("target")=="/backups/sql"]; sys.exit(0 if v and v[0]["source"]==sys.argv[1] else 1)' "$BK/sql"     && ok "los backups apuntan al proyecto y al directorio propios ($P, $BK/sql)"     || { bad "los scripts de backup NO apuntan al stack/directorio propios: se omite la fase de respaldo"; SKIP_RESTORE=1; }
fi
if (( ! SKIP_RESTORE )); then
  bash "$REPO/scripts/backup/backup.sh" full >"$ART/backup.log" 2>&1 && ok "backup.sh full (SQL, VERIFYONLY + sha256)" || { bad "backup.sh full (ver $ART/backup.log)"; tail -5 "$ART/backup.log"; }
  # sql/ pertenece al usuario de SQL Server dentro del contenedor (700): un usuario normal del host no puede listarlo; se consulta desde db.
  BN="$(dc exec -T db bash -c 'ls -t /backups/sql/*_full_*.bak 2>/dev/null | head -n1' | tr -d $'\r')"; BN="${BN##*/}"
  [[ -n "$BN" ]] && ok "el .bak existe en BACKUP_DIR del host ($BN)" || bad "no hay .bak en BACKUP_DIR"
  bash "$REPO/scripts/backup/restore-test.sh" >"$ART/restore-test.log" 2>&1 && ok "restore-test.sh: restauracion real en base aislada + DBCC CHECKDB" || { bad "restore-test.sh (ver $ART/restore-test.log)"; tail -8 "$ART/restore-test.log"; }
  # recuperacion ante perdida de datos: borrar el marcador y restaurar el .bak en una base paralela; el marcador debe estar
  if bash "$REPO/scripts/backup/restore-sql.sh" --backup "$BN" --target-db "${DB}_P04Check" >"$ART/restore-sql.log" 2>&1; then
    [[ "$(sql "SELECT Val FROM [${DB}_P04Check].dbo.HostIndependenceMarker WHERE Id=1")" == "$P" ]] && ok "restore-sql.sh: el .bak restaurado contiene el marcador" || bad "el .bak restaurado no contiene el marcador"
    sql "ALTER DATABASE [${DB}_P04Check] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [${DB}_P04Check];" >/dev/null
  else bad "restore-sql.sh (ver $ART/restore-sql.log)"; tail -6 "$ART/restore-sql.log"; fi
  wait_for 120 ready200 && ok "la app sigue sana tras backup/restauracion" || bad "app no sana tras backup/restauracion"
  unset COMPOSE_PROJECT_NAME COMPOSE_ENV_FILES COMPOSE_FILE BACKUP_DIR BACKUP_REMOTE BACKUP_REQUIRE_OFFSITE
fi

# ------------------------------------------------------------------ 6. limpieza y resumen
echo
echo "host-independence: $PASS PASS, $FAIL FAIL, $SKIP SKIP  (perfil: $ART/host-profile.json; familia=$FAMILY)"
(( KEEP )) || echo "limpieza: dc down -v + contenedores/volumenes/red del proyecto $P"
(( FAIL == 0 ))
