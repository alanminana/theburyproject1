# Decisiones comerciales confirmadas y pendientes fiscales (contador)

Actualizado 2026-11-05. Fuentes: `docs/iva-recargos-verificacion.md`, `docs/reportes-ganancia-devoluciones.md`, `docs/envios-armados-costo-real.md`.

**Regla:** una decisión *comercial* confirmada por el dueño no implica validez *fiscal*. Nada de lo pendiente está validado por un contador; el ERP aplica un criterio provisional y conservador, y cada punto es un cambio localizado cuando llegue la respuesta.

## 1. Decisiones comerciales CONFIRMADAS

| Decisión | Alcance | Estado en el ERP |
|---|---|---|
| Los recargos por tarjeta y transferencia en **ventas** se siguen aplicando al precio según la configuración de pagos | No eliminar recargos, no cambiar porcentajes, mantener el cálculo existente | Sin cambios. El recargo se refleja en `Venta.Total` y en Caja. Su tratamiento de IVA es el punto 2.3 (pendiente). |
| El recargo (hoy 3 %) al **pagar cuotas** por transferencia debe verse **discriminado** | Pantalla de cobro (individual y adelanto), resultado del pago, recibo, Detalle de crédito y movimientos de Caja | Implementado como desglose operativo: importe de la cuota, recargo con su porcentaje, importe del recargo y total efectivamente cobrado. No altera capital, interés, punitorio ni saldo de la cuota, y no emite comprobantes fiscales. |
| El costo real de entrega y de armado se descuenta de la ganancia (alternativa A+B: costo estándar por tipo + costo real por venta; el real reemplaza al estándar) | Configuración de pagos → Envíos y armados, Detalle de venta, reportes de ganancia | Implementado y probado. **Migración aplicada a la base real el 2026-11-05 (autorizada, con respaldo verificado); todos los costos comienzan en $0** y se cargan desde la aplicación (ver `docs/envios-armados-costo-real.md`). No cambia IVA ni facturación. |
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


## 5. Lista mínima de decisiones imprescindibles (2026-11-11)

Base: resumen preliminar del contador transmitido por el propietario el 2026-11-11 (sin validación fiscal definitiva ni autorización de implementación) y los tests `FiscalCalculosF4Tests` / `FiscalConciliacionF4Tests`. **P-02 sigue ABIERTA.** Nada de lo siguiente está implementado.

### 5-a. Preguntas para el contador (responder sí/no o con un número)

| # | Pregunta | Ejemplo numérico reproducible (test) |
|---|---|---|
| C1 | ¿Con ítems de $1.000,50 el total cobrado es $1.000 (hacia abajo) en lugar de $1.001? | `Caracteriza_RedondeoVigenteVsDescritoPorElContador`: 1.000,50 → hoy 1.000 |
| C2 | Con ítems de $1.000,51, ¿corresponde $1.001 (hoy se cobra $1.000)? ¿Y con $1.000,99? | mismo test: delta +1,00 en ,51 y ,99 |
| C3 | La diferencia de redondeo, ¿se absorbe en el IVA (hoy) y no en el neto? | `TruncadoAPesos_CasosBorde`: 1.000,49 → neto 826,85 + IVA 173,15 |
| C4 | ¿El descuento en efectivo reduce la base imponible (hoy sí)? | 3.000 con −5 % = 2.850 = 2.355,37 + 494,63 (`Persistido_VentaFacturada…`) |
| C5 | ¿El descuento en efectivo alcanza también al envío y al armado (hoy sí)? | `VentaServiciosEnvioArmadoTests.Preview_ElRecargoDelPlanAlcanzaAProductosArmadosYEnvio` |
| C6 | ¿El envío y el armado van al 21 % siempre (hoy sí)? | envío 200 = 165,29 + 34,71; armado 450 = 371,90 + 78,10 (`ServiciosVentaIva_Separar…`) |
| C7 | El recargo por tarjeta o transferencia, ¿se grava a la alícuota de cada ítem (hoy)? | 1.500 (21 % + 10,5 %) con +3 % = 1.545 = 1.317,30 + 227,70 (`Persistido_VentaFacturada…` caso 2) |
| C8 | ¿Se admiten recargos para todas las tarjetas y planes configurados? | tarjeta +10 % sobre 1.000 = 1.100 = 909,09 + 190,91 |
| C9 | Sobre cada cuota cobrada (600 por transferencia, recargo 18), ¿se factura solo el interés (100)? ¿El recargo (18)? ¿Nada (el capital ya está en la factura de la venta)? | `Cuotas_Recargo3PorCiento…`: Caja 618, sin factura |
| C10 | ¿Los punitorios llevan IVA? | `PunitorioCalculator` no genera IVA ni comprobante |
| C11 | ¿El interés del crédito (300 sobre una venta de 2.000) debe figurar en la factura o en el contrato? | venta 2.000 + interés 300; IVA solo sobre 2.000 (347,11) |
| C12 | ¿El IVA de compra es crédito fiscal recuperable? | venta neta 826,45, costo 400 → ganancia 426,45; con costo sin IVA 330,58 → 495,87 |
| C13 | ¿El IVA de lo devuelto se resta del período de la devolución (fecha en que se aprueba la devolución)? | devolución de 950,00 (IVA 164,88): reporte 1.260,35 → 1.095,47 (`Caracteriza_ReporteIva_NoNetea…`) |
| C14 | El plazo de 7 días para restituir dinero, ¿corre desde la compra? Pasado el plazo, ¿solo corresponde nota de crédito? | el ERP hoy limita a 30 días desde la venta y deja elegir reembolso o NC |
| C15 | El período fiscal de una venta, ¿es el día comercial argentino (UTC−3)? | venta 31/10 22:30 ART = 01/11 01:30 UTC; hoy cae en noviembre (`Caracteriza_VentaDeLas2230…`) |

### 5-b. Decisiones del propietario

| # | Decisión | Por qué bloquea |
|---|---|---|
| P1 | ¿Las facturas fiscales (con CAE) se emiten fuera del ERP (sí/no)? | Hoy el ERP no tiene CAE ni integración ARCA/AFIP (`docs/iva-preservacion-historica.md` §4). Si se emiten fuera, el IVA del ERP es informativo y varias filas se reducen a leyendas y reportes. |
| P2 | ¿Se pretende que el ERP emita facturación electrónica (sí/no)? Si sí: proyecto aparte (WSAA/WSFE, certificados, CAE, controlador de numeración), no un ajuste de reglas. | Sin esto, la nota de crédito fiscal (resp. 5) y la factura de cuotas (resp. 4) no se pueden implementar. |
| P3 | ¿Se autoriza netear devoluciones en el Reporte de IVA por fecha de la devolución (fila 2.5a, «clara, lista para autorizar») (sí/no)? | Cambia un reporte, no importes persistidos. Ver D-F4-1. |
| P4 | ¿Se autoriza agregar al impreso de factura interna la leyenda «documento no válido como factura» (sí/no)? Texto legal a definir por el contador. | Hallazgo F4.4: el impreso muestra la letra A/B/C sin leyenda (`docs/iva-preservacion-historica.md` §4). |
| P5 | ¿Se autoriza versionar el criterio de cálculo por operación (sí/no)? | Sin versión, un cambio fiscal futuro reescribe en lectura el Reporte de IVA y el comprobante de ventas históricas (`docs/iva-preservacion-historica.md` §2–3). Debe decidirse **antes** de implementar cualquier fila. |
| P6 | Autorización por fila: cuál de 2.1–2.8 se implementa y desde qué fecha de corte. | Ninguna se implementa sin autorización expresa. |
