# KIRA-VENTAS-MODAL-REWORK-1D — Integración cliente/producto/totales

## A. Objetivo

Verificar y documentar que las funciones existentes de `venta-create.js` siguen
operando correctamente dentro del nuevo layout de wizard fullscreen introducido
en la fase 1A/1B/1C.  
No se modifica código funcional. Se agregan contratos de test que fijan los
invariantes estructurales del wizard para proteger integraciones futuras.

---

## B. Base

- Commit base: `e70b4eb` — "Agregar JS del wizard de nueva venta (KIRA-VENTAS-MODAL-REWORK-1C)"
- Rama base: `main` (1C ya integrado)

---

## C. Archivos auditados

| Archivo | Rol |
|---|---|
| `Views/Venta/_VentaCrearModal.cshtml` | Estructura HTML del wizard |
| `wwwroot/js/venta-create.js` | Lógica funcional existente |
| `wwwroot/js/venta-modal-rework.js` | JS del wizard (fase 1C) |
| `Views/Venta/Index_tw.cshtml` | Carga de scripts y parcial del modal |

---

## D. Archivos modificados

| Archivo | Cambio |
|---|---|
| `TheBuryProyect.Tests/Unit/VentaCreateUiContractTests.cs` | 6 nuevos tests (sección 1D) |
| `docs/kira-ventas-modal-rework-1d-integracion.md` | Este documento |

---

## E. Hallazgos de la auditoría de integración

### E.1 — Autocomplete de cliente en `step-panel-cliente`

`#input-buscar-cliente` y `#dropdown-clientes` están dentro de `#step-panel-cliente`.
`venta-create.js` usa `document.querySelector()` que encuentra elementos en el DOM
completo independientemente de si están visibles (`hidden` solo oculta visualmente).
**Estado: Funciona correctamente.**

### E.2 — Tabla de detalles en `step-panel-productos`

`#tbody-detalles` está dentro de `#step-panel-productos`.
`renderDetalles()` escribe en `#tbody-detalles` siempre, sin importar el paso activo.
Cuando el usuario navega al paso 2, los productos ya renderizados están ahí.
**Estado: Funciona correctamente.**

### E.3 — Totales en el sidebar (siempre visible)

`#total-final`, `#total-subtotal`, `#total-descuento`, `#total-iva` están en el
`<aside class="lg:col-span-4">` — fuera de cualquier step panel.
El sidebar está visible en todos los pasos del wizard.
`actualizarTotalesUI()` actualiza estos elementos en tiempo real con MutationObserver
activado desde `venta-modal-rework.js` que sincroniza `#total-final` →
`#vm-modal-sticky-total`.
**Estado: Funciona correctamente.**

### E.4 — Badge de productos `#detalle-items-badge`

Está dentro del botón `#step-btn-productos` en el tablist del header.
El tablist es parte del `<header>` sticky — visible en todos los pasos.
`actualizarResumenOperacion()` lo actualiza con `innerHTML` (con icono Material).
**Estado: Funciona correctamente.**

### E.5 — Elementos "hero" (`#hero-*`) ausentes en el contexto modal

`venta-create.js` referencia `#hero-cliente`, `#hero-detalles-count`,
`#hero-total`, `#hero-tipo-pago` (solo existen en `Create_tw.cshtml`, la vista
standalone de edición, no en el contexto del modal embebido en `Index_tw.cshtml`).

`actualizarResumenOperacion()` guarda estos accesos con `if (heroCliente && ...)`,
`if (heroDetallesCount)`, `if (heroTipoPago)`, `if (heroTotal)`.
Los `document.querySelector()` devuelven `null`; las guardas previenen cualquier
error en tiempo de ejecución.
**Estado: Degradación silenciosa aceptable — no hay errores, no hay regresión.**

### E.6 — `scrollIntoView` en validación de submit

Dos llamadas en el submit listener:
- `panelAgregarProducto?.scrollIntoView(...)` — panel dentro de `step-panel-productos`
- `panelDiagnosticoCondicionesPago?.scrollIntoView(...)` — dentro de `step-panel-pago`

Si el usuario está en un paso diferente cuando confirma y la validación falla,
el `scrollIntoView` no produce scroll visible (el elemento está `hidden`).
El mensaje de feedback en `#venta-create-feedback-slot` sí se muestra.

**Deuda conocida para fase 1E:** Agregar navegación automática al paso relevante
cuando la validación bloquea el submit.

### E.7 — Scroll affordance en panel oculto al init

`venta-create.js` llama `ventaModule.initScrollAffordance('#venta-detalles-scroll')`
durante la inicialización.  
El elemento `#venta-detalles-scroll` está dentro de `#step-panel-productos` que
inicia con `class="hidden"`.  
El scroll affordance usa `requestAnimationFrame` y se actualiza en cada
`renderDetalles()`. Cuando el usuario navega al paso 2 (productos) y agrega
productos, la affordance se recalcula.  
**Estado: Comportamiento aceptable — no es un bug.**

---

## F. Contratos agregados

Sección `// ── KIRA-VENTAS-MODAL-REWORK-1D` en
`TheBuryProyect.Tests/Unit/VentaCreateUiContractTests.cs`:

| Test | Invariante protegida |
|---|---|
| `VentaCrearModal_BuscarClienteEstaEnPanelCliente` | `#input-buscar-cliente` y `#dropdown-clientes` están dentro de `#step-panel-cliente` |
| `VentaCrearModal_TbodyDetallesEstaEnPanelProductos` | `#tbody-detalles` está dentro de `#step-panel-productos` |
| `VentaCrearModal_TotalFinalEstaEnSidebar_FueraDeStepPanels` | `#total-final` está después del último step panel (en el sidebar) |
| `VentaCrearModal_DetalleBadgeEstaEnStepBtnProductos` | `#detalle-items-badge` está dentro del tab `#step-btn-productos` |
| `VentaCrearModal_PanelClienteVisiblePorDefecto_OtrosPanelesHidden` | Solo `step-panel-cliente` inicia sin `hidden`; los demás inician ocultos |
| `VentaCreateJs_ActualizarResumenOperacion_ActualizaDetalleBadgeConNullGuardEnHero` | `detalleItemsBadge.innerHTML` se actualiza y los hero refs usan guardas null |

---

## G. Qué no se tocó

- `venta-create.js` — sin cambios
- `venta-modal-rework.js` — sin cambios
- `_VentaCrearModal.cshtml` — sin cambios
- `Index_tw.cshtml` — sin cambios
- CSS, controllers, services, modelos, migraciones, endpoints
- Tests existentes (no se modificó ninguno)

---

## H. Cambios visibles para el usuario

Ninguno. Fase de validación y contratos únicamente.

---

## I. Validaciones ejecutadas

- `dotnet build --configuration Release` — ✅ OK (0 errores, 0 advertencias)
- `dotnet test --configuration Release --filter "VentaCreate"` — ✅ todos los tests pasan

---

## J. Playwright

No ejecutado. La fase no modifica HTML, CSS ni JS — no hay cambio visual que
verificar. Los invariantes estructurales quedan cubiertos por los contratos de test.

---

## K. Deuda remanente

1. **`scrollIntoView` en wizard** (1E): Cuando la validación de submit falla y el
   elemento relevante está en un paso oculto, navegar automáticamente al paso
   correspondiente antes de intentar hacer scroll.

2. **`#modal-pago-item` open trigger** (1E): `renderDetalles()` aún no genera un
   botón `btn-configurar-pago-item` por fila. La apertura del submodal por producto
   está pendiente de la fase 1E.

3. **`syncRevisionPanel()`** (1G): El paso de Revisión no popula datos dinámicos.
   Pendiente de la fase 1G.

4. **`updateStepState` / `setOperationState` conectados a reglas de negocio** (1E–1G):
   Actualmente la API pública del wizard solo es invocada por la navegación de tabs.
   La conexión con validaciones reales (cliente seleccionado, productos añadidos,
   pago configurado) es tarea de las fases 1E–1G.

---

## L. Próximo paso recomendado

**KIRA-VENTAS-MODAL-REWORK-1E — Navegación inteligente del wizard**

- Conectar `VentaModalRework.updateStepState()` a los eventos de `venta-create.js`:
  - Paso cliente → `complete` cuando `clienteSeleccionado !== null`
  - Paso productos → `complete` cuando `detalles.length > 0`
  - Paso pago → `complete` cuando tipo de pago configurado y sin bloqueo
  - Paso crédito → `complete` o `warning` según resultado de verificación crediticia
- Navegar automáticamente al paso correcto cuando la validación de submit falla
- Agregar botones "Siguiente / Anterior" opcionales en cada panel

---

## M. Working tree al cierre

Archivos commiteados:
- `M TheBuryProyect.Tests/Unit/VentaCreateUiContractTests.cs`
- `A docs/kira-ventas-modal-rework-1d-integracion.md`

Sin cambios en: `.claude/settings.local.json`, `AGENTS.md`, `CLAUDE.md`,
`Views/Producto/Unidades.cshtml`, `docs/misa-catalogo-ux-1g-aria-live-modales.md`
(pre-existentes, no commiteados).

`skills-lock.json` permanece como `D` (eliminado local, no commiteado).
