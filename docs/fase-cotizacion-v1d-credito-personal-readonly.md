# Fase Cotizacion V1D - Credito personal read-only

Agente: Carlos - Cotizacion V1D credito personal read-only.

## A. Objetivo

Agregar simulacion read-only de credito personal al resultado de Cotizacion V1, sin crear credito definitivo, venta, caja, stock, ProductoUnidad, factura ni persistencia de cotizacion.

## B. Diagnostico de servicios de credito

| Pregunta | Respuesta |
|---|---|
| Servicio canonico de simulacion sin credito definitivo | `ICreditoSimulacionVentaService.SimularAsync`; calcula un plan y devuelve JSON de simulacion, sin persistir credito. |
| Servicio que calcula cuota/interes | `IFinancialCalculationService`, consumido por `CreditoSimulacionVentaService`. |
| Servicio de restricciones por producto | `IProductoCreditoRestriccionService.ResolverAsync`; usa `ProductoCreditoRestriccion` y no `ProductoCondicionPago` legacy. |
| Servicio de configuracion/tasa | `IConfiguracionPagoService.ObtenerTasaInteresMensualCreditoPersonalAsync`, consumido indirectamente por `CreditoSimulacionVentaService`. Los planes/cuotas disponibles salen de `IConfiguracionPagoGlobalQueryService`. |
| Metodos con efectos secundarios a evitar | `CreditoService` de solicitud/alta, `VentaService` de confirmacion/guardado, `CajaService`, `MovimientoStockService`, `ProductoUnidadService` de transiciones, facturacion y cualquier metodo que guarde evaluaciones. |
| Datos minimos con cliente | `ClienteId`, productos validos, total calculado, configuracion activa de `CreditoPersonal`, planes activos, tasa configurada y restricciones por producto resueltas. |
| Comportamiento sin cliente | Devuelve opcion `CreditoPersonal` en `RequiereCliente`, `Disponible = false`, con advertencia clara y sin invocar simulacion de credito. |
| Cliente opcional en request | Ya estaba bien modelado: `CotizacionSimulacionRequest.ClienteId` es `int?` y permite `NombreClienteLibre`. |

## C. Clasificacion de componentes

| Componente | Clasificacion | Evidencia | Decision |
|---|---|---|---|
| `CotizacionPagoCalculator` | canonico nuevo | Implementa `ICotizacionPagoCalculator`, registrado en DI y testeado como contrato de Cotizacion V1. | Modificado. |
| `ICreditoSimulacionVentaService` | canonico | Registrado en DI, usado por `CreditoController`, tiene tests propios y no persiste credito. | Usado para planes read-only. |
| `IFinancialCalculationService` | canonico | Servicio puro registrado en DI y cubierto por tests de calculo financiero. | Usado indirectamente. |
| `IProductoCreditoRestriccionService` | canonico | Servicio read-only `AsNoTracking`, tests verifican bloqueos, max cuotas y no uso de `ProductoCondicionPago` legacy. | Usado para restricciones. |
| `IConfiguracionPagoService` | canonico mixto | Tiene metodos write, pero `ObtenerTasaInteresMensualCreditoPersonalAsync` es query de tasa; lo consume el simulador canonico. | Usado indirectamente, sin ampliar dependencia directa. |
| `IConfiguracionPagoGlobalQueryService` | canonico | Query `AsNoTracking` de medios/planes activos; ya usado por V1B. | Usado para planes de cuotas de `CreditoPersonal`. |
| `VentaService` | canonico Venta | Orquesta venta productiva, credito definitivo, caja/stock segun flujo. | No tocar/no inyectar. |
| `ProductoCondicionPago` | legacy/paralelo | Tests de restricciones confirman que no afecta credito personal actual. | No usar. |

Graphify: no habia `graphify-out/graph.json` en este worktree; se continuo con `rg`, lectura directa, DI y tests existentes.

## D. Decision tecnica

Cotizacion V1D integra credito personal en `CotizacionPagoCalculator` como simulacion read-only:

- usa `IProductoCreditoRestriccionService` para bloqueo y maximo de cuotas por producto;
- usa planes activos de `IConfiguracionPagoGlobalQueryService` para decidir cuotas a mostrar;
- usa `ICreditoSimulacionVentaService` para calcular cada plan;
- no usa servicios de creacion de credito definitivo ni Venta.

Se ampliaron campos minimos de `CotizacionPlanPagoResultado` para planes de credito:

- `TasaMensual`;
- `CostoFinancieroTotal`;
- `TipoCalculo`.

## E. Comportamiento sin cliente

Si `IncluirCreditoPersonal == true` y `ClienteId == null`:

- agrega opcion `CreditoPersonal`;
- `Estado = RequiereCliente`;
- `Disponible = false`;
- motivo: requiere cliente para evaluacion;
- advertencia: requiere cliente y evaluacion antes de confirmar;
- no invoca simulacion de credito ni restricciones por producto.

## F. Comportamiento con cliente

Si `ClienteId != null`:

1. Resuelve restricciones por producto.
2. Si hay producto bloqueante, devuelve `BloqueadoPorProducto`.
3. Si no hay configuracion/planes activos, devuelve `RequiereEvaluacion`.
4. Si hay planes, simula cada cuota permitida con `ICreditoSimulacionVentaService`.
5. Si al menos un plan se calcula, devuelve `Disponible` con planes.

Los planes incluyen cantidad de cuotas, total, valor cuota, tasa mensual, interes/costo financiero y advertencias de limite por producto cuando aplica.

## G. Restricciones por producto

- `Permitido = false` bloquea credito personal para toda la cotizacion.
- `MaxCuotasCredito` limita los planes mostrados.
- En multiples productos se usa el limite mas restrictivo, segun `ProductoCreditoRestriccionService`.
- No se reintrodujo `ProductoCondicionPago` como fuente de credito personal.

## H. Tests

Archivo actualizado:

- `TheBuryProyect.Tests/Unit/CotizacionPagoCalculatorContractTests.cs`

Casos agregados o reforzados:

- credito personal sin cliente devuelve `RequiereCliente`;
- sin cliente no invoca simulacion ni restricciones;
- con cliente y sin configuracion/calculo posible devuelve `RequiereEvaluacion`;
- con cliente y simulador canonico devuelve planes;
- producto bloqueado devuelve `BloqueadoPorProducto`;
- respeta `MaxCuotasCredito`;
- multiples productos usan restriccion mas baja;
- no crea venta, no registra caja, no toca stock y no crea credito definitivo por diseno de dependencias.

## I. Que NO se toco

- `Services/VentaService.cs`
- `Services/Interfaces/IVentaService.cs`
- `Controllers/VentaController.cs`
- `Controllers/VentaApiController.cs`
- `Views/Venta/*`
- `wwwroot/js/venta-create.js`
- `Services/DevolucionService.cs`
- `Controllers/DevolucionController.cs`
- `Views/Devolucion/*`
- `ProductoUnidad`
- `MovimientoStock`
- `Caja`
- `Factura`
- migraciones
- UI Razor / JS

## J. Riesgos/deuda

- La simulacion usa planes activos globales de `CreditoPersonal`; si el negocio necesita min/max por cliente/perfil en Cotizacion, conviene agregar un query read-only especifico antes de V1E.
- `ICreditoSimulacionVentaService` no recibe `ClienteId`; por ahora la presencia de cliente habilita simulacion estimativa, no evaluacion crediticia persistida.
- No se ejecuta scoring nuevo ni evaluacion guardada; esto es intencional para mantener read-only.
- La futura UI puede requerir IDs de plan/configuracion si luego convierte cotizacion en venta.

## K. Checklist actualizado

Carlos:

- [x] Diagnostico Ventas/Cotizacion cerrado.
- [x] Diseno V1 cerrado.
- [x] V1A DTOs/interfaz/tests base cerrado.
- [x] V1B calculo real read-only basico cerrado.
- [x] V1C API/controller read-only cerrado.
- [x] V1D credito personal simulado read-only.
- [ ] V1E UI cotizacion separada.
- [ ] V1.1 persistencia.
- [ ] Conversion Cotizacion -> Venta.

Juan:

- [ ] 10.7 Finalizacion reparacion pendiente/en curso segun repo principal.
- [ ] 10.8 QA E2E devolucion/garantia pendiente.
