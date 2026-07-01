# FASE 1 — Eje único de puntaje interno (0–5) que gobierna el cupo

> Cierre de FASE 1 del módulo de crédito. Estado: **COMPLETA y validada** (build 0/0, tests focalizados 308/308, QA en vivo verde sobre SQL Server LocalDB). Sin commitear.
> Contexto/plan general: `~/.claude/plans/hace-un-plan-completo-whimsical-fog.md`.

## Objetivo

Modelo funcional confirmado por el usuario: **un único puntaje interno de comportamiento (0–5)** que
sube/baja con los pagos **y gobierna el cupo de crédito**. Antes había dos ejes desacoplados:
`PuntajeCliente` (comportamiento, 1–5, no gobernaba el cupo) y `NivelRiesgo` (1–5, sí gobernaba el cupo).
`NivelRiesgo`/BCRA pasan a ser **filtro de aptitud**, no driver de cupo.

## Sub-lotes ejecutados

### 1a — Eje de scoring 0–5 con base 0
- `Models/Entities/ConfiguracionScoringCliente.cs`: `PuntajeBase` 1→0, `PuntajeMinimo` 1→0 (máx 5).
- Tests: `ClienteScoringCalculatorTests`, `ClienteScoringServiceTests` recalculados a base 0.
- Efecto: un cliente nuevo sin historial calcula puntaje 0.

### 1b — "Cliente nuevo arranca en 0" persistido
- `Models/Entities/Cliente.cs`: `PuntajeCliente` initializer 1→0.
- `Data/AppDbContext.cs`: `PuntajeCliente` → `HasDefaultValue(0).ValueGeneratedNever()` (persiste el 0 explícito; antes el default de BD = 1 pisaba el 0 por store-generated).
- Migración `20260701010417_AlterPuntajeClienteDefaultCero` (solo `AlterColumn` default 1→0; **no** re-basea datos existentes).
- Test `ClienteScoringServiceTests.ClienteNuevo_PersisteConPuntajeCero`.

### 1c — El cupo lo gobierna `PuntajeCliente` (re-key a puntaje int 0–5)
**Entidades / esquema:**
- `PuntajeCreditoLimite.Puntaje`: `NivelRiesgoCredito` (enum 1–5) → `int` (0–5).
- `ClienteCreditoConfiguracion.NivelCreditoManual`: `NivelRiesgoCredito?` → `int?` (0–5) — **nombre de columna conservado** (sin RenameColumn).
- Check constraints `CK_PuntajeCreditoLimites_Puntaje` y `CK_ClientesCreditoConfiguraciones_NivelCreditoManual`: `1..5` → `0..5`.
- Seed (`HasData`): filas puntaje 1–5 en int + **nueva fila puntaje 0 = $200.000** (Id 6) = cupo máximo del cliente nuevo (parametrizable, editable por Javo).
- Migración `20260701043907_AlterCupoPorPuntajeInterno` (drop+add constraints, `InsertData` puntaje 0). Down reversible.

**Servicios (backend = autoridad):**
- `CreditoDisponibleService`: el cupo automático se deriva de `cliente.PuntajeCliente` (antes `NivelRiesgo`); preset por puntaje (`ObtenerPresetPorPuntajeAsync(int)`); `GuardarLimitesPorPuntajeAsync` valida exactamente puntajes 0–5; override manual (`NivelCreditoManual` int) intacto; overrides `LimiteOverride`/`ExcepcionDelta` intactos.
- `CreditoDisponibleResultado`: campos `NivelCredito*` pasan a `int` (puntaje 0–5).
- `ICreditoDisponibleService`: firmas a `int`.
- `VentaService`: el snapshot de cupo al momento de la venta usa `PuntajeCliente`; `venta.PuntajeAlMomento` guarda el puntaje 0–5 que definió el cupo.
- `ClienteService.AsignarNivelCreditoManualAsync(int nivel, ...)`: override manual por puntaje; historial `Origen="PuntajeCreditoManual"`/`"PuntajeCreditoManualLimpio"`, `Puntaje`=puntaje manual, `NivelRiesgo`=snapshot del riesgo del cliente.

**Controller / ViewModels / Vistas:**
- `ClienteController`: `AsignarNivelCreditoManual(int nivelCreditoManual, ...)`, opciones y límites por `Enumerable.Range(0,6)`, `PuntajeActual` = `cliente.PuntajeCliente`.
- `ClienteDetalleViewModel` (`PuntajeActual`, `ClienteNivelCreditoOpcionViewModel.Nivel`), `ClienteCreditoLimitesViewModel` (`Puntaje`): `NivelRiesgoCredito`→`int`.
- `Views/Cliente/Details_tw.cshtml` y `_LimitesPorPuntajeModal_tw.cshtml`: textos "Nivel"→"Puntaje" (final/automático/manual, fuente, drawer "Asignar puntaje manual", modal "Límites por puntaje"). **IDs, `name`, `data-*` y `asp-action` conservados** para no romper JS/routing.

**Tests actualizados** (enum→int + seteo de `PuntajeCliente` como driver + puntajes 0–5): `CreditoDisponibleServiceTests`, `CreditoDisponibleServiceLimitesTests`, `ClienteServiceTests`, `VentaServiceCreditoPersonalTests`, y firmas de ~16 stubs de `ICreditoDisponibleService`/`IClienteService`.

## Deuda pre-existente resuelta en el camino
- **Regresión de HEAD (commit `4ca8ec7`)**: `ICajaService.ObtenerUltimoEfectivoCierreAsync` se agregó a la interfaz sin actualizar 18 stubs de test → el proyecto de tests **no compilaba de raíz**. Se implementó el método (devolviendo `null`) en los 18 stubs (fix test-only). Sin esto no se podía validar nada.

## Validación
- **Build**: main 0/0 (C# + vistas Razor); tests 0/0.
- **Tests focalizados**: 308/308 verdes (`CreditoDisponible*`, `ClienteScoring*`, `ClienteServiceTests`, `VentaServiceCreditoPersonal*`, `CreditoService*`).
- **Migraciones sobre SQL Server LocalDB real**: aplican desde cero; `PuntajeCreditoLimites` con filas 0–5 (puntaje 0 = $200.000); ambos check constraints `>=0 AND <=5`.
- **QA en vivo (Playwright, Cliente/Details)**: 0 errores de consola; cupo **$200.000** para cliente nuevo (puntaje 0); minimetrics "Puntaje final/automático/manual"; drawer "Asignar puntaje manual" con select **Puntaje 0 → $200.000, Puntaje 1–5 → $0**; modal "Límites por puntaje"; sin texto residual "Nivel".

## Paso de configuración requerido en despliegue (dato de Javo)
Las filas de cupo **puntaje 1–5 están en $0** por seed (como antes). Javo debe cargar los montos de cupo por
puntaje 1–5 en **Cliente/Details → "Límites por puntaje"**. Hasta entonces, un cliente con puntaje 1–5 tendrá
cupo $0 y solo el cliente nuevo (puntaje 0) tendrá el cupo $200.000. **No es un bug**: los valores de cupo son
dato de negocio configurable.

## Notas de rollout (sin riesgo de código)
- **Clientes existentes**: conservan su `PuntajeCliente` actual (1–5), que ahora mapea directo al cupo por puntaje.
  `ClienteScoringService.RecalcularAsync` los reajusta a la fórmula base-0 en cada venta/mora (self-healing). No se
  fuerza un recálculo masivo (no existe endpoint bulk; no es necesario para correctitud).
- El cupo pasó a depender del comportamiento (`PuntajeCliente`) en lugar del riesgo (`NivelRiesgo`): es el cambio
  buscado por el modelo funcional. `NivelRiesgo`/BCRA siguen como señales de aptitud (semáforo), no de cupo.

## Archivos tocados (scope crédito FASE 1)
Entidades: `Cliente.cs`, `ConfiguracionScoringCliente.cs`, `PuntajeCreditoLimite.cs`, `ClienteCreditoConfiguracion.cs`.
Data: `AppDbContext.cs` + migraciones `20260701010417_AlterPuntajeClienteDefaultCero`, `20260701043907_AlterCupoPorPuntajeInterno` (+ snapshot).
Services: `CreditoDisponibleService.cs`, `ICreditoDisponibleService.cs`, `CreditoDisponibleResultado.cs`, `VentaService.cs`, `ClienteService.cs`, `IClienteService.cs`.
Controllers: `ClienteController.cs`. ViewModels: `ClienteDetalleViewModel.cs`, `ClienteCreditoLimitesViewModel.cs`.
Vistas: `Details_tw.cshtml`, `_LimitesPorPuntajeModal_tw.cshtml`.
Tests: scoring + cupo + venta-crédito + 18 stubs de caja (regresión HEAD).

## Próximas fases (del plan)
FASE 2 garante validado · FASE 3 verificación cuenta de crédito · FASE 4 evaluador unificado + requisitos/Veraz-override ·
FASE 5 autorización puntual por permiso · FASE 6 mora + auto-ajuste puntaje + umbral 20 días · FASE 7 UI perfil ·
FASE 8 auditoría/tests.

## Addendum re-validación 2026-07-01
Re-validado desde cero (no asumido): build main 0/0, build tests 0/0, focalizados 308/308, migraciones aplican en
LocalDB real con constraints `0..5` y seed puntaje 0 = $200.000 verificados por SQL. **Corrección**: quedaban 2
rótulos residuales "nivel crediticio" en contexto de cupo, ahora corregidos → `_LimitesPorPuntajeModal_tw.cshtml`
(título/subtítulo) e `Index_tw.cshtml` (botón). Cierre canónico consolidado en `credito-fase-1-cierre.md`.
