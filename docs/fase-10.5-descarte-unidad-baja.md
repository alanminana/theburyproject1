# Fase 10.5 — Descarte en devolución marca unidad física como Baja

## A. Diagnóstico previo

**HEAD al inicio:** `2b1560f` — Docs: fase 10.4B UI detalle devolución con unidad física  
**Build inicial:** 0 errores, 0 advertencias  
**Tests iniciales:** 728/728 passing

Enum real confirmado por lectura directa de código:

- Campo: `DevolucionDetalle.AccionRecomendada` — tipo `AccionProducto`
- Valor relevante: `AccionProducto.Descarte = 4`
- El prompt mencionaba posible nombre `AccionRecomendadaDevolucion` — no existe en el código; el nombre real es `AccionProducto`

---

## B. Clasificación de componentes

| Componente | Clasificación | Evidencia | Decisión |
|---|---|---|---|
| `Services/DevolucionService.cs` | **Canónico** | Registrado en DI, único service de devoluciones, tests vigentes | Modificar mínimamente |
| `AccionProducto.Descarte` | **Canónico** | Definido en `Models/Entities/Devolucion.cs` línea 364, usado en tests y service | Usar como condición |
| `EstadoUnidad.Baja` | **Canónico** | Definido en `Models/Enums/EstadoUnidad.cs` línea 12, mapeado en UI | Usar como estado destino |
| `ActualizarEstadoUnidadDevolucionAsync` | **Canónico** | Privado en DevolucionService, ya usado para Devuelta y EnReparacion | Reutilizar — mismo patrón |
| `DevolucionServiceTests.cs` | **Canónico** | 728 tests passing, cobertura de todas las fases anteriores | Actualizar test de Descarte y agregar nuevos |
| `Views/Devolucion/Detalles.cshtml` | **Canónico** | Badge y label de Baja ya mapeados desde 10.4B | Sin cambios necesarios |

---

## C. Decisión técnica

El helper `ActualizarEstadoUnidadDevolucionAsync` ya implementa el patrón exacto:
1. Cargar unidades por IDs
2. Actualizar `Estado` y `UpdatedAt`
3. Crear `ProductoUnidadMovimiento` con `EstadoAnterior`, `EstadoNuevo`, `Motivo`, `OrigenReferencia`
4. Guardar

La decisión fue **reutilizar el mismo helper** con `EstadoUnidad.Baja` como estado destino, sin modificar el helper ni ningún otro componente.

**Descarte no toca stock agregado** — confirmado: en el service actual, Descarte nunca fue incluido en las listas `reintegros` ni `cuarentenas` que llaman a `RegistrarEntradasAsync`. Ese comportamiento se mantiene igual.

**Estados aceptables para pasar a Baja**: cualquier estado no explícitamente bloqueado por el helper (el helper no valida estado previo, solo busca la unidad por ID y la actualiza). En la práctica, el flujo real lleva la unidad por: `Vendida → Devuelta → Baja` o directo `Vendida → Baja` dependiendo del orden de acciones.

---

## D. Cambios aplicados

### `Services/DevolucionService.cs`

En `CompletarDevolucionAsync`, después del bloque `detallesReparacion`, se agregó:

```csharp
var detallesDescarte = devolucion.Detalles
    .Where(d => d.ProductoUnidadId.HasValue &&
                d.AccionRecomendada == AccionProducto.Descarte)
    .ToList();

if (detallesDescarte.Count > 0)
    await ActualizarEstadoUnidadDevolucionAsync(
        detallesDescarte, devolucion,
        EstadoUnidad.Baja,
        $"Unidad dada de baja por devolución descartada: {devolucion.NumeroDevolucion}",
        usuario);
```

### `TheBuryProyect.Tests/Integration/DevolucionServiceTests.cs`

- **Actualizado**: `Completar_ConUnidadDescarte_NoMarcaUnidadDevuelta` → renombrado a `Completar_ConUnidadDescarte_MarcaUnidadBaja`, assertion cambiada de `Vendida` a `Baja`
- **Agregados**: 4 tests nuevos (ver sección H)

---

## E. Modelo EF / migración

**Sin migración.** No se modificaron entidades ni esquema. `ProductoUnidad.Estado` y `ProductoUnidadMovimiento` ya existen desde Fase 8.2.

---

## F. Reglas de negocio

Al completar una devolución (`CompletarDevolucionAsync`):

```
Si detalle.ProductoUnidadId != null Y AccionRecomendada == Descarte:
    → ProductoUnidad.Estado = Baja
    → ProductoUnidad.UpdatedAt = DateTime.UtcNow
    → ProductoUnidadMovimiento registrado con:
        EstadoAnterior = estado anterior de la unidad
        EstadoNuevo = Baja
        Motivo = "Unidad dada de baja por devolución descartada: {NumeroDevolucion}"
        OrigenReferencia = "Devolucion:{Id}"
        UsuarioResponsable = usuario actual
    → NO se modifica Producto.StockActual
    → NO se crea MovimientoStock
    → NO se toca Caja, NotaCredito, Factura, VentaService
```

Si el detalle no tiene `ProductoUnidadId`, Descarte sigue funcionando como antes (sin efecto sobre unidades físicas).

---

## G. Flujo al completar devolución con Descarte

```
CompletarDevolucionAsync(id, rowVersion)
  ├── Marcar Devolucion.Estado = Completada
  ├── Procesar stock: reintegros → RegistrarEntradasAsync
  ├── Procesar stock: cuarentenas → RegistrarEntradasAsync
  ├── ActualizarEstadoUnidad: (ReintegrarStock | Cuarentena) → Devuelta
  ├── ActualizarEstadoUnidad: Reparacion → EnReparacion
  ├── ActualizarEstadoUnidad: Descarte → Baja   ← NUEVO (Fase 10.5)
  └── Si ReembolsoDinero + RegistrarEgresoCaja → CajaService
```

---

## H. Tests

### Tests actualizados

| Test | Cambio |
|---|---|
| `Completar_ConUnidadDescarte_MarcaUnidadBaja` | Antes: `NoMarcaUnidadDevuelta` esperaba `Vendida`. Ahora espera `Baja` |

### Tests nuevos (Fase 10.5)

| Test | Verifica |
|---|---|
| `Completar_ConUnidadDescarte_MarcaUnidadBaja` | `ProductoUnidad.Estado == Baja` al completar con Descarte |
| `Completar_ConUnidadDescarte_RegistraProductoUnidadMovimiento` | Movimiento existe con `EstadoAnterior=Vendida`, `EstadoNuevo=Baja`, motivo y OrigenReferencia correctos |
| `Completar_ConUnidadDescarte_NoGeneraMovimientoStock` | `StockActual` no cambia, `MovimientosStock` vacíos para ese producto |
| `Completar_SinUnidadDescarte_SigueFuncionando` | Descarte sin `ProductoUnidadId` completa sin error y sin movimientos de unidad |
| `Completar_ConDescarte_NoAfectaDevueltaNiReparacion` | Con 3 unidades (ReintegrarStock, Reparacion, Descarte) → Devuelta, EnReparacion, Baja respectivamente |

### Resultado

```
Correctas: 732/732 (antes 728/728)
Nuevos: +4 tests de Fase 10.5 + 1 test de regresión actualizado
```

---

## I. Qué NO se tocó

- `VentaService.cs` — no modificado
- `VentaController.cs` — no modificado
- `Venta` en ninguna vista ni script
- `CajaService` — no modificado
- `NotaCredito` — no modificado
- `RMA` — no modificado
- `Factura` — no modificado
- `MovimientoStockService` — no modificado
- `ProductoUnidadService` — no necesario (lógica en DevolucionService)
- `Views/Devolucion/Detalles.cshtml` — sin cambios (badge Baja ya mapeado)
- `Views/Devolucion/Index.cshtml` — sin cambios
- `Models/Entities/ProductoUnidad.cs` — sin cambios
- `Models/Enums/EstadoUnidad.cs` — sin cambios
- Migraciones — ninguna generada ni aplicada

---

## J. Riesgos / deuda

| Item | Detalle |
|---|---|
| RMA con unidad física | `DevolverProveedor` todavía no actualiza `ProductoUnidad.Estado`. Pendiente Fase 10.6 |
| Finalización de reparación | `EnReparacion → EnStock / Baja / Devuelta` no implementado. Pendiente Fase 10.7+ |
| QA E2E devolución | No hay tests E2E browser del flujo completo. Pendiente QA general |
| `VentaApiController_ConfiguracionPagosGlobal` | Test preexistente fuera de scope que falla por HTTPS TestHost. No tocado. |

---

## K. Checklist actualizado

### Cerrado

- [x] 8.2 — Trazabilidad individual por unidad física
- [x] 9.x — Caja / comprobantes / cancelación
- [x] 10.1 — Reporte global de unidades físicas
- [x] 10.2 — Diagnóstico devoluciones/garantía con unidad
- [x] 10.3 — Devolución simple con unidad física
- [x] 10.4 — Reparación desde devolución → EnReparacion
- [x] 10.4B — UI devolución muestra unidad física y estado resultante
- [x] 10.5 — Descarte en devolución → Baja

### Estado actual del backend (transiciones completas)

| AccionRecomendada | ProductoUnidad.Estado |
|---|---|
| ReintegrarStock | Devuelta |
| Cuarentena | Devuelta |
| Reparacion | EnReparacion |
| Descarte | **Baja** ← implementado en esta fase |
| DevolverProveedor | *(pendiente)* |

### Pendiente

- [ ] 10.6 — RMA / DevolverProveedor con unidad física
- [ ] 10.7 — Finalización reparación: EnReparacion → EnStock / Baja / Devuelta
- [ ] QA E2E devolución / garantía completo
- [ ] Diagnóstico / arquitectura Ventas-Cotización con Carlos
- [ ] Fix `VentaApiController_ConfiguracionPagosGlobal` HTTPS TestHost (fuera de scope)
