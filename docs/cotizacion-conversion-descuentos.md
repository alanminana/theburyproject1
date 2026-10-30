# Conversión de cotización con descuentos (HITO9-N2)

Estado: defecto de dinero ALCANZABLE desde el flujo real; corregido.

## Hallazgo
Una cotización con descuento de línea en % y/o descuento general, al convertirse en venta, perdía ambos
descuentos y la venta salía más cara que lo cotizado.

## Evidencia (clon de la base, solo UI/HTTP real, sin SQL de datos)
Cotización guardada con `POST /api/cotizacion/guardar` (la misma que usa el simulador): 2 x producto 1 con 10 %
de línea + 1 x producto 2 + descuento general $5.000, pago Efectivo (-5 %).

| Paso | Resultado |
|---|---|
| Persistencia (`CotizacionService.CrearAsync`) | `CotizacionDetalle.DescuentoPorcentajeSnapshot = 10`, `DescuentoImporteSnapshot = NULL`; el general sólo queda dentro de `Cotizacion.DescuentoTotal` (41.499,80 = línea + general); `Subtotal` de línea ya descontado |
| Pantalla/PDF | Total cotizado `TotalSeleccionado` = **$442.985,19** |
| Conversión (antes) | `Venta.Total` = **$482.410,00**, `Venta.Descuento` = 0, líneas sin descuento (+$39.424,81) |
| Conversión (después) | `Venta.Total` = **$442.985,19**, `Venta.Descuento` = 5.000, prorrateado en las líneas |

Permiso `ventas.discount` (PR #99):
- Sin permiso (rol Vendedor) no se puede guardar una cotización con descuento: 403 en `guardar` y `simular`.
- Sin permiso, convertir una cotización con descuento de línea ya se bloqueaba con mensaje explícito.
- Brecha: con descuento SOLO general el bloqueo no aplicaba (se miraba únicamente el snapshot de líneas) y la
  venta salía sin el descuento (cotizado $477.660,00 -> venta $482.410,00).

## Causa raíz
`CotizacionConversionService.ConstruirDetalles` usaba sólo `DescuentoImporteSnapshot` y `venta.Descuento = 0`.
El test "por diseño" sólo documentaba ese comportamiento (el doc `cotiz-qa-2` ya lo marcaba como
"puede sorprender"); no había una decisión de negocio de recalcular. El cotizador suma % (sobre el bruto de
línea) + importe, y el general no se guarda como campo propio.

## Corrección
- Línea: descuento = importe + round(bruto x % / 100) (misma fórmula de `CotizacionPagoCalculator`).
- General: se deriva de lo persistido (`sum(Subtotal de líneas) - (Subtotal - DescuentoTotal)`), se prorratea
  en las líneas (`DescuentoGeneralProrrateado`, `SubtotalFinal*`, igual que Venta/Create) y queda en
  `Venta.Descuento`; `Total = Subtotal - Descuento`. Se limita al total de productos.
- Permiso: la conversión también exige `ventas.discount` cuando hay descuento general (mensaje explícito, no se crea venta).
- Sin cambios de esquema ni migraciones.

## Decisión de negocio que queda abierta
Con "usar precio actual" (`UsarPrecioCotizado = false`) el % se aplica sobre el precio actual y el general
queda como importe fijo. Alternativa: congelar siempre el importe cotizado. No se cambió.
