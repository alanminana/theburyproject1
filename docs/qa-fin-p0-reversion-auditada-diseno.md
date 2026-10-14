# QA-FIN-P0 — Integridad financiera y diseño de reversión auditada

Estado: contención P0 implementada (rama `qa/fin-p0-integridad-20261014`). La reversión
definitiva **NO está implementada**: faltan decisiones contables/fiscales (sección 4).

## 1. Causa raíz

| # | Defecto | Causa |
|---|---------|-------|
| 1 | Se cancelaba una venta financiada con cuotas pagadas sin revertir pagos | `VentaService.CancelarVentaAsync` no consultaba pagos. `CancelarCreditoAsociadoAVenta` pasa a `Cancelada` las cuotas no pagadas y fuerza `Credito.SaldoPendiente = 0` / `Estado = Cancelado`; las `PagoCuota` quedaban `Aplicado` y la caja intacta (ledger y estado contradictorios). |
| 2 | Contramovimiento sobre movimiento de otra operación | `CajaService.RegistrarContramovimientoVentaAsync` usaba el fallback `VentaId == null && ReferenciaId == ventaId && Tipo == Ingreso`. `ReferenciaId` es polimórfico (ventaId, cuotaId, creditoId, devolucionId, ordenId ML) y ningún movimiento de cuota/anticipo setea `VentaId`: con ids numéricamente iguales se revertía un cobro de cuota o un anticipo ajeno. |
| 3 | No existe reversión auditada de pagos | El enum `EstadoPagoCuota.Anulado`, `PagoCuota.PagoCuotaOrigen`, `OrigenPagoCuota.Reversion` y `PunitorioAplicado.Estado=Anulado` existen en el modelo, pero no hay servicio que los use para anular un pago aplicado + su movimiento de caja. |

Ruta única hacia el método: `VentaController.CancelarConfirmed` (POST `Venta/Cancelar/{id}`, permiso
`ventas.cancel`) → `IVentaService.CancelarVentaAsync`. El resto de implementaciones de la interfaz
son stubs de test. `RechazarVentaAsync` solo toca ventas sin confirmar (sin pagos).

Relaciones: `Venta.CreditoId → Credito → Cuota (CreditoId) → PagoCuota (CuotaId, MovimientoCajaId?) → MovimientoCaja`.
`MovimientoCaja` de cuota: `Concepto=CobroCuota`, `ReferenciaId=cuotaId`, `VentaId=null`.
`MovimientoCaja` de venta contado: `Concepto=Venta*`, `VentaId=ventaId`. Anticipo: `Concepto=AnticipoCredito`, `ReferenciaId=creditoId`.

## 2. Contención implementada

- `CajaService.RegistrarContramovimientoVentaAsync`: el ingreso a revertir debe tener
  `VentaId == ventaId` **y** concepto de cobro de venta (`VentaEfectivo/Tarjeta/Cheque/Transferencia/MercadoPago`).
  Fallback histórico (sin `VentaId`) solo si además `Referencia == ventaNumero` y concepto de venta.
  Nunca se decide por un id numérico solo.
- `VentaService.CancelarVentaAsync`:
  - Rechaza (`InvalidOperationException`, antes de cualquier escritura) si el crédito de la venta tiene
    pagos por cualquiera de 3 vías: cuota con `MontoPagado > 0` o estado `Pagada/Parcial` (legado sin ledger),
    `PagoCuota` en `Aplicado`, o `MovimientoCaja` `CobroCuota` referenciado a una cuota del crédito.
    Un `PagoCuota` ya `Anulado` no bloquea.
  - Toma el lock de fila de la venta (`ExecuteUpdate` dentro de la transacción) antes de leerla: dos
    cancelaciones simultáneas se serializan y la segunda ve `Cancelada`.
- Tests: `CajaContramovimientoVentaIntegridadTests` (12 casos) y 5 casos nuevos en
  `VentaServiceCancelarCreditoLiberaCupoTests` (rechazo sin efectos, legado, caja huérfana, pago anulado, doble solicitud).

Límites honestos de la contención:
- No hay test con dos hilos reales (SQLite en memoria no reproduce el lock de fila de SQL Server); la
  serialización se verifica secuencialmente y por diseño del `UPDATE`. Falta una corrida contra SQL Server (suite `fin` de qa-run).
- Un cobro de cuota que entre **entre** el chequeo y el commit no está cubierto: el flujo de cobro
  (`CreditoService`) no toma el lock de la venta. Ver 3.4.
- El anticipo (`AnticipoCredito`) **no** bloquea la cancelación y **no** se revierte (antes se revertía
  solo por coincidencia de id). Es decisión de negocio D6.

## 3. Diseño de la reversión auditada (propuesta)

### 3.1 Qué revierte, por componente
- **Pagos de cuota**: por cada `PagoCuota` `Aplicado`, marcar `Estado=Anulado` (+`FechaAnulacion`, motivo, usuario) y
  crear un `PagoCuota` espejo `Origen=Reversion` con `PagoCuotaOrigenId` y importes negativos. No se borra nada.
- **Cuota**: recalcular `MontoPagado`, `FechaPago`, `Estado` desde el ledger (única fuente), no por resta.
- **Punitorios**: los `ImporteAplicadoPunitorio` cobrados se revierten con el pago; `PunitorioAplicado` devengados se pasan a `Anulado`
  (la cuota se cancela, no corresponde mora).
- **Caja**: por cada `MovimientoCaja` de pago, un `Egreso` `ReversionVenta` (o concepto nuevo `ReversionCobroCuota`)
  con `ReferenciaId` = movimiento original y `Observaciones` con su id; en la **apertura vigente** del usuario, no en la original (ver 3.5).
- **Saldos**: `Credito` → `Cancelado`, `SaldoPendiente=0`; liberación de cupo ya existente. El dinero a devolver al cliente se registra como egreso de caja, no como saldo a favor implícito.
- **Stock**: ya lo hace `DevolverStock`/`RevertirUnidadesVentaAsync`; la regla nueva es que debe ejecutarse **una sola vez** dentro de la misma transacción.

### 3.2 Registro de auditoría
Nueva entidad `ReversionVenta` (cabecera): `Id`, `VentaId`, `CreditoId`, `Motivo`, `Usuario`, `AutorizadoPor`,
`FechaUtc`, `IdempotencyKey` (único), `TotalRevertidoCuotas`, `TotalRevertidoPunitorios`, `TotalDevueltoCaja`, `AperturaCajaId`,
`FacturaId?`, `NotaCreditoId?`. Detalle `ReversionVentaItem`: `PagoCuotaId`, `PagoCuotaReversionId`, `MovimientoOriginalId`,
`MovimientoReversionId`, importes. Más entrada en el log de seguridad/auditoría existente.

### 3.3 Transacción atómica e idempotencia
Una sola transacción: lock de venta → validar estado/permiso/cierre → crear cabecera con `IdempotencyKey` único
(índice único; un reintento falla y devuelve el resultado previo) → ledger → caja → cuotas/crédito → stock → factura/NC → venta `Cancelada`.
Cualquier fallo = rollback total. El guard "ya revertido" se apoya en la cabecera única por `VentaId`, no en búsquedas por concepto.

### 3.4 Concurrencia con cobros
El cobro de cuota debe tomar el mismo lock (`UPDATE` de la venta del crédito, o RowVersion del `Credito`) y rechazar si el crédito está `Cancelado`
o hay una reversión en curso. Hoy no lo hace.

### 3.5 Facturación, notas de crédito y cierres de caja
- Hoy `MarcarFacturaAnulada` solo marca `Anulada=true`; no hay Nota de Crédito emitida. Fiscalmente una factura emitida se anula con NC
  (la factura de venta se emite por el total facturable; las cuotas cobradas no la alteran). Falta regla (D2).
- Si la apertura original ya tiene `CierreCaja`, no se puede insertar en ella: la contrapartida va en la apertura actual con referencia
  cruzada. Si no hay caja abierta, la reversión debe rechazarse (igual criterio que cobrar). Falta regla (D3).
- Medios con acreditación diferida (transferencia/MP/tarjeta, `EstadoAcreditacion`): revertir un movimiento aún `Pendiente` no debería generar egreso real (D4).

## 4. Decisiones de negocio requeridas antes de implementar

- **D1** ¿Se devuelve el dinero cobrado al cliente (egreso en caja) o queda como saldo a favor/nota de crédito comercial? ¿Total o descontando gastos/intereses devengados?
- **D2** Venta Facturada: ¿se emite Nota de Crédito (por el total o proporcional) o solo se anula la factura? Dependiente de contador/AFIP; ¿tope de plazo?
- **D3** Cierre de caja: ¿se permite revertir pagos de aperturas ya cerradas? ¿en qué caja se asienta la devolución? ¿qué pasa si no hay caja abierta?
- **D4** Pagos con acreditación pendiente o ya conciliados (tarjeta/MP/transferencia): ¿reversión contable inmediata o espera de contracargo?
- **D5** Punitorios cobrados: ¿se devuelven? ¿qué ocurre con acuerdos de pago (`AcuerdoPago`) y alertas de mora vinculadas?
- **D6** Anticipo: ¿debe devolverse siempre al cancelar? Hoy no se revierte (antes solo por accidente de ids). ¿Bloquear como los pagos de cuota?
- **D7** Permisos: ¿permiso específico `ventas.reversepayments` y doble autorización (supervisor)? ¿límite de antigüedad?
- **D8** Stock/unidades: ¿la mercadería entregada se devuelve físicamente siempre? (si fue entregada al cliente la reversión de stock puede no corresponder).
- **D9** Reversión **parcial** de un solo pago (sin cancelar la venta): ¿en alcance?

## 5. Evidencia

Antes del fix (mismo set de tests, código sin corregir): 11 fallos / 20. Después: 0 fallos; suite completa 5439 OK, 4 omitidos, 0 fallos.
Los E2E de escritura (`e2e-tx/financiero`, casos 31/33/34) deben re-correrse únicamente vía `qa-run.ps1` tras confirmación de QA-ORQUESTADOR:
el caso 31 (la venta sigue `Confirmada`) pasa a ser el comportamiento esperado; 33/34 (revertir/auditar pagos) seguirán fallando hasta la reversión definitiva.
