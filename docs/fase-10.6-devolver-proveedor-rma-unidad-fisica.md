# Fase 10.6 — DevolverProveedor / RMA con unidad física

**Agente**: Juan  
**Fecha**: 2026-05-15  
**Commit HEAD al cierre**: ver git log  
**Base**: ddecd5b (Fase 10.5: Descarte en devolucion marca unidad fisica como Baja)

---

## A. Diagnóstico previo

### Nombre real de la acción
```csharp
// Models/Entities/Devolucion.cs
public enum AccionProducto
{
    ReintegrarStock   = 0,
    Cuarentena        = 1,
    Reparacion        = 2,
    DevolverProveedor = 3,  // [Display(Name = "Devolver a Proveedor (RMA)")]
    Descarte          = 4
}
```

El valor real es `AccionProducto.DevolverProveedor`. No existe `RMA` ni `DevolverAProveedor` como acción.

### Estado de DevolverProveedor antes de esta fase
En `CompletarDevolucionAsync` (Services/DevolucionService.cs):
- **Gap confirmado**: la acción `DevolverProveedor` no estaba capturada en ningún bloque
- No generaba movimiento de stock agregado
- No tocaba Caja
- No tocaba comprobantes
- No creaba RMA automáticamente (el RMA se crea via `CrearRMAAsync` separado)
- No actualizaba `ProductoUnidad.Estado` → unidad quedaba en estado previo (típicamente `Vendida`)

---

## B. Estado actual de RMA/DevolverProveedor

El sistema tiene una entidad `RMA` completa con ciclo de vida propio:
```
EstadoRMA: Pendiente → AprobadoProveedor → EnTransito → RecibidoProveedor → EnEvaluacion → Resuelto / Rechazado
TipoResolucionRMA: Reemplazo / Reparacion / ReembolsoTotal / ReembolsoParcial / Credito
```

El RMA se crea separado via `CrearRMAAsync` (para devoluciones aprobadas con `RequiereRMA = true`).  
La acción `DevolverProveedor` en `AccionProducto` indica qué hacer con el producto físico al completar la devolución.

---

## C. Clasificación de componentes

| Componente | Clasificación | Evidencia | Decisión |
|---|---|---|---|
| `Services/DevolucionService.cs` | canónico | Registrado en DI, único service de devoluciones | Intervenir — agregar bloque DevolverProveedor |
| `ActualizarEstadoUnidadDevolucionAsync` | canónico | Helper privado reutilizado por Devuelta, EnReparacion, Baja | Reutilizar sin modificar |
| `AccionProducto.DevolverProveedor` | canónico | Valor 3 del enum AccionProducto, usado en vistas y lógica | Gap a cerrar |
| `EstadoUnidad` (enum) | canónico | Guardado como int, 0–8, sin estado proveedor/RMA | No extender en V1 |
| `RMA` (entidad) | canónico | Ciclo de vida proveedor ya completo | No tocar |
| `DevolucionServiceTests.cs` | canónico | Suite activa, 732+ tests en scope | Agregar tests |

---

## D. Decisión técnica de estado

**Opción elegida: A — DevolverProveedor → `EstadoUnidad.Devuelta`**

### Por qué esta opción
- No existe estado `EnProveedor` / `EnRMA` en `EstadoUnidad`
- Agregar valor = 9 requeriría revisar badges, reportes, switches y posibles migraciones → excede el micro-lote
- La entidad `RMA` ya provee trazabilidad del ciclo proveedor (Pendiente → EnTransito → RecibidoProveedor → Resuelto)
- `Devuelta` es semánticamente correcto: la unidad fue devuelta por el cliente y se destina al proveedor
- El `Motivo` en `ProductoUnidadMovimiento` diferencia: **"Unidad devuelta para gestión con proveedor: {numero}"** vs "Devolución completada: {numero}"
- Sin migración, sin cambio de enum, sin cambio de UI

### Deuda documentada
Si el negocio necesita distinguir visualmente unidades en tránsito al proveedor, agregar `EstadoUnidad.EnProveedor = 9` en una fase futura con badge, filtros en reporte global y cobertura de tests.

---

## E. Cambios aplicados

### Services/DevolucionService.cs — CompletarDevolucionAsync

Agregado después del bloque `detallesDescarte`:

```csharp
var detallesDevolverProveedor = devolucion.Detalles
    .Where(d => d.ProductoUnidadId.HasValue &&
                d.AccionRecomendada == AccionProducto.DevolverProveedor)
    .ToList();

if (detallesDevolverProveedor.Count > 0)
    await ActualizarEstadoUnidadDevolucionAsync(
        detallesDevolverProveedor, devolucion,
        EstadoUnidad.Devuelta,
        $"Unidad devuelta para gestión con proveedor: {devolucion.NumeroDevolucion}",
        usuario);
```

### TheBuryProyect.Tests/Integration/DevolucionServiceTests.cs

5 tests nuevos en sección "Fase 10.6":
1. `Completar_ConUnidadDevolverProveedor_MarcaUnidadDevuelta`
2. `Completar_ConUnidadDevolverProveedor_RegistraProductoUnidadMovimiento`
3. `Completar_ConUnidadDevolverProveedor_NoGeneraMovimientoStock`
4. `Completar_SinUnidadDevolverProveedor_SigueFuncionando`
5. `Completar_ConDevolverProveedor_NoAfectaOtrasAcciones`

---

## F. Reglas de negocio

Al completar una devolución con detalle que tenga `ProductoUnidadId` y `AccionRecomendada = DevolverProveedor`:

1. `ProductoUnidad.Estado` → `EstadoUnidad.Devuelta`
2. Se registra `ProductoUnidadMovimiento` con:
   - `EstadoAnterior`: estado previo (típicamente `Vendida` o `Entregada`)
   - `EstadoNuevo`: `Devuelta`
   - `Motivo`: `"Unidad devuelta para gestión con proveedor: {NumeroDevolucion}"`
   - `OrigenReferencia`: `"Devolucion:{Id}"`
3. **No se genera** movimiento de stock agregado (sin entrada a `MovimientosStock`)
4. **No se toca** `Producto.StockActual`
5. **No se toca** Caja
6. **No se toca** comprobantes / NotaCredito
7. **No se crea** RMA automáticamente — el RMA sigue siendo proceso separado

Si el detalle no tiene `ProductoUnidadId` (devolución sin unidad física): la devolución se completa normalmente sin crear movimiento de unidad.

---

## G. Flujo al completar devolución con DevolverProveedor

```
CompletarDevolucionAsync(id, rowVersion)
├── Verificar estado Aprobada
├── Marcar Completada
├── Procesar stock: reintegros + cuarentenas (no incluye DevolverProveedor)
├── ActualizarEstadoUnidad: ReintegrarStock/Cuarentena → Devuelta
├── ActualizarEstadoUnidad: Reparacion → EnReparacion
├── ActualizarEstadoUnidad: Descarte → Baja
├── ActualizarEstadoUnidad: DevolverProveedor → Devuelta  ← NUEVO
│   └── Motivo diferenciado: "Unidad devuelta para gestión con proveedor: ..."
└── Registrar reembolso caja si aplica
```

---

## H. UI

**Sin cambios de UI.**

- `EstadoUnidad.Devuelta` ya tiene badge `border-amber-500/30 bg-amber-500/10 text-amber-300` en `Views/Devolucion/Detalles.cshtml`
- Label: "Devuelta"
- La vista tiene `_ => e.ToString()` fallback — no crashea con valores nuevos
- Si en el futuro se agrega `EstadoUnidad.EnProveedor`, habrá que agregar arm explícito en `UnidadBadge` y `UnidadLabel`

---

## I. Tests

| Test | Qué valida |
|---|---|
| `Completar_ConUnidadDevolverProveedor_MarcaUnidadDevuelta` | Estado → Devuelta |
| `Completar_ConUnidadDevolverProveedor_RegistraProductoUnidadMovimiento` | Movimiento: EstadoAnterior, EstadoNuevo, Motivo contiene "proveedor" y número, OrigenReferencia |
| `Completar_ConUnidadDevolverProveedor_NoGeneraMovimientoStock` | StockActual sin cambio, MovimientosStock vacío |
| `Completar_SinUnidadDevolverProveedor_SigueFuncionando` | Sin ProductoUnidadId: completa OK, sin movimiento unidad |
| `Completar_ConDevolverProveedor_NoAfectaOtrasAcciones` | No regresión: Devuelta/EnReparacion/Baja/Devuelta simultáneos correctos |

**Suite al cierre:**
- Filtro en scope: 737/737
- Suite completa: 2726/2726

---

## J. Qué NO se tocó

- `Models/Enums/EstadoUnidad.cs` — no se agregó valor nuevo
- `Models/Entities/Devolucion.cs` — sin cambios
- `Models/Entities/ProductoUnidad.cs` — sin cambios
- `Views/Devolucion/Detalles.cshtml` — sin cambios
- `Services/VentaService.cs` — sin cambios (módulo Carlos)
- `Controllers/VentaController.cs` — sin cambios
- `Controllers/VentaApiController.cs` — sin cambios
- `Services/Cotizacion/*` — sin cambios (módulo Carlos)
- Migraciones — no hay migración nueva (sin cambio de schema)
- `Services/MovimientoStockService.cs` — sin cambios
- `Services/CajaService.cs` — sin cambios

---

## K. Riesgos / Deuda

| Item | Tipo | Impacto | Recomendación |
|---|---|---|---|
| `EstadoUnidad.Devuelta` no diferencia "devuelta interna" vs "enviada a proveedor" | Deuda semántica | Bajo (operacional) | Agregar `EstadoUnidad.EnProveedor = 9` si el negocio necesita tracking visual separado |
| `ActualizarEstadoUnidadDevolucionAsync` no valida si unidad ya está en `Devuelta` | Idempotencia | Bajo (doble completar bloqueado por estado Completada) | Aceptable en V1 |
| Completar finalización de reparación (`EnReparacion → EnStock/Baja/Devuelta`) | Funcionalidad pendiente | Medio | Fase 10.7 |

---

## L. Checklist actualizado

### Cerrado
- [x] 8.2 — Trazabilidad individual por unidad física
- [x] 9.x — Caja / comprobantes / cancelación
- [x] 10.1 — Reporte global de unidades físicas
- [x] 10.2 — Diagnóstico devoluciones/garantía con unidad
- [x] 10.3 — ReintegrarStock/Cuarentena → Devuelta
- [x] 10.4 — Reparacion → EnReparacion
- [x] 10.4B — UI muestra unidad física en devolución
- [x] 10.5 — Descarte → Baja
- [x] **10.6 — DevolverProveedor → Devuelta (con motivo diferenciado)**

### Tabla de acciones completa

| AccionProducto | EstadoUnidad destino | Motivo en movimiento |
|---|---|---|
| ReintegrarStock | Devuelta | "Devolución completada: {Numero}" |
| Cuarentena | Devuelta | "Devolución completada: {Numero}" |
| Reparacion | EnReparacion | "Reparación iniciada por devolución: {Numero}" |
| Descarte | Baja | "Unidad dada de baja por devolución descartada: {Numero}" |
| DevolverProveedor | Devuelta | "Unidad devuelta para gestión con proveedor: {Numero}" |

### Pendiente
- [ ] Finalización reparación: EnReparacion → EnStock / Baja / Devuelta (Fase 10.7)
- [ ] QA E2E devolución / garantía
- [ ] Carlos: Cotización V1A (rama carlos/cotizacion-v1-contratos)
- [ ] Test preexistente fuera de scope: `VentaApiController_ConfiguracionPagosGlobal` — HTTPS TestHost (flaky, reportado en 10.3)
- [ ] (Deuda) Estado `EnProveedor` en `EstadoUnidad` si negocio requiere diferenciación visual

---

## M. Coordinación con Carlos

No se tocó:
- `Services/VentaService.cs`
- `Controllers/VentaController.cs`
- `Controllers/VentaApiController.cs`
- `Views/Venta/*`
- `wwwroot/js/venta-create.js`
- `Services/Cotizacion/*`
- `Services/Models/Cotizacion*`
- `docs/fase-cotizacion-*`
- Tests de Cotización

Archivos no trackeados de Carlos en el repo principal (no incluidos en commit):
- `Services/CotizacionPagoCalculator.cs`
- `Services/Interfaces/ICotizacionPagoCalculator.cs`
- `Services/Models/CotizacionMedioPagoResultado.cs` (y otros Models Cotizacion)
- `TheBuryProyect.Tests/Unit/CotizacionPagoCalculatorContractTests.cs`
- `docs/fase-cotizacion-diseno-v1.md`
- `docs/fase-cotizacion-v1a-contratos.md`
