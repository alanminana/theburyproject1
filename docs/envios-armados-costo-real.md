# Envíos y armados: costo real de entrega y de armado

Actualizado 2026-11-05. **Decisión confirmada: alternativa A+B** (costo estándar configurable por tipo de servicio + costo real modificable por venta). Implementado y probado; **la migración fue aplicada a la base real el 2026-11-05 con autorización** (ver más abajo). Los costos comienzan en $0.

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
- Ventas existentes: sin costo hasta que se cargue un costo real. Los costos estándar se definen desde la aplicación (Configuración de pagos → Envíos y armados) y rigen para ventas **nuevas**; las ya registradas quedan en $0 salvo que se les cargue costo real.

## Migración aplicada a la base real (2026-11-05, autorizada)

`20261105142232_H12CostoServiciosVenta` se aplicó a `TheBuryProjectDb` con `scripts/sql/h12-migracion-costo-servicios-venta.sql`.

- **Qué agregó (solo esto):** `VentaEnvio.CostoEstandarServicio` y `CostoRealServicio` (`decimal(18,2) NULL`), `VentaDetalles.ArmadoCostoReal` (`NULL`), `VentaDetalles.ArmadoCostoUnitarioEstandar` y `ServiciosVentaPrecios.Costo` (`decimal(18,2) NOT NULL DEFAULT 0`), más el registro en `__EFMigrationsHistory` (134 → 135 migraciones). No modificó ningún valor histórico.
- **Costos:** todos comienzan en **$0**; los costos estándar y reales los carga el usuario desde la aplicación. No se cargó ningún costo en su nombre.
- **Guardas del script** (abortan sin cambiar nada): base distinta de `TheBuryProjectDb`; historial distinto de las 134 migraciones esperadas (faltantes, extras o posteriores); columnas H12 existentes sin migración registrada; migración registrada con columnas ausentes o con tipo/precisión/nulabilidad/default/calculada distintos; tablas inexistentes. Es transaccional, serializado y con ensayo (`DryRun=1`). Reaplicarlo no hace nada.
- **Pruebas previas (clon `TheBuryProjectDb` en una instancia LocalDB aparte):** ensayo, aplicación, reaplicación, y 11 casos negativos (nombre distinto, hueco en el historial, migración extra, columna parcial, migración registrada sin columnas, columna nullable/sin default, tipo `decimal(10,2)`, columna calculada). Compatibilidad: la versión desplegada antes de esta PR (main 3b16fb51) leyó, escribió y reportó igual contra el esquema migrado; un INSERT que omite las columnas nuevas toma el default 0.
- **Revisión independiente (Codex):** 2 pasadas sobre el SQL; 3 hallazgos (historial incompleto, definiciones no validadas, columnas calculadas) corregidos y probados.
- **Verificación posterior:** huella (conteos y checksums de columnas preexistentes) idéntica antes y después en ventas, detalles, envíos, precios de servicios, cuotas, créditos, movimientos de caja, stock de productos, permisos (`RolPermisos`, `AccionesModulo`) y plantillas de documentos.
- **Respaldo previo:** `D:\tmp\h12-previo-migracion-TheBuryProjectDb-<fecha>.bak` (`COPY_ONLY`, `CHECKSUM`, `RESTORE VERIFYONLY` correcto), tomado inmediatamente antes de aplicar.

### Recuperación

1. **Preferida (restaurar el respaldo):** cerrar la aplicación, `ALTER DATABASE TheBuryProjectDb SET SINGLE_USER WITH ROLLBACK IMMEDIATE; RESTORE DATABASE TheBuryProjectDb FROM DISK='<respaldo>' WITH REPLACE;` y volver a multiusuario. Se pierde todo lo registrado después del respaldo.
2. **Revertir solo las columnas** (si todavía no se cargaron costos que se quieran conservar): bloque «Rollback» comentado al final del script (quita las dos restricciones DEFAULT nombradas, las cinco columnas y el registro de la migración). Después hay que volver a una versión de la aplicación sin la PR #230.
3. Ante cualquier duda, primero `DryRun=1` del script para ver en qué estado está el esquema: aborta con un mensaje explícito si algo no coincide.

## Alternativas descartadas

C (gasto operativo vinculado a la venta) y D (solo aclarar el reporte) quedaron descartadas por la decisión A+B.
