# COTIZ-3B — Simulador de Cotización: Resultados como Cards

## A. Objetivo

Rediseñar los resultados del simulador de Cotización reemplazando la tabla densa de 8 columnas por un layout de cards responsivo, usando los tokens comerciales `.payment-option-card` y `.payment-status-chip` definidos en UX-COMERCIAL-1A, sin modificar cálculos, endpoints, payloads ni lógica de conversión.

## B. Relación con fases anteriores

- **UX-COMERCIAL-1A**: Definió los tokens CSS `.payment-option-card`, `.payment-option-card--selected`, `.payment-option-card--blocked`, `.payment-option-card--warning`, `.payment-status-chip` y variantes.
- **UX-COMERCIAL-1C**: Auditó el simulador. Identificó que `.payment-option-card` requería rediseño mayor y debía esperar COTIZ-3B.
- **COTIZ-3A**: Integró `.payment-status-chip` en `estadoBadge()`, protegió `esc()`, `optionKey()`, `updateSelectedRowHighlight()` con `data-cotizacion-row-key`.

## C. Archivos auditados (solo lectura)

- `wwwroot/css/shared-components.css` — tokens CSS verificados (`.payment-option-card`, padding, modificadores)
- `docs/cotiz-3a-simulador-resultados-visual-minimo.md` — estado anterior
- `docs/ux-comercial-1a-design-tokens-comerciales.md` — especificación tokens
- `docs/ux-comercial-1c-cotizacion-imprimir-simulador.md` — auditoría previa
- `Views/Cotizacion/Imprimir_tw.cshtml` — referencia visual
- `Views/Cotizacion/Detalles_tw.cshtml` — referencia visual
- `wwwroot/js/cotizacion-conversion.js` — sin cambios

## D. Archivos modificados

| Archivo | Cambio |
|---|---|
| `wwwroot/js/cotizacion-simulador.js` | Agregó `renderResultadoCard()`, adaptó `renderResultado()` y `updateSelectedRowHighlight()` |
| `Views/Cotizacion/Index_tw.cshtml` | Reemplazó tabla con `<div id="cotizacion-resultados-tbody" class="grid ...">` |
| `docs/cotiz-3b-simulador-resultados-cards.md` | Este documento |

## E. Diseño elegido: cards completas

Se implementó reemplazo completo de la tabla por cards. La migración fue segura porque:
- `els.resultadosTbody` es una referencia DOM que funciona igual apuntando a un `<div>` que a un `<tbody>`.
- El event delegation en `els.resultadosTbody` no depende del tipo de elemento.
- El selector `[data-cotizacion-opcion-key]` funciona en cualquier elemento DOM.
- El único selector específico de tabla era `tr[data-cotizacion-row-key]` — cambiado a `[data-cotizacion-row-key]`.

## F. Contratos preservados

| Contrato | Estado |
|---|---|
| `esc()` en todos los valores dinámicos de string | Preservado |
| Radio inputs `name="cotizacion-opcion-pago"` | Preservado |
| `data-cotizacion-opcion-key` en cada radio | Preservado |
| `data-cotizacion-row-key` en cada card/fila | Preservado |
| `state.opcionSeleccionada` | Preservado |
| `toSeleccion()` — retorna `{medioPago, plan, cantidadCuotas}` | Sin cambios |
| `optionKey()` | Sin cambios |
| `flattenOpciones()` | Sin cambios |
| `buildRequest()` / payload guardar | Sin cambios |
| Auto-selección de recomendado o primera opción con plan | Preservado |
| Radio `disabled` cuando `!plan` | Preservado |
| Endpoints `/api/cotizacion/simular` y `/api/cotizacion/guardar` | Sin cambios |
| Cálculos de totales, cuotas, recargos, descuentos | Sin cambios |
| Conversión a Venta | Sin cambios |

## G. Datos mostrados por card

Cada card muestra:
- **Header izquierdo**: radio de selección + nombre del medio de pago + chip de estado
- **Header derecho**: badge "Recomendado" (si aplica) + total final
- **Cuerpo** (solo si tiene plan): Plan, Cuotas (N x $X o "Pago único"), Recargo/Interés
- **Footer advertencias** (si existen): ícono warning + texto concatenado

## H. Selección y estado visual

- `updateSelectedRowHighlight()` agrega/quita `.payment-option-card--selected` en el div con `data-cotizacion-row-key`.
- Modificadores de card por tipo de opción:
  - Sin plan → `.payment-option-card--blocked` (radio disabled, pointer-events: none)
  - Con plan y estado RequiereCliente/RequiereEvaluacion o con advertencias → `.payment-option-card--warning`
  - Con plan, estado Disponible, sin advertencias → sin modificador (card default)
- Al seleccionar, se agrega `.payment-option-card--selected` (declarado después en CSS, sobreescribe --warning si aplica).

## I. Seguridad frontend

- `renderResultadoCard()` usa exclusivamente DOM methods (`createElement`, `textContent`, `appendChild`, `Object.assign(style)`).
- No se usa `innerHTML` en la nueva función.
- `textContent` auto-escapa HTML — todos los valores de string del servidor van por `textContent`.
- Valores numéricos (`formatCurrency`, `Intl.NumberFormat`) no pasan por `textContent` de forma insegura — son salidas de funciones de formato, no input de usuario.
- La función `estadoBadge()` (heredada de COTIZ-3A, usa innerHTML internamente) no se usa en `renderResultadoCard()`.

## J. Accesibilidad

- Radio inputs accesibles: tipo `radio`, `name` consistente, `disabled` cuando no hay plan.
- Labels conectan visualmente el radio al nombre del medio.
- Estado visible en texto (chip, no solo color).
- Foco visible heredado de estilos globales de radio input.
- Cards bloqueadas: `pointer-events: none` impide selección de opciones no disponibles.
- Área clickeable clara (radio + label).

## K. Mobile / responsive

- Grid responsivo: `grid-cols-1` (mobile) → `sm:grid-cols-2` → `xl:grid-cols-3`.
- Eliminado `min-w-[980px]` y scroll horizontal de la tabla original.
- Cards legibles desde 360px: total visible en header, cuotas en cuerpo, sin obligar a leer 8 columnas.
- Selección táctil clara: radio visible con área de toque razonable.

## L. Validaciones

- `git diff --check`: OK (advertencia LF→CRLF en `.claude/settings.local.json`, no es error de código).
- `git status`: solo archivos del proyecto + `.claude/settings.local.json` (no commiteado).

## M. Tests ejecutados

| Suite | Resultado |
|---|---|
| `LayoutUiContractTests` | 57/57 OK |
| `Layout\|Shared\|Navigation\|Sidebar\|Header\|UiContract\|Seguridad\|Auth\|Dashboard` | 230/230 OK |

## N. Playwright

Spec: `e2e/ui-4e-layout-visual.spec.js`
Resultado: **169/169 passed** — 0 failed, 0 skipped.

El test `cotizacion-desktop.png` (test 169) y `cotizacion-mobile.png` (test 161) pasaron, confirmando que la vista de Cotización renderiza correctamente con el nuevo layout de cards.

## O. Cierre de procesos

El servidor TheBuryProyect fue detectado en ejecución en puerto 5187 (iniciado externamente, probablemente por VSCode). No fue iniciado ni cerrado por esta tarea.

## P. Riesgos y deudas

| Item | Tipo | Severidad |
|---|---|---|
| `renderResultadoRow()` queda como dead code | Deuda técnica | Baja |
| No existe Playwright específico del simulador (flujo de simulación end-to-end) | Deuda QA | Media |
| Sin Playwright para el flujo de selección de card → guardar | Deuda QA | Media |
| Agrupación visual por medio de pago no implementada | Deuda UX | Baja-Media |

## Q. Próximo paso recomendado: COTIZ-QA

Crear `e2e/cotizacion-simulador.spec.js` que cubra:
- Simulación con productos y cliente
- Verificar que las cards renderizan con `.payment-option-card`
- Verificar que la selección manual actualiza `.payment-option-card--selected`
- Verificar que el flujo guardar funciona desde la card seleccionada
- Verificar mobile sin scroll horizontal
