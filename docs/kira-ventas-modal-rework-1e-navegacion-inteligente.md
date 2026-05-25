# KIRA-VENTAS-MODAL-REWORK-1E — Navegación inteligente del wizard

**Rama:** `kira/ventas-modal-rework-1e-navegacion-inteligente`  
**Base:** `main` HEAD `8b69920` (1D integrada)  
**Tipo:** JS integración UI / navegación inteligente / bajo-medio riesgo  
**Fecha:** 2026-05-25

---

## A. Estado inicial

- Wizard fullscreen de Nueva Venta con 5 pasos (cliente, productos, pago, crédito, revisión) funcionando desde 1D.
- Los tabs de pasos mostraban estado visual estático (sin reflejo del estado real del formulario).
- El botón Confirmar no navegaba al paso inválido antes de delegar a `VentaCrearModal.submit()`.
- `venta-create.js` tenía su propio scroll al primer error, pero no integraba con los tabs del wizard.
- Deuda conocida de 1D: `updateStepState` / `setOperationState` expuestos pero no conectados a eventos reales.

---

## B. Rama creada

`kira/ventas-modal-rework-1e-navegacion-inteligente`  
Base: `main` HEAD `8b69920`

---

## C. Archivos auditados

- `wwwroot/js/venta-modal-rework.js` — archivo principal, extendido
- `wwwroot/js/venta-create.js` — leído para entender flujo de DOM; no modificado
- `Views/Venta/_VentaCrearModal.cshtml` — leído para confirmar IDs y estructura; no modificado
- `TheBuryProyect.Tests/Unit/VentaCreateUiContractTests.cs` — extendido con 8 tests nuevos
- `docs/kira-ventas-modal-rework-1d-integracion.md` — leído como referencia de deuda

---

## D. Archivos modificados

| Archivo | Tipo de cambio |
|---|---|
| `wwwroot/js/venta-modal-rework.js` | Extendido: nuevas funciones, observadores, API pública |
| `TheBuryProyect.Tests/Unit/VentaCreateUiContractTests.cs` | Extendido: 8 tests nuevos de contrato |
| `docs/kira-ventas-modal-rework-1e-navegacion-inteligente.md` | Nuevo: documento de cierre |

---

## E. Cambios aplicados

### `venta-modal-rework.js`

- Constante `TIPO_PAGO_CREDITO = ['5', '7']` (CreditoPersonal, CuentaCorriente).
- `activateStep()` ahora llama `refreshState()` al final para re-aplicar clases de estado tras cambio de tab.
- `initModalOpenReset()` ahora incluye `setTimeout(refreshState, 50)` al abrir el modal.
- `init()` ahora también llama `initStateObservers()`, `initSubmitNavigation()` y `setTimeout(refreshState, 100)`.
- **Nueva función `evaluateStepStates()`:** lee DOM real (`#info-cliente`, `#tbody-detalles`, `#select-tipo-pago`, paneles de crédito) y retorna objeto `{ cliente, productos, pago, credito, revision }` con valores `'complete' | 'warning' | 'default'`.
- **Nueva función `refreshState()`:** llama `evaluateStepStates()` y aplica `updateStepState()` a cada paso; también llama `setOperationState()` según estado global.
- **Nueva función `safelyFocus(id)`:** foco diferido con `setTimeout(..., 60)` para evitar conflictos con animaciones.
- **Nueva función `goToFirstInvalidStep()`:** navega al primer paso inválido en orden (cliente → productos → crédito → pago) y hace foco en el campo relevante. Retorna el nombre del paso o `null` si todo está completo.
- **Nueva función `initStateObservers()`:** instala `MutationObserver` en `#info-cliente` (attributeFilter: class), `#tbody-detalles` (childList), paneles de crédito (attributeFilter: class); y listener `change` en `#select-tipo-pago` y `#VendedorUserId`.
- **Nueva función `initSubmitNavigation()`:** listener de captura en `document` para clicks en `#btn-confirmar` y `.vm-btn-confirm-sm` — llama `goToFirstInvalidStep()` sin llamar `preventDefault()`.
- **API pública extendida:** `window.VentaModalRework` ahora expone `refreshState` y `goToFirstInvalidStep`.

### `VentaCreateUiContractTests.cs`

8 tests nuevos bajo el marcador `// ── KIRA-VENTAS-MODAL-REWORK-1E`:

| Test | Qué verifica |
|---|---|
| `VentaModalReworkJs_ExponeFuncionesNavegacionInteligente` | `refreshState` y `goToFirstInvalidStep` en `window.VentaModalRework` |
| `VentaModalReworkJs_ImplementaEvaluacionDeEstadosPorPaso` | Función `evaluateStepStates` existe y retorna objeto con 5 pasos |
| `VentaModalReworkJs_ObservaSelectoresClaveDeEstado` | `initStateObservers` observa `info-cliente`, `tbody-detalles`, `select-tipo-pago` |
| `VentaModalReworkJs_NavegaAlPasoInvalidoEnSubmit` | `initSubmitNavigation` instala listener de captura |
| `VentaModalReworkJs_InterceptaClickConfirmarParaNavegar` | `initSubmitNavigation` NO llama `preventDefault` (solo navega) |
| `VentaModalReworkJs_EvaluaClienteConInfoClientePanel` | `evaluateStepStates` lee `#info-cliente` |
| `VentaModalReworkJs_EvaluaCreditoConPanelesDeCreditoVisibles` | `evaluateStepStates` lee paneles de crédito |
| `VentaCrearModal_InfoClientePanelExisteCon_id` | HTML tiene `id="info-cliente"` |

---

## F. Cambios descartados

- No se modificó `venta-create.js` (objetivo del prompt: usar observadores).
- No se agregaron atributos `data-*` a `_VentaCrearModal.cshtml` (no fueron necesarios).
- No se tocó CSS (clases ya existían desde 1B).

---

## G. Contratos preservados

- `VentaCrearModal.submit()`, `VentaCrearModal.open()`, `VentaCrearModal.close()` — intactos.
- `#venta-form`, `#btn-confirmar`, `#hdn-cliente-id`, `#info-cliente`, `#tbody-detalles`, `#select-tipo-pago` — no modificados.
- Paneles de crédito: `#panel-cupo-insuficiente`, `#panel-alerta-mora`, `#panel-documentacion-faltante`, `#panel-cupo-suficiente` — no modificados.
- API previa de `window.VentaModalRework` (activateStep, openSubmodal, closeSubmodal, setOperationState, updateStepState) — preservada.
- ARIA: `role="tab"`, `role="tabpanel"`, `aria-selected`, `tabindex`, `hidden` — no modificados.
- Antiforgery, endpoints, payloads — no tocados.
- Selectores usados por Playwright — no modificados.

---

## H. Qué no se tocó

- Controllers, services, models, migraciones.
- Endpoints o payloads.
- Stock, caja, crédito backend, cotización.
- CSS (salvo lectura de clases existentes).
- Playwright specs.
- Lógica de cálculo o reglas de negocio backend.
- `venta-create.js`.

---

## I. Cambios que debería notar el usuario

- Al abrir el modal de Nueva Venta, los tabs de pasos reflejan inmediatamente el estado real (cliente seleccionado → tab verde; productos sin cargar → tab neutro; etc.).
- Si el usuario hace clic en Confirmar sin completar pasos requeridos, el wizard navega automáticamente al primer paso inválido y hace foco en el campo relevante.
- El submit de `venta-create.js` sigue funcionando sin cambios: la navegación ocurre antes, no bloquea el envío.
- Al cambiar el tipo de pago (ej. a crédito personal), los tabs de crédito y revisión actualizan su estado visual en tiempo real.

---

## J. Validaciones ejecutadas

- `dotnet build --configuration Release` → 0 errores, 0 advertencias.
- `dotnet test --configuration Release --filter "VentaCreate"` → 121/121 OK.
- `git diff --check` → sin trailing whitespace en archivos nuevos (AGENTS.md y CLAUDE.md tienen trailing whitespace preexistente, no commiteados).
- `git status --short` → confirmado que solo los archivos esperados están staged.

---

## K. Tests ejecutados

```
dotnet test --configuration Release --filter "VentaCreate"
Correctas! - Con error: 0, Superado: 121, Omitido: 0, Total: 121
```

Incluye los 8 tests nuevos de 1E y los 113 preexistentes de 1A–1D.

---

## L. Playwright ejecutado

No ejecutado en esta fase.

La app no está corriendo en modo Development durante la tarea.  
Playwright E2E de flujo de venta puede ejecutarse cuando el servidor esté disponible.

---

## M. Resultados exactos

```
Build:
  0 Advertencia(s)
  0 Errores

Tests VentaCreate:
  Con error: 0, Superado: 121, Omitido: 0, Total: 121, Duración: ~321 ms
```

---

## N. Procesos cerrados

`dotnet build` y `dotnet test` finalizaron normalmente. No quedan procesos iniciados por la tarea.

---

## O. Procesos que quedaron corriendo y motivo

Procesos preexistentes no iniciados por esta tarea (no se cierran):

- VS Code / C# DevKit / MSBuild language server.
- Playwright MCP (si estaba activo).
- Context7 MCP (si estaba activo).

---

## P. Estado de archivos locales

- `.claude/settings.local.json` — modificado localmente, no commiteado (según regla de CLAUDE.md).
- `skills-lock.json` — eliminado localmente, no commiteado.
- `AGENTS.md` — modificado localmente, no commiteado.
- `CLAUDE.md` — modificado localmente, no commiteado.
- `Views/Producto/Unidades.cshtml` — modificado localmente, no commiteado.
- `docs/misa-catalogo-ux-1g-aria-live-modales.md` — modificado localmente, no commiteado.

---

## Q. Verificación de temporales

No se generaron archivos temporales por esta tarea.  
`tmpbuild*`, `tmptest*`, `test-results`, `playwright-report`, `graphify-out` — no creados.

---

## R. Working tree final

```
git status --short (archivos commiteados en esta rama):
M  TheBuryProyect.Tests/Unit/VentaCreateUiContractTests.cs
A  docs/kira-ventas-modal-rework-1e-navegacion-inteligente.md
M  wwwroot/js/venta-modal-rework.js

Archivos modificados localmente pero NO commiteados (preexistentes):
 M .claude/settings.local.json
 M AGENTS.md
 M CLAUDE.md
 M Views/Producto/Unidades.cshtml
 M docs/misa-catalogo-ux-1g-aria-live-modales.md
 D skills-lock.json
```

---

## S. Riesgos y deuda remanente

| Deuda | Descripción | Severidad |
|---|---|---|
| Foco en `#btn-verificar-elegibilidad` | El botón puede no existir en todos los estados del paso crédito. `safelyFocus` maneja el caso null graciosamente. | Baja |
| Observer en `#info-cliente` class | Depende de que `venta-create.js` use `show()`/`hide()` que agregan/quitan clase `hidden`. Si ese contrato cambia, el observer no detectaría el cambio de cliente. | Baja |
| Playwright de flujo completo | No ejecutado en esta fase. Verificación manual pendiente cuando el servidor esté disponible. | Media |
| `initSubmitNavigation` captura sin cancel | El click en Confirmar sigue llegando a `VentaCrearModal.submit()` incluso si hay pasos inválidos. venta-create.js tiene su propia validación. No es un bug, es el diseño intencional. | Ninguna |

---

## T. Commit

```
git commit -m "Agregar navegacion inteligente al wizard de venta (KIRA-VENTAS-MODAL-REWORK-1E)"
```

---

## U. Push

```
git push origin kira/ventas-modal-rework-1e-navegacion-inteligente
git switch main && git merge --ff-only kira/ventas-modal-rework-1e-navegacion-inteligente && git push origin main
```

---

## V. Próximo prompt recomendado

**KIRA-VENTAS-MODAL-REWORK-1F — Pago principal y pago por producto**

Integrar la UI del paso "Pago" con la lógica real de pago del wizard:
- Mostrar el tipo de pago seleccionado en el resumen.
- Condicionar la visibilidad de secciones según tipo de pago.
- Conectar totales y descuentos al panel de revisión.
- Preservar todos los contratos existentes de `venta-create.js`.
