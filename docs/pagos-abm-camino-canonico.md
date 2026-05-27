# PAGOS-ABM-1A - Camino canonico para medios, tarjetas y planes

Fecha: 2026-05-26

Alcance: diagnostico tecnico y contrato conceptual. No modifica venta, entidades, migraciones ni UI.

## Decision canonica

El camino canonico para configurar cobro global debe ser:

1. `ConfiguracionPago`: medio de pago global.
2. `ConfiguracionTarjeta`: marca/tarjeta asociada a un medio de pago.
3. `ConfiguracionPagoPlan`: plan global por medio y opcionalmente por tarjeta.

`ProductoCondicionPago*` queda fuera como fuente principal de cobro. Debe usarse solo como restriccion por producto si corresponde. `DatosTarjeta` queda fuera del ABM porque es snapshot historico de una venta.

## Evidencia de codigo

### Medios de pago

`ConfiguracionPago` tiene `TipoPago`, `Nombre`, `Descripcion`, `Activo`, flags de descuento/recargo global y defaults de credito personal. Tambien declara las relaciones `ConfiguracionesTarjeta` y `PlanesPago` (`Models/Entities/ConfiguracionPago.cs:13`, `Models/Entities/ConfiguracionPago.cs:58`).

En EF, `ConfiguracionesPago` tiene indice unico por `TipoPago` y relacion 1-N con tarjetas y planes (`Data/AppDbContext.cs:1467`, `Data/AppDbContext.cs:1469`, `Data/AppDbContext.cs:1474`). Esto confirma que una fila representa el medio global.

### Tarjetas

`ConfiguracionTarjeta` pertenece a `ConfiguracionPago`, tiene `NombreTarjeta`, `TipoTarjeta`, `Activa`, datos legacy de cuotas/tasa/recargo debito y `Observaciones` (`Models/Entities/ConfiguracionTarjeta.cs:12`, `Models/Entities/ConfiguracionTarjeta.cs:16`, `Models/Entities/ConfiguracionTarjeta.cs:19`, `Models/Entities/ConfiguracionTarjeta.cs:21`, `Models/Entities/ConfiguracionTarjeta.cs:24`, `Models/Entities/ConfiguracionTarjeta.cs:30`, `Models/Entities/ConfiguracionTarjeta.cs:33`).

En EF tiene tabla propia e indice por medio + nombre (`Data/AppDbContext.cs:1550`, `Data/AppDbContext.cs:1560`). Esto alcanza para Visa, Master, Amex, etc. asociadas a `TarjetaCredito` o `TarjetaDebito`.

### Planes/cuotas

`ConfiguracionPagoPlan` ya modela plan global por medio y tarjeta opcional: `ConfiguracionPagoId`, `ConfiguracionTarjetaId`, `TipoPago`, `CantidadCuotas`, `Activo`, `TipoAjuste`, `AjustePorcentaje`, `Etiqueta`, `Orden` y `Observaciones` (`Models/Entities/ConfiguracionPagoPlan.cs:8`, `Models/Entities/ConfiguracionPagoPlan.cs:14`, `Models/Entities/ConfiguracionPagoPlan.cs:16`, `Models/Entities/ConfiguracionPagoPlan.cs:18`, `Models/Entities/ConfiguracionPagoPlan.cs:20`, `Models/Entities/ConfiguracionPagoPlan.cs:22`, `Models/Entities/ConfiguracionPagoPlan.cs:24`, `Models/Entities/ConfiguracionPagoPlan.cs:26`, `Models/Entities/ConfiguracionPagoPlan.cs:28`, `Models/Entities/ConfiguracionPagoPlan.cs:31`, `Models/Entities/ConfiguracionPagoPlan.cs:33`).

En EF tiene checks de cuotas >= 1 y ajuste entre -100.0000 y 999.9999, indices de orden, y unicidad activa por medio/tipo/cuotas para plan general o por medio/tipo/tarjeta/cuotas para plan especifico (`Data/AppDbContext.cs:1490`, `Data/AppDbContext.cs:1507`, `Data/AppDbContext.cs:1509`, `Data/AppDbContext.cs:1514`). Esto soporta casos como Visa 1/2/3/8 cuotas, Master solo 9 cuotas, Amex configurable.

### Pantalla existente

Ya existe pantalla `ConfiguracionPago/MediosPago` que carga `MediosPago_tw` (`Controllers/ConfiguracionPagoController.cs:51`). La vista muestra tarjetas y planes y permite crear/editar/inactivar planes globales con alcance general o por tarjeta, cuotas, ajuste, orden, etiqueta y observaciones (`Views/ConfiguracionPago/MediosPago_tw.cshtml:206`, `Views/ConfiguracionPago/MediosPago_tw.cshtml:212`, `Views/ConfiguracionPago/MediosPago_tw.cshtml:226`, `Views/ConfiguracionPago/MediosPago_tw.cshtml:235`, `Views/ConfiguracionPago/MediosPago_tw.cshtml:239`, `Views/ConfiguracionPago/MediosPago_tw.cshtml:243`, `Views/ConfiguracionPago/MediosPago_tw.cshtml:253`, `Views/ConfiguracionPago/MediosPago_tw.cshtml:257`, `Views/ConfiguracionPago/MediosPago_tw.cshtml:297`, `Views/ConfiguracionPago/MediosPago_tw.cshtml:361`).

El servicio admin carga medios con tarjetas y planes, crea/actualiza planes y valida duplicados activos (`Services/ConfiguracionPagoService.cs:40`, `Services/ConfiguracionPagoService.cs:110`, `Services/ConfiguracionPagoService.cs:141`, `Services/ConfiguracionPagoService.cs:174`, `Services/ConfiguracionPagoService.cs:210`, `Services/ConfiguracionPagoService.cs:258`).

Tambien existe endpoint de consulta para venta `/api/ventas/configuracion-pagos-global`, que devuelve medios, tarjetas y planes generales/especificos (`Controllers/VentaApiController.cs:248`, `Controllers/VentaApiController.cs:324`, `Controllers/VentaApiController.cs:355`, `Controllers/VentaApiController.cs:361`, `Controllers/VentaApiController.cs:374`). No se debe modificar en esta fase.

## Campos actuales que alcanzan

Medio:

- `ConfiguracionPago.TipoPago`
- `Nombre`
- `Descripcion`
- `Activo`
- `PermiteDescuento`
- `PorcentajeDescuentoMaximo`
- `TieneRecargo`
- `PorcentajeRecargo`

Tarjeta:

- `ConfiguracionPagoId`
- `NombreTarjeta`
- `TipoTarjeta`
- `Activa`
- `Observaciones`

Plan:

- `ConfiguracionPagoId`
- `ConfiguracionTarjetaId` nullable
- `TipoPago`
- `CantidadCuotas`
- `Activo`
- `TipoAjuste`
- `AjustePorcentaje`
- `Etiqueta`
- `Orden`
- `Observaciones`

Interpretacion actual del ajuste: `AjustePorcentaje > 0` es recargo, `< 0` es descuento y `0` sin ajuste. La regla pura calcula una sola vez sobre el total y divide por cuotas (`Services/ConfiguracionPagoGlobalRules.cs:38`, `Services/ConfiguracionPagoGlobalRules.cs:47`).

## Campos que faltan o estan incompletos

No son necesarios para cerrar esta fase, pero deberian decidirse antes de un ABM completo:

- `TipoAjustePagoPlan` solo contiene `Porcentaje`; si el negocio necesita monto fijo, coeficiente financiero o tasa mensual, requiere migracion posterior (`Models/Enums/TipoAjustePagoPlan.cs`).
- No hay campo explicito `TipoOperacionAjuste` con valores `Recargo/Descuento/Ninguno`; hoy se infiere por signo de `AjustePorcentaje`. Puede mantenerse si se documenta bien, o agregarse por claridad de UX/API.
- `ConfiguracionTarjeta` no tiene unicidad filtrada por medio + nombre + no eliminado; hoy el indice no es unico. Si el ABM debe impedir marcas duplicadas activas por medio, hace falta migracion posterior.
- `ConfiguracionTarjeta` conserva campos legacy de maximo de cuotas/tasa mensual. Para el camino nuevo, las cuotas habilitadas deben salir de `ConfiguracionPagoPlan`, no de `CantidadMaximaCuotas`.
- No hay campo de icono/orden en `ConfiguracionPago`. Si la UI futura debe eliminar mapas de iconos hardcodeados, conviene agregar `Icono` y eventualmente `Orden`.
- No hay soft-delete/inactivar endpoint especifico para tarjetas en el admin global; la entidad tiene `Activa`.

## Migracion posterior

No hace falta migracion para documentar y usar el modelo actual en una primera fase de ABM de planes por cuota. Si se decide cualquiera de estos puntos, si hara falta migracion:

- agregar icono/orden a medios;
- agregar unicidad filtrada para tarjetas activas/no eliminadas por medio + nombre;
- agregar tipo semantico de ajuste distinto al signo del porcentaje;
- agregar tipos de ajuste distintos de porcentaje;
- separar contrato persistente para credito personal si se decide no representarlo como plan simple.

## ABM conceptual minimo

### Medio de pago

Medios esperados inicialmente:

- Efectivo
- Transferencia
- Tarjeta Debito
- Tarjeta Credito
- Credito Personal

Entidad: `ConfiguracionPago`.

Campos minimos de ABM:

- `Id`
- `TipoPago`
- `Nombre`
- `Descripcion`
- `Activo`
- `PermiteDescuento`
- `PorcentajeDescuentoMaximo`
- `TieneRecargo`
- `PorcentajeRecargo`

### Tarjeta

Entidad: `ConfiguracionTarjeta`.

Campos minimos de ABM:

- `Id`
- `ConfiguracionPagoId`
- `NombreTarjeta`
- `TipoTarjeta` (`Debito` o `Credito`)
- `Activa`
- `Observaciones`

Regla: las marcas como Visa, Master, Amex deben pertenecer al medio de pago correspondiente (`TarjetaCredito` o `TarjetaDebito`). No deben colgar de `TipoPago.Tarjeta`, que el servicio ya bloquea para nuevas configuraciones por ser historico/ambiguo (`Services/ConfiguracionPagoService.cs:198`, `Services/ConfiguracionPagoService.cs:206`).

### Plan

Entidad: `ConfiguracionPagoPlan`.

Campos minimos de ABM:

- `Id`
- `ConfiguracionPagoId`
- `ConfiguracionTarjetaId` opcional
- `CantidadCuotas`
- `TipoAjuste`
- `AjustePorcentaje`
- `Activo`
- `Orden`
- `Etiqueta`
- `Observaciones`

Reglas minimas:

- un plan general aplica al medio cuando `ConfiguracionTarjetaId` es null;
- un plan especifico aplica a una tarjeta/marca;
- solo puede existir un plan activo por medio + tarjeta opcional + cuotas;
- cuotas >= 1;
- ajuste entre -100.0000 y 999.9999;
- positivo = recargo, negativo = descuento, cero = sin ajuste.

## Credito personal

No debe mezclarse automaticamente con tarjetas.

Estado actual:

- `ConfiguracionPago` guarda defaults globales de credito personal: tasa mensual, gastos administrativos, min/max cuotas (`Models/Entities/ConfiguracionPago.cs:35`, `Models/Entities/ConfiguracionPago.cs:46`, `Models/Entities/ConfiguracionPago.cs:51`, `Models/Entities/ConfiguracionPago.cs:56`).
- Existe `PerfilCredito`, con tasa mensual, gastos administrativos, min/max cuotas, activo y orden (`Models/Entities/PerfilCredito.cs:27`, `Models/Entities/PerfilCredito.cs:36`, `Models/Entities/PerfilCredito.cs:43`, `Models/Entities/PerfilCredito.cs:50`, `Models/Entities/PerfilCredito.cs:57`, `Models/Entities/PerfilCredito.cs:62`).
- La vista `CreditoPersonal_tw` administra tasa mensual, gastos y rango de cuotas de defaults/perfiles (`Views/ConfiguracionPago/CreditoPersonal_tw.cshtml:82`, `Views/ConfiguracionPago/CreditoPersonal_tw.cshtml:88`, `Views/ConfiguracionPago/CreditoPersonal_tw.cshtml:94`, `Views/ConfiguracionPago/CreditoPersonal_tw.cshtml:100`, `Views/ConfiguracionPago/CreditoPersonal_tw.cshtml:135`, `Views/ConfiguracionPago/CreditoPersonal_tw.cshtml:139`).

Analisis:

- `ConfiguracionPagoPlan` podria representar una cantidad de cuotas con un ajuste porcentual sobre total, pero no representa por si solo tasa mensual, gastos administrativos, scoring, perfil, cupo, mora ni metodo financiero.
- En credito personal el ajuste puede significar interes financiero mensual, recargo comercial sobre total, gastos administrativos o una condicion crediticia. Esas semanticas ya existen parcialmente fuera de `ConfiguracionPagoPlan`.
- Meter credito personal ahora como planes simples arriesga confundir `AjustePorcentaje` (ajuste one-shot) con `TasaMensual` (calculo financiero), generando totales inconsistentes y deuda dificil de auditar.

Decision para esta fase: no implementar credito personal sobre `ConfiguracionPagoPlan`. Requiere fase separada para definir contrato: si se quiere "plan comercial de credito" debe convivir o integrarse explicitamente con `PerfilCredito` y `ConfiguracionCreditoVenta`.

## Hardcodes UI a eliminar luego

No corregir en PAGOS-ABM-1A. Registrar para fases posteriores:

- `wwwroot/js/venta-index.js` define nombres fijos de `TipoPago` por numero (`wwwroot/js/venta-index.js:18`).
- `wwwroot/js/venta-index.js` define iconos fijos por numero (`wwwroot/js/venta-index.js:30`).
- `wwwroot/js/venta-index.js` decide si es tarjeta con `cfg.tipoPago === 2 || cfg.tipoPago === 3 || cfg.tipoPago === 8` (`wwwroot/js/venta-index.js:147`).
- `wwwroot/js/venta-index.js` hardcodea labels de `TipoTarjeta` y `TipoCuotaTarjeta` con numeros 0/1 (`wwwroot/js/venta-index.js:160`, `wwwroot/js/venta-index.js:180`).
- `wwwroot/js/venta-index.js` usa limites visuales fijos, por ejemplo maximo 60 cuotas y 100% (`wwwroot/js/venta-index.js:169`, `wwwroot/js/venta-index.js:188`, `wwwroot/js/venta-index.js:263`, `wwwroot/js/venta-index.js:279`).
- `wwwroot/js/venta-create.js` duplica valores enteros del enum `TipoPago` (`wwwroot/js/venta-create.js:41`) y `TipoTarjeta` (`wwwroot/js/venta-create.js:59`).
- `wwwroot/js/venta-create.js` duplica labels de medios (`wwwroot/js/venta-create.js:65`).
- `wwwroot/js/venta-create.js` decide tarjetas/planes/credito con listas fijas (`wwwroot/js/venta-create.js:1344`, `wwwroot/js/venta-create.js:1348`, `wwwroot/js/venta-create.js:1354`).
- `wwwroot/js/venta-create.js` calcula fallback de `cantidadMaximaCuotas` desde planes con minimo 1 (`wwwroot/js/venta-create.js:1415`).
- `Views/Venta/Create_tw.cshtml` conserva fallback inicial "1 Pago" y texto/labels fijos para tarjeta y resumen (`Views/Venta/Create_tw.cshtml:482`, `Views/Venta/Create_tw.cshtml:500`, `Views/Venta/Create_tw.cshtml:504`, `Views/Venta/Create_tw.cshtml:508`).
- Hay switches Razor de labels/iconos de `TipoPago` en pantallas de venta/caja/reportes; no forman parte de esta fase pero deben migrarse a helper/metadata compartida si se elimina hardcode globalmente.

## Contratos propuestos para la fase siguiente

DTOs/ViewModels de lectura:

```csharp
public sealed class MedioPagoAdminDto
{
    public int Id { get; init; }
    public TipoPago TipoPago { get; init; }
    public string Nombre { get; init; } = string.Empty;
    public string? Descripcion { get; init; }
    public bool Activo { get; init; }
    public decimal? PorcentajeRecargo { get; init; }
    public decimal? PorcentajeDescuentoMaximo { get; init; }
    public IReadOnlyList<TarjetaAdminDto> Tarjetas { get; init; } = Array.Empty<TarjetaAdminDto>();
    public IReadOnlyList<PlanPagoAdminDto> Planes { get; init; } = Array.Empty<PlanPagoAdminDto>();
}

public sealed class TarjetaAdminDto
{
    public int Id { get; init; }
    public int ConfiguracionPagoId { get; init; }
    public string NombreTarjeta { get; init; } = string.Empty;
    public TipoTarjeta TipoTarjeta { get; init; }
    public bool Activa { get; init; }
    public string? Observaciones { get; init; }
}

public sealed class PlanPagoAdminDto
{
    public int Id { get; init; }
    public int ConfiguracionPagoId { get; init; }
    public int? ConfiguracionTarjetaId { get; init; }
    public int CantidadCuotas { get; init; }
    public TipoAjustePagoPlan TipoAjuste { get; init; }
    public decimal AjustePorcentaje { get; init; }
    public bool Activo { get; init; }
    public int Orden { get; init; }
    public string? Etiqueta { get; init; }
    public string? Observaciones { get; init; }
}
```

Comandos:

```csharp
public sealed class TarjetaCommand
{
    public int ConfiguracionPagoId { get; init; }
    public string NombreTarjeta { get; init; } = string.Empty;
    public TipoTarjeta TipoTarjeta { get; init; }
    public bool Activa { get; init; } = true;
    public string? Observaciones { get; init; }
}

public sealed class PlanPagoCommand
{
    public int ConfiguracionPagoId { get; init; }
    public int? ConfiguracionTarjetaId { get; init; }
    public int CantidadCuotas { get; init; }
    public TipoAjustePagoPlan TipoAjuste { get; init; } = TipoAjustePagoPlan.Porcentaje;
    public decimal AjustePorcentaje { get; init; }
    public bool Activo { get; init; } = true;
    public int Orden { get; init; }
    public string? Etiqueta { get; init; }
    public string? Observaciones { get; init; }
}
```

Validaciones de comando:

- tarjeta pertenece a un medio tarjeta debito/credito si se usa como marca de tarjeta;
- plan con tarjeta debe usar tarjeta del mismo `ConfiguracionPagoId`;
- duplicado activo por medio + tarjeta opcional + cuotas;
- no permitir `TipoPago.Tarjeta` para nuevas configuraciones;
- para credito personal, bloquear uso como plan hasta decidir contrato especifico.

## Endpoints futuros minimos

No implementar en esta fase. Propuesta:

- `GET /ConfiguracionPago/MediosPago`: vista admin existente.
- `GET /api/configuracion-pagos/medios`: listar medios con tarjetas y planes.
- `PATCH /api/configuracion-pagos/medios/{id}/estado`: activar/inactivar medio si se decide exponerlo.
- `GET /api/configuracion-pagos/medios/{medioId}/tarjetas`: listar tarjetas de medio.
- `POST /api/configuracion-pagos/medios/{medioId}/tarjetas`: crear tarjeta.
- `PUT /api/configuracion-pagos/tarjetas/{id}`: editar tarjeta.
- `PATCH /api/configuracion-pagos/tarjetas/{id}/estado`: activar/inactivar tarjeta.
- `GET /api/configuracion-pagos/planes?medioId={id}&tarjetaId={id?}`: listar planes.
- `POST /api/configuracion-pagos/planes`: crear plan.
- `PUT /api/configuracion-pagos/planes/{id}`: editar plan.
- `PATCH /api/configuracion-pagos/planes/{id}/estado`: activar/inactivar plan.
- `POST /api/configuracion-pagos/planes/validar-duplicado`: validar combinacion medio/tarjeta/cuotas antes de guardar.
- `PUT /api/configuracion-pagos/planes/orden`: reordenar planes si se mantiene orden manual.

Endpoints existentes a documentar/conservar:

- `POST ConfiguracionPago/CrearPlanGlobal`
- `POST ConfiguracionPago/EditarPlanGlobal/{id}`
- `POST ConfiguracionPago/CambiarEstadoPlanGlobal/{id}`
- `GET /api/ventas/configuracion-pagos-global`

## Siguiente micro-lote recomendado

PAGOS-ABM-1B: completar contrato admin de tarjetas sin tocar venta. Deberia agregar comandos/servicio/endpoints para crear/editar/inactivar `ConfiguracionTarjeta`, validar duplicados y dejar `MediosPago_tw` preparado para administrar marcas. Si se requiere metadata visual de medios, decidir antes si se agrega migracion de `Icono`/`Orden`.
