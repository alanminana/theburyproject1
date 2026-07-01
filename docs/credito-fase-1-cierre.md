# Crédito — Cierre FASE 1

> Estado: **FASE 1 CERRADA y validada** (build 0/0, tests focalizados 308/308, QA de datos sobre SQL Server LocalDB real). Sin commitear.
> Documento hermano con el detalle sub-lote por sub-lote: [`fase-1-eje-unico-puntaje-cupo.md`](fase-1-eje-unico-puntaje-cupo.md).
> Contexto/plan general: `~/.claude/plans/hace-un-plan-completo-whimsical-fog.md`. Fecha de cierre: 2026-07-01.

## Objetivo

FASE 1 dejó el modelo de scoring/cupo alineado a **un único eje de puntaje interno de comportamiento (0–5)**.
Antes había dos ejes desacoplados: `PuntajeCliente` (comportamiento, 1–5, **no** gobernaba el cupo) y
`NivelRiesgo` (1–5, **sí** gobernaba el cupo). Ahora el cupo lo gobierna `PuntajeCliente`; `NivelRiesgo`/BCRA
quedan como señal de aptitud/riesgo (semáforo), no como driver de cupo.

## Estado inicial (detectado en este chat)

- Working tree con el WIP de FASE 1 sin commitear (entidades, `AppDbContext`, 3 migraciones untracked, services,
  controller, viewmodels, vistas, tests). Rama `main`, HEAD `4ca8ec7`.
- Ya existía un doc de cierre previo (`fase-1-eje-unico-puntaje-cupo.md`) que reportaba FASE 1 completa 308/308.
- Verificación independiente del log: se re-corrió build + tests + QA de datos desde cero (no se asumió el log).
- Se detectaron **2 textos residuales visibles** "nivel crediticio" en contexto de cupo (ver Riesgos resueltos).

## Reglas funcionales aplicadas (confirmadas por Javo)

- Cliente nuevo arranca con **puntaje 0** (persistido).
- **Puntaje 0 → cupo $200.000** (tope del cliente nuevo, editable por Javo).
- El **cupo lo gobierna `PuntajeCliente`** (0–5), con override manual opcional.
- **Puntajes 1–5 configurables** por Javo (seed en $0 hasta que los cargue: dato de negocio, no bug).
- `NivelRiesgo`/BCRA = señal de riesgo/aptitud, **no** driver de cupo.
- El puntaje sube/baja por comportamiento de pago; Veraz/BCRA se mantienen separados del puntaje interno.

## Cambios realizados

### Entidades
- `Models/Entities/ConfiguracionScoringCliente.cs`: `PuntajeBase` 1→0, `PuntajeMinimo` 1→0 (máx 5). Cliente sin historial calcula 0.
- `Models/Entities/Cliente.cs`: `PuntajeCliente` initializer 1→0 (+ campo de scoring `CantidadComprasCliente`).
- `Models/Entities/PuntajeCreditoLimite.cs`: `Puntaje` `NivelRiesgoCredito` (enum 1–5) → `int` (0–5).
- `Models/Entities/ClienteCreditoConfiguracion.cs`: `NivelCreditoManual` `NivelRiesgoCredito?` → `int?` (0–5); **nombre de columna conservado** (sin RenameColumn).

### AppDbContext
- `Data/AppDbContext.cs`: `PuntajeCliente` → `HasDefaultValue(0).ValueGeneratedNever()` (persiste el 0 explícito; antes el default de BD = 1 lo pisaba). Check constraints re-escritas a `0..5`. Seed puntaje 0 = $200.000.

### Migraciones (aditivas, reversibles)
- `20260630120000_AddCantidadComprasClienteScoring` — agrega `Clientes.CantidadComprasCliente int default 0` (factor de scoring).
- `20260701010417_AlterPuntajeClienteDefaultCero` — `Clientes.PuntajeCliente` default 1→0 (no re-basea datos existentes).
- `20260701043907_AlterCupoPorPuntajeInterno` — drop+add de las 2 check constraints a `0..5` + `InsertData` puntaje 0 = $200.000. `Down` reversible.
- `Migrations/AppDbContextModelSnapshot.cs` coherente con las 3.

### Services (backend = autoridad)
- `Services/CreditoDisponibleService.cs`: cupo automático derivado de `cliente.PuntajeCliente` (antes `NivelRiesgo`); `ObtenerPresetPorPuntajeAsync(int)`; `ObtenerLimitePorPuntajeAsync`; `GuardarLimitesPorPuntajeAsync` valida exactamente puntajes 0–5. Override manual (`NivelCreditoManual` int) y overrides `LimiteOverride`/`ExcepcionDelta` intactos.
- `Services/Models/CreditoDisponibleResultado.cs` + `Services/Interfaces/ICreditoDisponibleService.cs`: campos/firmas `NivelCredito*` → `int` (puntaje 0–5).
- `Services/VentaService.cs`: snapshot de venta-crédito usa `config?.NivelCreditoManual ?? cliente.PuntajeCliente`; `venta.PuntajeAlMomento` guarda el puntaje 0–5 (antes `(int)NivelRiesgo * 2m`); lookup de límite por `p.Puntaje == cliente.PuntajeCliente`.
- `Services/ClienteService.cs` + `IClienteService.cs`: `AsignarNivelCreditoManualAsync(int nivel, ...)`; historial `Origen="PuntajeCreditoManual"`/`"PuntajeCreditoManualLimpio"`. `NivelRiesgo`→`PuntajeRiesgo (=NivelRiesgo*2)` se mantiene solo como señal de riesgo.
- `Services/ClienteScoringCalculator.cs` / `ClienteScoringService.cs` / `Models/ClienteScoringResultado.cs`: modelo base-0, acotado a `[0,5]`.

### Controllers
- `Controllers/ClienteController.cs`: `AsignarNivelCreditoManual(int nivelCreditoManual, ...)`; opciones y límites por `Enumerable.Range(0,6)`; `PuntajeActual = cliente.PuntajeCliente`. `asp-action`/`name` conservados para routing/JS.

### ViewModels
- `ViewModels/ClienteDetalleViewModel.cs` (`PuntajeActual`, `ClienteNivelCreditoOpcionViewModel.Nivel`), `ClienteCreditoLimitesViewModel.cs` (`Puntaje`), `ClienteViewModel.cs`: `NivelRiesgoCredito` → `int`.

### Vistas
- `Views/Cliente/Details_tw.cshtml`: textos visibles "Nivel" → "Puntaje" (final/automático/manual, fuente, drawer "Asignar puntaje manual"). IDs, `name`, `data-*`, `asp-action` conservados.
- `Views/Cliente/_LimitesPorPuntajeModal_tw.cshtml`: filas por "Puntaje N"; **título/subtítulo corregidos** a "Límites por puntaje" (ver Riesgos resueltos).
- `Views/Cliente/Index_tw.cshtml`: **botón corregido** "Límites por nivel crediticio" → "Límites por puntaje".

### Tests
- Ajustados a enum→int + `PuntajeCliente` como driver + puntajes 0–5: `CreditoDisponibleServiceTests`, `CreditoDisponibleServiceLimitesTests`, `ClienteScoring*`, `ClienteServiceTests`, `VentaServiceCreditoPersonalTests`, `ClienteScoringCalculatorTests`. Firmas de ~16 stubs de `ICreditoDisponibleService`/`IClienteService`.
- Test nuevo: `ClienteScoringServiceTests.ClienteNuevo_PersisteConPuntajeCero`.

## Migraciones

| Migración | Efecto |
|---|---|
| `20260630120000_AddCantidadComprasClienteScoring` | `Clientes.CantidadComprasCliente int default 0` |
| `20260701010417_AlterPuntajeClienteDefaultCero` | `Clientes.PuntajeCliente` default 1→0 |
| `20260701043907_AlterCupoPorPuntajeInterno` | check constraints → `0..5`; seed puntaje 0 = $200.000 |

Aplicaron desde cero en orden correcto sobre LocalDB real (ver Validaciones).

## Validaciones (ejecutadas en este chat, no asumidas del log)

- **Build main** (`dotnet build TheBuryProyect.csproj`): **OK — 0 errores / 0 advertencias**.
- **Build tests** (`dotnet build TheBuryProyect.Tests`): **OK — 0 errores / 0 advertencias**.
- **Tests focalizados** (`--filter "…CreditoDisponible|ClienteScoring|ClienteServiceTests|VentaServiceCreditoPersonal|CreditoService"`): **OK — 308/308**, 0 fallos.
- **QA LocalDB** (DB fresca `TheBuryProjectQAFase1` vía `dotnet ef database update`): las 19 migraciones aplican, "Done."
  - `PuntajeCreditoLimites`: puntaje 0 = $200.000; puntajes 1–5 = $0.
  - `CK_PuntajeCreditoLimites_Puntaje` = `[Puntaje]>=0 AND [Puntaje]<=5`.
  - `CK_ClientesCreditoConfiguraciones_NivelCreditoManual` = `NULL OR (0..5)`.
  - `Clientes.PuntajeCliente` default = `0`; `Clientes.CantidadComprasCliente` default = `0`.
  - DB de QA eliminada al cerrar.
- **QA texto de vistas (estático)**: sin texto residual "nivel crediticio" en `Views/`; todos los rótulos de cupo dicen "Puntaje".
- **QA en vivo (Playwright, Cliente/Details)**: ejecutada en la sesión previa (cupo $200.000 para puntaje 0, drawer "Asignar puntaje manual" con select Puntaje 0→$200.000 / 1–5→$0, 0 errores de consola). **No re-ejecutada** en este chat: el delta de esta sesión son 2 rótulos de texto (mecánicos, sin impacto de layout/interacción), cubiertos por verificación estática de las vistas y por la QA de datos.

## Resultado

**FASE 1 queda CERRADA.** El eje de puntaje interno 0–5 gobierna el cupo, el cliente nuevo persiste en 0 con tope
$200.000, y `NivelRiesgo` dejó de ser driver de cupo. Verificado por build 0/0, 308/308 tests y QA de datos autoritativa.

## Riesgos resueltos

- **Textos residuales "nivel crediticio"** (contradecían el modelo de puntaje): corregidos en
  `_LimitesPorPuntajeModal_tw.cshtml` (título/subtítulo) e `Index_tw.cshtml` (botón). Los `nivel*` restantes en
  `Details_tw.cshtml` son ids/`name`/`asp-action` internos, conservados a propósito para no romper JS/routing.
- **`NivelRiesgo` como driver de cupo**: verificado que ya no gobierna cupo en `Services` (solo `PuntajeCliente`).
  Los usos restantes de `NivelRiesgo` (`ClienteService.PuntajeRiesgo`, `ReporteService`) son señal de riesgo/reporte.
- **Símbolo colgado `ObtenerPresetPorNivelAsync`**: no existe (renombrado a `ObtenerPresetPorPuntajeAsync`), sin referencias rotas.
- **Regresión pre-existente de HEAD (`4ca8ec7`)**: `ICajaService.ObtenerUltimoEfectivoCierreAsync` se agregó a la
  interfaz sin actualizar 18 stubs de test → el proyecto de tests no compilaba de raíz. Resuelto (stub devuelve `null`).
- **Coherencia del snapshot EF**: verificada (int 0–5, constraints 0..5, seed 6 filas, defaults 0).

## Pendientes reales (dependen de Javo o de fase futura)

- **Configurar cupos puntaje 1–5** en Cliente/Details → "Límites por puntaje" (hoy $0 por seed). Dato de negocio.
- **Clientes existentes** conservan su `PuntajeCliente` (se re-ajustan por `ClienteScoringService.RecalcularAsync` en cada venta/mora; no hay recálculo masivo, no es necesario para correctitud).
- **Estrategia de commit**: el working tree está entrelazado con WIP ajeno (OrdenCompra / `CantidadComprasCliente` /
  regresión de caja). Commit temático por scope pendiente de decisión.

## Próximas fases

- **FASE 2 — Garante validado**: garante = cliente real, activo, que compró antes, puntaje ≥ 4, máx 3 garantizados. No aumenta cupo; solo habilita aprobación.
- **FASE 3 — Cuenta de crédito / disponible**: verificar límite, deuda actual y disponible; venta a crédito consume disponible; pago libera disponible. No romper caja/ventas.
- **FASE 4 — Evaluador unificado**: requisitos mínimos (DNI + Veraz + recibo servicio + (sueldo ∨ garante)); Veraz obligatorio; Veraz bajo bloquea solo clientes nuevos. Resultado: aprobado / bloqueado / requiere autorización / documentación incompleta / supera cupo.
- **FASE 5 — Autorización puntual**: por permiso; por compra puntual; registra cliente, venta, monto, regla incumplida, motivo, usuario, fecha/hora.
- **FASE 6 — Mora y ajuste de puntaje**: detectar 20 días de atraso; subir/bajar puntaje; registrar historial.
- **FASE 7 — UI perfil crediticio**: panel puntaje / cupo / deuda / disponible / Veraz / documentación / garante / alertas / autorizaciones / historial.
- **FASE 8 — Auditoría y tests**: unitarios + integración + QA visual + auditoría de autorizaciones.

## Próximo paso recomendado

Definir estrategia de commit temático del working tree (aislar scope crédito FASE 1 de WIP ajeno y de la regresión de
caja) y luego arrancar **FASE 2 — Garante validado** (aditiva, bajo riesgo).
