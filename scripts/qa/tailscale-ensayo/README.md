# Tailscale de ensayo con Headscale (no toca ninguna tailnet real)

Valida **el comportamiento de red** de la política de mínimo privilegio (solo `tcp/443` hacia `tag:erp`) con un servidor de control **propio y
descartable** ([Headscale](https://github.com/juanfont/headscale) v0.26.1) y clientes oficiales de Tailscale (v1.84.3). No usa ni modifica
ninguna cuenta/tailnet de Tailscale: los nodos de ensayo se registran **solo** en el Headscale local (`--login-server`).

**Qué NO prueba** (sigue exigiendo la tailnet real): la consola de Tailscale (aplicar `deploy/tailscale/policy.hujson`, sus `tests`), expiración de
clave deshabilitada, *Device approval*, *Node sharing*, SSO/identidades reales, MagicDNS, CGNAT/datos móviles.
Headscale usa `acls` (no `grants`); `policy-ensayo.hujson` es el equivalente semántico del grant real.

Pasos (servidor ERP con Docker + `tailscaled` instalado y SIN autenticar; clientes en otro host con Docker):

```bash
# 1. Headscale en el servidor (puertos 8085/tcp y 3478/udp; solo ensayo, retirarlo al terminar)
sed 's/__SERVER_IP__/<IP-LAN-del-servidor>/g' headscale-config.yaml.tmpl > etc/config.yaml ; cp policy-ensayo.hujson etc/policy.hujson
docker run -d --name bury-hs -p 8085:8080 -p 3478:3478/udp -v $PWD/etc:/etc/headscale -v $PWD/data:/var/lib/headscale headscale/headscale:v0.26.1 serve
for u in servidor persona1 persona2 persona3; do docker exec bury-hs headscale users create $u; done
# 2. Nodo servidor con la etiqueta (la clave de pre-autenticacion lleva tag:erp; el dueño de la etiqueta es el usuario `servidor`)
KS=$(docker exec bury-hs headscale preauthkeys create --user 1 --expiration 2h --tags tag:erp | tail -1)
sudo tailscale up --login-server=http://<IP>:8085 --authkey=$KS --hostname=tbp --advertise-tags=tag:erp --accept-dns=false --accept-routes=false --ssh=false
sudo ufw allow from <LAN> to any port 41641 proto udp      # WireGuard directo (opcional: sin esto usa el DERP embebido)
# 3. Clientes (usuarios 2..4). IMPORTANTE: TS_USERSPACE=false; en modo userspace (por defecto de la imagen) no hay interfaz TUN y
#    los otros procesos del contenedor NO salen por la tailnet (las pruebas "bloqueado" serian falsos positivos)
docker run -d --name bury-ts-p1 --cap-add NET_ADMIN --cap-add NET_RAW --device /dev/net/tun -e TS_USERSPACE=false -e TS_AUTHKEY=<clave-usuario-2> \
   -e TS_HOSTNAME=persona1 -e TS_EXTRA_ARGS="--login-server=http://<IP>:8085 --accept-dns=false" -e TS_STATE_DIR=/var/lib/tailscale tailscale/tailscale:v1.84.3
# 4. Matriz (control positivo + negativos) desde el netns de cada cliente
docker run --rm --network container:bury-ts-p1 -e SELF=<ip-tailnet-del-cliente> -v caddy-root-ca.crt:/ca.crt:ro -v $PWD/matriz-acceso.sh:/m.sh:ro alpine:3 sh /m.sh
# 5. Revocacion individual: quitar "persona2@" del grupo en policy.hujson y `docker kill -s HUP bury-hs`; repetir el paso 4
# 6. Retiro: docker rm -f bury-hs bury-ts-*; sudo tailscale logout; sudo ufw delete allow ... 41641/udp; rm -rf etc data
```

Requisito del filtro del servidor (`bury-firewall.sh`): `ALLOW_FROM` debe incluir `100.64.0.0/10@tailscale0`. Resultado de la corrida: `docs/p04-p08-matriz-criterios-vm-simulada.md`.
