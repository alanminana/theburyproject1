-- P-02 / C13-P3: migración P02NotaCreditoFiscalExterna (REVERTIR: elimina las columnas FiscalExterna* y sus datos). Generado con `dotnet ef migrations script` y ajustado:
-- SET XACT_ABORT ON (un error revierte todo) y GO antes del índice (el lote no compila si referencia columnas recién creadas).
-- NO aplicar sin respaldo verificado y sin autorización del propietario. Ver docs/p02-procedimiento-activacion.md.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DROP INDEX [IX_NotasCredito_FiscalExterna] ON [NotasCredito];

DECLARE @var nvarchar(max);
SELECT @var = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[NotasCredito]') AND [c].[name] = N'FiscalExternaCae');
IF @var IS NOT NULL EXEC(N'ALTER TABLE [NotasCredito] DROP CONSTRAINT ' + @var + ';');
ALTER TABLE [NotasCredito] DROP COLUMN [FiscalExternaCae];

DECLARE @var1 nvarchar(max);
SELECT @var1 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[NotasCredito]') AND [c].[name] = N'FiscalExternaFecha');
IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [NotasCredito] DROP CONSTRAINT ' + @var1 + ';');
ALTER TABLE [NotasCredito] DROP COLUMN [FiscalExternaFecha];

DECLARE @var2 nvarchar(max);
SELECT @var2 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[NotasCredito]') AND [c].[name] = N'FiscalExternaImporte');
IF @var2 IS NOT NULL EXEC(N'ALTER TABLE [NotasCredito] DROP CONSTRAINT ' + @var2 + ';');
ALTER TABLE [NotasCredito] DROP COLUMN [FiscalExternaImporte];

DECLARE @var3 nvarchar(max);
SELECT @var3 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[NotasCredito]') AND [c].[name] = N'FiscalExternaNumero');
IF @var3 IS NOT NULL EXEC(N'ALTER TABLE [NotasCredito] DROP CONSTRAINT ' + @var3 + ';');
ALTER TABLE [NotasCredito] DROP COLUMN [FiscalExternaNumero];

DECLARE @var4 nvarchar(max);
SELECT @var4 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[NotasCredito]') AND [c].[name] = N'FiscalExternaPuntoVenta');
IF @var4 IS NOT NULL EXEC(N'ALTER TABLE [NotasCredito] DROP CONSTRAINT ' + @var4 + ';');
ALTER TABLE [NotasCredito] DROP COLUMN [FiscalExternaPuntoVenta];

DECLARE @var5 nvarchar(max);
SELECT @var5 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[NotasCredito]') AND [c].[name] = N'FiscalExternaRegistradaEn');
IF @var5 IS NOT NULL EXEC(N'ALTER TABLE [NotasCredito] DROP CONSTRAINT ' + @var5 + ';');
ALTER TABLE [NotasCredito] DROP COLUMN [FiscalExternaRegistradaEn];

DECLARE @var6 nvarchar(max);
SELECT @var6 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[NotasCredito]') AND [c].[name] = N'FiscalExternaRegistradaPor');
IF @var6 IS NOT NULL EXEC(N'ALTER TABLE [NotasCredito] DROP CONSTRAINT ' + @var6 + ';');
ALTER TABLE [NotasCredito] DROP COLUMN [FiscalExternaRegistradaPor];

DECLARE @var7 nvarchar(max);
SELECT @var7 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[NotasCredito]') AND [c].[name] = N'FiscalExternaTipo');
IF @var7 IS NOT NULL EXEC(N'ALTER TABLE [NotasCredito] DROP CONSTRAINT ' + @var7 + ';');
ALTER TABLE [NotasCredito] DROP COLUMN [FiscalExternaTipo];

DELETE FROM [__EFMigrationsHistory]
WHERE [MigrationId] = N'20261120015427_P02NotaCreditoFiscalExterna';

COMMIT;
GO

