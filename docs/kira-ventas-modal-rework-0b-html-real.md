# KIRA-VENTAS-MODAL-REWORK-0B — Auditoría con HTML real

**Tipo:** Auditoría complementaria / doc-only  
**Fase:** KIRA-VENTAS-MODAL-REWORK-0B  
**Fecha:** 2026-05-24  
**Estado:** CERRADA — base de planificación para fase 1A

---

## A. Objetivo

Completar la auditoría comparativa iniciada en `docs/kira-ventas-modal-rework-0-auditoria.md`
usando el HTML real del nuevo diseño provisto por el usuario. Mapear:

- IDs coincidentes y divergentes
- CSS inline a extraer
- JS inline detectado y destino
- Names de formulario con cambios peligrosos
- Contratos que deben preservarse
- Cambio arquitectónico fundamental: modal panel → fullscreen window
- Elementos nuevos que requieren integración JS adicional
- Elementos del sistema actual ausentes en el nuevo diseño
- Roadmap corregido para 1A

---

## B. Base y contexto

- **Rama base:** `main` — commit `8d1d70e` (KIRA-VENTAS-MODAL-REWORK-0 integrada)
- **Modal actual canónico:** `Views/Venta/_VentaCrearModal.cshtml` (929 líneas, Razor completo)
- **JS productivo:** `wwwroot/js/venta-create.js` (2343 líneas)
- **CSS productivo:** `wwwroot/css/venta-module.css`
- **Auditoría base:** `docs/kira-ventas-modal-rework-0-auditoria.md`
- **HTML nuevo analizado:** provisto por el usuario en sesión KIRA-VENTAS-MODAL-REWORK-0B

---

## C. Motivo de la fase 0B

La fase 0 cerró con la nota:

> "El HTML de diseño nuevo no fue incluido en esta sesión. Las secciones D y F describen la
> estructura esperada según el brief. Cuando el usuario provea el HTML, actualizar la sección F."

Ahora el HTML fue provisto. Esta fase completa ese análisis con datos reales.
Además el usuario aclaró explícitamente: **el nuevo diseño es una nueva ventana completa,
no un modal panel flotante**. Esto es un cambio arquitectónico fundamental que afecta la estrategia 1A.

---

## D. Resumen del HTML real

| Aspecto | Valor |
|---|---|
| Tipo de presentación | Fullscreen takeover (`fixed inset-0 z-50 flex flex-col`) |
| Layout interno | Header sticky + `<main>` scrolleable + sidebar sticky |
| Grid principal | `lg:grid-cols-12` — col-span-8 (contenido) + col-span-4 (resumen) |
| Wizard de pasos | 5 tabs reales: Cliente · Productos · Pago · Crédito · Revisión |
| CSS inline | Bloque `<style>` extenso — sistema de clases propias (`.card`, `.field`, `.btn`, `.pill`, etc.) |
| JS inline | Presente (truncado en transmisión) — funciones `activateStep()` y `openModal()` detectadas como inline calls |
| Fuentes nuevas | Inter + JetBrains Mono vía Google Fonts CDN — reemplazar con fuentes del proyecto |
| Tailwind | CDN de prototipo — ya disponible globalmente, no copiar el `<link>` |
| Valores mock | Opciones hardcoded en `#select-tipo-pago` — sustituir por `@foreach (tiposPago)` |
| Antiforgery | NO visible en el HTML recibido — **agregar obligatoriamente en 1A** |

---

## E. Estructura del modal nuevo

```
<div id="modal-crear-venta">                  fixed inset-0 z-50 flex flex-col
  <div id="modal-crear-venta-backdrop">        backdrop absoluto (fondo oscuro)
  <header>                                     sticky top-0 z-20
    título + subtítulo
    #vm-estado-global                          pill de estado (Incompleta / Completa)
    #btn-cerrar-modal-crear-venta
    tablist (role="tablist")
      #step-btn-cliente    data-step="cliente"   aria-controls="step-panel-cliente"
      #step-btn-productos  data-step="productos" aria-controls="step-panel-productos"
      #step-btn-pago       data-step="pago"      aria-controls="step-panel-pago"
      #step-btn-credito    data-step="credito"   aria-controls="step-panel-credito"
      #step-btn-revision   data-step="revision"  aria-controls="step-panel-revision"
  <div id="venta-ajax-validation-summary">     banner de errores AJAX (hidden por defecto)
    <ul id="venta-ajax-error-list">
  <main class="flex-1 overflow-y-auto">
    <form id="venta-form" action="/Venta/CreateAjax" method="post">
      input hidden Estado
      #venta-modal-caja-cerrada                banner "Caja cerrada" (hidden, nuevo)
      <div class="lg:grid-cols-12 gap-5">
        <div class="lg:col-span-8">            PANELES DE PASOS
          #step-panel-cliente
          #step-panel-productos
          #step-panel-pago
          #step-panel-credito
          #step-panel-revision
        <div class="lg:col-span-4">           SIDEBAR (no llegó completo en transmisión)
          Resumen sticky + totales + btn-confirmar (estructura a verificar en 1A)
```

---

## F. CSS inline detectado y destino sugerido

El bloque `<style>` del HTML contiene un sistema de componentes completo, diferente al sistema `vm-*` actual.

| Clase nueva | Equivalente actual | Acción recomendada |
|---|---|---|
| `.card` | `.vm-section` | Crear `.vmr-card` o reutilizar `.vm-section` |
| `.card-sub` | `.vm-sub-panel` | Crear `.vmr-card-sub` o reutilizar |
| `.field` | `.vm-input` / `.vm-select` / `.vm-textarea` | Unificar con sistema `vm-*` existente |
| `.label` | `.vm-label` | Reutilizar `.vm-label` |
| `.req::after` | `.vm-required` | Mantener `.vm-required` existente |
| `.btn`, `.btn-primary`, etc. | Clases Tailwind inline en modal actual | Extraer a sistema de botones |
| `.pill`, `.pill-*` | No existe en sistema actual | Crear `.vmr-pill` en nuevo CSS |
| `.step-btn`, `.step-num` | No existe | Crear `.vmr-step-btn` en nuevo CSS |
| `.steps-scroll` | No existe | Crear en nuevo CSS |
| `.total-display` | `.font-mono` inline | Crear `.vmr-total-display` en nuevo CSS |
| `.modal-backdrop` | Backdrop inline en actual | Evaluar unificación |
| `.dropdown-list`, `.dropdown-item` | `.vm-dropdown` | Unificar o crear alias |
| `.dot` | No existe | Crear `.vmr-dot` |
| `@keyframes fadeIn` | No existe | Crear en nuevo CSS |

**Destino:** `wwwroot/css/venta-modal-rework.css` con prefijo `.vmr-` para clases nuevas.

**Regla crítica:** No reemplazar clases `.vm-*` existentes — son hooks del CSS canónico y de tests.

---

## G. JS inline detectado y destino sugerido

Del HTML analizado se detectaron las siguientes llamadas JS inline:

| Llamada | Ubicación | Análisis |
|---|---|---|
| `activateStep('credito')` | `onclick` en btn "Ir a verificación crediticia" | Función nueva del wizard — no existe en `venta-create.js` |
| `openModal('modal-documentacion')` | `onclick` en `#btn-cargar-documentacion` | **Rompe contrato actual** — actual usa `data-venta-modal-action/target` |
| `document.getElementById('btn-cerrar-modal-crear-venta').click()` | Banner caja cerrada | Pattern aceptable — cierra modal |

**Funciones a crear en `wwwroot/js/venta-modal-rework.js`:**

```javascript
function activateStep(stepName) { ... }   // controlador del wizard de pasos
function openModal(modalId) { ... }        // abre sub-modales por ID
function closeModal(modalId) { ... }       // cierra sub-modales por ID
```

**Regla crítica:** `openModal('modal-documentacion')` rompe el sistema de `data-venta-modal-action`.
En la fase 1A, `#btn-cargar-documentacion` debe mantener los data-attrs actuales:
```html
data-venta-modal-action="open"
data-venta-modal-target="documentacion"
```
O el handler en `venta-create.js` debe adaptarse para reconocer también la llamada directa.

---

## H. IDs del HTML nuevo (inventario completo)

### H.1 IDs presentes en el HTML recibido

```
modal-crear-venta
modal-crear-venta-backdrop
btn-cerrar-modal-crear-venta
vm-estado-global                         ← NUEVO
cv-title                                 ← NUEVO
venta-ajax-validation-summary
venta-ajax-error-list
step-btn-cliente                         ← NUEVO
step-btn-productos                       ← NUEVO
step-btn-pago                            ← NUEVO
step-btn-credito                         ← NUEVO
step-btn-revision                        ← NUEVO
step-panel-cliente                       ← NUEVO
step-panel-productos                     ← NUEVO
step-panel-pago                          ← NUEVO
step-panel-credito                       ← NUEVO
step-panel-revision                      ← NUEVO
venta-form
venta-modal-caja-cerrada                 ← NUEVO
input-buscar-cliente
dropdown-clientes
hdn-cliente-id
info-cliente
info-cliente-nombre
info-cliente-doc
btn-limpiar-cliente
FechaVenta
select-tipo-pago
panel-aviso-credito
credito-disponible
credito-solicitado
credito-margen
detalle-items-badge
filtro-categoria
filtro-marca
filtro-precio-min
filtro-precio-max
filtro-solo-stock
input-buscar-producto
dropdown-productos
panel-agregar-producto
txt-producto-seleccionado
hdn-producto-id
hdn-producto-codigo
hdn-producto-precio
hdn-producto-stock
hdn-producto-requiere-numero-serie
txt-cantidad
txt-descuento-item
btn-agregar-producto
stock-error
advertencia-stock-sin-identificar
venta-detalles-scroll
tbody-detalles
detalles-vacio
detalles-hidden-inputs
venta-detalles-cards                     ← NUEVO (mobile cards container)
venta-detalle-card-template              ← NUEVO (template element)
panel-tarjeta
select-tarjeta
select-cuotas-tarjeta
txt-num-autorizacion-tarjeta
panel-tarjeta-resumen
tarjeta-monto-cuota
tarjeta-total-interes
tarjeta-recargo
panel-cheque
txt-num-cheque
txt-banco-cheque
txt-titular-cheque
txt-cuit-cheque
txt-fecha-emision-cheque
txt-fecha-vencimiento-cheque
txt-monto-cheque
panel-credito-personal
panel-credito-cupo                       ← NUEVO
credito-cupo-valor                       ← NUEVO
credito-cupo-estado                      ← NUEVO
panel-mercadopago                        ← NUEVO (MercadoPago panel)
txt-mp-referencia                        ← NUEVO
select-mp-modalidad                      ← NUEVO
panel-credito-no-requerido               ← NUEVO
panel-verificacion-crediticia
btn-verificar-elegibilidad
venta-create-feedback-slot
panel-resultado-verificacion
verificacion-badge
verificacion-estado
verificacion-limite
verificacion-utilizado
verificacion-saldo
verificacion-barra
panel-cupo-suficiente
panel-cupo-insuficiente
cupo-insuficiente-detalle
panel-motivos
lista-motivos
panel-alerta-mora
alerta-mora-texto
panel-documentacion-faltante
lista-docs-faltantes
btn-cargar-documentacion
panel-excepcion-crediticia
hdn-aplicar-excepcion
panel-excepcion-inactiva
btn-aplicar-excepcion
panel-excepcion-activa
txt-excepcion-documental
btn-cancelar-excepcion
btn-confirmar-excepcion
revision-alertas                         ← NUEVO
revision-alertas-badge                   ← NUEVO
revision-alertas-vacio                   ← NUEVO
revision-alertas-lista                   ← NUEVO
```

> **Nota:** El HTML fue truncado por límite de transmisión. La sección de sidebar (`lg:col-span-4`),
> el paso Revisión completo y los sub-modales `#modal-pago-item` y `#modal-documentacion` no se
> recibieron completos. Se presume su presencia por compatibilidad pero deben verificarse en 1A.

---

## I. IDs actuales del modal existente (referencia)

Ver lista completa en sección H.1 de `docs/kira-ventas-modal-rework-0-auditoria.md`.

Los más críticos para la comparación están cubiertos en la sección J a continuación.

---

## J. Coincidencias de IDs

Los siguientes IDs del HTML nuevo coinciden exactamente con los del modal actual:

```
modal-crear-venta                    ✅
modal-crear-venta-backdrop           ✅
btn-cerrar-modal-crear-venta         ✅
venta-ajax-validation-summary        ✅
venta-ajax-error-list                ✅
venta-form                           ✅
input-buscar-cliente                 ✅
dropdown-clientes                    ✅
hdn-cliente-id                       ✅
info-cliente                         ✅
info-cliente-nombre                  ✅
info-cliente-doc                     ✅
btn-limpiar-cliente                  ✅
FechaVenta                           ✅
select-tipo-pago                     ✅
panel-aviso-credito                  ✅
credito-disponible                   ✅
credito-solicitado                   ✅
credito-margen                       ✅
detalle-items-badge                  ✅
filtro-categoria                     ✅
filtro-marca                         ✅
filtro-precio-min                    ✅
filtro-precio-max                    ✅
filtro-solo-stock                    ✅
input-buscar-producto                ✅
dropdown-productos                   ✅
panel-agregar-producto               ✅
txt-producto-seleccionado            ✅
hdn-producto-id                      ✅
hdn-producto-codigo                  ✅
hdn-producto-precio                  ✅
hdn-producto-stock                   ✅
hdn-producto-requiere-numero-serie   ✅
txt-cantidad                         ✅
txt-descuento-item                   ✅
btn-agregar-producto                 ✅
stock-error                          ✅
advertencia-stock-sin-identificar    ✅
venta-detalles-scroll                ✅
tbody-detalles                       ✅
detalles-vacio                       ✅
detalles-hidden-inputs               ✅
panel-tarjeta                        ✅
select-tarjeta                       ✅
select-cuotas-tarjeta                ✅
txt-num-autorizacion-tarjeta         ✅
panel-tarjeta-resumen                ✅
tarjeta-monto-cuota                  ✅
tarjeta-total-interes                ✅
tarjeta-recargo                      ✅
panel-cheque                         ✅
txt-num-cheque                       ✅
txt-banco-cheque                     ✅
txt-titular-cheque                   ✅
txt-cuit-cheque                      ✅
txt-fecha-emision-cheque             ✅
txt-fecha-vencimiento-cheque         ✅
txt-monto-cheque                     ✅
panel-credito-personal               ✅
panel-verificacion-crediticia        ✅
btn-verificar-elegibilidad           ✅
venta-create-feedback-slot           ✅
panel-resultado-verificacion         ✅
verificacion-badge                   ✅
verificacion-estado                  ✅
verificacion-limite                  ✅
verificacion-utilizado               ✅
verificacion-saldo                   ✅
verificacion-barra                   ✅
panel-cupo-suficiente                ✅
panel-cupo-insuficiente              ✅
cupo-insuficiente-detalle            ✅
panel-motivos                        ✅
lista-motivos                        ✅
panel-alerta-mora                    ✅
alerta-mora-texto                    ✅
panel-documentacion-faltante         ✅
lista-docs-faltantes                 ✅
btn-cargar-documentacion             ✅ (ID coincide — attrs diferentes, ver sección L)
panel-excepcion-crediticia           ✅
hdn-aplicar-excepcion                ✅ (ID coincide — name diferente, ver sección L)
panel-excepcion-inactiva             ✅
btn-aplicar-excepcion                ✅
panel-excepcion-activa               ✅
txt-excepcion-documental             ✅ (ID coincide — name faltante, ver sección L)
btn-cancelar-excepcion               ✅
btn-confirmar-excepcion              ✅
```

---

## K. IDs nuevos (no existen en el modal actual)

| ID nuevo | Ubicación en el nuevo HTML | Rol |
|---|---|---|
| `#cv-title` | Header — h2 título | ID del título accesible (`aria-labelledby`) |
| `#vm-estado-global` | Header — pill de estado | Estado global visible "Incompleta/Completa" |
| `#step-btn-cliente` | Header tablist | Tab de paso 1 |
| `#step-btn-productos` | Header tablist | Tab de paso 2 |
| `#step-btn-pago` | Header tablist | Tab de paso 3 |
| `#step-btn-credito` | Header tablist | Tab de paso 4 |
| `#step-btn-revision` | Header tablist | Tab de paso 5 |
| `#step-panel-cliente` | Main — sección paso 1 | Contenido del paso Cliente |
| `#step-panel-productos` | Main — sección paso 2 | Contenido del paso Productos |
| `#step-panel-pago` | Main — sección paso 3 | Contenido del paso Pago |
| `#step-panel-credito` | Main — sección paso 4 | Contenido del paso Crédito |
| `#step-panel-revision` | Main — sección paso 5 | Contenido del paso Revisión |
| `#venta-modal-caja-cerrada` | Form — banner | Banner "Caja cerrada" — nuevo flujo |
| `#venta-detalles-cards` | Paso Productos | Contenedor de cards mobile (md:hidden) |
| `#venta-detalle-card-template` | Paso Productos | `<template>` para cards mobile |
| `#panel-credito-no-requerido` | Paso Crédito | Panel informativo cuando pago no requiere crédito |
| `#panel-credito-cupo` | Paso Pago | Sub-panel de cupo en panel crédito personal |
| `#credito-cupo-valor` | Paso Pago | Display de cupo disponible |
| `#credito-cupo-estado` | Paso Pago | Pill de estado del cupo |
| `#panel-mercadopago` | Paso Pago | Panel de datos MercadoPago — nuevo |
| `#txt-mp-referencia` | Paso Pago | Input referencia MP |
| `#select-mp-modalidad` | Paso Pago | Select modalidad MP |
| `#revision-alertas` | Paso Revisión | Contenedor de alertas activas |
| `#revision-alertas-badge` | Paso Revisión | Badge resumen de alertas |
| `#revision-alertas-vacio` | Paso Revisión | Estado vacío (sin alertas) |
| `#revision-alertas-lista` | Paso Revisión | Lista de alertas activas |

---

## L. IDs peligrosos o incompatibles

### L.1 CRÍTICO — `name="AplicarExcepcion"` en lugar de `name="AplicarExcepcionDocumental"`

```html
<!-- HTML nuevo -->
<input id="hdn-aplicar-excepcion" name="AplicarExcepcion" type="hidden" value="false" />

<!-- Modal actual -->
<input type="hidden" name="AplicarExcepcionDocumental" id="hdn-aplicar-excepcion" value="false" />
```

**Impacto:** El backend espera `AplicarExcepcionDocumental`. Si se cambia el name, la excepción
documental no llegará al controller y la funcionalidad quedará rota silenciosamente.  
**Acción en 1A:** Mantener `name="AplicarExcepcionDocumental"`.

### L.2 CRÍTICO — `name` faltante en `#txt-excepcion-documental`

```html
<!-- HTML nuevo — SIN name -->
<textarea id="txt-excepcion-documental" class="field" ...></textarea>

<!-- Modal actual — CON name -->
<textarea name="MotivoExcepcionDocumentalCreate" id="txt-excepcion-documental" ...></textarea>
```

**Impacto:** Sin el name, el motivo de excepción no se envía en el POST.  
**Acción en 1A:** Agregar `name="MotivoExcepcionDocumentalCreate"`.

### L.3 ALTO — `#btn-cargar-documentacion` usa onclick en lugar de data-attrs

```html
<!-- HTML nuevo — onclick directo -->
<button id="btn-cargar-documentacion" onclick="openModal('modal-documentacion')">

<!-- Modal actual — data-attrs usados por venta-create.js -->
<button id="btn-cargar-documentacion"
        data-venta-modal-action="open"
        data-venta-modal-target="documentacion">
```

**Impacto:** El handler de `venta-create.js` escucha el atributo `data-venta-modal-action`,
no el onclick. Si se adopta el onclick, el sub-modal de documentación no se abrirá con el
flujo actual.  
**Acción en 1A:** Mantener los data-attrs del modal actual, O adaptar el handler en JS para
soportar ambos mecanismos.

### L.4 ALTO — `data-oc-scroll` removido de `#venta-detalles-scroll`

```html
<!-- HTML nuevo -->
<div id="venta-detalles-scroll" class="hidden..." data-venta-detalles-tabla>

<!-- Modal actual -->
<div id="venta-detalles-scroll" data-oc-scroll class="venta-scroll-medium">
```

**Impacto:** `horizontal-scroll-affordance.js` escucha `data-oc-scroll` para las fades laterales.
Si se remueve, el affordance de scroll no se inicializará.  
**Acción en 1A:** Conservar `data-oc-scroll` y atributos relacionados, O verificar si el nuevo
diseño los reemplaza con otro mecanismo.

### L.5 ALTO — `#select-tipo-pago` con opciones hardcoded

```html
<!-- HTML nuevo — opciones hardcoded (prototipo) -->
<option value="Efectivo">Efectivo</option>
<option value="Transferencia">Transferencia</option>
...

<!-- Modal actual — desde ViewBag -->
@if (tiposPago != null) { foreach (var tp in tiposPago) { <option>... } }
```

**Impacto:** Si se copian las opciones hardcoded, los medios de pago no reflejarán la
configuración del ERP.  
**Acción en 1A:** Usar el `@foreach (tiposPago)` del modal actual. El HTML nuevo tiene el
comentario `@* foreach *@` como placeholder — buen señal.

### L.6 MEDIO — `#select-cuotas-tarjeta` con opciones hardcoded

```html
<!-- HTML nuevo — cuotas fijas -->
<option value="1">1 pago</option>
<option value="3">3 cuotas</option>
<option value="6">6 cuotas</option>
<option value="12">12 cuotas</option>
```

**Impacto:** En el modal actual, las cuotas se cargan dinámicamente por `cargarTarjetasActivas()`
y `renderSelectorPlanesPago()`. Las opciones hardcoded no incluirán los planes reales de la tarjeta.  
**Acción en 1A:** Usar solo `<option value="1">1 Pago</option>` como fallback — el JS sobrescribe
las opciones al seleccionar una tarjeta.

---

## M. Formularios y names detectados

### M.1 Names presentes en el HTML nuevo

```
Estado                                  (hidden — valor Presupuesto)
ClienteId                               (hidden — id="hdn-cliente-id")
FechaVenta                              (input date)
TipoPago                                (select)
DatosTarjeta.ConfiguracionTarjetaId     (select-tarjeta)
DatosTarjeta.CantidadCuotas            (select-cuotas-tarjeta)
DatosTarjeta.NumeroAutorizacion         (txt-num-autorizacion-tarjeta)
DatosCheque.NumeroCheque               (txt-num-cheque)
DatosCheque.Banco                      (txt-banco-cheque)
DatosCheque.Titular                    (txt-titular-cheque)
DatosCheque.CUIT                       (txt-cuit-cheque)
DatosCheque.FechaEmision               (txt-fecha-emision-cheque)
DatosCheque.FechaVencimiento           (txt-fecha-vencimiento-cheque)
DatosCheque.Monto                      (txt-monto-cheque)
AplicarExcepcion                        ⚠️ DIFERENTE — debe ser AplicarExcepcionDocumental
```

### M.2 Names ausentes en el HTML nuevo (que existen en el actual)

```
DatosTarjeta.NombreTarjeta             ❌ FALTANTE — id="hdn-tarjeta-nombre"
DatosTarjeta.TipoTarjeta               ❌ FALTANTE — id="hdn-tarjeta-tipo"
DatosTarjeta.ConfiguracionPagoPlanId   ❌ FALTANTE — id="hdn-configuracion-pago-plan-id"
MotivoExcepcionDocumentalCreate        ❌ FALTANTE — id="txt-excepcion-documental" (sin name)
VendedorUserId                         ❓ No visible — puede estar en sidebar (truncado)
Observaciones                          ❓ No visible — puede estar en sidebar (truncado)
Subtotal                               ❓ No visible — puede estar en sidebar (truncado)
Descuento                              ❓ No visible — puede estar en sidebar (truncado)
IVA                                    ❓ No visible — puede estar en sidebar (truncado)
Total                                  ❓ No visible — puede estar en sidebar (truncado)
Detalles[i].*                          Generados por JS en #detalles-hidden-inputs ✅
```

> **Nota:** `DatosTarjeta.NombreTarjeta` y `DatosTarjeta.TipoTarjeta` son campos hidden que el JS
> puebla con datos de la tarjeta seleccionada. Son verificados por tests. **Deben agregarse en 1A.**

---

## N. Comparación campo por campo: actual vs HTML nuevo

| Campo / Elemento | Actual | HTML nuevo | Estado |
|---|---|---|---|
| **ARQUITECTURA** | | | |
| Tipo de presentación | Panel centrado (max-w-7xl, rounded) | Fullscreen window (flex flex-col) | 🔄 Cambio arquitectónico fundamental |
| Header | sticky dentro del panel | sticky del viewport completo | 🔄 Diferente — nuevo es más amplio |
| Layout de contenido | grid 2/3 + 1/3 (siempre visible) | 8/12 + 4/12 con tabs activos/hidden | 🔄 Wizard de pasos vs columnas siempre visibles |
| Navegación | Lineal, scroll vertical | Tabs de pasos (1-5) | 🆕 Nuevo patrón de navegación |
| **HEADER** | | | |
| ID raíz | `#modal-crear-venta` z-50 | `#modal-crear-venta` z-50 | ✅ Compatible |
| ID backdrop | `#modal-crear-venta-backdrop` | `#modal-crear-venta-backdrop` | ✅ Compatible |
| Btn cerrar | `#btn-cerrar-modal-crear-venta` | `#btn-cerrar-modal-crear-venta` | ✅ Compatible |
| ID título | (sin ID en actual) | `#cv-title` — usado en `aria-labelledby` | 🆕 Nuevo — agregar en 1A |
| Estado global | No existe | `#vm-estado-global` pill animado | 🆕 Nuevo — JS debe actualizar |
| Tabs de pasos | No existen | 5 step-btns + role=tablist | 🆕 Requiere JS del wizard |
| **VALIDACIÓN AJAX** | | | |
| Banner errores | `#venta-ajax-validation-summary` | `#venta-ajax-validation-summary` | ✅ Compatible |
| Lista errores | `#venta-ajax-error-list` | `#venta-ajax-error-list` | ✅ Compatible |
| **FORMULARIO** | | | |
| ID form | `#venta-form` | `#venta-form` | ✅ Compatible |
| action | `/Venta/CreateAjax` | `/Venta/CreateAjax` | ✅ Compatible |
| method | `post` | `post` | ✅ Compatible |
| Antiforgery | `@Html.AntiForgeryToken()` | ❌ NO PRESENTE en HTML recibido | 🔴 CRÍTICO — agregar en 1A |
| Hidden Estado | `name="Estado"` | `name="Estado"` con `data-static-fallback` | ✅ Compatible (ignorar el attr extra) |
| Banner caja cerrada | No existe | `#venta-modal-caja-cerrada` | 🆕 Nuevo — integrar o decidir |
| **PASO 1: CLIENTE** | | | |
| Búsqueda cliente | `#input-buscar-cliente` | `#input-buscar-cliente` | ✅ Compatible |
| Dropdown | `#dropdown-clientes` | `#dropdown-clientes` | ✅ Compatible |
| Hidden ClienteId | `name="ClienteId"` `id="hdn-cliente-id"` | `name="ClienteId"` `id="hdn-cliente-id"` | ✅ Compatible |
| Panel info cliente | `#info-cliente` | `#info-cliente` | ✅ Compatible |
| Nombre cliente | `#info-cliente-nombre` | `#info-cliente-nombre` | ✅ Compatible |
| Doc cliente | `#info-cliente-doc` | `#info-cliente-doc` | ✅ Compatible |
| Btn limpiar | `#btn-limpiar-cliente` | `#btn-limpiar-cliente` | ✅ Compatible |
| Fecha operación | `name="FechaVenta"` `id="FechaVenta"` | `name="FechaVenta"` `id="FechaVenta"` | ✅ Compatible |
| Selector tipo pago | `name="TipoPago"` `id="select-tipo-pago"` Razor `@foreach` | `name="TipoPago"` `id="select-tipo-pago"` opciones HARDCODED | ⚠️ Sustituir por Razor |
| Aviso crédito | `#panel-aviso-credito` 3 IDs | `#panel-aviso-credito` 3 IDs | ✅ Compatible |
| **PASO 2: PRODUCTOS** | | | |
| Badge count | `#detalle-items-badge` | `#detalle-items-badge` | ✅ Compatible |
| Filtros | 4 selects + 1 checkbox visibles siempre | Dentro de `<details>` collapsible | 🔄 Cambio UX — IDs coinciden |
| Filtro categoría | `#filtro-categoria` | `#filtro-categoria` | ✅ Compatible |
| Filtro marca | `#filtro-marca` | `#filtro-marca` | ✅ Compatible |
| Filtro precio min | `#filtro-precio-min` | `#filtro-precio-min` | ✅ Compatible |
| Filtro precio max | `#filtro-precio-max` | `#filtro-precio-max` | ✅ Compatible |
| Filtro solo stock | `#filtro-solo-stock` | `#filtro-solo-stock` | ✅ Compatible |
| Búsqueda producto | `#input-buscar-producto` | `#input-buscar-producto` | ✅ Compatible |
| Dropdown productos | `#dropdown-productos` | `#dropdown-productos` | ✅ Compatible |
| Panel agregar | `#panel-agregar-producto` | `#panel-agregar-producto` (siempre visible, sin hidden class) | ⚠️ Verificar comportamiento — actual empieza hidden |
| Txt producto | `#txt-producto-seleccionado` | `#txt-producto-seleccionado` | ✅ Compatible |
| Hidden producto ID | `#hdn-producto-id` | `#hdn-producto-id` | ✅ Compatible |
| Hidden producto código | `#hdn-producto-codigo` | `#hdn-producto-codigo` | ✅ Compatible |
| Hidden producto precio | `#hdn-producto-precio` | `#hdn-producto-precio` | ✅ Compatible |
| Hidden producto stock | `#hdn-producto-stock` | `#hdn-producto-stock` | ✅ Compatible |
| Hidden requiere N/S | `#hdn-producto-requiere-numero-serie` | `#hdn-producto-requiere-numero-serie` | ✅ Compatible |
| Cantidad | `#txt-cantidad` | `#txt-cantidad` | ✅ Compatible |
| Error stock | `#stock-error` | `#stock-error` | ✅ Compatible |
| Advertencia sin identificar | `#advertencia-stock-sin-identificar` | `#advertencia-stock-sin-identificar` | ✅ Compatible |
| Descuento item | `#txt-descuento-item` | `#txt-descuento-item` | ✅ Compatible |
| Btn agregar | `#btn-agregar-producto` | `#btn-agregar-producto` | ✅ Compatible |
| Selector unidad (trazabilidad) | `#panel-selector-unidad` `#select-producto-unidad` etc. | ❌ AUSENTE | 🔴 CRÍTICO — agregar en 1A |
| Tabla detalles | `#tbody-detalles` | `#tbody-detalles` | ✅ Compatible |
| Estado vacío | `#detalles-vacio` | `#detalles-vacio` | ✅ Compatible |
| Scroll affordance | `data-oc-scroll` attrs | `data-venta-detalles-tabla` — diferente | ⚠️ Conservar data-oc-scroll en 1A |
| Hidden inputs | `#detalles-hidden-inputs` | `#detalles-hidden-inputs` | ✅ Compatible |
| Mobile cards | No existe | `#venta-detalles-cards` + `<template>` | 🆕 Nuevo — integrar en 1A |
| **PASO 3: PAGO** | | | |
| Panel tarjeta | `#panel-tarjeta` | `#panel-tarjeta` | ✅ Compatible |
| Select tarjeta | `#select-tarjeta` `name=DatosTarjeta.ConfiguracionTarjetaId` | ✅ igual | ✅ Compatible |
| Hidden nombre tarjeta | `#hdn-tarjeta-nombre` `name=DatosTarjeta.NombreTarjeta` | ❌ AUSENTE | 🔴 CRÍTICO — tests lo verifican |
| Hidden tipo tarjeta | `#hdn-tarjeta-tipo` `name=DatosTarjeta.TipoTarjeta` | ❌ AUSENTE | 🔴 CRÍTICO — tests lo verifican |
| Hidden plan ID | `#hdn-configuracion-pago-plan-id` | ❌ AUSENTE | 🔴 CRÍTICO |
| Select cuotas | `#select-cuotas-tarjeta` | `#select-cuotas-tarjeta` — opciones hardcoded | ⚠️ Opciones se sobrescriben por JS |
| Resumen tarjeta | `#panel-tarjeta-resumen` | `#panel-tarjeta-resumen` | ✅ Compatible |
| Monto cuota | `#tarjeta-monto-cuota` | `#tarjeta-monto-cuota` | ✅ Compatible |
| Total interés | `#tarjeta-total-interes` | `#tarjeta-total-interes` | ✅ Compatible |
| Recargo | `#tarjeta-recargo` | `#tarjeta-recargo` | ✅ Compatible |
| Aviso sin interés | `#panel-aviso-cuotas-sin-interes` | ❌ AUSENTE | ⚠️ Agregar en 1A |
| Panel cheque | `#panel-cheque` + 7 fields | `#panel-cheque` + 7 fields | ✅ Compatible |
| Panel crédito personal | `#panel-credito-personal` | `#panel-credito-personal` | ✅ Compatible (contexto diferente) |
| Panel planes pago | `#panel-planes-pago` `#lista-planes-pago` | ❌ AUSENTE | 🔴 CRÍTICO — JS usa estos IDs |
| Estado cfg pagos global | `#configuracion-pagos-global-estado` | ❌ AUSENTE | 🔴 CRÍTICO — test lo verifica |
| Panel diagnóstico | `#panel-diagnostico-condiciones-pago` + 4 IDs | ❌ AUSENTE | ⚠️ Agregar oculto en 1A |
| Panel MercadoPago | No existe | `#panel-mercadopago` | 🆕 Nuevo |
| **PASO 4: CRÉDITO** | | | |
| Panel no requerido | No existe | `#panel-credito-no-requerido` | 🆕 Nuevo |
| Panel verificación | `#panel-verificacion-crediticia` | `#panel-verificacion-crediticia` | ✅ Compatible |
| Btn verificar | `#btn-verificar-elegibilidad` | `#btn-verificar-elegibilidad` | ✅ Compatible |
| Feedback slot | `#venta-create-feedback-slot` aria-live | `#venta-create-feedback-slot` aria-live | ✅ Compatible |
| Panel resultado | `#panel-resultado-verificacion` | `#panel-resultado-verificacion` | ✅ Compatible |
| Badge verificación | `#verificacion-badge` | `#verificacion-badge` | ✅ Compatible |
| Estado score | `#verificacion-estado` | `#verificacion-estado` | ✅ Compatible |
| Límite | `#verificacion-limite` | `#verificacion-limite` | ✅ Compatible |
| Utilizado | `#verificacion-utilizado` | `#verificacion-utilizado` | ✅ Compatible |
| Saldo | `#verificacion-saldo` | `#verificacion-saldo` | ✅ Compatible |
| Barra progreso | `#verificacion-barra` | `#verificacion-barra` | ✅ Compatible |
| Cupo suficiente | `#panel-cupo-suficiente` | `#panel-cupo-suficiente` | ✅ Compatible |
| Cupo insuficiente | `#panel-cupo-insuficiente` role=alert | `#panel-cupo-insuficiente` role=alert | ✅ Compatible |
| Detalle insuficiente | `#cupo-insuficiente-detalle` | `#cupo-insuficiente-detalle` | ✅ Compatible |
| Panel motivos | `#panel-motivos` | `#panel-motivos` | ✅ Compatible |
| Lista motivos | `#lista-motivos` | `#lista-motivos` | ✅ Compatible |
| Alerta mora | `#panel-alerta-mora` role=alert | `#panel-alerta-mora` role=alert | ✅ Compatible |
| Texto mora | `#alerta-mora-texto` | `#alerta-mora-texto` | ✅ Compatible |
| Doc faltante | `#panel-documentacion-faltante` | `#panel-documentacion-faltante` | ✅ Compatible |
| Lista docs | `#lista-docs-faltantes` | `#lista-docs-faltantes` | ✅ Compatible |
| Btn cargar doc | `#btn-cargar-documentacion` data-attrs | `#btn-cargar-documentacion` onclick | ⚠️ Cambio de contrato JS |
| Excepción | `#panel-excepcion-crediticia` | `#panel-excepcion-crediticia` | ✅ Compatible |
| Hidden excepción | `name="AplicarExcepcionDocumental"` | `name="AplicarExcepcion"` | 🔴 NAME CRÍTICO — cambiar |
| Textarea excepción | `name="MotivoExcepcionDocumentalCreate"` | Sin name | 🔴 NAME FALTANTE |
| **PASO 5: REVISIÓN** | | | |
| Panel revisión | No existe | `#step-panel-revision` + resumen | 🆕 Nuevo paso |
| Alertas activas | No existe | `#revision-alertas` `#revision-alertas-lista` | 🆕 Nuevo |
| **SIDEBAR (no recibido completo)** | | | |
| Totales | `#total-subtotal` `#total-descuento` `#total-iva` `#total-final` | ❓ En sidebar — verificar en 1A | ⚠️ Crítico |
| Hidden totales | `hdn-subtotal` `hdn-descuento` `hdn-iva` `hdn-total` | ❓ En sidebar — verificar | 🔴 Crítico para POST |
| Btn confirmar | `#btn-confirmar` onclick=VentaCrearModal.submit() | ❓ En sidebar — verificar | 🔴 Crítico |
| Sticky mobile | `#vm-modal-sticky-total` `.vm-mobile-summary-bar` | ❓ Posiblemente reemplazado | ⚠️ Verificar |
| VendedorUserId | `name="VendedorUserId"` | ❓ En sidebar — verificar | ⚠️ Crítico |
| Observaciones | `name="Observaciones"` `id="Observaciones"` | ❓ En sidebar — verificar | ⚠️ Crítico |
| **SUB-MODALES** | | | |
| Modal pago item | `#modal-pago-item` z-70 + IDs internos | ❓ No recibido (truncado) | ⚠️ Verificar en 1A |
| Modal documentación | `#modal-documentacion` data-venta-modal | ❓ No recibido (truncado) | ⚠️ Verificar en 1A |

---

## O. Contratos que deben preservarse

### O.1 IDs críticos — todos deben existir exactamente igual en 1A

```
#input-buscar-cliente, #dropdown-clientes, #hdn-cliente-id
#info-cliente, #info-cliente-nombre, #info-cliente-doc, #btn-limpiar-cliente
#input-buscar-producto, #dropdown-productos
#panel-agregar-producto, #txt-producto-seleccionado
#hdn-producto-id, #hdn-producto-codigo, #hdn-producto-precio
#hdn-producto-stock, #hdn-producto-requiere-numero-serie
#txt-cantidad, #stock-error, #advertencia-stock-sin-identificar
#txt-descuento-item, #btn-agregar-producto
#panel-selector-unidad, #select-producto-unidad
#producto-unidad-error, #aviso-sin-unidades, #link-gestionar-unidades
#tbody-detalles, #detalles-vacio, #detalles-hidden-inputs
#detalle-items-badge, #venta-detalles-scroll (con data-oc-scroll)
#select-tipo-pago
#panel-tarjeta, #select-tarjeta, #select-cuotas-tarjeta
#hdn-tarjeta-nombre, #hdn-tarjeta-tipo, #hdn-configuracion-pago-plan-id
#panel-tarjeta-resumen, #tarjeta-monto-cuota, #tarjeta-total-interes, #tarjeta-recargo
#panel-aviso-cuotas-sin-interes
#panel-cheque (+ 7 campos)
#panel-credito-personal, #panel-planes-pago, #lista-planes-pago
#configuracion-pagos-global-estado
#panel-diagnostico-condiciones-pago (+ 4 sub-IDs) — puede ser hidden
#panel-aviso-credito, #credito-disponible, #credito-solicitado, #credito-margen
#panel-verificacion-crediticia, #btn-verificar-elegibilidad
#venta-create-feedback-slot (aria-live="polite")
#panel-resultado-verificacion, #verificacion-badge, #verificacion-estado
#verificacion-limite, #verificacion-utilizado, #verificacion-saldo, #verificacion-barra
#panel-cupo-suficiente, #panel-cupo-insuficiente, #cupo-insuficiente-detalle
#panel-motivos, #lista-motivos
#panel-alerta-mora (role=alert), #alerta-mora-texto
#panel-documentacion-faltante, #lista-docs-faltantes
#btn-cargar-documentacion (con data-venta-modal-action/target)
#panel-excepcion-crediticia, #hdn-aplicar-excepcion
#panel-excepcion-inactiva, #btn-aplicar-excepcion
#panel-excepcion-activa, #btn-confirmar-excepcion, #btn-cancelar-excepcion
#txt-excepcion-documental (con name="MotivoExcepcionDocumentalCreate")
#total-subtotal, #total-descuento-label, #total-descuento, #total-iva, #total-final
#hdn-subtotal, #hdn-descuento, #hdn-iva, #hdn-total
#btn-confirmar (onclick=VentaCrearModal.submit())
#venta-ajax-validation-summary, #venta-ajax-error-list
#modal-pago-item (con todos sus IDs internos)
#modal-documentacion (con todos sus IDs internos)
```

### O.2 Names de formulario — no cambiar

```
ClienteId, FechaVenta, TipoPago, Estado
Subtotal, Descuento, IVA, Total
VendedorUserId, Observaciones
MotivoExcepcionDocumentalCreate
AplicarExcepcionDocumental          ← NO cambiar a "AplicarExcepcion"
DatosTarjeta.ConfiguracionTarjetaId, DatosTarjeta.NombreTarjeta
DatosTarjeta.TipoTarjeta, DatosTarjeta.CantidadCuotas
DatosTarjeta.NumeroAutorizacion, DatosTarjeta.ConfiguracionPagoPlanId
DatosCheque.NumeroCheque, DatosCheque.Banco, DatosCheque.Titular
DatosCheque.CUIT, DatosCheque.FechaEmision, DatosCheque.FechaVencimiento
DatosCheque.Monto
Detalles[i].ProductoId, Detalles[i].Cantidad, Detalles[i].PrecioUnitario
Detalles[i].Descuento, Detalles[i].Subtotal, Detalles[i].ProductoUnidadId
```

### O.3 Data-attributes — no cambiar

```
data-venta-modal-action="open"         en #btn-cargar-documentacion
data-venta-modal-action="close"        en overlays y botones de sub-modales
data-venta-modal-target="documentacion"
data-venta-modal="documentacion"       en #modal-documentacion
data-oc-scroll                         en #venta-detalles-scroll
data-oc-scroll-shell, data-oc-scroll-fade, data-oc-scroll-region, data-oc-scroll-table, data-oc-scroll-hint
data-index                             en .btn-eliminar-detalle (generado por JS)
data-plan-id                           en .plan-pago-btn (generado por JS)
data-configuracion-pago-id             en options de select-tipo-pago (generado por JS)
```

---

## P. Funciones actuales que no se pueden romper

| Función | Dependencias HTML |
|---|---|
| `cargarConfiguracionPagosGlobal()` | `#select-tipo-pago`, `#configuracion-pagos-global-estado` |
| `onTipoPagoChange()` | `#panel-tarjeta`, `#panel-cheque`, `#panel-credito-personal`, `#panel-verificacion-crediticia` |
| `cargarTarjetasActivas()` | `#select-tarjeta`, `#hdn-tarjeta-nombre`, `#hdn-tarjeta-tipo` |
| `recalcularTotales()` | `#hdn-subtotal`, `#hdn-descuento`, `#hdn-iva`, `#hdn-total`, `#total-final` |
| `actualizarTotalesUI()` | `#total-subtotal`, `#total-descuento-label`, `#total-descuento`, `#total-iva`, `#total-final` |
| `renderDetalles()` | `#tbody-detalles`, `#detalles-hidden-inputs`, `#detalles-vacio` |
| `renderSelectorPlanesPago()` | `#lista-planes-pago`, `.plan-pago-btn` (class) |
| `actualizarBloqueoContinuidadCondicionesPago()` | `#btn-confirmar`, `#panel-diagnostico-condiciones-pago` |
| `VentaCrearModal.submit()` | `#venta-form` con antiforgery y action correcto |
| MutationObserver sticky | `#total-final` → `#vm-modal-sticky-total` |
| Sub-modal doc handler | `data-venta-modal-action`, `data-venta-modal-target` |

---

## Q. Funciones nuevas que deben adaptarse para el nuevo diseño

| Función nueva requerida | Rol | Archivo destino |
|---|---|---|
| `activateStep(stepName)` | Activa un tab del wizard — muestra su panel, oculta los demás | `venta-modal-rework.js` |
| `updateStepState(stepName, state)` | Actualiza `data-state` del tab (pendiente/completo/atencion/bloqueado) | `venta-modal-rework.js` |
| `updateGlobalState()` | Actualiza `#vm-estado-global` según estado de pasos | `venta-modal-rework.js` |
| `openModal(modalId)` | Abre sub-modales por ID (nueva API) | `venta-modal-rework.js` |
| `closeModal(modalId)` | Cierra sub-modales por ID | `venta-modal-rework.js` |
| `syncRevisionPanel()` | Puebla `data-rev-*` del paso Revisión | `venta-modal-rework.js` |
| `updateRevisionAlertas()` | Muestra/oculta alertas en `#revision-alertas-lista` | `venta-modal-rework.js` |
| Actualizar `#credito-cupo-valor` | Espejo del cupo en paso Pago tras verificación | `venta-modal-rework.js` o extensión de `venta-create.js` |
| Actualizar `data-pago-summary` | Muestra nombre del tipo de pago en paso Pago | `venta-modal-rework.js` |

**Regla:** Ninguna de estas funciones debe redefinir ni reemplazar las funciones existentes de `venta-create.js`.

---

## R. Qué NO copiar directo del HTML nuevo

```
<!DOCTYPE html>
<html>
<head>
<title>
<script src="https://cdn.tailwindcss.com">           ← Ya disponible globalmente
<link href="https://fonts.googleapis.com/css2?family=Inter...">  ← Usar fuentes del proyecto
<link href="https://fonts.googleapis.com/icon?family=Material Symbols Outlined"> ← Ya disponible
<style> ... </style>                                 ← Extraer a venta-modal-rework.css
onclick="activateStep('credito')"                    ← Mover a venta-modal-rework.js
onclick="openModal('modal-documentacion')"           ← Reemplazar por data-attrs del sistema actual
<option value="Efectivo">Efectivo</option>           ← Reemplazar por @foreach (tiposPago)
data-static-fallback="1"                             ← Atributo de prototipo, no copiar
name="AplicarExcepcion"                              ← Usar AplicarExcepcionDocumental
<textarea id="txt-excepcion-documental">             ← Agregar name="MotivoExcepcionDocumentalCreate"
Opciones hardcoded de cuotas en select-cuotas-tarjeta ← Las sobrescribe el JS
Datos mock en #txt-producto-seleccionado             ← El JS los pone en ""
```

---

## S. Qué sí reutilizar del HTML nuevo

```
Estructura visual del header (con pill de estado)
Wizard de 5 tabs (step-btn + step-panel) — nueva navegación
Layout grid 8/12 + 4/12
Panel #panel-credito-no-requerido — buena UX informativa
Panel #panel-mercadopago — nuevo medio de pago con campos de referencia
#venta-modal-caja-cerrada — banner informativo de caja cerrada
Mobile cards (#venta-detalles-cards + <template>) — mejora de UX mobile
Panel #revision-alertas con data-alerta attrs — nuevo paso de revisión
#panel-credito-cupo / #credito-cupo-valor / #credito-cupo-estado en panel crédito
Variables CSS del <style> inline (paleta de color, token de radio, etc.) — centralizar en :root
#vm-estado-global pill de estado global del wizard
Sistema de pills (.pill-*) — mejora visual de estados
Clases .card y .card-sub para cards con fondos diferenciados — más claros que .vm-section actual
Sistema .field — unificar con .vm-input si es posible, o usar como sistema alternativo
Sistema .btn — puede coexistir con sistema vm-btn si se usa con prefijo vmr-
```

---

## T. Riesgos actualizados

### T.1 CRÍTICO — Cambio arquitectónico: panel centrado → fullscreen window

**Descripción:** El nuevo diseño elimina el panel flotante centrado (`max-w-7xl rounded-2xl`) y
reemplaza todo el contenido por un layout fullscreen con header+main+sidebar. El `fixed inset-0`
existe en ambos, pero el interior es completamente diferente.

**Impacto:** La fase 1A no es un "ajuste visual" — es un rewrite del layout interior completo.

**Mitigación:** Implementar por secciones. El wrapper `#modal-crear-venta` se mantiene igual.
El contenido interior se reemplaza con el nuevo layout, preservando todos los IDs internos.

### T.2 CRÍTICO — Antiforgery ausente en el HTML recibido

**Descripción:** El bloque `@Html.AntiForgeryToken()` no aparece en el HTML recibido
(puede ser que estuviera fuera del fragmento transmitido, pero es riesgo real si se omite en 1A).

**Impacto:** Sin antiforgery, todos los POST fallan con 400 BadRequest.

**Mitigación:** Verificar y agregar explícitamente en 1A como primer campo del `#venta-form`.

### T.3 CRÍTICO — Names de formulario cambiados

**Descripción:** `name="AplicarExcepcion"` y textarea sin name (sección L.1 y L.2).

**Impacto:** Excepción crediticia no llega al controller.

**Mitigación:** Corregir en 1A antes de cualquier prueba.

### T.4 CRÍTICO — IDs ausentes que JS usa directamente

**Descripción:** `#hdn-tarjeta-nombre`, `#hdn-tarjeta-tipo`, `#hdn-configuracion-pago-plan-id`,
`#panel-planes-pago`, `#lista-planes-pago`, `#configuracion-pagos-global-estado`,
`#panel-selector-unidad` (trazabilidad) — todos ausentes en el HTML nuevo.

**Impacto:** `cargarTarjetasActivas()`, `renderSelectorPlanesPago()`, trazabilidad física
dejan de funcionar. Tests de tarjeta fallan.

**Mitigación:** Agregar estos elementos en 1A, pueden estar ocultos si no aplica visualmente.

### T.5 ALTO — Wizard de pasos: nuevo patrón de navegación

**Descripción:** El HTML nuevo divide el contenido en 5 paneles tabulados. El JS actual opera
sobre todos los elementos directamente (sin importar en qué "paso" están). Con el wizard,
los paneles de pasos 2-5 empiezan con `class="hidden"`.

**Impacto:** Funciones como `onTipoPagoChange()` operan en elementos que pueden estar hidden.
`recalcularTotales()` necesita encontrar los spans aunque el paso Revisión no esté activo.

**Decisión requerida:** ¿El wizard oculta los elementos con `display:none` o solo visualmente?
Si es `display:none`, el JS puede fallar buscando elementos. Evaluar en fase 1C.

### T.6 ALTO — `#btn-cargar-documentacion` sin data-attrs

**Descripción:** Usa `onclick="openModal('modal-documentacion')"` en lugar de `data-venta-modal-action`.

**Impacto:** Handler de `venta-create.js` no captura el evento.

**Mitigación en 1A:** Mantener data-attrs del sistema actual. O adaptar JS en 1C.

### T.7 ALTO — `#venta-detalles-scroll` sin `data-oc-scroll`

**Descripción:** El atributo `data-oc-scroll` fue removido. `horizontal-scroll-affordance.js`
lo usa como hook de inicialización.

**Impacto:** Affordance de scroll horizontal desaparece.

**Mitigación en 1A:** Conservar `data-oc-scroll` y atributos relacionados del modal actual.

### T.8 ALTO — Sidebar y totales no recibidos (HTML truncado)

**Descripción:** La columna `lg:col-span-4` (sidebar con totales, btn-confirmar, vendedor,
observaciones) no llegó completa por truncamiento de mensaje.

**Impacto:** No se puede comparar el sidebar con el actual hasta ver el HTML completo.

**Mitigación en 1A:** Solicitar el HTML completo del sidebar antes de implementar esa sección.
Mantener el sidebar actual como fallback hasta tener la versión nueva.

### T.9 MEDIO — Panel MercadoPago: campos sin name de formulario

**Descripción:** `#txt-mp-referencia` y `#select-mp-modalidad` no tienen `name` atributo.

**Impacto:** Los datos de MercadoPago no se enviarían en el POST.

**Decisión requerida:** ¿El backend tiene un modelo para datos de MercadoPago?
Coordinar con backend antes de agregar este panel.

### T.10 MEDIO — Wizard: visibilidad de pasos vs funcionalidad JS

**Descripción:** Al activar el paso 3 (Pago), los paneles del paso 1 (Cliente) quedan hidden.
Pero el JS puede intentar leer `#select-tipo-pago` o `#hdn-cliente-id` en cualquier momento.

**Impacto:** Ninguno si los elementos existen en el DOM aunque estén hidden (display:none
no los elimina del DOM). Solo hay riesgo si el wizard usa `remove()` en lugar de `hidden class`.

**Mitigación:** Verificar que el wizard solo agrega/quita clases, no elimina elementos del DOM.

---

## U. Roadmap final corregido

### KIRA-VENTAS-MODAL-REWORK-1A — Skeleton Razor con nuevo layout

**Alcance:**

1. Solicitar HTML completo del sidebar antes de empezar (o implementar sidebar actual como fallback)
2. Reemplazar el layout interior de `_VentaCrearModal.cshtml` con la estructura nueva (header wizard + main + sidebar)
3. Implementar los 5 step-panels con el contenido de cada paso
4. Preservar TODOS los IDs, names, data-attrs de la lista O.1 y O.2
5. Agregar elementos ausentes del nuevo HTML: `#panel-selector-unidad`, `#hdn-tarjeta-nombre`, `#hdn-tarjeta-tipo`, `#hdn-configuracion-pago-plan-id`, `#panel-planes-pago`, `#lista-planes-pago`, `#configuracion-pagos-global-estado`, `#panel-diagnostico-condiciones-pago`, `#panel-aviso-cuotas-sin-interes`
6. Corregir: `name="AplicarExcepcionDocumental"`, agregar `name="MotivoExcepcionDocumentalCreate"`
7. Mantener `data-venta-modal-action/target` en `#btn-cargar-documentacion`
8. Mantener `data-oc-scroll` attrs en `#venta-detalles-scroll`
9. Convertir opciones hardcoded a Razor (`@foreach (tiposPago)`, etc.)
10. Agregar `@Html.AntiForgeryToken()` como primer campo del form

**Validar:** build + `dotnet test --filter "VentaCreate"` (60 tests deben pasar)

### KIRA-VENTAS-MODAL-REWORK-1B — CSS del nuevo diseño

**Alcance:** Crear `wwwroot/css/venta-modal-rework.css` con clases `vmr-*` para el nuevo sistema visual. No reemplazar `vm-*`.

**Validar:** build + inspección visual

### KIRA-VENTAS-MODAL-REWORK-1C — JS del wizard de pasos

**Alcance:** Crear `wwwroot/js/venta-modal-rework.js` con `activateStep()`, `updateStepState()`, `updateGlobalState()`, `syncRevisionPanel()`. Resolver conflicto de `openModal` vs `data-venta-modal-action`.

**Validar:** build + tests JS en VentaCreateUiContractTests + Playwright visual básico

### KIRA-VENTAS-MODAL-REWORK-1D — Integración cliente / producto / totales

**Alcance:** Verificar que con el nuevo layout, autocomplete, renderDetalles, y actualizarTotalesUI funcionan correctamente. Wizard no debe interferir con el JS que opera en elementos hidden.

**Validar:** Playwright flujo agregar cliente + productos + total

### KIRA-VENTAS-MODAL-REWORK-1E — Pago principal y pago por producto

**Alcance:** Verificar onTipoPagoChange con el nuevo layout. Verificar sub-modal #modal-pago-item.

**Validar:** Playwright flujo tipo de pago

### KIRA-VENTAS-MODAL-REWORK-1F — Crédito / documentación / excepción

**Alcance:** Verificar paso Crédito con `#panel-credito-no-requerido` vs `#panel-verificacion-crediticia`. Verificar sub-modal documentación.

**Validar:** Playwright crédito + documentación

### KIRA-VENTAS-MODAL-REWORK-1G — Confirmación / CreateAjax / submit

**Alcance:** Verificar `VentaCrearModal.submit()`, antiforgery, action. Integrar paso Revisión como revisión previa al confirm.

**Validar:** Playwright flujo completo venta exitosa + error

### KIRA-VENTAS-MODAL-REWORK-QA — Regresión completa

**Alcance:** VentaCreateUiContractTests 60/60 + E2E visual + accesibilidad + venta end-to-end.

---

## V. Próximo prompt recomendado para 1A

```
PROMPT — KIRA-VENTAS-MODAL-REWORK-1A — Skeleton Razor del nuevo modal

Base: main actualizado. Auditoría: docs/kira-ventas-modal-rework-0b-html-real.md.

Aclaración arquitectónica: el nuevo diseño es una ventana fullscreen (no un panel flotante centrado).
El layout interior cambia completamente pero los IDs y contratos se mantienen.

Tarea:
1. Implementar el nuevo layout interior en _VentaCrearModal.cshtml:
   header sticky con wizard de 5 tabs + main scrolleable + sidebar sticky (12 cols).

2. Contenido de cada step-panel según el HTML nuevo.

3. OBLIGATORIO agregar estos elementos ausentes en el HTML nuevo:
   - #panel-selector-unidad + #select-producto-unidad + #producto-unidad-error + #aviso-sin-unidades + #link-gestionar-unidades (trazabilidad)
   - #hdn-tarjeta-nombre (name="DatosTarjeta.NombreTarjeta")
   - #hdn-tarjeta-tipo (name="DatosTarjeta.TipoTarjeta")
   - #hdn-configuracion-pago-plan-id (name="DatosTarjeta.ConfiguracionPagoPlanId")
   - #panel-planes-pago + #lista-planes-pago
   - #configuracion-pagos-global-estado
   - #panel-diagnostico-condiciones-pago (oculto, con sus 4 IDs internos)
   - #panel-aviso-cuotas-sin-interes

4. CORREGIR:
   - name="AplicarExcepcionDocumental" (no "AplicarExcepcion")
   - Agregar name="MotivoExcepcionDocumentalCreate" al textarea de excepción
   - Mantener data-venta-modal-action/target en #btn-cargar-documentacion
   - Mantener data-oc-scroll attrs en #venta-detalles-scroll
   - Sustituir opciones hardcoded por @foreach (tiposPago)
   - Agregar @Html.AntiForgeryToken() en el form

5. No tocar JS ni CSS productivo todavía.

6. Validar: dotnet build + dotnet test --filter "VentaCreate" (60 tests).

Nota: El sidebar (col-span-4) puede basarse en el modal actual hasta recibir el HTML nuevo del sidebar.
```

---

## Validaciones ejecutadas (fase 0B)

```powershell
git diff --check    # Sin errores de whitespace
git status --short  # Solo nuevo archivo de documentación
```

**Build/tests/Playwright:** No ejecutados — fase doc-only. El diff confirma que solo se crea un archivo de documentación.

---

## Estado final del working tree

```
M  .claude/settings.local.json    ← LOCAL, no commitear
M  AGENTS.md                      ← LOCAL, no commitear
M  CLAUDE.md                      ← LOCAL, no commitear
M  Views/Producto/Unidades.cshtml ← LOCAL pre-existente, no commitear
M  docs/misa-catalogo-ux-1g-aria-live-modales.md ← LOCAL, no commitear
D  skills-lock.json               ← LOCAL, no commitear
?? docs/kira-ventas-modal-rework-0b-html-real.md  ← NUEVO — commitear
```
