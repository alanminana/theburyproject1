# TheBuryProject

ERP web para comercios: ventas (contado, tarjeta y crédito personal), cotizador, caja, clientes, productos e inventario, proveedores y órdenes de compra, créditos y mora, seguridad por roles y permisos.

Aplicación ASP.NET Core (.NET 8, MVC + Razor) con Entity Framework Core sobre SQL Server. En producción corre en contenedores Docker detrás de Caddy (HTTPS), accesible solo por LAN/VPN.

```text
Clientes LAN/VPN ── https://tbp ──> Caddy (443) ──> app:8080 ──> db:1433 (SQL Server Express)
```

## Instalar en un servidor

Un solo instalador para Windows 10/11 (Docker Desktop + WSL2) y Linux. Genera el `.env` con secretos aleatorios, construye la imagen, despliega, y en Windows configura firewall, backups programados y perfil de energía.

1. Instalar WSL2 y Docker Desktop (con la integración WSL activada para tu distro).
2. Clonar el repositorio **dentro de WSL** (no en `C:\`):

   ```powershell
   wsl -- git clone https://github.com/alanminana/theburyproject1.git ~/theburyproject1
   ```

3. En PowerShell **como administrador**, ejecutar el instalador. Si no se indica `-Distro`, **lista las distros de WSL instaladas y pregunta cuál usar** (no siempre se llama `Ubuntu`):

   ```powershell
   cd \wsl.localhost\<distro>\home\<usuario>\theburyproject1\scripts\install
   powershell -ExecutionPolicy Bypass -File .\install-windows.ps1 -AdminEmail correo@dominio.com -DryRun   # primero en seco
   powershell -ExecutionPolicy Bypass -File .\install-windows.ps1 -AdminEmail correo@dominio.com
   ```

   En Linux: `bash scripts/install/install.sh --admin-email correo@dominio.com --schedule`.

Guía completa (clientes, certificado, arranque tras corte de luz, actualización, problemas frecuentes): [docs/instalacion-servidor.md](docs/instalacion-servidor.md).

## Desarrollo local

Requisitos: SDK de .NET 8 (ver `global.json`), SQL Server o LocalDB, Node.js (solo para los tests E2E).

```bash
dotnet restore
dotnet build
dotnet run --project TheBuryProyect.csproj
```

Tests:

```bash
dotnet test TheBuryProyect.Tests          # unitarios e integración (xUnit, SQLite en memoria)
npx playwright test                       # E2E de navegador (ver docs/ci-playwright-e2e.md)
```

## Estructura

| Carpeta | Contenido |
|---|---|
| `Controllers/`, `Areas/` | Controladores MVC |
| `Services/`, `Data/`, `Models/`, `ViewModels/` | Lógica de negocio sobre `AppDbContext`, entidades y modelos de vista |
| `Views/`, `wwwroot/` | Vistas Razor, CSS (paleta centralizada en `palette.css`) y JS |
| `Migrations/` | Migraciones de EF Core |
| `TheBuryProyect.Tests/`, `e2e/` | Tests de .NET y Playwright |
| `scripts/` | Instalación, deploy/rollback y backups |
| `docker/`, `docker-compose*.yml`, `Caddyfile` | Contenedores y proxy |
| `docs/` | Documentación (estándar de UI, backups, despliegue, cierres de módulos) |

## Documentación útil

- [Instalación del servidor](docs/instalacion-servidor.md)
- [Despliegue a producción](docs/despliegue-produccion.md) · [Deploy y rollback](docs/deploy-rollback.md)
- [Backups y restauración](docs/backup-restore.md)
- [Estándar de UI](docs/ui/ERP-UI-STANDARD.md)
- [CI y E2E](docs/ci.md)

## Seguridad

No subir `.env`, claves ni backups al repositorio. El `.env` contiene todos los secretos y no está incluido en los backups: guardarlo en un gestor de contraseñas.
