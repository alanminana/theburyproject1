# UI-4C — Layout Mobile y Accesibilidad Avanzada

> Rama: `kira/ui-4c-layout-mobile-accesibilidad`
> Base: `main` en `64962bf` (UI-4B desktop rework)

## A. Objetivo

Mejorar el layout mobile y la accesibilidad avanzada del Layout global sin alterar lógica funcional, rutas ni módulos productivos. Sirve de continuación directa de UI-4B (desktop) y UI-4A (contratos).

## B. Archivos revisados

- `Views/Shared/_Layout.cshtml`
- `wwwroot/css/layout.css`
- `wwwroot/js/layout.js`
- `TheBuryProyect.Tests/Unit/LayoutUiContractTests.cs`
- Vistas de módulos (Dashboard, Venta, Caja, Cotizacion, Catalogo, Cliente): revisadas sin modificar.

## C. Archivos modificados

| Archivo | Cambios |
|---|---|
| `Views/Shared/_Layout.cshtml` | Skip-link, `aria-expanded`, `aria-controls`, `id="main-content"`, helper `NavCurrent`, `aria-current` en 10 nav links |
| `wwwroot/css/layout.css` | Estilos `.skip-link` |
| `wwwroot/js/layout.js` | `aria-expanded` sync, Escape, focus-trap liviano, retorno de foco, corrección localStorage |
| `TheBuryProyect.Tests/Unit/LayoutUiContractTests.cs` | 9 tests nuevos de contratos de accesibilidad |

## D. Diagnóstico mobile previo

| Aspecto | Estado anterior |
|---|---|
| `aria-expanded` en `#toggleSidebar` | Ausente |
| `aria-controls` en `#toggleSidebar` | Ausente |
| Skip-link al contenido principal | Ausente |
| `id` en `<main>` | Ausente |
| `aria-current="page"` en nav activo | Ausente |
| Escape para cerrar sidebar mobile | Ausente |
| Retorno de foco al cerrar mobile | Ausente |
| Focus-trap mientras sidebar abierto | Ausente |
| Conflicto localStorage / CSS auto-colapso | Presente: JS restauraba estado en rango 1024-1600px donde CSS ya forzaba colapso con `!important` |

## E. Cambios mobile aplicados

- **Botón hamburguesa**: se agrega `aria-expanded="false"` (JS lo sincroniza a `true`/`false`) y `aria-controls="sidebar"`.
- **Overlay**: sin cambios estructurales; comportamiento click-to-close preservado.
- **Escape**: nuevo listener `keydown` cierra el sidebar mobile al presionar Escape.
- **Focus-trap liviano**: mientras `#sidebar` tiene clase `.open`, Tab/Shift+Tab circula dentro del sidebar sin escapar al contenido oculto.
- **Retorno de foco**: al cerrar (por Escape, overlay o botón toggle), el foco vuelve a `#toggleSidebar`.
- **Conflicto localStorage/auto-colapso resuelto**: JS ya no restaura estado persistido cuando el viewport está en 1024–1600px (rango donde CSS auto-colapsa con `!important`).

## F. Cambios de accesibilidad aplicados

- **Skip-link**: `<a href="#main-content" class="skip-link">Ir al contenido principal</a>` insertado al inicio del `<body>`. Visible solo al recibir foco (`position: absolute; top: -100%` → `top: 0` en `:focus`).
- **`id="main-content"`** en `<main>`: destino del skip-link y landmark implícito de rol `main`.
- **`aria-current="page"`**: helper `NavCurrent(bool active)` retorna `"page"` o `null`. Razor omite el atributo cuando es null. Aplicado a los 10 links del sidebar: Ventas, Cotizaciones, Clientes, Inventario, Proveedores, Cajas, Reportes, Seguridad, Tickets, Plantillas.
- **Focus-visible CSS**: ya existía en UI-4B — sin cambios.
- **`aria-hidden="true"` en overlay**: ya existía — sin cambios.

## G. Contratos HTML/JS preservados

| Contrato | Estado |
|---|---|
| `id="sidebar"` | ✅ Preservado |
| `id="sidebarOverlay"` | ✅ Preservado |
| `id="toggleSidebar"` | ✅ Preservado |
| `id="collapseSidebar"` | ✅ Preservado |
| `id="btn-open-ticket-panel"` | ✅ Preservado |
| `id="confirmModal"` | ✅ Preservado |
| `id="confirmModalBody"` | ✅ Preservado |
| `id="confirmModalAction"` | ✅ Preservado |
| `[data-confirm-modal-close]` | ✅ Preservado |
| `window.TheBury` | ✅ Preservado |
| `openConfirmModal` / `closeConfirmModal` | ✅ Preservado |
| `.collapsed` / `.open` / `.active` | ✅ Preservados |
| `localStorage sidebar-collapsed` | ✅ Preservado |
| `RenderBody` / `Styles` / `Scripts` | ✅ Preservados |
| `aria-label="Abrir/cerrar menú"` | ✅ Preservado |
| `aria-label="Colapsar/expandir sidebar"` | ✅ Preservado |

## H. Tests agregados / modificados

Archivo: `TheBuryProyect.Tests/Unit/LayoutUiContractTests.cs`

9 tests nuevos bajo sección `UI-4C: contratos de accesibilidad mobile`:

| Test | Qué verifica |
|---|---|
| `Layout_TieneAriaExpandedEnToggleSidebar` | `aria-expanded="false"` presente en `#toggleSidebar` |
| `Layout_TieneAriaControlsEnToggleSidebar` | `aria-controls="sidebar"` presente |
| `Layout_TieneSkipLink` | clase `skip-link` y `href="#main-content"` presentes |
| `Layout_TieneMainContentId` | `id="main-content"` en `<main>` |
| `Layout_TieneAriaCurrentEnNavLinks` | `aria-current=` y `NavCurrent(` presentes en el template |
| `LayoutJs_SincronizaAriaExpanded` | `setAttribute('aria-expanded'` en layout.js |
| `LayoutJs_CierraConEscape` | `'Escape'` en layout.js |
| `LayoutJs_DevuelveFocoAlCerrarMobile` | `toggleBtn?.focus()` en layout.js |
| `LayoutCss_TieneSkipLink` | `.skip-link` en layout.css |

Total tests LayoutUiContractTests: **55/55**.

## I. Validaciones ejecutadas

```
dotnet build --configuration Release   → 0 errores, 0 advertencias
dotnet test --filter "LayoutUiContractTests" → 55/55 OK
dotnet test --filter "Layout|Shared|Navigation|Sidebar|Header|UiContract|Seguridad|Auth|Dashboard" → 228/228 OK
git diff --check → sin errores de whitespace (advertencia LF/CRLF de Windows: normal)
git status --short → 4 archivos modificados esperados
```

Verificación manual por diff:
- `#sidebar`, `#sidebarOverlay`, `#toggleSidebar`, `#collapseSidebar` ✅
- `.open`, `.collapsed`, `.active` ✅
- `localStorage sidebar-collapsed` ✅
- `RenderBody`, `RenderSection Styles/Scripts` ✅
- `aria-expanded` sincronizado en JS ✅
- `aria-current` aplicado en nav ✅
- Escape + overlay no rompen desktop (focus-trap solo activo cuando `.open` está presente, que es solo mobile) ✅

## J. Pruebas visuales / manuales

No ejecutadas en esta sesión (entorno sin navegador). Pruebas recomendadas:
1. Abrir Dashboard en desktop → sidebar desktop sin regresiones.
2. Achicar viewport a mobile (≤1023px) → sidebar oculto, hamburguesa visible.
3. Click hamburguesa → sidebar desliza, `aria-expanded="true"`.
4. Click overlay → sidebar cierra, foco vuelve al botón hamburguesa.
5. Abrir sidebar → presionar Escape → cierra, foco vuelve.
6. Navegar con Tab dentro del sidebar abierto → no escapa al contenido de fondo.
7. Tab desde fuera → skip-link recibe foco y se hace visible.
8. Activar skip-link → foco salta al contenido principal.
9. Revisar inspector: link activo tiene `aria-current="page"`, los demás no tienen el atributo.
10. Viewport 1400–1700px (notebook 125%) → sidebar auto-colapsado, JS no interfiere con localStorage.

## K. Riesgos / deudas

| Ítem | Nivel | Nota |
|---|---|---|
| Focus-trap no probado en navegador real | Bajo | Lógica estándar (`getFocusables` + Tab/Shift+Tab), bajo riesgo |
| `aria-current` en Razor: atributo omitido cuando null | Informativo | Comportamiento estándar de Razor para null attributes |
| Sin botón ×  explícito dentro del sidebar mobile | Bajo | Overlay + Escape + hamburguesa como toggle cubren el caso |
| Conflicto localStorage: collapseBtn en rango 1024–1600px actualiza localStorage aunque CSS ignora el estado | Bajo | El usuario puede presionar el botón, el localStorage se guarda pero el CSS lo ignora visualmente. Documentado, no resuelto — requeriría ocultar el botón en ese rango (alcance futuro) |

## L. Próximo paso recomendado

**UI-4D** (opcional): mejoras de UX mobile complementarias.
Candidatos:
- Botón ×  visible dentro del sidebar mobile.
- Tooltips en sidebar desktop colapsado (hover muestra nombre del ítem).
- Revisar header mobile: título de página y badges de cotizaciones en viewport pequeño.
- Pruebas visuales E2E con Playwright.

O cierre del rework visual con:
- **UI-5**: validación E2E visual (Playwright + screenshot regression).
- **UI-6**: mejoras UX en formularios/modales operativos.
