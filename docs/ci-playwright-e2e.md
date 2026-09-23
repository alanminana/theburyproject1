# Playwright E2E CI (self-contained)

Este documento describe `.github/workflows/playwright.yml` despues de convertirlo en un E2E
self-contained: ya no depende de una app externa ya levantada, LocalDB, secretos reales ni
infraestructura manual. Corre contra un stack Docker efimero propio, creado y destruido dentro del
mismo job.

Tarea relacionada pero **completamente separada**: hay otra tarea trabajando sobre staging en la
rama `staging-fixes-20260923`. Este bloque no la toco ni incorporo sus cambios.

## Por que fallaba antes

`playwright.yml` original: `npm ci` + `npx playwright install --with-deps` + `npx playwright test`,
sin levantar la app ni una base de datos. `playwright.config.js` apunta por defecto a
`http://localhost:5187` y `e2e/global-setup.js` exige `E2E_USER`/`E2E_PASS` de una cuenta ya
existente. En el runner de GitHub no hay nada escuchando en ese puerto ni esas credenciales: el
`global-setup` fallaba (o el resto del suite fallaba en cascada) antes de ejercitar ningun test real.
No es un bug de la app ni de los specs: es que el workflow nunca proveyo la infraestructura que la
suite siempre asumio disponible (documentado en los propios comentarios de `playwright.config.js`:
"Prerequisitos: 1. App corriendo... 2. Variables de entorno...").

## Arquitectura nueva

```text
GitHub runner (ubuntu-latest)
  │
  ├── setup .NET (global.json) + Node (lts/*) + cache NuGet
  ├── generar .env efimero en $RUNNER_TEMP (credenciales sinteticas, nunca commiteadas)
  ├── docker compose -f docker-compose.yml -f docker-compose.ci.yml up -d --build app
  │     └── resuelve dependencias: db (healthcheck) -> db-init (login dedicado) -> migrate
  │         (Development: EF migrations + seeds de produccion + CreateTestUsersAsync) -> app (Production)
  ├── esperar /health/ready == 200 (con logs de diagnostico si no llega a tiempo)
  ├── compilar TheBuryProyect.Tests (Release)
  ├── sembrar dataset E2E (GenericE2ESeedRunner + ClienteAptitudPunitorioE2ESeedRunner, idempotentes,
  │     ya versionados en TheBuryProyect.Tests/E2ESeeding/) contra la app-user (sin DDL)
  ├── exportar los ids del seed (JSON) como variables E2E_* del job
  ├── npm ci + playwright install --with-deps chromium
  ├── npx playwright test (E2E_BASE_URL=http://127.0.0.1:<puerto>, E2E_USER=administrador,
  │     E2E_PASS=Admin123! — usuario de prueba sintetico de Data/DbInitializer.cs, no un secreto)
  ├── siempre: logs de db/db-init/migrate/app + playwright-report/test-results como artifact
  └── siempre: docker compose down -v + borrar el .env efimero
```

Reutiliza `docker-compose.yml`, `Dockerfile`, `docker/db-init/*` y el servicio `migrate`
**exactamente como existen** (sin modificarlos). `docker-compose.ci.yml` es un overlay nuevo, solo
para CI, que:

- publica `db` y `app` en `127.0.0.1` (el runner necesita hablarles desde el host: el seed corre
  como `dotnet test` en el runner, y Playwright corre como proceso Node en el runner, ninguno de los
  dos dentro de la red Docker);
- pone `migrate` en `ASPNETCORE_ENVIRONMENT=Development` **solo para ese servicio** — es lo que hace
  que `DbInitializer.CreateTestUsersAsync` (ya versionado, no se agrega logica nueva) siembre los 7
  usuarios de prueba con contraseñas sinteticas hardcodeadas en el propio repo (`administrador` /
  `Admin123!`, etc.) — no son secretos, son fallback de desarrollo documentados en el codigo;
- **no** toca `app` (se queda en `Production`, igual que el stack real, para maxima paridad: no se
  usa `dotnet run` ni ningun otro modo que cambie el runtime respecto a Docker);
- **no** levanta `caddy` (requiere un dominio publico con DNS real para emitir certificados ACME —
  imposible en un runner efimero) ni los servicios de `backup`/`restore` (profile `tools`).

Caddy queda explicitamente fuera del alcance de este E2E: se prueba `app` (Kestrel) directamente por
HTTP en `127.0.0.1`. La capa de TLS/reverse-proxy/HTTP3 de Caddy no se ejercita aqui — es una
limitacion documentada, no un intento de esconder el problema.

## Dataset E2E

Los seeds de produccion (`Data/Seeds/RolesPermisosSeeder.cs`, `SucursalesSeeder.cs`) solo crean
roles, permisos, sucursales, plantilla de contrato y el admin inicial — ningun cliente ni producto.
Contra una base recien creada, la mayoria de los specs de Cotizacion/Venta hacian `test.skip` (por
diseño explicito de esos specs) por falta de datos buscables, no por ningun bug real.

Dos seeders versionados en `TheBuryProyect.Tests/E2ESeeding/` resuelven esto (idempotentes, no
tocan la base de desarrollo — exigen `E2E_SEED_CONNECTION` explicita):

- `GenericE2ESeeder` (nuevo en este bloque): 4 clientes y 5 productos con nombres que contienen las
  subcadenas que `e2e/helpers.js` usa para autocompletar (`an`,`el`,`or`,`is`,`ar`,`ro`,`al`) — cubre
  ~16 de los 21 specs que solo necesitan "que exista algo buscable".
- `ClienteAptitudPunitorioE2ESeeder` (PUN-ML10-G, ya existente): 9 escenarios de cliente con estados
  de mora/punitorio especificos + creditos/cuotas dedicados para los 5 specs que exigen IDs
  deterministas via variables `E2E_*` (`cliente-aptitud-punitorio`, `credito-adelanto-pago-multiple`,
  `credito-pago-cuota-individual`, `credito-pago-cuota-returnurl`, parte de
  `credito-punitorio-detalle`) — ya escribia el JSON que este workflow ahora consume.

Un especifico (`cotizacion-credito-personal-aptitud-contradiccion.spec.js`) depende de un DNI fijo
(`E2E_CLIENTE_NOAPTO_DNI`, default `35996614`) que ninguno de los dos seeders produce; el propio spec
ya hace `test.skip` si no lo encuentra (skip explicito por diseño, no agregado por este bloque).

## Hallazgo: drift preexistente app/test (fuera de alcance)

Corrida real completa (1 viewport, `1366x768`, contra el stack de este workflow funcionando
correctamente — app healthy, `/health/ready`=200, dataset sembrado): **35 failed, 1 flaky, 16
skipped, 144 passed** (196 tests, ~12m40s). El fallo raiz confirmado (`venta-pago-por-item.spec.js`):
el wizard de `Venta/Create` tiene ahora un primer paso "Cotizar" antes de "Cliente" (rediseño
posterior de la serie `COTIZACION-MOCKUP-01/02`); `helpers.js.searchAndSelectClient` espera
`#input-buscar-cliente` visible de entrada, pero en el paso "Cotizar" ese campo pertenece a otro
bloque. Esto **no es un problema de infraestructura**: los tests fallan igual con la app y la base
funcionando correctamente (confirmado con captura del DOM real en el momento del fallo).

El mismo patron (specs que interactuan con `Venta/Create` asumiendo su layout anterior) explica la
mayoria de los 35 fallos, agrupados en 10 archivos: `venta-pago-por-item` (6/6),
`venta-wizard-accessibility` (11/13 — cuenta de pasos/overflow del wizard), `credito-visual-excepcion-reposicion`
(5/7), `venta-functional-audit` (2/2), `venta-excepcion-documental-reload` (2/2),
`credito-visual-otros-motivos` (2/2), `credito-pago-cuota-individual` (3/12 — setup vía venta
descartable), `cotizacion-credito-personal-excepcion` (1/1 — "Pasar a venta"). Dos fallos parecen
de otra naturaleza y quedan sin clasificar en profundidad (fuera del alcance de este bloque, que es
infraestructura, no debugging de specs): `cliente-aptitud-punitorio.spec.js` (2, aserciones sobre
bloques de mora/punitorio en Cliente Details) y `ui-4e-layout-visual.spec.js` (1, diff de screenshot
"login-mobile.png" — probablemente baseline generado en otro SO/resolución, clase de fallo habitual
en snapshot testing cross-entorno). 1 flaky en `credito-adelanto-pago-multiple.spec.js` (paso en el
segundo intento).

No se modifico ningun spec ni la app para forzar estos casos a verde (prohibido explicitamente en el
alcance de este bloque). El workflow completo (infra, dataset, arranque, Playwright, artifacts,
cleanup) esta validado y funcionando; el numero de tests en rojo depende de que estos specs se
actualicen para el wizard actual de Venta/Create — tarea de otro equipo/bloque, no de CI.

## Ejecucion local

```bash
# .env efimero (nunca commitear; ver .env.example para todas las claves)
cat > .env <<'EOF'
MSSQL_SA_PASSWORD='...'
...
APP_PORT=18080
MSSQL_HOST_PORT=14330
EOF

docker compose -p bury-e2e-ci -f docker-compose.yml -f docker-compose.ci.yml up -d --build app
curl -f http://127.0.0.1:18080/health/ready

E2E_SEED_CONNECTION="Server=127.0.0.1,14330;Database=TheBuryProjectDb;User Id=erp_app_ci;Password=...;TrustServerCertificate=True" \
  dotnet test TheBuryProyect.Tests/TheBuryProyect.Tests.csproj -c Release \
  --filter "FullyQualifiedName~GenericE2ESeedRunner.Sembrar|FullyQualifiedName~ClienteAptitudPunitorioE2ESeedRunner.Sembrar"

npm ci && npx playwright install chromium
E2E_USER=administrador E2E_PASS='Admin123!' E2E_BASE_URL=http://127.0.0.1:18080 npx playwright test

docker compose -p bury-e2e-ci -f docker-compose.yml -f docker-compose.ci.yml down -v
```

## Seguridad

- `permissions: contents: read` a nivel de workflow.
- Ninguna credencial real: `MSSQL_SA_PASSWORD`/`ERP_DB_PASSWORD`/`ERP_MIGRATION_PASSWORD`/
  `ADMIN_PASSWORD` se generan con `openssl rand` dentro del job y se enmascaran (`::add-mask::`); el
  usuario E2E (`administrador`/`Admin123!`) es un fallback de desarrollo ya hardcodeado en
  `Data/DbInitializer.cs`, no un secreto de GitHub Actions.
- El `.env` efimero vive en `$RUNNER_TEMP` (nunca en el working tree del repo) y se borra siempre
  (`if: always()`).
- No se sube ningun artifact con `.env`, passwords, tokens ni Data Protection keys — solo
  `playwright-report/`, `test-results/` y logs de texto de los contenedores (`docker compose logs`,
  que no imprime env vars a menos que la app las loguee explicitamente; no lo hace).
- No se usa ningun `secrets.*` de GitHub Actions en este workflow.

## Archivos modificados

| Archivo | Proposito |
|---|---|
| `.github/workflows/playwright.yml` | Reescrito: stack Docker efimero completo en vez de asumir app externa |
| `docker-compose.ci.yml` | Overlay nuevo, solo CI: publica puertos a 127.0.0.1 + `migrate` en Development |
| `TheBuryProyect.Tests/E2ESeeding/GenericE2ESeeder.cs` | Nuevo: clientes/productos genericos buscables |
| `TheBuryProyect.Tests/E2ESeeding/GenericE2ESeedRunner.cs` | Nuevo: punto de entrada `dotnet test --filter` para el seeder de arriba |
| `docs/ci-playwright-e2e.md` | Este documento |

No se modifico: `docker-compose.yml`, `docker-compose.dev.yml`, `Dockerfile`, `docker/db-init/*`,
`Caddyfile`, `scripts/deploy/**`, `scripts/backup/**`, `scripts/monitoring/**`, migraciones
existentes, ningun spec de `e2e/*.spec.js`, ni `ClienteAptitudPunitorioE2ESeeder.cs`/
`ClienteAptitudPunitorioE2ESeedRunner.cs` (solo se agrego un archivo complementario nuevo).

## Estado

```text
[COMPLETAR al cierre con los numeros reales de la corrida completa]
```
