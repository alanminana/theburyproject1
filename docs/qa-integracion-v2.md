# QA-INTEGRACIÓN V2 — 2026-10-14

Rama: `integracion/qa-v2-20261014` (worktree `D:\git\theburyproject1-integracion-v2`), base `0a8506cf` (V1: SEG P0 + PRD P0 + FIN P0).
Sin push, sin deploy, sin migraciones ejecutadas, sin E2E, `main` y worktree principal intactos.

## Commits incorporados (merges `--no-ff`)

| Merge | Rama / HEAD real | Commits propios |
|---|---|---|
| `a85b2e15` | `qa/fin-conc-p0-20261014` @ `5c40a67a` | `a81e5e38` (auditoría), `9e732c5c`, `8bc0eb69`, `5c40a67a` |
| `4dbdaa9b` | `fix/qa-prd-p1-integridad` @ `e3e7b2cf` | `6e2a7859` (tests rojos a propósito), `e3e7b2cf` |
| `62d54a6a` | `fix/mora-config-integridad` @ `78a56fc8` | `b2de9b62`, `bb41cc54`, `78a56fc8` |

La rama `qa/fin-conc-20261014` (`a81e5e38`, solo auditoría) NO se mergeó aparte: ya está contenida en `qa/fin-conc-p0-20261014`.

Árbol de dependencias: FIN-CONC-P0 y PRD-P1 parten de la V1 (`369b77ed` / `10ecd0a7`, ambos contenidos en `0a8506cf`); Mora parte de `95044284`
(ancestro de V1). Los tres merges fueron limpios (0 conflictos de texto).

## Conflictos

- Texto: ninguno.
- Semánticos: ningún archivo fuente es tocado por más de una de las tres ramas nuevas. Contra la V1, solapan 5 archivos con P0
  (`MovimientoStockController`, `ProductoController`, `VentaService`, `Catalogo/Index_tw`, `Producto/FichaInventario`): se verificó con
  `git diff 0a8506cf HEAD` que las únicas líneas eliminadas son las que PRD-P1 reemplaza a propósito (parámetro `requestId` y `tbody` con
  `data-can-*`). Ninguna validación de permisos de P0 fue revertida.

## Migraciones

Única migración nueva: `20261014175359_QaPrdP1MovimientoStockRequestId` (B4): `MovimientosStock.RequestId uniqueidentifier NULL` + índice único
filtrado `IX_MovimientosStock_RequestId` (`WHERE RequestId IS NOT NULL`). Aditiva y reversible (`Down` presente). Snapshot +7 líneas, es la última del orden.
**NO aplicada a ninguna base.**

Mecanismos que la aplicarían automáticamente (revisar antes de arrancar cualquier imagen integrada):

1. `DbInitializer.Initialize` (`Program.cs:456`) llama `MigrateAsync()` al iniciar la app si `Database:InitializeOnStartup` es true (default true).
2. Servicio one-shot `migrate` de `docker-compose.yml` (`TheBuryProyect.dll --migrate`, `DbMigrationRunner`); la `app` lo espera con `InitializeOnStartup=false`.
3. `--configurar-documentos` también ejecuta `MigrateAsync()`.

Regla para la fase E2E: aplicar con autorización explícita, solo sobre bases QA descartables (`BuryQA_*`), y en los stacks QA mantener el
`migrate` bajo control manual.

## Mora UI (WIP del repo principal, NO integrado)

Inventario para una integración independiente (todo sin commit en `D:\git\theburyproject1`, rama `trabajo/cambios-pendientes`):

- Vistas: `Views/Mora/Index.cshtml` (291), `Alertas.cshtml` (344), `Logs.cshtml` (168). `ConfiguracionExpandida.cshtml` ya está versionada.
- CSS: `wwwroot/css/mora-module.css`.
- Tests: `Integration/MoraAlertasLogsHttpTests.cs`, `Unit/MoraAlertasLogsUiContractTests.cs`, `Unit/MoraIndexUiContractTests.cs`.
- E2E/config: `playwright.mora-index.config.js`, `playwright.mora-alertas-logs.config.js`, `playwright.smoke.config.js`, `e2e-smoke/`.
- `Controllers/MoraController.cs` y `Services/Interfaces/IMoraService.cs` del WIP son idénticos a la rama Mora integrada; `Services/MoraService.cs` difiere
  (la rama integrada ya contiene la versión corregida). Los 3 archivos `MoraConfiguracionExpandidaPreservacion*` y `docs/mora-config-preservacion.md`
  difieren de las versiones de la rama: **no copiar el WIP encima**; son la versión previa de lo ya integrado.
- Ajenos a Mora: `.claude/skills/*`, `skills-lock.json` (modificados / nuevos).

Plan sugerido: rama propia desde `integracion/qa-v2-20261014`, traer solo vistas + CSS + tests UI + configs Playwright, y correr los contract tests antes.

## Plan de regresión E2E (no ejecutado)

Condiciones previas:

- Imagen nueva construida desde el commit de integración final (hash exacto; no reutilizar `theburyproject/erp:qa-9504428` ni ninguna imagen anterior).
- Etiquetar `theburyproject/erp:qa-v2-<hash8>`; no tocar scripts compartidos de `~/.bury-qa` (el overlay referencia la imagen por variable/stack env: ajustar
  solo en una copia o en `stacks\<s>.env`, previa autorización).
- Autorización explícita para aplicar la migración B4 en cada base `BuryQA_*`.
- Correr en serie con `qa-run.ps1` (host con poca RAM libre).

| Suite | Stack / puerto | Spec | Foco de regresión |
|---|---|---|---|
| SEG | seg :18091 | `e2e-tx/seguridad/seguridad-usuarios-roles.spec.js` | usuario inactivo sin acceso (P0), permisos de roles |
| PRD | prd :18092 | `e2e-tx/productos/01..04` + `integrity.js` | permisos P0, B3–B9 de P1 (códigos, `RequestId` idempotente, alcance de precios, reversión, historial, UI `data-can-*`) |
| FIN | fin :18093 | `e2e-tx/financiero/financiero.spec.js` | integridad P0 + lock compartido cobro/cancelación |
| CFG | cfg :18094 | `e2e-tx/config/mora-config-concurrencia.spec.js` | preservación de 35 campos, RowVersion, validaciones; `Skip` CFG-DEF-04 |

Versiones: commit de integración final (`git rev-parse HEAD` de esta rama tras este documento), Playwright según `package.json` del repo,
imagen `qa-v2-<hash8>`, login sintético `administrador` / `Admin123!`.

## Resultados C# (build aislado en el worktree V2, sin app corriendo)

- `dotnet build` de `TheBuryProyect.Tests`: 0 errores, 15 warnings (preexistentes: nulabilidad, EF1003, xUnit2013).
- `dotnet test` completo: **PASS** — 5583 superadas, 0 fallidas, 21 omitidas, total 5604 (3 m 57 s).
- Omitidas (todas por diseño, ninguna regresión): 16 de concurrencia SQL Server real (`CancelarVsPagoServiciosRealesSqlServerTests` y `CancelarVsPagoLockModelSqlServerTests`, requieren compuerta/SQL Server),
  4 seed runners E2E (`GenericE2ESeedRunner`, `ClienteAptitudPunitorioE2ESeedRunner`, requieren variable de entorno + LocalDB descartable) y
  Mora `CFG14b`/`CFG-DEF-04` (`TipoTasaMora` no interpretado en `CalcularMora`, pendiente de decisión de negocio).
- Las 16 pruebas de concurrencia SQL Server omitidas respaldan la corrección FIN-CONC-P0: deben correrse en la fase E2E contra SQL Server real.
