# Credito - FASE 9 limpieza legacy

> Estado: **FASE 9E - cierre documental**. Fecha documental: 2026-07-03. Scope docs-only.

## Objetivo FASE 9

Cerrar la limpieza del flujo legacy de evaluacion crediticia que habia quedado marcado como no productivo despues de consolidar el camino canonico de credito.

El camino canonico sigue siendo:

- `VentaService`
- `ValidacionVentaService`
- `ClienteAptitudService`
- `CreditoDisponibleService`
- `ClienteScoringService` / `ClienteScoringCalculator`

## Secuencia

### 9A - Diagnostico legacy

Se confirmo que el flujo legacy de evaluacion crediticia ya no debia ampliarse y que la limpieza debia hacerse por micro-lotes, separando corte UI, servicio legacy, solicitud legacy y cierre documental.

### 9B - Corte del ultimo hilo productivo

Commit `4873fb3` - `refactor(credito): quitar evaluacion legacy de detalle de credito`.

- Se corto el uso productivo remanente en `Credito/Details`.
- Se dejo el detalle de credito alineado con el flujo canonico.
- No se tocaron entidad `EvaluacionCredito`, tabla `EvaluacionesCredito` ni migraciones.

### 9C - Eliminacion del servicio legacy

Commit `2c0030a` - `refactor(credito): eliminar servicio legacy de evaluacion crediticia`.

- Se elimino `Services/EvaluacionCreditoService.cs`.
- Se elimino `Services/Interfaces/IEvaluacionCreditoService.cs`.
- Se eliminaron tests legacy asociados a `EvaluacionCreditoService`.
- No se tocaron entidad `EvaluacionCredito`, tabla `EvaluacionesCredito` ni migraciones.

### 9D - Eliminacion de solicitud legacy

Commit `c91fc0a` - `refactor(credito): eliminar solicitud legacy de credito`.

- Se elimino `CreditoService.SolicitarCreditoAsync`.
- Se elimino el contrato correspondiente de `ICreditoService`.
- Se elimino `SolicitudCreditoViewModel`.
- Se eliminaron tests legacy asociados al flujo de solicitud legacy.
- No se tocaron entidad `EvaluacionCredito`, tabla `EvaluacionesCredito` ni migraciones.

## Que se elimino

- `EvaluacionCreditoService`.
- `IEvaluacionCreditoService`.
- `EvaluacionCreditoService.EvaluarSolicitudAsync`.
- `CreditoService.SolicitarCreditoAsync`.
- `SolicitudCreditoViewModel`.
- Tests legacy asociados al servicio legacy y a la solicitud legacy.

## Que no se elimino

- Entidad `EvaluacionCredito`.
- Tabla `EvaluacionesCredito`.
- Migraciones historicas relacionadas con `EvaluacionCredito` / `EvaluacionesCredito`.

La decision sobre conservar datos historicos o dropear la tabla queda fuera de FASE 9 y requiere una fase separada con revision de datos reales.

## Validaciones realizadas

- FASE 9E `git diff --check`: OK.
- FASE 9E `dotnet build TheBuryProyect.csproj --no-restore`: OK, 0 errores / 0 advertencias.
- FASE 9E `dotnet build TheBuryProyect.Tests/TheBuryProyect.Tests.csproj --no-restore`: OK, 0 errores / 0 advertencias.
- Tests focalizados: no se repiten en FASE 9E porque el cambio es docs-only.

## Deuda remanente

- Revisar datos historicos reales de `EvaluacionesCredito` antes de decidir si se conserva o se elimina la tabla.
- Mora/scoring: definir si `DiasGracia` impacta solo cobranza o tambien recalculo automatico de puntaje por mora.
- BCRA/aptitud no deterministico: diagnostico pendiente fuera de FASE 9.

## Commits FASE 9

| Fase | Commit | Descripcion |
|---|---|---|
| 9B | `4873fb3` | refactor(credito): quitar evaluacion legacy de detalle de credito |
| 9C | `2c0030a` | refactor(credito): eliminar servicio legacy de evaluacion crediticia |
| 9D | `c91fc0a` | refactor(credito): eliminar solicitud legacy de credito |

## Cierre FASE 9E

- Scope: documentacion.
- No se modifico codigo productivo.
- No se modificaron tests.
- No se modifico UI.
- No se toco entidad `EvaluacionCredito`.
- No se toco tabla `EvaluacionesCredito`.
- No se crearon migraciones.
- No se hizo push.
