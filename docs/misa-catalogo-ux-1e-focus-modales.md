# MISA-CATALOGO-UX-1E — Gestión de foco en modales de Catálogo

## Estado

Cerrado. Implementado y commiteado.

## Objetivo

Agregar gestión de foco correcta (WCAG 2.4.3 / APG Dialog pattern) a los 10 modales del Catálogo:

- Al abrir: mover foco al primer control interactivo útil o al título del modal.
- Al cerrar: devolver foco al trigger que abrió el modal.
- Verificar Escape (ya existía en todos los modales desde MISA-CATALOGO-UX-1B).

## Modales auditados

| Modal | Archivo JS | Foco al abrir | Foco al cerrar |
|---|---|---|---|
| Nuevo Producto | `producto-crear-modal.js` | `input[name="Codigo"]` | trigger |
| Editar Producto | `producto-editar-modal.js` | `#prod-edit-codigo` | trigger |
| Nueva Categoría | `categoria-crear-modal.js` | `input[name="Codigo"]` | trigger |
| Editar Categoría | `categoria-editar-modal.js` | `#cat-edit-codigo` | trigger |
| Nueva Marca | `marca-crear-modal.js` | `input[name="Codigo"]` | trigger |
| Editar Marca | `marca-editar-modal.js` | `#marca-edit-codigo` | trigger |
| Historial de Precio | `historial-precio-modal.js` | `[data-catalogo-modal-close="historial-precio"]` | trigger |
| Aumento de Precios | `precio-aumento-modal.js` | `#modal-aumento-precios-title` (tabindex="-1") | trigger |
| Comisión Vendedor | `producto-comision-modal.js` | `#comision-modal-porcentaje` (ya existía) | trigger |
| Movimientos Inventario | `movimientos-inventario-modal.js` | `#btn-cerrar-movimientos` | trigger |

## Estrategia de foco por tipo de modal

**Modales con formulario de alta/edición**: foco en el primer input (`Codigo`), que es el campo de ingreso inicial natural.

**Modales de solo lectura (Historial, Movimientos)**: foco en el botón de cerrar, que es el único control relevante en un panel informativo.

**Modal multi-paso (Aumento de Precios)**: foco en el `h2` título (`#modal-aumento-precios-title`) con `tabindex="-1"` dinámico. El modal tiene subpasos con controles dinámicos; el título es un ancla neutral y accesible.

**Modal Comisión**: ya tenía `currentBtn` como tracker del trigger. Solo se corrigió `closeModal()` para llamar `btn.focus()` antes de nullear.

## Cambios por archivo

### `catalogo-index.js`

Propagación del trigger a todos los `modalApi.open()`:

```js
// openCatalogoModal(name, trigger) ahora pasa trigger:
selectionPriceApi.openWithSelection(ids, trigger || null);
selectionPriceApi.open(trigger || null);
modalApi.open(productoId, trigger || null);
modalApi.open(trigger || null);
```

### `producto-crear-modal.js`

```js
let _openTrigger = null;

function open(trigger) {
    _openTrigger = (trigger instanceof Element) ? trigger : null;
    // ... show modal ...
    setTimeout(function () {
        var firstInput = document.querySelector('#form-nuevo-producto input[name="Codigo"]');
        if (firstInput) firstInput.focus();
    }, 50);
}

function close() {
    const trigger = _openTrigger;
    _openTrigger = null;
    // ... hide modal, resetForm ...
    if (trigger) trigger.focus();
}
```

### `categoria-crear-modal.js` / `marca-crear-modal.js`

Mismo patrón que `producto-crear-modal.js`, con los selectores correspondientes.

### `producto-editar-modal.js` / `categoria-editar-modal.js` / `marca-editar-modal.js`

```js
var _openTrigger = null;

function open(row, trigger) {
    currentRow = row;
    _openTrigger = (trigger instanceof Element) ? trigger : null;
    // ... show modal ...
    setTimeout(function () {
        var firstInput = el('prod-edit-codigo'); // o cat-edit-codigo, marca-edit-codigo
        if (firstInput) firstInput.focus();
    }, 50);
}

function close() {
    var trigger = _openTrigger;
    _openTrigger = null;
    // ... hide modal, clearErrors ...
    if (trigger) trigger.focus();
}

// En initDelegatedEvents:
open(row, editBtn); // era: open(row)
```

### `historial-precio-modal.js`

```js
let _openTrigger = null;

function open(productoId, trigger) {
    _openTrigger = (trigger instanceof Element) ? trigger : null;
    // ... show modal, fetchHistorial ...
    setTimeout(function () {
        var closeBtn = document.querySelector('[data-catalogo-modal-close="historial-precio"]');
        if (closeBtn) closeBtn.focus();
    }, 50);
}

function close() {
    var trigger = _openTrigger;
    _openTrigger = null;
    // ... hide modal ...
    if (trigger) trigger.focus();
}
```

### `precio-aumento-modal.js`

```js
let _openTrigger = null;

function open(trigger) {
    _openTrigger = (trigger instanceof Element) ? trigger : null;
    // ... show modal ...
    setTimeout(function () {
        var h2 = document.getElementById('modal-aumento-precios-title');
        if (h2) { h2.setAttribute('tabindex', '-1'); h2.focus(); }
    }, 50);
}

function openWithSelection(ids, trigger) {
    _openTrigger = (trigger instanceof Element) ? trigger : null;
    // mismo comportamiento de foco
}

function close() {
    var trigger = _openTrigger;
    _openTrigger = null;
    // ... hide modal ...
    if (trigger) trigger.focus();
}
```

### `producto-comision-modal.js`

Solo corrección de `closeModal()` (ya tenía `currentBtn`):

```js
function closeModal() {
    if (!modal) return;
    modal.classList.remove('flex');
    modal.classList.add('hidden');
    var btn = currentBtn;
    currentBtn = null;
    if (btn) btn.focus();
}
```

### `movimientos-inventario-modal.js`

```js
let _openTrigger = null;

// En btnOpen click: _openTrigger = btnOpen;
// En delegado [data-movimientos-producto-id]: _openTrigger = btn;

// En abrirModal():
setTimeout(function () {
    var closeBtn = document.getElementById('btn-cerrar-movimientos');
    if (closeBtn) closeBtn.focus();
}, 50);

// En closeModal():
var trigger = _openTrigger;
_openTrigger = null;
// ... hide modal ...
if (trigger) trigger.focus();
```

## Contratos preservados

- Sin cambios en `Views/Catalogo/Index_tw.cshtml`.
- Sin cambios en controllers, services, entidades ni migraciones.
- Sin cambios en CSS.
- Sin cambios en IDs, `name`, `data-*`, `asp-*`, antiforgery, endpoints ni payloads.
- Sin cambios en Escape (ya existía en todos desde MISA-CATALOGO-UX-1B).
- Sin cambios en lógica de submit / fetch / validación de formularios.
- `CatalogoModule.registerModalApi` / `getModalApi` sin cambios.

## Escape — verificación

Todos los modales ya tenían manejador `keydown Escape` que llama a `close()` / `closeModal()`. Al incorporar el retorno de foco en `close()`, Escape ahora también devuelve foco correctamente sin ningún cambio adicional.

## Archivos modificados

```
wwwroot/js/catalogo-index.js
wwwroot/js/producto-crear-modal.js
wwwroot/js/categoria-crear-modal.js
wwwroot/js/marca-crear-modal.js
wwwroot/js/historial-precio-modal.js
wwwroot/js/precio-aumento-modal.js
wwwroot/js/producto-editar-modal.js
wwwroot/js/categoria-editar-modal.js
wwwroot/js/marca-editar-modal.js
wwwroot/js/producto-comision-modal.js
wwwroot/js/movimientos-inventario-modal.js
```

## Validaciones ejecutadas

- `git diff --check`: sin whitespace errors.
- `git status --short`: solo archivos esperados modificados.
- `dotnet build --configuration Debug`: OK.
- `dotnet test --filter "LayoutUiContractTests"`: 57/57 OK.

## Rama

`misa/catalogo-ux-1e-focus-modales` → merge fast-forward a `main`.

## Deuda remanente

Ninguna abierta por esta fase.

## Próximo micro-lote sugerido

- MISA-CATALOGO-UX-1F: atrapado de foco dentro de los modales (focus trap) para navegación con Tab/Shift+Tab.
- O bien: auditoría UX de la vista de Ventas o Cotización si hay frentes abiertos.
