using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Exceptions;

namespace TheBuryProject.Tests.Unit;

public class CreditoDisponibleServiceTests
{
    // ---------------------------------------------------------------------------
    // A. CalcularLimiteEfectivo — unit tests puros, sin infraestructura
    // ---------------------------------------------------------------------------

    [Fact]
    public void CalcularLimiteEfectivo_ConOverride_UsaOverride()
    {
        var (limite, origen) = CreditoDisponibleService.CalcularLimiteEfectivo(
            limiteBase: 50_000m,
            limiteOverride: 80_000m,
            excepcionDeltaVigente: 10_000m);

        Assert.Equal(80_000m, limite);
        Assert.Equal("Override absoluto", origen);
    }

    [Fact]
    public void CalcularLimiteEfectivo_SinOverride_ConExcepcionVigente_UsaBaseMasExcepcion()
    {
        var (limite, origen) = CreditoDisponibleService.CalcularLimiteEfectivo(
            limiteBase: 50_000m,
            limiteOverride: null,
            excepcionDeltaVigente: 15_000m);

        Assert.Equal(65_000m, limite);
        Assert.Contains("Excepción", origen);
    }

    [Fact]
    public void CalcularLimiteEfectivo_SinOverride_SinExcepcion_UsaBase()
    {
        var (limite, origen) = CreditoDisponibleService.CalcularLimiteEfectivo(
            limiteBase: 50_000m,
            limiteOverride: null,
            excepcionDeltaVigente: 0m);

        Assert.Equal(50_000m, limite);
        Assert.Equal("Preset", origen);
    }

    // ---------------------------------------------------------------------------
    // B. CalcularDisponibleAsync — integration tests con SQLite :memory:
    // ---------------------------------------------------------------------------

    // Una sola conexión abierta a ":memory:" = una DB privada por test.
    // AppDbContext.SeedData siembra PuntajeCreditoLimite IDs 1-5 con LimiteMonto=0.
    // Los tests actualizan esas filas existentes (no insertan nuevas).
    // ClienteCreditoConfiguracion.RowVersion se auto-genera por randomblob(8)
    // configurado en el bloque de compatibilidad non-SQLServer de AppDbContext.
    private static (AppDbContext ctx, SqliteConnection conn) CreateContext()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(conn)
            .Options;
        var ctx = new AppDbContext(options);
        ctx.Database.EnsureCreated();
        return (ctx, conn);
    }

    // Id 5 = AprobadoTotal en el seed. Actualiza LimiteMonto para el test.
    private static async Task SetLimiteAprobadoTotal(AppDbContext ctx, decimal monto)
    {
        var preset = await ctx.PuntajesCreditoLimite.FindAsync(5);
        preset!.LimiteMonto = monto;
        await ctx.SaveChangesAsync();
    }

    private static Cliente BaseCliente(int id, NivelRiesgoCredito nivel = NivelRiesgoCredito.AprobadoTotal) =>
        new()
        {
            Id = id,
            Nombre = "Test",
            Apellido = "Cliente",
            TipoDocumento = "DNI",
            NumeroDocumento = $"1000000{id}",
            NivelRiesgo = nivel,
            IsDeleted = false,
            RowVersion = new byte[8]
        };

    [Fact]
    public async Task CalcularDisponibleAsync_SinConfig_UsaLimitePorPuntaje()
    {
        var (ctx, conn) = CreateContext();
        await using (ctx) using (conn)
        {
            await SetLimiteAprobadoTotal(ctx, 100_000m);
            ctx.Clientes.Add(BaseCliente(1, NivelRiesgoCredito.AprobadoTotal));
            await ctx.SaveChangesAsync();

            var service = new CreditoDisponibleService(ctx, Microsoft.Extensions.Logging.Abstractions.NullLogger<TheBuryProject.Services.CreditoDisponibleService>.Instance);
            var resultado = await service.CalcularDisponibleAsync(1);

            Assert.Equal(100_000m, resultado.Limite);
            Assert.Equal(100_000m, resultado.Disponible);
            Assert.Equal(0m, resultado.SaldoVigente);
            Assert.Equal("Puntaje", resultado.OrigenLimite);
        }
    }

    [Fact]
    public async Task CalcularDisponibleAsync_SinConfig_SinLimiteParaPuntaje_LanzaExcepcion()
    {
        var (ctx, conn) = CreateContext();
        await using (ctx) using (conn)
        {
            // Desactivar el preset de AprobadoTotal para que ObtenerLimitePorPuntajeAsync
            // no encuentre ninguna fila activa y lance CreditoDisponibleException.
            var preset = await ctx.PuntajesCreditoLimite.FindAsync(5);
            preset!.Activo = false;
            await ctx.SaveChangesAsync();

            ctx.Clientes.Add(BaseCliente(1, NivelRiesgoCredito.AprobadoTotal));
            await ctx.SaveChangesAsync();

            var service = new CreditoDisponibleService(ctx, Microsoft.Extensions.Logging.Abstractions.NullLogger<TheBuryProject.Services.CreditoDisponibleService>.Instance);

            await Assert.ThrowsAsync<CreditoDisponibleException>(
                () => service.CalcularDisponibleAsync(1));
        }
    }

    [Fact]
    public async Task CalcularDisponibleAsync_ConConfig_ConPreset_UsaLimitePreset()
    {
        var (ctx, conn) = CreateContext();
        await using (ctx) using (conn)
        {
            await SetLimiteAprobadoTotal(ctx, 200_000m);
            ctx.Clientes.Add(BaseCliente(1));
            await ctx.SaveChangesAsync();

            // Usa preset Id=5 (AprobadoTotal) que ya existe en el seed
            ctx.ClientesCreditoConfiguraciones.Add(new ClienteCreditoConfiguracion
            {
                ClienteId = 1,
                CreditoPresetId = 5,
            });
            await ctx.SaveChangesAsync();

            var service = new CreditoDisponibleService(ctx, Microsoft.Extensions.Logging.Abstractions.NullLogger<TheBuryProject.Services.CreditoDisponibleService>.Instance);
            var resultado = await service.CalcularDisponibleAsync(1);

            Assert.Equal(200_000m, resultado.Limite);
            Assert.Equal("Preset", resultado.OrigenLimite);
        }
    }

    [Fact]
    public async Task CalcularDisponibleAsync_ConConfig_ConOverride_UsaOverride()
    {
        var (ctx, conn) = CreateContext();
        await using (ctx) using (conn)
        {
            await SetLimiteAprobadoTotal(ctx, 100_000m);
            ctx.Clientes.Add(BaseCliente(1));
            await ctx.SaveChangesAsync();

            ctx.ClientesCreditoConfiguraciones.Add(new ClienteCreditoConfiguracion
            {
                ClienteId = 1,
                CreditoPresetId = 5,
                LimiteOverride = 300_000m,
            });
            await ctx.SaveChangesAsync();

            var service = new CreditoDisponibleService(ctx, Microsoft.Extensions.Logging.Abstractions.NullLogger<TheBuryProject.Services.CreditoDisponibleService>.Instance);
            var resultado = await service.CalcularDisponibleAsync(1);

            Assert.Equal(300_000m, resultado.Limite);
            Assert.Equal("Override absoluto", resultado.OrigenLimite);
        }
    }

    [Fact]
    public async Task CalcularDisponibleAsync_ConConfig_ConExcepcionVigente_SumaAlBase()
    {
        var (ctx, conn) = CreateContext();
        await using (ctx) using (conn)
        {
            await SetLimiteAprobadoTotal(ctx, 100_000m);
            ctx.Clientes.Add(BaseCliente(1));
            await ctx.SaveChangesAsync();

            ctx.ClientesCreditoConfiguraciones.Add(new ClienteCreditoConfiguracion
            {
                ClienteId = 1,
                CreditoPresetId = 5,
                ExcepcionDelta = 50_000m,
                ExcepcionDesde = DateTime.UtcNow.AddDays(-1),
                ExcepcionHasta = DateTime.UtcNow.AddDays(30),
            });
            await ctx.SaveChangesAsync();

            var service = new CreditoDisponibleService(ctx, Microsoft.Extensions.Logging.Abstractions.NullLogger<TheBuryProject.Services.CreditoDisponibleService>.Instance);
            var resultado = await service.CalcularDisponibleAsync(1);

            Assert.Equal(150_000m, resultado.Limite);
            Assert.Contains("Excepción", resultado.OrigenLimite);
        }
    }

    [Fact]
    public async Task CalcularDisponibleAsync_DescuentaSaldoVigente_CorrectamenteDelDisponible()
    {
        var (ctx, conn) = CreateContext();
        await using (ctx) using (conn)
        {
            await SetLimiteAprobadoTotal(ctx, 100_000m);
            ctx.Clientes.Add(BaseCliente(1));
            await ctx.SaveChangesAsync();

            ctx.Creditos.Add(new Credito
            {
                Id = 1,
                ClienteId = 1,
                SaldoPendiente = 30_000m,
                Estado = EstadoCredito.Activo,
                IsDeleted = false,
                RowVersion = new byte[8]
            });
            await ctx.SaveChangesAsync();

            var service = new CreditoDisponibleService(ctx, Microsoft.Extensions.Logging.Abstractions.NullLogger<TheBuryProject.Services.CreditoDisponibleService>.Instance);
            var resultado = await service.CalcularDisponibleAsync(1);

            Assert.Equal(100_000m, resultado.Limite);
            Assert.Equal(30_000m, resultado.SaldoVigente);
            Assert.Equal(70_000m, resultado.Disponible);
        }
    }
}
