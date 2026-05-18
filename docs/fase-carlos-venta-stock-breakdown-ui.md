# Fase Carlos — Venta stock breakdown UI

Fecha: 2026-05-18
Rama: `carlos/venta-stock-breakdown-ui`
Base: análisis de Juan en `juan/analisis-stock-unidades-venta` (sin cambios de código, diagnóstico puro).

---

## A. Diagnóstico

El sistema separa correctamente:
- `Producto.StockActual` = stock agregado/comercial
- `ProductoUnidad` = unidad física individual (trazabilidad opcional)

El campo que controla la trazabilidad es `Producto.RequiereNumeroSerie`.

Caso detectado:
- `StockActual = 2`, 1 unidad física `EnStock`, producto no trazable, venta cantidad = 2
- Resultado: `StockActual = 0`, `ProductoUnidad` sigue `EnStock` → conciliación = -1

Esto no rompe por excepción pero genera confusión operativa: el operador no distingue entre stock agregado y unidades físicas cargadas.

**Decisión funcional**: mejora informativa/UX — mostrar el desglose en el buscador de productos de venta. Sin cambiar reglas de negocio ni bloquear ventas.

---

## B. Endpoint modificado

`GET /api/ventas/BuscarProductos` en [Controllers/VentaApiController.cs](../Controllers/VentaApiController.cs)

El endpoint no cambió. Delega a `ProductoService.BuscarParaVentaAsync`, que fue enriquecido.

---

## C. Campos agregados

En [ViewModels/ProductoViewModel.cs](../ViewModels/ProductoViewModel.cs), clase `ProductoVentaDto`:

```csharp
public int UnidadesEnStock { get; set; }
public decimal StockSinIdentificar { get; set; }
```

---

## D. Regla de cálculo

En [Services/ProductoService.cs](../Services/ProductoService.cs), método `BuscarParaVentaAsync`:

Después de construir el listado de `ProductoVentaDto`, se ejecuta **una sola query agrupada** (no N+1):

```csharp
var conteosUnidades = await _context.ProductoUnidades
    .AsNoTracking()
    .Where(u => idsProductos.Contains(u.ProductoId) && !u.IsDeleted && u.Estado == EstadoUnidad.EnStock)
    .GroupBy(u => u.ProductoId)
    .Select(g => new { ProductoId = g.Key, Conteo = g.Count() })
    .ToDictionaryAsync(x => x.ProductoId, x => x.Conteo);

dto.UnidadesEnStock = conteosUnidades.TryGetValue(dto.Id, out var cnt) ? cnt : 0;
dto.StockSinIdentificar = dto.StockActual - dto.UnidadesEnStock;
```

`StockSinIdentificar` puede ser negativo: indica inconsistencia entre stock agregado y unidades físicas cargadas.

---

## E. Cambios en JS

En [wwwroot/js/venta-create.js](../wwwroot/js/venta-create.js):

Se agrega `function renderStockInfo(p)` antes del event listener de búsqueda:

- Si `unidadesEnStock <= 0`: muestra `Stock: X` (comportamiento anterior, sin cambio visual).
- Si `unidadesEnStock > 0` y producto **no trazable**: `Stock total: X · Identificadas: Y · Sin identificar: Z`
- Si `unidadesEnStock > 0` y producto **trazable** (`requiereNumeroSerie`): `Stock total: X · Unidades seleccionables: Y`
- Si `stockSinIdentificar < 0`: agrega badge ámbar `Revisar conciliación` (no bloqueante).

---

## F. Tests agregados

### VentaApiControllerTests (4 casos nuevos)

- `BusquedaProductoVenta_DevuelveUnidadesEnStock`: verifica serialización de `unidadesEnStock = 1`
- `BusquedaProductoVenta_DevuelveStockSinIdentificar`: verifica `stockSinIdentificar = 1`
- `BusquedaProductoVenta_StockSinIdentificarPuedeSerNegativo`: verifica caso inconsistente `stockSinIdentificar = -1`
- `BusquedaProductoVenta_NoRompeProductoSinUnidades`: verifica `unidadesEnStock = 0`, `stockSinIdentificar = stockActual`

### VentaCreateUiContractTests (2 casos nuevos)

- `VentaCreateJs_RenderizaStockBreakdown`: verifica que `renderStockInfo` contiene labels `Stock total:`, `Identificadas:`, `Sin identificar:`
- `VentaCreateJs_MuestraAdvertenciaConciliacionSiStockSinIdentificarNegativo`: verifica guard `stockSinIdentificar < 0` y texto `Revisar conciliación`

---

## G. Validaciones ejecutadas

```
dotnet build --configuration Release   → 0 errores, 0 advertencias
dotnet test --filter "VentaApiController|VentaCreateUiContract"  → 79/79
dotnet test --filter "Venta|VentaApiController|VentaController|ProductoUnidad|Conciliacion" → 885/885
git diff --check → sin errores de whitespace
```

---

## H. Qué NO se tocó

- `VentaService` — sin cambios
- `ValidarUnidadesTrazablesAsync` — sin cambios
- `ProductoUnidadService` — sin cambios
- `MovimientoStockService` — sin cambios
- Migraciones — sin cambios
- Entidades — sin cambios
- Caja, Factura, Cotización, Devolución — sin cambios
- Reglas de validación de stock — sin cambios
- Regla de `RequiereNumeroSerie` — sin cambios
- `VentaController.cs` — sin cambios
- `Views/Venta/Create_tw.cshtml` — sin cambios (el breakdown vive en JS)
- `Views/Venta/_VentaCrearModal.cshtml` — sin cambios

---

## I. Riesgos y deuda remanente

1. **La inconsistencia de conciliación no se bloquea**: El badge "Revisar conciliación" es solo informativo. El operador puede vender aunque haya inconsistencia.
2. **`StockSinIdentificar` puede ser decimal**: Si `StockActual` tiene decimales (productos por peso), el JS muestra el valor real sin redondear.
3. **La query agrupada depende de `ProductoUnidades`**: Si el índice en `ProductoId` no existe, puede ser lenta en volúmenes altos. Verificar índice en prod.
4. **La asignación manual de unidad física a venta no trazable no está implementada**: Para la fase siguiente.

---

## J. Próxima fase recomendada

**Advertencia no bloqueante al vender cantidad mayor a `StockSinIdentificar`**

Cuando el operador agrega al carrito una cantidad mayor al stock sin identificar (y hay unidades físicas disponibles), mostrar un aviso no bloqueante:

> "Cantidad supera el stock sin identificar (X). Se venderán unidades físicas cargadas. Revisar conciliación."

No bloquear la venta. Solo informar al operador.

Prompt sugerido: `Kira — Fase advertencia-stock-sin-identificar: mostrar aviso no bloqueante en carrito cuando cantidad > stockSinIdentificar`
