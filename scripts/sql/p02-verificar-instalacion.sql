-- P-02 / P6 y P3: verificación SOLO LECTURA de una base SQL Server antes y después de activar CriterioFiscal V2 / aplicar P02NotaCreditoFiscalExterna.
-- No modifica datos del ERP (solo lee; usa la tabla temporal #r de tempdb). Ejecutar con sqlcmd -I contra la base a verificar. Ver docs/p02-procedimiento-activacion.md.
--
--   @PuestaEnMarcha: día y hora UTC en que arranca la operación real de la instalación nueva. Con NULL solo se informan conteos.
--
-- Cada fila de resultado es «control | valor | esperado | estado». Cualquier estado distinto de OK/INFO detiene el procedimiento.
SET NOCOUNT ON;
DECLARE @PuestaEnMarcha datetime2 = NULL;   -- ej. '2026-12-01T03:00:00' (inicio del día comercial argentino en UTC)

IF OBJECT_ID(N'tempdb..#r') IS NOT NULL DROP TABLE #r;
CREATE TABLE #r (n int identity(1,1), control nvarchar(160), valor nvarchar(200), esperado nvarchar(160), estado nvarchar(10));
DECLARE @cols int = (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'NotasCredito') AND name LIKE N'FiscalExterna%');
DECLARE @idx int = (SELECT COUNT(*) FROM sys.indexes WHERE name = N'IX_NotasCredito_FiscalExterna');

-- 1. Esquema -------------------------------------------------------------------------------------------------------------------
INSERT #r SELECT N'Migraciones aplicadas', CAST(COUNT(*) AS nvarchar(20)), N'(informativo)', N'INFO' FROM __EFMigrationsHistory;
INSERT #r SELECT N'Última migración', MAX(MigrationId), N'20261120015427_P02NotaCreditoFiscalExterna (con P3) o 20261118172846_D03_… (sin P3)', N'INFO' FROM __EFMigrationsHistory;
INSERT #r VALUES (N'P3: columnas FiscalExterna* en NotasCredito', CAST(@cols AS nvarchar(20)), N'8 si P3 aplicada; 0 si no', N'INFO');
INSERT #r VALUES (N'P3: índice único IX_NotasCredito_FiscalExterna', CAST(@idx AS nvarchar(20)), N'1 si P3 aplicada; 0 si no', N'INFO');
INSERT #r VALUES (N'P3: esquema coherente (8 columnas e índice, o ninguno)',
       CASE WHEN @cols = 8 AND @idx = 1 THEN N'aplicada' WHEN @cols = 0 AND @idx = 0 THEN N'no aplicada' ELSE N'INCONSISTENTE' END,
       N'aplicada o no aplicada',
       CASE WHEN (@cols = 8 AND @idx = 1) OR (@cols = 0 AND @idx = 0) THEN N'OK' ELSE N'FALLA' END);

-- 2. Instalación limpia (antes de activar V2): no deben existir operaciones históricas importadas --------------------------------
INSERT #r SELECT N'Ventas totales', CAST(COUNT(*) AS nvarchar(20)), N'0 antes de la puesta en marcha', N'INFO' FROM Ventas;
INSERT #r SELECT N'Créditos totales', CAST(COUNT(*) AS nvarchar(20)), N'0 antes de la puesta en marcha', N'INFO' FROM Creditos;
INSERT #r SELECT N'Movimientos de caja totales', CAST(COUNT(*) AS nvarchar(20)), N'0 antes de la puesta en marcha', N'INFO' FROM MovimientosCaja;
INSERT #r SELECT N'Facturas totales', CAST(COUNT(*) AS nvarchar(20)), N'0 antes de la puesta en marcha', N'INFO' FROM Facturas;
INSERT #r VALUES (N'Ventas anteriores a la puesta en marcha',
       CASE WHEN @PuestaEnMarcha IS NULL THEN N'(sin fecha)' ELSE CAST((SELECT COUNT(*) FROM Ventas WHERE FechaVenta < @PuestaEnMarcha) AS nvarchar(20)) END, N'0',
       CASE WHEN @PuestaEnMarcha IS NULL THEN N'INFO' WHEN (SELECT COUNT(*) FROM Ventas WHERE FechaVenta < @PuestaEnMarcha) = 0 THEN N'OK' ELSE N'FALLA' END);

-- 3. Versión fiscal de las operaciones -------------------------------------------------------------------------------------------
INSERT #r SELECT N'Ventas con versión fiscal ' + CAST(CriterioFiscalVersion AS nvarchar(10)), CAST(COUNT(*) AS nvarchar(20)),
       N'solo versiones 1 y 2', CASE WHEN CriterioFiscalVersion IN (1, 2) THEN N'INFO' ELSE N'FALLA' END
FROM Ventas GROUP BY CriterioFiscalVersion;
INSERT #r SELECT N'Fecha efectiva de V2 (primera venta con versión 2, UTC)', ISNULL(CONVERT(nvarchar(30), MIN(FechaVenta), 126), N'(aún sin ventas V2)'),
       N'= puesta en marcha', N'INFO' FROM Ventas WHERE CriterioFiscalVersion = 2;
INSERT #r SELECT N'Ventas V2 anteriores a la puesta en marcha (V2 nunca retroactiva)',
       CASE WHEN @PuestaEnMarcha IS NULL THEN N'(sin fecha)' ELSE CAST((SELECT COUNT(*) FROM Ventas WHERE CriterioFiscalVersion = 2 AND FechaVenta < @PuestaEnMarcha) AS nvarchar(20)) END,
       N'0', CASE WHEN @PuestaEnMarcha IS NULL THEN N'INFO' WHEN (SELECT COUNT(*) FROM Ventas WHERE CriterioFiscalVersion = 2 AND FechaVenta < @PuestaEnMarcha) = 0 THEN N'OK' ELSE N'FALLA' END;
INSERT #r SELECT N'Ventas posteriores a la puesta en marcha con versión 1 (tras activar V2)',
       CASE WHEN @PuestaEnMarcha IS NULL THEN N'(sin fecha)' ELSE CAST((SELECT COUNT(*) FROM Ventas WHERE CriterioFiscalVersion = 1 AND FechaVenta >= @PuestaEnMarcha) AS nvarchar(20)) END,
       N'0 si V2 ya está activa', N'INFO';

-- 4. Consistencia de importes (V2): Neto + IVA = Total cobrado; factura = venta ----------------------------------------------------
INSERT #r SELECT N'Ventas V2 con Subtotal + IVA <> Total', CAST(COUNT(*) AS nvarchar(20)), N'0', CASE WHEN COUNT(*) = 0 THEN N'OK' ELSE N'FALLA' END
FROM Ventas WHERE CriterioFiscalVersion = 2 AND Estado IN (2, 3, 4) AND ABS(Subtotal + IVA - Total) > 0.005;
INSERT #r SELECT N'Facturas vigentes con total distinto del de su venta', CAST(COUNT(*) AS nvarchar(20)), N'0', CASE WHEN COUNT(*) = 0 THEN N'OK' ELSE N'FALLA' END
FROM Facturas f JOIN Ventas v ON v.Id = f.VentaId WHERE f.IsDeleted = 0 AND f.Anulada = 0 AND ABS(f.Total - v.Total) > 0.005;
INSERT #r SELECT N'Facturas con Subtotal + IVA <> Total', CAST(COUNT(*) AS nvarchar(20)), N'0', CASE WHEN COUNT(*) = 0 THEN N'OK' ELSE N'FALLA' END
FROM Facturas WHERE IsDeleted = 0 AND ABS(Subtotal + IVA - Total) > 0.005;
IF @cols = 8
BEGIN
    EXEC(N'INSERT #r SELECT N''Notas de crédito fiscales externas con importe > NC interna'', CAST(COUNT(*) AS nvarchar(20)), N''0'',
           CASE WHEN COUNT(*) = 0 THEN N''OK'' ELSE N''FALLA'' END
           FROM NotasCredito WHERE FiscalExternaImporte IS NOT NULL AND FiscalExternaImporte > MontoTotal + 0.01;
           INSERT #r SELECT N''Notas de crédito con vínculo fiscal externo'', CAST(COUNT(*) AS nvarchar(20)), N''(informativo)'', N''INFO''
           FROM NotasCredito WHERE FiscalExternaNumero IS NOT NULL;');
END

SELECT control, valor, esperado, estado FROM #r ORDER BY n;
