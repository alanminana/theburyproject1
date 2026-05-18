# UI-0B - Analisis funcional de flujos y modulos combinables

Agente: Juan  
Fecha: 2026-05-18  
Alcance: diagnostico funcional/documental. No se modificaron vistas, controllers, services ni tests.

## Resumen ejecutivo

El ERP ya muestra una direccion funcional clara: las pantallas operativas criticas son Venta, Caja, Catalogo/Inventario, Cliente/DocumentoCliente, Credito, MovimientoStock/Kardex, Devolucion y Reportes. Para el rework visual conviene separar "unificacion visual" de "unificacion funcional". Hay varias zonas que pueden compartir layout, lenguaje visual, filtros, estados y navegacion, pero no deben mezclarse en un unico flujo transaccional sin un analisis posterior de permisos, auditoria, caja, stock y contabilidad.

Recomendacion principal: iniciar el rework con una pantalla piloto de bajo riesgo en `Reporte/Index_tw` o `MovimientoStock/Index_tw`. Si se quiere una pantalla mas cercana a operacion diaria, la mejor piloto es `MovimientoStock/Index_tw` porque ya fue clasificada como canonica en docs previos, tiene flujo de solo consulta/ajuste acotado y permite probar tablas, filtros, badges, estados y dark theme sin tocar ventas ni caja.

No conviene arrancar por `Venta/Create_tw`, `Caja/Cerrar_tw`, `Producto/Unidades` ni `DocumentoCliente/Upload_tw`: son pantallas sensibles, con reglas de negocio, validaciones, permisos, auditoria o dependencias de otros modulos.

## Mapa funcional de modulos

| Modulo | Funcion operativa | Evidencia principal | Criticidad |
|---|---|---|---|
| Venta | Alta, edicion, confirmacion, autorizacion, facturacion, cancelacion y enlace con caja/stock/credito/documentos. | `VentaController`, `VentaService`, `Views/Venta/*`, tests `Venta*`. | Muy alta |
| Cotizacion | Simulacion read-only, guardado, impresion/PDF, vencimiento, cancelacion y conversion controlada a venta. | `CotizacionController`, `CotizacionApiController`, `CotizacionConversionService`, `Views/Cotizacion/*`. | Alta |
| Caja | Apertura, cierre, movimientos, acreditaciones, ventas del turno, saldos reales y contramovimientos. | `CajaController`, `CajaService`, `CajaViewModel`, docs fase 9.x/Kira. | Muy alta |
| Catalogo | Entrada canonica de inventario comercial: listado, filtros, productos, precios, categorias, marcas y cambios de precio. | `CatalogoController.Index`, `Views/Catalogo/Index_tw.cshtml`, redirect desde `ProductoController.Index`. | Alta |
| Producto | Maestro de producto y operaciones tecnicas: CRUD, trazabilidad, unidades fisicas, conciliacion y acciones por unidad. | `ProductoController`, `ProductoService`, `ProductoUnidadService`, `Views/Producto/*`. | Alta |
| MovimientoStock | Auditoria de stock agregado: historial global, Kardex por producto y ajustes. | `MovimientoStockController`, `MovimientoStockService`, `Views/MovimientoStock/*`, docs polish. | Alta |
| DocumentoCliente | Cola documental, upload, verificacion, rechazo, batch, descarga y retorno a venta/cliente. | `DocumentoClienteController`, `DocumentoClienteService`, `Views/DocumentoCliente/*`. | Alta |
| Cliente | Maestro y ficha 360: datos, documentos, creditos, aptitud, limite y BCRA. | `ClienteController.Details`, `ClienteDetalleViewModel`, `Views/Cliente/*`. | Alta |
| Devolucion | Devoluciones, aprobacion, rechazo, completar, garantia, RMA y notas de credito. | `DevolucionController`, `DevolucionService`, `Views/Devolucion/*`. | Alta |
| OrdenCompra | Ordenes, estados y recepcion de compras. | `OrdenCompraController`, `OrdenCompraService`, `Views/OrdenCompra/*`. | Media/alta |
| Proveedor | Maestro proveedor y productos asociados. | `ProveedorController`, `ProveedorService`, `Views/Proveedor/*`. | Media |
| Credito | Solicitud, aprobacion/rechazo/cancelacion, configuracion de venta, cuotas y cobranza. | `CreditoController`, `CreditoService`, `Views/Credito/*`. | Muy alta |
| Seguridad | Usuarios, roles, permisos y auditoria. | `SeguridadController`, `SeguridadAuditoriaService`, `Views/Seguridad/*`. | Muy alta |
| Reporte | Analitica pesada, exportaciones y consultas historicas. | `ReporteController`, `ReporteService`, `Views/Reporte/*`. | Media/alta |
| Dashboard/Home | Entrada operativa y metricas resumidas. | `HomeController`, `DashboardController`, `DashboardViewModel`, `Views/Home/Index.cshtml`, `Views/Dashboard/Index.cshtml`. | Media |

## Pantallas canonicas

| Pantalla | Clasificacion | Evidencia/decision |
|---|---|---|
| `Views/Venta/Index_tw.cshtml` | Canonica | Entrada del nav "Ventas"; usa `VentaController.Index`; integra modal de creacion cuando hay caja abierta. |
| `Views/Venta/Create_tw.cshtml` | Canonica sensible | Usada por `Venta/Create` y `Venta/Cotizar`; cubierta por tests de contrato UI. |
| `Views/Venta/Details_tw.cshtml` | Canonica sensible | Centro de acciones: confirmar, facturar, cancelar, contrato credito, documentacion. |
| `Views/Venta/Edit_tw.cshtml` | Canonica | Estados editables controlados por `VentaViewModel.PuedeEditar` y `VentaValidator`. |
| `Views/Venta/Facturar_tw.cshtml` | Canonica sensible | Activa factura y movimiento de caja; requiere caja abierta. |
| `Views/Cotizacion/Index_tw.cshtml` | Canonica | Pantalla de simulacion/alta separada de Venta. |
| `Views/Cotizacion/Listado_tw.cshtml` | Canonica | Listado por filtros, estado y fecha. |
| `Views/Cotizacion/Detalles_tw.cshtml` | Canonica | Muestra detalle y conversion. |
| `Views/Cotizacion/Imprimir_tw.cshtml` | Canonica | Vista de impresion/PDF. |
| `Views/Caja/Index_tw.cshtml` | Canonica sensible | Entrada de cajas, aperturas abiertas y estado operativo. |
| `Views/Caja/Abrir_tw.cshtml`, `Cerrar_tw.cshtml` | Canonicas sensibles | Impacto directo en turnos, saldos y auditoria. |
| `Views/Caja/DetallesApertura_tw.cshtml` | Canonica | Incluye ventas del turno, saldos y acreditaciones. |
| `Views/Catalogo/Index_tw.cshtml` | Canonica | Catalogo unificado; `ProductoController.Index` redirige a esta pantalla. |
| `Views/Producto/Unidades.cshtml` | Canonica sensible | Inventario fisico por producto, conciliacion y acciones por unidad. |
| `Views/Producto/UnidadesGlobal.cshtml` | Canonica | Reporte/operacion global de unidades fisicas. |
| `Views/Producto/UnidadHistorial.cshtml` | Canonica | Auditoria por unidad fisica. |
| `Views/MovimientoStock/Index_tw.cshtml` | Canonica | Historial global de movimientos; docs previos la clasifican canonica. |
| `Views/MovimientoStock/Kardex_tw.cshtml` | Canonica | Kardex por producto; historial/auditoria. |
| `Views/MovimientoStock/Create_tw.cshtml` | Canonica sensible | Ajuste manual de stock agregado. |
| `Views/Cliente/Index_tw.cshtml` | Canonica | Maestro/listado de clientes. |
| `Views/Cliente/Details_tw.cshtml` | Canonica | Ficha cliente con documentos, creditos, aptitud y limites. |
| `Views/DocumentoCliente/Index_tw.cshtml` | Canonica | Cola documental agrupada/filtrable y retorno a venta. |
| `Views/DocumentoCliente/Upload_tw.cshtml` | Canonica sensible | Upload, reemplazo, retorno a venta y validaciones. |
| `Views/DocumentoCliente/Details_tw.cshtml` | Canonica | Verificacion/rechazo/descarga. |
| `Views/Devolucion/Index.cshtml`, `Detalles.cshtml` | Canonicas sensibles | Devolucion, RMA, garantia y nota de credito. |
| `Views/OrdenCompra/Index_tw.cshtml`, `Details_tw.cshtml`, `Recepcionar_tw.cshtml` | Canonicas | Flujo de compra/recepcion. |
| `Views/Proveedor/Index_tw.cshtml`, `Details_tw.cshtml` | Canonicas | Maestro proveedor y relaciones. |
| `Views/Credito/*_tw.cshtml` | Canonicas sensibles | Solicitud, decision, configuracion venta, pagos y cuotas. |
| `Views/Seguridad/Index.cshtml`, `Auditoria_tw.cshtml` | Canonicas sensibles | Roles, permisos, usuarios y auditoria. |
| `Views/Reporte/*_tw.cshtml` | Canonicas | Analitica/exportacion separada de Home. |
| `Views/Dashboard/Index.cshtml` | Canonica dudosa como entrada | Tiene metricas pesadas; no aparece como entrada principal del nav actual, pero existe controller dedicado. |
| `Views/Home/Index.cshtml` | Canonica como entrada | Home del layout/brand; debe ser operativo, no analitico pesado. |

## Pantallas dudosas o legacy

| Pantalla/flujo | Clasificacion | Evidencia/impacto |
|---|---|---|
| `Views/Venta/Create_tw_legacy.cshtml` | Legacy | Nombre explicito `legacy`; `VentaController.Create/Cotizar` usan `Create_tw`. No expandir. |
| `ProductoController.Index` | Legacy/paralelo neutralizado | Comentario "Vista legacy"; redirige permanentemente a `Catalogo/Index`. |
| `Catalogo/Resumen` | Legacy neutralizado | Redirige a `Catalogo/Index` con mensaje. |
| `Catalogo/HistorialCambiosPrecio`, `DetalleCambioPrecio` como pantallas | Legacy neutralizado | Redirigen a `Catalogo/Index`; APIs siguen vivas para historial modal. |
| `Views/CambiosPrecios/*` | Dudoso/paralelo | Existe carpeta de vistas separada de `CatalogoController` actual; requiere verificacion antes de redisenar. |
| `Views/Producto/Edit_tw.cshtml` | Canonica parcial / dependiente | Se usa para editar producto, pero el maestro operativo visible es Catalogo. Mantener, no convertir en hub. |
| `Views/Producto/UnidadesGlobal.cshtml` | Canonica pero solapada con Reportes | Es inventario fisico operativo global, no reporte financiero. |
| `Views/Dashboard/Index.cshtml` vs `Views/Home/Index.cshtml` | Solapado/dudoso | Dos entradas de resumen. Home debe quedar operativo; Dashboard puede ser analitico ligero o esperar. |
| `Views/PlantillaContratoCredito/*` | Fuera de alcance UI-0B principal | Aparece en nav de configuracion; no mezclar con Credito operativo. |

## Modulos combinables visualmente

Estas combinaciones pueden compartir patrones visuales sin fusionar reglas:

| Grupo | Pantallas | Recomendacion visual |
|---|---|---|
| Venta + Cotizacion | Index/listado/detalle/crear | Mismo lenguaje de carrito, cliente, totales, productos, estados, badges y acciones primarias. Mantener rutas separadas. |
| Cliente + DocumentoCliente | `Cliente/Details` tab documentos, `DocumentoCliente/Index`, `Upload` | Unificar cards, tabla documental, estados, CTA de upload/verificar/rechazar y retorno contextual. |
| Catalogo + Producto/Unidades + MovimientoStock/Kardex | `Catalogo/Index`, `Producto/Unidades`, `MovimientoStock/Index`, `Kardex` | Mismo sistema de filtros, badges de stock, accesos cruzados y panel de historial. No mezclar acciones. |
| Caja + Venta | `Caja/DetallesApertura`, `Venta/Index`, `Venta/Details` | Mejorar enlaces desde apertura hacia ventas del turno y desde venta hacia caja/movimiento. Mantener pantallas separadas. |
| Dashboard/Home + Reporte/Index | Home operativo y Reportes analiticos | Mismo set de KPIs resumidos, pero Home debe tener accesos accionables y Reportes consultas pesadas. |
| OrdenCompra + Proveedor | OC list/detail/recepcion y ficha proveedor | Navegacion cruzada y resumen proveedor/ordenes; no fusionar maestro proveedor con recepcion. |
| Devolucion + Venta + ProductoUnidad | Devolucion detalle, venta detalle, unidad historial | Timeline visual compartido para unidad/venta/devolucion/RMA; acciones separadas. |
| Credito + Cliente + Venta | Ficha cliente, venta credito, configurar venta, pagos | Paneles consistentes de cupo, estado, cuotas y acciones; permisos separados. |

## Modulos combinables funcionalmente con cautela

| Combinacion | Posible combinacion | Cautelas |
|---|---|---|
| Cotizacion dentro de Venta | Flujo unico con estados o tab "Cotizar" dentro del modulo Ventas. | Requiere redisenar permisos `cotizaciones` vs `ventas`, conversion, caja cerrada, numeracion y vencimiento. |
| Cliente + DocumentoCliente | Integrar documentos como seccion principal de ficha cliente. | Mantener cola global y batch para backoffice documental. |
| Catalogo + Kardex | Abrir Kardex como tab/modal contextual del producto. | Kardex es auditoria; no debe esconder ajustes ni movimientos globales. |
| Producto + Inventario fisico | Ficha producto con tab unidades y conciliacion. | No mezclar edicion de producto con ajuste de stock/unidad. |
| Caja + ventas del turno | Detalle de apertura con tabla de ventas y accesos a venta. | No mezclar cierre/apertura con creacion o confirmacion de ventas. |
| Dashboard + Reportes | Home con resumen y links profundos a reportes. | Analitica pesada debe vivir en Reporte para no hacer Home lento/ruidoso. |
| Proveedor + OrdenCompra | Ficha proveedor con resumen de OCs. | Recepcion y estados de OC deben seguir en OrdenCompra. |

## Modulos que deben seguir separados

- Venta y Caja: venta confirma/factura/cancela; caja abre/cierra/acredita y audita saldo. Se conectan, pero no son el mismo acto operativo.
- Venta y Cotizacion: conversion controlada si; fusion funcional no todavia.
- Producto maestro y MovimientoStock: editar descripcion/precio/categoria no es lo mismo que registrar un ajuste de stock.
- ProductoUnidad e inventario agregado: unidades fisicas tienen estados propios; `Producto.StockActual` y Kardex son stock agregado.
- DocumentoCliente y Cliente maestro: ficha cliente puede mostrar documentos, pero verificacion/batch/upload operativo requieren modulo documental.
- Credito y Venta: configuracion/pagos/decision crediticia deben seguir con permisos y auditoria propios.
- Seguridad y cualquier modulo operativo: no combinar con pantallas de negocio.
- Reportes y Home: Home debe ser entrada operativa; reportes deben contener analitica pesada/exportaciones.
- Devolucion/RMA/NotaCredito y Venta: deben enlazarse, pero cada flujo tiene impactos propios en stock, caja y cliente.

## Riesgos

### Venta y Cotizacion

- Cotizacion permite cliente opcional/nombre libre en contratos de simulacion; Venta exige cliente para operar.
- Cotizacion tiene estados propios: borrador, emitida, vencida, cancelada, convertida a venta.
- Venta exige caja abierta para crear venta normal; la conversion desde cotizacion crea venta en estado Cotizacion con `AperturaCajaId = null`.
- La conversion valida precios actuales, productos activos, vencimiento, doble conversion y advertencias.
- Integrar todo en un unico flujo puede romper la separacion entre simulacion read-only y operacion transaccional.
- Riesgo de confundir precio cotizado con precio vigente.
- Riesgo de permitir confirmar una venta sin seleccionar unidad fisica trazable.
- Riesgo de mezclar permisos `cotizaciones:create/convert/cancel/expire` con `ventas:create/invoice/authorize`.

Decision recomendada: mantener pantallas separadas, unificar visualmente y conservar solo conversion Cotizacion a Venta como puente canonico. Un flujo unico con estados puede evaluarse despues, pero no es primer paso.

### Producto, Catalogo, Inventario fisico y MovimientoStock

- `Catalogo/Index` es la entrada canonica comercial; `Producto/Index` ya redirige.
- Producto maestro define datos comerciales y configuracion; unidades fisicas gestionan trazabilidad.
- Crear unidad fisica no modifica stock agregado; varias acciones de unidad dicen explicitamente que no modifican `StockActual`.
- La conciliacion stock/unidades ya fue separada por signo y registra ajuste por `MovimientoStockService`.
- `MovimientoStock`/Kardex es auditoria de stock agregado y debe preservar historial, incluso con productos eliminados.
- Mezclar ajuste de stock con edicion de producto aumenta riesgo de cambios contables/stock no intencionales.

Decision recomendada: mantener Catalogo como hub visual; Producto como maestro/formulario; Inventario fisico como detalle operativo; Kardex como tab/enlace contextual de solo lectura o ajuste controlado; movimientos separados por auditoria.

### Cliente y DocumentoCliente

- `Cliente/Details` ya construye documentos y creditos del cliente.
- `DocumentoCliente` tiene permiso especifico `clientes:viewdocs`, batch, upload, reemplazo, verificacion/rechazo, descarga y retorno a venta.
- Documentacion puede desbloquear credito/configuracion de venta.
- Integrar todo en ficha cliente mejora usabilidad, pero eliminar modulo documental romperia cola de trabajo backoffice.

Decision recomendada: integrar documentos en ficha cliente como tab/resumen fuerte y mantener DocumentoCliente como modulo documental aparte.

### Caja con ventas del turno/acreditaciones

- `Caja/DetallesApertura` ya distingue ventas del turno, resumen por medio y saldo real.
- Movimientos de venta, cuota, anticipo, devolucion y contramovimiento tienen reglas diferentes.
- Cierre de caja tiene arqueo y justificacion de diferencia; venta tiene cliente/productos/factura.
- Acreditaciones pendientes no equivalen a ventas efectivas.

Decision recomendada: mantener Caja separada, mejorar navegacion desde apertura hacia ventas y desde venta hacia caja/movimiento, evitar mezclar venta con cierre.

### Flujos separados por permisos, auditoria o impacto contable

- Venta: crear, confirmar, autorizar, rechazar, facturar, cancelar.
- Cotizacion: crear, cancelar, vencer, convertir.
- Caja: abrir, cerrar, registrar movimiento, acreditar, contramovimiento.
- MovimientoStock: ajuste, entrada/salida historica, Kardex.
- ProductoUnidad: crear unidad, carga masiva, marcar faltante, baja, reintegrar, finalizar reparacion.
- DocumentoCliente: upload, verificar, rechazar, batch, delete, descarga.
- Credito: aprobar, rechazar, cancelar, configurar venta, registrar pagos, adelantar cuota.
- Devolucion/RMA/NotaCredito: aprobar, rechazar, completar, crear RMA.
- Seguridad: editar usuarios, roles, permisos, copiar permisos, auditoria.
- Reportes/exportaciones: consultas historicas y descarga Excel/PDF.

## Analisis especifico requerido

### Venta / Cotizacion

Conviene mantener pantallas separadas y unificar visualmente. La integracion funcional segura ya existe: conversion Cotizacion a Venta mediante `CotizacionConversionService`.

No conviene integrar Cotizacion dentro de Venta en esta etapa. Tampoco conviene crear todavia un flujo unico con estados porque Venta tiene caja, stock, credito, facturacion y autorizacion; Cotizacion tiene simulacion, vencimiento y PDF. La opcion mas segura es: Cotizacion separada, visualmente hermana de Venta, con CTA claro "Convertir a venta" y preview de diferencias.

### Producto / Catalogo / Inventario fisico / MovimientoStock

Conviene mantener Catalogo como hub visible de inventario comercial; Producto como maestro/formulario de datos; Inventario fisico (`Producto/Unidades`, `UnidadesGlobal`, `UnidadHistorial`) como detalle operativo; MovimientoStock/Kardex como auditoria de stock agregado.

Kardex puede integrarse como pestana o modal contextual desde Catalogo/Producto, pero debe conservar pantalla/ruta propia. Los movimientos deben seguir separados por auditoria. Se debe evitar mezclar ajuste de stock con edicion de producto.

### Cliente / DocumentoCliente

Conviene integrar documentos en ficha cliente como tab/resumen y modal de upload contextual. Tambien conviene mantener DocumentoCliente como modulo aparte para cola documental, filtros, batch y verificacion. La ficha cliente debe resolver consulta individual; DocumentoCliente debe resolver operacion masiva/backoffice.

### Caja / Venta

Conviene mantener Caja separada. La mejora debe estar en navegacion: desde apertura a ventas del turno, desde venta a caja/movimiento asociado, y desde cierre a desglose de medios/acreditaciones. No conviene mezclar crear/confirmar venta con cierre de caja.

### Dashboard / Reportes

Conviene reducir ruido en Home y mover analitica pesada a Reportes. Home debe ser operativo: acciones frecuentes, alertas accionables, caja abierta, ventas pendientes, documentos pendientes, stock critico y links profundos. Reportes debe contener consultas historicas, filtros amplios, exportaciones y graficos pesados.

## Orden recomendado de rework

1. `Reporte/Index_tw` o `MovimientoStock/Index_tw`: piloto visual de bajo riesgo para filtros, tablas, cards sobrias y dark theme.
2. `MovimientoStock/Kardex_tw`: extender piloto con detalle historico y auditoria.
3. `Catalogo/Index_tw`: hub visual de inventario, con cuidado por modales y precio.
4. `Cliente/Details_tw` + documentos embebidos: mejorar ficha operativa sin eliminar modulo documental.
5. `DocumentoCliente/Index_tw`: cola documental agrupada y batch.
6. `Caja/DetallesApertura_tw`: mejorar lectura de turno, ventas, saldos y acreditaciones.
7. `Cotizacion/*`: unificar lenguaje visual con Venta sin fusion funcional.
8. `Venta/Index_tw` y `Venta/Details_tw`: despues de estabilizar patrones en modulos menos riesgosos.
9. `Venta/Create_tw`, `Caja/Cerrar_tw`, `Credito/ConfigurarVenta_tw`: esperar; son pantallas de alto impacto.
10. `Seguridad`, `Devolucion`, `OrdenCompra/Recepcionar`: redisenar despues de piloto y reglas visuales comunes.

## Recomendacion para pantalla piloto

Piloto recomendado: `Views/MovimientoStock/Index_tw.cshtml`.

Motivos:

- Es canonica y ya fue diagnosticada en `docs/fase-polish-movimientostock-kardex.md`.
- Tiene filtros, tabla, badges, signos, stats y enlaces a Kardex.
- Permite validar dark theme, accesibilidad, densidad de tabla y lectura operativa.
- El riesgo funcional es menor que Venta, Caja, Credito o DocumentoCliente/Upload.
- No requiere cambiar reglas de negocio ni servicios.

Alternativa aun mas conservadora: `Views/Reporte/Index_tw.cshtml`, si se quiere empezar por una pantalla principalmente navegacional.

## Reglas para no romper flujos

- No fusionar rutas solo por similitud visual.
- No mover acciones con impacto contable, caja, stock, credito o auditoria a modales genericos sin permisos explicitos.
- No esconder pantallas canonicas que funcionen como cola de trabajo: DocumentoCliente, MovimientoStock, Caja, Credito.
- No cambiar estados ni nombres de acciones durante el rework visual.
- Mantener returnUrl/returnToVentaId en flujos de documentos, credito y venta.
- Mantener caja abierta como precondicion de venta operativa.
- Mantener conversion Cotizacion a Venta como puente explicito y auditable.
- Mantener `Producto.StockActual`, `ProductoUnidad` y `MovimientoStock` como conceptos separados en UI.
- Mantener Home operativo y Reportes analitico.
- Antes de redisenar pantallas sensibles, revisar controller, viewmodel, JS, tests y docs de fase.

## Validaciones ejecutadas en diagnostico

- `git status --short`: detecto cambios/untracked preexistentes ajenos a este documento.
- `git log --oneline -5`: `e0d7603`, `3d3dd64`, `723307e`, `3c08504`, `f4dd700`.
- `dotnet build --configuration Release`: OK inicial, 0 errores, 0 advertencias.
- Graphify: instalado, pero no existe `graphify-out/graph.json`; no hubo consulta persistente disponible. Se continuo con lectura directa de codigo/docs/tests.

## Checklist actualizado

- [x] Revisar estado de rama, ultimos commits y build inicial.
- [x] Mapear controllers, views, services, viewmodels, docs y tests relevantes.
- [x] Clasificar pantallas canonicas, legacy/dudosas y solapadas.
- [x] Definir combinaciones visuales seguras.
- [x] Definir combinaciones funcionales que requieren analisis posterior.
- [x] Identificar pantallas que no conviene combinar.
- [x] Proponer orden de rework y pantalla piloto.
- [ ] Ejecutar validaciones finales luego de crear este documento.
- [ ] Commit y push de `docs/ui-0b-analisis-funcional-flujos-modulos.md`.

