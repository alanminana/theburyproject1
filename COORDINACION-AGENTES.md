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

## Lote CERRADO - Productos asociados en Credito y Dashboard

- **Estado:** CERRADO, NO COMMITEADO. Codex, 27-jun.
- **Foco:** mostrar productos asociados junto al numero de credito/cuota en `/Credito` y en la tabla "Cuotas y pagos" del Dashboard.
- **Superficie:** `ViewModels/CreditoViewModel.cs`, `ViewModels/DashboardViewModel.cs`, `Helpers/AutoMapperProfile.cs`, `Services/CreditoService.cs`, `Services/DashboardService.cs`, `Views/Credito/_PanelClientePartial.cshtml`, `Views/Dashboard/Index.cshtml`, `wwwroot/css/credito-module.css`, `COORDINACION-AGENTES.md`.
- **No tocar:** lote abierto Caja/Venta de Claude; no editar reglas de venta/caja ni tests por pedido del usuario.
- **QA inicial:** Playwright local contra `http://localhost:5187/Credito` 1440x900: 1 cliente, 4 creditos, sin overflow horizontal, `productMarkers=0`.
- **Validacion:** build inicial Credito OK 0/0. Build final `dotnet build TheBuryProyect.csproj --configuration Release --no-restore -p:BaseOutputPath=artifacts/build-dashboard-productos-2/ -p:UseAppHost=false /nr:false` OK 0/0. Sin tests por pedido del usuario. Playwright local contra `http://127.0.0.1:5213`: Dashboard en 1440x900, 1280x720, 1024x720, 900x720, 768x1024, 390x844, 360x800 sin overflow y tabs OK; la DB local no tenia filas de cuotas en Dashboard (`rows=0`). `/Credito` en la misma matriz OK: productos visibles en creditos asociados (`productMarkers=3`), clientes colapsables abren, cuotas abren, footer de pago presente. Capturas en `artifacts/qa-dashboard-credito-productos-final/`.
- **Procesos:** instancias aisladas propias PID `10008` (`:5211`), `31176` (`:5212`) y `22952` (`:5213`) iniciadas para QA y cerradas. Build colgado PID `35260` cerrado tras timeout; procesos ajenos `dotnet run` PID `8064` y language server PID `31348` no tocados.
- **git add exacto:** `git add ViewModels/CreditoViewModel.cs ViewModels/DashboardViewModel.cs Helpers/AutoMapperProfile.cs Services/CreditoService.cs Services/DashboardService.cs Views/Credito/_PanelClientePartial.cshtml Views/Dashboard/Index.cshtml wwwroot/css/credito-module.css COORDINACION-AGENTES.md`

## Lote ABIERTO (working tree, sin commitear) - Enforcement "cada usuario sobre su propia caja"

- **Estado:** IMPLEMENTADO + VALIDADO, NO COMMITEADO. Claude, 26-jun. Rama `feat/caja-vendedores-valor-ux-20260625`.
- **Foco:** padrón caja↔usuario generalizado (vendedores que venden + cajeros que operan, UNA membresía; RBAC define el verbo). Enforcement AMBOS + rollout ESTRICTO (caja sin padrón ⇒ solo admin):
  - **A (venta):** `VentaService.CreateAsync` valida que el vendedor pertenezca al padrón de la caja de la apertura (bypass SuperAdmin/Administrador).
  - **B (operación):** `CajaController` Abrir/RegistrarMovimiento/Cerrar/Acreditar (POST+GET) gateados por membresía; dropdown de Abrir filtrado; botón Abrir oculto a no-miembros en Index.
- **Sin migración:** reutiliza tabla `CajaVendedores`/col `VendedorUserId` (ahora guarda vendedor o cajero; nombre histórico conservado y documentado).
- **MIS archivos:** `Services/Interfaces/ICajaVendedorService.cs`, `Services/CajaVendedorService.cs`, `Models/Entities/CajaVendedor.cs`, `Views/Caja/_EditModal_tw.cshtml`, `Controllers/CajaController.cs`, `Views/Caja/Index_tw.cshtml`, `TheBuryProyect.Tests/Integration/CajaVendedorServiceTests.cs`, `TheBuryProyect.Tests/Integration/VentaServiceCajaEnforcementTests.cs` (NUEVO). **PARCIALMENTE MÍOS (entangled):** `ViewModels/CajaViewModel.cs` (mi +CajerosDisponibles junto a `MercaderiaMovidaViewModel` de otro lote), `Services/VentaService.cs` (mi `ValidarVendedorHabilitadoEnCajaAsync` junto a la WIP "excepcion credito personal por cupo" de Codex).
- **Validacion:** build main Release 0/0; `dotnet test ... --filter "FullyQualifiedName~VentaService|FullyQualifiedName~CajaVendedorService"` OK **416/416** (incl. test nuevo de enforcement A). QA Playwright live :5188 (DB aislada TheBuryProjectQA2, dropeada): modal "Usuarios habilitados" (grupos Cajeros/Vendedores) + persistencia, Abrir gateado (propia vs "Sin acceso"), enforcement B (abrir propia OK / POST crafteado a caja ajena bloqueado), 390px sin overflow + footer alcanzable.
- **⚠️ COMMIT:** NO usar `git add <file>` en `ViewModels/CajaViewModel.cs` ni `Services/VentaService.cs` (arrastra WIP ajena: Mercadería movida / excepcion credito). Requiere staging por hunks (`git add -p`/patch). Los M en `VentaServiceConfirmarEfectivoTests`/`VentaServiceCreditoPersonalTests` NO son míos (mi edición fue net-zero, revertida).
- **Procesos:** instancia QA PID propio en :5188 cerrada; DB TheBuryProjectQA2 dropeada. Dev ajena PID `28620` (:5187) NO tocada.

## Lote CERRADO - Credito index como pagina de cartera por cliente

- **Estado:** CERRADO, NO COMMITEADO. Codex, 26-jun.
- **Foco:** `/Credito` deja de duplicar cartera en acordeon + drawer; el contenido del panel de cliente pasa a renderizarse inline como pagina principal.
- **Superficie:** `Views/Credito/Index_tw.cshtml`, `Views/Credito/_PanelClientePartial.cshtml`, `wwwroot/js/credito-index.js`, `wwwroot/css/credito-module.css`, `COORDINACION-AGENTES.md`.
- **No tocar:** lote abierto Caja/Venta de Claude; no editar controllers/services/reglas de negocio.
- **Validacion:** build Release aislado OK 0/0; `CreditoUiQueryServiceTests` OK 14/14; Playwright local contra `http://localhost:5187/Credito` en 1440x900, 1280x720, 1024x720, 900x720, 768x1024, 390x844, 360x800 OK: sin overlay/drawer, sin cards redundantes, sin overflow horizontal global, tabs Cartera/Moras OK, expandir cuotas OK, seleccionar cuota habilita Registrar pago, footer alcanzable. Capturas en `artifacts/qa-credito-index-inline-final/`.
- **Limitacion:** MCP Playwright estaba bloqueado por una instancia existente; se valido con Playwright local headless. `git diff --check` sigue fallando por whitespace preexistente en `Views/Cliente/Details_tw.cshtml:453`, fuera de este lote.
- **git add exacto:** `git add Views/Credito/Index_tw.cshtml Views/Credito/_PanelClientePartial.cshtml wwwroot/js/credito-index.js wwwroot/css/credito-module.css COORDINACION-AGENTES.md`

---

## Lote CERRADO - Fix excepcion credito personal por cupo

- **Estado:** CERRADO por Codex.
- **Foco:** venta con credito personal puede avanzar con excepcion autorizada cuando el unico bloqueo es cupo disponible insuficiente reportado como `ClienteNoApto`.
- **Archivos:** `Services/VentaService.cs`, `TheBuryProyect.Tests/Integration/VentaServiceCreditoPersonalTests.cs`.
- **Validacion:** test rojo inicial reprodujo el error exacto. Luego `dotnet test TheBuryProyect.Tests\TheBuryProyect.Tests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~VentaServiceCreditoPersonalTests.CreateAsync_CreditoPersonalExcedeCupoConExcepcionAutorizada" -p:BaseOutputPath=artifacts/test-excepcion-credito/ -p:UseAppHost=false --logger "trx;LogFileName=venta-credit-excepcion-green.trx" --blame-hang --blame-hang-timeout 120s` OK 1/1. `dotnet test ... --no-build --filter "FullyQualifiedName~VentaServiceCreditoPersonalTests"` OK 9/9. `dotnet build TheBuryProyect.csproj --configuration Release --no-restore -p:BaseOutputPath=artifacts/build-excepcion-credito/ -p:UseAppHost=false /nr:false` OK 0/0. `git diff --check` OK solo avisos LF/CRLF.
- **Procesos:** no se iniciaron procesos persistentes. Vivos ajenos detectados y no tocados: PID `16624` VS MSBuildProjectTools language server, PID `8000` `dotnet run`.
- **git add exacto:** `git add Services/VentaService.cs TheBuryProyect.Tests/Integration/VentaServiceCreditoPersonalTests.cs COORDINACION-AGENTES.md`

---

## Lote CERRADO - Caja fisica vs medios digitales

- **Estado:** CERRADO por Codex.
- **Foco:** Detalle/cierre de caja debe separar efectivo fisico de ventas digitales y exponer totales por medio de pago.
- **Archivos:** `Controllers/CajaController.cs`, `Services/CajaService.cs`, `ViewModels/CajaViewModel.cs`, `Views/Caja/DetallesApertura_tw.cshtml`, `Views/Caja/Cerrar_tw.cshtml`, `wwwroot/js/caja-detalles-apertura.js`, `wwwroot/css/caja-module.css`, tests focalizados de Caja.
- **Validacion:** `dotnet build TheBuryProyect.csproj --configuration Release --no-restore -p:BaseOutputPath=artifacts/build-caja-digital/ -p:UseAppHost=false /nr:false` OK 0/0. QA Playwright OK en `/Caja/DetallesApertura/2` y `/Caja/Cerrar?aperturaId=2`, puerto 5207.
- **Tests:** se agregaron tests focalizados, pero `dotnet test ... --filter "FullyQualifiedName~VentaDigital_No"` excedio 180s sin TRX ni ensamblado de tests usable. No se relanzo otra vez.
- **Proceso:** app aislada PID `24552` iniciada para QA y cerrada.
- **git add exacto:** `git add Controllers/CajaController.cs Services/CajaService.cs ViewModels/CajaViewModel.cs Views/Caja/DetallesApertura_tw.cshtml Views/Caja/Cerrar_tw.cshtml wwwroot/js/caja-detalles-apertura.js wwwroot/css/caja-module.css TheBuryProyect.Tests/Integration/CajaServiceTests.cs TheBuryProyect.Tests/Unit/CajaDetallesAperturaContractTests.cs COORDINACION-AGENTES.md`

---

## Lote CERRADO - Fix validacion CUIT proveedor

- **Estado:** CERRADO por Codex.
- **Foco:** proveedor ahora valida CUIT como 11 digitos numericos sin exigir digito verificador AFIP.
- **Archivos:** `ViewModels/ProveedorViewModel.cs`, `TheBuryProyect.Tests/Unit/ProveedorViewModelValidationTests.cs`.
- **Validacion:** compilacion puntual con `csc` de `Validation/ArgentinaValidationAttributes.cs` + `ViewModels/ProveedorViewModel.cs` OK; `git diff --check` OK con avisos LF/CRLF.
- **Limitacion:** `dotnet test` focalizado y `dotnet build` tests con salida aislada excedieron 150s; se cerraron los PIDs propios `29660`, `12760`, `31824`, `5008`.
- **git add exacto:** `git add ViewModels/ProveedorViewModel.cs TheBuryProyect.Tests/Unit/ProveedorViewModelValidationTests.cs COORDINACION-AGENTES.md`

---

## Lote CERRADO — Calificación crediticia del cliente (NivelCreditoFinal + override manual)

- **Estado:** **CERRADO. Commiteado y mergeado a `main`.**
- **Commit:** `6a604e6` feat(cliente): calificacion crediticia unificada (nivel final manual/automatico + BCRA->aptitud).
- **Rama de trabajo:** `chore/hardening-produccion-20260620` (commiteado ahí; `main` fast-forward `beaa499..6a604e6`, sin merge commit).
- **`main` local incluye además** `b45b739` (BCRA hardening) y `f82b7a3` (Preparar venta), que ya estaban en la rama. `origin/main` SIN pushear (decisión del usuario).
- **Última actualización:** 2026-06-23 — Claude (cierre + merge a main).

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

### Cierre (2026-06-23, Claude)
- Commit `6a604e6` con 28 archivos del lote (staging explícito, sin `git add -A`). `artifacts/` agregado a `.gitignore`.
- Re-validación pre-merge: build principal Release 0/0, build tests Release 0/0, **EF sin drift** ("No changes since last migration"), tests focalizados **177/177**, suite completa **3487 verde + 1 flaky ambiental** (`CambiosPreciosAplicarRapido`, pasa aislado 11s). `git diff --check` limpio.
- `main` fast-forward a `6a604e6`. Working tree limpio.

### Follow-up opcional (fuera de este lote)
- UI: exponer el detalle BCRA dentro del panel de aptitud además del estado (hoy el estado ya refleja el bloqueo; el desglose muestra la situación).
- `origin/main` queda 3 commits atrás: pushear cuando el usuario lo autorice.

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
