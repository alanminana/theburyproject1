# Instalación del servidor

> **Plataforma de referencia (decisión del propietario, 2026-11-27): Ubuntu con Docker Engine. Seguir la [§4](#4-ubuntu-con-docker-engine-referencia).**
> Windows es el entorno principal de **desarrollo** ([desarrollo-local.md](desarrollo-local.md)). La [§3](#3-windows-1011-con-docker-desktop-alternativa) (Windows 10/11 con
> Docker Desktop) queda como alternativa; [Windows Server](instalacion-windows-server.md) está archivada.

Relacionado: [operación, rollback y recuperación](operacion-servidor-ubuntu.md) · [red y firewall](red-privada-lan-vpn.md) ·
[sincronización de entornos](sincronizacion-entornos.md) · [evidencias y límites](evidencias-instalacion.md).

Guía para instalar TheBuryProject en un servidor propio, accesible solo por LAN/VPN. El ERP corre en contenedores Docker (Linux) detrás de Caddy; el
mismo instalador (`install.sh`) sirve para las dos opciones y solo cambia cómo se prepara el anfitrión.

```text
Clientes LAN/VPN ── https://tbp ──> Caddy (443) ──> app:8080 ──> db:1433 (SQL Server Express)
```

Solo se publica el puerto **443**. `app` y `db` no son alcanzables desde fuera. Detalle de red y certificados: [red-privada-lan-vpn.md](red-privada-lan-vpn.md).

## 1. Qué automatiza el instalador

| Script | Dónde corre | Qué hace |
|---|---|---|
| `scripts/install/install.sh` | Linux o WSL2 | Genera `.env` con secretos aleatorios, construye la imagen con tag inmutable, despliega (`deploy.sh`) y extrae la CA de Caddy |
| `scripts/install/install-windows.ps1` | PowerShell **como administrador** | Verifica WSL2/Docker, ejecuta `install.sh` dentro de WSL, y configura firewall, tareas de backup, arranque y energía |
| `scripts/install/test-install.sh` | CI / desarrollo | Prueba el generador de `.env` sin Docker (corre en CI) |

Todo es **idempotente**: se puede volver a ejecutar sin romper nada. Un `.env` existente nunca se pisa.

## 2. Requisitos

- 8 GB de RAM o más (SQL Server Express usa hasta 1 GB, más Docker y la app), 20 GB de disco libre como mínimo.
- IP fija para el servidor: reserva DHCP en el router.
- Salida a Internet del **servidor** (descargar imágenes de Docker y el código). El ERP en sí no necesita Internet.
- Licencia de AutoMapper: por defecto **Community** (gratuita, sin clave). Ver [despliegue-produccion.md](despliegue-produccion.md) §9.

## 3. Windows 10/11 con Docker Desktop (alternativa)

### 3.1 Preparar la máquina (una vez)

1. Instalar WSL2 con Ubuntu: en PowerShell como administrador, `wsl --install -d Ubuntu` y reiniciar. Crear el usuario de Ubuntu cuando lo pida.
2. Instalar **Docker Desktop** con el backend WSL2. En *Settings > General* activar **Start Docker Desktop when you sign in**.
   En *Settings > Resources > WSL integration* activar la integración con **Ubuntu**.
3. (Recomendado) Limitar la memoria de WSL2 creando `C:\Users\<usuario>\.wslconfig`:

   ```ini
   [wsl2]
   memory=6GB
   swap=2GB
   ```

   y aplicar con `wsl --shutdown`.
4. Clonar el repositorio **dentro de WSL** (no en `C:\` ni en `/mnt/c`: los permisos y los saltos de línea se rompen):

   ```powershell
   wsl -d Ubuntu -- git clone https://github.com/alanminana/theburyproject1.git ~/theburyproject1
   ```

### 3.2 Instalar

PowerShell **como administrador**, desde el repositorio (clonado en WSL, accesible como `\\wsl.localhost\Ubuntu\home\<usuario>\theburyproject1`):

```powershell
cd \\wsl.localhost\Ubuntu\home\<usuario>\theburyproject1\scripts\install
powershell -ExecutionPolicy Bypass -File .\install-windows.ps1 -AdminEmail correo@dominio.com -AllowFrom LocalSubnet,10.8.0.0/24
```

- Si no se pasa `-Distro`, el script **lista las distros de WSL instaladas y pregunta cuál usar** (no siempre se llama `Ubuntu`). También se puede indicar directo: `-Distro Ubuntu-24.04`. Si el repositorio no está en `~/theburyproject1` de esa distro, pregunta la ruta (o usar `-RepoPath`).
- Primero probar con `-DryRun`: muestra todo lo que haría sin modificar nada.
- `-AllowFrom` define desde dónde se acepta el 443: `LocalSubnet` (la LAN) y la subred de la VPN (ejemplo WireGuard `10.8.0.0/24`).
- Parámetros útiles: `-Domain tbp`, `-BackupDir /srv/bury-backups`, `-FullBackupAt 02:30`, `-HostsIp <IP>` (agrega `IP tbp` a `hosts`, para probar en el propio servidor), `-TrustCaHere` (instala la CA en este equipo).
- Al terminar, el script de instalación muestra **una sola vez** la contraseña inicial del administrador. Anotarla.

El script hace, en orden: verificación de WSL2/Docker → `install.sh` en WSL → regla de Firewall (443 TCP/UDP, solo desde `-AllowFrom`) → tareas programadas
(`TheBury Backup Full` diaria, `TheBury Backup Log` cada 15 min, `TheBury Restore Test` semanal, `TheBury Docker Start` al iniciar sesión) → energía (sin suspender ni hibernar con corriente, tapa cerrada sin efecto).

Para quitar tareas y regla de firewall: `install-windows.ps1 -Uninstall` (no toca datos, volúmenes ni backups).

### 3.3 Arranque automático tras un corte de luz

Docker Desktop corre dentro de la **sesión del usuario**, y las tareas de backup usan esa misma sesión. Para que todo vuelva solo tras un reinicio hay que habilitar el **inicio de sesión automático** de Windows:

1. `netplwiz` → desmarcar *Los usuarios deben escribir su nombre y contraseña*. (Guarda la contraseña en el equipo: usar una cuenta dedicada sin privilegios innecesarios y proteger físicamente el servidor.)
2. En el BIOS/UEFI, activar *Restore on AC Power Loss* (o equivalente) para que el equipo encienda solo al volver la luz.
3. Fijar el horario de reinicio de Windows Update fuera del horario de uso.

Los contenedores tienen `restart: unless-stopped`: vuelven con Docker. Probarlo reiniciando el equipo y abriendo `https://tbp` sin tocar nada.

#### Qué hace cada servicio al reiniciar el equipo (verificado el 2026-10-29 en un stack aislado)

- `db`, `app` y `caddy` tienen `restart: unless-stopped`; `db-init` y `migrate` son one-shot (`restart: "no"`) y **no** se vuelven a ejecutar: es lo esperado.
- Al volver el daemon, `depends_on` **no** ordena el arranque (los contenedores se levantan en paralelo). Se probó la caída simultánea de `db`, `app` y `caddy`
  (proceso terminado desde dentro del contenedor, sin pasar por la API de Docker): los tres volvieron solos, `app` reintentó la conexión a SQL y `/health/ready` volvió a 200
  en ≈ 10-20 s, con los datos intactos. `docker compose down` + `up -d` (sin `-v`) conserva datos, claves y sesiones.
- **No vuelven solos** los contenedores detenidos a propósito: `docker stop`/`docker kill`/`docker compose stop` los marcan «manualmente detenidos» y `unless-stopped` los respeta
  (reproducido con `docker kill`: los tres quedaron en `Exited (137)` sin reiniciarse). Caso real: un reinicio justo durante `backup.sh all`
  (que detiene `app` ~15 s) deja `app` apagada. Tampoco arranca `caddy` si `ERP_BIND_ADDRESS` apunta a una IP que aún no existe (`docs/red-privada-lan-vpn.md` §6).
  Por eso, **tras cada reinicio** (o como tarea al iniciar sesión / unidad systemd `After=docker.service`) conviene ejecutar `docker compose up -d` y comprobar:

  ```bash
  cd <repo> && export ERP_IMAGE="$(cat .deploy-state/theburyproject/current-image)" \
    && docker compose up -d --wait && docker compose ps && curl --cacert caddy-root-ca.crt --resolve tbp:443:127.0.0.1 -fsS https://tbp/health/ready
  ```

  **Nunca ejecutar `docker compose up` sin `ERP_IMAGE`**: resolvería `theburyproject/erp:local` ([operacion-servidor-ubuntu.md](operacion-servidor-ubuntu.md) §0).

  Windows: agregar esa orden a la tarea `TheBury Docker Start`, después de que Docker Desktop esté listo
  (con WSL integrado: `wsl.exe -d <distro> -- bash -lc 'cd ~/theburyproject1 && ERP_IMAGE=$(cat .deploy-state/theburyproject/current-image) docker compose up -d'`).
  **Esta ampliación de la tarea no está automatizada ni probada en Windows**; es un paso manual hasta validarlo en el servidor definitivo.
- No se reinició el daemon de Docker/Docker Desktop (afectaría a otros contenedores del equipo de pruebas): el ciclo completo
  «apagar el equipo → encender → inicio de sesión automático → Docker Desktop → contenedores» queda **pendiente de ensayo en el servidor real**.

## 4. Ubuntu con Docker Engine (referencia)

Ubuntu 22.04 y 24.04 son las versiones para las que está escrito el bootstrap; la instalación física más reciente se midió en 26.04
([evidencias](evidencias-instalacion.md)). Antes de empezar: IP fija o reservada, salida a Internet, una cuenta de administración con `sudo` y
**8 GB de RAM, más swap**. La instalación compila la imagen **en el servidor**: con 4,5 GB y sin swap, el compilador de .NET agotó la memoria y la
máquina dejó de responder hasta apagarla (medido el 2026-11-27); con 4,5 GB y 4 GB de swap terminó bien. No se midió con 8 GB sin swap.
Comprobar con `free -h`. Si `Swap` es 0 (habitual en imágenes cloud):

```bash
sudo fallocate -l 4G /swap.img && sudo chmod 600 /swap.img && sudo mkswap /swap.img && sudo swapon /swap.img
echo '/swap.img none swap sw 0 0' | sudo tee -a /etc/fstab
```

| Dato a decidir | Ejemplo | Nota |
|---|---|---|
| Redes que usan el ERP (`--allow-from`) | `192.168.1.0/24` | LAN y/o VPN. Para Tailscale, `100.64.0.0/10` |
| Equipo que administra por SSH (`--admin-from`) | `192.168.1.10/32` | Un equipo concreto, nunca la LAN entera |
| Nombre del ERP | `tbp` | Lo resuelven los clientes |
| Carpeta de respaldos | `/srv/bury-backups` | Idealmente en otro disco; no basta como única copia |
| Criterio fiscal V2 (`--fiscal-v2-desde`) | sin indicar | Solo con la fecha decidida por el propietario: [puesta-en-marcha-p07-p02.md](puesta-en-marcha-p07-p02.md) |

### 4.1 Descargar el código

```bash
sudo apt-get update && sudo apt-get install -y git
git clone https://github.com/alanminana/theburyproject1.git ~/theburyproject1 && cd ~/theburyproject1
```

### 4.2 Preparar el anfitrión: Docker y firewall

```bash
sudo scripts/install/server-linux-bootstrap.sh --user "$USER" --allow-from 192.168.1.0/24 \
     --admin-from 192.168.1.10/32 --backup-dir /srv/bury-backups --dry-run
```

Revisar la salida y repetir **sin** `--dry-run`. El script instala Docker Engine y Compose del repositorio oficial, crea la carpeta de respaldos, instala
`bury-firewall` (filtro del 443 en `DOCKER-USER`, con servicio de systemd) y, **solo si se indicó `--admin-from`**, activa `ufw` para limitar SSH.
Agregar `--tailscale` instala Tailscale sin autenticarlo ([p05-tailscale-acceso-remoto.md](p05-tailscale-acceso-remoto.md)).

> **`ufw` no protege el 443 del ERP.** Docker publica puertos con reglas propias que las de `ufw` no filtran. El filtro es `bury-firewall`. Si se omite el
> bootstrap o se instala Docker a mano sin él, el 443 queda abierto a toda red que alcance el servidor.
>
> El firewall **falla cerrado**: si no se puede aplicar, Docker no arranca. El bootstrap instala además `bury-firewall-watch`, que repone o
> corta si las reglas se pierden con Docker en marcha ([red-privada-lan-vpn.md](red-privada-lan-vpn.md) §6).

Cerrar la sesión y abrir otra, para tomar el grupo `docker` (**equivale a root**: usarlo solo en la cuenta de administración).

### 4.3 Instalar el ERP

```bash
cd ~/theburyproject1
bash scripts/install/install.sh --admin-email correo@dominio.com --domain tbp \
     --backup-dir /srv/bury-backups --schedule
```

Genera `.env` con secretos aleatorios (no pisa uno existente), construye la imagen `AAAAMMDD-<commit>`, despliega, levanta Caddy, deja
`caddy-root-ca.crt` en la raíz del repositorio y, con `--schedule`, programa los respaldos. La contraseña inicial del administrador se muestra
**una sola vez**. `install.sh --no-deploy` solo deja listos `.env` y la carpeta de respaldos: **no** construye la imagen.

Si el proyecto Compose ya tiene volúmenes y no hay `.env`, el instalador se niega: para recuperar una instalación, seguir
[operacion-servidor-ubuntu.md](operacion-servidor-ubuntu.md) §6.

### 4.4 Comprobar el firewall y el aislamiento

En el servidor:

```bash
systemctl is-active docker bury-firewall && sudo bury-firewall verify   # "verificado (BURY-ERP, puerto 443)"
sudo bury-firewall status                                          # cadena BURY-ERP: orígenes autorizados y DROP final
ss -ltn                                                            # del ERP solo 443; nada en 1433 ni 8080
```

Desde un equipo **autorizado**, el 443 responde. Desde uno **no autorizado** (otra red) no debe responder: esa prueba externa es la que demuestra
el filtro en esta red. Si el ERP usa otro puerto, `--https-port` del bootstrap debe coincidir con `ERP_HTTPS_PORT`.

### 4.5 Verificar la instalación

Primero, **solo en una instalación nueva**, cerrar el crédito personal. La base recién creada trae un cupo de $200.000 en el puntaje 0, y hasta
cerrarlo `verificar-servidor.sh` da `FAIL V07` ([cierre-credito-cupos-cero-20261122.md](cierre-credito-cupos-cero-20261122.md)). El script usa la
pantalla real con el usuario `admin` y su contraseña inicial del `.env`: ejecutarlo **antes** de cambiarla o quitarla (§6). No usarlo sobre una
instalación en operación.

```bash
python3 scripts/install/cerrar-credito-inicial.py --base https://tbp --ca caddy-root-ca.crt --resolve 127.0.0.1 --user admin --env-file .env
scripts/install/verificar-servidor.sh --project theburyproject --lan-cidr 192.168.1.0/24 --sudo-cmd "sudo"
scripts/backup/backup.sh all && scripts/backup/restore-test.sh
```

Esperado: ningún `FAIL`. Los `WARN` son pendientes del propietario (por ejemplo, sin copia externa).

### 4.6 Resguardar fuera del servidor

Hacerlo **el mismo día**: paquete de recuperación, passphrase en otro lugar y un destino de respaldos fuera del equipo
([operacion-servidor-ubuntu.md](operacion-servidor-ubuntu.md) §3). Sin esto, perder el servidor es perder los datos.

### 4.7 Reinicio

Reiniciar el equipo y comprobar, sin tocar nada, que `https://tbp` abre desde un cliente, que los tres servicios están sanos y que
`sudo bury-firewall verify` termina bien ([operacion-servidor-ubuntu.md](operacion-servidor-ubuntu.md) §7).

## 5. Clientes (cada PC)

1. **Nombre `tbp`.** Preferido: un registro DNS local `tbp` → IP del servidor en el router (si no lo permite, `nslookup tbp` falla: usar `hosts`). Alternativa por PC: agregar `IP-DEL-SERVIDOR tbp` en `C:\Windows\System32\drivers\etc\hosts`.
   Por VPN (WireGuard), usar la IP del servidor dentro de la VPN y poner `DNS = <IP del router>` en la configuración del cliente.
2. **Confiar en la CA.** El instalador deja `caddy-root-ca.crt` en la raíz del repositorio (solo el certificado público). En cada PC, PowerShell como administrador:

   ```powershell
   Import-Certificate -FilePath .\caddy-root-ca.crt -CertStoreLocation Cert:\LocalMachine\Root
   ```

   Firefox usa su propio almacén (importarlo allí o activar `security.enterprise_roots.enabled`). Reiniciar el navegador.
3. Abrir **`https://tbp`** completo (con `https://`; si se escribe solo `tbp` el navegador lo busca como texto).

## 6. Después de instalar

1. **Guardar el `.env` completo en el gestor de contraseñas** (nota segura de Bitwarden). Contiene todos los secretos y **no está en los backups**: sin él no se puede recuperar el sistema en otro servidor.
2. Entrar con el usuario inicial, **cambiar la contraseña** y comprobar un segundo login. Luego quitar `ADMIN_PASSWORD` del `.env` y ejecutar `docker compose -p theburyproject rm -f migrate`.
3. Dejar fuera del servidor los respaldos, el paquete de recuperación y su passphrase: [operacion-servidor-ubuntu.md](operacion-servidor-ubuntu.md) §3.
4. Crear la cuenta de soporte propia (SuperAdmin nominal) desde la pantalla de usuarios; no compartir la del cliente.

## 7. Actualizar a una versión nueva

Procedimiento completo, con respaldo previo, revisión de migraciones y vuelta atrás: [operacion-servidor-ubuntu.md](operacion-servidor-ubuntu.md) §2 y §5
(en Windows con Docker Desktop, los mismos pasos dentro de WSL). La actualización **aplica las migraciones nuevas**, y `rollback.sh` vuelve la
imagen, **no** la base. Detalle del despliegue: [deploy-rollback.md](deploy-rollback.md).

## 8. Backups: decisión actual

Por defecto la instalación usa **solo backups locales** (`BACKUP_REQUIRE_OFFSITE=false`): full diario, log de transacciones cada 15 min y prueba de restauración semanal, en `BACKUP_DIR`.
Esto cubre errores de aplicación y borrados accidentales, **no** la pérdida del servidor. Para reducir el riesgo:

- Dejar `BACKUP_DIR` dentro de WSL (en Linux, `/srv/bury-backups`). **No usar `/mnt/c` ni `/mnt/d`:** la subcarpeta `sql` debe pertenecer al usuario del contenedor de SQL Server (uid 10001) y los discos de Windows (NTFS) no lo permiten. Esa subcarpeta no se puede listar con el usuario normal del host: es lo esperado.
- **Copiar los backups a un disco externo periódicamente** y guardarlo en otro lugar. En Windows la carpeta se ve desde el Explorador en `\\wsl.localhost\Ubuntu\<ruta>` (el contenido de `sql` requiere `wsl -u root`).
- En Linux, si hay un segundo disco físico, montarlo y usarlo como `BACKUP_DIR`.
- Para activar copia externa más adelante: [offsite-monitoring-activacion.md](offsite-monitoring-activacion.md) (definir `BACKUP_REMOTE=secure:erp` y `BACKUP_REQUIRE_OFFSITE=true`).

Restauración y recuperación en una máquina nueva: [operacion-servidor-ubuntu.md](operacion-servidor-ubuntu.md) §4 y §6; detalle en [backup-restore.md](backup-restore.md).

## 9. Verificación rápida

```bash
docker compose -p theburyproject ps                      # app, db y caddy healthy/running
curl --cacert caddy-root-ca.crt --resolve tbp:443:127.0.0.1 https://tbp/health/ready   # 200
bash scripts/backup/backup.sh full                       # backup de prueba (exit 0)
bash scripts/backup/restore-test.sh                      # restauración no destructiva
```

## 10. Problemas frecuentes

| Síntoma | Causa y solución |
|---|---|
| (Ubuntu) El 443 responde desde redes que no deberían | `--allow-from` demasiado amplio, reglas borradas con Docker en marcha, o instalación anterior al 2026-11-27 (falla abierta). `ufw` no filtra este puerto. Ver §4.4 |
| (Ubuntu) El ERP no responde y `docker` figura `failed` | El firewall no se pudo aplicar o comprobar y Docker no arrancó a propósito: [operacion-servidor-ubuntu.md](operacion-servidor-ubuntu.md) §7 |
| La instalación se queda compilando y el servidor no responde por SSH | Falta de memoria al construir la imagen: agregar RAM o swap (§4) |
| (Ubuntu) Otra PC de la LAN no llega a `https://tbp` | Su red no está en `ALLOW_FROM` (`/etc/bury/firewall.conf`; repetir el bootstrap), o no resuelve `tbp` |
| `bad interpreter` / `\r` en un `.sh` | El repositorio se clonó desde Windows. Volver a clonarlo **dentro de WSL** |
| El instalador dice que Docker no responde | Abrir Docker Desktop y activar la integración WSL con la distro (Settings > Resources > WSL integration) |
| Otras PCs no llegan a `https://tbp` | Firewall (regla `TheBury ERP 443` y `-AllowFrom`), resolución de `tbp` (`nslookup`/`hosts`), o reenvío del puerto desde WSL2 |
| Advertencia de certificado | Falta instalar `caddy-root-ca.crt` en esa PC y reiniciar el navegador |
| `http://tbp` no responde | Es lo esperado: solo se publica 443. Usar `https://tbp` |
| El ERP no vuelve tras reiniciar | Falta el inicio de sesión automático o Docker Desktop no arranca al iniciar sesión (§3.3) |
| El servidor (o VM de pruebas) no responde tras un corte o reinicio (queda sin IP) | DHCP falló (visto con VMware en red puenteada por Wi-Fi: `NetworkManager … ip-config-unavailable`). Usar **IP fija** en netplan o **reserva DHCP en el router**, y cable. Ver `p04-p08-matriz-criterios-vm-simulada.md` §2.1 |
| Backup termina en error 6 | `BACKUP_REQUIRE_OFFSITE=true` sin `BACKUP_REMOTE`. Para solo local: `false` |
| `preflight` rechaza el `.env` | Quedó un valor `CHANGE_ME` o falta `AUTOMAPPER_LICENSE_MODE=community` / `AUTOMAPPER_LICENSE_KEY` |

## 11. Alcance y pendientes

- **Qué está probado, sobre qué commit y qué no:** [evidencias-instalacion.md](evidencias-instalacion.md). §4.1–4.5 se ejecutaron el 2026-11-27 en
  una VM Ubuntu 24.04 limpia; §4.6, actualización, vuelta atrás y recuperación, en el mismo laboratorio. **No** en hardware físico ni en 26.04, y
  la configuración de clientes (§5) no se repitió.
- `install-windows.ps1` (alternativa Windows) está probado con `-DryRun`, validaciones de parámetros y `-Uninstall -DryRun`; sus efectos reales
  (firewall, tareas programadas, energía) no se validaron en un servidor de destino.
- **No incluido en el instalador:** alertas y observador externo (hay scripts aparte en `scripts/monitoring/`, ver [monitoreo-alertas.md](monitoreo-alertas.md)), activación de la copia externa de backups y planificador de backups dentro de Docker (corre en el anfitrión: cron en Linux, Programador de tareas en Windows).
