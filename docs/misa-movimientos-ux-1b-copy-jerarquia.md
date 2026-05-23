# MISA-MOVIMIENTOS-UX-1B - Copy y jerarquia visual de movimientos

## A. Objetivo

Mejorar claridad visual, copy operativo, contraste y navegacion simple en Movimientos, Kardex, modal de movimientos de Catalogo y AlertaStock.

## B. Base y contexto

Fase Razor-only basada en:

- `docs/misa-movimientos-ux-0-auditoria.md`
- `docs/misa-movimientos-ux-1a-accesibilidad-semantica.md`

La fase parte de `main` en `c3babd8`, despues de integrar `MISA-MOVIMIENTOS-UX-1A`.

## C. Deuda tomada desde MISA-MOVIMIENTOS-UX-0 y 1A

- Copy misleading `Total Movimientos (Hoy)` en el modal de movimientos.
- Copy confuso `Volver al Inventario` para cerrar el modal.
- Headers de tablas con `text-slate-400` en Index y Kardex.
- Textos secundarios criticos con `text-slate-500`.
- Columna `Saldo post.` sin ayuda contextual.
- Kardex sin link secundario de vuelta a Movimientos Index.
- AlertaStock sin link operativo directo a Kardex/Movimientos.

## D. Archivos auditados

- `Views/Catalogo/Index_tw.cshtml`
- `Views/MovimientoStock/Index_tw.cshtml`
- `Views/MovimientoStock/Kardex_tw.cshtml`
- `Views/AlertaStock/Index_tw.cshtml`
- `ViewModels/AlertaStockViewModel.cs`
- `Controllers/MovimientoStockController.cs`
- tests y specs relacionados disponibles en `TheBuryProyect.Tests` y `e2e`

## E. Hallazgos previos

- `Total Movimientos (Hoy)` estaba en el stat card del modal de movimientos.
- `Volver al Inventario` estaba dentro del boton `id="btn-cerrar-movimientos"`.
- Ambos eran copy visual; el hook critico del cierre es el `id`, que se preservo.
- La columna `Saldo post.` estaba en los headers de Index y Kardex sin `title`.
- Kardex tenia link iconico a Catalogo, pero no link secundario a Movimientos Index.
- `AlertaStockViewModel` expone `ProductoId`, y `MovimientoStock/Kardex/{id}` existe como ruta GET.

## F. Cambios aplicados por archivo

### `Views/Catalogo/Index_tw.cshtml`

- `Volver al Inventario` se cambio por `Cerrar`.
- `Total Movimientos (Hoy)` se cambio por `Total movimientos`.

### `Views/MovimientoStock/Index_tw.cshtml`

- Headers de tabla cambiados de `text-slate-400` a `text-slate-300`.
- `Saldo post.` recibio `title="Stock resultante después del movimiento"`.
- Hora, codigo de producto y contador de resultados pasaron de `text-slate-500` a `text-slate-400`.

### `Views/MovimientoStock/Kardex_tw.cshtml`

- Se agrego link secundario `Volver a movimientos` hacia `MovimientoStock/Index`.
- Headers de tabla cambiados de `text-slate-400` a `text-slate-300`.
- `Saldo post.` recibio `title="Stock resultante después del movimiento"`.
- Codigo de producto, hora y `Sin motivo registrado` pasaron de `text-slate-500` a `text-slate-400`.

### `Views/AlertaStock/Index_tw.cshtml`

- Se agrego accion secundaria `Kardex` por fila usando `asp-controller="MovimientoStock"`, `asp-action="Kardex"` y `asp-route-id="@a.ProductoId"`.

## G. Copy corregido

- `Total Movimientos (Hoy)` -> `Total movimientos`.
- `Volver al Inventario` -> `Cerrar`.

## H. Contraste mejorado

- Headers de tablas en Index y Kardex subieron a `text-slate-300`.
- Textos secundarios operativos seleccionados subieron de `text-slate-500` a `text-slate-400`.

## I. Tooltip agregado

- `Saldo post.` ahora explica: `Stock resultante después del movimiento`.

## J. Navegacion agregada o decision de no agregar

- Kardex conserva el link actual a Catalogo.
- Kardex suma `Volver a movimientos` hacia `MovimientoStock/Index`.
- AlertaStock suma link por fila a Kardex porque `ProductoId` esta disponible en el modelo.

## K. AlertaStock

El cambio fue seguro y Razor-only: no requirio ViewModel nuevo, controller, JS, endpoint ni query params.

## L. Contratos preservados

- Se preservo `id="btn-cerrar-movimientos"`.
- Se preservaron clases existentes del boton de cierre.
- Se preservaron `asp-*`, antiforgery, formularios, filtros, paginacion, columnas y orden de datos.
- No se tocaron hooks JS ni payloads.

## M. Que no se toco

- Backend, controllers, services, models, viewmodels y migraciones.
- JavaScript.
- CSS.
- Endpoints, payloads, permisos, PDF/Excel, filtros, paginacion y calculos de stock.

## N. Accesibilidad / baja vision

Los cambios reducen ambiguedad de copy, mejoran contraste en zonas de lectura repetida y agregan contexto a una abreviatura operacional.

## O. Riesgo funcional

Riesgo bajo. Los cambios son de Razor, texto, clases Tailwind existentes y navegacion hacia rutas ya existentes.

## P. Tests y validaciones

- `dotnet build --configuration Release`: sin resultado final por timeout local despues de 124 segundos.
- Revision de procesos: se detecto `TheBuryProyect.exe` Debug preexistente (PID 22124), no iniciado por esta tarea.
- `dotnet build --configuration Release -o tmpbuild_misa_movimientos_ux_1b`: compilacion correcta, 0 errores, 1 warning `NETSDK1194` esperado por usar `-o` a nivel solucion.
- `git diff --check`: pendiente al cierre.
- `git status --short`: pendiente al cierre.

No se ejecutaron tests por defecto porque no se modificaron tests ni contratos compartidos de layout. No se ejecuto suite general.

## Q. Playwright

No se ejecuto Playwright. La inspeccion encontro `e2e/ui-4e-layout-visual.spec.js`, pero no una spec especifica de Movimientos/AlertaStock; el cambio visual fue localizado y no toco JS.

## R. Procesos / file-lock

- Build directo: timeout local despues de 124 segundos, sin salida concluyente.
- Proceso preexistente documentado: PID 22124, `E:\theburyproject1\bin\Debug\net8.0\TheBuryProyect.exe`.
- No se cerro porque no fue iniciado por esta tarea.
- Build alternativo en `tmpbuild_misa_movimientos_ux_1b`: OK.

## S. Deudas restantes

- `MISA-MOVIMIENTOS-UX-1C`: mobile / scroll affordance en tablas anchas de Index y Kardex.
- AlertaStock puede recibir una fase dedicada para revisar headers, `scope`, contraste y navegacion completa si se quiere ampliar el frente.

## T. Proximo paso recomendado

`MISA-MOVIMIENTOS-UX-1C - Mobile / scroll affordance`:

- agregar `data-oc-scroll` en Index;
- agregar `data-oc-scroll` en Kardex;
- cargar affordance horizontal si corresponde;
- mantener alcance sin backend ni logica funcional.
