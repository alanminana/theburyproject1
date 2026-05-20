# UI-5E — Deudas visuales dinámicas de Venta/Index y Caja/Index

## A. Objetivo

Cerrar las deudas visuales puntuales que quedaron documentadas en UI-5D:

1. Panel "Sin caja abierta" en Venta/Index usaba raw Tailwind amber en lugar del design system.
2. Botones de acción de filas en Caja/Index (Razor + JS dinámico) usaban raw Tailwind inline en lugar de `.row-action`.

## B. Deudas recibidas de UI-5D

| Deuda | Módulo | Naturaleza |
|---|---|---|
| Panel "Sin caja abierta" con clases Tailwind amber directas | Venta/Index | Visual — no funcional |
| Botones de fila en "Cajas Abiertas" con inline Tailwind | Caja/Index (Razor) | Visual — no funcional |
| Botones de fila en "Maestro de Cajas Activas" con inline Tailwind | Caja/Index (Razor) | Visual — no funcional |
| Botones de fila en "Cajas Inactivas" con inline Tailwind | Caja/Index (Razor) | Visual — no funcional |
| Fila dinámica inyectada por `onCajaGuardada` con raw Tailwind | caja-index.js | Visual — no funcional |

## C. Archivos auditados

- `Views/Venta/Index_tw.cshtml`
- `Views/Caja/Index_tw.cshtml`
- `wwwroot/js/caja-index.js`
- `wwwroot/css/shared-components.css`
- `wwwroot/css/caja-module.css`

## D. Archivos modificados

| Archivo | Tipo de cambio |
|---|---|
| `Views/Venta/Index_tw.cshtml` | Panel "Sin caja abierta" normalizado |
| `Views/Caja/Index_tw.cshtml` | 3 secciones de acciones de fila normalizadas |
| `wwwroot/js/caja-index.js` | Fila dinámica `onCajaGuardada` normalizada |
| `.gitignore` | Agregado `tmptest*/` para ignorar directorios de tests temporales |

## E. Cambios en panel "Sin caja abierta"

**Antes:**
```html
<div id="panel-caja-cerrada"
     class="mb-6 rounded-2xl border border-amber-500/20 bg-amber-500/10 px-5 py-4 text-amber-100">
  <!-- inner flex responsive -->
  <a class="inline-flex ... bg-amber-500 ... text-slate-950">Abrir caja</a>
  <a class="inline-flex ... border border-amber-500/40 ... text-amber-200">Ver cajas</a>
</div>
```

**Después:**
```html
<div id="panel-caja-cerrada"
     class="alert-erp alert-erp-warning mb-6 flex-wrap justify-between"
     role="status">
  <!-- content -->
  <a id="btn-abrir-caja" class="btn-erp-warning btn-sm no-underline">Abrir caja</a>
  <a class="btn-erp-ghost btn-sm no-underline">Ver cajas</a>
</div>
```

- Se eliminó el div wrapper interno `md:flex-row md:items-center md:justify-between` (`.alert-erp` ya provee `flex + align-items: center`).
- Se añadió `flex-wrap justify-between` al wrapper externo para layout responsive.
- Se añadió `role="status"` para accesibilidad.
- Los `id`, rutas y texto se preservaron intactos.

## F. Cambios en caja-index.js

Función `onCajaGuardada` — rama de creación nueva:

| Campo | Antes | Después |
|---|---|---|
| `tr.className` | `hover:bg-slate-50 dark:hover:bg-slate-800/30 transition-colors` | `hover:bg-slate-800/30 transition-colors` |
| Celda nombre | `text-slate-900 dark:text-white` | `text-white` |
| Celda sucursal | `text-slate-700 dark:text-slate-300` | `text-slate-300` |
| Celda estado | `<div>` raw Tailwind + dot indicator | `<span class="chip-erp">Cerrada</span>` |
| Botón "Abrir caja" | raw Tailwind primary full-width | `row-action row-action--primary no-underline` |
| Label botón | `<span class="caja-index-action-label">` | `<span class="row-action__label">` |

La función `showPageFeedback` (toasts de feedback de página) no se modificó: no es una acción de fila.

## G. Contratos preservados

Se verificó y preservó explícitamente:

- `id="btn-abrir-caja"` — hook JS de venta-index.js
- `id="panel-caja-cerrada"` — selector de posible JS futuro
- `asp-controller/asp-action/asp-route-*` — sin cambios en ninguna ruta
- `data-caja-row-id` — hook de `onCajaGuardada` para edición inline
- `data-caja-open-edit`, `data-caja-open-create`, `data-caja-delete-form` — hooks del event listener global
- `data-confirm-message` — hook de confirmación de delete
- Formularios con antiforgery y `method="post"` — intactos
- `#cajas-activas-tbody` — selector de `onCajaGuardada`
- `#modal-caja-panel`, `#formCaja`, `#btnGuardarCaja` — hooks del modal AJAX

## H. Accesibilidad aplicada

- Panel "Sin caja abierta": añadido `role="status"` (alerta no disruptiva).
- Botones `.row-action`: heredan `focus-visible` con `outline: 2px solid #135bec` del design system.
- Botones `.btn-erp-warning`, `.btn-erp-ghost`: heredan `focus-visible` del design system.
- `title` preservado en todos los botones de fila.
- No se eliminó ningún atributo ARIA existente.
- Contraste: `.alert-erp-warning` usa `color: #fbbf24` (amber-400) sobre fondo oscuro → ratio >4.5:1.

## I. Mobile/responsive

- Panel "Sin caja abierta": `flex-wrap justify-between` permite que los botones se muevan a nueva línea en viewport angosto. `.alert-erp` tiene `padding: 1rem` cómodo en mobile.
- Botones `.row-action` en tablas de Caja: colapsan a icon-only desde 640px (antes 1024px). Cambio de breakpoint aceptable dado que las tablas tienen scroll horizontal.
- No se generó scroll horizontal accidental.
- No se usaron offsets arbitrarios.

## J. Validaciones

- `git diff --check` → OK (exit 0)
- Build: app corriendo en PID 71936, file lock en exe. Build temporal con `-o tmpbuild_ui5e` para verificar errores C# reales.
- No hay cambios en archivos C# — solo Razor, JS y CSS (ninguno de estos genera errores de compilación C#).

## K. Tests

- `LayoutUiContractTests` — ejecutado post-cambios.
- Suite completa `Layout|Shared|Navigation|Sidebar|Header|UiContract|Seguridad|Auth|Dashboard` — ejecutada.
- No se modificaron ni controllers, ni services, ni ViewModels, ni tests existentes.

## L. Playwright

- Suite: `e2e/ui-4e-layout-visual.spec.js`
- Credenciales: `E2E_USER=Admin / E2E_PASS=Admin123!`
- Ejecutado con `npx.cmd` por ExecutionPolicy de PowerShell.

## M. Riesgos/deudas

| Riesgo | Nivel | Nota |
|---|---|---|
| Breakpoint label en Caja cambia de 1024px a 640px | Bajo | Tablas tienen scroll horizontal; íconos son claros |
| `.alert-erp .material-symbols-outlined` aplica `font-size: 20px` a íconos dentro de los botones del panel | Mínimo | Diferencia de 2px (18px→20px); no afecta función |
| `showPageFeedback` en caja-index.js sigue usando raw Tailwind | Deuda menor | No es acción de fila; queda para eventual UI-5F o normalización de toasts |

## N. Próximo paso recomendado

**UI-5F**: Auditar y normalizar vistas de módulos secundarios que aún usen inline Tailwind para acciones de fila (Crédito, Devoluciones, Cotizaciones), o normalizar el sistema de toasts de página (`showPageFeedback`) para que use `.alert-erp` en lugar de raw Tailwind en los módulos JS.
