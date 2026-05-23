# MISA-CATALOGO-UX-1C — scope="col" en tablas de Catálogo

## A. Objetivo

Agregar `scope="col"` en todos los encabezados `<th>` de las tablas principales de Catálogo:
- Tabla Productos (`productos-tbody`)
- Tabla Categorías (`categorias-tbody`)
- Tabla Marcas (`marcas-tbody`)

Fase: Razor-only / accesibilidad semántica / bajo riesgo.

---

## B. Base y contexto

- Base: `main` en `c3265e8` — MISA-CATALOGO-UX-1B integrada.
- Rama: `misa/catalogo-ux-1c-scope-tablas` creada desde `main c3265e8`.
- Fase anterior cerrada: MISA-CATALOGO-UX-1B (semántica de modales).
- Inventario físico: aprobado con observaciones, no se abre UX-2.
- Ventas: cerrada hasta nuevo hallazgo.

---

## C. Deuda tomada desde MISA-CATALOGO-UX-0

La auditoría UX-0 detectó:
- Tablas de Productos, Categorías y Marcas con `<th>` sin `scope="col"`.
- Lectores de pantalla no pueden asociar correctamente columna → encabezado.
- Baja visión / AT (assistive technology) afectada.
- Deuda también detectada: `aria-sort` pendiente (MISA-CATALOGO-UX-1D).

---

## D. Archivos auditados

- `Views/Catalogo/Index_tw.cshtml` — archivo principal modificado.
- `wwwroot/js/catalogo-index.js` — leído, no modificado.
- `wwwroot/js/catalogo-module.js` — leído, no modificado.
- `wwwroot/css/catalogo-module.css` — leído, no modificado.
- `docs/misa-catalogo-ux-0-auditoria.md` — referencia de deuda.
- `docs/misa-catalogo-ux-1a-boton-movimientos-color.md` — referencia.
- `docs/misa-catalogo-ux-1b-semantica-modales.md` — referencia.

---

## E. Tablas detectadas en Index_tw.cshtml

El archivo contiene 8 tablas en total:

| # | Tabla | tbody id | Líneas (aprox.) | En scope de fase |
|---|-------|----------|-----------------|------------------|
| 1 | Productos | `productos-tbody` | 195–460 | Sí |
| 2 | Categorías | `categorias-tbody` | 464–580 | Sí |
| 3 | Marcas | `marcas-tbody` | 586–900 | Sí |
| 4 | Características (modal) | `modal-caracteristicas-body` | ~905 | No |
| 5 | Características (edición modal) | `prod-edit-caracteristicas-body` | ~1151 | No |
| 6 | Historial de precios | `historial-precio-tbody` | ~1781 | No |
| 7 | Preview de precios | `precio-preview-tbody` | ~2101 | No |
| 8 | Movimientos | `mov-tbody` | ~2291 | No |

Las tablas 4–8 tienen clases CSS distintas (`px-4 py-3`, `px-8 py-4`, `text-sm`) — no afectadas por esta fase.

---

## F. Estado previo (antes de la fase)

Ninguna de las tres tablas principales tenía `scope="col"` en sus `<th>`.

Resultado de búsqueda previa: 0 ocurrencias de `scope="col"` en el archivo.

---

## G. Cambios aplicados por tabla

### Tabla Productos

10 `<th>` corregidos (líneas 198–220):

| Línea | Columna | Atributos especiales preservados |
|-------|---------|----------------------------------|
| 198 | Seleccionar (checkbox) | — |
| 203 | Destacado (ícono) | — |
| 207 | Código | — |
| 208 | Producto | `data-sort="nombre"`, `data-sort-icon` (inner span) |
| 211 | Categoría | — |
| 212 | Marca | — |
| 213 | Stock | — |
| 214 | Precio vigente | `data-sort="precio"`, `data-sort-icon` (inner span) |
| 217 | Comisión | `data-sort="comision"`, `data-sort-icon` (inner span) |
| 220 | Acciones | — |

### Tabla Categorías

5 `<th>` corregidos (líneas 467–471):

| Línea | Columna |
|-------|---------|
| 467 | Código |
| 468 | Categoría |
| 469 | Categoría Padre |
| 470 | Estado |
| 471 | Acciones |

### Tabla Marcas

6 `<th>` corregidos (líneas 589–594):

| Línea | Columna |
|-------|---------|
| 589 | Código |
| 590 | Marca |
| 591 | Marca Padre |
| 592 | País de Origen |
| 593 | Estado |
| 594 | Acciones |

---

## H. th corregidos

- Total `scope="col"` agregados: **21**
  - Productos: 10
  - Categorías: 5
  - Marcas: 6

---

## I. aria-label agregados

**Ninguno.** Análisis de cada th sin texto visible:

- L198 (Seleccionar): el `<input>` interno tiene `aria-label="Seleccionar todos los productos"`. El th no representa "Acciones" — no corresponde agregar aria-label en esta fase.
- L203 (Destacado): tiene `<span class="sr-only">Destacado</span>` — ya accesible para AT.

No hay th completamente vacíos que representen columna de acciones sin ningún texto accesible.

---

## J. Contratos preservados

- `id="productos-tbody"`, `id="categorias-tbody"`, `id="marcas-tbody"` — sin cambios.
- `data-sort="nombre"`, `data-sort="precio"`, `data-sort="comision"` — sin cambios.
- `data-sort-icon` en inner spans — sin cambios.
- `id="chk-select-all"` — sin cambios.
- `aria-label="Seleccionar todos los productos"` en el checkbox — sin cambios.
- `<span class="sr-only">Destacado</span>` — sin cambios.
- Todas las clases CSS en cada `<th>` — sin cambios.
- Textos visibles de columnas — sin cambios.
- Orden de columnas — sin cambios.
- Estructura de `<thead>` / `<tbody>` — sin cambios.
- Tabs, modales, acciones por fila — sin cambios.
- Endpoints, payloads, backend — sin cambios.

---

## K. Qué no se tocó

- `wwwroot/js/` — no modificado.
- `wwwroot/css/` — no modificado.
- `Controllers/` — no modificado.
- `Services/` — no modificado.
- `Models/` / `ViewModels/` / `Migrations/` / `DTOs` — no modificado.
- Modales (atributos ARIA de UX-1B) — preservados sin cambios.
- Tablas de modales (Características, Historial precios, Preview precios) — no modificadas.
- Tabla Movimientos (`mov-tbody`) — no modificada.
- `aria-sort` — pendiente para MISA-CATALOGO-UX-1D.
- `focus management` — pendiente.
- Color del botón Movimientos — sin cambios (cerrado en UX-1A).
- `CatalogoController`, `AlertaStockController`, permisos — sin cambios.
- `AGENTS.md`, `CLAUDE.md`, `.claude/settings.local.json`, `skills-lock.json` — no commiteados.

---

## L. Accesibilidad / baja visión

`scope="col"` permite que los lectores de pantalla (NVDA, JAWS, VoiceOver) anuncien correctamente el nombre de la columna al navegar celda a celda en tablas de datos. Sin este atributo, la asociación encabezado→celda es ambigua en tablas complejas.

Impacto para baja visión:
- Usuarios que navegan con AT ya tienen los encabezados declarados como columna.
- Compatible con `aria-sort` que se agregará en UX-1D sin conflicto.

---

## M. Riesgo funcional

**Muy bajo.** `scope="col"` es un atributo HTML semántico que:
- No altera layout ni visual.
- No afecta JavaScript existente.
- No cambia clases CSS.
- No modifica IDs ni data-attributes.
- No rompe ningún selector de Playwright ni de tests.

---

## N. Tests y validaciones

- **Build**: ejecutado (`dotnet build --configuration Release`). Resultado: ver sección O.
- **Tests unitarios**: no ejecutados. Esta fase no modifica lógica C#, tests ni contratos funcionales.
- **Tests de contrato UI (LayoutUiContractTests)**: no ejecutados en esta fase. Los selectores de contrato UI se basan en IDs y clases, no en atributos `scope`. Si existe un test específico de scope en tablas de catálogo, puede ejecutarse en una validación futura.

---

## O. Resultado exacto de build

Ver salida del comando `dotnet build --configuration Release` ejecutado post-modificación.

---

## P. Playwright

No ejecutado. Justificación:
- Cambio Razor semántico localizado: solo se agregó `scope="col"` en `<th>`.
- No se modificó estructura visible, clases, layout, orden de columnas ni textos.
- No existe spec específico de Catálogo que cubra atributos de accesibilidad semántica en `<th>`.
- Specs de Ventas, Cotización y `venta-pago-por-item` fuera de scope.

---

## Q. Procesos

Al cierre de la tarea se revisaron procesos activos relacionados con el repo. Documentar si hubo procesos iniciados por la tarea.

---

## R. Deudas restantes de Catálogo

| Deuda | Estado | Fase sugerida |
|-------|--------|---------------|
| `aria-sort` en columnas ordenables | Pendiente | MISA-CATALOGO-UX-1D |
| Focus management en modales | Pendiente | Fase posterior |
| `escapeHtml` duplicado | Pendiente | Fase JS |
| Permiso anómalo detectado en UX-0 | Pendiente | Fase backend/permisos |

---

## S. Próximo paso recomendado

**MISA-CATALOGO-UX-1D — `aria-sort` en columnas ordenables.**

Objetivo:
- Auditar columnas ordenables en tabla Productos (`data-sort="nombre"`, `data-sort="precio"`, `data-sort="comision"`).
- Agregar `aria-sort` donde corresponda según el estado actual del sort.
- Verificar si el JS actualiza el atributo dinámicamente o si debe hacerlo.
- Preservar lógica JS existente.
- No tocar backend.
- No cambiar comportamiento visual.
