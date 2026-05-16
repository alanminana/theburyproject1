# Fase Cotizacion V1A - Contratos base

Agente: Carlos - Cotizacion V1A DTOs, contrato y tests base.

## A. Objetivo

Crear la base contractual de Cotizacion V1 como simulador no persistido: DTOs de entrada/salida, enums propios, interfaz `ICotizacionPagoCalculator`, implementacion minima controlada y tests unitarios de contrato.

## B. Decision

Esta fase solo agrega contratos y validacion minima. No implementa calculo real de pagos, no crea API, no crea UI, no persiste cotizaciones y no integra con el pipeline productivo de Venta.

## C. Archivos agregados

- `Services/Interfaces/ICotizacionPagoCalculator.cs`
- `Services/CotizacionPagoCalculator.cs`
- `Services/Models/CotizacionSimulacionRequest.cs`
- `Services/Models/CotizacionProductoRequest.cs`
- `Services/Models/CotizacionSimulacionResultado.cs`
- `Services/Models/CotizacionProductoResultado.cs`
- `Services/Models/CotizacionMedioPagoResultado.cs`
- `Services/Models/CotizacionPlanPagoResultado.cs`
- `Services/Models/CotizacionMedioPagoTipo.cs`
- `Services/Models/CotizacionOpcionPagoEstado.cs`
- `TheBuryProyect.Tests/Unit/CotizacionPagoCalculatorContractTests.cs`
- `docs/fase-cotizacion-v1a-contratos.md`

## D. Que no se toco

- `Services/VentaService.cs`
- `Services/Interfaces/IVentaService.cs`
- `Controllers/VentaController.cs`
- `Controllers/VentaApiController.cs`
- `Views/Venta/*`
- `wwwroot/js/venta-create.js`
- Devolucion, Garantia, ProductoUnidad, MovimientoStock, Caja, Factura, migraciones y `AppDbContext`.

## E. Tests

Tests unitarios agregados:

- request permite cliente opcional y nombre libre;
- resultado representa opcion disponible;
- resultado representa opcion no disponible;
- resultado representa advertencias;
- enums contienen medios y estados esperados;
- calculator implementa contrato;
- calculator sin productos devuelve error controlado;
- calculator con producto valido no requiere cliente.

## F. Riesgos

- La implementacion minima no calcula importes reales; V1B debe reemplazar la advertencia por calculo efectivo.
- No hay registro DI todavia porque no existe consumidor de aplicacion.
- No se actualizo `docs/fase-cotizacion-diseno-v1.md` porque no existe en este worktree.

## G. Proxima fase sugerida

V1B: implementar calculo real read-only para efectivo, transferencia y tarjeta usando `ConfiguracionPagoGlobalRules` e `IConfiguracionPagoGlobalQueryService`, con tests unitarios primero.

Nota V1B: `docs/fase-cotizacion-v1b-calculator-readonly.md` documenta la implementacion inicial de calculo read-only para productos, efectivo, transferencia, tarjeta, MercadoPago configurado y credito personal no definitivo.

## H. Checklist actualizado

Carlos:

- [x] Diagnostico Ventas/Cotizacion cerrado.
- [x] Diseno V1 cerrado.
- [x] V1A DTOs/interfaz/tests base cerrado.
- [ ] V1B implementacion calculator real.
- [ ] V1C API/UI cotizacion separada.
- [ ] V1.1 persistencia.
- [ ] Conversion Cotizacion -> Venta.

Juan:

- [x] 10.5 Descarte -> Baja aparentemente commiteado en `ddecd5b`.
- [ ] RMA / DevolverProveedor con unidad fisica.
- [ ] Finalizacion reparacion.
- [ ] QA E2E devolucion/garantia.
