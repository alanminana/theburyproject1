# KIRA-VENTAS-PAGE-REWORK-1A - /Venta/Create como pagina wizard

## A. Objetivo

Adaptar `Views/Venta/Create_tw.cshtml` para que `/Venta/Create` sea la pagina principal de Nueva Venta en formato wizard fullscreen dentro del layout normal del ERP.

## B. Base y contexto

Base: `main` en `916c529` - KIRA-VENTAS-PAGE-REWORK-0 integrada.

El criterio vigente es que Nueva Venta deja de ser modal como superficie principal. No se revierte el rework modal: se reutilizan tabs, clases `vm-*`, sidebar/resumen y sincronizacion visual donde aportan valor.

## C. Cambio de modal a pagina

`Create_tw.cshtml` ahora tiene root propio:

- `#venta-create-page`
- tablist de pasos dentro de la pagina
- paneles `#step-panel-cliente`, `#step-panel-productos`, `#step-panel-pago`, `#step-panel-credito`, `#step-panel-revision`
- sidebar persistente con vendedor, observaciones, totales y confirmacion
- barra mobile `vm-mobile-summary-bar`

No se usa `#modal-crear-venta` como contenedor de pagina.

## D. Archivos auditados

- `docs/kira-ventas-page-rework-0-arquitectura.md`
- `docs/kira-ventas-modal-rework-0b-html-real.md`
- `docs/kira-ventas-modal-rework-1a-skeleton-razor.md`
- `docs/kira-ventas-modal-rework-1b-css-wizard.md`
- `docs/kira-ventas-modal-rework-1c-js-wizard.md`
- `docs/kira-ventas-modal-rework-1d-integracion.md`
- `docs/kira-ventas-modal-rework-1e-navegacion-inteligente.md`
- `docs/kira-ventas-modal-rework-1f-pago-principal-producto.md`
- `Views/Venta/Create_tw.cshtml`
- `Views/Venta/_VentaCrearModal.cshtml`
- `Views/Venta/_VentaModuleStyles.cshtml`
- `Views/Venta/_VentaModuleScripts.cshtml` - no existe
- `wwwroot/js/venta-create.js`
- `wwwroot/js/venta-modal-rework.js`
- `wwwroot/css/venta-modal-rework.css`
- `TheBuryProyect.Tests/Unit/VentaCreateUiContractTests.cs`

## E. Archivos modificados

- `Views/Venta/Create_tw.cshtml`
- `wwwroot/js/venta-modal-rework.js`
- `wwwroot/css/venta-modal-rework.css`
- `TheBuryProyect.Tests/Unit/VentaCreateUiContractTests.cs`
- `docs/kira-ventas-page-rework-1a-create-wizard.md`

## F. Estructura anterior de /Venta/Create

La vista era una pagina standalone lineal:

- hero con metricas
- secciones numeradas 1 a 6
- grilla `lg:grid-cols-3`
- columna izquierda: datos, productos, pago
- columna derecha: verificacion, vendedor, totales
- `sticky-action-footer` mobile

El form ya era el correcto:

```html
<form id="venta-form" asp-action="Create" method="post">
```

## G. Nueva estructura wizard implementada

La pagina conserva el form real y organiza los controles existentes en cinco pasos:

- Cliente: cliente, fecha, tipo de pago principal y aviso de credito.
- Productos: busqueda, filtros, selector de unidad, detalle.
- Pago: tarjeta, cheque, credito personal, planes y diagnostico.
- Credito: verificacion crediticia y fallback "credito no requerido".
- Revision: resumen de cliente, pago, items, alertas y totales.

El sidebar queda fuera de los paneles para mantener visibles vendedor, observaciones, totales y confirmacion.

## H. Que se reutilizo del rework modal

- Clases `vm-step-tab`, `vm-step-tab--active`, `vm-step-panel-active`.
- Estado global `#vm-estado-global`.
- Hooks `data-side-*`, `data-rev-*`, `data-conf-*`, `data-pago-summary`.
- Barra mobile `vm-mobile-summary-bar` y espejo `#vm-modal-sticky-total`.
- Sincronizacion visual de `venta-modal-rework.js`.

## I. Que NO se migro del modal

- `#modal-crear-venta`.
- Backdrop del modal principal.
- Boton cerrar modal.
- `role="dialog"` / `aria-modal="true"` en el root de pagina.
- `CreateAjax`.
- `VentaCrearModal.submit()`.
- Submodal `#modal-pago-item`.
- `#modal-confirmar-operacion`.

Se conserva `#modal-documentacion` porque ya existia como submodal funcional de documentacion crediticia en la pagina.

## J. Contratos preservados

Preservados en `Create_tw.cshtml`:

- `#venta-form`
- `#btn-confirmar`
- `#input-buscar-cliente`, `#dropdown-clientes`, `#hdn-cliente-id`, `#info-cliente`
- `#input-buscar-producto`, `#dropdown-productos`, `#panel-agregar-producto`
- `#hdn-producto-id`, `#txt-cantidad`, `#txt-descuento-item`, `#btn-agregar-producto`
- `#tbody-detalles`, `#detalles-hidden-inputs`
- `#select-tipo-pago`
- `#total-subtotal`, `#total-descuento`, `#total-iva`, `#total-final`
- `#hdn-subtotal`, `#hdn-descuento`, `#hdn-iva`, `#hdn-total`
- `#panel-alerta-mora`, `#panel-cupo-insuficiente`, `#panel-documentacion-faltante`
- `#VendedorUserId`, `#Observaciones`
- `AplicarExcepcionDocumental`
- `MotivoExcepcionDocumentalCreate`
- `@Html.AntiForgeryToken()`

## K. Que no se toco

- Controllers
- Services
- Models
- Migrations
- Endpoints
- Payloads
- Stock
- Caja
- Credito backend
- Cotizacion backend
- `Views/Venta/Index_tw.cshtml`
- `_VentaCrearModal.cshtml`
- `venta-crear-modal.js`
- Playwright specs

## L. Riesgo funcional

Riesgo bajo-medio. La vista conserva el POST nativo a `Create` y los controles reales. El cambio visual usa paneles ocultos con `hidden`, por lo que los elementos siguen en el DOM para `venta-create.js`.

Riesgo pendiente: requiere QA visual real en browser para confirmar comportamiento desktop/mobile y ausencia de overflow.

## M. Validaciones

- `node --check wwwroot/js/venta-modal-rework.js` - OK.
- `dotnet build --configuration Release` - OK.
- `dotnet test --configuration Release --filter "VentaCreate"` - OK.

## N. Tests ejecutados

`dotnet test --configuration Release --filter "VentaCreate"`:

- 133/133 OK
- 0 fallidos
- 0 omitidos

## O. Playwright ejecutado u omitido

Omitido.

Motivo exacto: no habia proceso de app `TheBuryProyect`, `TheBuryProyect.dll` ni `dotnet run` disponible para abrir `/Venta/Create`. Se detectaron procesos Playwright MCP e IDE preexistentes, pero no una app escuchando para smoke visual.

No se afirma validacion visual OK.

## P. Deudas restantes

- KIRA-VENTAS-PAGE-REWORK-1B debe reemplazar accesos al modal por navegacion a `/Venta/Create`.
- QA visual desktop 1440x900 y mobile 390x844 pendiente con app disponible.
- Renombrar `venta-modal-rework.*` a naming de pagina puede quedar para fase posterior cuando el modal legacy deje de depender de esos archivos.
- El modal legacy sigue existiendo en `Index_tw.cshtml` hasta la fase 1B.

## Q. Proximo prompt recomendado

KIRA-VENTAS-PAGE-REWORK-1B - Reemplazar accesos al modal por navegacion a `/Venta/Create`.
