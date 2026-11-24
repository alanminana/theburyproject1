# Notebook Ubuntu como servidor independiente de pruebas (2026-11-24)

Instalación **de laboratorio**, independiente (la VM `tbp` quedó fuera del plan el 2026-11-24, ver §4): hardware, disco y CA propios. No es una migración productiva, no contiene datos reales
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
| Fiscal V2 | `FISCAL_CRITERIO_V2_DESDE=2026-11-01T03:00:00Z` solo en esta instalación |
| Crédito | cerrado: cupos $0 en los puntajes 0-5 (el 0 se bajó por la pantalla real `LimitesPorPuntajeGuardar`), sin planes; `p07-verificar-credito-cerrado.sql` sin FALLA |
| Cuenta | `lab-admin` (SuperAdmin sintético, correo `.invalid`). `p07-verificar-cuentas.sql @Cierre=1` la marca FALLA **por diseño**: solo aplica a producción |

Instalada con `scripts/install/server-linux-bootstrap.sh` + `scripts/install/install.sh` + `scripts/backup/install-schedule.sh --project theburyproject-notebook`
sin cambios de código.

## 2. Aislamiento

* Solo se publica `443` (Caddy). `db` (1433), `app` (8080) y la API de Caddy (2019) no se publican. Medido desde otro equipo de la LAN: 80, 1433, 2019, 8080 y 8443 cerrados.
* `bury-firewall` (DOCKER-USER) limita el 443 a `192.168.50.0/24`; `ufw` deniega todo lo entrante salvo SSH (22) desde el equipo administrador (/32).
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
  Hoy no hay ningún cliente autorizado (`authorized_keys` vacío). Para autorizar a otro equipo: agregar **su** clave pública con `restrict,from="<IP del equipo>"` y fijar la clave del servidor en su `known_hosts` por un canal independiente.

### Custodia de claves (pendiente del propietario)

Sin la clave, el pendrive es ilegible. Están **solo en la notebook** (disco interno, `root:0400`) y deben copiarse FUERA de ella y fuera del pendrive:

* `/etc/bury-backup/local-disk-bury-notebook.key` y `/etc/bury-backup/local-disk-bury-notebook.luks-header`
* `~/theburyproject1/.env` completo (secretos de SQL, Data Protection, contraseña inicial del admin)
* Las passwords de `rclone crypt` (`SECURE_PASSWORD` y `SECURE_PASSWORD2`) no existen todavía en esta instalación: se generan al activar un envío hacia este receptor.

### Recuperar desde el pendrive en otra máquina

```
sudo cryptsetup open --type luks2 --key-file local-disk-bury-notebook.key bury-notebook.luks recup
sudo mkdir -p /mnt/recup && sudo mount /dev/mapper/recup /mnt/recup
cd /mnt/recup/bury-backup && for f in sql/*.bak sql/*.trn files/*.tar.gz; do (cd "$(dirname "$f")" && sha256sum -c "$(basename "$f").sha256"); done
# restaurar SIEMPRE primero en un SQL descartable: scripts/backup/restore-test-local-copy.sh (docs/p06-copia-cifrada-produccion.md, «Recuperación»)
```

## 4. Qué se probó (2026-11-24, todo en esta notebook)

**Decisión del propietario (2026-11-24):** la VM `tbp` queda **fuera del plan** y no se vuelve a usar; esta notebook es el entorno independiente de pruebas hasta finalizar el ERP; no se usan datos comerciales reales.
La «restauración entre equipos con la VM» deja de ser un objetivo (se reemplaza por la recuperación desde instalación limpia de abajo).

| Prueba | Resultado |
|---|---|
| `install.sh` en base nueva | deploy OK, 145 migraciones, health live+ready 200, smoke OK, `app`/`migrate` con la imagen desplegada (`668ccf61b741`) |
| `p02-verificar-instalacion.sql`, `p07-verificar-credito-cerrado.sql` | sin FALLA (el de crédito falló al inicio por la semilla del puntaje 0 = $200.000 → corregido por la pantalla real `LimitesPorPuntajeGuardar`) |
| `backup.sh all` y copia al extraíble cifrado | OK (26-27 s); sha256 verificado |
| `verify-local-copy.sh` V1-V5 y fallo seguro (volumen cerrado) | OK; con el volumen cerrado `touch` denegado, `copy-removable --strict` rc 1, verificador rc 13, nada escrito en `/` |
| `restore-test-local-copy.sh`, `restore-test.sh`, `check-custody.sh` (trabajos semanales) | restore ONLINE, `DBCC CHECKDB` sin errores, 145 migraciones, 141 tablas, 4 s; custodia OK (2 avisos esperados: sin `BACKUP_REMOTE`, `root.key` de Caddy en el tar) |
| SFTP + `rclone crypt` **por loopback** | nombres cifrados en el pendrive, bajada con SHA256 idéntico; sin shell, sin contraseña, jaula; limpieza completa. No es transferencia entre equipos |
| **Reinicio completo** (`systemctl reboot`) | volvió sola, misma IP; SSH, Docker, `bury-firewall`, ufw, cron, volumen LUKS remontado, contenedores sanos con 0 reinicios, `ready=200` al primer intento; 145 migraciones; copia cifrada con el mismo hash. Arranque total 33 s (firmware 8,6 + loader 6,2 + kernel 1,2 + initrd 1,8 + userspace 15,0) |
| **Corte brusco** (`sysrq-b`, sin sync ni cierre de servicios) tras confirmar un cambio por pantalla (versión de punitorios 0 %/30 d/gracia 12) | SQL Server: «22 transactions rolled forward, 0 rolled back»; el cambio persistió; `DBCC CHECKDB` sin errores; copia cifrada reabierta (30 archivos sha256 OK); `ready=200` |
| **Recuperación desde instalación limpia** | clon nuevo del repo (`ab27a838`), `.env` extraído del paquete cifrado (`recovery-package.sh`), proyecto Compose nuevo con volúmenes vacíos, respaldos traídos de la copia del pendrive (30 archivos sha256 OK), `restore-files` + `restore-sql` (par coherente) + stack completo. Mismo usuario y hash de contraseña, 145 migraciones, **la cookie de sesión de la instancia viva fue aceptada por la restaurada** (mismas claves de Data Protection; sin cookie → 302 a Login), misma CA de Caddy (HTTPS 200 con la CA original), `DBCC CHECKDB` sin errores, verificadores P-02 y crédito sin FALLA. Stack DR destruido (0 residuos) |
| `scripts/qa/instalacion-limpia.sh` | OK (instalación limpia verificada) |
| `scripts/qa/p02/validacion-base-nueva.sh` | 37 OK / 0 FALLA (V2 desde el primer arranque, gracia por pantalla, V2 efectivo en la primera venta, backup + restore-test) |
| `scripts/qa/p07/run-p07.sh --no-ui` | **131/131** (ver §5) |
| D-05: activación `ReportOnly` / centinela por Caddy / reversión a `Off` | cabecera `Content-Security-Policy-Report-Only` presente; `/csp-report` por Caddy responde 204 **con y sin `Content-Length`** (riesgo R2 medido); activación 5,2 s, reversión 4,9 s; imagen intacta. **No inicia la ventana de 10 días hábiles** |
| Energía/servidor | tapa y suspensión deshabilitadas (`logind` + `*.target` enmascarados); SSH solo desde el equipo administrador (se eliminó una regla `OpenSSH ALLOW Anywhere` preexistente) |

### Hallazgo de recuperación (corregido): la copia del extraíble perdía el dueño de `sql/`

La copia del pendrive la hace el contenedor `backup-files` con `cp` sin conservar dueño ni modo: `sql/` y los `.bak/.trn` quedaban `root:root` (700/640) mientras que en el original son del uid 10001 (SQL Server). Al restaurar en una instalación limpia,
`RESTORE VERIFYONLY` **fallaba** y, si se levantaba el stack igual, `migrate` creaba una base vacía (no era una recuperación). **Corregido en código** (no es un paso manual): `copy-removable` conserva dueño y modo; `restore-sql.sh`
corrige el directorio y los archivos desde un contenedor root y aborta si no puede; la nueva `scripts/backup/recover.sh` hace la recuperación completa con salida estricta, compuerta previa al arranque y marca `.recovery-incomplete`
(ver `backup-restore.md` §10). Prueba: `scripts/qa/p07/run-dr-datos.sh` (abajo).

### Recuperación con datos comerciales sintéticos conocidos (`run-dr-datos.sh`): 47/47

Pila A: los recorridos P-07 cargan por la aplicación 1 cliente, 1 producto, 1 proveedor, 1 orden de compra, 5 ventas, 3 facturas, 8 movimientos de caja, 2 devoluciones, 1 nota de crédito, 1 crédito con 6 cuotas, 3 documentos subidos y 12 movimientos de stock;
con `app` detenida se toma la huella de **las 141 tablas** (filas + `CHECKSUM_AGG(BINARY_CHECKSUM(*))`) y de `uploads`, y se hace `backup.sh all`. Pila B (volúmenes vacíos): los respaldos llegan como `root:root` 600 y se recupera con `recover.sh`.
Casos negativos (todos se detienen con código ≠ 0, sin levantar `app`/`migrate`): `.bak` alterado (código 3, no cambia nada), tar ausente (código 3, no cambia nada), `.bak` corrupto con sha256 recalculado (código 3 tras restaurar archivos; la base no existe, queda la marca y `preflight.sh` rechaza desplegar, también con `BACKUP_DIR` relativo), `recover.sh` sobre la instalación viva (se niega, sin marca ni cambios) y con la aplicación detenida pero volúmenes con contenido (se niega, sin marca).
Positivo (con `--with-logs`: full + 1 log de transacciones): huella de las 141 tablas **idéntica** entre A y B, uploads idénticos, 0 violaciones de FK/CHECK; recuperación completa con stack sano; tras arrancar, clientes, ventas, facturas y documentos conservan filas y checksum.
Además, smoke de interfaz en Chrome real sobre el sistema recuperado (24 pantallas × 1366/390 vía túnel SSH): 48/48 en la segunda corrida (la primera tuvo 1 observación transitoria de recursos abortados en un arranque en frío).

## 5. Hallazgo: el runner P-07 estaba desactualizado

Con `main` `ab27a838`, `p07_recorridos.py` fallaba en R8.10-R8.12 y se cortaba con `IndexError`. **Causa raíz**: el PR #363 (mora contractual) hizo que generar el contrato de crédito exija una versión de punitorios
vigente y activa (`ContratoVentaCreditoValidacionException`); el guion (19/11) no la creaba. No es un defecto del ERP. PR #367: paso R8.3b (alta de la versión por la pantalla real, como SuperAdmin, tasa sintética, día comercial argentino): 131/131.
Esto además **valida el tramo crédito → contrato → cobros** que `aceptacion-final-20261123.md` §12b dejaba NO VALIDADO (con punitorio sintético 1 %, no 0 %).

## 6. Qué NO quedó demostrado

* **Respaldo independiente del anfitrión**: el pendrive está junto a la notebook. Cuenta como medio extraíble solo si se guarda fuera del equipo.
* **Custodia externa de claves** (LUKS, passphrase del paquete de recuperación, `.env`): hoy solo en la notebook (`/etc/bury-backup/`, `~/.bury-custodia/`) y el paquete cifrado en el pendrive.
* **Corte de energía real** (UPS, batería): solo se simuló el corte lógico.
* **Interfaz con navegador** (Playwright) y **lectores de pantalla**: no se ejecutaron en la notebook (no hay Chrome).
* **Red**: solo Wi-Fi con DHCP; SSH permitido únicamente desde la IP actual del equipo administrador. Reservar IP en el router y preferir cable.
