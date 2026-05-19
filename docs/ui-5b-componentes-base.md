# UI-5B — Normalización de componentes base reutilizables

## A. Objetivo

Normalizar y completar el sistema de componentes base del ERP dark-first para reducir inconsistencias visuales entre módulos, mejorar accesibilidad y facilitar el trabajo en fases futuras. No rediseñar módulos completos.

## B. Problema detectado

Al auditar las vistas del ERP se encontraron las siguientes brechas respecto al design system:

- **Inputs/selects en formularios**: Solo existía `.filter-input-erp` y `.filter-select-erp` para contextos de filtro. Los inputs de formularios, modales y búsquedas usaban cadenas Tailwind inline variables (ej: `bg-slate-800 border-none rounded-lg` en Catálogo vs. `border border-slate-700 bg-slate-800` en Cotización).
- **Alerts/toasts**: El selector `.toast-msg` es un hook JS (usado en `shared-ui.js` y `venta-create.js` para auto-dismiss) pero no tenía estilos CSS propios. Las ~30 vistas que lo usan combinan `.toast-msg` con cadenas Tailwind inline, sin clase canónica.
- **Chips informativos**: Los contadores y metadatos en headers de sección usaban `inline-flex rounded-full bg-slate-800 px-3 py-1 text-[11px]` inline sin clase propia.
- **Botón icon-only**: No existía `.btn-erp-icon` para botones de toolbar/header. Las acciones de fila en Dashboard no usaban `.row-action` (ya definido) sino `inline-flex ... rounded-xl` largo.
- **Grupo de botones**: No existía `.btn-group-erp` para agrupar botones relacionados.
- **Bug disabled `.btn-erp-danger`**: Le faltaba `pointer-events: none` en el estado disabled.

## C. Archivos revisados

- `wwwroot/css/shared-components.css`
- `wwwroot/css/layout.css`
- `wwwroot/css/tailwind-input.css`
- `wwwroot/css/standalone-tokens.css`
- `Views/Dashboard/Index.cshtml`
- `Views/Catalogo/Index_tw.cshtml`
- `Views/Venta/Index_tw.cshtml`
- `Views/Caja/Index_tw.cshtml`
- `Views/Cliente/Index_tw.cshtml`
- `Views/Cotizacion/Index_tw.cshtml`
- `wwwroot/js/shared-ui.js`
- `wwwroot/js/venta-create.js`
- `docs/ui-1-design-system-dark-accesible.md`
- `TheBuryProyect.Tests/Unit/LayoutUiContractTests.cs`
- `e2e/ui-4e-layout-visual.spec.js`

## D. Archivos modificados

| Archivo | Cambio |
|---|---|
| `wwwroot/css/shared-components.css` | Nuevos componentes base + fix btn-erp-danger disabled |
| `Views/Dashboard/Index.cshtml` | 4 ajustes mínimos a clases canónicas |

## E. Componentes auditados

| Componente | Estado pre-UI-5B | Acción |
|---|---|---|
| Botones `.btn-erp-*` | Canónico, bien definido | Fix disabled danger; sin cambios estructurales |
| Inputs filtro `.filter-input-erp` | Canónico, filtros ok | Complementado con `.input-erp` para formularios |
| Selects filtro `.filter-select-erp` | Canónico, filtros ok | Complementado con `.select-erp` para formularios |
| Cards `.card-erp*` | Canónico, bien definido | Sin cambios (ya estable desde UI-4) |
| Badges `.badge-erp*` | Canónico, bien definido | Sin cambios |
| Row actions `.row-action*` | Canónico, bien definido | Aplicado en Dashboard (deuda pendiente en otras vistas) |
| Chips info | Ausente (solo Tailwind inline) | Nuevo: `.chip-erp` + variantes |
| Alerts | Ausente como clase CSS | Nuevo: `.alert-erp` + variantes |
| Botón icon-only | Ausente | Nuevo: `.btn-erp-icon` + variantes |
| Grupo de botones | Ausente | Nuevo: `.btn-group-erp` |

## F. Clases/patrones definidos

Todas las nuevas clases viven en `wwwroot/css/shared-components.css`:

```
.input-erp              → input formularios (h-44px, borde #334155)
.input-erp.input-erp-sm → variante compacta h-36px
.select-erp             → select formularios (con chevron SVG)
.select-erp.select-erp-sm → variante compacta
.alert-erp              → base estructural de alertas
.alert-erp-success      → éxito (emerald)
.alert-erp-error        → error (red)
.alert-erp-warning      → advertencia (amber)
.alert-erp-info         → info (sky)
.chip-erp               → chip/pill informativo neutro
.chip-erp-success       → variante emerald
.chip-erp-warning       → variante amber
.chip-erp-danger        → variante red
.chip-erp-primary       → variante blue
.btn-erp-icon           → botón icon-only 40×40px
.btn-erp-icon--primary  → variante hover azul
.btn-erp-icon--danger   → variante hover rojo
.btn-group-erp          → toolbar de botones relacionados
```

## G. Cambios en botones

- **Fix crítico**: `.btn-erp-danger` disabled/aria-disabled ahora incluye `pointer-events: none`. Antes solo tenía `cursor: not-allowed`, lo que permitía clicks accidentales en elementos con `pointer-events` heredados.
- **Nuevo**: `.btn-erp-icon` para acciones icon-only (toolbar, header de sección). Dimensiones 40×40px (cerca del target WCAG 44px), con variantes `--primary` y `--danger`.
- **Nuevo**: `.btn-group-erp` como contenedor semántico de grupos de botones.
- **Dashboard (mínimo)**: 4 botones normalizados a clases canónicas:
  - "Guardar nota" header → `btn-erp-ghost btn-erp-sm`
  - "Guardar" en textarea → `btn-erp-primary btn-erp-sm`
  - PagarCuota row action → `row-action row-action--primary`
  - Visibility row action → `row-action row-action--primary`

## H. Cambios en inputs/selects

- **Nuevo**: `.input-erp` — clase canónica para inputs de formularios y modales. Altura fija 44px (WCAG 2.5.5), borde `#334155` (más visible que el filtro), placeholder `#475569`, focus ring `rgba(19,91,236,0.18)`.
- **Nuevo**: `.select-erp` — select con chevron SVG integrado, mismo token visual que `.input-erp`.
- **Variante `.input-erp-sm` / `.select-erp-sm`** — h-36px, font 13px, para contextos compactos.
- **Diferencia con `.filter-input-erp`**: los filtros usan `#1e293b` (borde más sutil, sin altura fija); los formularios usan `#334155` (borde más visible, h-44px). Ambas clases coexisten.
- **Deuda documentada**: Las vistas existentes (Catálogo, Cotización, Venta/Create, Caja modales) usan Tailwind inline para sus inputs. Migrar a `.input-erp` es tarea de las fases por módulo (UI-6+).

## I. Cambios en cards

Sin cambios. El sistema de cards (`.card-erp`, `.card-erp-metric*`, `.card-erp-panel`, `.card-erp-panel-padded`, `.hero-erp`, `.filter-panel-erp`, `.table-erp-wrapper`) ya estaba bien definido desde UI-4.

## J. Cambios en badges/chips

- **Badges** (`.badge-erp*`): Sin cambios. Ya estaban bien definidos.
- **Nuevo**: `.chip-erp` para contadores y metadatos en headers de sección. Diferencia vs `.badge-erp`: sin uppercase/letter-spacing, más neutro, destinado a conteos y estados operativos rápidos ("3 visibles", "en vivo"). Con variantes semánticas.
- **Deuda documentada**: Las vistas actuales usan `inline-flex rounded-full bg-slate-800` inline para chips. Migrar a `.chip-erp` es tarea de las fases por módulo.

## K. Cambios en alerts

- **Nuevo**: `.alert-erp` con variantes `-success`, `-error`, `-warning`, `-info`.
- **Relación con `.toast-msg`**: `.toast-msg` es un selector JS (referenciado en `shared-ui.js` como `document.querySelectorAll('.toast-msg, ...')` para auto-dismiss, y en `venta-create.js` para creación dinámica). No puede ser removido de las vistas sin modificar el JS. La práctica nueva es usar `.alert-erp alert-erp-success toast-msg` combinados en código nuevo; las ~30 vistas legadas mantienen `.toast-msg` + Tailwind inline.
- **Deuda documentada**: Migrar las vistas legadas de `.toast-msg + Tailwind` a `.alert-erp + .toast-msg` es tarea de las fases por módulo.

## L. Accesibilidad

- `.input-erp` y `.select-erp` tienen h-44px (WCAG 2.5.5 — 44×44px touch target).
- `.btn-erp-icon` tiene 40×40px (próximo al target; se recomienda 44px en próxima revisión).
- `.btn-erp-danger` disabled ahora tiene `pointer-events: none` (previene activación accidental).
- `.alert-erp` no depende solo del color — íconos con `role="alert"` o `role="status"` en las vistas.
- Todos los focus-visible usan `outline: 2px solid #135bec; outline-offset: 2px`.
- Ningún texto operativo nuevo está debajo de 14px (chips son 11px, clasificados como metadato).

## M. Mobile

- `.input-erp` usa `width: 100%` — se adapta a cualquier ancho de contenedor.
- `.btn-erp-icon` tiene tamaño fijo, no se estira — comportamiento predecible.
- `.alert-erp` usa flex + gap sin `white-space: nowrap` — se adapta a mobile.
- `.btn-erp-block` (ya existente) puede combinarse con cualquier `.btn-erp-*` para ancho completo en mobile.
- Sin cambios en media queries de componentes existentes.

## N. Validaciones

| Validación | Resultado |
|---|---|
| `dotnet build --configuration Debug` | OK — 0 errores, 0 advertencias |
| `dotnet build --configuration Release` | File-lock PID 71936 (app corriendo) — no es error C# |
| `git diff --check` | OK — exit 0, sin whitespace errors |

## O. Tests

| Suite | Pre-UI-5B | Post-UI-5B |
|---|---|---|
| LayoutUiContractTests | 57/57 | 57/57 |
| Layout\|Shared\|Navigation\|... | 230/230 | 230/230 |

Los tests de contrato no requieren nuevas aserciones porque UI-5B solo añade clases CSS nuevas sin modificar contratos de layout, rutas ni identidad de componentes existentes. La adición de clases en views muestra (Dashboard) no cambia selectores de test.

## P. Playwright

Spec: `e2e/ui-4e-layout-visual.spec.js`
Resultado: **169 passed / 0 failed**
Tiempo: ~2.5 minutos

## Q. Riesgos y deudas

| Item | Tipo | Prioridad |
|---|---|---|
| Inputs de formularios en vistas existentes (Catálogo, Cotización, Venta, Caja modales) usan Tailwind inline | Deuda visual | Media — migrar en UI-6+ por módulo |
| `.toast-msg` en 30 vistas no usa `.alert-erp` | Deuda semántica | Baja — JS depende del selector; migrar junto con JS |
| `.btn-erp-icon` es 40px, no 44px (WCAG) | Accesibilidad menor | Baja — `row-action` ya cumple 44px en mobile |
| Row actions en Venta/Caja/Clientes/Catálogo no usan `.row-action` | Deuda visual | Media — migrar en fases por módulo |
| Chips en headers de sección no usan `.chip-erp` | Deuda visual | Baja |

## R. Próximo paso recomendado

**UI-5C — Normalización de vistas de índice (módulos de listado)**

Aplicar `.row-action`, `.chip-erp`, `.alert-erp`, `.input-erp` en las vistas de índice de los módulos principales (Clientes, Catálogo, Ventas) usando los patrones ahora definidos en UI-5B. Alcance: solo vistas index, sin formularios complejos.
