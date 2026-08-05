using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Tests.Integration;

[Collection("HttpIntegration")]
public class CreditoPunitorioDetalleHttpTests : IClassFixture<CustomWebApplicationFactory>
{
    private const int ClienteId = 998_810;
    private const int CreditoId = 998_811;
    private const int CuotaId = 998_812;
    private const int AplicacionId = 998_813;
    private const int PagoId = 998_814;

    private readonly CustomWebApplicationFactory _factory;

    public CreditoPunitorioDetalleHttpTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task UsuarioConCreditosView_ObtieneDetalleReadOnlyConHistorialIncompleto()
    {
        await SeedAsync();
        await _factory.SeedUserWithCreditoViewPermissionAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.CreditoViewPermsUserId);

        var response = await client.GetAsync(Url());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Capital pendiente", html);
        Assert.Contains("Punitorio calculado hoy", html);
        Assert.Contains("Punitorio aplicado pendiente", html);
        Assert.Contains("Total cobrable actual", html);
        Assert.Contains("Historial incompleto", html);
        Assert.Contains("No reconstruible", html);
        Assert.Contains("Aplicaciones", html);
        Assert.Contains("Pagos", html);
        Assert.DoesNotContain("SnapshotJson", html);
        Assert.DoesNotContain("DesgloseSnapshotJson", html);
    }

    [Fact]
    public async Task UsuarioSinPermiso_Recibe403()
    {
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.NoPermsUserId);

        var response = await client.GetAsync(Url());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CuotaInexistente_Devuelve404()
    {
        await _factory.SeedUserWithCreditoViewPermissionAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.CreditoViewPermsUserId);

        var response = await client.GetAsync($"/Credito/{CreditoId}/Cuotas/404/Punitorio");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CuotaDeOtroCredito_Devuelve404()
    {
        await SeedAsync();
        await _factory.SeedUserWithCreditoViewPermissionAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.CreditoViewPermsUserId);

        var response = await client.GetAsync($"/Credito/{CreditoId + 1}/Cuotas/{CuotaId}/Punitorio");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task MultiplesRequests_NoPersistenNiConsultanElMontoPunitorioLegacy()
    {
        await SeedAsync();
        await _factory.SeedUserWithCreditoViewPermissionAsync();
        var before = await SnapshotAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.CreditoViewPermsUserId);

        var first = await client.GetAsync(Url());
        var second = await client.GetAsync(Url());

        first.EnsureSuccessStatusCode();
        second.EnsureSuccessStatusCode();
        var after = await SnapshotAsync();
        Assert.Equal(before, after);
        Assert.Equal(999_999m, after.MontoPunitorioLegacy);
    }

    private static string Url() => $"/Credito/{CreditoId}/Cuotas/{CuotaId}/Punitorio";

    private async Task SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();

        if (await context.Cuotas.AnyAsync(c => c.Id == CuotaId))
            return;

        var cliente = new Cliente
        {
            Id = ClienteId,
            TipoDocumento = "DNI",
            NumeroDocumento = "PUNML9B2-HTTP",
            Apellido = "Punitorio",
            Nombre = "HTTP",
            NombreCompleto = "Punitorio HTTP",
            Telefono = "0000",
            Domicilio = "Base de integración",
            Activo = true
        };
        var credito = new Credito
        {
            Id = CreditoId,
            ClienteId = ClienteId,
            Cliente = cliente,
            Numero = "PUN-ML9-B2-HTTP",
            MontoSolicitado = 100_000m,
            MontoAprobado = 100_000m,
            TasaInteres = 10m,
            CantidadCuotas = 1,
            MontoCuota = 110_000m,
            TotalAPagar = 110_000m,
            SaldoPendiente = 90_000m,
            Estado = EstadoCredito.Activo,
            FechaSolicitud = DateTime.UtcNow.AddMonths(-3),
            PuntajeRiesgoInicial = 100m
        };
        var cuota = new Cuota
        {
            Id = CuotaId,
            CreditoId = CreditoId,
            Credito = credito,
            NumeroCuota = 1,
            MontoCapital = 100_000m,
            MontoInteres = 10_000m,
            MontoTotal = 110_000m,
            MontoPagado = 20_000m,
            MontoPunitorio = 999_999m,
            FechaVencimiento = DateTime.UtcNow.AddMonths(-2),
            Estado = EstadoCuota.Parcial
        };
        var aplicacion = new PunitorioAplicado
        {
            Id = AplicacionId,
            CuotaId = CuotaId,
            Cuota = cuota,
            FechaCalculo = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10)),
            SaldoBase = 90_000m,
            DiasComputados = 30,
            Importe = 5_000m,
            Estado = EstadoPunitorioAplicado.Aplicado,
            DesgloseSnapshotJson = "{}",
            MotivoAplicacion = "Aplicación de integración",
            FechaAplicacion = DateTime.UtcNow.AddDays(-10),
            UsuarioAplicacion = "integration"
        };
        var pago = new PagoCuota
        {
            Id = PagoId,
            CuotaId = CuotaId,
            Cuota = cuota,
            PunitorioAplicadoId = AplicacionId,
            PunitorioAplicado = aplicacion,
            FechaPagoComercial = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-5)),
            ImporteTotal = 20_000m,
            ImporteAplicadoCuota = null,
            ImporteAplicadoPunitorio = null,
            MedioPago = "Efectivo",
            Origen = OrigenPagoCuota.BackfillIncompleto,
            Estado = EstadoPagoCuota.Aplicado,
            HistorialCompleto = false,
            MotivoIncompleto = "No se pudo reconstruir la composición histórica."
        };

        context.AddRange(cliente, credito, cuota, aplicacion, pago);
        await context.SaveChangesAsync();
    }

    private async Task<ReadOnlySnapshot> SnapshotAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();

        var cuota = await context.Cuotas
            .AsNoTracking()
            .Where(c => c.Id == CuotaId)
            .Select(c => new { c.MontoPagado, c.MontoPunitorio, c.Estado, c.UpdatedAt })
            .SingleAsync();

        return new ReadOnlySnapshot(
            cuota.MontoPagado,
            cuota.MontoPunitorio,
            cuota.Estado,
            cuota.UpdatedAt,
            await context.PunitoriosAplicados.CountAsync(p => p.CuotaId == CuotaId),
            await context.PagosCuota.CountAsync(p => p.CuotaId == CuotaId));
    }

    private sealed record ReadOnlySnapshot(
        decimal MontoPagado,
        decimal MontoPunitorioLegacy,
        EstadoCuota Estado,
        DateTime? UpdatedAt,
        int Aplicaciones,
        int Pagos);
}
