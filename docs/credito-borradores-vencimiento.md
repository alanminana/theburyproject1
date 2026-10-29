# Vencimiento de borradores de credito (H2-C2)

## Problema

Entrar al paso Credito de Venta/Create crea una venta en `PendienteFinanciacion` con su credito en
`PendienteConfiguracion` (`VentaService.CrearCreditoPendienteParaVentaAsync`). Ese credito esta en
`CreditoDisponibleService.EstadosVigentes`, asi que reserva cupo del cliente. Si el usuario abandona el formulario, el
borrador queda huerfano y el cupo del cliente se reduce sin que exista una operacion real.

## Solucion

Un job diario (`BorradorCreditoVencimientoBackgroundService`, mismo patron que
`AcuerdoIncumplimientoBackgroundService`: espera inicial, una ejecucion por fecha comercial, reintento horario si hubo
errores) llama a `BorradorCreditoVencimientoService.VencerBorradoresAsync`, que cancela cada borrador vencido con
`IVentaService.VencerBorradorCreditoAsync`.

Esa operacion **reutiliza la cancelacion de venta existente** (`CancelarVentaAsync`): mismo lock Credito -> Venta, mismas
guardias (devoluciones, anticipo cobrado, pagos aplicados), misma liberacion de cupo (credito `Cancelado`,
`SaldoPendiente = 0`, venta `Cancelada` con `FechaCancelacion` y `MotivoCancelacion`). Se ejecuta como usuario `Sistema`
y deja un log de auditoria por borrador y un resumen por corrida.

## Que es un borrador alcanzable

Todas las condiciones a la vez, verificadas **bajo lock** dentro de la propia transaccion de cancelacion:

- venta `PendienteFinanciacion`, `TipoPago = CreditoPersonal`, credito en `PendienteConfiguracion`;
- sin `FechaConfiguracionCredito`, confirmacion, facturacion, entrega ni apertura de caja;
- sin cuotas, facturas, movimientos de caja de la venta, anticipo cobrado (movimiento `AnticipoCredito` que referencia el
  credito), contrato de credito ni devoluciones (se consultan con `IgnoreQueryFilters`);
- ultima actividad (`UpdatedAt ?? CreatedAt`, de la venta y del credito) anterior o igual a `ahora - plazo`. El chequeo de
  la venta es parte del mismo `UPDATE` que toma el lock, de modo que una edicion concurrente lo descarta.

Nunca alcanza ventas `Presupuesto`, `Confirmada`, `Facturada`, `Entregada` ni creditos `Configurado`/`Generado`/`Activo`.
Es idempotente (un borrador ya cancelado se omite) y seguro con varias instancias (el lock serializa y se re-verifica).

## Configuracion

`ConfiguracionCredito.DiasVencimientoBorradorCredito` (columna nullable en `ConfiguracionesCredito`).

- Valor por defecto **provisorio: 7 dias** (`ConfiguracionCredito.DiasVencimientoBorradorCreditoPorDefecto`), pendiente de
  validacion del negocio. La migracion `BorradorCreditoVencimientoConfig` deja las filas existentes en 7.
- `null` o menor que 1 desactiva el vencimiento. Sin fila de configuracion rige el valor por defecto.
- Limite: un borrador vence cuando `ahora - actividad >= plazo` (el limite exacto vence).
- Todavia no hay pantalla para editarlo; se ajusta en la tabla. `UpdateConfiguracionAsync` no lo pisa.
