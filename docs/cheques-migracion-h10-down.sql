BEGIN TRANSACTION;
ALTER TABLE [MovimientosCaja] DROP CONSTRAINT [FK_MovimientosCaja_MovimientosCaja_MovimientoOrigenId];

DROP INDEX [IX_MovimientosCaja_MovimientoOrigenId_Concepto] ON [MovimientosCaja];

DECLARE @var nvarchar(max);
SELECT @var = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[MovimientosCaja]') AND [c].[name] = N'MovimientoOrigenId');
IF @var IS NOT NULL EXEC(N'ALTER TABLE [MovimientosCaja] DROP CONSTRAINT ' + @var + ';');
ALTER TABLE [MovimientosCaja] DROP COLUMN [MovimientoOrigenId];

DECLARE @var1 nvarchar(max);
SELECT @var1 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[DatosCheque]') AND [c].[name] = N'AcreditadoAt');
IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [DatosCheque] DROP CONSTRAINT ' + @var1 + ';');
ALTER TABLE [DatosCheque] DROP COLUMN [AcreditadoAt];

DECLARE @var2 nvarchar(max);
SELECT @var2 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[DatosCheque]') AND [c].[name] = N'AcreditadoPor');
IF @var2 IS NOT NULL EXEC(N'ALTER TABLE [DatosCheque] DROP CONSTRAINT ' + @var2 + ';');
ALTER TABLE [DatosCheque] DROP COLUMN [AcreditadoPor];

DECLARE @var3 nvarchar(max);
SELECT @var3 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[DatosCheque]') AND [c].[name] = N'MotivoRechazo');
IF @var3 IS NOT NULL EXEC(N'ALTER TABLE [DatosCheque] DROP CONSTRAINT ' + @var3 + ';');
ALTER TABLE [DatosCheque] DROP COLUMN [MotivoRechazo];

DECLARE @var4 nvarchar(max);
SELECT @var4 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[DatosCheque]') AND [c].[name] = N'RechazadoAt');
IF @var4 IS NOT NULL EXEC(N'ALTER TABLE [DatosCheque] DROP CONSTRAINT ' + @var4 + ';');
ALTER TABLE [DatosCheque] DROP COLUMN [RechazadoAt];

DECLARE @var5 nvarchar(max);
SELECT @var5 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[DatosCheque]') AND [c].[name] = N'RechazadoPor');
IF @var5 IS NOT NULL EXEC(N'ALTER TABLE [DatosCheque] DROP CONSTRAINT ' + @var5 + ';');
ALTER TABLE [DatosCheque] DROP COLUMN [RechazadoPor];

DELETE FROM [__EFMigrationsHistory]
WHERE [MigrationId] = N'20261030030301_H10ChequesRecibidos';

COMMIT;
GO

