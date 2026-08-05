using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Tests.Integration;

[Collection("HttpIntegration")]
public sealed class CreditoPagoCuotaIndividualHttpTests : IClassFixture<CustomWebApplicationFactory>
{
    private const int BaseId = 998_840;
    private readonly CustomWebApplicationFactory _factory;

    public CreditoPagoCuotaIndividualHttpTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task MatrizPermisos_ExigeCreditoViewYPayInstallmentEnGetYPost()
    {
        var escenario = await SeedAsync(1);
        await _factory.SeedUserWithCreditoViewPermissionAsync();
        await _factory.SeedUserWithCreditoPayBothPermissionsAsync();
        await _factory.SeedUserWithCreditoPayOnlyPermissionAsync();

        using var ambos = _factory.CreateClientWithUserId(CustomWebApplicationFactory.CreditoPayBothPermsUserId);
        using var soloVista = _factory.CreateClientWithUserId(CustomWebApplicationFactory.CreditoViewPermsUserId);
        using var soloPago = _factory.CreateClientWithUserId(CustomWebApplicationFactory.CreditoPayOnlyPermsUserId);

        Assert.Equal(HttpStatusCode.OK, (await ambos.GetAsync(PagoUrl(escenario.CuotaId))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await soloVista.GetAsync(PagoUrl(escenario.CuotaId))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await soloPago.GetAsync(PagoUrl(escenario.CuotaId))).StatusCode);

        var details = await soloVista.GetAsync($"/Credito/Details/{escenario.CreditoId}");
        Assert.Equal(HttpStatusCode.OK, details.StatusCode);
        Assert.DoesNotContain(PagoUrl(escenario.CuotaId), await details.Content.ReadAsStringAsync());

        using var postVacio = new FormUrlEncodedContent(Array.Empty<KeyValuePair<string, string>>());
        Assert.Equal(HttpStatusCode.Forbidden, (await soloVista.PostAsync(PagoUrl(escenario.CuotaId), postVacio)).StatusCode);
        using var postVacioSoloPago = new FormUrlEncodedContent(Array.Empty<KeyValuePair<string, string>>());
        Assert.Equal(HttpStatusCode.Forbidden, (await soloPago.PostAsync(PagoUrl(escenario.CuotaId), postVacioSoloPago)).StatusCode);
    }

    [Fact]
    public async Task Get_RenderizaSoloCamposDeIntencionYContextoSeparado()
    {
        var escenario = await SeedAsync(2, montoPunitorioLegacy: 777m);
        await _factory.SeedUserWithCreditoPayBothPermissionsAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.CreditoPayBothPermsUserId);

        var response = await client.GetAsync(PagoUrl(escenario.CuotaId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        foreach (var permitido in new[]
                 {
                     "Input.MontoIngresado", "Input.MedioPago", "Input.Comprobante",
                     "Input.Observaciones", "Input.CuotaRowVersionBase64"
                 })
            Assert.Contains($"name=\"{permitido}\"", html);

        Assert.DoesNotContain("name=\"FechaPago\"", html);
        Assert.DoesNotContain("name=\"CreditoId\"", html);
        Assert.DoesNotContain("name=\"CuotaId\"", html);
        Assert.DoesNotContain("name=\"MontoPunitorio\"", html);
        Assert.Contains("Capital pendiente", html);
        Assert.Contains("Punitorio calculado", html);
        Assert.Contains("Punitorio aplicado pendiente", html);
        Assert.Contains("Total cobrable actual", html);
        Assert.DoesNotContain("777,00", html);
    }

    [Fact]
    public async Task Preview_UsaAntiforgeryYNoPersiste()
    {
        var escenario = await SeedAsync(3);
        await _factory.SeedUserWithCreditoPayBothPermissionsAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.CreditoPayBothPermsUserId);
        var html = await GetHtmlAsync(client, escenario.CuotaId);
        var before = await SnapshotAsync(escenario.CuotaId);

        using var request = Formulario(html, "125.00");
        var response = await client.PostAsync($"{PagoUrl(escenario.CuotaId)}/Preview", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(0m, json.RootElement.GetProperty("aplicadoPunitorio").GetDecimal());
        Assert.Equal(125m, json.RootElement.GetProperty("aplicadoCapital").GetDecimal());
        Assert.Equal(125m, json.RootElement.GetProperty("totalCaja").GetDecimal());
        Assert.Equal(before, await SnapshotAsync(escenario.CuotaId));
    }

    [Fact]
    public async Task Post_RowVersionInvalida_Devuelve400SinPersistir()
    {
        var escenario = await SeedAsync(4);
        await _factory.SeedUserWithCreditoPayBothPermissionsAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.CreditoPayBothPermsUserId);
        var html = await GetHtmlAsync(client, escenario.CuotaId);
        var before = await SnapshotAsync(escenario.CuotaId);

        using var request = Formulario(html, "100.00", rowVersion: "no-es-base64");
        var response = await client.PostAsync(PagoUrl(escenario.CuotaId), request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await SnapshotAsync(escenario.CuotaId));
    }

    [Fact]
    public async Task Post_DobleEnvioParcialConMismoToken_Devuelve409YNoDuplica()
    {
        var escenario = await SeedAsync(5);
        await _factory.SeedUserWithCreditoPayBothPermissionsAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.CreditoPayBothPermsUserId);
        var html = await GetHtmlAsync(client, escenario.CuotaId);

        using var primero = Formulario(html, "100.00");
        var firstResponse = await client.PostAsync(PagoUrl(escenario.CuotaId), primero);
        using var segundo = Formulario(html, "100.00");
        var secondResponse = await client.PostAsync(PagoUrl(escenario.CuotaId), segundo);

        Assert.Equal(HttpStatusCode.Redirect, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
        var snapshot = await SnapshotAsync(escenario.CuotaId);
        Assert.Equal(100m, snapshot.MontoPagado);
        Assert.Equal(1, snapshot.Pagos);
        Assert.Equal(1, snapshot.MovimientosCaja);
    }

    [Fact]
    public async Task CuotaInexistente_Devuelve404()
    {
        await _factory.SeedUserWithCreditoPayBothPermissionsAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.CreditoPayBothPermsUserId);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(PagoUrl(404_404))).StatusCode);
    }

    // =========================================================================
    // PUN-ML9-D.1 (riesgo 3) — returnUrl
    // =========================================================================

    [Fact]
    public async Task Get_ReturnUrlLocal_SePropagaAlFormularioYAlEnlaceVolver()
    {
        var escenario = await SeedAsync(6);
        await _factory.SeedUserWithCreditoPayBothPermissionsAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.CreditoPayBothPermsUserId);
        var origen = $"/Credito/Details/{escenario.CreditoId}";

        var html = await GetHtmlAsync(client, escenario.CuotaId, origen);

        Assert.Contains($"<input type=\"hidden\" name=\"returnUrl\" value=\"{origen}\"", html);
        Assert.Contains($"href=\"{origen}\"", html);
    }

    [Fact]
    public async Task Get_ReturnUrlExterna_SeIgnoraYUsaFallbackCanonico()
    {
        var escenario = await SeedAsync(7);
        await _factory.SeedUserWithCreditoPayBothPermissionsAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.CreditoPayBothPermsUserId);
        const string externa = "https://evil.example.com/robar";

        var html = await GetHtmlAsync(client, escenario.CuotaId, externa);

        Assert.DoesNotContain("evil.example.com", html);
        Assert.DoesNotContain("name=\"returnUrl\"", html);
        Assert.Contains($"href=\"/Credito/Details/{escenario.CreditoId}\"", html);
    }

    [Fact]
    public async Task Post_Exitoso_ConReturnUrlLocal_RedirigeConservandolo()
    {
        var escenario = await SeedAsync(8);
        await _factory.SeedUserWithCreditoPayBothPermissionsAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.CreditoPayBothPermsUserId);
        var origen = $"/Credito/Details/{escenario.CreditoId}";
        var html = await GetHtmlAsync(client, escenario.CuotaId, origen);

        using var request = Formulario(html, "100.00", returnUrl: origen);
        var response = await client.PostAsync(PagoUrl(escenario.CuotaId), request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!;
        var query = ParseQuery(location);
        Assert.Equal(origen, query["returnUrl"].ToString());

        var segunda = await client.GetAsync(location);
        var htmlFinal = await segunda.Content.ReadAsStringAsync();
        Assert.Contains($"href=\"{origen}\"", htmlFinal);
    }

    [Fact]
    public async Task Post_Exitoso_ConReturnUrlExterna_NuncaRedirigeAUrlExterna()
    {
        var escenario = await SeedAsync(9);
        await _factory.SeedUserWithCreditoPayBothPermissionsAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.CreditoPayBothPermsUserId);
        var html = await GetHtmlAsync(client, escenario.CuotaId);

        using var request = Formulario(html, "100.00", returnUrl: "https://evil.example.com/robar");
        var response = await client.PostAsync(PagoUrl(escenario.CuotaId), request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!;
        Assert.DoesNotContain("evil.example.com", location.ToString());
        // returnUrl inválido (no local) -> GetSafeReturnUrl devuelve null -> el route value se omite.
        Assert.False(ParseQuery(location).ContainsKey("returnUrl"));
    }

    [Fact]
    public async Task Post_Rechazado400_ConservaReturnUrl()
    {
        var escenario = await SeedAsync(10);
        await _factory.SeedUserWithCreditoPayBothPermissionsAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.CreditoPayBothPermsUserId);
        var origen = "/Credito/CuotasVencidas";
        var html = await GetHtmlAsync(client, escenario.CuotaId, origen);

        using var request = Formulario(html, "100.00", rowVersion: "no-es-base64", returnUrl: origen);
        var response = await client.PostAsync(PagoUrl(escenario.CuotaId), request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var htmlError = await response.Content.ReadAsStringAsync();
        Assert.Contains($"<input type=\"hidden\" name=\"returnUrl\" value=\"{origen}\"", htmlError);
    }

    private static string PagoUrl(int cuotaId) => $"/Credito/PagarCuota/{cuotaId}";

    /// <summary>Location del redirect puede ser relativo (Uri.IsAbsoluteUri=false no soporta .Query).</summary>
    private static Dictionary<string, string> ParseQuery(Uri location)
    {
        var raw = location.OriginalString;
        var indice = raw.IndexOf('?');
        if (indice < 0)
            return new Dictionary<string, string>();

        return QueryHelpers.ParseQuery(raw[indice..])
            .ToDictionary(kv => kv.Key, kv => kv.Value.ToString());
    }

    private async Task<string> GetHtmlAsync(HttpClient client, int cuotaId, string returnUrl)
    {
        var response = await client.GetAsync($"{PagoUrl(cuotaId)}?returnUrl={Uri.EscapeDataString(returnUrl)}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private static FormUrlEncodedContent Formulario(
        string html, string monto, string? rowVersion = null, string? returnUrl = null)
    {
        var valores = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = HiddenValue(html, "__RequestVerificationToken"),
            ["Input.MontoIngresado"] = monto,
            ["Input.MedioPago"] = "Efectivo",
            ["Input.Comprobante"] = "HTTP-TEST",
            ["Input.Observaciones"] = "Prueba HTTP",
            ["Input.CuotaRowVersionBase64"] = rowVersion ?? HiddenValue(html, "Input.CuotaRowVersionBase64")
        };
        if (returnUrl != null)
            valores["returnUrl"] = returnUrl;
        return new FormUrlEncodedContent(valores);
    }

    private static string HiddenValue(string html, string name)
    {
        var tag = Regex.Match(
            html,
            $"<input[^>]*name=\"{Regex.Escape(name)}\"[^>]*>",
            RegexOptions.IgnoreCase).Value;
        var value = Regex.Match(tag, "value=\"([^\"]*)\"", RegexOptions.IgnoreCase);
        Assert.True(value.Success, $"No se encontró el input oculto {name}.");
        return WebUtility.HtmlDecode(value.Groups[1].Value);
    }

    private async Task<string> GetHtmlAsync(HttpClient client, int cuotaId)
    {
        var response = await client.GetAsync(PagoUrl(cuotaId));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private async Task<(int CreditoId, int CuotaId)> SeedAsync(int scenario, decimal montoPunitorioLegacy = 0m)
    {
        var clienteId = BaseId + scenario * 10;
        var creditoId = clienteId + 1;
        var cuotaId = clienteId + 2;
        using var scope = _factory.Services.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();

        if (!await context.Cuotas.AnyAsync(c => c.Id == cuotaId))
        {
            var cliente = new Cliente
            {
                Id = clienteId,
                TipoDocumento = "DNI",
                NumeroDocumento = $"PUNML9D-HTTP-{scenario}",
                Apellido = "Pago",
                Nombre = "HTTP",
                NombreCompleto = "Pago HTTP",
                Telefono = "0000",
                Domicilio = "Base de integración",
                Activo = true
            };
            var credito = new Credito
            {
                Id = creditoId,
                ClienteId = clienteId,
                Cliente = cliente,
                Numero = $"PUN-ML9-D-HTTP-{scenario}",
                MontoSolicitado = 1_000m,
                MontoAprobado = 1_000m,
                TasaInteres = 0m,
                CantidadCuotas = 1,
                MontoCuota = 1_000m,
                TotalAPagar = 1_000m,
                SaldoPendiente = 1_000m,
                Estado = EstadoCredito.Activo,
                FechaSolicitud = DateTime.UtcNow.AddMonths(-1),
                PuntajeRiesgoInicial = 100m
            };
            var cuota = new Cuota
            {
                Id = cuotaId,
                CreditoId = creditoId,
                Credito = credito,
                NumeroCuota = 1,
                MontoCapital = 1_000m,
                MontoInteres = 0m,
                MontoTotal = 1_000m,
                MontoPagado = 0m,
                MontoPunitorio = montoPunitorioLegacy,
                FechaVencimiento = DateTime.UtcNow.AddDays(5),
                Estado = EstadoCuota.Pendiente
            };
            context.AddRange(cliente, credito, cuota);
        }

        if (!await context.AperturasCaja.AnyAsync(a => !a.Cerrada && !a.IsDeleted))
        {
            var caja = new Caja
            {
                Id = BaseId + 90,
                Codigo = "PUNML9D",
                Nombre = "Caja PUN-ML9-D",
                IsDeleted = false
            };
            context.Cajas.Add(caja);
            context.AperturasCaja.Add(new AperturaCaja
            {
                Id = BaseId + 91,
                Caja = caja,
                CajaId = caja.Id,
                MontoInicial = 0m,
                UsuarioApertura = "integration",
                Cerrada = false,
                IsDeleted = false
            });
        }

        await context.SaveChangesAsync();
        return (creditoId, cuotaId);
    }

    private async Task<PagoSnapshot> SnapshotAsync(int cuotaId)
    {
        using var scope = _factory.Services.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();
        var cuota = await context.Cuotas.AsNoTracking().SingleAsync(c => c.Id == cuotaId);
        return new PagoSnapshot(
            cuota.MontoPagado,
            cuota.Estado,
            await context.PagosCuota.CountAsync(p => p.CuotaId == cuotaId),
            await context.MovimientosCaja.CountAsync(m => m.ReferenciaId == cuotaId && m.Concepto == ConceptoMovimientoCaja.CobroCuota));
    }

    private sealed record PagoSnapshot(
        decimal MontoPagado,
        EstadoCuota Estado,
        int Pagos,
        int MovimientosCaja);
}
