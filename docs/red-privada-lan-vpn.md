# Despliegue privado LAN/VPN (Caddy con CA interna)

Instalación para ~6 clientes en LAN o VPN. **El ERP no recibe conexiones desde Internet**: sin DNS público,
sin port-forwarding, sin Let's Encrypt/ACME. La VPN se instala y configura aparte.

**Servidor de referencia: Ubuntu con Docker Engine.** El 443 se filtra con `bury-firewall` (§6), **no** con `ufw`. VPN elegida: Tailscale
([p05-tailscale-acceso-remoto.md](p05-tailscale-acceso-remoto.md)); permisos por perfil: [p05-permisos-acceso-remoto.md](p05-permisos-acceso-remoto.md).

```text
Clientes LAN/VPN ── https://tbp ──> Caddy (443) ──> app:8080 ──> db:1433
```

- Caddy usa `tls internal` (Caddyfile): emite el certificado para `ERP_DOMAIN` con su **CA interna**; no necesita Internet ni el puerto 80.
- Único puerto publicado en producción: **443 TCP y 443 UDP (HTTP/3)**. `app` (8080) y `db` (1433) no definen `ports:`; solo son
  alcanzables por la red `bury-net`. Consecuencia: `http://tbp` no responde; hay que escribir `https://tbp`.
- `ERP_DOMAIN=tbp` en el `.env` privado (no versionado). El preflight solo exige que exista y no esté vacío; acepta hostnames sin FQDN.
- Salida a Internet del host: solo para apt/updates, GitHub, registro Docker, DNS y NTP. MercadoLibre desactivado. Backups locales por ahora.

## 1. Resolución del nombre `tbp` desde los clientes

**Opción simple (hasta ~6 PCs):** en cada PC Windows, editor como Administrador, agregar al final de
`C:\Windows\System32\drivers\etc\hosts`:

```text
<IP-LAN-DEL-SERVIDOR> tbp
```

(reemplazar por la IP real y reservada/estática del servidor; se define al instalar el host). Para clientes por VPN, usar la IP a la
que el servidor sea alcanzable desde la VPN.

**Alternativa:** registro DNS local `tbp` → IP del servidor en el router o DNS del local, si lo permite. No se exige un servidor DNS propio.

## 2. Confianza TLS (CA raíz de Caddy)

> Para equipos Windows usar `scripts/install/Install-ErpCa.ps1` (exige la huella SHA-256 entregada por otro canal y valida que sea una CA raíz sin clave privada) en lugar de `Import-Certificate` a ciegas. Ver [p05-tailscale-acceso-remoto.md](p05-tailscale-acceso-remoto.md) §5.

Tras el primer arranque, Caddy genera su CA en el volumen `caddy-data`. Solo la **CA raíz pública** se distribuye; **nunca** la clave
privada (`root.key`, `intermediate.key`) ni se versiona nada de `caddy-data`.

En el servidor, extraer únicamente el certificado público:

```bash
docker compose -p theburyproject cp caddy:/data/caddy/pki/authorities/local/root.crt ./caddy-root-ca.crt
```

(equivalente: `docker compose exec caddy cat /data/caddy/pki/authorities/local/root.crt > caddy-root-ca.crt`). Verificar que es un certificado
público: `openssl x509 -in caddy-root-ca.crt -noout -subject -dates`.

**Windows (cada cliente), PowerShell como Administrador:**

```powershell
Import-Certificate -FilePath .\caddy-root-ca.crt -CertStoreLocation Cert:\LocalMachine\Root
```

o gráficamente: doble clic en el `.crt` → Instalar certificado → Equipo local → "Colocar todos los certificados en el siguiente almacén" →
**Entidades de certificación raíz de confianza**. Firefox usa su propio almacén (Configuración → Certificados → Importar, o
`security.enterprise_roots.enabled=true`). Reiniciar el navegador.

La CA raíz dura ~10 años; el certificado de `tbp` se renueva solo. Si se pierde `caddy-data`, Caddy crea una CA nueva y hay que redistribuirla
(por eso `caddy-data` está en el backup de archivos).

### Verificación

```bash
curl --cacert caddy-root-ca.crt https://tbp/health/live    # 200
curl --cacert caddy-root-ca.crt https://tbp/health/ready   # 200
openssl s_client -connect tbp:443 -servername tbp </dev/null 2>/dev/null | openssl x509 -noout -subject -issuer -ext subjectAltName
```

Desde un navegador que confía en la CA: `https://tbp/health/live` y `https://tbp/health/ready` sin advertencias. (`curl -k` solo como diagnóstico.)

## 3. Portabilidad del host

| Portable (Docker, idéntico en cualquier host) | Específico del host |
|---|---|
| `db` (SQL Server Express 2022, contenedor propio), `db-init`, `migrate`, `app`, `caddy` | Instalación de Docker |
| Volúmenes (`bury-sqldata`, `bury-keys`, `bury-uploads`, `bury-appdata`, `caddy-data`), red `bury-net` | Firewall (permitir 443 solo desde LAN/VPN) |
| `scripts/deploy/deploy.sh` y `rollback.sh` | Autoarranque de Docker tras reinicio |
| Imagen con tag inmutable | Ubicación física de `BACKUP_DIR` |
| | Programador de tareas (cron / Task Scheduler) |
| | Red LAN/VPN, IP fija del servidor |

**A. Ubuntu + Docker Engine (referencia).** `scripts/install/server-linux-bootstrap.sh` instala Docker, el filtro del 443 y, con `--admin-from`,
`ufw` para SSH; backups con `scripts/backup/install-schedule.sh --install` (cron). Pasos: [instalacion-servidor.md](instalacion-servidor.md) §4.
**Importante:** Docker publica los puertos con sus propias reglas de iptables: una regla `ufw allow from ...` **no** restringe el 443 publicado por
Caddy, y `ufw default deny incoming` tampoco lo cierra.

**B. Windows 10/11 + Docker Desktop/WSL2.** Docker Desktop con backend WSL2, "Start Docker Desktop when you sign in" (o servicio equivalente)
y ejecutar los scripts `.sh` desde WSL2 o Git Bash sobre el mismo repo; regla de Windows Defender Firewall solo para 443 entrante desde la
LAN/VPN (**ojo**: una regla Allow del programa `com.docker.backend.exe` ya existente anula ese filtro; ver §6); backups con Task Scheduler invocando los mismos scripts (`wsl.exe -e ...`); en WSL2 el puerto publicado debe ser alcanzable desde la
LAN (verificar el reenvío de Docker Desktop; en modo mirrored networking suele funcionar directo). `install-schedule.sh` genera cron y **no**
está adaptado a Task Scheduler todavía. Ambos caminos usan el mismo stack Docker; sin cambios de scripts funcionales.

## 4. Actualización y rollback (sin cambios conceptuales)

Nueva release: obtener el tag Git → construir/cargar la imagen con **tag inmutable nuevo** (`ERP_IMAGE=theburyproject/erp:<tag>`) →
`scripts/deploy/deploy.sh` (preflight → backup → db-init → migrate → app → health → smoke) → queda registrada la release y la imagen anterior
se conserva. Detalle: `docs/deploy-rollback.md`.

Rollback: `scripts/deploy/rollback.sh --to-image theburyproject/erp:<tag-anterior> --confirm-compatible`.

Nunca: copiar archivos sobre el contenedor, reutilizar `:latest` ni un tag anterior para otro contenido, borrar volúmenes, `docker compose down -v`.
SQL, uploads, `App_Data` y las claves Data Protection viven en volúmenes y sobreviven a deploy y rollback.

## 5. Base de datos

Sin cambios: SQL Server Express 2022 en contenedor `db` separado, datos en `bury-sqldata`, solo en `bury-net`, puerto 1433 **no** publicado
en producción. No se mueve SQL a otra máquina ni se instala en el host.

Instalación automatizada del servidor (Windows o Linux): [instalacion-servidor.md](instalacion-servidor.md).

## 6. Checklist ejecutable: IP de publicación, firewall y prueba desde un cliente VPN

Verificado el 2026-10-29 (Docker Desktop/WSL2, pila de prueba en el puerto 6443, 4 interfaces del host): con `ERP_BIND_ADDRESS=127.0.0.1` el 443 publicado
responde **solo** en `127.0.0.1` (la IP de la Wi-Fi y las de otras interfaces dan «conexión rechazada»; único listener `127.0.0.1:<puerto>`);
con la IP de la Wi-Fi responde solo ahí (y `127.0.0.1` deja de responder); con `0.0.0.0` responde en todas. `db` (1433) y `app` (8080) no se publican.

1. **Elegir la IP.** Ver las IP del servidor y elegir la de la interfaz por la que llegan los clientes:
   Windows `Get-NetIPAddress -AddressFamily IPv4 | ft InterfaceAlias,IPAddress`; Linux `ip -4 -br addr`.
   - Solo LAN: la IP **reservada** (DHCP estático en el router) del servidor, p. ej. `192.168.1.50`.
   - LAN y VPN a la vez: `ERP_BIND_ADDRESS` admite **una sola** IP. Opciones: `0.0.0.0` + filtro de firewall (punto 3), o publicar solo en la IP de la VPN
     y que también los equipos del local entren por la VPN.
   - Si el cliente VPN llega a la IP LAN del servidor mediante una ruta (servidor en la LAN, VPN en el router), usar la IP LAN.
2. **Fijarla** en `.env`: `ERP_BIND_ADDRESS=<IP>` y recrear Caddy: `docker compose up -d --no-deps caddy`. Comprobar:
   Windows `Get-NetTCPConnection -LocalPort 443 -State Listen | ft LocalAddress`; Linux `ss -ltn 'sport = :443'` (debe mostrar `<IP>:443`, no `0.0.0.0`).
   **Trampa verificada**: si esa IP no existe en el momento de arrancar (interfaz VPN todavía caída, IP del DHCP que cambió) Caddy **no arranca**
   (`can't bind on the specified endpoint`; el contenedor queda en `Created` y `restart: unless-stopped` no lo reintenta). Por eso: IP reservada/estática y,
   si es la de una VPN, levantar los servicios con `ERP_IMAGE` definida ([operacion-servidor-ubuntu.md](operacion-servidor-ubuntu.md) §0) después de levantar la VPN (ver «Qué hace cada servicio al reiniciar» en `docs/instalacion-servidor.md` §3.3).
3. **Firewall del host.** En Ubuntu es el control que limita **qué redes** llegan al 443; `ERP_BIND_ADDRESS` solo elige **en qué IP** escucha:
   - **Ubuntu**: `ufw`/INPUT **no** filtran puertos publicados por Docker. El filtro es `scripts/install/bury-firewall.sh`, que
     `server-linux-bootstrap.sh` instala como `/usr/local/sbin/bury-firewall` con un servicio de systemd.

     | Qué hace | Detalle |
     |---|---|
     | Configuración | `/etc/bury/firewall.conf`: `ALLOW_FROM` (IPv4/CIDR, con `@interfaz` opcional) y `HTTPS_PORT` (igual a `ERP_HTTPS_PORT`) |
     | Reglas | Cadena `BURY-ERP` en `DOCKER-USER`: permite los orígenes listados y descarta el resto, TCP y UDP |
     | Alcance | Solo conexiones **entrantes** redirigidas a un contenedor (`--ctstate DNAT`); no afecta el HTTPS saliente |
     | Tailscale | El bootstrap ata los orígenes `100.64.0.0/10` a `tailscale0`, para que no se suplanten desde la LAN |
     | Persistencia | `bury-firewall.service` y un `ExecStartPost` de Docker |

     Cambiar orígenes: repetir el bootstrap con el `--allow-from` completo (reescribe `firewall.conf`), o editar el archivo y ejecutar
     `sudo bury-firewall apply`. Comprobar: `systemctl is-active bury-firewall && sudo bury-firewall status`. Vista previa sin cambios:
     `bury-firewall.sh print`.

     **Límites:** (1) si aplicar las reglas falla durante el arranque, Docker arranca igual y **el 443 queda abierto**: comprobar el estado tras cada
     reinicio o actualización de Docker; (2) entre el arranque de los contenedores y la aplicación de las reglas puede haber una ventana corta;
     (3) solo IPv4: `ERP_BIND_ADDRESS` debe ser IPv4 (el valor `0.0.0.0` no publica en IPv6; verificar con `ss -ltn`);
     (4) `bury-firewall remove` deja el 443 abierto. Una segunda instalación en el mismo equipo necesita otra cadena y otro puerto
     ([servidor-provisional-notebook.md](servidor-provisional-notebook.md) §2).

     No escribir reglas manuales de `iptables` en su lugar: una versión anterior de esta guía proponía reglas sin `--ctstate DNAT`, que también
     descartarían el tráfico HTTPS saliente de los contenedores.

     **Evidencia:** la prueba `scripts/install/test-firewall-isolation.sh` (Docker real y clientes en redes distintas) pasa 18/18 en CI. Cubre
     `bury-firewall.sh`, no la instalación del servicio por el bootstrap. [evidencias-instalacion.md](evidencias-instalacion.md).
   - **Windows + Docker Desktop**: el puerto lo abre un proceso de Windows (`com.docker.backend.exe`), así que Windows Defender Firewall sí aplica. Pero **una regla
     «Allow» de puerto desde la LAN (la que crea `install-windows.ps1`) no restringe nada si ya existe una regla Allow del programa `com.docker.backend.exe` sin
     límite de puerto/dirección** — en el equipo de prueba existía (perfil Público, cualquier puerto). Revisar:
     `Get-NetFirewallRule -Enabled True -Direction Inbound -Action Allow | ? DisplayName -match 'docker' | % { $_ | Get-NetFirewallApplicationFilter }`.
     Para restringir de verdad: `ERP_BIND_ADDRESS` y/o una regla **Block** (gana sobre Allow), p. ej.
     `New-NetFirewallRule -DisplayName 'TheBury block 443 Internet' -Direction Inbound -Action Block -Protocol TCP -LocalPort 443 -RemoteAddress Internet` (y UDP).
     Esa regla Block **no fue probada** con un cliente remoto; probarla antes de confiar en ella.
   - No abrir 443 en el router (sin port-forwarding) y no publicar 1433/8080 (el compose ya no lo hace).
4. **Probar desde un cliente VPN** (con la VPN conectada y `IP tbp` en `hosts`, ver §1):
   - `Test-NetConnection <IP-servidor> -Port 443` → `TcpTestSucceeded : True`; `-Port 1433` y `-Port 8080` → `False`.
   - `curl --cacert caddy-root-ca.crt https://tbp/health/ready` → `200` (sin la CA, error de certificado: esperado).
   - Navegador: `https://tbp`, login, cerrar sesión y volver a entrar; candado sin advertencias (CA instalada, §2).
   - Con la **VPN desconectada** (p. ej. datos móviles): `Test-NetConnection <IP-servidor> -Port 443` debe **fallar**. Si responde, el servicio está expuesto: revisar
     `ERP_BIND_ADDRESS`, el router y el firewall antes de seguir.
5. **Estado**: lo anterior se ejecutó contra el stack de prueba local; la verificación desde un cliente remoto real (VPN/LAN) sigue **pendiente del usuario** (requiere el servidor y la VPN definitivos).

## Pendiente (requiere el servidor final)

IP reservada, `hosts`/DNS, extracción e instalación de la CA en clientes, reglas de firewall, autoarranque, scheduler de backups y VPN.
