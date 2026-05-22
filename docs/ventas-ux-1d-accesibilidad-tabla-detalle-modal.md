# VENTAS-UX-1D — Accesibilidad tabla de detalle de productos en modal de venta

## A. Objetivo

Auditar y aplicar micro-ajustes de accesibilidad en la tabla de detalle de productos dentro de `Views/Venta/_VentaCrearModal.cshtml`. Fase de bajo riesgo: sin cambios funcionales, sin cambios en backend, sin cambios en JS.

## B. Base y contexto

- Base: `main` en `4aaa039` (VENTAS-UX-1C integrada)
- Rama: `kira/ventas-ux-1d-accesibilidad-tabla-modal`
- VENTAS-UX-1C ya había agregado: `role="alert"` en paneles de mora y cupo insuficiente, `for` en labels, copy "Igual al pago principal de la venta", label "Tipo de pago principal".

## C. Archivos auditados

- `Views/Venta/_VentaCrearModal.cshtml` — modificado
- `wwwroot/js/venta-create.js` — solo lectura, sin modificar
- `Views/Venta/Create_tw.cshtml` — solo lectura, sin modificar
- `docs/ventas-ux-1c-copy-accesibilidad-venta-create.md` — referencia
- `TheBuryProyect.Tests/Unit/VentaCreateUiContractTests.cs` — modificado

## D. Hallazgos

### Tabla principal de detalles (`id="tbody-detalles"`, líneas 326-336)

| Header | `scope="col"` antes | `aria-label` antes |
|--------|--------------------|--------------------|
| Cód. | ✗ | — |
| Producto | ✗ | — |
| Cant. | ✗ | — |
| Unitario | ✗ | — |
| Desc. | ✗ | — |
| Subtotal | ✗ | — |
| (vacío — acciones) | ✗ | ✗ |

**Botones de acción por fila:** generados dinámicamente por `venta-create.js`. No existen en Razor. No es posible ni correcto agregar `aria-label` desde el template estático.

### Tabla de desglose del plan (sub-modal de pago por ítem, líneas 794-802)

| Header | `scope="col"` antes |
|--------|---------------------|
| Producto | ✗ |
| Precio base | ✗ |
| Plan | ✗ |
| Ajuste | ✗ |
| Total financiado | ✗ |
| Valor cuota | ✗ |

### Contraste

- Headers de tabla usan `text-slate-500` — color secundario en dark theme. Para contenido de encabezado de columna no operativo directo, es aceptable.
- `text-slate-400` en la zona de tabla aparece solo en el hint "Deslizá para ver cantidades..." — texto no crítico, puede quedar.
- No se modificaron colores.

## E. Cambios aplicados en `_VentaCrearModal.cshtml`

### Tabla principal — `th` con `scope="col"`

Todos los `th` de la tabla de detalles recibieron `scope="col"`:

- `<th scope="col" ...>Cód.</th>`
- `<th scope="col" ...>Producto</th>`
- `<th scope="col" ...>Cant.</th>`
- `<th scope="col" ...>Unitario</th>`
- `<th scope="col" ...>Desc.</th>`
- `<th scope="col" ...>Subtotal</th>`
- `<th scope="col" aria-label="Acciones" ...></th>` — el th vacío recibió además `aria-label="Acciones"`

### Tabla de desglose del plan — `th` con `scope="col"`

Todos los `th` de la tabla del sub-modal recibieron `scope="col"`:

- `<th scope="col" ...>Producto</th>`
- `<th scope="col" ...>Precio base</th>`
- `<th scope="col" ...>Plan</th>`
- `<th scope="col" ...>Ajuste</th>`
- `<th scope="col" ...>Total financiado</th>`
- `<th scope="col" ...>Valor cuota</th>`

## F. Contratos preservados

- `id="tbody-detalles"` — intacto
- `id="modal-plan-producto"`, `id="modal-plan-precio-base"`, etc. — intactos
- Todos los `data-*` y `data-oc-scroll-*` — intactos
- `role="region"` y `aria-label="Detalle de productos"` en el contenedor — intactos
- `role="alert"` en `panel-alerta-mora` y `panel-cupo-insuficiente` — intactos (VENTAS-UX-1C)
- `<option value="">Igual al pago principal de la venta</option>` — intacto (VENTAS-UX-1C)
- `for="select-tipo-pago-item"` — intacto

## G. Qué no se tocó

- `wwwroot/js/venta-create.js` — solo lectura
- `Views/Venta/Create_tw.cshtml` — no modificado
- Controllers, Services, Models, ViewModels, Migrations, DTOs
- CSS (`shared-components.css`, `venta-module.css`)
- Endpoints, payloads, cálculos
- Stock, caja, crédito, conversión
- AGENTS.md, CLAUDE.md, `.claude/settings.local.json`, `skills-lock.json`

## H. Accesibilidad

- `scope="col"` en `th` permite a lectores de pantalla (NVDA, JAWS, VoiceOver) asociar correctamente cada celda de datos con su encabezado de columna.
- `aria-label="Acciones"` en el `th` vacío evita que el lector anuncie una columna sin nombre.
- El `role="region"` con `aria-label="Detalle de productos"` ya existía y sigue activo.

## I. Contraste

No se modificaron colores. Los headers usan `text-slate-500` que en dark theme tiene contraste aceptable para texto de encabezado. El texto operativo (celdas generadas por JS) usa `text-white` y `text-slate-200` — ya con buen contraste.

## J. Riesgo funcional

Riesgo: ninguno. Los atributos `scope` y `aria-label` son puramente semánticos, no afectan layout, eventos ni JS.

## K. Tests

Tests nuevos en `VentaCreateUiContractTests.cs`:

1. `VentaCrearModal_TablaDetalle_ThsTienenScopeCol` — verifica scope="col" en los th principales de la tabla de detalles
2. `VentaCrearModal_TablaDetalle_ThAccionesTieneAriaLabel` — verifica aria-label="Acciones" en el th vacío
3. `VentaCrearModal_TablaDesglosePlan_ThsTienenScopeCol` — verifica scope="col" en los th de la tabla del sub-modal
4. `VentaCrearModal_TablaDetalle_ConservaIdTbodyDetalles` — regresión: tbody-detalles sigue presente
5. `VentaCrearModal_TablaDetalle_ConservaRoleAlert_DesdeVentasUX1C` — regresión: role="alert" en panel-alerta-mora no fue eliminado

## L. Playwright

Ver resultados en sección de validaciones. La fase es visual-semántica; Playwright cubre layout y flujos funcionales.

## M. Procesos

Ver sección de cierre de procesos.

## N. Deudas restantes

- **Botones de acción por fila con aria-label:** los botones "Eliminar" y "Configurar pago" son renderizados por `venta-create.js` (`renderDetalles()`). Para agregar `aria-label` dinámico con el nombre del producto, se requeriría modificar JS. Queda como deuda de accesibilidad para una fase JS dedicada.
- **Contraste de headers:** `text-slate-500` en headers de tabla. No es bloqueante, pero podría mejorarse a `text-slate-300` en una fase CSS-only si se identifica como problema para usuarios con baja visión severa.

## O. Próximo paso recomendado

**VENTAS-UX-1E** — Mobile y experiencia de cobro en Venta/Create:
- botón confirmar sticky en mobile
- resumen más visible en pantallas chicas
- mejor jerarquía del estado previo a confirmar
- micro-ajustes visuales sin backend
