# Fase 14.2 - Modal de condiciones de pago por producto

## Alcance

La fase 14.2 modifica solamente la experiencia de configuracion del modal. No cambia endpoints, DTOs, persistencia, calculos de venta, caja, credito, reportes ni comprobantes.

## Campos por medio

- Efectivo, Transferencia, Cheque y Cuenta Corriente se tratan como medios directos: muestran Activa, Disponibilidad, Recargo %, Descuento max. % y Observaciones. No muestran cuotas.
- Tarjeta Debito, Tarjeta Credito y Mercado Pago se tratan como medios tipo tarjeta: muestran Activa, Disponibilidad, Cuotas sin interes, Cuotas con interes, Recargo %, Descuento max. %, Observaciones y reglas por tarjeta si existen.
- Credito Personal se configura separado de tarjeta: muestra Activa, Disponibilidad, Cuotas credito y Observaciones.

## Compatibilidad

Heredar sigue representado por null. Permitido sigue siendo true. Bloqueado sigue siendo false. Activa conserva la semantica actual: la regla participa en el resolver.

Los recargos y descuentos siguen siendo informativos en esta fase y no modifican totales.

## Deuda futura

Cuando se implemente cuotas por plan, cada cuota podra tener Activa y Ajuste %. La regla funcional definida es: cuota activa se muestra y se puede seleccionar; cuota inactiva no se muestra al vendedor. Si no hay configuracion especifica activa, se usa la configuracion global. El ajuste futuro debera aceptar negativos, cero y positivos, pero esta fase conserva las validaciones actuales de 0 a 100.
