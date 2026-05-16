# Fase: Carlos — Cleanup defensivo Ventas/Create modal

**Rama:** `carlos/cleanup-ventas-create-modal`
**Base:** `carlos/fix-ventas-create-e2e` (cherry-picked d69826e) + `main` (fa304b2)

---

## A. Objetivo

Cerrar tres deudas técnicas menores documentadas al cierre del fix anterior:

1. Null guard defensivo en `venta-create.js` línea ~1024
2. Inconsistencia `produto`/`producto` — diagnóstico y documentación
3. Tabla modal: 8 headers vs 7 columnas renderizadas

---

## B. Diagnóstico de `produto`/`producto`

**Resultado: no hay inconsistencia real.**

Todos los IDs en modal, `Create_tw.cshtml` y `venta-create.js` usan `hdn-producto-*` (con 'c', ortografía española). El reporte original de deuda fue impreciso — los IDs son consistentes en toda la codebase.

La `$` en `venta-create.js` línea 70 es `const $ = (sel) => document.querySelector(sel)`, no jQuery. Esto es relevante para el punto C.

---

## C. Cambio defensivo en JS

**Archivo:** `wwwroot/js/venta-create.js` (línea 1024)

**Antes:**
```javascript
hdnProductoRequiereNumeroSerie.value = requiereNumeroSerie ? 'true' : 'false';
```

**Después:**
```javascript
if (hdnProductoRequiereNumeroSerie) hdnProductoRequiereNumeroSerie.value = requiereNumeroSerie ? 'true' : 'false';
```

**Por qué:** `$` es `document.querySelector`, no jQuery. Si el input `#hdn-producto-requiere-numero-serie` está ausente del DOM, la variable es `null` y el acceso a `.value` lanza `TypeError: Cannot set properties of null`. El fix anterior (d69826e) agregó el input al modal, pero este guard previene futura regresión si el input fuera removido accidentalmente.

**Decisión:** El input ya existe en el modal (incluido via cherry-pick), por lo que el guard es puramente preventivo. No se usó optional chaining (`?.=`) porque no es sintaxis válida para asignación en JavaScript.

---

## D. Cambio de tabla

**Archivo:** `Views/Venta/_VentaCrearModal.cshtml`

**Eliminado:** header `<th>Tipo de pago</th>` de la tabla de detalles del modal.

**Por qué:** La función `renderDetalles()` en `venta-create.js` genera 7 columnas:
1. Código
2. Nombre/Producto
3. Cantidad
4. Precio unitario
5. Descuento
6. Subtotal
7. Botón eliminar

El header "Tipo de pago" era un residuo legacy del flujo de pago por ítem (ya eliminado en fases anteriores). No tenía columna correspondiente, causando desalineación de la tabla. El header vacío `<th class="py-3 px-2"></th>` ya cubría la columna de acciones.

No hay `colspan` afectado — el estado vacío (`#detalles-vacio`) es un `<div>` externo a la tabla.

---

## E. Tests agregados/ajustados

**Archivo:** `TheBuryProyect.Tests/Unit/VentaCreateUiContractTests.cs`

Dos tests nuevos en sección `// ── Carlos cleanup: defensive JS + tabla modal`:

```csharp
VentaCreateJs_UsaNullGuardParaHdnProductoRequiereNumeroSerie
// Verifica que el JS tiene if (hdnProductoRequiereNumeroSerie) antes del .value =
// Previene regresión del TypeError original

VentaCrearModal_TablaDetalle_NoContieneHeaderLegacyTipoPagoPorItem
// Verifica que el header "Tipo de pago" fue removido de la tabla del modal
// Previene que se reinstale el header sin columna correspondiente
```

**Conteo:**
- Antes del cherry-pick: 36 tests
- Después del cherry-pick (d69826e): 40 tests
- Después de este cleanup: 42 tests

---

## F. Validaciones técnicas

| Check | Resultado |
|---|---|
| `dotnet build --configuration Release` | 0 errores, 0 advertencias |
| `VentaCreateUiContractTests` | 42/42 |
| Suite completa Venta+VentaApi+ConfiguracionPago+ProductoUnidad+CondicionesPago | 984/984 |
| `git diff --check` | sin whitespace issues |

---

## G. Qué NO se tocó

- Lógica de `VentaService`, `CajaService`, `StockService`
- `VentaController` y `VentaApiController`
- ViewModels de Venta
- `Create_tw.cshtml` (no requirió cambios)
- Otros inputs hidden (`hdn-producto-id`, `hdn-producto-codigo`, `hdn-producto-precio`, `hdn-producto-stock`) — ya existen en modal, no generan TypeError al presnte
- Tests preexistentes — ninguno fue modificado

---

## H. Riesgo remanente

1. **Otros `.value` sin guard (líneas 1020–1023):** `hdnProductoId`, `hdnProductoCodigo`, `hdnProductoPrecio`, `hdnProductoStock` también usan `.value` directo. Si faltara algún input del modal, lanzarían TypeError. Riesgo bajo: los 4 están en el panel `#panel-agregar-producto` que no cambia entre fases. No se tocó para mantener foco mínimo.

2. **Sub-modal "Tipo de pago del producto" (línea ~747):** Este sub-modal tiene texto "Tipo de pago del producto" que es una feature diferente al header removido. No es legacy — es funcional.

---

## I. Checklist

- [x] No hay TypeError si falta el hidden opcional
- [x] Hidden requerido por JS existe en el modal
- [x] IDs `producto` documentados y confirmados consistentes
- [x] Tabla modal: 7 headers alineados con 7 columnas JS
- [x] Build Release OK
- [x] Tests 42/42 OK
- [x] Suite Venta 984/984 OK
- [x] diff-check OK
