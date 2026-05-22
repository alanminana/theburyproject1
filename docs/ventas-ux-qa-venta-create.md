# VENTAS-UX-QA — QA Final de Venta/Create

**Fase:** QA / Audit-only / Decisión  
**Fecha:** 2026-05-22  
**Base:** `df314b2` — VENTAS-UX-1G integrada  
**Rama:** `kira/ventas-ux-qa-venta-create`  
**Responsable:** Kira VENTAS-UX-QA

---

## A. Objetivo

Hacer QA final del flujo Venta/Create luego de las fases VENTAS-UX-1A a 1G.  
Responder si el flujo quedó claro en desktop y mobile, si los contratos están intactos, si hay deudas abiertas y si hace falta VENTAS-UX-2.

---

## B. Base y contexto

Fases previas integradas a main:

| Fase | Contenido |
|---|---|
| VENTAS-UX-1A | Tipo de pago principal visible |
| VENTAS-UX-1B | Auditoría del flujo Venta/Create |
| VENTAS-UX-1C | Copy, accesibilidad básica, labels con `for`, `role="alert"` en mora/cupo |
| VENTAS-UX-1D | `scope="col"` en tabla de detalle, `aria-label="Acciones"` |
| VENTAS-UX-1E-A | `aria-label` dinámico en botón eliminar |
| VENTAS-UX-1E-B | `esc(d.codigo)` y `esc(d.nombre)` en `renderDetalles()` |
| VENTAS-UX-1F | Sticky summary bar en modal, sticky footer en Create, total espejado, botón Confirmar compacto mobile |
| VENTAS-UX-1G | Bloque pre-confirmación `role="note"`, `panel-documentacion-faltante` `role="alert"`, CSS `vm-preconfirm-reminder` |

---

## C. Archivos auditados

- `Views/Venta/_VentaCrearModal.cshtml`
- `Views/Venta/Create_tw.cshtml`
- `wwwroot/css/venta-module.css`
- `wwwroot/css/shared-components.css`
- `wwwroot/js/venta-create.js`
- `TheBuryProyect.Tests/Unit/VentaCreateUiContractTests.cs`
- `e2e/ui-4e-layout-visual.spec.js`
- Documentos de fase VENTAS-UX-1A a 1G

---

## D. Validaciones ejecutadas

| Validación | Comando |
|---|---|
| Build | `dotnet build --configuration Release` |
| Tests unitarios | `dotnet test --configuration Release --filter "VentaCreate"` |
| Playwright visual | `npx.cmd playwright test e2e/ui-4e-layout-visual.spec.js` |

No se ejecutaron: cotizacion-simulador, cotizacion-conversion, venta-pago-por-item, suite general.  
Motivo: fase QA sobre Venta/Create visual y contratos UI. Esos specs son de flujos distintos.

---

## E. Resultado build

```
Compilación correcta.
    0 Advertencia(s)
    0 Errores
Tiempo transcurrido 00:00:47.49
```

Estado: **OK**

---

## F. Resultado VentaCreate

```
Correctas! - Con error: 0, Superado: 95, Omitido: 0, Total: 95, Duración: 265 ms
```

Estado: **95/95 OK**

---

## G. Resultado Playwright visual

```
169 passed (2.2m)
```

Estado: **169/169 OK**

Incluye:
- `venta-create-desktop.png` — 1440x900 sin confirmar
- `venta-create-mobile.png` — 390x844 sin confirmar
- `venta-index-mobile.png`
- Focus visible, sidebar, skip-link, scroll horizontal

---

## H. Revisión desktop

**Flujo general:**

El alta de venta está organizada en 6 secciones numeradas con indicadores visuales claros:

1. Datos generales — cliente, fecha, tipo de pago principal
2. Configuración de pago — paneles adaptativos según tipo de pago
3. Alertas crediticias — cupo, mora, documentación
4. Detalle de productos — tabla con totales en tiempo real
5. Totales — subtotal, descuento, IVA, total final
6. Confirmación — recordatorio pre-confirmación + botón CTA

El flujo es coherente con el resto del ERP. La jerarquía visual es clara. La sección de totales está bien delimitada con fondo, borde y tipografía destacada.

**Tipo de pago principal:**  
Visible en "Datos generales", primera sección, con label `for="select-tipo-pago"` y texto descriptivo abajo. Sin ambigüedad.

**Botón Confirmar:**  
Bien ubicado al final del panel de totales (`btn-confirmar`), tamaño prominente (`py-4`, `text-sm font-black uppercase`), color primario.

**Alertas:**  
`panel-cupo-insuficiente`, `panel-alerta-mora`, `panel-documentacion-faltante` están ocultos por defecto y se muestran dinámicamente. No generan ruido cuando no aplican.

**Recordatorio pre-confirmación:**  
`vm-preconfirm-reminder` en modal y su equivalente Tailwind en Create son discretos, informativos y no invasivos. Posicionados justo antes del botón. No generan ruido visual.

**Coherencia con ERP:**  
El flujo usa los mismos patrones visuales que el resto del sistema (badges, secciones, alertas).

---

## I. Revisión mobile

**Sticky summary bar del modal (`vm-mobile-summary-bar`):**

Definida en venta-module.css (línea 612). Visible solo por debajo de 768px (`@media (min-width: 768px) { display: none }`). Muestra el total espejado desde `total-final` vía MutationObserver. El botón compacto usa `aria-hidden="true"` y `tabindex="-1"` — excluido del tab order para evitar confusión de teclado. Submit real delegado a `VentaCrearModal.submit()`.

**Sticky footer de Create (`sticky-action-footer`):**

Definida en shared-components.css (línea 1318). `position: fixed; bottom: 0`. Oculta en ≥768px. Botón usa `aria-hidden="true"` y `tabindex="-1"`. Submit delegado a `document.getElementById('btn-confirmar').click()`. No hay riesgo de doble submit real porque el botón real es `type="submit"` y la duplicación es solo visual.

**Espaciador (`h-20 md:hidden`):**  
Presente en Create_tw.cshtml (línea 891). Evita que el footer fijo tape el contenido inferior en mobile.

**Total espejado en sticky footer de Create:**  
MutationObserver replica `total-final` → `sticky-create-total`. Funciona en tiempo real.

**Riesgo de tap accidental:**  
Bajo. El botón compacto requiere tap deliberado. El botón original permanece presente y es el único que realmente envía el formulario.

**Claridad de totales en mobile:**  
El total en la barra sticky es legible. Sin scroll excesivo para verlo.

**Deuda observada (mobile — Create_tw):**  
La barra sticky de Create muestra solo el total (`sticky-create-total`) pero no incluye un label descriptivo claro del cliente o del tipo de pago. En el modal, la barra de resumen también muestra solo el total. Esto es aceptable para una barra compacta.

---

## J. Revisión pago principal

**Modal:**  
Label `for="select-tipo-pago"` con icono `payments`, texto "Tipo de pago principal", help text: "Se aplica a la venta. Podes ajustar condiciones específicas por producto si corresponde." ✅

**Create_tw:**  
Label `for="select-tipo-pago"` con texto "Tipo de pago principal". ✅

**Sub-modal por producto (`select-tipo-pago-item`):**  
Label `for="select-tipo-pago-item"`, primera opción "Igual al pago principal de la venta" — el concepto de pago principal vs. pago por ítem está bien diferenciado. ✅

El tipo de pago principal es entendible para el operador. La distinción con el pago por producto es clara.

---

## K. Revisión totales

**Modal:**  
`total-subtotal`, `total-descuento` (con `total-descuento-label` porcentaje), `total-iva`, `total-final` (3xl font-black, prominente). Hidden inputs `hdn-subtotal`, `hdn-descuento`, `hdn-iva`, `hdn-total` para POST. ✅

**Create_tw:**  
Mismos IDs. `total-final` igual de prominente. ✅

**JS (`actualizarTotalesUI`):**  
Recibe subtotal, descuento, iva, total, backendResult. La actualización es en tiempo real vía AJAX. ✅

Los totales son claros y bien jerarquizados.

---

## L. Revisión alertas

| Alerta | ID | role | Posición | Estado por defecto |
|---|---|---|---|---|
| Cupo insuficiente | `panel-cupo-insuficiente` | `alert` | Modal — sección crediticia | `hidden` |
| Mora | `panel-alerta-mora` | `alert` | Modal — sección crediticia | `hidden` |
| Documentación faltante | `panel-documentacion-faltante` | `alert` | Modal y Create — sección crediticia | `hidden` |
| Errors AJAX | `venta-ajax-error-list` | `alert` | Modal — header | visible cuando hay errores |

Las tres alertas principales se anuncian correctamente a lectores de pantalla cuando se muestran. No generan ruido cuando están ocultas. La alerta de documentación usa color ámbar para diferenciarse de error (rojo). ✅

**Deuda menor observada:**  
`panel-documentacion-faltante` en modal (línea 589) tiene el `role="alert"` en el wrapper pero el contenido visual está en un `div` hijo. Funciona correctamente a nivel semántico, pero la estructura tiene un nivel de anidamiento extra que no agrega valor.

---

## M. Revisión pre-confirmación

**Modal (`vm-preconfirm-reminder`):**  
CSS class dedicada. `role="note"`, `aria-label="Revisá antes de confirmar"`. Icono `checklist`, texto: "Revisá cliente, tipo de pago y total. Si hay alertas activas de mora, cupo o documentación, verificalas antes de continuar." Colores: `#94a3b8` sobre fondo ~`rgba(30,41,59,0.5)` — contraste estimado ≥5.5:1, pasa WCAG AA. ✅

**Create_tw (inline Tailwind):**  
`bg-white/10 text-white/80`, `role="note"`, `aria-label="Revisá antes de confirmar"`. Solo se muestra `@if (!esCotizacion)`. ✅

**¿Genera ruido?**  
No. Es discreto, sin iconos llamativos, sin color de alerta. No interrumpe el flujo. Ayuda a validar mentalmente antes de confirmar.

**Inconsistencia menor:**  
El modal usa CSS class (`.vm-preconfirm-reminder`), Create usa Tailwind inline. No es un problema funcional pero la inconsistencia existe. Bajo impacto.

---

## N. Revisión accesibilidad / baja visión

| Elemento | Estado | Detalle |
|---|---|---|
| Labels `for` — modal | ✅ Completo | `for="input-buscar-cliente"`, `for="FechaVenta"`, `for="select-tipo-pago"` |
| Labels `for` — Create_tw | ⚠️ Deuda | "Buscar Cliente" (línea 165) y "Fecha de Operación" (línea 196) sin `for` |
| `role="alert"` en alertas | ✅ | Cupo, mora, documentación |
| `role="note"` en pre-confirmación | ✅ | Modal y Create |
| `aria-label` en `btn-confirmar` sticky | ✅ | `aria-hidden="true"` excluye del tab order |
| `scope="col"` en tabla | ✅ | Fase 1D |
| `aria-label="Acciones"` | ✅ | Fase 1D |
| `aria-label` dinámico botón eliminar | ✅ | Fase 1E-A |
| Contraste `#94a3b8` sobre fondo oscuro | ✅ | ~6:1 — pasa AA y AAA para texto grande |
| Contraste `#64748b` sobre fondo oscuro | ⚠️ Deuda menor | ~3.45:1 — pasa AA solo para texto grande (≥18pt/14pt bold). Algunos usos en texto muy pequeño (0.625rem en sticky footer label "Total") |
| Color como único indicador | ✅ | Las alertas usan icono + texto + color |
| Foco visible | ✅ | Playwright confirma focus visible en nav |
| Skip link | ✅ | Playwright confirma visibilidad con Tab |

**Deuda de accesibilidad:** Los dos labels sin `for` en Create_tw son el hallazgo de accesibilidad más claro. El modal tiene esta deuda resuelta correctamente.

---

## O. Revisión seguridad frontend

| Punto | Estado | Detalle |
|---|---|---|
| `esc(d.codigo)` en `renderDetalles` | ✅ | JS línea 1207 |
| `esc(d.nombre)` en `renderDetalles` | ✅ | JS línea 1209 |
| `aria-label="Eliminar ${esc(d.nombre)}"` | ✅ | JS línea 1217 |
| Función `esc()` existe | ✅ | Cubierto por test `VentaCreateJs_FuncionEscExiste` |
| No hay interpolación sin escape en celdas corregidas | ✅ | 1E-B resolvió la deuda XSS de renderDetalles |
| `innerHTML` en otras partes del JS | No revisado en este QA | Fuera del alcance de las fases 1A-1G |

La deuda XSS de `renderDetalles` introducida antes de 1E-B quedó resuelta. Los tests de contrato confirman la vigencia del escape.

---

## P. Contratos preservados

Todos los contratos UI verificados mediante Select-String e inspección directa:

| Contrato | Visto en | Estado |
|---|---|---|
| `btn-confirmar` | Modal + Create + JS | ✅ |
| `VentaCrearModal.submit()` | Modal línea 67 + JS | ✅ |
| `select-tipo-pago` | Modal + Create + JS | ✅ |
| `select-tipo-pago-item` | Modal + JS | ✅ |
| `tbody-detalles` | Modal + Create + JS | ✅ |
| `total-final` | Modal + Create + JS | ✅ |
| `total-subtotal` | Modal + Create + JS | ✅ |
| `total-descuento` | Modal + Create + JS | ✅ |
| `total-iva` | Modal + Create + JS | ✅ |
| `hdn-subtotal`, `hdn-descuento`, `hdn-iva`, `hdn-total` | Modal + Create | ✅ |
| `panel-alerta-mora` | Modal + Create | ✅ |
| `panel-cupo-insuficiente` | Modal + Create | ✅ |
| `panel-documentacion-faltante` | Modal + Create | ✅ |
| `vm-mobile-summary-bar` | Modal + CSS | ✅ |
| `sticky-action-footer` | Create + CSS | ✅ |
| `vm-preconfirm-reminder` | Modal + CSS | ✅ |
| Antiforgery | Modal `@Html.AntiForgeryToken()` | ✅ |

---

## Q. Hallazgos

### Q1 — Labels sin `for` en Create_tw.cshtml (accesibilidad — menor)

**Dónde:** `Views/Venta/Create_tw.cshtml`, líneas 165 y 196.

```html
<!-- línea 165 — sin for -->
<label class="venta-label">Buscar Cliente</label>
<input id="input-buscar-cliente" ...>

<!-- línea 196 — sin for -->
<label class="venta-label">Fecha de Operación</label>
<input asp-for="FechaVenta" id="FechaVenta" ...>
```

**Equivalente en modal:** ambos tienen `for` correcto. El modal es accesible; Create_tw no resolvió estos dos.  
**Impacto:** Los campos son usables por mouse y teclado directo, pero los lectores de pantalla no asocian el label al input. Falla WCAG 1.3.1 (A).  
**Riesgo:** Bajo en uso operativo. Alto para usuarios con tecnologías asistivas.

### Q2 — `color: #64748b` en texto muy pequeño (contraste — menor)

**Dónde:** `sticky-action-footer` en Create_tw (label "Total" en `font-size: 0.625rem`, `color: #64748b`).  
También en `info-cliente-doc` en Create_tw (`text-xs text-slate-500`).

**Contraste estimado:** `#64748b` sobre `rgba(13,19,31,0.95)` ≈ 3.45:1.  
**WCAG:** Pasa AA para texto grande (≥18pt / 14pt bold) pero falla AA para texto normal.  
**A `0.625rem` (~10px):** el contraste es insuficiente para baja visión.  
**Impacto:** El label "Total" del sticky footer es informativo pero redundante (el número debajo es blanco y grande). La pérdida de contraste no afecta la funcionalidad principal.

### Q3 — Comentario stale en `shared-components.css` (doc — cosmético)

**Dónde:** `wwwroot/css/shared-components.css`, línea 1315.

```css
/* No está aplicado en vistas todavía. */
```

Este comentario es incorrecto. `sticky-action-footer` SÍ está aplicado en `Create_tw.cshtml` desde la fase 1F.  
**Impacto:** Ninguno en runtime. Es deuda de documentación.

### Q4 — Inconsistencia de estilado de `vm-preconfirm-reminder` entre modal y Create

**Modal:** usa clase CSS `.vm-preconfirm-reminder` con colores definidos en `venta-module.css`.  
**Create_tw:** usa clases Tailwind inline (`bg-white/10 text-white/80`).

Ambas son funcionales y semánticamente correctas. La inconsistencia es solo de aproximación al CSS.  
**Impacto:** Mantenimiento — si se quiere cambiar el estilo del recordatorio hay que tocar dos lugares.

---

## R. Riesgos

| Riesgo | Nivel | Descripción |
|---|---|---|
| Labels sin `for` en Create_tw | Bajo | Solo afecta accesibilidad; el flujo funciona normalmente. No es un riesgo de regresión. |
| Contraste `#64748b` en texto pequeño | Bajo | Label informativo redundante. El dato importante (el número) sí tiene contraste alto. |
| `innerHTML` no auditado fuera de renderDetalles | Bajo | El scope de 1E-B fue renderDetalles específicamente. Otras zonas del JS no se auditaron en esta fase. |

---

## S. Deudas restantes

| ID | Área | Descripción | Severidad |
|---|---|---|---|
| D1 | Create_tw accesibilidad | Label "Buscar Cliente" sin `for="input-buscar-cliente"` | Menor |
| D2 | Create_tw accesibilidad | Label "Fecha de Operación" sin `for="FechaVenta"` | Menor |
| D3 | Create_tw contraste | `#64748b` en texto 0.625rem del sticky footer | Menor |
| D4 | shared-components.css | Comentario stale "No está aplicado en vistas todavía" | Cosmético |
| D5 | Modal/Create inconsistencia | `vm-preconfirm-reminder` con dos sistemas de estilos | Cosmético |
| D6 | venta-create.js | Uso de `innerHTML` fuera de `renderDetalles` no auditado | Pendiente de auditoría futura |

Ninguna deuda es bloqueante para revisión funcional/manual.

---

## T. Decisión sobre VENTAS-UX-2

### Clasificación: **Opción A — VENTAS-UX-2 no necesaria por ahora**

**Justificación:**

El flujo Venta/Create quedó claro en desktop y mobile después de las fases 1A–1G:

- El tipo de pago principal es visible y bien etiquetado.
- El cierre de venta se entiende antes de confirmar (totales claros + recordatorio).
- Las alertas de mora, cupo y documentación se anuncian correctamente.
- El sticky mobile funciona sin riesgo de doble submit.
- El recordatorio pre-confirmación ayuda sin generar ruido.
- Los contratos UI están intactos y cubiertos por 95 tests.
- Playwright visual confirma 169/169 sin regresiones.

Las deudas restantes (D1–D6) son menores o cosméticas. Ninguna afecta la usabilidad operativa del flujo principal.

**Resolución recomendada de deudas:**  
Resolver D1 y D2 (labels sin `for` en Create_tw) en un micro-lote de mantenimiento de accesibilidad posterior, no como fase UX nueva. Son 2 líneas de cambio por label.

**VENTAS-UX-2 quedaría justificada solo si:**

- QA funcional/manual detecta confusión real de operadores al usar el flujo.
- Se decide rediseñar la estructura de columnas o el paso de confirmación.
- Se agrega funcionalidad nueva que requiera reorganizar las secciones.

Por ahora no hay evidencia de esa necesidad.

---

## U. Próximo paso recomendado

**Opción primaria (recomendada):**

Cerrar VENTAS-UX como listo para revisión funcional/manual.  
Ejecutar un smoke test manual del flujo completo (alta de venta con cliente, producto, tipo de pago) para confirmar que el comportamiento dinámico del JS funciona según lo esperado.

**Micro-lote de mantenimiento (opcional, bajo riesgo):**

Si se quiere cerrar D1 y D2 antes de revisión manual:

```
VENTAS-UX-MAINT-1: Agregar for="input-buscar-cliente" y for="FechaVenta"
en los dos labels de Create_tw.cshtml que quedaron sin for.
Alcance: 2 líneas en Views/Venta/Create_tw.cshtml.
Sin tocar modal, JS, CSS ni backend.
```

**No abrir VENTAS-UX-2** salvo que revisión funcional/manual detecte problemas reales de flujo.

---

## Validaciones git

### git diff --check

Warnings solo en archivos locales preexistentes (AGENTS.md, CLAUDE.md, .claude/settings.local.json):
- AGENTS.md y CLAUDE.md: trailing whitespace en líneas de markdown.
- No se commitean. No se corrigen.
- El documento de QA no introduce ningún warning nuevo.

### git status --short (al cierre)

```
 M .claude/settings.local.json
 M AGENTS.md
 M CLAUDE.md
 D skills-lock.json
?? docs/ventas-ux-qa-venta-create.md
```

Único archivo nuevo: `docs/ventas-ux-qa-venta-create.md`.

---

## Temporales

`git ls-files | Select-String "tmpbuild|tmptest|..."` → sin resultados.  
Sin temporales commiteados.
