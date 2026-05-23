# MISA-INVENTARIO-FISICO-UX-2C - Acciones por unidad en modal

## A. Objetivo

Reducir la densidad de acciones por fila en `Views/Producto/Unidades.cshtml` y evaluar un patron mas claro para ejecutar acciones operativas por unidad, preservando formularios POST, endpoints, payloads y reglas de negocio.

## B. Base y contexto

- Base indicada: `main` en `4bad62c` - MISA-INVENTARIO-FISICO-UX-2B integrada.
- Rama de trabajo: `misa/inventario-fisico-ux-2c-acciones-unidad`.
- Tipo de fase: Razor-first / UX de acciones / bajo riesgo.
- 2B ya reorganizo la pantalla con modos internos: Unidades, Carga, Conciliacion y Configuracion.

## C. Deuda tomada desde 2A/2B

2A identifico que las acciones sensibles por unidad eran candidatas a modal/drawer porque los formularios dentro de la tabla aumentaban densidad, especialmente en mobile. 2B resolvio la arquitectura general por modos, pero dejo las acciones operativas dentro de `<details>` en la celda de cada fila.

## D. Acciones por unidad detectadas

- `Ver historial`: accion de lectura, siempre visible.
- `Gestionar unidad`: agrupador anterior con `<details>`.
- `Marcar faltante`: accion operativa con motivo obligatorio.
- `Reintegrar a stock`: accion operativa con motivo obligatorio.
- `Dar de baja`: accion sensible/destructiva con motivo obligatorio.
- `Finalizar reparacion`: accion operativa sensible con `EstadoDestino` y motivo obligatorio.

## E. Formularios POST detectados

- `POST Producto/MarcarUnidadFaltante`
  - `ProductoUnidadId`
  - `Motivo`
  - `AntiForgeryToken`
- `POST Producto/ReintegrarUnidadAStock`
  - `ProductoUnidadId`
  - `Motivo`
  - `AntiForgeryToken`
- `POST Producto/DarUnidadBaja`
  - `ProductoUnidadId`
  - `Motivo`
  - `AntiForgeryToken`
- `POST Producto/FinalizarReparacionUnidad`
  - `ProductoUnidadId`
  - `EstadoDestino`
  - `Motivo`
  - `AntiForgeryToken`

## F. Alternativas evaluadas

### Alternativa A - Mantener `<details>` y mejorar copy/densidad

Menor riesgo. Preserva HTML nativo sin JS y evita cambios de interaccion. Desventaja: mantiene los formularios dentro de la tabla; en mobile, la fila crece cuando se expande.

### Alternativa B - Modal/drawer inline por fila con HTML/Razor y JS minimo

Riesgo medio. Permite sacar las acciones operativas de la densidad visual de la tabla y darles un espacio deliberado de confirmacion. Es viable si los formularios se mantienen intactos, no se anidan y no se cambian endpoints.

### Alternativa C - Bloque mas claro dentro de la tabla, sin modal

Riesgo bajo/intermedio. Mejoraria el orden visual, pero seguiria dentro de la tabla y no resolveria la deuda principal de mobile.

## G. Alternativa elegida y motivo

Se eligio la Alternativa B con `<dialog>` nativo por fila.

Motivo: los formularios existentes son independientes y podian permanecer intactos dentro del dialog, sin crear vistas, endpoints, servicios, formularios nuevos ni modulo JS. El unico JavaScript agregado es un `showModal()` inline localizado en el boton `Gestionar unidad`; el cierre usa `form method="dialog"`.

## H. Cambios aplicados

- Se reemplazo el `<details>` de acciones operativas por un boton `Gestionar unidad`.
- Se agrego un `<dialog>` por unidad con:
  - `role="dialog"`
  - `aria-modal="true"`
  - `aria-labelledby`
  - titulo por unidad
  - boton de cierre accesible
- Se mantuvo `Ver historial` visible en la tabla.
- Se conservaron los cuatro formularios POST existentes dentro del dialog, sin anidarlos.
- Se amplio el espacio interno de los inputs y botones dentro del modal para mejorar legibilidad.
- No se agrego CSS global ni modulo JS nuevo.

## I. Contratos preservados

- `ProductoUnidadId`
- `Motivo`
- `EstadoDestino`
- `@Html.AntiForgeryToken()`
- `asp-action`
- `asp-controller`
- `asp-route-*`
- `name`
- `id`
- `required`
- labels `sr-only`
- partial `_EstadoUnidadBadge`
- link `Ver historial`
- formularios POST existentes
- anchors de 2B:
  - `#modo-unidades`
  - `#modo-carga`
  - `#modo-conciliacion`
  - `#modo-configuracion`
  - `#listado-unidades`
  - `#form-carga-masiva-unidades`
  - `#ajuste-asistido`

## J. Que no se toco

- Backend.
- Controllers.
- Services.
- Models.
- ViewModels.
- Migraciones.
- Endpoints.
- Payloads.
- Permisos.
- Reglas de stock.
- Calculos.
- CSS global.
- Tests.
- Specs Playwright.
- Ventas/Kira.
- Catalogo.
- Movimientos.
- AlertaStock.

## K. Riesgo funcional

Riesgo bajo/intermedio. El cambio introduce `<dialog>` y un `showModal()` inline, pero no cambia los formularios ni sus contratos. El principal riesgo residual es de compatibilidad/interaccion visual del dialog en navegadores antiguos o en mobile real. En navegadores modernos, `<dialog>` soporta apertura modal, foco y cierre con Escape.

## L. Validaciones

Validaciones ejecutadas:

- `dotnet build --configuration Release`: compilacion correcta, 0 advertencias, 0 errores, tiempo 00:01:04.64.
- `dotnet test --configuration Release --filter "FullyQualifiedName~ProductoControllerPrecioTests&FullyQualifiedName~Unidades"`: 21/21 correctas, 0 errores, 0 omitidas.
- `git diff --check`: falla por trailing whitespace en `AGENTS.md` y `CLAUDE.md`, cambios locales preexistentes fuera de alcance.
- `git diff --check -- Views/Producto/Unidades.cshtml docs/misa-inventario-fisico-ux-2c-acciones-unidad.md`: OK.
- `git status --short`: muestra los dos archivos de esta fase mas cambios locales preexistentes no commiteables.

## M. Tests/Playwright omitidos o ejecutados con motivo

Se ejecuto el filtro acotado de `ProductoControllerPrecioTests` con `Unidades` porque existen tests especificos de contrato para esta vista y la fase toca Razor con un `showModal()` inline localizado.

No se ejecuto suite general, tests de Ventas, tests de Cotizacion ni Playwright completo. No se ejecuto Playwright porque no se modificaron specs, no se agrego flujo E2E nuevo y el alcance pedido prohibia Playwright completo.

## N. Deudas restantes

- No hay Playwright especifico para Producto/Unidades que valide apertura/cierre del dialog y presencia de formularios.
- El dialog no reabre automaticamente si un POST invalido vuelve con ModelState; los endpoints actuales redirigen/muestran mensajes por TempData, por lo que no se agrego estado adicional.
- La tabla sigue siendo ancha en mobile; esta fase reduce densidad de acciones, no convierte el listado a cards.

## O. Proximo paso recomendado

`MISA-INVENTARIO-FISICO-UX-2D - Alta y carga dedicada`.
