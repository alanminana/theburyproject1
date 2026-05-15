# Fase 9.1 — Diagnóstico: Caja, Comprobantes y Cancelación de Venta Facturada

**Fecha:** 2026-05-15  
**Tipo:** Solo diagnóstico / documentación — sin cambios productivos  
**Estado:** Cerrado  

---

## A. Diagnóstico de Venta

### Estados posibles (`EstadoVenta`)

| Valor | Nombre | Significado |
|-------|--------|-------------|
| 0 | `Cotizacion` | Sin compromiso. Estado inicial por defecto. |
| 1 | `Presupuesto` | Presupuesto formal. |
| 2 | `Confirmada` | Venta confirmada. Stock descontado. MovimientoCaja creado (si aplica). |
| 3 | `Facturada` | Factura emitida. MovimientoCaja creado (si no existía). |
| 4 | `Entregada` | Entregada al cliente. |
| 5 | `Cancelada` | Cancelada. Stock revertido. Crédito restaurado si aplica. |
| 6 | `PendienteRequisitos` | Esperando documentación u otros requisitos. |
| 7 | `PendienteFinanciacion` | Crédito personal: pendiente configuración de financiamiento. |

### Cuándo una venta genera impacto en Caja

**Debería generar MovimientoCaja:**
- `ConfirmarVentaAsync` → Estado pasa a `Confirmada` → `RegistrarMovimientoVentaAsync`
- `FacturarVentaAsync` → fallback: si no se registró al confirmar, registra ahora

**No debería generar MovimientoCaja:**
- `Cotizacion`, `Presupuesto`, `PendienteRequisitos`, `PendienteFinanciacion`
- `TipoPago.CreditoPersonal` (ingresa por cuotas, no al confirmar)
- `TipoPago.CuentaCorriente` (no genera ingreso inmediato)

### Diferencia entre cotización, presupuesto, confirmada y facturada

- **Cotización/Presupuesto**: No descuentan stock, no crean movimiento de caja, no generan factura.
- **Confirmada**: Stock descontado, unidades marcadas Vendidas, MovimientoCaja creado (contado).
- **Facturada**: Igual que Confirmada + Factura emitida. Fallback de Caja si no se registró antes.
- **Cancelada**: Revertida. Stock devuelto. Unidades revertidas. Crédito restaurado si aplica. **Sin reversión de Caja. Sin anulación de Factura.**

---

## B. Diagnóstico de Caja

### Entidades involucradas

**`Caja`** — punto de venta físico  
- `EstadoCaja`: solo `Cerrada` (0) y `Abierta` (1)
- Sin historial de saldos ni estados adicionales

**`AperturaCaja`** — sesión de trabajo diario  
- `Cerrada` (bool) — no es un enum de estado
- `MontoInicial`
- Sin `SaldoActual` persistido — el saldo se calcula dinámicamente sobre los movimientos

**`MovimientoCaja`** — registro individual de ingreso o egreso  
- `TipoMovimientoCaja`: solo `Ingreso` (0) y `Egreso` (1)
- `ConceptoMovimientoCaja`: VentaEfectivo, VentaTarjeta, VentaCheque, CobroCuota, CancelacionCredito, AnticipoCredito, GastoOperativo, ExtraccionEfectivo, DepositoEfectivo, DevolucionCliente, AjusteCaja, Otro
- **No tiene campo `Estado`** (no existe Pendiente / Acreditado / Anulado / Revertido)
- **No tiene campo `MovimientoOrigenId`** (no puede referenciar el movimiento que revierte)
- `VentaId` (nullable) → permite trazar el movimiento a la venta, pero no es obligatorio en todos los casos

### Qué representa Caja hoy

**Caja hoy es una mezcla de dinero real y registros operativos sin distinción:**

| Medio de pago | Registro actual | ¿Es dinero real al momento? |
|---------------|-----------------|------------------------------|
| Efectivo | VentaEfectivo, ingreso inmediato | Sí |
| Tarjeta débito/crédito | VentaTarjeta, ingreso inmediato | No necesariamente (acreditación posterior) |
| Cheque | VentaCheque, ingreso inmediato | No (acreditación posterior) |
| Transferencia | VentaEfectivo, ingreso inmediato | No necesariamente |
| MercadoPago | VentaEfectivo, ingreso inmediato | No (acreditación D+1 o D+2) |
| CreditoPersonal | No genera ingreso inmediato | Correcto |
| CuentaCorriente | No genera ingreso inmediato | Correcto |

**Hallazgo B1**: Transferencia y MercadoPago se registran con concepto `VentaEfectivo`, sin distinguirlos del efectivo físico y sin estado de acreditación pendiente.

---

## C. Diagnóstico de Comprobantes

### Entidad `Factura`

- `VentaId` (FK a Venta)
- `Numero` (generado por `_numberGenerator.GenerarNumeroFacturaAsync`)
- `Tipo` (`TipoFactura`: A, B, etc.)
- `PuntoVenta`, `FechaEmision`
- `CAE`, `FechaVencimientoCAE` (preparado para AFIP)
- `Anulada` (bool), `FechaAnulacion`, `MotivoAnulacion`

### Ciclo de vida actual de la Factura

1. **Creación**: `FacturarVentaAsync` crea la Factura y pasa Venta a estado `Facturada`.
2. **Anulación manual**: `AnularFacturaAsync` marca `Anulada=true` y revierte `Venta.Estado` a `Confirmada` si no quedan otras facturas activas.
3. **Cancelación de venta**: `CancelarVentaAsync` **NO toca las Facturas asociadas**.

### Qué pasa con la Factura al cancelar una venta Facturada

**Al ejecutar `CancelarVentaAsync` sobre una venta en estado `Facturada`:**
- `Venta.Estado` → `Cancelada` ✓
- `Stock` → revertido ✓
- `Unidades` → revertidas ✓
- `Credito personal` → restaurado si aplica ✓
- `Factura` → **queda activa con `Anulada = false`** ✗ (BUG)
- `MovimientoCaja` → **queda activo sin reversión** ✗ (BUG)

---

## D. Escenarios Analizados

### Caso A — Venta creada pero no confirmada (Cotización / Presupuesto)

| Aspecto | Comportamiento actual |
|---------|----------------------|
| MovimientoCaja | No se crea |
| Factura | No se genera |
| Aparece en Caja | No |
| **Veredicto** | Correcto |

### Caso B — Venta confirmada en efectivo

| Aspecto | Comportamiento actual |
|---------|----------------------|
| MovimientoCaja | Se crea inmediatamente en `ConfirmarVentaAsync` |
| Concepto | `VentaEfectivo` |
| Tipo | `Ingreso` |
| Es dinero real | Sí (efectivo físico) |
| **Veredicto** | Correcto |

### Caso C — Venta con Transferencia / MercadoPago / Tarjeta

| Aspecto | Comportamiento actual |
|---------|----------------------|
| MovimientoCaja | Se crea inmediatamente al confirmar |
| Concepto | Tarjeta → `VentaTarjeta`. Transferencia/MercadoPago → `VentaEfectivo` |
| Estado acreditación | No existe |
| Transferencia/MercadoPago distinguida de efectivo | **No** — mismo concepto |
| **Veredicto** | Incompleto: no hay estado de acreditación, Transferencia/MercadoPago se registran como Efectivo |

### Caso D — Venta facturada

| Aspecto | Comportamiento actual |
|---------|----------------------|
| Factura generada | Sí (`FacturarVentaAsync`) |
| MovimientoCaja | Creado al confirmar (primario) o fallback al facturar si no estaba |
| Relación Factura ↔ MovimientoCaja | Ninguna FK directa. Solo comparten `VentaId`. |
| Doble registro posible | No — hay guard `yaRegistrado` antes del fallback |
| **Veredicto** | Funcional, pero sin vínculo directo Factura-MovimientoCaja |

### Caso E — Cancelación de venta confirmada (sin factura)

| Aspecto | Comportamiento actual |
|---------|----------------------|
| Stock revertido | Sí ✓ |
| Unidades revertidas | Sí ✓ |
| Crédito personal restaurado | Sí (si aplica) ✓ |
| MovimientoCaja revertido | **No** ✗ |
| Factura a anular | No aplica (no había factura) |
| **Veredicto** | Deuda contable: el ingreso en Caja queda activo aunque la venta fue cancelada |

### Caso F — Cancelación de venta facturada

| Aspecto | Comportamiento actual |
|---------|----------------------|
| Venta → Cancelada | Sí ✓ |
| Stock revertido | Sí ✓ |
| Unidades revertidas | Sí ✓ |
| **Factura anulada** | **No** ✗ — queda `Anulada = false` |
| **MovimientoCaja revertido** | **No** ✗ — ingreso queda activo |
| Estado inconsistente | Sí: venta Cancelada + Factura activa + Caja con ingreso no revertido |
| **Veredicto** | BUG funcional real + deuda contable |

---

## E. Hallazgos Clasificados

### A. Bug funcional real

**H-A1 — Factura activa tras cancelación de venta facturada**  
- `CancelarVentaAsync` no llama a `AnularFacturaAsync` ni marca `Factura.Anulada = true`
- Resultado: Venta en estado `Cancelada` con Factura `Anulada = false`
- Archivo: `Services/VentaService.cs:1006-1056`

**H-A2 — MovimientoCaja activo sin reversión tras cancelación**  
- `CancelarVentaAsync` no crea contramovimiento de caja
- `ICajaService` no expone ningún método de reversión
- Resultado: saldo de caja inflado por ventas canceladas
- Archivo: `Services/VentaService.cs:1006-1056`, `Services/Interfaces/ICajaService.cs`

### B. Diseño incompleto

**H-B1 — No existe `RevertirMovimientoVentaAsync` ni concepto de reversión**  
- `ConceptoMovimientoCaja` no tiene valor `ReversionVenta` ni `ContramovimientoVenta`
- `ICajaService` no expone método de reversión de movimiento de venta
- Archivo: `Models/Enums/ConceptoMovimientoCaja.cs`, `Services/Interfaces/ICajaService.cs`

**H-B2 — `AnularFacturaAsync` no revierte Caja**  
- Anular una factura manualmente revierte el estado de la venta a Confirmada pero no revierte el MovimientoCaja
- Archivo: `Services/VentaService.cs:1260-1319`

### C. Deuda contable

**H-C1 — `MovimientoCaja` no tiene estado de acreditación**  
- La entidad `MovimientoCaja` no tiene campo `Estado` (Pendiente/Acreditado/Rechazado/Revertido)
- Todos los movimientos se tratan como definitivos e ingresados
- Archivo: `Models/Entities/MovimientoCaja.cs`

**H-C2 — Transferencia y MercadoPago registrados como `VentaEfectivo`**  
- Ambos medios usan concepto `VentaEfectivo` sin distinguirse del efectivo físico
- No hay estado "pendiente de acreditación" para estos medios
- Archivo: `Services/CajaService.cs:581`

**H-C3 — Caja no distingue ingreso potencial de ingreso real**  
- Al confirmar una venta con tarjeta/transferencia/MercadoPago, Caja suma el monto inmediatamente como si ya estuviera disponible

### D. Deuda UX/reporting

**H-D1 — Sin vínculo directo Factura ↔ MovimientoCaja**  
- No existe FK que una una Factura específica a un MovimientoCaja específico
- El único puente es `VentaId` en ambas entidades

**H-D2 — Reporte de caja puede incluir ventas canceladas como ingresos**  
- Si una venta se confirma y luego se cancela, el ingreso de caja permanece y distorsiona el arqueo

### E. Comportamiento correcto actual

**H-E1 — CreditoPersonal y CuentaCorriente no generan caja inmediata** — Correcto por diseño.  
**H-E2 — Guard de doble registro en FacturarVenta** — Correcto: previene duplicados.  
**H-E3 — Cotización/Presupuesto no generan movimientos de caja** — Correcto.  
**H-E4 — CancelarVenta revierte stock y unidades para Confirmada y Facturada** — Correcto.  
**H-E5 — AnularFacturaAsync tiene lógica de reversión de estado de venta** — Correcto (aunque incompleto en Caja).

---

## F. Riesgos

| Riesgo | Severidad | Impacto |
|--------|-----------|---------|
| Factura activa tras cancelación de venta | Alto | Comprobante fiscal inválido activo en el sistema |
| MovimientoCaja no revertido al cancelar | Alto | Saldo de caja inflado; arqueos incorrectos |
| Transferencia/MercadoPago como VentaEfectivo | Medio | Saldo de caja anticipa dinero no acreditado |
| Sin concepto de reversión en MovimientoCaja | Medio | No hay trazabilidad de qué movimiento fue revertido por cuál |
| Sin estado en MovimientoCaja | Medio | Imposible filtrar movimientos pendientes vs acreditados en reportes |

---

## G. Recomendación Funcional V1

### Análisis de opciones

**Opción A — Caja solo dinero real**  
Solo registra ingresos efectivamente recibidos. Requiere cambio significativo en el flujo actual y una definición precisa de "cuándo se considera recibido" para cada medio de pago.  
**Demasiado disruptivo para V1.**

**Opción B — Caja operativa con estados**  
Agregar campo `EstadoMovimientoCaja` (Pendiente / Acreditado / Anulado / Revertido).  
Cambio de esquema de BD + migración + ajuste de queries de saldo.  
**Correcto a largo plazo pero amplio para V1.**

**Opción C — Separar Cobros de Caja**  
Crear entidad `CuentaPorCobrar` separada de `MovimientoCaja`.  
**Rediseño arquitectónico mayor. No para V1.**

**Opción D — Contramovimientos**  
Al cancelar: crear movimiento `Egreso` con concepto nuevo `ReversionVenta` que linkea al `VentaId` original.  
No borra nada. Trazabilidad completa. El saldo queda correcto.  
Mínima superficie de cambio en BD (solo nuevo concepto enum, nuevo método en CajaService).  
**Recomendada para V1.**

### Decisión recomendada V1: **Opción D — Contramovimientos**

Razones:
1. No requiere cambio de esquema en `MovimientoCaja`
2. Preserva trazabilidad histórica (el ingreso original queda registrado, el egreso de reversión también)
3. El saldo calculado (`CalcularSaldoActualAsync`) ya suma Ingresos y resta Egresos — el contramovimiento funcionaría automáticamente
4. Bajo riesgo: solo agrega un concepto nuevo al enum y un método en `ICajaService`
5. Apropiado para la etapa actual del ERP

**Para la Factura**: al cancelar una venta `Facturada`, llamar a `AnularFacturaAsync` dentro de `CancelarVentaAsync` antes de cambiar el estado.

---

## H. Plan de Fases Recomendado

### 9.2 — Anulación de Factura al cancelar venta facturada
- En `CancelarVentaAsync`: si `venta.Estado == Facturada`, llamar a `AnularFacturaAsync` para cada factura activa
- Test: cancelar venta facturada → Factura.Anulada == true

### 9.3 — Reversión de MovimientoCaja al cancelar
- Agregar `ConceptoMovimientoCaja.ReversionVenta` al enum
- Agregar `RevertirMovimientoVentaAsync(int ventaId, string usuario)` a `ICajaService`
- En `CancelarVentaAsync`: si venta era `Confirmada` o `Facturada` y TipoPago no es Crédito/CuentaCorriente, crear contramovimiento
- Test: cancelar venta confirmada → saldo de caja igual al saldo previo

### 9.4 — Estados de acreditación para medios no efectivos (alcance acotado)
- Agregar `ConceptoMovimientoCaja.VentaTransferencia` y `VentaMercadoPago` para distinguir de efectivo
- Evaluar agregar `EstadoAcreditacion` a `MovimientoCaja` (Pendiente / Acreditado)
- Solo para Transferencia y MercadoPago en primera iteración

### 9.5 — QA E2E: flujo completo venta → factura → caja → cancelación
- Test integración: venta Efectivo confirmada → cancelada → saldo caja = 0 (o igual al inicial)
- Test integración: venta Efectivo facturada → cancelada → Factura.Anulada = true + saldo caja = 0
- Test integración: AnularFacturaAsync → verifica que MovimientoCaja no queda huérfano

---

## I. Checklist

### Fase 9.1 — Diagnóstico (este documento)

- [x] Diagnóstico de estados de Venta
- [x] Diagnóstico de entidades Caja / AperturaCaja / MovimientoCaja
- [x] Diagnóstico de Factura y ciclo de vida
- [x] Análisis de ConfirmarVentaAsync → Caja
- [x] Análisis de FacturarVentaAsync → Caja (fallback)
- [x] Análisis de CancelarVentaAsync → sin reversión Caja ni anulación Factura
- [x] Análisis de AnularFacturaAsync → sin reversión Caja
- [x] Escenarios A a F documentados
- [x] Hallazgos clasificados (A: bug real, B: diseño incompleto, C: deuda contable, D: deuda UX, E: correcto)
- [x] Riesgos identificados
- [x] Recomendación V1 elegida y justificada (Opción D — Contramovimientos)
- [x] Plan de fases 9.2–9.5 propuesto
- [x] Build Release: OK (0 errores, 0 advertencias)
- [x] git diff --check: OK (sin whitespace issues)
- [x] Documento creado en `docs/fase-9.1-diagnostico-caja-comprobantes-cancelacion.md`

### Pendientes producción (NO implementados en esta fase)

- [ ] 9.2 — Anular Factura al cancelar venta facturada
- [ ] 9.3 — Contramovimiento de Caja al cancelar venta
- [ ] 9.4 — Estados de acreditación para Transferencia / MercadoPago
- [ ] 9.5 — QA E2E flujo venta → factura → caja → cancelación

---

## Archivos revisados (sin modificaciones)

| Archivo | Clasificación |
|---------|---------------|
| `Services/VentaService.cs` | Canónico |
| `Services/CajaService.cs` | Canónico |
| `Services/Interfaces/ICajaService.cs` | Canónico |
| `Services/Interfaces/IVentaService.cs` | Canónico |
| `Models/Entities/Venta.cs` | Canónico |
| `Models/Entities/MovimientoCaja.cs` | Canónico |
| `Models/Entities/Caja.cs` | Canónico |
| `Models/Entities/AperturaCaja.cs` | Canónico |
| `Models/Entities/Factura.cs` | Canónico |
| `Models/Enums/EstadoVenta.cs` | Canónico |
| `Models/Enums/TipoMovimientoCaja.cs` | Canónico |
| `Models/Enums/ConceptoMovimientoCaja.cs` | Canónico |
| `Models/Enums/TipoPago.cs` | Canónico |
| `Models/Enums/EstadoCaja.cs` | Canónico |
| `Tests/Integration/VentaComprobanteFacturaTests.cs` | Canónico |
| `Tests/Integration/VentaServiceFacturacionTests.cs` | Canónico |
| `Tests/Integration/AjusteGlobalVentaCajaComprobanteTests.cs` | Canónico |
