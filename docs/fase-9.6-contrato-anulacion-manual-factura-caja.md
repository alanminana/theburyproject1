# Fase 9.6 — Contrato de Anulación Manual de Factura y Caja

## A. Diagnóstico

| Aspecto | Estado antes de esta fase |
|---|---|
| `AnularFacturaAsync` | Marcaba factura Anulada, revertía venta Facturada → Confirmada. Sin ninguna interacción con Caja. |
| `CancelarVentaAsync` | Cancela completamente la venta y genera `ReversionVenta` en Caja. |
| Contrato documentado | No existía documentación explícita sobre si `AnularFactura` debía o no tocar Caja. |
| Tests de Caja en facturación | `VentaServiceFacturacionTests` usaba stub de Caja — imposible verificar impacto real. |

---

## B. Decisión funcional

**Hipótesis confirmada**: `AnularFacturaAsync` manual NO debe revertir movimientos de Caja.

**Justificación**:

- Al anular una factura, la venta regresa al estado **Confirmada** (no Cancelada).
- El estado Confirmada implica que el cobro sigue vigente: la mercadería fue entregada, el cliente pagó.
- Lo que se invalida es únicamente el **comprobante fiscal** (la factura en sí), no la operación comercial.
- La `ReversionVenta` en Caja corresponde exclusivamente a `CancelarVentaAsync`, donde la venta se anula por completo.

**Analogía operativa**:
> Anular una factura es como emitir una nota de corrección en papel sin devolver el dinero.
> Cancelar una venta es deshacer toda la operación: stock, caja y comprobante.

---

## C. Contrato final

### `AnularFacturaAsync(int facturaId, string motivo)`

| Efecto | Resultado |
|---|---|
| `Factura.Anulada` | `true` |
| `Factura.FechaAnulacion` | Asignada a `DateTime.UtcNow` |
| `Factura.MotivoAnulacion` | Asignado al parámetro `motivo` |
| `Venta.Estado` | Facturada → **Confirmada** (solo si no hay otras facturas activas) |
| `MovimientoCaja` de ingreso original | **Sin cambios** — mismo Id, mismo monto, mismo tipo, no eliminado |
| `ReversionVenta` en Caja | **No se genera** |
| Stock | Sin cambios |
| Trazabilidad | Sin cambios |

### Validaciones

| Condición | Resultado |
|---|---|
| `motivo` vacío o whitespace | `ArgumentException` |
| Factura no encontrada | `null` |
| Factura ya anulada | `InvalidOperationException` |

---

## D. Diferencia entre anular factura y cancelar venta

| Aspecto | AnularFacturaAsync | CancelarVentaAsync |
|---|---|---|
| Estado venta resultante | Confirmada | Cancelada |
| Ingreso caja original | Intacto | Revertido (ReversionVenta) |
| Genera ReversionVenta | **No** | **Sí** |
| Revierte stock | No | Sí (según configuración) |
| Revierte trazabilidad | No | Sí (según configuración) |
| Uso operativo | Corrección de comprobante fiscal | Cancelación total de la operación |

---

## E. Tests

### Existentes (antes de Fase 9.6)

En `VentaServiceFacturacionTests.cs` (stub de Caja — no pueden verificar impacto real en Caja):

| Test | Qué verifica |
|---|---|
| `AnularFactura_MotivoVacio_LanzaArgumentException` | Validación de motivo vacío |
| `AnularFactura_FacturaInexistente_RetornaNull` | Factura no encontrada |
| `AnularFactura_YaAnulada_LanzaExcepcion` | Doble anulación |
| `AnularFactura_UnicaFacturaActiva_ReviertVentaAConfirmada` | Estado de venta |
| `AnularFactura_OtraFacturaActiva_VentaPermaneceFacturada` | Venta con múltiples facturas |

### Agregados en Fase 9.6

En `VentaServiceE2ECancelacionTests.cs` (usa CajaService real):

| Test | Qué verifica |
|---|---|
| `AnularFacturaManual_IngresoOriginalCaja_QuedaIntacto` | Ingreso original sin modificar, sin ReversionVenta, exactamente 1 movimiento de caja para la venta |

---

## F. Qué NO se tocó

- `AnularFacturaAsync` — sin cambios funcionales
- `CancelarVentaAsync` — sin cambios
- `CajaService` — sin cambios
- Entidades ni migraciones — ningún campo nuevo
- Stock y trazabilidad — sin cambios
- Otros tests preexistentes — sin cambios

---

## G. Riesgos y deuda remanente

| Riesgo | Severidad | Mitigación / Pendiente |
|---|---|---|
| Operador puede anular factura y luego cancelar venta — quedaría ReversionVenta sin contraparte de ingreso acreditado en SaldoReal | Baja | Flujo permitido: la venta confirmada puede cancelarse normalmente; SaldoReal se ajustaría al cancelar |
| No existe flujo de nota de crédito real si se anula factura y se devuelve dinero | Media | Pendiente — fuera de scope de esta fase |
| Caja cerrada con ingreso pendiente: anular factura no afecta estado de acreditación pendiente | Baja | Correcto — el cobro sigue vigente; la acreditación es independiente del comprobante |

---

## H. Checklist

- [x] Contrato funcional confirmado y documentado
- [x] `AnularFacturaAsync` sin cambios de código (ya era correcto)
- [x] Test `AnularFacturaManual_IngresoOriginalCaja_QuedaIntacto` agregado en E2E tests
- [x] Docstring en `VentaServiceFacturacionTests.cs` actualizado con referencia al contrato Caja
- [x] Docstring en `VentaServiceE2ECancelacionTests.cs` actualizado con contratos 9.6
- [x] Documentación en `docs/fase-9.6-contrato-anulacion-manual-factura-caja.md`
- [x] Build verde
- [x] Tests relevantes pasando
