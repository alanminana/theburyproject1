# Decisiones comerciales confirmadas y pendientes fiscales (contador)

Actualizado 2026-11-05. Fuentes: `docs/iva-recargos-verificacion.md`, `docs/reportes-ganancia-devoluciones.md`, `docs/envios-armados-costo-real.md`.

**Regla:** una decisión *comercial* confirmada por el dueño no implica validez *fiscal*. Nada de lo pendiente está validado por un contador; el ERP aplica un criterio provisional y conservador, y cada punto es un cambio localizado cuando llegue la respuesta.

## 1. Decisiones comerciales CONFIRMADAS

| Decisión | Alcance | Estado en el ERP |
|---|---|---|
| Los recargos por tarjeta y transferencia en **ventas** se siguen aplicando al precio según la configuración de pagos | No eliminar recargos, no cambiar porcentajes, mantener el cálculo existente | Sin cambios. El recargo se refleja en `Venta.Total` y en Caja. Su tratamiento de IVA es el punto 2.3 (pendiente). |
| El recargo (hoy 3 %) al **pagar cuotas** por transferencia debe verse **discriminado** | Pantalla de cobro (individual y adelanto), resultado del pago, recibo, Detalle de crédito y movimientos de Caja | Implementado como desglose operativo: importe de la cuota, recargo con su porcentaje, importe del recargo y total efectivamente cobrado. No altera capital, interés, punitorio ni saldo de la cuota, y no emite comprobantes fiscales. |
| El costo real de entrega y de armado se descuenta de la ganancia (alternativa A+B: costo estándar por tipo + costo real por venta; el real reemplaza al estándar) | Configuración de pagos → Envíos y armados, Detalle de venta, reportes de ganancia | Implementado y probado en un clon; **migración sin aplicar a la base real** (ver `docs/envios-armados-costo-real.md`). No cambia IVA ni facturación. |
| Recibo con recargo/descuento del medio de pago y total cobrado (`RECIBO_COBRANZA`) | Plantilla persistida de la base real | **Aplicado a la base real** (versión 1 → 2, versión anterior conservada) con `scripts/sql/h12-recibo-cobranza-recargo.sql`. |
| Pago múltiple muestra el porcentaje del recargo/descuento, importe y total cobrado | Panel de cobro del cliente (Créditos) | Implementado (PR #229). |

Cómo queda el desglose del 3 % (sin migración ni cambios de entidad):

- **Cobro / adelanto de cuota:** preview y resultado muestran el importe, `Recargo por medio de pago (3 %)` con su importe, y `Total efectivamente cobrado`. El porcentaje es el configurado en Configuración de pagos para el medio, resuelto por el servidor (no se infiere en el navegador).
- **Cuota:** `Cuota.RecargoMedioPago` acumula el recargo (ya existía); `MontoPagado` sigue siendo solo capital/punitorio.
- **Caja:** `MovimientoCaja.ImporteBase`, `RecargoMedioPago` y `Monto = base + recargo` (ya existía), visibles en la conciliación.
- **Recibo:** variables nuevas `pago.tieneRecargo`, `pago.recargoMedioPago(+Formato)`, `pago.recargoPorcentajeFormato`, `pago.totalCobrado(+Formato/Letras)`. La plantilla **por defecto** (instalaciones nuevas) muestra el recargo y el total cobrado solo si hubo recargo, y "Son Pesos / Recibí conforme" usan el total cobrado. Las plantillas ya existentes en una base no se reescriben solas: hay que editarlas desde Configuración de documentos para incluir esas líneas.
- **Descuentos:** si el medio tiene descuento (p. ej. Efectivo −5 %), el recibo muestra `Descuento por medio de pago (5 %)` con su importe y el total cobrado.
- **Plantilla persistida de la base real:** actualizada con autorización (2026-11-05) mediante `scripts/sql/h12-recibo-cobranza-recargo.sql`. Se creó la versión 2 de `RECIBO_COBRANZA` reemplazando solo el bloque final; la versión 1 se conserva. Los recibos ya emitidos no cambian. La aplicación debe ejecutarse con una versión que incluya las variables nuevas (main ≥ a881a006).
- **Pago múltiple:** el preview y el resultado muestran `Recargo/Descuento por medio de pago (N %)`, el importe y el total efectivamente cobrado (PR #229).

## 2. Pendientes para el contador

Estado: **PENDIENTE** (sin criterio fiscal) o **PENDIENTE DE VALIDACIÓN** (criterio implementado, falta confirmarlo).

### 2.1 Intereses del crédito personal: factura e IVA — PENDIENTE
- **Qué hace hoy:** el interés vive en el crédito (`Credito.TotalAPagar`); queda fuera de la factura de venta y sin IVA. La factura cubre solo los ítems.
- **Falta saber:** si el interés de financiación de un crédito propio es exento, gravado o no alcanzado, y cómo se documenta.
- **Pregunta:** ¿el interés de un crédito propio sobre una venta de bienes es exento, gravado o no alcanzado? ¿Se documenta con factura, contrato u otro comprobante?
- **Podría cambiar:** factura de venta a crédito, contrato, IVA débito, cuotas (si se discrimina IVA en cada una).

### 2.2 Costo de productos con IVA y cálculo de ganancia — PENDIENTE
- **Qué hace hoy:** `PrecioCompra` es costo con IVA y gastos; la ganancia compara el precio de venta neto (sin IVA) contra ese costo. Subestima la ganancia si el IVA de compra es crédito fiscal.
- **Falta saber:** si el IVA de compra es crédito fiscal recuperable para este negocio.
- **Pregunta:** ¿el IVA de las compras es crédito fiscal recuperable? ¿El margen debe calcularse sobre costo sin IVA?
- **Podría cambiar:** reportes de ganancia y margen, catálogo (margen por producto), precio mínimo de Mercado Libre. `PorcentajeIVACompra` ya existe como dato informativo.

### 2.3 Recargos por tarjeta y transferencia en ventas (tratamiento fiscal) — PENDIENTE
- **Qué hace hoy:** el recargo se suma al total como importe final con IVA y se grava a la alícuota de los ítems (prorrateado si hay varias alícuotas).
- **Falta saber:** si integra el precio gravado del bien o es un concepto financiero distinto.
- **Pregunta:** ¿el recargo por pagar con tarjeta/transferencia integra el precio gravado del bien, o es un concepto financiero (exento/no gravado)? ¿A qué alícuota?
- **Podría cambiar:** `RecargoMedioPagoIva`, factura (línea propia si es concepto aparte), Reporte de IVA.

### 2.4 Tratamiento fiscal del 3 % cobrado en cuotas — PENDIENTE
- **Qué hace hoy:** entra a Caja y se muestra discriminado (sección 1), pero **no se factura ni se le calcula IVA** y no genera comprobante.
- **Falta saber:** si es un ingreso gravado y cómo se documenta.
- **Pregunta:** ¿un recargo cobrado por pagar una cuota por transferencia es un ingreso gravado o parte del cobro de la financiación? ¿Qué comprobante corresponde?
- **Podría cambiar:** emisión de comprobante o línea por cada cobro con recargo, Reporte de IVA, recibo.

### 2.5 IVA de devoluciones y notas de crédito — PENDIENTE
- **Qué hace hoy:** Reportes/Dashboard restan la devolución por su fecha. El Reporte de IVA y "IVA a depositar" **no** netean aún (lo avisan en su nota).
- **Falta saber:** período en que se computa la nota de crédito y si hay que emitir NC fiscal electrónica por cada devolución.
- **Pregunta:** ¿en qué período se resta el débito fiscal de una devolución (fecha de emisión de la NC)? ¿Se emite NC fiscal por cada devolución?
- **Podría cambiar:** Reporte de IVA (netear NC), emisión de NC electrónica, circuito de Devolución.

### 2.6 IVA de envíos y armados — PENDIENTE
- **Qué hace hoy:** se incluyen en el total de la venta y en el ingreso neto con IVA 21 % (`ServiciosVentaIva`); su costo (estándar o real) se descuenta de la ganancia sin tocar el IVA (ver `docs/envios-armados-costo-real.md`).
- **Falta saber:** si son prestaciones gravadas al 21 % cuando se facturan junto al bien.
- **Pregunta:** ¿el envío y el armado facturados junto con el bien son prestaciones gravadas al 21 %?
- **Podría cambiar:** `ServiciosVentaIva`, factura, Reporte de IVA, reportes de ganancia.

### 2.7 Descuento en Efectivo y base imponible — PENDIENTE DE VALIDACIÓN
- **Qué hace hoy:** el descuento del medio de pago (p. ej. Efectivo −5 %) reduce la base imponible y el IVA igual que un descuento de línea.
- **Falta saber:** si corresponde descuento comercial (reduce la base) o financiero posterior a la factura.
- **Pregunta:** ¿un descuento por pagar en efectivo reduce la base imponible o es un descuento financiero?
- **Podría cambiar:** una función de cálculo; neto/IVA de las ventas con descuento.

### 2.8 Redondeos y reconocimiento contable de devoluciones — PENDIENTE DE VALIDACIÓN
- **Qué hace hoy:** el total se trunca a pesos enteros y el centavo se absorbe en el IVA y luego en el neto. Una devolución *Aprobada* ya resta de ventas y ganancia (no espera a *Completada*).
- **Falta saber:** si el truncado es aceptable; si la devolución se reconoce al aprobar la NC o al recibir la mercadería (pregunta contable, no fiscal).
- **Pregunta:** ¿es aceptable truncar el total y absorber la diferencia en el IVA? ¿Se reconoce la devolución al aprobar la nota de crédito o al recibir la mercadería?
- **Podría cambiar:** importes de todas las facturas (redondeo); lista de estados que restan en reportes.

## 3. Qué llevar a la reunión

Una factura de ejemplo con descuento, una con recargo de tarjeta en cuotas, un cobro de cuota por transferencia con recargo (pantalla y recibo), una venta a crédito personal con su contrato, la tabla de alícuotas configuradas y el Reporte de IVA de un mes.
