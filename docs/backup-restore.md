# Backups y recuperación — TheBuryProject (Docker + SQL Server Express)

Objetivo: poder **perder el servidor y recuperar el ERP**, no solo tener un `.bak`. Todo el código está en
[`scripts/backup/`](../scripts/backup/) (versionado); la infraestructura (directorio de backups, herramientas) está en
`docker-compose.yml` (servicios del profile `tools`, que **nunca** arrancan con `docker compose up`).

Requisitos del host: Linux con `bash`, Docker Engine y **Docker Compose ≥ 2.24** (usa `env_file: required: false`).
Si los scripts no conservaron el bit ejecutable (clonado desde Windows): `git update-index --chmod=+x scripts/backup/*.sh` o `chmod +x scripts/backup/*.sh`.

## 1. Qué se respalda y por qué

| Dato | Dónde vive | Crítico | Si se pierde | Cómo se respalda |
|---|---|:-:|---|---|
| Base `TheBuryProjectDb` | volumen `bury-sqldata` | **Sí, irrecuperable** | se pierde el negocio | `BACKUP DATABASE` full diario + `BACKUP LOG` cada 15 min |
| Claves Data Protection | volumen `bury-keys` (`/keys`) | **Sí** | Se invalidan cookies y antiforgery **y los tokens de Mercado Libre guardados cifrados en la DB quedan ilegibles** (re-OAuth). Verificado: sin las claves, una sesión previa deja de valer | tar de archivos |
| `uploads` (documentos/imágenes de clientes) | volumen `bury-uploads` | **Sí, no regenerable** | se pierden los documentos | tar de archivos |
| `App_Data` (contratos de venta a crédito generados) | volumen `bury-appdata` | **Sí, no regenerable** | se pierden los contratos emitidos | tar de archivos |
| `caddy-data` (cuenta ACME + certificados) | volumen `caddy-data` | No — *acelera* la recuperación | Caddy reemite el certificado (necesita DNS y 80/443; riesgo de rate-limit de Let's Encrypt si se reemite muchas veces) | tar de archivos (incluido, `BACKUP_INCLUDE_CADDY`) |
| `caddy-config` | volumen `caddy-config` | No | se regenera desde el `Caddyfile` | **no** se respalda |
| Esquema/seeds (roles, permisos, plantilla) | migraciones en la imagen | No | los recrea el servicio `migrate` | no |
| **`.env` y `backup-offsite.env`** | el servidor | **Sí** | sin ellos no se puede levantar el stack ni **descargar** la copia externa | **NO están en los backups** (son secretos): guardarlos en un gestor de contraseñas fuera del servidor |

## 2. Estrategia

| Elemento | Valor | Razón |
|---|---|---|
| Recovery model de la DB | **FULL** (ya lo es: hereda de `model`) | Permite recuperar hasta el último log backup. **Con FULL y solo fulls diarios el `.ldf` crece sin límite**, así que los log backups no son opcionales |
| SQL full | diario 02:30 (hora local) | Base chica (72 MB) → segundos; la ventana nocturna no molesta |
| SQL log | cada 15 min | RPO ≈ 15 min. Cada log sale de pocos KB a pocos MB |
| Differential | **no** | Con esta base un full es barato; agregarlo suma pasos de restore sin beneficio real |
| Archivos | diario, junto al full, con `app` detenida ~10–15 s | Par SQL+archivos consistente (ver §7) |
| Retención local | 14 días (por nombre, UTC) y **siempre** los 3 más recientes de cada tipo | Cubre "descubrí tarde que estaba mal"; el mínimo evita vaciar el directorio si los backups dejan de generarse |
| Retención remota | 30 días | Más larga: es la copia que sobrevive al servidor |
| Copia externa | rclone (SFTP / S3 / B2 / Azure Blob…), obligatoria en producción (`BACKUP_REQUIRE_OFFSITE=true`) | Un backup solo en el mismo servidor no protege de perder el servidor |
| Prueba de restore | semanal, automática (`restore-test.sh`) | `RESTORE VERIFYONLY` **no** demuestra que el backup se pueda restaurar |
| RPO | ≈ 15 min (datos SQL); archivos: hasta 24 h (lo subido tras el último backup diario de archivos se pierde) | |

Si la base pasara a `SIMPLE`: solo se podría recuperar hasta el último full (RPO 24 h) y no hay log backups; `backup.sh log`
lo detecta y lo informa con `WARN` (no falla).

**Express**: no soporta `BACKUP … WITH COMPRESSION` (error 1844, verificado). `BACKUP_SQL_COMPRESSION=auto` lo omite en Express y
lo usa en otras ediciones. Límite de 10 GB de datos por base (el log no cuenta).

## 3. Backup manual

```bash
# desde la raíz del repo, con el stack corriendo
scripts/backup/backup.sh            # = all: full SQL + archivos (detiene app unos segundos) + copia externa + retención
scripts/backup/backup.sh full       # solo full SQL (online)
scripts/backup/backup.sh log        # solo log de transacciones (online)
scripts/backup/backup.sh files      # solo archivos (detiene app unos segundos)
echo $?                             # 0 = OK; ver códigos en §12
```

Qué hace `all`, en orden: prerequisitos → lock → **detiene `app`** → `BACKUP DATABASE … WITH CHECKSUM` → existencia y tamaño > 0 →
`RESTORE VERIFYONLY … WITH CHECKSUM` → `sha256` → tar de archivos (+ `gzip -t`, listado del tar, `sha256`) → **`app` vuelve a arrancar
y se espera a `healthy`** (también si algo falló: `trap`) → copia externa + verificación → retención (solo si todo lo anterior fue
OK). Si `app` estaba apagada antes, no se la arranca.

## 4. Backup automático

```bash
scripts/backup/install-schedule.sh --print                  # revisar qué se instalaría
sudo scripts/backup/install-schedule.sh --install           # /etc/cron.d/bury-backup
sudo scripts/backup/install-schedule.sh --uninstall
# opciones: --full-at 02:30  --log-every 15  --restore-test-day 0  --user root
```

Instala: `backup.sh all` diario, `backup.sh log` cada 15 min y `restore-test.sh` semanal. Los horarios de cron usan la **hora local
del servidor**; los nombres de archivo usan **UTC** siempre. stdout se descarta (va a `backup.log`); stderr va a
`/var/log/bury-backup-errors.log`. `--install` deja ese log con owner = usuario del cron (0600) e instala `/etc/logrotate.d/bury-backup`; la prueba de restore semanal publica `monitor-status/restore-test.json` para el monitor. Detalle y checklist: `docs/offsite-monitoring-activacion.md`. Un `log` que coincide con un `all` en curso sale con código 4 (omitido, sin ruido).
Alternativa a cron: un `systemd` timer que ejecute los mismos tres comandos.

## 5. Ubicación

`BACKUP_DIR` en `.env` (default `./backups`; **en producción usar una ruta fuera del repo, idealmente otro disco**, p. ej.
`/srv/bury-backups`):

```text
$BACKUP_DIR/
  sql/    TheBuryProjectDb_full_2026-09-21_233000Z.bak (+ .sha256)   TheBuryProjectDb_log_2026-09-21_234500Z.trn (+ .sha256)
  files/  files_2026-09-21_233000Z.tar.gz (+ .sha256)     keys/ uploads/ appdata/ caddy-data/
  logs/   backup.log (rota a 5 MB, conserva 3)
  .bury-backup-root   marca sin la cual la retención se niega a borrar nada
```

`sql/` se monta en `db` como `/backups/sql` (separado de `bury-sqldata`). Permisos (verificados en Linux): `BACKUP_DIR` `700`;
`sql/` `750` dueño `10001` (usuario `mssql`), `.bak` `640`; `files/` `600 root`. Un usuario cualquiera del host **no** puede leerlos.
Para copiar fuera del servidor a mano: `sudo rsync -a $BACKUP_DIR/ destino:` (o usar la copia externa automática).

## 6. Copia externa

Un solo mecanismo (rclone, en un contenedor) con destino configurable; no se eligió proveedor.

1. `cp backup-offsite.env.example backup-offsite.env`, `chmod 600`, descomentar **un** bloque (SFTP, S3, B2, Azure Blob…).
   Está en `.gitignore` y `.dockerignore`; las credenciales del proveedor viven solo en el contenedor `backup-offsite`.
2. En `.env`: `BACKUP_REMOTE=offsite:<ruta>` (ej. `offsite:mi-bucket/erp`), `BACKUP_REQUIRE_OFFSITE=true`.
3. **Cifrado**: encadenar un remoto `crypt` de rclone sobre `offsite` (instrucciones al final de `backup-offsite.env.example`) y usar
   `BACKUP_REMOTE=secure:erp`. Guardar la clave de cifrado **fuera del servidor**.
4. Probar: `scripts/backup/backup.sh full` y `scripts/backup/fetch-offsite.sh --list`.

Cada subida se **verifica**: si el remoto expone hash, con `rclone check`; si no (p. ej. SFTP sin shell), byte a byte con
`--download`. Al descargar (`fetch-offsite.sh`) además se comparan todos los `.sha256` calculados en origen. Sin `BACKUP_REMOTE`,
cada corrida avisa con `WARN` que los backups están solo en este servidor (y falla con exit 6 si `BACKUP_REQUIRE_OFFSITE=true`).
En SFTP, fijar la clave del host (`known_hosts_file`) en producción; las pruebas se hicieron sin validación de host key.

## 7. Consistencia de los archivos

Solo `app` escribe en `keys`, `uploads` y `App_Data`. Se eligió la opción más simple y segura: **detener `app` ~10–15 s** durante
`backup.sh all`/`files`, leer los volúmenes en **solo lectura** con tar y volver a arrancarla. Así (a) no hay archivos a medias y
(b) el full SQL y el tar son un **par coherente** (los documentos referenciados por la DB existen en el tar). Snapshots de
volumen/LVM lo evitarían, pero exigen infraestructura que no se asume. `caddy-data` se lee sin detener Caddy (escrituras raras y
atómicas). Para no detener `app`: `BACKUP_STOP_APP=false` (aceptando que el par SQL/archivos puede diferir por segundos).

## 8. Retención

`backup.sh` la aplica al final de cada `all`/`full`/`files` exitoso:

- Se borran solo archivos cuyo **nombre coincide exactamente** con el patrón que generan los scripts (`<db>_full_<UTC>.bak`,
  `<db>_log_<UTC>.trn`, `files_<UTC>.tar.gz`) y su `.sha256`. Archivos ajenos, otras bases y subdirectorios **no se tocan**.
- Antigüedad por el **timestamp del nombre**, no por mtime.
- Siempre se conservan los `BACKUP_RETENTION_MIN_KEEP` (3) más recientes de cada tipo.
- Los `.trn` solo se borran si son anteriores al full más antiguo que se conserva.
- Guardas: `RETENTION_DAYS` entero ≥ 1, `DB_NAME` validado, rutas literales dentro del contenedor y marca `.bury-backup-root`
  obligatoria (si el volumen no está montado, no borra nada).
- Remoto: `BACKUP_REMOTE_RETENTION_DAYS` (30; `0` = no borrar) con `rclone delete --min-age` filtrado por esos mismos nombres.

## 9. Restore

### 9.1 Restore de SQL (a una base distinta, no destructivo)

```bash
scripts/backup/restore-sql.sh --list
scripts/backup/restore-sql.sh --backup TheBuryProjectDb_full_2026-09-21_233000Z.bak --target-db TheBuryProjectDb_Check --with-logs
# punto en el tiempo (UTC, como los nombres): hasta el último log <= ese instante
scripts/backup/restore-sql.sh --backup ..._full_....bak --target-db X --with-logs --stopat '2026-09-21 14:05:00'
```

- Sin `--with-logs` recupera solo hasta el full; con `--with-logs` aplica los `.trn` posteriores en orden.
- Antes de restaurar: existe, tamaño > 0, `sha256`, `RESTORE VERIFYONLY`. Los archivos de datos se reubican a
  `/var/opt/mssql/data/<destino>*.mdf|ldf` (no pisan los de otra base).
- **Si la base destino ya existe, aborta** (exit 9). Reemplazarla exige `--overwrite --confirm-overwrite <mismo nombre>` y, si es
  la base del ERP, `app` detenida.

### 9.2 Prueba de restore periódica

```bash
scripts/backup/restore-test.sh      # último full + logs -> <db>_RestoreTest, DBCC CHECKDB, migraciones/tablas, y la elimina
```

Falla (exit 9) si el restore o `DBCC CHECKDB` fallan, si no hay historial de migraciones, o si el backup tiene *más* migraciones que
la base viva. Lo instala `install-schedule.sh` semanalmente. Es la prueba que sustenta el resto: **un backup no probado no existe**.

### 9.3 Restore de archivos

```bash
docker compose stop app                       # obligatorio
scripts/backup/restore-files.sh --list
scripts/backup/restore-files.sh --archive files_2026-09-21_233000Z.tar.gz          # aborta si algún volumen tiene contenido
scripts/backup/restore-files.sh --archive files_2026-09-21_233000Z.tar.gz --force  # vacía keys/uploads/appdata/caddy-data y restaura
docker compose start app
```

Conserva dueños (uid 1654 de `app`). Verifica `sha256` y el tar antes de tocar nada.

## 10. Recuperación completa: "perdí el servidor"

Servidor nuevo (Linux + Docker + Compose ≥ 2.24 + `git`). **No** arrancar todavía el stack completo: si `app` arranca antes de restaurar
las claves, genera claves nuevas y las cookies/tokens viejos quedan invalidados.

```bash
# 1. Código y secretos
git clone <repo> /opt/bury && cd /opt/bury
#    restaurar .env y backup-offsite.env desde el gestor de contraseñas (chmod 600); BACKUP_DIR con espacio suficiente

# 2. Traer los backups desde la copia externa (verifica hashes y sha256)
scripts/backup/fetch-offsite.sh --list
scripts/backup/fetch-offsite.sh

# 3. Archivos persistentes (crea los volúmenes vacíos; app aún no existe)
scripts/backup/restore-files.sh --archive files_<ultimo>.tar.gz

# 4. SQL: levantar SOLO db y restaurar con el nombre real de la base (full + logs)
docker compose up -d --wait db
scripts/backup/restore-sql.sh --backup TheBuryProjectDb_full_<ultimo>.bak --target-db TheBuryProjectDb --with-logs

# 5. Resto del stack: db-init re-vincula los usuarios SQL (SIDs nuevos), migrate valida el esquema (no-op si está al día), app, caddy
docker compose up -d --build --wait
```

Usar el **último full anterior al desastre** y los logs posteriores. Si el último full o algún log estuviera dañado, el script lo
rechaza (`sha256`/`VERIFYONLY`) y hay que retroceder a un full anterior (el remoto conserva 30 días).

## 11. Verificación de que el restore quedó bien

```bash
curl -fsS https://<dominio>/health/live ; curl -fsS https://<dominio>/health/ready          # 200 / 200
docker compose exec -T db bash -c "SQLCMDPASSWORD=\"\$MSSQL_SA_PASSWORD\" /opt/mssql-tools18/bin/sqlcmd -C -U sa -d TheBuryProjectDb   -Q \"SELECT COUNT(*) AS migraciones FROM __EFMigrationsHistory\""     # = nº de migraciones esperado (106 al escribir esto)
docker compose exec -T db bash -c "SQLCMDPASSWORD=\"\$MSSQL_SA_PASSWORD\" /opt/mssql-tools18/bin/sqlcmd -C -U sa -d TheBuryProjectDb   -Q \"SELECT name, IS_ROLEMEMBER('db_datawriter', name) AS writer, IS_ROLEMEMBER('db_ddladmin', name) AS ddl        FROM sys.database_principals WHERE name IN (N'<ERP_DB_USER>', N'<ERP_MIGRATION_USER>')\""   # app: writer=1 ddl=0 | migraciones: ddl=1
```

Además: iniciar sesión en la UI, abrir Dashboard y un cliente/venta reciente, abrir un documento subido (`/uploads/...`) y un
contrato de `App_Data`; y comprobar que una sesión abierta **antes** del desastre sigue válida (claves DP restauradas).

## 12. Códigos de salida (base para alertas futuras)

| Código | Significado |
|:-:|---|
| 0 | OK |
| 1 | uso/configuración inválida (`BACKUP_DIR` peligroso o no creable, variable mal definida…) |
| 2 | `BACKUP DATABASE`/`BACKUP LOG` falló |
| 3 | verificación falló: archivo ausente/vacío, `VERIFYONLY`, `sha256` (el `.bak` sospechoso se pone en cuarentena como `*.failed-verify`) |
| 4 | otra ejecución en curso (omitido) |
| 5 | backup de archivos falló |
| 6 | copia externa falló, no se pudo verificar, o es obligatoria y no está configurada |
| 7 | retención falló |
| 8 | prerequisito: docker/compose ausente, `db` detenido o SQL sin responder |
| 9 | restore rechazado o fallido (`restore-*.sh`, `restore-test.sh`) |
| 10 | `app` no volvió a `healthy` tras detenerla para el backup |

Nunca se reporta éxito sin haber verificado. Ante cualquier fallo no se ejecuta retención. Logs en `$BACKUP_DIR/logs/backup.log`
(sin passwords ni connection strings: `sa` se lee dentro del contenedor `db`; el proveedor externo solo en `backup-offsite`).

## 13. Seguridad

- Los backups **nunca** se sirven por web: ni `app` ni `caddy` montan `BACKUP_DIR`; verificado que `/backups/...` responde 404.
- Fuera de Git (`/backups/`, `*.bak`, `*.trn`, `backup-offsite.env`) y fuera del build context/imagen (`.dockerignore`): verificado
  con un build de canarios y buscando en la imagen. Los scripts **sí** están versionados.
- `BACKUP_DIR` `700`; los backups contienen datos de clientes **y las claves de Data Protection**: cifrar la copia externa (§6).
- Las herramientas (`backup-files`, `restore-files`, `backup-offsite`) no reciben credenciales de SQL ni del ERP. `sa` solo se usa
  dentro de `db` (`docker compose exec`); no se crearon usuarios ni permisos nuevos.

## 14. Pendientes específicos de backup/restore

- **Copia externa real no verificada**: los ensayos usaron un servidor SFTP local de prueba (transporte de red real, no un proveedor
  externo). Falta configurar el destino real, `BACKUP_REQUIRE_OFFSITE=true`, y (SFTP) pinnear la host key.
- Cifrado `crypt` del remoto: documentado, **no probado**.
- Programación cron: `--print` verificado; `--install` **no ejecutado** (no había un Linux con cron en el entorno de prueba).
- Permisos verificados en Linux (VM de Docker), no en el disco definitivo del servidor.
- Falta un aviso activo ante fallos (los códigos de salida están listos; el monitoreo queda para el bloque siguiente).
- Sin retención "mensual/anual": si se necesita histórico largo, definirla (p. ej. conservar el full del día 1 de cada mes).
- El backup no incluye `.env`/`backup-offsite.env`: mantener su copia manual y actualizada.
