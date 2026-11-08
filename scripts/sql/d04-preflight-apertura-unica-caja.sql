/*
  D-04 - PREFLIGHT de solo lectura (no modifica nada). Ejecutar con la aplicacion detenida, ANTES del respaldo y de la aplicacion:
    sqlcmd -S <servidor> -d TheBuryProjectDb -I -b -f 65001 -i d04-preflight-apertura-unica-caja.sql
  Para poder aplicar: BaseOk=1, Migraciones=136 con ultima 20261106072023_H01NumeroOrigenVenta, D04Registrada=0, CajasConDuplicadas=0,
  indices previos = IX_AperturasCaja_CajaId + PK, y 0 conexiones ajenas.
*/
SET NOCOUNT ON;
SELECT DB_NAME() AS Base, CASE WHEN DB_NAME() = N'TheBuryProjectDb' THEN 1 ELSE 0 END AS BaseOk;
SELECT COUNT(*) AS Migraciones, MAX(MigrationId) AS Ultima,
       SUM(CASE WHEN MigrationId = N'20261108185231_D04AperturaCajaUnicaAbierta' THEN 1 ELSE 0 END) AS D04Registrada
FROM dbo.__EFMigrationsHistory;
SELECT COUNT(*) AS Aperturas, SUM(CASE WHEN Cerrada = 0 AND IsDeleted = 0 THEN 1 ELSE 0 END) AS Vigentes FROM dbo.AperturasCaja;
SELECT COUNT(*) AS CajasConDuplicadas FROM (SELECT CajaId FROM dbo.AperturasCaja WHERE Cerrada = 0 AND IsDeleted = 0 GROUP BY CajaId HAVING COUNT(*) > 1) d;
SELECT name AS IndicePrevio, is_unique, filter_definition FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.AperturasCaja') AND name IS NOT NULL ORDER BY name;
SELECT COUNT(*) AS ConexionesAjenas FROM sys.dm_exec_sessions WHERE database_id = DB_ID() AND session_id <> @@SPID AND is_user_process = 1;
SELECT (SELECT COUNT(*) FROM dbo.Cajas) AS Cajas, (SELECT COUNT(*) FROM dbo.MovimientosCaja) AS MovimientosCaja,
       (SELECT COUNT(*) FROM dbo.Ventas) AS Ventas, (SELECT COUNT(*) FROM dbo.Cuotas) AS Cuotas, (SELECT COUNT(*) FROM dbo.Creditos) AS Creditos;
