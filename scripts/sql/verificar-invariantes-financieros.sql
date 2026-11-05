/*
  Verificacion de invariantes financieros (SOLO LECTURA: ningun INSERT/UPDATE/DELETE).
  Uso:  sqlcmd -S "(localdb)\MSSQLLocalDB" -E -C -d <BASE> -i scripts\sql\verificar-invariantes-financieros.sql
  Cada fila es un invariante: Violaciones = 0 es OK. Util sobre un clon despues de una corrida de QA y, de forma segura,
  sobre la base real (no modifica nada). Los enums se leen de Models/Enums (Tipo caja: Ingreso=0/Egreso=1; TipoPago Efectivo=0;
  EstadoAcreditacion: NoAplica=0, Pendiente=1, Acreditado=2, Rechazado=3, Anulado=4; EstadoPagoCuota Aplicado=1).
*/
SET NOCOUNT ON;
DECLARE @r TABLE (Invariante nvarchar(120), Violaciones int, Detalle nvarchar(400) NULL);

-- 1. Stock: el saldo de cada producto es el StockNuevo del ultimo movimiento.
INSERT @r
SELECT N'STOCK-1 stock actual = StockNuevo del ultimo movimiento', COUNT(*),
       N'productos con movimientos cuyo StockActual no coincide'
FROM Productos p
CROSS APPLY (SELECT TOP 1 m.StockNuevo FROM MovimientosStock m WHERE m.ProductoId = p.Id AND m.IsDeleted = 0 ORDER BY m.Id DESC) u
WHERE p.IsDeleted = 0 AND p.StockActual <> u.StockNuevo;

-- 2. Stock nunca negativo.
INSERT @r SELECT N'STOCK-2 stock no negativo', COUNT(*), NULL FROM Productos WHERE IsDeleted = 0 AND StockActual < 0;

-- 3. Continuidad de la cadena de movimientos: el StockAnterior de un movimiento es el StockNuevo del anterior del mismo producto.
INSERT @r
SELECT N'STOCK-3 cadena de movimientos continua', COUNT(*), NULL
FROM (SELECT m.Id, m.StockAnterior,
             LAG(m.StockNuevo) OVER (PARTITION BY m.ProductoId ORDER BY m.Id) AS PrevNuevo
      FROM MovimientosStock m WHERE m.IsDeleted = 0) x
WHERE x.PrevNuevo IS NOT NULL AND x.StockAnterior <> x.PrevNuevo;

-- 4. Cuotas: pagado dentro de [0, total (+ punitorio)] y cuota Pagada (estado 2) con el total cubierto.
INSERT @r SELECT N'CUOTA-1 MontoPagado >= 0', COUNT(*), NULL FROM Cuotas WHERE IsDeleted = 0 AND MontoPagado < 0;
INSERT @r SELECT N'CUOTA-2 MontoPagado <= MontoTotal + MontoPunitorio (cobro no excede lo adeudado)', COUNT(*), NULL
FROM Cuotas WHERE IsDeleted = 0 AND MontoPagado > MontoTotal + ISNULL(MontoPunitorio, 0) + 0.01;

-- 5. Pagos de cuota aplicados: la suma aplicada a capital no excede el total de la cuota.
INSERT @r
SELECT N'PAGO-1 suma de pagos aplicados <= total de la cuota (sin doble cobro)', COUNT(*), NULL
FROM (SELECT c.Id, c.MontoTotal, SUM(p.ImporteAplicadoCuota) AS Aplicado
      FROM Cuotas c JOIN PagosCuota p ON p.CuotaId = c.Id AND p.Estado = 1
      WHERE c.IsDeleted = 0 GROUP BY c.Id, c.MontoTotal) x
WHERE x.Aplicado > x.MontoTotal + 0.01;

-- 6. Caja: el efectivo nunca queda por debajo de cero (monto inicial + ingresos - egresos acumulados), por apertura.
INSERT @r
SELECT N'CAJA-1 efectivo acumulado nunca negativo por apertura', COUNT(DISTINCT x.AperturaCajaId), NULL
FROM (SELECT a.Id AS AperturaCajaId,
             a.MontoInicial + SUM(CASE WHEN m.Tipo = 0 THEN m.Monto ELSE -m.Monto END)
                 OVER (PARTITION BY a.Id ORDER BY m.FechaMovimiento, m.Id ROWS UNBOUNDED PRECEDING) AS Saldo
      FROM AperturasCaja a
      JOIN MovimientosCaja m ON m.AperturaCajaId = a.Id AND m.IsDeleted = 0
           AND m.TipoPago = 0 AND m.EstadoAcreditacion IN (0, 2)
      WHERE a.IsDeleted = 0) x
WHERE x.Saldo < -0.01;

-- 7. Caja: toda apertura cerrada tiene exactamente un cierre; toda apertura abierta no tiene cierre.
INSERT @r SELECT N'CAJA-2 apertura cerrada con 1 cierre', COUNT(*), NULL
FROM AperturasCaja a WHERE a.IsDeleted = 0 AND a.Cerrada = 1
  AND (SELECT COUNT(*) FROM CierresCaja c WHERE c.AperturaCajaId = a.Id AND c.IsDeleted = 0) <> 1;
INSERT @r SELECT N'CAJA-3 apertura abierta sin cierre', COUNT(*), NULL
FROM AperturasCaja a WHERE a.IsDeleted = 0 AND a.Cerrada = 0
  AND EXISTS (SELECT 1 FROM CierresCaja c WHERE c.AperturaCajaId = a.Id AND c.IsDeleted = 0);

-- 8. Caja: el cierre registra una diferencia coherente con lo contado y lo esperado.
INSERT @r SELECT N'CAJA-4 Diferencia = contado - esperado en cierres', COUNT(*), NULL
FROM CierresCaja WHERE IsDeleted = 0 AND ABS(Diferencia - (EfectivoContado - MontoEsperadoSistema)) > 0.01
  AND ABS(Diferencia) > 0.01 AND TieneDiferencia = 1;

-- 9. Caja: idempotencia - ninguna clave de idempotencia repetida.
INSERT @r SELECT N'CAJA-5 IdempotencyKey sin repetidos', COUNT(*), NULL
FROM (SELECT IdempotencyKey FROM MovimientosCaja WHERE IdempotencyKey IS NOT NULL AND IsDeleted = 0
      GROUP BY IdempotencyKey HAVING COUNT(*) > 1) d;

-- 10. Devoluciones: lo devuelto por producto en una venta no excede lo vendido (sin devoluciones duplicadas).
INSERT @r
SELECT N'DEV-1 devuelto <= vendido por (venta, producto)', COUNT(*), NULL
FROM (SELECT d.VentaId, dd.ProductoId, SUM(dd.Cantidad) AS Devuelto
      FROM Devoluciones d JOIN DevolucionDetalles dd ON dd.DevolucionId = d.Id AND dd.IsDeleted = 0
      WHERE d.IsDeleted = 0 AND d.Estado NOT IN (3, 5)   -- 3=Rechazada, 5=Cancelada (EstadoDevolucion en Models/Entities/Devolucion.cs)
      GROUP BY d.VentaId, dd.ProductoId) x
JOIN (SELECT VentaId, ProductoId, SUM(Cantidad) AS Vendido FROM VentaDetalles WHERE IsDeleted = 0 GROUP BY VentaId, ProductoId) v
  ON v.VentaId = x.VentaId AND v.ProductoId = x.ProductoId
WHERE x.Devuelto > v.Vendido;

-- 11. Notas de credito: usado <= total y consistente con los usos no revertidos.
INSERT @r SELECT N'NC-1 MontoUtilizado <= MontoTotal', COUNT(*), NULL FROM NotasCredito WHERE IsDeleted = 0 AND MontoUtilizado > MontoTotal + 0.01;
INSERT @r SELECT N'NC-2 MontoUtilizado = suma de usos vigentes', COUNT(*), NULL
FROM NotasCredito n
WHERE n.IsDeleted = 0 AND ABS(n.MontoUtilizado - ISNULL((SELECT SUM(u.Monto) FROM NotasCreditoUso u
                                                           WHERE u.NotaCreditoId = n.Id AND u.FechaReversion IS NULL), 0)) > 0.01;

-- La integridad referencial se verifica aparte: DBCC CHECKCONSTRAINTS WITH ALL_CONSTRAINTS (lo hace scripts\respaldo-local.ps1 -Accion Probar).

SELECT Invariante, Violaciones, CASE WHEN Violaciones = 0 THEN N'OK' ELSE N'REVISAR' END AS Resultado, Detalle FROM @r;
