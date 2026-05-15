# Fase 10.3 — Devolución simple con unidad física

**Estado:** Implementada  
**Fecha:** 2026-05-15  
**Agente:** Fase 10.3 — Devolución simple con unidad física  
**Alcance:** Agregar `ProductoUnidadId` a `DevolucionDetalle`, auto-inferencia desde `VentaDetalle`, validaciones de negocio, actualización de `ProductoUnidad.Estado = Devuelta` y registro de `ProductoUnidadMovimiento` al completar. Sin cambios en Caja, comprobantes, garantía/reparación, VentaService ni flujo sin unidad.

---

## A. Diagnóstico previo

### Estado del módulo de devoluciones (pre-10.3)

- `DevolucionService` es canónico. Implementa `IDevolucionService`.
- `DevolucionDetalle` tiene `ProductoId` pero **no** `ProductoUnidadId`.
- `ProductoUnidad.Estado` ya tenía `Devuelta` y `EnReparacion` definidos en el enum pero no usados desde devoluciones.
- `VentaDetalle` ya tiene `ProductoUnidadId?` (nullable) desde Fase 8.2.E.
- `CompletarDevolucionAsync` actualizaba stock agregado (vía `MovimientoStockService`) según `AccionRecomendada`, pero no actualizaba la unidad física.
- El sistema podía saber que se devolvió un producto pero no qué unidad física volvió.

### Decisión de inferencia automática

`VentaDetalle.ProductoUnidadId` ya existe. Cuando una devolución nace desde una venta con unidad trazada:

- Si hay **exactamente un** `VentaDetalle` para ese `ProductoId` en la venta, con `ProductoUnidadId` no nulo → se **auto-infiere** y copia al `DevolucionDetalle`.
- Si hay **múltiples** `VentaDetalles` para el mismo `ProductoId` (ambigüedad), **no** se auto-infiere. El caller debe pasarlo explícito.
- Si el detalle ya trae `ProductoUnidadId` explícito → se valida y usa como está.
- Si no hay unidad (venta histórica sin trazabilidad) → flujo sin cambios.

---

## B. Modelo implementado

### DevolucionDetalle — nuevo campo

```csharp
// Trazabilidad individual (Fase 10.3 — nullable para compatibilidad con devoluciones sin unidad)
public int? ProductoUnidadId { get; set; }
public virtual ProductoUnidad? ProductoUnidad { get; set; }
```

### AppDbContext — configuración FK e índice

```csharp
entity.HasOne(e => e.ProductoUnidad)
    .WithMany()
    .HasForeignKey(e => e.ProductoUnidadId)
    .IsRequired(false)
    .OnDelete(DeleteBehavior.NoAction);

entity.HasIndex(e => e.ProductoUnidadId)
    .HasDatabaseName("IX_DevolucionDetalles_ProductoUnidadId");
```

- FK nullable, sin cascade, `DeleteBehavior.NoAction` (patrón del proyecto).
- Índice simple (sin unicidad): una unidad puede aparecer en más de un DevolucionDetalle históricamente.

### Migración

`20260515165759_AddProductoUnidadToDevolucionDetalle`

- Agrega columna `ProductoUnidadId INT NULL` a `DevolucionDetalles`.
- Crea índice `IX_DevolucionDetalles_ProductoUnidadId`.
- Agrega FK a `ProductoUnidades.Id` con `NO ACTION`.
- Additive. No modifica datos existentes.

---

## C. Reglas de negocio

### Al crear una devolución (`CrearDevolucionAsync`)

1. Si `DevolucionDetalle.ProductoUnidadId` es null y la VentaDetalle tiene exactamente una entrada para ese `ProductoId` con `ProductoUnidadId` no nulo → **auto-iniere** el valor.
2. Si `ProductoUnidadId` tiene valor (explícito o inferido):
   - La unidad debe existir y no estar soft-deleted.
   - Debe pertenecer al mismo `ProductoId`.
   - Su estado **no puede ser** `Baja`, `Faltante`, `Anulada` ni `EnReparacion`.
   - Estados aceptados: `EnStock`, `Reservada`, `Vendida`, `Entregada`, `Devuelta` (por si se re-devuelve desde estado intermedio).
3. Si `ProductoUnidadId` es null → flujo original sin cambios.

### Al completar una devolución (`CompletarDevolucionAsync`)

Para cada detalle donde:
- `ProductoUnidadId` tiene valor
- `AccionRecomendada` es `ReintegrarStock` o `Cuarentena`

Se ejecuta:
1. `ProductoUnidad.Estado = EstadoUnidad.Devuelta`
2. `ProductoUnidad.UpdatedAt = DateTime.UtcNow`
3. `ProductoUnidadMovimiento` nuevo con:
   - `EstadoAnterior` = estado previo de la unidad
   - `EstadoNuevo = Devuelta`
   - `Motivo = "Devolución completada: {NumeroDevolucion}"`
   - `OrigenReferencia = "Devolucion:{devolucion.Id}"`
   - `UsuarioResponsable` = usuario actual

Para `AccionRecomendada = Descarte`, `Reparacion`, `DevolverProveedor` → **no** se actualiza la unidad física. Queda en su estado anterior para revisión operativa.

El stock agregado sigue comportándose igual que antes para todas las acciones.

---

## D. Flujo al completar devolución (con unidad)

```
CompletarDevolucionAsync(id, rowVersion)
  │
  ├─ devolucion.Estado = Completada → SaveChanges (bloque concurrencia)
  │
  ├─ Para AccionRecomendada = ReintegrarStock
  │    └─ MovimientoStockService.RegistrarEntradas (stock agregado ++)
  │
  ├─ Para AccionRecomendada = Cuarentena
  │    └─ MovimientoStockService.RegistrarEntradas (stock agregado ++)
  │
  ├─ Para detalles con ProductoUnidadId + (ReintegrarStock | Cuarentena)
  │    ├─ ProductoUnidad.Estado = Devuelta
  │    ├─ ProductoUnidad.UpdatedAt = ahora
  │    ├─ ProductoUnidadMovimiento (Vendida → Devuelta, motivo, referencia)
  │    └─ SaveChanges
  │
  ├─ Si ReembolsoDinero + RegistrarEgresoCaja
  │    └─ CajaService.RegistrarMovimientoDevolucion
  │
  └─ Transaction.Commit
```

**Invariante**: la unidad devuelta queda en `Devuelta` para revisión operativa posterior. **No** se marca automáticamente como `EnStock`. El operador decide el siguiente paso.

---

## E. Tests implementados

Archivo: `TheBuryProyect.Tests/Integration/DevolucionServiceTests.cs`

### Nuevos casos (Fase 10.3)

| Test | Qué valida |
|------|-----------|
| `Crear_SinUnidad_SigueFuncionandoIgualQueAntes` | devolución legacy sin unidad no se rompe |
| `Crear_ConAutoInferencia_CopiaProductoUnidadIdDeVentaDetalle` | se copia automaticamente cuando hay una sola VentaDetalle con unidad |
| `Crear_ConUnidadExplicitaValida_PersisiteProductoUnidadId` | unidad explícita válida se persiste |
| `Crear_UnidadDeOtroProducto_LanzaExcepcion` | unidad que no pertenece al ProductoId falla |
| `Crear_UnidadInexistente_LanzaExcepcion` | unidad con Id inexistente falla |
| `Crear_UnidadConEstadoIncompatible_LanzaExcepcion` (Theory x4) | estados Baja/Faltante/Anulada/EnReparacion fallan |
| `Crear_MultipleVentaDetallesParaMismoProducto_NoAutoInfiere` | ambigüedad → no auto-infiere |
| `Completar_ConUnidadReintegrar_MarcaUnidadDevuelta` | ReintegrarStock → unidad.Estado = Devuelta |
| `Completar_ConUnidadCuarentena_MarcaUnidadDevuelta` | Cuarentena → unidad.Estado = Devuelta |
| `Completar_ConUnidadDescarte_NoMarcaUnidadDevuelta` | Descarte → unidad queda en Vendida |
| `Completar_ConUnidad_RegistraProductoUnidadMovimiento` | se crea el movimiento histórico correcto |
| `Completar_SinUnidad_StockSigueFuncionandoYNoCreaMovimientoUnidad` | sin unidad: stock sube, sin movimiento de unidad |

---

## F. Qué NO se tocó

- **VentaService**: sin cambios.
- **Caja / MovimientoCaja**: sin cambios.
- **Comprobantes / Factura**: sin cambios.
- **Garantía / RMA / NotaCredito**: sin cambios.
- **UI / Controllers / ViewModels**: sin cambios (ver deuda UI más abajo).
- **Estado EnReparacion**: no se implementa en esta fase.
- **Nota de crédito fiscal**: fuera de alcance.
- **EnStock automático post-devolución**: no implementado. La unidad queda en `Devuelta` para revisión operativa manual.

---

## G. Riesgos y deuda remanente

### Deuda UI

La UI actual (`Views/Devolucion/`) no muestra ni permite seleccionar `ProductoUnidadId`.

Opciones V2:
- Si la devolución nace desde una venta, la UI podría mostrar automáticamente qué unidad física estaba asociada al detalle de venta.
- No requiere selector manual si se muestra como información de solo lectura.
- Si se requiere selección manual (casos sin auto-inferencia), necesita endpoint de búsqueda de unidades por ProductoId.

### Deuda funcional

- `AccionRecomendada = Descarte`: la unidad queda en `Vendida`. Operativamente debería marcarse como `Baja`. A implementar en fase posterior.
- `AccionRecomendada = Reparacion`: cuando se implemente garantía/reparación, la unidad debería pasar a `EnReparacion`.
- `AccionRecomendada = DevolverProveedor (RMA)`: la unidad debería poder vincularse al RMA para trazabilidad completa.

### Invariante operativo a documentar

La unidad devuelta queda en `Devuelta` — **no** en `EnStock`. Esto es intencional: el operador debe inspeccionar la unidad y decidir si reintegrarla al stock físico (cambiando el estado a `EnStock` manualmente o vía herramienta futura).

---

## H. Checklist

- [x] `DevolucionDetalle.ProductoUnidadId?` agregado con nav prop
- [x] AppDbContext: FK nullable + índice configurados
- [x] Migración `AddProductoUnidadToDevolucionDetalle` creada
- [x] `CrearDevolucionAsync`: auto-inferencia desde `VentaDetalle.ProductoUnidadId`
- [x] `CrearDevolucionAsync`: validaciones de negocio (existencia, ProductoId, estado)
- [x] `CrearDevolucionAsync`: aplicación de ProductoUnidadId efectivo al detalle
- [x] `CompletarDevolucionAsync`: actualiza `ProductoUnidad.Estado = Devuelta` para ReintegrarStock y Cuarentena
- [x] `CompletarDevolucionAsync`: crea `ProductoUnidadMovimiento` con estado anterior/nuevo, motivo, referencia
- [x] Devoluciones sin unidad: flujo sin cambios confirmado por test
- [x] Stock agregado: comportamiento sin cambios confirmado por test
- [x] Caja: no modificada
- [x] Tests de regresión: 12 nuevos casos cubriendo happy path, errores y edge cases
- [x] Build limpio (0 errores, 0 advertencias)
- [ ] Migración aplicada a base de datos local (pendiente según entorno)
- [ ] UI: mostrar unidad en vista de devolución (deuda V2)
- [ ] Descarte: marcar unidad como Baja (deuda V2)
- [ ] Reparación: marcar unidad como EnReparacion (deuda futura)
