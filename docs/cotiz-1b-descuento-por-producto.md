# COTIZ-1B — Descuento por producto en líneas del simulador

**Rama:** `kira/cotiz-1b-descuento-por-producto`
**Base:** `d86c12a` (COTIZ-1A)
**Fecha:** 2026-05-21

---

## A. Objetivo

Agregar descuento individual por producto (porcentaje e importe) en el formulario/simulador de Cotización, usando capacidades ya existentes en el backend. El usuario puede cargar un descuento por línea antes de simular. Los descuentos generales (COTIZ-1A) siguen funcionando en paralelo.

---

## B. Auditoría de soporte backend

| Punto | Artefacto | Estado |
|---|---|---|
| Request de simulación | `CotizacionSimulacionRequest` → `List<CotizacionProductoRequest>` | ✅ Existe |
| Request por producto | `CotizacionProductoRequest.DescuentoPorcentaje` (`decimal?`) | ✅ Existe |
| Request por producto | `CotizacionProductoRequest.DescuentoImporte` (`decimal?`) | ✅ Existe |
| Calculador | `CotizacionPagoCalculator.CalcularDescuentoProducto()` | ✅ Existe y valida |
| Snapshot entidad | `CotizacionDetalle.DescuentoPorcentajeSnapshot` (`decimal?`) | ✅ Existe |
| Snapshot entidad | `CotizacionDetalle.DescuentoImporteSnapshot` (`decimal?`) | ✅ Existe |
| Validación backend | 0–100 para porcentaje, no negativo para importe, no superar subtotal | ✅ Activa |

Soporte confirmado de punta a punta. No se requirieron cambios en backend.

---

## C. Campos agregados

- `DescuentoPorcentaje` por línea/producto: input numérico, `min="0"`, `max="100"`, `step="0.01"`.
- `DescuentoImporte` por línea/producto: input numérico, `min="0"`, `step="0.01"`.

---

## D. Nombres exactos en payload

Cada elemento de `productos[]` en el request JSON enviado al endpoint `/api/cotizacion/simular` incluye:

```json
{
  "productoId": 42,
  "cantidad": 2,
  "descuentoPorcentaje": 10.0,
  "descuentoImporte": null
}
```

Nombres usados: `descuentoPorcentaje`, `descuentoImporte`. Coinciden exactamente con `CotizacionProductoRequest`.

Criterio de envío: se envía `null` si el campo está vacío o es cero (mismo patrón que descuentos generales).

---

## E. Cambios en Razor

**Archivo:** `Views/Cotizacion/Index_tw.cshtml`

- `min-w-[760px]` → `min-w-[920px]` en la tabla de productos (necesario para 8 columnas).
- Agregadas dos `<th>` entre "Precio vigente" y "Subtotal":
  - `Dto. %`
  - `Dto. $`

El JS genera dinámicamente los `<td>` correspondientes por fila.

---

## F. Cambios en JS

**Archivo:** `wwwroot/js/cotizacion-simulador.js`

### State
Cada producto en `state.productos` ahora incluye:
```js
descuentoPorcentaje: null,
descuentoImporte: null
```

### renderProductos()
Agrega dos `<td>` por fila entre "Precio vigente" y "Subtotal":
- Input con `data-cotizacion-desc-pct-index="${index}"`
- Input con `data-cotizacion-desc-importe-index="${index}"`

Los valores se leen del state, preservándose ante cualquier re-render (ej: cambio de cantidad, agregar otro producto).

### buildRequest()
El map de productos ahora incluye:
```js
descuentoPorcentaje: (p.descuentoPorcentaje !== null && p.descuentoPorcentaje > 0) ? p.descuentoPorcentaje : null,
descuentoImporte: (p.descuentoImporte !== null && p.descuentoImporte > 0) ? p.descuentoImporte : null
```

### bindEvents() — productosTbody input
Handler reestructurado para manejar tres tipos de input:
1. `data-cotizacion-cantidad-index` → parsePositiveInt → re-render (comportamiento previo)
2. `data-cotizacion-desc-pct-index` → parseNonNegativeDecimal → actualiza state, no re-render (preserva foco)
3. `data-cotizacion-desc-importe-index` → parseNonNegativeDecimal → actualiza state, no re-render (preserva foco)

En los casos 2 y 3 se invalida `ultimaSimulacion` y se deshabilita el botón Guardar, forzando al usuario a re-simular.

---

## G. Tests actualizados

**Archivo:** `e2e/cotizacion-simulador.spec.js`

**T6 agregado:** "Descuento por producto — inputs presentes y simulacion funciona"
- Verifica presencia de `[data-cotizacion-desc-pct-index="0"]` y `[data-cotizacion-desc-importe-index="0"]`.
- Carga 10% de descuento porcentual.
- Simula y verifica que cards aparecen.
- Verifica que `#cotizacion-descuento` no muestra `$ 0,00` (el backend aplicó el descuento).

Tests previos T1–T5 sin cambios funcionales.

---

## H. Contratos preservados

- `CotizacionCrearRequest` sin cambios.
- `guardar()` usa `buildRequest()` internamente → los descuentos por producto quedan incluidos en la cotización guardada.
- `CotizacionDetalle.DescuentoPorcentajeSnapshot` / `DescuentoImporteSnapshot` se persisten vía `CotizacionService.CrearAsync()` (no tocado).
- `DescuentoGeneralPorcentaje` y `DescuentoGeneralImporte` sin cambios.
- Conversión a Venta: no tocada.

---

## I. Riesgo sobre cálculos

**Bajo.** El backend valida y calcula; el frontend solo envía los valores. La lógica en `CalcularDescuentoProducto` está probada y activa. El frontend usa `parseNonNegativeDecimal` (existente desde COTIZ-1A), que devuelve `null` para valores inválidos, vacíos o negativos.

---

## J. Riesgo sobre conversión a Venta

**Nulo.** `CotizacionConversionService` no fue tocado. Los snapshots se persisten en `CotizacionDetalle` por el servicio de creación existente.

---

## K. Mobile / responsive

La tabla de productos tiene `overflow-x-auto` en su wrapper. El `min-w` pasó de 760px a 920px, lo que es compatible con el scroll horizontal contenido. La página (documentElement) no genera overflow. T4 verifica que no haya scroll horizontal a nivel de página en 390px.

---

## L. Accesibilidad

- Los inputs de descuento usan `aria-label="Descuento porcentaje producto"` / `aria-label="Descuento importe producto"`.
- Los inputs de la columna Dto.% tienen `max="100"` para orientar al usuario.
- `placeholder="0"` indica valor vacío esperado.
- No se usó `innerHTML` con valores no-numéricos; `esc()` sigue siendo la única función usada para strings en el template.

---

## M. Validaciones

- Negativos: bloqueados por `min="0"` en HTML y `parseNonNegativeDecimal` en JS.
- Porcentaje > 100: el backend rechaza con error (devuelve `errores[]`), la UI lo muestra via `showFeedback`.
- Descuento > subtotal del producto: el backend rechaza con error.
- Vacío: tratado como `null` → sin descuento aplicado.

---

## N. Playwright

Spec principal: `e2e/cotizacion-simulador.spec.js`
- T1–T5: sin cambios funcionales.
- T6 (nuevo): valida inputs de descuento presentes y que la simulación funciona con descuento por producto.

---

## O. Procesos

No se iniciaron procesos long-running permanentes en esta fase.

---

## P. Deudas

- Los inputs de descuento no muestran el subtotal descontado en la tabla (se muestra el bruto). El backend calcula el neto y lo refleja en `descuentoTotal` del resultado. Documentado, no urgente.
- Si el usuario carga un descuento > 100% o > subtotal, el backend rechaza con error visible en feedback. No hay validación previa en frontend (no necesaria por diseño: el backend es autoridad).
- El descuento por producto guardado en `CotizacionDetalle` no se usa aún en la conversión a Venta (COTIZ-QA-2 / futura fase).

---

## Q. Próximo paso recomendado

**COTIZ-QA-2** — Validar flujo completo guardar → conversión → Venta, incluyendo que `DescuentoPorcentajeSnapshot` / `DescuentoImporteSnapshot` queden correctamente persistidos en `CotizacionDetalle`.
