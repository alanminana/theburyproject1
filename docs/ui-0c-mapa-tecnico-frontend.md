# UI-0C - Mapa tecnico frontend-backend para rework visual

Fecha: 2026-05-18

Responsable: Carlos

## Resumen ejecutivo

El rework visual debe avanzar por pantallas existentes, no por vistas nuevas paralelas. El ERP ya tiene contratos frontend-backend activos basados en IDs, `data-*`, scripts por modulo, endpoints JSON y tests de contrato UI. Cambiar markup sin preservar esos contratos puede romper ventas, cotizacion, caja, catalogo, tickets, documentos y flujos de credito aunque el cambio sea "solo visual".

La zona de mayor riesgo tecnico es Venta/Create con `_VentaCrearModal`: concentra modal complejo, busqueda asincronica, calculos backend, prevalidacion crediticia, seleccion de unidades fisicas, carga documental y submit AJAX. Cotizacion/Detalles tambien es alta por conversion a venta y endpoints de preview/convertir. Caja/DetallesApertura es riesgo medio-alto por responsive, tablas anchas, filtros locales e impresion. Producto/Unidades y MovimientoStock/Kardex son riesgo medio por acciones POST y conciliacion stock/unidades.

Las pantallas mas seguras para polish visual son listados o detalles read-only sin JS fuerte y con navegacion GET/links simples, por ejemplo reportes, listados simples de cotizacion, kardex read-only si no se tocan acciones de ajuste, y vistas de detalle con baja interaccion. Aun asi deben preservar `asp-action`, formularios, nombres de campos y secciones con tests existentes.

No existe `graphify-out/graph.json` al momento del analisis. Se continuo con lectura directa del codigo, `rg`, `Select-String`, inventario de scripts y tests.

## Validaciones iniciales

- `git status --short`: worktree ya venia con cambios no relacionados en `.graphifyignore`, `AGENTS.md`, `CLAUDE.md` y varios `.txt` no trackeados de inventario. No se tocaron ni se agregaron al commit.
- `git log --oneline -5`: ultimo commit en `main`: `e0d7603 Agregar convencion para tests HTTP de integracion`.
- `dotnet build --configuration Release`: OK, con warning MSB3026 por reintento de copia de `TheBuryProyect.dll` usado por otro proceso.
- `dotnet test --filter "UiContractTests"`: OK, 137 tests superados.

## Clasificacion operativa de componentes revisados

### Canonico

- `Views/Shared/_Layout.cshtml`: layout base activo. Carga CSS global, scripts globales, sidebar/header y modales compartidos.
- `wwwroot/js/layout.js`: contrato global de sidebar, overlay y dropdowns por IDs.
- `wwwroot/js/shared-ui.js`: contrato global de toasts y confirm modal.
- `wwwroot/js/horizontal-scroll-affordance.js`: helper global para tablas anchas con `data-oc-scroll`.
- `Views/Venta/Create_tw.cshtml` + `Views/Venta/_VentaCrearModal.cshtml` + `wwwroot/js/venta-create.js` + `wwwroot/js/venta-crear-modal.js`: camino activo de creacion de venta y modal.
- `Views/Cotizacion/Index_tw.cshtml` + `wwwroot/js/cotizacion-simulador.js`: simulador/guardado de cotizacion.
- `Views/Cotizacion/Detalles_tw.cshtml` + `wwwroot/js/cotizacion-conversion.js`: conversion controlada a venta.
- `Views/Caja/DetallesApertura_tw.cshtml` + `wwwroot/js/caja-detalles-apertura.js`: detalle operativo de apertura.
- `Views/Producto/Unidades.cshtml`: pantalla activa de unidades fisicas, conciliacion y acciones POST.
- `Views/MovimientoStock/Kardex_tw.cshtml`: vista de trazabilidad read-only vinculada a producto/unidades.
- Tests UI de `TheBuryProyect.Tests/Unit/*UiContractTests.cs` y `CajaDetallesAperturaContractTests.cs`: protegen contratos de HTML/JS.

### Duplicado/paralelo o legacy

- `Views/Venta/Create_tw_legacy.cshtml`: existe junto al flujo actual `Create_tw.cshtml`. No conviene tomarlo como base del rework salvo investigacion puntual.
- Vistas con sufijo `_tw` conviven con algunas vistas sin sufijo. Debe verificarse ruta/controller antes de redisenar.

### Incierto

- Scripts de modales especificos que pueden estar acoplados por inclusion indirecta desde layout o parciales compartidos: `ticket-modal.js`, `ticket-panel.js`, `venta-devolucion-modal.js`, `documento-upload-modal.js`. Antes de tocarlos hay que validar pantalla real y partial que los renderiza.

## Mapa vista -> JS -> controller -> tests

| Vista / partial | JS asociado | Controller / endpoint | Tests existentes | Riesgo |
|---|---|---|---|---|
| `Views/Shared/_Layout.cshtml` | `shared-ui.js`, `layout.js`, `notificaciones.js`, `ticket-modal.js`, `ticket-panel.js` | Layout global, SignalR/notificaciones, tickets API | sin test UI global especifico detectado | Alto por impacto transversal |
| `Views/Venta/Create_tw.cshtml` | `venta-create.js`, `venta-crear-modal.js`, `venta-module.js` | `VentaController.Create`, `VentaController.CreateAjax`, `VentaApiController`, `ProductoApiController`, `DocumentoClienteController.Upload` | `VentaCreateUiContractTests` | Muy alto |
| `Views/Venta/_VentaCrearModal.cshtml` | `venta-create.js`, `venta-crear-modal.js`, `horizontal-scroll-affordance.js` | `/Venta/CreateAjax`, `/api/ventas/*`, `/api/productos/{id}/unidades-disponibles`, `/DocumentoCliente/Upload` | `VentaCreateUiContractTests`, `VentaApiControllerTests` | Muy alto |
| `Views/Venta/Index_tw.cshtml` | `venta-index.js`, `venta-crear-modal.js`, `venta-module.js` | `VentaController.Index`, configuracion de pago modal | contratos indirectos de venta/configuracion | Alto |
| `Views/Venta/Details_tw.cshtml` | `details-venta.js`, `venta-devolucion-modal.js`, `venta-module.js` | `VentaController.Details`, devolucion/contexto | `VentaDetailsAjustePlanUiContractTests` | Alto |
| `Views/Venta/Edit_tw.cshtml` | scripts de edicion/venta | `VentaController.Edit` | `VentaEditUiContractTests` | Alto |
| `Views/Caja/Index_tw.cshtml` | `caja-index.js` | `CajaController.Create`, `Edit`, `Delete` con respuestas JSON para modal | sin UiContract especifico detectado | Alto |
| `Views/Caja/DetallesApertura_tw.cshtml` | `caja-detalles-apertura.js`, `horizontal-scroll-affordance.js` | `CajaController.DetallesApertura`, `Cerrar`, `RegistrarMovimiento`, links a venta | `CajaDetallesAperturaContractTests` | Medio-alto |
| `Views/Caja/Abrir_tw.cshtml` | `caja-abrir.js` | `CajaController.Abrir` | sin UiContract especifico detectado | Medio |
| `Views/Caja/Cerrar_tw.cshtml` | `caja-cerrar.js` | `CajaController.Cerrar` | sin UiContract especifico detectado | Medio-alto |
| `Views/Caja/Create_tw.cshtml`, `Edit_tw.cshtml` | `caja-form.js` | `CajaController.Create`, `Edit` | sin UiContract especifico detectado | Medio |
| `Views/Cotizacion/Index_tw.cshtml` | `cotizacion-simulador.js` | `CotizacionApiController.Simular`, `Guardar`; `CotizacionController.BuscarProductos`, `ProductoResumen`, `BuscarClientes` | `CotizacionControllerUiTests`, `CotizacionApiControllerTests`, calculator tests | Alto |
| `Views/Cotizacion/Detalles_tw.cshtml` | `cotizacion-conversion.js` | `CotizacionApiController.ConversionPreview`, `Convertir`; `CotizacionController.BuscarClientes`; venta edit URL | `CotizacionControllerUiTests`, `CotizacionConversionApiTests`, security tests | Alto |
| `Views/Cotizacion/Imprimir_tw.cshtml` | browser print, links | `CotizacionController.Imprimir`, `DescargarPdf` | `CotizacionControllerPdfTests` | Medio |
| `Views/Cotizacion/Listado_tw.cshtml` | sin JS fuerte detectado | `CotizacionController.Listado`, `Detalles` | tests de controller/cotizacion | Bajo-medio |
| `Views/Producto/Unidades.cshtml` | sin script externo especifico detectado; formularios POST | `ProductoController.Unidades`, `CrearUnidad`, `CrearUnidadesMasivas`, estados de unidad, conciliacion | tests de conciliacion/servicios relacionados, no UiContract especifico detectado | Medio-alto |
| `Views/Producto/UnidadesGlobal.cshtml` | sin JS fuerte detectado | `ProductoController.UnidadesGlobal` | tests de servicio/dominio relacionados | Bajo-medio |
| `Views/MovimientoStock/Kardex_tw.cshtml` | sin JS fuerte detectado | `MovimientoStockController.Kardex` | docs/tests de polish existentes, sin UiContract especifico detectado | Bajo-medio |
| `Views/MovimientoStock/Index_tw.cshtml` | potencial AJAX si se usa `ListJson`; no script externo especifico detectado en vista | `MovimientoStockController.Index`, `ListJson` | sin UiContract especifico detectado | Medio |
| `Views/MovimientoStock/Create_tw.cshtml` | script inline para hints de tipo | `MovimientoStockController.Create`, `BuscarProductos`, `ProductoInfo` | sin UiContract especifico detectado | Medio |
| `Views/Catalogo/Index_tw.cshtml` | `catalogo-index.js`, modales categoria/marca/producto/precio | `CatalogoController`, `ProductoController`, categoria/marca endpoints | tests de DTO/producto; sin UiContract dedicado detectado | Alto |
| `Views/Cliente/Details_tw.cshtml` | `cliente-details.js`, documento upload | `ClienteController`, `DocumentoClienteController` | tests cliente/documento | Alto |
| `Views/Ticket/*` y `Views/Shared/_Ticket*` | `ticket-modal.js`, `ticket-panel.js`, `ticket-module.js` | `TicketController`, `TicketApiController` | sin UiContract especifico detectado | Alto |

## A. Vistas con dependencia fuerte con JS

- Venta: `Create_tw.cshtml`, `_VentaCrearModal.cshtml`, `Index_tw.cshtml`, `Details_tw.cshtml`, `Edit_tw.cshtml`, `Facturar_tw.cshtml`.
- Cotizacion: `Index_tw.cshtml`, `Detalles_tw.cshtml`.
- Caja: `Index_tw.cshtml`, `Abrir_tw.cshtml`, `Cerrar_tw.cshtml`, `Create_tw.cshtml`, `Edit_tw.cshtml`, `RegistrarMovimiento_tw.cshtml`, `DetallesApertura_tw.cshtml`.
- Catalogo: `Index_tw.cshtml` y parciales/modales de producto, categoria, marca, precios e historial.
- Cliente: `Details_tw.cshtml`, formularios y modales de documento.
- DocumentoCliente: index/details/upload con scripts de documento.
- Ticket: panel y modales compartidos.
- Seguridad: tabs, roles, permisos y auditoria con scripts especificos.

## B. Vistas que dependen de IDs especificos

Dependencia muy fuerte:

- `_VentaCrearModal.cshtml`: `modal-crear-venta`, `modal-crear-venta-backdrop`, `btn-cerrar-modal-crear-venta`, `venta-form`, `input-buscar-cliente`, `dropdown-clientes`, `hdn-cliente-id`, `select-tipo-pago`, `input-buscar-producto`, `dropdown-productos`, `hdn-producto-id`, `hdn-producto-requiere-numero-serie`, `txt-cantidad`, `btn-agregar-producto`, `tbody-detalles`, `detalles-vacio`, `panel-tarjeta`, `select-tarjeta`, `select-cuotas-tarjeta`, `panel-cheque`, `panel-credito-personal`, `panel-verificacion-crediticia`, `btn-verificar-elegibilidad`, `venta-create-feedback-slot`, `panel-resultado-verificacion`, `verificacion-*`, `panel-documentacion-faltante`, `btn-cargar-documentacion`, `hdn-aplicar-excepcion`, `btn-aplicar-excepcion`, `btn-confirmar-excepcion`, `btn-cancelar-excepcion`, `VendedorUserId`, `total-*`, `hdn-*`, `btn-confirmar`, `detalles-hidden-inputs`, `modal-pago-item`, `select-tipo-pago-item`, `modal-plan-*`, `modal-documentacion`, `input-doc-archivo`, `select-tipo-documento`, `btn-subir-documento`.
- `Cotizacion/Index_tw.cshtml`: `cotizacion-feedback`, `cotizacion-producto-buscar`, `cotizacion-productos-dropdown`, `cotizacion-producto-seleccionado`, `cotizacion-cantidad`, `cotizacion-agregar-producto`, `cotizacion-producto-id-manual`, `cotizacion-cantidad-manual`, `cotizacion-agregar-manual`, `cotizacion-productos-tbody`, `cotizacion-productos-vacio`, `cotizacion-resultados-vacio`, `cotizacion-resultados`, `cotizacion-subtotal`, `cotizacion-descuento`, `cotizacion-total-base`, `cotizacion-resultados-tbody`, `cotizacion-cliente-buscar`, `cotizacion-clientes-dropdown`, `cotizacion-cliente-id`, `cotizacion-cliente-seleccionado`, `cotizacion-cliente-nombre`, `cotizacion-cliente-doc`, `cotizacion-limpiar-cliente`, `cotizacion-observaciones`, `cotizacion-simular`, `cotizacion-simular-estado`, `cotizacion-guardar`.
- `Cotizacion/Detalles_tw.cshtml`: `cotizacion-conversion-panel`, `cotizacion-btn-convertir`, `cotizacion-conversion-modal`, `cotizacion-modal-cerrar`, `cotizacion-conversion-loading`, `cotizacion-conversion-contenido`, `cotizacion-conversion-errores`, `cotizacion-conversion-errores-lista`, `cotizacion-conversion-advertencias`, `cotizacion-conversion-advertencias-lista`, `cotizacion-conversion-resumen`, `cotizacion-total-cotizado`, `cotizacion-detalles-preview-panel`, `cotizacion-cliente-override-panel`, `cotizacion-override-cliente-buscar`, `cotizacion-override-clientes-dropdown`, `cotizacion-override-cliente-id`, `cotizacion-override-cliente-nombre`, `cotizacion-precio-cotizado`, `cotizacion-precio-actual`, `cotizacion-confirmar-advertencias-panel`, `cotizacion-check-advertencias`, `cotizacion-conversion-error-general`, `cotizacion-conversion-footer`, `cotizacion-modal-cancelar`, `cotizacion-btn-confirmar-conversion`.
- `Caja/DetallesApertura_tw.cshtml`: `toast-error`, `toast-success`, `buscar-concepto`, `filtro-tipo`, `tabla-movimientos`, `caja-detalles-movimientos-scroll-hint`, `data-caja-print`, `data-tipo`, `data-concepto`, `data-oc-scroll*`.
- `Caja/Index_tw.cshtml`: `cajaMainContent`, `caja-index-feedback-slot`, `modalCajaContainer`, `cajas-activas-tbody`, `data-caja-open-create`, `data-caja-open-edit`, `data-caja-delete-form`, `data-caja-row-id`.
- `Caja/Cerrar_tw.cshtml`: `monto-esperado`, `form-cerrar`, `diferencia-status`, `diferencia-help`, `total-real`, `diferencia-icon`, `diferencia-valor`, `justificacion`, `data-caja-justificacion`.
- Layout: `sidebar`, `sidebarOverlay`, `toggleSidebar`, `collapseSidebar`, dropdown IDs terminados en `Menu`, `confirmModal`, `confirmModalAction`, `confirmModalBody`.

Dependencia media:

- `Producto/Unidades.cshtml`: IDs `ajuste-asistido`, `form-carga-masiva-unidades`, `listado-unidades`; lo critico son formularios POST, names y tokens antiforgery mas que JS externo.
- `MovimientoStock/Create_tw.cshtml`: `select-producto`, `select-tipo`, `tipo-hint-entrada`, `tipo-hint-salida`, `tipo-hint-ajuste`.
- `Catalogo/Index_tw.cshtml`: IDs de busqueda, tabs, seleccion, modales y `data-catalogo-*`.

## C. Modulos con tests de contrato UI

Tests detectados por filtro `UiContractTests`:

- `ConfigurarVentaUiContractTests.cs`
- `CreditoDetailsUiContractTests.cs`
- `VentaCreateUiContractTests.cs`
- `VentaDetailsAjustePlanUiContractTests.cs`
- `VentaEditUiContractTests.cs`

Tests de contrato UI adicionales aunque no matchean por nombre:

- `CajaDetallesAperturaContractTests.cs`
- `CotizacionControllerUiTests.cs`
- `CotizacionControllerPdfTests.cs`
- `CotizacionConversionApiTests.cs`
- `CotizacionApiControllerTests.cs`
- `CotizacionPagoCalculatorContractTests.cs`
- `ConfiguracionPagoGlobalAdminViewTests.cs`
- `ProductoCondicionesPagoAdminLegacyDespublicadoTests.cs`
- `ProductoControllerPrecioTests.cs` en integracion protege ausencia de contratos legacy.

## D. Pantallas tecnicamente riesgosas para rediseno

Muy alto:

- Venta/Create y `_VentaCrearModal`: IDs masivos, submit AJAX, calculos backend, endpoint de unidades, credito, documentos, modales internos.
- Cotizacion/Detalles: conversion a venta, modal, preview, confirmacion de advertencias, override de cliente, redireccion a venta.
- Layout general: sidebar/header/mobile/dropdowns/confirm modal/tickets/notificaciones.

Alto:

- Venta/Index, Details, Edit.
- Caja/Index por CRUD AJAX modal.
- Catalogo/Index por tabs, filtros, seleccion masiva, modales y fetch.
- Cliente/Details por upload documental y actualizacion BCRA.
- Ticket panel/modal por API, adjuntos y estado.

Medio-alto:

- Caja/DetallesApertura por tablas anchas, filtros locales, impresion y secciones contractuales.
- Producto/Unidades por acciones POST y conciliacion stock/unidades.
- Caja/Cerrar por calculo local de diferencia y justificacion.
- Cotizacion/Index por simulador, guardado y seleccion de opcion de pago.

## E. Pantallas aparentemente seguras para polish visual

Seguras con preservacion de formularios/links:

- `Cotizacion/Listado_tw.cshtml`: listado con links a detalle.
- `MovimientoStock/Kardex_tw.cshtml`: detalle read-only y links de navegacion.
- `Producto/UnidadesGlobal.cshtml`: listado/filtros GET y links a unidades/historial.
- Reportes (`Views/Reporte/*_tw.cshtml`) si se preservan filtros GET y tablas.
- `AlertaStock/Details_tw.cshtml`, `PorProducto.cshtml`, `Estadisticas_tw.cshtml` con cuidado en forms de acciones.
- `Caja/DetallesCierre_tw.cshtml` parece mas read-only que apertura, aunque debe conservar links a apertura.

No son "libres": igual deben preservar `asp-action`, `asp-controller`, `name`, antiforgery, tablas con scroll y textos protegidos por tests cuando existan.

## F. Scripts globales

- `layout.js`: sidebar mobile, overlay, collapse desktop, dropdowns por IDs.
- `shared-ui.js`: auto-dismiss de toasts, confirm modal global (`confirmModal*`), delegacion por `data-confirm`.
- `site.js`: base global del sitio, revisar antes de cambios transversales.
- `horizontal-scroll-affordance.js`: helper global para contenedores `data-oc-scroll`, `data-oc-scroll-region`, `data-oc-scroll-hint`, fades y tabla.
- `notificaciones.js`: notificaciones globales/SignalR cargado desde layout.
- `ticket-modal.js` y `ticket-panel.js`: se cargan desde layout cuando el panel/modal esta disponible; por impacto practico son compartidos aunque pertenezcan al modulo ticket.

## G. Scripts especificos de modulo

Venta:

- `venta-create.js`, `venta-crear-modal.js`, `venta-index.js`, `details-venta.js`, `venta-facturar.js`, `venta-devolucion-modal.js`, `venta-module.js`.

Caja:

- `caja-index.js`, `caja-detalles-apertura.js`, `caja-abrir.js`, `caja-cerrar.js`, `caja-form.js`, `caja-historial.js`, `caja-registrar-movimiento.js`.

Cotizacion:

- `cotizacion-simulador.js`, `cotizacion-conversion.js`.

Catalogo/productos/precios:

- `catalogo-index.js`, `catalogo-module.js`, `producto-crear-modal.js`, `producto-editar-modal.js`, `producto-edit-form.js`, `producto-comision-modal.js`, `precio-aumento-modal.js`, `historial-precio-modal.js`, `movimientos-inventario-modal.js`, `categoria-crear-modal.js`, `categoria-editar-modal.js`, `marca-crear-modal.js`, `marca-editar-modal.js`.

Cliente/documentos:

- `cliente-index.js`, `cliente-details.js`, `cliente-form.js`, `cliente-modal.js`, `documento-index.js`, `documento-details.js`, `documento-module.js`, `documento-upload.js`, `documento-upload-modal.js`.

Credito:

- `credito-index.js`, `credito-details.js`, `credito-pagar-cuota.js`, `credito-module.js`, `configurar-venta-credito.js`, `contrato-venta-credito-preparar.js`.

Otros:

- `dashboard-index.js`, `devolucion-index.js`, `devolucion-module.js`, `ordencompra-*`, `proveedor-*`, `seguridad-*`, `permisos-index.js`, `alerta-stock-index.js`, `module-index.js`, `ticket-*`.

## H. Vistas con modales complejos

- `Views/Venta/_VentaCrearModal.cshtml`: modal principal, modal pago por item, modal documentacion.
- `Views/Cotizacion/Detalles_tw.cshtml`: modal conversion a venta con preview, advertencias, override cliente y confirmacion.
- `Views/Caja/Index_tw.cshtml` + `_CreateModal_tw.cshtml` + `_EditModal_tw.cshtml`: carga partials por fetch y submit AJAX.
- `Views/Catalogo/Index_tw.cshtml` y parciales/modal scripts de producto/categoria/marca/precios.
- `Views/Cliente/Details_tw.cshtml`: modal upload documento.
- `Views/Shared/_TicketModal.cshtml`, `_TicketPanel.cshtml`, `Views/Ticket/_TicketActionModals.cshtml`.
- `Views/Shared/_VentaDevolucionModal.cshtml`.
- `Views/Shared/_ConfirmModal.cshtml`.
- Seguridad: modales de rol (`_CreateRoleModal_tw`, `_EditRoleModal_tw`, `_DuplicateRoleModal_tw`, `_CopyPermisosRolModal_tw`).

## I. Vistas que dependen de endpoints API

Venta/Create:

- `/api/ventas/BuscarClientes`
- `/api/ventas/BuscarProductos`
- `/api/ventas/CalcularTotalesVenta`
- `/api/ventas/configuracion-pagos-global`
- `/api/ventas/GetTarjetasActivas`
- `/api/ventas/CalcularCuotasTarjeta`
- `/api/ventas/PrevalidarCredito`
- `/api/productos/{productoId}/unidades-disponibles`
- `/Venta/CreateAjax`
- `/DocumentoCliente/Upload`

Cotizacion:

- `/api/cotizacion/simular`
- `/api/cotizacion/guardar`
- `/api/cotizacion/{id}/conversion/preview`
- `/api/cotizacion/{id}/conversion/convertir`
- `/api/cotizacion/{id}/cancelar`
- `/Cotizacion/BuscarProductos`
- `/Cotizacion/ProductoResumen`
- `/Cotizacion/BuscarClientes`
- `/Cotizacion/DescargarPdf`
- `/Cotizacion/Imprimir`

Caja:

- `Caja/Create`, `Caja/Edit`, `Caja/Delete` retornan JSON/partials para modal de index.
- `Caja/Abrir`, `Cerrar`, `RegistrarMovimiento` son POST normales con antiforgery.

Producto/unidades:

- `Producto/ActivarTrazabilidad/{productoId}`
- `Producto/DesactivarTrazabilidad/{productoId}`
- `Producto/CrearUnidad`
- `Producto/CrearUnidadesMasivas`
- `Producto/MarcarUnidadFaltante`
- `Producto/DarUnidadBaja`
- `Producto/ReintegrarUnidadAStock`
- `Producto/FinalizarReparacionUnidad`
- `Producto/ConciliarStockUnidades`
- `Producto/AjustarStockAgregadoAUnidadesFisicas`
- `Producto/AjustarStockAgregadoHaciaAbajo`

MovimientoStock:

- `MovimientoStock/ListJson`
- `MovimientoStock/BuscarProductos`
- `MovimientoStock/ProductoInfo`
- `MovimientoStock/Create`

Catalogo/tickets/documentos/cliente:

- `Catalogo/ToggleDestacado`
- `/api/tickets/*`
- `/Cliente/ActualizarBcra`
- endpoints de documento/upload segun partials.

## J. HTML que no debe cambiarse sin actualizar JS/tests

- IDs y `name` de `_VentaCrearModal.cshtml` listados en la seccion B.
- `data-venta-modal`, `data-venta-modal-action`, `data-venta-modal-target`.
- Contenedor `#venta-form`, action `/Venta/CreateAjax`, antiforgery y hidden inputs `Subtotal`, `Descuento`, `IVA`, `Total`.
- `data-requiere-numero-serie`, `data-unidades-en-stock`, `data-stock-sin-identificar` generados en resultados de busqueda de producto.
- Contrato de filas generadas por venta: `data-index`, `.btn-eliminar-detalle`, `detalles-hidden-inputs`.
- Root `data-cotizacion-simulador` y sus `data-*-url`.
- Root `data-cotizacion-conversion` y sus `data-preview-url`, `data-convertir-url`, `data-clientes-url`, `data-venta-edit-url`.
- IDs de conversion de cotizacion y modal.
- `data-cotizacion-medio`, `data-cotizacion-opcion-key`, `data-cotizacion-eliminar-index`, `data-cotizacion-cantidad-index`.
- `Caja/DetallesApertura`: textos/variables `Ventas efectivas`, `Sin impacto en caja`, `Registros de auditoria`, `Operaciones pendientes`, `ventasEfectivas`, `ventasPendientes`, `ventasAuditoria`, `<details>`, `<summary>`.
- `Caja/DetallesApertura`: `buscar-concepto`, `filtro-tipo`, `tabla-movimientos`, `data-tipo`, `data-concepto`, `data-caja-print`.
- Cualquier bloque `data-oc-scroll*`.
- Layout: `sidebar`, `sidebarOverlay`, `toggleSidebar`, `collapseSidebar`, IDs de dropdowns, `confirmModal*`.
- `Producto/Unidades`: formularios POST y `asp-action` de conciliacion/estados, nombres de ViewModel anidados, antiforgery.
- `MovimientoStock/Create`: IDs de hints si se conserva el script inline.

## K. Tests recomendados antes de redisenar

- `LayoutUiContractTests`: proteger IDs de sidebar, overlay, botones, confirm modal, render de scripts globales y seccion `Scripts`.
- `CajaIndexUiContractTests`: proteger `modalCajaContainer`, triggers `data-caja-open-*`, `cajas-activas-tbody`, contratos de partial modal y submit AJAX.
- `CajaCerrarUiContractTests`: proteger IDs de calculo local y panel de justificacion.
- `CotizacionListadoUiContractTests`: baseline para listado antes de polish.
- `ProductoUnidadesUiContractTests`: proteger acciones POST de unidades, conciliacion, IDs de secciones y formularios criticos.
- `MovimientoStockKardexUiContractTests`: proteger links a producto/catalogo, tabla de movimientos y estados de empty/filters.
- `CatalogoIndexUiContractTests`: proteger tabs, busqueda, seleccion masiva, modales y `data-catalogo-*`.
- `TicketSharedUiContractTests`: proteger `_TicketModal`, `_TicketPanel` y atributos `data-tp-*`.
- Tests de smoke HTML para las pantallas piloto con verificacion de scripts esperados y ausencia de vistas legacy.

## L. Pantalla piloto recomendada

Recomendacion tecnica: `Views/MovimientoStock/Kardex_tw.cshtml`.

Motivos:

- Es una pantalla operativa real y relacionada con trazabilidad/unidades, pero mayormente read-only.
- No concentra submit AJAX ni modales complejos.
- Tiene bajo riesgo de romper reglas de negocio si se limita a Razor/CSS.
- Permite probar patrones de tabla, badges, resumen, responsive y dark theme sin tocar controllers.
- Sirve como puente visual hacia `Producto/Unidades` y caja/ventas, que son mas riesgosas.

Alternativa de mayor valor pero mas riesgo: `Caja/DetallesApertura_tw.cshtml`, siempre creando/ajustando primero tests de contrato para filtros, print y bloques auditables.

No se recomienda como piloto: Venta/Create, Cotizacion/Detalles, Layout global, Catalogo/Index.

## M. Riesgos de crear vistas nuevas en vez de modificar existentes

- Duplica caminos canonicamente activos y aumenta la probabilidad de que controller, tests y usuarios apunten a vistas distintas.
- Puede dejar tests verdes sobre la vista vieja mientras el flujo real usa otra vista.
- Reintroduce deuda tipo `Create_tw_legacy.cshtml`.
- Obliga a mantener dos contratos de IDs/data attributes/scripts.
- Puede romper enlaces, rutas, permisos y parcializacion sin que el cambio parezca funcional.
- Dificulta rollback y seguimiento del rework por modulo.

Regla: para rework visual, modificar la vista canonica existente en micro-lotes. Crear vista nueva solo para prototipo descartable o si hay decision explicita de migracion controlada.

## N. Riesgos de modificar controllers para cambios puramente visuales

- Mezcla presentacion con cambios funcionales y vuelve mas riesgoso validar el lote.
- Puede alterar ViewModels, rutas, nombres de actions o JSON consumido por scripts.
- Rompe tests de controller/API por un objetivo visual.
- Puede afectar permisos, antiforgery, redirecciones, TempData y model binding.
- Incentiva mover deuda hacia backend sin necesidad.

Regla: si el objetivo es visual, tocar Razor/CSS y como maximo JS de presentacion asociado. Controllers/services solo si existe bug funcional o contrato insuficiente documentado y testeado.

## Analisis especifico - Venta/Create y _VentaCrearModal

### IDs criticos

Criticos para apertura/cierre y submit:

- `modal-crear-venta`, `modal-crear-venta-backdrop`, `btn-cerrar-modal-crear-venta`, `venta-form`, `venta-ajax-validation-summary`, `venta-ajax-error-list`, `btn-confirmar`, `VendedorUserId`.

Criticos para cliente/producto/detalle:

- `input-buscar-cliente`, `dropdown-clientes`, `hdn-cliente-id`, `info-cliente`, `info-cliente-nombre`, `info-cliente-doc`, `btn-limpiar-cliente`.
- `input-buscar-producto`, `dropdown-productos`, `panel-agregar-producto`, `txt-producto-seleccionado`, `hdn-producto-id`, `hdn-producto-codigo`, `hdn-producto-precio`, `hdn-producto-stock`, `hdn-producto-requiere-numero-serie`, `txt-cantidad`, `stock-error`, `advertencia-stock-sin-identificar`, `txt-descuento-item`, `btn-agregar-producto`, `tbody-detalles`, `detalles-vacio`, `detalles-hidden-inputs`.

Criticos para pagos y calculos:

- `select-tipo-pago`, `panel-tarjeta`, `select-tarjeta`, `select-cuotas-tarjeta`, `txt-num-autorizacion-tarjeta`, `panel-tarjeta-resumen`, `tarjeta-monto-cuota`, `tarjeta-total-interes`, `tarjeta-recargo`.
- `panel-cheque`, `txt-num-cheque`, `txt-banco-cheque`, `txt-titular-cheque`, `txt-cuit-cheque`, `txt-fecha-emision-cheque`, `txt-fecha-vencimiento-cheque`, `txt-monto-cheque`.
- `panel-credito-personal`, `panel-credito-cupo`, `credito-cupo-valor`, `credito-cupo-estado`.
- `total-subtotal`, `total-descuento-label`, `total-descuento`, `total-iva`, `total-final`, `hdn-subtotal`, `hdn-descuento`, `hdn-iva`, `hdn-total`.

Criticos para prevalidacion/documentacion:

- `panel-verificacion-crediticia`, `btn-verificar-elegibilidad`, `venta-create-feedback-slot`, `panel-resultado-verificacion`, `verificacion-badge`, `verificacion-estado`, `verificacion-limite`, `verificacion-utilizado`, `verificacion-saldo`, `verificacion-barra`, `panel-cupo-suficiente`, `panel-cupo-insuficiente`, `cupo-insuficiente-detalle`, `panel-motivos`, `lista-motivos`, `panel-alerta-mora`, `alerta-mora-texto`, `panel-documentacion-faltante`, `lista-docs-faltantes`.
- `hdn-aplicar-excepcion`, `panel-excepcion-crediticia`, `panel-excepcion-inactiva`, `btn-aplicar-excepcion`, `panel-excepcion-activa`, `txt-excepcion-documental`, `btn-confirmar-excepcion`, `btn-cancelar-excepcion`.
- `modal-documentacion`, `modal-documentacion-overlay`, `btn-cerrar-modal-doc`, `modal-lista-docs`, `input-doc-archivo`, `doc-archivo-nombre`, `select-tipo-documento`, `doc-upload-feedback`, `btn-subir-documento`, `btn-ir-documentacion`.

Criticos para modal pago por item:

- `modal-pago-item`, `modal-pago-item-titulo`, `select-tipo-pago-item`, `modal-pago-item-planes`, `modal-pago-item-resumen`, `modal-plan-producto`, `modal-plan-precio-base`, `modal-plan-cuotas-label`, `modal-plan-ajuste`, `modal-plan-total`, `modal-plan-cuota`, `btn-guardar-pago-item`.

### JS asociado

- `venta-create.js`: logica principal de busquedas, calculos, detalles, pagos, unidades, credito, documentacion y submit.
- `venta-crear-modal.js`: apertura/cierre, eventos `venta-crear-modal:open/close`, submit AJAX a `/Venta/CreateAjax` y errores.
- `venta-module.js`: modales por `data-venta-modal`.
- `horizontal-scroll-affordance.js`: scroll responsive de tablas.

### Endpoints usados

- `/api/ventas/BuscarClientes`
- `/api/ventas/BuscarProductos`
- `/api/productos/{productoId}/unidades-disponibles`
- `/api/ventas/CalcularTotalesVenta`
- `/api/ventas/configuracion-pagos-global`
- `/api/ventas/GetTarjetasActivas`
- `/api/ventas/CalcularCuotasTarjeta`
- `/api/ventas/PrevalidarCredito`
- `/Venta/CreateAjax`
- `/DocumentoCliente/Upload`

### Tests existentes

- `VentaCreateUiContractTests`: protege scripts, addEventListener, endpoints, ausencia de diagnostico legacy, `data-requiere-numero-serie`, submit y calculo de totales.
- `VentaApiControllerTests`: protege contratos JSON de API de ventas.
- `VentaServiceTests`: protege calculos y serializacion de respuesta.
- `ConfigurarVentaUiContractTests` y tests de credito relacionados protegen la rama de credito personal.

### Riesgos de rediseno

- Romper IDs usados por `document.querySelector`/`getElementById`.
- Cambiar `name` de inputs y romper model binding.
- Mover hidden inputs fuera del form.
- Eliminar `data-*` generados dinamicamente.
- Cambiar botones `type="button"` a submit o viceversa.
- Romper scroll affordance o tablas anchas.
- Cambiar el orden/semantica de secciones que los tests o usuarios esperan.

### Que se puede tocar

- Clases CSS/Tailwind, spacing, color, bordes, tipografia y jerarquia visual.
- Orden visual interno si se preservan IDs, names, form ownership, modales y eventos.
- Estados empty/error/loading visuales si se preservan contenedores.

### Que no se puede tocar sin actualizar JS/tests

- IDs, `name`, `data-*`, endpoints, action del form, antiforgery, hidden inputs, estructura de modal documentacion/pago por item, contratos de tabla detalles.

## Analisis especifico - Caja/DetallesApertura

### HTML critico

- Bloques protegidos por test: `Ventas efectivas`, `Sin impacto en caja`, `Registros de auditoria`, `Operaciones pendientes`.
- Variables/secciones protegidas: `ventasEfectivas`, `ventasPendientes`, `ventasAuditoria`.
- Elementos semanticos protegidos: `<details>`, `<summary>`.
- Filtros locales: `buscar-concepto`, `filtro-tipo`, `tabla-movimientos`, filas con `data-tipo`, celdas con `data-concepto`.
- Print: botones con `data-caja-print`.
- Responsive tables: `data-oc-scroll`, `data-oc-scroll-region`, `data-oc-scroll-table`, hints/fades.

### Helpers locales

La vista usa helpers/local logic Razor para formatear conceptos, separar ventas efectivas, ventas sin impacto inmediato, auditoria y pendientes. Esos bloques son parte del contrato funcional de lectura de caja y no deben simplificarse como si fueran solo cards.

### Bloques visuales

- Header/resumen de apertura.
- Alertas/toasts.
- Resumen de caja/operacion.
- Tablas de ventas efectivas.
- Tablas de operaciones sin impacto o pendientes.
- Registros de auditoria.
- Movimientos manuales con filtro local.
- Acciones: registrar movimiento, cerrar caja, imprimir.

### Tests existentes

- `CajaDetallesAperturaContractTests`: protege textos, secciones, ausencia de `Html.Raw` en seccion de ventas, mensajes y detalles/summary.

### Riesgo responsive

Medio-alto. Hay varias tablas anchas y scroll horizontal con affordance. El polish debe preservar los wrappers `data-oc-scroll*`, min-widths razonables, hints mobile y region focusable.

## Analisis especifico - Producto/Unidades y MovimientoStock/Kardex

### Acciones POST criticas en Producto/Unidades

- Activar/desactivar trazabilidad.
- Crear unidad individual.
- Crear unidades masivas y preview.
- Marcar unidad faltante.
- Dar unidad de baja.
- Reintegrar unidad a stock.
- Finalizar reparacion.
- Conciliar stock/unidades.
- Ajustar stock agregado a unidades fisicas.
- Ajustar stock agregado hacia abajo.

### Botones y formularios criticos

- Formularios con `asp-controller="Producto"` y `asp-action` anteriores.
- Antiforgery tokens.
- `ProductoId`, IDs de unidad, comentarios/motivos y campos de ViewModel anidados.
- `form-carga-masiva-unidades`.
- Seccion `ajuste-asistido`.
- Tabla/listado `listado-unidades`.

### Tests relacionados

- Documentacion y tests de conciliacion recientes cubren reglas de servicio/origen.
- No se detecto `ProductoUnidadesUiContractTests`; conviene crearlo antes de redisenar fuerte.

### Riesgo de tocar conciliacion

Alto si se modifican formularios, nombres de campos, acciones o condiciones de render. Bajo-medio si el cambio se limita a clases CSS, layout de bloques y badges sin mover contratos POST.

### MovimientoStock/Kardex

- Vista mayormente read-only con links a catalogo/producto/crear ajuste.
- Riesgo bajo-medio para polish visual.
- No tocar controller ni `Kardex` route. Preservar links a `MovimientoStock/Create` y `Catalogo/Index`.
- Si se redisenan tablas, mantener legibilidad y responsive.

## Analisis especifico - Cotizacion

### Conversion

- `Cotizacion/Detalles_tw.cshtml` expone root `data-cotizacion-conversion` con URLs de preview, convertir, clientes y venta edit.
- `cotizacion-conversion.js` abre modal, llama preview, renderiza errores/advertencias/detalles, permite override cliente, politica de precio y confirmacion.
- Endpoints: `/api/cotizacion/{id}/conversion/preview` y `/api/cotizacion/{id}/conversion/convertir`.

### PDF e imprimir

- `CotizacionController.Imprimir` renderiza `Imprimir_tw`.
- `CotizacionController.DescargarPdf` usa `ICotizacionPdfService` y retorna `application/pdf`.
- Tests relevantes: `CotizacionControllerPdfTests`.

### JS cotizacion-conversion

Depende de IDs estrictos del modal y de `data-*` en root. Tambien depende de antiforgery token disponible en pagina para POST.

### Tests relevantes

- `CotizacionControllerUiTests`: protege URLs/data attributes de simulador y conversion, y tokens del script.
- `CotizacionApiControllerTests`
- `CotizacionConversionApiTests`
- `CotizacionControllerPdfTests`
- `CotizacionPagoCalculatorContractTests`
- tests de seguridad de conversion/cancelacion/vencimiento en integracion.

## Analisis especifico - Layout general

### Views/Shared

- `_Layout.cshtml`: base del sistema, carga CSS/scripts, sidebar/header y secciones.
- `_ConfirmModal.cshtml`: requerido por `shared-ui.js`.
- `_TicketModal.cshtml`, `_TicketPanel.cshtml`: cargados/operados por scripts globales de ticket.
- `_VentaDevolucionModal.cshtml`: modal compartido de devolucion.
- `_LoginPartial.cshtml`: usuario/sesion.
- `_ValidationScriptsPartial.cshtml`: validacion cliente.
- `_EstadoUnidadBadge.cshtml`: badge reusable para unidades.

### Scripts globales

- `shared-ui.js`, `layout.js`, `notificaciones.js`, `ticket-modal.js`, `ticket-panel.js`, `horizontal-scroll-affordance.js`, `site.js`.

### CSS global

- `layout.css`, `site.css`, `dark-theme.css`, `shared-components.css`, `tailwind.css`, `tailwind-input.css`, `standalone-tokens.css`, `horizontal-scroll-affordance.css`.

### Sidebar/header

Contratos criticos:

- `sidebar`, `sidebarOverlay`, `toggleSidebar`, `collapseSidebar`.
- Dropdowns por IDs y `aria-expanded`.
- Menus con IDs terminados en `Menu`.

### Riesgo mobile

Alto. El layout controla sidebar mobile, overlay, collapse desktop y dropdowns. Cambios visuales globales deben validarse en mobile y monitores chicos antes de avanzar por modulos.

## Reglas tecnicas para el rework visual

1. Redisenar pantallas canonicas existentes, no crear vistas paralelas.
2. Preservar IDs, `name`, `data-*`, `asp-action`, `asp-controller`, `method`, antiforgery y hidden inputs.
3. Cambiar CSS/clases primero; tocar JS solo si el contrato visual lo exige y con tests.
4. No modificar controllers/services/ViewModels para polish visual.
5. Si una pantalla tiene script dedicado, revisar el script antes de mover HTML.
6. Si una pantalla tiene tests UI, actualizarlos solo cuando el nuevo contrato sea intencional.
7. Si una pantalla no tiene tests UI y tiene JS fuerte, crear test de contrato antes del rework.
8. Mantener `data-oc-scroll*` en tablas anchas.
9. Mantener modales en la misma relacion DOM que esperan los scripts.
10. Validar build, filtro UI contract y `git diff --check` en cada micro-lote.
11. Separar lotes: layout, pantalla piloto, modulo venta, modulo caja, modulo cotizacion, etc.
12. Documentar cualquier componente legacy/paralelo detectado y no expandirlo.

## Proximo micro-lote recomendado

Crear tests de contrato faltantes para la pantalla piloto `MovimientoStock/Kardex_tw.cshtml` y luego hacer un polish visual acotado de esa vista. Si se prefiere mayor valor operativo, crear primero `CajaIndexUiContractTests` y `CajaCerrarUiContractTests` antes de redisenar caja.

## Checklist UI-0C

- [x] Rama creada desde `main`: `carlos/ui-0c-mapa-tecnico-frontend`.
- [x] Estado inicial revisado.
- [x] Ultimos commits revisados.
- [x] Build Release ejecutado.
- [x] Filtro `UiContractTests` ejecutado.
- [x] Inventario de vistas/scripts/tests revisado.
- [x] Busquedas de IDs, `data-*`, JS, endpoints y tests ejecutadas.
- [x] Pantallas de alto y bajo riesgo clasificadas.
- [x] Contratos HTML criticos documentados.
- [x] Recomendacion de piloto definida.
- [x] Validaciones finales ejecutadas: `dotnet build --configuration Release`, `git diff --check`, `git status --short`.
- [ ] Commit y push del documento.
