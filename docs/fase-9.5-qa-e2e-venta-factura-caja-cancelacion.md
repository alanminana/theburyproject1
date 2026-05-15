# Fase 9.5 — QA E2E: Venta → Factura → Caja → Cancelación

**Fecha:** 2026-05-15
**Estado:** Cerrado

---

## A. Objetivo

Validar el flujo completo de cancelación de una venta con todos sus efectos secundarios mediante tests de integración automatizados:

- Crear venta → confirmar → registrar caja → facturar → cancelar
- Verificar que toda la cadena de entidades queda consistente tras la cancelación

Esta fase es principalmente QA/test. No se implementaron nuevas reglas de negocio.

---

## B. Flujo E2E Validado

```
Venta (Presupuesto)
  ↓ ConfirmarVentaAsync
Venta (Confirmada)
  + MovimientoCaja [Ingreso / VentaEfectivo]
  + StockActual -= cantidad
  + ProductoUnidad.Estado = Vendida (si trazable)
  ↓ FacturarVentaAsync
Venta (Facturada)
  + Factura [Anulada=false]
  ↓ CancelarVentaAsync
Venta (Cancelada)
  + Factura [Anulada=true, FechaAnulacion set, MotivoAnulacion set]
  + MovimientoCaja [Egreso / ReversionVenta] con mismo monto que ingreso
  + Saldo neto caja = 0
  + StockActual += cantidad (revertido)
  + ProductoUnidad.Estado = EnStock, VentaDetalleId = null (si trazable)
```

Contrato de `AnularFacturaAsync` manual (camino separado):
```
Factura [Anulada=true]
Venta retrocede a Confirmada (NO a Cancelada)
Sin contramovimiento de Caja
```

---

## C. Casos Cubiertos

| # | Caso | Archivo |
|---|------|---------|
| 1 | E2E completo: confirmar → facturar → cancelar → todas las entidades consistentes | `VentaServiceE2ECancelacionTests.cs` |
| 2 | E2E con producto trazable: unidad EnStock → Vendida → EnStock | `VentaServiceE2ECancelacionTests.cs` |
| 3 | AnularFacturaAsync manual → venta queda Confirmada, sin egreso caja | `VentaServiceE2ECancelacionTests.cs` |
| 4 | Cancelar venta Confirmada → StockActual revertido exactamente | `VentaServiceE2ECancelacionTests.cs` |
| 5 | Cancelar dos veces → no duplica MovimientoCaja ReversionVenta | `VentaServiceE2ECancelacionTests.cs` |
| 6 | Cancelar venta Confirmada → registra MovimientoStock de entrada | `VentaServiceE2ECancelacionTests.cs` |
| 7 | Cancelar venta Confirmada/Efectivo → egreso ReversionVenta creado | `VentaServiceCancelarCajaTests.cs` (9.3) |
| 8 | Cancelar venta Confirmada/Efectivo → saldo caja neutralizado | `VentaServiceCancelarCajaTests.cs` (9.3) |
| 9 | Cancelar venta Confirmada → ingreso original intacto | `VentaServiceCancelarCajaTests.cs` (9.3) |
| 10 | Cancelar venta Facturada → contramovimiento + factura anulada | `VentaServiceCancelarCajaTests.cs` (9.3) |
| 11 | Cancelar dos veces → lanza InvalidOperationException, no duplica egreso | `VentaServiceCancelarCajaTests.cs` (9.3) |
| 12 | Cancelar Presupuesto → sin movimiento caja | `VentaServiceCancelarCajaTests.cs` (9.3) |
| 13 | Cancelar CreditoPersonal confirmado → sin contramovimiento | `VentaServiceCancelarCajaTests.cs` (9.3) |
| 14 | Venta queda en estado Cancelada con FechaCancelacion y MotivoCancelacion | `VentaServiceCancelarCajaTests.cs` (9.3) |
| 15 | CajaService aislado: RegistrarContramovimientoVenta con ingreso → egreso creado | `VentaServiceCancelarCajaTests.cs` (9.3) |
| 16 | CajaService aislado: sin ingreso → retorna null | `VentaServiceCancelarCajaTests.cs` (9.3) |
| 17 | CajaService aislado: guard anti-doble reversión | `VentaServiceCancelarCajaTests.cs` (9.3) |

---

## D. Tests Agregados

### Archivo nuevo: `VentaServiceE2ECancelacionTests.cs`

6 tests de integración que usan servicios **reales** (no stubs) para `MovimientoStockService` y `ProductoUnidadService`:

1. `E2E_VentaFacturada_Cancelada_TodasLasEntidadesConsistentes`
2. `E2E_ProductoTrazable_CancelacionRevierteUnidadAEnStock`
3. `AnularFacturaManual_VentaFacturada_VentaQuedaConfirmadaNoLaCancela`
4. `CancelarVenta_Confirmada_StockActualRevertidoExactamente`
5. `CancelarVentaYaCancelada_NoDuplicaContramovimientoCaja`
6. `CancelarVenta_Confirmada_RegistraMovimientoStockEntrada`

**Diferencia clave respecto a los tests de Fase 9.3:** usan `MovimientoStockService` real y `ProductoUnidadService` real para verificar que `Producto.StockActual` se revierte y que la unidad trazable vuelve a `EnStock`.

### Correcciones aplicadas en `VentaServiceCancelarCajaTests.cs`

Tres bugs de datos preexistentes que bloqueaban la compilación y fallaban en ejecución:

1. **Compilación:** 15 errores de stubs desactualizados (`StubMovimientoStockCancelarCaja` y `StubContratoVentaCreditoCancelarCaja`) no implementaban los miembros actuales de las interfaces. Corregido actualizando los stubs al contrato vigente.

2. **FK violation `CancelarVenta_Confirmada_CreditoPersonal_NoContramovimiento`:** `VentaDetalle` se sembraba con `VentaId=0` antes del `SaveChanges` de la `Venta`. Corregido: SaveChanges de la Venta primero, luego agregar el detalle con `VentaId = venta.Id`.

3. **FK violation en tests 9 y 11 (CajaService aislado):** `MovimientoCaja` se sembraba con `VentaId=9901`/`9902` (IDs inexistentes). Corregido: se seedan Ventas reales y se usan sus IDs.

---

## E. Resultados

```
VentaServiceE2ECancelacionTests:   6/6 passed
VentaServiceCancelarCajaTests:    11/11 passed (era 8/11 antes de esta fase)
Suite ampliado (VentaService|Caja|Factura|ProductoUnidad|Cancelar): 588/588 passed
```

---

## F. Qué NO se tocó

- Reglas de medios de pago (Transferencia, MercadoPago)
- Estados de acreditación
- Nota de crédito
- Devoluciones/garantía con unidad
- Reporte global de unidades
- Migración de datos
- Diseño de Caja
- Lógica de facturación AFIP/CAE

---

## G. Riesgos / Deuda

| Ítem | Severidad | Observación |
|------|-----------|-------------|
| Tests de Fase 9.3 con stubs desactualizados | Resuelto | FK violations y compilación corregidos en esta fase |
| AnularFacturaAsync manual no genera contramovimiento de Caja | Bajo | Comportamiento documentado y validado. Puede ser una deuda funcional si se espera neutralizar caja también al anular manualmente |
| `RevertirUnidadesVentaAsync` depende de `VentaDetalleId` en `ProductoUnidad` | Bajo | Si la unidad fue vendida sin pasar por `MarcarVendidaAsync`, no se revierte. El test E2E confirma el camino canónico |
| Tests E2E sin aislamiento de datos entre test classes | Bajo | Cada test class usa su propia conexión SQLite in-memory; no hay riesgo de interferencia |

---

## H. Checklist

### Fases Cerradas
- [x] Bloque 8.2 — Trazabilidad individual de productos/unidades
- [x] 9.1 — Diagnóstico Caja y Comprobantes
- [x] 9.2 — Anulación de Factura al cancelar venta facturada
- [x] 9.3 — Contramovimiento de Caja al cancelar venta
- [x] **9.5 — QA E2E venta → factura → caja → cancelación** ← esta fase

### Pendientes
- [ ] 9.4 — Transferencia/MercadoPago y estados de acreditación
- [ ] Anulación manual de factura y Caja (¿debe generar contramovimiento?)
- [ ] Devoluciones/garantía con unidad física
- [ ] Reporte global de unidades

---

## I. Siguiente Micro-lote Recomendado

**9.4 — Transferencia/MercadoPago y estados de acreditación**

El flujo de cancelación está cubierto y validado para efectivo. La siguiente frontera es:
- Confirmar que cancelar una venta con TipoPago=Transferencia/MercadoPago genera (o no) contramovimiento de Caja según el estado de acreditación
- Definir y documentar qué pasa con el contramovimiento cuando la transferencia no fue acreditada aún
