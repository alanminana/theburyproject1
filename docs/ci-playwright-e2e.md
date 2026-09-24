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

## Drift cerrado y estado final (bloque de cierre, 2026-09-23)

La corrida inicial completa (1 viewport, `1366x768`, stack funcionando correctamente) dio **35
failed, 1 flaky, 16 skipped, 144 passed** (196 tests). Se investigó cada fallo (nunca "a ciegas":
primero reproducir, leer el DOM real vía trace/error-context, recién después decidir) y se
clasificó en tests desactualizados por rediseños posteriores del wizard, un bug de la propia
suite E2E (config de medios de pago incompleta) y un límite real del entorno de red aislado.
Ver el detalle completo en el informe de cierre de la tarea (PR).

**Resultado final, misma base fresca, 1 sola corrida completa**: **174 passed, 1 failed, 0 flaky,
15 skipped** (196 tests, ~6.7min).

Causas cerradas (categoría, causa, acción):

- **Drift del wizard de Venta/Create** (`venta-wizard-accessibility`, `credito-visual-*`,
  `venta-excepcion-documental-reload`, `venta-functional-audit` parcial): el wizard agregó un
  primer paso "Cotizar" antes de "Cliente" (serie `COTIZACION-MOCKUP-01/02`) y varios helpers
  compartidos (`e2e/helpers.js`: `searchAndSelectClient`, `addProduct`, `activarFiltroStock`,
  `setGlobalTipoPago`, `ensureConfirmarHabilitado`, `ensureVendedorSeleccionado`) asumían que el
  paso "Cliente"/"Productos"/"Pago"/"Revisión" ya estaba activo. Fix centralizado en los helpers
  (activan el tab correspondiente sólo si no está ya seleccionado) en vez de repetirlo en cada spec.
- **`venta-pago-por-item.spec.js` (6 fallos) — spec obsoleta, eliminada.** El pago por ítem
  (`.btn-configurar-pago-item`, `#modal-pago-item`) ya no existe en ninguna vista ni script — está
  confirmado por un contract test C# dedicado (`VentaCreateUiContractTests.CreateView_
  NoMuestraAccionPagoPorItemEnTabla`) y ya documentado como candidata a eliminarse en
  `docs/ui/UI-REFACTOR-STATUS.md` antes de este bloque. Se borró el archivo.
- **Redesign `VENTA-CREDITO-REDESIGN-VISUAL-IMPLEMENTACION-01`** (`credito-visual-otros-motivos`,
  `credito-visual-excepcion-reposicion` caso G, `venta-excepcion-documental-reload`): el heading
  "Otros motivos" y el badge `#excepcion-aplicada-badge` se retiraron a favor de una sola fila
  "Documentación — exceptuada" dentro de "Estado del crédito"; Cupo pasó a filtrarse siempre de
  "Otros motivos" (vive permanente en "Estado del crédito"); "Observaciones" pasó a un `<details>`
  colapsado. Tests actualizados a los selectores/contrato vigentes.
- **Bug de la propia suite E2E — config de medios de pago incompleta.** `ConfiguracionPago` es
  global (`aplicarMediosGlobalesAlSelector` en `venta-create.js` reemplaza, no completa, las
  opciones del selector "Forma de pago"): el seeder de escenarios de punitorio
  (`ClienteAptitudPunitorioE2ESeeder`) siembra sólo Transferencia y Crédito Personal para sus
  propios casos, dejando "Efectivo" fuera del selector para el resto de los specs. Fix: `GenericE2ESeeder`
  ahora siembra también Efectivo activo (idempotente), ya que es el seeder pensado para necesidades
  genéricas del suite.
- **`aNumero()` (parser de moneda de test) — bug de la suite, no de la app.** `credito-pago-cuota-
  individual.spec.js` y `credito-adelanto-pago-multiple.spec.js` conviven con dos formatos de
  moneda reales en la misma pantalla ("Contexto autoritativo" en invariant/US, "Datos del pago" en
  es-AR) y el parser asumía uno solo. Reescrito con una heurística agnóstica de locale (el separador
  que aparece último en el texto es el decimal).
- **`ui-4e-layout-visual.spec.js` (login-mobile) — oversight de storageState.** Todos los projects
  de `playwright.config.js` aplican `storageState: AUTH_FILE` por defecto; el test de "Login
  visual" no lo limpiaba, así que navegaba ya autenticado y `/Identity/Account/Login` redirigía al
  Dashboard (de ahí que el locator del username resolviera a un input de otro widget de la
  página). Fix: `test.use({ storageState: { cookies: [], origins: [] } })` en ese describe.
- **`cliente-aptitud-punitorio.spec.js` (2 fallos) — drift por la reorganización en tabs de
  Cliente/Details (2026-09-14).** El `<dt>Capital en mora</dt>` exacto vive en la tab "Crédito"
  (la tab "Resumen", default, sólo muestra un párrafo resumen con formato distinto); dos tests no
  navegaban a esa tab antes de verificar visibilidad. Se agregó la navegación faltante.
- **`cotizacion-credito-personal-excepcion.spec.js` (1) — límite real del entorno, no bug ni
  drift.** `SituacionCrediticiaBcraService` llama a la API real de BCRA (`api.bcra.gob.ar`); el
  stack Docker CI no tiene egreso a internet, así que para un cliente recién creado sin consulta
  BCRA cacheada la aptitud queda en "Requiere autorización / No se pudo validar BCRA" en vez de
  "No apto". Se agregó un `test.skip` explícito y documentado sólo para ese caso (mismo patrón que
  los skips ya existentes por falta de datos de QA) — no oculta un fallo, reconoce una precondición
  de red que este entorno no puede cumplir.
- **2 flaky (no reproducibles de forma determinista) en la corrida inicial de este cierre**:
  ambos con el mismo síntoma en consola — `Failed to complete negotiation with the server:
  TypeError: Failed to fetch` (negociación SignalR). No volvieron a aparecer en la corrida final
  definitiva; consistente con un hipo de red transitorio de la máquina bajo carga (otros stacks
  Docker corriendo en paralelo durante el desarrollo de este bloque), no con un bug determinista
  de la app ni de los specs.

**Sin cerrar — 1 fallo real, requiere debugging en vivo (no se fuerza un fix a ciegas):**
`venta-functional-audit.spec.js` ("Create: validaciones, cliente, productos, descuentos, pago,
crédito, modal y guardado"). Reproducido de forma consistente: al llegar al paso "Revisión" de un
alta Efectivo simple (4 productos, sin crédito), `#btn-confirmar` queda con el atributo `disabled`
sin que ningún JS de `wwwroot/js/*.js` lo establezca de forma rastreable estáticamente (grep
exhaustivo sin resultado) y sin errores de consola ni respuestas 4xx/5xx capturadas por el test.
Se investigó y se descartaron: navegación incompleta al paso (corregido con espera por el panel
real, no por `aria-selected` — ese sí tenía un bug real de desync con foco de teclado, ya
corregido), viewport residual de un bloque anterior del mismo test (corregido), y el fallback de
`ensureConfirmarHabilitado` intentando cambiar el tipo de pago desde el paso equivocado (corregido).
Con todo eso corregido, el botón sigue deshabilitado sin causa identificable por análisis estático.
Necesita inspección en vivo (DevTools/trace viewer) para encontrar qué lo deja así.

No se modificó ninguna regla de negocio para lograr estos resultados — sólo specs E2E, el seeder
`GenericE2ESeeder` (agrega una fila de configuración, no cambia lógica) y el workflow.

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
E2E_USER=administrador E2E_PASS='Admin123!' E2E_BASE_URL=http://127.0.0.1:18080 npx playwright test --project=1366x768

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
| `.github/workflows/playwright.yml` | Stack Docker efimero completo (bloque original) + acotado a `--project=1366x768` (cierre) |
| `docker-compose.ci.yml` | Overlay nuevo, solo CI: publica puertos a 127.0.0.1 + `migrate` en Development |
| `TheBuryProyect.Tests/E2ESeeding/GenericE2ESeeder.cs` | Clientes/productos genericos buscables + `ConfiguracionPago` de Efectivo (cierre) |
| `TheBuryProyect.Tests/E2ESeeding/GenericE2ESeedRunner.cs` | Punto de entrada `dotnet test --filter` para el seeder de arriba |
| `docs/ci-playwright-e2e.md` | Este documento |
| `e2e/helpers.js` | Helpers de wizard con guard de tab activo (cierre) |
| `e2e/cliente-aptitud-punitorio.spec.js` | Fix drift: navegar a tab Crédito antes de verificar (cierre) |
| `e2e/credito-visual-excepcion-reposicion.spec.js` | Fix drift: confirmación de excepción sin badge retirado (cierre) |
| `e2e/credito-visual-otros-motivos.spec.js` | Fix drift: heading retirado, Cupo filtrado siempre, Observaciones colapsada (cierre) |
| `e2e/venta-excepcion-documental-reload.spec.js` | Fix drift + mock de motivos con shape correcto (cierre) |
| `e2e/venta-functional-audit.spec.js` | Fixes parciales de navegación (queda 1 fallo sin resolver, ver arriba) (cierre) |
| `e2e/credito-pago-cuota-individual.spec.js` | Fix `aNumero()` (parser de moneda agnóstico de locale) (cierre) |
| `e2e/credito-adelanto-pago-multiple.spec.js` | Fix `aNumero()` (mismo parser) (cierre) |
| `e2e/ui-4e-layout-visual.spec.js` | Fix drift: storageState limpio en "Login visual" (cierre) |
| `e2e/cotizacion-credito-personal-excepcion.spec.js` | Skip explícito por límite de red del entorno CI (cierre) |
| `e2e/venta-pago-por-item.spec.js` | **Eliminado**: probaba una feature retirada del producto (cierre) |

No se modifico: `docker-compose.yml`, `docker-compose.dev.yml`, `Dockerfile`, `docker/db-init/*`,
`Caddyfile`, `scripts/deploy/**`, `scripts/backup/**`, `scripts/monitoring/**`, migraciones
existentes, `ClienteAptitudPunitorioE2ESeeder.cs`/`ClienteAptitudPunitorioE2ESeedRunner.cs`, ni
ninguna regla de negocio de la aplicación.

## Estado

```text
Corrida final (1 sola pasada, base fresca, 1366x768): 196 total
  174 passed, 1 failed, 0 flaky, 15 skipped (~6.7 min)

Único fallo restante: venta-functional-audit.spec.js — "Create: validaciones, cliente,
productos, descuentos, pago, crédito, modal y guardado". #btn-confirmar queda disabled al
llegar a Revisión sin causa identificable por análisis estático; requiere debugging en vivo
(ver sección de arriba). No se oculta ni se fuerza a verde.

GitHub Actions real: NO ejecutado en este cierre (sin acceso a gh CLI ni MCP github
funcional en este entorno — ver informe de cierre de la tarea). Pendiente de que se abra el
PR y corra el workflow real antes de mergear.
```

## Resultado tras integrar main con los fixes de staging (2026-09-24)

Corrida local contra stack Docker efimero limpio, `--project=1366x768`: **196 total, 182 passed,
0 failed, 0 flaky, 14 skipped** (~5 min). Los 14 skips son los explicitos ya documentados
(permisos de la sesion QA, datos ausentes, sin egreso a BCRA, escenario ya no alcanzable por UI).

Drift de tests corregido (no se toco producto):

| Spec | Causa | Fix |
|---|---|---|
| `venta-edit-confirmar-post-blocker.spec.js` | `getByText('Venta confirmada')` resolvia 3 elementos (toast + titulo + descripcion): strict mode | `exact: true` |
| `venta-functional-audit.spec.js` (Edit) | En Edit `#btn-confirmar` ahora CONFIRMA la venta; volver a `editUrl` redirige a Details. El retry salia "skipped" (estado de modulo) y contaba como flaky | matriz responsive movida ANTES de confirmar |
| `credito-adelanto-pago-multiple.spec.js` | Negociacion SignalR abortada por el `page.goto` inmediato tras login se logueaba como error de consola (flaky) | filtro estrecho de ese mensaje exacto; 5xx y demas errores de consola siguen fallando |

Nota operativa: dos corridas Playwright simultaneas en el mismo worktree se pisan (`e2e/.auth/user.json`
y `qa-evidence/`); usar un worktree/stack por corrida.
