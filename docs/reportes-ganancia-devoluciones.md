# Reportes: devoluciones netas y ganancia sobre neto (Hito 10)

Fuente única del cálculo: `Services/VentasNetas.cs`. Usada por `ReporteService` y `DashboardService`, por lo que ambos coinciden.

## Fórmulas

- **Ventas brutas** = Σ `Venta.Total` de las ventas del período que cuentan como venta efectiva.
- **Devoluciones** = Σ `TotalDevolucion` (ya valuada por el importe realmente cobrado, ver `DevolucionService.ImporteCobradoDeLinea`) de devoluciones en estado Aprobada o Completada, por `FechaDevolucion` (no por la fecha de la venta). Pendientes y rechazadas no restan.
- **Ventas netas** = brutas − devoluciones.
- **Ganancia por venta** = `(Total − IVA − neto del recargo de débito) − costo`.
  - Costo: `CostoTotalAlMomento`; si falta, `CostoUnitarioAlMomento × cantidad`; si falta, `PrecioCompra × cantidad`.
- **Ganancia neta del período** = Σ ganancia de ventas − (neto devuelto − costo de lo devuelto).
- **Margen** = ganancia neta / (Σ ingreso neto − neto devuelto).
- Se usa `Total − IVA` y no `Subtotal` porque las ventas de cotización antiguas tienen `Subtotal == Total`.

## Casos borde

- Descuentos y recargos que no son de débito quedan dentro de `Total`/`IVA`.
- Envío y armados son ingreso sin costo.
- Factura emitida o no: no cambia nada, se leen los campos de la venta.
- Producto sin costo: costo 0.
- Venta cancelada: no suma ni resta.
- Recargo de débito: se excluye de la ganancia de la venta y, de forma simétrica, la parte proporcional del recargo se descuenta del neto devuelto (devolución total de una venta con débito deja ganancia 0, no negativa).
- Filtro común (`VentasNetas.Comerciales()`): ventas comerciales, no eliminadas y de cliente no eliminado, igual en Reporte y Dashboard.
- Márgenes: calcula sobre precio neto y resta las unidades devueltas de los últimos 30 días.

## Decisiones pendientes (comportamiento actual conservado)

1. `PrecioCompra` incluye IVA de compra: no se sabe si es crédito fiscal recuperable (contador).
2. Envío y armados siguen dentro de la ganancia, sin costo.
3. El recargo de tarjeta en cuotas y de transferencia sigue dentro de la ganancia; solo el de débito se separa.
4. La merma por devolución de producto dañado no aparece (el costo se revierte igual).
5. Una devolución Aprobada sin completar ya resta, en vez de esperar a Completada.
6. `FechaVenta` y `FechaDevolucion` están en UTC y se comparan con límites de día locales: cerca de medianoche una devolución puede caer en el día siguiente en «hoy».
7. El ticket promedio del Dashboard sigue bruto.
8. Una nota de crédito cancelada después no revierte el conteo de la devolución.

## Fuera de alcance

El Reporte de IVA y «IVA a depositar» no netean devoluciones; los tops de productos y `VentasAgrupadas` usan ganancia neta de IVA pero no restan devoluciones.
