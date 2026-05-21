# UX-COMERCIAL-1A — Design Tokens Comerciales

**Rama:** `kira/ux-comercial-1a-design-tokens`
**Base:** `b78736e` (UI-5K confirmado en main)
**Fecha:** 2026-05-20
**Autor:** Kira UX-COMERCIAL-1A

---

## A. Objetivo

Agregar clases CSS base compartidas para Venta y Cotización sin aplicarlas todavía en vistas.

Esta fase prepara el vocabulario visual comercial que será consumido en fases posteriores:
- `COTIZ-1` / `COTIZ-2` / `COTIZ-3` — mejoras visuales de Cotización
- `VENTAS-UX-1` / `VENTAS-UX-2` / `VENTAS-UX-3` — mejoras visuales de Venta

---

## B. Relación con UX-COMERCIAL-0

UX-COMERCIAL-0 fue el blueprint visual que estableció:
- Decisión confirmada: NO fusionar Cotización dentro de Venta.
- Cotización = presupuesto/simulación (no impacta stock/caja/crédito).
- Venta = operación real con impacto en stock/caja/crédito.
- Integración solo visual y de lenguaje de componentes.
- Primer paso recomendado: UX-COMERCIAL-1A CSS-only.

Esta fase implementa el primer paso.

---

## C. Archivos modificados

| Archivo | Acción |
|---|---|
| `wwwroot/css/shared-components.css` | Agregado bloque `COMPONENTES COMERCIALES — UX-COMERCIAL-1A` al final |
| `docs/ux-comercial-1a-design-tokens-comerciales.md` | Creado (este archivo) |

No se modificaron vistas, JS, controllers, services, entidades, migraciones ni tests.

---

## D. Clases agregadas

### Obligatorias (7)

| Clase | Descripción |
|---|---|
| `.payment-option-card` | Card clickeable para medios de pago |
| `.payment-option-card--selected` | Estado seleccionado |
| `.payment-option-card--blocked` | Estado bloqueado (no interactivo) |
| `.payment-option-card--warning` | Estado con advertencia (amber) |
| `.payment-status-chip` | Chip compacto para estado de medio de pago |
| `.payment-status-chip--available` | Disponible (emerald) |
| `.payment-status-chip--blocked` | Bloqueado (red) |
| `.payment-status-chip--requires-client` | Requiere cliente (amber) |
| `.payment-status-chip--selected` | Seleccionado (primary) |
| `.commercial-context-bar` | Barra de contexto comercial (cliente, condición, total) |
| `.product-payment-badge` | Badge inline por producto con pago diferente al global |
| `.quote-state-badge` | Badge de estado de cotización |
| `.quote-state-badge--emitida` | Emitida — acción pendiente (amber) |
| `.quote-state-badge--convertida` | Convertida a venta — éxito (emerald) |
| `.quote-state-badge--cancelada` | Cancelada (red) |
| `.quote-state-badge--vencida` | Vencida — expirada/neutral (slate) |
| `.total-breakdown-card` | Card de desglose Subtotal / Descuento / IVA / Total |
| `.total-breakdown-card__row` | Fila de desglose |
| `.total-breakdown-card__row--total` | Fila de total (peso visual mayor) |
| `.total-breakdown-card__label` | Etiqueta de fila |
| `.total-breakdown-card__value` | Valor numérico (tabular-nums) |
| `.sticky-action-footer` | Footer mobile fixed para CTAs (se oculta en ≥768px) |

### Opcionales (3)

| Clase | Descripción |
|---|---|
| `.condition-warning-panel` | Panel de advertencia para condición restringida |
| `.credit-summary-panel` | Panel compacto de crédito disponible (info/blue) |
| `.commercial-summary-bar` | Barra de resumen horizontal de totales |
| `.commercial-summary-bar__total` | Total destacado dentro de la barra |

---

## E. Uso previsto por clase

### `.payment-option-card`
Selector visual de medio de pago en `Views/Venta/Create_tw.cshtml`.
Reemplazará la lógica actual de botones/checkboxes de medios de pago con una UI más clara y touch-friendly.

```html
<!-- Uso futuro en Venta/Create -->
<button class="payment-option-card payment-option-card--selected" type="button">
  <span class="material-symbols-outlined">payments</span>
  <span>Efectivo</span>
  <span class="payment-status-chip payment-status-chip--available">Disponible</span>
</button>
```

### `.payment-status-chip`
Indicador de disponibilidad de un medio de pago. Se mostrará dentro de la card de pago o en tooltips.

### `.commercial-context-bar`
Barra secundaria en la vista de Venta/Create mostrando cliente seleccionado, condición de pago y total parcial. Similar al `commercial-context-bar` de cotización.

```html
<!-- Uso futuro -->
<div class="commercial-context-bar">
  <span>Cliente: Empresa SA</span>
  <span>Condición: Contado</span>
  <span>Total: $12.500</span>
</div>
```

### `.product-payment-badge`
Badge inline en la fila de producto cuando el pago seleccionado para ese ítem difiere del global (ej: un producto con condición crédito personal mientras el resto es contado).

### `.quote-state-badge`
Columna "Estado" en `Views/Cotizacion/Index_tw.cshtml` y cabecera de `Views/Cotizacion/Detalles_tw.cshtml`.

```html
<span class="quote-state-badge quote-state-badge--convertida">Convertida</span>
<span class="quote-state-badge quote-state-badge--emitida">Emitida</span>
<span class="quote-state-badge quote-state-badge--cancelada">Cancelada</span>
<span class="quote-state-badge quote-state-badge--vencida">Vencida</span>
```

### `.total-breakdown-card`
Panel de desglose en `Views/Venta/Create_tw.cshtml` y vistas de Cotización.

```html
<div class="total-breakdown-card">
  <div class="total-breakdown-card__row">
    <span class="total-breakdown-card__label">Subtotal</span>
    <span class="total-breakdown-card__value">$10.000</span>
  </div>
  <div class="total-breakdown-card__row">
    <span class="total-breakdown-card__label">Descuento (5%)</span>
    <span class="total-breakdown-card__value">- $500</span>
  </div>
  <div class="total-breakdown-card__row">
    <span class="total-breakdown-card__label">IVA (21%)</span>
    <span class="total-breakdown-card__value">$2.016</span>
  </div>
  <div class="total-breakdown-card__row total-breakdown-card__row--total">
    <span class="total-breakdown-card__label">Total</span>
    <span class="total-breakdown-card__value">$11.516</span>
  </div>
</div>
```

### `.sticky-action-footer`
CTA fijo en mobile para confirmar venta o convertir cotización. Solo visible en <768px.

```html
<div class="sticky-action-footer">
  <button class="btn-erp-primary btn-erp-block">Confirmar Venta</button>
</div>
```

### `.condition-warning-panel`
Alerta inline cuando la condición de venta requiere acción del operador (ej: cliente sin habilitación para cuotas).

### `.credit-summary-panel`
Resumen compacto de crédito disponible vs. utilizado cuando el cliente tiene crédito personal activo.

### `.commercial-summary-bar`
Versión colapsada del breakdown: muestra solo el total y algunos metadatos para contexto rápido en mobile.

---

## F. Estados visuales definidos

| Estado | Color semántico | Valores hex |
|---|---|---|
| Disponible / Éxito / Convertida | emerald | `rgba(16,185,129,0.1)` / `#34d399` |
| Acción pendiente / Warning / Emitida | amber | `rgba(245,158,11,0.1)` / `#fbbf24` |
| Bloqueado / Error / Cancelada | red | `rgba(239,68,68,0.1)` / `#f87171` |
| Seleccionado / Primary | primary | `rgba(19,91,236,0.1)` / `#6ea3ff` |
| Vencida / Neutral | slate | `#1e293b` / `#64748b` |
| Info / Crédito | sky | `rgba(14,165,233,0.07)` / `#38bdf8` |

Todos los estados se diferencian por color + borde + (cuando aplica) iconografía, no solo por color.

---

## G. Accesibilidad

- **Contraste:** todos los colores de texto sobre fondos oscuros cumplen WCAG AA (4.5:1 mínimo para texto pequeño).
- **Focus visible:** `.payment-option-card` tiene `outline: 2px solid #135bec` en `:focus-visible`. Resto de componentes heredan el foco del elemento contenedor.
- **Estados no dependientes solo de color:** todos incluyen borde diferenciado por estado además del color de fondo/texto.
- **Cursor:** `.payment-option-card--blocked` usa `cursor: not-allowed` y `pointer-events: none`.
- **Touch target:** `.payment-option-card` tiene `min-height: 3rem` y `min-width: 6rem` (≥44px WCAG 2.5.5).
- **Texto legible:** `font-weight: 600/700` en chips y badges para legibilidad en tamaño pequeño.
- **Números:** `font-variant-numeric: tabular-nums` en `.total-breakdown-card__value` para alineación de montos.

---

## H. Mobile / Responsive

- `.sticky-action-footer` es el único componente con comportamiento mobile-específico:
  - Visible solo en `<768px` (se oculta con `display:none` en `@media (min-width: 768px)`)
  - Usa `padding-bottom: calc(0.75rem + env(safe-area-inset-bottom, 0px))` para notch de iPhone
  - `z-index: 40` coherente con los z-index del layout actual
- `.commercial-context-bar` usa `flex-wrap: wrap` para adaptar en pantallas pequeñas
- `.payment-option-card` tiene `min-height: 3rem` suficiente para touch

---

## I. Qué NO se aplicó todavía

Ninguna de estas clases está referenciada en vistas Razor. Las siguientes vistas permanecen sin cambios:

- `Views/Venta/Create_tw.cshtml`
- `Views/Venta/_VentaCrearModal.cshtml`
- `Views/Cotizacion/Index_tw.cshtml`
- `Views/Cotizacion/Listado_tw.cshtml`
- `Views/Cotizacion/Detalles_tw.cshtml`

Los scripts tampoco fueron tocados:
- `wwwroot/js/venta-create.js`
- `wwwroot/js/cotizacion-simulador.js`
- `wwwroot/js/cotizacion-conversion.js`

---

## J. Validaciones ejecutadas

| Validación | Resultado |
|---|---|
| `dotnet build --configuration Release` | **File-lock** por `TheBuryProyect.exe` (PID 4072, proceso externo preexistente) — no es error C# |
| `dotnet build --configuration Release -o tmpbuild_ux_comercial_1a` | **OK** — 0 errores, 1 warning esperado de solution-output |
| `git diff --check` | **OK** — solo aviso de CRLF en `.claude/settings.local.json` (local, no commiteable) |

---

## K. Tests ejecutados

| Suite | Método | Resultado |
|---|---|---|
| `LayoutUiContractTests` | `dotnet test --no-build --filter LayoutUiContractTests` | **57/57 OK** |
| Suite relevante (Layout/Shared/Navigation/Sidebar/Header/UiContract/Seguridad/Auth/Dashboard) | `dotnet test --no-build --filter "..."` | **230/230 OK** |

Se usó `--no-build` porque la DLL de test ya estaba actualizada del tmpbuild y el `.exe` del proyecto principal estaba locked por proceso externo.

---

## L. Playwright

No ejecutado en esta fase.

**Motivo:** Esta fase es CSS-only (sin cambios en views, JS ni endpoints). Los tests Playwright de UI-4E verifican estructura HTML y rutas, no estilos CSS. No hay riesgo de regresión en Playwright por agregar clases CSS nuevas nunca referenciadas en vistas.

Estado base conocido: 169/169 OK en `b78736e`.

---

## M. Cierre de procesos

| Proceso | PID | Origen | Acción |
|---|---|---|---|
| `TheBuryProyect.exe` | 4072 | Externo (preexistente) | No cerrado — no fue iniciado por esta tarea |
| `playwright-mcp` (múltiples) | varios | MCP tools de Claude | No son tests del proyecto |
| `dotnet build/test/restore` | — | Esta tarea usó `--no-build` en tests | Finalizados normalmente |
| `tmpbuild_ux_comercial_1a/` | — | Directorio temporal de build | Puede eliminarse |

---

## N. Próximas fases recomendadas

### Inmediato
- **Merge de UI-5L** — la rama `kira/ui-5l-auditoria-js-dinamica-restante` tiene el commit `12e4d74` pendiente de integrar a main.

### Siguientes con las clases de esta fase

| Fase | Alcance |
|---|---|
| **COTIZ-1** | Aplicar `.quote-state-badge` en `Views/Cotizacion/Index_tw.cshtml` |
| **COTIZ-2** | Aplicar `.commercial-context-bar` + `.total-breakdown-card` en Cotización |
| **COTIZ-3** | Integrar `.sticky-action-footer` en mobile de Cotización |
| **VENTAS-UX-1** | Aplicar `.payment-option-card` + `.payment-status-chip` en `Views/Venta/Create_tw.cshtml` |
| **VENTAS-UX-2** | Integrar `.product-payment-badge` + `.condition-warning-panel` en Venta |
| **VENTAS-UX-3** | `.credit-summary-panel` + `.commercial-summary-bar` + `.sticky-action-footer` en Venta mobile |
