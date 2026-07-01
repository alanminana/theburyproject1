using System.Net;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// FASE 4C-A: en Cliente/Details y RecalcularAptitud, la aptitud crediticia debe
/// evaluarse DESPUÉS de refrescar BCRA en la misma operación. Antes del fix, un
/// cliente recién consultado seguía viendo el estado bloqueante "sin consulta
/// registrada" hasta un segundo GET. Estos tests reproducen el orden a nivel de
/// servicio (BCRA → Aptitud) que el controller ahora respeta.
/// </summary>
public class ClienteDetalleBcraAptitudOrderTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public ClienteDetalleBcraAptitudOrderTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var ctx = new AppDbContext(_options);
        ctx.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private static ClienteAptitudService BuildAptitudService(AppDbContext ctx)
    {
        var creditoDisponible = new CreditoDisponibleService(ctx, NullLogger<CreditoDisponibleService>.Instance);
        var garanteService = new GaranteService(ctx, NullLogger<GaranteService>.Instance);
        return new ClienteAptitudService(ctx, NullLogger<ClienteAptitudService>.Instance, creditoDisponible, garanteService);
    }

    private static async Task<int> SeedClienteNuncaConsultadoAsync(AppDbContext ctx)
    {
        // Solo BCRA queda activo: aísla el efecto del orden BCRA→Aptitud del resto
        // de las dimensiones de aptitud (documentación/cupo/mora).
        ctx.Set<ConfiguracionCredito>().Add(new ConfiguracionCredito
        {
            ValidarDocumentacion = false,
            ValidarLimiteCredito = false,
            ValidarMora = false
        });

        var cliente = new Cliente
        {
            Nombre = "Test",
            Apellido = "BcraFresco",
            TipoDocumento = "DNI",
            NumeroDocumento = "10999001",
            NivelRiesgo = NivelRiesgoCredito.AprobadoTotal,
            IsDeleted = false,
            RowVersion = new byte[8],
            CuilCuit = "20123456789",
            SituacionCrediticiaBcra = null,
            SituacionCrediticiaConsultaOk = null,
            SituacionCrediticiaUltimaConsultaUtc = null
        };
        ctx.Clientes.Add(cliente);
        await ctx.SaveChangesAsync();
        return cliente.Id;
    }

    [Fact]
    public async Task ClienteNuncaConsultado_SinRefrescarBcraAntes_QuedaNoAptoPorFaltaDeConsulta()
    {
        using var ctx = new AppDbContext(_options);
        var clienteId = await SeedClienteNuncaConsultadoAsync(ctx);

        var aptitudService = BuildAptitudService(ctx);
        var resultado = await aptitudService.EvaluarAptitudSinGuardarAsync(clienteId);

        Assert.Equal(EstadoCrediticioCliente.NoApto, resultado.Estado);
        Assert.True(resultado.Bcra.EsBloqueante);
    }

    [Fact]
    public async Task ClienteNuncaConsultado_RefrescandoBcraAntesDeEvaluar_AptitudReflejaDatoFrescoEnLaMismaOperacion()
    {
        var fakeHttp = new FakeHttpHandlerBcra();
        fakeHttp.SetResponse(HttpStatusCode.NotFound, """{ "status": 404 }""");
        var httpClient = new HttpClient(fakeHttp) { BaseAddress = new Uri("https://api.bcra.gob.ar/") };
        var factory = new SingletonDbContextFactoryBcra(_options);
        var bcraService = new SituacionCrediticiaBcraService(
            httpClient, factory, NullLogger<SituacionCrediticiaBcraService>.Instance);

        using var ctx = new AppDbContext(_options);
        var clienteId = await SeedClienteNuncaConsultadoAsync(ctx);

        // Orden FASE 4C-A: refrescar BCRA primero...
        await bcraService.ConsultarYObtenerAsync(clienteId);

        // ...evaluar aptitud después, en la misma operación.
        var aptitudService = BuildAptitudService(ctx);
        var resultado = await aptitudService.EvaluarAptitudSinGuardarAsync(clienteId);

        Assert.Equal(1, fakeHttp.CallCount);
        Assert.False(resultado.Bcra.EsBloqueante);
        Assert.NotEqual(EstadoCrediticioCliente.NoApto, resultado.Estado);
    }
}
