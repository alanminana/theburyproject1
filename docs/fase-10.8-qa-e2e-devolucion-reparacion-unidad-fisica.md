# Fase 10.8 — QA E2E: Devolución, Reparación y Finalización con Unidad Física

## A. Objetivo

Agregar tests E2E/integración que validen el ciclo completo:

**Venta confirmada → Devolución (Reparacion) → Unidad EnReparacion → Finalizar reparación → EnStock / Baja / Devuelta**

Encadenando `VentaService`, `DevolucionService` y `ProductoUnidadService` en el mismo contexto SQLite in-memory.

No se implementaron reglas de negocio nuevas. No se modificó ningún servicio, entidad, migración ni UI.

---

## B. Escenarios cubiertos

### Ciclos E2E completos (VentaService → DevolucionService → FinalizarReparacion)

| Test | Destino final | Valida |
|---|---|---|
| `E2E_CicloCompleto_Reparacion_FinalizarAEnStock` | EnStock | Estado, historial, stock, no MovimientoStock |
| `E2E_CicloCompleto_Reparacion_FinalizarABaja` | Baja | Estado, historial, stock, no MovimientoStock |
| `E2E_CicloCompleto_Reparacion_FinalizarADevuelta` | Devuelta | Estado, historial, stock |

### Historial completo

| Test | Valida |
|---|---|
| `E2E_HistorialContieneTodosLosMovimientosOrdenados` | Historial ≥ 3 movimientos, Vendida→EnReparacion precede a EnReparacion→EnStock, orden temporal correcto |

### Invariante de stock agregado

| Test | Valida |
|---|---|
| `E2E_StockAgregadoNoCambiaEnNingunPasoDelCicloReparacion` | StockActual idéntico antes/después de CompletarDevolución(Reparacion) y después de FinalizarReparacion |

### No duplicación / idempotencia

| Test | Valida |
|---|---|
| `E2E_FinalizarReparacion_SegundoIntento_Falla` | Segundo intento de FinalizarReparacion lanza InvalidOperationException porque la unidad ya no está EnReparacion |

### Caja / comprobantes

| Test | Valida |
|---|---|
| `E2E_NoCajaRegistradaEnReparacionNiEnFinalizacion` | MovimientosCaja.Count no cambia durante el ciclo completo Reparacion → FinalizarReparacion |

### Regresión acciones previas

| Test | Acción | Valida |
|---|---|---|
| `E2E_Regresion_DevolucionReintegrarStock_ConservaBehaviorExistente` | ReintegrarStock | Unidad → Devuelta, stock sube, movimiento existe |
| `E2E_Regresion_DevolucionDescarte_MarcaBajaYNoGeneraStock` | Descarte | Unidad → Baja, stock no cambia, movimiento existe |
| `E2E_Regresion_DevolucionDevolverProveedor_MarcaDevueltaYNoGeneraStock` | DevolverProveedor | Unidad → Devuelta, stock no cambia, movimiento existe |
| `E2E_Regresion_DevolucionSinUnidad_Legacy_NoCreaMovimientoUnidad` | Reparacion sin unidad | Completar devuelve Completada, no crea ProductoUnidadMovimiento |

---

## C. Componentes revisados

| Componente | Clasificación | Rol en fase |
|---|---|---|
| `ProductoUnidadService.cs` | canónico | FinalizarReparacionAsync, CrearUnidadAsync |
| `DevolucionService.cs` | canónico | CompletarDevolucionAsync |
| `VentaService.cs` | canónico | ConfirmarVentaAsync (solo contexto E2E) |
| `MovimientoStockService.cs` | canónico | usado real en DevolucionService para validar regresión ReintegrarStock |
| `DevolucionServiceTests.cs` | canónico | tests existentes — no modificados |
| `ProductoUnidadServiceTests.cs` | canónico | tests unitarios FinalizarReparacion — no modificados |
| `VentaServiceProductoUnidadTrazabilidadTests.cs` | canónico | tests VentaService+Unidad — no modificados |

---

## D. Tests agregados

**Archivo nuevo:** `TheBuryProyect.Tests/Integration/ProductoUnidadReparacionE2ETests.cs`

11 tests nuevos. Todos en clase `ProductoUnidadReparacionE2ETests`.

El archivo incluye stubs file-scoped: `StubCajaE2ERep`, `StubAlertaStockE2ERep`, `StubCreditoDisponibleE2ERep`, `StubCurrentUserE2ERep`, `StubValidacionVentaE2ERep`.

Reutiliza stubs globales: `StubMovimientoStockEfectivo`, `StubContratoVentaCreditoService`, `StubConfiguracionPagoServiceVenta`.

---

## E. Reglas validadas

1. **Transición por DevolucionService(Reparacion)**: Vendida → EnReparacion.
2. **Transición por FinalizarReparacion**: EnReparacion → EnStock / Baja / Devuelta.
3. **Historial ordenado**: el movimiento Vendida→EnReparacion siempre precede al de finalización.
4. **No duplicación**: FinalizarReparacion con unidad no-EnReparacion lanza InvalidOperationException.
5. **No MovimientoStock** generado por ninguna de las dos operaciones.
6. **No MovimientoCaja** generado por reparacion ni por finalización.

---

## F. Stock agregado

Regla definida en 10.7 y confirmada en 10.8:

> Las transiciones individuales de `ProductoUnidad` (EnReparacion → EnStock / Baja / Devuelta) no modifican `Producto.StockActual`.

Validado en:
- `E2E_StockAgregadoNoCambiaEnNingunPasoDelCicloReparacion`
- Cada ciclo completo (A/B/C) afirma que `stockAntes == stockDespues` tras FinalizarReparacion.

La conciliación entre stock agregado y unidades individuales queda fuera de esta fase.

---

## G. Caja / comprobantes

- `DevolucionService` construido sin `ICajaService` (null) → no puede registrar movimiento de caja.
- `FinalizarReparacionAsync` no tiene interacción con `ICajaService`.
- Test `E2E_NoCajaRegistradaEnReparacionNiEnFinalizacion` confirma que `MovimientosCaja.Count` es idéntico antes y después del ciclo.
- La factura original no se altera. La NotaCredito sigue el flujo estándar (ya cubierto en `DevolucionServiceTests`).

---

## H. Historial individual

`ProductoUnidadMovimiento` permite reconstruir el ciclo completo:

```
AltaUnidad (EnStock → EnStock)
VentaService.ConfirmarAsync (EnStock → Vendida)
DevolucionService.CompletarAsync(Reparacion) (Vendida → EnReparacion)
ProductoUnidadService.FinalizarReparacionAsync (EnReparacion → destino)
```

Validado en `E2E_HistorialContieneTodosLosMovimientosOrdenados`:
- Historial ≥ 3 entradas.
- `idxRep < idxFin` (orden temporal garantizado).

---

## I. Qué NO se tocó

- Entidades, migraciones, enums.
- `VentaService.cs`, `DevolucionService.cs`, `ProductoUnidadService.cs` — sin cambios.
- `DevolucionServiceTests.cs`, `ProductoUnidadServiceTests.cs`, `VentaServiceProductoUnidadTrazabilidadTests.cs` — sin cambios.
- UI, vistas Razor, CSS, JS.
- Tests de Caja, Factura, Cotización/Carlos.
- Módulos de Carlos (Cotización).

---

## J. Riesgos / deuda

| Item | Detalle |
|---|---|
| VentaApiController HTTPS TestHost | Test preexistente fuera de scope — excluido de esta fase |
| Conciliación stock / unidades | Manual/asistida — queda fuera del alcance de esta fase |
| Polish UI historial | Badge visual en `UnidadHistorial.cshtml` — pendiente |
| Carlos Cotización V1E/V1.1 | Worktree separado — no tocado |

---

## K. Checklist actualizado

### Cerrado

- [x] 8.2 — Trazabilidad individual por unidad física
- [x] 9.x — Caja / comprobantes / cancelación
- [x] 10.1 — Reporte global de unidades físicas
- [x] 10.2 — Diagnóstico devoluciones/garantía
- [x] 10.3 — ReintegrarStock/Cuarentena → Devuelta
- [x] 10.4 — Reparacion → EnReparacion
- [x] 10.4B — UI muestra unidad física en devolución
- [x] 10.5 — Descarte → Baja
- [x] 10.6 — DevolverProveedor/RMA → Devuelta
- [x] 10.7 — Finalización reparación (EnReparacion → EnStock/Baja/Devuelta)
- [x] 10.8 — QA E2E devolución/reparación/finalización con unidad física

### Pendiente

- [ ] Polish UI historial: badge visual en `UnidadHistorial.cshtml`
- [ ] Test preexistente fuera de scope: `VentaApiController_ConfiguracionPagosGlobal` (HTTPS TestHost)
- [ ] Carlos Cotización V1E/V1.1/conversión (worktree `carlos/cotizacion-v1-contratos`)

### Siguiente micro-lote recomendado

**Polish UI historial** (badge visual en `UnidadHistorial.cshtml` para mostrar `EstadoUnidad` con color según estado). Bajo riesgo, no toca reglas de negocio, mejora claridad operativa.
