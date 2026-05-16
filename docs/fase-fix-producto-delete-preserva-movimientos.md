# Fix: Producto eliminado preserva MovimientoStock

**Rama:** `juan/fix-producto-movimientos-delete`
**Agente:** Juan
**Fecha:** 2026-05-16

---

## A. Objetivo

Corregir el bug por el cual al eliminar un producto desde Inventario/Productos,
sus movimientos de stock (Kardex) quedaban invisibles en la UI, aunque los
registros seguían existiendo en la base de datos.

---

## B. Bug reproducido

Al hacer soft-delete de un producto:

1. El Kardex del producto mostraba "Producto no encontrado" y redirigía.
2. La vista global de MovimientoStock (Index) dejaba de mostrar los movimientos del producto eliminado.
3. Las búsquedas de movimientos excluían los del producto eliminado.

---

## C. Causa raíz

Dos problemas combinados:

### C.1 — MovimientoStockService filtraba `!m.Producto.IsDeleted`

Seis métodos del service aplicaban este filtro en sus queries EF:

| Método | Línea original |
|---|---|
| `GetAllAsync` | `!m.IsDeleted && m.Producto != null && !m.Producto.IsDeleted` |
| `GetByIdAsync` | ídem |
| `GetByOrdenCompraIdAsync` | ídem |
| `GetByTipoAsync` | ídem |
| `GetByFechaRangoAsync` | ídem |
| `SearchAsync` | ídem |

Cuando un producto tiene `IsDeleted = true`, EF cargaba el producto (no hay global filter) pero el `Where` lo excluía, dejando los movimientos fuera del resultado.

### C.2 — `MovimientoStockController.Kardex()` usaba `GetByIdAsync`

`GetByIdAsync` filtra `!p.IsDeleted`. Producto soft-deleted → `null` → redirect
"Producto no encontrado". El Kardex era completamente inaccesible.

---

## D. Qué NO ocurría

- Los `MovimientoStock` **no se borraban físicamente**. El `ProductoService.DeleteAsync` aplica soft-delete (`IsDeleted = true`) y EF tiene `DeleteBehavior.Restrict` para `MovimientoStock → Producto`.
- No había cascade delete en DB.
- El bug era de **visibilidad/acceso**, no de integridad referencial.

---

## E. Decisión funcional

**Opción A — Soft delete con historial preservado (elegida).**

El patrón de soft delete ya estaba implementado en el sistema (`IsDeleted`, `GetAllAsync` filtra `!p.IsDeleted`). La corrección respeta ese patrón:

- Producto soft-deleted: `IsDeleted = true`, datos intactos.
- MovimientoStock: registros permanecen en DB y se vuelven accesibles.
- Kardex: accesible con el nuevo método que no filtra por `IsDeleted`.

---

## F. Cambios EF

Ninguno. La relación `MovimientoStock → Producto` ya estaba correctamente configurada
con `DeleteBehavior.Restrict` en `AppDbContext` (línea ~753). No se crearon migraciones.

---

## G. Cambios aplicados

### `Services/Interfaces/IProductoService.cs`
- Agrega `GetByIdParaHistorialAsync(int id)`: recupera un producto por ID incluyendo los soft-deleted. Uso exclusivo para contextos de historial/auditoría.

### `Services/ProductoService.cs`
- Implementa `GetByIdParaHistorialAsync`: query sin filtro `!p.IsDeleted`, con Include de Categoria y Marca.

### `Services/MovimientoStockService.cs`
- Elimina `&& m.Producto != null && !m.Producto.IsDeleted` de los 6 métodos afectados.
- `GetByProductoIdAsync` no tenía el problema (ya era correcto).

### `Controllers/MovimientoStockController.cs`
- `Kardex()`: usa `GetByIdParaHistorialAsync` en lugar de `GetByIdAsync`.
- Agrega `ViewBag.ProductoEliminado = producto.IsDeleted` para aviso visual futuro.

### `TheBuryProyect.Tests/Unit/VentaApiControllerTests.cs`
- Stub `StubProductoService` actualizado con implementación del nuevo método de interfaz.

---

## H. Tests agregados

**8 tests nuevos. Total suite: 641 tests, 0 errores.**

### MovimientoStockServiceTests.cs (3 tests)
- `GetByProductoId_ProductoSoftDeleted_PreservaMovimientos`
- `GetAllAsync_ProductoSoftDeleted_IncludeMovimientos`
- `SearchAsync_ProductoSoftDeleted_IncludeMovimientos`

### ProductoServiceTests.cs (5 tests)
- `Delete_ConMovimientoStock_PreservaMovimientos`
- `Delete_ConMovimientoStock_ProductoMarcadoIsDeleted`
- `GetByIdParaHistorial_ProductoActivo_RetornaProducto`
- `GetByIdParaHistorial_ProductoEliminado_RetornaProducto`
- `GetByIdParaHistorial_ProductoInexistente_RetornaNull`

---

## I. Validaciones ejecutadas

```
dotnet build --configuration Release  → 0 errores, 0 advertencias
dotnet test --filter "Producto|MovimientoStock|Inventario|Catalogo" --no-build
→ 641 tests, 0 errores, 0 omitidos
git diff --check → sin whitespace errors
```

---

## J. Qué NO se tocó

- `VentaService`, `CajaService`, `FacturaService`
- `DevolucionService`, `ProductoUnidadService`
- `CotizacionService` (módulo Carlos)
- `Program.cs`, `TestHost`
- Migraciones
- Vistas/Razor (solo `ViewBag.ProductoEliminado` agregado al controller, sin cambio en vista)
- `AppDbContext` relaciones EF (ya correctas)

---

## K. Riesgos y deuda remanente

### Sin riesgo inmediato
- Los registros existentes en DB no se ven afectados.
- Los tests existentes pasan sin modificación.

### Deuda identificada

1. **Vista `Kardex_tw`**: Recibe `ViewBag.ProductoEliminado = true` pero no lo usa aún. Pendiente: agregar banner "Este producto está eliminado" cuando corresponda. Bajo riesgo, mejora visual opcional.

2. **`GetByOrdenCompraIdAsync`**: El filtro se removió también aquí. Si en algún flujo de OrdenCompra se asumía que movimientos con productos eliminados no debían aparecer, revisar. Evaluado como historial válido.

3. **`MovimientoStockController.Index()`**: El dropdown de productos (`_productoService.GetAllAsync()`) solo muestra productos activos. Un usuario no puede filtrar por producto eliminado. Comportamiento aceptable (dropdown operativo), pero el historial ya aparece en la tabla global. Pendiente menor.

---

## L. Coordinación

- **Carlos** (E:\theburyproject-carlos-cotizacion, rama `carlos/cotizacion-v1-contratos`): no tocado.
- **Kira** (theburyproject-kira-testhost): no tocado.

---

## M. Checklist actualizado

### Juan
- [x] **BUG Producto eliminado borra movimientos** — CERRADO
- [ ] DIAGNÓSTICO StockActual vs unidades físicas — pendiente
- [ ] UI MovimientoStock/Kardex visual (banner producto eliminado) — pendiente opcional

### Carlos
- [ ] V1.8+ Cotización — pendiente/en curso

### Kira
- [ ] BUG Ventas/Create roto — pendiente/en curso

---

## N. Próximo paso recomendado (Juan)

**Diagnóstico: StockActual vs unidades físicas.**
Revisar si el `StockActual` del `Producto` puede divergir de las unidades físicas
en `ProductoUnidad` y bajo qué condiciones. Es el siguiente frente de auditoría
de inventario de mayor valor sin riesgo de regresión en lo ya estabilizado.
