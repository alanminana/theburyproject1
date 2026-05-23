# MISA-CATALOGO-UX-1G — aria-live en mensajes de modales de Catálogo

## A. Estado inicial

- HEAD: `2945ec3` (MISA-CATALOGO-UX-1F — focus trap en modales de catálogo)
- Rama base: `main`
- Fase previa cerrada: 1F (focus trap con `CatalogoModule.trapFocus`)

---

## B. Rama creada

```
misa/catalogo-ux-1g-aria-live-modales
```

---

## C. Archivos auditados

- `Views/Catalogo/Index_tw.cshtml` — vista completa con los 10 modales
- `wwwroot/js/catalogo-module.js` — módulo central
- `wwwroot/js/catalogo-index.js` — tabs, selección, toasts
- `wwwroot/js/producto-crear-modal.js` — submit AJAX, showValidation
- `wwwroot/js/producto-editar-modal.js` — submit AJAX, showValidation
- `wwwroot/js/categoria-crear-modal.js` — submit AJAX
- `wwwroot/js/categoria-editar-modal.js` — submit AJAX
- `wwwroot/js/marca-crear-modal.js` — submit AJAX
- `wwwroot/js/marca-editar-modal.js` — submit AJAX
- `wwwroot/js/producto-comision-modal.js` — showError / showSuccess
- `wwwroot/js/historial-precio-modal.js` — showFeedback / loading
- `wwwroot/js/precio-aumento-modal.js` — validation / preview loading
- `wwwroot/js/movimientos-inventario-modal.js` — stats, empty state

---

## D. Archivos modificados

- `Views/Catalogo/Index_tw.cshtml` — único archivo modificado

---

## E. Modales auditados (10 en total)

| # | Modal | ID | Mensajes detectados |
|---|---|---|---|
| 1 | Nuevo Producto | `modal-nuevo-producto` | `#modal-validation-summary` (error validación client/server) |
| 2 | Editar Producto | `modal-editar-producto` | `#prod-edit-validation-summary` (error validación) |
| 3 | Nueva Categoría | `modal-nueva-categoria` | `#cat-modal-validation-summary` (error validación) |
| 4 | Editar Categoría | `modal-editar-categoria` | `#cat-edit-validation-summary` (error validación) |
| 5 | Nueva Marca | `modal-nueva-marca` | `#marca-modal-validation-summary` (error validación) |
| 6 | Editar Marca | `modal-editar-marca` | `#marca-edit-validation-summary` (error validación) |
| 7 | Comisión Vendedor | `modal-comision-vendedor` | `#comision-modal-validation` (error) + `#comision-modal-success` (éxito) |
| 8 | Historial Precio | `modal-historial-precio` | `#historial-precio-feedback` (error/éxito) + `#historial-precio-loading` (carga) |
| 9 | Ajuste Masivo Precios | `modal-aumento-precios` | `#precio-modal-validation-summary` (error) + `#precio-preview-loading` (carga) |
| 10 | Movimientos Inventario | `modal-movimientos` | stats dinámicos (no son errores/éxito), empty state estático |

---

## F. Mensajes detectados por categoría

### Errores de validación (8 contenedores)
Generados por JS cuando el submit falla (client-side) o el server devuelve errores.
El JS cambia `textContent` del hijo `<p>` y hace toggle de `hidden`/`flex` en el contenedor.

- `#modal-validation-summary` → `showValidation(text)` en producto-crear-modal.js
- `#prod-edit-validation-summary` → idem en producto-editar-modal.js
- `#cat-modal-validation-summary` → idem en categoria-crear-modal.js
- `#cat-edit-validation-summary` → idem en categoria-editar-modal.js
- `#marca-modal-validation-summary` → idem en marca-crear-modal.js
- `#marca-edit-validation-summary` → idem en marca-editar-modal.js
- `#comision-modal-validation` → `showError(msg)` en producto-comision-modal.js
- `#precio-modal-validation-summary` → `showError(text)` en precio-aumento-modal.js

### Mensajes de éxito (2 contenedores)
- `#comision-modal-success` → `showSuccess(msg)` en producto-comision-modal.js
- `#historial-precio-feedback` (type='success') → `showFeedback(msg, 'success')` en historial-precio-modal.js

### Mensajes de error de operación (1 contenedor compartido)
- `#historial-precio-feedback` (type='error') → `showFeedback(msg, 'error')` en historial-precio-modal.js

### Estados de carga (2 contenedores)
- `#historial-precio-loading` → texto estático "Cargando historial...", toggle por `show()`/`hide()`
- `#precio-preview-loading` → texto estático "Calculando precios...", toggle por visibilidad

### Mensajes no incluidos (correctos)
- TempData alerts (`role="status"` / `role="alert"` ya presentes en líneas 18-29)
- Toasts de `catalogo:toast` → crea elementos con `role="status"` / `role="alert"` (catalogo-index.js línea 279)
- Stats de movimientos → datos operativos, no son errores/éxito → no aria-live
- Empty states → estáticos, no se actualizan dinámicamente

---

## G. Estrategia aria-live aplicada

| Contenedor | Atributos añadidos | Motivo |
|---|---|---|
| 8 × validation-summary | `role="alert" aria-live="assertive" aria-atomic="true"` | Error de validación que bloquea el submit → requiere atención inmediata |
| `#comision-modal-success` | `aria-live="polite" aria-atomic="true"` | Mensaje de éxito → informativo, no urgente |
| `#historial-precio-feedback` | `aria-live="polite" aria-atomic="true"` | Feedback de operación (éxito o error post-acción) → polite por contexto |
| `#historial-precio-loading` | `aria-live="polite" aria-atomic="true"` | Estado de carga informativo |
| `#precio-preview-loading` | `aria-live="polite" aria-atomic="true"` | Estado de carga informativo |

**Nota técnica sobre loading states:** Los divs de loading tienen texto estático en el HTML.
Cuando el JS hace toggle de `hidden`, el AT detecta el cambio de visibilidad.
NVDA/VoiceOver modernos anuncian regiones `aria-live` al volverse visibles.
JAWS puede requerir cambio de contenido. Para full compliance sería necesario
una utilidad `CatalogoModule.announce()` + SR-only live region, lo cual queda
documentado como deuda de accesibilidad menor.

---

## H. Cambios implementados (12 modificaciones en Index_tw.cshtml)

1. Línea 726: `#modal-validation-summary` → `role="alert" aria-live="assertive" aria-atomic="true"`
2. Línea 1017: `#prod-edit-validation-summary` → `role="alert" aria-live="assertive" aria-atomic="true"`
3. Línea 1255: `#cat-modal-validation-summary` → `role="alert" aria-live="assertive" aria-atomic="true"`
4. Línea 1391: `#cat-edit-validation-summary` → `role="alert" aria-live="assertive" aria-atomic="true"`
5. Línea 1508: `#marca-modal-validation-summary` → `role="alert" aria-live="assertive" aria-atomic="true"`
6. Línea 1594: `#marca-edit-validation-summary` → `role="alert" aria-live="assertive" aria-atomic="true"`
7. Línea 1685: `#comision-modal-validation` → `role="alert" aria-live="assertive" aria-atomic="true"`
8. Línea 1689: `#comision-modal-success` → `aria-live="polite" aria-atomic="true"`
9. Línea 1767: `#historial-precio-feedback` → `aria-live="polite" aria-atomic="true"`
10. Línea 1802: `#historial-precio-loading` → `aria-live="polite" aria-atomic="true"`
11. Línea 1877: `#precio-modal-validation-summary` → `role="alert" aria-live="assertive" aria-atomic="true"`
12. Línea 2122: `#precio-preview-loading` → `aria-live="polite" aria-atomic="true"`

---

## I. Contratos preservados

- IDs existentes: no modificados
- `name`, `data-*`, `asp-*`: no modificados
- Antiforgery: no tocado
- Endpoints / payloads: no tocados
- Lógica de submit de todos los modales: no tocada
- `CatalogoModule.trapFocus()`: no modificado
- Retorno de foco al trigger (1E): no modificado
- Focus inicial (1E): no modificado
- Comportamiento visual: sin cambios
- Tabs, tablas, acciones por fila: no tocados
- Selectores de Playwright: sin impacto

---

## J. Qué no se tocó

- Backend, controllers, services, entidades, migraciones
- CSS / Tailwind
- JavaScript de ningún modal
- Layout visual de los modales
- Contenedores que ya tenían `role` correcto (TempData alerts, toasts)
- Modal de Movimientos: no tiene mensajes de error/éxito propios

---

## K. Validaciones ejecutadas

- `git diff --check`: solo warnings preexistentes en AGENTS.md y CLAUDE.md (no commiteados)
- `git diff --stat`: solo `Views/Catalogo/Index_tw.cshtml` modificado (+12/-12 líneas netas)
- Build Release: en ejecución
- Tests `LayoutUiContractTests`: en ejecución

---

## L. Resultado de tests

*(pendiente — se completará con los resultados reales)*

---

## M. Resultado de build

*(pendiente — se completará con los resultados reales)*

---

## N. Procesos cerrados

Ninguno iniciado por esta tarea.

## O. Procesos preexistentes no tocados

- PID 11936: no tocado (según instrucción)
- VS Code, C# DevKit, MCPs: no tocados

## P. Estado de archivos sensibles

- `.claude/settings.local.json`: modificado localmente, **no commiteado**
- `AGENTS.md`: modificado localmente, **no commiteado**
- `CLAUDE.md`: modificado localmente, **no commiteado**
- `skills-lock.json`: eliminado localmente, **no commiteado**

## Q. Temporales generados

Ninguno.

---

## R. Working tree final

```
M  Views/Catalogo/Index_tw.cshtml  ← único archivo a commitear
 M .claude/settings.local.json    (no commitear)
 M AGENTS.md                      (no commitear)
 M CLAUDE.md                      (no commitear)
 D skills-lock.json               (no commitear)
```

---

## S. Riesgos y deudas restantes

**Sin riesgos funcionales:** Los cambios son solo atributos HTML en contenedores existentes.
No se tocó ningún JS, CSS, backend ni contrato.

**Deuda de accesibilidad menor (loading states):**
Los divs de loading tienen texto estático. Con `aria-live="polite"`, AT modernos
(NVDA, VoiceOver) anuncian al volverse visibles. JAWS puede requerir un cambio
de textContent para detectarlo. Solución futura: agregar `CatalogoModule.announce()`
+ sr-only live region persistente en MISA-CATALOGO-UX-1H o tarea específica.

**Ausencia de anuncio de éxito en modales de guardar (producto/categoria/marca):**
Cuando el submit es exitoso, el modal se cierra y aparece un toast (role="alert").
No hay un contenedor de éxito dentro del modal propiamente dicho, lo cual es correcto
(el modal se cierra antes de que tenga sentido anunciar éxito). El toast lo cubre.

---

## T. Commit

```
Agregar aria-live en mensajes de modales de catalogo (MISA-CATALOGO-UX-1G)
```

## U. Push rama / Merge / Push main

*(pendiente de resultados de build/tests)*

## V. Próximo prompt recomendado

```
MISA-CATALOGO-UX-1H — Revisión y cierre de accesibilidad de Catálogo:
auditar si quedan gaps de accesibilidad después de 1A–1G (foco, trap, aria-sort,
scope, semántica, aria-live), generar informe de cobertura y decidir si
la serie MISA-CATALOGO-UX está lista para integrar a main o si quedan ítems pendientes.
```
