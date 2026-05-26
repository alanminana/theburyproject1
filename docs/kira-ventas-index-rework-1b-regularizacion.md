# KIRA-VENTAS-INDEX-REWORK-1B - Regularizacion arbol local

## A. Objetivo

Regularizar el arbol local posterior a `KIRA-VENTAS-INDEX-REWORK-1A`, separar cambios propios del Index de cambios preexistentes fuera de alcance y decidir si el lote puede commitearse sin mezclar cambios ajenos.

No se redisenia mas en esta fase.

## B. Estado inicial

Rama observada:

- `kira/ventas-index-rework-1a-visual-parity`

Comandos iniciales ejecutados:

- `git status --short --branch`
- `git branch --show-current`
- `git log --oneline -10`
- `git status --porcelain=v1`
- `git diff --name-status`
- `git diff --stat`

Estado inicial observado:

- Modificados: `.claude/settings.local.json`, `AGENTS.md`, `CLAUDE.md`, `TheBuryProyect.Tests/Unit/VentaCreateUiContractTests.cs`, `Views/Producto/Unidades.cshtml`, `Views/Venta/Index_tw.cshtml`, `Views/Venta/_VentaModuleStyles.cshtml`, `docs/misa-catalogo-ux-1g-aria-live-modales.md`, `wwwroot/js/venta-create.js`.
- Eliminados unstaged: `skills-lock.json`.
- Eliminados staged al inicio: `Views/Venta/Create_tw_legacy.cshtml`, `Views/Venta/_VentaCrearModal.cshtml`, `wwwroot/css/venta-modal-rework.css`, `wwwroot/js/venta-crear-modal.js`, `wwwroot/js/venta-modal-rework.js`.
- Nuevos: `docs/kira-ventas-index-rework-1a-visual-parity.md`, `docs/kira-ventas-index-rework-1b-regularizacion.md`, `docs/kira-ventas-page-rework-reset-0-cleanup-legacy.md`, `wwwroot/css/venta-index-rework.css`, `wwwroot/css/venta-page-wizard.css`, `wwwroot/js/venta-index-rework.js`, `wwwroot/js/venta-page-wizard.js`.

Correccion importante frente al reporte previo:

- `Views/Venta/Create_tw.cshtml` existe.
- `Views/Venta/Edit_tw.cshtml` existe.
- No aparecieron eliminados en esta ejecucion.

## C. Rama actual

`kira/ventas-index-rework-1a-visual-parity`

Ultimos commits vistos:

- `4e9733a Cerrar QA final de nueva venta pagina wizard (KIRA-VENTAS-PAGE-REWORK-QA)`
- `0a0cba8 Cerrar smoke final de venta create como pagina wizard (KIRA-VENTAS-PAGE-REWORK-1F)`
- `1a72b4e Validar pago y credito en venta create wizard (KIRA-VENTAS-PAGE-REWORK-1E)`
- `8c501a6 Validar flujo de venta create como pagina wizard (KIRA-VENTAS-PAGE-REWORK-1D)`
- `1bcb270 Desacoplar wizard de venta del modal legacy (KIRA-VENTAS-PAGE-REWORK-1C)`
- `39d444d Reemplazar acceso modal por pagina nueva venta (KIRA-VENTAS-PAGE-REWORK-1B)`
- `da9467c Adaptar venta create como pagina wizard (KIRA-VENTAS-PAGE-REWORK-1A)`
- `916c529 Reencuadrar nueva venta como pagina (KIRA-VENTAS-PAGE-REWORK-0)`
- `ef09c9c Sincronizar pago y totales del wizard de venta (KIRA-VENTAS-MODAL-REWORK-1F)`
- `5958f26 Agregar navegacion inteligente al wizard de venta (KIRA-VENTAS-MODAL-REWORK-1E)`

## D. Archivos preexistentes fuera de alcance

Marcados fuera de alcance para Index 1A:

- `.claude/settings.local.json`
- `AGENTS.md`
- `CLAUDE.md`
- `skills-lock.json`
- `Views/Producto/Unidades.cshtml`
- `docs/misa-catalogo-ux-1g-aria-live-modales.md`
- `wwwroot/js/venta-create.js`
- `docs/kira-ventas-page-rework-reset-0-cleanup-legacy.md`
- `wwwroot/css/venta-page-wizard.css`
- `wwwroot/js/venta-page-wizard.js`
- `Views/Venta/Create_tw_legacy.cshtml`
- `Views/Venta/_VentaCrearModal.cshtml`
- `wwwroot/css/venta-modal-rework.css`
- `wwwroot/js/venta-crear-modal.js`
- `wwwroot/js/venta-modal-rework.js`

Accion de regularizacion aplicada:

- Se ejecuto `git restore --staged --` solo sobre los borrados legacy/modal que estaban staged al inicio.
- No se restauro su contenido en working tree.
- El objetivo fue evitar un commit accidental con borrados fuera de alcance.

No se tocaron:

- `AGENTS.md`
- `CLAUDE.md`
- `.claude/settings.local.json`
- `skills-lock.json`

## E. Archivos propios de Index 1A

Detectados como propios o candidatos del lote Index 1A:

- `Views/Venta/Index_tw.cshtml`
- `Views/Venta/_VentaModuleStyles.cshtml`
- `wwwroot/css/venta-index-rework.css`
- `wwwroot/js/venta-index-rework.js`
- `TheBuryProyect.Tests/Unit/VentaCreateUiContractTests.cs`
- `docs/kira-ventas-index-rework-1a-visual-parity.md`

Advertencia:

- `TheBuryProyect.Tests/Unit/VentaCreateUiContractTests.cs` no esta limpio para Index 1A: contiene eliminacion de tests del modal y una expectativa de `venta-page-wizard.js`, por lo que mezcla deuda/cambios del rework page-wizard o cleanup previo.

## F. Archivos restaurados

No se restauraron archivos desde `HEAD` en esta ejecucion.

Motivo:

- `Views/Venta/Create_tw.cshtml` existe.
- `Views/Venta/Edit_tw.cshtml` existe.
- No aparecieron eliminados en `git status --porcelain=v1`.

## G. HTML objetivo disponible o faltante

Faltante.

En esta ejecucion no se proporciono el HTML objetivo completo del Centro de Ventas. Por eso no se creo `docs/kira-ventas-index-html-objetivo.md`.

La proxima fase no deberia continuar con ajuste fino de paridad visual hasta que el usuario pegue o entregue el HTML objetivo completo.

## H. Validaciones ejecutadas

- `git status --short --branch`
- `git branch --show-current`
- `git log --oneline -10`
- `git status --porcelain=v1`
- `git diff --name-status`
- `git diff --stat`
- `git diff --cached --name-status`
- `node --check wwwroot/js/venta-index-rework.js`
- `node --check wwwroot/js/venta-index.js`
- `node --check wwwroot/js/venta-module.js`
- `dotnet build --configuration Release`
- `dotnet test --configuration Release --filter "IndexView"`
- `dotnet test --configuration Release --filter "VentaCreate"`
- `git diff --check -- Views/Venta/Index_tw.cshtml Views/Venta/_VentaModuleStyles.cshtml wwwroot/css/venta-index-rework.css wwwroot/js/venta-index-rework.js TheBuryProyect.Tests/Unit/VentaCreateUiContractTests.cs docs/kira-ventas-index-rework-1a-visual-parity.md`
- `git diff --check`

## I. Resultado build

`dotnet build --configuration Release`: OK.

Resultado exacto:

- 0 warnings.
- 0 errors.
- Tiempo: `00:01:30.85`.

## J. Resultado IndexView

`dotnet test --configuration Release --filter "IndexView"`: OK.

Resultado exacto:

- Failed: 0.
- Passed: 7.
- Skipped: 0.
- Total: 7.

## K. Resultado VentaCreate

`dotnet test --configuration Release --filter "VentaCreate"`: FAIL.

Resultado exacto:

- Failed: 1.
- Passed: 115.
- Skipped: 0.
- Total: 116.

Falla:

- Test: `TheBuryProject.Tests.Unit.VentaCreateUiContractTests.CreateView_CargaJsWizardDePagina`
- Archivo: `TheBuryProyect.Tests/Unit/VentaCreateUiContractTests.cs:1331`
- Error: `Assert.Contains() Failure: Sub-string not found`
- Esperado: `venta-page-wizard.js`
- String leido: `Views/Venta/Create_tw.cshtml`

Interpretacion:

- La falla ya no es `FileNotFoundException`.
- `Create_tw.cshtml` esta presente.
- El test modificado espera un contrato de page-wizard que no esta en la vista actual de `HEAD`.
- Esto bloquea el commit seguro de Index 1A porque el archivo de tests mezcla cambios fuera de alcance.

## L. Estado git final

Estado final observado despues de des-stagear borrados legacy/modal:

- `.claude/settings.local.json` modificado.
- `AGENTS.md` modificado.
- `CLAUDE.md` modificado.
- `TheBuryProyect.Tests/Unit/VentaCreateUiContractTests.cs` modificado.
- `Views/Producto/Unidades.cshtml` modificado.
- `Views/Venta/Create_tw_legacy.cshtml` eliminado unstaged.
- `Views/Venta/Index_tw.cshtml` modificado.
- `Views/Venta/_VentaCrearModal.cshtml` eliminado unstaged.
- `Views/Venta/_VentaModuleStyles.cshtml` modificado.
- `docs/misa-catalogo-ux-1g-aria-live-modales.md` modificado.
- `skills-lock.json` eliminado unstaged.
- `wwwroot/css/venta-modal-rework.css` eliminado unstaged.
- `wwwroot/js/venta-crear-modal.js` eliminado unstaged.
- `wwwroot/js/venta-create.js` modificado.
- `wwwroot/js/venta-modal-rework.js` eliminado unstaged.
- Nuevos: `docs/kira-ventas-index-rework-1a-visual-parity.md`, `docs/kira-ventas-index-rework-1b-regularizacion.md`, `docs/kira-ventas-page-rework-reset-0-cleanup-legacy.md`, `wwwroot/css/venta-index-rework.css`, `wwwroot/css/venta-page-wizard.css`, `wwwroot/js/venta-index-rework.js`, `wwwroot/js/venta-page-wizard.js`.

`git diff --cached --name-status`: sin archivos staged.

`git diff --check` scoped a archivos Index 1A: OK.

`git diff --check` global: FAIL por trailing whitespace en `AGENTS.md` y `CLAUDE.md`, fuera de alcance.

## M. Decision: no commitear

No se commitea Index 1A en esta fase.

Motivos:

- No esta disponible el HTML objetivo completo.
- `VentaCreate` falla.
- `TheBuryProyect.Tests/Unit/VentaCreateUiContractTests.cs` mezcla expectativas de page-wizard/modal fuera del lote Index 1A.
- El arbol mantiene cambios sensibles fuera de alcance: `AGENTS.md`, `CLAUDE.md`, `.claude/settings.local.json`, `skills-lock.json`.
- Hay borrados legacy/modal fuera de alcance que deben decidirse o commitearse en otro lote.
- No hay staged final, por lo que no hay riesgo inmediato de commit accidental.

## N. Proximo paso

Antes de `KIRA-VENTAS-INDEX-REWORK-1C`, hace falta decision explicita:

1. Proveer el HTML objetivo completo del Centro de Ventas para guardarlo en `docs/kira-ventas-index-html-objetivo.md`.
2. Separar o resolver el lote page-wizard/cleanup que afecta `VentaCreateUiContractTests.cs`, `wwwroot/js/venta-create.js`, los archivos `venta-page-wizard.*` y los borrados legacy/modal.
3. Luego correr QA visual desktop/mobile de `/Venta` con el HTML objetivo completo.

Proximo prompt recomendado:

`KIRA-VENTAS-INDEX-REWORK-1C - QA visual desktop/mobile de /Venta con HTML objetivo completo`
