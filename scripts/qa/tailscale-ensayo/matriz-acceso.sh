#!/bin/sh
apk add -q --no-cache curl >/dev/null 2>&1 || { echo "  (no se pudo instalar curl)"; }
printf "  https://tbp 443 (con CA): "; curl -s -m 8 --cacert /ca.crt --resolve tbp:443:100.64.0.1 -o /dev/null -w "%{http_code}\n" https://tbp/health/ready; [ $? -ne 0 ] && echo "sin respuesta"
for p in 443 22 1433 8080 80 3389; do printf "  tcp/$p: "; if nc -z -w 4 100.64.0.1 $p 2>/dev/null; then echo ABIERTO; else echo "bloqueado"; fi; done
printf "  ping a persona vecina: "; for ip in 100.64.0.5 100.64.0.6 100.64.0.7; do [ "$ip" = "$SELF" ] && continue; if ping -c1 -W2 $ip >/dev/null 2>&1; then printf "$ip=RESPONDE "; else printf "$ip=no "; fi; done; echo
