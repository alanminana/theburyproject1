# MISA-CATALOGO-UX-0 — Auditoría UX de Catálogo / Productos / Marcas / Categorías

**Fase**: Audit-only / Diagnóstico / Roadmap  
**Agente**: Misa  
**Fecha**: 2026-05-23  
**Estado**: Cerrado — documentación

---

## A. Objetivo

Auditar el estado actual del frontend del módulo Catálogo (Productos / Categorías / Marcas) del ERP TheBuryProject, identificar deuda UX/UI, accesibilidad, seguridad frontend y coherencia visual, y generar un roadmap de mejoras priorizadas.

**Sin código modificado.** Este documento es el único artefacto de la fase.

---

## B. Base

- **Rama auditada**: `misa/catalogo-ux-0-auditoria` (desde `main` en `166fae8`)
- **Fase previa cerrada**: MISA-INVENTARIO-FISICO-UX-QA
- **Agente Kira**: Ventas Create cerrado. No toca Catálogo.
- **Agente Misa**: Inventario físico cerrado. Ahora Catálogo.

---

## C. Decisiones de producto que no cambian

1. Catálogo mantiene estructura de tabs (Productos / Categorías / Marcas) en una sola vista.
2. No crear vistas nuevas para Categorías o Marcas.
3. No reemplazar modales por vistas standalone.
4. El botón Movimientos de Inventario **no debe ser rojo** (no es acción destructiva).
5. AlertaStock puede tener cross-link desde Catálogo pero no fusionarse.
6. Los permisos no cambian en fases UX.
7. `ProductoPrecioLista` es la fuente de verdad de precios.
8. `VentaService`, `CreditoService`, `CajaService` y otros canónicos no se tocan.

---

## D. Archivos auditados

| Archivo | Tipo | Líneas aprox. |
|---|---|---|
| `Views/Catalogo/Index_tw.cshtml` | Vista principal | 2379 |
| `Controllers/CatalogoController.cs` | Controlador | ~300 |
| `Controllers/AlertaStockController.cs` | Controlador | ~100 |
| `wwwroot/css/catalogo-module.css` | CSS módulo | 81 |
| `wwwroot/css/shared-components.css` | CSS compartido | largo |
| `wwwroot/js/catalogo-module.js` | Bus/registro | ~100 |
| `wwwroot/js/catalogo-index.js` | Lógica principal | ~600 |
| `wwwroot/js/producto-crear-modal.js` | Modal crear prod. | ~400 |
| `wwwroot/js/producto-editar-modal.js` | Modal editar prod. | ~300 |
| `wwwroot/js/categoria-crear-modal.js` | Modal crear cat. | ~200 |
| `wwwroot/js/categoria-editar-modal.js` | Modal editar cat. | ~200 |
| `wwwroot/js/marca-crear-modal.js` | Modal crear marca | ~200 |
| `wwwroot/js/marca-editar-modal.js` | Modal editar marca | ~200 |
| `wwwroot/js/movimientos-inventario-modal.js` | Modal movimientos | ~400 |
| `wwwroot/js/producto-comision-modal.js` | Modal comisión | ~150 |
| `Views/Producto/Edit_tw.cshtml` | Vista standalone | ~250 |

---

## E. Mapa de pantallas del módulo Catálogo

```
/Catalogo/Index
  └── Index_tw.cshtml (única vista activa)
       ├── Tab: Productos
       │    ├── Barra de filtros (#filterForm)
       │    ├── Tabla de productos (#tabla-productos)
       │    └── Barra de selección flotante (#selection-bar)
       ├── Tab: Categorías
       │    └── Tabla de categorías
       └── Tab: Marcas
            └── Tabla de marcas

/Producto/Edit/{id}   ← standalone, potencialmente redundante
/AlertaStock/Index    ← módulo separado, cross-link posible
```

Rutas legacy redirigen a Index_tw:
- `Catalogo/Resumen` → `Catalogo/Index`
- `Catalogo/HistorialCambiosPrecio` → `Catalogo/Index`
- `Catalogo/DetalleCambioPrecio` → `Catalogo/Index`

---

## F. Mapa de tabs y contenido

### Tab Productos
- Barra global de acciones: link a UnidadesGlobal, `#btn-movimientos-inventario`, `#btn-ajuste-masivo`
- Formulario de filtros: `#filterForm` — búsqueda texto, categoría, marca, stock bajo, lista de precios
- Tabla: `data-oc-scroll` con `--oc-scroll-min-width: 1160px`
- Columnas: Producto, SKU, Categoría, Marca, Stock, Precio, Acciones
- Acciones por fila: Historial | Comisión | Movimientos | Unidades | Editar | Eliminar (6 acciones)
- Barra flotante de selección: `#selection-bar` — aparece al seleccionar filas, muestra conteo y botón ajuste

### Tab Categorías
- Tabla: `data-oc-scroll` con `--oc-scroll-min-width: 980px`
- Acciones: Editar | Eliminar
- Botón: Agregar categoría → abre `#modal-categoria-crear`

### Tab Marcas
- Tabla: `data-oc-scroll` con `--oc-scroll-min-width: 1020px`
- Acciones: Editar | Eliminar
- Botón: Agregar marca → abre `#modal-marca-crear`

---

## G. Mapa de modales (10 modales inline en Index_tw.cshtml)

Todos los modales están definidos inline en la vista principal. Ninguno está extraído a partial.

| ID Modal | Trigger | JS Handler | Registrado en CatalogoModule |
|---|---|---|---|
| `#modal-producto-crear` | Botón "Agregar producto" | `producto-crear-modal.js` | ✅ sí (`'producto'`) |
| `#modal-producto-editar` | Botón Editar fila | `producto-editar-modal.js` | ❌ no |
| `#modal-categoria-crear` | Botón "Agregar categoría" | `categoria-crear-modal.js` | ✅ sí (`'categoria'`) |
| `#modal-categoria-editar` | Botón Editar (cat.) | `categoria-editar-modal.js` | ❌ no |
| `#modal-marca-crear` | Botón "Agregar marca" | `marca-crear-modal.js` | ✅ sí (`'marca'`) |
| `#modal-marca-editar` | Botón Editar (marca) | `marca-editar-modal.js` | ❌ no |
| `#modal-movimientos-inventario` | Botón Movimientos fila / barra global | `movimientos-inventario-modal.js` | ❌ no |
| `#modal-comision-vendedor` | Botón Comisión fila | `producto-comision-modal.js` | ❌ no |
| `#modal-unidades-producto` | Botón Unidades fila | (inline JS en vista) | ❌ no |
| `#modal-ajuste-masivo` | Botón Ajuste masivo / selection-bar | (inline + JS) | ❌ no |

**Hallazgo crítico**: Ninguno de los 10 modales tiene `role="dialog"` ni `aria-modal="true"`.  
Esto es deuda de accesibilidad transversal al módulo entero.

---

## H. Vista standalone potencialmente redundante

`Views/Producto/Edit_tw.cshtml` (ruta `/Producto/Edit/{id}`):
- Vista de página completa para editar producto
- Tiene `asp-for`, `asp-validation-*`, antiforgery token
- Tiene el mismo form que el modal `#modal-producto-editar`
- El flujo canónico desde Catálogo usa el **modal inline + AJAX**
- La vista standalone puede existir por un flujo alternativo o como respaldo
- **No está claro si tiene referencias activas** desde menú, links directos o tests
- **Riesgo**: si un usuario llega por URL directa, sale del contexto del módulo

**Acción futura**: verificar referencias antes de cualquier decisión de mantener/unificar.

---

## I. Hallazgos — Listado de productos

### I1. Botón Movimientos de Inventario en rojo (crítico — decisión de producto violada)

**Ubicación**: `wwwroot/css/catalogo-module.css` líneas 65–80

```css
#btn-movimientos-inventario {
    background-color: rgb(220, 38, 38);  /* rojo */
}
```

El botón Movimientos aparece en la barra de acciones global del tab Productos con color rojo (`bg-red-600`).  
La decisión de producto es explícita: **Movimientos no es una acción destructiva** y no debe ser rojo.  
El rojo en este ERP se reserva para Eliminar.

**Deuda**: CSS override explícito en `catalogo-module.css` que aplica color rojo a `#btn-movimientos-inventario`.

### I2. Acciones por fila — densidad alta (6 acciones)

Cada fila de producto tiene 6 botones de acción: Historial | Comisión | Movimientos | Unidades | Editar | Eliminar.

En mobile (< 768px), esta barra de acciones colapsa el layout de la tabla y es difícil de usar.  
En desktop, la columna de acciones ocupa espacio visual significativo.

**Observación**: Las acciones de baja frecuencia (Comisión, Historial) podrían estar en un submenú contextual o dropdown en una fase de densidad.

### I3. Encabezados de tabla sin `scope="col"`

Los `<th>` de la tabla de productos no tienen `scope="col"`.  
Esto afecta lectores de pantalla que no pueden asociar correctamente celdas con su columna.

### I4. Ordenamiento de columnas sin `aria-sort`

`catalogo-index.js` implementa ordenamiento de columnas client-side pero los botones de encabezado no tienen `aria-sort="ascending"` / `aria-sort="descending"` / `aria-sort="none"`.  
Los lectores de pantalla no anuncian el estado de ordenamiento.

### I5. Barra de selección flotante (`#selection-bar`)

La barra aparece cuando se seleccionan productos para ajuste masivo de precios.  
No tiene `aria-live="polite"` para anunciar a usuarios de lectores de pantalla que aparece/desaparece.  
El conteo de productos seleccionados tampoco tiene texto alternativo claro.

### I6. Filtros — botones de icono sin `aria-label`

El botón de submit del formulario de filtros tiene solo un ícono Material Symbol, sin `aria-label` ni `title` contextual suficiente:

```html
<button type="submit" class="btn-erp-icon btn-erp-icon--primary" title="Filtrar">
    <span class="material-symbols-outlined text-lg">filter_list</span>
</button>
```

El `title` está presente pero no es equivalente a `aria-label` para todos los AT.

---

## J. Hallazgos — Filtros

### J1. Submit automático en selects

`catalogo-index.js` hace auto-submit al cambiar los selects de Categoría, Marca, Stock Bajo y Lista de Precio.  
No hay debounce ni confirmación. Produce múltiples requests si el usuario navega rápido entre opciones.

### J2. Búsqueda de texto con debounce de 180ms

El input `#catalogo-search-input` hace live search con 180ms de debounce.  
Puede producir muchos requests en conexiones lentas o usuarios que tipean rápido.  
300–400ms sería más conservador para evitar carga innecesaria.

### J3. Estado de filtros no visible al usuario

Cuando hay filtros activos no hay indicador visual claro (badge, chip, color) que el usuario sepa que está viendo resultados filtrados.  
El único indicio es que los campos de filtro tienen valores seleccionados.

---

## K. Hallazgos — Acciones por fila

### K1. Eliminar sin confirmación visible en fila

El handler de eliminación de producto está inline en `@section Scripts` de la vista:

```javascript
document.addEventListener('click', function(e) {
    const btn = e.target.closest('[data-delete-producto-id]');
    // ...
    TheBury.confirmAction(...).then(confirmed => { ... });
});
```

Usa `TheBury.confirmAction` — hay confirmación. No hay deuda funcional aquí.  
**Observación**: el patrón inline en `@section Scripts` (en lugar de JS dedicado) es inconsistente con el resto del módulo.

### K2. Destacar producto — toggle optimista

`catalogo-index.js` implementa toggle de destacado via `fetch` + actualización optimista del ícono estrella.  
Si el request falla, el ícono vuelve al estado anterior.  
**Observación**: no hay feedback toast en caso de error del toggle. Solo revert silencioso.

### K3. Botón Unidades — sin JS dedicado

El botón Unidades de fila abre `#modal-unidades-producto` pero el handler está inline en la vista.  
Inconsistente con el patrón de los otros modales que tienen JS dedicado.

---

## L. Hallazgos — Tab Categorías

### L1. Estructura funcional completa

Las categorías tienen crear y editar vía modal. Funciona correctamente.

### L2. Sin subcategorías en UI de tabla

La tabla muestra solo Nombre y Acciones. Si una categoría tiene subcategorías, no es visible desde la tabla.  
El modal de creación sí tiene campo de categoría padre.

### L3. Confirmación de eliminación

`categoria-editar-modal.js` usa `window.TheBury.confirmAction` para eliminar. OK.

---

## M. Hallazgos — Tab Marcas

### M1. Estructura funcional completa

Las marcas tienen crear y editar vía modal. Funciona correctamente.

### M2. Sin subarcas en UI de tabla

Similar a categorías: si existen submarcas, no son visibles desde la tabla principal.

### M3. Confirmación de eliminación

`marca-editar-modal.js` usa `window.TheBury.confirmAction` para eliminar. OK.

---

## N. Hallazgos — Modales (transversales)

### N1. Ningún modal tiene `role="dialog"` ni `aria-modal="true"` (crítico)

Los 10 modales del módulo son `<div>` con clases CSS para mostrar/ocultar.  
Ninguno declara `role="dialog"` ni `aria-modal="true"`.

Sin estos atributos:
- Los lectores de pantalla no anuncian que se abrió un diálogo.
- El foco no queda atrapado dentro del modal (focus trap ausente).
- El usuario de teclado puede navegar fuera del modal sin cerrarlo.
- ARIA no puede asociar `aria-labelledby` / `aria-describedby` con el modal.

**Esta es la deuda de accesibilidad más grande del módulo.**

### N2. Sin gestión de foco al abrir modal

Al abrir un modal, el foco no se mueve al interior del modal (ni al primer input, ni al botón de cerrar).  
El usuario de teclado queda con el foco en el botón que abrió el modal, fuera del modal.

### N3. Sin focus trap

Al hacer Tab dentro de un modal abierto, el foco puede salir al contenido de la página detrás del modal.

### N4. Sin `aria-labelledby` en modales

Los modales no tienen `aria-labelledby` apuntando al título visible del modal.  
Los lectores de pantalla no anuncian el nombre del diálogo al abrirlo.

### N5. Cierre de modal — tecla Escape no documentada

Algunos modales responden a Escape (handler en JS), otros no.  
No es consistente entre todos los modales.

### N6. Overlay de fondo — sin rol semántico

El overlay de fondo de los modales es un `<div>` sin rol ni handler de cierre al hacer click fuera en todos los casos.

---

## O. Hallazgos — Mobile

### O1. Tabla de productos — scroll horizontal

La tabla usa `data-oc-scroll` con `--oc-scroll-min-width: 1160px`.  
El sistema de scroll horizontal con affordance funcional (fades laterales).  
En mobile, el scroll es usable pero la columna de 6 acciones hace las filas muy anchas.

### O2. Barra global de acciones — wrapping en mobile

En pantallas < 640px, los botones de la barra global (UnidadesGlobal, Movimientos, Ajuste masivo) pueden hacer wrap de forma no controlada.  
No hay `flex-wrap` con clase responsiva explícita en todos los casos.

### O3. Filtros en mobile

El formulario de filtros en mobile apila los campos verticalmente.  
Funciona pero ocupa mucho espacio antes de llegar a la tabla.  
Un panel colapsable de filtros podría mejorar la experiencia mobile.

### O4. Tabs en mobile

Los tabs (Productos / Categorías / Marcas) en mobile no tienen scroll horizontal si los labels son largos.  
En español con nombres medios, no hay overflow actualmente, pero es frágil.

### O5. Selection bar en mobile

La barra de selección flotante `#selection-bar` está fija en la parte inferior.  
En mobile puede superponerse con la barra de navegación del sistema operativo (iOS Safari, Android Chrome).

---

## P. Hallazgos — Accesibilidad y baja visión

### P1. Contraste de texto en estados deshabilitados

Los botones de acción en estado deshabilitado o con opacidad reducida pueden caer por debajo de WCAG AA (4.5:1 para texto normal).  
No auditado con herramienta, pero el dark theme `#161c28` con texto `#94a3b8` (slate-400) en placeholders no supera 4.5:1.

### P2. Placeholder de inputs

El placeholder de los inputs usa `color: #94a3b8` (slate-400).  
Contraste contra fondo `#161c28` es aproximadamente 4.2:1 — justo debajo de WCAG AA para texto de 14px.

### P3. Íconos como único indicador de acción

Varios botones de acción de fila usan solo íconos Material Symbols sin texto visible.  
El `title` HTML está presente en algunos pero no en todos.  
Sin `aria-label`, los usuarios de lector de pantalla dependen del `title` o del contenido del `<span>`.

Ejemplo: el botón de estrella (destacar) tiene solo el ícono `star` sin label descriptivo.

### P4. Color como único indicador de estado de stock

El badge de stock bajo posiblemente usa solo color rojo para indicar alertas.  
Un ícono o texto adicional mejoraría la experiencia para usuarios con daltonismo.

### P5. Foco visible

No se auditó completamente el estilo de `:focus-visible` en todos los componentes interactivos.  
En tareas anteriores de Inventario físico se encontraron inconsistencias de foco.  
Se presume deuda similar en Catálogo.

### P6. Tamaño de target táctil

Los botones de acción de fila en mobile pueden ser menores de 44×44px (mínimo recomendado WCAG 2.5.5).  
Con 6 acciones por fila, es difícil que todos cumplan sin rediseño de la columna de acciones.

### P7. Landmarks semánticos

La vista no declara `<main>`, `<nav>`, `<aside>` explícitos.  
Los landmarks están implícitos por la estructura Razor del layout, pero no están auditados.

### P8. Anuncios ARIA para operaciones AJAX

Los toasts de éxito/error se muestran visualmente pero no tienen `aria-live` que anuncie el resultado a lectores de pantalla.  
El sistema de `catalogo:toast` custom event no incluye un contenedor `aria-live`.

---

## Q. Hallazgos — Seguridad frontend

### Q1. `escapeHtml` duplicada en múltiples archivos

Los siguientes JS tienen su propia implementación de función de escape de HTML:
- `producto-crear-modal.js` → `escapeHtml(str)` y `escapeAttr(str)`
- `producto-editar-modal.js` → `escHtml(str)`
- `categoria-crear-modal.js` → `escHtml(str)`
- `categoria-editar-modal.js` → versión propia
- `marca-crear-modal.js` → `escHtml(str)`
- `marca-editar-modal.js` → versión propia

Todas son funcionalmente equivalentes (usan `div.textContent` o `createElement` + asignación de `textContent`).  
La duplicación aumenta el riesgo de que en alguna actualización se modifique una y no otra.  
**Observación de deuda**: centralizar en `TheBury.escHtml()` en una fase JS.

### Q2. Construcción de filas de tabla con template literals en movimientos

`movimientos-inventario-modal.js` construye filas HTML de la tabla de movimientos usando template literals asignados mediante la propiedad que establece HTML de un elemento (`tr.[propiedad HTML]`).  
Usa una función `esc(s)` local para escapar valores del servidor.

El patrón es más frágil que `createElement` + `textContent` porque requiere que el desarrollador recuerde aplicar `esc()` a cada valor interpolado.  
Si se agrega un campo nuevo y se olvida aplicar `esc()`, hay riesgo de inyección.

**Estado actual**: los campos actuales están escapados. No hay XSS confirmado.  
**Deuda**: migrar a `createElement` + `textContent` en una fase JS futura.

### Q3. Inyección de datos del servidor (`window.CatalogoData`)

En `@section Scripts` de Index_tw.cshtml:

```javascript
window.CatalogoData = {
    categorias: @Html.Raw(Json.Serialize(Model.CategoriasJson)),
    marcas: @Html.Raw(Json.Serialize(Model.MarcasJson)),
    subcategorias: @Html.Raw(Json.Serialize(Model.SubcategoriasJson)),
    submarcas: @Html.Raw(Json.Serialize(Model.SubMarcasJson))
};
```

`Html.Raw` con `Json.Serialize` es el patrón correcto para inyectar JSON seguro.  
`Json.Serialize` escapa los valores problemáticos.  
**No hay deuda activa aquí**, pero documentar como zona a preservar.

### Q4. Antiforgery token presente en todos los modales AJAX

Los modales que hacen POST tienen `@Html.AntiForgeryToken()` y los JS extraen el token via `document.querySelector('[name=__RequestVerificationToken]')`.  
**OK — no hay deuda.**

### Q5. Delete handler inline en vista

El handler de eliminación de producto está en `@section Scripts` en lugar de un archivo JS dedicado.  
No hay problema de seguridad, pero es inconsistente con el patrón del resto del módulo.

### Q6. Autocomplete con `textContent` (seguro)

`producto-crear-modal.js` construye los ítems del autocomplete usando `li.textContent = item.nombre` y `li.dataset.id = item.id`.  
**Patrón seguro — no hay deuda de XSS en autocomplete.**

---

## R. Hallazgos — Modal Movimientos de Inventario

### R1. Botón en barra global — color rojo incorrecto

Detallado en I1. El CSS override en `catalogo-module.css` aplica rojo al botón global de Movimientos.  
Es el hallazgo de mayor prioridad del módulo (viola decisión de producto explícita).

### R2. Botón Movimientos por fila — coherencia con barra global

Cada fila de producto tiene su propio botón Movimientos que abre el mismo modal con el producto preseleccionado.  
La coherencia visual entre botón de fila y botón global debe mantenerse tras corregir el color.

### R3. PDF/Excel no implementados

El HTML del modal tiene botones para exportar PDF y Excel, pero `movimientos-inventario-modal.js` no implementa la funcionalidad.  
Los botones están presentes pero sin handler.  
**No es bloqueante — pero puede confundir al usuario operativo.**

### R4. Paginación funcional

El modal tiene paginación server-side para los movimientos. Funciona.

### R5. Filtros de movimientos

El modal tiene filtros por tipo de movimiento y fecha. Funcional.

---

## S. Hallazgos — AlertaStock

### S1. Módulo separado con permiso correcto

`AlertaStockController` usa `[PermisoRequerido(Modulo = "stock", Accion = "viewalerts")]`.  
Es el permiso correcto y está separado del módulo Catálogo.

### S2. Cross-link desde Catálogo

Actualmente no hay un link directo desde el módulo Catálogo a AlertaStock para productos con stock bajo.  
El badge de "stock bajo" en la tabla de productos podría tener un link o botón que navegue a AlertaStock filtrando por ese producto.  
**Observación para roadmap — no es deuda crítica.**

---

## T. Anomalía de permisos — CatalogoController

**Hallazgo crítico**:

`CatalogoController` tiene:
```csharp
[PermisoRequerido(Modulo = "cotizaciones", Accion = "view")]
```

El módulo Catálogo está protegido con el permiso de **cotizaciones**, no de catálogo o productos.

Esto significa:
1. Un usuario con acceso a Cotizaciones puede ver el Catálogo aunque no tenga permiso explícito de Catálogo.
2. Un usuario sin acceso a Cotizaciones no puede acceder al Catálogo aunque tenga permisos de productos.
3. Si en el futuro se restringen los permisos de cotizaciones, el Catálogo quedaría inaccesible inesperadamente.

**No se cambia en fase UX.** Documentar para una fase de permisos/backend.

---

## U. Mapa de scripts cargados en Index_tw

El `@section Scripts` de la vista carga 13 archivos JS en este orden:

1. `catalogo-module.js` — bus global
2. `catalogo-index.js` — lógica principal de la vista
3. `producto-crear-modal.js`
4. `producto-editar-modal.js`
5. `categoria-crear-modal.js`
6. `categoria-editar-modal.js`
7. `marca-crear-modal.js`
8. `marca-editar-modal.js`
9. `movimientos-inventario-modal.js`
10. `producto-comision-modal.js`
11. (inline: `window.CatalogoData` injection)
12. (inline: delete handler)
13. (inline: inicialización de scroll affordance y otros)

**Observación**: 13 archivos JS más 3 bloques inline es una carga elevada para una sola vista.  
En una fase JS futura se podría considerar bundling o lazy loading de modales menos frecuentes.  
**No es deuda crítica — no bloquea funcionalidad.**

---

## V. Contratos que deben preservarse en todas las fases futuras

Los siguientes IDs, clases, `data-*` y contratos son usados por JavaScript y deben mantenerse:

| Selector | Usado por |
|---|---|
| `#catalogo-search-input` | catalogo-index.js |
| `#filterForm` | catalogo-index.js |
| `#tabla-productos` | catalogo-index.js, tests |
| `#selection-bar` | catalogo-index.js |
| `[data-catalogo-tab]` | catalogo-index.js |
| `[data-delete-producto-id]` | inline delete handler |
| `[data-edit-producto-id]` | producto-editar-modal.js |
| `[data-movimiento-producto-id]` | movimientos-inventario-modal.js |
| `[data-comision-producto-id]` | producto-comision-modal.js |
| `[data-unidades-producto-id]` | inline handler |
| `[data-destaca-id]` | catalogo-index.js |
| `.btn-star-destacado` | catalogo-index.js |
| `#btn-movimientos-inventario` | catalogo-index.js, catalogo-module.css |
| `#btn-ajuste-masivo` | catalogo-index.js |
| `window.CatalogoModule` | todos los modales |
| `window.CatalogoData` | producto-crear-modal.js |
| `catalogo:toast` custom event | catalogo-index.js |
| `[name=__RequestVerificationToken]` | todos los modales con POST |

---

## W. Roadmap de mejoras

### Fase 1A — Corrección crítica de color (prioridad ALTA)

**Objetivo**: Corregir el color del botón Movimientos de Inventario de rojo a neutral.  
**Archivos**: `wwwroot/css/catalogo-module.css`  
**Riesgo**: Muy bajo — solo CSS.  
**Validación**: Build + Playwright visual.

**Detalle**:
- Eliminar o reemplazar el override rojo de `#btn-movimientos-inventario` en `catalogo-module.css`
- El botón debe usar el estilo estándar del sistema (probablemente `btn-erp` o `btn-erp-secondary`)
- Verificar también el botón Movimientos por fila en la tabla

---

### Fase 1B — Accesibilidad: `role="dialog"` y `aria-modal` en modales (prioridad ALTA)

**Objetivo**: Agregar semántica ARIA básica a los 10 modales del módulo.  
**Archivos**: `Views/Catalogo/Index_tw.cshtml`  
**Riesgo**: Bajo — solo atributos HTML, sin cambio de comportamiento visual.  
**Validación**: Build + LayoutUiContractTests + Playwright.

**Detalle**:
- Agregar `role="dialog"` a los 10 contenedores de modal
- Agregar `aria-modal="true"` a los 10
- Agregar `aria-labelledby` apuntando al `<h2>` o título visible de cada modal
- Preservar todos los IDs y contratos existentes

---

### Fase 1C — Accesibilidad: `scope="col"` en tablas (prioridad MEDIA)

**Objetivo**: Agregar `scope="col"` a los `<th>` de las tres tablas del módulo.  
**Archivos**: `Views/Catalogo/Index_tw.cshtml`  
**Riesgo**: Muy bajo — solo atributo HTML semántico.  
**Validación**: Build + LayoutUiContractTests.

---

### Fase 1D — Accesibilidad: `aria-sort` en ordenamiento de columnas (prioridad MEDIA)

**Objetivo**: Agregar `aria-sort` a los encabezados ordenables para lectores de pantalla.  
**Archivos**: `Views/Catalogo/Index_tw.cshtml` + `wwwroot/js/catalogo-index.js`  
**Riesgo**: Bajo.  
**Validación**: Build + LayoutUiContractTests.

---

### Fase 1E — Accesibilidad: gestión de foco en modales (prioridad MEDIA)

**Objetivo**: Al abrir un modal, mover el foco al primer campo o al botón de cerrar.  
**Archivos**: `wwwroot/js/catalogo-index.js` + modales individuales  
**Riesgo**: Medio — toca JS de interacción de usuario.  
**Validación**: Build + Playwright.

**Nota**: Focus trap completo es fase posterior. Este paso es solo mover el foco al abrir.

---

### Fase 1F — UX: color del botón Movimientos por fila (prioridad BAJA — puede combinarse con 1A)

Si el botón Movimientos de fila también hereda algún estilo inadecuado, corregirlo en la misma fase 1A.

---

### Fase 2 — JS: centralizar `escapeHtml` (prioridad BAJA)

**Objetivo**: Crear `TheBury.escHtml()` en un archivo compartido y reemplazar las 6+ implementaciones duplicadas.  
**Archivos**: múltiples JS + posiblemente `shared.js` o nuevo archivo utilitario  
**Riesgo**: Medio — toca todos los archivos JS del módulo.  
**Validación**: Build + todos los tests JS-dependientes + Playwright flujos de modales.

---

### Fase 3 — UX: densidad de acciones por fila en mobile (prioridad BAJA)

**Objetivo**: Reducir la densidad de la columna de acciones en mobile.  
**Opción A**: Submenú contextual/dropdown para acciones de baja frecuencia.  
**Opción B**: Menú overflow (`⋮`) en mobile, acciones expandidas en desktop.  
**Archivos**: `Views/Catalogo/Index_tw.cshtml` + `wwwroot/js/catalogo-index.js` + CSS  
**Riesgo**: Medio — afecta estructura de tabla y handlers JS.  
**Validación**: Build + Playwright visual.

---

### Nota sobre anomalía de permisos

La anomalía de `[PermisoRequerido(Modulo = "cotizaciones")]` en CatalogoController **no entra en el roadmap UX**.  
Documentada aquí para conocimiento. Debe tratarse en una fase de backend/permisos dedicada.

---

## X. Resumen priorizado de deuda

| Prioridad | Hallazgo | Fase sugerida |
|---|---|---|
| CRÍTICA | Botón Movimientos rojo (viola decisión de producto) | 1A |
| CRÍTICA | 10 modales sin `role="dialog"` / `aria-modal` | 1B |
| CRÍTICA | Anomalía de permiso en CatalogoController | Backend (fuera de UX) |
| ALTA | Sin `aria-labelledby` en modales | 1B (junto con role/aria-modal) |
| ALTA | Sin `scope="col"` en tablas | 1C |
| ALTA | Sin `aria-sort` en columnas ordenables | 1D |
| ALTA | Sin gestión de foco al abrir modales | 1E |
| MEDIA | `escapeHtml` duplicada en 6+ archivos JS | 2 |
| MEDIA | PDF/Excel sin implementar en modal Movimientos | Backlog |
| MEDIA | Construcción de filas con template literal en modal Movimientos | 2 (junto con escapeHtml) |
| BAJA | Filtros activos sin indicador visual | 3 |
| BAJA | Densidad de acciones por fila en mobile | 3 |
| BAJA | Barra de selección sin `aria-live` | 1B o 1E |
| BAJA | Vista standalone `/Producto/Edit` potencialmente redundante | Verificar primero |
| BAJA | Debounce de búsqueda en 180ms (podría ser 350ms) | Opcional |
| BAJA | Cross-link Catálogo → AlertaStock | Backlog |

---

## Y. Qué NO se toca en las fases UX de Catálogo

- Controllers (`CatalogoController`, `ProductoController`, `CategoriaController`, `MarcaController`)
- Services (`ICatalogoService`, `ICatalogLookupService`, `MovimientoStockService`)
- Entidades y migraciones
- Endpoints y payloads AJAX
- Reglas de negocio
- `ProductoPrecioLista` (fuente de verdad de precios)
- Permisos (`[PermisoRequerido]`)
- Tests unitarios e integración (no modificar, no romper)
- Contratos existentes de IDs, `data-*`, `asp-*`, antiforgery

---

## Z. Validaciones de esta fase

**Fase audit-only — sin código modificado.**

```powershell
git diff --check   # sin trailing whitespace
git status --short # solo docs/misa-catalogo-ux-0-auditoria.md nuevo
```

No corresponde ejecutar build, tests ni Playwright porque:
- No se modificó ningún archivo de código fuente
- El único archivo creado es documentación Markdown

---

## Apéndice — Archivos con deuda confirmada

| Archivo | Tipo de deuda |
|---|---|
| `wwwroot/css/catalogo-module.css` | Override rojo en #btn-movimientos-inventario |
| `Views/Catalogo/Index_tw.cshtml` | 10 modales sin role/aria-modal, th sin scope, sort sin aria-sort |
| `wwwroot/js/movimientos-inventario-modal.js` | Template literal con propiedad de asignación HTML, escapeHtml duplicada |
| `wwwroot/js/producto-editar-modal.js` | escHtml duplicada, no registrado en CatalogoModule |
| `wwwroot/js/categoria-editar-modal.js` | escHtml duplicada, no registrado en CatalogoModule |
| `wwwroot/js/marca-editar-modal.js` | escHtml duplicada, no registrado en CatalogoModule |
| `Controllers/CatalogoController.cs` | Permiso de cotizaciones en lugar de catálogo |

---

*Documento generado en fase MISA-CATALOGO-UX-0. Sin código modificado. Solo lectura y análisis.*
