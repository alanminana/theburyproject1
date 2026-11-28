# Operación del servidor Ubuntu: actualizar, respaldar, restaurar, volver atrás y recuperar

Plataforma de referencia: **Ubuntu con Docker Engine**. Para la primera instalación: [instalacion-servidor.md](instalacion-servidor.md) §4.
Qué está probado y qué no: [evidencias-instalacion.md](evidencias-instalacion.md). Los pasos sin ensayo se marcan **NO VALIDADO**.

Todos los comandos se ejecutan en el servidor, dentro del clon del repositorio, con la cuenta de administración. Los ejemplos usan el proyecto
Compose `theburyproject`. Con otro nombre: `export COMPOSE_PROJECT_NAME=<proyecto>` **y** `--project <proyecto>` en `install.sh`,
`deploy.sh`, `rollback.sh`, `install-schedule.sh` y `verificar-servidor.sh`.

## 0. Regla antes de cualquier `docker compose up`

`docker-compose.yml` usa `image: ${ERP_IMAGE:-theburyproject/erp:local}` en `app` y `migrate`, y el despliegue **no** escribe `ERP_IMAGE` en `.env`.
Un `docker compose up` sin esa variable resuelve la imagen `:local`: puede construir o arrancar un código distinto del desplegado.

Definirla siempre desde el registro que deja el despliegue:

```bash
export ERP_IMAGE="$(cat .deploy-state/${COMPOSE_PROJECT_NAME:-theburyproject}/current-image)"
test -n "$ERP_IMAGE" && docker image inspect "$ERP_IMAGE" >/dev/null && docker compose up -d --wait
```

Si el archivo no existe o la imagen no está, **no** ejecutar `up`: volver a desplegar con `install.sh` (§2) o seguir §6.
`docker compose ps`, `logs` y `stop` no recrean contenedores y no necesitan la variable. `start` y `restart` tampoco los recrean, pero
**ejecutan también `db-init` y `migrate`** con la imagen que tenga cada contenedor: sin efecto en una instalación normal; tras una vuelta atrás,
leer §5 antes de usarlos.

## 1. Estado y verificación

```bash
docker compose ps                                              # app, db y caddy "healthy"/running
cat .deploy-state/theburyproject/current-image                 # imagen desplegada: AAAAMMDD-<commit>
scripts/install/verificar-servidor.sh --project theburyproject --lan-cidr 192.168.1.0/24 --sudo-cmd "sudo"
```

`verificar-servidor.sh` es de solo lectura: salud, migraciones, puertos, HTTPS, respaldos, custodia, firewall y arranque. `--lan-cidr` debe
listar **exactamente** los orígenes autorizados en `/etc/bury/firewall.conf`.

Tras un reinicio: los contenedores con `restart: unless-stopped` vuelven solos. Los detenidos a propósito (`docker stop`, `compose stop`, o un
reinicio durante `backup.sh all`) no vuelven: usar el bloque de §0. Comprobar además `sudo bury-firewall verify` (ver §7).

## 2. Actualizar a una versión nueva

1. **Verificar el origen** en GitHub: el commit de `main` que se va a instalar tiene el CI en verde.
2. **Ver qué cambia en la base**, antes de tocar nada:

   ```bash
   git fetch origin
   ACTUAL="$(sed 's/.*-\([0-9a-f]\{7,\}\)\(-dirty\)\?$/\1/' .deploy-state/theburyproject/current-image)"
   git diff --name-status "$ACTUAL" origin/main -- Migrations/
   ```

   Si hay migraciones nuevas, leerlas y clasificarlas ([deploy-rollback.md](deploy-rollback.md) §14: A, B o C). La actualización **las aplica
   sola**. Con datos reales, requieren autorización del propietario. La categoría decide cómo se vuelve atrás (§5).
3. **Respaldo completo con par coherente** (detiene `app` unos segundos) y prueba de restauración:

   ```bash
   scripts/backup/backup.sh all && scripts/backup/restore-test.sh
   ```

   El despliegue solo toma antes un respaldo **SQL** (`backup.sh full`), sin archivos: no reemplaza este paso.
4. **Actualizar y desplegar:**

   ```bash
   git status --porcelain        # debe estar vacío: no se edita código en el servidor
   git switch main && git pull --ff-only
   bash scripts/install/install.sh --yes            # agregar --project <proyecto> si no es el predeterminado
   ```

   Orden: construir imagen `AAAAMMDD-<commit>` → preflight → respaldo SQL → `db-init` → `migrate` → `app` → health → smoke → Caddy → verificación de
   la imagen efectiva. Si `migrate` falla, la `app` anterior sigue corriendo. Códigos de salida: [deploy-rollback.md](deploy-rollback.md).
5. **Verificar** (§1), iniciar sesión y abrir una pantalla de cada módulo usado. Si `verificar-servidor.sh` informa `V17c` (el firewall
   instalado difiere del repositorio), repetir el bootstrap con los parámetros actuales ([red-privada-lan-vpn.md](red-privada-lan-vpn.md) §6):
   actualizar el ERP **no** actualiza el firewall del anfitrión.
6. **Registrar** commit, imagen y fecha ([sincronizacion-entornos.md](sincronizacion-entornos.md)) y **crear un paquete de recuperación nuevo** (§3.2):
   su manifiesto guarda el commit que habrá que usar para recuperar.

## 3. Respaldar

### 3.1 Datos

```bash
scripts/backup/backup.sh all      # SQL completo + keys, uploads, App_Data y caddy-data (par coherente; detiene app unos segundos)
scripts/backup/backup.sh log      # log de transacciones, sin detener nada
sudo scripts/backup/install-schedule.sh --install --user "$(id -un)"     # diario, cada 15 min y restore-test semanal
scripts/backup/restore-test.sh    # restaura en una base temporal, valida y la elimina
```

Con otro proyecto: `install-schedule.sh --install --project <proyecto>`. Detalle, retención y disco extraíble: [backup-restore.md](backup-restore.md).

**Un respaldo que queda solo en el servidor no sirve si se pierde el servidor.** Configurar al menos un destino fuera del equipo:
copia externa (`BACKUP_REMOTE` + `backup-offsite.env`, [backup-restore.md](backup-restore.md) §6, y luego `restore-test.sh --offsite`) o disco
extraíble cifrado **que se guarde fuera del local**.

### 3.2 Lo que hace falta para recuperar y NO está en los respaldos de datos

| Qué | Cómo se resguarda | Dónde guardarlo |
|---|---|---|
| `.env` y `backup-offsite.env` (incluye las claves `rclone crypt`) | `scripts/backup/recovery-package.sh create --out <dir>` | Fuera del servidor |
| Passphrase de ese paquete | Gestor de contraseñas o copia impresa | **Lugar distinto** al del paquete |
| Carpeta de `BACKUP_OFFSITE_SSH_DIR` (`known_hosts`, clave SFTP) | **No la incluye ningún paquete**: copiarla aparte | Fuera del servidor, cifrada |
| Clave y encabezado LUKS de la copia local | `sudo scripts/backup/exportar-custodia.sh create --out <dir> --passphrase-file <f>` | Fuera del servidor y fuera de ese disco |
| Clave de sellado de la CA (solo si `BACKUP_CA_KEY=seal`) | [backup-ca-key-custodia.md](backup-ca-key-custodia.md) | Separada de los respaldos |
| Acceso al repositorio de GitHub | Cuenta y segundo factor | Independiente del servidor |

Verificar una vez **en otro equipo**: `scripts/backup/recovery-package.sh verify <paquete>.gpg`. Regenerar el paquete tras cada actualización o
cambio de `.env`.

**La passphrase es la pieza que hace útil todo lo demás.** Las claves `crypt` van dentro del paquete: con las copias y el paquete a salvo, pero
la passphrase solo en el servidor, perder el servidor deja las copias ilegibles. La clave SFTP, en cambio, se puede reemplazar si se administra
el destino (generar otra y autorizarla allí).

## 4. Restaurar (sin perder el servidor)

Probar siempre primero en una base distinta; no es destructivo:

```bash
scripts/backup/restore-sql.sh --list
scripts/backup/restore-sql.sh --backup <full>.bak --target-db TheBuryProjectDb_Check --with-logs
```

`--stopat 'AAAA-MM-DD HH:MM:SS'` (UTC) recupera hasta un instante. **Reemplazar la base viva es destructivo**: pierde todo lo posterior al
respaldo y requiere autorización del propietario, `app` detenida, un respaldo del estado actual y `--overwrite --confirm-overwrite TheBuryProjectDb`.
Restauración de archivos: [backup-restore.md](backup-restore.md) §9.3 (`--force` vacía los volúmenes).

## 5. Volver atrás (rollback)

`rollback.sh` cambia **solo la imagen de la aplicación**. No revierte migraciones ni toca datos.

| La actualización | Cómo volver |
|---|---|
| Sin migraciones, o solo categoría A | `scripts/deploy/rollback.sh --confirm-compatible` (usa `previous-image`) o `--to-image <imagen>` |
| Con categoría B o C | No usar solo `rollback.sh`: preferir una corrección hacia adelante; si no alcanza, restaurar la base (abajo) |

Ejecutar `rollback.sh` **antes** de cambiar el código del clon: la versión actual del script, además de volver la imagen de `app`, recrea el
contenedor `migrate` con la imagen de destino **sin ejecutarlo**. Después, `git switch --detach <commit anterior>` para que los scripts coincidan
con la imagen, y registrar el motivo.

> **Por qué importa** (reproducido el 2026-11-27 con la versión anterior de los scripts): `migrate` conservaba la imagen nueva y
> `docker compose start app` lo ejecutaba como dependencia. El primer `backup.sh all` después de volver atrás **migró otra vez la base
> restaurada**, con la aplicación vieja encima. Si se hizo una vuelta atrás con scripts anteriores al 2026-11-27, comprobar cuántas
> migraciones tiene la base. Mientras exista ese contenedor viejo, no ejecutar `backup.sh`, `docker compose start` ni `restart`:
> `ERP_IMAGE=<imagen anterior> docker compose up --no-start --no-deps --force-recreate migrate` lo corrige.

**Restaurar la base al estado anterior a la actualización.** Secuencia ensayada el 2026-11-27 en una VM Ubuntu descartable, de `c442b380`
(137 migraciones) a 145 y de vuelta. **Pierde todo lo cargado después del respaldo** y requiere autorización del propietario.

```bash
scripts/backup/backup.sh full                       # conservar el estado actual antes de reemplazarlo
docker compose stop app
scripts/backup/restore-sql.sh --list                # elegir el full previo a la actualización
scripts/backup/restore-sql.sh --backup <full-previo>.bak --target-db TheBuryProjectDb \
    --overwrite --confirm-overwrite TheBuryProjectDb
scripts/deploy/rollback.sh --confirm-compatible     # imagen anterior; no ejecuta migrate
git switch --detach <commit anterior>
```

Anotar el nombre del full de §2 paso 3 **antes** de actualizar: después hay varios, porque el despliegue toma otro y este procedimiento
empieza tomando uno más. No usar `--with-logs` aquí: reaplicaría los cambios posteriores. `restore-sql.sh` termina sugiriendo
`docker compose up -d`: **no ejecutarlo** durante una vuelta atrás. No ejecutar `install.sh` ni `up` con la imagen nueva: volvería a migrar.

Comprobar después: cantidad de migraciones, `/health/ready`, un inicio de sesión y `scripts/backup/backup.sh all && scripts/backup/restore-test.sh`.
Los **archivos subidos no se revierten** con esta secuencia: quedan los actuales. Un `up` posterior debe usar `current-image` (§0).

## 6. Recuperación completa en una máquina Ubuntu nueva

Para cuando el servidor original ya no existe. **No usa nada de él**, salvo lo que se resguardó afuera. Ensayado el 2026-11-27 en una VM
Ubuntu nueva con la opción A (SFTP cifrado de prueba); la opción B tiene la evidencia anterior ([evidencias-instalacion.md](evidencias-instalacion.md)).

### 6.1 Requisitos: sin esto no hay recuperación

- [ ] Acceso al repositorio de GitHub.
- [ ] Paquete `bury-recovery_*.gpg` y su passphrase (o una copia completa de `.env`, y de `backup-offsite.env` si hay copia externa).
- [ ] Respaldos fuera del servidor perdido: **(A)** destino externo accesible, con la carpeta de claves SFTP si corresponde, o **(B)** el disco
      extraíble con el paquete `bury-custodia-luks_*.gpg` y su passphrase.
- [ ] La clave de sellado de la CA, solo si `BACKUP_CA_KEY=seal`.
- [ ] Ubuntu con salida a Internet (imágenes y paquetes) e IP fija o reservada.

Si los respaldos o los secretos existían solo en el servidor perdido, **los datos no se pueden recuperar** con este procedimiento.

### 6.2 Preparar el anfitrión

```bash
sudo apt-get update && sudo apt-get install -y git
git clone https://github.com/alanminana/theburyproject1.git ~/theburyproject1 && cd ~/theburyproject1
sudo scripts/install/server-linux-bootstrap.sh --user "$USER" --allow-from 192.168.1.0/24 \
     --admin-from 192.168.1.10/32 --backup-dir /srv/bury-backups --dry-run      # revisar y repetir sin --dry-run
exit   # abrir una sesión nueva para tomar el grupo docker
```

Ejecutar el bootstrap **ahora, con el código de `main`**, y no repetirlo después de cambiar de commit en §6.3: un commit viejo instalaría un
firewall viejo. La imagen se compila en este equipo: hace falta la RAM y el swap de [instalacion-servidor.md](instalacion-servidor.md) §4.

Opciones y controles del firewall: [red-privada-lan-vpn.md](red-privada-lan-vpn.md) §6. `--admin-from` debe incluir la IP de la sesión SSH actual.

### 6.3 Secretos y versión del código

```bash
cd ~/theburyproject1
scripts/backup/recovery-package.sh extract /ruta/bury-recovery_<fecha>.gpg --to /dev/shm/rec    # pide la passphrase (o --passphrase-file)
cat /dev/shm/rec/MANIFEST.txt                  # fecha, "commit=" y archivos incluidos
install -m 600 /dev/shm/rec/.env .env
[ -f /dev/shm/rec/backup-offsite.env ] && install -m 600 /dev/shm/rec/backup-offsite.env backup-offsite.env
COMMIT=$(sed -n 's/^commit=//p' /dev/shm/rec/MANIFEST.txt); rm -rf /dev/shm/rec
git switch --detach "$COMMIT"
```

Debe usarse **el commit que estaba desplegado cuando se tomó el respaldo**. El del manifiesto es el que tenía el clon al crear el paquete: coincide
si se respetó §2 paso 6. Si no coincide, usar el del registro de despliegues. Con código más viejo que la base no continuar; con código más nuevo,
`migrate` aplicará las migraciones que falten.

Editar en `.env` solo lo propio del equipo nuevo: `ERP_BIND_ADDRESS`, `BACKUP_DIR`, `BACKUP_REMOVABLE_DIR`, `ERP_HTTPS_PORT`, `BURY_NET_SUBNET`.
Mantener `ERP_DOMAIN` para que los clientes sigan entrando igual. **No** generar un `.env` nuevo: tendría otros secretos.

```bash
scripts/backup/prepare-dir.sh     # ANTES de cualquier "docker compose up": si no, Docker crea BACKUP_DIR como root
```

### 6.4 Traer los respaldos

**A. Copia externa:** con SFTP, restaurar la carpeta de claves, definir `BACKUP_OFFSITE_SSH_DIR` en `.env` y
`export COMPOSE_FILE=docker-compose.yml:docker-compose.offsite-sftp.yml` (con disco externo, el overlay `docker-compose.offsite-disk.yml`).

```bash
scripts/backup/fetch-offsite.sh --list && scripts/backup/fetch-offsite.sh
```

**B. Disco extraíble cifrado:** extraer la clave en memoria, abrir en modo solo lectura y copiar. `<nombre>` es el de la copia (p. ej. `bury-prov`).

```bash
sudo apt-get install -y cryptsetup
sudo scripts/backup/exportar-custodia.sh extract /ruta/bury-custodia-luks_<fecha>.gpg --to /dev/shm/luks --passphrase-file <archivo 0600>
sudo ls /dev/shm/luks                                     # nombres de la clave y del encabezado
sudo cryptsetup open --readonly --type luks2 --key-file /dev/shm/luks/<clave>.key /ruta/<nombre>.luks recup
sudo mkdir -p /mnt/recup && sudo mount -o ro /dev/mapper/recup /mnt/recup
sudo cp -a /mnt/recup/bury-backup/sql/. /srv/bury-backups/sql/
sudo cp -a /mnt/recup/bury-backup/files/. /srv/bury-backups/files/
sudo umount /mnt/recup && sudo cryptsetup close recup && sudo rm -rf /dev/shm/luks
```

### 6.5 Construir la imagen y recuperar

`install.sh --no-deploy` **no construye la imagen** (termina antes). Construirla explícitamente:

```bash
export ERP_IMAGE="theburyproject/erp:$(date -u +%Y%m%d)-$(git rev-parse --short HEAD)"
docker build -t "$ERP_IMAGE" .
scripts/backup/recover.sh --plan          # muestra el par elegido (con --with-logs, también los logs); no cambia nada
scripts/backup/recover.sh --apply         # agregar --with-logs para lo más reciente (leer backup-restore.md §10.2)
```

`recover.sh` verifica los sha256 antes de tocar nada, restaura, exige base ONLINE con migraciones, usuarios y `DBCC CHECKDB`, y recién entonces
levanta `app`. Si falla, **no ejecutar `docker compose up` a mano**: `migrate` crearía una base vacía que parecería sana. Corregir y reintentar
(`--force-files` si los archivos ya se repusieron).

**No ejecutar `install.sh` ni `deploy.sh` antes de que `recover.sh` termine con `recover OK`.**

### 6.6 Dejarlo operativo

Solo después de `recover OK`:

```bash
mkdir -p .deploy-state/theburyproject && echo "$ERP_IMAGE" > .deploy-state/theburyproject/current-image
docker compose cp caddy:/data/caddy/pki/authorities/local/root.crt ./caddy-root-ca.crt
sudo scripts/backup/install-schedule.sh --install --user "$USER"
scripts/backup/backup.sh all && scripts/backup/restore-test.sh
scripts/install/verificar-servidor.sh --project theburyproject --lan-cidr 192.168.1.0/24 --sudo-cmd "sudo"
```

Luego: apuntar `ERP_DOMAIN` a la IP nueva (DNS local o `hosts`), comparar la huella de la CA (si cambió, reinstalarla en los clientes),
iniciar sesión, abrir un cliente o venta reciente, un documento subido y un contrato, y reconfigurar la copia externa y la custodia.

## 7. Firewall: comprobar, y qué hacer si Docker no arranca

El 443 que publica Docker lo filtra **únicamente** la cadena de `DOCKER-USER` que crea `bury-firewall`; `ufw` no lo filtra. El firewall
**falla cerrado**: si no se puede aplicar o comprobar, **Docker no queda en marcha** y el ERP está caído para todos. Detalle y límites:
[red-privada-lan-vpn.md](red-privada-lan-vpn.md) §6.

Comprobar tras instalar, reiniciar o actualizar Docker:

```bash
systemctl is-active docker bury-firewall && sudo bury-firewall verify     # "verificado (BURY-ERP, puerto 443)"
```

Si el ERP no responde y `docker` figura `failed`:

```bash
journalctl -u docker -b --no-pager | grep bury-firewall                   # la causa
# corregirla (normalmente /etc/bury/firewall.conf o /etc/docker/daemon.json)
sudo bury-firewall guard && sudo systemctl reset-failed docker && sudo systemctl start docker && sudo systemctl start bury-firewall
docker compose ps                                                          # los contenedores vuelven solos
```

Mientras Docker está caído **no corren los respaldos**: resolverlo no puede esperar. Nunca borrar el drop-in ni usar `bury-firewall remove`.

**Instalación anterior al 2026-11-27:** su firewall falla abierto y `git pull` no lo corrige. Seguir «Servidores instalados antes del 2026-11-27»
en [red-privada-lan-vpn.md](red-privada-lan-vpn.md) §6. `verificar-servidor.sh` lo señala como `V17b FAIL`.
