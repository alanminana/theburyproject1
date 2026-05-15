# Fase 10.12 — Polish UI: badges visuales en detalle de devolución

## A. Objetivo

Normalizar visualmente los enums operativos de la vista `Views/Devolucion/Detalles.cshtml` que aún se renderizaban como texto plano, aplicando badges compactos, de alto contraste y compatibles con dark theme.

Polish visual únicamente. Sin cambios en lógica de negocio, servicios, controllers ni entidades.

---

## B. Diagnóstico previo

| Enum | Línea | Estado antes | Decisión |
|---|---|---|---|
| `EstadoDevolucion` | 62–64 | ✅ badge con `DevState()` | no tocar |
| `TipoResolucionDevolucion` | 65–67 | ✅ badge con `ResClass()` | no tocar |
| `AccionProducto` | 130–132 | ✅ badge con `AccionClass()` | no tocar |
| `EstadoProductoDevuelto` | 127 | ❌ texto plano | normalizar |
| `EstadoNotaCredito` | 337 | ❌ texto plano | normalizar |
| `EstadoRMA` | 362 | ❌ texto plano | normalizar |
| `MotivoDevolucion` | 289 | texto plano | mantener (descriptor, no estado) |

**Archivos inspeccionados:** `Views/Devolucion/Detalles.cshtml`, `Models/Entities/Devolucion.cs`

**Clasificación de componentes:**
- `Views/Devolucion/Detalles.cshtml` → **canónico** — vista activa del flujo de devolución
- `EstadoDevolucion` → **canónico** — enum con cobertura en tests
- `AccionProducto` → **canónico** — enum con cobertura en tests
- `TipoResolucionDevolucion` → **canónico** — enum con cobertura en tests
- `EstadoProductoDevuelto` → **canónico** — enum definido en `Devolucion.cs`
- `EstadoNotaCredito` → **canónico** — enum definido en `Devolucion.cs`
- `EstadoRMA` → **canónico** — enum definido en `Devolucion.cs`

**Decisión UI:** helpers locales en `Detalles.cshtml`. No se crearon partials globales porque estos mappings son específicos de esta vista y no hay duplicación real en otras vistas.

---

## C. Enums normalizados

### EstadoProductoDevuelto
| Valor | Color |
|---|---|
| Nuevo, NuevoSellado | emerald (positivo) |
| AbiertoSinUso, UsadoBuenEstado | sky (aceptable) |
| UsadoConDetalles, Marcado | amber (leve desgaste) |
| Incompleto | orange (advertencia) |
| Defectuoso, Danado | rose (malo) |

### EstadoNotaCredito
| Valor | Color |
|---|---|
| Vigente | emerald |
| UtilizadaParcialmente | amber |
| UtilizadaTotalmente | slate |
| Vencida | rose |
| Cancelada | slate |

### EstadoRMA
| Valor | Color |
|---|---|
| Pendiente | amber |
| AprobadoProveedor | sky |
| EnTransito | violet |
| RecibidoProveedor | indigo |
| EnEvaluacion | sky |
| Resuelto | emerald |
| Rechazado | rose |

---

## D. Cambios visuales

**Archivo modificado:** `Views/Devolucion/Detalles.cshtml`

Cambios:
1. Agregados helpers `EstadoProdClass`, `EstadoNCClass`, `EstadoRMAClass` en el bloque `@{}` inicial.
2. `det.EstadoProducto` (columna tabla): reemplazado texto plano por badge `inline-flex rounded-full border`.
3. `Model.NotaCredito.Estado` (aside nota de crédito): reemplazado texto plano por badge compacto.
4. `Model.RMA.Estado` (aside RMA): reemplazado texto plano por badge compacto.

Los badges del aside usan `px-2 py-0.5 text-[10px]` (más compactos) para respetar el layout de `dl/dt/dd`.

Los badges de la tabla usan `px-2.5 py-1 text-[11px]` consistentes con `AccionProducto` en la misma tabla.

---

## E. Tests / validaciones

- Build Release: ✅ 0 errores, 0 advertencias
- `dotnet test --filter "Devolucion|ProductoUnidad|ProductoController"`: ✅ 339/339 passing
- `git diff --check`: ✅ sin errores de whitespace (aviso CRLF normal de Windows)

No se crearon tests Razor específicos. Los cambios son puramente visuales; no hay lógica de negocio tocada.

---

## F. Qué NO se tocó

- Reglas de negocio en `DevolucionService`
- `DevolucionController`
- Entidades (`Devolucion.cs`, enums)
- Migraciones
- Partial `_EstadoUnidadBadge`
- Helpers existentes (`DevState`, `ResClass`, `AccionClass`)
- Acciones operativas (Aprobar, Completar, Rechazar)
- `RowVersion`
- `MotivoDevolucion` (mantenido como texto plano — descriptor, no estado operativo)
- Módulos de Carlos (Cotización)
- `AGENTS.md`, `CLAUDE.md`, `docs/fase-cotizacion-diseno-v1.md`

---

## G. Riesgos / deuda

- Ningún riesgo funcional. Cambios puramente visuales en helpers locales.
- Si `EstadoProductoDevuelto`, `EstadoNotaCredito` o `EstadoRMA` agregan nuevos valores de enum en el futuro, los helpers caen al case `_` (slate neutro) — safe fallback.
- `MotivoDevolucion` en el aside podría recibir badge en el futuro si se identifica necesidad operativa. Actualmente no lo requiere.

---

## H. Checklist actualizado

### Completado
- [x] 10.9 — badge visual historial unidad física
- [x] 10.10 — badge visual EstadoUnidad en Unidades.cshtml
- [x] 10.11 — partial reutilizable `_EstadoUnidadBadge`
- [x] 10.12 — badges en detalle de devolución (`EstadoProductoDevuelto`, `EstadoNotaCredito`, `EstadoRMA`)

### Pendiente / próximo
- [ ] Revisar vistas de índice de devolución (`Views/Devolucion/Index.cshtml`) para consistencia de badges
- [ ] Evaluar si `MotivoDevolucion` requiere badge en alguna vista de listado
- [ ] Revisar si `EstadoRMA` y `EstadoNotaCredito` aparecen en otras vistas sin normalizar
