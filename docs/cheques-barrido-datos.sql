SET NOCOUNT ON;
PRINT '--- Ventas con TipoPago=Cheque(4) por estado';
SELECT Estado, COUNT(*) n FROM Ventas WHERE TipoPago=4 AND IsDeleted=0 GROUP BY Estado;
PRINT '--- DatosCheque (filas / vigentes)';
SELECT COUNT(*) total, SUM(CASE WHEN IsDeleted=0 THEN 1 ELSE 0 END) vigentes FROM DatosCheque;
PRINT '--- D3: ventas con cheque cuyo DatosCheque.Monto != Total (+CostoEnvio fuera del total) o sin DatosCheque';
SELECT v.Id, v.Numero, v.Estado, v.Total, d.Monto
FROM Ventas v LEFT JOIN DatosCheque d ON d.VentaId=v.Id AND d.IsDeleted=0
WHERE v.TipoPago=4 AND v.IsDeleted=0 AND (d.Id IS NULL OR ABS(d.Monto - v.Total) > 0.01);
PRINT '--- D6: banco+numero duplicado entre ventas vigentes (no canceladas, Estado<>5)';
SELECT d.Banco, d.NumeroCheque, COUNT(*) n
FROM DatosCheque d JOIN Ventas v ON v.Id=d.VentaId
WHERE d.IsDeleted=0 AND v.IsDeleted=0 AND v.Estado<>5
GROUP BY d.Banco, d.NumeroCheque HAVING COUNT(*)>1;
PRINT '--- D6: cheques con emision futura o vencimiento pasado (sin ser a la vista)';
SELECT COUNT(*) FROM DatosCheque d JOIN Ventas v ON v.Id=d.VentaId
WHERE d.IsDeleted=0 AND v.IsDeleted=0 AND (d.FechaEmision > GETUTCDATE() OR (d.FechaVencimiento < CAST(GETUTCDATE() AS date) AND CAST(d.FechaVencimiento AS date) <> CAST(d.FechaEmision AS date)));
PRINT '--- Movimientos de caja de cheque recibido (VentaCheque=2) por estado de acreditacion';
SELECT EstadoAcreditacion, COUNT(*) n, SUM(Monto) total FROM MovimientosCaja WHERE Concepto=2 AND IsDeleted=0 GROUP BY EstadoAcreditacion;
PRINT '--- Cobros de cuota con cheque (Concepto=3, TipoPago=4)';
SELECT EstadoAcreditacion, COUNT(*) n, SUM(Monto) total FROM MovimientosCaja WHERE Concepto=3 AND TipoPago=4 AND IsDeleted=0 GROUP BY EstadoAcreditacion;
PRINT '--- totales de control';
SELECT (SELECT COUNT(*) FROM MovimientosCaja) movimientos, (SELECT COUNT(*) FROM DatosCheque) datoscheque, (SELECT COUNT(*) FROM AperturasCaja) aperturas, (SELECT COUNT(*) FROM Ventas) ventas;
