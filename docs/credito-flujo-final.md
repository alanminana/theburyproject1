# Crédito — Flujo final consolidado (FASE 1-11D)

> Estado: **FASE 11E — cierre documental de BCRA determinístico**. Consolida FASE 1 a FASE 11D. Sin push salvo confirmación explícita. Fecha documental: 2026-07-03.
> Documentos hermanos: [`credito-fase-1-cierre.md`](credito-fase-1-cierre.md), [`credito-fase-2-garante.md`](credito-fase-2-garante.md), [`credito-fase-3-cuenta-disponible.md`](credito-fase-3-cuenta-disponible.md), [`credito-fase-4-evaluador-unificado.md`](credito-fase-4-evaluador-unificado.md), [`credito-fase-5-autorizacion-manual.md`](credito-fase-5-autorizacion-manual.md), [`credito-fase-6-mora-puntaje.md`](credito-fase-6-mora-puntaje.md), [`credito-fase-7-ui-perfil-crediticio.md`](credito-fase-7-ui-perfil-crediticio.md), [`credito-fase-9-limpieza-legacy.md`](credito-fase-9-limpieza-legacy.md), [`credito-fase-10-mora-scoring.md`](credito-fase-10-mora-scoring.md), [`credito-fase-11-bcra-deterministico.md`](credito-fase-11-bcra-deterministico.md).

## 1. Objetivo del documento

Dar una referencia única y actualizada del flujo de crédito tal como quedó después de FASE 1-10D, para que cualquier agente (o Javo) pueda entender el camino canónico sin recorrer los documentos de fase previos. No reemplaza esos documentos — los consolida.

## 2. Estado final del flujo crédito

El eje único de aptitud/cupo es `PuntajeCliente` (0-5), gobernado por `ClienteScoringService`/`ClienteScoringCalculator`, con override manual opcional por cliente. La aptitud para vender a crédito combina documentación, cupo, mora y BCRA/Veraz en `ClienteAptitudService`. La venta a crédito pasa siempre por `ValidacionVentaService` → `VentaService`, con autorización manual puntual cuando corresponde. El flujo legado de servicio/solicitud (`EvaluacionCreditoService`, `IEvaluacionCreditoService`, `CreditoService.SolicitarCreditoAsync`, `SolicitudCreditoViewModel`) fue eliminado en FASE 9C/9D; solo queda pendiente una decisión separada sobre la entidad/tabla `EvaluacionCredito`/`EvaluacionesCredito` y migraciones históricas.

## 3. Mapa funcional

1. **`Cliente/Details`** — ficha del cliente: puntaje protagonista, cupo, mora, historial de puntaje, autorizaciones pendientes, BCRA, documentación, garante, créditos del cliente (ver sección 6).
2. **Venta a crédito** — se origina en `Venta/Create` (o flujo de cotización → venta). `ValidacionVentaService.PrevalidarAsync`/`ValidarVentaCreditoPersonalAsync` corre antes de confirmar. Los contratos UI de `Venta/Create` (`role="alert"` en paneles de mora/cupo insuficiente, texto de ayuda del selector de tipo de pago, estructura HTML del aviso de unidades) están cubiertos por `VentaCreateUiContractTests` y fueron corregidos en FASE 10D (commit `4b4382c`) tras la regresión de contrato detectada en FASE 10C-FIX (commit `ee4ed20`).
3. **Evaluación de aptitud** — `ClienteAptitudService.EvaluarAptitudAsync`/`EvaluarAptitudSinGuardarAsync` combina documentación, cupo, mora y BCRA en un único `EstadoCrediticioCliente` (`Apto` / `RequiereAutorizacion` / `NoApto` / `NoEvaluado`).
4. **Cupo/disponible** — `CreditoDisponibleService.CalcularDisponibleAsync` resuelve `Limite - SaldoVigente` según `PuntajeCliente` (o nivel manual) contra la tabla `PuntajesCreditoLimite`.
5. **Autorización manual** — si la venta requiere autorización, queda `PendienteAutorizacion` y un segundo usuario la autoriza/rechaza puntualmente (`VentaService.SolicitarAutorizacionAsync`/`AutorizarAsync`).
6. **Configuración del crédito** — `Credito` pasa por estados (`PendienteConfiguracion` → `Configurado` → `Activo`) donde se definen cuotas/plan.
7. **Confirmación de venta** — `VentaService.ConfirmarVentaAsync` exige `EstadoAutorizacion == Autorizada` si `RequiereAutorizacion == true`.
8. **Pago de cuotas** — `CreditoService.PagarCuotaAsync` (individual o múltiple) actualiza cuota/saldo/caja y dispara recálculo de puntaje en la misma transacción.
9. **Recálculo de puntaje** — automático al pagar cuota (`ClienteScoringService.RecalcularYAuditarAsync`, origen `RecalculoAutomaticoPago`) o manual desde `Cliente/Details` (origen `RecalculoManual`, auditado desde FASE 8B1).
10. **Mora/cobranza** — `MoraService`/`MoraBackgroundService` detectan cuotas vencidas por fecha y generan `AlertaCobranza`. Desde FASE 10C, el job diario también puede cambiar el estado de cuota (`CreditoService.ActualizarEstadoCuotasAsync`) y recalcular/auditar `PuntajeCliente` por mora (`ClienteScoringService.RecalcularYAuditarAsync`, origen `RecalculoAutomaticoMora`), siempre gobernado por los flags de `ConfiguracionMora` (ver sección 4).

## 4. Reglas finales

- `PuntajeCliente` (0-5) es el eje único que gobierna cupo; no hay un segundo scoring paralelo.
- Puntaje 0 (cliente nuevo) tiene cupo default **200.000** (seed `PuntajesCreditoLimite`, editable por Javo). Puntajes 1-5 quedan en 0 hasta que Javo los configure.
- Garante real validado: no puede ser el mismo cliente, debe existir y estar activo, tener al menos 1 compra propia, puntaje ≥ 4, y no garantizar más de 3 clientes a la vez (`GaranteService.ValidarGaranteAsync`). Un garante válido puede sustituir el recibo de sueldo como requisito documental.
- BCRA/Veraz es obligatorio: sin CUIL/CUIT o sin ninguna consulta registrada, el cliente queda `NoApto`. Desde FASE 11C, la aptitud distingue **último intento** de **última consulta exitosa**: si existe una consulta exitosa previa, la clasificación se basa en ella aunque el último intento haya fallado por un error transitorio (timeout, 429, 5xx, JSON inválido) — así una situación bloqueante (3/4/5) previa se mantiene `NoApto`, y una situación normal previa no degrada a `RequiereAutorizacion` solo porque el último intento falló. Último intento fallido **sin** ninguna consulta exitosa previa → `RequiereAutorizacion`. Situación 0-1 normal, 2 requiere revisión, ≥3 bloquea. El timeout de red reintenta 1 vez (2 intentos totales), igual que el error de red. Detalle en [`credito-fase-11-bcra-deterministico.md`](credito-fase-11-bcra-deterministico.md).
- Buen pagador antiguo (puntaje ≥4, antigüedad ≥90 días, ≥1 compra, créditos en término ≥1, sin atrasos, sin mora activa) con BCRA situación ≥3 no queda `NoApto` automático: degrada a `RequiereAutorizacion` (FASE 4D).
- La autorización manual es puntual por venta: no modifica cupo, puntaje ni límite futuro, y no autoriza otras ventas. Queda auditada con usuario, motivo y fecha.
- Recálculo automático de `PuntajeCliente` ocurre al pagar cuota (dentro de la misma transacción del pago) y, desde FASE 10C, opcionalmente por mora diaria (ver regla siguiente).
- Recálculo manual de puntaje desde `Cliente/Details` queda auditado en `ClientePuntajeHistorial` con origen `RecalculoManual` (FASE 8B1, antes no se auditaba).
- Mora diaria (`MoraService`/`MoraBackgroundService`, FASE 10C, opción B): la mora **no** baja el puntaje inmediatamente al vencer la cuota. Dentro de `ConfiguracionMora.DiasGracia` no se impacta el puntaje; recién después de superar los días de gracia se puede recalcular. Todo queda gobernado por tres flags de `ConfiguracionMora`: `ActualizarMoraAutomaticamente` (master switch: si es `false`, el job no muta nada), `CambiarEstadoCuotaAuto` (habilita el llamado a `CreditoService.ActualizarEstadoCuotasAsync`) e `ImpactarScorePorMora` (habilita el recálculo/auditoría de `PuntajeCliente` por mora, origen `RecalculoAutomaticoMora`). Si el puntaje no cambia, `RecalcularYAuditarAsync` no duplica historial (mismo comportamiento que FASE 8B1).
- La aptitud (`ClienteAptitudService`) ya usaba umbrales de días de mora independientes del scoring: `ConfiguracionCredito.DiasParaRequerirAutorizacion` y `DiasParaNoApto`. FASE 10 no los modifica; conviven con `ConfiguracionMora.DiasGracia`, que aplica solo al impacto en `PuntajeCliente`.

## 5. Servicios canónicos

| Servicio | Responsabilidad |
|---|---|
| `ClienteAptitudService` | Evaluación de aptitud compuesta (documentación + cupo + mora + BCRA) → `EstadoCrediticioCliente`. |
| `CreditoDisponibleService` | Cálculo de límite/saldo/disponible según `PuntajeCliente` (o nivel manual) y presets `PuntajesCreditoLimite`. |
| `ClienteScoringService` / `ClienteScoringCalculator` | Cálculo y recálculo de `PuntajeCliente`, con auditoría opcional (`RecalcularYAuditarAsync`). |
| `GaranteService` | Validación, asignación y remoción de garante; búsqueda de candidatos. |
| `ValidacionVentaService` | Evaluación unificada de crédito para venta (prevalidación, validación de confirmación, resumen crediticio). |
| `VentaService` | Alta/edición/confirmación de venta; máquina de estados de autorización puntual. |
| `CreditoService` | Ciclo de vida del crédito (configuración, pago de cuotas, recálculo de puntaje por pago, `ActualizarEstadoCuotasAsync` desde FASE 10C). |
| `MoraService` / `MoraBackgroundService` | Detección de cuotas vencidas por fecha, generación de alertas de cobranza y, desde FASE 10C, cambio de estado de cuota + recálculo de puntaje por mora (gobernado por `ConfiguracionMora`). |

## 6. UI final Cliente/Details

- **Puntaje protagonista** — valor único (0-5) con chip de fuente Automático/Manual; detalle "Manual: X/5 • Automático: Y/5" solo si hay override (FASE 7C).
- **Cupo** — total/usado/disponible con barra de progreso y origen del cupo, sin cambios de cálculo (FASE 7).
- **Mora y umbrales** — chip de estado + monto en mora + mensaje contextual de umbral (autorización/NoApto) cuando hay mora activa (FASE 7D).
- **Historial de puntaje** — sección colapsable con tabla de cambios auditados (`ClientePuntajeHistorial`): fecha, puntaje anterior/nuevo, origen, registrado por, observación (FASE 7B).
- **Autorizaciones pendientes** — banner de alerta con últimas 5 ventas `PendienteAutorizacion` del cliente, con acceso condicionado al permiso `ventas.authorize` (FASE 7E).
- **BCRA** — panel lateral: situación crediticia, período informado, última consulta, botón actualizar. Desde FASE 11D, el chip de cabecera (`id="bcra-chip"`) distingue 4 estados (`Sin CUIL` / `Sin consultar` / `Consulta OK` / `Usando ultima consulta valida`, más `Error BCRA` sin último éxito) en vez de colapsar todo error en "Pendiente"; cuando se usa la última consulta válida se muestra un aviso explícito. El refresh AJAX (`cliente-details.js`) sincroniza el mismo chip y reusa el clasificador puro `ClienteAptitudService.ConstruirBcraDetalle` vía el endpoint del controller, sin duplicar reglas.
- **Documentación** — panel lateral: completa/incompleta, pendientes.
- **Garante** — panel lateral: asignado/sin asignar, validez, modal de asignación.
- **Créditos del cliente** — tabla de créditos del cliente, overflow corregido en FASE 7F.

## 7. Auditoría

- **`ClientePuntajeHistorial`** — registra todo cambio efectivo de `PuntajeCliente` (no duplica si el puntaje no cambia).
- **Origen `RecalculoAutomaticoPago`** — recálculo disparado por `CreditoService.PagarCuotaAsync` dentro de la transacción de pago (FASE 6C).
- **Origen `RecalculoManual`** — recálculo disparado desde `Cliente/Details` por un usuario; antes de FASE 8B1 no quedaba auditado, ahora usa el mismo `RecalcularYAuditarAsync` (FASE 8B1, commit `23dc225`).
- **Autorización manual de venta** — `EstadoAutorizacionVenta` con `FechaSolicitudAutorizacion`, `FechaAutorizacion`, `MotivoAutorizacion` y usuario que autoriza; no modifica cupo ni puntaje (FASE 5).

## 8. Legacy eliminado

El flujo fue marcado explícitamente como no productivo en FASE 8B3 (commit `eaadfda`) y luego limpiado en FASE 9:

- `EvaluacionCreditoService` eliminado en FASE 9C (commit `2c0030a`).
- `IEvaluacionCreditoService` eliminado en FASE 9C (commit `2c0030a`).
- `EvaluacionCreditoService.EvaluarSolicitudAsync` eliminado junto con el servicio en FASE 9C.
- `CreditoService.SolicitarCreditoAsync` eliminado en FASE 9D (commit `c91fc0a`).
- `SolicitudCreditoViewModel` eliminado en FASE 9D (commit `c91fc0a`).
- Tests legacy asociados eliminados en FASE 9C/9D.

El flujo canónico para cualquier validación nueva sigue siendo `VentaService` / `ValidacionVentaService` / `ClienteAptitudService`.

## 9. Deuda pendiente

- **Resuelto en FASE 10C**: `ActualizarEstadoCuotasAsync` ahora tiene caller productivo (`MoraService.ProcesarMoraAsync`, gated por `ConfiguracionMora.CambiarEstadoCuotaAuto`), y `ConfiguracionMora.CambiarEstadoCuotaAuto`/`ActualizarMoraAutomaticamente`/`ImpactarScorePorMora` ya tienen efecto real. Decisión de Javo (opción B) resuelta: la mora no baja el puntaje hasta superar `DiasGracia`. Detalle completo en [`credito-fase-10-mora-scoring.md`](credito-fase-10-mora-scoring.md).
- `ImpactarScorePorMora` (`ConfiguracionMora`) todavía no tiene exposición en UI de configuración — solo se puede activar por datos/migración/seed, no desde una pantalla. Deuda de FASE 10.
- **Resuelto en FASE 11C/11D**: BCRA/aptitud no determinístico. Un error transitorio ya no pisa la última situación BCRA válida conocida (separación último intento / último éxito, migración aditiva) y la UI de `Cliente/Details` sincroniza el mismo clasificador puro en carga inicial y refresh AJAX. Detalle completo en [`credito-fase-11-bcra-deterministico.md`](credito-fase-11-bcra-deterministico.md).
- Modo simulación BCRA (para QA/desarrollo sin depender de la API pública real) queda pendiente si Javo lo pide en una fase futura. Deuda de FASE 11.
- Entidad `EvaluacionCredito` y tabla `EvaluacionesCredito`: siguen existiendo; no se tocaron en FASE 9, FASE 10 ni FASE 11.
- Migraciones históricas de `EvaluacionCredito`/`EvaluacionesCredito`: siguen existiendo; no se modificaron ni se creó una migración de drop.
- Decisión futura: revisar datos históricos reales antes de decidir si conservar datos o dropear tabla en una fase separada.
- Optimización futura: la consulta de ventas pendientes de autorización en `ClienteController` trae y filtra en memoria (top 5) — sin impacto medido todavía.

## 10. Validaciones finales realizadas

- FASE 8D:
  - Build: no se ejecutó en ese lote (solo se corrieron tests focalizados con `--no-build` sobre binarios existentes).
  - Tests focalizados:
    ```
    dotnet test TheBuryProyect.Tests/TheBuryProyect.Tests.csproj --filter "FullyQualifiedName~ClienteAptitudServiceTests|FullyQualifiedName~ClienteScoringServiceTests|FullyQualifiedName~CreditoServicePuntajeClienteRecalculoTests|FullyQualifiedName~VentaServiceAutorizacionTests|FullyQualifiedName~VentaServiceCancelarCreditoLiberaCupoTests|FullyQualifiedName~GaranteServiceTests|FullyQualifiedName~CreditoDisponibleServiceLimitesTests" --no-build
    ```
    Resultado: **153/153 OK**, 0 fallos, 18s.
- FASE 9E:
  - `git diff --check`: OK.
  - `dotnet build TheBuryProyect.csproj --no-restore`: OK, 0 errores / 0 advertencias.
  - `dotnet build TheBuryProyect.Tests/TheBuryProyect.Tests.csproj --no-restore`: OK, 0 errores / 0 advertencias.
  - Tests focalizados: no se repiten en FASE 9E porque el cambio es docs-only.
- QA visual: no se repitió en FASE 9E; no hubo cambios UI.
- FASE 10C: `MoraServiceTests` (incl. nuevos casos de días de gracia/flags) y `CreditoServiceConsultasTests` en verde. Detalle en [`credito-fase-10-mora-scoring.md`](credito-fase-10-mora-scoring.md).
- FASE 10C-FIX / 10D: `VentaCreateUiContractTests` en verde (contratos de `Venta/Create` restaurados).
- FASE 10E:
  - `git diff --check`: OK.
  - `dotnet build TheBuryProyect.csproj --no-restore`: OK, 0 errores / 0 advertencias.
  - `dotnet build TheBuryProyect.Tests/TheBuryProyect.Tests.csproj --no-restore`: OK, 0 errores / 0 advertencias.
  - Tests focalizados: no se repiten en FASE 10E porque el cambio es docs-only.
- FASE 11C: build principal y build de tests OK, 0 errores / 0 advertencias. Tests focalizados **119/119 OK** (`ClienteAptitudServiceTests` + `SituacionCrediticiaBcraServiceTests`). Detalle en [`credito-fase-11-bcra-deterministico.md`](credito-fase-11-bcra-deterministico.md).
- FASE 11D: build principal y build de tests OK, 0 errores / 0 advertencias. Tests focalizados **400/400 OK** (incluye `ClienteDetailsBcraUiContractTests` nuevo).
- FASE 11E:
  - `git diff --check`: OK.
  - `dotnet build TheBuryProyect.csproj --no-restore`: OK, 0 errores / 0 advertencias.
  - `dotnet build TheBuryProyect.Tests/TheBuryProyect.Tests.csproj --no-restore`: OK, 0 errores / 0 advertencias.
  - Tests focalizados: no se repiten en FASE 11E porque el cambio es docs-only.

## 11. Tabla de commits relevantes FASE 6-10

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
| 9B | `4873fb3` | Quitar evaluación legacy de `Credito/Details` |
| 9C | `2c0030a` | Eliminar `EvaluacionCreditoService` / `IEvaluacionCreditoService` |
| 9D | `c91fc0a` | Eliminar `CreditoService.SolicitarCreditoAsync` / `SolicitudCreditoViewModel` |
| 9E | Este documento | Cierre documental de limpieza legacy de crédito |
| 10A | Sin commit propio | Diagnóstico mora/scoring (mora 100% dinámica, sin caller productivo) |
| 10B | Sin commit propio | Decisión de Javo: opción B (mora no baja puntaje hasta superar días de gracia) |
| 10C | `c76e5dc` | Aplicar mora con días de gracia (`MoraService` conecta `ActualizarEstadoCuotasAsync` + `RecalcularYAuditarAsync` origen `RecalculoAutomaticoMora`) |
| 10C-FIX | `ee4ed20` | Agregar rol alert a alerta de mora en venta |
| 10D | `4b4382c` | Cerrar contratos de creación de venta (`VentaCreateUiContractTests`) |
| 10E | Este documento + `docs/credito-fase-10-mora-scoring.md` | Cierre documental de FASE 10 (mora, scoring, contratos UI) |
| 11A | Sin commit propio | Diagnóstico BCRA no determinístico |
| 11B | Sin commit propio | Decisión: conservar último éxito ante error transitorio |
| 11C | `ee3ea96` | Conservar último éxito BCRA ante errores (migración aditiva + `ConstruirBcraDetalle`) |
| 11D | `c1a541c` | Sincronizar estado BCRA en detalle de cliente (chip + refresh AJAX) |
| 11E | Este documento + `docs/credito-fase-11-bcra-deterministico.md` | Cierre documental de FASE 11 (BCRA determinístico) |

## 12. Próximo paso

Decisión separada sobre la entidad/tabla `EvaluacionCredito`/`EvaluacionesCredito`: revisar datos históricos reales y decidir si se conserva o se elimina con migración futura. No forma parte de FASE 9E/10E/11E. Exposición UI de `ImpactarScorePorMora` y modo simulación BCRA quedan como deuda abierta para una fase futura si Javo las pide.

---

## Estado de cierre documental

- Archivo documental actualizado: `docs/credito-flujo-final.md`.
- Archivo documental creado (FASE 9E): `docs/credito-fase-9-limpieza-legacy.md`.
- Archivo documental creado (FASE 10E): `docs/credito-fase-10-mora-scoring.md`.
- Archivo documental creado (FASE 11E): `docs/credito-fase-11-bcra-deterministico.md`.
- No se modificó código productivo.
- No se modificaron tests.
- No se modificó UI.
- No se tocaron stashes.
- No se hizo push sin confirmación.
- No se avanzó a FASE 12.
