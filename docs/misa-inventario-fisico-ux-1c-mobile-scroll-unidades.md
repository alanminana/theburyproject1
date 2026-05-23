# MISA-INVENTARIO-FISICO-UX-1C — Mobile / scroll affordance en Producto/Unidades

## A. Objetivo

Mejorar la experiencia mobile y el affordance de scroll horizontal en `Views/Producto/Unidades.cshtml`, resolviendo deudas detectadas en la fase anterior sin cambiar lógica funcional, backend ni contratos JS.

## B. Base y contexto

- Base: `main` en commit `ad9e62a` (MISA-INVENTARIO-FISICO-UX-1B).
- Rama de trabajo: `misa/inventario-fisico-ux-1c-mobile-scroll-unidades`.
- Kira trabaja Ventas. Misa trabaja Inventario/Catálogo/Movimientos/Marcas/Categorías/Inventario físico.
- Ventas cerrada técnicamente hasta nuevo hallazgo.

## C. Deuda tomada desde MISA-INVENTARIO-FISICO-UX-1B

1. La tabla principal de unidades es ancha (9 columnas + acciones con `min-w-[18rem]`) y difícil de usar en mobile.
2. La tabla de preview de carga masiva carecía de scroll horizontal.
3. Faltaba affordance visual para indicar scroll horizontal disponible.
4. El link `href="#listado-unidades"` dentro de Conciliación (ahora posición 8) apuntaba hacia arriba (posición 4) con texto "Ver listado de unidades", que resultaba anti-intuitivo.

## D. Archivos auditados

- `Views/Producto/Unidades.cshtml` — archivo principal de la fase.
- `wwwroot/css/horizontal-scroll-affordance.css` — CSS del patrón `data-oc-scroll`.
- `wwwroot/js/horizontal-scroll-affordance.js` — JS del patrón `data-oc-scroll`.
- `wwwroot/js/layout.js` — no inicializa `data-oc-scroll` globalmente.
- `wwwroot/js/producto*.js`, `wwwroot/js/unidad*.js` — no existen módulos JS para Producto/Unidades.
- 32 vistas con `data-oc-scroll` — confirmación de uso extendido del patrón.
- `e2e/` — no existe spec específico para Producto/Unidades, mobile ni scroll.

## E. Hallazgos mobile

- La tabla principal tiene 9 columnas + columna de acciones con `min-w-[18rem]`. En mobile es muy ancha y no hay indicación visible de que se puede scrollear.
- La tabla ya tenía `overflow-x-auto` en el wrapper interno (línea 138 original). El scroll funciona, pero sin affordance el usuario no sabe que puede usarlo.
- La tabla de preview de carga masiva tenía `overflow-y-auto` (scroll vertical) pero no `overflow-x-auto`. Si el viewport es muy estrecho, la tabla quedaba sin scroll horizontal.

## F. Hallazgos del patrón data-oc-scroll

- El patrón `data-oc-scroll` está completamente implementado en CSS (`horizontal-scroll-affordance.css`) y JS (`horizontal-scroll-affordance.js`).
- **El patrón requiere inicialización JS explícita por módulo**. Cada página que lo usa (AlertaStock, Caja, Catálogo, etc.) tiene su propio módulo JS que llama `window.TheBury.initHorizontalScrollAffordance()`.
- No existe módulo JS para `Producto/Unidades`. `layout.js` no inicializa el patrón globalmente.
- Sin inicialización JS, el hint text quedaría permanentemente oculto (tiene atributo `hidden` y CSS `display: none !important`).
- Conclusión: usar hint estático visible en mobile con clase `lg:hidden`, sin JS y sin CSS nuevo. Solución mínima y equivalente en UX.

## G. Cambios aplicados

### Cambio 1 — Hint de scroll en tabla principal

Dentro de `<section id="listado-unidades">`, antes del `<div class="overflow-x-auto">`:

```html
<p class="px-4 pt-3 text-xs text-slate-500 lg:hidden" aria-hidden="true">Deslizá para ver más columnas →</p>
```

- Solo visible en pantallas menores a `lg` (1024px).
- `aria-hidden="true"` porque es ayuda visual, no información funcional.
- No altera estructura, contratos ni IDs existentes.

### Cambio 2 — Scroll horizontal en preview de carga masiva

```html
<!-- antes -->
<div class="max-h-72 overflow-y-auto">
<!-- después -->
<div class="max-h-72 overflow-x-auto overflow-y-auto">
```

- Agrega scroll horizontal al wrapper ya existente.
- La tabla de preview tiene 3 columnas (#, Serie, Estado inicial) — bajo riesgo, pero el viewport estrecho podría truncarla.

### Cambio 3 — Texto del link en Conciliación

```html
<!-- antes -->
<span class="material-symbols-outlined text-base">list_alt</span>
Ver listado de unidades

<!-- después -->
<span class="material-symbols-outlined text-base">arrow_upward</span>
Volver al listado de unidades
```

- Icono cambiado de `list_alt` a `arrow_upward` para indicar que el destino está arriba.
- Texto cambiado de "Ver listado de unidades" a "Volver al listado de unidades" para indicar dirección.
- El `href="#listado-unidades"` se preserva sin cambios.

## H. Tratamiento del link hacia #listado-unidades

Se eligió **Opción B**: cambiar el texto y el icono para indicar explícitamente la dirección. El link conserva su utilidad — desde el panel de Conciliación (posición 8 en la página) el usuario puede querer ir al listado (posición 4) para revisar unidades específicas antes de ajustar. El nuevo texto "Volver al listado de unidades" con ícono `arrow_upward` comunica con claridad que el destino está arriba.

## I. Contratos preservados

- `id="listado-unidades"` — preservado.
- `id="form-carga-masiva-unidades"` — preservado.
- `id="ajuste-asistido"` — preservado.
- Todos los `name`, `asp-for`, `asp-controller`, `asp-action`, `asp-route-*` — sin cambios.
- Todos los `@Html.AntiForgeryToken()` — sin cambios.
- Botones `name="CargaMasiva.Confirmar" value="false"` y `value="true"` — sin cambios.
- Partial `_EstadoUnidadBadge` — sin cambios.
- Todos los formularios POST — sin cambios.
- Todos los IDs de inputs (`motivo-faltante-@unidad.Id`, `motivo-baja-@unidad.Id`, etc.) — sin cambios.
- Labels `sr-only` de accesibilidad — sin cambios.

## J. Qué no se tocó

- Ningún controller.
- Ningún service.
- Ninguna entidad ni migración.
- Ningún ViewModel ni DTO.
- Ningún archivo JS.
- Ningún archivo CSS.
- Ningún endpoint ni payload.
- Ninguna regla de negocio.
- Ventas / Kira.
- Cotización.
- Caja, Crédito, stock funcional.
- `Views/Producto/UnidadesGlobal.cshtml`.
- `Views/Producto/UnidadHistorial.cshtml`.
- `Views/MovimientoStock/`.
- `Views/Catalogo/`.
- `Views/AlertaStock/`.
- Tests.
- Playwright specs.

## K. Accesibilidad / baja visión

- El hint de scroll usa `aria-hidden="true"` porque es decorativo/orientativo, no información crítica.
- El icono `arrow_upward` en el link de Conciliación refuerza la dirección visualmente.
- El texto "Volver al listado de unidades" es más descriptivo que "Ver listado de unidades" para lectores de pantalla.
- No se alteraron los `sr-only` labels ni los roles ARIA de la tabla.

## L. Riesgo funcional

Muy bajo. Los cambios son:
- Un párrafo HTML con `aria-hidden` visible solo en mobile.
- Una clase CSS adicional en un `div` wrapper.
- Texto e ícono de un link de navegación interna.

Ningún flujo funcional, cálculo, formulario ni endpoint fue modificado.

## M. Tests y validaciones

- Build: ejecutado. Ver sección N.
- Tests unitarios: no ejecutados. No se modificaron tests ni lógica de negocio.
- Tests de contrato UI: no hay contrato específico de Producto/Unidades que requiera actualización por estos cambios de hint/scroll/texto.

## N. Resultado de build

Ver resultado en sección de commit. Build Release ejecutado con `dotnet build --configuration Release`.

## O. Playwright

No ejecutado. No existe spec específico para Producto/Unidades, mobile ni scroll. No se tocaron flujos funcionales que estén cubiertos por specs existentes.

## P. Deudas restantes

1. **Acciones de fila** — columna Acciones tiene `min-w-[18rem]` con múltiples formularios en columna. En mobile resulta muy densa. Candidato para MISA-INVENTARIO-FISICO-UX-1D.
2. **Patrón data-oc-scroll completo** — si en el futuro se agrega un módulo JS de Producto/Unidades, se podría migrar al patrón con fade y hint dinámico. Por ahora el hint estático cumple el objetivo.
3. **Tabla preview de carga masiva** — solo 3 columnas. El scroll horizontal agregado es precautorio; en la práctica el riesgo es bajo.

## Q. Próximo paso recomendado

**MISA-INVENTARIO-FISICO-UX-1D** — Acciones de fila colapsables / reducción de densidad.

Objetivo tentativo:
- Reducir altura visual de filas con muchas acciones (columna Acciones actual tiene `min-w-[18rem]`).
- Ordenar acciones de historial, faltante, reintegro, baja y reparación.
- Evaluar patrón colapsable o agrupación progresiva.
- Sin backend, sin cambios funcionales, sin reemplazar modales por vistas.
