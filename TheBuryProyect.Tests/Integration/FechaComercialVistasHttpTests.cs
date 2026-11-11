using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Tests.Helpers;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Reloj fijo en la franja crítica: 2026-11-11 01:30 UTC = 2026-11-10 22:30 en Argentina (UTC-3).
/// El día UTC ya es el 11 pero el día comercial sigue siendo el 10.
/// </summary>
public sealed class FactoryRelojFranjaCritica : CustomWebApplicationFactory
{
    public static readonly DateTimeOffset InstanteUtc = new(2026, 11, 11, 1, 30, 0, TimeSpan.Zero);

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new TimeProviderFijo(InstanteUtc));
        });
    }
}

/// <summary>
/// F5 — las vistas calculan «hoy» con el día comercial (IRelojComercial) y no con la fecha del servidor (UTC en el
/// contenedor). Entre las 21:00 y las 24:00 de Argentina el día UTC ya es el de mañana: el <c>min</c> de un
/// input date quedaba en mañana (el navegador impedía elegir hoy) y los valores por defecto salían corridos un día.
/// </summary>
[Collection("HttpIntegration.4")]
public class FechaComercialVistasHttpTests : IClassFixture<FactoryRelojFranjaCritica>
{
    private const string TestUser = "testuser";
    private const string HoyComercial = "2026-11-10";
    private const string DiaUtc = "2026-11-11";

    private readonly FactoryRelojFranjaCritica _factory;

    public FechaComercialVistasHttpTests(FactoryRelojFranjaCritica factory) => _factory = factory;

    private async Task<string> GetHtmlAsync(string url)
    {
        await _factory.SeedTestUserAsync();
        var client = _factory.CreateAuthenticatedClient();
        var response = await client.GetAsync(url);
        var html = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"GET {url} → {(int)response.StatusCode}\n{html}");
        return WebUtility.HtmlDecode(html);
    }

    private static string Tag(string html, string id)
    {
        var m = Regex.Match(html, "<input[^>]*\\bid=\"" + Regex.Escape(id) + "\"[^>]*>", RegexOptions.Singleline);
        Assert.True(m.Success, $"No existe #{id}.");
        return m.Value;
    }

    private static string Attr(string tag, string name)
    {
        var m = Regex.Match(tag, "\\b" + name + "=\"([^\"]*)\"");
        return m.Success ? m.Groups[1].Value : "";
    }

    [Fact]
    public async Task Cotizador_ValidaHasta_MinEsElDiaComercialNoElDiaUtc()
    {
        var html = await GetHtmlAsync("/Cotizacion");

        Assert.Equal(HoyComercial, Attr(Tag(html, "cotizacion-fecha-vencimiento"), "min"));
        Assert.DoesNotContain($"min=\"{DiaUtc}\"", html);
    }

    [Fact]
    public async Task VentaCreate_FechasPorDefecto_SonElDiaComercial()
    {
        await _factory.SeedTestUserAsync();
        await SembrarCajaAbiertaAsync();

        var html = await GetHtmlAsync("/Venta/Create");

        Assert.Equal(HoyComercial, Attr(Tag(html, "txt-fecha-vencimiento-cheque"), "min"));
        var fechaVenta = Regex.Match(html, @"<input[^>]*\bname=""FechaVenta""[^>]*>", RegexOptions.Singleline).Value;
        Assert.Equal(HoyComercial, Attr(fechaVenta, "min"));
        Assert.DoesNotContain($"min=\"{DiaUtc}\"", html);
    }

    [Fact]
    public async Task MoraFichaCliente_Promesa_MinYValorSonElDiaComercial()
    {
        var clienteId = await SembrarClienteEnMoraAsync();

        var html = await GetHtmlAsync($"/Mora/FichaCliente/{clienteId}");

        var promesa = Tag(html, "pr-fecha");
        Assert.Equal(HoyComercial, Attr(promesa, "min"));
        Assert.Equal("2026-11-17", Attr(promesa, "value"));

        var acuerdo = Tag(html, "ac-primera");
        Assert.Equal("2026-12-10", Attr(acuerdo, "value"));
    }

    private async Task SembrarCajaAbiertaAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await using var context = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        if (await context.AperturasCaja.AnyAsync(a => !a.Cerrada && a.UsuarioApertura == TestUser)) return;

        var caja = new Caja { Codigo = "CAJA-F5", Nombre = "Caja F5", IsDeleted = false, RowVersion = new byte[8] };
        context.Cajas.Add(caja);
        await context.SaveChangesAsync();
        context.AperturasCaja.Add(new AperturaCaja
        {
            CajaId = caja.Id, UsuarioApertura = TestUser, MontoInicial = 0m, Cerrada = false, IsDeleted = false, RowVersion = new byte[8]
        });
        await context.SaveChangesAsync();
    }

    private async Task<int> SembrarClienteEnMoraAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await using var context = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();

        var cliente = new Cliente
        {
            Nombre = "Fecha", Apellido = "Comercial", TipoDocumento = "DNI",
            NumeroDocumento = Guid.NewGuid().ToString("N")[..8], Email = "fecha-comercial@test.com", Telefono = "3511234567"
        };
        context.Set<Cliente>().Add(cliente);
        await context.SaveChangesAsync();

        var credito = new Credito
        {
            Numero = Guid.NewGuid().ToString("N")[..10], ClienteId = cliente.Id, Estado = EstadoCredito.Activo,
            MontoSolicitado = 10_000m, MontoAprobado = 10_000m, SaldoPendiente = 5_000m, TasaInteres = 3m,
            CantidadCuotas = 12, FechaSolicitud = FactoryRelojFranjaCritica.InstanteUtc.UtcDateTime.AddMonths(-3)
        };
        context.Set<Credito>().Add(credito);
        await context.SaveChangesAsync();

        context.Set<Cuota>().Add(new Cuota
        {
            CreditoId = credito.Id, NumeroCuota = 1, MontoCapital = 800m, MontoInteres = 200m, MontoTotal = 1_000m,
            FechaVencimiento = new DateTime(2026, 10, 20), Estado = EstadoCuota.Pendiente
        });
        context.Set<AlertaCobranza>().Add(new AlertaCobranza
        {
            ClienteId = cliente.Id, CreditoId = credito.Id, Prioridad = PrioridadAlerta.Alta,
            Tipo = TipoAlertaCobranza.CuotaVencida, Mensaje = "Cuota vencida de prueba", MontoVencido = 1_000m,
            CuotasVencidas = 1, DiasAtraso = 20, FechaAlerta = FactoryRelojFranjaCritica.InstanteUtc.UtcDateTime,
            EstadoGestion = EstadoGestionCobranza.Pendiente
        });
        await context.SaveChangesAsync();
        return cliente.Id;
    }
}
