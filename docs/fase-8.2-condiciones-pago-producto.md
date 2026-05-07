# Fase 8.2 - Diseno tecnico de condiciones de pago por producto

Agente Kira

## A. Diagnostico tecnico posterior a Fase 8.1

La Fase 8.1 dejo congelado el comportamiento actual sin modificar logica productiva:

- `ConfiguracionPago` sigue siendo la configuracion global por `TipoPago`.
- `ConfiguracionTarjeta` sigue concentrando reglas de tarjetas: activa/inactiva, cuotas, tipo de cuota, tasa y recargo de debito.
- `Producto.MaxCuotasSinInteresPermitidas` es la unica regla por producto actualmente existente.
- `ConfiguracionPagoService.ObtenerMaxCuotasSinInteresEfectivoAsync` resuelve solo tarjetas sin interes y devuelve `null` cuando la tarjeta no existe, esta inactiva, no es sin interes o no tiene `CantidadMaximaCuotas`.
- En carrito multi-producto, los productos con `MaxCuotasSinInteresPermitidas = null` se ignoran y los productos con valor participan por minimo.
- El valor actual `MaxCuotasSinInteresPermitidas = 0` queda caracterizado como `Math.Max(1, efectivo)`, por lo tanto termina en `1`.
- `VentaApiController.CalcularTotalesVenta` envia todos los `ProductoId` del carrito a la resolucion de maximo efectivo.
- El recargo de debito se calcula actualmente desde `ConfiguracionTarjeta` en preview y se persiste en `DatosTarjeta.RecargoAplicado` al guardar datos de tarjeta.
- `CajaService.RegistrarMovimientoVentaAsync` conserva `MovimientoCaja.Monto == venta.Total`, vincula `VentaId`, guarda `TipoPago`, `MedioPagoDetalle` y snapshot de `RecargoDebitoAplicado`.

Restriccion importante para fases siguientes: el nuevo modelo no debe duplicar la logica de `ConfiguracionPagoService`, `VentaService`, `CreditoService` ni `CajaService`; debe introducir un contrato de resolucion efectiva que esos componentes consuman despues.

## B. Entidades propuestas

Recomendacion: usar una entidad raiz `ProductoCondicionPago` por producto y medio de pago, con entidades hijas solo cuando el medio necesita detalle especifico.

### ProductoCondicionPago

Entidad raiz para declarar si un producto hereda o sobrescribe la configuracion global de un `TipoPago`.

Campos propuestos:

- `Id`
- `ProductoId`
- `TipoPago`
- `bool? Permitido`: `null` = usa global; `true` = permitido para este producto; `false` = bloqueado para este producto.
- `bool? PermiteDescuento`: `null` = usa global.
- `decimal? PorcentajeDescuentoMaximo`: `null` = usa global.
- `bool? TieneRecargo`: `null` = usa global.
- `decimal? PorcentajeRecargo`: `null` = usa global.
- `bool Activo`
- `string? Observaciones`
- auditoria heredada de `AuditableEntity`.

### ProductoCondicionPagoTarjeta

Detalle opcional para tarjetas cuando `TipoPago` sea `TarjetaCredito`, `TarjetaDebito` o alias legacy `Tarjeta`.

Campos propuestos:

- `Id`
- `ProductoCondicionPagoId`
- `int? ConfiguracionTarjetaId`: `null` = regla para cualquier tarjeta del tipo de pago; con valor = regla especifica para esa tarjeta.
- `bool? Permitida`: `null` = hereda de `ProductoCondicionPago.Permitido` y luego global/tarjeta.
- `bool? PermiteCuotas`: `null` = usa tarjeta/global.
- `int? CantidadMaximaCuotas`: `null` = usa tarjeta/global.
- `TipoCuotaTarjeta? TipoCuota`: `null` = usa tarjeta.
- `decimal? TasaInteresesMensual`: `null` = usa tarjeta.
- `bool? TieneRecargoDebito`: `null` = usa tarjeta.
- `decimal? PorcentajeRecargoDebito`: `null` = usa tarjeta.
- `bool Activo`
- `string? Observaciones`.

### ProductoCondicionPagoCredito

Detalle opcional para `TipoPago.CreditoPersonal` y, si se decide, `CuentaCorriente`.

Campos propuestos:

- `Id`
- `ProductoCondicionPagoId`
- `int? MinCuotas`: `null` = usa global/perfil/cliente.
- `int? MaxCuotas`: `null` = usa global/perfil/cliente.
- `decimal? TasaInteresMensual`: `null` = usa global/perfil/cliente.
- `decimal? GastosAdministrativos`: `null` = usa global/perfil/cliente.
- `decimal? MontoMinimo`: `null` = sin restriccion por producto.
- `decimal? MontoMaximo`: `null` = sin restriccion por producto.
- `bool? RequiereGarante`: `null` = usa reglas actuales.
- `bool Activo`
- `string? Observaciones`.

### Alternativa descartada para Fase 8.3

No conviene agregar columnas directas a `Producto` para cada medio de pago. Escala mal, mezcla catalogo con politica comercial, y haria dificil expresar reglas por tarjeta concreta, credito o futuras billeteras.

## C. Relaciones EF propuestas

Relaciones:

- `Producto 1:N ProductoCondicionPago`.
- `ProductoCondicionPago 1:0..1 ProductoCondicionPagoTarjeta`.
- `ProductoCondicionPago 1:0..1 ProductoCondicionPagoCredito`.
- `ProductoCondicionPagoTarjeta N:1 ConfiguracionTarjeta` opcional.

Indices:

- `UX_ProductoCondicionesPago_Producto_TipoPago_Activo`: unico filtrado por `IsDeleted = 0` para evitar dos reglas activas del mismo producto y medio.
- `IX_ProductoCondicionesPago_TipoPago`.
- `IX_ProductoCondicionesPago_ProductoId`.
- `UX_ProductoCondicionesPagoTarjeta_Condicion_Tarjeta`: unico filtrado por `IsDeleted = 0`; permite un detalle general `ConfiguracionTarjetaId = null` y detalles por tarjeta si la DB lo soporta con filtro especifico.
- `IX_ProductoCondicionesPagoTarjeta_ConfiguracionTarjetaId`.

Constraints sugeridas:

- porcentajes entre `0` y `100`.
- cuotas mayores o iguales a `1` para datos nuevos.
- `MinCuotas <= MaxCuotas` cuando ambos existan.
- si `TipoCuota = ConInteres`, tasa requerida cuando se sobrescribe la tasa.
- si `TieneRecargoDebito = true`, `PorcentajeRecargoDebito` requerido y mayor a `0`.

Delete behavior:

- `Producto -> ProductoCondicionPago`: `Restrict` o soft delete coordinado. Recomendado `Restrict`, porque el sistema ya usa `IsDeleted` y conviene preservar auditoria comercial.
- `ProductoCondicionPago -> hijos`: `Cascade`, porque los hijos no tienen sentido sin la raiz.
- `ProductoCondicionPagoTarjeta -> ConfiguracionTarjeta`: `SetNull`, igual que `DatosTarjeta`, para preservar la regla historica aunque se elimine/desactive una tarjeta.

## D. DTOs/contratos propuestos

### Lectura para catalogo/modal futuro

`ProductoCondicionesPagoCatalogoDto`

- `ProductoId`
- `ProductoCodigo`
- `ProductoNombre`
- `IReadOnlyList<ProductoCondicionPagoDto> Condiciones`

`ProductoCondicionPagoDto`

- `Id`
- `TipoPago`
- `bool? Permitido`
- `bool Activo`
- `decimal? PorcentajeDescuentoMaximo`
- `decimal? PorcentajeRecargo`
- `ProductoCondicionPagoTarjetaDto? Tarjeta`
- `ProductoCondicionPagoCreditoDto? Credito`
- `string? Observaciones`

### Escritura para catalogo/modal futuro

`GuardarProductoCondicionesPagoRequest`

- `ProductoId`
- `IReadOnlyList<GuardarProductoCondicionPagoItem> Condiciones`
- `byte[]? RowVersion` o rowversion por item si se edita granularmente.

`GuardarProductoCondicionPagoItem`

- `int? Id`
- `TipoPago TipoPago`
- `bool? Permitido`
- campos nullable de descuento/recargo.
- `GuardarProductoCondicionPagoTarjetaItem? Tarjeta`
- `GuardarProductoCondicionPagoCreditoItem? Credito`
- `bool Activo`
- `string? Observaciones`

### Resolucion efectiva para venta/carrito

`ResolverCondicionesPagoCarritoRequest`

- `IReadOnlyList<ProductoCarritoPagoItem> Productos`
- `TipoPago? TipoPagoSeleccionado`
- `int? ConfiguracionTarjetaId`
- `int? ClienteId`
- `decimal Total`

`ProductoCarritoPagoItem`

- `int ProductoId`
- `decimal Cantidad`
- `decimal PrecioUnitario`

`CondicionesPagoCarritoResultado`

- `IReadOnlyList<MedioPagoEfectivoDto> Medios`
- `IReadOnlyList<CondicionPagoBloqueoDto> Bloqueos`
- `bool TieneRestriccionesPorProducto`

`MedioPagoEfectivoDto`

- `TipoPago TipoPago`
- `bool Permitido`
- `string FuentePermitido`: `Global`, `Producto`, `Tarjeta`, `Carrito`.
- `int? ConfiguracionTarjetaId`
- `int? MaxCuotas`
- `bool MaxCuotasLimitadoPorProducto`
- `decimal? PorcentajeRecargo`
- `decimal? PorcentajeDescuentoMaximo`
- `decimal? RecargoPreview`
- `decimal? DescuentoMaximoPreview`
- `IReadOnlyList<int> ProductoIdsRestrictivos`
- `IReadOnlyList<string> Advertencias`

`CondicionPagoBloqueoDto`

- `TipoPago TipoPago`
- `int ProductoId`
- `string Motivo`

Importante: este DTO debe representar recargos/descuentos, pero no aplicarlos todavia en Fase 8.3 salvo los caminos ya existentes.

## E. Reglas de prioridad propuestas

Prioridad general:

1. Estado estructural: producto activo/no eliminado, medio global activo, tarjeta activa.
2. Bloqueo explicito por producto (`Permitido = false`).
3. Regla especifica por tarjeta del producto.
4. Regla general por tipo de pago del producto.
5. Configuracion global por `TipoPago`.
6. Defaults actuales de servicio, solo donde ya existan.

Reglas concretas:

- `null` significa "usar global/heredado" en campos configurables.
- `Permitido = false` en cualquier producto del carrito bloquea ese medio para todo el carrito.
- `MaxCuotas` debe resolverse como minimo restrictivo entre tarjeta, productos y, para credito personal, reglas de cliente/perfil/global/producto.
- Productos sin regla o con campo `null` no participan en el minimo restrictivo.
- Para tarjeta sin interes, mantener el comportamiento actual: si no hay `CantidadMaximaCuotas`, la resolucion devuelve `null`.
- `MaxCuotasSinInteresPermitidas = 0 => 1` debe documentarse como comportamiento legacy caracterizado, no como regla nueva deseada.
- Recargos/descuentos por producto deben almacenarse y exponerse en resolucion efectiva, pero no aplicarse a venta/caja hasta una fase posterior explicita.

## F. Validaciones de negocio propuestas

Guardado:

- `ProductoId` existente, activo y no eliminado.
- una sola condicion activa por `ProductoId + TipoPago`.
- `Permitido = false` no deberia convivir con overrides financieros obligatorios; se pueden guardar observaciones, pero los campos economicos deberian quedar null.
- porcentajes `0..100`.
- cuotas `>= 1` para nuevas condiciones.
- `MinCuotas <= MaxCuotas`.
- tarjeta hija solo para tipos tarjeta.
- credito hijo solo para `CreditoPersonal` o `CuentaCorriente` si se aprueba funcionalmente.
- `ConfiguracionTarjetaId` debe existir y no estar eliminada; puede estar inactiva solo si se decide permitir preconfiguracion futura, pero la resolucion efectiva debe bloquearla mientras este inactiva.
- `PorcentajeRecargoDebito` requerido si `TieneRecargoDebito = true`.
- `TasaInteresesMensual` requerida si se sobrescribe una tarjeta con cuotas con interes.

## G. Impacto esperado en venta, caja, credito, reportes y comprobantes

Venta:

- Fase 8.3 deberia introducir contratos y un resolver sin cambiar calculos actuales.
- En fases posteriores, `CalcularTotalesVenta` consumiria `CondicionesPagoCarritoResultado` para exponer medios permitidos, bloqueos y limites.
- La validacion de confirmacion deberia usar la misma resolucion efectiva para evitar divergencia entre preview y persistencia.

Caja:

- No deberia cambiar en Fase 8.3.
- Cuando se apliquen recargos por producto, caja debe seguir guardando `MovimientoCaja.Monto == venta.Total` y snapshots separados para desglose.

Credito:

- No cambiar `CreditoService` en Fase 8.3.
- Futuras reglas por producto deberian limitar rangos/tasas mediante un resolver, sin duplicar `CreditoConfiguracionHelper`.

Reportes:

- Sin impacto inmediato.
- Futuro: exponer origen de condiciones y recargos/descuentos aplicados como snapshots para auditoria comercial.

Comprobantes:

- Sin impacto inmediato.
- Futuro: si recargos/descuentos por producto afectan el total, deberian quedar snapshot en venta/detalle antes de aparecer en comprobantes.

## H. Dudas funcionales bloqueantes

- Confirmar formalmente que `null` significa "usar global" para todos los campos de condicion por producto.
- Confirmar que `Permitido = false` en un producto bloquea el medio para todo el carrito, aunque otros productos lo permitan.
- Confirmar si el minimo restrictivo aplica siempre a cuotas sin interes, cuotas con interes y credito personal.
- Confirmar si reglas por tarjeta especifica deben ganar sobre regla general de tarjeta del producto.
- Confirmar si `CuentaCorriente` entra en el alcance de condiciones por producto o queda fuera.
- Confirmar si recargo/descuento por producto sera solo configuracion almacenada por ahora, sin impacto en venta.
- Definir si `MaxCuotasSinInteresPermitidas = 0` debe migrarse luego a `null`, bloquearse por validacion nueva, o mantenerse legacy como `1`.
- Definir si una tarjeta inactiva con regla por producto debe ocultarse, bloquearse con motivo o mostrarse como no disponible.

## I. Alcance recomendado para Fase 8.3

Fase 8.3 deberia limitarse a preparacion tecnica sin cambio productivo:

- agregar clases de contratos/DTOs de condiciones de pago por producto.
- agregar modelos de resultado de resolucion efectiva.
- agregar interfaces del futuro resolver sin registrar implementacion productiva, o implementar solo logica pura/testeable sin conectarla a UI/venta.
- agregar tests unitarios de reglas puras si no dependen de DB.
- documentar comportamiento legacy de `MaxCuotasSinInteresPermitidas = 0`.
- no crear migraciones.
- no tocar UI.
- no cambiar calculos de venta, caja ni credito.

## J. Archivos que deberian modificarse en Fase 8.3

Posibles archivos nuevos:

- `Models/Entities/ProductoCondicionPago.cs`
- `Models/Entities/ProductoCondicionPagoTarjeta.cs`
- `Models/Entities/ProductoCondicionPagoCredito.cs`
- `ViewModels/ProductoCondicionesPagoViewModels.cs`
- `ViewModels/Requests/GuardarProductoCondicionesPagoRequest.cs`
- `Services/Models/CondicionesPagoCarritoResultado.cs`
- `Services/Interfaces/IProductoCondicionPagoResolver.cs`
- `Services/ProductoCondicionPagoRules.cs` o helper puro equivalente.
- tests unitarios nuevos para reglas de prioridad.

Archivos a tocar solo si la fase autoriza EF no migrado:

- `Data/AppDbContext.cs` para `DbSet` y configuracion EF.
- `Models/Entities/Producto.cs` para navegacion `CondicionesPago`.

Archivos que no deberian tocarse todavia:

- `Services/VentaService.cs`
- `Services/CreditoService.cs`
- `Services/CajaService.cs`
- `Controllers/VentaApiController.cs`
- vistas y JavaScript.
- migraciones.

## K. Riesgos/deuda tecnica

- El enum tiene alias legacy `CreditoPersonall = 5`; cualquier contrato debe normalizar a `CreditoPersonal`.
- Hay encoding mojibake en comentarios existentes; no mezclar limpieza de encoding con esta fase.
- La regla actual `0 => 1` es tecnicamente segura por clamp, pero funcionalmente ambigua.
- Si se aplican recargos por producto sin snapshots nuevos, caja/reportes/comprobantes podrian perder trazabilidad.
- Si preview y confirmacion usan resolvers distintos, reaparecera riesgo de divergencia entre UI y persistencia.
- Una entidad raiz demasiado generica puede volverse dificil de validar; por eso se recomiendan hijos especificos por tarjeta/credito.

## L. Checklist actualizado de Fase 8

- Fase 8.1: caracterizacion de comportamiento actual completada.
- Fase 8.2: diseno tecnico de modelo y contratos documentado.
- Fase 8.3: crear contratos/DTOs y reglas puras sin DB/productivo.
- Fase 8.4: introducir entidades EF y migracion, previa decision funcional cerrada.
- Fase 8.5: implementar resolver efectivo con DB y tests de integracion.
- Fase 8.6: conectar preview de venta sin alterar persistencia.
- Fase 8.7: conectar validacion de confirmacion y snapshots.
- Fase 8.8: UI de catalogo/modal.
- Fase 8.9: reportes/comprobantes y auditoria de origen.

## M. Prompt recomendado para la proxima fase

```
Agente Kira - Fase 8.3

Objetivo: crear contratos, DTOs y reglas puras para condiciones de pago por producto, sin tocar DB, sin migraciones, sin UI y sin modificar logica productiva de venta/caja/credito.

Usar como base docs/fase-8.2-condiciones-pago-producto.md.

Restricciones:
- no modificar servicios productivos existentes;
- no crear entidades EF definitivas conectadas al DbContext;
- no crear migraciones;
- no cambiar UI;
- no romper tests de Fase 8.1;
- representar null como "usa global";
- documentar el comportamiento legacy MaxCuotasSinInteresPermitidas = 0 => 1 sin cambiarlo.

Tareas:
- crear DTOs de lectura/escritura;
- crear DTOs de resolucion efectiva para carrito;
- crear helper puro de prioridad/resolucion;
- agregar tests unitarios del helper para Permitido=false, null usa global, minimo restrictivo y recargos/descuentos solo representados;
- ejecutar dotnet build y tests enfocados.
```
