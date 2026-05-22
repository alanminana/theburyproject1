# VENTAS-UX-1B — Auditoría UX del flujo Venta/Create

**Fase:** VENTAS-UX-1B  
**Agente:** Kira VENTAS-UX-1B  
**Fecha:** 2026-05-22  
**Base:** e2a7db9 (VENTAS-UX-1A integrada, main)  
**Rama:** kira/ventas-ux-1b-auditoria-flujo-venta-create  

---

## A. Objetivo

Auditar el flujo completo de creación de venta después de VENTAS-UX-1A. Detectar problemas de UX/UI, accesibilidad, mobile y riesgos funcionales. Definir un plan de mejoras incrementales seguras para las fases siguientes.

No se realizaron cambios en producción. Esta fase es exclusivamente de análisis y documentación.

---

## B. Estado inicial

VENTAS-UX-1A entregó:

- `select-tipo-pago` visible en `_VentaCrearModal.cshtml`
- Label "Tipo de pago principal" con ícono `payments`
- Texto de ayuda permanente bajo el select
- Copy de paso 1 actualizado
- Copy del sub-modal `modal-pago-item` actualizado
- 5 tests de contrato en `VentaCreateUiContractTests.cs`

Suites al momento de iniciar:

- VentaCreate 60/60 OK
- LayoutUiContractTests 57/57 OK
- Cotización 170/170 OK
- Suite general 235/235 OK
- Playwright visual 169/169 OK

---

## C. Archivos auditados

| Archivo | Tamaño | Rol |
|---|---|---|
| `Views/Venta/_VentaCrearModal.cshtml` | 68 KB | Modal principal (nueva venta desde Index) |
| `Views/Venta/Create_tw.cshtml` | 65 KB | Página standalone de nueva venta |
| `wwwroot/js/venta-create.js` | 106 KB | Lógica JS compartida por ambas vistas |
| `wwwroot/css/venta-module.css` | 19 KB | Estilos del módulo venta |
| `TheBuryProyect.Tests/Unit/VentaCreateUiContractTests.cs` | — | Tests de contrato UI |
| `e2e/venta-pago-por-item.spec.js` | 23 KB | Tests E2E del pago por ítem (mayoría skipped) |

---

## D. Mapa del flujo actual

### Dos puntos de entrada para crear una venta

```
1. Index → btn "Nueva Venta" → modal overlay (#modal-crear-venta, z-50)
           Usa: _VentaCrearModal.cshtml + venta-create.js

2. /Venta/Create (GET) → página standalone
           Usa: Create_tw.cshtml + venta-create.js
```

Ambas rutas comparten `venta-create.js` como script único. Las diferencias estructurales entre las dos vistas son fuente de inconsistencias.

### Pasos del modal (_VentaCrearModal.cshtml)

```
§ 1  Datos generales      — cliente, fecha, tipo de pago principal
§ 2  Selección de productos — búsqueda, filtros, tabla de ítems
§ 3  Detalle de cobro     — paneles dinámicos según tipo de pago
§ 4  Verificación crediticia — solo visible con Crédito Personal
§ 5  Vendedor y observaciones — trazabilidad interna
§ 6  Totales y confirmación  — columna derecha (side panel)
```

Sub-modales anidados:
- `#modal-pago-item` (z-70) — tipo de pago por ítem
- `#modal-documentacion` (z-60) — documentación crediticia

### Pasos de Create_tw.cshtml (standalone)

```
Hero section    — métricas: Cliente / Detalle / Total estimado / Cobro
§ 1  Datos generales      — cliente, fecha, tipo de pago
§ 2  Selección de productos — búsqueda, filtros, tabla
§ 3  Detalle de cobro     — paneles dinámicos
§ 4  Verificación crediticia — solo con Crédito Personal
§ 5  Vendedor, documentación, observaciones — columna derecha
§ 6  Totales y confirmación
```

---

## E. Problemas UX detectados

### E-1. Asimetría de labels entre las dos vistas

**Modal:** Label `"Tipo de pago principal"` con texto de ayuda permanente:
> "Se aplica a la venta. Podes ajustar condiciones especificas por producto si corresponde."

**Create_tw:** Label `"Tipo de pago"` (sin "principal") con texto de estado dinámico JS:
> "Cargando configuracion global de pagos..."

El label de Create_tw no deja claro que ese campo gobierna toda la venta. El texto "Cargando..." desaparece cuando el JS carga la configuración, pero no hay texto permanente de ayuda como en el modal.

**Riesgo:** Bajo. Solo copy.

### E-2. Opción default del sub-modal de pago por ítem confusa

`#select-tipo-pago-item` tiene como primera opción:
```
<option value="">Tipo predeterminado del sistema</option>
```

El usuario no entiende si "predeterminado del sistema" significa:
1. el tipo de pago principal de **esta venta**
2. un tipo configurado globalmente en el sistema

La segunda opción del footer del modal lo aclara:
> "Si no cambias nada, se usara el tipo de pago principal de la venta."

Pero la etiqueta de la opción no lo dice directamente.

**Riesgo:** Bajo. Solo copy. Cambiar a "Igual al pago principal de la venta" clarifica sin afectar lógica.

### E-3. Hero metric "Cobro" poco prominente

En Create_tw, el hero metric de cobro muestra:
```
"Cobro"
"Sin definir"
"Selecciona el medio principal de cobro para toda la venta."
```

El texto "Sin definir" en blanco sobre fondo oscuro tiene bajo contraste y baja jerarquía visual. El usuario puede ignorarlo y avanzar a §2 sin definir el tipo de pago.

En el modal, el equivalente es `#hero-tipo-pago` actualizado por JS — mismo problema, misma jerarquía visual baja.

**Riesgo:** Bajo. Ajuste visual del placeholder.

### E-4. El tipo de pago no aparece en el resumen de totales

El bloque § 6 (Totales y confirmación) muestra:
```
Subtotal Neto       $X
Descuento (0%)      -$0
IVA total           $X
──────────────────
TOTAL FINAL         $X
```

No incluye:
- Qué tipo de pago se está usando
- Si hay recargo por débito o cuotas
- El desglose de cuotas si es tarjeta

El panel `#panel-tarjeta-resumen` (Monto/cuota, Total con interés, Recargo) está dentro de §3 (Detalle de cobro), separado del bloque de totales. El usuario tiene que subir visualmente para ver ese desglose.

**Riesgo:** Medio. Mostrar tipo de pago en §6 es copy/readonly (bajo riesgo). Integrar el total con recargo en §6 requiere coordinar con la lógica JS (medio riesgo).

### E-5. Sin estado de validación pre-confirmación

El botón "Confirmar Transacción" no da feedback sobre el estado actual antes de ser presionado:
- ¿Se seleccionó un cliente?
- ¿Hay al menos un producto?
- ¿Está el tipo de pago definido?
- ¿Si es crédito, se verificó elegibilidad?

Solo bloquea el botón cuando hay bloqueo por diagnóstico crediticio. Para los otros casos, el usuario presiona el botón y recibe el error después (en la parte superior del modal, posiblemente fuera del viewport si scrolleó).

**Riesgo:** Medio. Requiere lógica JS adicional para estados.

### E-6. Los pasos están numerados con gaps contextuales

Los pasos § 1 al § 5 tienen números visibles. El §4 (verificación crediticia) solo aparece con Crédito Personal. Cuando no aplica, el usuario ve §1 → §2 → §3 → §5 sin §4.

No hay indicación de que §4 fue omitido intencionalmente. Un usuario que nunca usó crédito personal puede confundirse con la numeración discontinua.

**Riesgo:** Bajo. Solo visual/copy.

### E-7. Scroll del error de validación fuera del viewport

`#venta-ajax-validation-summary` en el modal está en la parte superior del formulario (debajo del header). Si el usuario scrolleó hasta §3 o §4 para completar datos de pago y luego hace click en "Confirmar", el error aparece arriba — fuera del viewport.

El `aria-live="polite"` de `#venta-create-feedback-slot` es un slot de feedback más contextual, pero solo existe dentro del panel de verificación crediticia (§4), no como feedback global.

**Riesgo:** Bajo para arreglar (scroll-to-error o toast sticky). Medio si se intenta refactorizar la validación JS.

---

## F. Problemas visuales detectados

### F-1. Panel tarjeta-resumen separado del bloque de totales

El resumen de tarjeta (cuotas, interés, recargo) vive en §3, pero el total final vive en §6. El usuario debe mirar dos lugares para entender el costo real de la operación con tarjeta.

### F-2. El total no refleja visualmente el recargo incluido

Cuando hay recargo por débito, `#total-final` muestra el total con recargo, pero no hay indicación visual de que el recargo está incluido en ese número. El label solo dice "TOTAL FINAL" sin aclaración.

### F-3. Texto "Tiempo real" en §6 no diferencia la situación sin productos

El badge "Tiempo real" en §6 del modal está siempre visible, incluso cuando no hay productos y el total es $0,00. No comunica el estado "aún no hay datos para calcular".

### F-4. Estados de paneles dinámicos sin transición visible

Cuando el usuario cambia el tipo de pago, el panel de cobro cambia (aparece/desaparece panel-tarjeta, panel-cheque, panel-credito-personal) sin animación. En pantallas grandes no es problema, pero en mobile puede confundir si el scroll cambia abruptamente.

---

## G. Problemas mobile

### G-1. Botón de confirmación al final del scroll

En mobile, el layout de 3 columnas colapsa a 1 columna. El orden DOM es:
```
§1 → §2 → §3 → §4 (si crédito) → [columna derecha: §5 + §6]
```

Esto significa que en mobile, el botón "Confirmar Transacción" queda después de §5 (vendedor y observaciones), al final de una página muy larga. El usuario tiene que hacer scroll muy largo para confirmar.

### G-2. Panel de verificación crediticia rompe el flujo lineal en mobile

En desktop, §4 (verificación crediticia) está en la columna derecha, visible junto a §1-§3. En mobile, aparece después de §3 en el flujo linear. Esto puede confundir al usuario que espera continuar con §5.

### G-3. Tabla de ítems — hint de scroll muy pequeño

El hint "Deslizá la tabla para revisar cantidades, descuentos y subtotales completos." está en texto xs/slate-600 y queda oculto visualmente en mobile (bajo contraste). El scroll horizontal existe pero no es evidente.

### G-4. Filtros de búsqueda de productos — grid de 4 columnas en mobile

La sección de filtros avanzados (`grid md:grid-cols-4`) colapsa a 1 columna en mobile, lo que hace la sección muy larga. No es crítico pero agrega scroll innecesario.

### G-5. Modal-pago-item tiene max-w-md bien en mobile

El sub-modal de pago por ítem tiene `max-w-md` y padding apropiado. Funciona en pantallas pequeñas. No es un problema.

---

## H. Problemas de accesibilidad

### H-1. Labels sin asociación `for` en modal-pago-item

```html
<!-- Actual — sin for -->
<label class="vm-label">Tipo de pago para este producto</label>
<select id="select-tipo-pago-item" ...>

<!-- Actual — sin for -->
<label class="vm-label">Plan de pago</label>
<div id="modal-pago-item-planes" ...>
```

El `<select id="select-tipo-pago-item">` no tiene un `<label for="select-tipo-pago-item">`. Un lector de pantalla no puede asociar el label con el campo.

**Riesgo:** Bajo. Agregar `for="select-tipo-pago-item"` al label es seguro.

### H-2. Labels sin `for` en §3 de Create_tw

```html
<label class="block text-xs ...">Tarjeta</label>
<select id="select-tarjeta" ...>

<label class="block text-xs ...">Cuotas</label>
<select id="select-cuotas-tarjeta" ...>

<label class="block text-xs ...">N° Autorización</label>
<input id="txt-num-autorizacion-tarjeta" ...>
```

En Create_tw.cshtml, los labels del panel de tarjeta en §3 no tienen atributo `for`. Los mismos campos en el modal (`_VentaCrearModal.cshtml`) sí tienen `<label class="vm-label" for="...">`.

**Riesgo:** Bajo. Solo agregar `for` al atributo.

### H-3. Panel-alerta-mora sin `role="alert"`

```html
<div id="panel-alerta-mora" class="hidden rounded-xl border border-red-500/30 bg-red-500/10 p-4">
```

Es un aviso de riesgo financiero importante. No tiene `role="alert"` ni `aria-live`. Un lector de pantalla no lo anunciaría al aparecer.

**Riesgo:** Bajo. Solo agregar atributo ARIA.

### H-4. Color como único diferenciador en panel-cupo

El estado del cupo disponible vs. insuficiente se comunica por color (emerald vs red) y texto. Sin `role="alert"` en `#panel-cupo-insuficiente`, un lector de pantalla puede no anunciarlo.

### H-5. Focus management del modal-pago-item no verificado

Al abrir `#modal-pago-item`, no está documentado que el foco se mueva al primer elemento interactivo del sub-modal. Si el foco queda en el elemento que disparó la apertura, la navegación por teclado es incorrecta.

### H-6. Botón de confirmación en modal — onclick inline

```html
<button type="button" id="btn-confirmar" onclick="VentaCrearModal.submit()">
```

El evento está inline. No es problema de accesibilidad estricto, pero es inconsistente con el patrón de event listeners del resto del JS.

---

## I. Riesgos funcionales

### I-1. Cambios en `select-tipo-pago` pueden romper el JS

`venta-create.js` referencia `$('#select-tipo-pago')` en múltiples lugares. Cualquier cambio de `id`, `name`, o posición en el DOM puede silenciosamente desactivar:
- `onTipoPagoChange()` — paneles dinámicos
- `recalcularTotales()` — envía `tipoPago` al backend
- `actualizarResumenOperacion()` — hero metric
- `aplicarLimiteCuotasTarjeta()` — filtro de cuotas

**Conclusión:** No cambiar el atributo `id="select-tipo-pago"` ni su `name="TipoPago"`.

### I-2. Cambios en `select-tipo-pago-item` pueden romper pago por ítem

El sub-modal de pago por ítem tiene lógica en JS que lee el valor del select para:
- Cargar planes dinámicos del item
- Guardar el pago por ítem en el estado de detalles
- Enviar el payload al backend (si pago por ítem está activo)

**Conclusión:** No cambiar `id="select-tipo-pago-item"`, solo el texto de la option default.

### I-3. Cambios en la lista de opciones del select-tipo-pago-item

El select tiene opciones hardcodeadas (0-8). Si se cambian valores numéricos, se rompe el mapping con el enum `TipoPago.cs` del backend.

**Conclusión:** Solo cambiar texto de la opción `value=""`, nunca los valores numéricos.

### I-4. Cambiar la estructura visual de §6 puede desincronizar totales

`actualizarTotalesUI()` en JS actualiza por ID: `#total-subtotal`, `#total-descuento`, `#total-iva`, `#total-final`, `#tarjeta-monto-cuota`, `#tarjeta-total-interes`, `#tarjeta-recargo`. Cualquier cambio que renombre o elimine esos IDs rompe el cálculo en tiempo real.

**Conclusión:** Al agregar elementos al bloque de totales, solo agregar — no renombrar IDs existentes.

### I-5. Cambios en el orden de secciones en mobile pueden afectar tab order

El tab order en HTML sigue el orden del DOM. Si se reordena §4 (verificación crediticia) para que quede antes de §3 en mobile, el tab order cambia globalmente en desktop también.

**Conclusión:** Usar CSS order (Flexbox/Grid) para reordenar en mobile sin tocar el DOM.

---

## J. Recomendaciones

### J-1. Armonizar label de tipo de pago en Create_tw (seguro, bajo riesgo)

Cambiar en Create_tw.cshtml:
```
ACTUAL:  <label class="venta-label">Tipo de pago</label>
PROPUESTA: <label class="venta-label" for="select-tipo-pago">Tipo de pago principal</label>
```
Y agregar texto de ayuda permanente equivalente al del modal.

### J-2. Mejorar copy opción default de select-tipo-pago-item (seguro, bajo riesgo)

```
ACTUAL:  <option value="">Tipo predeterminado del sistema</option>
PROPUESTA: <option value="">Igual al pago principal de la venta</option>
```

No cambia el `value=""`, solo el texto visible. El JS usa el `value` para la lógica.

### J-3. Agregar `for` a labels de modal-pago-item (seguro, bajo riesgo)

```
ACTUAL:  <label class="vm-label">Tipo de pago para este producto</label>
PROPUESTA: <label class="vm-label" for="select-tipo-pago-item">Tipo de pago para este producto</label>
```

### J-4. Agregar `for` a labels de §3 en Create_tw (seguro, bajo riesgo)

Los labels de tarjeta, cuotas y N° autorización en Create_tw no tienen `for`. Agregarlos.

### J-5. Agregar `role="alert"` a panel-alerta-mora (seguro, bajo riesgo)

```
ACTUAL:  <div id="panel-alerta-mora" class="hidden ...">
PROPUESTA: <div id="panel-alerta-mora" class="hidden ..." role="alert">
```

### J-6. Mostrar tipo de pago seleccionado en el resumen §6 (riesgo medio)

Agregar una línea readonly al bloque de totales:
```
Medio de cobro      [Tipo de pago actual]
```
Actualizada por JS al cambiar el select. No toca lógica de cálculo.

### J-7. Indicador visual pre-confirmación (riesgo medio, fase propia)

Agregar un bloque de estado antes del botón de confirmar que indique:
- Cliente seleccionado: Sí/No
- Productos agregados: N
- Tipo de pago: definido/sin definir
Implementar como check en JS sobre estado del formulario antes de mostrar el botón activo.

### J-8. Scroll-to-error al confirmar (riesgo bajo, fase propia)

Cuando hay errores de validación, hacer scroll automático a `#venta-ajax-validation-summary`.

---

## K. Priorización por fases

### VENTAS-UX-1C — Micro-ajustes de copy y accesibilidad (bajo riesgo)

Archivos a tocar: solo `_VentaCrearModal.cshtml`, `Create_tw.cshtml`

| # | Cambio | Archivo | Tipo |
|---|---|---|---|
| 1 | Label "Tipo de pago" → "Tipo de pago principal" | Create_tw | copy |
| 2 | Agregar texto de ayuda permanente al select de tipo de pago | Create_tw | copy |
| 3 | Opción default modal-pago-item: "Tipo predeterminado del sistema" → "Igual al pago principal de la venta" | _VentaCrearModal | copy |
| 4 | `for="select-tipo-pago-item"` en label de modal-pago-item | _VentaCrearModal | accesibilidad |
| 5 | `for` en labels de §3 de Create_tw (tarjeta, cuotas, autorización) | Create_tw | accesibilidad |
| 6 | `role="alert"` en panel-alerta-mora | _VentaCrearModal + Create_tw | accesibilidad |
| 7 | Tests de contrato para los cambios anteriores | VentaCreateUiContractTests.cs | tests |

No tocar: venta-create.js, CSS, backend, endpoints.

### VENTAS-UX-1D — Mejoras de resumen y validación pre-confirm (riesgo medio)

| # | Cambio | Tipo |
|---|---|---|
| 1 | Mostrar tipo de pago en §6 (bloque totales) | JS + HTML |
| 2 | Scroll-to-error en submit fallido | JS |
| 3 | Indicador visual pre-confirmación | JS + HTML |
| 4 | Tests de contrato y E2E para nuevos estados | tests |

### VENTAS-UX-1E — Mobile y experiencia de cobro (riesgo medio-alto)

| # | Cambio | Tipo |
|---|---|---|
| 1 | Botón de confirmación sticky en mobile | CSS |
| 2 | Reordenar paneles en mobile con CSS (no DOM) | CSS |
| 3 | Indicador de scroll horizontal más visible en tabla de ítems | HTML/CSS |
| 4 | Transición suave al cambiar tipo de pago | CSS |

---

## L. Qué NO tocar

- `venta-create.js` — 2300+ líneas, lógica de cálculo sensible, IDs acoplados al DOM
- `id="select-tipo-pago"` y `name="TipoPago"` — usados en JS y payload
- `id="select-tipo-pago-item"` — usado en JS del sub-modal
- Valores numéricos de las opciones del select de tipo de pago
- Cualquier `id` referenciado en venta-create.js para actualizarTotalesUI
- Endpoints de backend
- Entidades, ViewModels, DTOs
- Lógica de crédito, caja y stock
- Cotización y conversión Cotización → Venta
- `Program.cs`, controllers, services, migrations

---

## M. Propuesta VENTAS-UX-1C

### Descripción

Implementar los micro-ajustes de bajo riesgo identificados en §K: armonizar labels, mejorar copy del sub-modal de pago por ítem, añadir atributos ARIA faltantes, y agregar tests de contrato que cubran los cambios.

### Cambios concretos

1. **Create_tw.cshtml** — Label "Tipo de pago" → "Tipo de pago principal" con `for` y texto de ayuda
2. **_VentaCrearModal.cshtml** — Opción default del select-tipo-pago-item
3. **_VentaCrearModal.cshtml** — `for="select-tipo-pago-item"` en label
4. **Create_tw.cshtml** — `for` en labels del panel de tarjeta
5. **_VentaCrearModal.cshtml + Create_tw.cshtml** — `role="alert"` en panel-alerta-mora
6. **VentaCreateUiContractTests.cs** — Tests de contrato para todos los cambios

### Validaciones esperadas

```
dotnet build --configuration Release
dotnet test --configuration Release --filter "VentaCreate"
dotnet test --configuration Release --filter "LayoutUiContractTests"
npx.cmd playwright test e2e/ui-4e-layout-visual.spec.js
npx.cmd playwright test e2e/venta-pago-por-item.spec.js
```

---

## N. Validaciones ejecutadas en VENTAS-UX-1B

Esta fase solo creó documentación. No se tocó código de producción.

```
git diff --check   → OK (sin conflictos de espaciado)
git status --short → solo el nuevo doc y archivos locales no commiteables
```

No se ejecutó build ni tests por no haber cambios en código fuente.

---

## O. Procesos

No se iniciaron procesos pesados en esta tarea.

- TheBuryProyect.exe: no iniciado por esta tarea
- dotnet build/test: no ejecutados (solo documentación)
- Playwright: no ejecutado
- node: no iniciado

---

## P. Deudas

| Deuda | Alcance | Riesgo |
|---|---|---|
| Focus management modal-pago-item | Verificar que al abrir el sub-modal el foco va al primer elemento | Bajo |
| Consistencia de role="alert" en panel-cupo-insuficiente | Similar a panel-alerta-mora | Bajo |
| Resumen de cuotas integrado en §6 | Requiere coordinar con JS de totales | Medio |
| E2E del flujo completo del modal (T1 en venta-pago-por-item.spec.js) | La mayoría de tests están en skip por falta de datos de prueba | Alto |
| Consolidación de diferencias entre modal y Create_tw | Las dos vistas divergen en labels y helpers; se duplica la deuda por cada mejora | Medio |
