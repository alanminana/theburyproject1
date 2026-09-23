using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Regresión: la creación de Caja vía AJAX devolvía siempre entity.id=0 porque el controller
/// armaba la respuesta con el CajaViewModel de entrada (nunca asignado) en vez del Caja
/// persistido devuelto por CrearCajaAsync.
/// </summary>
[Collection("HttpIntegration")]
public class CajaControllerCreateHttpTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public CajaControllerCreateHttpTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Create_Ajax_DevuelveIdRealYCoincideConElPersistido()
    {
        await _factory.SeedTestUserAsync();
        var client = _factory.CreateAuthenticatedClient();
        var token = await GetAntiForgeryTokenAsync(client);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var codigo = $"QA-CAJA-{suffix}";
        var nombre = $"Caja QA {suffix}";

        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Codigo"] = codigo,
            ["Nombre"] = nombre,
            ["Activa"] = "true"
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/Caja/Create")
        {
            Content = new FormUrlEncodedContent(form)
        };
        request.Headers.Add("X-Requested-With", "XMLHttpRequest");

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.GetProperty("ok").GetBoolean());

        var idEnRespuesta = json.RootElement.GetProperty("entity").GetProperty("id").GetInt32();
        Assert.True(idEnRespuesta > 0, "response.Id debe ser mayor a 0 (id real persistido), no 0.");

        using var scope = _factory.Services.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();

        var cajaPersistida = await context.Cajas.SingleAsync(c => c.Codigo == codigo);
        Assert.Equal(cajaPersistida.Id, idEnRespuesta);
    }

    private static async Task<string> GetAntiForgeryTokenAsync(HttpClient client)
    {
        var response = await client.GetAsync("/Caja/CreatePartial");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var match = Regex.Match(
            html,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"",
            RegexOptions.CultureInvariant);

        Assert.True(match.Success, "No se encontró el token antiforgery en el modal de creación de Caja.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }
}
