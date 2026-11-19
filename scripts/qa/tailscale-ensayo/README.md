# Tailscale de ensayo con Headscale (no toca ninguna tailnet real)

Valida **el comportamiento de red** de la política de mínimo privilegio con un servidor de control **propio y descartable**
([Headscale](https://github.com/juanfont/headscale) v0.26.1) y clientes oficiales de Tailscale (v1.84.3). No usa ni modifica ninguna cuenta/tailnet
de Tailscale: los nodos de ensayo se registran **solo** en el Headscale local (`--login-server`).

Roles (perfil `linux-vm` de `deploy/tailscale/policy.hujson`): **usuario remoto** = `443`; **administrador remoto** = `443` + `22` (SSH, un único
puerto de administración); **intruso** = sin grupo, nada. Headscale usa `acls` (no `grants`); `policy-ensayo.hujson` es su equivalente, explícito por protocolo.
En el ensayo solo se midió TCP (HTTPS y banner SSH); UDP/443 e ICMP figuran en la política pero no se midieron.

**Qué NO prueba** (sigue exigiendo la tailnet real): la consola de Tailscale (aplicar `policy.hujson`, sus `tests`), expiración de clave deshabilitada,
*Device approval*, *Node sharing*, SSO/identidades reales, MagicDNS, CGNAT/datos móviles.

## Requisitos

- **Servidor** (Linux con Docker, ERP instalado, `tailscaled` instalado y SIN autenticar) con `ALLOW_FROM` de `bury-firewall.sh` incluyendo `100.64.0.0/10@tailscale0`
  (lo hace `server-linux-bootstrap.sh --allow-from …,100.64.0.0/10`). Checkout de este repositorio en `$REPO`; `$CA` = ruta del archivo `caddy-root-ca.crt` (CA pública de Caddy).
- **Clientes**: otro host con Docker (en Windows, Docker Desktop). Ningún cliente puede ser el servidor.
- Usar **nombres de contenedor literales** y no borrar directorios por script: el retiro (paso 8) es manual.

## Pasos

```bash
# --- en el SERVIDOR ---
export REPO=~/theburyproject1 SERVER_IP=<IP-LAN-del-servidor> LAN=<LAN-CIDR>
T=$REPO/scripts/qa/tailscale-ensayo
# 1. Headscale (puertos 8085/tcp y 3478/udp; solo ensayo). ~/hs-ensayo NO debe existir.
mkdir ~/hs-ensayo && mkdir ~/hs-ensayo/etc ~/hs-ensayo/data
sed "s/__SERVER_IP__/$SERVER_IP/g" $T/headscale-config.yaml.tmpl > ~/hs-ensayo/etc/config.yaml ; cp $T/policy-ensayo.hujson ~/hs-ensayo/etc/policy.hujson
docker run -d --name bury-hs -p 8085:8080 -p 3478:3478/udp -v ~/hs-ensayo/etc:/etc/headscale -v ~/hs-ensayo/data:/var/lib/headscale headscale/headscale:v0.26.1 serve
for u in servidor usuario1 admin1 intruso1; do docker exec bury-hs headscale users create $u; done      # ids 1..4
# 2. Nodo servidor con la etiqueta (clave de pre-autenticación con tag:erp; dueño de la etiqueta: el usuario `servidor`)
KS=$(docker exec bury-hs headscale preauthkeys create --user 1 --expiration 2h --tags tag:erp | tail -1)
sudo tailscale up --login-server=http://$SERVER_IP:8085 --authkey=$KS --hostname=tbp --advertise-tags=tag:erp --accept-dns=false --accept-routes=false --ssh=false --netfilter-mode=nodivert
sudo ufw allow from $LAN to any port 41641 proto udp      # WireGuard directo: con nodivert ya no lo acepta tailscaled solo
# 3. Claves de los clientes (usuarios 2, 3, 4 = usuario1, admin1, intruso1): guardarlas para el paso 4
for n in 2 3 4; do echo "usuario-id $n: $(docker exec bury-hs headscale preauthkeys create --user $n --expiration 2h | tail -1)"; done

# --- en el host de los CLIENTES (una orden por cliente; sustituir <clave-...>) ---
# TS_USERSPACE=false es imprescindible: en modo userspace (por defecto de la imagen) no hay interfaz TUN y los demás procesos del contenedor
# NO salen por la tailnet (los "bloqueado" serían falsos positivos).
docker run -d --name bury-ts-usuario1 --cap-add NET_ADMIN --cap-add NET_RAW --device /dev/net/tun -e TS_USERSPACE=false -e TS_AUTHKEY=<clave-usuario-2> \
   -e TS_HOSTNAME=usuario1 -e TS_EXTRA_ARGS="--login-server=http://<SERVER_IP>:8085 --accept-dns=false" -e TS_STATE_DIR=/var/lib/tailscale tailscale/tailscale:v1.84.3
#   idem bury-ts-admin1 (<clave-usuario-3>, TS_HOSTNAME=admin1) y bury-ts-intruso1 (<clave-usuario-4>, TS_HOSTNAME=intruso1)

# --- de nuevo en el SERVIDOR ---
# 4. IP de tailnet del administrador y su regla de host (SOLO su /32, atada a tailscale0). Sin --netfilter-mode=nodivert esta regla NO tiene efecto.
ADMIN_IP=$(docker exec bury-hs headscale nodes list -o json | jq -r '.[]|select(.name=="admin1")|.ip_addresses[0]')
sudo ufw allow in on tailscale0 from $ADMIN_IP to any port 22 proto tcp

# --- matriz, desde el host de los clientes (un cliente por vez; SELF = su IP de tailnet, PEERS = las de los demás) ---
# 5. docker run --rm --network container:bury-ts-usuario1 -e SELF=<ip> -e PEERS="<ip-usuario1> <ip-admin1> <ip-intruso1>" \
#        -v "<ruta-al-checkout>/scripts/qa/tailscale-ensayo/matriz-acceso.sh:/m.sh:ro" -v "<ruta-a-la-CA>/caddy-root-ca.crt:/ca.crt:ro" alpine:3 sh /m.sh
# 6. Revocación: editar los grupos de ~/hs-ensayo/etc/policy.hujson en el servidor y `docker kill -s HUP bury-hs`; repetir el paso 5.
```

## 8. Retiro (a mano, uno por uno; no se automatiza el borrado)

```bash
# clientes:  docker stop bury-ts-usuario1 bury-ts-admin1 bury-ts-intruso1 ; docker rm bury-ts-usuario1 bury-ts-admin1 bury-ts-intruso1
# servidor:  docker stop bury-hs ; docker rm bury-hs ; sudo tailscale logout ; sudo tailscale set --netfilter-mode=on
#            sudo ufw delete allow in on tailscale0 from <ADMIN_IP> to any port 22 proto tcp
#            sudo ufw delete allow from <LAN> to any port 41641 proto udp
#            y borrar a mano el directorio ~/hs-ensayo (config, política y base de Headscale de ensayo)
```

Hallazgo que este ensayo destapó: **sin `--netfilter-mode=nodivert`** `tailscaled` pone `ts-input`/`ts-forward` antes que `ufw` y `DOCKER-USER`, así que el
firewall del host no ve el tráfico de la tailnet. Resultados y contadores: `docs/p05-tailscale-acceso-remoto.md` §9b.
