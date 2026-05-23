# MISA-INVENTARIO-FISICO-UX-2F - QA visual y contratos de Producto/Unidades

## A. Objetivo

Hacer QA visual y de contratos de `Views/Producto/Unidades.cshtml` despues de las fases 2B-2E, sin implementar cambios productivos.

La fase busca decidir si Producto/Unidades queda cerrado para este ciclo o si requiere una microfase adicional.

## B. Base y contexto

- Base indicada: `main` en `5b94006` - MISA-INVENTARIO-FISICO-UX-2E integrada.
- Rama de trabajo: `misa/inventario-fisico-ux-2f-qa-producto-unidades`.
- Tipo de fase: QA / audit-only / documentacion.
- HEAD verificado antes de crear la rama: `5b94006`.
- `git pull --ff-only`: sin cambios, rama base actualizada.
- App local: `http://localhost:5187/` respondio y redirigio a login. `http://localhost:5187/Producto/Unidades/` respondio 404 por falta de producto/id. `http://localhost:5187/Producto/Unidades/1` respondio 200 redirigiendo a login (`/Identity/Account/Login?ReturnUrl=/Producto/Unidades/1`). No se hizo QA visual autenticado por falta de sesion/producto valido confirmado.

## C. Fases revisadas

- `docs/misa-inventario-fisico-ux-2a-arquitectura-producto-unidades.md`
- `docs/misa-inventario-fisico-ux-2b-tabs-unidades.md`
- `docs/misa-inventario-fisico-ux-2c-acciones-unidad.md`
- `docs/misa-inventario-fisico-ux-2d-alta-carga.md`
- `docs/misa-inventario-fisico-ux-2e-conciliacion.md`
- `docs/misa-inventario-fisico-ux-qa-producto-unidades.md`

## D. Archivos auditados

- `Views/Producto/Unidades.cshtml`
- `docs/misa-inventario-fisico-ux-2a-arquitectura-producto-unidades.md`
- `docs/misa-inventario-fisico-ux-2b-tabs-unidades.md`
- `docs/misa-inventario-fisico-ux-2c-acciones-unidad.md`
- `docs/misa-inventario-fisico-ux-2d-alta-carga.md`
- `docs/misa-inventario-fisico-ux-2e-conciliacion.md`
- `docs/misa-inventario-fisico-ux-qa-producto-unidades.md`
- Tests existentes relacionados, solo lectura:
  - `TheBuryProyect.Tests/Integration/ProductoControllerPrecioTests.cs`
  - `TheBuryProyect.Tests/Integration/ProductoUnidadServiceTests.cs`
  - `TheBuryProyect.Tests/Integration/ConciliacionStockUnidadesTests.cs`
  - `TheBuryProyect.Tests/Integration/ProductoUnidadReparacionE2ETests.cs`
  - tests de Venta/Devolucion que consumen `ProductoUnidadId`

## E. Resultado navegacion interna

Resultado: correcto.

La navegacion interna por modos existe y conserva los anchors pedidos:

- `#modo-unidades`
- `#modo-carga`
- `#modo-conciliacion`
- `#modo-configuracion`
- `#listado-unidades`
- `#form-carga-masiva-unidades`
- `#ajuste-asistido`

El nav superior usa anclas simples, sin JS nuevo, y separa los modos de trabajo principales: Unidades, Carga, Conciliacion y Configuracion. Los links cruzados vuelven al listado, llevan a carga masiva o dirigen al ajuste asistido sin cambiar rutas ni payloads.

## F. Resultado modo Unidades

Resultado: aprobado.

El modo Unidades queda como foco operativo principal:

- Header del producto con nombre, codigo, stock agregado, badge de trazabilidad y link a Kardex SKU.
- Resumen de unidades fisicas.
- Filtros GET por texto, estado y checks operativos.
- Listado principal con `id="listado-unidades"`.
- Tabla con `scope="col"` en encabezados.
- Estado renderizado mediante partial `_EstadoUnidadBadge`.
- Mensaje vacio claro cuando no hay resultados.

El listado sigue usando tabla ancha con scroll horizontal, lo cual es funcional y consistente con fases anteriores. No se detecto perdida de jerarquia respecto de 2B-2E.

## G. Resultado acciones por unidad

Resultado: aprobado con observacion menor.

2C reemplazo el bloque inline de acciones por un boton `Gestionar unidad` que abre `<dialog>` nativo por fila. En la lectura actual:

- `Ver historial` queda siempre visible.
- `Gestionar unidad` aparece solo cuando la unidad tiene acciones disponibles.
- `<dialog>` tiene `role="dialog"`, `aria-modal="true"` y `aria-labelledby`.
- El cierre usa `form method="dialog"`.
- Los formularios POST no estan anidados entre si.
- Los motivos conservan labels `sr-only`, `maxlength="500"` y `required`.

Observacion: al usar `onclick="document.getElementById(...).showModal()"`, la interaccion depende de soporte moderno de `<dialog>`. Es aceptable para esta fase y no cambia contratos, pero sigue sin cobertura Playwright especifica.

## H. Resultado modo Carga

Resultado: aprobado.

2D dejo el modo Carga separado en dos paneles:

- Alta individual: formulario `POST Producto/CrearUnidad`.
- Carga masiva: formulario `POST Producto/CrearUnidadesMasivas` con `id="form-carga-masiva-unidades"`.

La carga masiva mantiene el flujo:

- preparar datos;
- previsualizar con `CargaMasiva.Confirmar=false`;
- confirmar con `CargaMasiva.Confirmar=true` solo si `PreviewListo` y hay preview;
- preview en tabla con scroll y `_EstadoUnidadBadge`.

Se conserva el mensaje contractual: crear/cargar unidades fisicas no ajusta el stock agregado.

## I. Resultado modo Conciliacion

Resultado: aprobado.

2E dejo la conciliacion enfocada como panel de decision:

- Explica stock agregado, unidades fisicas y diferencia.
- Muestra estado principal: conciliado, diferencia o requiere revision.
- Separa KPIs primarios del desglose informativo.
- Mantiene links hacia listado y carga.
- El bloque `#ajuste-asistido` se renderiza solo si corresponde.
- Los ajustes sensibles conservan motivo obligatorio y POST tradicional.

No se detecto cambio de autoridad de negocio: la vista informa y guia, pero no decide reglas finales. Los ajustes siguen dependiendo del backend y recalculo servidor.

## J. Resultado modo Configuracion

Resultado: aprobado.

El modo Configuracion aisla trazabilidad individual:

- Badge visible de Activada/Desactivada.
- Acciones `ActivarTrazabilidad` y `DesactivarTrazabilidad` conservan POST y antiforgery.
- La desactivacion queda bloqueada si hay unidades activas, con `aria-disabled="true"` y `title`.
- El copy aclara impacto en venta sin mezclarlo con carga o conciliacion.

No se detectaron cambios de permisos, endpoints ni reglas de venta.

## K. Resultado mobile

Resultado: cerrado con observaciones.

Desde lectura de markup, mobile queda funcional:

- Nav de modos con `overflow-x-auto`.
- Secciones con `scroll-mt-24`.
- Filtros y formularios colapsan a una columna.
- Dialog de acciones usa `w-[min(92vw,34rem)]`.
- Carga masiva y preview conservan wrappers con overflow.

Observaciones no bloqueantes:

- La tabla principal sigue siendo ancha y requiere scroll horizontal.
- No hay cards mobile especificas para unidades.
- El modo Conciliacion puede seguir siendo largo cuando hay diferencia y ajuste asistido.

Estas deudas ya estaban identificadas y no justifican una microfase inmediata salvo feedback real de operadores o decision explicita de invertir en mobile avanzado.

## L. Resultado accesibilidad / baja vision

Resultado: aprobado con deuda menor documentada.

Fortalezas confirmadas:

- `scope="col"` en tablas.
- Labels `sr-only` en motivos de acciones por unidad.
- `aria-modal`, `aria-labelledby` y boton de cierre con `aria-label` en dialog.
- `aria-disabled="true"` en trazabilidad bloqueada.
- Botones con texto visible ademas de iconos.
- Contraste fuerte para texto principal, acciones sensibles y estados importantes.

Deuda menor:

- Varias etiquetas secundarias usan `text-xs` y `text-slate-500`. En baja vision estricta puede ser justo para informacion auxiliar.
- Algunos labels usan wrapping con `<label><span>...</span><input /></label>` en lugar de `for/id` explicito. Es funcional, pero no es el patron mas robusto.

No se encontro una barrera nueva introducida por 2B-2E.

## M. Resultado contratos criticos

Resultado: contratos criticos preservados por lectura de Razor.

Contratos de navegacion y anchors:

- `id="modo-unidades"`
- `id="modo-carga"`
- `id="modo-conciliacion"`
- `id="modo-configuracion"`
- `id="listado-unidades"`
- `id="form-carga-masiva-unidades"`
- `id="ajuste-asistido"`

Contratos GET/links:

- `asp-controller="Catalogo" asp-action="Index"`
- `asp-controller="MovimientoStock" asp-action="Kardex" asp-route-id="@Model.ProductoId"`
- `asp-controller="Producto" asp-action="Unidades" asp-route-productoId="@Model.ProductoId"`
- `asp-controller="Producto" asp-action="UnidadHistorial" asp-route-unidadId="@unidad.Id"`

Contratos POST por unidad:

- `POST Producto/MarcarUnidadFaltante`
- `POST Producto/ReintegrarUnidadAStock`
- `POST Producto/DarUnidadBaja`
- `POST Producto/FinalizarReparacionUnidad`
- `name="ProductoUnidadId"`
- `name="Motivo"`
- `name="EstadoDestino"`
- `required`
- `@Html.AntiForgeryToken()`

Contratos de carga:

- `POST Producto/CrearUnidad`
- `POST Producto/CrearUnidadesMasivas`
- `asp-for="CrearUnidad.ProductoId"`
- `asp-for="CrearUnidad.NumeroSerie"`
- `asp-for="CrearUnidad.UbicacionActual"`
- `asp-for="CrearUnidad.Observaciones"`
- `asp-for="CargaMasiva.ProductoId"`
- `asp-for="CargaMasiva.CantidadSinSerie"`
- `asp-for="CargaMasiva.NumerosSerieTexto"`
- `asp-for="CargaMasiva.UbicacionActual"`
- `asp-for="CargaMasiva.Observaciones"`
- `name="CargaMasiva.Confirmar"` con `value="false"` y `value="true"`

Contratos de configuracion y conciliacion:

- `POST Producto/ActivarTrazabilidad`
- `POST Producto/DesactivarTrazabilidad`
- `POST Producto/AjustarStockAgregadoAUnidadesFisicas`
- `POST Producto/AjustarStockAgregadoHaciaAbajo`
- `name="ProductoId"`
- `name="Motivo"`
- `required`
- `@Html.AntiForgeryToken()`

Contratos parciales/tests:

- `_EstadoUnidadBadge` se conserva en listado y preview.
- `ProductoControllerPrecioTests` contiene cobertura de vista/controlador sobre Unidades, carga masiva, conciliacion, filtros, redirecciones y acciones.
- No se detecto spec Playwright especifica para Producto/Unidades.

## N. Errores encontrados

No se encontraron errores funcionales ni rupturas de contrato por lectura de codigo.

El unico limite del QA manual fue ambiental: la app local respondio, pero la URL sin id dio 404 y la URL con id conocido redirigio a login. No se valido rendering autenticado con datos reales.

## O. Observaciones

- Producto/Unidades quedo mucho mas legible despues de 2B-2E: los modos reducen mezcla cognitiva sin cambiar endpoints.
- Las acciones por unidad en `<dialog>` reducen densidad de tabla y mantienen POST server-rendered.
- Carga y conciliacion ahora tienen espacios propios y copy mas claro.
- La deuda mobile de tabla ancha sigue viva, pero es deuda estructural no bloqueante.
- La falta de Playwright especifico sigue siendo la deuda de QA mas concreta, aunque no era parte de esta fase.

## P. Decision final

**B: cerrado con observaciones.**

Producto/Unidades puede cerrarse para este ciclo. No se recomienda abrir una microfase inmediata de UI/contratos antes de avanzar al siguiente frente.

La decision no es A porque quedan observaciones reales: tabla mobile ancha, falta de Playwright especifico y labels secundarios pequenos. Ninguna observacion bloquea uso operativo ni contratos.

## Q. Deudas restantes

- No hay spec Playwright especifica para Producto/Unidades.
- La tabla de unidades sigue dependiendo de scroll horizontal en mobile.
- El modo Conciliacion puede ser largo en mobile con diferencia y ajuste asistido.
- No hay estado activo real de tabs/anclas sin JS.
- Algunos labels secundarios `text-xs text-slate-500` pueden ser mejorables para baja vision.

## R. Proximo frente recomendado

Avanzar a:

`MISA-ALERTASTOCK-UX-0 - Auditoria UX de AlertaStock`

Motivo: Producto/Unidades queda cerrado con observaciones y no conviene seguir invirtiendo microfases en esta pantalla sin feedback real o una fase tecnica especifica de Playwright.
