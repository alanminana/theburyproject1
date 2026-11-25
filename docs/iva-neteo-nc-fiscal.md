# Neteo informativo del IVA de devoluciones por NC fiscal externa (2026-11-25)

**Criterio interno aprobado por el propietario. No es una certificación fiscal del contador.** La facturación fiscal sigue siendo externa; el IVA del ERP es informativo.

## Qué hace el Reporte de IVA

* Resta el IVA de una devolución **solo si** existe una nota de crédito fiscal externa **válida vinculada** (pantalla «Detalle de la devolución», permiso `notascredito.linkfiscal`).
* La imputa al **período de la fecha de la NC fiscal** (`NotaCredito.FiscalExternaFecha`), nunca al de la venta original ni al de la devolución.
* **No modifica** importes, IVA ni período de la venta original: el «IVA de ventas» del reporte queda igual; el IVA de las NC fiscales se informa aparte («Notas de crédito fiscales por alícuota») y se resta en la «Diferencia de IVA».
* Sin NC fiscal vinculada no se resta nada, aunque la devolución esté aprobada y tenga NC interna.

## Cómo se calcula

1. Importe = `FiscalExternaImporte` (lo que acredita el comprobante fiscal, con IVA).
2. Se reparte entre las alícuotas de **lo efectivamente devuelto**: cada ítem devuelto pesa su subtotal, repartido entre las líneas de ese producto en la venta, con la **alícuota histórica de la línea** (nunca la actual del producto). Sin detalle utilizable, se reparte como las líneas de la venta.
3. En cada alícuota: `neto = total / (1 + alícuota)` redondeado a centavos y `IVA = total − neto` (el redondeo se absorbe en el IVA, igual que en la venta). La suma de las partes es **exactamente** el importe de la NC; alícuota 0 % no genera IVA.

## Válida significa

Vínculo completo (punto de venta, tipo, número, fecha, importe), importe > 0 y no superior a la NC interna, NC no cancelada, devolución aprobada o completada, venta facturada o entregada. Una NC cuenta **una sola vez** (una por devolución y el comprobante fiscal es único).

## Casos cubiertos por pruebas

Alícuotas mixtas (21 % + 10,5 %: 227,70 de IVA sobre 1.545,00), devolución parcial, NC fiscal por menos que la NC interna, varias devoluciones de la misma venta en meses distintos, cambio de mes (la venta queda en su mes y el mes de la NC queda con IVA neto negativo), vínculo inválido o NC cancelada, filtro por alícuota, redondeos en centavos y venta sin desglose histórico (se informa y no se resta).

## Límites

* Es informativo: no emite ni sustituye ningún comprobante.
* La condición de crédito fiscal de las compras (IVA recuperable) **no** forma parte de este cambio.
* Reportes de ventas y Dashboard siguen restando la devolución por su fecha de efecto (aprobación con V2); este cambio solo afecta al Reporte de IVA.
