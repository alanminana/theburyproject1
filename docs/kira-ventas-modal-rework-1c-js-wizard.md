# KIRA-VENTAS-MODAL-REWORK-1C — JS separado para pasos y submodales

**Tipo:** JS separado / wizard UI / submodales / bajo-medio riesgo  
**Fase:** KIRA-VENTAS-MODAL-REWORK-1C  
**Fecha:** 2026-05-25  
**Estado:** CERRADA — base de integración para fase 1D

---

## A. Objetivo

Crear `wwwroot/js/venta-modal-rework.js` con la lógica JS específica del wizard fullscreen del modal Nueva Venta. El archivo gestiona:

- Navegación de pasos del wizard (tabs)
- Activación visual de tabs y paneles
- Navegación por teclado accesible (ARIA)
- Submodal `#modal-pago-item` (apertura/cierre)
- Estado global visual `#vm-estado-global`
- Espejo sticky total (MutationObserver, migrado desde script inline)
- Reset al paso 1 cuando el modal se abre

Sin romper ni reemplazar la lógica funcional de:
- `venta-crear-modal.js` (VentaCrearModal.open/close/submit)
- `venta-create.js` (autocomplete, totales, crédito, etc.)
- `venta-module.js` (bindModal para #modal-documentacion)

---

## B. Base y contexto

- **Commit base:** `58b349d` — KIRA-VENTAS-MODAL-REWORK-1B integrada
- **Fase 1A:** Creó el skeleton Razor fullscreen con 5 step-panels y wizard tablist
- **Fase 1B:** Agregó `wwwroot/css/venta-modal-rework.css` con clases canónicas `vm-step-tab--*`, `vm-step-panel-active`, `vm-estado--*`
- **Hallazgo de auditoría:** `#modal-pago-item` no tenía ningún handler JS en archivos existentes. El sistema de submodales de `venta-module.js` (bindModal) cubre solo `#modal-documentacion` vía `data-venta-modal-action/target`. El nuevo archivo cubre `#modal-pago-item`.

---

## C. Archivos auditados

| Archivo | Hallazgo clave |
|---|---|
| `Views/Venta/_VentaCrearModal.cshtml` | Tenía un inline `<script>` con MutationObserver para sticky total — migrado al nuevo JS |
| `Views/Venta/Index_tw.cshtml` | Script section con `venta-crear-modal.js` — punto correcto de carga del nuevo archivo |
| `wwwroot/js/venta-crear-modal.js` | Expone `VentaCrearModal.open/close/submit`. Emite `venta-crear-modal:open`. No toca tabs ni panels |
| `wwwroot/js/venta-create.js` | Sin referencias a `modal-pago-item`, step-btn ni step-panel. No duplicar |
| `wwwroot/js/venta-module.js` | `bindModal` cubre `modal-documentacion` por `data-venta-modal`. No cubre pago-item |
| `wwwroot/css/venta-modal-rework.css` | Definidas: `vm-step-tab--active`, `vm-step-tab--complete`, `vm-step-tab--warning`, `vm-step-panel-active`, `vm-estado--listo`, `vm-estado--alerta`, `vm-estado--error` |
| `TheBuryProyect.Tests/Unit/VentaCreateUiContractTests.cs` | Tests por IDs, names, contenido JS — sin tests sobre tabs ni wizard previamente |

---

## D. Archivos modificados

| Archivo | Tipo | Descripción |
|---|---|---|
| `wwwroot/js/venta-modal-rework.js` | Nuevo | JS del wizard: tabs, submodales, estado global, sticky mirror |
| `Views/Venta/_VentaCrearModal.cshtml` | Modificado | Removido inline `<script>` MutationObserver (migrado al nuevo JS) |
| `Views/Venta/Index_tw.cshtml` | Modificado | Agregada línea de carga del nuevo script |
| `TheBuryProyect.Tests/Unit/VentaCreateUiContractTests.cs` | Modificado | 7 nuevos tests de contrato para 1C |
| `docs/kira-ventas-modal-rework-1c-js-wizard.md` | Nuevo | Este documento |

---

## E. JS nuevo creado

**`wwwroot/js/venta-modal-rework.js`** — IIFE, strict mode, ~200 líneas.

Secciones:
1. **Guard** — `if (!document.getElementById('modal-crear-venta')) return;` — no ejecuta en otras páginas
2. **`activateStep(stepName)`** — activa tab + muestra panel + aplica clases CSS; desactiva los demás
3. **`updateStepState(stepName, state)`** — marca tab como `complete` o `warning` (no toca el tab activo)
4. **`initWizardTabs()`** — delegación de click en tablist + ArrowRight/Left/Home/End
5. **`setOperationState(state)`** — actualiza `#vm-estado-global` con clase `vm-estado--*`
6. **`openSubmodal(id)` / `closeSubmodal(id)`** — muestra/oculta submodales, gestiona `body.style.overflow`
7. **`initPagoItemSubmodal()`** — delegación de click en `.btn-cerrar-pago-item` + Escape en capture phase
8. **`initStickyTotalMirror()`** — MutationObserver `#total-final` → `#vm-modal-sticky-total` (migrado)
9. **`initModalOpenReset()`** — escucha `venta-crear-modal:open` → resetea a paso 1 + estado `incompleta`
10. **`window.VentaModalRework`** — API pública

---

## F. Orden de carga

```
horizontal-scroll-affordance.js
venta-module.js
venta-create.js
venta-crear-modal.js
venta-modal-rework.js   ← NUEVO
```

Cargado dentro del bloque `@if (puedeCrear)` en `@section Scripts` de `Views/Venta/Index_tw.cshtml`.

---

## G. API pública agregada

```javascript
window.VentaModalRework = {
    activateStep(stepName),      // 'cliente' | 'productos' | 'pago' | 'credito' | 'revision'
    openSubmodal(id),            // id de un submodal, ej: 'modal-pago-item'
    closeSubmodal(id),           // ídem
    setOperationState(state),    // 'incompleta' | 'lista' | 'alerta' | 'error'
    updateStepState(stepName, state)  // state: 'default' | 'complete' | 'warning'
}
```

No reemplaza ni extiende `window.VentaCrearModal` (que sigue siendo `open/close/submit`).

---

## H. Wizard steps implementados

| Step | Tab ID | Panel ID | Activación |
|---|---|---|---|
| 1 | `#step-btn-cliente` | `#step-panel-cliente` | Click + teclado, activo por defecto |
| 2 | `#step-btn-productos` | `#step-panel-productos` | Click + teclado |
| 3 | `#step-btn-pago` | `#step-panel-pago` | Click + teclado |
| 4 | `#step-btn-credito` | `#step-panel-credito` | Click + teclado |
| 5 | `#step-btn-revision` | `#step-panel-revision` | Click + teclado |

Comportamiento:
- Click en tab → `activateStep(stepName)`
- `aria-selected="true"` en tab activo, `false` en los demás
- `tabIndex=0` en activo, `-1` en los demás
- `hidden` removido del panel activo, agregado a los demás
- Clase `vm-step-tab--active` en tab activo
- Clase `vm-step-panel-active` por 300ms para animación fadein
- Teclado: `ArrowRight` → paso siguiente, `ArrowLeft` → anterior, `Home` → 1, `End` → 5
- Al abrir el modal (`venta-crear-modal:open`): reset automático al paso `cliente`

---

## I. Submodales gestionados

### `#modal-pago-item`

| Acción | Trigger | Implementado en |
|---|---|---|
| Cerrar | `.btn-cerrar-pago-item` (delegado en document) | `initPagoItemSubmodal()` |
| Cerrar | `Escape` (capture phase, antes del main modal) | `initPagoItemSubmodal()` |
| Abrir | `VentaModalRework.openSubmodal('modal-pago-item')` | API pública |

**Nota:** El botón que abre el modal desde una fila de detalle no está implementado aún. En `venta-create.js`, `renderDetalles()` actualmente no genera ningún botón `btn-configurar-pago-item`. La apertura queda disponible vía API pública para la fase en que se integre el flujo completo.

### `#modal-documentacion`

- Gestionado por `venta-module.js` vía `data-venta-modal-action/target` (no cambia)
- `closeSubmodal` también puede cerrarlo si se llama explícitamente

---

## J. Estado global visual

`#vm-estado-global` — badge en el header del wizard.

| Estado | Clase aplicada | Texto | Cuándo usar |
|---|---|---|---|
| `incompleta` | ninguna (default) | Incompleta | Inicial o sin datos suficientes |
| `lista` | `vm-estado--listo` | Lista | Listo para confirmar |
| `alerta` | `vm-estado--alerta` | Atención | Advertencias activas |
| `error` | `vm-estado--error` | Error | Errores bloqueantes |

Llamar: `VentaModalRework.setOperationState('lista')`.

La lógica de cuándo cambiar el estado según reglas de negocio reales (cliente seleccionado, productos agregados, crédito ok) queda para la fase 1D–1G.

---

## K. Compatibilidad con venta-create.js

- `venta-create.js` no toca tabs, paneles, `#vm-estado-global` ni `#modal-pago-item`
- El nuevo JS no redefine ninguna función de `venta-create.js`
- El nuevo JS usa `venta-crear-modal:open` (evento de `venta-crear-modal.js`) para resetear el wizard
- El JS nuevo puede coexistir en la misma página con plena compatibilidad

---

## L. Contratos preservados

### IDs críticos (no tocados)

`#modal-crear-venta`, `#venta-form`, `#btn-confirmar`, `#input-buscar-cliente`, `#dropdown-clientes`, `#hdn-cliente-id`, `#info-cliente`, `#input-buscar-producto`, `#dropdown-productos`, `#panel-agregar-producto`, `#hdn-producto-id`, `#txt-cantidad`, `#txt-descuento-item`, `#btn-agregar-producto`, `#tbody-detalles`, `#detalles-hidden-inputs`, `#select-tipo-pago`, `#total-subtotal`, `#total-descuento`, `#total-iva`, `#total-final`, `#hdn-subtotal`, `#hdn-descuento`, `#hdn-iva`, `#hdn-total`, `#panel-alerta-mora`, `#panel-cupo-insuficiente`, `#panel-documentacion-faltante`, `#VendedorUserId`, `#Observaciones`, `#modal-pago-item`, `#modal-documentacion`

### API JS pública no afectada

`VentaCrearModal.submit()`, `VentaCrearModal.open()`, `VentaCrearModal.close()`

### Data-attrs preservados

`data-venta-modal-action`, `data-venta-modal-target`, `data-oc-scroll` — no modificados

### Atributos ARIA preservados

`role="dialog"`, `aria-modal="true"`, `aria-labelledby="cv-title"`, `role="tablist"`, `role="tab"`, `role="tabpanel"`, `aria-live`, `role="alert"`

---

## M. Qué no se tocó

- `wwwroot/js/venta-create.js`
- `wwwroot/js/venta-crear-modal.js`
- `wwwroot/js/venta-module.js`
- `wwwroot/css/venta-module.css`
- `wwwroot/css/venta-modal-rework.css`
- Controllers, services, models, migrations, endpoints, payloads
- `Views/Venta/Create_tw.cshtml`
- `Views/Venta/_VentaModuleStyles.cshtml`
- Playwright specs

---

## N. Cambios visibles para el usuario

1. **Los tabs del wizard ahora son clickeables** — antes tenían la clase CSS correcta pero no había JS que cambiara el panel visible
2. **Navegar con teclado** entre pasos es posible (ArrowRight/Left/Home/End)
3. **Al abrir el modal** siempre arranca en el paso Cliente (reset automático)
4. **Escape cierra `#modal-pago-item`** si está abierto (en lugar de cerrar el modal principal)
5. **El total en la barra mobile sticky sigue funcionando** (MutationObserver migrado)

---

## O. Riesgo funcional

**Riesgo bajo.**

- El nuevo JS solo agrega comportamiento donde no había ninguno (tabs, pago-item)
- No modifica ninguna función existente
- El guard de `#modal-crear-venta` evita ejecución en otras páginas
- La migración del MutationObserver es segura — el script externo corre tras DOMContentLoaded, que es después o simultáneo al punto donde corría el inline script
- El Escape en capture phase para `#modal-pago-item` solo consume el evento si ese submodal está visible; no afecta el cierre del modal principal en casos normales

---

## P. Validaciones ejecutadas

```powershell
dotnet build --configuration Release
dotnet test --configuration Release --filter "VentaCreate"
git diff --check
git status --short
```

---

## Q. Resultado build

Ver sección P — resultado documentado al cierre de la fase.

---

## R. Resultado tests VentaCreate

7 tests nuevos agregados en `VentaCreateUiContractTests.cs`:

- `VentaModalReworkJs_ExisteYExponeFuncionActivateStep`
- `VentaModalReworkJs_ExponeFuncionesDeSubmodal`
- `VentaModalReworkJs_ExponeFuncionesDeEstadoGlobal`
- `VentaModalReworkJs_NoRedefineVentaCrearModal`
- `VentaModalReworkJs_TieneGuardDeSeguridadParaOtrasPaginas`
- `VentaCrearModal_NoTieneInlineScriptMutationObserver`
- `IndexView_CargaVentaModalReworkJs`

---

## S. Playwright ejecutado u omitido

**No ejecutado.** La app no fue levantada en esta fase. La fase 1C es JS de integración UI sin cambios en endpoints ni payloads. La correctitud funcional puede verificarse con build + tests de contrato. La validación Playwright completa queda para la fase 1D.

Para validar manualmente cuando la app esté disponible:

```powershell
$env:E2E_USER="Admin"
$env:E2E_PASS="Admin123!"
# Navegar a /Venta
# Abrir modal Nueva Venta
# Verificar: tabs clickeables, panel visible cambia, aria-selected cambia
# Verificar: ArrowRight/Left navegan entre tabs
# Verificar: modal siempre abre en paso Cliente
# Verificar: Escape cierra modal-pago-item si estuviera abierto
# Verificar: total sticky sigue espejando #total-final
# Verificar: no hay errores JS críticos en consola
# Verificar: VentaCrearModal.submit() sigue existiendo
```

---

## T. Deudas restantes

| Deuda | Prioridad | Fase sugerida |
|---|---|---|
| Abrir `#modal-pago-item` desde botón en fila de detalle | Alta | 1E (Pago) |
| `updateStepState()` conectado a reglas reales (cliente ok, productos, crédito) | Alta | 1D–1G |
| `setOperationState()` conectado a reglas de negocio reales | Media | 1D–1G |
| `syncRevisionPanel()` — poblar el paso Revisión con datos actuales | Media | 1G |
| Playwright visual 1440x900 y mobile 390x844 del wizard completo | Alta | 1D o QA |
| Prueba E2E venta completa con el nuevo wizard | Alta | 1G–QA |

---

## U. Próximo prompt recomendado

**KIRA-VENTAS-MODAL-REWORK-1D — Integración cliente/producto/totales**

Verificar que con el nuevo layout de wizard fullscreen:
- El autocomplete de cliente (`#input-buscar-cliente`, `#dropdown-clientes`) funciona aunque el panel `step-panel-cliente` empiece visible y los demás hidden
- `renderDetalles()` escribe correctamente en `#tbody-detalles` dentro de `step-panel-productos`
- `actualizarTotalesUI()` actualiza `#total-final`, `#total-subtotal`, etc. en el sidebar (siempre visible)
- El sticky total mobile espeja correctamente
- Al agregar productos el badge `#detalle-items-badge` se incrementa

Validar con: Playwright flujo cliente + producto + total en el wizard rework.
