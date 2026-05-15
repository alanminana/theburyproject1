# Fase 8.2.D — Diseño: Integración ProductoUnidad con Ventas

**Estado:** Implementado. Ver cierre en `docs/fase-8.2-cierre-trazabilidad-individual.md`
**Fecha:** 2026-05-14

**Basado en:** Fases 8.2.A, 8.2.B, 8.2.C completadas

---

## A. Diagnóstico de flujos de venta

### Flujo de creación (`VentaService.CreateAsync`)

- Crea la `Venta` + `VentaDetalle` desde el ViewModel.
- Llama `AgregarDetalles` → mapea `VentaDetalleViewModel` → `VentaDetalle` vía AutoMapper.
- Aplica precio vigente, calcula totales y comisiones.
- **No descuenta stock.**
- **No toca ProductoUnidad.**
- `VentaDetalle` actualmente no tiene `ProductoUnidadId`.

### Flujo de edición (`VentaService.UpdateAsync`)

- Permite editar la venta mientras está en `Cotizacion` / `Presupuesto` / estados previos a confirmación.
- Llama `ActualizarDetalles` → actualiza o elimina detalles existentes.
- Recalcula totales.
- **No descuenta stock.**
- **No toca ProductoUnidad.**

### Flujo de confirmación (`VentaService.ConfirmarVentaAsync`)

- Punto de descuento real de stock: llama `DescontarStockYRegistrarMovimientos`.
- `DescontarStockYRegistrarMovimientos` llama `_movimientoStockService.RegistrarSalidasAsync` → kardex agregado por SKU / `StockActual`.
- **Aquí debe llamarse `ProductoUnidadService.MarcarVendidaAsync` para cada detalle con unidad trazable.**

### Flujo de confirmación crédito personal (`VentaService.ConfirmarVentaCreditoAsync`)

- Variante para crédito personal configurado.
- También llama `DescontarStockYRegistrarMovimientos` (línea ~879).
- **Mismo punto de integración con ProductoUnidad que `ConfirmarVentaAsync`.**

### Flujo de cancelación (`VentaService.CancelarVentaAsync`)

- Si venta estaba en `Confirmada` o `Facturada`, llama `DevolverStock`.
- `DevolverStock` llama `_movimientoStockService.RegistrarEntradasAsync` → revierte kardex.
- **Aquí debe revertirse el estado de la unidad trazable (nueva transición).**
- Si venta nunca fue confirmada: no toca stock, no debe tocar ProductoUnidad.

### Preview / CalcularTotales

- `CalcularTotalesVentaRequest.DetalleCalculoVentaRequest` no tiene `ProductoUnidadId`.
- Correcto: el preview nunca debe tocar estado de unidades.

### `MovimientoStockService`

- Kardex agregado por SKU (`ProductoId`, `Cantidad`, `StockActual`).
- Independiente de ProductoUnidad: mantener sin cambios.
- Las dos trazabilidades (kardex agregado + trazabilidad individual) coexisten en V1.

### `VentaDetalle` actual

- Entidad canónica.
- No tiene `ProductoUnidadId` ni navegación a `ProductoUnidad`.
- FK a `Venta` y `Producto`. Campos de precios, IVA, comisión, costo snapshot.

### `ProductoUnidad` actual

- Tiene `VentaDetalleId` nullable (FK → `VentaDetalles`, configurado `WithMany()` + `OnDelete(NoAction)`).
- Tiene navegación `VentaDetalle`.
- `MarcarVendidaAsync` ya asigna `VentaDetalleId`, `ClienteId` y `FechaVenta`.

### Tests existentes

- Archivo: `TheBuryProyect.Tests/Integration/VentaServiceSharedStubs.cs` + múltiples archivos de integración.
- Cubren: confirmación, cancelación, crédito personal, precios, tarjeta, comisiones.
- **No hay tests de trazabilidad individual (ProductoUnidad).**

### Puntos de inyección identificados

| Dónde | Acción sobre ProductoUnidad |
|---|---|
| `ConfirmarVentaAsync` → dentro de `DescontarStockYRegistrarMovimientos` | Llamar `MarcarVendidaAsync` por cada detalle trazable |
| `ConfirmarVentaCreditoAsync` → misma llamada a `DescontarStockYRegistrarMovimientos` | Igual que arriba |
| `CancelarVentaAsync` → si `Estado == Confirmada || Facturada` | Llamar nueva transición (ver sección E) |
| `UpdateAsync` → `ActualizarDetalles` | Re-validar disponibilidad de la unidad al editar |
| `CreateAsync` → `AgregarDetalles` | Validar disponibilidad de la unidad al crear |

---

## B. Modelo propuesto

### Cambio en `VentaDetalle`

```csharp
// Agregar en VentaDetalle.cs
public int? ProductoUnidadId { get; set; }
public virtual ProductoUnidad? ProductoUnidad { get; set; }
```

- Nullable para compatibilidad total con ventas históricas.
- Obligatorio solo cuando `Producto.RequiereNumeroSerie == true`.

### Configuración EF Core en `AppDbContext`

Dentro del bloque `modelBuilder.Entity<VentaDetalle>`:

```csharp
// Agregar al bloque VentaDetalle existente
entity.HasIndex(e => e.ProductoUnidadId)
    .IsUnique()
    .HasDatabaseName("UX_VentaDetalles_ProductoUnidadId")
    .HasFilter("[IsDeleted] = 0 AND [ProductoUnidadId] IS NOT NULL");

entity.HasOne(e => e.ProductoUnidad)
    .WithMany()
    .HasForeignKey(e => e.ProductoUnidadId)
    .IsRequired(false)
    .OnDelete(DeleteBehavior.Restrict);
```

**Importante:** esto crea una segunda FK independiente (`VentaDetalle.ProductoUnidadId → ProductoUnidades.Id`), separada de la FK existente (`ProductoUnidad.VentaDetalleId → VentaDetalles.Id`). EF Core las trata como dos relaciones distintas. No hay circularidad porque están configuradas con `WithMany()` en ambos lados (sin colección inversa mapeada).

### Relación resultante

```
VentaDetalle.ProductoUnidadId → ProductoUnidades.Id  (nueva FK)
ProductoUnidad.VentaDetalleId → VentaDetalles.Id     (FK ya existente, sin cambios)
```

El índice único filtrado (`WHERE IsDeleted = 0 AND ProductoUnidadId IS NOT NULL`) garantiza que una unidad física no pueda aparecer en más de un detalle activo.

### Decisión V1: cantidad por línea

**Una línea de `VentaDetalle` trazable = Cantidad 1.**

Para vender 3 unidades trazables del mismo producto:
- 3 líneas de `VentaDetalle`, cada una con su propio `ProductoUnidadId` y `Cantidad = 1`.

**Postergado para V2:** tabla intermedia `VentaDetalleUnidad` para soporte de múltiples unidades por línea.

### `VentaDetalleViewModel` — extensión

```csharp
// Agregar en VentaDetalleViewModel.cs
public int? ProductoUnidadId { get; set; }
public string? ProductoUnidadCodigo { get; set; }    // solo lectura/display
public string? ProductoUnidadNumeroSerie { get; set; } // solo lectura/display
```

### `CalcularTotalesVentaRequest`

**No agregar `ProductoUnidadId`** al request de preview/cálculo. El cálculo de totales es independiente de la trazabilidad individual.

---

## C. Reglas de validación

### Validar al crear y editar (`CreateAsync`, `UpdateAsync`)

Para cada `VentaDetalle`:

**Si `Producto.RequiereNumeroSerie == true`:**
1. `ProductoUnidadId` debe estar informado (obligatorio).
2. La unidad debe pertenecer al mismo `ProductoId` del detalle.
3. La unidad debe estar en estado `EstadoUnidad.EnStock`.
4. La unidad no debe estar soft-deleted.
5. `Cantidad` debe ser exactamente `1`.
6. No puede haber dos líneas en la misma venta con el mismo `ProductoUnidadId`.

**Si `Producto.RequiereNumeroSerie == false`:**
1. `ProductoUnidadId` debe ser `null`. Si viene informado: rechazar (evitar contaminación de datos).

**Ventas históricas (datos previos a Fase 8.2.E):**
- `ProductoUnidadId == null` siempre es válido para productos trazables → compatibilidad total.
- La validación de unidad obligatoria aplica solo a ventas nuevas o editadas después de la implementación.

### Validar al confirmar (`ConfirmarVentaAsync`)

Para cada detalle trazable con `ProductoUnidadId` informado:
1. Re-verificar que la unidad sigue en estado `EnStock` al momento de confirmar.
2. Si la unidad ya fue vendida entre CreateAsync y ConfirmarVentaAsync (race condition): lanzar excepción descriptiva con nombre de la unidad.

### Punto de validación en código

Crear clase `VentaUnidadTrazableValidator` o método privado `ValidarUnidadesTrazablesAsync` dentro de `VentaService`. No agregar esta lógica al `IVentaValidator` existente (que no es async).

---

## D. Momento de transición de estado

| Evento | Estado unidad | Acción |
|---|---|---|
| `CreateAsync` (Cotización/Presupuesto) | No cambia | Solo validar disponibilidad |
| `UpdateAsync` | No cambia | Re-validar disponibilidad de la unidad asignada |
| `ConfirmarVentaAsync` | `EnStock → Vendida` | Llamar `MarcarVendidaAsync(productoUnidadId, ventaDetalleId, clienteId, usuario)` |
| `ConfirmarVentaCreditoAsync` | `EnStock → Vendida` | Igual |
| Preview / CalcularTotales | No cambia | Jamás tocar estado |
| Cotización/Presupuesto → Cancelada | No cambia | Nada que hacer (nunca se tocó) |

**Decisión clave V1:** La unidad pasa a `Vendida` únicamente al confirmar la venta real, no al cotizar ni al reservar.

**Riesgo conocido — Race condition:** entre `CreateAsync` y `ConfirmarVentaAsync`, otra venta puede confirmar la misma unidad. Mitigación V1: re-validar `EnStock` en `ConfirmarVentaAsync` con error descriptivo. Mitigación V2: implementar `EstadoUnidad.Reservada` (ya definido en el enum) para el lapso entre creación y confirmación.

---

## E. Rollback y anulación

### Venta cancelada antes de ser confirmada

- `Estado` ∈ {`Cotizacion`, `Presupuesto`, `PendienteRequisitos`, `PendienteFinanciacion`}
- La unidad nunca fue marcada como `Vendida`.
- **Acción:** no tocar ProductoUnidad. El detalle queda soft-deleted.

### Venta confirmada (o facturada) cancelada

- La unidad está en estado `Vendida`.
- Necesita transición `Vendida → EnStock` (no existe aún en `IProductoUnidadService`).
- **Propuesta:** agregar `RevertirVentaAsync(int productoUnidadId, string motivo, string? usuario)` a `IProductoUnidadService`.
  - Transición permitida: `Vendida → EnStock`.
  - Limpiar `VentaDetalleId`, `ClienteId`, `FechaVenta`.
  - Registrar movimiento de historial con motivo.
- Llamar desde `CancelarVentaAsync`, sección que hoy solo llama `DevolverStock`.

**Nota:** no se usa `Anulada` del enum para cancelaciones automáticas de venta. `Anulada` se reserva para bajas por causas externas (robo, pérdida, error de ingreso). La cancelación de venta debe devolver la unidad a `EnStock` directamente para no bloquear re-venta.

### Devolución formal (flujo `Devolucion`)

- Requiere nuevo método `MarcarDevueltaAsync(int productoUnidadId, string motivo, string? usuario)`.
- Transición permitida: `Vendida → Devuelta` (o `Entregada → Devuelta`).
- Después de evaluación:
  - `Devuelta → EnStock`: usar `ReintegrarAStockAsync` (ya existe).
  - `Devuelta → Baja`: usar `MarcarBajaAsync` (ya existe).
  - `Devuelta → EnReparacion`: nuevo método a definir en V2.

### Rollback de transacción EF Core

Si `ConfirmarVentaAsync` falla **después** de llamar `MarcarVendidaAsync`:
- Todo está en la misma transacción (`_context.Database.BeginTransactionAsync()`).
- El rollback revierte el `SaveChanges` que incluyó el estado `Vendida`.
- **No hay riesgo de estado inconsistente** si se llama `MarcarVendidaAsync` dentro de la misma unidad de trabajo antes del `SaveChangesAsync` final.

**Orden correcto en `ConfirmarVentaAsync`:**
1. Llamar `DescontarStockYRegistrarMovimientos` (kardex).
2. Marcar unidades trazables como `Vendida` (directo en EF, sin `SaveChanges` intermedio).
3. Resto del flujo.
4. `SaveChangesAsync` único.
5. `CommitAsync`.

Esto asegura que, si algo falla antes del commit, ambas operaciones se revierten juntas.

---

## F. UI y API futura (diseño, sin implementar)

### Endpoint de unidades disponibles

```
GET /api/productos/{productoId}/unidades-disponibles
```

Respuesta: lista de `{ Id, CodigoInternoUnidad, NumeroSerie, UbicacionActual }` donde `Estado == EnStock && !IsDeleted`.

Usar `ProductoUnidadService.ObtenerDisponiblesPorProductoAsync` (ya existe).

### En el formulario de nueva venta / edición

Cuando el usuario selecciona un producto con `RequiereNumeroSerie == true`:
- Mostrar selector de unidades disponibles (dropdown o campo de búsqueda).
- Opciones de búsqueda: por `CodigoInternoUnidad`, por `NumeroSerie`.
- Futuro: escaneo de QR o código de barras.
- Bloquear cantidad > 1.
- Si el usuario quiere agregar otra unidad del mismo SKU: botón "Agregar otra unidad" → nueva línea con nuevo selector.

### Prevención de duplicados en UI

Al seleccionar una unidad en una línea, excluirla de los selectors de las otras líneas de la misma venta (validación también en backend).

### Campos adicionales en ViewModel

- `VentaDetalleViewModel.ProductoUnidadId` (int?) → campo oculto en formulario.
- `VentaDetalleViewModel.ProductoUnidadCodigo` (string?) → display only.
- `VentaDetalleViewModel.ProductoUnidadNumeroSerie` (string?) → display only.

---

## G. Tests futuros (Fase 8.2.E)

### Tests de validación al crear/editar

```csharp
[Fact] VentaDetalle_ProductoTrazable_SinUnidadId_RechazaVenta()
[Fact] VentaDetalle_ProductoTrazable_UnidadDeOtroProducto_Rechaza()
[Fact] VentaDetalle_ProductoTrazable_UnidadNoEnStock_Rechaza()
[Fact] VentaDetalle_ProductoTrazable_UnidadSoftDeleted_Rechaza()
[Fact] VentaDetalle_ProductoTrazable_CantidadMayorA1_Rechaza()
[Fact] VentaDetalle_ProductoTrazable_UnidadDuplicadaEnMismaVenta_Rechaza()
[Fact] VentaDetalle_ProductoNoTrazable_ConUnidadInformada_Rechaza()
```

### Tests de confirmación

```csharp
[Fact] ConfirmarVenta_ProductoTrazable_MarcaUnidadVendida()
[Fact] ConfirmarVenta_ProductoTrazable_RegistraMovimientoEnStock_A_Vendida()
[Fact] ConfirmarVenta_ProductoTrazable_UnidadYaVendidaEnRaceCondition_LanzaExcepcion()
[Fact] ConfirmarVenta_ProductoTrazable_VentaDetalleIdAsignadoEnUnidad()
```

### Tests de cancelación

```csharp
[Fact] CancelarVenta_NoConfirmada_NoModificaUnidad()
[Fact] CancelarVenta_Confirmada_ProductoTrazable_RevierteUnidadAEnStock()
[Fact] CancelarVenta_Confirmada_ProductoTrazable_RegistraMovimientoVendida_A_EnStock()
```

### Tests de compatibilidad histórica

```csharp
[Fact] VentaHistorica_SinProductoUnidadId_EsValida()
[Fact] VentaHistorica_ConfirmarSinUnidad_DescuentaKardexNormal()
[Fact] VentaHistorica_CancelarSinUnidad_DevuelveStockNormal()
```

### Tests de preview

```csharp
[Fact] PreviewCalcularTotales_NoModificaEstadoDeNingunaUnidad()
```

---

## H. Validaciones ejecutadas

- `dotnet build` (configuración Debug): **0 errores, 0 advertencias.**
- Release build bloqueado por proceso en ejecución (`TheBuryProyect.exe` PID 4944): solo warnings de file-lock, sin errores de compilación.
- `git diff --check`: sin whitespace errors en archivos nuevos.

---

## I. Commit

Solo se creó documentación de diseño. No hay cambios en código de producción ni migraciones.

```
git add docs/fase-8.2.D-diseno-integracion-producto-unidad-ventas.md
git commit -m "Fase 8.2.D: diseñar integración de ProductoUnidad con ventas"
```

---

## J. Riesgos y deuda

| Riesgo | Severidad | Mitigación V1 | Trabajo futuro |
|---|---|---|---|
| Race condition entre Create y Confirmar | Media | Re-validar EnStock en ConfirmarVentaAsync | Implementar EstadoUnidad.Reservada en V2 |
| Bidireccionalidad de FKs (VentaDetalle.ProductoUnidadId + ProductoUnidad.VentaDetalleId) | Baja | Encapsular en transacción única, siempre actualizar ambos lados en el mismo SaveChanges | Evaluar si una sola FK es suficiente (simplificación) |
| `RequiereNumeroSerie` como flag de trazabilidad | Baja | Reutilizar en V1 | Evaluar campo `RequiereTrazabilidadIndividual` separado si surge la necesidad de trazabilidad sin número de serie |
| `ConfirmarVentaCreditoAsync` también llama `DescontarStockYRegistrarMovimientos` | Bajo | Agregar el mismo hook de marcar unidades | No olvidar al implementar |
| Tests existentes no cubren ProductoUnidad | Bajo | Los tests actuales siguen siendo válidos | Agregar suite de trazabilidad en Fase 8.2.E |
| `RevertirVentaAsync` y `MarcarDevueltaAsync` no existen aún | Media | Diseñados aquí, implementar en Fase 8.2.E | — |
| Devolucion.DevolucionDetalle no tiene ProductoUnidadId | Media | Sin impacto en V1 | Agregar en el flujo de devolución cuando se integre con trazabilidad |

---

## K. Checklist actualizado

### Completado

- [x] Fase 8.2.A: entidades `ProductoUnidad` y `ProductoUnidadMovimiento`
- [x] Fase 8.2.B: `ProductoUnidadService` — alta, código interno, consultas
- [x] Fase 8.2.C: transiciones `MarcarVendidaAsync`, `MarcarFaltanteAsync`, `MarcarBajaAsync`, `ReintegrarAStockAsync`
- [x] Fase 8.2.D: diseño de integración con ventas (este documento)

### Pendiente — Fase 8.2.E (implementación)

- [ ] Agregar `VentaDetalle.ProductoUnidadId` (nullable int) + navegación
- [ ] Configurar EF: FK + índice único filtrado en VentaDetalles
- [ ] Crear migración
- [ ] Agregar `ProductoUnidadId` a `VentaDetalleViewModel`
- [ ] Crear `ValidarUnidadesTrazablesAsync` en `VentaService`
- [ ] Extender `CreateAsync` / `UpdateAsync` con validación de unidades trazables
- [ ] Extender `ConfirmarVentaAsync` para llamar `MarcarVendidaAsync` por detalle trazable
- [ ] Extender `ConfirmarVentaCreditoAsync` igual que `ConfirmarVentaAsync`
- [ ] Extender `CancelarVentaAsync` para llamar `RevertirVentaAsync` por detalle trazable
- [ ] Agregar `RevertirVentaAsync` (Vendida → EnStock) a `IProductoUnidadService` e implementación
- [ ] Agregar `MarcarDevueltaAsync` (Vendida → Devuelta) a `IProductoUnidadService` e implementación
- [ ] Agregar `ProductoUnidadId` a `ActualizarDetalles` en `VentaService`
- [ ] Agregar endpoint `GET /api/productos/{id}/unidades-disponibles`
- [ ] Escribir tests de integración (suite completa del punto G)
- [ ] UI: selector de unidades disponibles en formulario de venta

### Siguiente micro-lote recomendado

**Fase 8.2.E — Implementación backend (sin UI):**
1. Migración: `VentaDetalle.ProductoUnidadId` + FK + índice único.
2. Extender `IProductoUnidadService` con `RevertirVentaAsync` y `MarcarDevueltaAsync`.
3. Implementar `ValidarUnidadesTrazablesAsync` en `VentaService`.
4. Hook en `ConfirmarVentaAsync` + `ConfirmarVentaCreditoAsync`.
5. Hook en `CancelarVentaAsync`.
6. Tests de integración.

Dejar UI para Fase 8.2.F.
