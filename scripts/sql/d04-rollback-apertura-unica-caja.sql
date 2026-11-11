/*
  D-04 - REVERSION de los indices de AperturasCaja (inversa de d04-migracion-apertura-unica-caja.sql). No toca ninguna fila.
  Guardas (aborta sin cambiar nada): base correcta; D-04 registrada; D-04 es la ULTIMA migracion registrada (si hay otras posteriores, se aborta:
  revertir solo los indices dejaria un historial incoherente); los indices estan en el estado posterior a D-04.
  Transaccional, serializado, con DryRun=1 (revierte). Requiere QUOTED_IDENTIFIER ON: usar siempre sqlcmd -I.
  Se invoca desde scripts/ops/d04-ventana.ps1 -Modo Rollback (exige token). NO EJECUTAR contra TheBuryProjectDb sin autorizacion expresa.
    sqlcmd -S <servidor> -d <base> -I -b -f 65001 -i d04-rollback-apertura-unica-caja.sql -v DryRun=1 -v BaseEsperada=<base>
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;

DECLARE @DryRun bit = $(DryRun);
DECLARE @Migracion nvarchar(150) = N'20261108185231_D04AperturaCajaUnicaAbierta';
DECLARE @BaseEsperada sysname = N'$(BaseEsperada)';

BEGIN TRANSACTION;

DECLARE @Lock int;
EXEC @Lock = sp_getapplock @Resource = N'd04-migracion-apertura-unica-caja', @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 30000;
IF @Lock < 0
    THROW 51060, 'No se pudo obtener el bloqueo de aplicacion: hay otra ejecucion en curso.', 1;

IF DB_NAME() <> @BaseEsperada
    THROW 51061, 'Base de destino incorrecta. No se modifica nada.', 1;

IF NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId = @Migracion)
BEGIN
    PRINT 'D-04 no esta registrada: no hay nada que revertir.';
    ROLLBACK TRANSACTION;
    RETURN;
END;

IF EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId > @Migracion)
    THROW 51062, 'Hay migraciones posteriores a D-04 registradas: no se revierten solo los indices. No se modifica nada.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.AperturasCaja') AND name = N'UX_AperturasCaja_CajaId_Abierta')
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.AperturasCaja') AND name = N'IX_AperturasCaja_CajaId_Cerrada')
   OR EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.AperturasCaja') AND name = N'IX_AperturasCaja_CajaId')
    THROW 51063, 'Los indices de AperturasCaja no estan en el estado posterior a D-04. No se modifica nada.', 1;

DROP INDEX [UX_AperturasCaja_CajaId_Abierta] ON dbo.AperturasCaja;
DROP INDEX [IX_AperturasCaja_CajaId_Cerrada] ON dbo.AperturasCaja;
CREATE INDEX [IX_AperturasCaja_CajaId] ON dbo.AperturasCaja ([CajaId]);
DELETE FROM dbo.__EFMigrationsHistory WHERE MigrationId = @Migracion;

IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.AperturasCaja') AND name IN (N'UX_AperturasCaja_CajaId_Abierta', N'IX_AperturasCaja_CajaId_Cerrada'))
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.AperturasCaja') AND name = N'IX_AperturasCaja_CajaId')
   OR EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId = @Migracion)
    THROW 51064, 'Verificacion fallida tras la reversion. Se revierte la transaccion.', 1;

PRINT 'D-04: indices revertidos y migracion des-registrada.';
IF @DryRun = 1
BEGIN
    PRINT 'DryRun=1: se revierte.';
    ROLLBACK TRANSACTION;
END
ELSE
BEGIN
    COMMIT TRANSACTION;
    PRINT 'Revertido.';
END;
