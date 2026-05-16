# Fase Cotización V1.8 — Preview tabla de cambios unitarios en conversión

## Objetivo

Extender el preview de conversión Cotización → Venta para mostrar en el modal una tabla detallada de cambios de precio por producto, incluyendo diferencia unitaria y diferencia total por línea.

---

## Diagnóstico previo

### Estado del DTO antes de V1.8

`CotizacionConversionDetallePreview` ya tenía:
- `ProductoId`, `CodigoProducto`, `NombreProducto`, `Cantidad`
- `PrecioCotizado`, `PrecioActual` (nullable)
- `ProductoActivo`, `PrecioCambio`, `RequiereUnidadFisica`
- `Advertencias` (por línea)

**Faltaban:** `DiferenciaUnitaria` (decimal?) y `DiferenciaTotal` (decimal?)

`CotizacionConversionPreviewResultado` ya tenía `HayCambiosDePrecios` y `Detalles`.

### Estado de la UI antes de V1.8

El modal solo mostraba:
- Errores globales (lista roja)
- Advertencias globales (lista ámbar)
- Total cotizado
- Panel de cliente faltante
- Política de precios (radio buttons)
- Checkbox de confirmación de advertencias

**El detalle por producto no se renderizaba en el modal** pese a estar disponible en el JSON de preview.

---

## Clasificación de componentes

| Componente | Clasificación | Decisión |
|---|---|---|
| `CotizacionConversionDetallePreview` | canónico | extendido con 2 campos nuevos |
| `CotizacionConversionPreviewResultado` | canónico | sin cambios |
| `CotizacionConversionService` | canónico | calcula diferencias en `PreviewConversionAsync` |
| `cotizacion-conversion.js` | canónico | agrega `renderTablaDetalles` |
| `Detalles_tw.cshtml` | canónico | agrega contenedor `cotizacion-detalles-preview-panel` |
| `CotizacionConversionServiceTests.cs` | canónico | 4 tests nuevos de diferencias |
| `CotizacionConversionApiTests.cs` | canónico | 1 test nuevo de endpoint |

---

## DTOs extendidos

### `CotizacionConversionDetallePreview`

Campos agregados:

```csharp
public decimal? DiferenciaUnitaria { get; init; }
public decimal? DiferenciaTotal { get; init; }
```

**Semántica:**
- `DiferenciaUnitaria = PrecioActual - PrecioCotizado` (null si no hay precio actual)
- `DiferenciaTotal = DiferenciaUnitaria * Cantidad` (null si DiferenciaUnitaria es null)
- Positivo → precio subió (más caro)
- Negativo → precio bajó (más barato)
- Cero → sin cambio

---

## Cálculo de diferencias

En `CotizacionConversionService.PreviewConversionAsync`, dentro del loop de detalles:

```csharp
decimal? diferenciaUnitaria = precioActual.HasValue
    ? precioActual.Value - detalle.PrecioUnitarioSnapshot
    : null;
decimal? diferenciaTotal = diferenciaUnitaria.HasValue
    ? diferenciaUnitaria.Value * (int)detalle.Cantidad
    : null;
```

El cálculo ocurre sin efectos secundarios. No modifica DB, no crea venta, no toca stock.

---

## UI modal

### Nuevo elemento en `Detalles_tw.cshtml`

Contenedor agregado dentro de `#cotizacion-conversion-resumen`, entre el bloque de total y el panel de cliente:

```html
<div id="cotizacion-detalles-preview-panel"
     class="hidden rounded-lg border border-slate-800 bg-slate-800/40 p-4 space-y-3">
</div>
```

### Nueva función en `cotizacion-conversion.js`

`renderTablaDetalles(detalles)`:
- Si no hay detalles: no renderiza nada
- Si hay detalles pero ninguno tiene `precioCambio`: muestra "No se detectaron cambios de precio."
- Si hay cambios: renderiza tabla completa con todas las líneas, resaltando las modificadas en ámbar

**Columnas:**
| Producto | Cant. | Precio cotizado | Precio actual | Diferencia | Total dif. | Estado |

**Colores:**
- Diferencia positiva (precio subió): rojo (`text-red-400`)
- Diferencia negativa (precio bajó): verde (`text-emerald-400`)
- Sin diferencia o sin precio actual: gris neutro
- Fila con cambio de precio: texto ámbar

**Seguridad:** Se usa `textContent` exclusivamente, nunca `innerHTML`. Sin XSS posible.

### Wire-up

```javascript
// En renderPreview(data):
renderTablaDetalles(data.detalles);

// En resetModal():
if (detallesPreviewPanel) { clearChildren(detallesPreviewPanel); hide(detallesPreviewPanel); }
```

---

## Tests

### Tests de servicio (integración) — `CotizacionConversionServiceTests.cs`

| Test | Verifica |
|---|---|
| `Preview_ProductoSinCambioPrecio_DiferenciaEsCero` | DiferenciaUnitaria=0, DiferenciaTotal=0, PrecioCambio=false cuando precios iguales |
| `Preview_ProductoConCambioPrecio_IncluyeDiferenciaUnitaria` | DiferenciaUnitaria=50 cuando precio sube de 100 a 150 |
| `Preview_CambioPrecio_DiferenciaTotal_EsDiferenciaUnitariaPorCantidad` | DiferenciaTotal = DiferenciaUnitaria × Cantidad (50 × 2 = 100) |
| `Preview_SinPrecioActual_DiferenciaEsNull` | null en ambos campos cuando resolver no tiene precio |

### Tests de API (unit) — `CotizacionConversionApiTests.cs`

| Test | Verifica |
|---|---|
| `PreviewEndpoint_DevuelveDetalleConDiferencias` | El endpoint retorna OkObjectResult con detalle que incluye DiferenciaUnitaria y DiferenciaTotal |

**Total tests de Cotizacion: 109 → 114 (5 nuevos)**
**Suite amplia: 2181 → 379 dentro del filtro** (sin regresiones)

---

## Qué NO se tocó

- `ConvertirAVentaAsync` (conversión real) — sin cambios
- `VentaService`, `VentaController`, `VentaApiController` — sin cambios
- `venta-create.js` — sin cambios
- IVA, trazabilidad, permisos, numeración — sin cambios
- Migraciones — ninguna (solo DTO en memoria, no entidad de BD)
- `Program.cs`, `HSTS`, `TestHost` — sin cambios
- `DocumentoCliente`, `Inventario`, `MovimientoStock` — sin cambios

---

## Riesgos y deuda

- El contenedor del modal (`max-w-2xl`) puede ser ajustado si la tabla requiere más espacio en casos con nombres de producto largos. El `overflow-x-auto` en la tabla mitiga el problema.
- La tabla no tiene paginación. En cotizaciones con muchos detalles, el `max-h-[60vh]` del contenedor del modal maneja el scroll.
- No hay tests de JS (no hay infraestructura de testing frontend en este proyecto). La validación es: build + tests backend + revisión manual del modal.

---

## Checklist V1.8

- [x] DTO extendido con `DiferenciaUnitaria` y `DiferenciaTotal`
- [x] Servicio calcula diferencias sin efectos secundarios
- [x] Razor: contenedor `cotizacion-detalles-preview-panel` agregado al modal
- [x] JS: `renderTablaDetalles` implementado con `textContent` (sin XSS)
- [x] JS: integrado en `renderPreview` y `resetModal`
- [x] 4 tests de servicio nuevos pasando
- [x] 1 test de API nuevo pasando
- [x] Build Release: 0 errores, 0 advertencias
- [x] Tests Cotizacion: 114/114 passing
- [x] Suite amplia: 379/379 passing
- [x] git diff --check: limpio (solo warnings CRLF esperados)
- [x] Documentación creada

---

## Siguiente micro-lote recomendado

**V1.9 — Numeración robusta de cotizaciones**

Garantizar que el número de cotización sea único, correlativo y sin huecos bajo concurrencia. Revisar el generador actual (`CotizacionNumberGenerator` o similar) y comparar con el patrón de `VentaNumberGenerator`.
