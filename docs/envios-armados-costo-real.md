# Envíos y armados: costo real de entrega y de armado

Actualizado 2026-11-05. **Decisión confirmada: alternativa A+B** (costo estándar configurable por tipo de servicio + costo real modificable por venta). Implementado y probado en un clon; **la migración NO está aplicada a la base real** (se autoriza por separado).

## Cuatro conceptos distintos (no confundir)

| Concepto | Dónde vive | Qué es |
|---|---|---|
| 1. Precio que se cobra al cliente por envío | `ServicioVentaPrecio.Precio` (Ciudad/Rural, Configuración de pagos → Envíos y armados); congelado en `VentaEnvio.CostoEnvio` al guardar la venta. | **Ingreso.** A pesar del nombre, `CostoEnvio` es el *precio al cliente*. |
| 2. Gasto de transporte al comprar un producto | `Producto.CostoEnvio` (permiso `productos.editshippingcost`). | Informativo; ya está incluido en `Producto.PrecioCompra`. Costo de **compra**, no de entrega. |
| 3. Costo real de entregar una venta | `VentaEnvio.CostoEstandarServicio` (snapshot) y `VentaEnvio.CostoRealServicio` (cargado a mano). | **Costo** (nuevo). |
| 4. Costo real de realizar un armado | `VentaDetalle.ArmadoCostoUnitarioEstandar` (snapshot por unidad) y `VentaDetalle.ArmadoCostoReal` (total de la línea, cargado a mano). | **Costo** (nuevo). |

## Cómo funciona

- **Costo estándar:** columna `Costo` en `ServiciosVentaPrecios`, editable junto al precio en Configuración de pagos → Envíos y armados (mismo permiso `configuracion.update`). El costo de armado es **por unidad**. "Caja cerrada" no lleva armado ni costo.
- **Snapshot:** al guardar o convertir una venta (Venta/Create y Edit, conversión de cotización) se congela el costo estándar vigente. Cambiar el costo global después **no** altera ventas ya registradas.
- **Costo real por venta:** panel «Costo de envío y armado» en el Detalle de venta (solo con permiso `ventas.update` y si la venta tiene servicios; no en ventas canceladas). Se carga el costo real de entrega y/o el total real de cada armado; vacío = usar el estándar; `0` es un valor válido.
- **Regla central (`Helpers/CostoServiciosVenta`):** el costo real, si existe, **reemplaza** al estándar de ese servicio; **nunca se descuentan ambos**. Ventas anteriores (sin snapshot ni costo real) = costo 0, es decir, **los reportes históricos no cambian**.
- **Ganancia y reportes:** `ReporteService` descuenta el costo de servicios (entrega + armados) del costo de cada venta (reporte de ventas y agrupación por día/mes). El ingreso de envío/armado ya estaba en el ingreso neto; ahora tiene su costo.
- **Seguridad de la carga:** valida todo antes de guardar (sin cambios parciales), rechaza líneas ajenas, importes negativos o mayores a 20.000.000, y rechaza formularios obsoletos (control de versión por fila): si otra persona cambió los costos desde que abriste la pantalla, hay que recargar.
- **Edición de una venta:** el costo real de armado se conserva solo si la línea es inequívocamente la misma (mismo producto, tipo y cantidad, sin duplicados); si cambia o es ambigua, vuelve al estándar. El costo real de entrega se conserva mientras el envío exista.

## Qué NO cambia / límites conocidos

- No se modificó ningún criterio de IVA ni de facturación. El importe cobrado incluye IVA y la ganancia trabaja sobre neto sin IVA; el tratamiento fiscal de envío/armado sigue **pendiente del contador** (`docs/iva-decisiones-contador.md` §2.6).
- Una devolución no recupera el costo de entrega/armado ya incurrido (se mantiene en la ganancia de la venta).
- La agrupación de ventas por **categoría** sigue calculando solo productos (los servicios no pertenecen a una categoría).
- Mercado Libre usa su propio `CostoEnvioEstimado`; no se tocó.
- Ventas existentes: sin costo hasta que se cargue un costo real (o se vuelvan a guardar con el estándar configurado).

## Aplicar a la base real (requiere autorización explícita)

La migración `20261105142232_H12CostoServiciosVenta` es **aditiva** (5 columnas; las obligatorias con `DEFAULT 0`, el resto `NULL`). Verificada sobre un clon de la base real. Script idempotente: `scripts/sql/h12-migracion-costo-servicios-venta.sql`. Pasos previstos: respaldo verificado (`BACKUP … WITH CHECKSUM`, `RESTORE VERIFYONLY`), ensayo en clon, aplicar, y luego cargar los costos estándar en Configuración de pagos.

## Alternativas descartadas

C (gasto operativo vinculado a la venta) y D (solo aclarar el reporte) quedaron descartadas por la decisión A+B.
