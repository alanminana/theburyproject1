using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Verificacion OPT-IN contra la Central de Deudores real del BCRA (V-01). No corre en CI:
/// requiere BURY_BCRA_REAL=1 y salida a internet. Usa solo CUIT de personas juridicas publicas
/// (Banco de la Nacion Argentina) y una base SQLite en memoria; no toca ninguna base real.
/// </summary>
public class SituacionCrediticiaBcraRealApiTests : IDisposable
{
    private const string CuitBancoNacion = "30500010912";
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public SituacionCrediticiaBcraRealApiTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using var ctx = new AppDbContext(_options);
        ctx.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private static bool Habilitado =>
        Environment.GetEnvironmentVariable("BURY_BCRA_REAL") == "1";

    private async Task<(Cliente, SituacionCrediticiaBcraService)> ArmarAsync(string cuil)
    {
        await using var ctx = new AppDbContext(_options);
        var doc = Guid.NewGuid().ToString("N")[..8];
        var cliente = new Cliente
        {
            Nombre = "Test", Apellido = "BCRA", TipoDocumento = "DNI", NumeroDocumento = doc,
            Email = $"{doc}@test.com", Activo = true, CuilCuit = cuil
        };
        ctx.Clientes.Add(cliente);
        await ctx.SaveChangesAsync();

        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        return (cliente, new SituacionCrediticiaBcraService(
            http, new SingletonDbContextFactoryBcra(_options), NullLogger<SituacionCrediticiaBcraService>.Instance));
    }

    [Fact]
    public async Task ApiReal_CuitConDeudas_DevuelveRespuestaExitosaParseada()
    {
        if (!Habilitado) return;

        var (cliente, service) = await ArmarAsync(CuitBancoNacion);
        await service.ForzarActualizacionAsync(cliente.Id);

        await using var ctx = new AppDbContext(_options);
        var bd = await ctx.Clientes.FindAsync(cliente.Id);
        Assert.True(bd!.SituacionCrediticiaConsultaOk, bd.SituacionCrediticiaDescripcion);
        Assert.InRange(bd.SituacionCrediticiaBcra!.Value, 1, 5);
        Assert.Matches(@"^\d{6}$", bd.SituacionCrediticiaPeriodo!);
    }

    [Fact]
    public async Task ApiReal_IdentificacionInexistente_Es404InterpretadoComoSinRegistro()
    {
        if (!Habilitado) return;

        var (cliente, service) = await ArmarAsync("20000000001");
        await service.ForzarActualizacionAsync(cliente.Id);

        await using var ctx = new AppDbContext(_options);
        var bd = await ctx.Clientes.FindAsync(cliente.Id);
        Assert.True(bd!.SituacionCrediticiaConsultaOk);
        Assert.Equal("Sin registro en BCRA (no encontrado)", bd.SituacionCrediticiaDescripcion);
    }
}
