# Fase 10.11 — Partial reutilizable _EstadoUnidadBadge

## A. Objetivo

Eliminar la duplicación del mapping visual de `EstadoUnidad` (funciones `BadgeCss` / `EstadoLabel` inline) en 4 vistas, centralizándolo en un único partial reutilizable: `Views/Shared/_EstadoUnidadBadge.cshtml`.

## B. Diagnóstico de duplicación

| Vista | Funciones duplicadas | Líneas eliminadas |
|---|---|---|
| `Views/Producto/UnidadHistorial.cshtml` | `BadgeCss`, `EstadoLabel` | 26 |
| `Views/Producto/Unidades.cshtml` | `BadgeCss`, `EstadoLabel` | 26 |
| `Views/Producto/UnidadesGlobal.cshtml` | `BadgeCss`, `EstadoLabel` | 26 |
| `Views/Devolucion/Detalles.cshtml` | `UnidadBadge`, `UnidadLabel` | 27 |

Mapping idéntico en las 4 vistas (9 estados, mismo color y mismo label).

## C. Decisión técnica

- Modelo del partial: `@model TheBuryProject.Models.Enums.EstadoUnidad?`
- Acepta nullable para tolerancia defensiva; todos los callers actuales pasan non-nullable (conversión implícita).
- Renders badge compacto estándar (`px-2 py-0.5 text-xs font-bold`).
- Badge grande de header en `UnidadHistorial` (era `px-3 py-1 text-sm`) normalizado a compact: cambio visual menor aceptable en refactor de mantenibilidad.
- Badge tiny+uppercase en `Devolucion/Detalles` (`text-[10px] uppercase tracking`) normalizado a compact: ídem.
- Ubicación: `Views/Shared/` — patrón canónico del proyecto (todos los partials usan prefijo `_` en esa carpeta).
- No se creó ViewModel adicional: partial simple según spec.

## D. Partial creado

`Views/Shared/_EstadoUnidadBadge.cshtml`

- `@model EstadoUnidad?`
- Fallback para null: renderiza nada (silent empty).
- Fallback para estado desconocido: `border-slate-600/30 bg-slate-600/10 text-slate-400`.
- Label fallback: `e.ToString()`.

## E. Vistas actualizadas

| Vista | Usages reemplazados |
|---|---|
| `UnidadHistorial.cshtml` | 3 (estado actual header + EstadoAnterior + EstadoNuevo en tabla) |
| `Unidades.cshtml` | 1 (columna Estado del listado) |
| `UnidadesGlobal.cshtml` | 1 (columna Estado del inventario global) |
| `Devolucion/Detalles.cshtml` | 1 (unidad física asociada al detalle) |

`@using TheBuryProject.Models.Enums` eliminado de `UnidadHistorial.cshtml` y `UnidadesGlobal.cshtml` (ya no referencian `EstadoUnidad` directamente).  
Mantenido en `Unidades.cshtml` (`EstadoUnidad.EnStock`, `Enum.GetValues<EstadoUnidad>()`).  
Mantenido en `Devolucion/Detalles.cshtml` (`EstadoDevolucion`, `TipoResolucionDevolucion`, `AccionProducto`).

## F. Estados mapeados

| Estado | Color |
|---|---|
| EnStock | emerald |
| Vendida | sky |
| Entregada | blue |
| Devuelta | amber |
| EnReparacion | purple |
| Faltante | red |
| Baja | slate |
| Anulada | slate |
| Reservada | orange |

## G. Tests / validaciones

- `dotnet build --configuration Release`: OK, 0 errores, 0 advertencias.
- `dotnet test --filter "ProductoUnidad|ProductoController|Devolucion"`: 339/339 passing.
- `git diff --check`: limpio (solo warnings LF→CRLF por configuración de Git, no son errores).
- No se agregaron tests Razor: no existe infraestructura de renderizado Razor en el proyecto de tests.

## H. Qué NO se tocó

- Servicios, controllers, entidades, migraciones.
- Lógica de negocio, reglas de transición de estados.
- `EstadoUnidad.cs` (solo lectura).
- Módulos de Carlos (Cotización, worktree separado).
- Venta, Caja, Factura, MovimientoStock.
- `DevState`, `ResClass`, `AccionClass` en `Devolucion/Detalles.cshtml` (mapean otros enums).
- JS, layout, filtros, acciones.

## I. Riesgos / deuda

- Badge grande (`px-3 py-1 text-sm`) del header de `UnidadHistorial` fue normalizado a compact. Si se requiere recuperar el tamaño grande, se puede extender el partial con un parámetro ViewData o crear una variante. Riesgo visual: mínimo.
- Badge tiny+uppercase (`text-[10px] uppercase tracking-[0.14em]`) de `Devolucion/Detalles` fue normalizado. Ídem.
- Si se agrega un nuevo `EstadoUnidad` en el futuro, solo hay que actualizar `_EstadoUnidadBadge.cshtml` — ese es el único lugar de mantenimiento.

## J. Checklist actualizado

### Cerrado

- [x] 8.2 — Trazabilidad individual por unidad física
- [x] 9.x — Caja / comprobantes / cancelación
- [x] 10.1 — Reporte global de unidades físicas
- [x] 10.2 — Diagnóstico devoluciones/garantía
- [x] 10.3 — ReintegrarStock/Cuarentena → Devuelta
- [x] 10.4 — Reparacion → EnReparacion
- [x] 10.4B — UI muestra unidad física en devolución
- [x] 10.5 — Descarte → Baja
- [x] 10.6 — DevolverProveedor/RMA → Devuelta
- [x] 10.7 — Finalización reparación
- [x] 10.8 — QA E2E devolución/reparación/finalización
- [x] 10.9 — Polish UI historial
- [x] 10.10 — Badge visual en Unidades.cshtml
- [x] 10.11 — Partial reutilizable EstadoUnidadBadge

### Pendiente después de 10.11

- Test preexistente fuera de scope: `VentaApiController_ConfiguracionPagosGlobal` — HTTPS TestHost
- Carlos Cotización V1.2/V1.3 (worktree separado)
- Próximo bloque funcional a definir
