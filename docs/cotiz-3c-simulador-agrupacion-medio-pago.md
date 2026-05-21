# COTIZ-3C — Agrupación visual de resultados por medio de pago

## A. Objetivo

Agrupar las cards del simulador de Cotización por medio de pago y eliminar el dead code `renderResultadoRow()`, sin alterar cálculos, endpoints, payloads, selección, guardado ni conversión a venta.

## B. Relación con fases anteriores

- **COTIZ-3A**: Simulador visual mínimo — base del formulario y estructura de secciones.
- **COTIZ-3B**: Resultados como cards — eliminó la tabla de 8 columnas, introdujo `.payment-option-card`, `.payment-status-chip`, `.payment-option-card--selected`, radio inputs, y auto-selección recomendada.
- **COTIZ-QA**: E2E específico del simulador — T1 a T4 validando estructura, cards, selección y mobile sin overflow.
- **COTIZ-3C** (esta fase): Agrupación por medio de pago + eliminación de dead code. Agrega T5 al spec.

## C. Archivos auditados (solo lectura)

- `Views/Cotizacion/Index_tw.cshtml` — estructura del DOM, contenedor `#cotizacion-resultados-tbody` con grid Tailwind.
- `wwwroot/css/shared-components.css` — clases `.payment-option-card*` y `.payment-status-chip*`.
- `docs/cotiz-3b-simulador-resultados-cards.md` — contratos de COTIZ-3B.
- `docs/cotiz-qa-simulador-playwright.md` — documentación del spec E2E.
- `wwwroot/js/cotizacion-conversion.js` — no tocado, no referencia al simulador.

## D. Archivos modificados

| Archivo | Tipo de cambio |
|---|---|
| `wwwroot/js/cotizacion-simulador.js` | Agrupación, dead code eliminado |
| `e2e/cotizacion-simulador.spec.js` | Nuevo test T5, actualización de header |
| `docs/cotiz-3c-simulador-agrupacion-medio-pago.md` | Este documento |

`Views/Cotizacion/Index_tw.cshtml` — **no modificado**. El contenedor existente era suficiente.

## E. Campo usado para agrupar

Campo: `row.opcion.medioPago` — enum numérico (0–5) que identifica el medio de pago.  
Label visual: `medioLabel(row.opcion.medioPago, row.opcion.nombreMedioPago)` — prioriza `nombreMedioPago` si está disponible, cae a tabla de enum si es número, o usa el string directamente.

Fallback para vacío: `'Sin medio informado'`.

## F. Diseño de grupos

Cada grupo es un `div.payment-option-group` con `grid-column: 1 / -1` (ocupa toda la fila del grid padre). Estructura interna:

```
div.payment-option-group
  div (header)
    span (título del medio en uppercase)
    span (badge "N opción/es")
  div (subgrid: auto-fill, minmax 260px)
    div.payment-option-card  (por cada row del grupo)
    ...
```

El subgrid usa `repeat(auto-fill, minmax(260px, 1fr))` para ser responsive sin media queries inline, adaptándose a cualquier ancho incluyendo 390px mobile.

Si solo hay un grupo, igual se muestra el header para claridad operativa.

## G. Contratos preservados

- `optionKey(row)` — sin cambios.
- `toSeleccion(row)` — sin cambios.
- `state.opcionSeleccionada` — sin cambios. La auto-selección del recomendado sigue operando sobre el array `rows` original antes de agrupar.
- `updateSelectedRowHighlight(key)` — sin cambios. Usa `querySelectorAll('[data-cotizacion-row-key]')` dentro de `resultadosTbody`, que sigue encontrando todos los cards aunque estén dentro de secciones de grupo.
- `renderResultadoCard(row)` — sin cambios.
- `esc()` — sin cambios.
- Radio inputs: `name="cotizacion-opcion-pago"`, `data-cotizacion-opcion-key` — sin cambios.
- `data-cotizacion-row-key` en cada card — sin cambios.
- Payload de guardado — sin cambios.
- Endpoints `/api/cotizacion/simular` y `/api/cotizacion/guardar` — sin cambios.
- Conversión Cotización → Venta — sin cambios.

## H. Cambios visuales esperados

El usuario debería notar:
- Cards agrupadas bajo un encabezado de medio de pago (ej. "EFECTIVO", "TARJETA CREDITO").
- Badge de cantidad de opciones por grupo (ej. "3 opciones").
- Mejor comparación entre alternativas del mismo medio.
- Misma selección y comportamiento que COTIZ-3B.

El usuario no debería notar cambios en cálculos, cuotas, recargos, descuentos, conversión, endpoints, stock, caja ni crédito.

## I. Seguridad frontend

- El header del grupo usa `title.textContent` y `countBadge.textContent` — sin `innerHTML`.
- `renderResultadoCard()` sigue usando DOM methods y `textContent` según COTIZ-3B.
- `esc()` preservada para todos los campos de datos del usuario.
- Sin introducción de nuevos vectores XSS.

## J. Accesibilidad

- Cada grupo tiene título visible en texto (`span` con `textContent`).
- Cards mantienen radio input accesible con `name` y `label`.
- No se depende solo de color para comunicar estado (chips de texto preservados).
- El header del grupo no usa `aria-*` adicionales: es presentacional, no interactivo.

## K. Mobile/responsive

- El subgrid interno usa `minmax(260px, 1fr)` — en 390px genera 1 columna, sin scroll horizontal.
- Las secciones de grupo tienen `grid-column: 1 / -1` sobre el grid padre, nunca ensanchan el layout.
- COTIZ-QA T4 sigue validando no overflow en 390px.

## L. Tests actualizados

Se agrega **T5** a `e2e/cotizacion-simulador.spec.js`:

- Verifica que exista al menos un `.payment-option-group` cuando hay cards.
- Verifica que cada grupo contenga al menos una `.payment-option-card`.
- Verifica que la auto-selección siga funcionando (`.payment-option-card--selected` visible).

El spec no usa nombres de medios hardcodeados — valida estructura y clases.

## M. Validaciones ejecutadas

| Validación | Resultado |
|---|---|
| `dotnet build --configuration Release` | 0 errores |
| `dotnet test LayoutUiContractTests` | 57/57 OK |
| `dotnet test Layout\|Shared\|Navigation\|...` | 230/230 OK |
| `git diff --check` | Sin errores de whitespace |
| `git status --short` | Solo archivos decididos |

## N. Playwright

| Spec | Resultado |
|---|---|
| `cotizacion-simulador.spec.js` | 35/35 OK (T1–T5 × 7 viewports) |
| `ui-4e-layout-visual.spec.js` | 169/169 OK |

El global-setup tuvo 1 flaky en el primer intento (timeout por warm-up de la app), se recuperó en retry. Todos los tests de contenido pasaron.

## O. Dead code eliminado

`renderResultadoRow()` — definida en COTIZ-3B, nunca referenciada. Eliminada en COTIZ-3C.

## P. Riesgos y deudas remanentes

- **Filtros de medio de pago**: los checkboxes de "Medios a incluir" no tienen cobertura E2E. Deuda recibida.
- **Test de guardar cotización**: sin spec dedicado. Deuda recibida.
- **Spec conversión Cotización → Venta**: sin spec dedicado. Deuda recibida.
- **Orden de grupos**: preserva el orden de aparición del backend. Si el backend cambia el orden, los grupos cambiarán visualmente (comportamiento correcto y esperado).
- **Medio vacío**: si `opcion.medioPago` es `null` o `undefined`, la clave del grupo es `undefined`. Se crea un grupo "Sin medio informado". No se han observado casos reales.

## Q. Próximo paso recomendado

**COTIZ-1** — Completar formulario de Cotización (campos adicionales, validaciones frontend, manejo de errores del backend).

O alternativamente:

**COTIZ-QA-2** — Agregar spec de guardar cotización y/o conversión Cotización → Venta.
