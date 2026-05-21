# UX-COMERCIAL-1C — Auditoría Cotización Imprimir y Simulador

## A. Objetivo

Auditar `Views/Cotizacion/Imprimir_tw.cshtml` y `Views/Cotizacion/Index_tw.cshtml` (simulador)
para evaluar la aplicabilidad de los tokens comerciales definidos en UX-COMERCIAL-1A,
y decidir si conviene aplicar `.payment-option-card`, `.payment-status-chip` o `.total-breakdown-card`.

Fase principalmente de auditoría y diagnóstico. No se hace rediseño grande ni se toca lógica funcional.

---

## B. Relación con fases anteriores

| Fase | Qué aportó |
|------|-----------|
| UX-COMERCIAL-0 | Definió separación Cotización ≠ Venta. Cotización = simulación, Venta = operación real. |
| UX-COMERCIAL-1A | Creó tokens CSS en `shared-components.css`: `.payment-option-card`, `.payment-status-chip`, `.total-breakdown-card`, `.quote-state-badge`, etc. |
| UX-COMERCIAL-1B | Aplicó `.quote-state-badge` en `Listado_tw.cshtml` y `Detalles_tw.cshtml`. |
| UX-COMERCIAL-1C | Esta fase: auditoría de Imprimir y simulador. Sin aplicación de tokens. |

---

## C. Vistas auditadas

| Vista | Tamaño | Observación |
|-------|--------|-------------|
| `Imprimir_tw.cshtml` | 16 KB | `Layout = null`. CSS inline propio. Vista de impresión standalone. |
| `Index_tw.cshtml` | 21 KB | Simulador. Usa Tailwind + layout principal. Carga `cotizacion-simulador.js`. |
| `Detalles_tw.cshtml` | 20 KB | Solo referencia. Ya tiene `quote-state-badge` de UX-1B. |
| `Listado_tw.cshtml` | 7 KB | Solo referencia. Ya tiene `quote-state-badge` de UX-1B. |

---

## D. JS auditado

| Archivo | Tamaño | Observación |
|---------|--------|-------------|
| `cotizacion-simulador.js` | 27 KB | Simulador completo. IIFE autocontenida. |
| `cotizacion-conversion.js` | 16 KB | Solo referencia. Maneja conversión a Venta. No modificar. |

---

## E. Hallazgos en Imprimir_tw.cshtml

### Estructura general
- **`Layout = null`**: HTML standalone completo con `<style>` inline propio.
- **No carga** `shared-components.css`, ni ninguna hoja externa de la app.
- CSS diseñado para fondo blanco (`background: #ffffff`) y texto oscuro (`color: #111827`). Print-safe.

### Estado visible
- Tiene `switch` Razor con clases locales: `.badge`, `.badge-vencida`, `.badge-convertida`, `.badge-cancelada`.
- Los colores son adecuados para impresión: bordes y texto en `#d97706`, `#dc2626`, `#059669`, `#6b7280` sobre fondo blanco.

### Sección de pago seleccionado
- Bloque Razor estático (`.section > .field`) con: Medio, Plan, Cuotas, Valor cuota.
- Si no hay opción seleccionada, muestra texto "Sin opcion de pago seleccionada."

### Tabla de productos
- Tabla Razor estática. Soporte condicional para columna descuento.
- Legible y correcta para impresión.

### Ajuste por plan de pago
- Sección condicional: solo aparece si hay recargo, descuento o interés en la opción seleccionada.
- Tabla secondary con columnas dinámicas según qué ajustes aplican.

### Desglose de totales
- Sección `.totals` Razor puro: Subtotal, Descuento total, Total base, Total c/plan (condicional), Total.
- Clases locales: `.total-row`, `.total-row:last-child` (fondo negro, texto blanco para total final).

### Media queries
- `@media (max-width: 720px)`: layout responsive en mobile.
- `@media print`: oculta `.actions`, elimina bordes de página, fondo blanco.

---

## F. Hallazgos en simulador (Index_tw.cshtml + cotizacion-simulador.js)

### Layout HTML (Razor)
- Barra de contexto con badges informativos (Tailwind inline).
- Sección Productos: tabla con `overflow-x-auto` y `min-w-[760px]`. Generada por JS (`renderProductos`).
- Sección Resultados: 3 cards fijos (Subtotal, Descuento, Total base) + tabla de opciones de pago.
- Aside: Cliente opcional, Medios a incluir, Observaciones, botones Simular/Guardar.

### Tabla de resultados (generada por JS)
- `overflow-x-auto` + `min-w-[980px]`. Columnas: Medio, Estado, Plan, Cuotas, Total, Valor cuota, Recargo/Interés, Advertencias.
- Cada fila generada por `renderResultadoRow()` con `innerHTML`.
- Selección de opción via radio button por fila (estado en `state.opcionSeleccionada`).

### Estados de opción de pago detectados
La función `estadoBadge()` en `cotizacion-simulador.js` genera chips inline con estas variantes:

| Estado enum | Label visual | Color Tailwind actual |
|-------------|-------------|----------------------|
| 0 | Disponible | Verde (`emerald-500`) |
| 1 | NoDisponible | Gris (`slate-700/800`) |
| 2 | RequiereCliente | Ámbar (`amber-500`) |
| 3 | RequiereEvaluacion | Ámbar (`amber-500`) |
| 4 | BloqueadoPorProducto | Rojo (`red-500`) |
| 5 | PlanInactivo | Gris (fallback) |
| 6 | CuotaInactiva | Gris (fallback) |

Implementación actual (JS):
```js
function estadoBadge(estado) {
    const label = estadoLabel(estado);
    const tone = {
        Disponible: 'border-emerald-500/20 bg-emerald-500/10 text-emerald-400',
        RequiereCliente: 'border-amber-500/20 bg-amber-500/10 text-amber-400',
        RequiereEvaluacion: 'border-amber-500/20 bg-amber-500/10 text-amber-400',
        BloqueadoPorProducto: 'border-red-500/20 bg-red-500/10 text-red-400',
        NoDisponible: 'border-slate-700 bg-slate-800 text-slate-400'
    }[label] || 'border-slate-700 bg-slate-800 text-slate-400';
    return `<span class="inline-flex rounded-full border px-2 py-0.5 text-xs font-bold ${tone}">${esc(label)}</span>`;
}
```

Equivalencia directa con `payment-status-chip`:
- `Disponible` → `.payment-status-chip--available`
- `RequiereCliente` / `RequiereEvaluacion` → `.payment-status-chip--requires-client`
- `BloqueadoPorProducto` / `NoDisponible` → `.payment-status-chip--blocked`

### Scroll horizontal
- Tabla de resultados: `overflow-x-auto` + `min-w-[980px]`. Correcto para pantallas medianas.
- No tiene problemas de contenido desbordante.

### Advertencias
- Columna "Advertencias" en tabla de resultados: texto plano, escapado correctamente con `esc()`.
- `opcion.motivoNoDisponible` + `plan.advertencias` concatenados con ` · `.

### Seguridad HTML
- `esc()` protege todos los valores de servidor que se insertan via `innerHTML`.
- No hay inyección de datos sin escapar. Sin deuda de seguridad en esta función.

---

## G. Aplicabilidad de `.quote-state-badge`

### En Imprimir_tw.cshtml

**DECISIÓN: NO aplicar en esta fase.**

**Razón técnica:** `Imprimir_tw.cshtml` usa `Layout = null` con CSS inline standalone. No carga `shared-components.css`.
Para usar `.quote-state-badge` habría que cargar la hoja compartida, lo cual introduce:

1. **Riesgo de rotura print**: `shared-components.css` usa colores dark (`rgba(245,158,11,0.1)`, `#fbbf24`) diseñados para fondo oscuro. Sobre el fondo blanco de impresión, el amarillo `#fbbf24` tiene contraste insuficiente WCAG.
2. **Scope no aislado**: cargar toda la hoja compartida para solo el badge introduce dependencias innecesarias en una vista standalone.

**Estado actual**: las clases locales `.badge`, `.badge-vencida`, `.badge-convertida`, `.badge-cancelada` son correctas y print-safe. No hay regresión visual.

**Deuda**: si en el futuro se quiere uniformidad, crear una versión print-safe del token (CSS de alto contraste, bordes sólidos, sin transparencias) como `@media print` override dentro de la hoja compartida, o extraer un subset `cotizacion-print.css`.

### En Listado_tw / Detalles_tw
Ya aplicado en UX-COMERCIAL-1B. ✓

---

## H. Aplicabilidad de `.payment-status-chip`

### En cotizacion-simulador.js

**DECISIÓN: Documentar para COTIZ-3. NO aplicar en UX-1C.**

**Razón**: La función `estadoBadge()` genera los chips con clases Tailwind inline. El reemplazo por `.payment-status-chip--available`, `.payment-status-chip--blocked`, `.payment-status-chip--requires-client` es un cambio **mínimo y sin lógica** (solo renombrar clases CSS en el string de HTML del badge). Sin embargo:

- La preferencia de esta fase es no modificar JS.
- El cambio no aporta funcionalidad nueva, solo uniformidad visual con el design system.
- No hay regresión de UX visible: los chips actuales ya comunican los estados correctamente.

**Plan para COTIZ-3:**
```js
// Reemplazar estadoBadge() actual por:
function estadoBadge(estado) {
    const label = estadoLabel(estado);
    const mod = {
        Disponible: 'payment-status-chip--available',
        RequiereCliente: 'payment-status-chip--requires-client',
        RequiereEvaluacion: 'payment-status-chip--requires-client',
        BloqueadoPorProducto: 'payment-status-chip--blocked',
        NoDisponible: 'payment-status-chip--blocked'
    }[label] || 'payment-status-chip--blocked';
    return `<span class="payment-status-chip ${mod}">${esc(label)}</span>`;
}
```

Prerequisito: verificar que `shared-components.css` se carga en la vista (layout principal lo incluye).

---

## I. Aplicabilidad de `.payment-option-card`

**DECISIÓN: Documentar para COTIZ-3. NO aplica en UX-1C.**

**Razón**: Los resultados del simulador se renderizan en una tabla HTML densa (8 columnas) generada por JS.
`.payment-option-card` está diseñado para selección interactiva tipo card grid, no para tabla comparativa.

Reemplazar la tabla por cards requeriría:
1. Reestructurar `renderResultadoRow()` completamente.
2. Definir layout responsive para múltiples cards.
3. Evaluar si la columna comparativa (Cuotas, Total, Valor cuota, Recargo) sigue legible en formato card.
4. Posiblemente expandir el modelo visual para permitir comparativa side-by-side.

**Plan para COTIZ-3**: diseñar variante de layout que combine cards agrupadas por MedioPago
con planes como sub-items, manteniendo la densidad informativa actual.

---

## J. Aplicabilidad de `.total-breakdown-card`

### En Imprimir_tw.cshtml

**DECISIÓN: NO aplicar en esta fase.**

**Razón**: mismo problema que `quote-state-badge`. `total-breakdown-card` usa colores dark
(`background-color: #161c28`, `color: #94a3b8`) inapropiados para impresión en fondo blanco.
El bloque `.totals` local ya es correcto y print-safe.

### En Index_tw.cshtml (3 cards de resumen)

**DECISIÓN: NO aplicar en esta fase.**

Los 3 cards de resumen (Subtotal, Descuento, Total base) usan Tailwind con layout horizontal `grid sm:grid-cols-3`.
`total-breakdown-card` asume layout vertical (flex-col). Adaptarlos requeriría reestructurar el HTML
y no hay ganancia funcional.

**Opción futura**: si se agrega un panel lateral de totales en COTIZ-3 (panel sticky de resumen
con breakdown completo), sería el momento natural para usar `total-breakdown-card`.

---

## K. Cambios aplicados

**Ninguno.** Esta fase es de auditoría pura.

---

## L. Cambios postergados

| Token | Acción postergada | Fase objetivo |
|-------|------------------|--------------|
| `payment-status-chip` | Reemplazar clases Tailwind en `estadoBadge()` por `.payment-status-chip--*` | COTIZ-3 |
| `payment-option-card` | Rediseñar tabla de resultados como cards por medio de pago | COTIZ-3 |
| `total-breakdown-card` | Panel de totales sticky en sidebar del simulador | COTIZ-3 |
| `quote-state-badge` (print) | Versión print-safe del token o subset CSS separado | Deuda |
| `commercial-context-bar` | Header de contexto con cliente + total en simulador | COTIZ-3 o posterior |

---

## M. Riesgos y deudas

| Ítem | Riesgo | Mitigación |
|------|--------|-----------|
| CSS dark en vista print | Si se carga `shared-components.css` en Imprimir_tw sin override, los tokens dark son ilegibles en impresión. | No cargar. Mantener CSS local hasta crear subset print-safe. |
| `innerHTML` en simulador | Patrón legacy aceptable dado que `esc()` protege todos los valores. | No es riesgo activo. Documentado en UI-5L como patrón a vigilar. |
| Tabla de 980px en mobile | `min-w-[980px]` con `overflow-x-auto` es correcto pero denso en pantallas pequeñas. | Scroll horizontal funcional. En COTIZ-3 evaluar layout card para mobile. |
| Opción seleccionada solo visible via radio | No hay feedback visual de fila seleccionada en tabla (sin highlight de fila). | En COTIZ-3 agregar `tr.selected` o similar al cambiar radio. |

---

## N. Validaciones ejecutadas

- `dotnet build --configuration Release` → Compilación correcta. 0 errores, 0 advertencias.
- `git diff --check` → OK.
- `git status --short` → solo docs/ modificado.

---

## O. Tests ejecutados

- `LayoutUiContractTests` → ver sección P.
- Suite ampliada (`Layout|Shared|Navigation|Sidebar|Header|UiContract|Seguridad|Auth|Dashboard`) → ver sección P.

---

## P. Tests

Rama sin cambios funcionales. Solo documentación.
Build y LayoutUiContractTests confirmados OK (ver sección Q para resultados).

---

## Q. Playwright

Ejecutado: `npx.cmd playwright test e2e/ui-4e-layout-visual.spec.js`
Resultado esperado: 169 passed / 0 failed (confirmar en sección T del informe final).

---

## R. Próximo paso recomendado

**COTIZ-3 — Rework visual del simulador de resultados**

Alcance sugerido para COTIZ-3:
1. Aplicar `.payment-status-chip` en `estadoBadge()` de `cotizacion-simulador.js`.
2. Rediseñar la tabla de resultados hacia layout card agrupado por MedioPago con planes como sub-items.
3. Agregar highlight visual de fila seleccionada en la tabla de opciones.
4. Evaluar `.total-breakdown-card` para panel sticky de totales en sidebar.
5. Evaluar `.commercial-context-bar` para header de contexto (cliente, total, estado).
6. Definir subset CSS print-safe para `quote-state-badge` y `total-breakdown-card` en `Imprimir_tw`.
