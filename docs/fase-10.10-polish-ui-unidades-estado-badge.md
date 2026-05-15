# Fase 10.10 — Badge visual EstadoUnidad en Unidades.cshtml

## A. Objetivo

Aplicar el mismo patrón visual de badge por `EstadoUnidad` en la columna Estado de `Views/Producto/Unidades.cshtml`, eliminando la inconsistencia visual con `UnidadHistorial.cshtml`, `UnidadesGlobal.cshtml` y `Devolucion/Detalles.cshtml`.

## B. Diagnóstico previo

**Estado de la columna antes:**
- Línea 517 (original): `<td class="px-4 py-3 text-sm text-slate-200">@unidad.Estado</td>`
- Texto plano, sin badge visual.

**Dato que llega a la vista:**
- `unidad.Estado` de tipo `EstadoUnidad` enum (valores del dominio, directamente tipados).

**Helpers locales existentes:**
- `Unidades.cshtml`: ninguno (target de esta fase).
- `UnidadHistorial.cshtml`: `BadgeCss` + `EstadoLabel` (patrón de referencia de 10.9).
- `UnidadesGlobal.cshtml`: `BadgeCss` + `EstadoLabel` (idéntico).
- `Devolucion/Detalles.cshtml`: `UnidadBadge` + `UnidadLabel` (mismo mapping, nombre distinto).

**Tests de vista/controller:**
- No existen tests de vistas Razor en el proyecto.
- Los tests de `ProductoController` cubren comportamiento de controller, no renderizado HTML.

## C. Decisión UI

**Opción elegida: A — Badge local en Unidades.cshtml**

Copiar el mismo pattern de `UnidadHistorial.cshtml` (10.9) directamente en el bloque `@{}` de `Unidades.cshtml`.

**Motivo:**
- Micro-lote puro, bajo riesgo.
- Sin tocar otras vistas ni crear infraestructura nueva.
- Consistente con la decisión tomada en 10.1 (UnidadesGlobal) y 10.9 (UnidadHistorial).

**Deuda reconocida:**
- El mapping `BadgeCss`/`EstadoLabel` queda duplicado en 4 vistas.
- Partial reutilizable `_EstadoUnidadBadge.cshtml` sigue pendiente para una fase posterior.

## D. Cambios visuales

**Archivo modificado:** `Views/Producto/Unidades.cshtml`

1. Agregadas funciones locales `BadgeCss(EstadoUnidad)` y `EstadoLabel(EstadoUnidad)` en el bloque `@{}` inicial.
2. Reemplazada celda `Estado` en la tabla de unidades:
   - Antes: texto plano `@unidad.Estado`
   - Después: badge `inline-flex rounded-full border` con color y etiqueta legible

## E. Estados mapeados

| Estado        | Color visual  | Etiqueta       |
|---------------|---------------|----------------|
| EnStock       | emerald       | En stock       |
| Vendida       | sky           | Vendida        |
| Entregada     | blue          | Entregada      |
| Devuelta      | amber         | Devuelta       |
| EnReparacion  | purple        | En reparación  |
| Faltante      | red           | Faltante       |
| Baja          | slate         | Baja           |
| Anulada       | slate         | Anulada        |
| Reservada     | orange        | Reservada      |

## F. Tests / validaciones

- `dotnet build --configuration Release` → Compilación correcta, 0 errores, 0 advertencias.
- `dotnet test --filter "ProductoUnidad|ProductoController|Devolucion"` → 339/339 passing.
- `git diff --check` → limpio (solo warning LF/CRLF de Windows, no es error).
- No se agregaron tests de vista: la vista solo cambió renderizado visual, sin lógica de negocio ni nuevas rutas.

## G. Qué NO se tocó

- Servicios, entidades, migraciones.
- Controllers (ninguno).
- `UnidadHistorial.cshtml`, `UnidadesGlobal.cshtml`, `Devolucion/Detalles.cshtml`.
- Módulos de Venta, Caja, Factura, Cotización.
- JS / filtros / acciones de la tabla.
- `AGENTS.md` ni `CLAUDE.md`.
- Archivos de Carlos (`docs/fase-cotizacion-diseno-v1.md`, worktree `carlos/cotizacion-v1-contratos`).

## H. Riesgos / deuda

- Duplicación de `BadgeCss`/`EstadoLabel` en 4 vistas: reconocida, pendiente para fase posterior.
- Si se agrega un nuevo valor al enum `EstadoUnidad`, hay que actualizar el mapping en las 4 vistas manualmente.
- Partial reutilizable `_EstadoUnidadBadge.cshtml` sigue sin crearse (decisión explícita de esta fase).

## I. Checklist actualizado

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
- [x] 10.9 — Polish UI historial unidad física
- [x] 10.10 — Badge visual EstadoUnidad en Unidades.cshtml

### Pendiente
- [ ] Partial reutilizable `_EstadoUnidadBadge.cshtml` para eliminar duplicación en 4 vistas
- [ ] Test preexistente fuera de scope: `VentaApiController_ConfiguracionPagosGlobal` (HTTPS TestHost)
- [ ] Carlos: Cotización V1.2 / V1.3
- [ ] Próximo bloque funcional a definir
