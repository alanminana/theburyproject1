# MISA-ALERTASTOCK-UX-0 — Auditoría UX visual de AlertaStock

Tipo: Auditoría UX visual / Playwright-first / doc-only
Base: `c553333` — MISA-INVENTARIO-FISICO-UX-2L integrada
Rama: `misa/alertastock-ux-0-auditoria`
Fecha: 2026-05-23

---

## A. Objetivo

Auditar completamente el módulo AlertaStock para detectar problemas visuales, de UX, accesibilidad y navegación, y planificar microfases de corrección.

No se implementan cambios en esta fase.

---

## B. Base y contexto

Ciclo cerrado previo:
- Ventas / Kira ✓
- Catálogo / Productos / Categorías / Marcas ✓
- Movimientos / Kardex ✓
- Inventario físico / Producto Unidades ✓

AlertaStock fue tocado parcialmente desde Movimientos (se agregaron links desde Kardex al alertas del producto) pero no tuvo auditoría UX propia. Ahora se audita de forma completa.

---

## C. Archivos auditados

### Vistas
- `Views/AlertaStock/Index_tw.cshtml`
- `Views/AlertaStock/Criticos_tw.cshtml`
- `Views/AlertaStock/Details_tw.cshtml`
- `Views/AlertaStock/Estadisticas_tw.cshtml`
- `Views/AlertaStock/PorProducto.cshtml`

### Controller
- `Controllers/AlertaStockController.cs`

### ViewModels
- `ViewModels/AlertaStockViewModel.cs` (contiene: AlertaStockViewModel, AlertaStockFiltroViewModel, AlertaStockEstadisticasViewModel, ProductoAlertaViewModel, CategoriaAlertaViewModel, ProductoCriticoViewModel)

### JavaScript
- `wwwroot/js/alerta-stock-index.js`

### CSS
- Sin CSS propio para AlertaStock
- `@section Styles`: carga `horizontal-scroll-affordance.css` solo en Index
- CSS global: `layout.css`, `shared-components.css`, `tailwind.css`

### Navegación (sidebar)
- `Views/Shared/_Layout.cshtml`

### Tests de integración
- `TheBuryProyect.Tests/Integration/AlertaStockServiceTests.cs`
- No existe `AlertaStockControllerTests.cs`

### Tests E2E / Playwright
- Sin specs E2E para AlertaStock. Cero cobertura Playwright.

---

## D. Mapa de pantallas

| Pantalla | Ruta | Vista |
|---|---|---|
| Listado principal | `/AlertaStock` o `/AlertaStock/Index` | `Index_tw.cshtml` |
| Productos críticos | `/AlertaStock/Criticos` | `Criticos_tw.cshtml` |
| Detalle de alerta | `/AlertaStock/Details/{id}` | `Details_tw.cshtml` |
| Estadísticas | `/AlertaStock/Estadisticas` | `Estadisticas_tw.cshtml` |
| Alertas por producto | `/AlertaStock/PorProducto/{productoId}` | `PorProducto.cshtml` |

Acciones POST (no tienen vista propia):
- `POST /AlertaStock/Resolver/{id}` → redirige a Index o returnUrl
- `POST /AlertaStock/Ignorar/{id}` → redirige a Index o returnUrl
- `POST /AlertaStock/GenerarAlertas` → redirige a Index
- `GET /AlertaStock/Pendientes` → redirige a Index con filtro Estado=Pendiente

---

## E. Mapa de flujo actual

```
Sidebar (Logística → Inventario → Catálogo)
     ↓ (no hay link directo a AlertaStock desde sidebar)
Producto/Details o URL directa
     ↓
Index (listado con filtros: Producto, Tipo, Prioridad, Estado)
     ├── Criticos (botón en header)
     ├── Estadisticas (botón en header)
     ├── GenerarAlertas (botón submit en header)
     ├── Details (link desde nombre de producto o botón Ver)
     │      ├── Resolver (POST con textarea)
     │      ├── Ignorar (POST con textarea)
     │      └── PorProducto (botón historial del producto)
     └── Kardex (link desde acciones de fila, por ProductoId)

Criticos
     └── PorProducto (link desde "Alertas pend." de cada producto)

Estadisticas
     ├── Details (link desde "Últimas alertas")
     └── PorProducto (link desde "Productos con más alertas")

PorProducto
     ├── Details (link desde cada fila)
     └── Resolver (POST directo desde historial)
```

---

## F. Mapa de controllers/endpoints

| Método | Acción | Permiso |
|---|---|---|
| GET | Index | stock.viewalerts |
| GET | Pendientes | stock.viewalerts |
| GET | Criticos | stock.viewalerts |
| GET | Details/{id} | stock.viewalerts |
| GET | Estadisticas | stock.viewalerts |
| GET | PorProducto/{id} | stock.viewalerts |
| POST | Resolver/{id} | stock.viewalerts + antiforgery |
| POST | Ignorar/{id} | stock.viewalerts + antiforgery |
| POST | GenerarAlertas | stock.viewalerts + antiforgery |

El controller está bien estructurado. Usa `IAlertaStockService`, `IProductoService`, `ICurrentUserService` y `ILogger`. `ProcesarAccionAlerta` es un método privado auxiliar que unifica Resolver e Ignorar. Sin lógica de negocio en el controller.

---

## G. Mapa de JS/CSS

### JS detectado
- `wwwroot/js/alerta-stock-index.js` — exclusivo de Index
  - Inicializa `autoDismissToasts` (4500ms)
  - Inicializa `horizontal-scroll-affordance` para todos los `[data-oc-scroll]`
  - Captura submit de `[data-alerta-confirm]` para mostrar confirmación del sistema (TheBury.confirmAction)
  - Sin `innerHTML`. Sin interpolarización de datos. Seguro.

- `wwwroot/js/horizontal-scroll-affordance.js` — cargado globalmente, instanciado desde alerta-stock-index.js

### CSS detectado
- No hay CSS propio para AlertaStock
- Index carga `horizontal-scroll-affordance.css` via `@section Styles`
- Criticos, Details, Estadisticas y PorProducto: no cargan CSS adicional
- Criticos y PorProducto usan `overflow-x-auto` nativo sin la capa de affordance

### ViewModel artefactos legacy
`AlertaStockViewModel.cs` contiene propiedades que no se usan en las vistas Tailwind actuales:
```csharp
public string BadgeTipo     // usa "bg-warning", "bg-danger", "bg-dark" (Bootstrap)
public string BadgePrioridad // usa "bg-info", "bg-warning", "bg-danger", "bg-dark text-white" (Bootstrap)
public string BadgeEstado   // usa "bg-warning", "bg-info", "bg-success", "bg-secondary" (Bootstrap)
public string IconoTipo     // usa "bi-exclamation-triangle", "bi-exclamation-octagon", etc. (Bootstrap Icons)
```
Estas propiedades no se referencian en ninguna vista `*_tw.cshtml`. Son deuda del rediseño Tailwind. Riesgo bajo (solo en ViewModel, no afecta UI).

---

## H. Tests/specs encontrados

| Archivo | Tipo | Cobertura |
|---|---|---|
| `AlertaStockServiceTests.cs` | Integración | Servicio backend (BuscarAsync, Resolver, Ignorar, etc.) |
| Sin archivos E2E | — | Cero specs Playwright para AlertaStock |
| Sin ControllerTests | — | Cero tests de controller para AlertaStock |

AlertaStock no tiene tests Playwright ni tests de controller. El módulo solo está cubierto a nivel de servicio.

---

## I. Resultado desktop (análisis estático de código)

La app no estaba disponible para auditoría visual live en este momento. El análisis se realiza por lectura directa de Razor, JS, CSS y el controller.

### Index_tw
- Encabezado con badges de conteo bien visible
- Tabla responsive con `data-oc-scroll`
- Filtros con labels correctos
- Acciones por fila: Ver, Kardex, Resolver, Ignorar
- Paginación preserva filtros
- Toast messages con roles correctos

### Criticos_tw
- Breadcrumb ✓
- Cards de métricas (agotados, críticos, total, valor en riesgo)
- Dos tablas separadas: Agotados (rojo) / Bajo mínimo (ámbar)
- `overflow-x-auto` sin affordance visual

### Details_tw
- Breadcrumb ✓
- Badges de tipo/prioridad/estado en header
- Cards de métricas (stock actual, mínimo, %, reposición)
- Panel de detalle de alerta (mensaje, fechas, observaciones)
- Formularios Resolver/Ignorar con textarea

### Estadisticas_tw
- Breadcrumb ✓
- Cards de métricas (pendientes, urgentes, vencidas, tasa resolución)
- Barras de progreso por prioridad
- Grid de tipos de stock
- Listas "Últimas alertas" y "Productos con más alertas"

### PorProducto.cshtml
- Breadcrumb ✓
- Encabezado con nombre y código de producto
- Tabla de historial de alertas del producto

---

## J. Resultado mobile (análisis estático de código)

### Index_tw — mobile
- `data-oc-scroll-hint` con texto "Deslizá la tabla..." visualmente indicado (solo en mobile, oculto en desktop)
- Hint está `hidden` y se hace visible via JS — correcto
- Filtros: `min-w-[220px] flex-1` en input de producto podría causar que el input sea más ancho que la pantalla en 390px si los demás selects se apilan
- Acciones de header: `flex-col → sm:flex-row` wrappean correctamente
- Row actions: `row-action__label` visible en desktop, collapsed en mobile (estilo del sistema)

### Criticos_tw — mobile
- Tablas de 8 columnas con solo `overflow-x-auto` sin indicación de scroll
- Usuario no sabe que puede deslizar
- En 390px la tabla quedará comprimida sin hint visual
- Hero y breadcrumb se adaptan bien

### Details_tw — mobile
- Grid `sm:grid-cols-2 lg:grid-cols-4` para métricas → 1 columna en mobile. Bien.
- `lg:grid-cols-3` para detalle + acciones → ambas secciones apiladas en mobile. Bien.

### Estadisticas_tw — mobile
- `lg:grid-cols-2` para las dos secciones principales → apiladas en mobile. Bien.
- Barras de progreso adaptables.

### PorProducto.cshtml — mobile
- Tabla de 8 columnas con solo `overflow-x-auto` sin hint
- Mismo problema que Criticos

---

## K. Hallazgos visuales

| ID | Severidad | Vista | Hallazgo |
|---|---|---|---|
| VIS-01 | Media | Criticos | `overflow-x-auto` sin scroll affordance (no se usa `data-oc-scroll`) |
| VIS-02 | Media | PorProducto | Mismo: `overflow-x-auto` sin affordance |
| VIS-03 | Baja | Estadisticas | Barras de progreso usan `bg-current opacity-70`; la legibilidad depende del color de texto del badge padre |
| VIS-04 | Baja | Index | Badges "X visibles" contabiliza solo la página actual, no el total. Puede ser confuso en modo paginado |
| VIS-05 | Baja | PorProducto | Acciones de fila usan clases inline ad-hoc (`bg-emerald-900/40`) en vez del sistema `row-action` del ERP |
| VIS-06 | Baja | Index empty state | El ícono `check_circle` en emerald para "no hay alertas" puede leerse como positivo incluso si la ausencia se debe a filtros, no a stock sano |

---

## L. Hallazgos UX

| ID | Severidad | Vista | Hallazgo |
|---|---|---|---|
| UX-01 | Alta | Sidebar | **AlertaStock no tiene entrada en el sidebar.** Solo accesible via URL directa o desde páginas de producto. Baja discoverability del módulo para usuarios operativos. |
| UX-02 | Alta | Details | `onclick="return confirm(...)"` en botón Ignorar usa `confirm()` nativo del browser, inconsistente con el sistema `data-alerta-confirm` + `TheBury.confirmAction` usado en Index y GenerarAlertas |
| UX-03 | Media | Criticos | Sin link a Kardex desde productos críticos. El usuario ve el problema pero no puede ir directamente al movimiento de stock |
| UX-04 | Media | Details | Sin link a Kardex ni a Movimientos desde el detalle de alerta. Acción natural post-revisión es ver el kardex |
| UX-05 | Media | PorProducto | Sin link a Kardex del producto. Vista de historial de alertas sin acceso al kardex |
| UX-06 | Media | Todas | Sin link al detalle del producto en Catálogo desde ninguna vista de AlertaStock |
| UX-07 | Media | Index | "X visibles" en badge puede confundir en modo paginado. Mejor "X en esta página" o "X de N" |
| UX-08 | Baja | Estadisticas | "Productos con más alertas" solo enlaza a PorProducto. No hay acceso directo a Catálogo o Kardex desde estadísticas |
| UX-09 | Baja | PorProducto | Acción "Resolver" en historial no tiene confirmación. Resuelve directamente desde la fila sin feedback intermedio |
| UX-10 | Baja | Index | Filtro `SoloUrgentes` y `SoloVencidas` están en el filtro VM pero no expuestos en la UI. Funcionalidad disponible pero invisible |

---

## M. Hallazgos mobile / scroll

| ID | Severidad | Vista | Hallazgo |
|---|---|---|---|
| MOB-01 | Media | Criticos | Tabla de 8 columnas con `overflow-x-auto` sin hint de scroll en mobile |
| MOB-02 | Media | PorProducto | Tabla de 8 columnas con `overflow-x-auto` sin hint de scroll en mobile |
| MOB-03 | Baja | Index | Row de filtros podría comprimir el input de búsqueda en 360-375px. Testear con viewport muy estrecho |
| MOB-04 | Baja | Criticos | La sección de métricas `grid-cols-4` en sm → 2 cols en mobile. Correcto, pero "Valor en riesgo" con signo `$` podría truncarse en montos altos |

---

## N. Hallazgos accesibilidad / baja visión

| ID | Severidad | Vista | Hallazgo |
|---|---|---|---|
| ACC-01 | Alta | Todas | **`scope="col"` ausente en todos los `<th>` de todas las tablas de AlertaStock.** Las tablas tienen columnas pero ningún header tiene `scope`. |
| ACC-02 | Alta | Details | Los dos `<textarea>` (observaciones/motivo) solo tienen `placeholder`. Sin `<label>` asociada via `for/id`. Inaccesibles para lector de pantalla. |
| ACC-03 | Media | Criticos | Las secciones de tabla ("Agotados" / "Stock por debajo del mínimo") son `<section>` con `<h2>` — semánticamente correcto. Pero los `overflow-x-auto` no tienen `role` ni `aria-label`. |
| ACC-04 | Media | PorProducto | El contenedor `overflow-x-auto` de la tabla de historial no tiene `role="region"` ni `aria-label` (a diferencia de Index que sí los tiene). |
| ACC-05 | Media | PorProducto | Acción "Ver" en fila es solo ícono `visibility` sin `row-action__label`. Sin `aria-label` explícito en el elemento. |
| ACC-06 | Baja | Details | `aria-label` en textarea sería más robusto que placeholder solo para screen readers |
| ACC-07 | Baja | Index | `role="status"` en toast de Info es correcto. El toast de Error usa `role="alert"` — correcto. Consistente. |
| ACC-08 | Baja | Details | El botón "Ignorar" en Details usa `onclick="return confirm(...)"` — los diálogos nativos del browser son accesibles pero inconsistentes con el sistema del ERP |

---

## O. Hallazgos de tablas y filtros

### Tablas

| Vista | Columnas | `scope="col"` | Affordance | Observaciones |
|---|---|---|---|---|
| Index | 8 | Ausente | `data-oc-scroll` ✓ | Bien estructurada |
| Criticos/Agotados | 8 | Ausente | Solo `overflow-x-auto` | Sin hint mobile |
| Criticos/Bajo mínimo | 8 | Ausente | Solo `overflow-x-auto` | Sin hint mobile |
| PorProducto | 8 | Ausente | Solo `overflow-x-auto` | Sin hint mobile |

### Filtros (Index)

| Campo | Label | For/id | Tipo | Observaciones |
|---|---|---|---|---|
| Producto | ✓ `for="alerta-producto"` | ✓ `id="alerta-producto"` | text | Bien |
| Tipo | ✓ `for="alerta-tipo"` | ✓ `id="alerta-tipo"` | select | Bien |
| Prioridad | ✓ `for="alerta-prioridad"` | ✓ `id="alerta-prioridad"` | select | Bien |
| Estado | ✓ `for="alerta-estado"` | ✓ `id="alerta-estado"` | select | Bien |
| SoloUrgentes | Ausente en UI | — | — | Filtro disponible en VM, no expuesto |
| SoloVencidas | Ausente en UI | — | — | Filtro disponible en VM, no expuesto |

Los filtros expuestos están bien implementados semánticamente. Los filtros ocultos (SoloUrgentes, SoloVencidas) son funcionalidad disponible pero no accesible al usuario.

---

## P. Hallazgos de navegación: Kardex / Movimientos / Catálogo

### Desde Index
| Destino | Presente | Forma |
|---|---|---|
| Details (alerta) | ✓ | Nombre de producto (link) + botón Ver |
| Kardex | ✓ | Botón `row-action` por fila (MovimientoStock/Kardex/{ProductoId}) |
| Criticos | ✓ | Botón en header |
| Estadisticas | ✓ | Botón en header |
| Catálogo (producto) | ✗ | No hay link al producto en Catálogo |
| Movimientos (lista) | ✗ | No hay link a MovimientoStock/Index |
| Producto/Unidades | ✗ | No hay link a Producto/Unidades |

### Desde Criticos
| Destino | Presente | Forma |
|---|---|---|
| Index (alertas) | ✓ | Botón "Ver todas las alertas" |
| Estadisticas | ✓ | Botón en header |
| PorProducto | ✓ | Link desde "Alertas pend." de cada producto |
| Kardex | ✗ | No hay link. Usuario no puede ir a Kardex desde Criticos |
| Catálogo (producto) | ✗ | No hay link al detalle del producto |

### Desde Details
| Destino | Presente | Forma |
|---|---|---|
| Index (alertas) | ✓ | Botón Volver |
| PorProducto | ✓ | Botón "Historial del producto" |
| Kardex | ✗ | No hay link. Acción natural post-revisión |
| Catálogo (producto) | ✗ | No hay link |
| Movimientos | ✗ | No hay link |

### Desde Estadisticas
| Destino | Presente | Forma |
|---|---|---|
| Index (alertas) | ✓ | Botón Volver |
| Criticos | ✓ | Botón |
| Details | ✓ | Desde "Últimas alertas" |
| PorProducto | ✓ | Desde "Productos con más alertas" |
| Kardex | ✗ | No |
| Catálogo | ✗ | No |

### Desde PorProducto
| Destino | Presente | Forma |
|---|---|---|
| Index (alertas) | ✓ | Botón Volver |
| Details | ✓ | Botón Ver en fila |
| Kardex | ✗ | No hay link. Vista de historial sin acceso al kardex del producto |
| Catálogo (producto) | ✗ | No hay link |
| Movimientos | ✗ | No |

**Resumen de gap de navegación**: Ninguna vista de AlertaStock tiene link a Catálogo (producto). Criticos y PorProducto no tienen link a Kardex. Details no tiene link a Kardex.

---

## Q. Contratos críticos a preservar

Los siguientes IDs, atributos y contratos deben mantenerse en cualquier microfase posterior:

### Formularios / antiforgery
- `@Html.AntiForgeryToken()` en todos los formularios POST (Resolver, Ignorar, GenerarAlertas) — obligatorio
- `<input type="hidden" name="rowVersion" />` en Resolver e Ignorar — optimistic concurrency
- `<input type="hidden" name="id" />` en acciones de fila
- `<input type="hidden" name="returnUrl" />` en Details

### JS hooks
- `data-alerta-confirm` → capturado por `alerta-stock-index.js` para confirmación
- `[data-oc-scroll]` → capturado por `horizontal-scroll-affordance.js` e `alerta-stock-index.js`
- `.toast-msg` → capturado por `theBury.autoDismissToasts`
- `role="status"` / `role="alert"` en toasts

### Selectores CSS críticos
- `.btn-erp-danger`, `.btn-erp-ghost`, `.btn-erp-secondary`, `.btn-erp-success` — clases del sistema
- `.badge-erp`, `.badge-erp-danger`, `.badge-erp-warning`, `.badge-erp-info`, `.badge-erp-success`, `.badge-erp-neutral` — sistema de badges
- `.row-action`, `.row-action--primary`, `.row-action--warning`, `.row-action__label` — sistema de acciones de fila
- `.card-erp-metric`, `.card-erp-metric-danger`, `.card-erp-metric-warning`, `.card-erp-metric-success` — sistema de métricas
- `.card-erp-panel`, `.card-erp-panel-padded` — sistema de paneles
- `.hero-erp`, `.filter-select-erp` — componentes del sistema
- `.table-erp-wrapper` — wrapper de tabla con scroll

### Permisos
- `[PermisoRequerido(Modulo = "stock", Accion = "viewalerts")]` — no cambiar sin verificar DI y permisos

---

## R. Roadmap propuesto MISA-ALERTASTOCK-UX

### MISA-ALERTASTOCK-UX-1A — Accesibilidad semántica
**Alcance:**
- Agregar `scope="col"` en todos los `<th>` de Index, Criticos, PorProducto
- Agregar `<label>` a los dos `<textarea>` en Details
- Agregar `aria-label` al contenedor `overflow-x-auto` de PorProducto
- Unificar confirmación de Ignorar en Details: reemplazar `onclick="return confirm(...)"` por `data-alerta-confirm` para usar `TheBury.confirmAction`

**Riesgo:** Bajo. Solo semántica y atributos. No se toca lógica.
**Tests a correr:** Build + LayoutUiContractTests si existen selectores afectados.

---

### MISA-ALERTASTOCK-UX-1B — Scroll affordance y mobile
**Alcance:**
- Migrar Criticos_tw y PorProducto.cshtml a `data-oc-scroll` (igual que Index)
- Agregar `@section Styles` con `horizontal-scroll-affordance.css` en Criticos y PorProducto
- Cargar `horizontal-scroll-affordance.js` en `@section Scripts` de Criticos y PorProducto
- Agregar `data-oc-scroll-hint` con texto de scroll en mobile para Criticos y PorProducto

**Riesgo:** Bajo. Solo agrega la capa de affordance ya existente.
**No tocar:** lógica del controller, datos de la tabla, servicios.

---

### MISA-ALERTASTOCK-UX-1C — Navegación cruzada
**Alcance:**
- Agregar link a Kardex desde Criticos (por cada producto en ambas tablas)
- Agregar link a Kardex desde Details (en la barra de acciones del header)
- Agregar link a Kardex desde PorProducto (en header o fila)
- Agregar link al Catálogo del producto desde Details y PorProducto
- Evaluar si exponer filtros `SoloUrgentes` / `SoloVencidas` en la UI del Index (checkbox o toggle)

**Riesgo:** Bajo. Solo links adicionales. No se toca lógica.
**Preservar:** `asp-controller="MovimientoStock"`, `asp-action="Kardex"`, `asp-route-id` como en Index.

---

### MISA-ALERTASTOCK-UX-1D — Consistencia de acciones y copy
**Alcance:**
- Normalizar acciones en PorProducto a sistema `row-action` / `row-action__label` (igual que Index)
- Agregar `aria-label` al botón "Ver" en PorProducto
- Revisar badging de "X visibles" en Index para paginated results
- Limpiar `BadgeTipo`, `BadgePrioridad`, `BadgeEstado`, `IconoTipo` del ViewModel si se confirma que no se usan en ningún lugar

**Riesgo:** Bajo para acciones visuales. ViewModel cleanup: revisar referencias antes de eliminar.

---

### MISA-ALERTASTOCK-QA — QA visual final
**Alcance:**
- Crear spec Playwright base para AlertaStock: login, navegación a Index, filtros, Details, Criticos, Estadisticas
- Validar mobile en 390x844 con Playwright
- Revisar scroll affordance en Criticos y PorProducto
- Confirmar contratos críticos intactos
- Cerrar la fase MISA-ALERTASTOCK-UX

**Riesgo:** Bajo. Solo lectura + specs nuevos.

---

## S. Qué se ve bien

- El sistema de alertas está funcionalmente completo (Index, Criticos, Details, Estadisticas, PorProducto).
- El controller es limpio, bien estructurado, sin lógica de negocio.
- El JS de Index (`alerta-stock-index.js`) es seguro, no usa `innerHTML`, correcto.
- Los filtros del Index tienen labels semánticamente correctos (`for`/`id`).
- Los toasts usan `role="status"` y `role="alert"` correctamente.
- Las acciones de fila en Index tienen `aria-label` por producto.
- El sistema de confirmación `data-alerta-confirm` funciona bien en Index.
- Los badges de prioridad/tipo/estado usan colores consistentes con el design system.
- Las tarjetas de métricas (Criticos, Details, Estadisticas) son claras y legibles.
- La paginación de Index preserva el estado de filtros.
- La navegación a Kardex desde Index ya está implementada y es el caso de uso principal.
- Breadcrumbs presentes en todas las vistas excepto Index.

---

## T. Qué se ve mal

1. **No hay entrada en el sidebar** — el módulo existe pero es esencialmente invisible para usuarios que no conocen la URL. Es el hallazgo de mayor impacto operativo.
2. **`scope="col"` ausente en todas las tablas** — gap de accesibilidad sistemático.
3. **Textareas sin `<label>`** en Details — accesibilidad básica faltante.
4. **Criticos y PorProducto sin scroll affordance** — en mobile las tablas de 8 columnas son silenciosamente scrolleables sin indicación visual.
5. **Inconsistencia de confirmación** — `confirm()` nativo en Details vs. sistema `TheBury.confirmAction` en Index.
6. **Navegación cruzada incompleta** — no se puede ir a Kardex desde Criticos, Details ni PorProducto; no se puede ir al producto en Catálogo desde ninguna vista.
7. **Cero specs E2E** — el módulo no tiene cobertura Playwright.

---

## U. Qué no conviene cambiar

- La estructura funcional del controller (acciones, permisos, antiforgery, rowVersion).
- El sistema de badges y componentes CSS existentes.
- Los links a Kardex desde Index (ya funcionan).
- El sistema de scroll affordance de Index (ya funciona).
- Los filtros de Index (bien implementados).
- Las propiedades funcionales del ViewModel (StockActual, Prioridad, Estado, etc.).
- El `ProcesarAccionAlerta` del controller.
- El `AlertaStockFiltroViewModel` (contiene campos útiles aunque no todos estén expuestos en UI).

---

## V. Contratos críticos a preservar (resumen)

Ver sección Q para detalle completo. En resumen:
- `@Html.AntiForgeryToken()` en todos los POSTs
- `rowVersion` en Resolver/Ignorar
- `returnUrl` en Details
- `data-alerta-confirm` en GenerarAlertas
- `[data-oc-scroll]` en Index
- `.toast-msg` con roles ARIA
- Todos los `asp-action`, `asp-controller`, `asp-route-*` de los links existentes
- El permiso `stock.viewalerts`

---

## W. Roadmap propuesto (resumen)

| Fase | Foco | Riesgo | Prioridad |
|---|---|---|---|
| MISA-ALERTASTOCK-UX-1A | Accesibilidad semántica: `scope="col"`, labels, aria, confirmación unificada | Bajo | Alta |
| MISA-ALERTASTOCK-UX-1B | Scroll affordance mobile en Criticos y PorProducto | Bajo | Alta |
| MISA-ALERTASTOCK-UX-1C | Navegación cruzada: links a Kardex y Catálogo, filtros ocultos | Bajo | Media |
| MISA-ALERTASTOCK-UX-1D | Consistencia de acciones: row-action en PorProducto, ViewModel cleanup | Bajo | Media |
| MISA-ALERTASTOCK-QA | QA visual + specs Playwright base | Bajo | Alta |

**Nota sobre sidebar**: Agregar AlertaStock al sidebar requiere decisión de diseño de IA (¿en Logística? ¿junto a Inventario?). Se sugiere evaluar en la fase 1A o 1C. Si se decide agregar, tocar `_Layout.cshtml` solo después de confirmar el permiso correcto (`stock.viewalerts`).

---

## X. Decisión de prioridad

**Prioridad recomendada: MISA-ALERTASTOCK-UX-1A primero.**

Motivo: La ausencia de `scope="col"` y labels en textareas son deudas de accesibilidad sistemáticas. La inconsistencia de confirmación (UX-02) es un riesgo UX menor pero reparable en el mismo lote. Es el micro-lote de menor riesgo y mayor impacto en calidad base.

**Luego: MISA-ALERTASTOCK-UX-1B.**

Motivo: El scroll affordance en Criticos y PorProducto es visualmente importante para mobile. Es un copy-paste del patrón ya existente en Index. Muy bajo riesgo.

**Luego: MISA-ALERTASTOCK-UX-1C (navegación).**

Motivo: Los links a Kardex desde Criticos y Details son la funcionalidad operativa más útil. Son simples `<a asp-*>`. Sin tocar lógica.

**La entrada al sidebar** debería discutirse antes de implementar 1A o planificarse en 1C como ítem separado. Requiere una decisión: ¿en qué sección? ¿bajo qué permiso?

---

## Y. Próximo prompt recomendado

```
PROMPT — MISA-ALERTASTOCK-UX-1A — Accesibilidad semántica de AlertaStock

Base: main actual después de integrar MISA-ALERTASTOCK-UX-0.
Contexto: auditoría UX-0 detectó:
- scope="col" ausente en todas las tablas de Index, Criticos, PorProducto
- <textarea> en Details sin <label> asociada
- El contenedor overflow-x-auto de PorProducto sin role/aria-label
- Botón Ignorar en Details usa confirm() nativo en vez de data-alerta-confirm

Alcance permitido:
- Agregar scope="col" a todos los <th> de Index_tw, Criticos_tw, PorProducto
- Agregar <label for="..."> a los dos <textarea> en Details_tw
- Agregar aria-label al overflow-x-auto de PorProducto
- Reemplazar onclick="return confirm(...)" en Details_tw por data-alerta-confirm

No tocar:
- JS funcional, lógica de Razor, estilos, backend, controller, servicios

Validar: build + LayoutUiContractTests si existen selectores afectados.
Cerrar con informe completo según CLAUDE.md sección 28.
```

---

*Documento generado en fase audit-only. No se modificó código productivo.*
*HEAD base: `c553333`*
