# Crédito — Flujo final consolidado (FASE 1-8C)

> Estado: **FASE 8D — cierre documental**. Consolida FASE 1 a FASE 8C. `main` ahead 3 de `origin/main` (`23dc225`, `eaadfda`, `1ff4a4d`). Sin push. Fecha documental: 2026-07-03.
> Documentos hermanos: [`credito-fase-1-cierre.md`](credito-fase-1-cierre.md), [`credito-fase-2-garante.md`](credito-fase-2-garante.md), [`credito-fase-3-cuenta-disponible.md`](credito-fase-3-cuenta-disponible.md), [`credito-fase-4-evaluador-unificado.md`](credito-fase-4-evaluador-unificado.md), [`credito-fase-5-autorizacion-manual.md`](credito-fase-5-autorizacion-manual.md), [`credito-fase-6-mora-puntaje.md`](credito-fase-6-mora-puntaje.md), [`credito-fase-7-ui-perfil-crediticio.md`](credito-fase-7-ui-perfil-crediticio.md).

## 1. Objetivo del documento

Dar una referencia única y actualizada del flujo de crédito tal como quedó después de FASE 1-8C, para que cualquier agente (o Javo) pueda entender el camino canónico sin recorrer los 7 documentos de fase previos. No reemplaza esos documentos — los consolida.

## 2. Estado final del flujo crédito

El eje único de aptitud/cupo es `PuntajeCliente` (0-5), gobernado por `ClienteScoringService`/`ClienteScoringCalculator`, con override manual opcional por cliente. La aptitud para vender a crédito combina documentación, cupo, mora y BCRA/Veraz en `ClienteAptitudService`. La venta a crédito pasa siempre por `ValidacionVentaService` → `VentaService`, con autorización manual puntual cuando corresponde. El flujo legado (`EvaluacionCreditoService`, `SolicitarCreditoAsync`) está marcado como no productivo pero no eliminado.

## 3. Mapa funcional

1. **`Cliente/Details`** — ficha del cliente: puntaje protagonista, cupo, mora, historial de puntaje, autorizaciones pendientes, BCRA, documentación, garante, créditos del cliente (ver sección 6).
2. **Venta a crédito** — se origina en `Venta/Create` (o flujo de cotización → venta). `ValidacionVentaService.PrevalidarAsync`/`ValidarVentaCreditoPersonalAsync` corre antes de confirmar.
3. **Evaluación de aptitud** — `ClienteAptitudService.EvaluarAptitudAsync`/`EvaluarAptitudSinGuardarAsync` combina documentación, cupo, mora y BCRA en un único `EstadoCrediticioCliente` (`Apto` / `RequiereAutorizacion` / `NoApto` / `NoEvaluado`).
4. **Cupo/disponible** — `CreditoDisponibleService.CalcularDisponibleAsync` resuelve `Limite - SaldoVigente` según `PuntajeCliente` (o nivel manual) contra la tabla `PuntajesCreditoLimite`.
5. **Autorización manual** — si la venta requiere autorización, queda `PendienteAutorizacion` y un segundo usuario la autoriza/rechaza puntualmente (`VentaService.SolicitarAutorizacionAsync`/`AutorizarAsync`).
6. **Configuración del crédito** — `Credito` pasa por estados (`PendienteConfiguracion` → `Configurado` → `Activo`) donde se definen cuotas/plan.
7. **Confirmación de venta** — `VentaService.ConfirmarVentaAsync` exige `EstadoAutorizacion == Autorizada` si `RequiereAutorizacion == true`.
8. **Pago de cuotas** — `CreditoService.PagarCuotaAsync` (individual o múltiple) actualiza cuota/saldo/caja y dispara recálculo de puntaje en la misma transacción.
9. **Recálculo de puntaje** — automático al pagar cuota (`ClienteScoringService.RecalcularYAuditarAsync`, origen `RecalculoAutomaticoPago`) o manual desde `Cliente/Details` (origen `RecalculoManual`, auditado desde FASE 8B1).
10. **Mora/cobranza** — `MoraService`/`MoraBackgroundService` detectan cuotas vencidas por fecha y generan `AlertaCobranza`; no tocan `PuntajeCliente` (deuda explícita, ver sección 9).

## 4. Reglas finales

- `PuntajeCliente` (0-5) es el eje único que gobierna cupo; no hay un segundo scoring paralelo.
- Puntaje 0 (cliente nuevo) tiene cupo default **200.000** (seed `PuntajesCreditoLimite`, editable por Javo). Puntajes 1-5 quedan en 0 hasta que Javo los configure.
- Garante real validado: no puede ser el mismo cliente, debe existir y estar activo, tener al menos 1 compra propia, puntaje ≥ 4, y no garantizar más de 3 clientes a la vez (`GaranteService.ValidarGaranteAsync`). Un garante válido puede sustituir el recibo de sueldo como requisito documental.
- BCRA/Veraz es obligatorio: sin CUIL/CUIT o sin consulta registrada, el cliente queda `NoApto`. Consulta fallida o sin situación informada → `RequiereAutorizacion`. Situación 0-1 normal, 2 requiere revisión, ≥3 bloquea.
- Buen pagador antiguo (puntaje ≥4, antigüedad ≥90 días, ≥1 compra, créditos en término ≥1, sin atrasos, sin mora activa) con BCRA situación ≥3 no queda `NoApto` automático: degrada a `RequiereAutorizacion` (FASE 4D).
- La autorización manual es puntual por venta: no modifica cupo, puntaje ni límite futuro, y no autoriza otras ventas. Queda auditada con usuario, motivo y fecha.
- Recálculo automático de `PuntajeCliente` solo ocurre al pagar cuota (dentro de la misma transacción del pago).
- Recálculo manual de puntaje desde `Cliente/Details` queda auditado en `ClientePuntajeHistorial` con origen `RecalculoManual` (FASE 8B1, antes no se auditaba).
- Mora diaria (`MoraService`/`MoraBackgroundService`) no recalcula `PuntajeCliente` — sigue siendo deuda explícita de FASE 6D.

## 5. Servicios canónicos

| Servicio | Responsabilidad |
|---|---|
| `ClienteAptitudService` | Evaluación de aptitud compuesta (documentación + cupo + mora + BCRA) → `EstadoCrediticioCliente`. |
| `CreditoDisponibleService` | Cálculo de límite/saldo/disponible según `PuntajeCliente` (o nivel manual) y presets `PuntajesCreditoLimite`. |
| `ClienteScoringService` / `ClienteScoringCalculator` | Cálculo y recálculo de `PuntajeCliente`, con auditoría opcional (`RecalcularYAuditarAsync`). |
| `GaranteService` | Validación, asignación y remoción de garante; búsqueda de candidatos. |
| `ValidacionVentaService` | Evaluación unificada de crédito para venta (prevalidación, validación de confirmación, resumen crediticio). |
| `VentaService` | Alta/edición/confirmación de venta; máquina de estados de autorización puntual. |
| `CreditoService` | Ciclo de vida del crédito (configuración, pago de cuotas, recálculo de puntaje por pago). |
| `MoraService` / `MoraBackgroundService` | Detección de cuotas vencidas por fecha y generación de alertas de cobranza (no toca puntaje). |

## 6. UI final Cliente/Details

- **Puntaje protagonista** — valor único (0-5) con chip de fuente Automático/Manual; detalle "Manual: X/5 • Automático: Y/5" solo si hay override (FASE 7C).
- **Cupo** — total/usado/disponible con barra de progreso y origen del cupo, sin cambios de cálculo (FASE 7).
- **Mora y umbrales** — chip de estado + monto en mora + mensaje contextual de umbral (autorización/NoApto) cuando hay mora activa (FASE 7D).
- **Historial de puntaje** — sección colapsable con tabla de cambios auditados (`ClientePuntajeHistorial`): fecha, puntaje anterior/nuevo, origen, registrado por, observación (FASE 7B).
- **Autorizaciones pendientes** — banner de alerta con últimas 5 ventas `PendienteAutorizacion` del cliente, con acceso condicionado al permiso `ventas.authorize` (FASE 7E).
- **BCRA** — panel lateral: situación crediticia, período informado, última consulta, botón actualizar.
- **Documentación** — panel lateral: completa/incompleta, pendientes.
- **Garante** — panel lateral: asignado/sin asignar, validez, modal de asignación.
- **Créditos del cliente** — tabla de créditos del cliente, overflow corregido en FASE 7F.

## 7. Auditoría

- **`ClientePuntajeHistorial`** — registra todo cambio efectivo de `PuntajeCliente` (no duplica si el puntaje no cambia).
- **Origen `RecalculoAutomaticoPago`** — recálculo disparado por `CreditoService.PagarCuotaAsync` dentro de la transacción de pago (FASE 6C).
- **Origen `RecalculoManual`** — recálculo disparado desde `Cliente/Details` por un usuario; antes de FASE 8B1 no quedaba auditado, ahora usa el mismo `RecalcularYAuditarAsync` (FASE 8B1, commit `23dc225`).
- **Autorización manual de venta** — `EstadoAutorizacionVenta` con `FechaSolicitudAutorizacion`, `FechaAutorizacion`, `MotivoAutorizacion` y usuario que autoriza; no modifica cupo ni puntaje (FASE 5).

## 8. Legacy marcado

Marcado explícitamente como no productivo en FASE 8B3 (commit `eaadfda`), conservado solo por cobertura de tests existente — no ampliar su uso:

- `EvaluacionCreditoService` / `IEvaluacionCreditoService`.
- `EvaluacionCreditoService.EvaluarSolicitudAsync`.
- `CreditoService.SolicitarCreditoAsync`.
- `SolicitudCreditoViewModel`.

El flujo canónico para cualquier validación nueva es `VentaService` / `ValidacionVentaService` / `ClienteAptitudService`.

## 9. Deuda pendiente

- Sección "Evaluación" en `Credito/Details_tw.cshtml` (`Model.Evaluacion`) sigue mostrando datos del flujo legacy, huérfana del flujo canónico.
- `EvaluacionCreditoService` sigue registrado en DI aunque no tiene caller productivo.
- `EstadoCuota.Vencida` / `CreditoService`-adyacente `ActualizarEstadoCuotasAsync` sin caller productivo (mora se detecta por fecha, no por este estado) — deuda de FASE 6D.
- `ConfiguracionMora.CambiarEstadoCuotaAuto` / `ActualizarMoraAutomaticamente` existen pero no tienen efecto real hoy — deuda de FASE 6D.
- Decisión pendiente de Javo: si `DiasGracia` debe aplicar al recálculo de puntaje por mora, o solo a cobranza — bloquea implementar `RecalculoAutomaticoMora` en `MoraService` (FASE 6D).
- BCRA/aptitud no determinístico: diagnóstico aparte pendiente, no abordado en FASE 4-8C.
- Optimización futura: la consulta de ventas pendientes de autorización en `ClienteController` trae y filtra en memoria (top 5) — sin impacto medido todavía.

## 10. Validaciones finales realizadas (FASE 8D)

- Build: no se ejecutó en este lote (solo se corrieron tests focalizados con `--no-build` sobre binarios existentes).
- Tests focalizados:
  ```
  dotnet test TheBuryProyect.Tests/TheBuryProyect.Tests.csproj --filter "FullyQualifiedName~ClienteAptitudServiceTests|FullyQualifiedName~ClienteScoringServiceTests|FullyQualifiedName~CreditoServicePuntajeClienteRecalculoTests|FullyQualifiedName~VentaServiceAutorizacionTests|FullyQualifiedName~VentaServiceCancelarCreditoLiberaCupoTests|FullyQualifiedName~GaranteServiceTests|FullyQualifiedName~CreditoDisponibleServiceLimitesTests" --no-build
  ```
  Resultado: **153/153 OK**, 0 fallos, 18s.
- QA visual: no se repitió en FASE 8D; hereda el QA visual desktop/mobile de FASE 7F (sin overflow, sin errores de consola).

## 11. Tabla de commits relevantes FASE 6-8

| Fase | Commit | Descripción |
|---|---|---|
| 6C | `fd2d9d4` | Recalcular `PuntajeCliente` al pagar cuotas |
| 7B | `184d980` | Historial de puntaje visible en Cliente/Details |
| 7C | `dd76473` | Consolidar puntaje protagonista |
| 7D | `46f309d` | Umbrales de mora visibles |
| 7E | `7781c6d` | Autorizaciones pendientes visibles |
| 7F | `a827f9a`, `4f2f365` | QA visual: fix overflow historial y créditos |
| 8B1 | `23dc225` | Auditar recálculo manual de puntaje (`RecalcularYAuditarAsync`, origen `RecalculoManual`) |
| 8B3 | `eaadfda` | Marcar flujo legacy de evaluación crediticia (XML `<remarks>` en 4 archivos) |
| 8B5 | `1ff4a4d` | Actualizar tests de aptitud por cupo de puntaje cero (drift `Puntaje 0 = 200000` resuelto) |
| 8D | Este documento | Cierre documental del flujo consolidado FASE 1-8C |

## 12. Próximo paso

**FASE 8E — Checklist final / push / cierre.**

---

## Estado de cierre documental

- Archivo documental creado: `docs/credito-flujo-final.md`.
- No se modificó código productivo.
- No se modificaron tests.
- No se modificó UI.
- No se tocaron stashes.
- No se hizo push.
- No se avanzó a FASE 8E.
