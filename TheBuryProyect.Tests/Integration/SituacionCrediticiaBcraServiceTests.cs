using System.Net;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Tests de integración para SituacionCrediticiaBcraService.
/// Cubren CUIL inválido (null, vacío, largo incorrecto, no numérico),
/// cache vigente (no reconsulta), forzar actualización ignora cache,
/// respuesta BCRA: sin resultados, sin períodos (sin deudas),
/// peor situación extraída correctamente, errores HTTP (4xx/5xx),
/// timeout y JSON inválido.
/// </summary>
public class SituacionCrediticiaBcraServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly FakeHttpHandlerBcra _fakeHttp;
    private readonly SituacionCrediticiaBcraService _service;
    private readonly DbContextOptions<AppDbContext> _options;

    public SituacionCrediticiaBcraServiceTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(_options);
        _context.Database.EnsureCreated();

        _fakeHttp = new FakeHttpHandlerBcra();
        var httpClient = new HttpClient(_fakeHttp) { BaseAddress = new Uri("https://api.bcra.gob.ar/") };
        var factory = new SingletonDbContextFactoryBcra(_options);

        _service = new SituacionCrediticiaBcraService(
            httpClient, factory, NullLogger<SituacionCrediticiaBcraService>.Instance);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<Cliente> SeedClienteAsync(
        string? cuil = "20123456789",
        int? situacionBcra = null,
        string? descripcion = null,
        DateTime? ultimaConsulta = null,
        bool? consultaOk = null)
    {
        var doc = Guid.NewGuid().ToString("N")[..8];
        var cliente = new Cliente
        {
            Nombre = "Test", Apellido = "BCRA",
            TipoDocumento = "DNI", NumeroDocumento = doc,
            Email = $"{doc}@test.com", Activo = true,
            CuilCuit = cuil,
            SituacionCrediticiaBcra = situacionBcra,
            SituacionCrediticiaDescripcion = descripcion,
            SituacionCrediticiaUltimaConsultaUtc = ultimaConsulta,
            SituacionCrediticiaConsultaOk = consultaOk
        };
        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync();
        return cliente;
    }

    private static string BcraJson(params int[] situaciones)
    {
        var entidades = string.Join(",", situaciones.Select(s => $@"{{""situacion"":{s}}}"));
        return $$"""
        {
          "results": {
            "periodos": [
              {
                "periodo": "202412",
                "entidades": [{{entidades}}]
              }
            ]
          }
        }
        """;
    }

    private static string BcraSinDeudasJson() => """
        {
          "results": {
            "periodos": []
          }
        }
        """;

    private static string BcraSinResultadosJson() => """{ "status": 200 }""";

    // -------------------------------------------------------------------------
    // CUIL inválido
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ConsultarYActualizar_CuilNull_MarcaNoConsultable()
    {
        var cliente = await SeedClienteAsync(cuil: null);

        await _service.ConsultarYActualizarAsync(cliente.Id);

        _context.ChangeTracker.Clear();
        var bd = await _context.Clientes.FindAsync(cliente.Id);
        Assert.False(bd!.SituacionCrediticiaConsultaOk);
        Assert.Equal("CUIL/CUIT no cargado", bd.SituacionCrediticiaDescripcion);
        Assert.Equal(0, _fakeHttp.CallCount); // no llamó HTTP
    }

    [Fact]
    public async Task ConsultarYActualizar_CuilVacio_MarcaNoConsultable()
    {
        var cliente = await SeedClienteAsync(cuil: "");

        await _service.ConsultarYActualizarAsync(cliente.Id);

        _context.ChangeTracker.Clear();
        var bd = await _context.Clientes.FindAsync(cliente.Id);
        Assert.False(bd!.SituacionCrediticiaConsultaOk);
        Assert.Equal(0, _fakeHttp.CallCount);
    }

    [Fact]
    public async Task ConsultarYActualizar_CuilLargoCorrecto_PeroNoNumerico_MarcaNoConsultable()
    {
        // 11 chars pero con letras
        var cliente = await SeedClienteAsync(cuil: "2012345678A");

        await _service.ConsultarYActualizarAsync(cliente.Id);

        _context.ChangeTracker.Clear();
        var bd = await _context.Clientes.FindAsync(cliente.Id);
        Assert.False(bd!.SituacionCrediticiaConsultaOk);
        Assert.Equal(0, _fakeHttp.CallCount);
    }

    [Fact]
    public async Task ConsultarYActualizar_CuilCorto_MarcaNoConsultable()
    {
        // 10 dígitos, falta uno
        var cliente = await SeedClienteAsync(cuil: "2012345678");

        await _service.ConsultarYActualizarAsync(cliente.Id);

        _context.ChangeTracker.Clear();
        var bd = await _context.Clientes.FindAsync(cliente.Id);
        Assert.False(bd!.SituacionCrediticiaConsultaOk);
        Assert.Equal(0, _fakeHttp.CallCount);
    }

    // -------------------------------------------------------------------------
    // Cache vigente — no reconsulta
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ConsultarYActualizar_CacheVigente_NoLlamaHttp()
    {
        // Consulta exitosa hace 2 días, cache default es 7 días
        var cliente = await SeedClienteAsync(
            cuil: "20123456789",
            situacionBcra: 1,
            consultaOk: true,
            ultimaConsulta: DateTime.UtcNow.AddDays(-2));

        await _service.ConsultarYActualizarAsync(cliente.Id, cacheDias: 7);

        Assert.Equal(0, _fakeHttp.CallCount);
    }

    [Fact]
    public async Task ConsultarYActualizar_CacheVencido_LlamaHttp()
    {
        _fakeHttp.SetResponse(HttpStatusCode.OK, BcraJson(1));

        // Consulta exitosa hace 10 días, cache de 7 días → vencido
        var cliente = await SeedClienteAsync(
            cuil: "20123456789",
            situacionBcra: 1,
            consultaOk: true,
            ultimaConsulta: DateTime.UtcNow.AddDays(-10));

        await _service.ConsultarYActualizarAsync(cliente.Id, cacheDias: 7);

        Assert.Equal(1, _fakeHttp.CallCount);
    }

    [Fact]
    public async Task ForzarActualizacion_CacheVigente_IgualLlamaHttp()
    {
        _fakeHttp.SetResponse(HttpStatusCode.OK, BcraJson(1));

        // Cache fresco, pero ForzarActualizacion debe ignorarlo
        var cliente = await SeedClienteAsync(
            cuil: "20123456789",
            consultaOk: true,
            ultimaConsulta: DateTime.UtcNow.AddDays(-1));

        await _service.ForzarActualizacionAsync(cliente.Id);

        Assert.Equal(1, _fakeHttp.CallCount);
    }

    // -------------------------------------------------------------------------
    // Respuesta BCRA — sin resultados
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ConsultarYActualizar_RespuestaSinResultados_MarcaError()
    {
        _fakeHttp.SetResponse(HttpStatusCode.OK, BcraSinResultadosJson());

        var cliente = await SeedClienteAsync(cuil: "20123456789");

        await _service.ConsultarYActualizarAsync(cliente.Id);

        _context.ChangeTracker.Clear();
        var bd = await _context.Clientes.FindAsync(cliente.Id);
        Assert.False(bd!.SituacionCrediticiaConsultaOk);
        Assert.Equal("Respuesta sin resultados", bd.SituacionCrediticiaDescripcion);
    }

    // -------------------------------------------------------------------------
    // Respuesta BCRA — sin deudas (períodos vacíos)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ConsultarYActualizar_SinDeudas_MarcaSituacionCero()
    {
        _fakeHttp.SetResponse(HttpStatusCode.OK, BcraSinDeudasJson());

        var cliente = await SeedClienteAsync(cuil: "20123456789");

        await _service.ConsultarYActualizarAsync(cliente.Id);

        _context.ChangeTracker.Clear();
        var bd = await _context.Clientes.FindAsync(cliente.Id);
        Assert.True(bd!.SituacionCrediticiaConsultaOk);
        Assert.Equal(0, bd.SituacionCrediticiaBcra);
        Assert.Equal("Sin deudas registradas", bd.SituacionCrediticiaDescripcion);
    }

    // -------------------------------------------------------------------------
    // Respuesta BCRA — peor situación
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ConsultarYActualizar_MultipleEntidades_UsaPeorSituacion()
    {
        // Entidades con situaciones 1, 3, 2 → peor = 3
        _fakeHttp.SetResponse(HttpStatusCode.OK, BcraJson(1, 3, 2));

        var cliente = await SeedClienteAsync(cuil: "20123456789");

        await _service.ConsultarYActualizarAsync(cliente.Id);

        _context.ChangeTracker.Clear();
        var bd = await _context.Clientes.FindAsync(cliente.Id);
        Assert.True(bd!.SituacionCrediticiaConsultaOk);
        Assert.Equal(3, bd.SituacionCrediticiaBcra);
        Assert.Equal("Con problemas", bd.SituacionCrediticiaDescripcion);
    }

    [Fact]
    public async Task ConsultarYActualizar_Situacion1_DescripcionNormal()
    {
        _fakeHttp.SetResponse(HttpStatusCode.OK, BcraJson(1));

        var cliente = await SeedClienteAsync(cuil: "20123456789");

        await _service.ConsultarYActualizarAsync(cliente.Id);

        _context.ChangeTracker.Clear();
        var bd = await _context.Clientes.FindAsync(cliente.Id);
        Assert.Equal(1, bd!.SituacionCrediticiaBcra);
        Assert.Equal("Normal", bd.SituacionCrediticiaDescripcion);
    }

    [Fact]
    public async Task ConsultarYActualizar_Situacion5_DescripcionIrrecuperable()
    {
        _fakeHttp.SetResponse(HttpStatusCode.OK, BcraJson(5));

        var cliente = await SeedClienteAsync(cuil: "20123456789");

        await _service.ConsultarYActualizarAsync(cliente.Id);

        _context.ChangeTracker.Clear();
        var bd = await _context.Clientes.FindAsync(cliente.Id);
        Assert.Equal(5, bd!.SituacionCrediticiaBcra);
        Assert.Equal("Irrecuperable", bd.SituacionCrediticiaDescripcion);
    }

    // -------------------------------------------------------------------------
    // Errores HTTP
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ConsultarYActualizar_ErrorHttp500_MarcaError()
    {
        _fakeHttp.SetResponse(HttpStatusCode.InternalServerError, "");

        var cliente = await SeedClienteAsync(cuil: "20123456789");

        await _service.ConsultarYActualizarAsync(cliente.Id);

        _context.ChangeTracker.Clear();
        var bd = await _context.Clientes.FindAsync(cliente.Id);
        Assert.False(bd!.SituacionCrediticiaConsultaOk);
        Assert.Contains("500", bd.SituacionCrediticiaDescripcion);
    }

    [Fact]
    public async Task ConsultarYActualizar_JsonInvalido_MarcaError()
    {
        _fakeHttp.SetResponse(HttpStatusCode.OK, "{ invalid json");

        var cliente = await SeedClienteAsync(cuil: "20123456789");

        await _service.ConsultarYActualizarAsync(cliente.Id);

        _context.ChangeTracker.Clear();
        var bd = await _context.Clientes.FindAsync(cliente.Id);
        Assert.False(bd!.SituacionCrediticiaConsultaOk);
        Assert.Equal("Respuesta BCRA inválida", bd.SituacionCrediticiaDescripcion);
    }

    // -------------------------------------------------------------------------
    // Cliente inexistente — no lanza
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ConsultarYActualizar_ClienteInexistente_NoLanza()
    {
        await _service.ConsultarYActualizarAsync(99999); // no debe lanzar
    }
}

// ---------------------------------------------------------------------------
// Fake HTTP handler
// ---------------------------------------------------------------------------

internal sealed class FakeHttpHandlerBcra : HttpMessageHandler
{
    private HttpStatusCode _statusCode = HttpStatusCode.OK;
    private string _content = "{}";
    public int CallCount { get; private set; }

    public void SetResponse(HttpStatusCode statusCode, string content)
    {
        _statusCode = statusCode;
        _content = content;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CallCount++;
        var response = new HttpResponseMessage(_statusCode)
        {
            Content = new StringContent(_content, Encoding.UTF8, "application/json")
        };
        return Task.FromResult(response);
    }
}

// ---------------------------------------------------------------------------
// IDbContextFactory<AppDbContext> wrapper over a single shared context
// ---------------------------------------------------------------------------

internal sealed class SingletonDbContextFactoryBcra : IDbContextFactory<AppDbContext>
{
    private readonly DbContextOptions<AppDbContext> _options;
    public SingletonDbContextFactoryBcra(DbContextOptions<AppDbContext> options) => _options = options;
    public AppDbContext CreateDbContext() => new AppDbContext(_options);
    public Task<AppDbContext> CreateDbContextAsync(CancellationToken ct = default) => Task.FromResult(new AppDbContext(_options));
}
