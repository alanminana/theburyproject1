#!/usr/bin/env bash
# Tests de las funciones de validacion de scripts/install/verificar-servidor-lib.sh (puertos publicados y cadena de firewall).
# Sin docker, sin root, sin tocar el sistema: trabajan sobre FIXTURES con la salida cruda de `docker port` e `iptables -S`.
#   bash scripts/install/test-verificar-servidor.sh        (exit 0 = todo OK)
set -uo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=verificar-servidor-lib.sh
source "$HERE/verificar-servidor-lib.sh"
T=$(mktemp -d); trap 'rm -rf "$T"' EXIT
PASS=0 FAIL=0
ok()  { PASS=$((PASS+1)); echo "PASS $1"; }
bad() { FAIL=$((FAIL+1)); echo "FAIL $1"; }
si()  { local n=$1; shift; if "$@"; then ok "$n"; else bad "$n"; fi; }
no()  { local n=$1; shift; if "$@"; then bad "$n"; else ok "$n"; fi; }
fx()  { printf '%s\n' "$@" >"$T/f"; echo "$T/f"; }   # fx linea... -> ruta del fixture

check_bash() { bash -n "$HERE/verificar-servidor.sh" && bash -n "$HERE/verificar-servidor-lib.sh"; }
si "bash -n verificar-servidor.sh y su lib" check_bash

# ---- puertos publicados (formato `docker port`, con el servicio delante)
G1='caddy 443/tcp -> 0.0.0.0:8443'; G2='caddy 443/udp -> 0.0.0.0:8443'
si "solo caddy 443 tcp+udp hacia el puerto configurado -> OK" puertos_publicados_ok "$(fx "$G1" "$G2")" 8443
si "...tambien con IPv6 ([::]:8443)" puertos_publicados_ok "$(fx "$G1" 'caddy 443/tcp -> [::]:8443' "$G2")" 8443
si "nada publicado por db/app (archivo con solo caddy) -> OK" puertos_publicados_ok "$(fx "$G1")" 8443
no "SQL remapeado (0.0.0.0:11433->1433) -> NO es OK (el puerto del host no engana)" puertos_publicados_ok "$(fx "$G1" "$G2" 'db 1433/tcp -> 0.0.0.0:11433')" 8443
si "...y se detecta como puerto interno publicado" bash -c 'source "$1"; [[ -n "$(puertos_internos_publicados "$2")" ]]' _ "$HERE/verificar-servidor-lib.sh" "$(fx 'db 1433/tcp -> 0.0.0.0:11433')"
no "app 8080 publicado (aunque sea en loopback) -> NO es OK" puertos_publicados_ok "$(fx "$G1" "$G2" 'app 8080/tcp -> 127.0.0.1:8080')" 8443
no "API de Caddy 2019 publicada -> NO es OK" puertos_publicados_ok "$(fx "$G1" "$G2" 'caddy 2019/tcp -> 0.0.0.0:2019')" 8443
no "Caddy publica ademas el 80 -> NO es OK" puertos_publicados_ok "$(fx "$G1" "$G2" 'caddy 80/tcp -> 0.0.0.0:80')" 8443
no "Caddy 443 publicado hacia OTRO puerto del host -> NO es OK" puertos_publicados_ok "$(fx 'caddy 443/tcp -> 0.0.0.0:8444' 'caddy 443/udp -> 0.0.0.0:8444')" 8443
no "puerto del host que solo CONTIENE el configurado (18443) -> NO es OK" puertos_publicados_ok "$(fx 'caddy 443/tcp -> 0.0.0.0:18443')" 8443

# ---- cadena de firewall (formato `iptables -S CADENA`), tal como la deja bury-firewall.sh
R_T='-A BURY-ERP-PROV -s 192.168.50.0/24 -p tcp -m conntrack --ctstate DNAT --ctorigdstport 8443 --ctdir ORIGINAL -j RETURN'
R_U='-A BURY-ERP-PROV -s 192.168.50.0/24 -p udp -m conntrack --ctstate DNAT --ctorigdstport 8443 --ctdir ORIGINAL -j RETURN'
D_T='-A BURY-ERP-PROV -p tcp -m conntrack --ctstate DNAT --ctorigdstport 8443 --ctdir ORIGINAL -j DROP'
D_U='-A BURY-ERP-PROV -p udp -m conntrack --ctstate DNAT --ctorigdstport 8443 --ctdir ORIGINAL -j DROP'
END='-A BURY-ERP-PROV -j RETURN'
si "cadena completa (RETURN de la LAN y DROP final, tcp y udp) -> OK con el CIDR esperado" cadena_firewall_ok "$(fx '-N BURY-ERP-PROV' "$R_T" "$R_U" "$D_T" "$D_U" "$END")" 8443 192.168.50.0/24
si "...y sin indicar CIDR" cadena_firewall_ok "$(fx "$R_T" "$R_U" "$D_T" "$D_U" "$END")" 8443
no "sin los DROP (conserva la LAN y el salto) -> NO es OK (no aisla)" cadena_firewall_ok "$(fx "$R_T" "$R_U" "$END")" 8443 192.168.50.0/24
no "sin el DROP de udp -> NO es OK" cadena_firewall_ok "$(fx "$R_T" "$R_U" "$D_T" "$END")" 8443 192.168.50.0/24
no "sin el RETURN de udp -> NO es OK" cadena_firewall_ok "$(fx "$R_T" "$D_T" "$D_U" "$END")" 8443 192.168.50.0/24
no "DROP ANTES del RETURN (la LAN quedaria descartada) -> NO es OK" cadena_firewall_ok "$(fx "$D_T" "$D_U" "$R_T" "$R_U" "$END")" 8443 192.168.50.0/24
no "origen permitido distinto del esperado (10.0.0.0/8) -> NO es OK" cadena_firewall_ok "$(fx "${R_T//192.168.50.0\/24/10.0.0.0/8}" "${R_U//192.168.50.0\/24/10.0.0.0/8}" "$D_T" "$D_U" "$END")" 8443 192.168.50.0/24
no "reglas de OTRO puerto (443) no cuentan para 8443 -> NO es OK" cadena_firewall_ok "$(fx "${R_T//8443/443}" "${R_U//8443/443}" "${D_T//8443/443}" "${D_U//8443/443}" "$END")" 8443 192.168.50.0/24
no "un DROP con origen (no es el descarte final) -> NO es OK" cadena_firewall_ok "$(fx "$R_T" "$R_U" "${D_T/-A BURY-ERP-PROV/-A BURY-ERP-PROV -s 1.2.3.4/32}" "${D_U/-A BURY-ERP-PROV/-A BURY-ERP-PROV -s 1.2.3.4/32}" "$END")" 8443 192.168.50.0/24
no "RETURN incondicional al inicio (el DROP quedaria anulado) -> NO es OK" cadena_firewall_ok "$(fx '-A BURY-ERP-PROV -j RETURN' "$R_T" "$R_U" "$D_T" "$D_U" "$END")" 8443 192.168.50.0/24
no "ACCEPT incondicional al inicio -> NO es OK" cadena_firewall_ok "$(fx '-A BURY-ERP-PROV -j ACCEPT' "$R_T" "$R_U" "$D_T" "$D_U" "$END")" 8443 192.168.50.0/24
no "RETURN con -s 0.0.0.0/0 antes del DROP (equivale a incondicional) -> NO es OK" cadena_firewall_ok "$(fx "${R_T/192.168.50.0\/24/0.0.0.0\/0}" "${R_U/192.168.50.0\/24/0.0.0.0\/0}" "$D_T" "$D_U" "$END")" 8443 192.168.50.0/24
no "RETURN incondicional solo para tcp 8443 antes del DROP -> NO es OK" cadena_firewall_ok "$(fx '-A BURY-ERP-PROV -p tcp -m conntrack --ctstate DNAT --ctorigdstport 8443 --ctdir ORIGINAL -j RETURN' "$R_T" "$R_U" "$D_T" "$D_U" "$END")" 8443 192.168.50.0/24
si "RETURN incondicional de OTRO puerto (443) no afecta a 8443 -> OK" cadena_firewall_ok "$(fx '-A BURY-ERP-PROV -p tcp -m conntrack --ctstate DNAT --ctorigdstport 443 --ctdir ORIGINAL -j RETURN' "$R_T" "$R_U" "$D_T" "$D_U" "$END")" 8443 192.168.50.0/24
no "regla con opciones desconocidas (-i eth0) -> falla cerrado" cadena_firewall_ok "$(fx '-A BURY-ERP-PROV -i eth0 -j RETURN' "$R_T" "$R_U" "$D_T" "$D_U" "$END")" 8443 192.168.50.0/24
no "el origen permitido llega a un DROP antes que al RETURN -> NO es OK" cadena_firewall_ok "$(fx "$D_T" "$D_U" "$R_T" "$R_U" "$END")" 8443 192.168.50.0/24
no "otro origen permitido (10.0.0.0/8 ACCEPT) ANTES de la LAN -> NO es OK" cadena_firewall_ok "$(fx '-A BURY-ERP-PROV -s 10.0.0.0/8 -p tcp -m conntrack --ctstate DNAT --ctorigdstport 8443 --ctdir ORIGINAL -j ACCEPT' "$R_T" "$R_U" "$D_T" "$D_U" "$END")" 8443 192.168.50.0/24
no "otro origen permitido (10.0.0.0/8 RETURN) DESPUES del DROP (inalcanzable, pero la regla no es la esperada) -> NO es OK" cadena_firewall_ok "$(fx "$R_T" "$R_U" "$D_T" "$D_U" "${R_T//192.168.50.0\/24/10.0.0.0/8}" "$END")" 8443 192.168.50.0/24
si "dos origenes autorizados (LAN y tailnet) indicados con coma -> OK" cadena_firewall_ok "$(fx "$R_T" "${R_T//192.168.50.0\/24/100.64.0.0/10}" "$R_U" "${R_U//192.168.50.0\/24/100.64.0.0/10}" "$D_T" "$D_U" "$END")" 8443 192.168.50.0/24,100.64.0.0/10
no "...pero con solo la LAN indicada, el origen 100.64.0.0/10 NO esta autorizado -> NO es OK" cadena_firewall_ok "$(fx "$R_T" "${R_T//192.168.50.0\/24/100.64.0.0/10}" "$R_U" "$D_T" "$D_U" "$END")" 8443 192.168.50.0/24
no "cadena vacia -> NO es OK" cadena_firewall_ok "$(fx '-N BURY-ERP-PROV')" 8443 192.168.50.0/24
si "verificar-servidor.sh: opcion sin valor (--project al final) -> exit 2 sin colgarse" bash -c 'timeout 10 bash "$1" --project >/dev/null 2>&1; [[ $? = 2 ]]' _ "$HERE/verificar-servidor.sh"
si "verificar-servidor.sh: --lan-cidr al final -> exit 2" bash -c 'timeout 10 bash "$1" --lan-cidr >/dev/null 2>&1; [[ $? = 2 ]]' _ "$HERE/verificar-servidor.sh"
# ---- DOCKER-USER: el trafico debe LLEGAR a la cadena de la instalacion
J='-A DOCKER-USER -j BURY-ERP-PROV'
si "DOCKER-USER: salto incondicional primero -> OK" docker_user_alcanza_cadena "$(fx '-N DOCKER-USER' "$J" '-A DOCKER-USER -j BURY-ERP' '-A DOCKER-USER -j RETURN')" BURY-ERP-PROV
si "...tras el salto a otra cadena BURY (no termina el recorrido) -> OK" docker_user_alcanza_cadena "$(fx '-A DOCKER-USER -j BURY-ERP' "$J" '-A DOCKER-USER -j RETURN')" BURY-ERP-PROV
no "RETURN incondicional ANTES del salto -> NO es OK (evita el filtro)" docker_user_alcanza_cadena "$(fx '-A DOCKER-USER -j RETURN' "$J")" BURY-ERP-PROV
no "ACCEPT incondicional antes del salto -> NO es OK" docker_user_alcanza_cadena "$(fx '-A DOCKER-USER -j ACCEPT' "$J")" BURY-ERP-PROV
no "RETURN condicional antes del salto (-s 10.0.0.0/8) -> NO es OK" docker_user_alcanza_cadena "$(fx '-A DOCKER-USER -s 10.0.0.0/8 -j RETURN' "$J")" BURY-ERP-PROV
no "salto CONDICIONAL (-p tcp) -> NO es OK (no alcanza a todo el trafico)" docker_user_alcanza_cadena "$(fx '-A DOCKER-USER -p tcp -j BURY-ERP-PROV' '-A DOCKER-USER -j RETURN')" BURY-ERP-PROV
no "el salto no existe (solo el RETURN final de Docker) -> NO es OK" docker_user_alcanza_cadena "$(fx '-N DOCKER-USER' '-A DOCKER-USER -j RETURN')" BURY-ERP-PROV
no "DOCKER-USER vacia -> NO es OK" docker_user_alcanza_cadena "$(fx '-N DOCKER-USER')" BURY-ERP-PROV
echo; echo "verificar-servidor tests: $PASS PASS, $FAIL FAIL"; (( FAIL == 0 ))
