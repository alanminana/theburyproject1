# Offsite + scheduler + monitoreo: activación en el host

Estado 2026-09-23 (main @ ecf9e0f): **preparado, no activado**. No hay proveedor offsite, receptor de alertas ni
host definitivo. Este documento no contiene secretos; los valores reales van solo en `backup-offsite.env`,
`monitor.env` y `.env` (0600, fuera de Git). Detalle de scripts: `backup-restore.md`; monitor: `monitoreo-alertas.md`.

## 1. Proveedores offsite soportados (elegir uno; no hay elección hecha)

Mecanismo único: rclone en el contenedor `backup-offsite`, remoto llamado `offsite`, cifrado opcional con remoto `secure` (crypt).

| Proveedor | `RCLONE_CONFIG_OFFSITE_*` requeridas | Credencial | Ventaja operativa | Dependencia |
|---|---|---|---|---|
| SFTP | `TYPE=sftp`, `HOST`, `USER`, `PASS` (obscure) o `KEY_FILE`, `KNOWN_HOSTS_FILE` | clave SSH o password | Sin cuenta cloud; sirve cualquier Linux propio | Otro servidor en OTRO sitio; mantener su disco/SO; sin hash remoto → verificación byte a byte (más lenta) |
| S3 / compatible | `TYPE=s3`, `PROVIDER`, `ACCESS_KEY_ID`, `SECRET_ACCESS_KEY`, `REGION`, (`ENDPOINT` si no es AWS), bucket | access key con permiso solo sobre el bucket | Durabilidad gestionada; hashes remotos; versionado/object-lock posible | Cuenta y facturación del proveedor; salida a Internet |
| Backblaze B2 | `TYPE=b2`, `ACCOUNT`(keyID), `KEY`(applicationKey), bucket | application key limitada al bucket | Costo bajo, simple | Cuenta B2 |
| Azure Blob | `TYPE=azureblob`, `ACCOUNT`, `KEY`, contenedor | account key (o SAS) | Útil si ya hay Azure | Cuenta Azure |

Cifrado (recomendado, ya generado en `backup-offsite.env`): `RCLONE_CONFIG_SECURE_TYPE=crypt`,
`..._REMOTE=offsite:<bucket-o-ruta>`, `BACKUP_REMOTE=secure:erp` en `.env`. Sin la password crypt los backups remotos son ilegibles.

## 2. Checklist de activación offsite (en el host definitivo)

1. Completar el bloque del proveedor en `backup-offsite.env` (0600) y `BACKUP_REMOTE=secure:erp` en `.env`.
2. SFTP: obtener la host key por un canal independiente, compararla (`ssh-keyscan -t ed25519 host | ssh-keygen -lf -`) y fijarla con `KNOWN_HOSTS_FILE`. S3/B2/Azure: confirmar endpoint/región/bucket.
3. Confirmar que rclone lee la config sin interactividad: `docker compose run --rm -T backup-offsite lsf secure:erp/sql` (debe listar vacío sin error).
4. Upload de prueba: `scripts/backup/backup.sh full` → log con `copia externa sql: subida y VERIFICADA`; `echo $?` = 0.
5. Checksum remoto: `docker compose run --rm -T backup-offsite check /backups/sql secure:erp/sql --one-way --download` sin diferencias.
6. Fetch desde el remoto: en un directorio/host vacío `scripts/backup/fetch-offsite.sh` (baja y valida `.sha256` de origen).
7. `scripts/backup/restore-test.sh` sobre lo descargado; luego, al menos una vez, el drill completo (`backup-restore.md` §10).
8. Confirmar la retención remota (`BACKUP_REMOTE_RETENTION_DAYS=30`) sobre datos reales y que la cuenta puede borrar solo su prefijo.
9. Recién entonces: `sudo scripts/backup/install-schedule.sh --install --user <usuario>`.

`BACKUP_REQUIRE_OFFSITE=true` ya está en `production.env`. **Consecuencia:** mientras `BACKUP_REMOTE` esté vacío, cada
ejecución (incluido el log cada 15 min) termina con exit 6. Es intencional (fail-closed); no instalar el scheduler antes del paso 4.

## 3. Scheduler (Linux, `/etc/cron.d/bury-backup`)

`install-schedule.sh --print` genera exactamente (usuario `deploy` y repo en `/opt/theburyproject` como ejemplo):

```
30 2 * * *   deploy  cd /opt/theburyproject && /bin/bash /opt/theburyproject/scripts/backup/backup.sh all      >/dev/null 2>>/var/log/bury-backup-errors.log
*/15 * * * * deploy  cd /opt/theburyproject && /bin/bash /opt/theburyproject/scripts/backup/backup.sh log      >/dev/null 2>>/var/log/bury-backup-errors.log
30 4 * * 0   deploy  cd /opt/theburyproject && /bin/bash /opt/theburyproject/scripts/backup/restore-test.sh    >/dev/null 2>>/var/log/bury-backup-errors.log
```

- Usuario: `--user`, debe pertenecer al grupo `docker` y escribir `BACKUP_DIR` (`/srv/bury-backups`) y el repo. Por defecto `root`.
- Zona horaria: los horarios de cron usan la hora **local del host**; los nombres de archivo y los logs, UTC. Fijar la zona antes de instalar (`timedatectl`).
- Solapamiento: lock por directorio `BACKUP_DIR/.lock` (pid; se recupera si el pid murió); segunda ejecución → exit 4 sin ruido. `restore-test.sh` no toma ese lock (04:30 vs full 02:30).
- Salida: 0 ok · 1 config · 2 SQL · 3 verificación · 4 ocupado · 5 archivos · 6 offsite · 7 retención · 8 prerequisito · 9 restore · 10 app no volvió healthy.
- **Log de errores** `/var/log/bury-backup-errors.log`: `--install` lo crea con owner = `--user` (grupo primario del usuario), modo `0600`, sin abrirlo a otros. Bug corregido: antes quedaba `root:600`; con un usuario no-root la redirección `2>>` del cron fallaba y **el job no llegaba a ejecutarse**. `--install` falla con mensaje claro si no puede preparar owner/permisos, es idempotente (corrige un modo/owner distinto y conserva el contenido) y `--print` no toca el filesystem.
- **Rotación**: `--install` instala también `/etc/logrotate.d/bury-backup` (semanal o al superar 10 MB, 8 rotaciones, `compress`/`delaycompress`, `missingok`, `notifempty`, `create 0600 <usuario> <grupo>`; sin `copytruncate` porque cron abre el archivo en cada corrida). Ver el texto exacto con `install-schedule.sh --print-logrotate --user <usuario>`. `--uninstall` la quita y conserva el log. Para instalarla a mano en el host: `install-schedule.sh --print-logrotate --user <u> | sudo tee /etc/logrotate.d/bury-backup` y validar con `sudo logrotate -d /etc/logrotate.d/bury-backup`.
- **Restore-test semanal monitoreado**: `restore-test.sh` publica `BACKUP_DIR/monitor-status/restore-test.json` (ver §5). Un fallo ya no queda solo en el log de errores.
- **No instalar el scheduler antes de validar el upload remoto** (paso 4 de §2): con `BACKUP_REQUIRE_OFFSITE=true` y `BACKUP_REMOTE` vacío cada corrida termina en exit 6.
- Tests sin cron/root reales: `bash scripts/backup/test-hardening.sh` (usa `BURY_SCHEDULE_SANDBOX=<dir>`, que redirige `/etc` y `/var/log` a un directorio temporal; solo para pruebas).

## 4. Monitor: variables del receptor real

`/etc/bury-monitor/monitor.env` (0600). Vacías hoy; ningún valor inventado.

| Variable | Qué necesita | Obligatoria |
|---|---|---|
| `BURY_MONITOR_WEBHOOK` | URL HTTPS que acepte POST JSON `{event_id,key,severity,status(firing/resolved),time,detail}` y responda 2xx | Sí para alertas |
| `BURY_MONITOR_TOKEN` | Token Bearer que el receptor exija (header `Authorization`) | Solo si el receptor lo pide |
| `BURY_MONITOR_HEARTBEAT` | URL Push de Uptime Kuma (o similar) en **otro host**, con su token en la URL | Sí para detectar host/agente caído |

No hay identificador de canal: el ruteo a Slack/Telegram/email lo hace el receptor. Idempotencia por `event_id`.
Instalación: ver `monitoreo-alertas.md` (“Instalación en el host definitivo”). Ajustar `config.json`: `project` EXACTO de Compose, `base_url`, `backup_dir=/srv/bury-backups`.

## 5. Matriz de fallos

“Tarda” = latencia hasta la alerta con muestreo de 60 s. Todas las alertas requieren webhook; sin él quedan en cola y en `history` (SQLite) y el monitor emite `collector.channel` WARNING.

| Caso | Quién detecta | Tarda | ¿Alerta? | Evidencia | Limitación |
|---|---|---|---|---|---|
| SQL caído | monitor (`http.ready`, estado db) + healthcheck Docker | ~2 min (hold 120 s) | Sí, CRITICAL | history, `docker ps` | `live` sigue 200; la causa exacta no se asume |
| App caída | monitor (`http.erp/live`, estado app) | ~2 min | Sí | idem | — |
| Caddy caído | monitor (`http.erp` público, estado caddy) | ~2 min | Sí | idem | Si el monitor prueba por la misma ruta que Caddy, no distingue app de Caddy sin `docker` |
| Disco bajo | monitor (disks) | ≤60 s tras cruzar umbral | Sí (70 % warn, 85 % acción, 90 %/10 GiB crit) | history | Sin borrado automático |
| OOM | monitor (stream de eventos Docker + inspect) | inmediato | Sí, latched hasta `--ack-oom` | tabla `oom` | Picos entre muestras de `docker stats` no se ven; si el stream cae → `collector.events` |
| Reinicio de contenedor | monitor (RestartCount) | ≤60 s | WARNING; ≥3 en 10 min CRITICAL | history | — |
| Migración fallida | monitor (exit≠0 de migrate/db-init) + arranque de app bloqueado | ≤60 s | Sí | `docker logs` | One-shot: exit 0 no alerta |
| Full vencido | monitor (`monitor-status/full.json`) | warn 26 h, crit 30 h | Sí | JSON + `backup.log` | Solo cuenta éxito **verificado** |
| Log vencido | monitor (`log.json`) | warn 30 min, crit 45 min | Sí | idem | Log omitido por SIMPLE no renueva |
| Offsite falló | script (exit 6, no queda “ok”) + monitor (`offsite-*`, `run-*`) | en la corrida (≤15 min log / ≤24 h full) | Sí | exit 6, `backup.log`, `bury-backup-errors.log` | El full local sí queda verificado; el fallo remoto es señal aparte |
| Monitor sin webhook | el propio monitor (`collector.channel`) | 1.er ciclo | WARNING **solo visible en logs/history**, no llega a nadie | `delivery.failed`, cola SQLite | Sin canal, nadie se entera; la cola entrega en orden al configurarlo (verificado con prueba) |
| Restore-test semanal falló | monitor (`backup.restore-test`, `restore-test.json`) | ≤60 s tras el fallo | Sí, CRITICAL (último intento con exit ≠ 0 o `verified` ≠ true); se resuelve solo con el siguiente éxito | `restore-test.json`, `backup.log`, `bury-backup-errors.log` | Solo se entera de corridas que llegaron a ejecutarse; si cron no corre, lo cubre el umbral de vencido |
| Restore-test vencido / nunca ejecutado | monitor | warning a 8 días, critical a 10 (`restore_test.warning/critical` en `config.json`) | Sí | idem | Sin archivo: se mide desde la primera observación del monitor (no alerta en la ventana normal previa a la primera ejecución semanal) |
| Host apagado / sin energía / router / sitio | **nadie local** | — | Solo si existe el observador externo | heartbeat vencido en Kuma | **El monitor en el mismo sitio no puede detectar pérdida total.** Requiere observador y canal independientes (heartbeat en otro host/sitio) |

### Evidencia del restore-test (`monitor-status/restore-test.json`)

`{"time","exit","verified","last_success","message"}`: `time`/`exit`/`verified` = **último intento**; `last_success` = último éxito verificado
(se conserva entre fallos; `null` si nunca hubo); `message` = resumen sanitizado (≤200 caracteres). Escritura atómica, 0600, publicada siempre al salir
(éxito, fallo de validación, prerequisito no disponible). Un `.json` ausente no es fallo hasta cumplirse la ventana de gracia.

### Logs de rclone y redacción

stderr de rclone se conserva recortado a 300 caracteres en `backup.log`/`bury-backup-errors.log` **después** de pasar por `sanitize_text`
(`scripts/backup/lib.sh`), que redacta pares clave=valor / clave: valor cuyo nombre contiene password/passwd/pass, secret, token, api/access/account/private key,
credential, authorization o termina en `_key`/`_key_id` (incluye nombres con dígito final, p. ej. `RCLONE_CONFIG_SECURE_PASSWORD2`), `Bearer`/`Basic`,
`usuario:clave@host`, `AKIA…`, JWT, `sig=` de SAS y `AccountKey`. Conserva el resto del mensaje. Es un filtro de mejor esfuerzo: **no** garantiza detectar cualquier secreto.
`RCLONE_CONFIG_SECURE_PASSWORD2` sintético se imprimió durante una prueba preproductiva; al crear la configuración offsite definitiva se generan password y salt **nuevos** (no reutilizar los de prueba).

## 6. Recovery package

`~/secure-preprod/recovery-package/*.enc` (AES-256-CBC, PBKDF2 600 000): checksum OK, descifra OK (verificado 2026-09-23),
fuera del repo y fuera de `BACKUP_DIR`. **Pero** `.enc` y `recovery-passphrase.txt` están en el mismo host y directorio
→ **PREPARADO PERO NO OFFSITE** (mientras `.enc` y passphrase sigan en el mismo host no hay recuperación cifrada offsite). Falta mover el `.enc` y la passphrase, por separado, a custodia fuera del servidor (gestor de contraseñas / soporte offline).

## 7. Datos externos pendientes

Proveedor offsite y sus credenciales/endpoint (y host key si SFTP) · `ERP_DOMAIN` y `ADMIN_EMAIL` reales ·
URL de webhook (y token si aplica) · URL Push de un observador externo · usuario Linux y ruta del host definitivo ·
custodia separada del recovery package y de la password crypt.
