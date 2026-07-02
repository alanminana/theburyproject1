# Credito FASE 6 - Mora y ajuste de PuntajeCliente

> Estado: **FASE 6 CERRADA parcial** (sub-fases A, B, C, D documentadas). FASE 6E es cierre documental. Commit funcional `fd2d9d4` en `main`, sin push. Fecha documental: 2026-07-02.
> Documentos hermanos: [`credito-fase-1-cierre.md`](credito-fase-1-cierre.md), [`credito-fase-2-garante.md`](credito-fase-2-garante.md), [`credito-fase-3-cuenta-disponible.md`](credito-fase-3-cuenta-disponible.md), [`credito-fase-4-evaluador-unificado.md`](credito-fase-4-evaluador-unificado.md), [`credito-fase-5-autorizacion-manual.md`](credito-fase-5-autorizacion-manual.md).

## Objetivo de FASE 6

Conectar mora/pagos con `PuntajeCliente`.

Reglas de alcance:

- No crear un scoring nuevo.
- Reusar `ClienteScoringService` / `ClienteScoringCalculator`.
- Auditar cambios automaticos de `PuntajeCliente`.
- No tocar autorizacion manual, BCRA ni garante.

---

## FASE 6A - Diagnostico

Existen dos sistemas separados:

- **Cobranza operativa:** `MoraService` / `MoraBackgroundService`.
- **Evaluacion/scoring:** `ClienteAptitudService` / `ClienteScoringService`.

Hallazgos:

- Mora se calcula mayormente por fecha, no por `EstadoCuota.Vencida`.
- `EstadoCuota.Vencida` / `ActualizarEstadoCuotasAsync` queda como deuda preexistente sin caller productivo.
- `PuntajeCliente` solo se recalculaba manualmente desde `Cliente/Details`.

---

## FASE 6B - Tests de contrato

Tests rojos esperados definidos para:

- Pagar cuota atrasada recalcula y penaliza.
- Pagar cuota en termino recalcula sin penalizar.
- Cambio de `PuntajeCliente` registra `ClientePuntajeHistorial`.
- Si no cambia, no duplica historial.
- No toca BCRA/garante/autorizacion manual.

No se commiteo 6B en rojo por separado; quedo integrado directamente en el commit de 6C.

---

## FASE 6C - Recalcular PuntajeCliente al pagar cuotas

**Commit:** `fd2d9d49467ffcdc97ed8e4d4c516982be31926d`

- `CreditoService.PagarCuotaAsync` recalcula `PuntajeCliente` dentro de la misma transaccion del pago.
- El flujo de pago multiple tambien recalcula, una unica vez por cliente.
- Se inyecto `IClienteScoringService` en `CreditoService`.
- No se duplica la formula de scoring (se reusa el service existente).
- Se registra `ClientePuntajeHistorial` solo si `PuntajeCliente` cambia.
- Origen de auditoria usado: `RecalculoAutomaticoPago`.

Archivos tocados: `Services/CreditoService.cs`, `Tests/.../CreditoServicePagoDisponibleTests.cs`, `Tests/.../CreditoServicePuntajeClienteRecalculoTests.cs`.

Validaciones:

- `CreditoServicePuntajeClienteRecalculoTests` 5/5.
- `ClienteScoring` 27/27.
- `PagarCuota` 27/27.
- `CreditoService` completo 175/175.

Ajuste colateral: `CreditoServicePagoDisponibleTests` adaptado porque `PuntajeCliente` ya no queda estatico despues del pago.

---

## FASE 6D - Diagnostico mora vencida sin pago

Hallazgos:

- `MoraBackgroundService` corre 1 vez por dia segun `ConfiguracionMora`.
- `MoraService.ProcesarMoraAsync` detecta cuotas vencidas y genera `AlertaCobranza`.
- `MoraService` no llama a `ClienteScoringService`.
- Existe query reutilizable para obtener clientes con cuotas vencidas.
- No hace falta `EstadoCuota.Vencida` para detectar mora (se detecta por fecha).

**Decision: no implementar en esta fase.**

Motivo:

- Falta decision funcional: ¿`PuntajeCliente` baja apenas vence la cuota, o recien despues de `DiasGracia`?
- Implementarlo ahora tocaria `MoraService` y ampliaria el alcance mas alla del pago de cuota.
- FASE 6C ya cerro el disparador seguro por pago de cuota, que es la parte con contrato claro.

Opcion recomendada para una fase futura:

- Extraer el recalculo + auditoria a un helper compartido (`ClienteScoringService.RecalcularYAuditarAsync` o equivalente).
- `MoraService` recalcularia los clientes unicos afectados por mora, con `Origen=RecalculoAutomaticoMora`.

---

## Flujo final implementado en FASE 6

1. Cliente paga cuota.
2. `CreditoService` actualiza cuota, saldo y caja.
3. Antes del commit de la transaccion:
   - llama a `ClienteScoringService.RecalcularAsync`;
   - recalcula `PuntajeCliente`;
   - si cambio, registra `ClientePuntajeHistorial` con `Origen=RecalculoAutomaticoPago`.
4. El cupo se actualiza indirectamente porque `CreditoDisponibleService` usa `PuntajeCliente`.
5. No se toca BCRA.
6. No se toca garante.
7. No se toca autorizacion manual.

Mora vencida sin pago **no** dispara recalculo todavia (ver FASE 6D).

---

## Reglas finales

- `PuntajeCliente` puede cambiar automaticamente por pago de cuota.
- Los cambios automaticos quedan auditados en `ClientePuntajeHistorial`.
- No se registra historial si el puntaje no cambia.
- No se duplica la formula de scoring.
- No se recalcula por mora diaria todavia.
- Mora vencida sin pago queda como decision posterior (deuda explicita).

---

## Deuda / riesgos remanentes

- Decidir si `DiasGracia` aplica al scoring o solo a cobranza.
- Decidir si `MoraService` debe disparar `RecalculoAutomaticoMora`.
- Extraer helper compartido de recalculo + auditoria para evitar duplicacion entre `CreditoService` y `MoraService` cuando se implemente el disparador de mora.
- `EstadoCuota.Vencida` / `ActualizarEstadoCuotasAsync` sigue sin caller productivo.
- `ConfiguracionMora.CambiarEstadoCuotaAuto` y `ActualizarMoraAutomaticamente` existen pero hoy no tienen efecto real.
- 4 tests preexistentes de `ClienteAptitudServiceTests` siguen fallando por drift `Puntaje 0 = 200000` (fuera de scope de FASE 6).
- No hacer push hasta decision explicita.

---

## Commits de FASE 6

| Sub-fase | Commit | Descripcion |
|---|---|---|
| 6A | Sin commit propio | Diagnostico cobranza vs scoring |
| 6B | Sin commit propio | Tests de contrato integrados en 6C |
| 6C | `fd2d9d49467ffcdc97ed8e4d4c516982be31926d` | Recalcular PuntajeCliente al pagar cuotas |
| 6D | Sin commit propio | Diagnostico mora vencida sin pago, no implementado |
| 6E | Este documento | Cierre documental de FASE 6 |

---

## Estado de cierre documental

- Archivo documental creado: `docs/credito-fase-6-mora-puntaje.md`.
- No se modifico codigo productivo.
- No se modificaron tests.
- No se modifico UI.
- No se tocaron stashes.
- No se hizo push.
- No se hizo commit.
