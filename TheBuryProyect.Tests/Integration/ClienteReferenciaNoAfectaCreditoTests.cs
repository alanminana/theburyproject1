using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Exceptions;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// D-02 AC-02-4 (decisión R-06, alternativa A): las referencias son informativas. La aptitud crediticia y el
/// cupo disponible (los evaluadores reales ClienteAptitudService y CreditoDisponibleService) deben dar exactamente
/// el mismo resultado con y sin referencias, en cualquier estado de verificación.
/// </summary>
public class ClienteReferenciaNoAfectaCreditoTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public ClienteReferenciaNoAfectaCreditoTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using var ctx = new AppDbContext(_options);
        ctx.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private static async Task<int> SeedClienteAsync(AppDbContext ctx, bool validarTodo)
    {
        ctx.Set<ConfiguracionCredito>().Add(new ConfiguracionCredito
        {
            ValidarDocumentacion = validarTodo,
            ValidarLimiteCredito = validarTodo,
            ValidarMora = validarTodo
        });
        var cliente = new Cliente
        {
            Nombre = "Test", Apellido = "SinImpacto", TipoDocumento = "DNI",
            NumeroDocumento = Guid.NewGuid().ToString("N")[..8], Telefono = "3415551000",
            NivelRiesgo = NivelRiesgoCredito.AprobadoTotal, PuntajeCliente = 3, Activo = true,
            CuilCuit = "20123456789", SituacionCrediticiaBcra = 1,
            SituacionCrediticiaConsultaOk = true, SituacionCrediticiaUltimaConsultaUtc = DateTime.UtcNow
        };
        ctx.Clientes.Add(cliente);
        await ctx.SaveChangesAsync();
        return cliente.Id;
    }

    private static async Task<(string Aptitud, string Disponible)> EvaluarAsync(AppDbContext ctx, int clienteId)
    {
        var creditoDisponible = new CreditoDisponibleService(ctx, NullLogger<CreditoDisponibleService>.Instance);
        var garante = new GaranteService(ctx, NullLogger<GaranteService>.Instance);
        var aptitudService = new ClienteAptitudService(ctx, NullLogger<ClienteAptitudService>.Instance, creditoDisponible, garante);

        var aptitud = await aptitudService.EvaluarAptitudSinGuardarAsync(clienteId);

        string disponible;
        try
        {
            disponible = JsonSerializer.Serialize(await creditoDisponible.CalcularDisponibleAsync(clienteId));
        }
        catch (CreditoDisponibleException ex)
        {
            disponible = "EXCEPCION:" + ex.Message;
        }

        return (SinMarcasDeTiempo(JsonSerializer.Serialize(aptitud)), SinMarcasDeTiempo(disponible));
    }

    // La evaluación sella la hora en que se hizo: es lo único que cambia entre dos corridas idénticas.
    private static string SinMarcasDeTiempo(string json) =>
        System.Text.RegularExpressions.Regex.Replace(json, @"\d{4}-\d{2}-\d{2}T[\d:.]+Z?", "<ts>");

    private static async Task AgregarReferenciasEnTodosLosEstadosAsync(AppDbContext ctx, int clienteId)
    {
        var i = 0;
        foreach (var estado in Enum.GetValues<EstadoVerificacionReferencia>())
        {
            foreach (var tipo in Enum.GetValues<TipoReferenciaCliente>())
            {
                var tel = $"34144400{++i:00}";
                ctx.ClienteReferencias.Add(new ClienteReferencia
                {
                    ClienteId = clienteId, Tipo = tipo, Nombre = $"Ref {i}", Telefono = tel, TelefonoNormalizado = tel,
                    EstadoVerificacion = estado,
                    FechaVerificacion = estado == EstadoVerificacionReferencia.Pendiente ? null : DateTime.UtcNow
                });
            }
        }
        await ctx.SaveChangesAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AptitudYCupo_SonIdenticosConYSinReferencias(bool validarTodo)
    {
        int clienteId;
        (string Aptitud, string Disponible) sin;
        await using (var ctx = new AppDbContext(_options))
        {
            clienteId = await SeedClienteAsync(ctx, validarTodo);
            sin = await EvaluarAsync(ctx, clienteId);
        }

        await using (var ctx = new AppDbContext(_options))
            await AgregarReferenciasEnTodosLosEstadosAsync(ctx, clienteId);

        await using (var ctx = new AppDbContext(_options))
        {
            var con = await EvaluarAsync(ctx, clienteId);
            Assert.Equal(sin.Aptitud, con.Aptitud);
            Assert.Equal(sin.Disponible, con.Disponible);
        }
    }

    [Fact]
    public async Task AptitudYCupo_NoCambianAlVerificarOEliminarReferencias()
    {
        int clienteId;
        (string Aptitud, string Disponible) base_;
        await using (var ctx = new AppDbContext(_options))
        {
            clienteId = await SeedClienteAsync(ctx, validarTodo: true);
            await AgregarReferenciasEnTodosLosEstadosAsync(ctx, clienteId);
            base_ = await EvaluarAsync(ctx, clienteId);
        }

        await using (var ctx = new AppDbContext(_options))
        {
            foreach (var r in await ctx.ClienteReferencias.Where(r => r.ClienteId == clienteId).ToListAsync())
            {
                r.EstadoVerificacion = EstadoVerificacionReferencia.Rechazada;
                r.IsDeleted = true;
            }
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = new AppDbContext(_options))
        {
            var despues = await EvaluarAsync(ctx, clienteId);
            Assert.Equal(base_.Aptitud, despues.Aptitud);
            Assert.Equal(base_.Disponible, despues.Disponible);
        }
    }

    [Theory]
    [InlineData("ClienteAptitudService.cs")]
    [InlineData("CreditoDisponibleService.cs")]
    [InlineData("ClienteScoringService.cs")]
    [InlineData("ClienteScoringCalculator.cs")]
    [InlineData("ValidacionVentaService.cs")]
    [InlineData("CreditoService.cs")]
    public void ContratoR06_LosEvaluadoresDeCreditoNoLeenReferencias(string archivo)
    {
        var codigo = File.ReadAllText(Path.Combine(FindRepoRoot(), "Services", archivo));

        Assert.DoesNotContain("ClienteReferencia", codigo);
        Assert.DoesNotContain("Referencias", codigo);
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TheBuryProyect.csproj")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("No se encontró la raíz del repositorio.");
    }
}
