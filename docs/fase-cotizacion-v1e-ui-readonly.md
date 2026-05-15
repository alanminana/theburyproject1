# Fase Cotizacion V1E - UI read-only separada

Agente: Carlos - Cotizacion V1E UI separada read-only.

## A. Objetivo

Agregar una pantalla Razor separada para simular cotizaciones read-only usando `POST /api/cotizacion/simular`.

La pantalla permite seleccionar productos, cantidades, cliente opcional y medios de pago a incluir. No crea venta, no toca stock, no registra caja, no genera factura y no crea credito definitivo.

## B. Decision UI separada

Se creo un flujo propio bajo `CotizacionController`, separado de `VentaController`, `VentaService`, `VentaApiController`, `Views/Venta/*` y `wwwroot/js/venta-create.js`.

La vista usa `Index_tw.cshtml` porque las pantallas nuevas del proyecto usan el sufijo `_tw` y los controllers cercanos devuelven explicitamente vistas `_tw`.

## C. Rutas

- MVC: `GET /Cotizacion`
- Productos para UI: `GET /Cotizacion/BuscarProductos`
- Producto por ID manual: `GET /Cotizacion/ProductoResumen`
- Clientes para UI: `GET /Cotizacion/BuscarClientes`
- Simulacion read-only: `POST /api/cotizacion/simular`

Los endpoints auxiliares de `CotizacionController` son read-only y no reemplazan el endpoint canonico de simulacion.

## D. Archivos creados

- `Controllers/CotizacionController.cs`
- `Views/Cotizacion/Index_tw.cshtml`
- `Views/Shared/_Layout.cshtml`
- `wwwroot/js/cotizacion-simulador.js`
- `TheBuryProyect.Tests/Unit/CotizacionControllerUiTests.cs`
- `docs/fase-cotizacion-v1e-ui-readonly.md`

## E. Flujo de usuario

1. Buscar producto por nombre/codigo o ingresar `ProductoId` manual.
2. Agregar cantidad.
3. Seleccionar cliente opcional.
4. Marcar medios a incluir.
5. Ejecutar simulacion.
6. Revisar tabla comparativa por medio, estado, plan, cuotas, total, valor cuota y advertencias.

## F. Que NO se toco

- `Services/VentaService.cs`
- `Services/Interfaces/IVentaService.cs`
- `Controllers/VentaController.cs`
- `Controllers/VentaApiController.cs`
- `Views/Venta/*`
- `wwwroot/js/venta-create.js`
- Devolucion, Garantia, ProductoUnidad, MovimientoStock, Caja, Factura
- migraciones
- entidades productivas de Venta

## G. Seguridad/permisos

Se aplico:

- `[Authorize]`
- `[PermisoRequerido(Modulo = "ventas", Accion = "view")]`

Deuda: crear permiso especifico `cotizaciones.view` cuando el modulo de cotizaciones tenga entidad/persistencia propia.

## H. Tests

Se agregaron tests unitarios de contrato UI/MVC:

- autorizacion y permiso del controller;
- `Index` devuelve `Index_tw`;
- el controller no depende de Venta/Caja/Stock;
- la vista consume `POST /api/cotizacion/simular` y script propio;
- el script no depende de `venta-create.js` ni de endpoints `/api/ventas/Buscar...`.

## I. Riesgos/deuda

- El selector de producto es minimo y read-only. Usa endpoints propios de Cotizacion, pero la busqueda reutiliza `IProductoService.BuscarParaVentaAsync`, que ya resuelve precio vigente para venta.
- El selector de cliente es minimo. No crea cliente ni evalua credito.
- La UI muestra opciones de planes devueltas por el contrato actual; si V1.1 necesita convertir una opcion exacta a venta, podria requerir IDs de plan/configuracion en los DTOs.
- Se agrego acceso minimo en la navegacion principal bajo Operaciones, visible con `ventas/view`.

## J. Checklist actualizado

Carlos:

- [x] Diagnostico Ventas/Cotizacion cerrado.
- [x] Diseno V1 cerrado.
- [x] V1A DTOs/interfaz/tests base cerrado.
- [x] V1B calculo real read-only basico cerrado.
- [x] V1C API/controller read-only cerrado.
- [x] V1D credito personal simulado read-only cerrado.
- [x] V1E UI cotizacion separada.
- [ ] V1.1 persistencia.
- [ ] Conversion Cotizacion -> Venta.

Juan:

- [ ] 10.7 Finalizacion reparacion pendiente/en curso segun repo principal.
- [ ] 10.8 QA E2E devolucion/garantia pendiente.
