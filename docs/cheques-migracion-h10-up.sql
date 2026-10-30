BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030030301_H10ChequesRecibidos'
)
BEGIN
    ALTER TABLE [MovimientosCaja] ADD [MovimientoOrigenId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030030301_H10ChequesRecibidos'
)
BEGIN
    ALTER TABLE [DatosCheque] ADD [AcreditadoAt] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030030301_H10ChequesRecibidos'
)
BEGIN
    ALTER TABLE [DatosCheque] ADD [AcreditadoPor] nvarchar(50) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030030301_H10ChequesRecibidos'
)
BEGIN
    ALTER TABLE [DatosCheque] ADD [MotivoRechazo] nvarchar(500) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030030301_H10ChequesRecibidos'
)
BEGIN
    ALTER TABLE [DatosCheque] ADD [RechazadoAt] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030030301_H10ChequesRecibidos'
)
BEGIN
    ALTER TABLE [DatosCheque] ADD [RechazadoPor] nvarchar(50) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030030301_H10ChequesRecibidos'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_MovimientosCaja_MovimientoOrigenId_Concepto] ON [MovimientosCaja] ([MovimientoOrigenId], [Concepto]) WHERE [MovimientoOrigenId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030030301_H10ChequesRecibidos'
)
BEGIN
    ALTER TABLE [MovimientosCaja] ADD CONSTRAINT [FK_MovimientosCaja_MovimientosCaja_MovimientoOrigenId] FOREIGN KEY ([MovimientoOrigenId]) REFERENCES [MovimientosCaja] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030030301_H10ChequesRecibidos'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261030030301_H10ChequesRecibidos', N'10.0.13');
END;

COMMIT;
GO

