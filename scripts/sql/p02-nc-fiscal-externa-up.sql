-- P-02 / C13-P3: migración P02NotaCreditoFiscalExterna (aplicar). Generado con `dotnet ef migrations script` y ajustado:
-- SET XACT_ABORT ON (un error revierte todo) y GO antes del índice (el lote no compila si referencia columnas recién creadas).
-- NO aplicar sin respaldo verificado y sin autorización del propietario. Ver docs/p02-procedimiento-activacion.md.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
ALTER TABLE [NotasCredito] ADD [FiscalExternaCae] nvarchar(14) NULL;

ALTER TABLE [NotasCredito] ADD [FiscalExternaFecha] datetime2 NULL;

ALTER TABLE [NotasCredito] ADD [FiscalExternaImporte] decimal(18,2) NULL;

ALTER TABLE [NotasCredito] ADD [FiscalExternaNumero] nvarchar(8) NULL;

ALTER TABLE [NotasCredito] ADD [FiscalExternaPuntoVenta] nvarchar(5) NULL;

ALTER TABLE [NotasCredito] ADD [FiscalExternaRegistradaEn] datetime2 NULL;

ALTER TABLE [NotasCredito] ADD [FiscalExternaRegistradaPor] nvarchar(256) NULL;

ALTER TABLE [NotasCredito] ADD [FiscalExternaTipo] nvarchar(3) NULL;

GO

CREATE UNIQUE INDEX [IX_NotasCredito_FiscalExterna] ON [NotasCredito] ([FiscalExternaPuntoVenta], [FiscalExternaTipo], [FiscalExternaNumero]) WHERE [FiscalExternaNumero] IS NOT NULL;

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261120015427_P02NotaCreditoFiscalExterna', N'10.0.13');

COMMIT;
GO

