# KIRA-VENTAS-PAGE-REWORK-1B - Reemplazar accesos al modal

## A. Objetivo

Reemplazar el acceso operativo a Nueva Venta desde `/Venta` para que navegue a `/Venta/Create`, dejando la pagina wizard como flujo principal.

## B. Base y contexto

Base esperada: `main` en `da9467c` - KIRA-VENTAS-PAGE-REWORK-1A integrada.

La fase 1A dejo `/Venta/Create` como pagina wizard con root `#venta-create-page`, form nativo `#venta-form` con `asp-action="Create"` y boton final `#btn-confirmar`.

## C. Decision de producto

Nueva Venta no debe abrirse como modal desde `/Venta`.

El flujo principal es `/Venta/Create`. `CreateAjax` y `VentaCrearModal.submit()` quedan como deuda legacy del modal, no como camino principal de la pagina nueva.

## D. Archivos auditados

- `docs/kira-ventas-page-rework-0-arquitectura.md`
- `docs/kira-ventas-page-rework-1a-create-wizard.md`
- `Views/Venta/Index_tw.cshtml`
- `Views/Venta/Create_tw.cshtml`
- `Views/Venta/_VentaCrearModal.cshtml`
- `Views/Venta/_VentaModuleStyles.cshtml`
- `Views/Venta/_VentaModuleScripts.cshtml` - no existe en este arbol
- `wwwroot/js/venta-crear-modal.js`
- `wwwroot/js/venta-modal-rework.js`
- `wwwroot/js/venta-create.js`
- `TheBuryProyect.Tests/Unit/VentaCreateUiContractTests.cs`

## E. Archivos modificados

- `Views/Venta/Index_tw.cshtml`
- `TheBuryProyect.Tests/Unit/VentaCreateUiContractTests.cs`
- `docs/kira-ventas-page-rework-1b-reemplazar-accesos-modal.md`

## F. Como se abria el modal antes

`Index_tw.cshtml` renderizaba un boton `#btn-abrir-modal-crear-venta` cuando `puedeCrear` era true.

Luego cargaba:

- `venta-create.js`
- `venta-crear-modal.js`
- `venta-modal-rework.js`

`venta-crear-modal.js` escuchaba el click de `#btn-abrir-modal-crear-venta`, ejecutaba `VentaCrearModal.open()`, removia `hidden` de `#modal-crear-venta` y bloqueaba el scroll del body.

Ademas, `Index_tw.cshtml` renderizaba `<partial name="_VentaCrearModal" />` dentro de `@if (puedeCrear)`.

## G. Como navega ahora a /Venta/Create

El boton operativo se reemplazo por un link Razor:

```html
<a asp-controller="Venta" asp-action="Create" class="btn-erp-primary btn-sm no-underline sm:w-auto">
```

El texto visible sigue siendo `Nueva Venta`.

La condicion `puedeCrear` se conserva, por lo que el acceso solo aparece cuando la caja/permisos permiten operar. Si no se puede operar, se conserva el boton bloqueado existente.

## H. Render del partial modal

Se elimino el render de `_VentaCrearModal` desde `/Venta`.

No se borro el archivo fisico `Views/Venta/_VentaCrearModal.cshtml`; queda como legacy temporal hasta una fase de limpieza especifica.

## I. Scripts del modal

Se dejaron de cargar desde `Index_tw.cshtml`:

- `venta-create.js`
- `venta-crear-modal.js`
- `venta-modal-rework.js`

No se borraron archivos fisicos. `Create_tw.cshtml` sigue cargando `venta-create.js` y `venta-modal-rework.js` para la pagina wizard.

## J. Contratos preservados

- Listado de ventas en `/Venta`.
- Filtros del index.
- Acciones de detalle, devolucion y anulacion.
- Modal de recargo/descuento por tipo de pago.
- Modal de devolucion cuando aplica.
- Condicion `puedeCrear`.
- Boton bloqueado cuando no hay caja abierta.
- `/Venta/Create` con `#venta-create-page`.
- `#venta-form` con submit nativo a `Create`.
- `#btn-confirmar` en `Create_tw.cshtml`.

## K. Que no se toco

- Controllers.
- Services.
- Models.
- Migrations.
- Endpoints.
- Payloads.
- Stock.
- Caja backend.
- Credito backend.
- Cotizacion backend.
- `Views/Venta/Create_tw.cshtml`.
- `Views/Venta/_VentaCrearModal.cshtml`.
- Archivos JS fisicos.
- Playwright specs.

## L. Riesgo funcional

Riesgo bajo. El cambio solo reemplaza la entrada visual desde `/Venta` y remueve dependencias del modal en esa vista.

Riesgo pendiente: QA visual en browser debe confirmar que el click navega correctamente y que no queda overlay/backdrop residual en desktop y mobile.

## M. Validaciones

- `dotnet build --configuration Release` - OK.
- `dotnet test --configuration Release --filter "VentaCreate"` - OK.
- `git diff --check` - pendiente al cierre.
- `git status --short` - pendiente al cierre.

## N. Tests ejecutados

`dotnet test --configuration Release --filter "VentaCreate"`:

- 134/134 OK
- 0 fallidos
- 0 omitidos

## O. Playwright ejecutado u omitido

Omitido.

Motivo exacto: no habia proceso de app `TheBuryProyect`, `TheBuryProyect.dll` ni `dotnet run` disponible para abrir `/Venta` y `/Venta/Create`. Se detectaron procesos Playwright MCP e IDE preexistentes, pero no una app del ERP escuchando para smoke visual.

No se afirma validacion visual OK.

## P. Deudas restantes

- `_VentaCrearModal.cshtml` sigue existiendo como legacy temporal.
- `venta-crear-modal.js` sigue existiendo como archivo legacy temporal.
- `venta-modal-rework.js` conserva naming de modal aunque ya soporta `#venta-create-page`.
- `venta-create.js` aun escucha eventos `venta-crear-modal:open/close`, inofensivos si el modal no se carga desde `/Venta`.

## Q. Proximo prompt recomendado

KIRA-VENTAS-PAGE-REWORK-1C - Desacoplar JS/CSS de nombre modal y limpiar dependencias legacy.
