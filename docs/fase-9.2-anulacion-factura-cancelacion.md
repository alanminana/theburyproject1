# Fase 9.2 — Anulación de Factura al cancelar venta facturada

## Qué se corrigió

Al cancelar una venta con estado `Facturada`, la factura asociada ahora queda anulada automáticamente.

### Cambios en `VentaService.cs`

**Método privado extraído:** `MarcarFacturaAnulada(Factura factura, string motivo)`
- Setea `Anulada = true`, `FechaAnulacion`, `MotivoAnulacion`, `UpdatedAt`
- Sin transacción, sin SaveChanges, sin cambio de estado de venta
- Reutilizado por `AnularFacturaAsync` y `CancelarVentaAsync`

**`AnularFacturaAsync` refactorizado:**
- Delega los campos de anulación a `MarcarFacturaAnulada`
- Mantiene su lógica de venta (Estado → Confirmada si no hay otras facturas activas)
- Mantiene su propia transacción
- Comportamiento externo idéntico al anterior

**`CancelarVentaAsync` modificado:**
- Después de `DesvincularUnidadesDeDetallesCancelados`
- Si `venta.Estado == Facturada`: carga factura activa y llama `MarcarFacturaAnulada`
- Si no hay factura activa: loguea warning, no rompe
- El flujo continúa: `venta.Estado = Cancelada` se aplica siempre al final
- La venta **nunca** termina en `Confirmada` por esta ruta

**Regla final implementada:**
```
Venta facturada cancelada → Factura.Anulada = true
                           + Factura.FechaAnulacion = now
                           + Factura.MotivoAnulacion = "Venta cancelada: {motivo}"
                           + Venta.Estado = Cancelada
```

## Tests

### Actualizado
- **Test 37** (`CancelarVenta_Facturada_FacturaQuedaAnulada`): reemplaza el test de deuda documentada; ahora afirma `Assert.True(factura.Anulada)`

### Agregados
- **Test 38**: `CancelarVenta_Facturada_FacturaAnuladaTieneFechaAnulacion`
- **Test 39**: `CancelarVenta_Facturada_FacturaAnuladaTieneMotivoAnulacion`
- **Test 40**: `CancelarVenta_Facturada_VentaTerminaEnCancelada_NoEnConfirmada`
- **Test 41**: `CancelarVenta_Confirmada_SinFactura_NoIntentaAnularNada`

### Resultado
478 tests / 0 errores / 0 warnings

## Qué NO se tocó

- **Caja (MovimientoCaja)**: el ingreso de caja registrado al facturar NO se revierte. Pendiente Fase 9.3.
- **Nota de crédito**: no implementada.
- **Pagos / TipoPago**: no modificados.
- **Esquema de base de datos**: sin migraciones.
- **Trazabilidad individual**: no modificada (ya funcionaba correctamente).
- `AnularFacturaAsync` vía controller (flujo manual): sin cambio de comportamiento externo.

## Riesgos y deuda remanente

| Item | Estado |
|---|---|
| Reversión de MovimientoCaja al cancelar | Pendiente Fase 9.3 |
| Transferencia/MercadoPago como VentaEfectivo | Pendiente Fase 9.4 |
| QA E2E cancelación completa | Pendiente Fase 9.5 |

## Commit

`52199f3` — `Fase 9.2: Anular factura al cancelar venta facturada`
