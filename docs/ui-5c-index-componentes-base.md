# UI-5C — Normalización de vistas index: componentes base

## A. Objetivo

Aplicar los componentes base definidos en UI-5B a vistas index/listado de bajo y medio riesgo.
Reducir Tailwind inline repetido y unificar acciones de fila, filtros, chips y alerts.

## B. Vistas auditadas

| Vista | Archivo |
|-------|---------|
| Clientes | `Views/Cliente/Index_tw.cshtml` |
| Proveedores | `Views/Proveedor/Index_tw.cshtml` |
| Catálogo/Inventario | `Views/Catalogo/Index_tw.cshtml` |
| Ventas | `Views/Venta/Index_tw.cshtml` |
| Caja | `Views/Caja/Index_tw.cshtml` |
| Cotización | `Views/Cotizacion/Index_tw.cshtml` |

## C. Vistas modificadas

| Vista | Cambios aplicados |
|-------|-------------------|
| `Cliente/Index_tw.cshtml` | Alerts, search input, filter buttons, status chips |
| `Proveedor/Index_tw.cshtml` | Search input, filter buttons, status chips, category chips |
| `Catalogo/Index_tw.cshtml` | Alerts, search input, 4 filter selects, filter buttons, counter badge |

## D. Vistas postergadas

| Vista | Razón |
|-------|-------|
| `Venta/Index_tw.cshtml` | Panel caja-cerrada con lógica de negocio compleja; status badges de ventas mezclan estado + autorización; postergar a UI-5D |
| `Caja/Index_tw.cshtml` | Módulo apertura/cierre; row actions dentro de sección operativa; postergar a UI-5D |
| `Cotizacion/Index_tw.cshtml` | Simulador hardcodeado; prohibido por alcance |
| `Dashboard/Index.cshtml` | Ya normalizada en UI-3 |

## E. Componentes aplicados

- `.alert-erp`, `.alert-erp-success`, `.alert-erp-error` + `.toast-msg` (hook JS preservado)
- `.filter-input-erp`, `.filter-input-erp-icon` (nuevo modificador para input con ícono prefijado)
- `.filter-select-erp`
- `.btn-erp-primary.btn-sm` (submit filtro)
- `.btn-erp-secondary.btn-sm` (clear filtro)
- `.btn-erp-icon`, `.btn-erp-icon--primary` (submit filtro icon-only en Catalogo)
- `.chip-erp`, `.chip-erp-success`, `.chip-erp-primary` (status y category chips)
- `.badge-erp`, `.badge-erp-neutral` (counter en Catalogo)

## F. Cambios en acciones de fila

Sin cambios. Cliente y Proveedor ya usaban `.row-action`, `.row-action--primary`, `.row-action--danger` desde UI-5B. Catálogo tiene acciones de fila más complejas; pendiente UI-5D.

## G. Cambios en filtros

| Vista | Campo | Antes | Después |
|-------|-------|-------|---------|
| Cliente | Search text | Tailwind inline | `filter-input-erp filter-input-erp-icon` |
| Cliente | Submit filtro | `inline-flex h-9 bg-primary/10...` | `btn-erp-primary btn-sm` |
| Cliente | Clear filtro | `inline-flex h-9 bg-slate-800...` | `btn-erp-secondary btn-sm` |
| Proveedor | Search text | Tailwind inline | `filter-input-erp filter-input-erp-icon` |
| Proveedor | Submit filtro | `inline-flex h-9 bg-primary/10...` | `btn-erp-primary btn-sm` |
| Proveedor | Clear filtro | `inline-flex h-9 bg-slate-800...` | `btn-erp-secondary btn-sm` |
| Catalogo | Search text | Tailwind inline | `filter-input-erp filter-input-erp-icon` |
| Catalogo | Select categoría | `w-full bg-slate-800 border-none...` | `filter-select-erp` |
| Catalogo | Select marca | `w-full bg-slate-800 border-none...` | `filter-select-erp` |
| Catalogo | Select stock | `w-full bg-slate-800 border-none...` | `filter-select-erp` |
| Catalogo | Select lista precios | `w-full bg-slate-800 border-none...` | `filter-select-erp` |
| Catalogo | Submit icon | `p-2.5 bg-primary/10...` | `btn-erp-icon btn-erp-icon--primary` |
| Catalogo | Clear icon | `p-2.5 bg-slate-800...` | `btn-erp-icon` |

## H. Cambios en chips/badges

| Vista | Elemento | Antes | Después |
|-------|----------|-------|---------|
| Cliente | Estado Activo | `inline-flex rounded-full bg-emerald-500/10 text-emerald-400` | `chip-erp chip-erp-success` |
| Cliente | Estado Inactivo | `inline-flex rounded-full bg-slate-800 text-slate-400` | `chip-erp` |
| Proveedor | Estado Activo | `inline-flex rounded-full bg-emerald-500/10 text-emerald-400` | `chip-erp chip-erp-success` |
| Proveedor | Estado Inactivo | `inline-flex rounded-full bg-slate-800 text-slate-400` | `chip-erp` |
| Proveedor | Categorías | `inline-flex rounded-md border border-slate-700 bg-slate-800/70` | `chip-erp max-w-[9rem] truncate` |
| Proveedor | +N más | `inline-flex rounded-md border border-primary/20 bg-primary/10` | `chip-erp chip-erp-primary` |
| Catalogo | Counter visible | `inline-flex rounded-full bg-slate-800 uppercase tracking-[0.16em]` | `badge-erp badge-erp-neutral` |

## I. Cambios en alerts

| Vista | Tipo | Antes | Después |
|-------|------|-------|---------|
| Cliente | Success | `toast-msg flex items-center gap-3 rounded-xl border border-emerald-500/20 bg-emerald-500/10 p-4 text-sm font-semibold text-emerald-400` | `alert-erp alert-erp-success toast-msg` |
| Cliente | Error | `toast-msg flex items-center gap-3 rounded-xl border border-red-500/20 bg-red-500/10 p-4 text-sm font-semibold text-red-400` | `alert-erp alert-erp-error toast-msg` |
| Catalogo | Success | mismo patrón inline | `alert-erp alert-erp-success toast-msg` |
| Catalogo | Error | mismo patrón inline | `alert-erp alert-erp-error toast-msg` |

## J. Contratos preservados

- `id="filterForm"` (Cliente) ✓
- `id="form-filtros"` (Proveedor) ✓
- `id="catalogo-search-input"`, `id="catalogo-select-categoria"`, `id="catalogo-select-marca"`, `id="catalogo-select-stock"`, `id="catalogo-select-listaprecio"` (Catalogo) ✓
- `id="chk-select-all"` ✓
- `id="productos-visible-count"` (span interno preservado, solo cambió el contenedor externo) ✓
- Todos los `data-*` de JS preservados ✓
- Todos los `asp-controller`, `asp-action`, `asp-route-*` ✓
- Todos los `name` de inputs/selects ✓
- `@Html.AntiForgeryToken()` sin tocar ✓
- `.toast-msg` preservado como hook de JS (combinado con `.alert-erp`) ✓
- Scripts `@section Scripts` sin tocar ✓

## K. Accesibilidad

- `.filter-input-erp`: `height: 44px` — WCAG 2.5.5 touch target ✓
- `.btn-erp-primary`, `.btn-erp-secondary`, `.btn-erp-icon`: `focus-visible` con outline ✓
- `.chip-erp`: contraste de texto con fondo semántico ✓
- `.alert-erp`: `role="status"` / `role="alert"` preservados ✓
- No se redujo ningún texto operativo por debajo de 14px ✓
- Íconos con `aria-label` preservados en acciones de fila ✓

## L. Mobile/responsive

- `filter-input-erp` es `width: 100%` → funciona en cualquier ancho ✓
- `btn-erp-primary.btn-sm` y `btn-erp-secondary.btn-sm` preservan `height: 2.25rem` → mismo touch target ✓
- `chip-erp` tiene `flex-shrink: 0` → no colapsa en contenedores flex ✓
- Ningún cambio introduce scroll horizontal ✓
- Proveedor `max-w-[9rem] truncate` en chips de categoría → trunca texto largo sin romper layout ✓

## M. Cambios en shared-components.css

Un gap real detectado y resuelto:

```css
/* Modificador para inputs con ícono absoluto prefijado */
.filter-input-erp-icon { padding-left: 2.5rem; }
```

Agrega espacio a la izquierda para el ícono absoluto en inputs de búsqueda.

## N. Validaciones

| Check | Resultado |
|-------|-----------|
| `git diff --check` | OK — sin errores de espaciado |
| `git status --short` | 4 archivos modificados |
| Build (tmpbuild_ui5c) | ✓ 0 errores, 1 warning NETSDK1194 (conocido) |

## O. Tests

| Suite | Resultado |
|-------|-----------|
| `LayoutUiContractTests` | 57/57 ✓ |
| Layout, Shared, Navigation, Auth, Dashboard | 230/230 ✓ |

## P. Playwright

| Suite | Resultado |
|-------|-----------|
| `e2e/ui-4e-layout-visual.spec.js` | **169/169 passed** ✓ |

Nota: Los tests de Playwright verifican layout global, nav, sidebar y responsive. Las vistas modificadas (cliente, proveedor, catálogo) están cubiertas por los tests mobile de `/Cliente`, catálogo desktop/mobile y responsive general.

## Q. Riesgos / Deudas remanentes

1. **Filter submit visual**: `btn-erp-primary.btn-sm` (azul sólido) vs anterior tonal (bg-primary/10). Cambio de peso visual intencional — normaliza hacia acción primaria canónica.
2. **chip-erp `border-radius: 9999px`**: category chips de Proveedor pasan de `rounded-md` a pill. Cambio cosmético menor.
3. **Venta/Index_tw.cshtml**: Status badges de venta (Completada, Cancelada, Pendiente, etc.) y chips de autorización postergados a UI-5D.
4. **Caja/Index_tw.cshtml**: Row actions y status badges de apertura postergados a UI-5D.
5. **`filter-input-erp-icon`**: Modificador CSS solo cubre `padding-left: 2.5rem`. Si se usa otro tamaño de ícono, habrá que ajustar.

## R. Próximo paso recomendado

**UI-5D — Normalización de vistas index de complejidad media** (Venta/Index_tw, Caja/Index_tw):
- Status badges semánticos de venta (Completada, Cancelada, Pendiente, PendienteAutorizacion, Devuelta) → `.chip-erp` con variantes
- Chips de autorización pendiente → `.badge-erp badge-erp-warning`
- Panel "caja cerrada" → `.alert-erp alert-erp-warning` + estructura interna
- Row actions de Caja → auditar si ya usan `.row-action` o necesitan normalización
