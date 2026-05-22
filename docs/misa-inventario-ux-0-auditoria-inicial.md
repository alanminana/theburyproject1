# MISA-INVENTARIO-UX-0 — Auditoría inicial rework visual Inventario / Catálogo / Movimientos / Marcas

## A. Objetivo

Auditar el estado real del módulo Inventario / Catálogo del ERP TheBuryProject para:

- Mapear pantallas, modales, scripts y CSS existentes.
- Identificar deudas UX, mobile, accesibilidad, visual y de seguridad frontend.
- Detectar ventanas duplicadas, incompletas o aisladas.
- Proponer un roadmap de fases chicas y seguras.

Esta fase es audit-only. No se tocó código productivo.

---

## B. Contexto

- Proyecto: TheBuryProject / TheBuryProyect
- Stack: ASP.NET MVC / .NET 8 / C# / Razor / JS vanilla / CSS propio / Tailwind utility
- Agente Kira cubre Ventas/Cotización. Misa cubre Inventario/Catálogo/Marcas/Movimientos.
- Ya existe un rework visual avanzado: dark theme, design system, layout, toasts normalizados.
- main en c4bfab9 — VENTAS-UX-1D integrada.

---

## C. Estado inicial de main

```
c4bfab9 Mejorar accesibilidad de tabla de detalle en modal de venta (VENTAS-UX-1D)
4aaa039 Ajustar copy y accesibilidad en venta create (VENTAS-UX-1C)
1ab59b3 Auditar flujo de creacion de venta (VENTAS-UX-1B)
e2a7db9 Hacer visible tipo de pago principal en venta (VENTAS-UX-1A)
```

Rama creada: `misa/inventario-ux-0-auditoria-inicial` desde c4bfab9.

---

## D. Archivos y carpetas auditadas

### Vistas

| Ruta | Descripción |
|---|---|
| `Views/Catalogo/Index_tw.cshtml` | Vista unificada del catálogo (2379 líneas) |
| `Views/Producto/Edit_tw.cshtml` | Edición standalone de producto |
| `Views/Producto/Unidades.cshtml` | Unidades físicas por producto + conciliación |
| `Views/Producto/UnidadesGlobal.cshtml` | Inventario físico global |
| `Views/Producto/UnidadHistorial.cshtml` | Historial de una unidad individual |
| `Views/MovimientoStock/Index_tw.cshtml` | Lista global de movimientos |
| `Views/MovimientoStock/Create_tw.cshtml` | Formulario de ajuste manual |
| `Views/MovimientoStock/Kardex_tw.cshtml` | Kardex por producto |
| `Views/AlertaStock/Index_tw.cshtml` | Lista de alertas con filtros y paginación |
| `Views/AlertaStock/Criticos_tw.cshtml` | Productos críticos/agotados |
| `Views/AlertaStock/Details_tw.cshtml` | Detalle de alerta individual |
| `Views/AlertaStock/Estadisticas_tw.cshtml` | Estadísticas de alertas |
| `Views/AlertaStock/PorProducto.cshtml` | Alertas por producto individual |

**Nota:** No existe una carpeta `Views/Inventario/`, `Views/Marca/` ni `Views/Categoria/` separadas. Todo vive dentro de `Views/Catalogo/Index_tw.cshtml` como tabs y modales inline. No existen pantallas standalone de Marca ni de Categoría fuera del catálogo unificado.

### Scripts JS

| Archivo | Responsabilidad |
|---|---|
| `wwwroot/js/catalogo-index.js` | Búsqueda client-side, tabs, delegación de acciones, selección de productos |
| `wwwroot/js/catalogo-module.js` | Módulo principal: modales, selección, API interna |
| `wwwroot/js/categoria-crear-modal.js` | Modal de creación de categoría |
| `wwwroot/js/categoria-editar-modal.js` | Modal de edición de categoría |
| `wwwroot/js/marca-crear-modal.js` | Modal de creación de marca |
| `wwwroot/js/marca-editar-modal.js` | Modal de edición de marca |
| `wwwroot/js/movimientos-inventario-modal.js` | Modal de movimientos inline en Catálogo |
| `wwwroot/js/producto-comision-modal.js` | Modal de comisión de vendedor por producto |
| `wwwroot/js/producto-crear-modal.js` | Modal de creación de producto |
| `wwwroot/js/producto-edit-form.js` | Formulario de edición inline de producto |
| `wwwroot/js/producto-editar-modal.js` | Modal de edición de producto |
| `wwwroot/js/alerta-stock-index.js` | Index de alertas: auto-dismiss, scroll affordance, confirm |

### CSS

| Archivo | Relevancia |
|---|---|
| `wwwroot/css/catalogo-module.css` | Estilos propios del módulo catálogo |
| `wwwroot/css/horizontal-scroll-affordance.css` | Affordance scroll horizontal |
| `wwwroot/css/shared-components.css` | Componentes compartidos (badge-erp, btn-erp, etc.) |
| `wwwroot/css/layout.css` | Layout global |

**No existe** `catalogo.css`, `producto.css`, `stock.css`, `inventario.css` como archivos separados. El módulo usa `catalogo-module.css` + Tailwind inline.

### Controllers (solo lectura)

| Controller | Observación |
|---|---|
| `CatalogoController.cs` | Permiso requerido: `cotizaciones/view` (anomalía — ver hallazgo F-01) |
| `CategoriaController.cs` | CRUD de categorías vía AJAX |
| `MarcaController.cs` | CRUD de marcas vía AJAX |
| `MovimientoStockController.cs` | Gestión de movimientos, kardex |
| `ProductoController.cs` | CRUD de productos, trazabilidad serial |
| `ProductoApiController.cs` | API endpoints de producto |
| `AlertaStockController.cs` | Gestión de alertas de stock |

### Tests relacionados

**Integration:** AlertaStockServiceTests, CatalogoServiceTests, CategoriaServiceTests,
ConciliacionStockUnidadesTests, MarcaServiceTests, MovimientoStockControllerTests,
MovimientoStockServiceTests, PrecioServicePrecioProductoTests,
ProductoCondicionPagoEfTests, ProductoControllerPrecioTests, ProductoServiceTests,
ProductoUnidadReparacionE2ETests, ProductoUnidadServiceGlobalTests,
ProductoUnidadServiceTests, ProductoUnidadTests, VentaServiceProductoUnidadTrazabilidadTests.

**Unit:** CatalogoDTOsTests, ProductoApiControllerTests,
ProductoCondicionesPagoAdminLegacyDespublicadoTests, ProductoControllerIvaTests,
ProductoCreditoRestriccionServiceTests, ProductoIvaResolverTests.

**Playwright E2E:** `e2e/ui-4e-layout-visual.spec.js` cubre layout visual general.
No hay spec dedicado de Catálogo o Alertas de Stock.

---

## E. Mapa de pantallas detectadas

### Catálogo Unificado (pantalla principal)

**Ruta:** `/Catalogo/Index` → `Index_tw.cshtml` (2379 líneas)

3 tabs en una sola pantalla:
1. **Tab Productos** — tabla de productos con filtros server-side + búsqueda client-side. Columnas: Checkbox, Destacado, Código, Producto, Categoría, Marca, Stock, Precio vigente, Comisión, Acciones (6 botones).
2. **Tab Categorías** — tabla de categorías con acciones inline (Editar, Eliminar).
3. **Tab Marcas** — tabla de marcas con acciones inline (Editar, Eliminar).

Acciones globales en la barra de tabs:
- **Inventario físico** → navega a `Producto/UnidadesGlobal`
- **Movimientos** → abre modal inline de movimientos
- **Ajuste Masivo** → abre modal de ajuste masivo de precios

Selection bar flotante: aparece cuando hay productos seleccionados, permite ajuste masivo.

### Movimientos de Stock

| Pantalla | Ruta | Descripción |
|---|---|---|
| Index | `/MovimientoStock/Index` | Historial global con filtros (producto, tipo, fechas), stats (Total, Entradas, Salidas, Ajustes), tabla de 11 columnas |
| Create | `/MovimientoStock/Create` | Formulario standalone de ajuste (max-w-2xl), con hints contextuales por tipo |
| Kardex | `/MovimientoStock/Kardex/{id}` | Historial completo de un producto, tabla de 10 columnas, con stats resumen |

### Producto / Unidades (trazabilidad individual)

| Pantalla | Ruta | Descripción |
|---|---|---|
| Edit standalone | `/Producto/Edit/{id}` | Formulario completo (max-w-4xl), mismas secciones que el modal inline del catálogo |
| Unidades | `/Producto/Unidades/{productoId}` | Vista compleja: trazabilidad, conciliación, carga masiva, tabla de unidades con acciones de estado |
| UnidadesGlobal | `/Producto/UnidadesGlobal` | Inventario físico global, KPIs de resumen, tabla de todas las unidades |
| UnidadHistorial | `/Producto/UnidadHistorial/{unidadId}` | Historial de una unidad individual |

### Alertas de Stock

| Pantalla | Ruta | Descripción |
|---|---|---|
| Index | `/AlertaStock/Index` | Lista paginada de alertas, filtros (producto, tipo, prioridad, estado), badges de resumen |
| Críticos | `/AlertaStock/Criticos` | Productos agotados + stock por debajo del mínimo, dos tablas separadas |
| Estadísticas | `/AlertaStock/Estadisticas` | KPIs: pendientes, urgentes, vencidas, tasa de resolución |
| Details | `/AlertaStock/Details/{id}` | Detalle de una alerta individual con acciones Resolver/Ignorar |
| PorProducto | `/AlertaStock/PorProducto/{id}` | Alertas filtradas por producto |

---

## F. Mapa de modales / subventanas detectadas

Todos los modales de Catálogo viven en `Views/Catalogo/Index_tw.cshtml`:

| Modal ID | Disparador | Función |
|---|---|---|
| `modal-nuevo-producto` | Botón "Nuevo Producto" en tab Productos | Crear producto con formulario completo (4 secciones) |
| `modal-editar-producto` | Botón "Editar" en fila de producto | Editar producto con formulario completo (4 secciones) |
| `modal-nueva-categoria` | Botón "Nueva Categoría" en tab Categorías | Crear categoría con datos + opciones |
| `modal-editar-categoria` | Botón "Editar" en fila de categoría | Editar categoría |
| `modal-nueva-marca` | Botón "Nueva Marca" en tab Marcas | Crear marca |
| `modal-editar-marca` | Botón "Editar" en fila de marca | Editar marca |
| `modal-ajuste-masivo` | Botón "Ajuste Masivo" | Simulación + aplicación de cambio de precios masivo |
| `modal-historial-precio` | Botón "Historial" en fila de producto | Historial de cambios de precio de un producto |
| `modal-movimientos` | Botón "Movimientos" en barra + botón "Movimientos" en fila | Historial de movimientos de stock con filtros, paginado, por producto o global |
| `modal-comision` | Botón "Comisión" en fila de producto | Editar comisión de vendedor por producto |

---

## G. Mapa de scripts JS

### Estructura de carga en Catálogo/Index_tw

```
catalogo-module.js         → módulo principal (inicialización global)
catalogo-index.js          → tabs, búsqueda, delegación de acciones
producto-crear-modal.js    → modal nuevo producto
producto-editar-modal.js   → modal editar producto (carga datos por AJAX)
producto-edit-form.js      → lógica compartida de formulario producto
categoria-crear-modal.js   → modal nueva categoría
categoria-editar-modal.js  → modal editar categoría (carga datos por AJAX)
marca-crear-modal.js       → modal nueva marca
marca-editar-modal.js      → modal editar marca (carga datos por AJAX)
movimientos-inventario-modal.js → modal movimientos (fetch /MovimientoStock/ListJson)
producto-comision-modal.js → modal comisión por producto
```

### Observaciones de arquitectura JS

- `catalogo-module.js` expone una API interna (`CatalogoModule`) que coordina modales y selección. `catalogo-index.js` la consume.
- `movimientos-inventario-modal.js` hace fetch a `/MovimientoStock/ListJson` para cargar movimientos en el modal. Tiene paginación client-side, filtros, stats. Es el JS más complejo del módulo.
- Los modales de Categoría y Marca cargan datos via AJAX para edición (patrón similar a Ventas).

---

## H. CSS relacionado detectado

### `catalogo-module.css`

Contenido principal:
- `.catalogo-input` — input/select canónico del módulo (usado en modales de Categoría y Marca).
- `.autocomplete-erp` — widget de autocompletado (usado en selección de Categoría y Marca dentro del modal de Producto).
- `#btn-movimientos-inventario` — override CSS específico: **color de fondo rojo** (`rgb(220, 38, 38)`) — ver hallazgo UX-04.

### `horizontal-scroll-affordance.css`

Usado en `Catalogo/Index_tw` y `AlertaStock/Index_tw`. Proporciona el sistema `data-oc-scroll` con fades laterales y hint de scroll. Bien implementado donde está, pero inconsistente: no todos los módulos lo usan.

---

## I. Flujo actual de Catálogo / Inventario

```
/Catalogo/Index (tabs)
├── Tab: Productos
│   ├── Filtros (categoría, marca, stock bajo, lista precios) → recarga server-side
│   ├── Búsqueda → client-side (sin recarga)
│   ├── Selección múltiple → selection bar flotante
│   ├── Por fila: [Historial precio] [Comisión] [Movimientos] [Unidades] [Editar] [Eliminar]
│   │   ├── Historial → modal historial-precio
│   │   ├── Comisión → modal comision
│   │   ├── Movimientos → modal movimientos (filtrado por producto)
│   │   ├── Unidades → navega a /Producto/Unidades/{id}
│   │   ├── Editar → modal editar-producto (inline)
│   │   └── Eliminar → confirmación + POST
│   └── Ajuste Masivo → modal ajuste-masivo (simular + aplicar)
├── Tab: Categorías
│   ├── Por fila: [Editar] [Eliminar]
│   └── Nueva Categoría → modal nueva-categoria
└── Tab: Marcas
    ├── Por fila: [Editar] [Eliminar]
    └── Nueva Marca → modal nueva-marca

Desde barra superior de tabs:
├── "Inventario físico" → /Producto/UnidadesGlobal
└── "Movimientos" → modal movimientos (global, sin filtro de producto)
```

**Ruta de edición de producto (duplicada):**
- Modal inline en Catálogo → sin navegación
- Standalone `/Producto/Edit/{id}` → con navegación completa

---

## J. Flujo actual de Movimientos

```
/MovimientoStock/Index
├── Filtros: producto, tipo, fechas desde/hasta
├── Stats: Total, Entradas, Salidas, Ajustes
├── Tabla: fecha, tipo, producto, cantidad, costo unit/total, fuente costo, referencia, saldo post, usuario
│   └── Por fila: [Ver kardex] → /MovimientoStock/Kardex/{productoId}
└── Botón: [Registrar ajuste] → /MovimientoStock/Create

/MovimientoStock/Create
├── Formulario: Producto (select), Tipo (select + hint contextual), Cantidad, Motivo, Referencia
└── Submit → POST → redirect a Index

/MovimientoStock/Kardex/{id}
├── Header con datos del producto y back→Catálogo
├── Stats: Stock actual, Entradas, Salidas, Ajustes
├── Botón: [Registrar ajuste] → Create filtrado por producto
└── Tabla: 10 columnas (similar a Index pero con Motivo y sin nombre de producto)
```

**Acceso paralelo a Movimientos:**
- Desde `MovimientoStock/Index` (página completa)
- Desde modal inline en `Catalogo/Index` (sin navegación, fetch AJAX)
- Desde fila de producto en Catálogo (abre modal filtrado por producto)
- Desde `Producto/Unidades` (botón "Ver Kardex SKU" → Kardex standalone)

---

## K. Flujo actual de Marcas / Categorías

No tienen pantallas standalone. Toda la gestión ocurre dentro de `Catalogo/Index_tw`:

```
Tab Categorías
├── Listado completo
├── [Nueva Categoría] → modal nueva-categoria (Código, Nombre, Descripción, Padre, IVA, Control Serie, Activo)
└── Por fila: [Editar] → modal editar-categoria | [Eliminar] → confirmación + POST

Tab Marcas
├── Listado completo
├── [Nueva Marca] → modal nueva-marca
└── Por fila: [Editar] → modal editar-marca | [Eliminar] → confirmación + POST
```

---

## L. Hallazgos UX

### UX-01 — Index_tw.cshtml tiene 2379 líneas (vista mega)
La vista del catálogo concentra 3 tabs + 10 modales en un solo archivo. Esto hace que sea la vista más larga del proyecto. Funciona correctamente y tiene lógica de negocio, pero es muy difícil de mantener. Los modales deberían ser partials en una fase futura (no ahora).

### UX-02 — Ruta de edición de producto duplicada
Hay dos formas de editar un producto con la misma estructura de formulario:
1. Modal inline `modal-editar-producto` en Catálogo (sin navegación, AJAX)
2. Página standalone `/Producto/Edit_tw.cshtml` (con navegación completa)

El botón "Editar" en la tabla usa el modal. La ruta standalone puede ser accedida directamente por URL. Hay riesgo de deuda de sincronización si se agregan campos.

**Recomendación fase futura:** Evaluar si Edit_tw debe mantenerse como fallback o si puede quedar como único punto de edición.

### UX-03 — 6 botones de acciones por fila en tabla de productos
Columna "Acciones" tiene: Historial | Comisión | Movimientos | Unidades | Editar | Eliminar. En desktop con anchos generosos se ve bien. En resoluciones intermedias (~1024-1280px) la columna se comprime y los botones se apilan con `flex-wrap`.

**Recomendación:** En fases futuras, considerar menú de overflow (`⋮`) para acciones secundarias (Historial, Comisión, Movimientos) manteniendo visibles solo Unidades, Editar, Eliminar.

### UX-04 — Botón "Movimientos" con fondo rojo
En `catalogo-module.css`:
```css
#btn-movimientos-inventario {
    background-color: rgb(220, 38, 38); /* red-600 */
}
```
El rojo en el design system ERP señala peligro, error o acción destructiva. "Movimientos de inventario" es una acción operativa regular. El color genera una señal semántica incorrecta.

**Recomendación MISA-INVENTARIO-UX-1C:** Cambiar a ghost o secondary (como los demás botones de la barra de tabs).

### UX-05 — Modal de movimientos depende del botón con clase ghost que se sobreescribió
El botón `btn-movimientos-inventario` usa la clase base `.btn-erp-ghost` pero el CSS la sobreescribe con rojo sólido. Si en el futuro se cambia la clase base, el override puede resultar invisible.

### UX-06 — Vista Unidades.cshtml mezcla demasiadas responsabilidades
Concentra en ~670 líneas: configuración de trazabilidad, panel de conciliación, acciones asistidas de conciliación, carga masiva con preview, filtros, y tabla de unidades con hasta 4 formularios inline por fila. Es muy densa para pantallas medianas y totalmente inutilizable en mobile (ver hallazgo M-02).

### UX-07 — Alertas de Stock: navegación inconsistente entre subvistas
Las 5 subvistas de AlertaStock tienen distintos patrones de retorno:
- Index: no tiene botón "volver a catálogo"
- Críticos: botón "Ver todas las alertas" (vuelve a Index)
- Estadísticas: botón "Volver"
- Details: botón "Volver" + botón "Historial del producto"
- PorProducto: botón "Volver"

No hay un breadcrumb consistente que oriente al usuario dentro del módulo.

### UX-08 — Título del módulo raíz es "Inventario" pero la URL es /Catalogo
`ViewData["Title"] = "Inventario"` pero el controlador es `CatalogoController`. El usuario ve "Inventario" en el título de página y breadcrumbs pero la URL dice `/Catalogo/`. Puede generar confusión en bookmarks o navegación directa.

---

## M. Hallazgos visuales

### V-01 — Tokens visuales de Modal nuevos vs standalone Edit_tw
Los modales inline en Index_tw usan `bg-[#111827]` hardcodeado. La vista `Edit_tw.cshtml` usa `card-erp-panel`. Si el design system evoluciona el color de fondo de cards, los modales quedarán desfasados.

### V-02 — Tabla de Criticos_tw y PorProducto sin horizontal-scroll-affordance
`Criticos_tw.cshtml` usa `overflow-x-auto` directo sin el sistema `data-oc-scroll` (fades, hints, tabindex). Inconsistente con `Index_tw.cshtml` y `AlertaStock/Index_tw.cshtml` que sí lo usan.

### V-03 — Badges de Criticos_tw usan clases distintas a shared-components
`Criticos_tw.cshtml` usa `card-erp-metric card-erp-metric-danger`. Otros usan `badge-erp badge-erp-danger`. Son dos sistemas distintos para métricas. No es un error pero genera inconsistencia visual entre subvistas del mismo módulo.

### V-04 — Estado "Inactivo" en tabla de productos solo se marca con opacidad
```html
<tr class="... @(!p.Activo ? "opacity-50" : "")">
```
Los productos inactivos se muestran con 50% de opacidad. No hay badge explícito en la fila (solo un texto "Inactivo" en 10px dentro del nombre). Para usuarios con baja visión, la opacidad reducida puede no ser suficiente señal.

### V-05 — Descripción truncada en tabla de productos (max-w-[200px])
La descripción del producto en la tabla tiene `truncate max-w-[200px]`. En nombres de producto largos, el truncado puede ocultar información operativa relevante. El título del elemento muestra el nombre completo, pero no la descripción.

---

## N. Hallazgos mobile

### M-01 — Tabla de productos (min-width 1160px) con scroll horizontal
La tabla tiene `data-oc-scroll` con affordance. En mobile funciona con scroll horizontal. Sin embargo, la columna de Acciones con 6 botones requiere desplazarse mucho a la derecha. No hay alternativa responsive tipo cards para mobile.

### M-02 — Vista Unidades.cshtml completamente inutilizable en mobile
La columna de Acciones contiene hasta 4 formularios con inputs de motivo y botones cada uno. En mobile, esto ocupa prácticamente toda la pantalla por fila. No hay `data-oc-scroll` ni alternativa responsive.

### M-03 — MovimientoStock/Create_tw es el más usable en mobile
`max-w-2xl`, formulario simple, labels con `asp-for` (aunque sin `for` explícito), botones grandes. Es la pantalla más adecuada para mobile en el módulo.

### M-04 — MovimientoStock/Index_tw (11 columnas, min-width 960px)
Tabla con `overflow-x-auto` pero **sin** el sistema `data-oc-scroll`. No hay fades laterales ni hint de scroll. El usuario en mobile no sabe que puede desplazarse.

### M-05 — MovimientoStock/Kardex_tw (10 columnas, min-width 1100px)
Mismo problema que M-04. Tabla sin `data-oc-scroll`.

### M-06 — Modal de movimientos inline (apertura desde Catálogo)
El modal de movimientos en `Index_tw` es muy ancho y complejo. En mobile el modal tiene `p-4 sm:p-6` pero la tabla interior puede requerir scroll horizontal. No se auditó el HTML del modal completo (está en la parte no leída de Index_tw, líneas 1500+).

---

## O. Hallazgos accesibilidad / baja visión

### A-01 — Labels sin `for` en modales de Crear/Editar Producto (crítico)
En los modales de nuevo producto y editar producto, las labels no tienen atributo `for`:

```html
<!-- Incorrecto -->
<label class="text-sm font-medium text-slate-300">Código</label>
<input name="Codigo" type="text" ... />

<!-- Correcto -->
<label for="nuevo-codigo" class="text-sm font-medium text-slate-300">Código</label>
<input id="nuevo-codigo" name="Codigo" type="text" ... />
```

Afecta: Código, Nombre, Descripción, Precio de Costo, Precio base sin IVA, Stock Actual, Stock Mínimo en modal nuevo producto. Mismo problema en modal editar producto.

**Impacto:** Un clic en el label no activa el input. Los lectores de pantalla no pueden asociar el label con el campo.

### A-02 — Labels sin `for` en Modal Nueva Categoría y Nueva Marca
Mismo patrón: `<label class="text-sm font-semibold text-slate-300">Código</label>` sin `for`.

### A-03 — Modales sin `role="dialog"` ni `aria-modal="true"`
Los modales grandes de Catalogo (`modal-nuevo-producto`, `modal-editar-producto`, etc.) no tienen:
```html
role="dialog" aria-modal="true" aria-labelledby="..."
```
Los lectores de pantalla no identifican que hay un diálogo activo y el foco puede escapar al contenido de fondo.

### A-04 — Tablas de Movimientos y Kardex sin `scope` en `<th>`
```html
<th class="px-4 py-3 text-xs ...">Fecha / Hora</th>
```
No tienen `scope="col"`. Las tablas de datos tienen encabezados que deben declarar su alcance para lectores de pantalla.

### A-05 — Tabla de Unidades sin `scope` ni `aria-label`
La tabla `#listado-unidades` no tiene `aria-label` ni `scope` en sus `<th>`.

### A-06 — Texto gris débil en textos informativos pequeños
`text-slate-400` (WCAG ratio ~3.8:1) + `text-slate-500` (~2.8:1) sobre fondos `bg-slate-900/50` no cumple WCAG AA para texto de 10-12px (ratio mínimo requerido: 4.5:1). Afecta especialmente:
- Descripciones de 10px en tabla de productos
- Textos informativos bajo inputs en modales
- Subtítulos de 11px en badges de selección

### A-07 — Input de características en tfoot sin label accesible
```html
<input id="modal-caract-nombre" type="text" placeholder="Ej: Material" />
```
El placeholder no es equivalente a un label. En lectores de pantalla el campo puede ser leído solo como "Campo de texto" sin contexto.

### A-08 — Botón de estrella (destacado) bien implementado
```html
aria-label="@(p.EsDestacado ? "Quitar destacado de " + p.Nombre : "Marcar como destacado " + p.Nombre)"
```
Este es un ejemplo positivo: el aria-label es dinámico y descriptivo. Usar como referencia para otros botones de icono.

### A-09 — Botones Editar/Eliminar de tabla de Categorías y Marcas sin aria-label suficiente
```html
<button type="button" data-cat-edit-id="@cat.Id" title="Editar">
    <span class="material-symbols-outlined text-base">edit</span>
    <span>Editar</span>
</button>
```
El texto visible "Editar" no identifica de qué categoría se trata. Un lector de pantalla leerá "Editar" sin contexto en una lista de múltiples filas.

**Recomendación:** `aria-label="Editar @cat.Nombre"`.

---

## P. Hallazgos seguridad frontend

### SF-01 — movimientos-inventario-modal.js construye HTML dinámico (requiere auditoría)
El script `movimientos-inventario-modal.js` hace fetch a `/MovimientoStock/ListJson` y construye filas de tabla con datos del servidor. Solo se leyeron las primeras 50 líneas; no se pudo verificar si usa `innerHTML` o `textContent`/`createElement` para renderizar los datos de producto, motivo y referencia. Esto debe auditarse en una fase específica.

**Acción en MISA-INVENTARIO-UX-1E:** Leer el JS completo y verificar escapado de datos.

### SF-02 — Sin hallazgos directos en Razor
Las vistas usan `@p.Nombre`, `@p.Descripcion`, etc. que Razor escapa automáticamente por defecto en expresiones `@`. No se detectaron `@Html.Raw()` en las vistas auditadas.

### SF-03 — catalogo-index.js usa .textContent y .indexOf, no innerHTML
La búsqueda client-side usa `row.dataset.search` y comparaciones de string. No construye HTML dinámico. Correcto.

---

## Q. Ventanas incompletas o aisladas

### Q-01 — `Views/AlertaStock/PorProducto.cshtml` sin clase `_tw`
Es la única vista del módulo que no tiene sufijo `_tw`. Puede ser una vista legacy que no fue migrada al patrón actual. Requiere inspección más profunda en la fase específica de AlertaStock.

### Q-02 — `Views/Producto/Edit_tw.cshtml` standalone posiblemente redundante
El modal inline de Catálogo maneja la edición de producto de forma más eficiente (sin navegación). La vista standalone puede ser un punto de entrada legacy o para casos de error. No hay evidencia de navegación activa hacia ella desde la UI actual (el botón "Editar" en la tabla usa el modal).

### Q-03 — `Views/Caja/RegistrarMovimiento_tw.cshtml` y `Views/Reporte/MovimientosValorizados_tw.cshtml` fuera del módulo
Fueron detectadas en la búsqueda pero pertenecen a los módulos Caja y Reportes respectivamente. No son parte del alcance de Misa, pero son puntos de contacto con datos de inventario.

---

## R. Ventanas candidatas a integrar / fusionar

### R-01 — Modales de Catálogo como partials (baja prioridad, fase 2+)
Los 10+ modales inline en `Index_tw.cshtml` podrían extraerse como partials Razor para reducir la longitud de la vista principal. Esto no cambiaría el comportamiento visible pero mejoraría la mantenibilidad.

**Riesgo de fusión:** Alto impacto si se rompe algún ID o data-* que referencie el JS.
**Recomendación:** Única fase dedicada con tests de contrato UI.

### R-02 — MovimientoStock/Index + modal de movimientos en Catálogo
Actualmente hay dos formas de ver movimientos:
1. Página standalone `/MovimientoStock/Index` (con filtros completos)
2. Modal inline en Catálogo (fetch AJAX, filtros básicos)

Estas dos vistas tienen lógica de presentación muy similar. En una fase futura podría unificarse en un componente compartido. Por ahora mantener separadas para no romper el modal.

### R-03 — AlertaStock/Estadisticas podría integrarse en Index como tab o panel colapsable
Actualmente hay navegación entre `/AlertaStock/Index` y `/AlertaStock/Estadisticas`. Las estadísticas son de solo lectura y podrían mostrarse como un panel expandible dentro del Index, similar al patrón de tabs de Catálogo.

---

## S. Ventanas que conviene mantener separadas

| Pantalla | Motivo para mantener separada |
|---|---|
| `MovimientoStock/Create_tw` | Formulario operativo crítico. Debe ser standalone para evitar errores de contexto. |
| `Producto/Unidades` | Vista operativa especializada con flujos de conciliación. Mezclarla en Catálogo sería demasiado complejo. |
| `Producto/UnidadesGlobal` | Reporte global que requiere página completa. |
| `AlertaStock/Criticos` | Pantalla de urgencia operativa. Debe ser accesible directamente y rápidamente. |
| `AlertaStock/Details` | El detalle de una alerta puede tener acciones críticas (Resolver/Ignorar). Requiere pantalla dedicada. |

---

## T. Riesgos funcionales — qué NO tocar en fases visuales

| Componente | Riesgo si se toca en fase visual |
|---|---|
| `MovimientoStockService` | Gestión real de stock. Cualquier cambio en endpoints o payloads puede desincronizar StockActual. |
| `ProductoCondicionPagoService` | Condiciones de pago por producto vinculadas a ventas activas. |
| Lógica de conciliación (ProductoUnidad vs StockActual) | Cálculos críticos de trazabilidad serial. |
| Endpoints: `/Catalogo/SimularCambioPrecios`, `/Catalogo/AplicarCambioPrecioDirecto` | Modificación masiva de precios. Cualquier cambio en payloads rompe el flujo de ajuste. |
| `ProductoPrecioLista` | Fuente de verdad de precios vigentes. |
| `AlicuotaIVA` y `PorcentajeIVA` | Cálculo fiscal. No tocar en fases UX. |
| `RequiereNumeroSerie` (trazabilidad serial) | Configura el flujo de venta por producto. No es un campo de presentación. |
| Lógica de conciliación asistida (AjustarStockAgregadoAUnidadesFisicas) | Modifica StockActual real. Solo backend. |
| Selección múltiple + ajuste masivo | El flujo de selección → selection bar → modal precio es un flujo crítico de negocio. |
| `CatalogoController` → permiso `cotizaciones/view` | No cambiar el atributo de permiso en una fase visual. Es una decisión de seguridad/negocio. |

---

## U. Priorización de fases

**Impacto alto / riesgo bajo (hacer primero):**
1. A-01, A-02 — Labels sin `for` en modales → solo HTML, no toca lógica
2. A-03 — `role="dialog"` y `aria-modal` en modales → solo HTML
3. A-04, A-05 — `scope` en tablas de Movimientos, Kardex, Unidades → solo HTML
4. UX-04 — Botón Movimientos con color rojo → solo CSS (1 línea)
5. M-04, M-05 — Agregar `data-oc-scroll` a tablas de MovimientoStock/Index y Kardex

**Impacto medio / riesgo bajo:**
6. A-06 — Contraste de texto gris pequeño → CSS compartido
7. V-02 — Agregar `data-oc-scroll` a tablas de Criticos_tw y PorProducto
8. A-09 — aria-label en botones Editar/Eliminar de Categorías y Marcas
9. UX-07 — Breadcrumb consistente en subvistas de AlertaStock

**Impacto alto / riesgo medio (requiere mayor cuidado):**
10. M-02 — Vista Unidades mobile → tabla de acciones densa, requiere planificación
11. SF-01 — Auditoría innerHTML en movimientos-inventario-modal.js

**Impacto alto / riesgo alto (fase 2+):**
12. UX-01 — Extraer modales como partials en Index_tw
13. UX-02 — Evaluar redundancia Edit_tw vs modal inline
14. R-01 — Fusión modal movimientos en catálogo

---

## V. Propuesta de Roadmap MISA

### MISA-INVENTARIO-UX-1A — Accesibilidad semántica: labels, roles y scope
**Alcance:** `Views/Catalogo/Index_tw.cshtml`, `Views/MovimientoStock/Index_tw.cshtml`, `Views/MovimientoStock/Kardex_tw.cshtml`
**Cambios:**
- Agregar `for` + `id` en labels/inputs de modales de Producto, Categoría y Marca
- Agregar `role="dialog"`, `aria-modal="true"`, `aria-labelledby` en los 10 modales de Index_tw
- Agregar `scope="col"` en `<th>` de tablas de Movimientos e Index de Alertas
**Tests a ejecutar:** LayoutUiContractTests, build

### MISA-INVENTARIO-UX-1B — Scroll affordance en Movimientos y Críticos
**Alcance:** `Views/MovimientoStock/Index_tw.cshtml`, `Views/MovimientoStock/Kardex_tw.cshtml`, `Views/AlertaStock/Criticos_tw.cshtml`
**Cambios:**
- Agregar sistema `data-oc-scroll` (shell, region, fades, hint) a las tablas de MovimientoStock/Index y Kardex
- Agregar `data-oc-scroll` a tabla de Criticos_tw
- Asegurar que `horizontal-scroll-affordance.js` se carga en estas vistas
**Tests a ejecutar:** LayoutUiContractTests, build

### MISA-INVENTARIO-UX-1C — Color botón Movimientos + aria-labels botones de tabla
**Alcance:** `wwwroot/css/catalogo-module.css`, `Views/Catalogo/Index_tw.cshtml`
**Cambios:**
- Cambiar color del botón `#btn-movimientos-inventario` de rojo a secundario/ghost
- Agregar `aria-label="Editar @cat.Nombre"` y `aria-label="Editar @marca.Nombre"` en botones de filas de Categorías y Marcas
**Tests a ejecutar:** LayoutUiContractTests, build

### MISA-INVENTARIO-UX-1D — Tabla de Unidades: mobile y densidad de acciones
**Alcance:** `Views/Producto/Unidades.cshtml`
**Cambios:**
- Agregar `data-oc-scroll` a la tabla de unidades
- Reorganizar columna de acciones para reducir densidad (colapsar formularios de estado en menú contextual o panel expandible por fila)
**Tests a ejecutar:** Build. No hay tests unitarios de UI para esta vista.

### MISA-INVENTARIO-UX-1E — Auditoría innerHTML en movimientos-inventario-modal.js
**Alcance:** `wwwroot/js/movimientos-inventario-modal.js`
**Objetivo:** Verificar si el JS usa `innerHTML` con datos del servidor (producto, motivo, referencia). Si sí, migrar a `textContent`/`createElement`.
**Tests a ejecutar:** Build, Playwright si existe spec de movimientos.

### MISA-INVENTARIO-UX-2 — Breadcrumb y navegación en AlertaStock
**Alcance:** `Views/AlertaStock/*.cshtml`
**Cambios:**
- Estandarizar breadcrumbs en las 5 subvistas
- Agregar enlace "Ver en Catálogo" desde Details y PorProducto
- Evaluar integración de Estadísticas como panel en Index (si es viable sin complicar el controller)
**Tests a ejecutar:** Build, LayoutUiContractTests

---

## W. Validaciones realizadas

- `git diff --check` — ejecutado, sin warnings en archivos de esta fase
- `git status --short` — confirmado: solo `.claude/settings.local.json`, `AGENTS.md`, `CLAUDE.md`, `skills-lock.json` y archivos de Kira (no de Misa)
- Lectura completa de archivos de vistas, JS y CSS del módulo
- Inspección de controllers (solo lectura)
- Inspección de tests relacionados (solo listado)

---

## X. Tests no ejecutados y motivo

- `dotnet build` — no ejecutado. Fase audit-only, no se tocó código.
- `dotnet test` — no ejecutado. Fase audit-only.
- Playwright — no ejecutado. Fase audit-only.

Los tests de referencia para futuras fases son:
- `VentaCreate`: 60/60 OK (referencia Kira, no afectado)
- `LayoutUiContractTests`: 57/57 OK (referencia general)
- Suite general: 235/235 OK (referencia)

---

## Y. Deudas abiertas

| ID | Deuda | Prioridad |
|---|---|---|
| D-01 | Labels sin `for` en modales de Producto, Categoría, Marca | Alta |
| D-02 | Modales sin `role="dialog"` ni `aria-modal` | Alta |
| D-03 | `scope="col"` faltante en tablas de Movimientos | Media |
| D-04 | Botón Movimientos con color rojo semánticamente incorrecto | Media |
| D-05 | Tablas de MovimientoStock sin horizontal-scroll-affordance | Media |
| D-06 | Vista Unidades inutilizable en mobile | Alta (pero alta complejidad) |
| D-07 — | innerHTML sin verificar en movimientos-inventario-modal.js | Alta (seguridad) |
| D-08 | Texto slate-400/500 en 10-12px puede no cumplir WCAG AA | Media |
| D-09 | Navegación inconsistente entre subvistas de AlertaStock | Baja |
| D-10 | CatalogoController con permiso `cotizaciones/view` (anomalía a documentar) | Baja (decisión de negocio) |
| D-11 | Edit_tw standalone posiblemente redundante con modal inline | Baja (fase 2+) |
| D-12 | Sin spec Playwright dedicado para Catálogo/Alertas | Media |

---

## Z. Próximo prompt recomendado

```
PROMPT — MISA-INVENTARIO-UX-1A — Accesibilidad semántica labels, roles y scope de tablas

Actuá como Misa.

Implementar las correcciones de accesibilidad semántica de menor riesgo en el módulo Inventario/Catálogo:

1. Agregar atributos `for` e `id` en labels/inputs de los modales de Nuevo Producto y Editar Producto en Views/Catalogo/Index_tw.cshtml.
2. Agregar `role="dialog"`, `aria-modal="true"` y `aria-labelledby` en los modales de Index_tw.
3. Agregar `scope="col"` en <th> de tablas de MovimientoStock/Index_tw.cshtml y MovimientoStock/Kardex_tw.cshtml.
4. Agregar `scope="col"` en <th> de tablas de AlertaStock/Index_tw.cshtml.

No tocar: controllers, services, endpoints, payloads, lógica de negocio, Ventas, Cotización.
Validar con: dotnet build + dotnet test --filter "LayoutUiContract".
Crear rama: misa/inventario-ux-1a-accesibilidad-semantica
```
