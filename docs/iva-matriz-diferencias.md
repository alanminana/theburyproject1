# P-02 — IVA y facturación: matriz de diferencias y cambios necesarios

Fecha: 2026-11-10. Base: `main` cc44bbfc. Complementa `docs/iva-decisiones-contador.md` (preguntas) y `docs/iva-recargos-verificacion.md` (verificación). **No se implementa ninguna regla fiscal en este documento ni se modifica ningún importe histórico.**

## 0. Qué falta para poder implementar

La tarea P-02 del tablero maestro de Notion y las **ocho respuestas recibidas** no pude leerlas: esta sesión no tiene conector de Notion. Por eso este documento **no clasifica las respuestas** como confirmadas, ambiguas o contradictorias. Para completarlo hace falta una de dos cosas: que se pegue el texto de las ocho respuestas, o que se habilite el conector. Con eso, la columna «Respuesta del contador» de la sección 3 se completa y cada fila pasa a una de tres clases: **implementable ya** (respuesta clara, cambio localizado, expresamente autorizado), **requiere aclaración** (ambigua o contradictoria) o **requiere decisión del propietario**.

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
