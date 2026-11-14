#!/usr/bin/env bash
# P-04: portabilidad ESTATICA entre hosts (Windows 11 + WSL2 / Ubuntu). Sin Docker, sin root, sin red; solo lee el repositorio.
# Detecta lo que rompe un checkout hecho en Windows y luego usado por contenedores o scripts Linux:
#   - fin de linea: todo archivo que consume Linux (sh, Dockerfile, Caddyfile, yml, py, sql, service, hujson, .env.example) debe ser LF
#     por atributo de Git (independiente de core.autocrlf) y estar en LF en el arbol de trabajo;
#   - scripts de shell con shebang, sintaxis valida (bash -n) y modo ejecutable 100755 en el indice (un clon en Linux los ejecuta tal cual);
#   - los compose validan con `docker compose config` si hay Docker (CI y PC de desarrollo).
#   bash scripts/install/test-host-portability.sh        (exit 0 = todo OK)
set -uo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$HERE/../.." || exit 2
PASS=0 FAIL=0
ok()  { PASS=$((PASS+1)); echo "PASS $1"; }
bad() { FAIL=$((FAIL+1)); echo "FAIL $1"; }

mapfile -t LINUX_FILES < <(git ls-files -- '*.sh' 'Dockerfile' 'Caddyfile' '*.yml' '*.yaml' '*.py' '*.sql' '*.service' '*.hujson' '.env.example' '*.env.example' \
  | grep -Ev '^(node_modules|\.agents|\.claude|\.codex|e2e|e2e-smoke|TheBuryProyect\.Tests/artifacts)/' | grep -Ev '(^|/)(skills|data)/')
echo "archivos que consume Linux: ${#LINUX_FILES[@]}"

# 1. atributo eol=lf (resuelve .gitattributes; no depende de core.autocrlf del equipo)
NOT_LF=()
while IFS= read -r line; do
  f="${line%%: eol:*}"; v="${line##*: }"
  [[ "$v" == lf ]] || NOT_LF+=("$f($v)")
done < <(git check-attr eol -- "${LINUX_FILES[@]}")
(( ${#NOT_LF[@]} == 0 )) && ok "los ${#LINUX_FILES[@]} archivos de Linux tienen eol=lf por atributo" || { bad "sin eol=lf: ${NOT_LF[*]:0:8}"; }

# 2. arbol de trabajo sin CR (un checkout viejo hecho con core.autocrlf=true los dejo en CRLF)
CRLF=()
for f in "${LINUX_FILES[@]}"; do [[ -f "$f" ]] && grep -q $'\r' "$f" 2>/dev/null && CRLF+=("$f"); done
if (( ${#CRLF[@]} == 0 )); then ok "arbol de trabajo en LF"
else bad "con CRLF en el arbol de trabajo (${#CRLF[@]}): ${CRLF[*]:0:6}. Reparar: git rm --cached -r -q . && git reset --hard (o volver a clonar con core.autocrlf=input)"; fi

# 3. shebang + sintaxis + modo ejecutable de los scripts (los .sh de scripts/ y docker/)
BADS=() BADX=() BADN=()
while IFS= read -r f; do
  [[ "$(head -c2 "$f")" == '#!' ]] || BADS+=("$f")
  bash -n "$f" 2>/dev/null || BADN+=("$f")
  mode="$(git ls-files -s -- "$f" | cut -d' ' -f1)"
  [[ "$mode" == 100755 ]] || BADX+=("$f")
done < <(printf '%s\n' "${LINUX_FILES[@]}" | grep -E '^(scripts|docker)/.*\.sh$')
(( ${#BADS[@]} == 0 )) && ok "todos los .sh tienen shebang" || bad "sin shebang: ${BADS[*]}"
(( ${#BADN[@]} == 0 )) && ok "bash -n correcto en todos los .sh" || bad "error de sintaxis: ${BADN[*]}"
(( ${#BADX[@]} == 0 )) && ok "todos los .sh con modo 100755 en el indice" || bad "sin bit ejecutable en el indice: ${BADX[*]:0:6} (git update-index --chmod=+x)"

# 4. compose: validan (solo con Docker)
if command -v docker >/dev/null 2>&1 && docker compose version >/dev/null 2>&1; then
  export ERP_DOMAIN=tbp MSSQL_SA_PASSWORD=x ERP_DB_USER=u ERP_DB_PASSWORD=x ERP_MIGRATION_USER=m ERP_MIGRATION_PASSWORD=x ADMIN_EMAIL=a@b.c ADMIN_USERNAME=a ADMIN_PASSWORD=x BACKUP_DIR=/tmp/x
  for c in "docker-compose.yml" "docker-compose.yml docker-compose.ci.yml" "docker-compose.yml docker-compose.dev.yml" "docker-compose.monitoring.yml"; do
    args=(); for f in $c; do args+=(-f "$f"); done
    docker compose "${args[@]}" --profile tools config --quiet >/dev/null 2>&1 && ok "compose valido: $c" || bad "compose invalido: $c"
  done
else echo "SKIP compose (sin Docker)"; fi

echo "host portability tests: $PASS PASS, $FAIL FAIL"
(( FAIL == 0 ))
