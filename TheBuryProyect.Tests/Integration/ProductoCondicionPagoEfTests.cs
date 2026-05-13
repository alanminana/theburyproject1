using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using System.Reflection;
using TheBuryProject.Data;
using TheBuryProject.Migrations;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Caracterizacion legacy/admin de pago por producto; no define contrato canonico de Nueva Venta.
/// </summary>
[Trait("Area", "LegacyPagoPorProducto")]
public sealed class ProductoCondicionPagoEfTests
{
    [Fact]
    public async Task ProductoCondicionPago_PersisteReglaGeneralYEspecificaSinNormalizarTarjetaLegacy()
    {
        await using var fixture = await ProductoCondicionPagoDbFixture.CreateAsync();
        var producto = await fixture.SeedProductoAsync();
        var tarjeta = await fixture.SeedTarjetaAsync();

        fixture.Context.ProductoCondicionesPago.Add(new ProductoCondicionPago
        {
            ProductoId = producto.Id,
            TipoPago = TipoPago.Tarjeta,
            Permitido = true,
            MaxCuotasSinInteres = 6,
            Observaciones = "Regla legacy explicitamente persistida",
            Tarjetas =
            {
                new ProductoCondicionPagoTarjeta
                {
                    ConfiguracionTarjetaId = null,
                    Permitido = true,
                    MaxCuotasSinInteres = 3
                },
                new ProductoCondicionPagoTarjeta
                {
                    ConfiguracionTarjetaId = tarjeta.Id,
                    Permitido = false,
                    PorcentajeRecargo = 5m
                }
            }
        });
        await fixture.Context.SaveChangesAsync();

        var condicion = await fixture.Context.ProductoCondicionesPago
            .Include(c => c.Tarjetas)
            .SingleAsync(c => c.ProductoId == producto.Id);

        Assert.Equal(TipoPago.Tarjeta, condicion.TipoPago);
        Assert.NotEmpty(condicion.RowVersion);
        Assert.Equal(2, condicion.Tarjetas.Count);
        Assert.Contains(condicion.Tarjetas, t => t.ConfiguracionTarjetaId is null);
        Assert.Contains(condicion.Tarjetas, t => t.ConfiguracionTarjetaId == tarjeta.Id && t.Permitido == false);
    }

    [Fact]
    public async Task ProductoCondicionPago_NoPermiteDosCondicionesActivasParaMismoProductoYTipoPago()
    {
        await using var fixture = await ProductoCondicionPagoDbFixture.CreateAsync();
        var producto = await fixture.SeedProductoAsync();

        fixture.Context.ProductoCondicionesPago.AddRange(
            new ProductoCondicionPago { ProductoId = producto.Id, TipoPago = TipoPago.CreditoPersonal },
            new ProductoCondicionPago { ProductoId = producto.Id, TipoPago = TipoPago.CreditoPersonal });

        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.Context.SaveChangesAsync());
    }

    [Fact]
    public async Task ProductoCondicionPagoTarjeta_NoPermiteDuplicarReglaGeneralActiva()
    {
        await using var fixture = await ProductoCondicionPagoDbFixture.CreateAsync();
        var producto = await fixture.SeedProductoAsync();
        var condicion = await fixture.SeedCondicionAsync(producto.Id, TipoPago.TarjetaCredito);

        fixture.Context.ProductoCondicionesPagoTarjeta.AddRange(
            new ProductoCondicionPagoTarjeta { ProductoCondicionPagoId = condicion.Id, ConfiguracionTarjetaId = null },
            new ProductoCondicionPagoTarjeta { ProductoCondicionPagoId = condicion.Id, ConfiguracionTarjetaId = null });

        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.Context.SaveChangesAsync());
    }

    [Fact]
    public async Task ProductoCondicionPagoTarjeta_NoPermiteDuplicarReglaEspecificaActiva()
    {
        await using var fixture = await ProductoCondicionPagoDbFixture.CreateAsync();
        var producto = await fixture.SeedProductoAsync();
        var tarjeta = await fixture.SeedTarjetaAsync();
        var condicion = await fixture.SeedCondicionAsync(producto.Id, TipoPago.TarjetaCredito);

        fixture.Context.ProductoCondicionesPagoTarjeta.AddRange(
            new ProductoCondicionPagoTarjeta { ProductoCondicionPagoId = condicion.Id, ConfiguracionTarjetaId = tarjeta.Id },
            new ProductoCondicionPagoTarjeta { ProductoCondicionPagoId = condicion.Id, ConfiguracionTarjetaId = tarjeta.Id });

        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.Context.SaveChangesAsync());
    }

    [Fact]
    public async Task ProductoCondicionPago_RechazaCuotasMenoresAUno()
    {
        await using var fixture = await ProductoCondicionPagoDbFixture.CreateAsync();
        var producto = await fixture.SeedProductoAsync();

        fixture.Context.ProductoCondicionesPago.Add(new ProductoCondicionPago
        {
            ProductoId = producto.Id,
            TipoPago = TipoPago.CreditoPersonal,
            MaxCuotasCredito = 0
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.Context.SaveChangesAsync());
    }

    [Fact]
    public async Task ProductoCondicionPago_RechazaPorcentajesFueraDeRango()
    {
        await using var fixture = await ProductoCondicionPagoDbFixture.CreateAsync();
        var producto = await fixture.SeedProductoAsync();

        fixture.Context.ProductoCondicionesPago.Add(new ProductoCondicionPago
        {
            ProductoId = producto.Id,
            TipoPago = TipoPago.Efectivo,
            PorcentajeDescuentoMaximo = 101m
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.Context.SaveChangesAsync());
    }

    [Fact]
    public async Task ProductoCondicionPago_RechazaPorcentajeNegativoActual()
    {
        await using var fixture = await ProductoCondicionPagoDbFixture.CreateAsync();
        var producto = await fixture.SeedProductoAsync();

        fixture.Context.ProductoCondicionesPago.Add(new ProductoCondicionPago
        {
            ProductoId = producto.Id,
            TipoPago = TipoPago.TarjetaCredito,
            PorcentajeRecargo = -1m
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.Context.SaveChangesAsync());
    }

    [Fact]
    public async Task ProductoCondicionPagoTarjeta_RechazaPorcentajeNegativoActual()
    {
        await using var fixture = await ProductoCondicionPagoDbFixture.CreateAsync();
        var producto = await fixture.SeedProductoAsync();
        var condicion = await fixture.SeedCondicionAsync(producto.Id, TipoPago.TarjetaCredito);

        fixture.Context.ProductoCondicionesPagoTarjeta.Add(new ProductoCondicionPagoTarjeta
        {
            ProductoCondicionPagoId = condicion.Id,
            PorcentajeDescuentoMaximo = -1m
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.Context.SaveChangesAsync());
    }

    // ============================================================
    // ProductoCondicionPagoPlan — Fase 15.3
    // ============================================================

    [Fact]
    public async Task ProductoCondicionPagoPlan_PersistePlanActivoConAjustePositivo()
    {
        await using var fixture = await ProductoCondicionPagoDbFixture.CreateAsync();
        var producto = await fixture.SeedProductoAsync();
        var condicion = await fixture.SeedCondicionAsync(producto.Id, TipoPago.TarjetaCredito);

        fixture.Context.ProductoCondicionPagoPlanes.Add(new ProductoCondicionPagoPlan
        {
            ProductoCondicionPagoId = condicion.Id,
            CantidadCuotas = 6,
            Activo = true,
            AjustePorcentaje = 5.5m
        });
        await fixture.Context.SaveChangesAsync();

        var plan = await fixture.Context.ProductoCondicionPagoPlanes.SingleAsync(p => p.ProductoCondicionPagoId == condicion.Id);
        Assert.Equal(6, plan.CantidadCuotas);
        Assert.True(plan.Activo);
        Assert.Equal(5.5m, plan.AjustePorcentaje);
        Assert.NotEmpty(plan.RowVersion);
    }

    [Fact]
    public async Task ProductoCondicionPagoPlan_PersistePlanActivoConAjusteCero()
    {
        await using var fixture = await ProductoCondicionPagoDbFixture.CreateAsync();
        var producto = await fixture.SeedProductoAsync();
        var condicion = await fixture.SeedCondicionAsync(producto.Id, TipoPago.CreditoPersonal);

        fixture.Context.ProductoCondicionPagoPlanes.Add(new ProductoCondicionPagoPlan
        {
            ProductoCondicionPagoId = condicion.Id,
            CantidadCuotas = 3,
            Activo = true,
            AjustePorcentaje = 0m
        });
        await fixture.Context.SaveChangesAsync();

        var plan = await fixture.Context.ProductoCondicionPagoPlanes.SingleAsync(p => p.ProductoCondicionPagoId == condicion.Id);
        Assert.Equal(0m, plan.AjustePorcentaje);
    }

    [Fact]
    public async Task ProductoCondicionPagoPlan_PersistePlanActivoConAjusteNegativo()
    {
        await using var fixture = await ProductoCondicionPagoDbFixture.CreateAsync();
        var producto = await fixture.SeedProductoAsync();
        var condicion = await fixture.SeedCondicionAsync(producto.Id, TipoPago.TarjetaCredito);

        fixture.Context.ProductoCondicionPagoPlanes.Add(new ProductoCondicionPagoPlan
        {
            ProductoCondicionPagoId = condicion.Id,
            CantidadCuotas = 1,
            Activo = true,
            AjustePorcentaje = -10m
        });
        await fixture.Context.SaveChangesAsync();

        var plan = await fixture.Context.ProductoCondicionPagoPlanes.SingleAsync(p => p.ProductoCondicionPagoId == condicion.Id);
        Assert.Equal(-10m, plan.AjustePorcentaje);
    }

    [Fact]
    public async Task ProductoCondicionPagoPlan_PermiteActivoFalsePersistido()
    {
        await using var fixture = await ProductoCondicionPagoDbFixture.CreateAsync();
        var producto = await fixture.SeedProductoAsync();
        var condicion = await fixture.SeedCondicionAsync(producto.Id, TipoPago.TarjetaCredito);

        fixture.Context.ProductoCondicionPagoPlanes.Add(new ProductoCondicionPagoPlan
        {
            ProductoCondicionPagoId = condicion.Id,
            CantidadCuotas = 12,
            Activo = false,
            AjustePorcentaje = 0m
        });
        await fixture.Context.SaveChangesAsync();

        var plan = await fixture.Context.ProductoCondicionPagoPlanes
            .IgnoreQueryFilters()
            .SingleAsync(p => p.ProductoCondicionPagoId == condicion.Id);
        Assert.False(plan.Activo);
    }

    [Fact]
    public async Task ProductoCondicionPagoPlan_RechazaCantidadCuotasMenorAUno()
    {
        await using var fixture = await ProductoCondicionPagoDbFixture.CreateAsync();
        var producto = await fixture.SeedProductoAsync();
        var condicion = await fixture.SeedCondicionAsync(producto.Id, TipoPago.TarjetaCredito);

        fixture.Context.ProductoCondicionPagoPlanes.Add(new ProductoCondicionPagoPlan
        {
            ProductoCondicionPagoId = condicion.Id,
            CantidadCuotas = 0,
            AjustePorcentaje = 0m
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.Context.SaveChangesAsync());
    }

    [Fact]
    public async Task ProductoCondicionPagoPlan_PermiteMultiplesCuotasDistintasEnMismaCondicion()
    {
        await using var fixture = await ProductoCondicionPagoDbFixture.CreateAsync();
        var producto = await fixture.SeedProductoAsync();
        var condicion = await fixture.SeedCondicionAsync(producto.Id, TipoPago.TarjetaCredito);

        fixture.Context.ProductoCondicionPagoPlanes.AddRange(
            new ProductoCondicionPagoPlan { ProductoCondicionPagoId = condicion.Id, CantidadCuotas = 3, AjustePorcentaje = 0m },
            new ProductoCondicionPagoPlan { ProductoCondicionPagoId = condicion.Id, CantidadCuotas = 6, AjustePorcentaje = 5m },
            new ProductoCondicionPagoPlan { ProductoCondicionPagoId = condicion.Id, CantidadCuotas = 12, AjustePorcentaje = 10m });

        await fixture.Context.SaveChangesAsync();

        var planes = await fixture.Context.ProductoCondicionPagoPlanes
            .Where(p => p.ProductoCondicionPagoId == condicion.Id)
            .ToListAsync();
        Assert.Equal(3, planes.Count);
    }

    [Fact]
    public async Task ProductoCondicionPagoPlan_EvitaDuplicadoDeMismaCuotaEnMismaCondicionGeneral()
    {
        await using var fixture = await ProductoCondicionPagoDbFixture.CreateAsync();
        var producto = await fixture.SeedProductoAsync();
        var condicion = await fixture.SeedCondicionAsync(producto.Id, TipoPago.TarjetaCredito);

        fixture.Context.ProductoCondicionPagoPlanes.AddRange(
            new ProductoCondicionPagoPlan { ProductoCondicionPagoId = condicion.Id, CantidadCuotas = 6, AjustePorcentaje = 0m },
            new ProductoCondicionPagoPlan { ProductoCondicionPagoId = condicion.Id, CantidadCuotas = 6, AjustePorcentaje = 5m });

        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.Context.SaveChangesAsync());
    }

    [Fact]
    public async Task ProductoCondicionPagoPlan_MaximosEscalaresExistentesNoSonModificadosPorLaEntidad()
    {
        await using var fixture = await ProductoCondicionPagoDbFixture.CreateAsync();
        var producto = await fixture.SeedProductoAsync();

        var condicion = new ProductoCondicionPago
        {
            ProductoId = producto.Id,
            TipoPago = TipoPago.TarjetaCredito,
            MaxCuotasSinInteres = 6,
            MaxCuotasConInteres = 12
        };
        fixture.Context.ProductoCondicionesPago.Add(condicion);
        await fixture.Context.SaveChangesAsync();

        fixture.Context.ProductoCondicionPagoPlanes.Add(new ProductoCondicionPagoPlan
        {
            ProductoCondicionPagoId = condicion.Id,
            CantidadCuotas = 3,
            AjustePorcentaje = 0m
        });
        await fixture.Context.SaveChangesAsync();

        var condicionRecargada = await fixture.Context.ProductoCondicionesPago
            .SingleAsync(c => c.Id == condicion.Id);
        Assert.Equal(6, condicionRecargada.MaxCuotasSinInteres);
        Assert.Equal(12, condicionRecargada.MaxCuotasConInteres);
    }

    // ============================================================
    // ProductoCreditoRestriccion — Fase 7.14
    // ============================================================

    [Fact]
    public async Task ProductoCreditoRestriccion_PersisteRestriccionValida()
    {
        await using var fixture = await ProductoCondicionPagoDbFixture.CreateAsync();
        var producto = await fixture.SeedProductoAsync();

        fixture.Context.ProductoCreditoRestricciones.Add(new ProductoCreditoRestriccion
        {
            ProductoId = producto.Id,
            Permitido = false,
            MaxCuotasCredito = 6,
            Observaciones = "Bloqueo operativo de credito personal"
        });
        await fixture.Context.SaveChangesAsync();

        var restriccion = await fixture.Context.ProductoCreditoRestricciones
            .SingleAsync(r => r.ProductoId == producto.Id);

        Assert.False(restriccion.Permitido);
        Assert.True(restriccion.Activo);
        Assert.Equal(6, restriccion.MaxCuotasCredito);
        Assert.NotEmpty(restriccion.RowVersion);
    }

    [Fact]
    public async Task ProductoCreditoRestriccion_PermiteMaxCuotasCreditoNull()
    {
        await using var fixture = await ProductoCondicionPagoDbFixture.CreateAsync();
        var producto = await fixture.SeedProductoAsync();

        fixture.Context.ProductoCreditoRestricciones.Add(new ProductoCreditoRestriccion
        {
            ProductoId = producto.Id,
            MaxCuotasCredito = null
        });
        await fixture.Context.SaveChangesAsync();

        var restriccion = await fixture.Context.ProductoCreditoRestricciones
            .SingleAsync(r => r.ProductoId == producto.Id);

        Assert.Null(restriccion.MaxCuotasCredito);
        Assert.True(restriccion.Permitido);
    }

    [Fact]
    public async Task ProductoCreditoRestriccion_RechazaMaxCuotasCreditoCero()
    {
        await using var fixture = await ProductoCondicionPagoDbFixture.CreateAsync();
        var producto = await fixture.SeedProductoAsync();

        fixture.Context.ProductoCreditoRestricciones.Add(new ProductoCreditoRestriccion
        {
            ProductoId = producto.Id,
            MaxCuotasCredito = 0
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.Context.SaveChangesAsync());
    }

    [Fact]
    public async Task ProductoCreditoRestriccion_NoPermiteDosRestriccionesActivasParaMismoProducto()
    {
        await using var fixture = await ProductoCondicionPagoDbFixture.CreateAsync();
        var producto = await fixture.SeedProductoAsync();

        fixture.Context.ProductoCreditoRestricciones.AddRange(
            new ProductoCreditoRestriccion { ProductoId = producto.Id },
            new ProductoCreditoRestriccion { ProductoId = producto.Id, Permitido = false });

        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.Context.SaveChangesAsync());
    }

    [Fact]
    public async Task ProductoCreditoRestriccion_PermiteHistoricoInactivoOEliminadoParaMismoProducto()
    {
        await using var fixture = await ProductoCondicionPagoDbFixture.CreateAsync();
        var producto = await fixture.SeedProductoAsync();

        fixture.Context.ProductoCreditoRestricciones.AddRange(
            new ProductoCreditoRestriccion { ProductoId = producto.Id, Activo = true },
            new ProductoCreditoRestriccion { ProductoId = producto.Id, Activo = false, Permitido = false },
            new ProductoCreditoRestriccion { ProductoId = producto.Id, IsDeleted = true, Permitido = false });

        await fixture.Context.SaveChangesAsync();

        var visibles = await fixture.Context.ProductoCreditoRestricciones
            .Where(r => r.ProductoId == producto.Id)
            .ToListAsync();
        var totalHistorico = await fixture.Context.ProductoCreditoRestricciones
            .IgnoreQueryFilters()
            .CountAsync(r => r.ProductoId == producto.Id);

        Assert.Equal(2, visibles.Count);
        Assert.Equal(3, totalHistorico);
    }

    [Fact]
    public async Task ProductoCreditoRestriccion_FkProductoFunciona()
    {
        await using var fixture = await ProductoCondicionPagoDbFixture.CreateAsync();
        var producto = await fixture.SeedProductoAsync();

        fixture.Context.ProductoCreditoRestricciones.Add(new ProductoCreditoRestriccion
        {
            ProductoId = producto.Id,
            MaxCuotasCredito = 3
        });
        await fixture.Context.SaveChangesAsync();

        var restriccion = await fixture.Context.ProductoCreditoRestricciones
            .Include(r => r.Producto)
            .SingleAsync(r => r.ProductoId == producto.Id);

        Assert.Equal(producto.Id, restriccion.Producto.Id);
        Assert.Equal(producto.Codigo, restriccion.Producto.Codigo);
    }

    // ============================================================
    // ProductoCreditoRestriccion — Fase 7.15 backfill desde legacy
    // ============================================================

    [Fact]
    public void ProductoCreditoRestriccion_MigracionBackfill_CopiaSoloCreditoPersonalActivoNoEliminado()
    {
        var sql = GetSingleSqlOperationFromMigrationUp(
            new BackfillProductoCreditoRestriccionesFromCondicionesPago());

        Assert.Contains("[TipoPago] = 5", sql);
        Assert.Contains("[IsDeleted] = 0", sql);
        Assert.Contains("[Activo] = 1", sql);
        Assert.Contains("INSERT INTO [ProductoCreditoRestricciones]", sql);
        Assert.Contains("[ProductoId]", sql);
        Assert.Contains("[Permitido]", sql);
        Assert.Contains("[MaxCuotasCredito]", sql);
        Assert.Contains("[Observaciones]", sql);
        Assert.Contains("[CreatedAt]", sql);
        Assert.Contains("[UpdatedAt]", sql);
        Assert.Contains("[CreatedBy]", sql);
        Assert.Contains("[UpdatedBy]", sql);
        Assert.Contains("COALESCE(c.[Permitido], CAST(1 AS bit))", sql);
    }

    [Fact]
    public void ProductoCreditoRestriccion_MigracionBackfill_NoCopiaCamposDePlanesTarjetasOAjustes()
    {
        var sql = GetSingleSqlOperationFromMigrationUp(
            new BackfillProductoCreditoRestriccionesFromCondicionesPago());

        Assert.DoesNotContain("PorcentajeRecargo", sql);
        Assert.DoesNotContain("PorcentajeDescuentoMaximo", sql);
        Assert.DoesNotContain("ConfiguracionTarjetaId", sql);
        Assert.DoesNotContain("ProductoCondicionPagoPlanId", sql);
        Assert.DoesNotContain("ProductoCondicionPagoPlanes", sql);
        Assert.DoesNotContain("ProductoCondicionesPagoTarjeta", sql);
    }

    [Fact]
    public void ProductoCreditoRestriccion_MigracionBackfill_DetectaDuplicadosYEvitaDuplicarDestino()
    {
        var sql = GetSingleSqlOperationFromMigrationUp(
            new BackfillProductoCreditoRestriccionesFromCondicionesPago());

        Assert.Contains("GROUP BY [ProductoId]", sql);
        Assert.Contains("HAVING COUNT(*) > 1", sql);
        Assert.Contains("THROW 51001", sql);
        Assert.Contains("NOT EXISTS", sql);
        Assert.Contains("FROM [ProductoCreditoRestricciones] AS r", sql);
    }

    [Fact]
    public void ProductoCreditoRestriccion_MigracionBackfill_DownEsNoDestructivo()
    {
        var migration = new BackfillProductoCreditoRestriccionesFromCondicionesPago();
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        InvokeMigrationMethod(migration, "Down", builder);

        var operation = Assert.IsType<SqlOperation>(Assert.Single(builder.Operations));
        Assert.Contains("Down no destructivo", operation.Sql);
        Assert.DoesNotContain("DELETE", operation.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TRUNCATE", operation.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP", operation.Sql, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetSingleSqlOperationFromMigrationUp(Migration migration)
    {
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        InvokeMigrationMethod(migration, "Up", builder);

        var operation = Assert.IsType<SqlOperation>(Assert.Single(builder.Operations));
        return operation.Sql;
    }

    private static void InvokeMigrationMethod(Migration migration, string methodName, MigrationBuilder builder)
    {
        var method = migration.GetType().GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);
        method.Invoke(migration, new object[] { builder });
    }

    private sealed class ProductoCondicionPagoDbFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private ProductoCondicionPagoDbFixture(SqliteConnection connection, AppDbContext context)
        {
            _connection = connection;
            Context = context;
        }

        public AppDbContext Context { get; }

        public static async Task<ProductoCondicionPagoDbFixture> CreateAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;
            var context = new AppDbContext(options);
            await context.Database.EnsureCreatedAsync();

            return new ProductoCondicionPagoDbFixture(connection, context);
        }

        public async Task<Producto> SeedProductoAsync()
        {
            var categoria = new Categoria { Codigo = Guid.NewGuid().ToString("N")[..12], Nombre = "Categoria test" };
            var marca = new Marca { Codigo = Guid.NewGuid().ToString("N")[..12], Nombre = "Marca test" };
            var producto = new Producto
            {
                Codigo = Guid.NewGuid().ToString("N")[..12],
                Nombre = "Producto test",
                Categoria = categoria,
                Marca = marca,
                PrecioCompra = 100m,
                PrecioVenta = 150m,
                StockActual = 1m
            };

            Context.Productos.Add(producto);
            await Context.SaveChangesAsync();
            return producto;
        }

        public async Task<ConfiguracionTarjeta> SeedTarjetaAsync()
        {
            var configuracionPago = new ConfiguracionPago
            {
                TipoPago = TipoPago.TarjetaCredito,
                Nombre = Guid.NewGuid().ToString("N")[..12]
            };
            var tarjeta = new ConfiguracionTarjeta
            {
                ConfiguracionPago = configuracionPago,
                NombreTarjeta = Guid.NewGuid().ToString("N")[..12],
                TipoTarjeta = TipoTarjeta.Credito,
                PermiteCuotas = true,
                CantidadMaximaCuotas = 12
            };

            Context.ConfiguracionesTarjeta.Add(tarjeta);
            await Context.SaveChangesAsync();
            return tarjeta;
        }

        public async Task<ProductoCondicionPago> SeedCondicionAsync(int productoId, TipoPago tipoPago)
        {
            var condicion = new ProductoCondicionPago
            {
                ProductoId = productoId,
                TipoPago = tipoPago
            };

            Context.ProductoCondicionesPago.Add(condicion);
            await Context.SaveChangesAsync();
            return condicion;
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
