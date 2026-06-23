# Coordinación de agentes — TheBuryProject

> Archivo compartido entre **Codex** y **Claude Code** para no pisarse cuando se alterna de agente.
> No es documentación de fase (eso vive en `docs/`). Es un tablero de trabajo vivo.
>
> **Protocolo mínimo**
> 1. Antes de tocar código, leer este archivo y `git status`.
> 2. Anotar acá qué lote estás trabajando, qué archivos tocás y en qué estado quedó.
> 3. No tocar los archivos listados en "Superficie bloqueada" de un lote ABIERTO por el otro agente sin avisar acá.
> 4. Al cerrar un lote: pasar su estado a CERRADO, dejar evidencia de build/tests y el `git add` exacto.
> 5. Un solo foco principal por lote (regla del CLAUDE.md/AGENTS.md).

---

## Lote ACTIVO — Calificación crediticia del cliente (NivelCreditoFinal + override manual)

- **Estado:** Implementación COMPLETA y verde en build+tests. **Pendiente solo QA visual mobile y validación e2e en app/DB real.**
- **Rama:** `chore/hardening-produccion-20260620`
- **Sin commitear** (working tree). `main` intacto.
- **Última actualización:** 2026-06-23 — Claude (retomando trabajo de Codex tras corte por límite de uso).

### Objetivo del lote
Ordenar el flujo confuso de "puntajes". Introducir un único **Nivel crediticio final** que manda sobre el cupo:

```
NivelCreditoFinal = NivelCreditoManual (si existe) ?? NivelCreditoAutomatico
NivelCreditoAutomatico = Cliente.NivelRiesgo  (V1 segura, sin recálculo nuevo)
LimiteAsignado = preset del NivelCreditoFinal
Disponible = max(0, LimiteAsignado - SaldoVigente)
```

`PuntajeCliente` (scoring de comportamiento) **ya NO se muestra como puntaje principal** ni define cupo: es factor histórico.

### HECHO (con evidencia)

**Backend / datos**
- `Models/Entities/ClienteCreditoConfiguracion.cs`: +`NivelCreditoManual` (NivelRiesgoCredito?), `MotivoNivelCreditoManual`, `NivelCreditoManualAsignadoPor`, `NivelCreditoManualAsignadoEnUtc`. No se borró nada (LimiteOverride/CreditoPresetId/ExcepcionDelta siguen).
- Migración `Migrations/20260623153000_AddNivelCreditoManualCliente.cs` (+Designer): 4 columnas aditivas nullable + check constraint `NivelCreditoManual IN [1..5] OR NULL`. Snapshot `AppDbContextModelSnapshot.cs` actualizado a mano. **Aditiva y reversible** (Down completo). **NO aplicada todavía a DB real** (ver Pendiente).
- `Data/AppDbContext.cs`: mapeo del check constraint + columnas.
- `Services/CreditoDisponibleService.cs`: resuelve `nivelFinal = nivelManual ?? nivelAutomatico`. Si hay nivel manual → límite del preset del nivel manual e **ignora overrides monetarios** (LimiteOverride/Excepcion/LimiteCredito). Sin manual → conserva la ruta legacy actual (compatibilidad). Setea `LimitePresetId` en todas las ramas. Mensajes pasados a ASCII ("Nivel crediticio").
- `Services/Models/CreditoDisponibleResultado.cs`: +`NivelCreditoAutomatico/Manual/Final`, `FuenteNivelCredito`, motivo/usuario/fecha del manual, `LimitePresetId`.
- `Services/ClienteService.cs` (+`IClienteService.cs`): `AsignarNivelCreditoManualAsync` y `LimpiarNivelCreditoManualAsync`. Crean/actualizan config 1:1, dejan historial en `ClientesPuntajeHistorial` (Origen `NivelCreditoManual` / `NivelCreditoManualLimpio`), motivo obligatorio, usuario+fecha. **No tocan `Cliente.NivelRiesgo`** (automático y manual quedan separados).
- `Services/VentaService.cs`: snapshot de crédito en la venta usa `NivelCreditoManual ?? NivelRiesgo` y desactiva overrides monetarios cuando hay nivel manual.

**Controller / UI**
- `Controllers/ClienteController.cs`: POST `AsignarNivelCreditoManual` y `LimpiarNivelCreditoManual` (motivo obligatorio, permiso `clientes/managecreditlimits`, antiforgery, recalcula aptitud con `EvaluarAptitudAsync(guardarResultado:true)`, returnUrl seguro). `ConstruirOpcionesNivelCreditoAsync` alimenta niveles+límites. PuntajeActual del panel pasa a ser `NivelCreditoFinal`.
- `ViewModels/ClienteDetalleViewModel.cs`: +`NivelesDisponibles` y VM `ClienteNivelCreditoOpcionViewModel`.
- `Views/Cliente/Details_tw.cshtml`: panel **"Calificacion crediticia"** con Nivel final / automático / manual, Estado de aptitud, Fuente, Límite asignado, Saldo usado, Disponible, y **Desglose** (Documentación / BCRA / Mora / Comportamiento / Override / Motivo). Modal **"Asignar nivel manual"** (select nivel + motivo obligatorio + preview límite/disponible) y form **"Limpiar override"**. "Límites por puntaje" → "Límites por nivel crediticio".
- `Views/Cliente/_LimitesPorPuntajeModal_tw.cshtml`: textos a "nivel crediticio".
- `wwwroot/js/cliente-details.js`: wiring del modal (abrir/cerrar por botón/fondo/Escape, preview en cliente con `data-limit`/`data-saldo-usado`). Preview es solo informativo; la autoridad es el backend al guardar.

**Tests (mecánicos + de comportamiento)**
- `CreditoDisponibleServiceTests`: nivel manual 1 con límite 0 ⇒ disponible 0; sin manual ⇒ vuelve al automático; preset id correcto.
- `ClienteServiceTests`: asignar persiste config + historial; limpiar deja override null + historial.
- `VentaServiceCreditoPersonalTests`: snapshot con nivel final/manual.
- Stubs `IClienteService` en `CotizacionControllerPdfTests`, `CotizacionControllerUiTests`, `VentaApiControllerTests`: implementan los 2 métodos nuevos (no-op).

### Validación ejecutada (2026-06-23, Claude)
```
Build TheBuryProyect.csproj (Release/isolated): OK — 0 errores / 0 advertencias
Tests focalizados del lote: OK — 90/90
  (CreditoDisponibleServiceTests, ClienteServiceTests Asignar/Limpiar,
   VentaServiceCreditoPersonalTests, CotizacionControllerPdf/Ui, VentaApiControllerTests)
git diff --check: limpio (solo avisos LF/CRLF de normalización)
```
> Nota: se compila/testea con `-p:BaseOutputPath=artifacts/...binN/ -p:UseAppHost=false` porque hay una instancia dev viva que bloquea `bin/Debug`. Ver Procesos.

### QA visual — COMPLETA
- Codex (desktop): 1440x900, 1280x720, 1024x720, 900x720 OK.
- Claude (mobile/tablet, 2026-06-23, :5199): **768x1024, 390x844, 360x800 OK**. Sin overflow horizontal en ningún viewport. Modal "Asignar nivel manual" abre y entra completo; Guardar/Cancelar visibles y clickeables en los 3. En 360x800 la tarjeta (~797px) deja ~23px de padding inferior clippeado, pero los botones de acción quedan a y≈724 (totalmente alcanzables). Capturas: `qa-nivel-manual-{768,390,360}.jpeg`. Consola sin errores JS (solo INFO SignalR).

### Validación e2e — COMPLETA (Claude, :5199, instancia con binario nuevo)
La migración está **aplicada en la DB local** (DbInitializer.Initialize la corrió al arrancar; las columnas existen — assign/clear escriben en `ClientesCreditoConfiguraciones` sin error). Caso **Alan ClienteId=2** validado end-to-end:
- **Sin override (estado base):** Nivel final 3 (Automatico), Nivel manual `-`, Scoring comportamiento 1/5, Aptitud **Apto**, Doc Completa, BCRA 1 Normal, Mora Al día, Saldo $0, Límite $2.000, **Disponible $2.000**. Nivel 1 a $0 NO lo afecta. ✔
- **Override manual nivel 1 (límite $0):** Nivel final 1, Fuente Manual, Override Sí, NivelRiesgo automático intacto (3), Aptitud recalculada a **No apto** (cupo $0), **Disponible $0**. Flash "Nivel crediticio manual asignado: 1." ✔
- **Limpiar override:** vuelve a Nivel final 3, Fuente Automatico, Override No, Aptitud **Apto**, **Disponible $2.000**. Flash "Nivel manual limpiado...". Alan queda idéntico al estado inicial. ✔
- Observación menor (no defecto): tras assign+clear, Alan conserva un row inerte en `ClienteCreditoConfiguracion` (NivelCreditoManual=null). Esto cambia el label "Origen del límite" de "Nivel crediticio" a "Preset", pero el disponible es idéntico ($2.000, preset nivel 3).

### Regla BCRA→aptitud — IMPLEMENTADA (Claude, 2026-06-23)
Bloqueo duro por Central de Deudores, **sin migración ni flag de config** (gateado por presencia de dato confiable):
- `ConfiguracionCredito` NO se tocó. Se reutiliza `Cliente.SituacionCrediticiaBcra` (int?) + `SituacionCrediticiaConsultaOk` (bool?).
- Clasificación pura `ClienteAptitudService.ConstruirBcraDetalle(situacion, consultaOk, descripcion)`: sin consulta válida o sin situación ⇒ `Evaluada=false` (NO bloquea); situación 0/1 = normal; **2 = requiere revisión** (RequiereAutorizacion); **≥3 = no apto** (bloqueo). Integrada en `DeterminarEstadoFinal`.
- Diseño clave: es **no-op cuando no hay dato confiable o la situación es normal**, así no rompe los tests existentes que afirman `Detalles` vacío en casos Apto, ni cambia clientes sin BCRA. Alan (situación 1) queda Apto, sin cambios.
- Archivos: `Services/ClienteAptitudService.cs`, `ViewModels/AptitudCrediticiaViewModel.cs` (+`AptitudBcraDetalle`), `TheBuryProyect.Tests/Unit/ClienteAptitudServiceTests.cs` (+11 tests: clasificador + integración + regresión normal).
- Build main 0/0. Tests BCRA verdes dentro de la suite completa.

### Suite COMPLETA ejecutada (Claude, 2026-06-23) — VERDE
`dotnet test` total **3488** → 3486 verdes + 2 fallos analizados y resueltos:
1. `CreditoDisponibleServiceLimitesTests.GuardarLimitesPorPuntaje_LimiteConDecimales_RetornaError`: el test esperaba el mensaje con tilde ("números enteros") pero Codex pasó el mensaje a ASCII ("numeros enteros") al reescribir `CreditoDisponibleService`. **Alineado el test** → pasa. (Archivo agregado al lote.)
2. `CambiosPreciosAplicarRapidoTest.Post_AplicarRapido_SinListasExplicitas_UsaListaPredeterminada`: `TaskCanceledException` por `HttpClient.Timeout 100s`; tardó 4m17s en la suite pero **pasa aislado en 7s**. Flaky por timeout bajo carga (TestHost), ajeno al lote crédito/aptitud. No es defecto.
Re-corrida aislada de ambos: **2/2 OK**. Efectivamente suite verde con el fix de alineación.

### PENDIENTE (lo que falta para cerrar)
1. Limpiar artefactos de build/test no versionados (`artifacts/`, `TheBuryProyect.Tests/artifacts/`, `*.jpeg` de QA en raíz) antes del commit — son salidas, no van al commit.
2. Decidir commit (ver `git add` abajo).
3. (Follow-up opcional) UI: exponer el detalle BCRA dentro del panel de aptitud además del estado (hoy el estado ya refleja el bloqueo; el desglose muestra la situación).

### Superficie bloqueada por este lote (no editar en paralelo sin avisar)
```
Controllers/ClienteController.cs
Data/AppDbContext.cs
Migrations/20260623153000_AddNivelCreditoManualCliente.cs (+ .Designer.cs)
Migrations/AppDbContextModelSnapshot.cs
Models/Entities/ClienteCreditoConfiguracion.cs
Services/ClienteAptitudService.cs        (regla BCRA)
Services/ClienteService.cs
Services/CreditoDisponibleService.cs
Services/Interfaces/IClienteService.cs
Services/Models/CreditoDisponibleResultado.cs
Services/VentaService.cs
ViewModels/AptitudCrediticiaViewModel.cs (AptitudBcraDetalle)
ViewModels/ClienteDetalleViewModel.cs
Views/Cliente/Details_tw.cshtml
Views/Cliente/_LimitesPorPuntajeModal_tw.cshtml
wwwroot/js/cliente-details.js
AGENTS.md                                 (puntero a este archivo)
TheBuryProyect.Tests/Integration/ClienteServiceTests.cs
TheBuryProyect.Tests/Integration/CreditoDisponibleServiceLimitesTests.cs (alineación mensaje ASCII)
TheBuryProyect.Tests/Integration/VentaServiceCreditoPersonalTests.cs
TheBuryProyect.Tests/Unit/ClienteAptitudServiceTests.cs (tests BCRA)
TheBuryProyect.Tests/Unit/CreditoDisponibleServiceTests.cs
TheBuryProyect.Tests/Unit/CotizacionControllerPdfTests.cs
TheBuryProyect.Tests/Unit/CotizacionControllerUiTests.cs
TheBuryProyect.Tests/Unit/VentaApiControllerTests.cs
```

### `git add` exacto para este lote (cuando se decida commitear; NO usar `git add -A`)
```
git add Controllers/ClienteController.cs Data/AppDbContext.cs \
  Migrations/20260623153000_AddNivelCreditoManualCliente.cs \
  Migrations/20260623153000_AddNivelCreditoManualCliente.Designer.cs \
  Migrations/AppDbContextModelSnapshot.cs \
  Models/Entities/ClienteCreditoConfiguracion.cs \
  Services/ClienteAptitudService.cs \
  Services/ClienteService.cs Services/CreditoDisponibleService.cs \
  Services/Interfaces/IClienteService.cs Services/Models/CreditoDisponibleResultado.cs \
  Services/VentaService.cs \
  ViewModels/AptitudCrediticiaViewModel.cs ViewModels/ClienteDetalleViewModel.cs \
  Views/Cliente/Details_tw.cshtml Views/Cliente/_LimitesPorPuntajeModal_tw.cshtml \
  wwwroot/js/cliente-details.js AGENTS.md COORDINACION-AGENTES.md \
  TheBuryProyect.Tests/Integration/ClienteServiceTests.cs \
  TheBuryProyect.Tests/Integration/CreditoDisponibleServiceLimitesTests.cs \
  TheBuryProyect.Tests/Integration/VentaServiceCreditoPersonalTests.cs \
  TheBuryProyect.Tests/Unit/ClienteAptitudServiceTests.cs \
  TheBuryProyect.Tests/Unit/CreditoDisponibleServiceTests.cs \
  TheBuryProyect.Tests/Unit/CotizacionControllerPdfTests.cs \
  TheBuryProyect.Tests/Unit/CotizacionControllerUiTests.cs \
  TheBuryProyect.Tests/Unit/VentaApiControllerTests.cs
```
> NO commitear `artifacts/`, `TheBuryProyect.Tests/artifacts/`, `*.jpeg` de QA ni `.playwright-mcp/` (outputs).
> Nota: decidir si `COORDINACION-AGENTES.md` se versiona o se mantiene local (tablero de trabajo). Si es local, quitarlo del `git add` y agregarlo a `.gitignore`.

---

## Procesos vivos (limpiar cuando corresponda)
- **PID 74016** — `dotnet artifacts/buildbin/.../TheBuryProyect.dll --urls http://127.0.0.1:5199`: instancia dev aislada que **lanzó Codex** para QA y quedó viva. Bloquea `artifacts/buildbin`. No la inició Claude; **no se mató** (regla: no matar procesos ajenos). Cerrar con `Stop-Process -Id 74016 -Force` cuando ya no se use.
- Puerto :5187 ya **no** está LISTENING (la app vieja que bloqueaba `bin/Debug` se cerró).

## Cómo levantar para QA (resumen, ver memoria "QA Local App Harness")
- LocalDB, crear DB vacía PRIMERO (gotcha DbInitializer), entorno Development, puerto libre.
- Login dev: `admin` / `Admin123!`.
- Ruta a validar: `/Cliente/Details/2`.
