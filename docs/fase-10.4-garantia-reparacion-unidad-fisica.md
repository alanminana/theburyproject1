# Fase 10.4 — Garantía / reparación con unidad física

**Estado:** Implementada  
**Fecha:** 2026-05-15  
**Agente:** Fase 10.4 — Garantía / reparación con unidad física  
**Alcance:** Cuando una devolución tiene `ProductoUnidadId` y `AccionRecomendada == Reparacion`, la unidad física pasa a `EstadoUnidad.EnReparacion` y se registra `ProductoUnidadMovimiento`. Sin cambios en Caja, comprobantes, VentaService, stock agregado, UI ni módulo de taller.

---

## A. Diagnóstico previo

### Estado al cierre de Fase 10.3

- `DevolucionDetalle.ProductoUnidadId` nullable implementado.
- `CrearDevolucionAsync` valida unidades y auto-infiere `ProductoUnidadId`.
- `CompletarDevolucionAsync` solo procesaba `ReintegrarStock|Cuarentena → Devuelta`.
- `AccionProducto.Reparacion = 2` ya existía en el enum desde antes de 10.3.
- `EstadoUnidad.EnReparacion = 5` ya existía desde Fase 8.2.
- Reparacion al completar devolución: no hacía nada con la unidad física.
- 722 tests passing.

### Decisión de diseño

El enum `AccionProducto.Reparacion` ya estaba definido y la UI puede enviarlo. La regla era simple: si hay unidad trazada y la acción es Reparacion, la unidad debe pasar a `EnReparacion`. La lógica de transición era idéntica a la de `Devuelta`, solo variando el estado destino y el motivo. Se extrajo el helper `ActualizarEstadoUnidadDevolucionAsync` para eliminar la duplicación.

---

## B. Clasificación de componentes

| Componente | Clasificación | Evidencia | Decisión |
|---|---|---|---|
| `Services/DevolucionService.cs` | canónico | Registrado en DI, controllers activos, 727 tests | Modificado — helper extraído, Reparacion agregado |
| `AccionProducto.Reparacion` (en `Devolucion.cs`) | canónico | Enum existente con valor 2, usable desde UI | Solo usado, no modificado |
| `EstadoUnidad.EnReparacion` (en `EstadoUnidad.cs`) | canónico | Valor 5 preexistente, nunca asignado desde devoluciones | Ahora asignado por CompletarDevolucionAsync |
| `ProductoUnidad.cs` | canónico | Entidad de trazabilidad individual | No modificado |
| `ProductoUnidadMovimiento.cs` | canónico | Historial individual, ya usado en 10.3 | No modificado |
| `DevolucionServiceTests.cs` | canónico | Tests de integración activos | Agregados 5 tests nuevos |

---

## C. Decisión técnica

### Opción elegida: helper privado + dos llamadas

Se extrajo `ActualizarEstadoUnidadDevolucionAsync(List<DevolucionDetalle>, Devolucion, EstadoUnidad, string motivo, string usuario)`.

`CompletarDevolucionAsync` ahora invoca el helper dos veces:
1. Detalles con `ReintegrarStock|Cuarentena` → `EstadoUnidad.Devuelta`, motivo: `"Devolución completada: {numero}"`
2. Detalles con `Reparacion` → `EstadoUnidad.EnReparacion`, motivo: `"Reparación iniciada por devolución: {numero}"`

### Por qué no un segundo bloque duplicado

La especificación indicó explícitamente extraer helper si hay lógica duplicada. El bloque previo de 40 líneas se redujo a 2 llamadas. Los tests de regresión de 10.3 (Devuelta) siguen pasando sin cambios.

### Estados aceptables para transición a EnReparacion

Estados válidos de entrada: `Vendida`, `Entregada`, `Devuelta`.

`EnStock` y `Reservada` son edge cases legacy (el flujo normal no llega aquí en esos estados porque la unidad fue vendida antes de ser devuelta). Se aceptan sin bloqueo — no vale la pena cortar el flujo por escenarios improbables. Se registra el movimiento normalmente.

`Baja`, `Faltante`, `Anulada`, `EnReparacion` son bloqueados al crear la devolución (`CrearDevolucionAsync` línea 195), por lo que al completar no pueden llegar en esos estados.

---

## D. Cambios aplicados

### `Services/DevolucionService.cs`

**Nuevo helper privado** (dentro de la región de `CompletarDevolucionAsync`):
```csharp
private async Task ActualizarEstadoUnidadDevolucionAsync(
    List<DevolucionDetalle> detalles,
    Devolucion devolucion,
    EstadoUnidad estadoDestino,
    string motivo,
    string usuario)
```
Carga las unidades desde DB, aplica `estadoDestino`, registra `ProductoUnidadMovimiento` con `OrigenReferencia = "Devolucion:{id}"`, guarda y loguea.

**`CompletarDevolucionAsync`** — bloque de unidades físicas reemplazado:
- Antes: bloque inline de 40 líneas solo para `Devuelta`.
- Ahora: `detallesDevuelta` + llamada al helper → `Devuelta`; `detallesReparacion` + llamada al helper → `EnReparacion`.

### `TheBuryProyect.Tests/Integration/DevolucionServiceTests.cs`

5 tests nuevos en sección `Fase 10.4`:

1. `Completar_ConUnidadReparacion_MarcaUnidadEnReparacion`
2. `Completar_ConUnidadReparacion_RegistraProductoUnidadMovimiento`
3. `Completar_ConUnidadReparacion_NoGeneraMovimientoStockAgregado`
4. `Completar_SinUnidadReparacion_SigueFuncionando`
5. `Completar_ConUnidadReparacion_NoAfectaOtrasAcciones` (non-regresión: ReintegrarStock → Devuelta, Reparacion → EnReparacion en la misma devolución)

---

## E. Modelo EF / migración

**No se requirió migración.** Todos los campos necesarios ya existían:
- `DevolucionDetalle.ProductoUnidadId` nullable (Fase 10.3)
- `EstadoUnidad.EnReparacion` ya como valor del enum
- `ProductoUnidadMovimiento` sin cambios

---

## F. Reglas de negocio

Al completar devolución con `AccionRecomendada == Reparacion` y `ProductoUnidadId` no nulo:

1. Cargar la unidad desde DB (`!IsDeleted`).
2. Si la unidad no existe: se ignora con `continue` (log implícito del helper).
3. Registrar `ProductoUnidadMovimiento`:
   - `EstadoAnterior` = estado actual de la unidad
   - `EstadoNuevo` = `EnReparacion`
   - `Motivo` = `"Reparación iniciada por devolución: {NumeroDevolucion}"`
   - `OrigenReferencia` = `"Devolucion:{Id}"`
   - `UsuarioResponsable` = usuario actual del contexto
4. Actualizar `ProductoUnidad.Estado = EnReparacion`, `UpdatedAt = UtcNow`.
5. `SaveChangesAsync()`.

**Stock agregado (`Producto.StockActual`)**: no se modifica. Reparacion no generaba ni genera `MovimientoStock`.

**Caja**: no se toca. Solo ejecuta si `TipoResolucion == ReembolsoDinero && RegistrarEgresoCaja == true`.

---

## G. Flujo al completar devolución con Reparacion

```
CompletarDevolucionAsync(id, rowVersion)
  → MarcarCompletada()
  → RegistrarEntradasStock(ReintegrarStock) si hay
  → RegistrarEntradasStock(Cuarentena) si hay
  → ActualizarEstadoUnidadDevolucionAsync([detallesDevuelta], Devuelta)
  → ActualizarEstadoUnidadDevolucionAsync([detallesReparacion], EnReparacion)  ← NUEVO
  → RegistrarEgresoCaja() si TipoResolucion=Reembolso y RegistrarEgresoCaja
  → Commit
```

---

## H. UI / ViewModels

No se implementaron cambios en UI ni ViewModels.

**Diagnóstico**: `AccionProducto.Reparacion` ya es un valor del enum. La UI de devoluciones ya puede enviarlo como `AccionRecomendada` en el detalle. La auto-inferencia de `ProductoUnidadId` desde `VentaDetalle` (Fase 10.3) alcanza para el flujo de reparación — no se requiere selector manual.

La vista de devolución no muestra la unidad asociada ni el estado resultante. Esto es deuda explícita documentada en la sección I.

---

## I. Tests

| Test | Resultado |
|---|---|
| `Completar_ConUnidadReparacion_MarcaUnidadEnReparacion` | PASS |
| `Completar_ConUnidadReparacion_RegistraProductoUnidadMovimiento` | PASS |
| `Completar_ConUnidadReparacion_NoGeneraMovimientoStockAgregado` | PASS |
| `Completar_SinUnidadReparacion_SigueFuncionando` | PASS |
| `Completar_ConUnidadReparacion_NoAfectaOtrasAcciones` | PASS |
| Todos los tests de 10.3 (no regresión) | PASS |
| Suite completa (727 tests) | PASS |

Ejecutado con:
```
dotnet test --filter "Devolucion|ProductoUnidad|VentaService|Caja|Factura" --configuration Release
```
Resultado: 727/727 passing.

---

## J. Qué NO se tocó

- `VentaService` — sin cambios
- `CajaService` — sin cambios
- `MovimientoStockService` — sin cambios
- `Producto.StockActual` — sin cambios para Reparacion
- `Factura` / comprobantes — sin cambios
- `AppDbContext` — sin cambios (no hay migración)
- Controllers y ViewModels — sin cambios
- Views/Devolucion — sin cambios
- Tests preexistentes — todos siguen pasando sin modificación

---

## K. Riesgos / deuda

| Item | Tipo | Prioridad | Nota |
|---|---|---|---|
| UI devolución no muestra unidad asociada | deuda UX | media | Usuario no ve qué unidad quedó en reparación |
| `AccionProducto.Descarte` no actualiza unidad a `Baja` | deuda de negocio | media | Fase 10.5 o similar |
| Finalización de reparación (`EnReparacion → EnStock / Baja / Devuelta`) | deuda de negocio | alta | Módulo de taller pendiente |
| RMA / DevolverProveedor con unidad física | deuda de negocio | media | Fase posterior |
| `EnStock` / `Reservada` al momento de completar Reparacion | edge case legacy | baja | Se acepta sin bloqueo, documentado aquí |

---

## L. Checklist actualizado

### Cerrado

- [x] Base / limpieza
- [x] Condiciones de pago / CréditoPersonal separado del legacy
- [x] MovimientoStock / Kardex
- [x] Trazabilidad individual por unidad (Fase 8.2)
- [x] Caja / comprobantes / cancelación (Fase 9.x)
- [x] Reporte global de unidades físicas (Fase 10.1)
- [x] Diagnóstico devoluciones/garantía (Fase 10.2)
- [x] Devolución simple con unidad física (Fase 10.3)
- [x] Reparacion desde devolución → EnReparacion (Fase 10.4)

### Pendiente después de 10.4

- [ ] UI devolución: mostrar unidad asociada y estado resultante
- [ ] Descarte → Baja (unidad física)
- [ ] Finalización de reparación: EnReparacion → EnStock / Baja / Devuelta
- [ ] RMA / DevolverProveedor con unidad física
- [ ] QA E2E devolución / garantía / reparación
- [ ] Nota de crédito fiscal avanzada si negocio lo exige

---

## M. Siguiente micro-lote recomendado

**Opción A (bajo riesgo, alto valor):** UI devolución — mostrar la unidad física asociada en la vista de detalle y en el cierre de devolución. Permite al operador ver qué unidad quedó en reparación, devuelta o cuarentena. Sin cambios de negocio.

**Opción B (backend):** Descarte → `Baja`. Completar la tabla de transiciones de unidad al cerrar devolución. Requiere definir si Descarte modifica stock agregado (probablemente no, igual que Reparacion).

**Recomendación**: Opción A primero — operativamente el usuario necesita ver la unidad; Opción B después para cerrar toda la tabla de acciones.
