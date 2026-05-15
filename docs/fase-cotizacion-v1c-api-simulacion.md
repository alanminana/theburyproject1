# Fase Cotizacion V1C - API read-only de simulacion

Agente: Carlos - Cotizacion V1C API/controller read-only.

## A. Objetivo

Exponer el calculator read-only de Cotizacion V1 mediante un endpoint propio, separado de `VentaController`, `VentaApiController` y `venta-create.js`.

La API permite simular cotizaciones no persistidas sin crear venta, caja, stock, factura, credito definitivo ni cotizacion persistida.

## B. Ruta del endpoint

Controller:

- `Controllers/CotizacionApiController.cs`

Endpoint:

- `POST /api/cotizacion/simular`

La ruta usa singular `cotizacion` porque el alcance V1 es simulacion no persistida de una cotizacion puntual. Queda separada de `api/ventas`.

## C. Request / response

Request:

- body JSON compatible con `CotizacionSimulacionRequest`.

Response:

- body JSON compatible con `CotizacionSimulacionResultado`.

No se ampliaron DTOs en V1C. La necesidad de exponer IDs de plan/tarjeta para UI futura queda como deuda de V1D/V1E.

## D. Status codes

- `400 BadRequest`: request nulo o `ModelState` invalido.
- `200 OK`: simulacion ejecutada, incluso si `resultado.Exitoso = false` por errores funcionales como producto sin precio vigente.
- `500 InternalServerError`: excepcion no controlada del calculator o dependencia read-only.

Decision: errores funcionales de simulacion se devuelven como contrato de negocio en `CotizacionSimulacionResultado`, no como fallo HTTP, para que la futura UI pueda mostrar errores y advertencias de cotizacion de forma uniforme.

## E. Seguridad / permisos

Se aplico:

- `[Authorize]`
- `[PermisoRequerido(Modulo = "ventas", Accion = "view")]`

No existe permiso especifico de Cotizacion. Por compatibilidad incremental se reutiliza lectura de Ventas, dado que la cotizacion opera sobre precios y medios de pago de venta pero no confirma ventas.

Deuda: definir permiso especifico `cotizaciones.view` o modulo equivalente cuando exista UI/backoffice de Cotizaciones.

## F. Tests

Archivo agregado:

- `TheBuryProyect.Tests/Unit/CotizacionApiControllerTests.cs`

Casos cubiertos:

- request nulo devuelve `BadRequest`;
- request valido llama a `ICotizacionPagoCalculator` y devuelve `Ok`;
- resultado con advertencias devuelve `Ok` con advertencias;
- resultado funcional no exitoso devuelve `Ok` con `Exitoso = false`;
- el controller no inyecta Venta/Caja/Stock;
- ruta documentada por atributos;
- autorizacion y permiso aplicado.

No se agrego integration test con `WebApplicationFactory` porque los patrones existentes para controllers cercanos usan tests unitarios con fakes y no requieren DB.

## G. Que NO se toco

- `Services/VentaService.cs`
- `Services/Interfaces/IVentaService.cs`
- `Controllers/VentaController.cs`
- `Controllers/VentaApiController.cs`
- `Views/Venta/*`
- `wwwroot/js/venta-create.js`
- Devolucion, Garantia, ProductoUnidad, MovimientoStock, Caja, Factura, migraciones y entidades de Venta.

## H. Riesgos / deuda

- Falta permiso especifico de Cotizacion.
- V1E agrega UI separada para consumir `POST /api/cotizacion/simular`.
- Falta contrato enriquecido con IDs de plan/tarjeta si la UI necesita seleccionar una opcion exacta.
- V1D agrega simulacion read-only de credito personal; sigue pendiente conversion a venta/credito definitivo.
- Persistencia y conversion Cotizacion -> Venta siguen fuera de alcance.

## I. Checklist actualizado

Carlos:

- [x] Diagnostico Ventas/Cotizacion cerrado.
- [x] Diseno V1 cerrado.
- [x] V1A DTOs/interfaz/tests base cerrado.
- [x] V1B calculo real read-only basico.
- [x] V1C API/controller read-only.
- [ ] V1D credito personal simulado real.
- [ ] V1E UI cotizacion separada.
- [ ] V1.1 persistencia.
- [ ] Conversion Cotizacion -> Venta.

Juan:

- [ ] 10.6 DevolverProveedor / RMA con unidad fisica.
- [ ] 10.7 Finalizacion reparacion.
- [ ] 10.8 QA E2E devolucion/garantia.
