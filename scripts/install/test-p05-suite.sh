#!/usr/bin/env bash
# P-05: corre TODAS las validaciones automatizables de acceso remoto (Tailscale / Caddy TLS / firewall / modelo de acceso) y dice, para cada
# plataforma, que quedo probado con datos sinteticos y que necesita infraestructura real. No instala ni cambia nada del equipo.
#
#   bash scripts/install/test-p05-suite.sh [--docker] [--in-dind]
#     (sin flags)  pruebas offline: politica Tailscale (perfiles linux-vm y windows), modelo de acceso Linux, instalador/firewall (print),
#                  portabilidad de archivos
#     --docker     ademas Caddy `tls internal` + confianza de la CA (usa el daemon del host: p. ej. CI)
#     --in-dind    como --docker pero con un dockerd DESCARTABLE (privilegiado) para Caddy y para el firewall DOCKER-USER real:
#                  la forma segura en una PC de desarrollo (Windows 11/WSL2) o en el servidor sin tocar su iptables
# Salida: 0 todo OK | 1 alguna prueba fallo | 2 prerequisito. En Windows las pruebas PowerShell se corren aparte (ver el final).
set -uo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DOCKER=0 DIND=0
for a in "$@"; do case "$a" in --docker) DOCKER=1 ;; --in-dind) DOCKER=1; DIND=1 ;; -h|--help) sed -n '2,13p' "$0"; exit 0 ;; *) echo "argumento desconocido: $a"; exit 2 ;; esac; done

declare -a NAMES RESULTS
run() { # run "descripcion" comando...
  local n=$1; shift
  local out rc; out="$("$@" 2>&1)"; rc=$?
  NAMES+=("$n"); RESULTS+=("$([[ $rc == 0 ]] && echo OK || echo FALLO)|$(echo "$out" | grep -E '[0-9]+ PASS' | tail -n1)")
  (( rc == 0 )) || { echo "--- $n: FALLO (exit $rc) ---"; echo "$out" | grep -E '^FAIL|FALLO|ERROR' | head -10; }
}

run "Politica Tailscale (ACL/grants, roles, aislamiento, revocacion; perfiles linux-vm y windows)" bash "$HERE/test-tailscale-policy.sh"
run "Modelo de acceso del host Linux (la LAN solo web; administracion solo tailnet o /32)"        bash "$HERE/test-access-model.sh"
run "Instalador: .env, firewall (print), validacion de argumentos"                                bash "$HERE/test-install.sh"
run "Portabilidad de archivos entre hosts (LF, shebang, modo ejecutable, compose)"                bash "$HERE/test-host-portability.sh"
if (( DOCKER )); then
  flag=(); (( DIND )) && flag=(--in-dind)
  run "Caddy tls internal + confianza de la CA raiz (cliente que confia / no confia)"             bash "$HERE/test-caddy-tls.sh" "${flag[@]}"
  if (( DIND )) || [[ "$(id -u)" == 0 ]]; then
    run "Firewall DOCKER-USER real con clientes en tailnet / LAN / ajeno"                           bash "$HERE/test-firewall-isolation.sh" "${flag[@]}"
  else
    NAMES+=("Firewall DOCKER-USER real"); RESULTS+=("OMITIDA|requiere root o --in-dind")
  fi
fi

echo
printf '%-100s %s\n' "PRUEBA" "RESULTADO"
FAILED=0
for i in "${!NAMES[@]}"; do
  printf '%-100s %s\n' "${NAMES[$i]:0:100}" "${RESULTS[$i]//|/  }"
  [[ "${RESULTS[$i]}" == FALLO* ]] && FAILED=1
done
cat <<'EOF'

Windows 11 (PowerShell 5.1, sin Docker; se corren en el propio equipo o en CI windows-latest):
  powershell -NoProfile -ExecutionPolicy Bypass -File scripts\install\Test-RemoteAccessModel.ps1   modelo de acceso: reglas de firewall sinteticas
  powershell -NoProfile -ExecutionPolicy Bypass -File scripts\install\Test-InstallErpCa.ps1        instalacion de la CA en clientes (-VerifyOnly)
  powershell -NoProfile -ExecutionPolicy Bypass -File scripts\install\Test-ErpHealth.ps1            chequeo de salud con escenarios simulados
  powershell -NoProfile -ExecutionPolicy Bypass -File scripts\install\preflight-windows.ps1         SOLO LECTURA del equipo real (FALLA/ATENCION = lo que falta instalar)

NO automatizable (requiere el servidor y la tailnet reales; ver docs/p04-plataforma-matriz-automatizacion.md):
  - que el nodo exista en la tailnet y la ACL/grants publicada en la consola de Tailscale sea la del repo (deploy/tailscale/*.hujson);
  - acceso desde un cliente remoto real por Tailscale y falla de acceso desde fuera de la tailnet (datos moviles);
  - clave del nodo sin expiracion, "Run unattended", MagicDNS/nombre definitivo y CA instalada en los clientes reales;
  - reglas Allow amplias de Docker Desktop (Windows) frente al filtro del firewall: probar con un cliente LAN real;
  - ufw + DOCKER-USER en el Ubuntu real (el dockerd descartable prueba la logica, no el iptables del servidor);
  - reinicio sin sesion iniciada / corte de energia (inicio de sesion automatico en Windows, systemd en Ubuntu, BIOS, UPS).
EOF
(( FAILED == 0 ))
