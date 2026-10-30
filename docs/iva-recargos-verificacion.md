# IVA, intereses y recargos: verificación integral (Hito 9, M4)

Fecha: 2026-10-29/30. Base: `main` 216e8166. Método: clon de la base real (`TheBuryProjectDb_qa_h9m4`, ya eliminado), app en `127.0.0.1:6300`, ventas reales creadas por los endpoints HTTP (CreateAjax, Confirmar, Facturar, conversión de cotización, QuickCreate/Aprobar/Completar de devoluciones, Mora) y contraste contra consultas SQL propias. La base real no se tocó.

Alcance de este documento: separar **errores de implementación** (corregidos aquí, con test que falla antes y pasa después) de **decisiones fiscales no definidas** (no se tocó ninguna alícuota ni criterio impositivo nuevo: se aplicó, con signo, el mismo criterio ya adoptado en #182).

## 1. Matriz verificada

| Caso | Venta (Subtotal+IVA = Total) | Factura cierra | Caja (movimiento) | Devolución total/parcial | Resultado |
|---|---|---|---|---|---|
| Efectivo, descuento de línea 10 % (directa) | sí | sí | sí | parcial x2 suma 328.498,20 sobre 328.498 cobrados (+0,20) | devolucion: otro agente (D7) |
| Efectivo, descuento general (directa) | sí | sí | sí | exacta | OK |
| Transferencia +3 % (directa) | sin recargo: la venta directa no aplica el plan de Transferencia | sí | sí | exacta | decisión funcional (D4) |
| Efectivo -5 % / Transferencia +3 % (de cotización) | **no**: Subtotal bruto, IVA sin ajustar | **no** (neto+IVA > o < total) | sí | **reintegraba precio de lista** con el descuento | IVA/factura corregidos aqui; reembolso: otro agente (D7) |
| Débito +3 % (directa y de cotización) | sí | sí | sí | exacta | OK (#182) |
| Tarjeta crédito 3 cuotas +10 % (directa y de cotización) | sí | sí | sí | exacta | OK |
| Crédito personal con anticipo e interés de plan | Venta = ítems; el interés vive en `Credito.TotalAPagar` | la factura de venta cubre solo los ítems | anticipo y cuotas por Caja | bloqueada con crédito vigente (#176) | interés fuera de IVA: decisión D1; factura NO VALIDADA (exige contrato impreso) |
| Venta con envío (directa y de cotización) | sí (envío al 21 %) | sí | sí | envío no se devuelve; ajuste prorrateado | OK |
| Multi-alícuota 21 % + 10,5 % (directa, débito, cotización) | sí | sí | sí | exacta | OK |

Evidencia SQL (clon): 7 facturas emitidas antes del arreglo no cerraban (ventas 2058-2064: desvío total 248.660,38); las 16 emitidas después cierran con desvío 0,00. La venta 2058 (Efectivo -5 %, cobrado 173.374,05) tenía Subtotal 182.499,00, IVA 31.673,38 (sobre precio de lista) y su devolución total egresó 182.499,00 de caja (9.124,95 de más).

## 2. Discrepancias encontradas

| # | Hallazgo | Tipo | Estado |
|---|---|---|---|
| H1 | Venta nacida de cotización guardaba `Subtotal` con IVA incluido (`Subtotal == Total`) y `IVA` calculado sobre precio de lista: la factura no cerraba, Caja "Neto" mostraba el bruto y el IVA de un descuento de Efectivo quedaba sobreestimado; el recargo de Transferencia no tenía IVA | Implementación | Corregido |
| H2 | Descuento de Efectivo (o de un plan con ajuste negativo) no reducía neto ni IVA de la venta ni del comprobante | Implementación | Corregido (mismo criterio que un descuento de línea) |
| H3 | Devolución de una venta con descuento del medio de pago reintegra el precio de lista (más de lo cobrado; venta 2058: egreso 182.499,00 sobre 173.374,05 cobrados) | Implementación | NO corregido aquí: lo corrige otro agente en `DevolucionService` (D7) |
| H4 | Devoluciones sucesivas pueden sumar centavos de más sobre el total truncado a pesos (+0,20 en la venta 2049) | Implementación (baja) | Mismo agente y archivo (D7) |
| H5 | Reporte de IVA tomaba solo las líneas de producto: omitía el IVA del envío, de los armados y del recargo/descuento del medio de pago (15 ventas del mismo turno: reporte total 3.228.484,20 e IVA 532.829,54; Caja y facturas 3.372.328,01 e IVA 558.222,27) | Implementación | Corregido: usa la misma discriminación que el comprobante |
| H6 | **Cotización con descuentos**: la conversión pierde el descuento porcentual de línea, no suma porcentaje + importe y no propaga el descuento general. Cotizado 402.323,29 / 220.316,05 / 280.673,00 / 170.636,00; persistido 436.998,10 / 239.473,97 / 290.973,00 / 200.748,00 | Implementación | NO corregido aquí: lo corrige #203 (integración #204). El defecto se reprodujo en el clon sobre main; la corrección de #203 no se re-verificó aquí (pendiente de integrar) |
| H7 | El paso automático de un acuerdo de mora a Cumplido no dejaba evento de auditoría (sí el incumplimiento) | Implementación (baja) | Corregido: `CumplimientoAutomatico` |
| H8 | Reportes y Dashboard suman `Venta.Total` completo, sin descontar devoluciones ni notas de crédito (9 ventas con devolución completa suman 2.121.412 de las 3.631.412 del mes) | Definición de KPI | Decisión D3 |
| H9 | "Ganancia" del Reporte de Ventas y Márgenes = total con IVA - costo; el margen incluye el IVA y el recargo financiero (salvo débito) | Definición de KPI / fiscal | Decisión D5 |
| H10 | Venta directa con Transferencia no aplica el plan de +3 % que sí aplica la cotización (mismo medio, distinto precio según el origen) | Funcional | **Resuelto en el Hito 10** (D4, ver sección 5) |

Sin defectos: cancelación de venta pagada con nota de crédito (el saldo vuelve a Vigente, la caja se revierte, el reintento responde "La venta ya está cancelada" sin doble restitución, también facturada: factura anulada y stock repuesto) y acuerdos de pago con Transferencia, Tarjeta crédito y Tarjeta débito y punitorio > 0 (ver QA en el informe del PR).

## 3. Cambios de código (todos reversibles, sin tocar alícuotas)

- `RecargoMedioPagoIva.Discriminar` / `ResumenPorAlicuota`: fuente única que, dado el total cobrado y las líneas, reparte el ajuste del medio de pago (con signo) por alícuota, absorbe el truncado a pesos y garantiza neto + IVA = total.
- `VentaService.NormalizarNetoEIvaDesdeTotal`: deja `Subtotal` neto e `IVA` coherentes en ventas de cotización (conversión y, para ventas viejas, al facturar). `DiscriminarRecargoEnNetoEIva` ahora también resta (descuento).
- `FacturaComprobanteBuilder` y la vista del comprobante: muestran el descuento incluido igual que el recargo.
- `CotizacionConversionService`: normaliza Subtotal/IVA de la venta convertida y el preview de factura usa la misma convención (los descuentos de la cotización los corrige #203).
- `ReporteService.GenerarReporteIvaAsync`: por venta (líneas + servicios + ajuste).
- `MoraService`: evento `CumplimientoAutomatico`.

Datos existentes: la base real no tiene facturas emitidas vigentes (una sola, anulada), así que no hace falta reparar datos. Una venta confirmada antes de este cambio se normaliza al facturarla.

## 4. Decisiones fiscales y funcionales

**D1 (la única indispensable): tratamiento del interés de financiación y del recargo del medio de pago.** Hoy el recargo de débito/tarjeta/transferencia se grava a la alícuota de los ítems (criterio #182, ahora también aplicado al de Efectivo/Transferencia de cotizaciones) y el interés del crédito personal queda fuera de la factura de venta. Bloquea declarar con seguridad el IVA de las ventas con tarjeta en cuotas o transferencia.

| Opción | Efecto | Impacto en el ERP |
|---|---|---|
| A. Gravado a la alícuota de los ítems (vigente) | Mayor IVA débito; simple de explicar | Ninguno |
| B. Gravado siempre al 21 % | Distinto cuando hay ítems al 10,5 % | Cambiar la base de `Discriminar` (una función) |
| C. Exento / no gravado | Menor IVA débito; el recargo sale del neto | `Discriminar` reparte solo con alícuota 0 y el comprobante lo muestra aparte |
| D. Concepto aparte con alícuota propia | Línea propia en la factura | Cambio de modelo (línea de recargo), más trabajo |

Recomendación: mantener A hasta la validación del contador (es el más conservador y ya está probado); elegir C si se concluye que el interés de financiación va aparte. La decisión se toma una sola vez y se cambia en `RecargoMedioPagoIva.Discriminar`.

Decisiones no bloqueantes (recomendación entre paréntesis):

- D2. Los descuentos de medio de pago reducen la base imponible, igual que los de línea (mantener; ya implementado, confirmar con el contador).
- D3. Netear devoluciones y notas de crédito en "IVA a depositar", Reporte de IVA, Reportes de Ventas y Dashboard (recomendado: restar el IVA proporcional de las devoluciones completadas del período; hoy el reporte de IVA lo avisa explícitamente en su nota).
- D4. Aplicar el plan de Efectivo/Transferencia también a la venta directa, o mostrar que solo rige para la cotización (recomendado: unificar con la cotización).
- D5. Definir "ganancia" y margen sobre neto (sin IVA) y separar el recargo financiero (recomendado).
- D7. Devolución con descuento del medio de pago o centavos truncados: reembolso mayor que lo cobrado; corrección en curso por otro agente (no se repite aquí). Con este PR el descuento ya queda discriminado en `Venta.IVA`, lo que esa corrección puede usar.
- D6. El recargo cobrado al pagar una cuota con Transferencia (+3 %, p. ej. 300 sobre 10.000) entra a caja pero no se factura ni se discrimina.

## 5. Hito 10: D4 resuelto (venta directa con Efectivo / Transferencia)

Se siguió la recomendación de D4 (unificar con la cotización). La venta directa (`Venta/Create`, `CreateAjax` y `Edit`) aplica el plan general de 1 pago de Efectivo o Transferencia con la misma fuente (`AjusteMedioUnPago`) que la conversión de una cotización:

- El servidor es autoritativo: `VentaService.CreateAsync`/`UpdateAsync` aplican el ajuste justo después de `CalcularTotales` (una sola vez; `CalcularTotales` siempre parte de la base de lista) y lo discriminan en neto + IVA con `DiscriminarRecargoEnNetoEIva` (Subtotal + IVA = Total).
- El preview de totales (`/api/ventas/CalcularTotalesVenta`) devuelve el mismo importe (`TotalConAjustePagoGlobal`), de modo que el wizard muestra el mismo total que se guarda, con la fila «Recargo/Descuento por forma de pago» en Revisión. Al cambiar el medio entre Efectivo, Transferencia y otros el wizard recalcula.
- Convertir una cotización y editar la venta resultante no aplica el ajuste dos veces (cubierto por tests y verificado en el clon).
- Con la configuración real (Efectivo -5 %, Transferencia +3 %), una venta directa de 182.499,00 cobra 173.374,05 (Efectivo) y 187.973,97 (Transferencia), igual que la misma venta cotizada: Subtotal + IVA = Total, la factura cierra y la caja registra el total.
- **Cambio de comportamiento a tener presente:** la venta directa en Efectivo ahora cobra con el descuento del plan configurado (antes cobraba a precio de lista). Si el negocio no quiere descuento en el mostrador, se desactiva en el plan de Efectivo (Configuración de pagos), no en el código. Una venta directa ya creada no cambia hasta que se edite.
- Sin plan activo, o con ajuste 0, no hay cambio.

