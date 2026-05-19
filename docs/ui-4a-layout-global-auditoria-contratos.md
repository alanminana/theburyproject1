# UI-4A — Auditoría Layout Global y Contratos

**Rama:** `kira/ui-4a-layout-global-auditoria-contratos`
**Estado:** Completado
**HEAD al inicio:** `4f68954`
**Fecha:** 2026-05-18

---

## A. Objetivo

Auditar el Layout global (`_Layout.cshtml`) y sus dependencias antes de aplicar el rework visual de UI-4B/UI-4C.
Mapear contratos HTML/JS, detectar riesgos, problemas de accesibilidad y mobile.
Crear tests de contrato mínimos para proteger regresiones.

---

## B. Archivos revisados

| Archivo | Tipo |
|---|---|
| `Views/Shared/_Layout.cshtml` | Layout canónico global |
| `Views/Shared/_ConfirmModal.cshtml` | Parcial modal confirmación |
| `Views/Shared/_TicketModal.cshtml` | Parcial modal ticket |
| `Views/Shared/_TicketPanel.cshtml` | Parcial panel tickets |
| `Views/Shared/_ValidationScriptsPartial.cshtml` | Parcial jQuery validation |
| `wwwroot/css/layout.css` | CSS sidebar/responsive |
| `wwwroot/css/shared-components.css` | CSS componentes canónicos |
| `wwwroot/css/standalone-tokens.css` | Tokens para páginas sin Layout |
| `wwwroot/js/layout.js` | JS sidebar toggle/collapse |
| `wwwroot/js/shared-ui.js` | JS utilitarios globales |

---

## C. Layout canónico detectado

**Archivo único:** `Views/Shared/_Layout.cshtml`

- `<html class="dark" lang="es">` — dark mode por clase en root
- `<body class="text-slate-100 font-display" style="background-color:#101622;">`
- Estructura raíz: `div.flex.h-screen.overflow-hidden`
  - `<aside id="sidebar">` — sidebar lateral fijo
  - `<div id="sidebarOverlay">` — overlay mobile
  - `<main class="flex-1 flex flex-col overflow-hidden">` — área de contenido
    - `<header>` — top bar h-16
    - `<div class="flex-1 overflow-y-auto">@RenderBody()</div>` — contenido de página

No hay layouts alternativos activos en el ERP (Login usa `Layout = null` + `standalone-tokens.css`).

---

## D. CSS global detectado

Cargado en orden en `_Layout.cshtml`:

1. **`tailwind.css`** — Tailwind v4 compilado (clases utilitarias de toda la UI)
2. **`layout.css`** — sidebar responsive, colapsable, scrollbar, escala tipográfica notebook
3. **`shared-components.css`** — sistema canónico de botones, cards, badges, tablas, filtros, row-actions
4. **Sección `@RenderSection("Styles")`** — inyección por vista (opcional)

**`standalone-tokens.css`** — NO cargado por el Layout; solo para Login/AccessDenied.

---

## E. JS global detectado

Cargado en orden al final del `<body>`:

| Orden | Archivo | Condicional |
|---|---|---|
| 1 | `jquery.min.js` | Siempre |
| 2 | `shared-ui.js` | Siempre |
| 3 | SignalR CDN + `notificaciones.js` | Solo si `canViewNotifications` |
| 4 | `layout.js` | Siempre |
| 5 | `ticket-modal.js` | Solo si `canCreateTicket` |
| 6 | `ticket-panel.js` | Solo si `canViewTickets` |
| 7 | `@RenderSection("Scripts")` | Inyección por vista |

---

## F. Contratos HTML/JS detectados

### IDs críticos en `_Layout.cshtml`

| ID | Usado por | Riesgo si se elimina |
|---|---|---|
| `#sidebar` | `layout.js` | Toggle/collapse deja de funcionar |
| `#sidebarOverlay` | `layout.js` | Overlay mobile no aparece/desaparece |
| `#toggleSidebar` | `layout.js` | Botón mobile no abre sidebar |
| `#collapseSidebar` | `layout.js` | Botón desktop no colapsa sidebar |
| `#collapseIcon` | `layout.css` | Ícono no rota al colapsar |
| `#btn-open-ticket-panel` | `ticket-panel.js` | El botón del header no abre el panel |

### IDs críticos en `_ConfirmModal.cshtml`

| ID | Usado por |
|---|---|
| `#confirmModal` | `shared-ui.js` — `openConfirmModal` / `closeConfirmModal` |
| `#confirmModalBody` | `shared-ui.js` — reemplaza texto del modal |
| `#confirmModalAction` | `shared-ui.js` — clona el botón para limpiar listeners |
| `[data-confirm-modal-close]` | `shared-ui.js` — delega cierre de modal |

### API global expuesta por `shared-ui.js`

| Función | Usada en |
|---|---|
| `window.TheBury` | Objeto global; múltiples módulos JS |
| `window.openConfirmModal(text, cb)` | Múltiples vistas (Venta, Caja, etc.) |
| `window.closeConfirmModal()` | Idem |
| `TheBury.confirmAction(msg, cb)` | Wrapper de confirmación |
| `TheBury.formatCurrency(value)` | Formato ARS en vistas |
| `TheBury.autoDismissToasts(delay)` | Toast auto-dismiss en módulos |
| `TheBury.normalizeText(value)` | Búsqueda accent-insensitive |

### Clases CSS usadas por `layout.js`

| Clase | Elemento | Descripción |
|---|---|---|
| `.collapsed` | `#sidebar` | Sidebar colapsado en desktop |
| `.open` | `#sidebar` | Sidebar abierto en mobile |
| `.active` | `#sidebarOverlay` | Overlay visible en mobile |
| `.sidebar-label` | elementos de texto | Ocultos al colapsar |

### localStorage

| Key | Valor | Descripción |
|---|---|---|
| `sidebar-collapsed` | `'1'` / `'0'` | Estado persistido del sidebar en desktop |

### Secciones Razor (contratos con vistas)

- `@RenderSection("Styles", required: false)` — vistas inyectan CSS adicional
- `@RenderSection("Modals", required: false)` — vistas inyectan modals propios
- `@RenderSectionAsync("Scripts", required: false)` — vistas inyectan JS propio

---

## G. Navegación / Sidebar / Header

### Sidebar

- `<aside id="sidebar">` — ancho `w-64` (256px), background `#101622`
- **Brand:** link a `Home/Index` con logo `bury.png`
- **Secciones de nav (condicionales por permisos):**
  - Operaciones: Ventas, Cotizaciones, Clientes
  - Logística: Inventario, Proveedores, Cajas
  - Reportes: Reportes
  - Sistemas: Seguridad, Tickets, Plantillas contrato
- **Estado activo:** helper `IsActive(controller, action?)` → clase `bg-primary/10 text-primary-on-dark`
- **User info (bottom):** nombre de usuario, rol/es, logout (form POST)
- **Collapse toggle (desktop):** `#collapseSidebar`, `#collapseIcon`, `hidden lg:flex`

### Header (top bar)

- `<header class="h-16 border-b border-slate-800">` — background `#101622`
- Izquierda: mobile toggle + page title + cotizaciones USD hardcodeadas
- Derecha: notificaciones (condicional), search (sin implementar), tickets (condicional), separador, help
- Cotizaciones: Dolar Blue `$1.245,00`, Tarjeta `$1.482,50`, Oficial `$942,00` — **datos estáticos hardcodeados**

---

## H. Menú mobile

| Elemento | Clase CSS trigger | Descripción |
|---|---|---|
| `#toggleSidebar` | `lg:hidden` | Botón solo visible en mobile/tablet |
| `#sidebar` | `.open` → `transform: translateX(0)` | Sidebar desliza desde la izquierda |
| `#sidebarOverlay` | `.active` → `display: block` | Overlay negro semitransparente |

- Sidebar en mobile: `position: fixed`, `z-index: 50`, inicia fuera de pantalla (`translateX(-100%)`)
- Overlay: `z-40`, cierra sidebar al hacer click
- Al cerrar: `closeSidebar()` → quita `.open` del sidebar y `.active` del overlay

---

## I. Riesgos por módulo

### Riesgos altos

1. **IDs del sidebar y overlay** — cualquier cambio de nombre en `#sidebar`, `#sidebarOverlay`, `#toggleSidebar`, `#collapseSidebar` rompe `layout.js` silenciosamente (los `?.` evitan excepciones pero la UI queda rota)

2. **`#btn-open-ticket-panel`** — único punto de entrada del botón "Incidentes" del header hacia `ticket-panel.js`; renombrarlo desconecta el panel sin error visible

3. **`#confirmModal` / `#confirmModalAction`** — usados por `window.openConfirmModal`; múltiples módulos dependen de esta API para confirmaciones destructivas

4. **Logout form** — requiere antiforgery token y ASP.NET Identity; no debe modificarse en rework visual

5. **Orden de carga JS** — `layout.js` depende de jQuery y `shared-ui.js`; alterar el orden rompe el sidebar y los modales

### Riesgos medios

6. **Dead code en `layout.js`**: `setupDropdown('notificacionesDropdown', 'notificacionesMenu')` y `setupDropdown('userMenuBtn', 'userMenu')` — ambos IDs no existen en el HTML actual. El código es inofensivo (`?.` en `getElementById`) pero indica que se eliminaron dropdowns de user/notificaciones y quedó código obsoleto.

7. **Cotizaciones USD hardcodeadas** en el header — al rediseñar el header en UI-4B habrá que decidir si se conectan a una API real o se eliminan

8. **`sidebar-collapsed` en localStorage** — si se cambia el mecanismo de colapso, los usuarios con sesión activa pueden ver comportamiento inconsistente al primer login post-deploy

9. **`@media (min-width: 1024px) and (max-width: 1600px)`** — auto-colapso para 125% scaling en notebooks 1080p; workaround que puede colisionar con cambios de ancho del sidebar

### Riesgos bajos

10. **Sección `Modals`** — si una vista inyecta un modal con `z-index` muy bajo puede quedar tapado por el overlay de ticket panel (`z-[54]`) o ticket modal (`z-[60]`)

---

## J. Problemas de accesibilidad detectados

| Severidad | Elemento | Problema |
|---|---|---|
| Media | Nav links en sidebar colapsado | Sin `title` ni `aria-label`; al colapsar, el link queda solo con el ícono sin texto accesible |
| Media | `text-[10px]` en rol de usuario (user info) | Tamaño de fuente muy pequeño (~10px); puede ser ilegible para baja visión |
| Media | `text-slate-400` en cabeceras de sección del nav | Contraste ~3.5:1 sobre `#101622`; OK para texto 10px **bold** (WCAG AA large text: 3:1) pero límite |
| Media | Botón search | Sin funcionalidad implementada; `<button>` sin acción confunde lectores de pantalla |
| Baja | Botón help | Sin `aria-label` explícito (solo title implícito del icon) |
| Baja | Botón notificaciones | Sin `aria-expanded` gestionado por JS; el estado no se comunica a lectores de pantalla |
| Baja | Cotizaciones USD en header | Solo visibles en `lg:flex`; el valor informativo no es accesible en mobile |
| Info | Estado activo en nav | `text-primary-on-dark` (#5b8dff) sobre `#101622` = ~6.1:1 ✓ WCAG AA |
| Info | Foco visible | `focus-visible` no definido en nav links; hereda Tailwind defaults (outline del navegador) |

---

## K. Problemas mobile/responsive

| Problema | Impacto |
|---|---|
| Auto-colapso CSS (`1024px–1600px`) + localStorage persisten estados contradictorios | En notebooks de 15" al 125% scaling, el sidebar se auto-colapsa por CSS pero localStorage puede indicar expandido → ícono de flecha queda invertido |
| Content area en mobile no tiene padding bottom extra para dispositivos con home indicator (safe area) | Puede cortar contenido en iPhones con notch si el ERP se usa en mobile browser |
| Sidebar mobile requiere 2 acciones (tap toggle + tap overlay) para abrir y cerrar | Flujo estándar, no es problema crítico |
| `ticket-panel` en mobile (`z-[55]`) es slide-over de 480px — en mobile <480px ocupa toda la pantalla | Aceptable, pero sin scroll nativo en iOS puede ser problemático |

---

## L. Duplicaciones detectadas

- **Ninguna duplicación de layout** — existe un único `_Layout.cshtml` canónico
- El código `setupDropdown` en `layout.js` es dead code (IDs no existen en HTML) — no es duplicación sino deuda técnica menor

---

## M. Tests existentes relacionados

| Filtro | Tests encontrados | Estado |
|---|---|---|
| `UiContract` | 5 archivos (Venta, Crédito) | 173 tests OK antes de UI-4A |
| `Dashboard` | `DashboardServiceTests.cs` | OK |
| `Seguridad` | Múltiples tests | OK |
| `Layout` | Ninguno antes de UI-4A | — |
| `Sidebar`, `Header`, `Navigation` | Ninguno | — |

---

## N. Tests agregados

**Archivo:** `TheBuryProyect.Tests/Unit/LayoutUiContractTests.cs`

**46 tests** creados y validados. Cubren:

| Categoría | Tests |
|---|---|
| `_Layout.cshtml` — estructura HTML | 12 |
| `_Layout.cshtml` — scripts globales | 5 |
| `_Layout.cshtml` — CSS global | 3 |
| `_Layout.cshtml` — permisos/roles | 2 |
| `_ConfirmModal.cshtml` — contratos | 4 |
| `shared-ui.js` — API global | 7 |
| `layout.js` — IDs críticos | 8 |
| `layout.css` — clases del sidebar | 4 |
| **Total** | **46** |

**Resultado:** 46/46 ✓

---

## O. Plan recomendado UI-4B — Layout desktop

**Objetivo:** Rework visual del Layout en desktop (sidebar, header) manteniendo todos los contratos detectados.

**Scope permitido:**

- Mejorar contraste del sidebar (`sidebar-label` secciones, estado hover, active)
- Agregar `title` / `aria-label` a los nav links para accesibilidad en modo colapsado
- Agregar `aria-label` al botón help y `aria-expanded` al botón notificaciones
- Revisar si el botón search se elimina o implementa
- Decidir qué hacer con cotizaciones hardcodeadas (API vs eliminación vs placeholder)
- Limpiar dead code de `setupDropdown` en `layout.js` (`notificacionesDropdown`, `userMenuBtn`)
- Ajustar `text-[10px]` del rol a mínimo `text-xs` (12px)
- Considerar `focus-visible` explícito en nav links

**Contratos que NO se deben romper (protegidos por UI-4A tests):**

- `#sidebar`, `#sidebarOverlay`, `#toggleSidebar`, `#collapseSidebar`, `#collapseIcon`
- `#btn-open-ticket-panel`
- `#confirmModal`, `#confirmModalBody`, `#confirmModalAction`, `[data-confirm-modal-close]`
- `window.TheBury`, `window.openConfirmModal`, `window.closeConfirmModal`
- Clases CSS `.collapsed`, `.open`, `.active`, `.sidebar-label`
- `localStorage` key `sidebar-collapsed`
- Orden de carga: jQuery → shared-ui.js → layout.js

---

## P. Plan recomendado UI-4C — Layout mobile y accesibilidad

**Objetivo:** Resolver issues de accesibilidad y mobile detectados en UI-4A.

**Scope sugerido:**

- Agregar `title` y `aria-label` a nav links para modo colapsado (mejora accesibilidad crítica)
- Revisar safe area inset en content area para iOS
- Revisar comportamiento de `z-index` entre sidebar mobile, ticket panel y modals
- Evaluar eliminar el dead code de `setupDropdown` en `layout.js`
- Validar foco visible en teclado en nav, sidebar collapse y header buttons
- Agregar `aria-expanded` al toggle del sidebar mobile

---

## Q. Deudas remanentes

1. **Dead code en `layout.js`**: `setupDropdown('notificacionesDropdown', ...)` y `setupDropdown('userMenuBtn', ...)` — IDs no presentes en HTML. Limpiar en UI-4B.
2. **Cotizaciones USD hardcodeadas** en header — requiere decisión de negocio antes de UI-4B.
3. **Focus visible** en nav links y botones del header — no hay `focus-visible` explícito; depende del outline del browser.
4. **Botón search** sin implementar — actualmente es un `<button>` sin acción; confunde usuarios de teclado/lectores.
5. **`text-[10px]` en rol de usuario** — tamaño límite para accesibilidad; evaluar aumentar a `text-xs`.

---

## R. Validaciones ejecutadas

| Validación | Resultado |
|---|---|
| `git status --short` al inicio | Limpio (`main` en `4f68954`) |
| `git fetch --all --prune && git pull` | Up to date |
| `dotnet build --configuration Release` | ✓ 0 errores, 0 advertencias |
| `dotnet test --filter "Layout|Shared|Navigation|Sidebar|Header|UiContract|Seguridad|Auth|Dashboard"` | ✓ 173/173 (pre UI-4A) |
| `dotnet test --filter "LayoutUiContract"` | ✓ 46/46 (tests nuevos) |
| `git diff --check` | Sin whitespace errors |
| `git status --short` final | Solo archivos de UI-4A |

---

## S. Archivos modificados en UI-4A

| Archivo | Acción |
|---|---|
| `TheBuryProyect.Tests/Unit/LayoutUiContractTests.cs` | Creado (46 tests de contrato) |
| `docs/ui-4a-layout-global-auditoria-contratos.md` | Creado (este documento) |

No se modificaron vistas, controllers, services, modelos, migraciones ni CSS/JS de producción.

---

## T. Próximo prompt recomendado: UI-4B Layout global desktop

**Nombre sugerido:** `UI-4B — Rework visual Layout desktop (sidebar, header)`

**Prerequisitos:**
- UI-4A completado y mergeado en main ✓
- Tests de contrato Layout 46/46 ✓
- Decisión tomada sobre cotizaciones hardcodeadas

**Alcance:**
- Mejorar contraste y jerarquía visual del sidebar
- Limpiar dead code en `layout.js`
- Mejorar accesibilidad: `aria-label` en nav links colapsados, `aria-expanded` en toggles, `aria-label` en botón help
- Ajustar tamaño de fuente del rol de usuario
- Validar con `LayoutUiContractTests` que no se rompen contratos
- Tests de regresión visual mínimos si aplica
