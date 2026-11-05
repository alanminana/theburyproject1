BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261105142232_H12CostoServiciosVenta'
)
BEGIN
    ALTER TABLE [VentaEnvio] ADD [CostoEstandarServicio] decimal(18,2) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261105142232_H12CostoServiciosVenta'
)
BEGIN
    ALTER TABLE [VentaEnvio] ADD [CostoRealServicio] decimal(18,2) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261105142232_H12CostoServiciosVenta'
)
BEGIN
    ALTER TABLE [VentaDetalles] ADD [ArmadoCostoReal] decimal(18,2) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261105142232_H12CostoServiciosVenta'
)
BEGIN
    ALTER TABLE [VentaDetalles] ADD [ArmadoCostoUnitarioEstandar] decimal(18,2) NOT NULL DEFAULT 0.0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261105142232_H12CostoServiciosVenta'
)
BEGIN
    ALTER TABLE [ServiciosVentaPrecios] ADD [Costo] decimal(18,2) NOT NULL DEFAULT 0.0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261105142232_H12CostoServiciosVenta'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261105142232_H12CostoServiciosVenta', N'10.0.13');
END;

COMMIT;
GO

