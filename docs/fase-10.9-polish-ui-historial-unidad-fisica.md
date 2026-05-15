# Fase 10.9 — Polish UI historial de unidad física: badge visual por EstadoUnidad

## Objetivo

Mejorar la legibilidad visual del historial de unidad física (`UnidadHistorial.cshtml`) mostrando badges coloreados por `EstadoUnidad` en lugar de texto plano.

Facilita la lectura de ciclos de vida como:
- EnStock → Vendida
- Vendida → EnReparacion
- EnReparacion → EnStock / Baja / Devuelta
- Vendida → Baja / Devuelta

Sin cambios en reglas de negocio, servicios, entidades ni migraciones.

---

## Diagnóstico previo

### Estado antes de esta fase

| Elemento | Estado anterior |
|---|---|
| Badge estado actual (header) | `border-slate-700` plano, sin color por estado |
| Columna "Estado anterior" | Texto plano `@movimiento.EstadoAnterior` |
| Columna "Estado nuevo" | Texto plano blanco `@movimiento.EstadoNuevo` |
| `EstadoAnterior`/`EstadoNuevo` en ViewModel | `EstadoUnidad` enum — disponibles directamente |

### Patrones de badge existentes en otras vistas

- `Views/Producto/UnidadesGlobal.cshtml`: `BadgeCss(EstadoUnidad)` + `EstadoLabel(EstadoUnidad)` — mapping completo
- `Views/Devolucion/Detalles.cshtml`: `UnidadBadge(EstadoUnidad)` + `UnidadLabel(EstadoUnidad)` — idéntico, nombres distintos

### Clasificación de componentes tocados

| Componente | Clasificación | Decisión |
|---|---|---|
| `UnidadHistorial.cshtml` | canónico | modificado — objetivo de la fase |
| `ProductoController.cs` | canónico | no tocado |
| `EstadoUnidad.cs` | canónico | solo lectura |
| `ProductoUnidadHistorialViewModel` | canónico | no tocado — datos ya disponibles como enum |
| Badge en `Detalles.cshtml` / `UnidadesGlobal.cshtml` | canónico | referencia visual, mapping replicado localmente |

---

## Decisión UI

**Opción A — badge local en `UnidadHistorial.cshtml`.**

Justificación:
- Micro-lote de bajo riesgo, un solo archivo Razor.
- El mapping es corto (9 estados).
- El patrón ya existe como local en otras 2 vistas — consistencia sin ampliar alcance.
- Crear un partial (`_EstadoUnidadBadge.cshtml`) sería extensión de scope no requerida en este micro-lote.

---

## Cambios aplicados

### Archivo modificado

[Views/Producto/UnidadHistorial.cshtml](../Views/Producto/UnidadHistorial.cshtml)

### Cambios concretos

1. Agregado `@using TheBuryProject.Models.Enums` en el encabezado de la vista.
2. Agregadas funciones locales `BadgeCss(EstadoUnidad)` y `EstadoLabel(EstadoUnidad)` en el bloque `@{ }`.
3. Badge de estado actual (header): reemplazado `border-slate-700 text-slate-200` plano por badge coloreado con `BadgeCss(Model.EstadoActual)`.
4. Columnas de movimientos:
   - "Estado anterior" → badge coloreado `BadgeCss(movimiento.EstadoAnterior)`
   - "Estado nuevo" → badge coloreado `BadgeCss(movimiento.EstadoNuevo)` precedido por flecha `→` para marcar la dirección del ciclo
5. Encabezados de columna renombrados de "Estado anterior" / "Estado nuevo" a **"De"** / **"A"** — más compactos y alineados con la flecha visual.

---

## Estados mapeados

| EstadoUnidad | Color badge | Label |
|---|---|---|
| EnStock | emerald | En stock |
| Vendida | sky | Vendida |
| Entregada | blue | Entregada |
| Devuelta | amber | Devuelta |
| EnReparacion | purple | En reparación |
| Faltante | red | Faltante |
| Baja | slate/neutral | Baja |
| Anulada | slate/neutral | Anulada |
| Reservada | orange | Reservada |

Criterio de accesibilidad: badges incluyen siempre texto del estado — no dependen solo del color.

---

## Tests / validaciones

- `dotnet build --configuration Release` → OK, 0 errores, 0 advertencias.
- `dotnet test --filter "ProductoUnidad|ProductoController|Devolucion"` → 339/339 passing.
- `git diff --check` → limpio (solo advertencia de normalización LF→CRLF, comportamiento estándar de git en Windows).
- No hay tests de vista Razor en la infraestructura actual. El cambio es puramente visual sobre datos ya validados por los tests de controller/service de fases anteriores.

---

## Qué NO se tocó

- Reglas de negocio.
- `ProductoController.cs`.
- `ProductoUnidadHistorialViewModel`.
- `EstadoUnidad.cs` (enum).
- `ProductoUnidad.cs`, `ProductoUnidadMovimiento.cs`.
- `DevolucionService`, `ProductoUnidadService`, `VentaService`.
- Migraciones.
- `Unidades.cshtml` (Estado en columna de listado sigue siendo texto plano — deuda documentada).
- `UnidadesGlobal.cshtml` (sin cambios).
- `Views/Devolucion/Detalles.cshtml` (sin cambios).
- Módulos de Carlos: Cotización, contratos.

---

## Riesgos / deuda remanente

- **Deuda visual menor**: `Views/Producto/Unidades.cshtml` línea 517 muestra `@unidad.Estado` como texto plano en la columna Estado del listado de unidades por producto. No es historial — queda como deuda de baja prioridad.
- **Duplicación controlada**: `BadgeCss`/`EstadoLabel` existen ahora en 3 vistas locales (`UnidadesGlobal`, `Detalles`, `UnidadHistorial`). Consolidar en partial o TagHelper sería deuda técnica a evaluar en un micro-lote posterior dedicado.
- **Test preexistente fuera de scope**: `VentaApiController_ConfiguracionPagosGlobal` — HTTPS TestHost — sigue pendiente de fases anteriores.

---

## Checklist actualizado

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
- [x] 10.7 — Finalización reparación (EnReparacion → EnStock/Baja/Devuelta)
- [x] 10.8 — QA E2E devolución/reparación/finalización (2746/2746)
- [x] 10.9 — Polish UI historial: badge visual por EstadoUnidad

### Pendiente
- [ ] Deuda visual: `Unidades.cshtml` columna Estado → badge (baja prioridad)
- [ ] Deuda técnica: consolidar `BadgeCss`/`EstadoLabel` en partial/TagHelper si se intervienen más vistas
- [ ] Test preexistente fuera de scope: `VentaApiController_ConfiguracionPagosGlobal` HTTPS TestHost
- [ ] Carlos: Cotización V1.1 / conversión (worktree separado, rama `carlos/cotizacion-v1-contratos`)
- [ ] Próximo bloque funcional a definir

---

## Siguiente micro-lote recomendado

**Badge en columna Estado de `Unidades.cshtml`** — mismo micro-lote, mismo patrón, bajo riesgo.  
O bien definir el próximo bloque funcional mayor según prioridad de negocio.
