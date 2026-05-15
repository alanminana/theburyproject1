# Fase 10.13 — Polish UI: badges consistentes en índice de devoluciones

## A. Objetivo

Normalizar los badges de estados operativos en `Views/Devolucion/Index.cshtml` para que sean
visualmente consistentes con `Views/Devolucion/Detalles.cshtml`.

Alcance: polish visual únicamente.
Sin cambios a controllers, services, entidades, migraciones ni reglas de negocio.

---

## B. Diagnóstico previo

### Divergencias detectadas

| Aspecto | Index.cshtml (antes) | Detalles.cshtml (referencia) |
|---|---|---|
| `border` en `<span>` badge | ausente | presente |
| Opacidad de fondo | `/15` (ej. `bg-amber-500/15`) | `/10` (ej. `bg-amber-500/10`) |
| `border-{color}-*/30` en helper | ausente | presente |
| Fallback `DevState` | clases CSS conflictivas (`bg-slate-100 text-slate-700 bg-slate-800 text-slate-300`) | `border-slate-600/30 bg-slate-600/10 text-slate-400` |
| Fallback `CritClass` | mismo bug | limpio |
| `EstadoDevolucion.Cancelada` | no mapeado | — |
| `TipoResolucionDevolucion.NotaCredito` | no mapeado (caía en `_`) | mapeado |
| `EstadoGarantia.Utilizada` / `Cancelada` | no mapeados | — |

### Clasificación de componentes

| Componente | Clasificación | Decisión |
|---|---|---|
| `Views/Devolucion/Index.cshtml` | canónico | modificar |
| `Views/Devolucion/Detalles.cshtml` | canónico | referencia visual |
| `EstadoDevolucion` | canónico | solo lectura |
| `TipoResolucionDevolucion` | canónico | solo lectura |
| `EstadoGarantia` | canónico | solo lectura |

---

## C. Enums normalizados

### EstadoDevolucion (6 valores)

| Valor | Color |
|---|---|
| Pendiente | amber |
| EnRevision | sky |
| Aprobada | indigo |
| Completada | emerald |
| Rechazada | rose |
| Cancelada | slate (neutro) |

### TipoResolucionDevolucion (4 valores)

| Valor | Color |
|---|---|
| NotaCredito | amber |
| ReembolsoDinero | rose |
| CambioMismoProducto | sky |
| CambioOtroProducto | violet |

### EstadoGarantia — GarClass (lógica combinada fecha + estado)

| Condición | Color |
|---|---|
| FechaVencimiento < hoy OR Vencida | rose |
| EnUso | sky |
| Utilizada OR Cancelada | slate (neutro) |
| Próxima a vencer (≤30 días) | amber |
| Vigente normal | emerald |

### Impacto (CritClass — calculado)

| Valor | Color |
|---|---|
| Alto | rose |
| Medio | amber |
| Bajo (default) | slate (neutro) |

---

## D. Cambios visuales aplicados

Archivo modificado: `Views/Devolucion/Index.cshtml`

### Helpers normalizados

- `DevState`: patrón `border-{color}-500/30 bg-{color}-500/10 text-{color}-300`, añadido `Cancelada`, fallback corregido.
- `ResClass`: patrón normalizado, añadido `NotaCredito` como caso explícito, fallback cambiado a slate.
- `GarClass`: patrón normalizado, añadidos `Utilizada` y `Cancelada`, refactorizado sin llaves innecesarias.
- `CritClass`: patrón normalizado, fallback corregido (eliminadas clases conflictivas).

### Spans de badge

Añadida clase `border` a los 4 `<span>` de badges en filas de tabla:

- Resolución (`@ResClass`)
- Impacto (`@CritClass`)
- Estado devolución (`@DevState`)
- Estado garantía (`@GarClass`)

---

## E. Tests / validaciones

```
dotnet build --configuration Release   → 0 errores, 0 advertencias
dotnet test --filter "Devolucion|ProductoUnidad|ProductoController" → 339/339 passing
git diff --check → OK
```

No se esperan tests Razor específicos para cambios de clase CSS.

---

## F. Qué NO se tocó

- Controllers (DevolucionController, GarantiaController)
- Services de devolución, RMA, nota de crédito, garantía
- Entidades y enums (solo lectura)
- Migraciones
- JS: devolucion-module.js, devolucion-index.js
- Acciones operativas (Aprobar, Rechazar, Completar)
- RowVersion
- Módulos de Carlos: Cotización, worktree `carlos/cotizacion-v1-contratos`
- `docs/fase-cotizacion-diseno-v1.md`
- Venta, Caja, Factura, ProductoUnidadService

---

## G. Riesgos / deuda

- **Riesgo bajo**: cambio puramente visual, sin lógica de negocio.
- **Deuda remanente**: si en el futuro se agregan nuevos valores a `EstadoDevolucion`, `TipoResolucionDevolucion` o `EstadoGarantia`, deberán mapearse en los helpers de Index y Detalles.
- `GarClass` mantiene lógica basada en fecha + estado: no fue extraída a partial global porque depende del objeto `Garantia`, no solo del enum. Decisión correcta para este alcance.

---

## H. Checklist actualizado

- [x] 10.9 — badge visual en historial de unidad física
- [x] 10.10 — badge EstadoUnidad en Unidades.cshtml
- [x] 10.11 — partial reutilizable `_EstadoUnidadBadge.cshtml`
- [x] 10.12 — badges en detalle de devolución (Detalles.cshtml)
- [x] 10.13 — badges consistentes en índice de devoluciones (Index.cshtml)

### Siguiente micro-lote recomendado

**10.14 — Consistencia de badges en otras vistas de postventa (RMA, NotaCredito)**

Verificar si `Views/RMA/` o vistas de `NotaCredito` presentan las mismas divergencias:
sin `border`, fallbacks rotos, opacidades `/15` en lugar de `/10`.
Alcance idéntico: polish visual, sin tocar reglas de negocio.
