# MISA-MOVIMIENTOS-UX-1C - Mobile / scroll affordance en Movimientos y Kardex

## A. Objetivo

Mejorar la experiencia mobile de las tablas anchas de `MovimientoStock/Index` y `MovimientoStock/Kardex` usando el patron existente de scroll affordance del ERP.

La fase se limito a Razor y carga de assets existentes. No se cambiaron reglas de negocio, endpoints, payloads, columnas, filtros ni datos.

## B. Base y contexto

- Base esperada: `main` en `652da8a` - `Mejorar copy y jerarquia visual de movimientos (MISA-MOVIMIENTOS-UX-1B)`.
- Rama de trabajo: `misa/movimientos-ux-1c-mobile-scroll`.
- Frente activo: Inventario / Movimientos / Kardex.
- Tipo de fase: Razor-only con carga de CSS/JS existente.

## C. Deuda tomada desde MISA-MOVIMIENTOS-UX-0 y 1B

La auditoria `docs/misa-movimientos-ux-0-auditoria.md` ya habia identificado que:

- `MovimientoStock/Index` usa `min-w-[960px]` y `overflow-x-auto`, pero no tenia `data-oc-scroll`.
- `MovimientoStock/Kardex` usa `min-w-[1100px]` y `overflow-x-auto`, pero no tenia `data-oc-scroll`.
- `horizontal-scroll-affordance.css` y `horizontal-scroll-affordance.js` estaban ausentes en estas vistas.
- AlertaStock y Catalogo ya usaban el patron como referencia.

MISA-MOVIMIENTOS-UX-1B dejo cerrado copy, contraste y navegacion; esta fase no reabre esos temas.

## D. Archivos auditados

- `docs/misa-movimientos-ux-0-auditoria.md`
- `Views/MovimientoStock/Index_tw.cshtml`
- `Views/MovimientoStock/Kardex_tw.cshtml`
- `Views/AlertaStock/Index_tw.cshtml`
- `Views/Catalogo/Index_tw.cshtml`
- `wwwroot/css/horizontal-scroll-affordance.css`
- `wwwroot/js/horizontal-scroll-affordance.js`
- tests bajo `TheBuryProyect.Tests` relacionados con Movimiento/Stock/Kardex/Layout
- specs bajo `e2e`

## E. Hallazgos mobile

- Index ya tenia overflow horizontal mediante `overflow-x-auto`, pero sin affordance visual.
- Kardex ya tenia overflow horizontal mediante `overflow-x-auto`, pero sin affordance visual.
- Kardex es mas critico en mobile porque su tabla minima es mas ancha (`1100px`).
- No habia spec e2e especifica para Movimientos/Kardex/mobile/scroll; solo se encontro `e2e/ui-4e-layout-visual.spec.js`.

## F. Hallazgos del patron data-oc-scroll

- AlertaStock carga `horizontal-scroll-affordance.css` en `@section Styles`.
- AlertaStock carga `horizontal-scroll-affordance.js` y luego `alerta-stock-index.js`, que inicializa todos los `[data-oc-scroll]`.
- Catalogo carga el CSS en `@section Styles`.
- Catalogo carga `horizontal-scroll-affordance.js` antes de `catalogo-module.js` / `catalogo-index.js`, y `catalogo-index.js` inicializa todos los `[data-oc-scroll]`.
- `horizontal-scroll-affordance.js` no inicializa automaticamente todos los contenedores al cargarse. Expone `window.TheBury.initHorizontalScrollAffordance`.
- La inicializacion requiere un root con `data-oc-scroll` y un hijo `data-oc-scroll-region`.
- `data-oc-scroll-hint` es opcional. En esta fase no se agrego texto visible nuevo; se usaron fades laterales del patron.

## G. Cambios aplicados por archivo

### `Views/MovimientoStock/Index_tw.cshtml`

- Se cargo `~/css/horizontal-scroll-affordance.css` en `@section Styles`.
- El wrapper principal de la tabla paso a usar `data-oc-scroll` y `--oc-scroll-min-width: 960px`.
- Se preservo `table-erp-wrapper`.
- Se agrego `data-oc-scroll-shell`.
- Se agregaron fades izquierdo/derecho con `data-oc-scroll-fade`.
- Se movio el `overflow-x-auto` al `data-oc-scroll-region`, como en el patron existente.
- Se agrego `tabindex="0"`, `role="region"` y `aria-label`.
- Se agrego `data-oc-scroll-table` a la tabla.
- Se cargo `~/js/horizontal-scroll-affordance.js` en `@section Scripts`.
- Se agrego inicializacion inline minima para los roots `[data-oc-scroll]`.

### `Views/MovimientoStock/Kardex_tw.cshtml`

- Se cargo `~/css/horizontal-scroll-affordance.css` en `@section Styles`.
- El wrapper principal de la tabla paso a usar `data-oc-scroll` y `--oc-scroll-min-width: 1100px`.
- Se preservo `table-erp-wrapper`.
- Se agrego `data-oc-scroll-shell`.
- Se agregaron fades izquierdo/derecho con `data-oc-scroll-fade`.
- Se movio el `overflow-x-auto` al `data-oc-scroll-region`, como en el patron existente.
- Se agrego `tabindex="0"`, `role="region"` y `aria-label`.
- Se agrego `data-oc-scroll-table` a la tabla.
- Se cargo `~/js/horizontal-scroll-affordance.js` en `@section Scripts`.
- Se agrego inicializacion inline minima para los roots `[data-oc-scroll]`.

## H. Assets existentes cargados o decision de no cargarlos

Se cargaron assets existentes:

- `wwwroot/css/horizontal-scroll-affordance.css`
- `wwwroot/js/horizontal-scroll-affordance.js`

No se modificaron esos archivos.

## I. Contratos preservados

- Columnas y orden de columnas.
- Datos renderizados.
- Links `asp-controller`, `asp-action` y `asp-route-id`.
- Filtros de Index.
- Botones existentes.
- TempData existente.
- Clases operativas de tabla y filas.
- `scope="col"` ya existente.
- Endpoints y payloads.

## J. Que no se toco

- Controllers.
- Services.
- Models.
- ViewModels.
- Migrations.
- DTOs.
- JS de logica.
- CSS global o archivos CSS existentes.
- Modal Movimientos.
- Catalogo.
- AlertaStock.
- Producto/Unidades.
- PDF/Excel.
- Filtros, paginacion, permisos, stock funcional, calculos o reglas de negocio.

## K. Accesibilidad / baja vision

- El contenedor scrolleable ahora es focusable con teclado mediante `tabindex="0"`.
- El contenedor expone `role="region"` y `aria-label` descriptivo.
- Los fades laterales existentes ayudan a senalar contenido fuera del viewport.
- No se agrego copy visible nuevo para evitar reabrir la fase de copy cerrada en 1B.

## L. Riesgo funcional

Riesgo bajo.

El cambio afecta solo estructura HTML alrededor de tablas ya existentes y carga de assets existentes. No cambia backend ni datos.

## M. Tests y validaciones

Validaciones ejecutadas:

- `dotnet build --configuration Release`
- `git diff --check`
- `git diff --check -- Views/MovimientoStock/Index_tw.cshtml Views/MovimientoStock/Kardex_tw.cshtml docs/misa-movimientos-ux-1c-mobile-scroll.md`
- `git status --short`

Resultado de build:

- `dotnet build --configuration Release` no devolvio resultado antes del timeout del wrapper.
- Se cerro el MSBuild huerfano iniciado por esa ejecucion.
- Se ejecuto una sola vez `dotnet build --configuration Release -o tmpbuild_misa_movimientos_ux_1c`.
- Resultado impreso por la compilacion temporal: `Compilacion correcta`, `1 Advertencia(s)`, `0 Errores`.
- Warning observado: `NETSDK1194`, esperado por usar `-o` al compilar una solucion.
- El wrapper del comando temporal vencio al final, despues de imprimir el resultado correcto.
- `tmpbuild_misa_movimientos_ux_1c` fue eliminado.

No se planifico `dotnet test` por no tocar tests ni reglas funcionales.

## N. Playwright

No se planifico Playwright por defecto. La inspeccion encontro solo `e2e/ui-4e-layout-visual.spec.js`, sin spec especifica de Movimientos/Kardex/mobile/scroll.

## O. Procesos / file-lock si aplica

Al inicio habia procesos preexistentes relacionados con VS Code, C# DevKit, Playwright MCP, Context7 MCP y una app `TheBuryProyect.exe` en Debug. No fueron iniciados por esta fase.

Durante la fase se cerro solo el proceso MSBuild huerfano iniciado por el build directo:

- PID `33896`
- Command line: `dotnet.exe ... MSBuild.dll ... /nodeReuse:true`

No se cerraron procesos preexistentes.

## P. Deudas restantes

- No existe spec e2e especifica para Movimientos/Kardex/mobile/scroll.
- El modal Movimientos de Catalogo sigue fuera de alcance de esta fase.
- PDF/Excel visual queda para MISA-MOVIMIENTOS-UX-1D.

## Q. Proximo paso recomendado

MISA-MOVIMIENTOS-UX-1D - PDF/Excel visual fix:

- resolver botones PDF/Excel visibles pero no funcionales;
- deshabilitarlos con `title` / `aria-disabled` u ocultarlos temporalmente;
- sin backend;
- sin implementar export real;
- sin tocar JS si puede resolverse en Razor.
