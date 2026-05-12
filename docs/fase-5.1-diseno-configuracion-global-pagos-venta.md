# Fase 5.1 - ADR diseno configuracion global de pagos por venta

## A. Contexto

El bloque anterior dejo consolidada la decision funcional de venta:

**1 venta = 1 tipo de pago principal.**

Nueva Venta volvio a usar `Venta.TipoPago` como fuente principal. El pago por producto quedo aislado como compatibilidad legacy y no forma parte del flujo activo. Caja y reportes deben interpretar el medio de pago desde la venta, no desde cada detalle.

La proxima etapa no debe reactivar pago por producto. El objetivo es definir una configuracion global de medios, tarjetas y planes aplicable a la venta completa.

Componentes actuales relevantes:

- `ConfiguracionPago`: base actual de configuracion por `TipoPago`.
- `ConfiguracionTarjeta`: base actual de tarjetas.
- `ConfiguracionPagoService`: servicio actual de configuracion, tarjetas y defaults de credito personal.
- `VentaService`: autoridad final de persistencia y reglas de venta.
- `VentaApiController`: preview y endpoints auxiliares para Nueva Venta.
- `CajaService`: registra movimientos desde `Venta.TipoPago` y total final.
- `ReporteService`: filtra y agrupa ventas por `Venta.TipoPago`.

## B. Decision funcional principal

La configuracion global de pagos debe operar a nivel venta.

La venta elige un unico `TipoPago` general. La configuracion global define si ese medio esta activo, que datos requiere, que tarjetas puede usar, que planes/cuotas estan disponibles y que recargo o descuento corresponde.

El backend es autoridad final para validar medio, tarjeta, plan y calculo del ajuste. El frontend puede previsualizar, pero no decide el resultado financiero final.

## C. Alcance

Este ADR cubre:

- medios de pago globales;
- tarjetas asociadas;
- planes/cuotas globales;
- recargos y descuentos aplicados a la venta completa;
- snapshot recomendado para auditoria;
- impacto esperado en venta, caja, comprobantes, reportes y comision;
- entidades reutilizables y entidades nuevas sugeridas;
- fases de implementacion y tests requeridos.

## D. Fuera de alcance

Queda fuera de este ADR:

- implementar codigo;
- crear migraciones;
- modificar entidades;
- tocar UI;
- modificar Seguridad;
- reactivar pago por producto;
- pago mixto;
- redisenar Nueva Venta;
- eliminar entidades, columnas o migraciones legacy;
- modificar credito personal salvo definicion de limites de integracion.

## E. Modelo funcional propuesto

### Medio de pago global

Cada medio representa una opcion elegible para la venta completa.

Campos funcionales:

- `TipoPago`;
- nombre visible;
- activo/inactivo;
- orden;
- observaciones;
- requiere tarjeta;
- requiere cuotas;
- requiere datos adicionales;
- impacta caja;
- permite descuento;
- permite recargo.

Medios previstos:

- Efectivo;
- Transferencia;
- Tarjeta debito;
- Tarjeta credito;
- Mercado Pago / medio digital;
- Credito personal;
- Cheque;
- Cuenta corriente, si existe funcionalmente como medio operativo.

Regla:

Si un medio esta inactivo, no aparece en Nueva Venta y el backend debe rechazarlo si llega en una solicitud de venta nueva.

### Tarjetas

`ConfiguracionTarjeta` sigue siendo la base para tarjetas.

Reglas:

- una tarjeta puede estar activa o inactiva;
- tarjeta inactiva no aparece en Nueva Venta;
- backend debe rechazar tarjeta inactiva;
- tarjeta credito puede tener planes/cuotas;
- tarjeta debito normalmente no tiene cuotas;
- tarjeta debito solo deberia permitir cuotas si existe una decision explicita futura.

### Planes/cuotas globales

Se recomienda crear una entidad futura `ConfiguracionPagoPlan`.

Campos sugeridos:

- `ConfiguracionPagoId`;
- `ConfiguracionTarjetaId` nullable;
- `CantidadCuotas`;
- `Activo`;
- `TipoAjuste`;
- `AjustePorcentaje`;
- `Etiqueta`;
- `Orden`;
- `Observaciones`.

Reglas:

- plan inactivo no aparece en Nueva Venta;
- backend debe rechazar plan inactivo;
- el ajuste se aplica una sola vez sobre el total de la venta;
- no representa interes mensual;
- no se calcula por producto;
- no depende del carrito ni de intersecciones entre productos.

## F. Modelo tecnico propuesto

### Reutilizar

`ConfiguracionPago` debe seguir siendo la entidad base del medio global. Ya contiene:

- `TipoPago`;
- `Nombre`;
- `Descripcion`;
- `Activo`;
- `PermiteDescuento`;
- `PorcentajeDescuentoMaximo`;
- `TieneRecargo`;
- `PorcentajeRecargo`;
- defaults de credito personal.

`ConfiguracionTarjeta` debe seguir siendo la entidad base de tarjetas. Ya contiene:

- `ConfiguracionPagoId`;
- `NombreTarjeta`;
- `TipoTarjeta`;
- `Activa`;
- `PermiteCuotas`;
- `CantidadMaximaCuotas`;
- `TipoCuota`;
- `TasaInteresesMensual`;
- recargo debito;
- observaciones.

### Extender luego, con migracion aprobada

Campos candidatos para `ConfiguracionPago`:

- `Orden`;
- `RequiereTarjeta`;
- `RequiereCuotas`;
- `RequiereDatosAdicionales`;
- `ImpactaCaja`;
- `NombreVisibleSnapshotDefault`, solo si se decide snapshot configurable.

Entidad nueva candidata:

```csharp
public class ConfiguracionPagoPlan : AuditableEntity
{
    public int ConfiguracionPagoId { get; set; }
    public int? ConfiguracionTarjetaId { get; set; }
    public int CantidadCuotas { get; set; }
    public bool Activo { get; set; } = true;
    public TipoAjustePagoPlan TipoAjuste { get; set; }
    public decimal AjustePorcentaje { get; set; }
    public string? Etiqueta { get; set; }
    public int Orden { get; set; }
    public string? Observaciones { get; set; }
}
```

Restricciones sugeridas:

- `CantidadCuotas >= 1`;
- `AjustePorcentaje >= -100`;
- precision decimal explicita;
- indice por `ConfiguracionPagoId`, `ConfiguracionTarjetaId`, `CantidadCuotas` y no eliminado logicamente;
- `ConfiguracionTarjetaId = null` representa plan general del medio.

### Services

Responsabilidades recomendadas:

- `ConfiguracionPagoService`: CRUD y queries de configuracion.
- Nuevo service/reglas puro: calculo y validacion de medio/tarjeta/plan global para venta.
- `VentaService`: aplicacion final y persistencia.
- `VentaApiController`: preview y lectura de opciones para Nueva Venta.

No conviene expandir `ProductoCondicionPagoRules` ni `CondicionesPagoCarritoResolver` para este flujo.

## G. Reglas de negocio

### Medio inactivo

- No aparece en Nueva Venta.
- Backend rechaza ventas nuevas con ese medio.
- Ventas historicas deben poder visualizarse.

### Tarjeta inactiva

- No aparece como opcion.
- Backend rechaza su uso en ventas nuevas.
- Ventas historicas deben conservar visualizacion mediante snapshot o datos persistidos.

### Plan inactivo

- No aparece.
- Backend rechaza su uso en ventas nuevas.
- Ventas historicas deben conservar datos del plan aplicado.

### Recargos y descuentos

- Efectivo y transferencia pueden tener descuento automatico opcional.
- Cualquier ajuste global modifica `Venta.Total`.
- El ajuste global se calcula una sola vez sobre el total de la venta.
- El ajuste no es interes mensual.
- Frontend solo previsualiza.
- Backend calcula, valida y persiste.

### Cuotas

- El valor de cuota se muestra como `total final / cantidad de cuotas`.
- Para tarjeta credito, las cuotas provienen de planes globales.
- Para tarjeta debito, no hay cuotas salvo decision explicita posterior.
- Mercado Pago puede operar como medio digital simple o con planes, pendiente de decision funcional.

### Credito personal

- Mantiene flujo propio.
- La configuracion global solo habilita/deshabilita el medio y puede conservar defaults globales.
- No se mezclan planes globales de tarjeta con credito personal.

### Cheque

- Puede ser habilitado/deshabilitado desde configuracion global.
- Sus datos adicionales deben seguir tratandose en el flujo propio.

### Cuenta corriente

- Debe confirmarse si existe como medio operativo activo.
- Si no impacta caja inmediata, debe quedar explicitado igual que credito personal.

## H. Impacto en Venta

Nueva Venta debe:

- listar solo medios activos;
- enviar `TipoPago` general;
- cargar tarjetas activas si el medio las requiere;
- cargar planes activos si el medio/tarjeta los requiere;
- previsualizar ajuste global;
- no enviar pago por detalle;
- no enviar `ProductoCondicionPagoPlanId` por detalle.

Backend debe:

- validar medio activo;
- validar tarjeta activa cuando aplique;
- validar plan activo cuando aplique;
- calcular ajuste global;
- persistir total final;
- persistir snapshot de pago aplicado;
- conservar compatibilidad con ventas historicas.

## I. Impacto en Caja

Caja debe:

- usar `Venta.TipoPago`;
- registrar el total final persistido;
- no depender de `VentaDetalle.TipoPago`;
- no depender de `ProductoCondicionPagoPlanId`;
- distinguir medios sin impacto inmediato, como credito personal y cuenta corriente si aplica.

La configuracion global no debe recalcular caja sobre configuraciones actuales si la venta ya fue confirmada.

## J. Impacto en comprobantes/reportes

Comprobantes y detalles de venta deben:

- mostrar medio general;
- mostrar tarjeta si aplica;
- mostrar plan/cuotas si aplica;
- usar snapshot o datos persistidos de la venta;
- no presentar pago por producto como flujo activo.

Reportes deben:

- filtrar y agrupar por `Venta.TipoPago`;
- usar total final persistido;
- no interpretar `VentaDetalle.TipoPago` como fuente principal;
- no depender de configuracion actual para reinterpretar ventas historicas.

## K. Impacto en comision

Decision pendiente:

La regla funcional deseada es comision por producto sobre precio final imputado a ese producto.

Si un ajuste global de pago modifica `Venta.Total`, debe decidirse si ese ajuste se prorratea a las lineas y actualiza la base de comision.

No se debe implementar una regla silenciosa. La fase correspondiente debe incluir tests de:

- venta con recargo global;
- venta con descuento global;
- multiples productos con distintas comisiones;
- redondeo de prorrateo;
- base final de comision.

## L. Entidades actuales reutilizables

### `ConfiguracionPago`

Clasificacion: canonic/parcial.

Uso recomendado:

- medio global por `TipoPago`;
- estado activo/inactivo;
- nombre y descripcion;
- recargo/descuento base;
- defaults de credito personal.

### `ConfiguracionTarjeta`

Clasificacion: canonic/parcial.

Uso recomendado:

- catalogo de tarjetas;
- estado activo/inactivo;
- tipo credito/debito;
- relacion con medio;
- soporte transitorio para cantidad maxima de cuotas.

### `DatosTarjeta`

Clasificacion: canonic para snapshot de datos de tarjeta de venta.

Uso recomendado:

- persistir datos aplicados en la venta;
- extender luego solo si se aprueba snapshot de plan global.

### `ProductoCondicionPago*`

Clasificacion: legacy para venta activa.

Uso recomendado:

- mantener compatibilidad;
- no usar como modelo principal;
- no reactivar en Nueva Venta.

## M. Entidades nuevas sugeridas

### `ConfiguracionPagoPlan`

Entidad recomendada para modelar planes globales por medio y opcionalmente por tarjeta.

Motivo:

- `ConfiguracionTarjeta.CantidadMaximaCuotas` solo expresa un maximo;
- no permite planes individuales activos/inactivos;
- no permite etiqueta por plan;
- no modela ajuste por plan de forma auditable.

### Snapshot de plan aplicado

Puede resolverse extendiendo `DatosTarjeta` o creando una entidad/snapshot asociado a `Venta`, segun alcance aprobado.

Campos recomendados:

- `TipoPago`;
- `ConfiguracionTarjetaId`;
- cantidad de cuotas;
- porcentaje aplicado;
- monto aplicado;
- nombre medio snapshot;
- nombre tarjeta snapshot;
- nombre plan snapshot.

## N. Riesgos

### Alto

- Modificar totales sin cerrar prorrateo para comision.
- Crear migraciones antes de aprobar entidad de planes.
- Mezclar credito personal con planes de tarjeta.
- Recalcular ventas historicas usando configuracion actual.

### Medio

- `ConfiguracionPagoService` ya concentra demasiadas responsabilidades.
- Persisten restos de restricciones por producto en calculo de cuotas.
- Vistas administrativas de `ConfiguracionPagoController` deben verificarse antes de construir UI nueva.

### Bajo

- Filtrar medios/tarjetas/planes inactivos.
- Agregar tests puros de calculo.
- Usar UI legacy solo como inspiracion visual.

## O. Decisiones abiertas

- Confirmar si efectivo y transferencia tendran descuento automatico.
- Confirmar si Mercado Pago tendra planes o solo un ajuste simple.
- Confirmar si cuenta corriente esta activa como medio de venta.
- Confirmar si `TipoPago.Tarjeta` generico debe ocultarse como legacy.
- Confirmar si el nombre visible del medio debe snapshotearse siempre.
- Confirmar donde persistir snapshot del plan aplicado.
- Confirmar prorrateo de ajuste global a lineas para comision.
- Confirmar redondeo financiero de ajuste y cuota.
- Confirmar si tarjeta debito puede tener cuotas en algun escenario.

## P. Plan de implementacion por fases

### Fase 5.2 - Reglas puras y tests de ajuste global

Objetivo:

- definir calculo de ajuste global sin tocar base de datos;
- cubrir recargo, descuento, sin ajuste y redondeo.

No debe tocar:

- migraciones;
- UI;
- pago por producto legacy.

### Fase 5.3 - Modelo de planes globales

Objetivo:

- aprobar entidad `ConfiguracionPagoPlan`;
- crear migracion minima;
- agregar constraints e indices.

No debe tocar:

- Nueva Venta;
- Caja;
- reportes.

### Fase 5.4 - Servicio de configuracion global

Objetivo:

- exponer medios, tarjetas y planes activos;
- validar inactivos;
- preparar DTOs para venta.

No debe tocar:

- UI final;
- comprobantes;
- reportes.

### Fase 5.5 - UI administrativa

Objetivo:

- pantalla de configuracion global de medios, tarjetas y planes;
- usar el concepto visual del legacy solo como inspiracion.

No debe tocar:

- pago por producto;
- Seguridad.

### Fase 5.6 - Integracion con Nueva Venta

Objetivo:

- cargar medios activos;
- cargar tarjetas/planes activos;
- preview de ajuste global.

No debe tocar:

- pago mixto;
- calculos por producto.

### Fase 5.7 - Backend final de venta

Objetivo:

- validar configuracion activa;
- aplicar ajuste global;
- persistir snapshot.

### Fase 5.8 - Caja, comprobantes, reportes y comision

Objetivo:

- validar total final en caja;
- mostrar snapshot en comprobantes;
- reportar por `Venta.TipoPago`;
- cerrar regla de comision/prorrateo.

### Fase 5.9 - Limpieza legacy posterior

Objetivo:

- inventariar pago por producto remanente;
- no borrar entidades ni migraciones sin decision explicita.

## Q. Tests requeridos

Tests minimos:

- medio inactivo no aparece en Nueva Venta;
- backend rechaza medio inactivo;
- tarjeta inactiva no aparece;
- backend rechaza tarjeta inactiva;
- plan inactivo no aparece;
- backend rechaza plan inactivo;
- recargo global se aplica una sola vez;
- descuento global se aplica una sola vez;
- tarjeta credito muestra planes validos;
- tarjeta debito no muestra cuotas por defecto;
- Mercado Pago respeta configuracion definida;
- Caja registra `Venta.TipoPago` y total final;
- reportes agrupan por `Venta.TipoPago`;
- comprobante muestra medio general y plan si aplica;
- comision usa base definida cuando se cierre prorrateo.

Tests legacy a conservar separados:

- pago por producto;
- `ProductoCondicionPagoRules`;
- modal legacy de condiciones por producto.

## R. Checklist

[x] Decision principal documentada: 1 venta = 1 tipo de pago principal
[x] Medios globales definidos
[x] Tarjetas definidas
[x] Planes globales sugeridos
[x] Recargo/descuento global definido
[x] Snapshot recomendado
[x] Impacto en venta documentado
[x] Impacto en caja documentado
[x] Impacto en comprobantes/reportes documentado
[x] Impacto en comision documentado como decision pendiente
[x] Credito personal delimitado
[x] Pago mixto fuera de alcance
[x] Pago por producto legacy no reactivado
[x] Entidades actuales reutilizables listadas
[x] Entidad nueva sugerida listada
[x] Riesgos listados
[x] Decisiones abiertas listadas
[x] Fases propuestas
[x] Tests requeridos listados
