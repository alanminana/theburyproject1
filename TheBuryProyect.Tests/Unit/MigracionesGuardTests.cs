using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TheBuryProject.Data;

namespace TheBuryProject.Tests.Unit;

/// <summary>Protección de bases reales: con historial y migraciones pendientes, el arranque no migra sin habilitación explícita.</summary>
public class MigracionesGuardTests : IDisposable
{
    private readonly SqliteConnection _conn = new("DataSource=:memory:");
    public MigracionesGuardTests() => _conn.Open();
    public void Dispose() => _conn.Dispose();

    private AppDbContext Ctx() => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options);
    private static IConfiguration Cfg(bool? allow) => new ConfigurationBuilder()
        .AddInMemoryCollection(allow == null ? [] : new Dictionary<string, string?> { ["Database:AllowMigrations"] = allow.ToString() }).Build();

    private void SimularBaseConHistorial()
    {
        using var ctx = Ctx();
        var primera = ctx.Database.GetMigrations().First();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "CREATE TABLE \"__EFMigrationsHistory\" (\"MigrationId\" TEXT PRIMARY KEY, \"ProductVersion\" TEXT); " +
                          $"INSERT INTO \"__EFMigrationsHistory\" VALUES ('{primera}', '10.0.0');";
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public async Task BaseNuevaSinHistorial_NoPideHabilitacion()
    {
        await using var ctx = Ctx();
        await MigracionesGuard.AsegurarAutorizadoAsync(ctx, Cfg(null));
    }

    [Fact]
    public async Task BaseConHistorialYPendientes_SinHabilitar_Falla_YNoTocaElHistorial()
    {
        SimularBaseConHistorial();
        await using var ctx = Ctx();
        var ex = await Assert.ThrowsAsync<MigracionesNoAutorizadasException>(() => MigracionesGuard.AsegurarAutorizadoAsync(ctx, Cfg(null)));
        Assert.Contains("Database:AllowMigrations", ex.Message);
        Assert.Single(await ctx.Database.GetAppliedMigrationsAsync());
    }

    [Fact]
    public async Task BaseConTablasPeroSinHistorial_SinHabilitar_Falla()
    {
        using (var cmd = _conn.CreateCommand()) { cmd.CommandText = "CREATE TABLE Clientes (Id INTEGER PRIMARY KEY)"; cmd.ExecuteNonQuery(); }
        await using var ctx = Ctx();
        var ex = await Assert.ThrowsAsync<MigracionesNoAutorizadasException>(() => MigracionesGuard.AsegurarAutorizadoAsync(ctx, Cfg(null)));
        Assert.Contains("historial", ex.Message);
    }

    [Fact]
    public async Task ErrorAlVerificar_FallaCerrado()
    {
        var ctx = Ctx();
        await ctx.DisposeAsync();
        await Assert.ThrowsAsync<MigracionesNoAutorizadasException>(() => MigracionesGuard.AsegurarAutorizadoAsync(ctx, Cfg(null)));
    }

    [Fact]
    public async Task ServidorInaccesible_NoEsBaseInexistente_FallaCerrado()
    {
        await using var ctx = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=127.0.0.1,1;Database=x;User Id=u;Password=p;Connect Timeout=2;TrustServerCertificate=True").Options);
        await Assert.ThrowsAsync<MigracionesNoAutorizadasException>(() => MigracionesGuard.AsegurarAutorizadoAsync(ctx, Cfg(null)));
    }

    [Fact]
    public async Task ConfiguracionFalse_TambienFalla()
    {
        SimularBaseConHistorial();
        await using var ctx = Ctx();
        await Assert.ThrowsAsync<MigracionesNoAutorizadasException>(() => MigracionesGuard.AsegurarAutorizadoAsync(ctx, Cfg(false)));
    }

    [Fact]
    public async Task Habilitada_Permite()
    {
        SimularBaseConHistorial();
        await using var ctx = Ctx();
        await MigracionesGuard.AsegurarAutorizadoAsync(ctx, Cfg(true));
    }

    [Fact]
    public async Task ModoMigrate_ExplicitoPorComando_Permite()
    {
        SimularBaseConHistorial();
        await using var ctx = Ctx();
        await MigracionesGuard.AsegurarAutorizadoAsync(ctx, Cfg(null), explicitoPorComando: true);
    }
}
