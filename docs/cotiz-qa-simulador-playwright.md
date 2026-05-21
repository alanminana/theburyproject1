# COTIZ-QA — Validación E2E del Simulador de Cotización

## A. Objetivo

Agregar cobertura E2E específica para el simulador de Cotización post-COTIZ-3B.
La fase valida que las cards de resultados funcionan correctamente, sin modificar cálculos, endpoints, payloads ni lógica de negocio.

## B. Contexto COTIZ-3B

COTIZ-3B reemplazó la tabla de resultados de 8 columnas con `min-w-[980px]` por un grid responsivo de cards `.payment-option-card`.

Cambios que COTIZ-QA valida:

| Elemento | Antes (COTIZ-3A) | Después (COTIZ-3B) |
|---|---|---|
| Contenedor resultados | `<tbody>` dentro de `<table min-w-[980px]>` | `<div id="cotizacion-resultados-tbody" class="grid ...">` |
| Cada opción | `<tr>` con 8 celdas | `<div class="payment-option-card">` |
| Estado | `<td>` con texto | `<span class="payment-status-chip ...">` |
| Selección | `tr.bg-primary/10` | `div.payment-option-card--selected` |
| Mobile | Scroll horizontal forzado | Grid 1 col → 2 sm → 3 xl |

## C. Spec agregado

`e2e/cotizacion-simulador.spec.js`

## D. Escenarios cubiertos

| Test | Descripción | Viewports |
|---|---|---|
| T1 | Carga del simulador — estructura inicial | 7 (todos los proyectos) |
| T2 | Simulación genera cards `.payment-option-card` | 7 |
| T3 | Selección aplica `.payment-option-card--selected` y radio checked | 7 |
| T4 | Mobile 390px — sin scroll horizontal en resultados | 7 |

**Total: 29 tests (1 setup + 4 × 7 viewports)**

### T1 — Estructura inicial

- `#cotizacion-resultados-tbody` existe en el DOM desde carga.
- `#cotizacion-resultados-vacio` visible; `#cotizacion-resultados` oculto.
- `#cotizacion-simular` visible y habilitado.
- No hay `<table>` en `#cotizacion-resultados`.
- No hay `[class*="min-w-"]` en `#cotizacion-resultados`.

### T2 — Cards de resultados

- Se agrega un producto via búsqueda (`#cotizacion-producto-buscar`).
- Se simula con `#cotizacion-simular`.
- `#cotizacion-resultados` se hace visible.
- Al menos una `.payment-option-card` existe y es visible.
- Al menos un `.payment-status-chip` es visible.
- No hay `<table>` en los resultados.
- `#cotizacion-resultados-vacio` se oculta.

### T3 — Selección

- Tras simulación, se identifica el primer radio habilitado no seleccionado.
- Se hace click en ese radio.
- Exactamente una `.payment-option-card--selected` es visible.
- El radio target queda `checked`.
- Si auto-selección ya aplicó y no hay otro radio para cambiar, se verifica el estado auto-seleccionado.

### T4 — Mobile sin overflow

- Viewport forzado a 390×844.
- Si hay productos: se simula y se verifican cards.
- `document.documentElement.scrollWidth <= window.innerWidth + 2` (margen de 2px).

## E. Selectores usados

Todos son selectores existentes en producción. No se agregaron `data-testid`.

```
#cotizacion-producto-buscar          — input de búsqueda
#cotizacion-productos-dropdown       — dropdown con buttons
#cotizacion-agregar-producto         — botón agregar producto
#cotizacion-simular                  — botón simular
#cotizacion-resultados               — contenedor principal (hidden/visible)
#cotizacion-resultados-vacio         — mensaje de estado vacío
#cotizacion-resultados-tbody         — grid de cards
.payment-option-card                 — cada card de opción
.payment-option-card--selected       — card seleccionada activa
.payment-status-chip                 — chip de estado por opción
input[name="cotizacion-opcion-pago"] — radios de selección
#cotizacion-producto-seleccionado    — indicador de producto seleccionado
```

## F. Datos/seed usados o limitaciones

- No se usan IDs hardcodeados.
- La búsqueda usa términos `['an', 'el', 'or', 'is', 'ar', 'ro', 'al']` (mismo criterio que `helpers.js` de venta).
- Requiere al menos 1 producto en la DB con nombre que contenga esas letras.
- Si no hay productos: T2, T3 hacen skip con mensaje. T4 igual verifica el overflow sin datos.
- La simulación usa la API `/api/cotizacion/simular` real — sin mocks.
- No se guardan cotizaciones en la DB en estos tests (no se llama a `#cotizacion-guardar`).

## G. Mobile/overflow

T4 valida que en 390×844:
- No hay scroll horizontal (`scrollWidth <= innerWidth + 2`).
- Si hay resultados, las cards son visibles (grid de 1 columna en mobile).

COTIZ-3B eliminó `min-w-[980px]` del contenedor de resultados. T1 valida que no existe ningún `[class*="min-w-"]` en la sección de resultados.

## H. Validaciones

| Validación | Resultado |
|---|---|
| `dotnet build --configuration Release` | OK — 0 errores |
| `git diff --check` | OK |
| `git status --short` | Solo `.claude/settings.local.json` local |

## I. Tests .NET

| Suite | Resultado |
|---|---|
| `LayoutUiContractTests` | 57/57 OK |
| `Layout\|Shared\|Navigation\|...` | 230/230 OK |

## J. Playwright

### Spec nuevo

`npx.cmd playwright test e2e/cotizacion-simulador.spec.js`

**29 passed / 0 failed** — 58.3s

Detalle por viewport:

| Viewport | T1 | T2 | T3 | T4 |
|---|---|---|---|---|
| 1366x768 | OK | OK | OK | OK |
| 1280x720 | OK | OK | OK | OK |
| 768x1024 | OK | OK | OK | OK |
| 390x844  | OK | OK | OK | OK |
| 1440x900 | OK | OK | OK | OK |
| 360x740  | OK | OK | OK | OK |
| 412x915  | OK | OK | OK | OK |

### Regresión visual existente

`npx.cmd playwright test e2e/ui-4e-layout-visual.spec.js`

**169 passed / 0 failed** — 1.8m

## K. Procesos

- `TheBuryProyect.exe` (PID 23760, Debug build): preexistente, no iniciado por esta tarea, no cerrado sin confirmación.
- MCP servers (`@playwright/mcp`, `context7-mcp`), VSCode, node: infraestructura normal de sesión.
- No se inició ningún servidor de app en esta fase.

## L. Riesgos/deudas

| Ítem | Descripción |
|---|---|
| `renderResultadoRow()` | Dead code (tabla legacy). No eliminado en COTIZ-QA per spec. Eliminar en COTIZ-3C. |
| Agrupación visual por medio de pago | No implementada. Deuda de COTIZ-3C. |
| Guardar cotización | No testado en E2E (crea datos en DB). Se puede agregar como T5 opcional en COTIZ-3C. |
| Conversión a Venta | Spec de conversión pertenece al spec de venta o a COTIZ-1. No incluido aquí. |
| Filtros de medio | Los checkboxes de medio no se validan en esta fase. |

## M. Próximo paso recomendado

**COTIZ-3C** — Agrupación visual de resultados por medio de pago, y eliminación de `renderResultadoRow()`.

O **COTIZ-1** — Flujo completo de creación de cotización desde Venta/Create con selección de medio y conversión.
