# Fase 10.4B — UI detalle de devolución con unidad física

**Estado:** Implementada  
**Fecha:** 2026-05-15  
**Alcance:** Visualización de la unidad física asociada en la vista de detalle de devolución. Sin cambios de negocio, sin selectores manuales, sin modificar flujo existente.

---

## A. Contexto previo

Al cierre de Fase 10.4:
- `DevolucionDetalle.ProductoUnidadId` nullable implementado (10.3).
- `ObtenerDevolucionAsync` no incluía `ThenInclude(dd => dd.ProductoUnidad)`.
- `DevolucionController.Detalles` era un placeholder que redirigía a Index.
- `Views/Devolucion/Detalles.cshtml` no existía.
- `NumeroDevolucion` en Index no tenía enlace a la vista de detalle.

---

## B. Cambios implementados

### `Services/DevolucionService.cs`

Agregado `ThenInclude(dd => dd.ProductoUnidad)` en `ObtenerDevolucionAsync`:

```csharp
.Include(d => d.Detalles.Where(...))
    .ThenInclude(dd => dd.Producto)
.Include(d => d.Detalles.Where(...))    // ← nueva línea
    .ThenInclude(dd => dd.ProductoUnidad)
```

EF Core 8 requiere repetir la expresión `.Include(...)` completa para agregar un segundo `ThenInclude` al mismo nivel. Patrón canónico del proyecto.

### `Controllers/DevolucionController.cs`

`Detalles(int id)` reemplaza el placeholder por implementación real:

```csharp
public async Task<IActionResult> Detalles(int id)
{
    var devolucion = await _devolucionService.ObtenerDevolucionAsync(id);
    if (devolucion == null) { TempData["Error"] = ...; return RedirectToAction(nameof(Index)); }

    var viewModel = new DevolucionDetallesViewModel
    {
        Devolucion = devolucion,
        Detalles = devolucion.Detalles.ToList(),
        NotaCredito = devolucion.NotaCredito,
        RMA = devolucion.RMA
    };
    return View(viewModel);
}
```

`DevolucionDetallesViewModel` ya existía y tenía exactamente los campos necesarios.

### `Views/Devolucion/Detalles.cshtml` (nueva)

Vista de detalle de devolución. Estructura:

- **Header**: breadcrumb a Index, número, cliente, fecha, badge de estado y resolución.
- **Tabla de ítems**: Producto, Cantidad, Estado producto, Acción recomendada, Unidad física, Subtotal.
  - Columna "Unidad física": muestra `CodigoInternoUnidad`, `NumeroSerie` si existe, badge de `EstadoUnidad`.
  - Si `ProductoUnidad == null`: texto "Sin unidad física" en gris.
- **Observaciones técnicas**: sección colapsada si hay detalles con obs técnicas.
- **Acciones**: Aprobar / Completar / Rechazar (inline con RowVersion, según estado).
  - Rechazar: panel oculto con input de motivo, toggle por JS simple.
- **Aside**: datos generales (número, fecha, cliente, venta, motivo, aprobado por), Nota de Crédito (si existe), RMA (si existe).

Paleta de badges de `EstadoUnidad`: copiada de `UnidadesGlobal.cshtml` (patrón canónico del proyecto):
- EnStock → emerald, Vendida → sky, Entregada → blue, Devuelta → amber, EnReparacion → purple, Faltante → red, Baja/Anulada → slate, Reservada → orange.

Paleta de badges de `AccionProducto`:
- ReintegrarStock → emerald, Cuarentena → amber, Reparacion → purple, DevolverProveedor → sky, Descarte → rose.

### `Views/Devolucion/Index.cshtml`

`NumeroDevolucion` ahora enlaza a la vista de Detalles:

```html
<a href="@Url.Action("Detalles", "Devolucion", new { id = d.Id })"
   class="text-sm font-black text-primary tabular-nums no-underline hover:underline">
   @d.NumeroDevolucion
</a>
```

---

## C. Test nuevo

`ObtenerDevolucion_ConProductoUnidad_IncluyeDatosUnidad`

Verifica que `ObtenerDevolucionAsync` carga `ProductoUnidad` en cada `DevolucionDetalle`:
- Seed: cliente, producto, unidad (Vendida), venta con detalle, devolución, devolucionDetalle con `ProductoUnidadId`.
- Assert: `detalle.ProductoUnidad != null`, `Id` correcto, `CodigoInternoUnidad` correcto, `Estado == Vendida`.

**Resultado:** 728/728 passing.

---

## D. Componentes clasificados

| Componente | Clasificación | Decisión |
|---|---|---|
| `DevolucionService.ObtenerDevolucionAsync` | canónico | Agregado ThenInclude mínimo |
| `DevolucionController.Detalles` | canónico (era placeholder) | Implementado |
| `DevolucionDetallesViewModel` | canónico | Sin cambios — ya tenía la estructura correcta |
| `Views/Devolucion/Detalles.cshtml` | nuevo canónico | Creado |
| `Views/Devolucion/Index.cshtml` | canónico | Enlace a Detalles agregado |

---

## E. Qué NO se tocó

- `DevolucionService` (lógica de negocio) — sin cambios
- `CajaService` — sin cambios
- `MovimientoStockService` — sin cambios
- Migrations / AppDbContext — sin cambios
- Otros controllers — sin cambios
- `DevolucionDetallesViewModel` — sin cambios
- Tests preexistentes — todos siguen pasando

---

## F. Deuda remanente

| Item | Tipo | Prioridad |
|---|---|---|
| `AccionProducto.Descarte` → unidad no se marca como `Baja` | deuda negocio | media |
| Finalización de reparación (`EnReparacion → EnStock / Baja / Devuelta`) | deuda negocio | alta |
| RMA / DevolverProveedor con unidad física | deuda negocio | media |
| Formulario CrearRMA desde vista Detalles | deuda UX | baja |
| QA E2E devolución / garantía / reparación | QA | media |

---

## G. Checklist

- [x] `ObtenerDevolucionAsync`: ThenInclude(ProductoUnidad) agregado
- [x] `DevolucionController.Detalles`: implementación real (no más placeholder)
- [x] `Views/Devolucion/Detalles.cshtml`: vista completa con unidad, acciones, aside
- [x] `Views/Devolucion/Index.cshtml`: NumeroDevolucion enlaza a Detalles
- [x] Test: ObtenerDevolucion_ConProductoUnidad_IncluyeDatosUnidad
- [x] Build limpio (0 errores, 0 advertencias)
- [x] 728/728 tests passing
- [x] Commit y push: 60ff2eb

---

## H. Siguiente micro-lote recomendado

**Opción A (backend):** `AccionProducto.Descarte → Baja`. Cierra la tabla de transiciones de unidad. Requiere definir si Descarte modifica stock agregado (probablemente no). Bajo riesgo, patrón idéntico al de Reparacion.

**Opción B (negocio):** Finalización de reparación — módulo/acción para pasar `EnReparacion → EnStock / Baja / Devuelta`. Alto valor operativo, requiere más diseño.

**Recomendación:** Opción A — bajo riesgo, 1 test, cierra deuda pendiente de la tabla de acciones.
