# KIRA-VENTAS-MODAL-REWORK-1B — CSS del wizard fullscreen Nueva Venta

---

## A. Objetivo

Agregar CSS específico para que el nuevo modal fullscreen wizard de Nueva Venta se vea consistente, usable, responsive y pulido, sin tocar lógica funcional, endpoints, JS productivo ni reglas de negocio.

---

## B. Base y contexto

- **Commit base:** `bb0e0e4` — `Merge kira/ventas-modal-rework-1a-skeleton-razor`
- **Fase previa:** 1A reemplazó el layout del modal de panel centrado `max-w-7xl` a wizard fullscreen `fixed inset-0 flex flex-col` con 5 tabs, grid 12 columnas, sidebar y submodales preservados.
- **Problema a resolver:** La estructura existe pero el CSS del wizard fullscreen no estaba implementado. Los tabs usaban cadenas de utilidades Tailwind inline sin clases canónicas. El sidebar no tenía sticky. El mobile summary bar tenía `position: sticky` pensado para el layout antiguo.

---

## C. Archivos auditados

| Archivo | Hallazgo |
|---|---|
| `Views/Venta/_VentaCrearModal.cshtml` | Tabs con Tailwind inline, sin clases canónicas. Mobile summary bar con top:4.75rem del layout viejo. Grid 12 cols con `lg:items-start`. |
| `wwwroot/css/venta-module.css` | Ya define las clases `vm-label`, `vm-input`, `vm-select`, `vm-textarea`, `vm-section`, `vm-step`, `vm-dropdown`, `vm-btn-*`, `vm-totals`, `vm-preconfirm-reminder`, `vm-mobile-summary-bar`, `vm-modal-panel`, `vm-modal-header-sep`, etc. |
| `wwwroot/css/shared-components.css` | Normalización de íconos y componentes compartidos. Sin conflicto. |
| `Views/Venta/_VentaModuleStyles.cshtml` | Partial que carga `horizontal-scroll-affordance.css` y `venta-module.css`. Punto de carga para el nuevo archivo. |
| `Views/Venta/Create_tw.cshtml` | Incluye `<partial name="_VentaModuleStyles" />` cerca del cierre. |
| `wwwroot/js/venta-create.js` | Revisado para confirmar que no hay dependencias de las clases Tailwind inline de los tabs (el JS lee `aria-selected`, `data-step`, `id`s). No tocar. |
| `TheBuryProyect.Tests/Unit/VentaCreateUiContractTests.cs` | Tests por IDs y `name` — no dependen de clases CSS de tabs. Sin impacto. |

---

## D. Archivo CSS elegido y motivo

**Decisión:** Crear archivo separado `wwwroot/css/venta-modal-rework.css`.

**Motivo:** El rework del wizard tiene una capa visual propia del fullscreen con semántica nueva (step tabs, estados de pasos, sidebar sticky, mobile layout). Mantenerlo separado de `venta-module.css` aísla el cambio, facilita el rollback y evita aumentar el peso del módulo general de ventas para views que no usan el wizard.

**Carga:** Agregado en `Views/Venta/_VentaModuleStyles.cshtml` **después** de `venta-module.css` para que los overrides de este archivo ganen correctamente.

---

## E. Clases detectadas en _VentaCrearModal.cshtml (antes de 1B)

### Ya definidas en venta-module.css (sin cambio necesario)
- `vm-label`, `vm-required`
- `vm-input`, `vm-textarea`, `vm-select`
- `vm-search-wrap`, `vm-search-icon`
- `vm-dropdown`
- `vm-section`, `vm-step`
- `vm-modal-panel`, `vm-modal-header-sep`
- `vm-btn-confirm`, `vm-btn-confirm-sm`, `vm-btn-add`, `vm-btn-verify`, `vm-btn-ghost`
- `vm-totals`, `vm-preconfirm-reminder`
- `vm-mobile-summary-bar`, `vm-mobile-summary-bar__info`
- `vm-checkbox-label`
- `vm-panel-producto`, `vm-sub-panel`
- `vm-error-summary`
- `venta-stat`, `venta-stat__label`, `venta-stat__value`
- `venta-scroll-medium`, `venta-create-doc-footer`, `venta-create-doc-action`

### Usadas solo como Tailwind inline (reemplazadas en 1B)
- Tabs del wizard: cadenas de `flex items-center gap-2 whitespace-nowrap rounded-xl border border-blue-500/60 bg-blue-500/15 px-4 py-2 text-xs font-bold text-blue-300 transition-all` y equivalentes para inactivo
- Números de pasos: `flex h-5 w-5 shrink-0 items-center justify-center rounded-full bg-blue-500/bg-slate-700 ...`
- Badge de items: `inline-flex items-center rounded-full bg-slate-700 px-1.5 py-0.5 text-[9px] font-black text-slate-300`

---

## F. Clases agregadas / modificadas

### Nuevas en venta-modal-rework.css

| Clase | Descripción |
|---|---|
| `.vm-step-tab` | Base de botón de paso del wizard |
| `.vm-step-tab--active` | Paso activo/actual (azul) |
| `.vm-step-tab--complete` | Paso completado (verde) |
| `.vm-step-tab--warning` | Paso con advertencia (ámbar) |
| `.vm-step-tab__num` | Círculo numérico del tab |
| `.vm-step-tab__badge` | Badge contador de items |
| `.vm-step-panel-active` | Fade-in al mostrar un panel (para JS fase 1C) |
| `.vm-estado--listo` | Estado global badge verde |
| `.vm-estado--alerta` | Estado global badge ámbar |
| `.vm-estado--error` | Estado global badge rojo |

### Overrides en venta-modal-rework.css

| Elemento / Clase | Cambio aplicado |
|---|---|
| `#modal-crear-venta > main` | `min-height: 0` — evita overflow en flex |
| `#modal-crear-venta aside` (lg+) | `position: sticky; top: 1.5rem; align-self: start` |
| `.vm-mobile-summary-bar` | `position: relative; top: auto` — sobreescribe sticky del layout viejo |
| `#modal-crear-venta [role="tablist"]` | `scrollbar-width: none` + `::-webkit-scrollbar { display: none }` |
| `#tbody-detalles tr` | Hover + transición |
| `#tbody-detalles td` | Padding y color estandarizados |
| `#vm-estado-global` | Transición de color |
| `#modal-pago-item`, `#modal-documentacion` | `overflow-y: auto` |

### Cambios en _VentaCrearModal.cshtml

| Elemento | Cambio |
|---|---|
| `#step-btn-cliente` class | `vm-step-tab vm-step-tab--active` (reemplaza cadena Tailwind activa) |
| `#step-btn-productos` class | `vm-step-tab` (reemplaza cadena Tailwind inactiva) |
| `#step-btn-pago` class | `vm-step-tab` |
| `#step-btn-credito` class | `vm-step-tab` |
| `#step-btn-revision` class | `vm-step-tab` |
| Spans numéricos (todos) | `vm-step-tab__num` |
| `#detalle-items-badge` class | `vm-step-tab__badge` |

### Cambios en _VentaModuleStyles.cshtml

Agregada línea al final:
```html
<link rel="stylesheet" href="~/css/venta-modal-rework.css" asp-append-version="true" />
```

---

## G. Cambios visuales aplicados

1. **Tabs del wizard** — aspecto canónico con clases CSS, sin depender de Tailwind inline para los estados. El tab activo (Cliente) muestra azul. Los demás muestran gris neutro con hover sutil.
2. **Sidebar sticky** — en desktop (≥1024px) el sidebar queda pegado a `top: 1.5rem` dentro del main scrolleable. A medida que se scrollea el contenido principal, el sidebar permanece visible.
3. **Tablist sin scrollbar** — la barra de pasos en mobile/tablet scrollea horizontalmente sin mostrar la barra de scroll nativa del browser.
4. **Mobile summary bar** — corregida para el layout fullscreen. Ya no usa `position: sticky` incorrecto. Es un flex-child natural entre el header y el main.
5. **Tabla de detalles** — filas con hover sutil (rgba 30,41,59 / 50%) y transición fluida.
6. **Panel transitions** — al usar `.vm-step-panel-active` (activado por JS en fase 1C), los paneles hacen fade+slide-up de 0.18s.
7. **Estado global badge** — transición de color al cambiar entre estados (listo/alerta/error).
8. **Filtros collapsibles** — detalles `<details>` con fade-in al expandirse.

---

## H. Desktop esperado (1440x900)

- Header sticky visible con título, badge de estado y botón cerrar
- Tabs de pasos alineados horizontalmente, tab activo con borde y fondo azul
- Panel principal (col-span-8) con contenido del paso visible
- Sidebar (col-span-4) sticky a la derecha, siempre visible al scrollear
- Cards con bordes `1.5px solid #334155`
- Campos vm-input/vm-select con bordes `1.5px solid #2d3748`
- Botón confirmar azul al fondo del sidebar
- Totales visibles en el sidebar sin scrollear

---

## I. Mobile esperado (390x844)

- Header compacto con título
- Tabs scrolleables horizontalmente sin scrollbar visible
- Sin sidebar al costado — apilado bajo el panel principal
- Mobile summary bar visible debajo del header con total y botón Confirmar compacto
- Campos con `min-height: 3rem` para facilidad de tap
- Padding reducido a `1rem` en grid para maximizar área de contenido
- Submodales dentro del viewport con `overflow-y: auto`

---

## J. Contratos preservados

Los siguientes IDs y atributos no fueron modificados:

`#modal-crear-venta`, `#venta-form`, `#btn-confirmar`, `#input-buscar-cliente`, `#dropdown-clientes`, `#hdn-cliente-id`, `#info-cliente`, `#input-buscar-producto`, `#dropdown-productos`, `#panel-agregar-producto`, `#hdn-producto-id`, `#txt-cantidad`, `#txt-descuento-item`, `#btn-agregar-producto`, `#tbody-detalles`, `#detalles-hidden-inputs`, `#select-tipo-pago`, `#total-subtotal`, `#total-descuento`, `#total-iva`, `#total-final`, `#hdn-subtotal`, `#hdn-descuento`, `#hdn-iva`, `#hdn-total`, `#panel-alerta-mora`, `#panel-cupo-insuficiente`, `#panel-documentacion-faltante`, `#VendedorUserId`, `#Observaciones`

Atributos preservados: `asp-action`, `asp-controller`, `asp-route`, `name`, `id`, `required`, `AntiForgeryToken`, `data-venta-modal-action`, `data-venta-modal-target`, `data-oc-scroll`, `role`, `aria-*`, `data-step`

Contratos JS: `VentaCrearModal.submit()`, `VentaCrearModal.open()`, `VentaCrearModal.close()` — no fueron afectados.

---

## K. Qué no se tocó

- Controllers
- Services
- Models / entidades
- Migrations
- Endpoints / payloads
- `wwwroot/js/venta-create.js`
- `wwwroot/css/venta-module.css` (solo adición vía partial — no se modificó su contenido)
- `wwwroot/css/shared-components.css`
- Lógica Razor (condiciones, helpers, ViewBag)
- Formularios (`name`, `id`, `required`, antiforgery)
- Playwright specs
- Tests de contrato UI

---

## L. Riesgo funcional

**Riesgo bajo.** Los cambios son CSS + clases en atributos `class` de botones de tab. El JS de `venta-create.js` lee los tabs por `id` (`#step-btn-cliente`, etc.), `aria-selected` y `data-step`. No lee las clases Tailwind eliminadas. Los tests de VentaCreate verifican IDs, `name` y contratos funcionales — no clases CSS de tabs.

**Riesgo único identificado:** Si algún selector CSS en `venta-module.css` o `tailwind.css` apuntaba a las cadenas de clase exactas de los tabs (improbable — Tailwind no genera selectores de clase compuestos de ese tipo), podría perder cobertura. Revisado: no hay tal selector.

---

## M. Validaciones ejecutadas

- `dotnet build --configuration Release`
- `dotnet test --configuration Release --filter "VentaCreate"`
- `git diff --check`
- `git status --short`

---

## N. Tests ejecutados

`dotnet test --configuration Release --filter "VentaCreate"` — ver resultado en sección M.

---

## O. Playwright ejecutado u omitido

**Pendiente de ejecutar.** No se ejecutó Playwright en esta fase porque la app no fue levantada. La fase es CSS-only + clases `class` en tabs. La correctitud del CSS puede verificarse con build + tests de contrato.

Para validar manualmente:
```powershell
$env:E2E_USER="Admin"
$env:E2E_PASS="Admin123!"
# Navegar a /Venta, abrir modal Nueva Venta
# Verificar desktop 1440x900: tabs visibles, sidebar sticky, sin overflow
# Verificar mobile 390x844: tabs scrolleables, summary bar visible, campos accesibles
```

---

## P. Deudas restantes

| Deuda | Prioridad | Fase |
|---|---|---|
| JS para activar/desactivar tabs al navegar entre pasos | Alta | 1C |
| JS para agregar `.vm-step-panel-active` al mostrar panel | Alta | 1C |
| JS para actualizar `#vm-estado-global` con `.vm-estado--*` | Alta | 1C |
| JS para mostrar/ocultar submodales (`modal-pago-item`, `modal-documentacion`) | Alta | 1C |
| Validar Playwright completo post-1C | Media | 1C |
| Revisión visual en 1280px (breakpoint intermedio) | Baja | Post-1C |
| `vm-mobile-summary-bar` con total real actualizado en tiempo real | Baja | Ya funciona vía MutationObserver en script inline |

---

## Q. Próximo prompt recomendado

**KIRA-VENTAS-MODAL-REWORK-1C — JS separado para pasos y submodales**

Implementar el JavaScript que:
- Activa/desactiva tabs usando las clases canónicas `.vm-step-tab--active`, `.vm-step-tab--complete`, `.vm-step-tab--warning`
- Muestra/oculta step panels con `.vm-step-panel-active`
- Gestiona los submodales `#modal-pago-item` y `#modal-documentacion`
- Actualiza `#vm-estado-global` con las clases `.vm-estado--*`
- Sin tocar `venta-create.js`
