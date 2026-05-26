# KIRA-VENTAS-PAGE-REWORK-RESET-0 - Cleanup legacy Nueva Venta

## A. Objetivo

Eliminar legacy seguro del flujo modal antiguo de Nueva Venta y dejar `/Venta/Create` preparado para el reset visual de la proxima fase.

## B. Motivo de limpieza

Nueva Venta ya opera como pagina y no como modal desde `main` `4e9733a`.
Antes de recrear la pantalla con paridad visual, convenia retirar archivos que podian reintroducir el flujo modal, el submit AJAX antiguo o nombres de assets confusos.

## C. Archivos auditados

- `Views/Venta/Autorizar_tw.cshtml`
- `Views/Venta/Cancelar_tw.cshtml`
- `Views/Venta/ComprobanteFactura_tw.cshtml`
- `Views/Venta/Create_tw.cshtml`
- `Views/Venta/Create_tw_legacy.cshtml`
- `Views/Venta/Delete_tw.cshtml`
- `Views/Venta/Details_tw.cshtml`
- `Views/Venta/Edit_tw.cshtml`
- `Views/Venta/Facturar_tw.cshtml`
- `Views/Venta/Index_tw.cshtml`
- `Views/Venta/Rechazar_tw.cshtml`
- `Views/Venta/_VentaCrearModal.cshtml`
- `Views/Venta/_VentaModuleStyles.cshtml`
- `wwwroot/js/venta-create.js`
- `wwwroot/js/venta-crear-modal.js`
- `wwwroot/js/venta-modal-rework.js`
- `wwwroot/css/venta-modal-rework.css`
- `wwwroot/css/venta-module.css`
- `TheBuryProyect.Tests/Unit/VentaCreateUiContractTests.cs`

## D. Referencias legacy encontradas

- `Create_tw.cshtml` cargaba `venta-modal-rework.js`.
- `_VentaModuleStyles.cshtml` cargaba `venta-modal-rework.css`.
- `_VentaCrearModal.cshtml` conservaba `modal-crear-venta`, `CreateAjax` y `VentaCrearModal.submit()`.
- `venta-crear-modal.js` conservaba `VentaCrearModal`, eventos `venta-crear-modal:*` y POST a `/Venta/CreateAjax`.
- `venta-modal-rework.js/css` contenian compatibilidad y selectores del modal legacy.
- `VentaCreateUiContractTests.cs` todavia tenia contratos directos contra `_VentaCrearModal.cshtml` y `venta-modal-rework.*`.
- `VentaController.CreateAjax` sigue existiendo en backend, pero no se toco por alcance prohibido.

## E. Archivos eliminados

- `Views/Venta/Create_tw_legacy.cshtml`
- `Views/Venta/_VentaCrearModal.cshtml`
- `wwwroot/js/venta-crear-modal.js`
- `wwwroot/js/venta-modal-rework.js`
- `wwwroot/css/venta-modal-rework.css`

## F. Archivos conservados y motivo

- Vistas funcionales del modulo Venta: se conservaron porque pertenecen a flujos operativos de autorizacion, cancelacion, comprobante, detalle, edicion, facturacion, listado y rechazo.
- `Views/Venta/Create_tw.cshtml`: es el camino canonico actual de Nueva Venta.
- `Views/Venta/_VentaModuleStyles.cshtml`: sigue centralizando estilos del modulo.
- `wwwroot/js/venta-create.js`: contiene la logica frontend productiva de `/Venta/Create`.
- `wwwroot/css/venta-module.css`: contiene estilos compartidos del modulo.
- `Controllers/VentaController.cs`: no se toco por restriccion explicita del prompt.

## G. Archivos nuevos creados

- `wwwroot/js/venta-page-wizard.js`
- `wwwroot/css/venta-page-wizard.css`

## H. Referencias actualizadas

- `Create_tw.cshtml` carga `venta-page-wizard.js`.
- `_VentaModuleStyles.cshtml` carga `venta-page-wizard.css`.
- Tests de contrato leen `venta-page-wizard.*`.
- `venta-create.js` ya no escucha eventos `venta-crear-modal:open/close`.

## I. Contratos preservados

Se preservaron `#venta-create-page`, `#venta-form`, POST nativo `asp-action="Create"`, antiforgery, ids criticos de cliente/producto/totales/pago/credito/documentacion, `VendedorUserId`, `Observaciones`, `AplicarExcepcionDocumental` y `MotivoExcepcionDocumentalCreate`.

## J. Que no se toco

No se tocaron controllers, services, models, migrations, endpoints, payloads, stock, caja backend, credito backend, cotizacion backend, reglas de negocio, calculo backend ni rutas.

## K. Riesgo funcional

Riesgo bajo-medio: se eliminaron archivos legacy no cargados por el flujo actual, pero se renombro el asset JS/CSS que gobierna el wizard de pagina. La cobertura de contrato y `node --check` reducen el riesgo de ruptura basica.

## L. Validaciones

Pendientes de ejecucion en esta fase:

- `node --check wwwroot/js/venta-create.js`
- `node --check wwwroot/js/venta-page-wizard.js`
- `dotnet build --configuration Release`
- `dotnet test --configuration Release --filter "VentaCreate"`
- `git diff --check`
- `git status --short`

## M. Tests

Se actualizaron contratos para:

- `Index_tw` sin boton `btn-abrir-modal-crear-venta`.
- `Index_tw` sin partial `_VentaCrearModal`.
- `Create_tw` sin `modal-crear-venta`.
- `Create_tw` sin `modal-confirmar-operacion`.
- `Create_tw` sin `VentaCrearModal.submit()`.
- `Create_tw` conserva `venta-create-page`, `venta-form` y POST nativo a `Create`.
- `_VentaModuleStyles` carga `venta-page-wizard.css`.
- `Create_tw` carga `venta-page-wizard.js`.

## N. Playwright/manual

Pendiente u omitible segun disponibilidad de app. Esta fase no recrea visualmente la pantalla.

## O. Proxima fase visual

`KIRA-VENTAS-PAGE-REWORK-RESET-1` debe recrear `/Venta/Create` con paridad visual exacta del HTML objetivo, sin reintroducir modal legacy ni AJAX `CreateAjax`.
