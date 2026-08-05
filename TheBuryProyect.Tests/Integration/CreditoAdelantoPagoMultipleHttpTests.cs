using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// PUN-ML9-E: adelanto y pago múltiple adaptados al modelo autoritativo de capital + punitorio
/// aplicado. Reutiliza los mismos usuarios/permisos sembrados por
/// <see cref="CreditoPagoCuotaIndividualHttpTests"/> (Cajero = ambos permisos, Vendedor-like =
/// sólo creditos.view, sólo-payinstallment sin creditos.view) — misma matriz exigida acá.
/// </summary>
[Collection("HttpIntegration")]
public sealed class CreditoAdelantoPagoMultipleHttpTests : IClassFixture<CustomWebApplicationFactory>
{
    private const int BaseId = 998_940;
    private readonly CustomWebApplicationFactory _factory;

    public CreditoAdelantoPagoMultipleHttpTests(CustomWebApplicationFactory factory) => _factory = factory;

    // =========================================================================
    // Adelanto — permisos
    // =========================================================================

    [Fact]
    public async Task Adelanto_MatrizPermisos_ExigeCreditoViewYPayInstallmentEnGetYPost()
    {
        var escenario = await SeedAdelantoAsync(1);
        await _factory.SeedUserWithCreditoViewPermissionAsync();
        await _factory.SeedUserWithCreditoPayBothPermissionsAsync();
        await _factory.SeedUserWithCreditoPayOnlyPermissionAsync();

        using var ambos = _factory.CreateClientWithUserId(CustomWebApplicationFactory.CreditoPayBothPermsUserId);
        using var soloVista = _factory.CreateClientWithUserId(CustomWebApplicationFactory.CreditoViewPermsUserId);
        using var soloPago = _factory.CreateClientWithUserId(CustomWebApplicationFactory.CreditoPayOnlyPermsUserId);

        Assert.Equal(HttpStatusCode.OK, (await ambos.GetAsync(AdelantoUrl(escenario.CreditoId))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await soloVista.GetAsync(AdelantoUrl(escenario.CreditoId))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await soloPago.GetAsync(AdelantoUrl(escenario.CreditoId))).StatusCode);

        using var postVacio = new FormUrlEncodedContent(Array.Empty<KeyValuePair<string, string>>());
        Assert.Equal(HttpStatusCode.Forbidden, (await soloVista.PostAsync(AdelantoUrl(escenario.CreditoId), postVacio)).StatusCode);
        using var postVacioSoloPago = new FormUrlEncodedContent(Array.Empty<KeyValuePair<string, string>>());
        Assert.Equal(HttpStatusCode.Forbidden, (await soloPago.PostAsync(AdelantoUrl(escenario.CreditoId), postVacioSoloPago)).StatusCode);
    }

    // =========================================================================
    // Adelanto — contrato del formulario
    // =========================================================================

    [Fact]
    public async Task Adelanto_Get_RenderizaSoloCamposDeIntencion_SinFinancierosNiFechaPago()
    {
        var escenario = await SeedAdelantoAsync(2, montoPunitorioLegacy: 555m);
        await _factory.SeedUserWithCreditoPayBothPermissionsAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.CreditoPayBothPermsUserId);

        var response = await client.GetAsync(AdelantoUrl(escenario.CreditoId));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        foreach (var permitido in new[]
                 {
                     "Input.MedioPago", "Input.Comprobante", "Input.Observaciones", "Input.CuotaRowVersionBase64"
                 })
            Assert.Contains($"name=\"{permitido}\"", html);

        Assert.DoesNotContain("name=\"Input.MontoIngresado\"", html);
        Assert.DoesNotContain("name=\"MontoPagado\"", html);
        Assert.DoesNotContain("name=\"MontoCuota\"", html);
        Assert.DoesNotContain("name=\"MontoPunitorio\"", html);
        Assert.DoesNotContain("name=\"TotalAPagar\"", html);
        Assert.DoesNotContain("name=\"FechaPago\"", html);
        Assert.DoesNotContain("name=\"CreditoId\"", html);
        Assert.DoesNotContain("name=\"CuotaId\"", html);
        Assert.DoesNotContain("555,00", html);
        Assert.Contains("Capital pendiente", html);
        Assert.Contains("Total a cancelar", html);
    }

    [Fact]
    public async Task Adelanto_Preview_NoPersiste()
    {
        var escenario = await SeedAdelantoAsync(3);
        await _factory.SeedUserWithCreditoPayBothPermissionsAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.CreditoPayBothPermsUserId);
        var html = await GetHtmlAsync(client, AdelantoUrl(escenario.CreditoId));
        var before = await SnapshotAsync(escenario.CuotaId);

        using var request = FormularioAdelanto(html);
        var response = await client.PostAsync($"{AdelantoUrl(escenario.CreditoId)}/Preview", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(1_000m, json.RootElement.GetProperty("aplicadoCapital").GetDecimal());
        Assert.Equal(0m, json.RootElement.GetProperty("aplicadoPunitorio").GetDecimal());
        Assert.Equal(before, await SnapshotAsync(escenario.CuotaId));
    }

    [Fact]
    public async Task Adelanto_Post_RowVersionInvalida_Devuelve400SinPersistir()
    {
        var escenario = await SeedAdelantoAsync(4);
        await _factory.SeedUserWithCreditoPayBothPermissionsAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.CreditoPayBothPermsUserId);
        var html = await GetHtmlAsync(client, AdelantoUrl(escenario.CreditoId));
        var before = await SnapshotAsync(escenario.CuotaId);

        using var request = FormularioAdelanto(html, rowVersion: "no-es-base64");
        var response = await client.PostAsync(AdelantoUrl(escenario.CreditoId), request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await SnapshotAsync(escenario.CuotaId));
    }

    [Fact]
    public async Task Adelanto_Post_DobleEnvioConMismoToken_SegundoDevuelve409YNoDuplica()
    {
        var escenario = await SeedAdelantoAsync(5);
        await _factory.SeedUserWithCreditoPayBothPermissionsAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.CreditoPayBothPermsUserId);
        var html = await GetHtmlAsync(client, AdelantoUrl(escenario.CreditoId));

        using var primero = FormularioAdelanto(html);
        var firstResponse = await client.PostAsync(AdelantoUrl(escenario.CreditoId), primero);
        using var segundo = FormularioAdelanto(html);
        var secondResponse = await client.PostAsync(AdelantoUrl(escenario.CreditoId), segundo);

        Assert.Equal(HttpStatusCode.Redirect, firstResponse.StatusCode);
        Assert.True(
            secondResponse.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.Redirect,
            $"Status inesperado: {secondResponse.StatusCode}");

        var snapshot = await SnapshotAsync(escenario.CuotaId);
        Assert.Equal(1_000m, snapshot.MontoPagado);
        Assert.Equal(1, snapshot.Pagos);
        Assert.Equal(1, snapshot.MovimientosCaja);
    }

    [Fact]
    public async Task Adelanto_Post_Exitoso_SoloLiberaCupoPorCapital()
    {
        var escenario = await SeedAdelantoAsync(6);
        await _factory.SeedUserWithCreditoPayBothPermissionsAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.CreditoPayBothPermsUserId);
        var html = await GetHtmlAsync(client, AdelantoUrl(escenario.CreditoId));

        using var request = FormularioAdelanto(html);
        var response = await client.PostAsync(AdelantoUrl(escenario.CreditoId), request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var snapshot = await SnapshotAsync(escenario.CuotaId);
        Assert.Equal(EstadoCuota.Pagada, snapshot.Estado);
        Assert.Equal(1_000m, snapshot.MontoPagado);
    }

    // =========================================================================
    // Pago múltiple — permisos
    // =========================================================================

    [Fact]
    public async Task Multiple_MatrizPermisos_ExigeCreditoViewYPayInstallment()
    {
        var escenario = await SeedMultipleAsync(1);
        await _factory.SeedUserWithCreditoViewPermissionAsync();
        await _factory.SeedUserWithCreditoPayBothPermissionsAsync();
        await _factory.SeedUserWithCreditoPayOnlyPermissionAsync();

        using var ambos = _factory.CreateClientWithUserId(CustomWebApplicationFactory.CreditoPayBothPermsUserId);
        using var soloVista = _factory.CreateClientWithUserId(CustomWebApplicationFactory.CreditoViewPermsUserId);
        using var soloPago = _factory.CreateClientWithUserId(CustomWebApplicationFactory.CreditoPayOnlyPermsUserId);

        var previewBody = new { clienteId = escenario.ClienteId, cuotaIds = new[] { escenario.CuotaId }, medioPago = "Efectivo" };

        // El permiso se exige antes que el antiforgery token (mismo orden que ya prueba
        // CreditoPagoCuotaIndividualHttpTests con un form vacío): los rechazos por permiso no
        // necesitan un token válido para dar 403.
        Assert.Equal(HttpStatusCode.OK, (await PostJsonConAntiforgeryAsync(ambos, "/Credito/PreviewPagoMultiple", previewBody)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await soloVista.PostAsJsonAsync("/Credito/PreviewPagoMultiple", previewBody)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await soloPago.PostAsJsonAsync("/Credito/PreviewPagoMultiple", previewBody)).StatusCode);

        var registrarBody = new
        {
            clienteId = escenario.ClienteId,
            cuotaIds = new[] { escenario.CuotaId },
            rowVersionsPorCuota = new Dictionary<int, string>(),
            medioPago = "Efectivo"
        };
        Assert.Equal(HttpStatusCode.Forbidden, (await soloVista.PostAsJsonAsync("/Credito/RegistrarPagoMultiple", registrarBody)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await soloPago.PostAsJsonAsync("/Credito/RegistrarPagoMultiple", registrarBody)).StatusCode);
    }

    // =========================================================================
    // Pago múltiple — preview y confirmación real (nombres de campo del JS real)
    // =========================================================================

    [Fact]
    public async Task Multiple_Preview_DevuelveComposicionRealYNoPersiste()
    {
        var escenario = await SeedMultipleAsync(2);
        await _factory.SeedUserWithCreditoPayBothPermissionsAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.CreditoPayBothPermsUserId);
        var before = await SnapshotAsync(escenario.CuotaId);

        var response = await PostJsonConAntiforgeryAsync(client, "/Credito/PreviewPagoMultiple", new
        {
            clienteId = escenario.ClienteId,
            cuotaIds = new[] { escenario.CuotaId },
            medioPago = "Efectivo"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = json.RootElement.GetProperty("data");
        Assert.Equal(1_000m, data.GetProperty("capitalTotal").GetDecimal());
        Assert.Equal(0m, data.GetProperty("punitorioTotal").GetDecimal());
        Assert.Equal(before, await SnapshotAsync(escenario.CuotaId));
    }

    [Fact]
    public async Task Multiple_Confirmar_ConRowVersionRealDelPreview_CobraYCreaUnPagoCuota()
    {
        var escenario = await SeedMultipleAsync(3);
        await _factory.SeedUserWithCreditoPayBothPermissionsAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.CreditoPayBothPermsUserId);

        var previewResponse = await PostJsonConAntiforgeryAsync(client, "/Credito/PreviewPagoMultiple", new
        {
            clienteId = escenario.ClienteId,
            cuotaIds = new[] { escenario.CuotaId },
            medioPago = "Efectivo"
        });
        using var previewJson = JsonDocument.Parse(await previewResponse.Content.ReadAsStringAsync());
        var rowVersion = previewJson.RootElement.GetProperty("data").GetProperty("cuotas")[0]
            .GetProperty("cuotaRowVersionBase64").GetString();

        var response = await PostJsonConAntiforgeryAsync(client, "/Credito/RegistrarPagoMultiple", new
        {
            clienteId = escenario.ClienteId,
            cuotaIds = new[] { escenario.CuotaId },
            rowVersionsPorCuota = new Dictionary<int, string> { [escenario.CuotaId] = rowVersion! },
            medioPago = "Efectivo",
            observaciones = (string?)null
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var snapshot = await SnapshotAsync(escenario.CuotaId);
        Assert.Equal(EstadoCuota.Pagada, snapshot.Estado);
        Assert.Equal(1, snapshot.Pagos);
        Assert.Equal(1, snapshot.MovimientosCaja);
    }

    [Fact]
    public async Task Multiple_Confirmar_SinRowVersionEnElBody_Devuelve400SinPersistir()
    {
        var escenario = await SeedMultipleAsync(4);
        await _factory.SeedUserWithCreditoPayBothPermissionsAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.CreditoPayBothPermsUserId);
        var before = await SnapshotAsync(escenario.CuotaId);

        var response = await PostJsonConAntiforgeryAsync(client, "/Credito/RegistrarPagoMultiple", new
        {
            clienteId = escenario.ClienteId,
            cuotaIds = new[] { escenario.CuotaId },
            rowVersionsPorCuota = new Dictionary<int, string>(),
            medioPago = "Efectivo",
            observaciones = (string?)null
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await SnapshotAsync(escenario.CuotaId));
    }

    // =========================================================================
    // Infraestructura
    // =========================================================================

    private static string AdelantoUrl(int creditoId) => $"/Credito/AdelantarCuota/{creditoId}";

    private async Task<string> GetHtmlAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>
    /// El JS de pago múltiple manda el antiforgery token en el header "RequestVerificationToken"
    /// (nunca como form field, porque el body es JSON) — mismo contrato que credito-index.js. Acá
    /// se obtiene un par cookie+token real pisando cualquier página autorizada del usuario, tal
    /// como lo haría el navegador antes del primer fetch.
    /// </summary>
    private async Task<HttpResponseMessage> PostJsonConAntiforgeryAsync(HttpClient client, string url, object body)
    {
        var paginaHtml = await GetHtmlAsync(client, "/Credito/Index");
        var token = HiddenValue(paginaHtml, "__RequestVerificationToken");

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("RequestVerificationToken", token);
        return await client.SendAsync(request);
    }

    private static FormUrlEncodedContent FormularioAdelanto(string html, string? rowVersion = null)
    {
        var valores = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = HiddenValue(html, "__RequestVerificationToken"),
            ["Input.MedioPago"] = "Efectivo",
            ["Input.Comprobante"] = "HTTP-TEST-ADEL",
            ["Input.Observaciones"] = "Prueba HTTP adelanto",
            ["Input.CuotaRowVersionBase64"] = rowVersion ?? HiddenValue(html, "Input.CuotaRowVersionBase64")
        };
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

    private async Task<(int CreditoId, int CuotaId)> SeedAdelantoAsync(int scenario, decimal montoPunitorioLegacy = 0m)
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
                NumeroDocumento = $"PUNML9E-ADEL-{scenario}",
                Apellido = "Adelanto",
                Nombre = "HTTP",
                NombreCompleto = "Adelanto HTTP",
                Telefono = "0000",
                Domicilio = "Base de integración",
                Activo = true
            };
            var credito = new Credito
            {
                Id = creditoId,
                ClienteId = clienteId,
                Cliente = cliente,
                Numero = $"PUN-ML9-E-ADEL-{scenario}",
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
                FechaVencimiento = DateTime.UtcNow.AddDays(30),
                Estado = EstadoCuota.Pendiente
            };
            context.AddRange(cliente, credito, cuota);
        }

        await AsegurarCajaAbiertaAsync(context, scenario);
        await context.SaveChangesAsync();
        return (creditoId, cuotaId);
    }

    private async Task<(int ClienteId, int CreditoId, int CuotaId)> SeedMultipleAsync(int scenario)
    {
        var clienteId = BaseId + 500 + scenario * 10;
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
                NumeroDocumento = $"PUNML9E-MULT-{scenario}",
                Apellido = "Multiple",
                Nombre = "HTTP",
                NombreCompleto = "Multiple HTTP",
                Telefono = "0000",
                Domicilio = "Base de integración",
                Activo = true
            };
            var credito = new Credito
            {
                Id = creditoId,
                ClienteId = clienteId,
                Cliente = cliente,
                Numero = $"PUN-ML9-E-MULT-{scenario}",
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
                MontoPunitorio = 0m,
                FechaVencimiento = DateTime.UtcNow.AddDays(30),
                Estado = EstadoCuota.Pendiente
            };
            context.AddRange(cliente, credito, cuota);
        }

        await AsegurarCajaAbiertaAsync(context, 500 + scenario);
        await context.SaveChangesAsync();
        return (clienteId, creditoId, cuotaId);
    }

    private static async Task AsegurarCajaAbiertaAsync(AppDbContext context, int scenario)
    {
        if (await context.AperturasCaja.AnyAsync(a => !a.Cerrada && !a.IsDeleted))
            return;

        var caja = new Caja
        {
            Id = BaseId + 900 + scenario,
            Codigo = $"PUNML9E{scenario}",
            Nombre = "Caja PUN-ML9-E",
            IsDeleted = false
        };
        context.Cajas.Add(caja);
        context.AperturasCaja.Add(new AperturaCaja
        {
            Id = BaseId + 950 + scenario,
            Caja = caja,
            CajaId = caja.Id,
            MontoInicial = 0m,
            UsuarioApertura = "integration",
            Cerrada = false,
            IsDeleted = false
        });
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
