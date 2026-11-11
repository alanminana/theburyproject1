# P-02 — Preservación histórica y naturaleza de los comprobantes (F4.3 y F4.4)

Fecha: 2026-11-11. Base: `main` 29dff9d9. Solo análisis de código: **no se tocó ningún dato, no hay migraciones, no se implementó ningún criterio fiscal.** Complementa `docs/iva-matriz-diferencias.md` y `docs/iva-decisiones-contador.md`. **P-02 permanece ABIERTA.**

## 1. Qué se guarda congelado y qué se recalcula al leer

**Importes persistidos (congelados al momento de la operación):**

| Dato | Dónde |
|---|---|
| Neto, IVA y total de la venta | `Venta.Subtotal` (neto), `Venta.IVA`, `Venta.Total`, `Venta.AjusteMedioPagoPorcentaje/Monto`, `DatosTarjeta.*Aplicado` |
| Alícuota, neto e IVA por línea; costo | `VentaDetalle.PorcentajeIVA`, `SubtotalNeto/IVA`, `SubtotalFinalNeto/IVA`, `CostoUnitarioAlMomento`, `CostoTotalAlMomento` |
| Armado y envío (importe y costo) | `VentaDetalle.ArmadoSubtotal`, `VentaEnvio.CostoEnvio/IncluidoEnTotal`, `CostoEstandarServicio/CostoRealServicio` |
| Factura | `Factura.Subtotal`, `IVA`, `Total`, `Numero`, `Tipo`, `FechaEmision`, anulación |
| Cobros | `MovimientoCaja.Monto/ImporteBase/RecargoMedioPago/DescuentoMedioPago`, `PagoCuota.*`, `Cuota.RecargoMedioPago` |
| Crédito | `Credito.TotalAPagar`, `Cuota.MontoCapital/MontoInteres/MontoTotal/MontoPunitorio` |
| Devoluciones | `Devolucion.TotalDevolucion`, `DevolucionDetalle.Subtotal`, `NotaCredito.MontoTotal` |

**Lo que se RECALCULA en lectura con el criterio vigente (riesgo de reescribir el pasado):**

| Pantalla / servicio | Qué recalcula | Qué lee de datos crudos | Riesgo si cambia el criterio |
|---|---|---|---|
| Reporte de IVA (`ReporteService.GenerarReporteIvaAsync`) | Desglose por alícuota: llama a `RecargoMedioPagoIva.ResumenPorAlicuota(venta.Total, líneas)` y `ServiciosVentaIva.Separar` de armados y envío; el ajuste del medio de pago y el truncado se reparten otra vez | `Venta.Total`, líneas, `ArmadoSubtotal`, `VentaEnvio.CostoEnvio` | **Alto.** `ServiciosVentaIva.Porcentaje` sale de `ProductoIvaResolver.PorcentajeDefault` (configuración `Iva:PorcentajeDefault`): cambiar esa alícuota reescribe el IVA de servicios de TODAS las ventas pasadas en el reporte, mientras `Venta.IVA` y `Factura.IVA` quedan con la alícuota anterior. Cualquier cambio de la fórmula de prorrateo, truncado o tratamiento del recargo también. |
| Comprobante de factura (`FacturaComprobanteBuilder`) | Líneas de armado y envío (`ServiciosVentaIva`), resumen por alícuota y recargo discriminado; los totales netos salen de `Factura.*` solo si son distintos de 0 | `Venta`, líneas, `Factura` | **Medio-alto.** Reimprimir una factura vieja tras un cambio fiscal muestra un desglose recalculado que puede no coincidir con `Factura.Subtotal/IVA`. |
| Ganancia y márgenes (`VentasNetas.CalcularIngresoNeto`, `ObtenerDevolucionesAsync`) | Ingreso neto = `Total − IVA − neto del recargo de débito` (el neto del recargo se reparte otra vez con `Repartir`); neto de lo devuelto = proporción de las líneas | `Venta.Total/IVA`, `DatosTarjeta.RecargoAplicado` | **Medio.** Cambiar el tratamiento del recargo de débito o de intereses cambia la ganancia histórica. |
| Costo de líneas sin snapshot | Si `CostoTotalAlMomento` es 0 (ventas legacy), usa `Producto.PrecioCompra` **actual** | `Producto.PrecioCompra` | **Medio.** El margen de ventas viejas sin snapshot cambia al cambiar el costo del producto. Las demás líneas están congeladas (corregido en H-14). |
| Reporte de Márgenes del catálogo | Precio de venta y costo vigentes del producto | `Producto` | Bajo (es una foto actual, no un reporte histórico). |
| Comisiones | Leen `ComisionMonto` congelado | — | Ninguno. |
| Caja y conciliación (`CajaConciliacionBuilder`) | Leen movimientos congelados; el IVA de una venta sale de `Venta.IVA` | `MovimientoCaja`, `Venta` | Bajo. |
| Cuotas y mora | Importes congelados; el punitorio se calcula con la configuración vigente al cobrar y se persiste | `Cuota`, `PunitorioAplicado` | Bajo para cuotas existentes. El **recargo del medio de pago** se resuelve al cobrar y queda en la cuota, el movimiento y el recibo. |
| Recibo (`DocumentoContextoBuilder`) | Lee el recargo del movimiento de caja, no lo recalcula | `MovimientoCaja` | Ninguno. |
| Notas de crédito internas | `MontoTotal` congelado al aprobar | — | Ninguno. |

Conclusión: **las facturas, ventas, cuotas, movimientos de Caja y notas de crédito están protegidas** (importes persistidos). **Se reescriben al leer el Reporte de IVA, el desglose del comprobante impreso y la ganancia/margen**, porque reconstruyen el reparto por alícuota, el recargo y los servicios a partir de `Total` y de líneas con el criterio y la alícuota vigentes. Un cambio fiscal futuro cambiaría el pasado en esas tres vistas aunque no se modifique ningún dato.

## 2. ¿Hace falta versionar el criterio?

**Sí, para cualquier cambio fiscal futuro que afecte el reparto por alícuota, el tratamiento de recargos/descuentos/servicios o el redondeo.** Un cambio hacia adelante sin versión haría que el Reporte de IVA de un mes ya presentado cambie al reabrirlo. La regla ya aceptada en la matriz («todo cambio de cálculo es hacia adelante») no se puede cumplir hoy en esos reportes porque no hay dato que diga con qué criterio se calculó cada venta.

## 3. Diseño mínimo propuesto (solo documento, sin implementar)

1. **Tabla `CriterioFiscalVersion`**: `Id`, `Codigo` (p. ej. `V1-2026-11`), `VigenteDesde` (fecha de corte), `AlicuotaServicios`, `TratamientoRecargoVentas` (enum: alícuota de ítems / 21 % / aparte), `DescuentoEfectivoReduceBase` (bool), `ModoRedondeo` (truncar / comercial con umbral), `InteresGravado` (bool), `NetearDevolucionesEnIva` (bool). Sembrar la versión 1 con el criterio actual.
2. **Campo `Venta.CriterioFiscalVersionId`** (nullable; `null` = versión 1, el criterio de hoy congelado). Se asigna al crear/confirmar la venta según su fecha. Las devoluciones y notas de crédito usan la versión de la venta original para el reparto y la versión vigente a la fecha de la devolución para el período.
3. **Punto de corte por fecha de la operación**, no por fecha de lectura: una venta del 2026-10 sigue en V1 aunque se lea en 2027.
4. **Qué servicios la leen**: `ReporteService.GenerarReporteIvaAsync`, `FacturaComprobanteBuilder`, `ServiciosVentaIva`, `RecargoMedioPagoIva.Discriminar`, `VentaService` (cálculo y `TruncarAPesos`), `VentasNetas`. Cada uno recibe la versión de la venta en lugar de leer `ProductoIvaResolver.PorcentajeDefault` o constantes.
5. **Alternativa más barata (si solo cambian servicios o alícuota general):** persistir por venta `ServiciosNeto`/`ServiciosIva` (hoy se deducen al leer) y leerlos en reporte y comprobante. Cubre el riesgo más concreto (alícuota de servicios) sin tabla de versiones.

**Plan de migración (no ejecutar):** (1) migración aditiva: tabla + columna nullable, sin backfill de importes; (2) sembrar V1 con `VigenteDesde` anterior a la primera venta; (3) leer `null` como V1 en todos los servicios (compatible hacia atrás, sin reescribir datos); (4) agregar tests de caracterización del Reporte de IVA con ventas V1 antes de introducir V2; (5) implementar V2 solo con autorización por fila, con comparación del Reporte de IVA de un mes antes/después en un clon; (6) aplicar la migración a la base real solo con autorización y respaldo verificado.

## 4. Facturación fiscal (F4.4): verificación contra el código

**Conclusión previa confirmada: comprobantes internos, sin CAE y sin integración con ARCA/AFIP.**

Evidencia (búsqueda en código, vistas, configuración y paquetes de `main`):

- `Factura` tiene `Numero`, `Tipo` (A/B/C/NotaCredito/NotaDebito), `PuntoVenta`, `Subtotal`, `IVA`, `Total` y anulación. **No tiene CAE, vencimiento de CAE, CUIT del emisor, código de barras ni condición frente al IVA.** `CAE`, `WSFE`, `wsaa`, `CodigoBarras` no existen en el código de la aplicación; `AFIP` aparece solo en un comentario sobre prefijos de CUIT de proveedores; `ARCA` no aparece.
- Numeración: `VentaNumberGenerator.GenerarNumeroFacturaAsync` genera `<prefijo>-<AAAAMM>-<correlativo>` desde la configuración de prefijos (`TipoPrefijoNumeracion`), no un número autorizado.
- Paquetes NuGet: ninguno de facturación electrónica. Existe `System.Security.Cryptography.Pkcs` en el `csproj`; no se encontró uso en el código (cs) buscando `Pkcs`, así que no se verificó para qué se incluyó.
- `PuntoVenta` es un texto libre de hasta 5 dígitos que tipea el usuario al facturar.
- `TipoFactura.NotaCredito` y `NotaDebito` existen en el enum y en el generador de prefijos, pero **no hay ningún flujo que emita una `Factura` de esos tipos**: la devolución genera una entidad distinta, `NotaCredito`, interna.
- `EmpresaConfiguracion.Cuit` existe en la configuración de documentos (contratos y recibos), no en el comprobante de factura.

**Diferencias que conviene tener claras:**

| Concepto | Qué es hoy en el ERP |
|---|---|
| Documento interno del ERP | `Factura` (número propio `<prefijo>-<AAAAMM>-<n>`): comprobante operativo; sus importes (neto, IVA, total) son una discriminación informativa. Es la «factura» de la pantalla «Emitir factura». |
| Comprobante fiscal válido | El que emite ARCA/AFIP con CAE (factura electrónica o controlador fiscal). **No existe en el ERP.** |
| Nota de crédito interna | `NotaCredito` (`NC-…`): saldo a favor del cliente utilizable como medio de pago o reembolso; se genera al aprobar la devolución; no mueve Caja; no es un comprobante fiscal. |
| Nota de crédito fiscal | Comprobante con CAE que anula total o parcialmente una factura fiscal. **No existe en el ERP.** |
| Recibo de cobranza | Documento interno generado por el motor documental. |

**Observación para decisión (no se cambió ningún texto legal):** el impreso `Views/Venta/ComprobanteFactura_tw.cshtml` muestra la letra del tipo (A/B/C) en grande, «Nro.», «Punto de venta» y «Fecha», con el título «Comprobante de factura» y el título de la ventana «Factura <tipo> <número>». **No contiene ninguna leyenda de «documento no válido como factura»**, ni CUIT del emisor, ni CAE. Un tercero podría confundirlo con una factura. La pantalla «Emitir factura» habla de «Emisión de comprobante». Se reporta como decisión del propietario (P4 en `docs/iva-decisiones-contador.md`). Además, el código trata una venta Confirmada sin facturar como «informal» (no declara IVA en Caja ni en el Reporte de IVA): conviene que el contador confirme si ese circuito existe a propósito.

**P-02 permanece ABIERTA**: faltan la validación fiscal definitiva del contador, las decisiones del propietario sobre facturación electrónica y autorización por fila.
