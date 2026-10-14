// Escaneo de integridad global sobre BuryQA_PRD (solo lectura).
const db = require('./lib/db');
const out = {};
out.stockNegativo = db.num('SELECT COUNT(*) FROM Productos WHERE StockActual<0');
out.precioNegativo = db.num('SELECT COUNT(*) FROM Productos WHERE PrecioVenta<0 OR PrecioCompra<0');
out.stockVsUltimoMovimiento = db.rows(`SELECT p.Codigo,p.StockActual,m.StockNuevo FROM Productos p CROSS APPLY (SELECT TOP 1 StockNuevo FROM MovimientosStock WHERE ProductoId=p.Id ORDER BY Id DESC) m WHERE p.StockActual<>m.StockNuevo`);
out.cadenasRotas = db.rows(`SELECT m.ProductoId,m.Id FROM MovimientosStock m CROSS APPLY (SELECT TOP 1 StockNuevo FROM MovimientosStock x WHERE x.ProductoId=m.ProductoId AND x.Id<m.Id ORDER BY x.Id DESC) pr WHERE m.StockAnterior<>pr.StockNuevo`);
out.primerMovNoCero = db.rows(`SELECT m.ProductoId,m.Id,m.StockAnterior FROM MovimientosStock m WHERE m.Id=(SELECT MIN(Id) FROM MovimientosStock WHERE ProductoId=m.ProductoId) AND m.StockAnterior<>0`);
out.productosSinMovYConStock = db.num(`SELECT COUNT(*) FROM Productos p WHERE p.StockActual>0 AND NOT EXISTS (SELECT 1 FROM MovimientosStock m WHERE m.ProductoId=p.Id)`);
out.codigosDuplicadosNormalizados = db.rows(`SELECT UPPER(LTRIM(RTRIM(Codigo))),COUNT(*) FROM Productos WHERE IsDeleted=0 GROUP BY UPPER(LTRIM(RTRIM(Codigo))) HAVING COUNT(*)>1`);
out.eventosSinDetalle = db.num(`SELECT COUNT(*) FROM CambioPrecioEventos e WHERE e.IsDeleted=0 AND NOT EXISTS (SELECT 1 FROM CambioPrecioDetalles d WHERE d.EventoId=e.Id)`);
out.movimientosQA = db.num(`SELECT COUNT(*) FROM MovimientosStock m JOIN Productos p ON p.Id=m.ProductoId WHERE p.Codigo LIKE 'QAPRD%'`);
out.productosQA = db.num(`SELECT COUNT(*) FROM Productos WHERE Codigo LIKE 'QAPRD%' OR LTRIM(Codigo) LIKE 'QAPRD%'`);
console.log(JSON.stringify(out, null, 1));
