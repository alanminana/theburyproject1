# P-02 — IVA y facturación: matriz de diferencias y cambios necesarios

Fecha: 2026-11-10. Base: `main` cc44bbfc. Complementa `docs/iva-decisiones-contador.md` (preguntas) y `docs/iva-recargos-verificacion.md` (verificación). **No se implementa ninguna regla fiscal en este documento ni se modifica ningún importe histórico.**

## 0. Estado de las respuestas del contador

> **Actualización 2026-11-11.** La versión del 2026-11-10 de este documento decía que no se habían leído las respuestas (sin conector de Notion). Hoy el propietario transmitió un **resumen preliminar** de las ocho respuestas; está incorporado y clasificado en la sección 5. **Es un «resumen preliminar transmitido por el propietario el 2026-11-11, sin validación fiscal definitiva ni autorización de implementación».** Ninguna fila pasa a «implementable» sin autorización expresa del propietario: la clase máxima es «clara, lista para autorizar». La tarea **P-02 permanece ABIERTA**.

(Historial, texto del 2026-11-10: «La tarea P-02 del tablero maestro de Notion y las ocho respuestas recibidas no pude leerlas: esta sesión no tiene conector de Notion. Por eso este documento no clasifica las respuestas como confirmadas, ambiguas o contradictorias».)

## 1. Naturaleza de los comprobantes (hallazgo medido en el código)

- `Factura` (`Models/Entities/Factura.cs`) guarda `Numero`, `Tipo` (A/B/C/Nota de crédito/Nota de débito), `PuntoVenta`, `Subtotal`, `IVA`, `Total` y anulación. **No tiene CAE, vencimiento de CAE, CUIT del emisor ni código de barras**, y no existe ninguna integración con ARCA/AFIP (sin servicio de facturación electrónica, sin dependencia, sin configuración).
- Conclusión: los comprobantes del ERP son **internos** (numeración correlativa propia). No son facturas electrónicas fiscales. Si el negocio emite sus facturas fiscales con otro sistema o un controlador fiscal, el ERP es registro operativo y el IVA que muestra es una **discriminación informativa**, no la liquidación.
- **Pregunta bloqueante para el propietario:** ¿las facturas fiscales se emiten fuera del ERP? (Y, si no, ¿se pretende que el ERP emita facturación electrónica? Eso es un proyecto nuevo, no un ajuste de reglas, y no está en el alcance aprobado.)

## 2. Circuito actual (dónde vive cada cálculo)

| Concepto | Dónde | Comportamiento vigente |
|---|---|---|
| Neto/IVA de una venta | `VentaService` (`NormalizarNetoEIvaDesdeTotal`), `CotizacionConversionService` | Neto + IVA = total; el total se **trunca a pesos enteros** (`TruncarAPesos`, `Math.Floor`) y el centavo se absorbe en IVA y luego en neto |
| Recargo/descuento del medio de pago | `Helpers/RecargoMedioPagoIva.Discriminar` | Se reparte por alícuota de los ítems (criterio conservador); el descuento de Efectivo reduce base e IVA como un descuento de línea |
| Envío y armado | `ServiciosVentaIva` | IVA 21 % incluido en el total |
| Factura (comprobante) | `Helpers/FacturaComprobanteBuilder` | Muestra neto, IVA, descuento/recargo incluido; cierra siempre (neto + IVA = total) |
| Interés del crédito personal | `Credito.TotalAPagar`, cuotas | Fuera de la factura de venta y **sin IVA** |
| Recargo 3 % en cuotas | `Cuota.RecargoMedioPago`, `MovimientoCaja` | Discriminado en cobro/recibo/caja; **sin IVA, sin comprobante** |
| Reporte de IVA | `ReporteService.GenerarReporteIvaAsync` | Por venta (líneas + servicios + ajuste de medio de pago); **no netea devoluciones/notas de crédito** |
| Ganancia | Reportes (`ProductoService`, `PrecioCompra`) | Venta neta sin IVA contra `PrecioCompra` con IVA (puede subestimar si el IVA de compra es crédito fiscal); descuenta costo de envío/armado |
| Devoluciones | `DevolucionService` | Reintegra lo efectivamente cobrado (con ajuste del medio de pago, proporcional) |

## 3. Matriz de diferencias por pregunta pendiente

| # | Tema | Hoy | Alternativas y cambio necesario | Impacto en históricos | Riesgo | Respuesta del contador | Clase |
|---|---|---|---|---|---|---|---|
| 2.1 | Interés del crédito personal | Fuera de factura, sin IVA | Si **gravado**: línea de interés con IVA en la factura/contrato y IVA débito en `ReporteService`; si **exento/no alcanzado**: solo leyenda. Toca `FacturaComprobanteBuilder`, contrato, cuotas | Ventas a crédito ya emitidas: el interés no tiene IVA registrado; **no se recalcula**, rige hacia adelante | Alto (cambia importes visibles al cliente) | Pendiente de leer | — |
| 2.2 | IVA de compras y ganancia | Costo con IVA | Si el IVA de compra es crédito fiscal: margen sobre costo **sin** IVA (`PorcentajeIVACompra` ya existe como dato). Toca reportes de ganancia/margen, catálogo, precio mínimo de Mercado Libre | La ganancia histórica cambia solo si se recalcula el reporte (los datos no se modifican) | Medio | Pendiente de leer | — |
| 2.3 | Recargo de tarjeta/transferencia en ventas | Se grava a la alícuota de los ítems | A) vigente; B) siempre 21 %; C) concepto financiero aparte (línea propia, exento/no gravado). Cambia `RecargoMedioPagoIva.Discriminar` y el comprobante | Facturas emitidas no se tocan | Medio | Pendiente de leer | — |
| 2.4 | 3 % en cuotas | Sin IVA ni comprobante | Si gravado: emitir línea/comprobante por cobro y sumar a IVA; si no, mantener | Cobros históricos sin comprobante; no se emiten con efecto retroactivo | Alto | Pendiente de leer | — |
| 2.5 | Devoluciones y notas de crédito | Reportes netean por fecha de devolución; **el Reporte de IVA no** | Netear el débito fiscal por el período de la nota de crédito en `GenerarReporteIvaAsync`; decidir si cada devolución emite NC fiscal (depende del punto 1) | Reportes recalculados, datos sin cambios | Medio | Pendiente de leer | — |
| 2.6 | IVA de envío y armado | 21 % incluido | Confirmar gravado al 21 %; si no, `ServiciosVentaIva` | Facturas emitidas no se tocan | Bajo | Pendiente de leer | — |
| 2.7 | Descuento en Efectivo | Reduce base imponible | Si es **financiero posterior**: no reduce base, se muestra aparte; cambia una función de cálculo | Facturas emitidas no se tocan | Bajo | Pendiente de leer | — |
| 2.8 | Redondeo y reconocimiento de devoluciones | Total truncado a pesos; devolución *Aprobada* resta | Cambiar a redondeo comercial (`MidpointRounding`) altera el total de **toda** venta nueva; reconocer al *Completar* cambia la lista de estados de los reportes | Solo hacia adelante: un cambio de redondeo **no debe** reescribir ventas existentes | Alto (redondeo) | Pendiente de leer | — |

## 4. Reglas para cuando haya respuestas

1. Se implementa **solo** una fila cuya respuesta sea clara, no contradictoria y esté **expresamente autorizada** por el propietario.
2. Todo cambio de cálculo es **hacia adelante**: no se modifica ningún importe histórico (facturas, ventas, cobros, reportes ya emitidos). Si un reporte debe recalcularse, se recalcula en lectura, sin escribir.
3. Cada cambio con test que falle antes y pase después, revisión de Codex y comparación del Reporte de IVA de un mes antes/después en un clon.
4. Si los comprobantes resultan ser internos y el IVA se liquida fuera del ERP, varias filas (2.1, 2.4) se reducen a **leyendas y reportes**, no a emisión de comprobantes.


## 5. Matriz completada con el resumen preliminar del contador (2026-11-11)

Fuente de la columna «Respuesta»: **resumen preliminar transmitido por el propietario el 2026-11-11, sin validación fiscal definitiva ni autorización de implementación.** Clases: **coincide** con lo implementado, **difiere** (delta exacto + ejemplo numérico), **ambigua** (pregunta precisa). Los ejemplos salen de los tests nuevos `FiscalCalculosF4Tests` y `FiscalConciliacionF4Tests` (valores calculados a mano, leídos de lo persistido). La numeración 2.1–2.8 no coincide 1:1 con las 8 respuestas; el mapeo está en la primera columna.

| Fila (respuesta) | Respuesta del contador (preliminar) | Clase | Detalle y ejemplo numérico | Pregunta precisa o siguiente paso |
|---|---|---|---|---|
| 2.1 Interés del crédito (resp. 1) | Los intereses del crédito propio no están alcanzados por IVA; falta justificar frente a las normas de intereses de financiación de ventas | **Coincide**; **ambigua** en la justificación | Hoy el interés vive en `Credito.TotalAPagar` y no lleva IVA. Ej.: venta 2.000, anticipo 500, 3 cuotas de 600 (capital 500 + interés 100): el cliente paga 500 + 1.800 = 2.300; los 300 de interés no pasan por IVA ni por la factura (IVA de la venta 347,11 = solo ítems). | ¿Hay que dejar constancia del interés en la factura o en el contrato (sí/no)? Sin cambio de código. |
| 2.2 Costo y ganancia (resp. 2) | Calcular ganancia con precio de costo CON IVA; falta distinguir costo gerencial de crédito fiscal recuperable | **Coincide**; **ambigua** en el alcance | Hoy `PrecioCompra` (con IVA) contra venta neta sin IVA. Ej.: producto vendido a 1.000 (neto 826,45) con costo 400: ganancia 426,45; con costo sin IVA (330,58) sería 495,87 (delta 69,42). | ¿El IVA de compra es crédito fiscal recuperable para este negocio (sí/no)? Si sí, ¿se informa además un margen sobre costo sin IVA (sí/no)? |
| 2.3 Recargos en ventas (resp. 3) | Forman parte de la base imponible; revisar por tipo de operación y restricciones de recargos con tarjeta | **Coincide**; **ambigua** en tipo de operación y tarjeta | Hoy se grava a la alícuota de los ítems, prorrateado. Ej.: transferencia +3 % sobre A (21 %) + B (10,5 %): total 1.545 = neto 1.317,30 + IVA 227,70; tarjeta +10 % sobre 1.000: 1.100 = 909,09 + 190,91. | ¿El recargo se admite para todas las tarjetas y planes configurados (sí/no)? ¿Algún medio debe ir sin recargo? |
| 2.4 Cobro de cuotas (resp. 4) | Facturar cuotas abonadas por medios registrables; falta precisar capital ya facturado, intereses, punitorios, recargos y documentación | **Difiere** | Hoy el cobro de cuota genera recibo (documento interno) y movimiento de Caja, **sin factura**. Ej.: cuota de 600 por transferencia: Caja 618 (base 600 + recargo 18 = 3 %); ninguna factura, ningún IVA. | ¿Qué se factura por cada cuota: solo intereses, intereses + recargo, o nada porque el capital ya está en la factura de la venta? ¿Los punitorios llevan IVA (sí/no)? |
| 2.5 Devoluciones y NC (resp. 5) | Emitir NC si la venta fue facturada y reconocer el IVA según el período de la NC | **Difiere** (dos partes) | (a) El Reporte de IVA no netea. Ej.: V1 (3 × 1.000 con −5 %, cobrado 2.850, IVA 494,63); se devuelve 1 unidad = 950,00 (neto 785,12, IVA 164,88): el reporte sigue en 1.260,35 y quedaría en 1.095,47. (b) No existe NC fiscal: la NC del ERP (`NotaCredito`) es interna, sin CAE. | (a) **Clara, lista para autorizar**: netear en el Reporte de IVA el IVA de lo devuelto por la fecha de la devolución Aprobada/Completada (misma regla que ya usa el Reporte de Ventas). (b) Decisión del propietario sobre facturación electrónica (`docs/iva-decisiones-contador.md` §5-b). |
| 2.6 Envío y armado (resp. 6) | Envío y armado al 21 % | **Coincide** | Hoy `ServiciosVentaIva`: precio final con IVA 21 % incluido. Ej.: envío 200 = 165,29 + 34,71; armado 450 = 371,90 + 78,10; producto 1.000 + servicios 650 = 1.650 = 1.363,64 + 286,36. | ¿Algún envío (p. ej. fuera del país) o armado sin venta de bien lleva otra alícuota (sí/no)? |
| 2.7 Descuento en efectivo (resp. 6) | «Con descuento en efectivo» | **Ambigua** | Hoy reduce base e IVA y alcanza también al envío y armado (el plan del medio se calcula sobre el total). Ej.: 3.000 con −5 %: cobrado 2.850 = 2.355,37 + 494,63 (sin descuento: 2.479,34 + 520,66). | ¿El descuento en efectivo es comercial, o sea reduce la base como hoy (sí/no)? ¿Alcanza al envío y armado (sí/no)? |
| 2.8a Redondeo (resp. 8) | A pesos: hacia arriba si supera $0,50, hacia abajo si es inferior | **Difiere** | Hoy se **trunca** siempre hacia abajo (`Math.Floor`). Ej.: ítems 1.000,51 y 1.000,99 → se cobra 1.000 (contador: 1.001, delta +1,00); 1.000,49 → 1.000 (igual); 1.000,50 → 1.000 (igual si «supera» es estricto). El centavo se absorbe en el IVA y luego en el neto: 1.000,49 → neto 826,85 + IVA 173,15. | ¿Con exactamente $0,50 se redondea hacia abajo (sí/no)? ¿La diferencia de redondeo se absorbe en el IVA como hoy (sí/no)? |
| 2.8b Reconocimiento de devoluciones (resp. 7) | Disminuir la venta al devolver; restituir dinero hasta una semana y luego crédito; separar devolución comercial, restitución, derechos del consumidor y documento fiscal | **Coincide** en reconocer al devolver; **ambigua** en el plazo | Hoy la devolución Aprobada/Completada resta de ventas y ganancia por su fecha. El operador elige reembolso o NC; el ERP limita devolver a 30 días desde la venta (`DiasLimiteDevolucion`) pero no distingue «dinero hasta 7 días, luego crédito». | ¿Los 7 días corren desde la compra o desde la solicitud (compra/solicitud)? ¿Pasado el plazo solo corresponde NC (sí/no)? |

Defecto matemático hallado durante la auditoría, **con corrección propuesta en el PR #280, todavía sin fusionar** (espera la revisión independiente de Codex; no es una de las 8 filas): con el total truncado a pesos, devolver todo reintegraba hasta $0,99 más de lo cobrado (3 × 333,33 = 999,99; cobrado 999; reintegro 999,99; con la corrección, 999,00). Los tests de consistencia citados en esta sección (`FiscalCalculosF4Tests`, `FiscalConciliacionF4Tests`) están en ese mismo PR.

### Otras diferencias entre módulos medidas el 2026-11-11 (criterio, NO corregidas)

| # | Hallazgo | Números | Clase |
|---|---|---|---|
| D-F4-1 | El Reporte de IVA no netea devoluciones (conocida) | IVA 1.260,35 vs 1.095,47 con el criterio de la NC | Criterio pendiente (2.5a) |
| D-F4-2 | Día de imputación: los reportes de Ventas, IVA y «por día» usan el día **UTC** de `FechaVenta`; mora y punitorios usan el día comercial (UTC−3) | Venta del 31/10 22:30 ART = 01/11 01:30 UTC: aparece en noviembre (octubre 0 ventas; noviembre 1 venta e IVA 210,00) | Criterio pendiente (¿el período es el día comercial?) |
| D-F4-3 | Venta legacy con envío fuera del total: Caja cobra `TotalACobrar` = Total + envío; factura y Reporte de Ventas solo el Total | Caja 1.200, factura 1.000, reporte 1.000: 200 de envío sin IVA ni factura | Dato legacy (no se recalcula) |
| D-F4-4 | Venta Confirmada sin facturar (o con factura anulada) cuenta en Ventas y Caja pero no declara IVA | V5: 1.000 en Ventas y Caja, 0 en IVA | Criterio vigente documentado |
| D-F4-5 | El recargo 3 % de cuotas entra a Caja y recibo, no a Ventas ni IVA | Ingresos de Caja 8.213 = Ventas 8.495 − capital a crédito pendiente 500 + interés cobrado 200 + recargo 18 | Criterio pendiente (2.4) |
