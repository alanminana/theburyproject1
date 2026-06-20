# Despliegue a producción — TheBuryProject

Guía operativa para llevar el ERP a producción de forma segura. Complementa el
hardening aplicado en código (DataProtection, headers, rate limiting, health check).

## 1. Variables de entorno / secretos (obligatorias)

Nunca commitear secretos. Proveer por variables de entorno, user-secrets o el
gestor de secretos de la plataforma. Claves esperadas:

| Clave | Descripción |
|-------|-------------|
| `ConnectionStrings__DefaultConnection` | Cadena de conexión a SQL Server de producción. |
| `Admin__Email` | Email del usuario administrador inicial (SuperAdmin). |
| `Admin__Password` | Password del admin. **Si falta, en producción NO se crea el admin** (por diseño). |
| `Admin__UserName` | (Opcional) username del admin; default `admin`. |
| `MercadoLibre__ClientId` | App ID de Mercado Libre. |
| `MercadoLibre__ClientSecret` | Secret de la app de Mercado Libre. |
| `MercadoLibre__RedirectUri` | URL pública de callback OAuth (debe coincidir con la app ML). |
| `DataProtection__KeysPath` | Ruta persistente para las claves de Data Protection (ver §3). |

> En contenedor usar `__` (doble guion bajo) como separador de sección.

## 2. Base de datos

- El arranque aplica migraciones con `MigrateAsync()` y siembra roles/permisos/sucursales
  y el usuario admin (si `Admin:Password` está configurado).
- **Primera vez**: apuntar a una base vacía. `DbInitializer` evita migrar sobre una base
  con tablas pero sin historial `__EFMigrationsHistory` (para no chocar con esquemas ajenos).
- Recomendado: ejecutar migraciones de forma controlada antes del arranque masivo.

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

- En no-Development la app fuerza HSTS y HTTPS redirection.
- Detrás de reverse proxy (nginx/traefik/Ingress) configurar forwarded headers si se
  necesita el esquema/host real.
- Headers de seguridad activos: `X-Content-Type-Options`, `X-Frame-Options=SAMEORIGIN`,
  `Referrer-Policy=no-referrer`, `X-Permitted-Cross-Domain-Policies=none`.

## 6. Health check

- Endpoint de liveness: `GET /health` (responde `Healthy`). Usar en el orquestador.
- Mejora futura opcional: readiness con chequeo de DB
  (`AddDbContextCheck<AppDbContext>()`, requiere el paquete
  `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore`).

## 7. Docker

- `Dockerfile` usa imágenes .NET 8 (`aspnet:8.0` / `sdk:8.0`), alineadas con el target.
- Recordar montar los volúmenes de §3 y §4.

## 8. Deuda conocida / mejoras pendientes

- **CSP (Content-Security-Policy)**: no se aplica todavía porque las vistas usan
  estilos/scripts inline y SignalR; requiere QA visual antes de activar.
- **Endpoints de diagnóstico** (`/Diagnostico/*`): ahora solo responden en Development
  (404 en producción). No reactivar sin necesidad.
- **Webhook ML** (`/api/mercadolibre/webhook`): anónimo por contrato de ML, protegido con
  rate limiting (1000/min). Evaluar validación adicional por `user_id`/IP si ML lo permite.
