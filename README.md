# TheBuryProject

ERP web para comercios: ventas (contado, tarjeta y crédito personal), cotizador, caja, clientes, productos e inventario, proveedores y órdenes de compra, créditos y mora, seguridad por roles y permisos.

Aplicación ASP.NET Core (.NET 10, MVC + Razor) con Entity Framework Core sobre SQL Server. En producción corre en contenedores Docker detrás de Caddy (HTTPS), accesible solo por LAN/VPN.

```text
Clientes LAN/VPN ── https://tbp ──> Caddy (443) ──> app:8080 ──> db:1433 (SQL Server Express)
```

## Instalar en un servidor

La plataforma de referencia es **Ubuntu con Docker Engine**. El orden importa: primero el anfitrión (Docker y firewall) y después el ERP.

```bash
sudo apt-get update && sudo apt-get install -y git
git clone https://github.com/alanminana/theburyproject1.git ~/theburyproject1 && cd ~/theburyproject1
sudo scripts/install/server-linux-bootstrap.sh --user "$USER" --allow-from 192.168.1.0/24 \
     --admin-from 192.168.1.10/32 --backup-dir /srv/bury-backups --dry-run      # revisar y repetir sin --dry-run
# cerrar la sesion y volver a entrar
cd ~/theburyproject1 && bash scripts/install/install.sh --admin-email correo@dominio.com --backup-dir /srv/bury-backups --schedule
```

> **`ufw` no protege los puertos que publica Docker.** El 443 lo filtra `bury-firewall`, que instala el bootstrap. Verificarlo desde un equipo no autorizado.

Antes de instalar, leer la guía completa: [docs/instalacion-servidor.md](docs/instalacion-servidor.md) §4. Perder el servidor sin haber resguardado afuera
los respaldos **y** los secretos es perder los datos: [docs/operacion-servidor-ubuntu.md](docs/operacion-servidor-ubuntu.md) §3 y §6.

**Alternativa Windows 10/11 con Docker Desktop (WSL2):** clonar dentro de WSL y, en PowerShell como administrador:

```powershell
cd \\wsl.localhost\<distro>\home\<usuario>\theburyproject1\scripts\install
powershell -ExecutionPolicy Bypass -File .\install-windows.ps1 -AdminEmail correo@dominio.com -DryRun   # primero en seco
powershell -ExecutionPolicy Bypass -File .\install-windows.ps1 -AdminEmail correo@dominio.com
```

Preparación y límites: [guía de instalación](docs/instalacion-servidor.md) §3.

## Desarrollo local

Windows es el entorno principal de desarrollo. Requisitos: SDK de .NET 10 (ver `global.json`), SQL Server LocalDB con `sqlcmd`, Git y Node.js (solo para los tests E2E).

El ERP local se arranca con el lanzador, que se niega si el código no es el de `origin/main` o si la base no coincide con sus migraciones:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\iniciar-erp-local.ps1
```

Configuración inicial, base nueva y migraciones: [docs/desarrollo-local.md](docs/desarrollo-local.md).

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

- [Desarrollo local](docs/desarrollo-local.md)
- [Instalación del servidor](docs/instalacion-servidor.md) · [Operación y recuperación](docs/operacion-servidor-ubuntu.md)
- [Red, firewall y acceso LAN/VPN](docs/red-privada-lan-vpn.md)
- [Sincronización GitHub / Windows / Ubuntu](docs/sincronizacion-entornos.md) · [Evidencias y límites](docs/evidencias-instalacion.md)
- [Despliegue a producción](docs/despliegue-produccion.md) · [Deploy y rollback](docs/deploy-rollback.md)
- [Backups y restauración](docs/backup-restore.md)
- [Estándar de UI](docs/ui/ERP-UI-STANDARD.md)
- [CI y E2E](docs/ci.md)

## Seguridad

No subir `.env`, claves ni backups al repositorio. El `.env` contiene todos los secretos y no está incluido en los backups: guardarlo en un gestor de contraseñas.
