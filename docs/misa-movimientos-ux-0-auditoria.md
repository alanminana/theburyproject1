# MISA-MOVIMIENTOS-UX-0 — Auditoría UX de Movimientos / Kardex / Movimientos diarios

**Fase:** Audit-only  
**Base:** `476bb20` — MISA-CATALOGO-CIERRE integrada  
**Rama:** `misa/movimientos-ux-0-auditoria`  
**Fecha:** 2026-05-23  
**Agente:** Misa

---

## A. Objetivo

Auditar completamente la UX/UI frontend del módulo de Movimientos de Inventario, Kardex y flujos asociados (AlertaStock, Producto/Unidades, Catálogo) sin modificar ningún archivo de código productivo.

Identificar:
- pantallas existentes y sus relaciones;
- problemas de accesibilidad, mobile, contraste y semántica;
- deuda funcional (PDF/Excel, paginación, filtros faltantes);
- problemas de navegación cruzada;
- propuesta de fases de mejora.

---

## B. Base y contexto

- Main HEAD: `476bb20` — MISA-CATALOGO-CIERRE integrada.
- Módulos cerrados previamente: Ventas/Kira, Inventario físico, Catálogo, Modales de Catálogo, Botón Movimientos en Catálogo, Tablas y accesibilidad de Catálogo.
- Esta auditoría abre el frente: Movimientos / Kardex / Movimientos diarios.
- El botón "Movimientos" en Catálogo recibió corrección de color en MISA-CATALOGO-UX-1A.
- El modal de Movimientos recibió mejoras de focus trap y aria-live en MISA-CATALOGO-UX-1E/1F/1G como parte del cierre de Catálogo.

---

## C. Decisiones del usuario (preexistentes)

- No reemplazar modales por vistas nuevas.
- El modal de Movimientos en Catálogo sigue siendo modal; puede recibir rework visual/semántico en fases futuras.
- Si un modal está inline y el archivo es grande, puede extraerse a partial en fase futura, pero sigue siendo modal.
- Movimientos debe seguir accesible desde Catálogo.
- No mezclar UX visual con permisos, backend o reportes funcionales.
- No tocar stock funcional ni reglas de negocio en ninguna fase UX.

---

## D. Archivos auditados

### Vistas
| Archivo | Estado |
|---|---|
| `Views/MovimientoStock/Index_tw.cshtml` | Auditado |
| `Views/MovimientoStock/Kardex_tw.cshtml` | Auditado |
| `Views/MovimientoStock/Create_tw.cshtml` | Auditado |
| `Views/Catalogo/Index_tw.cshtml` (sección modal Movimientos, líneas 2157–2337) | Auditado |
| `Views/AlertaStock/Index_tw.cshtml` | Auditado |
| `Views/AlertaStock/Criticos_tw.cshtml` | Auditado |
| `Views/Producto/Unidades.cshtml` (header y link a Kardex) | Auditado parcial |
| `Views/Producto/UnidadesGlobal.cshtml` | Auditado parcial |

### JavaScript
| Archivo | Estado |
|---|---|
| `wwwroot/js/movimientos-inventario-modal.js` | Auditado completo |
| `wwwroot/js/catalogo-index.js` | Auditado (sección relevante) |

### CSS
| Archivo | Relevancia |
|---|---|
| `wwwroot/css/horizontal-scroll-affordance.css` | Ausente en MovimientoStock/Index y Kardex |
| `wwwroot/css/shared-components.css` | Cargado globalmente |
| `wwwroot/css/catalogo-module.css` | Solo en Catálogo |

### Controllers y servicios
| Archivo | Estado |
|---|---|
| `Controllers/MovimientoStockController.cs` | Auditado completo |
| `Services/Interfaces/IMovimientoStockService.cs` | Auditado completo |
| `Services/MovimientoStockService.cs` | Auditado parcial |

### Tests
| Archivo | Estado |
|---|---|
| `TheBuryProyect.Tests/Integration/MovimientoStockControllerTests.cs` | Auditado (estructura) |

---

## E. Mapa de pantallas

| Pantalla | Ruta | Archivo | Descripción |
|---|---|---|---|
| Movimientos Index | `/MovimientoStock` | `Index_tw.cshtml` | Historial general filtrable de todos los productos |
| Kardex | `/MovimientoStock/Kardex/{id}` | `Kardex_tw.cshtml` | Historial completo de un producto/SKU específico |
| Crear ajuste | `/MovimientoStock/Create` | `Create_tw.cshtml` | Formulario para registrar ajuste manual de stock |
| Modal Movimientos | Inline en Catálogo | `Catálogo Index_tw.cshtml` (líneas 2157–2337) | Modal JS con fetch a `/MovimientoStock/ListJson` |
| AlertaStock Index | `/AlertaStock` | `AlertaStock/Index_tw.cshtml` | Grilla de alertas de stock con filtros |
| AlertaStock Críticos | `/AlertaStock/Criticos` | `AlertaStock/Criticos_tw.cshtml` | Subconjunto de productos agotados o críticos |
| Producto Unidades | `/Producto/Unidades/{productoId}` | `Producto/Unidades.cshtml` | Unidades físicas individuales + link a Kardex |
| Producto UnidadesGlobal | `/Producto/UnidadesGlobal` | `Producto/UnidadesGlobal.cshtml` | Reporte global de unidades, sin link a Kardex |

---

## F. Mapa de flujos

### Flujo operativo de revisión diaria de movimientos

```
Operador → Catálogo Index
    → botón "Movimientos" (global) → Modal Movimientos (fetch /ListJson)
    → fila de producto → botón "Movimientos" por fila → Modal (filtrado por producto)
    → link "Ver kardex" en modal → Kardex del producto
    → botón "Registrar ajuste" en Kardex → Create
    → post OK → redirect a Kardex

Operador → Movimientos Index (navegación directa)
    → filtros (Producto, Tipo, FechaDesde, FechaHasta) → GET search
    → link "Ver kardex" por fila → Kardex del producto

Operador → AlertaStock
    → ver stock crítico/agotado
    → NO hay link a Kardex ni a Movimientos (DEUDA)

Operador → Producto/Unidades (desde Catálogo)
    → botón "Ver kardex SKU" → Kardex del producto (CORRECTO)
    → NO hay link a Movimientos Index desde Unidades

Operador → Producto/UnidadesGlobal
    → NO hay link a Kardex ni a Movimientos (DEUDA)
```

### Flujo de create ajuste de stock

```
Create GET (con productoId opcional)
    → muestra stock actual si hay producto preseleccionado
    → formulario: Producto, Tipo, Cantidad, Motivo, Referencia
    → hints visuales por tipo (Entrada / Salida / Ajuste)
    → POST → validación servidor
    → success → redirect a Kardex del producto
    → error → vuelve al formulario con errores
```

---

## G. Mapa de JS

| Archivo | Función principal | Observaciones |
|---|---|---|
| `movimientos-inventario-modal.js` | Modal de Movimientos en Catálogo | Open/close, fetch, render table, paginación, filtros |
| `catalogo-index.js` | Búsqueda client-side en Catálogo | Delega apertura de modal al módulo |
| `catalogo-module.js` | Focus trap del modal | `window.CatalogoModule.trapFocus(modal, e)` |

No existe JS dedicado para:
- `MovimientoStock/Index` (todo server-side con form GET)
- `MovimientoStock/Kardex` (solo lectura server-side)
- `MovimientoStock/Create` (script inline para hints de tipo)
- Export PDF/Excel (no implementado)

---

## H. Mapa de CSS

| Clase / Feature | Dónde se carga | Observaciones |
|---|---|---|
| `horizontal-scroll-affordance.css` | AlertaStock/Index, Catálogo/Index | **Ausente** en MovimientoStock/Index y Kardex |
| `data-oc-scroll` | AlertaStock/Index, Catálogo/Index | **Ausente** en MovimientoStock/Index y Kardex |
| `table-erp-wrapper` | MovimientoStock/Index, Kardex | Solo `overflow-x-auto` sin affordance |
| `badge-erp`, `btn-erp-*`, `row-action` | AlertaStock | No usados en MovimientoStock — usa clases Tailwind directas |
| `shared-components.css` | Global | Componentes canónicos del sistema |

---

## I. Mapa de endpoints / controladores

| Endpoint | Método | Permiso | Descripción |
|---|---|---|---|
| `/MovimientoStock` | GET | `movimientos:view` | Index con filtros |
| `/MovimientoStock/ListJson` | GET | `movimientos:view` | JSON para modal |
| `/MovimientoStock/Kardex/{id}` | GET | `movimientos:view` | Kardex por producto |
| `/MovimientoStock/Create` | GET | `movimientos:view` | Formulario ajuste |
| `/MovimientoStock/Create` | POST | `movimientos:view` | Registrar ajuste |
| `/MovimientoStock/GetProductoInfo/{id}` | GET | `movimientos:view` | Info producto (API) |
| `/MovimientoStock/BuscarProductos` | GET | `movimientos:view` | Autocomplete (API) |

**Ausentes (deuda funcional):**
- `/MovimientoStock/ExportPdf` — no existe
- `/MovimientoStock/ExportExcel` — no existe

El controller tiene `[Authorize]` + `[PermisoRequerido(Modulo = "movimientos", Accion = "view")]` a nivel de clase — todos los endpoints requieren autenticación y permiso.

---

## J. Hallazgos — MovimientoStock/Index

### J1. Tabla sin scroll affordance
- La tabla usa `min-w-[960px]` + `overflow-x-auto` en un wrapper `table-erp-wrapper`.
- **No carga `horizontal-scroll-affordance.css` ni usa `data-oc-scroll`.**
- En mobile el scroll horizontal ocurre silenciosamente, sin hint visual ni gradiente lateral.
- Comparación: AlertaStock y Catálogo ya usan `data-oc-scroll` correctamente.
- **Severidad: media** — el scroll existe pero no se indica.

### J2. Sin paginación
- El listado carga todos los movimientos que devuelve `SearchAsync` sin paginación server-side ni client-side.
- Con un historial de meses o años, la tabla puede volverse muy larga.
- El modal en Catálogo sí tiene paginación client-side (25/50/100 filas).
- **Severidad: media** — problema de performance y usabilidad a mediano plazo.

### J3. Sin filtro de fecha predeterminado / orientación temporal
- El formulario no pre-filtra por día actual ni período reciente.
- Sin filtros activos, se carga todo el historial.
- Para "revisión diaria de movimientos" el operador debería ver hoy por defecto.
- **Severidad: media** — UX para operación diaria no está optimizada.

### J4. TempData sin roles de accesibilidad
- `TempData["Success"]` y `TempData["Error"]` son divs sin `role="status"` / `role="alert"`.
- Lectores de pantalla no anuncian estos mensajes automáticamente.
- Comparación: AlertaStock/Index usa `role="status"` y `role="alert"` correctamente.
- **Severidad: media** — deuda de accesibilidad.

### J5. Contraste bajo en headers de tabla y texto secundario
- `text-slate-400 uppercase` en `<th>`: contraste ~3.5:1 sobre `bg-slate-800/30`.
- `text-slate-500 font-mono` en código de producto: contraste ~2.8:1.
- WCAG AA requiere 4.5:1 para texto pequeño (xs/12px).
- **Severidad: media** — problema de baja visión.

### J6. Columna "Saldo post." sin descripción clara
- Header abreviado, puede confundir.
- No tiene `title` ni tooltip. Las columnas "Fuente costo" sí tienen `cursor-help` + `title`.
- **Severidad: baja.**

### J7. Densidad de columnas
- 11 columnas en `min-w-[960px]`: Fecha, Tipo, Producto, Cantidad, Costo unit., Costo total, Fuente costo, Referencia, Saldo post., Usuario, Acción.
- Para revisión diaria, las columnas de costo pueden ser secundarias respecto a Saldo.
- La densidad hace la tabla difícil de leer a 1280px de ancho.

---

## K. Hallazgos — Kardex

### K1. Tabla sin scroll affordance
- `min-w-[1100px]` — más ancha que Index (960px), crítica en mobile.
- Misma ausencia de `data-oc-scroll` y `horizontal-scroll-affordance.css`.
- **Severidad: media-alta** — la tabla es más ancha y tiene más columnas que Index.

### K2. Sin filtros de fecha
- El Kardex muestra **todo el historial del producto** sin poder filtrar por período.
- Sin paginación.
- Para un operador que quiere ver "¿qué pasó esta semana con este producto?", no hay forma de filtrar.
- **Severidad: media** — gap funcional claro para revisión diaria.

### K3. Volver apunta a Catálogo, no a Movimientos Index
- La flecha `arrow_back` dirige a `Catálogo/Index`, no a `MovimientoStock/Index`.
- El operador que llegó desde `MovimientoStock/Index` vía "Ver kardex" no puede volver al Index fácilmente.
- **Severidad: media** — afecta al flujo de revisión desde Index.

### K4. TempData sin roles de accesibilidad
- Mismo problema que Index: `TempData["Success"]` / `TempData["Error"]` sin `role="status"` / `role="alert"`.
- **Severidad: media.**

### K5. Contraste bajo en texto secundario
- `text-slate-500` en hora, código de producto, usuario — contraste bajo.
- `text-slate-400` en headers — mismo problema que Index.
- **Severidad: media.**

### K6. Texto de estado vacío de Motivo en cursiva baja
- Cuando Motivo está vacío: clase `text-sm italic text-slate-500`.
- Cursiva + contraste bajo — doblemente difícil de leer para baja visión.
- **Severidad: baja.**

---

## L. Hallazgos — Modal Movimientos en Catálogo

### L1. Labels de filtros sin atributo `for`
- Los `<label>` de los filtros del modal (Rango de Fechas, Tipo, Producto, Fuente de Costo) no tienen atributo `for`.
- Los inputs no tienen `id` que los labels puedan referenciar (o el `id` existe pero el label no lo referencia).
- Rompe la asociación label→input para lectores de pantalla.
- **Severidad: alta** — deuda de accesibilidad directa.

### L2. Cabeceras de tabla del modal sin `scope="col"`
- La tabla del modal (líneas 2293–2305) tiene `<th>` sin `scope="col"`.
- Contrasta con Index y Kardex que sí tienen `scope="col"` correctamente.
- Precedente de corrección: `MISA-CATALOGO-UX-1C` ya corrigió scope en tablas de Catálogo.
- **Severidad: alta** — deuda de accesibilidad.

### L3. Botones PDF y Excel sin handler funcional
- La "Actions Bar" del modal tiene dos botones: PDF y Excel.
- Ninguno tiene `id`, `data-*` ni listener en el JS del modal.
- No existe endpoint en el controller para export.
- Son botones sin acción — generan expectativa falsa en el operador.
- **Severidad: alta** — deuda funcional con impacto en UX.

### L4. Etiqueta "Total Movimientos (Hoy)" es misleading
- El stat card dice "Total Movimientos (Hoy)".
- El fetch inicial no filtra por fecha actual — trae todos los movimientos sin restricción de fecha.
- El texto "Hoy" es incorrecto salvo que se aplique filtro de fecha.
- **Severidad: media** — copy incorrecto que desinforma al operador.

### L5. `<h1>` dentro del modal
- El título del modal usa `<h1 id="modal-movimientos-title">`.
- Dentro de una página que ya tiene su propio `<h1>`, usar otro `<h1>` rompe la jerarquía de encabezados.
- Debería ser `<h2>`.
- **Severidad: media** — afecta lectores de pantalla.

### L6. Botón cerrar con copy confuso
- El botón `#btn-cerrar-movimientos` dice "Volver al Inventario".
- En semántica de modales, el botón de cierre debería decir "Cerrar".
- "Volver al Inventario" puede confundir porque el operador ya está en Inventario/Catálogo.
- **Severidad: baja** — copy confuso.

### L7. Tamaño del modal en mobile
- `max-w-[1280px]` con tabla `min-w-[1180px]` — el modal es muy ancho.
- En viewport < 1280px el modal usa el ancho completo con scroll horizontal de la tabla dentro del modal con scroll vertical propio.
- En 390px (mobile) la experiencia puede ser engorrosa: doble scroll.
- **Severidad: media** — experiencia mobile degradada.

### L8. Accesibilidad correctamente implementada (BIEN)
- `role="dialog"`, `aria-modal="true"`, `aria-labelledby="modal-movimientos-title"` — CORRECTO.
- Focus management: foco va a `#btn-cerrar-movimientos` al abrir, vuelve al trigger al cerrar — CORRECTO.
- Escape key cierra el modal — CORRECTO.
- Click en overlay cierra el modal — CORRECTO.
- Focus trap delegado a `window.CatalogoModule.trapFocus` — CORRECTO.
- Estos elementos fueron corregidos en MISA-CATALOGO-UX-1E/1F y deben preservarse.

---

## M. Hallazgos — Filtros

### M1. Index (server-side) — bien implementado
- 4 filtros: Producto (select), Tipo (select), FechaDesde (date), FechaHasta (date).
- Todos tienen `<label for="...">` con IDs correspondientes — CORRECTO.
- Botón "Buscar" explícito. Botón "Limpiar" con link GET — CORRECTO.
- Contador de resultados visible — CORRECTO.

### M2. Kardex — sin filtros
- No tiene ningún filtro de período ni de tipo.
- Todo el historial del producto se muestra de una vez sin paginación.
- Para trazabilidad completa está bien, pero para operación diaria es excesivo.

### M3. Modal Movimientos — filtros ricos pero con deudas
- 4 filtros servidor (fecha desde/hasta, tipo, producto) + 1 filtro client-side (fuente de costo).
- Los filtros de fecha no tienen valor por defecto — el fetch inicial trae todo.
- Labels sin `for` — DEUDA (detallado en L1).
- El filtro de fuente de costo es client-side (filtra sobre datos ya cargados) — funciona bien.
- El badge "N filtros activos" es un buen indicador visual.

---

## N. Hallazgos — Tablas

| Tabla | scope="col" | data-oc-scroll | Paginación | Observaciones |
|---|---|---|---|---|
| MovimientoStock/Index | ✅ correcto | ❌ ausente | ❌ sin paginación | 11 columnas, min-w-960px |
| MovimientoStock/Kardex | ✅ correcto | ❌ ausente | ❌ sin paginación | 10 columnas, min-w-1100px |
| Modal Movimientos | ❌ ausente | overflow-x-auto inline | ✅ client-side 25/50/100 | 11 columnas, min-w-1180px |
| AlertaStock/Index | ❌ ausente | ✅ correcto | ✅ server-side | scope ausente — deuda propia |
| AlertaStock/Criticos | ❌ ausente | overflow-x-auto inline | ❌ sin paginación | Fuera del alcance inmediato |

Index y Kardex tienen `scope="col"` correcto. El modal no — inconsistencia con el resto del módulo.

---

## O. Hallazgos — Acciones

### O1. "Ver kardex" en Index
- Link por fila con `title="Ver kardex de {nombre}"` — CORRECTO.
- Texto claro y descriptivo.

### O2. "Registrar ajuste" en Index y Kardex
- Botón en header de ambas vistas — consistente.
- En Kardex pasa `productoId` pre-seleccionado.

### O3. Botón "Movimientos" en fila de Catálogo
- Usa `data-movimientos-producto-id` y `data-movimientos-producto-nombre`.
- Abre el modal filtrado por producto — CORRECTO, revisado en MISA-CATALOGO.

### O4. Link "Ver kardex SKU" en Producto/Unidades
- Link con `asp-controller="MovimientoStock"`, `asp-action="Kardex"`, `asp-route-id="@Model.ProductoId"` — CORRECTO.

---

## P. Hallazgos — Mobile

### P1. Tablas sin affordance de scroll
- Index (`min-w-[960px]`) y Kardex (`min-w-[1100px]`) tienen `overflow-x-auto` pero sin `data-oc-scroll`.
- En mobile el usuario no sabe que hay columnas fuera del viewport.
- AlertaStock/Index ya tiene la solución correcta.
- **Acción recomendada:** aplicar `data-oc-scroll` en fases futuras.

### P2. Modal en mobile
- El modal en mobile ocupa el ancho completo.
- La tabla interior tiene scroll horizontal dentro del modal con scroll vertical propio — doble scroll confuso en 390px.
- **Severidad: media.**

### P3. Filtros del modal en mobile
- En `lg:grid-cols-5` apila correctamente en mobile.
- Los dos inputs de fecha en `flex gap-2` pueden estrecharse en pantallas muy pequeñas.
- La sección de filtros ocupa mucha pantalla antes de llegar a la tabla.

### P4. Formulario Create en mobile
- `max-w-2xl` — se adapta bien en mobile, campos en columna única.

---

## Q. Hallazgos — Accesibilidad / Baja visión

### Q1. Labels sin for en modal (crítico)
- Detallado en L1 — los filtros del modal no tienen `label[for]`.

### Q2. scope="col" ausente en modal (crítico)
- Detallado en L2.

### Q3. TempData sin role en Index y Kardex (medio)
- Detallado en J4 y K4.

### Q4. Contraste de headers de tabla
- `text-slate-400 uppercase tracking-wide` en `<th>` sobre `bg-slate-800/30`:
  - `#94a3b8` sobre `#1e293b` — ratio ~3.4:1.
  - WCAG AA para texto pequeño no bold requiere 4.5:1.
  - Los headers están en `text-xs` (12px) — **no cumple WCAG AA**.

### Q5. Contraste de texto secundario en celdas
- `text-slate-500` en hora, código de producto, usuario:
  - `#64748b` sobre `#0f172a` — ratio ~2.8:1.
  - **No cumple WCAG AA para ningún tamaño de texto.**

### Q6. Badges de tipo (Entrada / Salida / Ajuste)
- `text-emerald-400` / `text-rose-400` / `text-amber-400` sobre fondos con transparencia baja.
- Ninguno cumple WCAG AA para texto pequeño.
- Sin embargo hay redundancia semántica con el signo +/- y el texto — el significado no depende solo del color.

### Q7. Focus visible
- Los botones y links usan `:focus` implícito del browser o `focus:ring-*`.
- No se detecta supresión de `outline` en los archivos CSS de Movimientos.
- Focus trap en modal implementado correctamente.

### Q8. Color como único significado — OK
- Columna "Cantidad" usa color para indicar signo, con redundancia textual (+/-).
- No hay dependencia exclusiva del color para transmitir información.

---

## R. Hallazgos — Seguridad frontend

### R1. Función de escape en movimientos-inventario-modal.js — SEGURO
- El archivo implementa una función `esc` que asigna el valor mediante `textContent` y luego lee el resultado como markup — técnica correcta de escape que convierte caracteres especiales en entidades HTML antes de interpolarlos en strings de markup.
- Todos los campos de texto provenientes del servidor se pasan por esta función antes de interpolarse.
- Los campos numéricos (cantidades, costos, saldos, IDs) se usan directamente sin interpolar como strings HTML — no hay riesgo ya que son primitivos numéricos.
- Los hrefs de "Ver kardex" usan el ID de producto (integer) — no hay riesgo de inyección en la URL.
- **Conclusión: no se detectan vulnerabilidades XSS activas en el modal.**

### R2. Limpieza de la tabla al re-renderizar
- El cuerpo de la tabla se vacía asignando un string vacío antes de poblar con nuevas filas.
- Alternativa más moderna: usar `replaceChildren()`. No es un riesgo funcional, solo una mejora de calidad de código.

### R3. Inyección de datos de server en script de Catálogo
- El Catálogo/Index inyecta datos de categorías y marcas en un objeto global JS usando `System.Text.Json.JsonSerializer.Serialize`.
- El serializador de .NET escapa caracteres especiales — no hay riesgo XSS.
- No es específico de Movimientos.

---

## S. Hallazgos — PDF/Excel

### S1. Botones PDF y Excel en modal — deuda funcional
- El modal Movimientos tiene botones "PDF" y "Excel" con íconos y texto visibles al usuario.
- Ninguno tiene `id`, `data-*` ni listener en el JS del modal.
- El controller **no tiene endpoints** de export.
- El servicio `IMovimientoStockService` **no tiene métodos** de export.
- Los botones son completamente no funcionales — generan frustración silenciosa.
- **Severidad: alta** — deuda funcional con impacto UX negativo claro.

### S2. No hay export en Index ni Kardex
- `Index_tw.cshtml` y `Kardex_tw.cshtml` no tienen botones de export.
- Coherente con la ausencia de backend de export.
- La deuda de export vive únicamente en el modal.

### S3. Opciones para resolver
- **Opción A:** Implementar export real (PDF/Excel) — fase funcional `MISA-MOVIMIENTOS-FUNC-1`.
- **Opción B (recomendada):** Deshabilitar botones con `disabled` + tooltip explicativo hasta que el export esté implementado. Evita engañar al operador sin eliminar el placeholder.

---

## T. Hallazgos — Navegación cruzada

### T1. Catálogo → Movimientos — CORRECTO
- Botón global "Movimientos" en header de Catálogo abre modal global.
- Botón por fila "Movimientos" abre modal filtrado por producto.
- Cierre del modal devuelve el foco al trigger.

### T2. Modal → Kardex — CORRECTO
- Link "Ver kardex" por fila en el modal navega a `/MovimientoStock/Kardex/{id}`.

### T3. Kardex → Catálogo — PARCIAL
- Flecha `arrow_back` lleva a Catálogo.
- **No hay link de vuelta a MovimientoStock/Index.**
- Si el operador llegó desde Index el camino de vuelta no es directo.

### T4. Index → Kardex — CORRECTO
- Link "Ver kardex" por fila.

### T5. Kardex → Crear ajuste — CORRECTO
- Botón "Registrar ajuste" en header de Kardex con `productoId` pre-cargado.

### T6. AlertaStock → Kardex/Movimientos — AUSENTE (DEUDA)
- En `AlertaStock/Index_tw.cshtml`: el operador ve qué productos tienen alertas pero no tiene link al Kardex ni a Movimientos.
- En `AlertaStock/Criticos_tw.cshtml`: tampoco hay link a Kardex.
- El flujo natural "este producto está agotado → ¿qué movimientos tuvo recientemente?" no es posible sin navegar manualmente.
- **Severidad: media** — gap de navegación operativa.

### T7. Producto/Unidades → Kardex — CORRECTO
- Botón "Ver kardex SKU" en header de Unidades funciona correctamente.

### T8. Producto/UnidadesGlobal → Kardex/Movimientos — AUSENTE
- No hay link a Kardex ni a Movimientos desde el reporte global de unidades.
- **Severidad: baja** — es un reporte de unidades físicas, puede no ser necesario.

---

## U. Qué NO conviene cambiar

1. La lógica de escape y sanitización en el JS del modal — protege correctamente.
2. El focus management del modal — implementado correctamente en MISA-CATALOGO-UX-1E/1F.
3. El mecanismo `data-movimientos-producto-id` — delegación de eventos limpia.
4. Los `scope="col"` en Index y Kardex — correctos, no tocar.
5. El banner informativo SKU en Index, Kardex y Create — útil para el operador.
6. Las hints contextuales por tipo en Create (Entrada/Salida/Ajuste) — buen UX operativo.
7. El sistema de badges de tipo (emerald/rose/amber) — coherente con el resto del ERP.
8. Los endpoints del controller — no cambiar rutas ni payloads en fases UX.
9. La paginación client-side del modal — funcional y bien implementada.
10. El botón "Registrar ajuste" en header de Index y Kardex — posición consistente.
11. Los atributos de accesibilidad del modal (`role="dialog"`, `aria-modal`, `aria-labelledby`) — correctos desde MISA-CATALOGO.

---

## V. Roadmap MISA-MOVIMIENTOS

### MISA-MOVIMIENTOS-UX-1A — Accesibilidad semántica
**Alcance:**
- Labels con `for` en filtros del modal de Movimientos.
- `scope="col"` en cabeceras de tabla del modal.
- `role="status"` / `role="alert"` en TempData de Index y Kardex.
- `<h2>` en lugar de `<h1>` como label del modal.
- **Archivos:** `Views/Catalogo/Index_tw.cshtml` (sección modal), `Views/MovimientoStock/Index_tw.cshtml`, `Views/MovimientoStock/Kardex_tw.cshtml`.
- **No tocar:** controller, servicios, endpoints, CSS, JS de lógica.
- **Validar:** build, tests de Layout y UiContract.

### MISA-MOVIMIENTOS-UX-1B — UX visual / jerarquía y copy
**Alcance:**
- Contraste de headers de tabla (`text-slate-400` → `text-slate-300`).
- Copy "Total Movimientos (Hoy)" → "Total movimientos".
- Copy "Volver al Inventario" → "Cerrar" en botón del modal.
- Columna "Saldo post." → tooltip `title` explicativo.
- Link de vuelta a Index desde Kardex (segundo link además del Catálogo).
- Link "Ver historial de movimientos" en AlertaStock/Index → Movimientos Index.
- **Archivos:** `Views/Catalogo/Index_tw.cshtml` (modal), `Views/MovimientoStock/Kardex_tw.cshtml`, `Views/AlertaStock/Index_tw.cshtml`.
- **No tocar:** JS, CSS, backend.

### MISA-MOVIMIENTOS-UX-1C — Mobile / scroll affordance
**Alcance:**
- Agregar `data-oc-scroll` a tabla de Index.
- Agregar `data-oc-scroll` a tabla de Kardex.
- Cargar `horizontal-scroll-affordance.css` y `.js` en Index y Kardex.
- **Archivos:** `Views/MovimientoStock/Index_tw.cshtml`, `Views/MovimientoStock/Kardex_tw.cshtml`.
- **No tocar:** JS de lógica, controller.

### MISA-MOVIMIENTOS-UX-1D — Botones PDF/Excel (fix visual inmediato)
**Alcance:**
- Deshabilitar botones PDF y Excel en el modal con `disabled` + `title` explicativo.
- O quitar los botones hasta que el export esté implementado.
- **Archivos:** `Views/Catalogo/Index_tw.cshtml` (sección modal, líneas ~2280–2286).
- **No tocar:** JS, controller.
- **Nota:** esta es la solución UX mínima. La implementación real va en `MISA-MOVIMIENTOS-FUNC-1`.

### MISA-MOVIMIENTOS-UX-1E — Filtros y orientación temporal
**Alcance:**
- Pre-filtrar Index por fecha actual o pasar parámetro defecto.
- Decisión de producto: default "hoy", "últimos 7 días" o "sin restricción".
- Agregar filtros de período en Kardex.
- **Archivos:** `Views/MovimientoStock/Index_tw.cshtml`, `Views/MovimientoStock/Kardex_tw.cshtml`, posiblemente `Controllers/MovimientoStockController.cs`.
- **Coordinación:** requiere decisión de producto antes de implementar.

### MISA-MOVIMIENTOS-JS-SECURITY-1 — Revisión de patrones de manipulación DOM
**Alcance:**
- Evaluar migración de `tbody.innerHTML = ''` a `replaceChildren()`.
- Verificar y documentar todos los puntos de renderizado dinámico en el modal.
- Documentar que la seguridad actual es adecuada o aplicar mejoras preventivas.
- **Archivos:** `wwwroot/js/movimientos-inventario-modal.js`.
- **Nota:** no se detectaron vulnerabilidades activas. Esta fase es preventiva/calidad de código.

### MISA-MOVIMIENTOS-FUNC-1 — Export PDF/Excel (condicional)
**Alcance:**
- Implementar endpoint real de export en controller.
- Implementar método en service.
- Conectar botones del modal con handler JS.
- **Condición:** solo si el usuario confirma que es necesario como feature.
- **Archivos:** `Controllers/MovimientoStockController.cs`, `Services/MovimientoStockService.cs`, `Services/Interfaces/IMovimientoStockService.cs`, `wwwroot/js/movimientos-inventario-modal.js`.

### MISA-MOVIMIENTOS-QA — QA visual/manual final
**Alcance:**
- Playwright o inspección manual de todas las pantallas del módulo.
- Verificar flujos Index → Kardex → Create → Index.
- Verificar flujo modal Catálogo → filtros → Kardex.
- Verificar mobile en 390px.
- Verificar flujo AlertaStock → Movimientos (si se agrega en UX-1B).
- Ejecutar suite de tests de Movimientos.

---

## X. Próximo prompt recomendado

```
PROMPT — MISA-MOVIMIENTOS-UX-1A — Accesibilidad semántica de Movimientos

Actuá como Misa. Base: main 476bb20 post-merge. Rama: misa/movimientos-ux-1a-accesibilidad.

Implementar los siguientes cambios de accesibilidad:

1. Views/Catalogo/Index_tw.cshtml (sección modal Movimientos, líneas ~2225–2275):
   - Agregar for="mov-fecha-desde" al label de "Rango de Fechas" (el input id="mov-fecha-desde" ya existe).
   - Agregar for="mov-tipo" al label de "Tipo de Movimiento".
   - Agregar for="mov-producto" al label de "Producto (SKU/Nombre)".
   - Agregar for="mov-fuente-costo" al label de "Fuente de Costo".
   - Agregar scope="col" a todos los <th> de la tabla del modal (líneas ~2293–2305).
   - Cambiar <h1 id="modal-movimientos-title"> a <h2 id="modal-movimientos-title">.

2. Views/MovimientoStock/Index_tw.cshtml:
   - Agregar role="status" al div de TempData["Success"].
   - Agregar role="alert" al div de TempData["Error"].

3. Views/MovimientoStock/Kardex_tw.cshtml:
   - Agregar role="status" al div de TempData["Success"].
   - Agregar role="alert" al div de TempData["Error"].

No tocar: controller, services, CSS, JS de lógica, endpoints, contratos de datos.
Validar: dotnet build, dotnet test --filter "Layout|UiContract", git diff --check.
Seguir convenciones de CLAUDE.md.
```

---

## Validaciones de esta fase (audit-only)

- `git diff --check` — se ejecutará al commitear.
- `git status --short` — solo el nuevo documento de auditoría debe aparecer como nuevo.
- No se ejecutó build (fase doc-only).
- No se ejecutaron tests (fase doc-only).
- No se ejecutó Playwright (fase doc-only).
- No se tocó código productivo.

---

## Procesos

- No se iniciaron procesos adicionales durante esta auditoría.
- Procesos preexistentes (VS Code, C# DevKit, MCP) no fueron tocados.

---

## Archivos locales sensibles (preexistentes, no comprometidos)

| Archivo | Estado | Acción |
|---|---|---|
| `.claude/settings.local.json` | Modificado local preexistente | No commitear |
| `AGENTS.md` | Modificado local preexistente | No commitear |
| `CLAUDE.md` | Modificado local preexistente | No commitear |
| `docs/misa-catalogo-ux-1g-aria-live-modales.md` | Modificado local preexistente | No commitear |
| `skills-lock.json` | Eliminado local preexistente | No commitear |
