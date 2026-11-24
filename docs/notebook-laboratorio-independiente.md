# Notebook Ubuntu como servidor independiente de pruebas (2026-11-24)

Instalación **de laboratorio**, independiente de la VM `tbp`: hardware, disco y CA propios. No es una migración productiva, no contiene datos reales
y **no equivale** a una primera venta comercial real ni a los 10 días hábiles de observación CSP.

## 1. Qué es

| | |
|---|---|
| Equipo | notebook x86_64, 8 núcleos, 30 GiB RAM, SSD interno 238 GB (`sda`), pendrive USB 14,6 GB (`sdb`) |
| Sistema | Ubuntu 26.04.1 LTS, kernel 7.0.0-44 |
| Red | **solo Wi-Fi** (`wlp5s0`, 192.168.50.223/24, DHCP); el puerto Ethernet está sin cable. Recomendado: reserva DHCP en el router y cable |
| Software | Docker Engine 29.10.0, Compose 5.8.0, git 2.53.0, OpenSSL 3.5.5, Python 3.14.4 (repositorio oficial de Docker, codename `resolute`) |
| ERP | commit `ab27a838` (main, PR #365), imagen `theburyproject/erp:20261124-ab27a838` |
| Instancia | proyecto Compose `theburyproject-notebook`, dominio `tbp-nb` (`https://tbp-nb`, resolver a la IP del equipo), CA de Caddy propia |
| Base | SQL Server Express nueva y vacía; 145 migraciones; volúmenes `theburyproject-notebook_*` |
| Fiscal V2 | `FISCAL_CRITERIO_V2_DESDE=2026-11-01T03:00:00Z` solo en esta instalación (la VM no se tocó) |
| Crédito | cerrado: cupos $0 en los puntajes 0-5 (el 0 se bajó por la pantalla real `LimitesPorPuntajeGuardar`), sin planes; `p07-verificar-credito-cerrado.sql` sin FALLA |
| Cuenta | `lab-admin` (SuperAdmin sintético, correo `.invalid`). `p07-verificar-cuentas.sql @Cierre=1` la marca FALLA **por diseño**: solo aplica a producción |

Instalada con `scripts/install/server-linux-bootstrap.sh` + `scripts/install/install.sh` + `scripts/backup/install-schedule.sh --project theburyproject-notebook`
sin cambios de código.

## 2. Aislamiento

* Solo se publica `443` (Caddy). `db` (1433), `app` (8080) y la API de Caddy (2019) no se publican. Medido desde otro equipo de la LAN: 80, 1433, 2019, 8080 y 8443 cerrados.
* `bury-firewall` (DOCKER-USER) limita el 443 a `192.168.50.0/24`; `ufw` deniega todo lo entrante salvo SSH (22) desde el equipo administrador y la VM `tbp` (/32).
  **Si la IP del administrador cambia (DHCP), SSH deja de entrar: usar la consola local o reservar la IP.**
* HTTPS desde otro equipo: 200 con `caddy-root-ca.crt`; sin la CA, rechazado.
* Docker con `shutdown-timeout: 60`. Pertenecer al grupo `docker` equivale a root.

## 3. Pendrive y respaldos

* `sdb1` (ext4, UUID `f6fad9b8-…`) llegó **vacío** (solo `lost+found`); no se formateó ni reparticionó. Montaje persistente por UUID en `/mnt/bury-pendrive`
  (`nofail,noexec,nosuid,nodev`); desmontado, el punto de montaje es inmutable (`chattr +i`) y no se puede escribir en `/`.
* Copia secundaria **cifrada** (LUKS2, archivo `bury-notebook.luks` de 12 GB dispersos) → `/mnt/bury-lab-respaldo` = `BACKUP_REMOVABLE_DIR`, con marca, SHA256 y poda por capacidad (tope 9 GB, libre mínimo 1 GB).
* Cron `/etc/cron.d/bury-backup-notebook` (full+archivos 02:30, log cada 15 min, restore-test semanal domingo 04:30, custodia domingo 05:30) y verificador `bury-backup-localcopy-bury-notebook` 03:15.
* Retención: local por capacidad (`BACKUP_MIN_FREE_GB=20`) y por días (14); extraíble por capacidad. `BACKUP_REQUIRE_OFFSITE=false`: **no hay copia externa activa**.
* Recepción de respaldos de otro equipo: usuario `bury-sftp` (sin shell, `internal-sftp`, jaula `/mnt/bury-pendrive/sftp-recepcion`, solo clave, `authorized_keys` vacío y propiedad de root en `/var/lib/bury-sftp/.ssh/`).
  Para autorizar a la VM: agregar **su** clave pública con `restrict,from="<IP de la VM>"`; clave del servidor a fijar en `known_hosts` de la VM por canal independiente.

### Custodia de claves (pendiente del propietario)

Sin la clave, el pendrive es ilegible. Están **solo en la notebook** (disco interno, `root:0400`) y deben copiarse FUERA de ella y fuera del pendrive:

* `/etc/bury-backup/local-disk-bury-notebook.key` y `/etc/bury-backup/local-disk-bury-notebook.luks-header`
* `~/theburyproject1/.env` completo (secretos de SQL, Data Protection, contraseña inicial del admin)
* Las passwords de `rclone crypt` (`SECURE_PASSWORD` y `SECURE_PASSWORD2`) no existen todavía en esta instalación: se generan al activar el envío desde la VM.

### Recuperar desde el pendrive en otra máquina

```
sudo cryptsetup open --type luks2 --key-file local-disk-bury-notebook.key bury-notebook.luks recup
sudo mkdir -p /mnt/recup && sudo mount /dev/mapper/recup /mnt/recup
cd /mnt/recup/bury-backup && for f in sql/*.bak sql/*.trn files/*.tar.gz; do (cd "$(dirname "$f")" && sha256sum -c "$(basename "$f").sha256"); done
# restaurar SIEMPRE primero en un SQL descartable: scripts/backup/restore-test-local-copy.sh (docs/p06-copia-cifrada-produccion.md, «Recuperación»)
```

## 4. Qué se probó (2026-11-24)

| Prueba | Resultado |
|---|---|
| `install.sh` en base nueva | deploy OK, 145 migraciones, health live+ready 200, smoke OK, `app`/`migrate` con la imagen desplegada (`668ccf61b741`) |
| `p02-verificar-instalacion.sql` | sin FALLA |
| `p07-verificar-credito-cerrado.sql` | FALLA inicial real (semilla puntaje 0 = $200.000) → corregido por pantalla → sin FALLA |
| `backup.sh all` | OK (27 s); copia al extraíble cifrado verificada con sha256 |
| `verify-local-copy.sh` V1-V5 | OK |
| Fallo seguro (volumen cifrado cerrado) | `touch` denegado, `copy-removable --strict` rc 1, verificador rc 13, nada escrito en `/`; tras remontar, OK |
| `restore-test-local-copy.sh` | full restaurado ONLINE, `DBCC CHECKDB` sin errores, 145 migraciones, 141 tablas, 0 residuos |
| SFTP + `rclone crypt` **por loopback** (copia propia de la notebook) | subida con nombres cifrados en el pendrive, bajada con SHA256 idéntico; sin shell, sin contraseña, jaula; limpieza completa |
| Reinicio controlado de `db`/`app`/`caddy` | `ready=200` en 6 s, misma imagen, datos intactos |
| `scripts/qa/p07/run-p07.sh --no-ui` (pila descartable en esta notebook) | **131/131** (ver §5) |

## 5. Hallazgo: el runner P-07 estaba desactualizado

Con `main` `ab27a838`, `p07_recorridos.py` fallaba en R8.10-R8.12 y se cortaba con `IndexError`. **Causa raíz**: el PR #363 (mora contractual) hizo que generar el contrato de crédito exija una versión de punitorios
vigente y activa (`ContratoVentaCreditoValidacionException`); el guion (19/11) no la creaba. No es un defecto del ERP. Se agregó el paso R8.3b (alta de la versión por la pantalla real, como SuperAdmin, tasa sintética): 131/131.

## 6. Qué NO quedó demostrado

* **Restauración entre equipos con respaldos de la VM `tbp`**: la VM (192.168.50.162) **estaba apagada o fuera de la red** durante la sesión (sin ping ni ARP). No se la encendió ni se tocó. Falta ejecutar con la VM encendida: clave SSH de la VM → `authorized_keys` de `bury-sftp`,
  `known_hosts` fijado, passwords `rclone crypt`, envío de una cadena existente, bajada, sha256, restauración y `DBCC CHECKDB` aquí. La prueba por loopback demuestra el receptor, **no** la transferencia entre equipos.
* Reinicio de la notebook (arranque del volumen cifrado, `bury-firewall`, contenedores): no se reinició. Montaje persistente configurado, **sin ensayar**.
* Interfaz de usuario (Playwright): se omitió (`--no-ui`); no hay Chrome en la notebook.
* Ninguna copia es externa al lugar físico: el pendrive vive junto a la notebook.
