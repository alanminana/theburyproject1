# MISA-MOVIMIENTOS-QA - QA final de Movimientos / Kardex / modal Movimientos

## A. Objetivo

Cerrar QA final del frente Movimientos / Kardex / modal Movimientos y decidir si el modulo queda cerrado o si hace falta abrir `MISA-MOVIMIENTOS-UX-1E`.

Esta fase fue audit-only. No se implementaron cambios productivos.

## B. Base y contexto

- Base de trabajo: `main` en `fcaf3bd` - `Aclarar export no disponible en movimientos (MISA-MOVIMIENTOS-UX-1D)`.
- Rama de trabajo: `misa/movimientos-qa-final`.
- Frente auditado: Inventario / Movimientos / Kardex / modal Movimientos en Catalogo.
- App local detectada: `TheBuryProyect.exe` preexistente en `127.0.0.1:5187`.
- QA HTTP liviano: `/MovimientoStock` y `/AlertaStock` responden `302` hacia login sin sesion activa. No se levanto servidor nuevo.

## C. Fases revisadas

- `MISA-MOVIMIENTOS-UX-0` - auditoria inicial.
- `MISA-MOVIMIENTOS-UX-1A` - accesibilidad semantica.
- `MISA-MOVIMIENTOS-UX-1B` - copy, contraste y navegacion.
- `MISA-MOVIMIENTOS-UX-1C` - mobile / scroll affordance.
- `MISA-MOVIMIENTOS-UX-1D` - PDF/Excel visual fix.

## D. Archivos auditados

- `Views/Catalogo/Index_tw.cshtml`
- `Views/MovimientoStock/Index_tw.cshtml`
- `Views/MovimientoStock/Kardex_tw.cshtml`
- `Views/AlertaStock/Index_tw.cshtml`
- `docs/misa-movimientos-ux-0-auditoria.md`
- `docs/misa-movimientos-ux-1a-accesibilidad-semantica.md`
- `docs/misa-movimientos-ux-1b-copy-jerarquia.md`
- `docs/misa-movimientos-ux-1c-mobile-scroll.md`
- `docs/misa-movimientos-ux-1d-pdf-excel-visual-fix.md`

## E. Resultado modal Movimientos

Resultado: correcto.

- `#modal-movimientos` conserva `role="dialog"`, `aria-modal="true"` y `aria-labelledby="modal-movimientos-title"`.
- El titulo del modal usa `<h2 id="modal-movimientos-title">`.
- Los filtros principales conservan IDs y labels asociados:
  - `mov-fecha-desde`
  - `mov-tipo`
  - `mov-producto`
  - `mov-fuente-costo`
- La tabla del modal conserva `scope="col"` en sus cabeceras.
- Los botones PDF y Excel estan deshabilitados con `disabled`, `aria-disabled="true"`, `cursor-not-allowed`, baja opacidad y `title` explicativo.
- No se detecto reintroduccion de handlers, endpoints o payloads de export inexistentes.

Observacion menor: el label "Rango de Fechas" apunta a `mov-fecha-desde`; `mov-fecha-hasta` queda dentro del mismo grupo visual sin label propio. Es aceptable para el cierre actual porque la deuda critica de labels sin `for` quedo resuelta y no se cambio markup funcional.

## F. Resultado MovimientoStock/Index

Resultado: correcto.

- `TempData["Success"]` usa `role="status"`.
- `TempData["Error"]` usa `role="alert"`.
- La tabla mantiene `scope="col"` en sus cabeceras.
- `Saldo post.` conserva `title="Stock resultante despues del movimiento"`.
- Los filtros conservan `asp-controller="MovimientoStock"`, `asp-action="Index"` y nombres esperados: `ProductoId`, `Tipo`, `FechaDesde`, `FechaHasta`.
- El link por fila a Kardex conserva `asp-controller="MovimientoStock"`, `asp-action="Kardex"` y `asp-route-id="@m.ProductoId"`.

## G. Resultado Kardex

Resultado: correcto.

- `TempData["Success"]` usa `role="status"`.
- `TempData["Error"]` usa `role="alert"`.
- La tabla mantiene `scope="col"` en sus cabeceras.
- `Saldo post.` conserva `title="Stock resultante despues del movimiento"`.
- Existe link de vuelta a Catalogo.
- Existe link de vuelta a Movimientos con `asp-controller="MovimientoStock"` y `asp-action="Index"`.
- El boton `Registrar ajuste` conserva `asp-controller="MovimientoStock"`, `asp-action="Create"` y `asp-route-productoId="@producto.Id"`.

## H. Resultado AlertaStock

Resultado: correcto para la integracion pedida con Movimientos, con observacion propia de AlertaStock.

- Existe link por fila a Kardex con `asp-controller="MovimientoStock"`, `asp-action="Kardex"` y `asp-route-id="@a.ProductoId"`.
- El link incluye `title="Ver kardex"` y `aria-label="Ver kardex de @a.ProductoNombre"`.
- La tabla conserva el patron `data-oc-scroll` ya existente.
- Observacion: las cabeceras de la tabla de `AlertaStock/Index_tw.cshtml` siguen sin `scope="col"`. Esto no bloquea el cierre de Movimientos porque pertenece al frente AlertaStock y ya queda mejor tratado en una fase dedicada `MISA-ALERTASTOCK-UX-0`.

## I. Resultado mobile / scroll

Resultado: correcto.

- `MovimientoStock/Index_tw.cshtml` carga `horizontal-scroll-affordance.css` y `horizontal-scroll-affordance.js`.
- `MovimientoStock/Index_tw.cshtml` usa `data-oc-scroll`, `data-oc-scroll-shell`, fades laterales, `data-oc-scroll-region`, `tabindex="0"`, `role="region"` y `aria-label`.
- `MovimientoStock/Kardex_tw.cshtml` replica el mismo patron con ancho minimo propio de Kardex.
- No se detectaron cambios de columnas, datos ni filtros asociados al scroll.

## J. Resultado accesibilidad

Resultado: correcto para Movimientos/Kardex/modal.

- Dialog semantico preservado en modal Movimientos.
- Titulo del modal en `h2`.
- Labels de filtros del modal asociados por `for`.
- Cabeceras de tablas principales con `scope="col"`.
- Mensajes de exito/error con roles accesibles en Index y Kardex.
- Regiones de scroll focusables con `tabindex="0"` y `role="region"`.

## K. Resultado PDF/Excel visual fix

Resultado: correcto.

- PDF y Excel no parecen acciones activas.
- Los botones estan visual y semanticamente deshabilitados.
- Se conserva el texto visible `PDF` / `Excel` para no alterar la estructura del modal.
- La implementacion funcional real de export queda fuera de esta fase y, si se decide, corresponde a una fase funcional separada.

## L. Contratos preservados

Por lectura de codigo, se preservan:

- `asp-*` de filtros y navegacion.
- IDs del modal y filtros (`modal-movimientos`, `modal-movimientos-title`, `mov-*`).
- `data-movimientos-producto-id` y `data-movimientos-producto-nombre` en Catalogo.
- Nombres de filtros de Movimientos (`ProductoId`, `Tipo`, `FechaDesde`, `FechaHasta`).
- Columnas de tablas de Movimientos, Kardex y modal.
- Rutas `MovimientoStock/Index`, `MovimientoStock/Kardex/{id}` y `MovimientoStock/Create`.
- Payloads y endpoints existentes.
- Permisos y reglas de stock, porque no se tocaron backend, controllers, services, modelos ni migraciones.

## M. Errores encontrados

No se encontraron errores bloqueantes en Movimientos / Kardex / modal Movimientos.

No se detectaron regresiones en las mejoras de `UX-1A`, `UX-1B`, `UX-1C` ni `UX-1D` por lectura de codigo.

## N. Observaciones

- La app local estaba disponible como proceso preexistente, pero sin sesion activa las rutas auditadas redirigen a login. Por eso la QA final se baso en lectura de codigo.
- Los filtros temporales siguen siendo una mejora de orientacion operativa, no un bloqueo del modulo.
- `AlertaStock/Index_tw.cshtml` mantiene deuda semantica propia: cabeceras sin `scope="col"`.

## O. Decision final A/B/C

Decision: **B - Movimientos cerrado con observaciones**.

El frente Movimientos / Kardex / modal Movimientos queda cerrado a nivel UX inmediato. Las observaciones restantes no bloquean uso diario ni requieren reabrir el frente de Movimientos ahora.

## P. Decision sobre MISA-MOVIMIENTOS-UX-1E

No abrir `MISA-MOVIMIENTOS-UX-1E` por ahora.

Motivo: los filtros temporales pueden mejorar la orientacion diaria, pero no aparecen como requisito indispensable para operar. Quedan como backlog, a priorizar solo si producto confirma que revisar "hoy", "ultimos 7 dias" o periodos cerrados es parte central del uso diario.

## Q. Deudas restantes

- Backlog Movimientos: filtros temporales/defaults en Index/Kardex si producto los vuelve necesarios.
- Backlog funcional: export real PDF/Excel si se decide implementarlo.
- Backlog AlertaStock: fase propia para semantica de tabla, contraste, acciones y navegacion completa.
- QA visual autenticada queda pendiente si se quiere evidencia manual con sesion real, datos y capturas.

## R. Proximo frente recomendado

Recomendado: `MISA-INVENTARIO-FISICO-UX-2B - Tabs internos Producto/Unidades`.

Alternativa si se quiere cerrar el modulo completo de stock antes de avanzar: `MISA-ALERTASTOCK-UX-0`.
