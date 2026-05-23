# MISA-MOVIMIENTOS-UX-1A - Accesibilidad semantica de Movimientos

## A. Objetivo

Aplicar correcciones semanticas minimas en Movimientos de Inventario sin cambiar comportamiento, estilos, JavaScript, backend, endpoints ni reglas de negocio.

## B. Base y contexto

- Base: `main` en `646ccbc - Corregir whitespace en auditoria de movimientos`.
- Documento base: `docs/misa-movimientos-ux-0-auditoria.md`.
- Fase: Razor-only / accesibilidad semantica / bajo riesgo.

## C. Deuda tomada desde MISA-MOVIMIENTOS-UX-0

- Labels de filtros del modal Movimientos sin `for`.
- Cabeceras de la tabla del modal sin `scope="col"`.
- Titulo del modal como `h1` dentro de una pagina que ya tiene jerarquia propia.
- Mensajes `TempData["Success"]` y `TempData["Error"]` en Index y Kardex sin roles accesibles.

## D. Archivos auditados

- `docs/misa-movimientos-ux-0-auditoria.md`
- `Views/Catalogo/Index_tw.cshtml`
- `Views/MovimientoStock/Index_tw.cshtml`
- `Views/MovimientoStock/Kardex_tw.cshtml`
- Tests y specs disponibles relacionados con Movimiento, Stock, Kardex, Catalogo, Layout y visual.

## E. Hallazgos previos

- El modal Movimientos esta inline en `Views/Catalogo/Index_tw.cshtml`, seccion `modal-movimientos`.
- `aria-labelledby` apunta a `modal-movimientos-title`.
- Los controles de filtros ya tenian IDs: `mov-fecha-desde`, `mov-fecha-hasta`, `mov-tipo`, `mov-producto`, `mov-fuente-costo`.
- La tabla del modal tenia 11 cabeceras `th` sin `scope`.
- `MovimientoStock/Index_tw.cshtml` y `MovimientoStock/Kardex_tw.cshtml` ya tenian tablas con `scope="col"` correcto, pero TempData sin roles.

## F. Cambios aplicados por archivo

### `Views/Catalogo/Index_tw.cshtml`

- Se agrego `for` a labels de filtros del modal Movimientos.
- Se agrego `scope="col"` a las 11 cabeceras de la tabla del modal.
- Se cambio el titulo del modal de `h1` a `h2`, preservando `id`, clases y texto.

### `Views/MovimientoStock/Index_tw.cshtml`

- Se agrego `role="status"` al contenedor de `TempData["Success"]`.
- Se agrego `role="alert"` al contenedor de `TempData["Error"]`.

### `Views/MovimientoStock/Kardex_tw.cshtml`

- Se agrego `role="status"` al contenedor de `TempData["Success"]`.
- Se agrego `role="alert"` al contenedor de `TempData["Error"]`.

## G. Labels asociados

- `Rango de Fechas` -> `for="mov-fecha-desde"`.
- `Tipo de Movimiento` -> `for="mov-tipo"`.
- `Producto (SKU/Nombre)` -> `for="mov-producto"`.
- `Fuente de Costo` -> `for="mov-fuente-costo"`.

## H. `scope="col"` agregados

Se agrego `scope="col"` a las 11 columnas del modal Movimientos:

- Fecha/Hora
- Tipo
- Producto
- Cant.
- Costo unitario
- Costo total
- Fuente costo
- Referencia
- Usuario
- Saldo Post.
- Accion

## I. Cambio `h1` a `h2`

El titulo `Movimientos de Inventario` ahora usa `h2`, preservando `id="modal-movimientos-title"` y el `aria-labelledby` del dialog.

## J. Roles TempData agregados

- Success: `role="status"`.
- Error: `role="alert"`.

Aplicado en `MovimientoStock/Index_tw.cshtml` y `MovimientoStock/Kardex_tw.cshtml`.

## K. Contratos preservados

Se preservaron:

- IDs existentes.
- Clases existentes.
- Textos visibles.
- Orden y cantidad de columnas.
- Estructura de `thead` y `tbody`.
- Hooks usados por JS.
- Endpoints y payloads.
- Antiforgery y contratos Razor existentes.

## L. Que no se toco

No se tocaron:

- JavaScript.
- CSS.
- Controllers.
- Services.
- Models.
- ViewModels.
- Migrations.
- DTOs.
- Endpoints.
- Payloads.
- Permisos.
- PDF/Excel.
- Reglas de negocio.
- Stock funcional.
- `MovimientoStockService`.
- AlertaStock.

## M. Accesibilidad / baja vision

La fase mejora navegacion por lector de pantalla y asociacion label-control sin alterar experiencia visual. Los roles en mensajes permiten anunciar exito o error con semantica adecuada.

## N. Riesgo funcional

Riesgo bajo: los cambios son atributos HTML y jerarquia semantica en Razor. No se modifico logica, datos enviados, selectores JS, estilos ni rutas.

## O. Tests y validaciones

- Build Release requerido por tocar Razor.
- Tests no ejecutados por defecto: no se modificaron tests ni contratos compartidos de layout.
- `git diff --check` requerido.
- `git status --short` requerido.

## P. Playwright

No corresponde ejecutar Playwright por defecto: la fase no cambia layout visual, JS ni flujos funcionales.

## Q. Procesos / file-lock si aplica

Pendiente completar al cierre con resultado real de build y revision de procesos.

## R. Deudas restantes

- UX visual / jerarquia y copy queda para `MISA-MOVIMIENTOS-UX-1B`.
- No se corrigio copy `Total Movimientos (Hoy)`.
- No se cambio `Volver al Inventario`.
- No se agrego tooltip nuevo a `Saldo post.`.
- No se agregaron enlaces nuevos entre Index, Kardex o AlertaStock.

## S. Proximo paso recomendado

`MISA-MOVIMIENTOS-UX-1B - UX visual / jerarquia y copy`, manteniendo alcance sin backend si es posible.
