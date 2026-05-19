# UI-5D — Normalización visual de Venta/Index y Caja/Index

## A. Objetivo

Aplicar los componentes base del design system ERP (`.badge-erp`, `.chip-erp`, `.alert-erp`, `.btn-erp-icon`) en
`Venta/Index_tw.cshtml` y `Caja/Index_tw.cshtml` sin modificar lógica de negocio, rutas, permisos ni contratos HTML/JS.

## B. Vistas auditadas

- `Views/Venta/Index_tw.cshtml`
- `Views/Venta/Create_tw.cshtml` (revisada, no modificada — fuera de alcance)
- `Views/Caja/Index_tw.cshtml`
- `Views/Caja/Cerrar_tw.cshtml` (revisada, no modificada — fuera de alcance)
- `wwwroot/css/shared-components.css` (leída, no modificada — clases ya cubiertas)
- `wwwroot/css/caja-module.css` (leída — detectado `.caja-index-action-label` y JS de inyección de filas)
- `wwwroot/js/caja-index.js` (leído — detectada inyección dinámica de filas con clases inline)

## C. Vistas modificadas

| Archivo | Cambios |
|---|---|
| `Views/Venta/Index_tw.cshtml` | Alerts, badges estado, chips autorización, filter buttons |
| `Views/Caja/Index_tw.cshtml` | Alerts, badge En vivo, chips estado caja, chip movimientos, chips section |

## D. Vistas postergadas

| Vista | Razón |
|---|---|
| `Views/Venta/Create_tw.cshtml` | Fuera de alcance explícito |
| `Views/Caja/Cerrar_tw.cshtml` | Fuera de alcance explícito |
| `Views/Caja/DetallesApertura_tw.cshtml` | Fuera de alcance explícito |
| Botones de acción de filas en Caja/Index | El JS (`caja-index.js`) inyecta filas nuevas con las mismas clases inline hardcodeadas; cambiar solo el Razor crearía inconsistencia visual entre filas estáticas y filas dinámicas |
| Panel "Sin caja abierta" en Venta/Index | Es un panel de acción complejo (icono + texto + 2 botones), no una alert simple; `.alert-erp` no encaja semánticamente |
| Chips del encabezado de sección en Venta/Index (Hoy, conteo, total) | Ya visualmente consistentes; cambio de bajo impacto, mejor conservarlos |

## E. Componentes aplicados

- `.alert-erp`, `.alert-erp-success`, `.alert-erp-error`, `.alert-erp-warning`
- `.badge-erp`, `.badge-erp-neutral`, `.badge-erp-info`, `.badge-erp-success`, `.badge-erp-danger`, `.badge-erp-warning`, `.badge-erp-primary`
- `.chip-erp`, `.chip-erp-warning`, `.chip-erp-primary`, `.chip-erp-danger`, `.chip-erp-success`
- `.btn-erp-icon`, `.btn-erp-icon--primary`

## F. Cambios en Venta/Index

### F1. TempData alerts (líneas 52-71)

**Antes:** `.toast-msg flex items-center gap-3 rounded-xl border border-emerald-500/20 bg-emerald-500/10 p-4 text-sm font-semibold text-emerald-400`  
**Después:** `.toast-msg alert-erp alert-erp-success` (ídem error/warning)  
El hook JS `.toast-msg` se preserva para auto-dismiss.

### F2. Badge de estado de venta (tabla, columna Estado)

**Antes:** Tupla C# `(cssClasses, label)` con Tailwind inline `bg-emerald-900/30 text-emerald-400`, etc.  
**Después:** Variables `estadoBadgeVariant` (string con clase canónica) y `estadoLabel`, renderizadas como `<span class="badge-erp @estadoBadgeVariant">`.

Mapeo semántico:
- `Cotizacion` → `badge-erp-neutral`
- `Presupuesto` → `badge-erp-info`
- `Confirmada`, `Facturada`, `Entregada` → `badge-erp-success`
- `Cancelada` → `badge-erp-danger`
- `PendienteRequisitos`, `PendienteFinanciacion` → `badge-erp-warning`

El `line-through` para ventas canceladas se preserva.

### F3. Chip de autorización (tabla, columna Autorización)

**Antes:** Tupla 3-uple C# con texto + dot inline (`w-1.5 h-1.5 rounded-full bg-amber-500`).  
**Después:** Variables `autorizacionChipVariant` y `autorizacionLabel`, renderizadas como `<span class="chip-erp @autorizacionChipVariant">`.

Mapeo semántico:
- `NoRequiere` → `.chip-erp` (neutral)
- `PendienteAutorizacion` → `.chip-erp chip-erp-warning`
- `Autorizada` → `.chip-erp chip-erp-primary`
- `Rechazada` → `.chip-erp chip-erp-danger`

### F4. Botones de filtro (Filtrar / Limpiar)

**Antes:** Tailwind inline `rounded-lg bg-primary/10 p-2.5 text-primary transition-colors hover:bg-primary hover:text-white`  
**Después:** `.btn-erp-icon` y `.btn-erp-icon--primary` — tamaño fijo 40×40px, foco visible, sin label.

## G. Cambios en Caja/Index

### G1. TempData alerts (Success/Warning/Error)

Mismo patrón que Venta/Index: `.toast-msg alert-erp alert-erp-*`.

### G2. Badge "En vivo"

**Antes:** `px-2 py-0.5 rounded bg-primary/20 text-primary text-[10px] font-bold uppercase tracking-wider`  
**Después:** `.badge-erp badge-erp-primary`

### G3. Chips de estado Abierta/Cerrada (tabla maestro)

**Antes:** `div.flex` con dot (`size-2 rounded-full bg-emerald-500 animate-pulse`) + text inline.  
**Después:**
- Abierta → `<span class="chip-erp chip-erp-success"><span class="size-2 rounded-full bg-current animate-pulse"></span> Abierta</span>`
- Cerrada → `<span class="chip-erp">Cerrada</span>`

El `animate-pulse` se preserva para el estado activo usando `bg-current` en lugar de `bg-emerald-500`.

### G4. Chip de conteo de movimientos

**Antes:** `px-2 py-0.5 rounded-full bg-slate-800 text-xs font-bold text-slate-400 w-fit`  
**Después:** `.chip-erp` (neutral)

### G5. Section counter badges (3 secciones)

**Antes:** `div.caja-index-section-badge inline-flex items-center gap-2 rounded-full border border-slate-700 bg-slate-900/60 px-3 py-2 text-xs font-semibold text-slate-300` (Tailwind inline repetido 3 veces)  
**Después:** `div.caja-index-section-badge chip-erp` — el layout `.caja-index-section-badge` se conserva, el Tailwind inline se reemplaza por `.chip-erp`. Los íconos internos mantienen su color semántico (`text-primary`, `text-slate-500`); el tamaño 14px lo gestiona `.chip-erp .material-symbols-outlined`.

## H. Contratos preservados

- Todos los `id` (incluyendo `panel-caja-cerrada`, `btn-abrir-caja`, `ventas-index-scroll`, `form-filtros`, `modal-recargo`, `cajaMainContent`, `caja-index-feedback-slot`, `cajas-activas-tbody`, `modalCajaContainer`, etc.)
- Todos los `data-*` (`data-venta-modal-*`, `data-oc-scroll-*`, `data-open-devolucion-modal`, `data-venta-id`, `data-action`, `data-caja-*`, `data-caja-delete-form`, `data-confirm-message`)
- Hook JS `.toast-msg` preservado en todas las alerts modificadas
- `asp-controller`, `asp-action`, `asp-route-*`, `method`, antiforgery — sin cambios
- Scripts de vista — sin cambios
- Nombres de inputs — sin cambios

## I. Accesibilidad

- `.alert-erp` incluye `role="status"` / `role="alert"` en el Razor (preservado)
- `.badge-erp` y `.chip-erp` no dependen solo del color: incluyen texto legible
- `.btn-erp-icon` tiene `focus-visible: outline 2px solid #135bec` — foco visible WCAG AA
- El dot `animate-pulse` en "Abierta" suma indicador de movimiento, no reemplaza el texto
- Contraste: variantes semánticas de `.badge-erp` y `.chip-erp` superan 4.5:1 sobre `#161c28`

## J. Mobile/Responsive

- `.alert-erp` es `display: flex` — fluye bien en mobile
- `.badge-erp` y `.chip-erp` son `inline-flex` con `white-space: nowrap` — no rompen layout
- `.btn-erp-icon` es 40×40px — touch target adecuado
- Tablas con `overflow-x-auto` y `data-oc-scroll` — sin cambios, sin scroll horizontal nuevo
- No se introdujeron `width` o `margin` fixed que rompan mobile

## K. Validaciones ejecutadas

| Validación | Resultado |
|---|---|
| `dotnet build -o tmpbuild_ui5d` (pre-cambios) | OK — file lock documentado PID 71936, build temporal OK |
| `dotnet build -o tmpbuild_ui5d` (post-cambios) | OK (exit 0) |
| `git diff --check` | OK (exit 0) — sin trailing whitespace |
| `git status --short` | 2 archivos modificados + tmptest_ui5d/ no trackeado |

## L. Tests

| Suite | Resultado |
|---|---|
| `LayoutUiContractTests` | 57/57 OK |
| `Layout\|Shared\|Navigation\|Sidebar\|Header\|UiContract\|Seguridad\|Auth\|Dashboard` | 230/230 OK |

Nota: `dotnet test` en proyectos principales falla por file lock (PID 71936). Se usó output temporal: `TheBuryProyect.Tests/TheBuryProyect.Tests.csproj -o tmptest_ui5d`.

## M. Playwright

Spec ejecutado: `e2e/ui-4e-layout-visual.spec.js`

## N. Resultado Playwright exacto

```
169 passed (3.8m)
```

0 failed. Total exacto: **169/169 passed**.

## O. Riesgos y deudas remanentes

| Riesgo/Deuda | Descripción |
|---|---|
| Botones acción Caja/Index no normalizados | `caja-index.js` inyecta filas con clases inline idénticas. Para normalizar, habría que actualizar también el JS. Deuda documentada. |
| Panel "Sin caja abierta" Venta/Index | Inline Tailwind complejo. Candidato a componente propio o refactor estructural en UI-5E+. |
| `caja-index-action-label` | Oculta labels en `lg`, similar a `.row-action__label` en `sm`. Si se decide migrar a `.row-action`, el JS también debe actualizarse. |
| File lock PID 71936 | La app está corriendo en puerto 5187. Los comandos de build/test usan `-o tmpbuild_ui5d` / `-o tmptest_ui5d`. |

## P. Próximo paso recomendado

**UI-5E** — Extender la normalización visual a otras vistas secundarias de Venta (Details, Facturar, Autorizar, Cancelar) y Caja (DetallesApertura, Historial), o iniciar auditoría de vistas de Devolución/Crédito con el mismo design system.
