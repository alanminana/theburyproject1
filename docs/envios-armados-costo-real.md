# Envíos y armados: ¿existe el costo real? (investigación 2026-11-05)

Solo investigación; **no hay cambios de modelo ni migraciones**. Las alternativas esperan decisión del dueño.

## Qué existe hoy (cuatro conceptos distintos)

| Concepto | Dónde vive | Qué es |
|---|---|---|
| 1. Precio que se cobra al cliente por envío | `ServicioVentaPrecio` (Ciudad/Rural, configurable en Configuración de pagos → Envíos y armados); congelado en `VentaEnvio.CostoEnvio` al guardar la venta. | **Ingreso.** A pesar del nombre, `CostoEnvio` es el *precio al cliente*. |
| 2. Gasto de transporte al comprar un producto | `Producto.CostoEnvio` (permiso `productos.editshippingcost`). | Informativo; ya está incluido en `Producto.PrecioCompra`. Es costo de **compra**, no de entrega. |
| 3. Costo real de entregar una venta | **No existe.** `VentaEnvio` solo tiene `Transportista` (texto libre), seguimiento, fechas y estado. | — |
| 4. Costo real de realizar un armado | **No existe.** `VentaDetalle` guarda `TipoArmado`, `ArmadoPrecioUnitario` y `ArmadoSubtotal` (precio cobrado). | — |

Otros hallazgos:

- `ServicioVentaPrecio` tiene solo `Precio` y `Activo`: no hay columna de costo.
- Reportes (`ReporteService`, `VentasNetas.CalcularIngresoNeto`): ganancia = ingreso neto de la venta (que **incluye** envío y armados como ingreso, IVA 21 %) − `CostoTotalAlMomento` de los productos. Envío y armado entran como ingreso sin costo, es decir con **margen implícito del 100 %**: la ganancia de las ventas con servicios está sobreestimada.
- Mercado Libre tiene `CostoEnvioEstimado` (costo que absorbe el vendedor en esa plataforma); es un concepto aparte, usado solo en el precio mínimo de ML. No se tocó.
- No hay hoy un registro de gastos operativos asociados a una venta.

## Qué falta

Un dato de **costo** del servicio (lo que nos cuesta hacer la entrega o el armado) y su resta en la ganancia, separado del precio cobrado.

## Alternativas (a decidir)

**A — Costo estándar por tipo de servicio (recomendada, la más simple).** Agregar `Costo` a `ServicioVentaPrecio` (Envío Ciudad/Rural, Armado 1..6), editable en la misma pantalla donde hoy se cargan los precios. Al guardar la venta se congela un snapshot (`VentaEnvio.CostoRealEnvio`, `VentaDetalle.ArmadoCostoUnitario`), igual que hoy con el precio. El reporte de ganancia resta ese costo y muestra la ganancia de servicios por separado. Requiere migración (3 columnas) y un ajuste de `ReporteService`. Ventas históricas: costo 0 (comportamiento actual, sin recalcular).
Ventaja: sin carga manual por venta; consistente con el modelo actual. Limitación: es un promedio, no el costo exacto de cada entrega.

**B — Costo real cargado por venta.** Al despachar o entregar, el usuario ingresa lo que costó el flete o el armado. Exacto, pero agrega un paso operativo y puede quedar sin cargar. Se puede combinar con A (A como valor sugerido, B como corrección).

**C — Gasto operativo vinculado a la venta.** Registrar el costo como egreso de Caja/gasto con `VentaId`. Evita tocar `VentaEnvio`/`VentaDetalle`, pero necesita un concepto de gasto vinculado que hoy no existe y complica los reportes.

**D — No cambiar el modelo y aclarar el reporte.** Mostrar envío/armado como ingreso aparte y avisar que la ganancia no los descuenta. Riesgo cero, pero no resuelve el cálculo.

## Decisión que se necesita

¿A (costo estándar), A+B (estándar + ajuste real) o D mientras tanto? Conviene además saber con el contador si el envío/armado se factura gravado (ver `docs/iva-decisiones-contador.md` §2.6), porque cambia la base del margen.
