# UI-4B — Layout global desktop dark accesible

**Rama:** `kira/ui-4b-layout-global-desktop-dark-accesible`
**Estado:** Completado
**HEAD al inicio:** `6847922` (main)
**Fecha:** 2026-05-19

---

## A. Objetivo

Aplicar rework visual desktop al Layout global (`_Layout.cshtml`, `layout.css`, `layout.js`).
Mejorar sidebar, header, navegación, contraste, estado activo, hover/focus y legibilidad en desktop y notebook.
Mantener intactos todos los contratos HTML/JS detectados en UI-4A.

---

## B. Archivos revisados

| Archivo | Tipo |
|---|---|
| `Views/Shared/_Layout.cshtml` | Layout canónico global |
| `wwwroot/css/layout.css` | CSS sidebar/responsive |
| `wwwroot/css/shared-components.css` | CSS componentes canónicos (no modificado) |
| `wwwroot/js/layout.js` | JS sidebar toggle/collapse |
| `docs/ui-4a-layout-global-auditoria-contratos.md` | Auditoría previa (referencia) |

---

## C. Archivos modificados

| Archivo | Cambios |
|---|---|
| `Views/Shared/_Layout.cshtml` | Visual desktop: contraste, estados, aria, títulos |
| `wwwroot/css/layout.css` | Nuevas reglas: `.nav-item-active`, `focus-visible` |
| `wwwroot/js/layout.js` | Eliminación de dead code (`setupDropdown`, `closeAllDropdowns`) |

---

## D. Cambios visuales aplicados

### Sidebar

| Elemento | Antes | Después |
|---|---|---|
| Fondo del sidebar | `#101622` (idéntico al body) | `#0c111e` (diferenciado, da profundidad) |
| Borde derecho | `border-slate-800` (invisible) | `border-slate-700` (visible) |
| Bordes internos (user info, collapse) | `border-slate-800` | `border-slate-700` |
| Section headers: tamaño | `text-[10px]` (~10px) | `text-[11px]` (~11px) |
| Section headers: color | `text-slate-400` (~3.5:1) | `text-slate-300` (~5.1:1) |
| Estado activo nav: bg | `bg-primary/10` (10% opacity) | `bg-primary/20` (20% opacity) |
| Estado activo nav: indicador | solo color | color + `.nav-item-active` (left accent bar) |
| Hover nav inactivo | `hover:bg-slate-800` | + `hover:text-slate-100` |
| Rol de usuario: tamaño | `text-[10px]` (~10px) | `text-xs` (12px) |
| Rol de usuario: color | `text-slate-400` | `text-slate-300` |
| Avatar usuario: bg | `bg-slate-800` | `bg-slate-700` |
| Avatar usuario: icono | `text-slate-400` | `text-slate-300` |
| Logo brand: bg | `bg-slate-800` | `bg-slate-700` |
| Subtítulo brand | `text-slate-400` | `text-slate-300` |
| Focus visible (nav links, botones) | outline del browser | `outline: 2px solid #5b8dff` |

### Header

| Elemento | Antes | Después |
|---|---|---|
| Borde inferior | `border-slate-800` | `border-slate-700` |
| Cotizaciones labels: tamaño | `text-[10px]` (~10px) | `text-xs` (12px) |
| Cotizaciones labels: color | `text-slate-400` | `text-slate-300` |
| Botón search | sin `type`, sin `aria-label` | `type="button" aria-label="Buscar"` |
| Botón notifications | sin `type`, sin `aria-label` | `type="button" aria-label="Notificaciones"` |
| Botón help | sin `type`, sin `aria-label` | `type="button" aria-label="Ayuda"` |
| Íconos de botones | sin `aria-hidden` | `aria-hidden="true"` |
| Focus visible (botones) | outline del browser | `outline: 2px solid #5b8dff` |

### Nav links

| Cambio | Detalle |
|---|---|
| `title` attribute | Agregado en los 10 nav links + brand link |
| Accesibilidad colapsado | Con sidebar colapsado el tooltip `title` es legible |

### JavaScript

| Cambio | Detalle |
|---|---|
| Dead code eliminado | `setupDropdown()`, `closeAllDropdowns()`, las 2 llamadas y el listener global |
| Motivo | Los IDs `notificacionesDropdown`, `notificacionesMenu`, `userMenuBtn`, `userMenu` no existen en el HTML — código inofensivo pero confuso y sin efecto |

---

## E. Contratos preservados

| Contrato | Estado |
|---|---|
| `id="sidebar"` | ✓ |
| `id="sidebarOverlay"` | ✓ |
| `id="toggleSidebar"` + `aria-label="Abrir/cerrar menú"` | ✓ |
| `id="collapseSidebar"` + `id="collapseIcon"` + `aria-label="Colapsar/expandir sidebar"` | ✓ |
| `id="btn-open-ticket-panel"` | ✓ |
| `id="confirmModal"` / `#confirmModalBody` / `#confirmModalAction` | ✓ (en parciales, no tocadas) |
| `[data-confirm-modal-close]` | ✓ |
| `window.TheBury` / `openConfirmModal` / `closeConfirmModal` | ✓ (shared-ui.js no tocado) |
| Clases JS: `.collapsed`, `.open`, `.active` | ✓ |
| `localStorage` key `sidebar-collapsed` | ✓ |
| Orden de carga: jQuery → shared-ui.js → layout.js | ✓ |
| CSS: `#sidebar.collapsed`, `#sidebar.open`, `#sidebarOverlay.active` | ✓ |
| Secciones Razor: `Styles`, `Modals`, `Scripts` | ✓ |
| `@RenderBody()` | ✓ |
| Logout form ASP.NET Identity | ✓ (sin modificar) |
| `data-open-ticket-modal` | ✓ |

---

## F. Accesibilidad aplicada

| Mejora | Elemento |
|---|---|
| `focus-visible` explícito (2px solid #5b8dff) | nav links, botones sidebar, botones header |
| `aria-label` | search, notifications, help buttons |
| `aria-hidden="true"` | íconos decorativos en botones del header |
| `title` | 10 nav links + brand link (tooltip al colapsar) |
| Tamaño mínimo texto informativo | rol: 10px → 12px; section headers: 10px → 11px |
| Contraste labels → `text-slate-300` | section headers, rol, cotizaciones labels |
| Estado activo no solo dependiente del color | left accent bar (`.nav-item-active`) |

---

## G. Desktop / notebook

- Sidebar diferenciado del body con fondo `#0c111e` y borde `border-slate-700`.
- Estado activo más visible: `bg-primary/20` + indicador lateral izquierdo.
- Hover con texto `text-slate-100` para mejor legibilidad.
- Section headers más legibles (`text-[11px] text-slate-300`).
- Datos del usuario más legibles (rol `text-xs text-slate-300`).
- Botones del header más accesibles.
- Aplicado con el mismo sizing del notebook 15" (ver media query 1024–1600px en layout.css).

---

## H. Mobile — no abordado / preservado

- Sidebar mobile (`position: fixed`, `transform: translateX(-100%)`) sin cambios funcionales.
- Toggle mobile `#toggleSidebar` preservado con su `aria-label`.
- Overlay `#sidebarOverlay` preservado.
- Transiciones mobile (`.open` / `.active`) preservadas.
- El rework mobile completo queda pendiente para UI-4C.
- Las mejoras de color/contraste también benefician mobile sin romperlo.

---

## I. Validaciones ejecutadas

| Validación | Resultado |
|---|---|
| `dotnet build --configuration Release` | ✓ 0 errores |
| `dotnet test --filter "LayoutUiContractTests"` | ✓ 46/46 |
| `dotnet test --filter "Layout\|Shared\|Navigation\|..."` | ✓ 219/219 |
| `git diff --check` | ✓ sin whitespace errors |
| `git status --short` | 3 archivos correctos |
| Contratos HTML críticos verificados por grep | ✓ todos presentes |

---

## J. Tests ejecutados

| Suite | Resultado |
|---|---|
| `LayoutUiContractTests` — 46 tests | ✓ 46/46 |
| Layout, Shared, Navigation, Sidebar, Header | ✓ 219/219 (incluye UiContract, Seguridad, Auth, Dashboard) |

Los 46 tests de contrato de UI-4A pasan sin modificaciones.

---

## K. Riesgos / deudas remanentes

| Deuda | Estado |
|---|---|
| Dead code `setupDropdown` en layout.js | **Eliminado** en UI-4B |
| Cotizaciones USD hardcodeadas | Mejorado visualmente (contraste), pero datos siguen estáticos — decisión de negocio pendiente |
| Focus-visible implícito | **Resuelto** — reglas explícitas en layout.css |
| `text-[10px]` en rol/section headers | **Resuelto** — aumentado a text-xs / text-[11px] |
| `text-slate-400` en información operativa | **Resuelto** — subido a text-slate-300 |
| `aria-label` en botones del header | **Resuelto** — search, notifications, help |
| Nav links sin title para modo colapsado | **Resuelto** — title agregado en 11 links |
| Conflicto localStorage vs CSS auto-colapso | No abordado — deuda técnica media, afecta notebooks 125% scaling. Queda para UI-4C. |
| Rework mobile completo | No abordado — queda para UI-4C |
| `aria-expanded` en toggle mobile | No implementado — deuda de accesibilidad, queda para UI-4C |

---

## L. Próximo paso recomendado: UI-4C

**Nombre sugerido:** `UI-4C — Layout mobile y accesibilidad avanzada`

**Scope sugerido:**
- `aria-expanded` en toggle mobile sidebar
- Safe area insets para iOS en content area
- Revisar z-index entre sidebar mobile, ticket panel y modals
- Resolver conflicto localStorage vs CSS auto-colapso en notebooks 125%
- Validar foco en teclado end-to-end (sidebar → header → contenido)
- Evaluar `aria-current="page"` en nav link activo (semántica de nav accesible)
- Revisar si `btn-open-ticket-panel` en header necesita `aria-expanded` al abrir el panel
