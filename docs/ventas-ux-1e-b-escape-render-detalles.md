# VENTAS-UX-1E-B — Escape seguro en celdas dinámicas de renderDetalles()

## A. Objetivo

Corregir de forma mínima y segura el escape de datos dinámicos en las celdas generadas por `renderDetalles()` en `wwwroot/js/venta-create.js`, reduciendo el riesgo XSS en el frontend sin alterar estructura visual, comportamiento, eventos ni lógica funcional.

## B. Base y contexto

- Rama base: `main` en `18975bf` (MISA-INVENTARIO-UX-0 sobre e97e6f7 VENTAS-UX-1E-A).
- Fase previa: VENTAS-UX-1E-A agregó `aria-label="Eliminar ${esc(d.nombre)}"` al botón eliminar en `renderDetalles()`.
- Esta fase cubre la deuda complementaria: las celdas de texto de la fila renderizada que aún interpolaban strings sin escape.

## C. Deuda tomada desde VENTAS-UX-1E-A

VENTAS-UX-1E-A documentó la siguiente deuda abierta:

> En `wwwroot/js/venta-create.js`, dentro de `renderDetalles()`, todavía hay interpolaciones dinámicas en celdas `<td>` sin escape seguro: `d.nombre`, `d.codigo`, `productoUnidadLabel` y otros valores derivados de producto/detalle.

## D. Archivos auditados

- `wwwroot/js/venta-create.js` — función `renderDetalles()` (líneas 1193–1245)
- `TheBuryProyect.Tests/Unit/VentaCreateUiContractTests.cs` — bloque de tests VENTAS-UX-1E-A y patrón `ExtractFunction`
- `Views/Venta/_VentaCrearModal.cshtml` — solo lectura, no modificado
- `docs/ventas-ux-1e-a-accesibilidad-js-botones-detalle.md` — referencia

## E. Hallazgos en renderDetalles()

### Interpolaciones sin escape (corregidas)

| Línea | Expresión original | Riesgo | Acción |
|-------|--------------------|--------|--------|
| 1207 | `${d.codigo}` | XSS — string de texto del servidor | → `${esc(d.codigo)}` |
| 1209 | `${d.nombre}` | XSS — string de texto del servidor | → `${esc(d.nombre)}` |

### Ya escapados antes de esta fase

| Línea | Expresión | Estado |
|-------|-----------|--------|
| 1210 | `${esc(d.productoUnidadLabel)}` | ✓ ya escapado |
| 1217 | `${esc(d.nombre)}` en `aria-label` | ✓ escapado por VENTAS-UX-1E-A |

### Numéricos — sin necesidad de esc()

| Línea | Expresión | Motivo |
|-------|-----------|--------|
| 1212 | `${d.cantidad}` | número entero controlado |
| 1213 | `${formatCurrency(d.precioUnitario)}` | función interna controlada |
| 1214 | `${d.descuento}%` | número parseFloat, literal `%` |
| 1215 | `${formatCurrency(d.subtotal)}` | función interna controlada |

### Hidden inputs — seguros por construcción

Las líneas 1226–1239 usan `document.createElement('input')` con `.value = val` — el DOM escapa automáticamente el valor, no hay riesgo XSS.

## F. Cambios aplicados

### `wwwroot/js/venta-create.js`

Función `renderDetalles()`:

```diff
-  <td class="py-4 px-2 text-xs font-mono">${d.codigo}</td>
+  <td class="py-4 px-2 text-xs font-mono">${esc(d.codigo)}</td>

-  <div>${d.nombre}</div>
+  <div>${esc(d.nombre)}</div>
```

Solo 2 líneas modificadas dentro del template literal de `renderDetalles()`.

## G. Seguridad frontend / escape

La función `esc()` (líneas 211–218) escapa los 5 caracteres HTML críticos:

- `&` → `&amp;`
- `<` → `&lt;`
- `>` → `&gt;`
- `"` → `&quot;`
- `'` → `&#39;`

Con este cambio, todos los campos de texto derivados del servidor que se interpolan en la fila de detalle quedan escapados. La superficie XSS en `renderDetalles()` queda eliminada.

## H. Contratos preservados

- Clases CSS: sin cambios.
- `data-index` del botón eliminar: sin cambios.
- `aria-label` del botón eliminar: sin cambios (preservado de VENTAS-UX-1E-A).
- `btn-eliminar-detalle`: sin cambios.
- Eventos del tbody: sin cambios.
- Orden de columnas: sin cambios.
- Formato de moneda: sin cambios.
- Payloads de hidden inputs: sin cambios.
- Endpoints: sin cambios.
- IDs del DOM: sin cambios.

## I. Qué no se tocó

- Controllers, Services, Models, ViewModels, DTOs, Migrations.
- `Views/Venta/_VentaCrearModal.cshtml` y `Create_tw.cshtml`.
- CSS / Tailwind.
- Cálculos de subtotal, descuento, total.
- Lógica de stock, caja, crédito.
- Flujo de Cotización.
- `Program.cs`, DI, endpoints.
- `AGENTS.md`, `CLAUDE.md`, `.claude/settings.local.json`, `skills-lock.json`.
- `docs/misa-inventario-ux-0-auditoria-inicial.md`.

## J. Riesgo funcional

**Ninguno.** Los cambios son estrictamente de escapado. El navegador renderiza el texto igual que antes para valores normales. Solo cambia el comportamiento ante valores con caracteres HTML especiales (`<`, `>`, `&`, `"`, `'`), que en datos de producto legítimos no se esperan, pero que ahora quedan neutralizados en lugar de interpretarse como markup.

## K. Tests

### Tests nuevos (VENTAS-UX-1E-B)

| Test | Verifica |
|------|----------|
| `VentaCreateJs_RenderDetalles_CeldaCodigoUsaEsc` | `esc(d.codigo)` aparece en renderDetalles |
| `VentaCreateJs_RenderDetalles_CeldaNombreUsaEscEnContenido` | `esc(d.nombre)` aparece ≥ 2 veces en renderDetalles (celda + aria-label) |

### Tests previos que siguen pasando

- `VentaCreateJs_RenderDetalles_BtnEliminarAriaLabelUsaEscParaNombre` — sigue pasando (esc en aria-label intacto).
- `VentaCreateJs_RenderDetalles_BtnEliminarTieneAriaLabel` — sigue pasando.
- `VentaCreateJs_RenderDetalles_BtnEliminarConservaClasesYDataIndex` — sigue pasando.
- `VentaCreateJs_FuncionEscExiste` — sigue pasando.

## L. Validaciones

| Validación | Resultado |
|------------|-----------|
| `dotnet build --configuration Release` | ✓ Compilación correcta — 0 errores, 0 advertencias |
| `dotnet test --filter "VentaCreate"` | ✓ 79/79 (77 previos + 2 nuevos) |
| `git diff --check` | Sin trailing whitespace en archivos de esta fase |
| Playwright | No ejecutado — cambio de escape puro, sin alteración visual ni de interacción |

## M. Procesos

No se iniciaron procesos externos en esta fase. El servidor de desarrollo no fue arrancado.

## N. Deudas restantes

- `formatearUnidadDisponible()` (líneas 220–228): interpola `serie` y `ubicacion` sin esc, pero el resultado va a un `<option>` creado via DOM (`textContent`/`label`), no a innerHTML. Seguro por construcción. No requiere acción en esta fase.
- `dropdownClientes.innerHTML` y `dropdownProductos.innerHTML` (líneas 901, 998): ya usan `esc()` para los campos textuales críticos. Fuera del alcance de esta fase.
- Otros `innerHTML` en el archivo (badges, listas de crédito, documentos): fuera del alcance de Venta Create renderDetalles y de esta fase.

## O. Próximo paso recomendado

**VENTAS-UX-1F — Mobile visual y experiencia de cobro en Venta/Create.**

Objetivo tentativo:
- Botón confirmar sticky en mobile.
- Resumen de totales más visible en pantallas chicas.
- Mejor jerarquía visual del cierre de venta.
- Sin backend, sin cálculos, sin endpoints, sin payloads, sin stock/caja/crédito, sin Cotización.
