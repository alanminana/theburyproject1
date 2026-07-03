# Credito FASE 7 - UI del perfil crediticio en Cliente/Details

> Estado: **FASE 7 CERRADA**. Commits `184d980`..`4f2f365` en `main`, `ahead 2` de `origin/main` junto con un commit no relacionado de CUIL/CUIT (`bb2c9c4`). Fecha documental: 2026-07-02.
> Documentos hermanos: [`credito-fase-5-autorizacion-manual.md`](credito-fase-5-autorizacion-manual.md), [`credito-fase-6-mora-puntaje.md`](credito-fase-6-mora-puntaje.md).

## Objetivo de FASE 7

Consolidar en `Cliente/Details` la información crediticia dispersa entre FASE 5 (autorización manual) y FASE 6 (mora/puntaje), sin tocar reglas de negocio: un único puntaje protagonista, historial de puntaje, umbrales de mora y autorizaciones pendientes visibles desde la ficha del cliente.

Reglas de alcance:

- No modificar `ClienteScoringService` / `ClienteAptitudService` (salvo exponer datos ya calculados).
- No modificar `MoraService` / `MoraBackgroundService`.
- No modificar reglas de BCRA, garante ni autorización manual.
- No cambiar el cálculo de cupo.

---

## Resumen por lote

### 7A - Diagnóstico UI

Diagnóstico previo (no commiteado por separado): la card "Calificación crediticia" de `Cliente/Details` repetía el mismo dato (`NivelCreditoAutomatico` = `PuntajeCliente` vía `CreditoDisponibleService`) en 3-4 lugares distintos como "Puntaje automático" y "Comportamiento", sin historial visible, sin umbrales de mora y sin visibilidad de ventas pendientes de autorización. Definió el alcance de 7B-7F.

### 7B - Historial de puntaje

**Commit:** `184d980`

- `ClienteService.GetHistorialPuntajeAsync` trae los últimos N registros de `ClientePuntajeHistorial`, derivando `PuntajeAnterior` del registro cronológicamente previo (no es columna persistida).
- Nuevo `ClientePuntajeHistorialItemViewModel` y `ClienteDetalleViewModel.HistorialPuntaje`.
- `ClienteController` lo carga en un `try/catch` con log de warning (no bloquea la ficha si falla).
- Sección colapsable "Historial de puntaje" en `Details_tw.cshtml` con tabla (Fecha, Puntaje anterior, Puntaje nuevo, Origen, Registrado por, Observación) o placeholder si está vacía.

### 7C - Puntaje protagonista

**Commit:** `dd76473`

- Consolidación visual: "Puntaje automático" y "Comportamiento" eran el mismo dato mostrado varias veces.
- Ahora se muestra un único "Puntaje crediticio" con chip de fuente (Automático/Manual) y, solo si hay override manual, el detalle "Manual: X/5 • Automático calculado: Y/5".
- Sin cambios de backend ni de cálculo.

### 7D - Umbrales de mora

**Commit:** `46f309d`

- `ClienteAptitudService.EvaluarMoraInternaAsync` expone `DiasParaRequerirAutorizacion` y `DiasParaNoApto` (ya existían en `ConfiguracionCredito`) en `AptitudMoraDetalle`.
- Nueva propiedad calculada `AptitudMoraDetalle.MensajeUmbralDias`: texto contextual según si la mora activa supera el umbral de autorización o de NoApto; `null` si no hay mora (sin ruido visual).
- Mensaje mostrado bajo el desglose de la card de calificación crediticia.

### 7E - Autorizaciones pendientes

**Commit:** `7781c6d`

- `ClienteController` inyecta `IVentaService` y consulta ventas del cliente con `EstadoAutorizacion = PendienteAutorizacion`, filtradas por `RequiereAutorizacion`, últimas 5, en `try/catch` con log de warning.
- Nuevo `VentaPendienteAutorizacionItemViewModel` y `ClienteDetalleViewModel.VentasPendientesAutorizacion`.
- Banner de alerta arriba de la card principal con número, fecha, total y resumen de razones; botón "Ver autorización" si el usuario tiene permiso `ventas.authorize`, o "Ver venta" (solo lectura) si no.

### 7F - QA visual desktop/mobile

**Commits:** `a827f9a`, `4f2f365`

- Fix de solapamiento en la tabla de historial de puntaje: `table-layout: auto` + `overflow-wrap: anywhere` en `.puntaje-history-table` (columnas Origen/Registrado por sin espacios desbordaban bajo `table-layout: fixed`).
- Mismo patrón de fix aplicado a la tabla "Créditos del cliente" (`.creditos-cliente-table`).
- QA visual confirmó ambas tablas sin overflow horizontal ni solapamiento en desktop y mobile.

---

## Estado final de Cliente/Details

- **Puntaje crediticio:** valor único (0-5) con chip de fuente Automático/Manual; detalle Manual vs Automático solo si hay override.
- **Cupo total/usado/disponible:** `result-card` con barra de progreso y estado (ok/warn/bad) sin cambios de cálculo (usa `CreditoDisponibleService` existente).
- **Origen de cupo:** en el desglose (`kv-row` "Origen del cupo").
- **Mora y umbrales:** chip de estado de mora + monto en mora + `MensajeUmbralDias` contextual (autorización/NoApto) cuando hay mora activa.
- **Historial de puntaje:** sección colapsable con tabla de cambios auditados (`ClientePuntajeHistorial`), o placeholder vacío.
- **BCRA:** panel lateral sin cambios (situación crediticia, período informado, última consulta, botón actualizar).
- **Documentación:** panel lateral sin cambios (completa/incompleta, pendientes).
- **Garante:** panel lateral sin cambios (asignado/sin asignar, validez, modal de asignación).
- **Autorizaciones pendientes:** banner de alerta arriba de la card principal, condicional a que existan ventas pendientes.
- **Créditos del cliente:** tabla con overflow corregido (7F), sin cambios de datos.

---

## Reglas respetadas

- No se tocaron reglas de scoring (`ClienteScoringService`/`ClienteScoringCalculator` intactos).
- No se implementó scoring diario por mora (sigue siendo deuda de FASE 6D).
- No se tocó `MoraService`/`MoraBackgroundService`.
- No se tocó BCRA (`SituacionCrediticiaBcraService`).
- No se tocó garante (`GaranteService`).
- No se modificaron reglas de autorización manual (`EstadoAutorizacionVenta`, flujo de `Venta/Autorizar`).
- No se cambió el cálculo de cupo (`CreditoDisponibleService`).

---

## Validaciones realizadas

- Build Debug (`TheBuryProyect.csproj`): OK, 0 errores / 0 advertencias.
- Build Debug (`TheBuryProyect.Tests.csproj`): OK, 0 errores / 0 advertencias.
- Tests focalizados `ClienteServiceTests`: 89/89 OK (incluye casos de normalización de CUIL del commit `bb2c9c4`, no relacionado a FASE 7).
- QA visual desktop/mobile: OK (7F), sin overflow horizontal en tablas de historial de puntaje ni de créditos.
- Consola navegador: sin errores reportados durante 7F.
- Limpieza de procesos/datos QA: sin procesos de la app quedaron abiertos; solo se detectaron `dotnet.exe` de extensiones de VSCode (MSBuild language server, mssql tools), no de la app ni de MSBuild del proyecto.

---

## Deudas conocidas

- 4 tests preexistentes de `ClienteAptitudServiceTests` fallan por drift `Puntaje 0 = 200000` (fuera de scope de FASE 6 y FASE 7).
- `EstadoCuota.Vencida` sin caller productivo (deuda de FASE 6D).
- `ConfiguracionMora.CambiarEstadoCuotaAuto` / `ActualizarMoraAutomaticamente` sin efecto real (deuda de FASE 6D).
- Decisión pendiente: si `DiasGracia` debe aplicar al scoring o solo a cobranza (deuda de FASE 6D).
- Posible diagnóstico aparte de no-determinismo en BCRA/aptitud, no abordado en esta fase.
- Posible optimización futura de la consulta de ventas pendientes en `ClienteController` (hoy trae y filtra en memoria, top 5).
- No se tocaron stashes existentes.

---

## Tabla de commits de FASE 7

| Sub-fase | Commit | Descripción |
|---|---|---|
| 7A | Sin commit propio | Diagnóstico UI del perfil crediticio |
| 7B | `184d980` | Historial de puntaje visible en Cliente/Details |
| 7C | `dd76473` | Consolidar puntaje protagonista |
| 7D | `46f309d` | Umbrales de mora visibles |
| 7E | `7781c6d` | Autorizaciones pendientes visibles |
| 7F | `a827f9a`, `4f2f365` | QA visual: fix overflow historial y créditos |
| 7G | Este documento | Cierre documental de FASE 7 |

---

## Próxima fase recomendada

**FASE 8 — Auditoría final / cierre.**

---

## Estado de cierre documental

- Archivo documental creado: `docs/credito-fase-7-ui-perfil-crediticio.md`.
- No se modificó código productivo.
- No se modificaron tests.
- No se modificó UI.
- No se tocaron stashes.
- No se hizo push.
- No se hizo commit.
