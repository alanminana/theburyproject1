# P-06 — cierre técnico de respaldos con dos instalaciones en la misma VM (2026-11-23)

> Histórico: la VM `tbp` quedó fuera del plan el 2026-11-24; este documento conserva la evidencia tal como se emitió.

Alcance: que los respaldos de la instalación nueva (proyecto Compose `theburyproject-prod`, `/srv/bury-backups-prod`, clon `~/theburyproject-prod-src`)
convivan con la preproducción (`theburyproject`, `/srv/bury-backups`, clon `~/theburyproject1`) sin mezclarse, y dejar a la vista qué protege cada
respaldo, cómo se recupera y qué sigue sin cubrirse. No repite las pruebas históricas de P-06 (ver [backup-restore.md](backup-restore.md),
[p06-respaldo-disco-local-vm.md](p06-respaldo-disco-local-vm.md), [backup-dr-rotacion-claves.md](backup-dr-rotacion-claves.md)).

> **Estado real de P-06: NO está Finalizado.** Sigue sin existir ninguna copia independiente del equipo (VM/PC). El riesgo está aceptado
> temporalmente por el propietario; **no está mitigado**. Ver §7.

## 1. Defectos verificados y corregidos (PR de este cierre)

| # | Defecto (evidencia) | Corrección |
|---|---|---|
| D1 | **Proyecto Compose equivocado.** `docker-compose.yml` fija `name: theburyproject`. Un cron o un restore lanzado desde el clon de producción **sin** `COMPOSE_PROJECT_NAME` apunta a la pila de preproducción. Reproducido con Docker real en la VM: desde un clon sin la variable, con `BACKUP_DIR=/srv/bury-backups-prod`, `docker compose ps db` resuelve el `db` de preprod (que monta `/srv/bury-backups/sql`). Consecuencia: `backup.sh` habría detenido la `app` de preprod y guardado **su** base en el directorio de producción; `restore-files.sh --force` habría vaciado volúmenes ajenos. El `.env` de producción no define `COMPOSE_PROJECT_NAME` (el instalador pasa `-p`), así que cualquier ejecución manual desde el clon caía en el defecto. | `assert_backup_dir_matches_project` en `lib.sh` (se ejecuta en `check_prereqs`, o sea `backup.sh`, `restore-sql.sh`, `restore-test.sh`, y en `restore-files.sh`): compara el directorio del host que el `db` del proyecto efectivo monta en `/backups/sql` con `$BACKUP_DIR/sql` y **aborta (exit 8)** ante discrepancia positiva, nombrando proyecto y rutas. No bloquea si no hay evidencia (sin `db` creado, rutas de Docker Desktop). Un restore que lee de un directorio de preparación (staging) distinto del que monta el `db` exige el opt-in explícito `BURY_ALLOW_BACKUP_DIR_MISMATCH=1` (avisa por WARN; solo lo usa el simulacro `test-offsite-sftp.sh`, nunca va en `.env` ni cron). Verificado en la VM contra las dos pilas reales (preprod: pasa; producción sin variable: aborta). |
| D2 | `install-schedule.sh` escribía siempre `/etc/cron.d/bury-backup`, `/etc/logrotate.d/bury-backup` y `/var/log/bury-backup-errors.log`: instalarlo desde el clon de producción **reemplazaba el cron de preprod**. | `--project PROYECTO` (archivos propios `bury-backup-<sufijo>`, `COMPOSE_PROJECT_NAME` en cada job, log de errores propio), `--log-offset MIN` (escalonar el log: `7,22,37,52`) y rechazo de `--install`/`--uninstall` si el archivo existente apunta a **otro clon** o a **otro proyecto** (dos `--project` pueden dar el mismo sufijo, p. ej. `prod` y `theburyproject-prod`: la identidad es el proyecto grabado en el cron; revisión Codex). El cron manual ya instalado en la VM (`/etc/cron.d/bury-backup-prod`) es equivalente (usa `COMPOSE_PROJECT_NAME` y `COMPOSE_ENV_FILES` como variables del cron). |
| D3 | El destino extraíble (`BACKUP_REMOVABLE_DIR`) guardaba siempre en `<destino>/bury-backup/`. Los nombres (`TheBuryProjectDb_full_<ts>.bak`) son idénticos entre instalaciones (ambas usan `TheBuryProjectDb`), y la retención por cadenas las trataría como una sola: una instalación podaría las copias de la otra. | La primera copia reclama el destino grabando `BACKUP_DIR` en la marca `.bury-backup-removable` bajo `flock` de la propia marca y con relectura (`removable_claim`); `removable_available` rechaza (atrasada, exit 1 en `--strict`, nunca rompe el backup) un destino cuya marca nombra a otra instalación, y si no se puede reclamar o confirmar el dueño no se copia ni poda nada (fail-closed; revisión Codex). Marca vacía (legado) = libre, se reclama al copiar. |
| D4 | No había forma de ensayar la restauración de un `BACKUP_DIR` **real** en un SQL Server descartable (solo desde la copia secundaria), ni medir tiempos. | `restore-test-local-copy.sh --from-backup-dir DIR` (solo lectura, `:ro`) y línea `TIEMPOS` en la evidencia. |

Pruebas añadidas: `test-hardening.sh` (107 casos, 0 fallos en la VM) y `test-retention-capacity.sh` (102 casos, 0 fallos; +6 del destino extraíble).
En `test-retention-capacity.sh` falla un caso **ajeno** (`test_telegram_relay…cannot_create_unbounded_threads`): intermitente en `main` sin estos
cambios (falló 1 de 2 corridas en el clon de producción a 647207c1).

## 2. Qué datos y archivos protege cada respaldo

| Componente | Dónde | Cómo | Notas |
|---|---|---|---|
| Base SQL Server `TheBuryProjectDb` | `$BACKUP_DIR/sql` | full diario + log de transacciones cada 15 min (`.bak`/`.trn` + `.sha256`, `RESTORE VERIFYONLY`) | Recuperación `FULL`; restore a un instante con `--with-logs --stopat` |
| Claves de Data Protection (sesiones/cookies) | `files_<ts>.tar.gz` → `keys/` | tar con la `app` detenida (par consistente con el full, manifiesto `set_*.txt`) | Sin ellas las sesiones viejas se invalidan |
| Archivos subidos y `App_Data` | `uploads/`, `appdata/` | ídem | |
| CA y certificados de Caddy (`caddy-data`) | `caddy-data/` | ídem. Con `BACKUP_CA_KEY=include` (hoy) **`root.key` viaja dentro del tar** | Los clientes confían en esa CA: perderla obliga a redistribuir `caddy-root-ca.crt` |
| Configuración y secretos (`.env`, `backup-offsite.env`) | **NO se respaldan** | `recovery-package.sh` (cifrado, ver §6) | Sin `.env` la base restaurada no se puede reabrir (SA, usuarios de la app, clave de licencia) |
| Código / imagen | Git + `docker build` (etiqueta `theburyproject/erp:<fecha>-<commit>`) | no se respalda | Se reconstruye desde el commit |
| Cron, monitor, unidades systemd, firewall | **NO se respaldan** | repo (`scripts/`) + procedimiento de instalación | Se reinstalan |
| LUKS de la copia local (preprod) | `/etc/bury-backup/local-disk.key` y `.luks-header` | **NO se respaldan**: custodia manual (§6) | Producción **no tiene** copia LUKS |

Verificado hoy sobre el respaldo real de producción (`files_2026-11-23_183040Z.tar.gz`): contiene `keys/`, `uploads/`, `appdata/`, `caddy-data/`.

## 3. Cómo se restaura la instalación (producción)

Siempre con el proyecto explícito (`COMPOSE_PROJECT_NAME=theburyproject-prod`) y `COMPOSE_ENV_FILES` al `.env` de producción; con D1 corregido, olvidarlo
**aborta** en lugar de actuar sobre preprod. Detalle completo: [backup-restore.md](backup-restore.md) §9–§10.

1. Servidor nuevo: instalar Docker, clonar el repo en el mismo commit (o posterior compatible), restaurar el `.env` desde el paquete de recuperación (§6).
2. `scripts/backup/prepare-dir.sh`; traer los respaldos a `BACKUP_DIR` (copia local, disco externo o `fetch-offsite.sh`).
3. `docker compose up -d db`; `scripts/backup/restore-files.sh --archive files_<ts>.tar.gz` (con `app` detenida) y
   `scripts/backup/restore-sql.sh --backup <full> --target-db TheBuryProjectDb --with-logs [--stopat 'AAAA-MM-DD HH:MM:SS']` (con `--overwrite --confirm-overwrite` solo si la base existe).
4. `docker compose up -d`; comprobar login con la cookie vieja, datos y `DBCC CHECKDB`.

El par `full` + `tar` acreditado por el manifiesto `set_*.txt` da el estado coherente; con logs la base avanza más que los archivos (§10.2 del manual).

## 4. Evidencia de restauración aislada (2026-11-23)

Respaldo **real** de producción (`/srv/bury-backups-prod`, montado `:ro`) restaurado en un SQL Server descartable (proyecto Compose aleatorio, sin puertos
publicados, eliminado al terminar; `residuos: contenedores=0 volumenes=0 temporal=no`), con `restore-test-local-copy.sh --from-backup-dir`:

```
full: TheBuryProjectDb_full_2026-11-23_183038Z.bak  logs posteriores: 1
OK  full + 1 log(s) copiados con sha256 verificado
OK  RESTORE de full + 1 log(s) -> [LocalCopyRestoreTest] ONLINE (logs aplicados = logs copiados)
OK  DBCC CHECKDB sin errores
OK  __EFMigrationsHistory=145 migraciones, 141 tablas
TIEMPOS (s): copia+sha256=1 levantar-SQL=10 restore=3 dbcc=1 total=16
```

**Cómo leer el tiempo:** 16 s es el tiempo real de ESTA corrida con una base **recién instalada, sin datos de negocio** (todas las tablas principales en 0 filas, respaldo de
~33 MB en disco). No se extrapola: la recuperación completa de un servidor (instalar Docker, traer respaldos, restaurar archivos, levantar el ERP) no se midió aquí.
Las mediciones con datos de mayor tamaño están en [backup-restore.md](backup-restore.md) §10.1.

No se restauró nada sobre una base activa ni se tocó `/srv/bury-backups`. Por la regla de no ejecutar dos pruebas pesadas de SQL Server a la vez, se verificó antes que
no hubiera `backup.sh`/`install.sh`/`deploy.sh` en curso (la instalación de producción ya había terminado).

## 5. Pérdida potencial de datos (sin garantías inventadas)

* Cron de producción: full 03:45 (hora local), log de transacciones en los minutos 7/22/37/52, restore-test dominical 05:45. Preprod: 02:30 y `*/15`. Cada `BACKUP_DIR` tiene su propio `flock`.
* Con la VM y su disco `sda` intactos, la pérdida máxima esperable es **la actividad desde el último log** (≈15 min si todos los logs corrieron bien). Eso presupone que los logs corrieron: el monitor lo vigila (§8).
* Si se pierde el disco `sda` o la VM, **se pierden también los respaldos de producción** (mismo disco; sin copia LUKS ni externa). En ese caso la pérdida es la totalidad de lo operado desde la instalación.
* El full diario detiene la `app` unos segundos (del orden de 10–20 s medidos); no es "cero interrupción".

## 6. Secretos pendientes de custodia **fuera de la VM** (acción del propietario)

Nada de esto debe quedar solo en la VM ni junto a los respaldos. Cada elemento va a un lugar distinto de su clave de descifrado.

| Secreto | Dónde está hoy | Qué hacer |
|---|---|---|
| `.env` de producción (SA, admin, usuarios de la app, licencia) | `~/theburyproject-prod-src/.env` (0600) | `scripts/backup/recovery-package.sh create --out <dir fuera de BACKUP_DIR>` (GnuPG AES-256 autenticado; passphrase por terminal; `gpg 2.4.8` y tmpfs presentes). Copiar el `.gpg` a un medio fuera de la VM/PC; la **passphrase en otro lugar** (gestor de contraseñas/papel). Comprobar con `recovery-package.sh verify` |
| `.env` de preproducción | `~/theburyproject1/.env` | Igual (prueba de ensayo; menos crítico) |
| Clave LUKS de la copia local (preprod) | `/etc/bury-backup/local-disk.key` (root 0400, 64 B) | `sudo cat` y copiar **fuera de la VM**. Sin ella el contenedor es irrecuperable |
| Cabecera LUKS | `/etc/bury-backup/local-disk.luks-header` (root 0400, 16 MiB) | `sudo cat` y copiar fuera de la VM. Se usa con `cryptsetup luksHeaderRestore` si se corrompe la cabecera |
| Passphrase del paquete de recuperación | solo en la cabeza/gestor del propietario | No guardarla junto al `.gpg` (regla C09 de `check-custody.sh`) |
| `monitor.env`, `telegram-relay.env` (webhook, token del bot) | `/etc/bury-monitor/` | Reemitibles (se crean otro bot/webhook); anotar dónde |
| Acceso SSH/sudo a la VM | usuario `snow` | Fuera del alcance de los scripts |
| `caddy-root-ca.crt` (pública) | en el tar y en los clientes | Conservar una copia; **la clave privada ya viaja dentro del tar** (`BACKUP_CA_KEY=include`): quien lea el respaldo puede emitir certificados que los clientes aceptan. Pasar a `BACKUP_CA_KEY=seal` es decisión del propietario ([backup-ca-key-custodia.md](backup-ca-key-custodia.md)) |

Ejemplo seguro para las claves LUKS (desde la PC, sin escribirlas en el repo ni en el chat):
`ssh snow@<vm> 'sudo cat /etc/bury-backup/local-disk.key' > <medio-externo>\tbp-local-disk.key` y lo mismo con `local-disk.luks-header`; luego
`sha256sum` de ambos lados. Este cierre **no copió ni leyó el contenido** de ninguna clave.

## 7. Riesgos residuales y estado real

1. **Sin copia independiente del equipo (aceptado temporalmente, NO mitigado).** `BACKUP_REMOTE` vacío en ambas instalaciones; la copia LUKS de preprod está en el mismo host físico (otro VMDK) y no es offsite.
2. **Producción no tiene ni siquiera la copia local cifrada** (`BACKUP_REMOVABLE_DIR=` vacío). `setup-local-disk-copy.sh` usa nombres fijos (`tbp-respaldo`, `/etc/bury-backup/local-disk.key`) y **se niega** a duplicarse (falla cerrado, comprobado por lectura); dar copia local a producción exige parametrizarlo o decidir otro destino. Con D3, ambas instalaciones no pueden compartir el mismo destino por error.
3. **Sin límites de capacidad:** `BACKUP_MAX_TOTAL_GB=0` y `BACKUP_MIN_FREE_GB=0` en ambas. Las dos instalaciones, las imágenes Docker y los volúmenes comparten `/` (99 GB; 72 GB libres hoy). La retención por días (14, mínimo 3) es lo único que acota. Si el disco se llena, `BACKUP DATABASE` falla con exit 2 y queda reflejado en el estado del monitor, pero no hay poda por capacidad.
4. **Monitor de producción: instalado el 2026-11-23 (manualmente, §8).** `bury-monitor-prod.service` está activo; una falla de respaldo de producción alerta por el relay existente, además de dejar `/var/log/bury-backup-prod-errors.log`.
5. **Identidad de respaldo:** ambas bases se llaman `TheBuryProjectDb`; un `.bak` copiado a mano entre directorios es indistinguible por nombre. D1/D3 impiden el cruce por configuración, no por copia manual de archivos. No se añadió validación del contenido (cambiaría el formato de respaldo).
6. `restore-sql.sh` con `--overwrite --confirm-overwrite` sobre la base viva sigue siendo posible por diseño (exige `app` detenida); este cierre no la modificó.
7. `.env` de producción sin `COMPOSE_PROJECT_NAME`: con D1 las ejecuciones manuales sin la variable abortan con un mensaje claro. **Resuelto el 2026-11-23:** `COMPOSE_PROJECT_NAME=theburyproject-prod` agregado al `.env` de producción (el resto del `.env` intacto); `docker compose` a mano ya apunta al proyecto correcto.

**P-06: parcialmente cerrado.** Lo técnico del aislamiento entre instalaciones y la recuperación aislada quedan verificados; el criterio "respaldo independiente del equipo" **sigue pendiente**.

## 8. Alertas ante errores de respaldo

* Cada corrida escribe `$BACKUP_DIR/monitor-status/{full,log,files,run-*,restore-test,custody}.json`; un fallo lo pone en `exit != 0`/`verified false` y el monitor (`monitor.py`) alerta (CRITICAL) por fallo o antigüedad.
* Preprod: `bury-monitor.service` (config en `/etc/bury-monitor/config.json`, `backup_dir=/srv/bury-backups`) → webhook/relay Telegram ya activos.
* **Producción: `bury-monitor-prod.service` instalado y activo desde el 2026-11-23** (instalación manual equivalente al procedimiento de abajo; **no reinstalar ni reemplazar**). Vigila solo `/srv/bury-backups-prod` y el proyecto `theburyproject-prod`; reutiliza el relay existente sin destinos nuevos. Diferencias respecto del procedimiento original, descubiertas al instalar:
  * **Sin heartbeat compartido:** el `monitor.env` copiado de preprod trae `BURY_MONITOR_HEARTBEAT*`; se eliminaron de la copia de producción para que el latido de un monitor no tape la caída del otro.
  * **CA de Caddy de producción:** el monitor valida TLS con el almacén de CAs del sistema, que no confía en la CA propia de la instancia de producción (sin esto: `collector.tls` CRITICAL por `SSLCertVerificationError`). La unidad de producción lleva `Environment=SSL_CERT_FILE=/etc/bury-monitor-prod/caddy-root-ca.crt` (certificado público, huella verificada contra el Caddy en vivo).
  * Las ventanas `offsite-*` y `offsite_restore_test` están retiradas mientras no haya copia externa.
  * Las ventanas de antigüedad (`log` 30/45 min, `full` 26/30 h) no dependen del minuto del cron, así que el escalonado de producción no obliga a cambiarlas.
  * El servicio corre como root (puede leer `sql/`, que es del usuario `mssql`).
* **Instalador para futuras instancias:** `scripts/monitoring/instalar-instancia.sh` (probado por `test-instalar-instancia.sh`, en CI) automatiza el procedimiento y aplica las correcciones anteriores (`--ca-file` instala la CA y fija `SSL_CERT_FILE`; el heartbeat no se copia salvo `--keep-heartbeat`). Por defecto solo genera archivos en un directorio temporal; `--apply` (root) instala y **falla cerrado si la instancia ya existe**, por eso no sirve para producción, que ya está instalada. Prueba aislada realizada (falla y recuperación de respaldos con directorio temporal y receptor local) y `monitor.py --test-channel` entregado al relay.

Procedimiento manual original (referencia histórica; ya aplicado en la VM):

```
sudo mkdir -p /etc/bury-monitor-prod && sudo cp /etc/bury-monitor/{monitor.env,config.json} /etc/bury-monitor-prod/
sudo sed -i '/^BURY_MONITOR_HEARTBEAT/d' /etc/bury-monitor-prod/monitor.env      # no compartir heartbeat
# editar /etc/bury-monitor-prod/config.json: "project":"theburyproject-prod", "backup_dir":"/srv/bury-backups-prod",
#   "state_dir":"/var/lib/bury-monitor-prod", "base_url":"https://tbp-prod:8443"; quitar "offsite-sql"/"offsite-files" de backup_windows
#   mientras no haya copia externa
sudo install -m 644 <caddy-root-ca.crt de prod> /etc/bury-monitor-prod/caddy-root-ca.crt
sudo sed -e 's#/etc/bury-monitor/#/etc/bury-monitor-prod/#g'          -e 's#StateDirectory=bury-monitor#StateDirectory=bury-monitor-prod#' -e 's#/var/lib/bury-monitor#/var/lib/bury-monitor-prod#'          -e 's#TheBuryProject host monitoring#TheBuryProject PROD host monitoring#'          -e 's#^EnvironmentFile=.*#&
Environment=SSL_CERT_FILE=/etc/bury-monitor-prod/caddy-root-ca.crt#' /etc/systemd/system/bury-monitor.service          | sudo tee /etc/systemd/system/bury-monitor-prod.service >/dev/null
sudo systemctl daemon-reload && sudo systemctl enable --now bury-monitor-prod
```
* Verificar con una falla provocada **en un directorio de prueba** (no en producción) antes de dar la alerta por probada.

## 9. Archivos de este cierre

`scripts/backup/lib.sh` (D1), `restore-files.sh`, `install-schedule.sh` (D2), `lib-capacity.sh` (D3), `restore-test-local-copy.sh` (D4), `test-hardening.sh`, `test-retention-capacity.sh`.
