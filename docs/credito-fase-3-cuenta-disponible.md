# Crédito FASE 3 — Cierre de cuenta crédito/disponible

> Estado: **FASE 3 CERRADA** (sub-fases A, B, C). Commits en `main`, sin push. Fecha de cierre: 2026-07-01.
> Documentos hermanos: [`credito-fase-1-cierre.md`](credito-fase-1-cierre.md), [`credito-fase-2-garante.md`](credito-fase-2-garante.md).

## A. Objetivo de FASE 3

Cerrar el ciclo de vida de la **cuenta de crédito** del cliente para que el disponible mostrado y usado en validaciones sea siempre el real:

- **Cupo asignado**: límite efectivo derivado del puntaje (automático o manual) más overrides/excepciones vigentes.
- **Deuda vigente / saldo usado**: suma de `SaldoPendiente` de los créditos en estados vigentes.
- **Disponible real**: `límite efectivo − saldo vigente`, nunca negativo.
- **Consumo/liberación de cupo**: el cupo se reserva al generar una venta a crédito y se libera al pagar cuotas o al cancelar/rechazar la venta asociada.

FASE 3 no cambia la fórmula de cupo (esa es FASE 1); cierra los eventos del ciclo de vida que antes dejaban el disponible desincronizado del estado real de la deuda.

---

## B. Regla confirmada de cupo

- El cupo lo gobierna `PuntajeCliente` (0–5) o la configuración manual existente (`NivelCreditoManual`, overrides, excepciones) — ver [FASE 1](credito-fase-1-cierre.md). No cambia en FASE 3.
- El **garante NO aumenta el cupo**. `GaranteService.AsignarGaranteAsync` no toca `LimiteCredito` (regla ya establecida en FASE 2, ahora también visible en UI — ver punto D).
- `SaldoVigente` se calcula con `CreditoDisponibleService.CalcularSaldoVigenteAsync(clienteId)`: suma `SaldoPendiente` de los créditos del cliente con `SaldoPendiente > 0` y `Estado` dentro de `EstadosVigentes` (`Solicitado`, `Aprobado`, `Activo`, `PendienteConfiguracion`, `Configurado`, `Generado`). `Cancelado` y `Finalizado` quedan **fuera** de la suma.
- `Disponible = Math.Max(0, límiteEfectivo − SaldoVigente)` — único punto de cálculo, en `CreditoDisponibleService.CalcularDisponibleAsync`.
- **No duplicar la fórmula** en controller ni vista: `Cliente/Details_tw.cshtml` y `ClienteController` consumen el resultado de `CreditoDisponibleService`, no recalculan.

---

## C. FASE 3A — Pago libera disponible

**Commit:** `e6a2500` — `test(credito): cubrir liberación de disponible al pagar cuotas`

- `CreditoService.PagarCuotaAsync` (Services/CreditoService.cs:491) llama a `RecalcularSaldoCreditoAsync` (Services/CreditoService.cs:915) tras registrar el pago.
- `RecalcularSaldoCreditoAsync` recalcula `credito.SaldoPendiente` como la suma del capital pendiente de las cuotas no canceladas (`CalcularCapitalPendienteCuota`), es decir, **baja proporcionalmente al capital pagado**, no solo cuando el crédito termina.
- Si todas las cuotas quedan `Pagada` o `Cancelada`, el crédito pasa a `EstadoCredito.Finalizado` y sale de `EstadosVigentes` → deja de contar en `SaldoVigente` → libera el cupo restante.
- **Tests agregados:** `TheBuryProyect.Tests/Integration/CreditoServicePagoDisponibleTests.cs` (316 líneas, archivo nuevo):
  - `PagoParcial_ReduceSaldoPendienteProporcional_YLiberaDisponibleParcialmente`
  - `PagoTotal_FinalziaCredito_YLiberaDisponibleCompleto`
  - `PagoTotal_UnoDeDosCreditos_LiberaSoloCupoDelCreditoPagado`

---

## D. FASE 3B — Cliente/Details muestra cupo/deuda/disponible

**Commit:** `b9db824` — `feat(credito): mostrar origen de cupo y nota de garante en cliente`

- `Views/Cliente/Details_tw.cshtml`: agrega fila **"Origen del cupo"** en el desglose (`panel?.Valores?.OrigenLimite`, ya calculado por `CreditoDisponibleService`, sin lógica nueva en la vista).
- Agrega nota visible bajo el bloque Garante: **"El garante no aumenta el cupo asignado."**
- Diff acotado a 2 líneas agregadas en un solo archivo — sin tocar controller ni backend.
- QA visual realizado en 1440×900, 1280×720, 390×844 (según reporte de sesión); sin capturas adjuntas en este documento.

---

## E. FASE 3C — Cancelar/rechazar venta a crédito libera cupo

**Commit:** `550cf28` — `fix(credito): liberar cupo al cancelar ventas a credito`

**Bug corregido:** al cancelar o rechazar una venta a crédito, el crédito asociado quedaba con `SaldoPendiente > 0` y en estado vigente ("crédito zombie"), consumiendo cupo del cliente indefinidamente aunque la venta ya no existiera.

**Cambios en `Services/VentaService.cs`:**
- Nuevo método privado `CancelarCreditoAsociadoAVenta(Credito credito, string motivo)`: si el crédito no está ya `Cancelado`, lo pasa a `EstadoCredito.Cancelado`, fija `FechaFinalizacion`, pone `SaldoPendiente = 0`, agrega el motivo a `Observaciones` y cancela toda cuota que no esté `Pagada` ni `Cancelada`.
- Camino de **venta con crédito pendiente** (antes de generar cuotas reales, `venta.CreditoId.HasValue` sin `VentaCreditoCuotas`): al dar de baja la venta, ahora busca el crédito (`venta.Credito` o query con `Include(Cuotas)`) y llama a `CancelarCreditoAsociadoAVenta` antes de limpiar `venta.CreditoId`.
- `RestaurarCreditoPersonall` (camino con `VentaCreditoCuotas` reales, es decir crédito ya generado): antes solo revertía sumando el monto financiado a `SaldoPendiente` y borraba las cuotas de venta si `venta.VentaCreditoCuotas.Any()` — dejaba el crédito **vigente** con saldo. Ahora siempre resuelve el crédito por `venta.CreditoId`, borra `VentaCreditoCuotas` si existen, y llama a `CancelarCreditoAsociadoAVenta` para cancelarlo por completo (en vez de "restaurarlo" a vigente).
- **Tests agregados:** `TheBuryProyect.Tests/Integration/VentaServiceCancelarCreditoLiberaCupoTests.cs` (456 líneas, archivo nuevo):
  - `CancelarVentaCreditoPendiente_LiberaCupo`
  - `RechazarVentaCreditoPendiente_LiberaCupo`
  - `CancelarVentaCreditoGenerada_LiberaCupo`
- Tests confirmados rojo→verde en la sesión (fallaban contra el código previo, pasan tras el fix).

---

## F. Flujo final confirmado

1. **Crear venta a crédito** → reserva cupo (el crédito queda en estado vigente con `SaldoPendiente` > 0, cuenta en `CalcularSaldoVigenteAsync`).
2. **Pagar cuota** → `RecalcularSaldoCreditoAsync` baja `SaldoPendiente` proporcionalmente; si se completa, el crédito pasa a `Finalizado` y libera el cupo restante (FASE 3A).
3. **Cancelar/rechazar venta a crédito** → el crédito asociado se cancela (`SaldoPendiente = 0`, `Estado = Cancelado`), sale de `EstadosVigentes` y libera el cupo completo (FASE 3C).
4. **Cliente/Details** → muestra origen del cupo, y aclara que el garante no lo aumenta (FASE 3B); los valores de cupo/deuda/disponible vienen siempre de `CreditoDisponibleService`, nunca recalculados en vista/controller.

---

## G. Deuda / riesgos remanentes

- La rama legacy E4 con `VentaCreditoCuotas` sigue existiendo (`RestaurarCreditoPersonall` la sigue manejando), pero no es el camino canónico de `VentaController` para crédito personal actual. No se eliminó en este lote — fuera de scope.
- Posible micro-lote futuro: **edición de venta pendiente** con actualización del monto reservado (hoy el flujo cubre alta/pago/baja, no edición del monto financiado de una venta a crédito ya creada).
- **FASE 4 pendiente**: evaluador unificado / aptitud compuesta, incluyendo regla "sueldo OR garante" (mencionada como pendiente ya en el cierre de [FASE 2](credito-fase-2-garante.md), punto "Requisito compuesto").
- 4 tests preexistentes de `ClienteAptitudServiceTests` fallaban antes de este lote; no se tocaron ni se mezclaron con el trabajo de FASE 3.
- Los 4 stashes existentes en el repo (`mercadolibre`, `kira/producto-unidades-ui-1a-rebuild`, `kira/pagos-abm-1b-tarjetas-admin`, `juan/fix-producto-movimientos-delete`) quedan fuera de scope — no tocados.
- `main` está 10 commits ahead de `origin/main`: no se hizo push sin decisión explícita del usuario.

---

## Commits de FASE 3

| Sub-fase | Commit | Descripción |
|---|---|---|
| 3A | `e6a2500` | test(credito): cubrir liberación de disponible al pagar cuotas |
| 3B | `b9db824` | feat(credito): mostrar origen de cupo y nota de garante en cliente |
| 3C | `550cf28` | fix(credito): liberar cupo al cancelar ventas a credito |
