using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Exceptions;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// D-02 (decisión del propietario, R-06 resuelta): para otorgar crédito personal se exige un mínimo configurable de
/// referencias VERIFICADAS (no dadas de baja). La regla vive solo en <c>ClienteAptitudService</c>: si faltan, el estado
/// es RequiereAutorizacion (nunca levanta ni reemplaza un bloqueo) y el cupo disponible no cambia.
/// </summary>
public class ClienteReferenciaCreditoReglaTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public ClienteReferenciaCreditoReglaTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using var ctx = new AppDbContext(_options);
        ctx.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private static ClienteAptitudService Servicio(AppDbContext ctx)
    {
        var creditoDisponible = new CreditoDisponibleService(ctx, NullLogger<CreditoDisponibleService>.Instance);
        var garante = new GaranteService(ctx, NullLogger<GaranteService>.Instance);
        return new ClienteAptitudService(ctx, NullLogger<ClienteAptitudService>.Instance, creditoDisponible, garante);
    }

    private static async Task<int> SeedClienteAsync(AppDbContext ctx, int minimo, bool exigirDocumentacion = false)
    {
        ctx.Set<ConfiguracionCredito>().Add(new ConfiguracionCredito
        {
            ReferenciasVerificadasMinimas = minimo,
            ValidarDocumentacion = exigirDocumentacion,
            ValidarLimiteCredito = false,
            ValidarMora = false
        });
        var cliente = new Cliente
        {
            Nombre = "Test", Apellido = "Referencias", TipoDocumento = "DNI",
            NumeroDocumento = Guid.NewGuid().ToString("N")[..8], Telefono = "3415551000",
            NivelRiesgo = NivelRiesgoCredito.AprobadoTotal, PuntajeCliente = 3, Activo = true,
            CuilCuit = "20123456789", SituacionCrediticiaBcra = 1,
            SituacionCrediticiaConsultaOk = true, SituacionCrediticiaUltimaConsultaUtc = DateTime.UtcNow
        };
        ctx.Clientes.Add(cliente);
        await ctx.SaveChangesAsync();
        return cliente.Id;
    }

    private static async Task AgregarReferenciaAsync(
        AppDbContext ctx, int clienteId, EstadoVerificacionReferencia estado, bool baja = false, int tel = 1)
    {
        var telefono = $"34144400{tel:00}";
        ctx.ClienteReferencias.Add(new ClienteReferencia
        {
            ClienteId = clienteId, Tipo = TipoReferenciaCliente.Personal, Nombre = $"Ref {tel}",
            Telefono = telefono, TelefonoNormalizado = telefono, EstadoVerificacion = estado,
            FechaVerificacion = estado == EstadoVerificacionReferencia.Pendiente ? null : DateTime.UtcNow,
            IsDeleted = baja
        });
        await ctx.SaveChangesAsync();
    }

    private async Task<AptitudCrediticiaViewModel> EvaluarAsync(int clienteId)
    {
        await using var ctx = new AppDbContext(_options);
        return await Servicio(ctx).EvaluarAptitudSinGuardarAsync(clienteId);
    }

    [Fact]
    public async Task SinReferenciasVerificadas_ConMinimoPorDefecto_RequiereAutorizacionYNoEsBloqueo()
    {
        int id;
        await using (var ctx = new AppDbContext(_options))
            id = await SeedClienteAsync(ctx, minimo: ConfiguracionCredito.ReferenciasVerificadasMinimasPorDefecto);

        var aptitud = await EvaluarAsync(id);

        Assert.Equal(EstadoCrediticioCliente.RequiereAutorizacion, aptitud.Estado);
        Assert.True(aptitud.Referencias.Evaluada);
        Assert.Equal(1, aptitud.Referencias.Requeridas);
        Assert.Equal(0, aptitud.Referencias.Verificadas);
        Assert.False(aptitud.Referencias.Cumple);
        var detalle = Assert.Single(aptitud.Detalles, d => d.Categoria == "Referencias");
        Assert.False(detalle.EsBloqueo);
        Assert.Contains("0 de 1", detalle.Descripcion);
    }

    [Fact]
    public async Task ConUnaReferenciaVerificada_ElClienteEsApto()
    {
        int id;
        await using (var ctx = new AppDbContext(_options))
        {
            id = await SeedClienteAsync(ctx, minimo: 1);
            await AgregarReferenciaAsync(ctx, id, EstadoVerificacionReferencia.Verificada);
        }

        var aptitud = await EvaluarAsync(id);

        Assert.Equal(EstadoCrediticioCliente.Apto, aptitud.Estado);
        Assert.True(aptitud.Referencias.Cumple);
        Assert.Equal(1, aptitud.Referencias.Verificadas);
        Assert.DoesNotContain(aptitud.Detalles, d => d.Categoria == "Referencias");
    }

    [Theory]
    [InlineData(EstadoVerificacionReferencia.Pendiente)]
    [InlineData(EstadoVerificacionReferencia.NoLocalizada)]
    [InlineData(EstadoVerificacionReferencia.Rechazada)]
    public async Task SoloCuentanLasVerificadas_LasDemasEstadosNoAlcanzan(EstadoVerificacionReferencia estado)
    {
        int id;
        await using (var ctx = new AppDbContext(_options))
        {
            id = await SeedClienteAsync(ctx, minimo: 1);
            await AgregarReferenciaAsync(ctx, id, estado);
        }

        var aptitud = await EvaluarAsync(id);

        Assert.Equal(EstadoCrediticioCliente.RequiereAutorizacion, aptitud.Estado);
        Assert.Equal(0, aptitud.Referencias.Verificadas);
    }

    [Fact]
    public async Task UnaReferenciaVerificadaDadaDeBaja_NoCuenta()
    {
        int id;
        await using (var ctx = new AppDbContext(_options))
        {
            id = await SeedClienteAsync(ctx, minimo: 1);
            await AgregarReferenciaAsync(ctx, id, EstadoVerificacionReferencia.Verificada, baja: true);
        }

        var aptitud = await EvaluarAsync(id);

        Assert.Equal(EstadoCrediticioCliente.RequiereAutorizacion, aptitud.Estado);
        Assert.Equal(0, aptitud.Referencias.Verificadas);
    }

    [Fact]
    public async Task LasReferenciasDeOtroCliente_NoCuentan()
    {
        int id;
        await using (var ctx = new AppDbContext(_options))
        {
            id = await SeedClienteAsync(ctx, minimo: 1);
            var otro = new Cliente
            {
                Nombre = "Otro", Apellido = "Cliente", TipoDocumento = "DNI",
                NumeroDocumento = Guid.NewGuid().ToString("N")[..8], Telefono = "3415552000",
                NivelRiesgo = NivelRiesgoCredito.AprobadoTotal, PuntajeCliente = 3, Activo = true
            };
            ctx.Clientes.Add(otro);
            await ctx.SaveChangesAsync();
            await AgregarReferenciaAsync(ctx, otro.Id, EstadoVerificacionReferencia.Verificada);
        }

        var aptitud = await EvaluarAsync(id);

        Assert.Equal(0, aptitud.Referencias.Verificadas);
        Assert.Equal(EstadoCrediticioCliente.RequiereAutorizacion, aptitud.Estado);
    }

    [Theory]
    [InlineData(2, 1, false)]
    [InlineData(2, 2, true)]
    [InlineData(3, 5, true)]
    [InlineData(10, 9, false)]
    [InlineData(10, 10, true)]
    public async Task ElMinimoEsConfigurable(int minimo, int verificadas, bool cumple)
    {
        int id;
        await using (var ctx = new AppDbContext(_options))
        {
            id = await SeedClienteAsync(ctx, minimo);
            for (var i = 1; i <= verificadas; i++)
                await AgregarReferenciaAsync(ctx, id, EstadoVerificacionReferencia.Verificada, tel: i);
        }

        var aptitud = await EvaluarAsync(id);

        Assert.Equal(cumple, aptitud.Referencias.Cumple);
        Assert.Equal(cumple ? EstadoCrediticioCliente.Apto : EstadoCrediticioCliente.RequiereAutorizacion, aptitud.Estado);
        Assert.Equal(minimo, aptitud.Referencias.Requeridas);
    }

    [Fact]
    public async Task ConMinimoCero_LaReglaEstaDesactivada()
    {
        int id;
        await using (var ctx = new AppDbContext(_options))
            id = await SeedClienteAsync(ctx, minimo: 0);

        var aptitud = await EvaluarAsync(id);

        Assert.Equal(EstadoCrediticioCliente.Apto, aptitud.Estado);
        Assert.False(aptitud.Referencias.Evaluada);
        Assert.True(aptitud.Referencias.Cumple);
        Assert.DoesNotContain(aptitud.Detalles, d => d.Categoria == "Referencias");
    }

    [Fact]
    public async Task FaltanReferencias_PeroHayUnBloqueo_ElClienteSigueNoApto()
    {
        int id;
        await using (var ctx = new AppDbContext(_options))
            id = await SeedClienteAsync(ctx, minimo: 1, exigirDocumentacion: true); // sin documentos: bloqueo duro

        var aptitud = await EvaluarAsync(id);

        Assert.Equal(EstadoCrediticioCliente.NoApto, aptitud.Estado);
        Assert.Contains(aptitud.Detalles, d => d.Categoria == "Documentación" && d.EsBloqueo);
        Assert.Contains(aptitud.Detalles, d => d.Categoria == "Referencias" && !d.EsBloqueo);
    }

    [Fact]
    public async Task ElCupoDisponible_NoDependeDeLasReferencias()
    {
        int id;
        string sin;
        await using (var ctx = new AppDbContext(_options))
        {
            id = await SeedClienteAsync(ctx, minimo: 1);
            sin = await DisponibleAsync(ctx, id);
            await AgregarReferenciaAsync(ctx, id, EstadoVerificacionReferencia.Verificada);
        }

        await using var ctx2 = new AppDbContext(_options);
        Assert.Equal(sin, await DisponibleAsync(ctx2, id));
    }

    private static async Task<string> DisponibleAsync(AppDbContext ctx, int clienteId)
    {
        var servicio = new CreditoDisponibleService(ctx, NullLogger<CreditoDisponibleService>.Instance);
        try
        {
            return JsonSerializer.Serialize(await servicio.CalcularDisponibleAsync(clienteId));
        }
        catch (CreditoDisponibleException ex)
        {
            return "EXCEPCION:" + ex.Message;
        }
    }

    [Fact]
    public async Task ActualizarElMinimo_ValidaRangoYPersiste()
    {
        await using var ctx = new AppDbContext(_options);
        await SeedClienteAsync(ctx, minimo: 1);
        var servicio = Servicio(ctx);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            servicio.UpdateReferenciasCreditoAsync(new ReferenciasCreditoViewModel { ReferenciasVerificadasMinimas = -1 }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            servicio.UpdateReferenciasCreditoAsync(new ReferenciasCreditoViewModel { ReferenciasVerificadasMinimas = 11 }));
        Assert.Equal(1, (await servicio.GetReferenciasCreditoAsync()).ReferenciasVerificadasMinimas);

        await servicio.UpdateReferenciasCreditoAsync(new ReferenciasCreditoViewModel { ReferenciasVerificadasMinimas = 0 });
        Assert.Equal(0, (await servicio.GetReferenciasCreditoAsync()).ReferenciasVerificadasMinimas);
        await servicio.UpdateReferenciasCreditoAsync(new ReferenciasCreditoViewModel { ReferenciasVerificadasMinimas = 10 });
        Assert.Equal(10, (await servicio.GetReferenciasCreditoAsync()).ReferenciasVerificadasMinimas);
    }

    [Theory]
    [InlineData("CreditoDisponibleService.cs")]
    [InlineData("ClienteScoringService.cs")]
    [InlineData("ClienteScoringCalculator.cs")]
    [InlineData("ValidacionVentaService.cs")]
    [InlineData("VentaService.cs")]
    [InlineData("CotizacionConversionService.cs")]
    [InlineData("CreditoService.cs")]
    public void FuenteUnica_SoloElServicioDeAptitudLeeLasReferencias(string archivo)
    {
        var codigo = File.ReadAllText(Path.Combine(FindRepoRoot(), "Services", archivo));

        Assert.DoesNotContain("ClienteReferencias", codigo);
        Assert.DoesNotContain("ClienteReferencia ", codigo);
    }

    [Fact]
    public void FuenteUnica_ElServicioDeAptitudCuentaSoloVerificadasNoEliminadas()
    {
        var codigo = File.ReadAllText(Path.Combine(FindRepoRoot(), "Services", "ClienteAptitudService.cs"));

        Assert.Contains("r.EstadoVerificacion == EstadoVerificacionReferencia.Verificada", codigo);
        Assert.Contains("!r.IsDeleted", codigo);
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
