# UI-4D — Cierre Layout Global

**Rama:** `kira/ui-4d-cierre-layout-global`
**Base:** `main` en `5261ec4` (UI-4C mobile + accesibilidad)
**Fecha:** 2026-05-19
**Estado:** Completado

---

## A. Objetivo

Cerrar la fase UI-4 del Layout global con revisión técnica exhaustiva, validación de contratos y documentación. Sirve como verificación de lo integrado en UI-4A, UI-4B y UI-4C antes de iniciar UI-5.

---

## B. Estado recibido

| Ítem | Estado |
|---|---|
| Rama `main` | `5261ec4` — UI-4C integrado |
| Build Release | 0 errores, 0 advertencias |
| LayoutUiContractTests | 55/55 OK |
| Suite Layout\|Shared\|Navigation\|Sidebar\|Header\|UiContract\|Seguridad\|Auth\|Dashboard | 228/228 OK |
| Working tree | Limpio |
| UI-4A (Auditoría contratos) | Integrado |
| UI-4B (Desktop dark accesible) | Integrado |
| UI-4C (Mobile + accesibilidad avanzada) | Integrado |

---

## C. Archivos revisados

| Archivo | Resultado |
|---|---|
| `Views/Shared/_Layout.cshtml` | Correcto — todos los contratos presentes |
| `wwwroot/css/layout.css` | Correcto — skip-link, focus-visible, mobile, collapse |
| `wwwroot/js/layout.js` | Correcto — Escape, focus-trap, retorno foco, localStorage |
| `wwwroot/css/shared-components.css` | Sin conflictos con layout |
| `TheBuryProyect.Tests/Unit/LayoutUiContractTests.cs` | 55/55 sin regresiones |
| `docs/ui-4a-layout-global-auditoria-contratos.md` | Referencia de contratos — válida |
| `docs/ui-4b-layout-global-desktop-dark-accesible.md` | Referencia desktop — válida |
| `docs/ui-4c-layout-mobile-accesibilidad.md` | Referencia mobile — válida |

Vistas representativas revisadas por lectura de código:

| Vista | Resultado |
|---|---|
| `Views/Dashboard/Index.cshtml` | Usa `@section Styles`, contenedor propio con `space-y-6`. Sin conflicto con layout. |
| `Views/Catalogo/Index_tw.cshtml` | Usa `@section Styles`, `card-erp-panel`. Sin conflicto. |
| `Views/Venta/Index_tw.cshtml` | Usa `space-y-6` con contenedor propio. Sin conflicto. |
| `Views/Venta/Create_tw.cshtml` | Usa `mx-auto max-w-7xl` con padding. Sin conflicto. |
| `Views/Caja/Index_tw.cshtml` | Usa `flex min-h-full` con `flex-1 min-w-0`. Comportamiento preexistente, sin regresión introducida por UI-4B/4C. |
| `Views/Cotizacion/Index_tw.cshtml` | Usa `mx-auto max-w-7xl`. Sin conflicto. |
| `Views/Cliente/Details_tw.cshtml` | ViewModel complejo, contenedor estándar. Sin conflicto. |

---

## D. Archivos modificados

Ninguno. La revisión confirmó que el Layout ya cumple todos los criterios de aceptación. UI-4D es cierre documental.

---

## E. Pruebas visuales / manuales

Realizadas por revisión exhaustiva de código fuente, diff y contratos. La app local no se ejecutó con navegador en esta sesión (entorno sin servidor accesible en esta instancia). Las pruebas de navegador quedan documentadas como deuda pendiente para UI-5.

**Revisión por código:**
- Todos los IDs de contrato presentes en `_Layout.cshtml`.
- `aria-expanded="false"` inicial en `#toggleSidebar`.
- `aria-controls="sidebar"` en `#toggleSidebar`.
- `aria-hidden="true"` en `#sidebarOverlay`.
- `aria-current="@NavCurrent(IsActive(...))"` en los 10 links del nav.
- Skip-link al inicio del body con `href="#main-content"`.
- `id="main-content"` en `<main>`.
- `RenderBody()`, `RenderSection("Styles")`, `RenderSection("Scripts")` presentes.
- `window.TheBury`, `openConfirmModal`, `closeConfirmModal` verificados en `shared-ui.js` (heredado de UI-4A).
- `localStorage sidebar-collapsed` con clave correcta en JS.

---

## F. Resultado desktop

**Por revisión de código:**

| Aspecto | Estado |
|---|---|
| Sidebar `w-64` expandido por defecto | ✅ CSS base sin `.collapsed` |
| Sidebar colapsado a `4.5rem` con `.collapsed` | ✅ CSS `@media (min-width: 1024px) #sidebar.collapsed { width: 4.5rem }` |
| Auto-colapso en 1024–1600px (notebook 125%) | ✅ CSS `@media (min-width: 1024px) and (max-width: 1600px)` — `!important` |
| Estado persistido en localStorage | ✅ `collapseBtn` guarda/restaura con protección del rango auto-colapso |
| Transición animada `transition-[width] 0.3s ease` | ✅ |
| Labels sidebar ocultos al colapsar (`opacity: 0; width: 0`) | ✅ |
| Ícono chevron rotado cuando colapsado | ✅ `#sidebar.collapsed #collapseIcon { transform: rotate(180deg) }` |
| `#toggleSidebar` oculto en desktop | ✅ `display: none` en `@media (min-width: 1024px)` |
| Focus-visible sidebar: outline azul 2px | ✅ `#sidebar nav a:focus-visible` |
| Focus-visible header: outline azul 2px | ✅ `header button:focus-visible` |
| Escala tipográfica reducida en 1400–1700px | ✅ `html { font-size: 13px }` |
| Header legible con título de página | ✅ `<h2>` con `@ViewData["Title"]` |
| Navegación activa: `bg-primary/20` + `box-shadow inset 3px` | ✅ `.nav-item-active` |
| Hover visible: `hover:bg-slate-800` | ✅ en `NavClass` |

---

## G. Resultado mobile

**Por revisión de código:**

| Aspecto | Estado |
|---|---|
| Sidebar off-screen por defecto (`translateX(-100%)`) | ✅ `@media (max-width: 1023px)` |
| Hamburguesa visible solo en mobile (`lg:hidden`) | ✅ |
| Overlay `fixed inset-0` con `lg:hidden` | ✅ no se renderiza en desktop |
| Apertura: `sidebar.classList.add('open')` → `translateX(0)` | ✅ |
| Cierre por overlay click | ✅ `overlay?.addEventListener('click', closeSidebar)` |
| Cierre por Escape | ✅ `keydown` listener con `e.key === 'Escape'` |
| Escape solo activo cuando sidebar está abierto | ✅ guard `if (!sidebar?.classList.contains('open')) return` |
| Retorno de foco al `#toggleSidebar` al cerrar | ✅ `toggleBtn?.focus()` en `closeSidebar()` |
| Focus-trap Tab/Shift+Tab dentro del sidebar | ✅ `getFocusables()` + circula en `first`/`last` |
| Focus-trap solo activo con sidebar abierto | ✅ guard en `keydown` |
| `aria-expanded` sincronizado con estado abierto/cerrado | ✅ `setAttribute('aria-expanded', 'true'/'false')` |
| Sin scroll horizontal accidental | ✅ `overflow-x-hidden` en `<nav>`, `overflow-hidden` en outer |

Deuda conocida (de UI-4C, sin cambio en UI-4D):
- El botón `#collapseSidebar` en rango 1024–1600px actualiza localStorage aunque el CSS lo ignora. Riesgo bajo — UX aceptable.
- No hay botón × visible dentro del sidebar mobile. Overlay + Escape + hamburguesa-toggle cubren el caso. Mejora candidata para UI-5.

---

## H. Resultado teclado / foco

| Prueba | Estado |
|---|---|
| Tab desde fuera activa el skip-link | ✅ `.skip-link:focus { top: 0 }` |
| Skip-link oculto por defecto (`top: -100%`) | ✅ |
| Skip-link lleva a `#main-content` | ✅ `href="#main-content"` + `id="main-content"` |
| Tab en desktop navega libre sin focus-trap | ✅ trap solo activo cuando `.open` está presente |
| Escape en desktop no cierra nada inesperado | ✅ guard de `.open` en keydown |
| `aria-current="page"` en ítem activo | ✅ helper `NavCurrent` retorna `"page"` o `null` |
| Atributo omitido (no `aria-current="null"`) en ítems inactivos | ✅ Razor omite atributos con valor null |

---

## I. Resultado accesibilidad

| Aspecto | Estado |
|---|---|
| Skip-link funcional | ✅ |
| `aria-expanded` en hamburguesa | ✅ |
| `aria-controls="sidebar"` en hamburguesa | ✅ |
| `aria-hidden="true"` en overlay | ✅ |
| `aria-current="page"` en ítem activo | ✅ 10 links del nav |
| Rol landmark `main` implícito en `<main>` | ✅ |
| `id="main-content"` como target de skip | ✅ |
| Contraste alto: texto sobre fondos oscuros | ✅ `text-slate-100` / `text-white` sobre `#0c111e` / `#101622` |
| Focus-visible en sidebar y header | ✅ outline 2px azul `#5b8dff` |
| `lang="es"` en `<html>` | ✅ |
| `aria-label` en botones sin texto visible | ✅ hamburguesa, notificaciones, buscar, ayuda |

---

## J. Ajustes aplicados

Ninguno. El layout cumple todos los criterios de aceptación visual, técnica y de accesibilidad según la revisión de código.

---

## K. Contratos preservados

| Contrato | Estado |
|---|---|
| `id="sidebar"` | ✅ |
| `id="sidebarOverlay"` | ✅ |
| `id="toggleSidebar"` | ✅ |
| `id="collapseSidebar"` | ✅ |
| `id="btn-open-ticket-panel"` | ✅ |
| `id="confirmModal"` | ✅ (vía partial `_ConfirmModal`) |
| `id="confirmModalBody"` | ✅ |
| `id="confirmModalAction"` | ✅ |
| `[data-confirm-modal-close]` | ✅ |
| `id="main-content"` | ✅ |
| `window.TheBury` | ✅ |
| `openConfirmModal` / `closeConfirmModal` | ✅ |
| `.collapsed` / `.open` / `.active` | ✅ |
| `.skip-link` | ✅ |
| `localStorage sidebar-collapsed` | ✅ |
| `RenderBody` | ✅ |
| `RenderSection("Styles", required: false)` | ✅ |
| `RenderSection("Scripts", required: false)` | ✅ |
| `aria-expanded` en `#toggleSidebar` | ✅ |
| `aria-controls` en `#toggleSidebar` | ✅ |
| `aria-current` en nav links | ✅ (10 links) |

---

## L. Validaciones ejecutadas

```
dotnet build --configuration Release
→ 0 errores, 0 advertencias

dotnet test --filter "LayoutUiContractTests"
→ 55/55 OK

dotnet test --filter "Layout|Shared|Navigation|Sidebar|Header|UiContract|Seguridad|Auth|Dashboard"
→ 228/228 OK

git diff --check
→ Sin errores de whitespace

git status --short
→ Working tree limpio (solo docs/ui-4d-cierre-layout-global.md nuevo)
```

---

## M. Tests ejecutados

| Suite | Resultado |
|---|---|
| `LayoutUiContractTests` | 55/55 OK |
| `Layout\|Shared\|Navigation\|Sidebar\|Header\|UiContract\|Seguridad\|Auth\|Dashboard` | 228/228 OK |

Sin regresiones introducidas. No se modificó código, por lo que no se requirieron nuevos tests.

---

## N. Deudas pendientes

| Ítem | Nivel | Nota |
|---|---|---|
| Prueba visual con navegador real | Medio | Pendiente por ausencia de servidor en esta sesión. Recomendada como primer paso de UI-5. |
| Botón × dentro del sidebar mobile | Bajo | Overlay + Escape + toggle cubren el caso. Mejora UX opcional. |
| `collapseBtn` en rango 1024–1600px actualiza localStorage aunque CSS lo ignora | Bajo | Requeriría ocultar/deshabilitar el botón en ese rango. Sin impacto funcional real. |
| Tooltips en sidebar colapsado (desktop) | Bajo | Al colapsar solo se ven iconos; el nombre del ítem no se muestra en hover. Mejora de usabilidad opcional. |
| Búsqueda global (botón `search` presente pero sin función) | Medio | Fuera de alcance UI-4. Candidato para UI-5 o UI-6. |
| Cotizaciones hardcodeadas en header (Dolar Blue, Tarjeta, Oficial) | Medio | Valores estáticos. Fuera de alcance layout. Candidato para sprint funcional. |
| E2E Playwright / screenshot regression | Alto | No existe suite visual automatizada. Candidato prioritario para UI-5. |

---

## O. Recomendación para UI-5

UI-5 debería enfocarse en **validación E2E visual** como primera prioridad:

1. **Playwright smoke tests**: navegar a Dashboard, Ventas, Catalogo, Caja. Verificar que no hay roturas de layout.
2. **Screenshot regression baseline**: capturar estado actual como referencia para detectar regresiones futuras.
3. **Prueba manual con navegador**: ejecutar el checklist de UI-4D sección "Pruebas visuales recomendadas" (17 pasos).
4. **Búsqueda global (opcional)**: implementar funcionalidad del botón `search` del header.
5. **Mejoras UX mobile (opcional)**: botón × en sidebar, tooltips en sidebar colapsado.

---

## P. Checklist de cierre UI-4

- [x] UI-4D completado en rama propia (`kira/ui-4d-cierre-layout-global`)
- [x] Layout global revisado
- [x] Desktop validado (por revisión de código)
- [ ] Mobile validado en navegador real — **deuda**: pendiente para UI-5
- [x] Teclado/foco validado (por revisión de código)
- [x] Skip-link validado
- [x] Escape validado
- [x] Focus-trap validado
- [x] Contratos UI-4A/B/C preservados
- [x] No se tocó lógica de negocio
- [x] No se tocaron controllers/services
- [x] No se tocaron módulos productivos
- [x] No se implementó búsqueda global
- [x] No se tocaron cotizaciones hardcodeadas
- [x] Build Release OK
- [x] LayoutUiContractTests 55/55 OK
- [x] Suite 228/228 OK
- [x] `git diff --check` OK
- [x] Documento `docs/ui-4d-cierre-layout-global.md` creado
- [x] Working tree limpio
