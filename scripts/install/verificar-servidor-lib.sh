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
# cadena_firewall_ok ARCHIVO PUERTO [CIDR]: ARCHIVO = `iptables -S <cadena>`. SIMULA el recorrido de la cadena, en orden, para el trafico DNAT al PUERTO:
#   - un origen EXTERNO (fuera de cualquier -s) debe terminar en DROP: la PRIMERA regla que lo alcanza tiene que ser un DROP (una regla incondicional
#     RETURN/ACCEPT anterior lo dejaria pasar y se rechaza), para tcp y udp;
#   - un origen PERMITIDO (CIDR, o el de la primera regla RETURN con -s) debe terminar en RETURN/ACCEPT (la regla RETURN llega antes que el DROP).
#   Falla cerrado: una regla con opciones que este validador no entiende (-i, -d, --dport...) no se da por buena.
cadena_firewall_ok() {
  local f="$1" port="$2" cidr="${3:-}" proto
  local -a tk
  local allowed="$cidr" line
  if [[ -z "$allowed" ]]; then   # origen permitido = el de la primera regla RETURN con -s
    while IFS= read -r line; do [[ "$line" == "-A "* && "$line" == *" -s "* && "$line" == *"-j RETURN"* ]] || continue
      read -ra tk <<<"$line"; local k; for ((k=0;k<${#tk[@]};k++)); do [[ "${tk[k]}" == -s ]] && { allowed="${tk[k+1]}"; break 2; }; done; done <"$f"
  fi
  [[ -n "$allowed" ]] || return 1
  for proto in tcp udp; do
    local who res
    for who in outsider allowed; do
      res=""
      while IFS= read -r line; do
        [[ "$line" == "-A "* ]] || continue
        read -ra tk <<<"$line"
        local src="" pr="" op="" tg="" i=2 unk=0
        while ((i<${#tk[@]})); do
          case "${tk[i]}" in
            -s) src="${tk[i+1]}"; i=$((i+2)) ;;
            -p) pr="${tk[i+1]}"; i=$((i+2)) ;;
            -m) [[ "${tk[i+1]}" == conntrack ]] || unk=1; i=$((i+2)) ;;
            --ctstate) [[ "${tk[i+1]}" == DNAT ]] || unk=1; i=$((i+2)) ;;
            --ctorigdstport) op="${tk[i+1]}"; i=$((i+2)) ;;
            --ctdir) [[ "${tk[i+1]}" == ORIGINAL ]] || unk=1; i=$((i+2)) ;;
            -j) tg="${tk[i+1]}"; i=$((i+2)) ;;
            *) unk=1; i=$((i+1)) ;;
          esac
        done
        [[ $unk -eq 0 ]] || { res="UNKNOWN"; break; }
        [[ -z "$pr" || "$pr" == "$proto" || "$pr" == all ]] || continue            # otro protocolo
        [[ -z "$op" || "$op" == "$port" ]] || continue                              # otro puerto
        [[ "$src" == "0.0.0.0/0" ]] && src=""
        if [[ -n "$src" ]]; then
          if [[ "$who" == outsider ]]; then continue; fi                           # un origen externo no esta dentro de ningun -s
          [[ "$src" == "$allowed" ]] || continue
        fi
        res="$tg"; break
      done <"$f"
      if [[ "$who" == outsider ]]; then [[ "$res" == DROP ]] || return 1; else [[ "$res" == RETURN || "$res" == ACCEPT ]] || return 1; fi
    done
  done
  return 0
}
