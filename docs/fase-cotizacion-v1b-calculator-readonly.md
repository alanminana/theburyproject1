# Fase Cotizacion V1B - Calculator read-only inicial

Agente: Carlos - Cotizacion V1B calculator read-only inicial.

## A. Objetivo

Implementar el primer calculo real de Cotizacion V1 como simulador no persistido, separado del pipeline productivo de Venta.

Alcance cubierto:

- productos y precios vigentes;
- efectivo;
- transferencia;
- tarjeta de credito;
- tarjeta de debito si existe configuracion activa;
- MercadoPago si esta modelado como medio activo;
- credito personal solo como estado no definitivo.

## B. Componentes revisados

| Componente | Clasificacion | Evidencia | Decision |
|---|---|---|---|
| `CotizacionPagoCalculator.cs` | canonico nuevo | Implementa `ICotizacionPagoCalculator`; V1A lo dejo como implementacion minima. | Reemplazado por calculo read-only. |
| `ICotizacionPagoCalculator.cs` | canonico nuevo | Contrato propio de Cotizacion V1, sin dependencia de Venta. | Mantener firma publica. |
| `ConfiguracionPagoGlobalRules` | canonico | Reglas puras sin DB, con tests unitarios para ajuste, descuento y valor de cuota. | Usar directamente para planes. |
| `IConfiguracionPagoGlobalQueryService` | canonico | Query `AsNoTracking`; devuelve medios activos, tarjetas activas y planes activos; excluye `TipoPago.Tarjeta` historico. | Usar como fuente read-only de configuracion. |
| `IProductoService.ObtenerPrecioVigenteParaVentaAsync` | canonico | Devuelve precio vigente de lista o precio base; `null` para producto inexistente/inactivo. | Usar para resolver precio. |
| DTOs `Cotizacion*` | canonicos nuevos | Creados en V1A y testeados como contrato de simulacion. | Reutilizados sin ampliar superficie publica. |

Graphify: no habia `graphify-out/graph.json` en este worktree, por lo que el analisis continuo con lectura directa y busquedas `rg`.

## C. Decision tecnica

`CotizacionPagoCalculator` usa solo:

- `IProductoService`;
- `IConfiguracionPagoGlobalQueryService`;
- `ConfiguracionPagoGlobalRules`.

No usa `VentaService`, `VentaApiController`, `IConfiguracionPagoService`, `Caja`, stock, unidades fisicas, factura ni credito definitivo.

## D. Dependencias read-only usadas

- `IProductoService.ObtenerPrecioVigenteParaVentaAsync`: consulta precio vigente.
- `IConfiguracionPagoGlobalQueryService.ObtenerActivaParaVentaAsync`: consulta medios, tarjetas y planes activos.
- `ConfiguracionPagoGlobalRules.Calcular`: calcula ajuste, total final y valor de cuota.

## E. Medios implementados

- Efectivo: disponible aun sin configuracion especifica; si hay plan general activo aplica su ajuste.
- Transferencia: disponible aun sin configuracion especifica; si hay plan general activo aplica su ajuste.
- Tarjeta credito: disponible solo si hay medio, tarjeta y plan activo.
- Tarjeta debito: disponible solo si hay medio, tarjeta y plan activo.
- MercadoPago: disponible solo si esta configurado como medio activo.

## F. Medios no implementados

- Credito personal real: queda como `RequiereCliente` o `RequiereEvaluacion`.
- Cheque/cuenta corriente: fuera de alcance de V1B.
- Integracion externa MercadoPago: fuera de alcance; MercadoPago solo se trata como medio configurado.

## G. Reglas de calculo

- Producto invalido o cantidad invalida devuelve error y `Exitoso = false`.
- Producto sin precio vigente devuelve error y `Exitoso = false`.
- Precio manual no se aplica en V1B; se informa advertencia y se usa precio vigente.
- Descuento por producto y descuento general se validan contra valores negativos, porcentaje mayor a 100 y descuento mayor al subtotal.
- `Subtotal` conserva base bruta de productos.
- `DescuentoTotal` suma descuentos por producto y generales.
- `TotalBase` es subtotal menos descuentos.
- Planes con ajuste positivo informan recargo/interes; ajuste negativo informa descuento.
- Valor de cuota se calcula con `ConfiguracionPagoGlobalRules`.
- Planes/tarjetas inactivas no se muestran porque el query canonico ya los excluye.

## H. Tests

Archivo actualizado:

- `TheBuryProyect.Tests/Unit/CotizacionPagoCalculatorContractTests.cs`

Cobertura agregada:

- producto valido calcula subtotal y total base;
- cotizacion sin cliente;
- producto sin precio;
- cantidad invalida;
- efectivo y transferencia disponibles;
- efectivo y transferencia sin cuotas;
- tarjeta credito genera planes activos;
- tarjeta no muestra cuotas/planes no disponibles;
- tarjeta calcula valor de cuota;
- tarjeta solicitada inexistente/inactiva devuelve no disponible;
- MercadoPago sin mapeo devuelve advertencia;
- credito personal sin cliente requiere cliente;
- credito personal no crea credito definitivo;
- no efectos secundarios por ausencia de servicios de Venta/Caja/Stock y fakes que solo exponen consultas read-only.

## I. Que NO se toco

- `Services/VentaService.cs`
- `Services/Interfaces/IVentaService.cs`
- `Controllers/VentaController.cs`
- `Controllers/VentaApiController.cs`
- `Views/Venta/*`
- `wwwroot/js/venta-create.js`
- Devolucion, Garantia, ProductoUnidad, MovimientoStock, Caja, Factura, migraciones y entidades de Venta.

## J. Riesgos/deuda

- La respuesta de planes todavia no expone IDs de tarjeta/plan; V1C API/UI podria necesitar ampliar DTOs.
- Efectivo/transferencia se muestran disponibles sin configuracion especifica por decision incremental; si negocio exige configuracion obligatoria, cambiar a `NoDisponible`.
- MercadoPago depende de que exista como `TipoPago.MercadoPago` activo en configuracion global.
- Credito personal real queda para V1C/V1D.

## K. Checklist actualizado

Nota V1C: `docs/fase-cotizacion-v1c-api-simulacion.md` expone el calculator mediante `POST /api/cotizacion/simular` en un controller read-only separado de Venta.

Carlos:

- [x] Diagnostico Ventas/Cotizacion cerrado.
- [x] Diseno V1 cerrado.
- [x] V1A DTOs/interfaz/tests base cerrado.
- [x] V1B calculo real read-only basico.
- [ ] V1C API/UI cotizacion separada.
- [ ] V1D credito personal simulado.
- [ ] V1.1 persistencia.
- [ ] Conversion Cotizacion -> Venta.

Juan:

- [ ] 10.6 DevolverProveedor / RMA con unidad fisica.
- [ ] 10.7 Finalizacion reparacion.
- [ ] 10.8 QA E2E devolucion/garantia.
