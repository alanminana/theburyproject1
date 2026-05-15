# Fase 10.17 — Diagnóstico y normalización de badges en Catálogo

## A. Objetivo

Revisar íntegramente `Views/Catalogo/Index_tw.cshtml` y detectar estados renderizados como texto plano, flags visuales inconsistentes, badges con clases conflictivas, badges sin border, opacidad divergente y oportunidades de reutilizar partials/helpers existentes. Normalizar solo si hay divergencias reales.

---

## B. Diagnóstico previo

### Validación inicial

- **HEAD**: `56a45b0` — Fase 10.16 cerrada correctamente.
- **Build Release**: 0 errores, 0 warnings.
- **git diff --check**: limpio.
- **Archivos ajenos no trackeados**: `docs/fase-cotizacion-diseno-v1.md` (Carlos, no tocado).

### Estructura del archivo (2383 líneas)

| Sección | Líneas | Descripción |
|---|---|---|
| Variables de inicialización | 1–12 | Bloque @{ } con contadores y listas |
| TempData Alerts | 20–34 | Toasts de éxito/error |
| Tab Bar + Acciones | 37–80 | Tabs Productos/Categorías/Marcas + botones |
| Tab Productos | 83–435 | Filtros + tabla + selection bar + footer |
| Tab Categorías | 437–557 | Tabla + footer |
| Tab Marcas | 559–682 | Tabla + footer |
| Modales | 684–2383 | Nuevo/Editar Producto, Categoría, Marca, Historial Precio, Ajuste Masivo, Movimientos, etc. |

---

## C. Estados/flags encontrados

### Tabla Productos

| Flag/Estado | Tipo de render | Líneas | Observación |
|---|---|---|---|
| Stock (Sin Stock / Stock Bajo / Normal) | Badge `rounded-full` con border y bg/10 | 232–248, 297–300 | Internamente consistente |
| Activo/Inactivo (fila) | `opacity-50` en `<tr>` | 252 | Intencional |
| Inactivo (texto inline en nombre) | Texto plano `text-red-400 text-[10px]` | 288–291 | Intencional: tabla sin columna Estado |
| EsDestacado | Ícono estrella amber/slate | 265–273 | Correcto, no es badge |
| JS chips (selección, ajuste masivo) | Driven por JS | 71–78 | No renderizados por Razor |

### Tabla Categorías

| Flag/Estado | Tipo de render | Líneas |
|---|---|---|
| Activo/Inactivo (fila) | `opacity-50` en `<tr>` | 484 |
| Inactivo inline en nombre | Texto plano `text-red-400` | 497–500 |
| Estado en columna dedicada | Badge `rounded-full` con border | 506–517 |

### Tabla Marcas

| Flag/Estado | Tipo de render | Líneas |
|---|---|---|
| Activo/Inactivo (fila) | `opacity-50` en `<tr>` | 607 |
| Inactivo inline en nombre | Texto plano `text-red-400` | 620–623 |
| Estado en columna dedicada | Badge `rounded-full` con border | 630–641 |

---

## D. Divergencias detectadas y decisiones

### Divergencia 1: 5 variables Razor muertas (CORREGIDA)

**Archivos**: líneas 7–11 del archivo original.

```csharp
var stockBajoCount = Model.ProductosStockBajo;
var productosActivos = productos.Count(p => p.Activo);
var productosInactivos = productos.Count - productosActivos;
var categoriasActivas = Model.CategoriasListado.Count(c => c.Activo);
var marcasActivas = Model.MarcasListado.Count(m => m.Activo);
```

Estas 5 variables estaban declaradas pero nunca renderizadas en ningún punto del template. Código muerto de Razor. Se eliminaron.

**Riesgo**: ninguno. Son C# local variables solo visibles en el bloque Razor.

### Divergencia 2: Clases conflictivas en botón "Limpiar filtros" (CORREGIDA)

**Línea original 182**:
```html
class="p-2.5 bg-slate-200 bg-slate-800 text-slate-300 hover:bg-slate-300 hover:bg-slate-700 ..."
```

Tenía `bg-slate-200` + `bg-slate-800` y `hover:bg-slate-300` + `hover:bg-slate-700` — artefactos de una versión light mode anterior. Quedó:
```html
class="p-2.5 bg-slate-800 text-slate-300 hover:bg-slate-700 ..."
```

### Divergencia 3: Clases de texto conflictivas en iconos de estado vacío (CORREGIDA)

3 iconos de estado vacío (Productos, Categorías, Marcas) tenían `text-slate-300 text-slate-600` simultáneamente. Se eliminó `text-slate-300` (residual). Quedó `text-slate-600` en todos.

Afectados: líneas 377, 540, 664 del archivo original.

### No-divergencias verificadas

| Ítem | Resultado |
|---|---|
| Todos los badges tienen `border border-{color}-500/30` | ✓ Correcto |
| Patrón `bg-{color}-500/20` consistente internamente | ✓ Sin conflicto |
| No hay helpers locales duplicados | ✓ |
| `_EstadoUnidadBadge.cshtml` no aplica (no hay EstadoUnidad en Catálogo) | ✓ |
| No hay JS que renderice badges de estado | ✓ |
| Texto "Inactivo" inline es intencional (Productos sin columna Estado) | ✓ Mantener |
| Redundancia inline en Categorías/Marcas aceptable por diseño | ✓ Mantener |

---

## E. Cambios aplicados

| Archivo | Tipo de cambio | Detalle |
|---|---|---|
| `Views/Catalogo/Index_tw.cshtml` | Eliminar código muerto | 5 variables Razor nunca renderizadas |
| `Views/Catalogo/Index_tw.cshtml` | Limpiar clases conflictivas | `bg-slate-200`/`hover:bg-slate-300` eliminados del botón "Limpiar filtros" |
| `Views/Catalogo/Index_tw.cshtml` | Limpiar clases conflictivas | `text-slate-300` eliminado de 3 iconos de estado vacío |

---

## F. Helpers/partials reutilizados o creados

- `_EstadoUnidadBadge.cshtml`: **no aplica**. El Catálogo no usa el enum `EstadoUnidad`. Los badges de stock y Activo/Inactivo son locales y correctos.
- No se crearon helpers nuevos. No había duplicación real que lo justificara.

---

## G. Tests / validaciones

```
dotnet test --filter "Catalogo|Producto|ProductoUnidad" --configuration Release
→ 578/578 passing, 0 errores.

dotnet build --configuration Release
→ 0 errores, 0 warnings.

git diff --check → limpio.
```

---

## H. Qué NO se tocó

- Controllers (CatalogoController, ProductoController) — solo lectura.
- Services.
- Entidades/Enums.
- JS del catálogo.
- Modales del catálogo (lógica de formularios).
- Filtros, botones de acción, paginación.
- AGENTS.md / CLAUDE.md (cambios preexistentes ajenos).
- `docs/fase-cotizacion-diseno-v1.md` (Carlos).

---

## I. Riesgos / deuda

- **Baja deuda visual**: el patrón de badges usa `/20` en bg y `-400` en text, mientras que `_EstadoUnidadBadge` usa `/10` y `-300`. Esta variación es aceptable porque Catálogo no usa el enum `EstadoUnidad` y el patrón es internamente consistente. No se considera divergencia activa.
- **KPI cards**: las variables eliminadas sugieren que en algún momento hubo o se planificó una sección de resumen de métricas (productos activos, inactivos, stock bajo). Si se quiere agregar, el ViewModel ya tiene `ProductosStockBajo` y las colecciones necesarias.

---

## J. Checklist actualizado

- [x] Validación inicial: HEAD 56a45b0, build limpio
- [x] Lectura completa de `Views/Catalogo/Index_tw.cshtml` (2383 líneas)
- [x] Diagnóstico de todos los estados/flags visuales
- [x] Clasificación de componentes
- [x] Eliminación de 5 variables Razor muertas
- [x] Limpieza de clases bg conflictivas en botón "Limpiar filtros"
- [x] Limpieza de clases text conflictivas en 3 iconos de estado vacío
- [x] Build Release: 0 errores, 0 warnings
- [x] Tests: 578/578 passing
- [x] git diff --check: limpio
- [x] Documentación fase 10.17
- [x] Commit y push

---

## K. Siguiente micro-lote recomendado

**Fase 10.18**: Revisar `Views/DocumentoCliente/Index_tw.cshtml` — ya se detectó `EstadoNombre` renderizado como texto plano (línea 255). Es candidato para normalizar badges de estado de documentos de cliente con el patrón canónico.
