# MISA-CATALOGO-UX-1B — Semántica accesible de modales de Catálogo

## A. Objetivo

Agregar semántica accesible mínima (`role="dialog"`, `aria-modal="true"`, `aria-labelledby`) a los 10 modales existentes del módulo Catálogo en `Views/Catalogo/Index_tw.cshtml`.

## B. Base y contexto

- Fase anterior: MISA-CATALOGO-UX-1A (corregir color botón #btn-movimientos-inventario)
- Main en: `276344f — Corregir color de movimientos en catalogo (MISA-CATALOGO-UX-1A)`
- Rama de trabajo: `misa/catalogo-ux-1b-semantica-modales`

## C. Deuda tomada desde MISA-CATALOGO-UX-0

La auditoría UX-0 detectó que los modales del módulo Catálogo carecían de:
- `role="dialog"`
- `aria-modal="true"`
- `aria-labelledby` asociado a un título visible

## D. Archivos auditados

- `Views/Catalogo/Index_tw.cshtml` — modificado
- `wwwroot/js/catalogo-index.js` — solo lectura (sin modificar)
- `wwwroot/js/catalogo-module.js` — solo lectura (sin modificar)
- `docs/misa-catalogo-ux-0-auditoria.md` — referencia

## E. Mapa de modales encontrados

| # | ID del modal | Propósito | Línea outer div |
|---|---|---|---|
| 1 | `modal-nuevo-producto` | Alta de producto | 680 |
| 2 | `modal-editar-producto` | Edición de producto | 994 |
| 3 | `modal-nueva-categoria` | Alta de categoría | 1224 |
| 4 | `modal-editar-categoria` | Edición de categoría | 1370 |
| 5 | `modal-nueva-marca` | Alta de marca | 1477 |
| 6 | `modal-editar-marca` | Edición de marca | 1573 |
| 7 | `modal-comision-vendedor` | Asignar comisión vendedor | 1662 |
| 8 | `modal-historial-precio` | Historial de cambios de precio | 1735 |
| 9 | `modal-aumento-precios` | Ajuste masivo de precios (3 pasos) | 1830 |
| 10 | `modal-movimientos` | Movimientos de inventario | 2162 |

## F. Estado previo por modal

Todos los 10 modales carecían de:
- `role="dialog"` — ausente en los 10
- `aria-modal="true"` — ausente en los 10
- `aria-labelledby` — ausente en los 10

El único modal que tenía un id en el título era `modal-comision-vendedor`: su `<h2>` ya tenía `id="comision-modal-nombre"` (usado por JS para poblar el nombre del producto).

## G. Cambios aplicados por modal

### 1. modal-nuevo-producto
- Outer div: `role="dialog" aria-modal="true" aria-labelledby="modal-nuevo-producto-title"`
- `<h2>`: agregado `id="modal-nuevo-producto-title"`

### 2. modal-editar-producto
- Outer div: `role="dialog" aria-modal="true" aria-labelledby="modal-editar-producto-title"`
- `<h2>`: agregado `id="modal-editar-producto-title"`

### 3. modal-nueva-categoria
- Outer div: `role="dialog" aria-modal="true" aria-labelledby="modal-nueva-categoria-title"`
- `<h2>`: agregado `id="modal-nueva-categoria-title"`

### 4. modal-editar-categoria
- Outer div: `role="dialog" aria-modal="true" aria-labelledby="modal-editar-categoria-title"`
- `<h2>`: agregado `id="modal-editar-categoria-title"`

### 5. modal-nueva-marca
- Outer div: `role="dialog" aria-modal="true" aria-labelledby="modal-nueva-marca-title"`
- `<h2>`: agregado `id="modal-nueva-marca-title"`

### 6. modal-editar-marca
- Outer div: `role="dialog" aria-modal="true" aria-labelledby="modal-editar-marca-title"`
- `<h2>`: agregado `id="modal-editar-marca-title"`

### 7. modal-comision-vendedor
- Outer div: `role="dialog" aria-modal="true" aria-labelledby="comision-modal-nombre"`
- `<h2 id="comision-modal-nombre">` ya existía — sin cambio en el elemento de título.

### 8. modal-historial-precio
- Outer div: `role="dialog" aria-modal="true" aria-labelledby="modal-historial-precio-title"`
- `<h2>`: agregado `id="modal-historial-precio-title"`

### 9. modal-aumento-precios
- Outer div: `role="dialog" aria-modal="true" aria-labelledby="modal-aumento-precios-title"`
- `<h2>`: agregado `id="modal-aumento-precios-title"`

### 10. modal-movimientos
- Outer div: `role="dialog" aria-modal="true" aria-labelledby="modal-movimientos-title"`
- `<h1>` (único modal con h1): agregado `id="modal-movimientos-title"`

## H. Casos con aria-labelledby

Todos los modales usan `aria-labelledby` apuntando a su título visible:

| Modal | aria-labelledby | Texto del título |
|---|---|---|
| modal-nuevo-producto | `modal-nuevo-producto-title` | "Nuevo Producto" |
| modal-editar-producto | `modal-editar-producto-title` | "Editar Producto" |
| modal-nueva-categoria | `modal-nueva-categoria-title` | "Nueva Categoría" |
| modal-editar-categoria | `modal-editar-categoria-title` | "Editar Categoría" |
| modal-nueva-marca | `modal-nueva-marca-title` | "Nueva Marca" |
| modal-editar-marca | `modal-editar-marca-title` | "Editar Marca" |
| modal-comision-vendedor | `comision-modal-nombre` | Nombre del producto (dinámico vía JS) |
| modal-historial-precio | `modal-historial-precio-title` | "Cambios aplicados al producto" |
| modal-aumento-precios | `modal-aumento-precios-title` | "Actualizar precios por alcance" |
| modal-movimientos | `modal-movimientos-title` | "Movimientos de Inventario" |

## I. Casos con aria-label

Ninguno. Todos los modales tienen títulos visibles que sirven como label semántico.

## J. Contratos preservados

- IDs de todos los modales: sin cambio
- `data-catalogo-modal-close`, `data-prod-edit-modal-close`, `data-cat-edit-modal-close`, `data-marca-edit-modal-close`, `data-comision-modal-close` — sin cambio
- `id="comision-modal-nombre"` — sin cambio (id preexistente usado por JS)
- `asp-controller`, `asp-action`, antiforgery, inputs, names — sin cambio
- Clases CSS de todos los elementos — sin cambio
- Estructura HTML — sin cambio
- Orden de modales — sin cambio
- Estado hidden/show — sin cambio

## K. Qué no se tocó

- JS: ningún archivo de `wwwroot/js/`
- CSS: ningún archivo de `wwwroot/css/`
- Controllers, Services, ViewModels, Entities, Migrations — sin cambio
- Tabs, tablas, filtros, acciones por fila — sin cambio
- Formularios, inputs, selects — sin cambio
- Botones de cierre — sin cambio
- Permisos — sin cambio
- Lógica de modales — sin cambio

## L. Accesibilidad / baja visión

Los cambios aplicados permiten que:
- Lectores de pantalla (NVDA, JAWS, VoiceOver) anuncien correctamente el nombre del diálogo al abrirse.
- `aria-modal="true"` indica al AT que el contenido fuera del modal está inerte mientras el modal está activo.
- Todos los títulos son textos descriptivos del propósito del modal.
- El caso `comision-modal-nombre` usa un label dinámico (nombre del producto) lo que es semánticamente correcto: el AT anunciará "Nombre del producto - diálogo".

## M. Riesgo funcional

Riesgo: nulo.
- Los atributos ARIA agregados son de presentación semántica únicamente.
- No afectan el comportamiento del DOM ni la lógica de apertura/cierre.
- No afectan CSS ni clases.
- No afectan JS.

## N. Tests y validaciones

**Build:**
- `dotnet build --configuration Release -o tmpbuild_misa_catalogo_ux_1b`
- Resultado: **0 errores, 1 advertencia** (NETSDK1194 no relacionada al cambio)

**Tests unitarios:** No ejecutados. No existe suite de tests específica para Catálogo UiContract. Los cambios son solo atributos ARIA — no hay lógica de negocio ni contratos HTML/JS que verificar con tests.

**Temporales:** `tmpbuild_misa_catalogo_ux_1b/` generado por el build alternativo — no commiteado.

## O. Playwright

No ejecutado. Los cambios son solo atributos semánticos sin impacto visual ni estructural. No existe spec específico de Catálogo/modales detectado en la carpeta `e2e/`.

## P. Procesos

Build con output alternativo para evitar file-lock del proceso preexistente. El proceso no fue iniciado por esta tarea.

## Q. Deudas restantes

- `scope="col"` falta en las 3 tablas de Catálogo (Productos, Categorías, Marcas) — previsto en MISA-CATALOGO-UX-1C.
- Focus management en apertura/cierre de modales — no auditado en esta fase.
- Trap de foco dentro de modales — no auditado en esta fase.
- Tecla Escape para cierre — depende de JS existente, no modificado.
- Los 10 modales siguen sin `<dialog>` nativo de HTML5 — opción para fase futura si se decide migrar.

## R. Próximo paso recomendado

**MISA-CATALOGO-UX-1C — `scope="col"` en tablas de Catálogo**

Objetivo:
- Agregar `scope="col"` en las tres tablas de Catálogo (Productos, Categorías, Marcas).
- Preservar estructura, orden y contratos.
- No tocar JS, CSS ni backend.
