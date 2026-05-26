# KIRA-VENTAS-INDEX-REWORK-1B2 - Regularizar page wizard antes de Index Ventas

## A. Objetivo

Regularizar el lote previo de page-wizard/cleanup para que `VentaCreate` deje de bloquear el commit separado de `KIRA-VENTAS-INDEX-REWORK-1A`, sin redisenar Index y sin tocar backend.

## B. Estado inicial

Rama: `kira/ventas-index-rework-1a-visual-parity`.

Estado inicial observado:

- `Build` reportado previamente como OK, pero en esta ejecucion el primer `dotnet build --configuration Release` hizo timeout a los 124 s.
- `IndexView` reportado previamente OK 7/7.
- `VentaCreate` reportado previamente FAIL: 115 passed, 1 failed.
- Arbol sucio con cambios propios de Index 1A, cleanup page-wizard y archivos ajenos/sensibles.
- Sin commit, push ni merge.

## C. Causa exacta del fallo VentaCreate

El test `CreateView_CargaJsWizardDePagina` esperaba `venta-page-wizard.js` en `Views/Venta/Create_tw.cshtml`, pero la vista seguia cargando `~/js/venta-modal-rework.js`.

Evidencia:

- `wwwroot/js/venta-page-wizard.js` existe.
- `wwwroot/css/venta-page-wizard.css` existe.
- `wwwroot/js/venta-modal-rework.js` no existe.
- `wwwroot/css/venta-modal-rework.css` no existe.
- `_VentaModuleStyles.cshtml` ya carga `venta-page-wizard.css`.
- `docs/kira-ventas-page-rework-reset-0-cleanup-legacy.md` documenta que `Create_tw.cshtml` debe cargar `venta-page-wizard.js`.

## D. Opcion elegida A/B

Opcion A.

Motivo: los assets `venta-page-wizard.js` y `venta-page-wizard.css` ya existen y forman parte del cleanup previsto. La vista estaba desfasada con respecto al contrato y al documento de cleanup.

## E. Cambios aplicados

- `Views/Venta/Create_tw.cshtml`: se reemplazo la carga de `venta-modal-rework.js` por `venta-page-wizard.js`.

No se cambio:

- `#venta-create-page`.
- `_VentaModuleStyles.cshtml`, porque ya cargaba `venta-page-wizard.css`.
- Tests.
- Backend.
- Endpoints.
- Payloads.
- Rutas.

## F. HTML objetivo guardado o motivo si no se pudo

No se pudo guardar `docs/kira-ventas-index-html-objetivo.md`.

Motivo: en esta ejecucion no esta disponible el HTML objetivo completo del Centro de Ventas. Segun el prompt, si no esta disponible debe pedirse antes de continuar con ese documento.

## G. Archivos propios de Index 1A

Detectados como propios o candidatos del lote Index 1A:

- `Views/Venta/Index_tw.cshtml`
- `wwwroot/css/venta-index-rework.css`
- `wwwroot/js/venta-index-rework.js`
- `docs/kira-ventas-index-rework-1a-visual-parity.md`

`Views/Venta/_VentaModuleStyles.cshtml` queda relacionado con carga de estilos del modulo; en esta fase no se modifico.

## H. Archivos fuera de alcance

No deben entrar en los commits de esta fase:

- `.claude/settings.local.json`
- `AGENTS.md`
- `CLAUDE.md`
- `skills-lock.json`
- `Views/Producto/Unidades.cshtml`
- `docs/misa-catalogo-ux-1g-aria-live-modales.md`
- `tmpbuild*`
- `test-results`
- `playwright-report`
- screenshots
- logs temporales
- archivos ajenos

## I. Validaciones

Ejecutadas:

- `git status --short --branch`
- `git diff --name-status`
- `git diff --stat`
- `git log --oneline -10`
- `Test-Path` de vistas y assets indicados
- `Select-String` de referencias `venta-page-wizard`, `venta-modal-rework`, `modal-crear-venta`, `VentaCrearModal`, `venta-crear-modal`
- `node --check wwwroot/js/venta-index-rework.js`
- `node --check wwwroot/js/venta-index.js`
- `node --check wwwroot/js/venta-module.js`
- `node --check wwwroot/js/venta-page-wizard.js`
- `dotnet build --configuration Release`
- `dotnet build --configuration Release --no-restore`
- `dotnet test --configuration Release --no-build --filter "IndexView"`
- `dotnet test --configuration Release --no-build --filter "VentaCreate"`

## J. Resultado build

- `dotnet build --configuration Release`: timeout despues de 124 s. No se cuenta como OK.
- `dotnet build --configuration Release --no-restore`: OK.

Resultado exacto del build OK:

- 0 warnings.
- 0 errors.
- Tiempo: `00:01:20.02`.

## K. Resultado IndexView

`dotnet test --configuration Release --no-build --filter "IndexView"`: OK.

Resultado exacto:

- Failed: 0.
- Passed: 7.
- Skipped: 0.
- Total: 7.
- Duracion: 548 ms.

## L. Resultado VentaCreate

`dotnet test --configuration Release --no-build --filter "VentaCreate"`: OK.

Resultado exacto:

- Failed: 0.
- Passed: 116.
- Skipped: 0.
- Total: 116.
- Duracion: 1 s.

## M. Estado git final

Verificado despues del cambio:

- `git diff --check` scoped a archivos de esta zona: OK.
- No se ejecuto el `git diff --check` scoped incluyendo `docs/kira-ventas-index-html-objetivo.md` porque ese archivo no existe todavia por falta del HTML objetivo completo.
- `git status --short --branch` sigue mostrando arbol sucio.

Archivos relevantes de esta microfase:

- `Views/Venta/Create_tw.cshtml` modificado.
- `docs/kira-ventas-index-rework-1b2-regularizar-page-wizard.md` nuevo.

Archivos propios/candidatos de Index 1A siguen sin commitear:

- `Views/Venta/Index_tw.cshtml`
- `wwwroot/css/venta-index-rework.css`
- `wwwroot/js/venta-index-rework.js`
- `docs/kira-ventas-index-rework-1a-visual-parity.md`

Archivos sensibles/ajenos siguen presentes en el arbol y no deben stagearse:

- `.claude/settings.local.json`
- `AGENTS.md`
- `CLAUDE.md`
- `skills-lock.json`
- `Views/Producto/Unidades.cshtml`
- `docs/misa-catalogo-ux-1g-aria-live-modales.md`

## N. Decision: commitear o no

No commitear todavia.

Motivos:

- Falta `docs/kira-ventas-index-html-objetivo.md` porque no se recibio el HTML objetivo completo en esta ejecucion.
- El prompt exige que el HTML objetivo quede guardado antes de decidir commit.
- El arbol sigue mezclando archivos propios, cleanup y archivos sensibles/ajenos que no deben stagearse.

## O. Proximo paso

Pedir al usuario el HTML objetivo completo del Centro de Ventas. Luego:

- crear `docs/kira-ventas-index-html-objetivo.md`;
- repetir `git diff --check` scoped;
- revisar `git status --short`;
- si todo sigue verde, commitear primero 1B2 y despues Index 1A en commit separado.
