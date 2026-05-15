# Fase 10.16 — Diagnóstico y normalización de badges en Producto, Proveedor y ProductoUnidad

## A. Objetivo

Revisar si los módulos Producto, Proveedor y ProductoUnidad tienen estados visuales renderizados como texto plano, badges divergentes, helpers duplicados o fallbacks incorrectos. Normalizar solo si hay divergencias reales.

---

## B. Diagnóstico previo

### Estructura de vistas encontrada

- `Views/ProductoUnidad/` — **no existe**. Las vistas de unidad viven bajo `Views/Producto/`.
- `Views/Catalogo/Index_tw.cshtml` — existe (no renderiza estados de unidad directamente).
- `Views/Producto/` — 4 vistas activas.
- `Views/Proveedor/` — 3 vistas activas (Index_tw, Details_tw, _ProveedorModuleStyles).

### Enums encontrados

| Enum | Archivo | Uso |
|---|---|---|
| `EstadoUnidad` | `Models/Enums/EstadoUnidad.cs` | `ProductoUnidad.Estado`, historial de movimientos |
| `EstadoProductoDevuelto` | `Models/Entities/Devolucion.cs` | Solo en módulo Devolución — fuera de alcance |
| `AccionProducto` | `Models/Entities/Devolucion.cs` | Solo en módulo Devolución — fuera de alcance |

No existe `EstadoProducto` ni `EstadoProveedor` como enum. Proveedor usa `bool Activo`.

### Helpers canónicos existentes

- `Helpers/TicketUiHelper.cs` — para Ticket
- `Helpers/OrdenCompraUiHelper.cs` — creado en 10.15 para OrdenCompra
- `Views/Shared/_EstadoUnidadBadge.cshtml` — partial canónico para `EstadoUnidad`

No existe helper para Producto ni Proveedor.

---

## C. Vistas revisadas y estado

### `Views/Producto/UnidadHistorial.cshtml`
- Usa `<partial name="_EstadoUnidadBadge" model="Model.EstadoActual" />` y `model="movimiento.EstadoAnterior/Nuevo"`.
- **Canónico**. Sin divergencias.

### `Views/Producto/UnidadesGlobal.cshtml`
- Tabla de unidades: usa `<partial name="_EstadoUnidadBadge" model="item.Estado" />`.
- Cards de resumen: bloques informativos por estado (emerald, sky, red, amber, purple, etc.) — patrón coherente y específico de esta vista.
- **Canónico**. Sin divergencias.

### `Views/Producto/Unidades.cshtml`
- Tabla de preview (carga masiva): línea 437 mostraba `@EstadoUnidad.EnStock` como texto plano.
- **Divergencia menor**: texto plano sin usar el partial canónico.
- **Corregido**: normalizado a `<partial name="_EstadoUnidadBadge" model="(EstadoUnidad?)EstadoUnidad.EnStock" />`.

### `Views/Proveedor/Index_tw.cshtml`
- Columna Estado: usa badges locales con dot indicator para Activo/Inactivo.
- Activo: `bg-emerald-500/10 text-emerald-400` sin border.
- Inactivo: `bg-slate-800 text-slate-400` sin border.
- Patrón **consistente internamente**. Sin bug. No normalizado (estilo propio del módulo, no hay helper para bool Activo).

### `Views/Proveedor/Details_tw.cshtml`
- Sección "Información General": mostraba Activo/Inactivo con estilo divergente del Index.
- **BUG** en Inactivo: clases duplicadas y conflictivas `bg-slate-100 text-slate-600 bg-slate-800 text-slate-400` (restos de modo light sin limpiar).
- **Divergencia**: Activo usaba `font-medium`, `bg-emerald-900/30`, `border-emerald-800` — diferente del Index.
- **Corregido**: ambos badges normalizados al patrón del Index (dot + rounded-full, `/10` opacity, `font-bold`).

---

## D. Estados / enums mapeados

| Módulo | Estado visual | Tipo | Render pre-10.16 | Render post-10.16 |
|---|---|---|---|---|
| ProductoUnidad — Historial | `EstadoUnidad` | enum | partial ✓ | sin cambio |
| ProductoUnidad — Global | `EstadoUnidad` | enum | partial ✓ | sin cambio |
| ProductoUnidad — Unidades (preview) | `EstadoUnidad.EnStock` | enum literal | texto plano ✗ | partial ✓ |
| Proveedor — Index | `bool Activo` | bool | badge local ✓ | sin cambio |
| Proveedor — Details | `bool Activo` | bool | badge divergente + BUG ✗ | badge normalizado ✓ |

---

## E. Cambios aplicados

### 1. `Views/Proveedor/Details_tw.cshtml`

**Problema**: Badge Activo/Inactivo en sección "Información General" tenía:
- Inactivo: clases duplicadas `bg-slate-100 text-slate-600 bg-slate-800 text-slate-400` (bug light+dark mode)
- Activo: `font-medium` (vs `font-bold`), `bg-emerald-900/30` (vs `/10`), `border-emerald-800` (vs sin border/canónico)
- Divergencia visual respecto a Index_tw.cshtml

**Fix**: Ambos badges normalizados al patrón del Index:
```html
<!-- Activo -->
<span class="inline-flex items-center gap-1.5 rounded-full bg-emerald-500/10 px-2.5 py-1 text-xs font-bold text-emerald-400">
    <span class="w-1.5 h-1.5 rounded-full bg-emerald-500"></span>
    Activo
</span>

<!-- Inactivo -->
<span class="inline-flex items-center gap-1.5 rounded-full bg-slate-800 px-2.5 py-1 text-xs font-bold text-slate-400">
    <span class="w-1.5 h-1.5 rounded-full bg-slate-400"></span>
    Inactivo
</span>
```

### 2. `Views/Producto/Unidades.cshtml`

**Problema**: Tabla de preview de carga masiva mostraba `@EstadoUnidad.EnStock` como texto plano.

**Fix**: Normalizado al partial canónico:
```html
<td class="px-4 py-2"><partial name="_EstadoUnidadBadge" model="(EstadoUnidad?)EstadoUnidad.EnStock" /></td>
```

---

## F. Helpers / partials reutilizados o creados

- `Views/Shared/_EstadoUnidadBadge.cshtml` — reutilizado para `Unidades.cshtml` preview. Ya existía desde 10.11.
- No se creó ningún helper nuevo (Proveedor Activo/Inactivo es un bool simple en dos vistas, no justifica helper).

---

## G. Tests / validaciones

- `dotnet build --configuration Release` → **0 errores, 0 warnings**
- `dotnet test --filter "Producto|Proveedor|ProductoUnidad"` → **564/564 passing**
- `git diff --check` → limpio (warning CRLF de normalización Git, no es error de código)

---

## H. Qué NO se tocó

- Reglas de negocio
- Services
- Controllers
- Entidades
- Enums
- Migraciones
- `Views/Producto/Edit_tw.cshtml` — no muestra estados/badges relevantes
- `Views/Catalogo/Index_tw.cshtml` — no renderiza estados de unidad directamente
- `Views/Proveedor/Index_tw.cshtml` — sin divergencias, no modificado
- Módulos Venta, Caja, Factura, Devolución, Cotización
- Módulo de Carlos (rama `carlos/cotizacion-v1-contratos`)

---

## I. Riesgos / deuda

- `Views/Proveedor/Index_tw.cshtml`: el badge Inactivo usa `bg-slate-800` sólido en vez de `/10`. Deuda menor, estéticamente funcional, no es un bug.
- `Views/Catalogo/Index_tw.cshtml`: demasiado grande para lectura completa. No se detectaron divergencias en los primeros scans. Queda como candidato opcional para revisión futura si se sospecha algún estado sin badge.
- No existe `EstadoProducto` ni `EstadoProveedor` como enum — si en el futuro se crean, requerirán helpers análogos.

---

## J. Checklist actualizado

- [x] 10.9 — Badge en historial de unidad
- [x] 10.10 — Badge EstadoUnidad en Unidades.cshtml
- [x] 10.11 — Partial `_EstadoUnidadBadge`
- [x] 10.12 — Badges en detalle de devolución
- [x] 10.13 — Badges en índice de devolución
- [x] 10.14 — Diagnóstico RMA/NotaCredito
- [x] 10.15 — Badges Ticket y OrdenCompra
- [x] 10.16 — Diagnóstico y normalización badges Producto / Proveedor / ProductoUnidad

## Siguiente micro-lote recomendado

Revisar `Views/Catalogo/Index_tw.cshtml` (vista grande, ~1000 líneas) para verificar si hay estados de producto, stock o filtros activos renderizados sin badges. Es el único módulo del alcance donde no se completó lectura íntegra.
