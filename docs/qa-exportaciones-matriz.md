# QA de exportaciones Excel / PDF / CSV — matriz de validación (2026-11-07)

Alcance: todos los exportadores que existen en la aplicación. Se abrió cada archivo con herramientas ajenas al código
que lo genera (openpyxl, `pdftotext`) y se contrastó contra consultas SQL directas sobre SQL Server real (LocalDB,
catálogo descartable `qa_export_20261107`, creado con las migraciones; la base real `TheBuryProjectDb` no se tocó).
No se modificó ninguna regla comercial ni fiscal, ni roles, permisos o scripts de seguridad.

## Cómo se reproduce

```powershell
$env:QA_EXPORT_SQL = 'Server=(localdb)\MSSQLLocalDB;Database=qa_export_<fecha>;Trusted_Connection=True;TrustServerCertificate=True'
$env:QA_EXPORT_OUT = '<carpeta de salida>'
dotnet test TheBuryProyect.Tests --filter "FullyQualifiedName~ExportacionesReporteConsistenciaTests"
python scripts/qa/verificar-exportaciones.py <carpeta de salida> qa_export_<fecha>   # contraste externo (SQL + openpyxl + pdftotext)
```

Sin `QA_EXPORT_SQL` las mismas pruebas corren en SQLite en memoria (es lo que ejecuta CI). El catálogo debe empezar con
`qa_export` (protección contra la base real). Al terminar: `DROP DATABASE qa_export_<fecha>`.

## Escenario sembrado (noviembre 2026)

62 ventas comerciales en noviembre entre Facturada/Entregada/Confirmada (efectivo, débito con recargo $12,10, descuento
comercial $20, crédito personal), 55 de relleno (supera el tope histórico de 50 filas del PDF), una venta cancelada,
un presupuesto y una venta eliminada (no deben aparecer), límites exactos `01/11 00:00:00` y `30/11 23:59:59,9999999`
(entran), `31/10 23:59:59` y `01/12 00:00:00` (no entran), un cliente activo y otro **dado de baja después de operar**
(su histórico se conserva), 6 devoluciones (parcial completada, total aprobada, de una venta de octubre devuelta en
noviembre, rechazada, pendiente y de venta cancelada: las tres últimas no restan), morosidad con pago parcial y cliente
dado de baja, movimientos de stock valorizados dentro y fuera del período.

## Matriz

| # | Reporte | Formato | Filtros ejercitados | Importes contrastados con SQL | Resultado | Defecto |
|---|---------|---------|---------------------|-------------------------------|-----------|---------|
| 1 | Ventas (`Reporte/ExportarVentasExcel`) | xlsx | período (nov, oct, vacío, límites), cliente, tipo de pago, con y sin `ventas.discount` | cantidad de filas (62), Σ Total (6.944,10), Σ IVA (1.205,43), Σ Subtotal neto (5.738,67), Σ Descuento (20), Total = IVA + Subtotal en cada fila, fila TOTALES, Devoluciones del período (−363), NETO (6.581,10) | OK | — |
| 2 | Ventas (`Reporte/ExportarVentasPdf`) | pdf | período, vacío | filas de detalle = filas del Excel (62), resumen: ventas brutas, devoluciones y ventas netas = SQL / Excel | OK tras corrección | **D-1** truncaba a 50 filas |
| 3 | Morosidad (`Reporte/ExportarMorosidadExcel`) | xlsx | sin filtros | Σ deuda vencida (1.100), clientes (2, incluido el dado de baja), documento con ceros a la izquierda como texto, teléfono como texto | OK tras corrección | **D-2** columnas Mora/Gracia sin formato monetario |
| 4 | Morosidad (`Reporte/ExportarMorosidadPdf`) | pdf | sin filtros | Deuda Vencida = SQL, lista a los dos clientes | OK | — |
| 5 | Márgenes (`Reporte/ExportarMargenesExcel`) | xlsx | categoría / marca (código del servicio) | una fila por producto no eliminado; código con ceros como texto; ganancia 40 y margen 40 % sobre precio neto | OK | — |
| 6 | Movimientos valorizados (`Reporte/ExportarMovimientosValorizadosExcel`) | xlsx | período | 3 filas del período (el de diciembre queda fuera), Σ costo total (900), fila TOTALES | OK | — |
| 7 | Devoluciones (`Devolucion/Index?exportCsv=true`) | csv | pestaña, búsqueda, estado, resolución (usa la lista filtrada, no la página) | una fila por devolución (6), Σ Monto (726), cliente dado de baja con su nombre | OK tras corrección | **D-3** sin BOM UTF-8; **D-4** DNI repetido en la columna Cliente |
| 8 | Garantías (`Devolucion/Index?tab=garantias&exportCsv=true`) | csv | ídem | generación cubierta por pruebas unitarias (BOM, tildes, DNI, neutralización de fórmulas CWE-1236) | OK tras corrección | D-3, D-4 |
| 9 | Cotización (`Cotizacion/DescargarPdf`) | pdf | con y sin `ventas.discount` | Subtotal, Descuento total, Armados, Total base y TOTAL = datos de la cotización; aritmética Subtotal − Descuento + Armados + Envío = Total base; sin permiso se oculta el descuento sin cambiar el TOTAL | OK | — |
| 10 | Historial de precios (`PrecioService.ExportarHistorialPreciosAsync`) | xlsx | — | **sin endpoint ni vista que lo invoque**; solo servicio con 8 pruebas propias | NO ALCANZABLE desde la aplicación | — |
| 11 | Contrato de venta a crédito / Documentos (`ContratoVentaCredito`, `Documento`) | pdf | — | documentos emitidos por plantilla con numeración propia (sistema documental); no son reportes, validados por sus suites | FUERA DE ALCANCE | — |

Observaciones que no se tocaron (decisión del propietario o fuera de alcance):

* **IVA y devoluciones**: la columna IVA del Excel de Ventas suma el IVA de las ventas del período y **no** netea el IVA de
  las devoluciones (la fila "Devoluciones del período" solo resta el Total y la Ganancia). Coincide con el criterio
  implementado y con `docs/iva-decisiones-contador.md` (§2.5, pendiente del contador); no se modificó.
* **Separador decimal del CSV**: importes con punto (`1500.50`, cultura invariante), contrato fijado por una prueba existente.
  Con Excel en configuración regional es-AR el punto no se reconoce como decimal. Decisión pendiente (cambiar a coma
  rompería a quien lo consuma por programa).
* **Reporte de IVA** (`GenerarReporteIvaAsync`): no tiene exportación a archivo.
* **Ventas, columna Tipo Pago**: muestra el nombre interno del enumerado (`TarjetaDebito`), igual que la pantalla.
* **Crédito/CuotasVencidas**: el botón "Exportar" ejecuta `window.print()` (imprime la página; no genera archivo).
* **Ventas, enlaces de exportación**: no propagan `MarcaId`, pero la pantalla no tiene ese filtro, así que no es alcanzable.
* **Fechas del Excel de Ventas**: usan el formato de fecha-hora regional de Excel (formato incorporado 22), no uno fijo.
  Las fechas siguen en UTC (punto 6 de `docs/reportes-ganancia-devoluciones.md`, ya registrado).

## Defectos corregidos

| ID | Defecto | Causa | Corrección | Regresión |
|----|---------|-------|------------|-----------|
| D-1 | El PDF de Ventas listaba solo las primeras 50 ventas mientras su encabezado sumaba todas y el Excel las listaba completas (62 filas esperadas, 50 en el archivo) | `reporte.Ventas.Take(50)` en `GenerarVentasPdfAsync` | `ReporteService.FilasDelPdfDeVentas` devuelve todas las filas (la tabla pagina sola) | `VentasPdf_SeGeneraYContieneTodasLasVentasDelPeriodo` + `scripts/qa/verificar-exportaciones.py` cuenta las filas del PDF con `pdftotext` |
| D-2 | Excel de Morosidad: "Deuda en Mora" y "Deuda en Gracia" salían sin formato monetario | el formato solo cubría `E:F` | formato `$#,##0.00` también en `I:J` | `MorosidadExcel_ContrastaConSql_…` |
| D-3 | CSV de Devoluciones y Garantías sin BOM: Excel lo abre como ANSI y rompe tildes y eñes (clientes, productos, motivos) | `Encoding.UTF8.GetBytes` no emite preámbulo | `CsvConBom` antepone el BOM UTF-8 | `GenerarCsv_EmpiezaConBomUtf8_YConservaTildesYEnies` |
| D-4 | CSV de Devoluciones y Garantías: la columna Cliente traía `Apellido, Nombre - DNI: n` y la siguiente columna repetía el documento | uso de `ToDisplayName()` (mismo defecto ya corregido en el Excel de Ventas y Morosidad) | `NombreClienteCsv` → `Apellido, Nombre` | `GenerarCsv_ClienteSinDniRepetido_ElDocumentoVaSoloEnSuColumna` |

D-1 y D-2 se reprodujeron fallando antes de corregir (62 vs 50 filas; formato vacío); D-3 y D-4 se reprodujeron
revirtiendo temporalmente `DevolucionService.cs` (BOM `[73,100,59]` en lugar de `[239,187,191]`; "DNI:" presente).

## Resultado de la corrida

* `ExportacionesReporteConsistenciaTests`: 16/16 en SQLite y 16/16 en SQL Server real (LocalDB).
* `scripts/qa/verificar-exportaciones.py`: todos los contrastes OK (SQL ↔ Excel ↔ PDF ↔ CSV).
* Pruebas relacionadas (`Reporte*`, `DevolucionServiceFilterExportTests`, `PrecioServiceExportTests`): 171/171.
