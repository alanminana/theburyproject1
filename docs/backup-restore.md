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
| `caddy-data` (CA interna de Caddy + certificados) | volumen `caddy-data` | No — *acelera* la recuperación | Caddy regenera su CA y certificados internos; **los clientes deberán volver a confiar en la nueva CA raíz** (por eso conviene respaldar `caddy-data`) | tar de archivos (incluido, `BACKUP_INCLUDE_CADDY`) |
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

Un solo mecanismo (rclone, en un contenedor `backup-offsite`) con destino configurable; **el proveedor lo elige el usuario** (ver 6.1).
Las credenciales viven solo en `backup-offsite.env` (0600, fuera de Git y de la imagen); en `.env` solo van `BACKUP_REMOTE` y flags.
**Siempre cifrar** (remoto `crypt`, 6.3): los backups contienen datos de clientes y las claves de Data Protection.

### 6.1 Qué ubicación elegir

| Opción | Ventajas | Riesgos / costo | Cuándo |
|---|---|---|---|
| **Disco USB externo / NAS** en el mismo local (backend `local`, overlay `docker-compose.offsite-disk.yml`) | Sin cuenta ni Internet; barato; restauración rápida (red local/USB) | Mismo sitio: **no protege de incendio, robo o inundación**; el disco desconectado deja el punto de montaje vacío (por eso el centinela, 6.2); hay que rotar/llevar el disco | Mínimo viable; complemento de una copia fuera del local |
| **SFTP a otro equipo / otra sede** | Sin cuenta cloud; otro sitio físico; control propio | Hay que mantener ese servidor (disco, SO, clave SSH); sin hash remoto → verificación byte a byte (más lenta); fijar la host key | Si ya existe otra sede o un VPS propio |
| **Nube S3 compatible / Backblaze B2 / Azure Blob** | Otro sitio, durabilidad gestionada, hashes remotos, versionado/object-lock opcional | Cuenta y facturación; salida a Internet; una access key robada borra todo si no se limita al bucket (usar clave solo-bucket; ideal sin permiso de borrado + retención por el proveedor) | **Recomendado como copia fuera del local** |

Recomendación para un uso local: **dos copias externas** — disco externo cifrado (restauración rápida) + nube cifrada con clave limitada
al bucket (sobrevive al local). El script sube a un solo `BACKUP_REMOTE`; para dos destinos se encadena un remoto `union`/segunda tarea
(no implementado: hoy se elige uno y el segundo se copia a mano con `rsync`/`rclone sync`).
Sea cual sea: guardar `.env`, `backup-offsite.env` y las **dos passwords de crypt** en un gestor de contraseñas **fuera del servidor**.

### 6.2 Procedimiento: disco externo / NAS (probado el 2026-10-29)

1. Montar el disco/NAS en el host (Linux: `/mnt/bury-offsite` por `fstab`/`systemd.mount`; Windows+WSL2: ruta visible en WSL). Debe ser **otro
   disco o equipo**, no `BACKUP_DIR` ni el volumen de datos.
2. En `.env` (el cron/Task Scheduler no heredan variables de entorno: va todo aquí):

   ```text
   COMPOSE_PATH_SEPARATOR=,
   COMPOSE_FILE=docker-compose.yml,docker-compose.offsite-disk.yml
   BACKUP_OFFSITE_DISK_DIR=/mnt/bury-offsite
   BACKUP_REMOTE=secure:erp
   BACKUP_REQUIRE_OFFSITE=true
   BACKUP_OFFSITE_MARKER=.bury-offsite-ok
   ```

3. En `backup-offsite.env` (`chmod 600`): `RCLONE_CONFIG_OFFSITE_TYPE=local` y el bloque `crypt` de 6.3 con `RCLONE_CONFIG_SECURE_REMOTE=offsite:/offsite-disk/erp`.
4. Crear el centinela **una vez, con el disco montado**: `docker compose run --rm -T --no-deps backup-offsite touch secure:erp/.bury-offsite-ok`.
   Si el disco se desconecta, el directorio queda vacío; sin centinela rclone escribiría igual en el disco del servidor y el log diría
   «subida y VERIFICADA» (reproducido). Con `BACKUP_OFFSITE_MARKER` el backup falla con exit 6 antes de subir nada.
5. Probar: `scripts/backup/backup.sh full` → `copia externa sql: subida y VERIFICADA`, exit 0; `scripts/backup/fetch-offsite.sh --list`.

### 6.3 Cifrado (crypt) y otros proveedores

Generar dos passwords (una vez) y ofuscarlas: `docker run --rm rclone/rclone obscure "<password>"` (instrucciones y bloques SFTP/S3/B2/Azure
en `backup-offsite.env.example`). El remoto `secure` cifra **contenido y nombres**: en el destino solo se ven nombres opacos
(verificado). Sin las dos passwords los backups son ilegibles — es la propiedad buscada y también el riesgo: respaldarlas fuera del servidor.
SFTP/nube: completar el bloque `offsite` del proveedor, `BACKUP_REMOTE=secure:erp` (con `RCLONE_CONFIG_SECURE_REMOTE=offsite:<bucket-o-ruta>`)
y, en SFTP, la host key (`KNOWN_HOSTS_FILE`); el centinela no hace falta.

### 6.4 Integridad, retención y fallo (comportamiento verificado)

- Cada subida se **verifica**: con hash remoto, `rclone check`; sin hash (SFTP, `crypt`, disco local cifrado), byte a byte con `--download`.
  Al descargar (`fetch-offsite.sh`) se comparan además los `.sha256` calculados en origen. Un byte alterado en el remoto cifrado hace fallar la
  descarga (`failed to authenticate decrypted block`, exit 6) — probado.
- La subida cubre **todos los backups locales aún no subidos** (no solo los de esta corrida): si un `.trn` no pudo subirse (remoto caído),
  la corrida siguiente lo repone y la cadena de logs remota no queda con un hueco. La verificación activa solo cubre los archivos de la corrida.
- Retención remota: `BACKUP_REMOTE_RETENTION_DAYS` (30) borra por antigüedad **solo** nombres del sistema (`*_full_*.bak*`, `*_log_*.trn*`, `files_*.tar.gz*`);
  archivos ajenos se conservan (probado). No hay mínimo de copias en el remoto: si se necesita, usar la retención del proveedor.
- `BACKUP_REQUIRE_OFFSITE=true`: exit 6 si `BACKUP_REMOTE` está vacío **o** el remoto no responde (SFTP caído, disco sin centinela, credenciales
  inválidas); el mensaje de `backup.log` incluye la causa real (`connection refused`, `no such host`, `permission denied`).
  No se ejecuta la retención local tras un fallo y no se borra ningún backup histórico.
- Sin `BACKUP_REMOTE`, cada corrida avisa con `WARN` que los backups están solo en este servidor.

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

### 10.1 RTO y RPO medidos (ensayo del 2026-10-29, Docker Desktop, base de prueba 13 MB)

Recuperación en una pila **nueva y aislada** (otro proyecto Compose, otros volúmenes/puerto/subred) **solo desde la copia externa**
(`secure:erp` sobre disco externo cifrado; `BACKUP_DIR` vacío): `fetch-offsite.sh` → `restore-files.sh` → `up -d --wait db` → `restore-sql.sh --with-logs`
(full + 2 logs) → `up -d --build --wait`.

| Paso | Tiempo |
|---|---|
| fetch + verificación de integridad (byte a byte y sha256) | 11 s |
| restore de archivos / arranque de `db` / restore SQL (full + 2 logs) | 2 s / 6 s / 8 s |
| db-init + migrate + app + caddy `healthy` | 17 s |
| **Total técnico hasta `/health/ready` = 200** | **44 s** |

Verificado: `/health/live` y `/health/ready` 200; login con el usuario administrador; **conteos y `CHECKSUM_AGG` idénticos** al origen en
`Clientes, Ventas, VentaDetalles, Creditos, Cuotas, MovimientosCaja, AspNetUsers, Productos, __EFMigrationsHistory` (133 migraciones);
una **cookie de sesión emitida en el servidor original sigue válida** en la pila recuperada porque se restauró `bury-keys` (control negativo:
con el volumen de claves vacío la misma cookie es rechazada → 302 al login); roles SQL `app` writer/sin DDL y `migrate` con DDL intactos.
La fila insertada **después del último backup de log** no existe en la pila recuperada (RPO real).

- **RTO**: el tiempo técnico crece con el tamaño de la base y el ancho de banda hacia la copia externa (aprox. descarga + restore); a eso se suma el
  tiempo humano (conseguir el servidor, el `.env`, las passwords de crypt). Con una base del orden de 100 MB-1 GB, esperar minutos, no horas;
  **medirlo con la base real** al activar el destino definitivo.
- **RPO (configuración por defecto)**: datos SQL ≈ **15 min** (backup de log cada 15 min, subido al remoto en la misma corrida; más lo que tarde la subida);
  hasta el último full si el log falla. **Archivos** (uploads, contratos, claves): hasta **24 h** (tar diario). Para un uso local es razonable;
  si se suben documentos/contratos durante el día y 24 h es mucho, ejecutar `backup.sh files` más seguido (detiene `app` ~10-15 s) o agendar un `files` a mediodía.
- Ya queda cubierto el caso «el log remoto quedó con un hueco»: la subida repone los `.trn` pendientes (6.4).

## 11. Verificación de que el restore quedó bien

```bash
curl -fsS https://<dominio>/health/live ; curl -fsS https://<dominio>/health/ready          # 200 / 200
docker compose exec -T db bash -c "SQLCMDPASSWORD=\"\$MSSQL_SA_PASSWORD\" /opt/mssql-tools18/bin/sqlcmd -C -U sa -d TheBuryProjectDb   -Q \"SELECT COUNT(*) AS migraciones FROM __EFMigrationsHistory\""     # = nº de migraciones esperado (133 al 2026-10-29)
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

- **Proveedor externo real no configurado (decisión del usuario)**: probado el 2026-10-29 con (a) disco externo simulado (`local` + `crypt`) y
  (b) servidor SFTP en contenedor (`sftp` + `crypt`), sin credenciales reales. Falta elegir destino (6.1), completar `backup-offsite.env`,
  `BACKUP_REQUIRE_OFFSITE=true`, el centinela (disco/NAS) o la host key (SFTP) y repetir el ensayo de 10.1 con la base real.
- Cifrado `crypt`: **probado** (nombres y contenido cifrados; alteración detectada). Falta guardar las dos passwords fuera del servidor.
- Programación cron: `--print` verificado; `--install` **no ejecutado** (no había un Linux con cron en el entorno de prueba). En Windows las tareas
  las crea `install-windows.ps1` (usan `.env`, así que la configuración del overlay de 6.2 también aplica).
- Permisos verificados en Linux (VM de Docker), no en el disco definitivo del servidor.
- El aviso activo ante fallos depende del monitor (`docs/monitoreo-alertas.md`); sin él, un `exit 6` solo queda en `backup.log` y `monitor-status/`.
- Sin retención "mensual/anual": si se necesita histórico largo, definirla (p. ej. conservar el full del día 1 de cada mes).
- El backup no incluye `.env`/`backup-offsite.env`: mantener su copia manual y actualizada.
