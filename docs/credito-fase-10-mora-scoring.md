# Crédito FASE 10 — Mora con días de gracia y scoring

> Estado: **FASE 10 CERRADA funcionalmente (10A-10D) + cierre documental (10E)**. main == origin/main al iniciar 10E. Fecha documental: 2026-07-03.
> Documentos hermanos: [`credito-flujo-final.md`](credito-flujo-final.md) (consolidado), [`credito-fase-6-mora-puntaje.md`](credito-fase-6-mora-puntaje.md) (diagnóstico previo de FASE 6D que dejó esta decisión pendiente), [`credito-fase-9-limpieza-legacy.md`](credito-fase-9-limpieza-legacy.md).

## Objetivo de FASE 10

Cerrar la deuda explícita dejada por FASE 6D: decidir si `ConfiguracionMora.DiasGracia` aplica al recálculo de `PuntajeCliente` por mora o solo a cobranza, e implementar esa decisión conectando el job diario de mora con `ActualizarEstadoCuotasAsync` y `ClienteScoringService.RecalcularYAuditarAsync`.

## Decisión funcional: opción B

Javo eligió la **opción B**: la mora **no** baja el puntaje inmediatamente al vencer la cuota. Se respetan los días de gracia (`ConfiguracionMora.DiasGracia`) antes de impactar `PuntajeCliente`. Recién superado ese plazo, el job diario puede recalcular y auditar el puntaje.

## FASE 10A — Diagnóstico

Confirmado (sin tocar código):

- Mora es 100% dinámica: se detecta por fecha de vencimiento de cuota, no por `EstadoCuota.Vencida`.
- `CreditoService.ActualizarEstadoCuotasAsync` existía sin caller productivo.
- `MoraBackgroundService`/`MoraService` solo generaban alertas de cobranza; no tocaban `PuntajeCliente`.
- `ClienteScoringService` solo recalculaba puntaje al pagar cuota (`RecalculoAutomaticoPago`) o manualmente desde `Cliente/Details` (`RecalculoManual`).
- La aptitud (`ClienteAptitudService`) ya funcionaba correctamente vía `ConfiguracionCredito` (`DiasParaRequerirAutorizacion`, `DiasParaNoApto`) — no dependía de este flujo.
- Los flags `ConfiguracionMora.CambiarEstadoCuotaAuto` / `ActualizarMoraAutomaticamente` existían en el modelo pero no tenían efecto real (código muerto de configuración).

Sin código en esta sub-fase. Ver también memoria de sesión previa (03-jul, sin commit propio).

## FASE 10B — Decisión de Javo

Opción B confirmada por Javo (sin commit propio; decisión funcional que desbloqueó 10C).

## FASE 10C — Implementación mora con días de gracia

**Commit:** `c76e5dc989dcb03e5aa3b0620153e2d84ef763c4`

`Services/MoraService.cs`:

- `ProcesarMoraAsync` ahora, después de generar alertas de cobranza, evalúa `ConfiguracionMora`:
  - Si `ActualizarMoraAutomaticamente == false` → no muta nada (master switch).
  - Si `ActualizarMoraAutomaticamente == true` y `CambiarEstadoCuotaAuto == true` → llama `ICreditoService.ActualizarEstadoCuotasAsync()`.
  - Si `ActualizarMoraAutomaticamente == true` y `ImpactarScorePorMora == true` → llama al nuevo método privado `ImpactarScorePorMoraAsync(fechaLimite, ct)`.
- `ImpactarScorePorMoraAsync` calcula `fechaLimite = hoy - DiasGracia` (ver caller) y busca clientes con cuotas no pagadas/no canceladas cuya `FechaVencimiento` sea anterior a esa fecha límite (es decir, ya superaron los días de gracia). Para cada cliente único, llama `IClienteScoringService.RecalcularYAuditarAsync(clienteId, origen: "RecalculoAutomaticoMora", ...)`.
- `MoraService` recibió dos dependencias nuevas por constructor: `ICreditoService` y `IClienteScoringService`.
- `RecalcularYAuditarAsync` (reusado, sin duplicar fórmula) ya evita duplicar `ClientePuntajeHistorial` si el puntaje no cambia — mismo comportamiento validado en FASE 8B1.

Archivos: `Services/MoraService.cs`, `TheBuryProyect.Tests/Integration/CreditoServiceConsultasTests.cs`, `TheBuryProyect.Tests/Integration/MoraServiceTests.cs`.

Tests nuevos en `MoraServiceTests` cubren:

- `CambiarEstadoCuotaAuto == true` + `ActualizarMoraAutomaticamente == true` → llama `ActualizarEstadoCuotasAsync` (1 vez).
- `CambiarEstadoCuotaAuto == false` → no llama `ActualizarEstadoCuotasAsync`.
- `ActualizarMoraAutomaticamente == false` (aunque los otros dos flags estén en `true`) → no llama `ActualizarEstadoCuotasAsync` ni genera historial `RecalculoAutomaticoMora`.
- Mora dentro de `DiasGracia` → `ImpactarScorePorMoraAsync` no audita puntaje.
- Mora que superó `DiasGracia` con `ImpactarScorePorMora == true` → sí audita, origen `RecalculoAutomaticoMora`.

## FASE 10C-FIX — Contrato UI: alerta de mora

**Commit:** `ee4ed20e5be96c512f9e90178dec70708a1d685a`

`VentaCreateUiContractTests.CreateView_PanelAlertaMoraTieneRoleAlert` esperaba `role="alert"` dentro de los primeros 200 caracteres desde `id="panel-alerta-mora"` en `Views/Venta/Create_tw.cshtml`. El atributo estaba presente pero después del `class="..."` largo, fuera de la ventana de 200 caracteres que valida el test. Fix: reordenar atributos (`id` + `role="alert"` primero, `class` después) sin cambiar clases, ids ni comportamiento.

## FASE 10D — Contratos UI Venta/Create

**Commit:** `4b4382ce58c8ef0879233fef764f840adbeab70d`

Corrige contratos UI preexistentes de `Views/Venta/Create_tw.cshtml` detectados al validar `VentaCreateUiContractTests` completo:

- Agrega el texto de ayuda `"Selecciona el medio principal de cobro para toda la venta."` bajo el selector de tipo de pago principal (esperado por `CreateView_TieneSelectorTipoPagoGeneralVisible`).
- Reordena `role="alert"` en `panel-cupo-insuficiente` (mismo patrón que 10C-FIX) para que quede dentro de la ventana de 200 caracteres validada por `CreateView_PanelCupoInsuficienteTieneRoleAlert`.
- Corrige un cierre de `<div>` mal anidado en el aviso "Cargá una unidad física desde Gestionar unidades antes de vender" (el `</div>` cerraba antes de tiempo, dejando el markup roto).
- Colapsa el bloque de recordatorio pre-confirmación (`role="note"`) a una estructura de una sola línea de apertura, sin cambiar el texto ni el contenido, para que coincida con el patrón exacto validado por los tests de contrato.

No se tocó lógica de negocio, JS, ni backend — solo estructura/atributos HTML preexistentes de la vista.

## Regla final implementada

- `ConfiguracionMora.ActualizarMoraAutomaticamente == false` bloquea cualquier mutación automática del job de mora (ni cambio de estado de cuota, ni impacto en puntaje).
- `ConfiguracionMora.CambiarEstadoCuotaAuto == true` (con el master switch activo) permite que el job marque cuotas vencidas vía `CreditoService.ActualizarEstadoCuotasAsync`.
- `ConfiguracionMora.ImpactarScorePorMora == true` (con el master switch activo) permite recalcular y auditar `PuntajeCliente` por mora.
- Dentro de `ConfiguracionMora.DiasGracia`, no se impacta `PuntajeCliente` aunque la cuota esté vencida.
- Después de superar `DiasGracia`, el cliente entra al lote que `ImpactarScorePorMoraAsync` recalcula.
- Si el recálculo no cambia el puntaje, `RecalcularYAuditarAsync` no duplica `ClientePuntajeHistorial` (mismo contrato que `RecalculoAutomaticoPago`/`RecalculoManual`).
- La aptitud de venta (`ClienteAptitudService`) sigue gobernada por sus propios umbrales (`ConfiguracionCredito.DiasParaRequerirAutorizacion`/`DiasParaNoApto`), independientes de `ConfiguracionMora.DiasGracia`. FASE 10 no tocó esa lógica.

## Servicios involucrados

- `MoraService` — orquesta el job diario: genera alertas, y desde FASE 10C aplica cambio de estado de cuota + impacto en score según `ConfiguracionMora`.
- `MoraBackgroundService` — dispara `MoraService.ProcesarMoraAsync` según `ConfiguracionMora.HoraEjecucionDiaria`/`ProcesoAutomaticoActivo` (sin cambios en FASE 10).
- `CreditoService.ActualizarEstadoCuotasAsync` — ahora tiene caller productivo (antes era código sin invocar).
- `ClienteScoringService.RecalcularYAuditarAsync` — reusado sin duplicar fórmula; origen nuevo `RecalculoAutomaticoMora`.
- `ClienteAptitudService` — no modificado; sus umbrales de mora (`DiasParaRequerirAutorizacion`/`DiasParaNoApto`) son independientes y ya funcionaban antes de FASE 10.

## Validaciones

- Build principal y build de tests: OK, 0 errores / 0 advertencias (por sub-fase, ver commits).
- Tests focalizados FASE 10C: `MoraServiceTests` (incl. casos nuevos de días de gracia y flags) y `CreditoServiceConsultasTests` en verde.
- Tests focalizados FASE 10C-FIX / 10D: `VentaCreateUiContractTests` en verde (contratos de `Venta/Create` restaurados, incluyendo `CreateView_PanelAlertaMoraTieneRoleAlert` y `CreateView_PanelCupoInsuficienteTieneRoleAlert`).
- FASE 10E (este cierre): `git diff --check` OK; build principal y build de tests OK, 0 errores / 0 advertencias. Sin tests focalizados porque el cambio es docs-only.

## Commits FASE 10

| Sub-fase | Commit | Descripción |
|---|---|---|
| 10A | Sin commit propio | Diagnóstico mora/scoring |
| 10B | Sin commit propio | Decisión de Javo: opción B |
| 10C | `c76e5dc` | feat(credito): aplicar mora con dias de gracia |
| 10C-FIX | `ee4ed20` | fix(ui): agregar rol alert a alerta de mora en venta |
| 10D | `4b4382c` | fix(ui): cerrar contratos de creacion de venta |
| 10E | Este documento | Cierre documental de FASE 10 |

## Deuda remanente

- `ImpactarScorePorMora` (`ConfiguracionMora`) no tiene exposición en ninguna pantalla de configuración todavía; solo se puede activar por datos/migración/seed. Si Javo quiere habilitarlo en producción, hace falta UI de configuración en una fase futura.
- BCRA no determinístico sigue pendiente de diagnóstico aparte (no abordado en FASE 10).
- Entidad/tabla histórica `EvaluacionCredito`/`EvaluacionesCredito` sigue pendiente de decisión futura (conservar datos o dropear en migración separada) — no se tocó en FASE 10.

---

## Estado de cierre documental

- Archivo documental creado: `docs/credito-fase-10-mora-scoring.md`.
- Archivo documental actualizado: `docs/credito-flujo-final.md`.
- No se modificó código productivo.
- No se modificaron tests.
- No se modificó UI.
- No se tocaron stashes.
- No se hizo push sin confirmación.
- No se avanzó a FASE 11.
