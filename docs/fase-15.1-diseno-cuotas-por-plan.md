# Fase 15.1 - Diseno cuotas por plan activas/inactivas

Agente Kira

> Nota Fase 7.7D: documento historico de diseno. No debe leerse como autorizacion para reactivar pago por producto en Nueva Venta. El estado actual usa configuracion global de pagos, conserva **1 venta = 1 TipoPago principal** y clasifica `ProductoCondicionPago*` como legacy administrativo/no canonico. Para `CreditoPersonal`, solo permanecen restricciones de elegibilidad/rango (`Permitido` y `MaxCuotasCredito`) si aplican; no planes, ajustes ni pagos por item.

> Nota Fase 7.10: no existe admin/modal legacy vigente para administrar estos planes. Fueron retirados el modal, endpoints admin, service admin y JS asociado. Las referencias de este documento son historicas y no habilitan reactivar pago por producto; Nueva Venta sigue con configuracion global y `CreditoPersonal` solo conserva `Permitido`/`MaxCuotasCredito`.

## A. Diagnostico del modelo actual

El modelo actual trabaja con maximos escalares, no con planes individuales.

- `ProductoCondicionPago` define una condicion por producto y `TipoPago`, con `Permitido`, `Activo`, `MaxCuotasSinInteres`, `MaxCuotasConInteres`, `MaxCuotasCredito`, `PorcentajeRecargo`, `PorcentajeDescuentoMaximo` y observaciones.
- `ProductoCondicionPagoTarjeta` agrega una regla opcional para tarjeta general o tarjeta especifica. `ConfiguracionTarjetaId = null` representa regla general del medio tipo tarjeta.
- `ConfiguracionPago` conserva configuracion global por medio, incluyendo recargo/descuento e intereses/defaults de credito personal.
- `ConfiguracionTarjeta` conserva configuracion global por tarjeta: tipo debito/credito, maximo de cuotas, tipo de cuota e interes mensual.
- `ProductoCondicionPagoRules.ResolverCondicionesCarrito` toma las restricciones por producto y devuelve el minimo efectivo: si dos productos tienen 12 y 6 cuotas, el carrito queda en 6. Los ajustes de producto son informativos y no modifican totales.
- `CondicionesPagoCarritoResultado` expone maximos efectivos y detalles de bloqueo/restriccion, pero no expone una lista de cuotas visibles.
- En venta, `VentaApiController.CalcularTotalesVenta` todavia aplica el recargo de debito global existente. Las condiciones por producto se diagnostican por `/api/ventas/DiagnosticarCondicionesPagoCarrito`.
- En backend productivo, `VentaService` ya valida maximos de tarjeta y credito personal por producto, pero solo con la logica escalar actual.

Conclusion: la proxima estructura debe convivir con maximos existentes como fallback, no reemplazarlos de golpe.

## B. Propuesta de modelo de cuotas por plan

Recomendacion: crear una tabla comun para planes por condicion de pago, no tablas separadas por tarjeta/credito.

Entidad propuesta:

```csharp
public class ProductoCondicionPagoPlan : AuditableEntity
{
    public int ProductoCondicionPagoId { get; set; }
    public int? ProductoCondicionPagoTarjetaId { get; set; }

    public int CantidadCuotas { get; set; }
    public bool Activo { get; set; } = true;

    // Negativo = descuento, cero = sin ajuste, positivo = recargo.
    public decimal AjustePorcentaje { get; set; }

    public TipoAjustePagoPlan TipoAjuste { get; set; } = TipoAjustePagoPlan.Porcentaje;
    public string? Observaciones { get; set; }

    public virtual ProductoCondicionPago ProductoCondicionPago { get; set; } = null!;
    public virtual ProductoCondicionPagoTarjeta? ProductoCondicionPagoTarjeta { get; set; }
}
```

Enum propuesto:

```csharp
public enum TipoAjustePagoPlan
{
    Porcentaje = 0
}
```

Por ahora `TipoAjustePagoPlan` puede parecer redundante, pero deja explicitado que el ajuste actual es porcentual y evita reinterpretar el signo despues. No conviene agregar tipo recargo/descuento separado: el signo del porcentaje ya expresa la regla de negocio confirmada.

Constraints recomendadas:

- `CantidadCuotas >= 1`.
- `AjustePorcentaje` con precision `decimal(7,4)` o `decimal(8,4)`, permitiendo negativos, cero y positivos.
- Rango tecnico sugerido: `AjustePorcentaje >= -100 AND AjustePorcentaje <= 999.9999`. El limite inferior evita descuentos mayores al 100%; el superior evita datos absurdos sin impedir recargos altos configurables.
- `Observaciones` maximo 500.
- Indice unico activo por alcance: `ProductoCondicionPagoId + ProductoCondicionPagoTarjetaId + CantidadCuotas`, filtrado por `IsDeleted = 0`. Para SQL Server, tratar `NULL` de `ProductoCondicionPagoTarjetaId` con indice filtrado separado o columna computada solo si hace falta.

Relacion:

- Planes generales del medio: `ProductoCondicionPagoId` con `ProductoCondicionPagoTarjetaId = null`.
- Planes de tarjeta especifica/general tipo tarjeta: `ProductoCondicionPagoTarjetaId` informado cuando la cuota pertenece a esa regla.
- Credito personal: planes colgados de `ProductoCondicionPago`, sin regla de tarjeta.
- Mercado Pago, Tarjeta Debito y Tarjeta Credito usan la misma estructura de planes tipo tarjeta.

## C. Propuesta de DTOs

DTO de lectura:

```csharp
public sealed class ProductoCondicionPagoPlanDto
{
    public int? Id { get; init; }
    public int? ProductoCondicionPagoTarjetaId { get; init; }
    public int CantidadCuotas { get; init; }
    public bool Activo { get; init; } = true;
    public decimal AjustePorcentaje { get; init; }
    public TipoAjustePagoPlan TipoAjuste { get; init; } = TipoAjustePagoPlan.Porcentaje;
    public string? Observaciones { get; init; }
    public byte[]? RowVersion { get; init; }
}
```

Agregar a `ProductoCondicionPagoDto`:

- `IReadOnlyList<ProductoCondicionPagoPlanDto> Planes`.

Agregar a `ProductoCondicionPagoTarjetaDto`:

- `IReadOnlyList<ProductoCondicionPagoPlanDto> Planes`.

DTO de escritura:

```csharp
public sealed class GuardarProductoCondicionPagoPlanItem
{
    public int? Id { get; init; }
    public int CantidadCuotas { get; init; }
    public bool Activo { get; init; } = true;
    public decimal AjustePorcentaje { get; init; }
    public TipoAjustePagoPlan TipoAjuste { get; init; } = TipoAjustePagoPlan.Porcentaje;
    public string? Observaciones { get; init; }
    public byte[]? RowVersion { get; init; }
}
```

Resultado de resolver:

```csharp
public sealed class CondicionPagoPlanDisponibleDto
{
    public int CantidadCuotas { get; init; }
    public decimal AjustePorcentaje { get; init; }
    public FuenteCondicionPagoEfectiva Fuente { get; init; }
    public int? ProductoId { get; init; }
    public int? ConfiguracionTarjetaId { get; init; }
    public bool EsGlobalFallback { get; init; }
}
```

Agregar a `CondicionesPagoCarritoResultado`:

- `IReadOnlyList<CondicionPagoPlanDisponibleDto> PlanesDisponibles`.
- `bool UsaPlanesEspecificos`.
- `bool UsaFallbackGlobalPlanes`.
- `IReadOnlyList<CondicionPagoPlanGrupoDto> GruposPlanProducto` para carrito mixto.

## D. Propuesta de reglas/resolver

Reglas funcionales:

- Si hay planes especificos activos para el alcance aplicable, esos planes reemplazan al maximo escalar para la visibilidad.
- Plan inactivo no aparece y no se puede seleccionar.
- Si existen planes configurados pero todos estan inactivos, se considera que no hay configuracion especifica activa y se usa fallback global.
- Si no hay planes especificos activos, se usa la configuracion global/maximos actuales.
- Los maximos actuales siguen vigentes como compatibilidad durante migracion y como fallback.

Orden de resolucion para tarjeta/MP/debito:

1. Regla por tarjeta especifica activa, si aplica.
2. Regla general de tarjeta activa.
3. Condicion general del medio por producto.
4. Configuracion global.

Orden de resolucion para credito personal:

1. Planes activos de `ProductoCondicionPago` para `CreditoPersonal`.
2. `MaxCuotasCredito` actual por producto.
3. Perfil/configuracion global de credito personal.

Carrito con un solo producto:

- Mostrar todos los planes activos del producto aplicable.
- Si no hay planes activos especificos, construir opciones desde el fallback global.

Carrito mixto:

- No aplicar un unico porcentaje global a toda la venta.
- Para habilitar una cuota del carrito completo, la cantidad de cuotas debe existir como plan activo compatible para todos los productos que tienen configuracion especifica activa, o estar permitida por fallback para los que no tienen configuracion especifica.
- Si una misma cantidad de cuotas tiene porcentajes distintos entre productos, el resolver debe devolver grupos por `ProductoId/CantidadCuotas/AjustePorcentaje/Fuente`.
- La UI puede mostrar "plan mixto" y el futuro calculo debera prorratear ajuste por linea/producto, no por total general.

## E. Impacto por medio de pago

Tarjeta Credito:

- Usa planes por tarjeta especifica cuando exista `ConfiguracionTarjetaId`.
- Si el plan define `AjustePorcentaje`, ese ajuste futuro reemplaza al recargo/descuento informativo de la condicion para esa cuota.
- El interes mensual actual de `ConfiguracionTarjeta` debe mantenerse hasta la fase de calculo real. En integracion futura hay que decidir si convive o si el plan porcentual lo reemplaza.

Tarjeta Debito:

- Se configura igual que tarjeta credito para este analisis, aunque normalmente tendra 1 cuota.
- El recargo debito global actual de `ConfiguracionTarjeta.PorcentajeRecargoDebito` sigue vigente hasta que la fase de calculo real defina precedencia.

Mercado Pago:

- Se registra como `TipoPago.MercadoPago`, sin conexion externa.
- Debe usar la misma semantica de planes que tarjeta: cuotas activas visibles, ajuste por plan y fallback global.
- Si se modela Mercado Pago con `ConfiguracionTarjeta`, conviene documentar que es un proveedor/medio interno, no una integracion.

## F. Impacto en venta y productos mixtos

Venta necesita pasar de "limitar select hasta N" a "poblar select desde planes disponibles".

En fase futura, el preview debe recibir:

- tipo de pago;
- tarjeta/proveedor seleccionado;
- cuota/plan seleccionado;
- detalles del carrito con producto, cantidad, precio y descuento.

Para productos mixtos:

- Si todos los productos comparten misma cuota y mismo ajuste, se puede mostrar un resumen simple.
- Si comparten cuota pero tienen ajustes distintos, se debe calcular por linea y mostrar total ajustado por grupos.
- Si una cuota esta activa para un producto e inactiva/no disponible para otro, esa cuota no debe ser seleccionable para la venta completa salvo que la UI permita pagos divididos por producto, que hoy no existe.
- Los snapshots futuros deben guardar por venta/detalle el plan aplicado para auditoria.

## G. Impacto en credito personal

Recomendacion funcional: credito personal debe mantener el interes mensual existente como motor financiero principal en las fases inmediatas.

El nuevo `AjustePorcentaje` para credito personal debe tratarse inicialmente como ajuste comercial por plan, no como reemplazo directo de la tasa mensual. Antes de implementarlo en calculo real, negocio debe definir si:

- el ajuste porcentual se suma/resta al capital antes de calcular cuotas;
- reemplaza la tasa mensual;
- modifica la tasa mensual;
- o solo opera como descuento/recargo comercial aparte.

Hasta esa decision, credito personal puede usar planes activos solo para visibilidad/seleccion de cantidad de cuotas y mantener `TasaInteresMensual`/perfil actual.

## H. Impacto en caja/reportes/comprobantes

Caja:

- No requiere cambios en esta fase.
- En integracion futura, caja debe recibir el total ya persistido por venta/factura, no recalcular ajustes.
- Credito personal y cuenta corriente hoy no registran movimiento de caja directo; eso debe preservarse.

Reportes:

- Reportes de ventas/margenes/comisiones deben leer importes persistidos y, si se agregan ajustes, distinguir precio base, ajuste de plan y total.
- Reportes no deben inferir recargos desde configuracion vigente porque la configuracion puede cambiar despues de la venta.

Comprobantes/facturas:

- Deben usar snapshots persistidos.
- Si el ajuste se aplica en total de venta, el comprobante debe poder mostrarlo como linea/resumen o incorporado por detalle, segun decision fiscal/negocio.
- No conviene calcular ajustes en `FacturaComprobanteBuilder`; debe recibir datos ya resueltos.

## I. Propuesta UI/UX

Modal de condiciones de pago:

- Mantener selector de medios actual.
- En medios tipo tarjeta y Mercado Pago, reemplazar los campos de maximos por una grilla de planes:
  - Cuotas.
  - Activa.
  - Ajuste %.
  - Observaciones.
- En reglas por tarjeta, cada tarjeta debe poder tener su propia grilla de planes.
- En credito personal, mostrar grilla de cuotas credito con la misma semantica.
- Acciones minimas: agregar cuota, activar/desactivar, editar porcentaje, eliminar/limpiar fila.
- Mostrar fallback: "Sin planes activos especificos: usa configuracion global".
- No mostrar planes inactivos al vendedor en venta; en el modal administrativo si deben verse para poder reactivarlos.

Venta:

- El select de cuotas debe alimentarse de `PlanesDisponibles`.
- Para cuotas mixtas con porcentajes distintos, mostrar etiqueta compacta: "6 cuotas - ajustes por producto".
- No aplicar totales en UI hasta fase de calculo real; primero solo caracterizacion.

## J. Migracion necesaria

No crear migracion en 15.1.

Migracion futura:

- Crear tabla `ProductoCondicionPagoPlanes`.
- Agregar FK a `ProductoCondicionesPago`.
- Agregar FK nullable a `ProductoCondicionesPagoTarjeta`.
- Agregar constraints de cuota y porcentaje.
- Agregar indices por condicion, tarjeta y cuota.
- Agregar navegaciones en entidades existentes.
- Actualizar snapshot EF.

No se recomienda backfill automatico masivo al crear la tabla. Los maximos actuales deben seguir siendo fallback. Si negocio quiere precargar planes desde maximos, hacerlo como script operativo separado y reversible, no como comportamiento escondido de migracion.

## K. Tests necesarios

Antes de implementar:

- Tests de caracterizacion del resolver actual: maximos globales, restricciones por producto, tarjeta especifica, credito personal y recargos informativos sin modificar total.
- Tests de contrato UI actual del modal, para asegurar que Fase 15.5 no rompa carga/guardado.
- Tests de venta actual: diagnostico no modifica totales, select de cuotas se limita por maximo, backend rechaza cuota excedida.

Durante implementacion:

- Validacion de entidad/servicio: cuota menor a 1 falla; porcentaje negativo/cero/positivo se acepta; duplicado activo falla.
- Resolver: plan activo aparece; plan inactivo no aparece; sin plan activo usa global; tarjeta especifica pisa tarjeta general; tarjeta general pisa condicion.
- Carrito mixto: interseccion de cuotas; porcentajes distintos devuelven grupos; cuota ausente en un producto no queda disponible.
- Credito personal: planes restringen opciones sin reemplazar tasa mensual.
- Venta futura: snapshots de plan aplicado y calculo por linea cuando haya ajustes reales.

## L. Riesgos/deuda tecnica

- La coexistencia entre `PorcentajeRecargo`/`PorcentajeDescuentoMaximo` y `AjustePorcentaje` puede generar doble interpretacion. Debe definirse precedencia: plan especifico gana sobre ajuste informativo general.
- `TipoPago.Tarjeta` legacy sigue siendo ambiguo y debe mantenerse normalizado.
- Mercado Pago puede quedar mal modelado si se mezcla como tarjeta fisica. Conviene tratarlo como medio tipo tarjeta, no como tarjeta bancaria.
- Productos mixtos requieren calculo por linea; aplicar un porcentaje al total seria incorrecto.
- Credito personal tiene dos conceptos distintos: tasa financiera mensual y ajuste comercial por plan.

## M. Fases recomendadas

- Fase 15.2: tests de caracterizacion y contratos actuales, sin cambios productivos.
- Fase 15.3: entidades, DbSet, configuracion EF y migracion, sin usar todavia en venta.
- Fase 15.4: DTOs, service y resolver con planes disponibles, manteniendo fallback escalar.
- Fase 15.5: UI del modal para administrar planes.
- Fase 15.6: integracion en venta/backend solo para seleccion/validacion de planes.
- Fase 15.7: calculo real de ajustes por plan, con snapshots.
- Fase 15.8: caja/reportes/comprobantes sobre importes persistidos.
- Staging/release final: QA funcional, rollback y documentacion operativa.

## N. Preguntas pendientes

- En tarjeta credito, el ajuste por plan reemplaza o convive con `TasaInteresesMensual`?
- En debito, el recargo debito global actual debe convertirse en plan de 1 cuota o seguir separado?
- En Mercado Pago, se quiere configurar por proveedor unico o por "tarjetas" internas de Mercado Pago?
- En credito personal, el ajuste porcentual afecta capital, tasa mensual o total final?
- Se permitiran pagos divididos por producto en el futuro? Si no, el carrito debe usar interseccion estricta.
- El descuento maximo general sigue existiendo cuando un plan tiene ajuste negativo?

## O. Checklist actualizado

- Fase 8 - Condiciones de pago por producto: cerrada.
- Fase 9 - Credito personal por producto: cerrada.
- Fase 10 - Refactor ProductoController: cerrada.
- Fase 11 - Limpieza QA/release readiness: cerrada.
- Fase 12 - Refactor CreditoController: cerrada.
- Fase 13 - Validacion tecnica local: cerrada.
- Fase 14 - UX/UI modal condiciones de pago: cerrada.
- Fase 15.1 - Diseno cuotas por plan: cerrada con este documento.
- Fase 15.2 - Tests de caracterizacion/preparacion: proxima.
- Fase 15.3 - Entidades/migracion: pendiente.
- Fase 15.4 - Service/resolver: pendiente.
- Fase 15.5 - UI modal cuotas por plan: pendiente.
- Fase 15.6 - Integracion venta/backend: pendiente.
- Staging/release final: pendiente.

## P. Prompt recomendado para proxima fase

```text
Agente Kira - Fase 15.2

Objetivo: agregar tests de caracterizacion/preparacion para cuotas por plan sin modificar DB, sin migraciones, sin UI y sin logica productiva.

Restricciones:
- No crear entidades nuevas todavia.
- No crear migraciones.
- No ejecutar database update.
- No modificar calculos de venta.
- No tocar CajaService, reportes ni comprobantes salvo lectura si hace falta.

Tareas:
- Cubrir comportamiento actual de ProductoCondicionPagoRules con maximos globales y por producto.
- Cubrir tarjeta especifica/general y fallback global.
- Cubrir credito personal con MaxCuotasCredito actual.
- Cubrir que ajustes informativos no modifican TotalReferencia/TotalSinAplicarAjustes.
- Cubrir Venta/Create actual: diagnostico no cambia totales y solo limita cuotas.
- Dejar documentado que estos tests son baseline antes de entidad ProductoCondicionPagoPlan.

Validacion:
- Ejecutar dotnet test con filtros relacionados.
- Ejecutar dotnet build.
- Ejecutar git status --short al final.
```
