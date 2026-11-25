#!/usr/bin/env bash
# Funciones puras de verificar-servidor.sh (se hace `source`; no ejecutan nada ni tocan el sistema). Las prueba test-verificar-servidor.sh.

# --- funciones puras de validacion (reciben archivos con la salida cruda; las prueba test-verificar-servidor.sh)
# puertos_publicados_ok ARCHIVO PUERTO_HTTPS: ARCHIVO = lineas "<servicio> <puerto-contenedor>/<proto> -> <ip>:<puerto-host>" (docker port).
#   Falla si ALGUN servicio publica un puerto INTERNO (1433, 8080, 2019) aunque el puerto del host sea otro (p. ej. 11433->1433), o si se publica
#   algo distinto de caddy 443/tcp y 443/udp hacia PUERTO_HTTPS.
puertos_internos_publicados() { grep -E '^[a-z]+ (1433|8080|2019)/' "$1"; }
puertos_publicados_ok() {
  local f="$1" port="$2"
  [[ -z "$(puertos_internos_publicados "$f")" ]] || return 1
  # solo se admite: caddy 443/tcp y 443/udp -> :PORT
  ! grep -Ev "^caddy 443/(tcp|udp) -> [][0-9a-f.:*]+:${port}$" "$f" | grep -q .
}
# cadena_firewall_ok ARCHIVO PUERTO [CIDR]: ARCHIVO = `iptables -S <cadena>`. Exige, para tcp y udp, una regla RETURN con origen permitido
#   (CIDR si se indica) y, DESPUES de ella, una regla DROP sin origen, ambas sobre el PUERTO original DNAT. Sin DROP o con el DROP antes del RETURN, falla.
cadena_firewall_ok() {
  local f="$1" port="$2" cidr="${3:-}" proto i ret drop line
  for proto in tcp udp; do
    ret=0 drop=0 i=0
    while IFS= read -r line; do
      i=$((i+1))
      [[ "$line" == *"-p $proto "* && "$line" == *"--ctorigdstport $port "* ]] || continue
      if [[ "$line" == *"-j RETURN"* && "$line" == *" -s "* ]]; then
        if [[ -z "$cidr" || "$line" == *" -s $cidr "* ]]; then [[ $ret -eq 0 ]] && ret=$i; fi
      elif [[ "$line" == *"-j DROP"* && "$line" != *" -s "* ]]; then [[ $drop -eq 0 ]] && drop=$i
      fi
    done <"$f"
    [[ $ret -gt 0 && $drop -gt 0 && $ret -lt $drop ]] || return 1
  done
  return 0
}
