# COTIZ-3A — Normalización visual mínima del simulador de resultados

**Fase:** COTIZ-3A  
**Rama:** kira/cotiz-3a-simulador-resultados-visual-minimo  
**Fecha:** 2026-05-21  
**Agente:** Kira COTIZ-3A

---

## A. Objetivo

Aplicar mejoras visuales mínimas y seguras al simulador de resultados de Cotización:
- Reemplazar chips de estado inline (Tailwind ad-hoc) por el token CSS `.payment-status-chip`.
- Agregar highlight visual de la fila seleccionada sin modificar lógica funcional.
- Mejorar legibilidad de filas con `transition-colors`.
- No rediseñar la tabla a cards (eso es COTIZ-3B).

---

## B. Relación con UX-COMERCIAL-1C

UX-COMERCIAL-1C (commit 1359877) auditó el simulador y concluyó:
- `estadoBadge()` usa Tailwind inline y puede migrarse con bajo riesgo a `.payment-status-chip`.
- `.payment-option-card` requiere rediseño mayor → espera COTIZ-3B.
- Falta highlight visual de la opción seleccionada.

COTIZ-3A ejecuta las dos primeras acciones de bajo riesgo identificadas en ese diagnóstico.

---

## C. Archivos auditados (solo lectura)

- `wwwroot/js/cotizacion-simulador.js` — fuente primaria de cambios
- `Views/Cotizacion/Index_tw.cshtml` — auditado, sin modificación necesaria
- `Views/Cotizacion/Imprimir_tw.cshtml` — sin cambios
- `Views/Cotizacion/Detalles_tw.cshtml` — sin cambios
- `Views/Cotizacion/Listado_tw.cshtml` — sin cambios
- `wwwroot/js/cotizacion-conversion.js` — sin cambios
- `wwwroot/css/shared-components.css` — auditado, no modificado
- `docs/ux-comercial-1c-cotizacion-imprimir-simulador.md` — diagnóstico base
- `docs/ux-comercial-1a-design-tokens-comerciales.md` — tokens base

---

## D. Archivos modificados

| Archivo | Cambio |
|---|---|
| `wwwroot/js/cotizacion-simulador.js` | estadoBadge(), renderResultadoRow(), updateSelectedRowHighlight(), change handler |

`Views/Cotizacion/Index_tw.cshtml` — no modificado (no hizo falta wrapper adicional).

---

## E. Estados detectados en el simulador

Enum `EstadoMedioPago` detectado en `estadoLabel()`:

| Valor | String |
|---|---|
| 0 | Disponible |
| 1 | NoDisponible |
| 2 | RequiereCliente |
| 3 | RequiereEvaluacion |
| 4 | BloqueadoPorProducto |
| 5 | PlanInactivo |
| 6 | CuotaInactiva |

El chip original solo mapeaba 5 estados (0–4). `PlanInactivo` y `CuotaInactiva` caían al fallback slate.

---

## F. Mapeo a `.payment-status-chip`

| Estado | Modificador CSS |
|---|---|
| Disponible | `payment-status-chip--available` |
| RequiereCliente | `payment-status-chip--requires-client` |
| RequiereEvaluacion | `payment-status-chip--requires-client` |
| BloqueadoPorProducto | `payment-status-chip--blocked` |
| PlanInactivo | `payment-status-chip--blocked` |
| CuotaInactiva | `payment-status-chip--blocked` |
| NoDisponible | `payment-status-chip--blocked` |
| (fallback) | `payment-status-chip--blocked` |

**Nota:** No existe variante `--neutral` o `--inactive` en el design system. `--blocked` es la más semánticamente cercana para todos los estados de no-disponibilidad. Se documenta para COTIZ-3B si se desea diferenciar `NoDisponible` con un tono más neutro.

---

## G. Highlight de opción seleccionada

**Aplicado.** Implementación en tres puntos:

1. `renderResultadoRow()`: agrega `tr.dataset.cotizacionRowKey = key` al elemento `<tr>`.
2. Nueva función `updateSelectedRowHighlight(selectedKey)`: recorre todas las filas y hace toggle de `bg-primary/10` según coincidencia de key.
3. `renderResultado()`: llama `updateSelectedRowHighlight()` tras auto-seleccionar la opción recomendada.
4. Handler `change` de `resultadosTbody`: llama `updateSelectedRowHighlight()` al cambiar la selección manual.

**La lógica de negocio no fue tocada:** `state.opcionSeleccionada`, `toSeleccion()`, `optionKey()`, el radio input, su `name`, su `value` y el payload de guardar permanecen intactos.

---

## H. Cambios visuales aplicados

1. **`estadoBadge()`**: reemplaza `inline-flex rounded-full border px-2 py-0.5 text-xs font-bold [tailwind-color]` por `payment-status-chip payment-status-chip--[modifier]`. El resultado es coherente con el design system comercial establecido en UX-COMERCIAL-1A.

2. **Highlight de fila seleccionada**: clase `bg-primary/10` toggled en el `<tr>` activo. Clase `transition-colors` agregada a todos los `<tr>` de resultado para suavizar el cambio.

3. **Sin cambio estructural**: la tabla mantiene sus 8 columnas, orden, datos y semántica.

---

## I. Contratos preservados

- Endpoint `/api/cotizacion/simular` — sin cambios.
- Endpoint `/api/cotizacion/guardar` — sin cambios.
- Payload de simulación — sin cambios.
- Payload de guardado (incluyendo `opcionSeleccionada`, `medioPago`, `plan`, `cantidadCuotas`) — sin cambios.
- Radio input `name="cotizacion-opcion-pago"` — sin cambios.
- `data-cotizacion-opcion-key` — sin cambios.
- `state.opcionSeleccionada` — sin cambios.
- `toSeleccion()` — sin cambios.
- `flattenOpciones()` — sin cambios.
- `buildRequest()` — sin cambios.
- `CotizacionController` — no tocado.
- `CotizacionService` — no tocado.
- `CotizacionConversionService` — no tocado.

---

## J. Seguridad frontend

- `esc()` preservada en todos los valores dinámicos: nombre de medio, label de estado, plan, advertencias, keys.
- El único `innerHTML` nuevo es el de `estadoBadge()` que usa `esc(label)` — label es un string controlado internamente (mapeado desde enum) y adicionalmente escapado.
- No se introduce `innerHTML` con datos del servidor sin escapar.
- No se eliminó ningún `esc()` existente.

---

## K. Accesibilidad

- Los chips conservan texto visible (`esc(label)`) — sin íconos que requieran `aria-label` adicional.
- `transition-colors` es visual pura, sin impacto en navegación por teclado.
- El highlight de fila (`bg-primary/10`) es visual; el radio button sigue siendo el control semántico de selección.
- No se introdujo ningún elemento interactivo nuevo.

---

## L. Mobile / Responsive

- La tabla mantiene `min-w-[980px]` con `overflow-x-auto` (definido en `Index_tw.cshtml`).
- No se cambió la densidad de columnas.
- `.payment-status-chip` tiene `white-space: nowrap` — evita ruptura del chip en mobile.
- La deuda de densidad mobile de la tabla de resultados queda documentada para COTIZ-3B.

---

## M. Validaciones

- `git diff --check` — OK (advertencia sobre CRLF en `.claude/settings.local.json`, archivo excluido del commit).
- Build Release — OK, 0 errores, 0 advertencias.

---

## N. Tests

| Suite | Resultado |
|---|---|
| LayoutUiContractTests | 57/57 OK |
| Layout\|Shared\|Navigation\|Sidebar\|Header\|UiContract\|Seguridad\|Auth\|Dashboard | 230/230 OK |

---

## O. Playwright

Spec ejecutada: `e2e/ui-4e-layout-visual.spec.js`

**Resultado: 169 passed / 0 failed**

No existe spec puntual de Cotización/Index en esta suite. Queda pendiente para COTIZ-QA o COTIZ-3B crear prueba E2E específica del simulador.

---

## P. Cierre de procesos

- `TheBuryProyect.dll` (PID 21580) — iniciado por esta tarea para Playwright; cerrado al finalizar.
- Procesos `dotnet` de build/test — finalizaron solos al completar la operación.
- No se detectaron procesos externos anómalos relacionados con esta tarea.

---

## Q. Riesgos y deudas

| Ítem | Severidad | Destino |
|---|---|---|
| NoDisponible usa `--blocked` (rojo) en lugar de tono neutro | Baja — no afecta funcionalidad | COTIZ-3B: agregar `--neutral` al design system si se requiere |
| Tabla de resultados densa en mobile (min-w-[980px]) | Media | COTIZ-3B: cards por medio de pago |
| No existe spec Playwright para el simulador (flujo real E2E) | Media | COTIZ-QA: crear spec de simulación y guardado |
| `hover:bg-white/5` en filas seleccionadas puede opacarse levemente en hover | Muy baja | Aceptable para COTIZ-3A |

---

## R. Próximo paso recomendado

**COTIZ-3B — Cards por medio de pago en el simulador**

Prerequisitos: COTIZ-3A en main.  
Objetivo: reemplazar la tabla de resultados por cards tipo `.payment-option-card` (token ya disponible en shared-components.css), una card por medio de pago con planes colapsables.  
Impacto: solo `cotizacion-simulador.js` y eventualmente `Index_tw.cshtml` para el wrapper.
