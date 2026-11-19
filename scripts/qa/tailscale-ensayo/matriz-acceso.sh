#!/bin/sh
# Matriz de acceso desde el netns de un cliente tailscale (kernel mode) hacia el servidor tag:erp (100.64.0.1).
# Variables: SELF = IP tailnet de este cliente; PEERS = IPs tailnet de las otras personas (para comprobar que NO se alcanzan).
for i in 1 2 3; do command -v curl >/dev/null 2>&1 && break; apk add -q --no-cache curl >/dev/null 2>&1; sleep 2; done
printf "  https://tbp 443 (con CA): "
if command -v curl >/dev/null 2>&1; then code=$(curl -s -m 8 --cacert /ca.crt --resolve tbp:443:100.64.0.1 -o /dev/null -w "%{http_code}" https://tbp/health/ready); echo "${code:-000}"; else echo "N/D (curl no se pudo instalar: NO usar este resultado)"; fi
for p in 443 22 1433 8080 80 3389; do
  printf "  tcp/$p: "
  if nc -z -w 4 100.64.0.1 $p 2>/dev/null; then
    if [ "$p" = 22 ]; then echo "ABIERTO (banner: $(echo | nc -w 3 100.64.0.1 22 2>/dev/null | head -1 | tr -d '\r'))"; else echo ABIERTO; fi
  else echo bloqueado; fi
done
printf "  ping a otras personas: "; for ip in $PEERS; do [ "$ip" = "$SELF" ] && continue; if ping -c1 -W2 $ip >/dev/null 2>&1; then printf "$ip=RESPONDE "; else printf "$ip=no "; fi; done; echo
