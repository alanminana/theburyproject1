# Fase 9.4 — Medios de pago y acreditación en Caja

**Fecha:** 2026-05-15  
**Estado:** Completado

---

## A. Diagnóstico previo

### Bug confirmado

`CajaService.cs:581` (antes del fix):

```csharp
TipoPago.Transferencia or TipoPago.MercadoPago => ConceptoMovimientoCaja.VentaEfectivo,
// Transferencias se registran como efectivo
```

`Transferencia` y `MercadoPago` se mapeaban a `VentaEfectivo` en el campo `Concepto` de `MovimientoCaja`.

### Contexto existente

- `MovimientoCaja` ya tenía `TipoPago?` (nullable) y `MedioPagoDetalle?` — estructurado y correcto desde la migración `AddMovimientoCajaPagoEstructurado`.
- `ResolverMedioPagoMovimiento` en CajaService ya priorizaba `TipoPago` sobre `Concepto` para el resumen en UI — por eso el resumen por medio de pago ya funcionaba.
- El problema era solo semántico: la columna "Concepto" en la vista mostraba "Venta Efectivo" para Transferencia y MercadoPago.
- No había `VentaTransferencia` ni `VentaMercadoPago` en el enum.
- No había `EstadoAcreditacion` en el modelo.
- 550 tests pasando en baseline.

### Enums existentes antes del fix

```
ConceptoMovimientoCaja: VentaEfectivo=0, VentaTarjeta=1, VentaCheque=2, CobroCuota=3,
  CancelacionCredito=4, AnticipoCredito=5, GastoOperativo=10, ExtraccionEfectivo=11,
  DepositoEfectivo=12, DevolucionCliente=20, ReversionVenta=21, AjusteCaja=30, Otro=99

TipoPago: Efectivo=0, Transferencia=1, TarjetaDebito=2, TarjetaCredito=3, Cheque=4,
  CreditoPersonal=5, MercadoPago=6, CuentaCorriente=7, Tarjeta=8
```

---

## B. Decisión V1

**Opción elegida:** Separar conceptos + agregar EstadoAcreditacion.

El modelo ya tenía `TipoPago` estructurado (lo correcto para conciliación) pero `Concepto` era incorrecto. Se corrigieron ambos.

**Saldo de caja V1:** Sin filtro por estado. El saldo incluye todos los movimientos independientemente del EstadoAcreditacion. Esto mantiene la compatibilidad con todos los tests y reportes existentes. Filtrar saldo por estado es deuda documentada para 9.4B.

---

## C. Reglas por medio de pago

| TipoPago | Concepto | EstadoAcreditacion |
|---|---|---|
| Efectivo | VentaEfectivo | Acreditado |
| Transferencia | VentaTransferencia | Pendiente |
| MercadoPago | VentaMercadoPago | Pendiente |
| TarjetaDebito | VentaTarjeta | Pendiente |
| TarjetaCredito | VentaTarjeta | Pendiente |
| Tarjeta | VentaTarjeta | Pendiente |
| Cheque | VentaCheque | Pendiente |
| CreditoPersonal | — no genera movimiento inmediato — | — |
| CuentaCorriente | — no genera movimiento inmediato — | — |
| ReversionVenta (contramovimiento) | ReversionVenta | Revertido |

---

## D. Cambios de modelo

### 1. `ConceptoMovimientoCaja.cs` — agregado

```csharp
VentaTransferencia = 6,
VentaMercadoPago = 7,
```

### 2. `EstadoAcreditacionMovimientoCaja.cs` — nuevo enum

```csharp
NoAplica = 0, Pendiente = 1, Acreditado = 2,
Rechazado = 3, Anulado = 4, Revertido = 5
```

### 3. `MovimientoCaja.cs` — nueva propiedad

```csharp
public EstadoAcreditacionMovimientoCaja? EstadoAcreditacion { get; set; }
```

Nullable: los movimientos anteriores a 9.4 quedan con `null` (equivalente a NoAplica).

### 4. Migración EF Core

`20260515152426_AddEstadoAcreditacionMovimientoCaja`  
Columna: `EstadoAcreditacion int NULL` en tabla `MovimientosCaja`.  
Aplicada al DB de desarrollo.

---

## E. Cambios de CajaService / VentaService

### `RegistrarMovimientoVentaAsync`

Reemplazado el switch de `concepto` (una sola variable) por un switch de tupla `(concepto, estadoAcreditacion)`:

```csharp
var (concepto, estadoAcreditacion) = tipoPago switch
{
    TipoPago.Efectivo => (VentaEfectivo, Acreditado),
    TipoPago.Transferencia => (VentaTransferencia, Pendiente),
    TipoPago.MercadoPago => (VentaMercadoPago, Pendiente),
    TipoPago.TarjetaDebito or TipoPago.TarjetaCredito or TipoPago.Tarjeta => (VentaTarjeta, Pendiente),
    TipoPago.Cheque => (VentaCheque, Pendiente),
    ...
};
```

`EstadoAcreditacion = estadoAcreditacion` se asigna en la construcción del `MovimientoCaja`.

### `RegistrarContramovimientoVentaAsync`

Agregado: `EstadoAcreditacion = EstadoAcreditacionMovimientoCaja.Revertido` en la construcción del contramovimiento.

---

## F. Impacto en saldo

**Sin cambio en V1.** `CalcularSaldoActualAsync` sigue sumando todos los movimientos (Ingreso - Egreso). Los movimientos Pendientes inflan el saldo operativo igual que los Acreditados.

**Deuda documentada para 9.4B:**
- Implementar `SaldoReal` (solo Acreditado) vs `SaldoOperativo` (todos).
- Reporte de movimientos pendientes de acreditación.
- Botón/flujo para marcar Transferencia/MercadoPago como Acreditado manualmente.

---

## G. Tests

### Nuevos (14 tests en `CajaServiceMedioPagoAcreditacionTests.cs`)

- Efectivo → VentaEfectivo, Acreditado
- Transferencia → VentaTransferencia, Pendiente
- MercadoPago → VentaMercadoPago, Pendiente
- TarjetaDebito → VentaTarjeta, Pendiente
- TarjetaCredito → VentaTarjeta, Pendiente
- Tarjeta → VentaTarjeta, Pendiente
- Cheque → VentaCheque, Pendiente
- Transferencia NO es VentaEfectivo
- MercadoPago NO es VentaEfectivo
- ReversionTransferencia → EstadoAcreditacion=Revertido, TipoPago=Transferencia
- ReversionMercadoPago → EstadoAcreditacion=Revertido
- ReversionEfectivo → EstadoAcreditacion=Revertido, TipoPago=Efectivo
- Saldo incluye Transferencia Pendiente
- Saldo neutral después de cancelar Transferencia

### Tests existentes: sin regresiones

- Baseline: 2653 → Final: 2667 (+ 14 nuevos)
- `VentaServiceCancelarCajaTests`: 11 tests — todos pasando
- `CajaServiceTests`: todos pasando (incluyendo Transferencia y MercadoPago en resumen)
- `AjusteGlobalVentaCajaComprobanteTests`: pasando
- Fase 9.2/9.3/9.5: pasando

---

## H. Qué NO se tocó

- Lógica de cancelación de venta (9.2/9.3/9.5) — sin cambios funcionales
- `CalcularSaldoActualAsync` — sin cambios (V1 no filtra por estado)
- Comprobantes/facturas — sin cambios
- `VentaService` — sin cambios
- `ICajaService` — sin cambios de contrato
- Tests preexistentes — ninguno modificado
- Integración real con MercadoPago — fuera de alcance
- Conciliación bancaria automática — fuera de alcance
- Tarjeta avanzada — fuera de alcance
- Flujo de acreditación manual (Pendiente → Acreditado) — fuera de alcance (9.4B)

---

## I. Riesgos y deuda

| Item | Tipo | Prioridad |
|---|---|---|
| `SaldoReal` vs `SaldoOperativo` — saldo no filtra Pendientes | Deuda 9.4B | Alta |
| Flujo para marcar Transferencia/MP como Acreditado | Feature 9.4B | Alta |
| Movimientos legacy (null en EstadoAcreditacion) sin clasificación | Deuda menor | Baja |
| Tarjeta: acreditación varía por terminal/banco | Complejidad futura | Media |
| Cheque: acreditación puede demorar días | Complejidad futura | Baja |

---

## J. Checklist

- [x] `ConceptoMovimientoCaja`: VentaTransferencia, VentaMercadoPago agregados
- [x] `EstadoAcreditacionMovimientoCaja`: nuevo enum
- [x] `MovimientoCaja.EstadoAcreditacion`: propiedad nullable agregada
- [x] Migración EF Core creada y aplicada
- [x] `CajaService.RegistrarMovimientoVentaAsync`: concepto correcto + estado
- [x] `CajaService.RegistrarContramovimientoVentaAsync`: estado Revertido
- [x] Vista `DetallesApertura_tw`: FormatConcepto actualizado + badge EstadoAcreditacion
- [x] Tests nuevos: 14 casos cubriendo reglas V1
- [x] Build limpio: 0 errores, 0 advertencias
- [x] Tests: 2667/2667 pasando
- [x] `git diff --check`: sin problemas de whitespace
- [ ] Migración en producción (pendiente de deploy)
- [ ] Flujo acreditación manual (9.4B)
- [ ] SaldoReal vs SaldoOperativo (9.4B)
