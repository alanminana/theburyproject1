# Fase Kira — Advertencia de stock sin identificar en venta

## A. Contexto

El sistema separa dos conceptos de stock:

- `Producto.StockActual` — stock agregado/comercial.
- `ProductoUnidad` — unidad física individual (opcional, solo para productos trazables).
- `MovimientoStock` — historial del stock agregado.

Regla central:
- `RequiereNumeroSerie = true` → `ProductoUnidadId` obligatorio al vender.
- `RequiereNumeroSerie = false` → sin selección de unidad física; solo descuenta `StockActual`.

Caso problemático detectado (previo a esta fase):

```
StockActual = 2
UnidadesEnStock = 1
Producto no trazable
Cantidad vendida = 2
→ StockActual final = 0, ProductoUnidad sigue EnStock, StockSinIdentificar = -1
```

No hay excepción ni corrupción directa, pero hay inconsistencia silenciosa. Carlos (fase venta-stock-breakdown-ui) expuso esta información en el DTO y en la UI. Esta fase agrega una advertencia no bloqueante cuando el flujo puede generar esa inconsistencia.

## B. Regla UX implementada

Mostrar advertencia si:

```
producto.requiereNumeroSerie === false
&& productoActualUnidadesEnStock > 0
&& (
    productoActualStockSinIdentificar < 0
    || cantidad > productoActualStockSinIdentificar
)
```

La advertencia no bloquea la venta. Es informativa.

## C. Casos en que aparece

1. **StockSinIdentificar < 0** (inconsistencia preexistente): aparece al seleccionar el producto, independiente de la cantidad.
2. **Cantidad > StockSinIdentificar** (venta que consumirá unidades no conciliadas): aparece al seleccionar producto o al cambiar cantidad.

Texto según caso:
- Negativo: `"Revisar conciliación: hay más unidades físicas registradas que stock agregado disponible."`
- Cantidad excede: `"Advertencia: la cantidad supera el stock sin identificar disponible. Hay unidades físicas registradas que no se asociarán automáticamente a esta venta."`

## D. Casos en que NO aparece

- Producto trazable (`RequiereNumeroSerie = true`): usa el selector de unidad física estándar.
- Producto sin unidades físicas registradas (`unidadesEnStock <= 0`): stock simple, sin inconsistencia posible.
- Cantidad <= StockSinIdentificar y StockSinIdentificar >= 0: operación normal.

## E. Cambios aplicados

### `wwwroot/js/venta-create.js`

- **State**: agregadas variables `productoActualUnidadesEnStock` y `productoActualStockSinIdentificar`.
- **DOM refs**: agregada `advertenciaStockSinIdentificar = $('#advertencia-stock-sin-identificar')`.
- **Dropdown item HTML**: agregados `data-unidades-en-stock` y `data-stock-sin-identificar` al template del item de búsqueda.
- **Click handler del dropdown**: lee y guarda los nuevos data attributes; llama `actualizarAdvertenciaStockSinIdentificar()` al seleccionar.
- **`txtCantidad` input handler**: llama `actualizarAdvertenciaStockSinIdentificar()` al cambiar cantidad.
- **Reset del panel** (post-agregar al carrito): resetea vars a 0 y oculta la advertencia.
- **Nueva función**: `actualizarAdvertenciaStockSinIdentificar()` — evalúa la regla, actualiza `textContent` y llama `show()`/`hide()`.

### `Views/Venta/Create_tw.cshtml`

- Agregado `<div id="advertencia-stock-sin-identificar" class="hidden col-span-2 md:col-span-5 ...">` entre `panel-selector-unidad` y el campo descuento.

### `Views/Venta/_VentaCrearModal.cshtml`

- Mismo div de advertencia agregado entre `stock-error` y el campo descuento.

## F. Tests agregados

En `TheBuryProyect.Tests/Unit/VentaCreateUiContractTests.cs`:

1. `VentaCreate_View_ContieneAdvertenciaStockSinIdentificar` — verifica existencia del contenedor en `Create_tw.cshtml`.
2. `VentaCreateJs_ContieneFuncionActualizarAdvertenciaStockSinIdentificar` — verifica que la función existe.
3. `VentaCreateJs_AdvertenciaEvaluaCantidadMayorAStockSinIdentificar` — verifica condición `cantidad > productoActualStockSinIdentificar` y `< 0`.
4. `VentaCreateJs_AdvertenciaSoloParaNoTrazablesConUnidades` — verifica uso de `requiereNumeroSerie` y `productoActualUnidadesEnStock`.
5. `VentaCreateJs_AdvertenciaNoBloqueaAgregarProducto` — verifica que el submit no bloquea por esta advertencia.

## G. Validaciones

- Build Release: OK (0 errores, 0 warnings).
- `VentaCreateUiContractTests`: 43/43 pasan.
- Suite amplio `Venta|VentaApiController|VentaController|ProductoUnidad|Conciliacion`: 890/890 pasan.
- `git diff --check`: sin whitespace issues.

## H. Qué NO se tocó

- `VentaService` — sin cambios.
- `ValidarUnidadesTrazablesAsync` — sin cambios.
- `ProductoUnidadService` — sin cambios.
- `MovimientoStockService` — sin cambios.
- Caja, Factura, Cotización — sin cambios.
- Migraciones — sin cambios.
- Payload enviado al backend — sin cambios.
- Endpoint `/api/ventas/BuscarProductos` — sin cambios (usa los campos ya enriquecidos por Carlos).

## I. Riesgos / deuda remanente

- La inconsistencia `StockSinIdentificar < 0` es pre-existente y no se resuelve aquí. Esta fase solo la expone con advertencia visual.
- Si se agregan otros puntos de entrada para seleccionar producto (ej. búsqueda por código, scan), deberán incluir los mismos data attributes y llamar `actualizarAdvertenciaStockSinIdentificar()`.
- La función usa `textContent` (no `innerHTML`) — segura contra XSS.
- Si `advertenciaStockSinIdentificar` no existe en el DOM (ej. modal sin el div), la función sale silenciosamente por el guard `if (!advertenciaStockSinIdentificar) return`.
