# Crédito FASE 12 — Configuración de mora (exposición UI de score por mora)

> Estado: **FASE 12 CERRADA funcionalmente (12A diagnóstico + 12B implementación + 12B-QA) + cierre documental (12C)**. main == origin/main al iniciar 12C. Fecha documental: 2026-07-03.
> Documentos hermanos: [`credito-flujo-final.md`](credito-flujo-final.md) (consolidado), [`credito-fase-10-mora-scoring.md`](credito-fase-10-mora-scoring.md), [`credito-fase-11-bcra-deterministico.md`](credito-fase-11-bcra-deterministico.md).

## Objetivo de FASE 12

Cerrar la deuda explícita dejada en FASE 10: `ImpactarScorePorMora` (y el resto de la configuración de score por mora de `ConfiguracionMora`) existía en la entidad, el backend y la DB, pero solo se podía activar por datos/migración/seed — no había ninguna pantalla de configuración que lo expusiera. FASE 12 lo expone en la pantalla canónica de configuración de mora, sin cambiar reglas de negocio de scoring ni de mora (definidas en FASE 6/10).

## FASE 12A — Diagnóstico

Confirmado (sin tocar código, sin commit propio):

- `ImpactarScorePorMora` existía en la entidad `ConfiguracionMora`, tenía efecto real en el backend desde FASE 10C (gate del recálculo/auditoría de `PuntajeCliente` por mora, origen `RecalculoAutomaticoMora`) y estaba persistido en DB, pero **no** estaba expuesto en ningún ViewModel de configuración ni en UI.
- El módulo de mora no tenía una vista Razor activa de configuración expandida: el `MoraController` no servía una pantalla que cubriera la región de score.
- Existía un `ConfiguracionMoraController` que respondía JSON (API), no una pantalla Razor de edición canónica.
- Se decidió el camino canónico antes de implementar (ver decisión técnica).

## Decisión técnica

- **Camino canónico:** exponer la configuración por la acción Razor `MoraController.ConfiguracionExpandida` (GET) + `GuardarConfiguracionExpandida` (POST), ambas gobernadas por `[PermisoRequerido(Modulo = "mora", Accion = "config")]`, con vista `Views/Mora/ConfiguracionExpandida.cshtml` y ViewModel `ConfiguracionMoraExpandidaViewModel`.
- **No** se usó `ConfiguracionMoraController` (JSON) como camino principal: quedó como estaba, sin volverse el punto de edición de la UI.

## FASE 12B — Implementación

**Commit:** `cc0d802c3c5b3880cde7d33a713174f46007d3aa` — `feat(credito): exponer configuracion de score por mora`

- **Vista nueva `Views/Mora/ConfiguracionExpandida.cshtml`** — pantalla canónica de configuración de mora, incluye la región de score por mora.
- **Link en sidebar (`Views/Shared/_Layout.cshtml`)** — entrada discreta "Configuración de mora" (icono `tune`) dentro del grupo Sistemas, condicionada a `User.TienePermiso("mora", "config")` (`canConfigMora`); el grupo Sistemas ahora también se muestra si el usuario tiene ese permiso.
- **Región Score en el ViewModel (`ViewModels/Mora/ConfiguracionMoraExpandidaViewModel.cs`, `#region Score por Mora`)** — expone:
  - `ImpactarScorePorMora` (`bool`) — master del impacto de score por mora.
  - `PuntosRestarPorCuotaVencida` (`int?`, rango 0-1000).
  - `PuntosRestarPorDiaMora` (`decimal?`, rango 0-1000).
  - `PuntosMaximosARestar` (`int?`, rango 0-1000).
  - `RecuperarScoreAlPagar` (`bool`).
  - `PorcentajeRecuperacionScore` (`decimal?`, rango 0-100).
- **Mapping AutoMapper (`Helpers/AutoMapperProfile.cs`)** — se agregó el mapeo de lectura `CreateMap<ConfiguracionMora, ConfiguracionMoraExpandidaViewModel>()`; el GET de la pantalla expandida quedaba sin type map. Los campos de Tramos no existen en la entidad y quedan en default.
- **Persistencia (`Services/MoraService.cs`, `UpdateConfiguracionExpandidaAsync`)** — persiste los campos de score de la región (además del resto de la configuración expandida).
- **Tests agregados:**
  - `TheBuryProyect.Tests/Integration/MoraServiceTests.cs` — servicio (persistencia de la región score).
  - `TheBuryProyect.Tests/Unit/MappingProfileTests.cs` — mapping `ConfiguracionMora → ConfiguracionMoraExpandidaViewModel`.
  - `TheBuryProyect.Tests/Unit/ConfiguracionMoraUiContractTests.cs` (nuevo) — contrato de UI de la vista.

## FASE 12B-QA

QA visual y funcional con Playwright sobre la app viva:

- QA desktop 1440x900: OK.
- QA mobile 390x844: OK.
- Navegación desde el sidebar hacia la pantalla: OK.
- Permiso `mora:config` verificado: solo el rol con el permiso ve el link y accede (login de QA: SuperAdmin `admin` / `Admin123!`).
- Guardado real: OK — `ImpactarScorePorMora` y la región score persisten en DB.
- Consola del navegador: sin errores.

## Bug corregido (decimales `step="any"`)

**Commit:** `c6a6dddec22d5a101e7d6963d5fcc0c2cf0be70d` — `fix(mora): permitir guardar configuracion con decimales (step any)`

- **Síntoma:** el botón Guardar no persistía nada; el submit se bloqueaba en silencio para valores decimales.
- **Causa raíz:** los inputs decimales usaban `step="0.01"`. jQuery Validate aplica la regla HTML5 `step` y rechaza valores como `5.0000` ("Please enter a multiple of 0.01") por imprecisión de punto flotante (`5 % 0.01 != 0` en JS), abortando el submit.
- **Fix:** `step="0.01"` → `step="any"` en los tres inputs decimales de la vista: `TasaMoraBase`, `PuntosRestarPorDiaMora` y `PorcentajeRecuperacionScore` (`Views/Mora/ConfiguracionExpandida.cshtml`, 3 líneas).
- **Verificación e2e:** con el fix el form valida, guarda e `ImpactarScorePorMora` persiste en DB.

## Commits FASE 12

| Sub-fase | Commit | Descripción |
|---|---|---|
| 12A | Sin commit propio | Diagnóstico: score por mora sin exposición UI; elección de camino canónico |
| 12B | `cc0d802` | feat(credito): exponer configuracion de score por mora |
| 12B-fix | `c6a6ddd` | fix(mora): permitir guardar configuracion con decimales (step any) |
| 12C | Este documento | Cierre documental de FASE 12 |

## Validaciones

- FASE 12B: build principal y build de tests OK, 0 errores / 0 advertencias. Tests focalizados **141/141 OK** (servicio + mapping + contrato de UI).
- FASE 12B-QA: QA visual desktop 1440x900 y mobile 390x844 OK; navegación y permiso `mora:config` verificados; guardado persistido en DB; consola sin errores.
- FASE 12C (este cierre): `git diff --check` OK; build principal y build de tests OK, 0 errores / 0 advertencias. Sin tests focalizados porque el cambio es docs-only.

## Deuda remanente

- Permisos de rol para `mora:config`: al momento del QA solo el SuperAdmin (`admin`) tiene el permiso sembrado. Si se quiere que otros roles accedan a la configuración de mora, hay que sembrar el permiso `mora:config` para esos roles.
- Barra roja vacía del validation-summary: si el resumen de validación se muestra vacío en algún form, queda como deuda global de UI (no específica de esta pantalla).
- Modo simulación BCRA (para QA/desarrollo sin depender de la API pública real): deuda heredada de FASE 11, no tocada en FASE 12.
- Entidad/tabla histórica `EvaluacionCredito`/`EvaluacionesCredito`: sigue pendiente de decisión futura (conservar datos o dropear en migración separada) — no se tocó en FASE 12.

---

## Estado de cierre documental

- Archivo documental creado: `docs/credito-fase-12-configuracion-mora.md`.
- Archivo documental actualizado: `docs/credito-flujo-final.md`.
- No se modificó código productivo.
- No se modificaron tests.
- No se modificó UI.
- No se crearon migraciones.
- No se tocó BCRA, garante ni autorización manual.
- No se tocaron stashes.
- No se hizo push sin confirmación.
- No se avanzó a FASE 13.
