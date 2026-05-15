# Fase 10.7 — Finalización de reparación de unidad física

## A. Diagnóstico previo

### Flujo existente antes de esta fase

- `ProductoUnidadService` tenía métodos de transición: `MarcarFaltanteAsync`, `MarcarBajaAsync`, `ReintegrarAStockAsync`, `RevertirVentaAsync`, `MarcarDevueltaAsync`.
- Ninguno aceptaba origen `EnReparacion`.
- `ReintegrarAStockAsync` solo acepta `Faltante` o `Devuelta` → `EnStock`.
- `MarcarBajaAsync` solo acepta `EnStock`, `Devuelta`, `Faltante` → `Baja`.
- No existía ningún método ni endpoint para cerrar el ciclo de una unidad en reparación.

### Stock agregado

Confirmado: **ningún método del service toca `Producto.StockActual` ni genera `MovimientoStock`**. El patrón del sistema es transición individual + `ProductoUnidadMovimiento`. La conciliación de stock agregado es manual y asistida (vía `ConciliarStockUnidades`).

## B. Clasificación de componentes

| Componente | Clasificación | Evidencia | Decisión |
|---|---|---|---|
| `ProductoUnidadService` | canónico | Usado en DI, controller, tests de integración | Extender con `FinalizarReparacionAsync` |
| `IProductoUnidadService` | canónico | Interfaz del servicio canónico | Agregar firma del nuevo método |
| `ProductoController` | canónico | Maneja todas las acciones de unidades físicas | Agregar action `FinalizarReparacionUnidad` |
| `Views/Producto/Unidades.cshtml` | canónico | Vista principal de gestión de unidades | Agregar form condicional |
| `ProductoUnidadMovimiento` | canónico | Historial de ciclo de vida de unidades | Se registra en cada transición |
| `EstadoUnidad.EnReparacion` | canónico | Enum existente, estado origen validado | Sin cambios al enum |
| `ProductoUnidadItemViewModel` | canónico | ViewModel de ítem de unidad en lista | Agregada propiedad `PuedeFinalizarReparacion` |

## C. Decisión técnica

**Ubicación de la acción V1:** desde `Views/Producto/Unidades.cshtml` + `ProductoController`.

Razón: la finalización de reparación es una transición del ciclo de vida de la unidad física, no una modificación de devolución. La vista de unidades ya tiene todas las demás acciones de estado (faltante, baja, reintegro). Es el lugar natural y de menor acoplamiento.

**Stock agregado:** no se toca. Sigue el patrón canónico del sistema. Si el operador quiere alinear el stock agregado, usa el ajuste asistido de conciliación existente.

**Sin migración:** no hay cambios en entidades ni en la base de datos.

## D. Cambios aplicados

### `Services/Interfaces/IProductoUnidadService.cs`

- Agregado `using TheBuryProject.Models.Enums`.
- Nuevo método en la interfaz:

```csharp
Task<ProductoUnidad> FinalizarReparacionAsync(
    int productoUnidadId,
    EstadoUnidad estadoDestino,
    string motivo,
    string? usuario = null);
```

### `Services/ProductoUnidadService.cs`

- Implementación de `FinalizarReparacionAsync`.
- Validaciones: motivo obligatorio, estado destino permitido (EnStock, Baja, Devuelta), estado origen obligatoriamente EnReparacion.
- Usa helper canónico `CargarYValidarTransicionAsync` con `new[] { EstadoUnidad.EnReparacion }`.
- Registra `ProductoUnidadMovimiento` con `OrigenReferencia = "FinalizacionReparacion:{id}"`.
- No toca `Producto.StockActual` ni genera `MovimientoStock`.

### `ViewModels/ProductoUnidadesViewModels.cs`

- Nuevo ViewModel `ProductoUnidadFinalizarReparacionViewModel` con `ProductoUnidadId`, `EstadoDestino`, `Motivo`.
- Propiedad `PuedeFinalizarReparacion` agregada a `ProductoUnidadItemViewModel`.

### `Controllers/ProductoController.cs`

- `MapearUnidadItem`: agregado `PuedeFinalizarReparacion = unidad.Estado == EstadoUnidad.EnReparacion`.
- Nueva action `FinalizarReparacionUnidad` en `POST /Producto/FinalizarReparacionUnidad`.
  - Valida motivo, captura excepciones, usa `TempData` para mensajes.
  - Redirige a `Unidades` del producto.

### `Views/Producto/Unidades.cshtml`

- Condición del bloque de acciones extendida con `|| unidad.PuedeFinalizarReparacion`.
- Form nuevo visible solo para `PuedeFinalizarReparacion == true`.
- Permite elegir destino (EnStock / Baja / Devuelta) y motivo obligatorio.
- Botón "Finalizar reparacion" en estilo azul para distinguirlo visualmente de las otras acciones.

### `TheBuryProyect.Tests/Unit/ProductoApiControllerTests.cs`

- `StubProductoUnidadService` actualizado con la firma del nuevo método (`throw new NotImplementedException()`).

## E. Modelo EF / migración

Sin cambios en entidades ni en el esquema. Sin migración.

## F. Reglas de negocio

| Regla | Detalle |
|---|---|
| Estado origen obligatorio | Solo `EnReparacion` puede llamar a este flujo |
| Estados destino permitidos | `EnStock`, `Baja`, `Devuelta` |
| Motivo obligatorio | Máx. 500 caracteres |
| Historial | Se registra siempre un `ProductoUnidadMovimiento` |
| Stock agregado | No se modifica. El operador puede conciliar manualmente si lo necesita |

## G. Stock agregado — decisión tomada

**No se toca Producto.StockActual en esta fase.**

Razón: el sistema nunca modifica stock agregado al cambiar estados de unidades individuales. La conciliación es un paso separado, explícito y asistido. Agregar lógica de stock en esta transición rompería el patrón canónico y podría duplicar stock si la unidad ya había sido contada en una devolución previa.

Si en el futuro se decide que `EnReparacion → EnStock` debe generar un movimiento de entrada, eso debe decidirse con evidencia de todos los flujos afectados y documentarse como deuda de conciliación.

## H. UI implementada

- Form visible en la columna "Acciones" de la tabla de unidades, **solo para unidades en estado EnReparacion**.
- Select con 3 opciones: Reintegrar a stock / Dar de baja / Marcar como devuelta.
- Input texto para motivo obligatorio.
- Botón "Finalizar reparacion" en azul para diferenciarlo de otras acciones.
- No modal: formulario inline como el resto de las acciones de la vista.

## I. Tests

9 tests nuevos en `ProductoUnidadServiceTests.cs` (test #33 a #41):

| Test | Verifica |
|---|---|
| `FinalizarReparacion_UnidadEnReparacion_AEnStock_CambiaEstado` | Transición a EnStock |
| `FinalizarReparacion_UnidadEnReparacion_ABaja_CambiaEstado` | Transición a Baja |
| `FinalizarReparacion_UnidadEnReparacion_ADevuelta_CambiaEstado` | Transición a Devuelta |
| `FinalizarReparacion_RegistraProductoUnidadMovimiento` | Movimiento registrado con motivo y OrigenReferencia |
| `FinalizarReparacion_UnidadNoEnReparacion_Falla` | Falla si unidad no está en EnReparacion |
| `FinalizarReparacion_DestinoNoPermitido_Falla` | Falla si destino es Vendida u otro no permitido |
| `FinalizarReparacion_MotivoVacio_Falla` | Falla con motivo vacío |
| `FinalizarReparacion_AEnStock_NoModificaStockAgregado` | Stock agregado no cambia |
| `FinalizarReparacion_NoGeneraMovimientoStock` | MovimientoStock no se genera |

## J. Qué NO se tocó

- `VentaService`, `VentaController`, `VentaApiController`
- `Caja`, `Factura`, `Comprobante`
- `DevolucionService`, `DevolucionController`
- `EstadoUnidad` enum (sin cambios)
- `ProductoUnidadMovimiento` entidad (sin cambios)
- Migraciones
- Módulos de Cotización (Carlos)
- Lógica de devolución

## K. Riesgos y deuda

- **Stock desalineado:** si una unidad pasa a `EnReparacion` desde una devolución donde no se reintegró stock, y luego se finaliza a `EnStock`, el stock agregado no se actualiza automáticamente. El operador debe conciliar manualmente. Esto es correcto por diseño pero debe ser conocido por el equipo operativo.
- **Sin confirmación previa:** el form es directo (sin modal de confirmación). Podría agregarse en una fase de polish sin riesgo funcional.
- **Devuelta como destino:** `EnReparacion → Devuelta` deja la unidad disponible para ser reintegrada posteriormente con la acción existente `ReintegrarUnidadAStock`. Esto es intencional — no es un estado final.

## Checklist actualizado

### Cerrado

- [x] 8.2 — Trazabilidad individual por unidad física
- [x] 9.x — Caja / comprobantes / cancelación
- [x] 10.1 — Reporte global de unidades físicas
- [x] 10.2 — Diagnóstico devoluciones/garantía con unidad
- [x] 10.3 — ReintegrarStock/Cuarentena → Devuelta
- [x] 10.4 — Reparacion → EnReparacion
- [x] 10.4B — UI detalle devolución muestra unidad física
- [x] 10.5 — Descarte → Baja
- [x] 10.6 — DevolverProveedor/RMA → Devuelta
- [x] 10.7 — Finalización de reparación (EnReparacion → EnStock / Baja / Devuelta)

### Pendiente

- [ ] QA E2E devolución / garantía / reparación flujo completo
- [ ] Carlos: Cotización V1C / V1D
- [ ] Test preexistente fuera de scope: `VentaApiController_ConfiguracionPagosGlobal` — HTTPS TestHost (fallo transitorio conocido)
- [ ] (Opcional) Modal de confirmación en UI "Finalizar reparacion"
- [ ] (Opcional) Conciliación automática de stock agregado al finalizar reparación a EnStock

## Siguiente micro-lote recomendado

**QA E2E del flujo completo:** devolución → reparación → finalización → verificar historial.

O si se prioriza feature: **badge visual de estado en historial de unidad** para que el operador vea de forma más clara la secuencia de estados al ver `UnidadHistorial.cshtml`.
