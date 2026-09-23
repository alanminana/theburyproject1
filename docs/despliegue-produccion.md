# Despliegue a producción — TheBuryProject

Guía operativa para llevar el ERP a producción de forma segura. Complementa el
hardening aplicado en código (DataProtection, headers, rate limiting, health check).

## 1. Variables de entorno / secretos (obligatorias)

Nunca commitear secretos. Proveer por variables de entorno, user-secrets o el
gestor de secretos de la plataforma. Claves esperadas:

| Clave | Descripción |
|-------|-------------|
| `ConnectionStrings__DefaultConnection` | Cadena de conexión a SQL Server de producción. |
| `ErpDb__User` / `ErpDb__Password` | Obligatorios en Production, sin `sa` ni `CHANGE_ME`. Se aplican sobre la cadena base sin credenciales mediante `SqlConnectionStringBuilder`. Development/Testing mantienen LocalDB/Windows Auth. |
| `Admin__Email` | Email del usuario administrador inicial (SuperAdmin). |
| `Admin__Password` | Obligatoria en Production si el administrador todavía no existe; su ausencia impide completar el bootstrap. Retirarla después del primer login y cambio de contraseña. Un administrador existente permite migrar sin este secreto. |
| `Admin__UserName` | (Opcional) username del admin; default `admin`. |
| `MercadoLibre__ClientId` | App ID de Mercado Libre. |
| `MercadoLibre__ClientSecret` | Secret de la app de Mercado Libre. |
| `MercadoLibre__RedirectUri` | URL pública de callback OAuth (debe coincidir con la app ML). |
| `DataProtection__KeysPath` | Ruta persistente para las claves de Data Protection (ver §3). |

> En contenedor usar `__` (doble guion bajo) como separador de sección.

Inventario, separación por servicio, permisos de archivos, rotación y evidencia de pruebas:
[Secretos y configuración sensible](secretos-produccion.md). User-secrets se reserva para Development.

## 2. Base de datos

- **Docker**: las migraciones y seeds (roles, permisos, sucursales, plantilla de contrato, usuario admin si
  `Admin:Password` está configurado) los ejecuta el servicio one-shot `migrate` (`TheBuryProyect.dll --migrate`,
  mismo `DbInitializer`); `app` arranca con `Database:InitializeOnStartup=false` y no toca el esquema. Orden:
  `db → db-init → migrate → app → caddy`. Si `migrate` termina con código ≠ 0, `app` no arranca.
- **Desarrollo Windows (LocalDB)**: sin cambios; `Database:InitializeOnStartup` no está definido (default `true`)
  y la app migra/siembra al arrancar como siempre.
- **Primera vez**: apuntar a una base vacía. `DbInitializer` evita migrar sobre una base
  con tablas pero sin historial `__EFMigrationsHistory` (para no chocar con esquemas ajenos).
- Nueva migración en producción: `docker compose up -d --build` (o `docker compose run --rm migrate`); `migrate`
  aplica lo pendiente y termina. Un `migrate` sin pendientes es un no-op (seeds idempotentes, sin duplicados).

## 3. Data Protection (CRÍTICO en contenedores / multi-instancia)

Las claves de Data Protection cifran: tokens de Mercado Libre guardados en DB,
cookies de autenticación y tokens antiforgery.

- La app persiste las claves en `DataProtection:KeysPath` (default: `<ContentRoot>/keys`).
- **En Docker/Kubernetes montar un volumen persistente** en esa ruta y compartirlo
  entre instancias. Sin volumen, al reiniliar el contenedor:
  - se invalidan los refresh tokens de ML (obliga re-OAuth),
  - se desloguea a todos los usuarios,
  - se rompen formularios con antiforgery en vuelo.

Ejemplo (docker run): `-e DataProtection__KeysPath=/keys -v bury-keys:/keys`

## 4. Almacenamiento de archivos subidos

`wwwroot/uploads` guarda documentos e imágenes. En contenedor montar un volumen
persistente para no perderlos en cada redeploy.

Ejemplo: `-v bury-uploads:/app/wwwroot/uploads`

## 5. HTTPS / red

- Caddy (servicio `caddy`, `Caddyfile`) termina TLS (ACME + renovación) y redirige HTTP→HTTPS; es lo único publicado
  (80/443). `app` (Kestrel, HTTP 8080) y `db` (1433) solo son alcanzables por la red interna `bury-net`.
- Requiere `ERP_DOMAIN` real en `.env` con DNS apuntando al servidor y 80/443 abiertos. Firewall: permitir 80/443; no exponer 8080 ni 1433.
- Forwarded Headers (`Program.cs`, primer middleware): solo confía en X-Forwarded-* de loopback y de
  `ForwardedHeaders__KnownNetworks` (= `BURY_NET_SUBNET`, subred fija de `bury-net`). No usar `ASPNETCORE_FORWARDEDHEADERS_ENABLED`.
- En no-Development la app aplica HSTS (solo sobre HTTPS resuelto vía forwarded headers). `UseHttpsRedirection()` solo se
  registra si se define `HTTPS_PORT`; con Caddy no hace falta (la redirección es del proxy).
- Desarrollo: `-f docker-compose.dev.yml` publica Kestrel en `127.0.0.1:8080` y no arranca Caddy (salvo `--profile tls`).
- Headers de seguridad activos: `X-Content-Type-Options`, `X-Frame-Options=SAMEORIGIN`,
  `Referrer-Policy=no-referrer`, `X-Permitted-Cross-Domain-Policies=none`.

## 6. Health check

- `GET /health/live`: liveness. Solo confirma que el proceso ASP.NET responde; no depende de SQL Server. 200 `Healthy`.
- `GET /health/ready`: readiness. Ademas verifica que `AppDbContext` puede conectarse a SQL Server (`CanConnectAsync`, sin leer ni escribir datos, timeout 4 s). 200 `Healthy` / 503 `Unhealthy`.
- Las respuestas son texto plano sin detalle de checks, excepciones ni connection string.
- El `HEALTHCHECK` del Dockerfile usa `/health/ready`. `/health` fue retirado.
- Con SQL caido, `app` pasa a `unhealthy` pero no se reinicia (Docker no reinicia por health); se recupera solo cuando SQL vuelve.


## 7. Docker

- `Dockerfile` usa imágenes .NET 10 (`aspnet:10.0` / `sdk:10.0`), alineadas con el target y con `global.json`.
- Recordar montar los volúmenes de §3 y §4.

## 7.1 Backups y recuperación

Documentación completa: **[backup-restore.md](backup-restore.md)**. Resumen operativo:

```bash
scripts/backup/backup.sh                               # backup manual completo (SQL + archivos + copia externa + retención)
sudo scripts/backup/install-schedule.sh --install      # programación: full diario, log cada 15 min, prueba de restore semanal
scripts/backup/restore-test.sh                         # prueba de restore no destructiva (base temporal)
```

- SQL: full diario + backup del log cada 15 min (la DB está en recovery model FULL) → RPO ≈ 15 min. Destino: `BACKUP_DIR` (`.env`),
  fuera del volumen de datos de SQL. Retención local 14 días, remota 30.
- Archivos: `keys` (Data Protection), `uploads`, `App_Data` y `caddy-data`, con `app` detenida ~10–15 s para un par consistente con el SQL.
- Copia externa por rclone (SFTP/S3/B2/Azure…): `backup-offsite.env` + `BACKUP_REMOTE`; en producción `BACKUP_REQUIRE_OFFSITE=true`.
- **`.env` y `backup-offsite.env` no van en los backups**: guardarlos aparte (gestor de contraseñas). Sin ellos no se puede recuperar.
- Recuperación desde cero: `docs/backup-restore.md` §10.

## 8. Deuda conocida / mejoras pendientes

- **CSP (Content-Security-Policy)**: no se aplica todavía porque las vistas usan
  estilos/scripts inline y SignalR; requiere QA visual antes de activar.
- **Endpoints de diagnóstico** (`/Diagnostico/*`): ahora solo responden en Development
  (404 en producción). No reactivar sin necesidad.
- **Webhook ML** (`/api/mercadolibre/webhook`): anónimo por contrato de ML, protegido con
  rate limiting (1000/min). Evaluar validación adicional por `user_id`/IP si ML lo permite.

## Credenciales SQL: administrativa, migraciones y aplicación

| Servicio | Credencial | Tareas |
|---|---|---|
| `db`, `db-init` | `MSSQL_SA_PASSWORD` (`sa`) | Servidor SQL; crear base, logins y usuarios; conceder permisos (`docker/db-init/`). |
| `migrate` | `ERP_MIGRATION_USER` / `ERP_MIGRATION_PASSWORD` | Migraciones EF (DDL) y seeds. Único servicio que recibe `Admin__*`. |
| `app` | `ERP_DB_USER` / `ERP_DB_PASSWORD` | CRUD, Identity, health check. **Sin DDL. No recibe `sa` ni credenciales de migración.** |

| Usuario | db_datareader | db_datawriter | db_ddladmin | db_owner | sysadmin |
|---|:-:|:-:|:-:|:-:|:-:|
| `ERP_DB_USER` (app) | sí | sí | **no** | no | no |
| `ERP_MIGRATION_USER` (migrate) | sí | sí | sí | no | no |

`db-init` es idempotente: crea ambos logins (sin roles de servidor) y usuarios, re-sincroniza sus passwords con `.env`
(rotación: cambiar la variable y `docker compose up -d`) y **revoca `db_ddladmin`/`db_owner` a la app** si un despliegue
anterior se los había dado. `ERP_DB_USER` y `ERP_MIGRATION_USER` deben ser distintos. Los objetos creados por `migrate`
quedan en `dbo`, por lo que los roles de la app los cubren automáticamente.

Desarrollo local en Windows no cambia: LocalDB con `Trusted_Connection=True`. La autenticación SQL dedicada aplica solo a Docker/producción.
