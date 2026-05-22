# VENTAS-UX-1E-A — Accesibilidad JS mínima en botones dinámicos de detalle de venta

## A. Objetivo

Agregar `aria-label` al botón dinámico generado por `renderDetalles()` en `venta-create.js`.

El botón `btn-eliminar-detalle` era un botón ícono sin texto accesible. Los lectores de pantalla
no podían describir su acción. Esta fase lo corrige sin cambiar comportamiento, estructura visual
ni lógica funcional.

## B. Base y contexto

- Base: `c4bfab9` — VENTAS-UX-1D integrada.
- Rama: `kira/ventas-ux-1e-a-accesibilidad-js-botones-detalle`.
- Fase dentro del rework VENTAS-UX: mejora incremental de accesibilidad.

## C. Deuda tomada desde VENTAS-UX-1D

VENTAS-UX-1D agregó `scope="col"` y `aria-label="Acciones"` a los headers de la tabla de detalle
en el modal. Sin embargo, los botones de acción por fila son generados dinámicamente por JavaScript
y no existen en Razor. Quedaron fuera del alcance de VENTAS-UX-1D. Esta fase cierra esa deuda.

## D. Archivos auditados

- `wwwroot/js/venta-create.js` — función `renderDetalles()` y función `esc()`
- `TheBuryProyect.Tests/Unit/VentaCreateUiContractTests.cs` — tests de contrato existentes
- `Views/Venta/_VentaCrearModal.cshtml` — solo lectura, sin modificaciones
- `Views/Venta/Create_tw.cshtml` — solo lectura, sin modificaciones

## E. Hallazgos

### Función renderDetalles()

- Ubicación: `wwwroot/js/venta-create.js`, línea ~1205.
- Usa `tbodyDetalles.innerHTML` con template strings y `.map()` sobre el array `detalles[]`.
- Genera una fila `<tr>` por producto con 7 celdas.

### Botones dinámicos detectados

**Solo hay un botón por fila:** `btn-eliminar-detalle`.

- Contenido: ícono Material Symbols `delete` — sin texto visible.
- Antes de esta fase: sin `aria-label`, sin `title`. Inaccesible para lectores de pantalla.
- No existe botón de "configurar pago por item" — fue eliminado en fases anteriores.

### Función esc()

- Existe en el mismo archivo.
- Escapa: `&`, `<`, `>`, `"` (como `&quot;`), `'` (como `&#39;`).
- Es segura para usar dentro de atributos HTML con comillas dobles.

## F. Cambios aplicados

### `wwwroot/js/venta-create.js`

Agregado `aria-label="Eliminar ${esc(d.nombre)}"` al botón `btn-eliminar-detalle` en `renderDetalles()`.

**Antes:**
```html
<button type="button" class="btn-eliminar-detalle ..." data-index="${i}">
```

**Después:**
```html
<button type="button" class="btn-eliminar-detalle ..." data-index="${i}" aria-label="Eliminar ${esc(d.nombre)}">
```

Uso de `esc(d.nombre)` para escape seguro del nombre del producto en el atributo.

### `TheBuryProyect.Tests/Unit/VentaCreateUiContractTests.cs`

4 tests nuevos en sección `VENTAS-UX-1E-A`:

1. `VentaCreateJs_RenderDetalles_BtnEliminarTieneAriaLabel` — verifica que `renderDetalles()` incluye `aria-label=` junto a `btn-eliminar-detalle`.
2. `VentaCreateJs_RenderDetalles_BtnEliminarAriaLabelUsaEscParaNombre` — verifica que el `aria-label` usa `esc(d.nombre)` para escape seguro.
3. `VentaCreateJs_RenderDetalles_BtnEliminarConservaClasesYDataIndex` — verifica que `btn-eliminar-detalle`, `data-index=` y `type="button"` se conservan.
4. `VentaCreateJs_FuncionEscExiste` — verifica que `esc()` existe y escapa comillas dobles.

## G. Contratos preservados

- Clase `.btn-eliminar-detalle` conservada.
- `data-index="${i}"` conservado.
- `type="button"` conservado.
- Ícono `delete` de Material Symbols conservado.
- Evento `click` en `tbodyDetalles` con `.btn-eliminar-detalle` conservado.
- `tbodyDetalles.innerHTML` como mecanismo de render conservado.
- Función `esc()` no modificada.
- IDs, `data-*`, `asp-*`, payloads, endpoints: sin cambios.
- Tests existentes (73): todos siguen pasando.

## H. Qué no se tocó

- Controllers
- Services
- Models / ViewModels / DTOs / Migrations
- `wwwroot/css/`
- `Views/Venta/_VentaCrearModal.cshtml`
- `Views/Venta/Create_tw.cshtml`
- Cálculos de totales
- Flujo de stock, caja, crédito
- Cotización
- Endpoints y payloads
- Confirmación de venta
- `Program.cs`
- `AGENTS.md`, `CLAUDE.md`

## I. Accesibilidad

- Lectores de pantalla ahora pueden anunciar "Eliminar [nombre del producto]" al enfocar el botón.
- El nombre del producto en el `aria-label` identifica exactamente qué se va a eliminar.
- Sin cambios visuales para usuarios sin tecnología asistiva.

## J. Seguridad frontend / escape

- `esc()` escapa `"` como `&quot;` — el nombre del producto no puede romper el atributo `aria-label`.
- No se usa `innerHTML` con datos sin escape para el atributo.
- No se introducen nuevos vectores XSS.
- La celda `${d.nombre}` en el `<td>` ya existía sin escape antes de esta fase — esa deuda queda
  pendiente para una fase de seguridad específica (fuera del alcance de VENTAS-UX-1E-A).

## K. Riesgo funcional

Riesgo: **muy bajo**.
- Solo se agrega un atributo HTML estático al botón.
- No se cambia ningún evento, clase funcional, selector CSS, payload ni endpoint.
- No se altera el orden de render ni la estructura DOM.

## L. Tests

- VentaCreate antes: 73/73 OK.
- VentaCreate después: **77/77 OK** (4 tests nuevos agregados).
- Tests nuevos cubren: `aria-label` presente, uso de `esc()`, conservación de clases/data-*, existencia de función `esc()`.

## M. Validaciones

- `dotnet build --configuration Release`: **0 errores, 0 advertencias**.
- `dotnet test --configuration Release --filter "VentaCreate"`: **77/77 OK**.
- `git diff --check`: warnings solo en AGENTS.md y CLAUDE.md (preexistentes, no commiteados).
- Playwright: no ejecutado. El cambio es solo `aria-label` en atributo; no altera estructura visual
  ni comportamiento de botones.

## N. Procesos

- No se inició `dotnet run` ni servidor de desarrollo.
- No se inició Playwright browser.
- Los procesos de build y test finalizan solos.

## O. Deudas restantes

1. **XSS en celdas de renderDetalles**: `${d.nombre}`, `${d.codigo}` y otros valores se interpolan
   sin `esc()` en celdas `<td>`. Riesgo bajo en contexto ERP (datos vienen del backend autenticado),
   pero es deuda documentada. Requiere fase de seguridad JS específica.
2. **Mobile y sticky footer**: pendiente de fases posteriores VENTAS-UX.
3. **Resumen de venta**: no rediseñado — pendiente de fase posterior.

## P. Próximo paso recomendado

```
VENTAS-UX-1E-B — Revisar accesibilidad de otros elementos dinámicos en venta-create.js
(inputs, selects, labels generados por JS fuera de renderDetalles) O
VENTAS-UX-1F — Mobile y layout del formulario de venta
```
