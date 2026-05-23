# MISA-CATALOGO-UX-1F — Focus trap en modales de Catálogo

## Estado

Cerrado. Implementado y commiteado.

## Objetivo

Implementar focus trap accesible (WCAG 2.4.3 / APG Dialog pattern) en los 10 modales del Catálogo:

- Tab desde el último elemento enfocable vuelve al primero.
- Shift+Tab desde el primer elemento enfocable vuelve al último.
- Shift+Tab desde elementos con `tabindex="-1"` (solo foco programático) también vuelve al último.
- Escape sigue cerrando como en MISA-CATALOGO-UX-1E.
- Al cerrar conserva el retorno de foco al trigger de MISA-CATALOGO-UX-1E.

## Estado inicial

Branch: `misa/catalogo-ux-1e-focus-modales` integrado a main en `213c3de`.

Los 10 modales ya tenían:
- `role="dialog" aria-modal="true" aria-labelledby="..."` (MISA-CATALOGO-UX-1B)
- Foco al abrir / retorno al trigger al cerrar (MISA-CATALOGO-UX-1E)
- Escape para cerrar en todos

No tenían: focus trap (Tab/Shift+Tab podían escapar el dialog).

## Modales auditados

| Modal | Archivo JS | Elementos enfocables representativos |
|---|---|---|
| Nuevo Producto | `producto-crear-modal.js` | inputs, selects, buttons, autocomplete buttons |
| Editar Producto | `producto-editar-modal.js` | inputs, selects, buttons, textarea |
| Nueva Categoría | `categoria-crear-modal.js` | inputs, select, checkbox, buttons |
| Editar Categoría | `categoria-editar-modal.js` | inputs, select, checkbox, buttons |
| Nueva Marca | `marca-crear-modal.js` | inputs, select, checkbox, buttons |
| Editar Marca | `marca-editar-modal.js` | inputs, select, checkbox, buttons |
| Historial de Precio | `historial-precio-modal.js` | botón cerrar (panel read-only) |
| Aumento de Precios | `precio-aumento-modal.js` | radios, selects, inputs, buttons; pasos ocultos excluidos por `offsetParent` |
| Comisión Vendedor | `producto-comision-modal.js` | input porcentaje, botón guardar, botón cerrar |
| Movimientos Inventario | `movimientos-inventario-modal.js` | botón cerrar, filtros, botones paginación |

## Estrategia

### Utilidad compartida en `catalogo-module.js`

Una sola función `CatalogoModule.trapFocus(modal, event)` disponible para todos los modales. Carga antes que cualquier modal JS.

```js
window.CatalogoModule.trapFocus = function (modal, e) {
    if (e.key !== 'Tab') return;
    var focusable = Array.prototype.filter.call(
        modal.querySelectorAll(
            'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'
        ),
        function (node) { return node.offsetParent !== null; }
    );
    if (!focusable.length) return;
    var first = focusable[0];
    var last = focusable[focusable.length - 1];
    var active = document.activeElement;
    if (e.shiftKey) {
        if (active === first || !modal.contains(active) || focusable.indexOf(active) === -1) {
            e.preventDefault();
            last.focus();
        }
    } else {
        if (active === last || !modal.contains(active)) {
            e.preventDefault();
            first.focus();
        }
    }
};
```

**Selector:** Incluye elementos nativamente enfocables no deshabilitados, más `[tabindex]:not([tabindex="-1"])` para elementos con tabindex explícito ≥ 0. Excluye `tabindex="-1"` (solo foco programático, como el `h2` del modal de Aumento de Precios).

**Filtro `offsetParent !== null`:** Excluye elementos ocultos (paneles de pasos no activos, estados de carga, empty states). Previene que elementos de pasos ocultos en el modal de Aumento de Precios queden atrapados en el ciclo.

**Condición Shift+Tab `focusable.indexOf(active) === -1`:** Cuando el foco está en un elemento con `tabindex="-1"` (e.g., `h2` del modal de Aumento de Precios) y el usuario presiona Shift+Tab, redirige al último elemento en lugar de escapar el modal.

### Cada modal: una línea en el `keydown` listener existente

```js
if (e.key === 'Tab' && window.CatalogoModule) window.CatalogoModule.trapFocus(modal, e);
```

Los `keydown` listeners también fueron refactorizados para mayor claridad: early-return si el modal no está visible, en lugar de condición inline.

## Archivos modificados

```
wwwroot/js/catalogo-module.js         ← función trapFocus agregada
wwwroot/js/producto-crear-modal.js    ← initEscKey() actualizado
wwwroot/js/categoria-crear-modal.js   ← initEscKey() actualizado
wwwroot/js/marca-crear-modal.js       ← initEscKey() actualizado
wwwroot/js/historial-precio-modal.js  ← keydown listener actualizado
wwwroot/js/precio-aumento-modal.js    ← keydown listener actualizado
wwwroot/js/movimientos-inventario-modal.js ← keydown listener actualizado
wwwroot/js/producto-editar-modal.js   ← keydown listener actualizado
wwwroot/js/categoria-editar-modal.js  ← keydown listener actualizado
wwwroot/js/marca-editar-modal.js      ← keydown listener actualizado
wwwroot/js/producto-comision-modal.js ← keydown listener actualizado
```

No se modificó `Views/Catalogo/Index_tw.cshtml`.

## Contratos preservados

- IDs, `name`, `data-*`, `asp-*`, antiforgery, endpoints, payloads: sin cambios.
- Retorno de foco al trigger al cerrar (MISA-CATALOGO-UX-1E): preservado.
- Foco al primer control al abrir (MISA-CATALOGO-UX-1E): preservado.
- Escape cierra el modal: preservado en todos.
- Submit / fetch / validación: sin cambios.
- `CatalogoModule.registerModalApi` / `getModalApi`: sin cambios.
- Sin cambios en CSS, controllers, services, entidades ni migraciones.

## Validaciones ejecutadas

- `git diff --check` en archivos commiteados: sin errores.
- `git status --short`: solo archivos esperados.
- Build Debug: 0 errores, 0 advertencias.
- `LayoutUiContractTests`: 57/57 OK.

## Riesgos y deudas

- **`precio-aumento-modal.js` Tab desde `h2[tabindex="-1"]`**: cuando el foco inicial está en el `h2` y el usuario presiona Tab (no Shift+Tab), el browser mueve el foco naturalmente al primer elemento real en el modal (dentro del modal). Comportamiento correcto sin intervención. No se consideró un riesgo.
- **Modales simultáneos**: no ocurre en este ERP. Si ocurriera, el listener de cada modal verifica su propio `hidden` antes de actuar.
- **Elementos en `display:contents`**: `offsetParent === null` puede fallar en estos casos, pero no se usan en los modales actuales.

## Rama y commit

`misa/catalogo-ux-1f-focus-trap-modales` → merge fast-forward a `main`.

## Próximo micro-lote sugerido

- Auditoría UX de la vista de Ventas.
- O bien: MISA-CATALOGO-UX-1G si se requiere aria-live para mensajes de error/éxito dentro de los modales.
