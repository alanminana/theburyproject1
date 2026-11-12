#!/usr/bin/env bash
# Prueba REAL de la confianza TLS: levanta el Caddy PINEADO de docker-compose.yml con el Caddyfile REAL del repo (`tls internal`) delante de
# un "app" sintetico, extrae SOLO la CA raiz publica como lo hace la guia, y comprueba con un cliente que confia/no confia segun corresponda.
# Datos sinteticos; no toca el stack del ERP ni la base. Prefijo buryca-, se elimina al terminar.
#
#   bash scripts/install/test-caddy-tls.sh                 en un host con Docker (p. ej. CI)
#   bash scripts/install/test-caddy-tls.sh --in-dind       en una PC: dockerd descartable (privilegiado) en lugar del daemon del host
set -uo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$HERE/../.."

if [[ "${1:-}" == "--in-dind" ]]; then
  export MSYS_NO_PATHCONV=1
  name="buryca-dind-$$"
  trap 'docker rm -f "$name" >/dev/null 2>&1' EXIT
  docker run -d --privileged --name "$name" -e DOCKER_TLS_CERTDIR= docker:dind >/dev/null || exit 2
  for _ in $(seq 1 60); do docker exec "$name" docker info >/dev/null 2>&1 && break; sleep 1; done
  docker exec "$name" docker info >/dev/null 2>&1 || { echo "el dockerd interno no arranco"; exit 2; }
  docker exec "$name" apk add --no-cache bash >/dev/null 2>&1 || exit 2
  docker exec "$name" mkdir -p /opt/bf/scripts/install || exit 2
  docker exec -i "$name" sh -c 'cat > /opt/bf/Caddyfile' <"$REPO/Caddyfile" || exit 2
  docker exec -i "$name" sh -c 'cat > /opt/bf/docker-compose.yml' <"$REPO/docker-compose.yml" || exit 2
  docker exec -i "$name" sh -c 'cat > /opt/bf/scripts/install/test-caddy-tls.sh' <"$HERE/test-caddy-tls.sh" || exit 2
  docker exec "$name" bash /opt/bf/scripts/install/test-caddy-tls.sh
  exit $?
fi

CADDY_IMG=$(grep -oE 'caddy:2-alpine@sha256:[0-9a-f]{64}' "$REPO/docker-compose.yml" | head -1)
ALPINE_IMG=$(grep -oE 'alpine:3@sha256:[0-9a-f]{64}' "$REPO/docker-compose.yml" | head -1)
[[ -n "$CADDY_IMG" && -n "$ALPINE_IMG" ]] || { echo "no se pudieron leer las imagenes pineadas de docker-compose.yml"; exit 2; }
P=buryca
DOMAIN=tbp
PASS=0 FAIL=0
ok()  { PASS=$((PASS+1)); echo "PASS $1"; }
bad() { FAIL=$((FAIL+1)); echo "FAIL $1"; }
check() { local name=$1; shift; if "$@"; then ok "$name"; else bad "$name"; fi; }
T=$(mktemp -d)
cleanup() { docker rm -f $P-caddy $P-app $P-client >/dev/null 2>&1; docker network rm $P-net >/dev/null 2>&1; rm -rf "$T"; }
trap cleanup EXIT
cleanup >/dev/null 2>&1; T=$(mktemp -d)

docker network create $P-net >/dev/null || exit 2
docker pull -q "$CADDY_IMG" >/dev/null && docker pull -q "$ALPINE_IMG" >/dev/null || { echo "no se pudieron bajar las imagenes"; exit 2; }
SRV='srv() { while :; do printf "HTTP/1.0 200 OK\r\nContent-Type: text/plain\r\n\r\nERP-OK\n" | nc -l -p "$1" >/dev/null; done; }'
docker run -d --name $P-app --network $P-net --network-alias app "$ALPINE_IMG" sh -c "$SRV; srv 8080" >/dev/null || exit 2
# Mismo Caddyfile y misma variable que en produccion; el alias de red hace que `tbp` resuelva a Caddy (en produccion: hosts / MagicDNS).
docker run -d --name $P-caddy --network $P-net --network-alias $DOMAIN -e ERP_DOMAIN=$DOMAIN \
  -v "$REPO/Caddyfile:/etc/caddy/Caddyfile:ro" "$CADDY_IMG" >/dev/null || exit 2
docker run -d --name $P-client --network $P-net "$ALPINE_IMG" sleep 900 >/dev/null || exit 2
docker exec $P-client apk add --no-cache curl openssl >/dev/null 2>&1 || { echo "no se pudo instalar curl/openssl en el cliente"; exit 2; }

for _ in $(seq 1 40); do   # esperar a que Caddy genere su CA y emita el certificado
  docker exec $P-caddy test -s /data/caddy/pki/authorities/local/root.crt 2>/dev/null && break; sleep 1
done
sleep 2

echo "== Extraccion de la CA (solo el certificado publico) =="
docker cp $P-caddy:/data/caddy/pki/authorities/local/root.crt "$T/caddy-root-ca.crt" 2>/dev/null
check "se extrae root.crt" test -s "$T/caddy-root-ca.crt"
check "root.crt es un certificado y NO contiene clave privada" bash -c 'grep -q "BEGIN CERTIFICATE" "$1" && ! grep -q "PRIVATE KEY" "$1"' _ "$T/caddy-root-ca.crt"
check "root.crt es CA raiz autofirmada (CA:TRUE)" bash -c 'o=$(openssl x509 -in "$1" -noout -text); grep -q "CA:TRUE" <<<"$o" && [[ "$(openssl x509 -in "$1" -noout -subject | cut -d= -f2-)" == "$(openssl x509 -in "$1" -noout -issuer | cut -d= -f2-)" ]]' _ "$T/caddy-root-ca.crt"
check "la clave privada de la CA existe en el servidor pero NO es lo que se extrae" bash -c 'docker exec $1 test -s /data/caddy/pki/authorities/local/root.key && ! grep -q "PRIVATE KEY" "$2"' _ $P-caddy "$T/caddy-root-ca.crt"
FP=$(openssl x509 -in "$T/caddy-root-ca.crt" -noout -fingerprint -sha256 | cut -d= -f2)
check "la huella SHA-256 se puede calcular (se entrega al cliente por otro canal)" test "${#FP}" = 95

echo "== Cliente que CONFIA en la CA =="
docker cp "$T/caddy-root-ca.crt" $P-client:/tmp/ca.crt
check "HTTPS al ERP con la CA: 200 y contenido del ERP" bash -c 'docker exec $1 curl -sS --max-time 8 --cacert /tmp/ca.crt https://$2/ 2>/dev/null | grep -q ERP-OK' _ $P-client $DOMAIN
check "el certificado de hoja cubre el nombre $DOMAIN (SAN)" bash -c 'docker exec $1 sh -c "openssl s_client -connect $2:443 -servername $2 </dev/null 2>/dev/null | openssl x509 -noout -ext subjectAltName" | grep -q "DNS:$2"' _ $P-client $DOMAIN
check "la hoja la emite la CA extraida (verify return code 0)" bash -c 'docker exec $1 sh -c "openssl s_client -connect $2:443 -servername $2 -CAfile /tmp/ca.crt </dev/null 2>&1" | grep -q "Verify return code: 0"' _ $P-client $DOMAIN

echo "== Cliente que NO confia =="
check "sin la CA, curl rechaza el certificado (no hay confianza implicita)" bash -c '! docker exec $1 curl -sS --max-time 8 https://$2/ >/dev/null 2>&1' _ $P-client $DOMAIN
check "con una CA AJENA (otra raiz), curl rechaza el certificado" bash -c 'docker exec $1 sh -c "openssl req -x509 -newkey rsa:2048 -nodes -keyout /tmp/o.key -out /tmp/otra.crt -subj /CN=otra-ca -days 2 >/dev/null 2>&1; ! curl -sS --max-time 8 --cacert /tmp/otra.crt https://$2/ >/dev/null 2>&1"' _ $P-client $DOMAIN
check "HTTP plano nunca sirve el ERP: solo redirige a HTTPS (Caddy escucha 80 solo dentro de la red Docker)" bash -c 'o=$(docker exec $1 curl -sS --max-time 6 -o /dev/null -w "%{http_code} %{redirect_url}" http://$2/ 2>/dev/null); [[ "$o" == 30[78]\ https://* ]] && ! docker exec $1 curl -sS --max-time 6 http://$2/ 2>/dev/null | grep -q ERP-OK' _ $P-client $DOMAIN
check "docker-compose.yml publica SOLO 443 tcp/udp de Caddy (80 no se publica, ni 8080 ni 1433)" bash -c 'p=$(grep -E "^[[:space:]]+- \"[^\"]*:[0-9]+(/udp)?\"" "$1/docker-compose.yml" | grep -v "BACKUP\|/backups"); [[ $(wc -l <<<"$p") == 2 ]] && [[ $(grep -c ":443\"\|:443/udp\"" <<<"$p") == 2 ]]' _ "$REPO"
check "un nombre distinto del configurado no obtiene certificado valido" bash -c '! docker exec $1 curl -sS --max-time 6 --cacert /tmp/ca.crt --resolve otro.local:443:$(docker inspect $2 --format "{{(index .NetworkSettings.Networks \"$3\").IPAddress}}") https://otro.local/ >/dev/null 2>&1' _ $P-client $P-caddy $P-net

echo "== Proxy =="
check "una peticion con X-Forwarded-For forjado atraviesa Caddy sin error (la confianza en el reenvio se prueba en Program.cs, no aqui)" bash -c 'docker exec $1 curl -sS --max-time 8 --cacert /tmp/ca.crt -H "X-Forwarded-For: 6.6.6.6" https://$2/ 2>/dev/null | grep -q ERP-OK' _ $P-client $DOMAIN

echo
echo "caddy tls tests: $PASS PASS, $FAIL FAIL"
(( FAIL == 0 ))
