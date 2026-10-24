using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using TheBuryProject.Tests.Infrastructure;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// COM-S-010 / SEC-D-001: las APIs con cookie (/api/cotizacion, /api/tickets, /api/Notificacion, /api/ventas,
/// /api/productos) y Cliente/ActualizarBcra aceptaban POST/PUT/PATCH/DELETE sin token antiforgery (solo las mitigaba
/// SameSite=Lax). Ahora lo exigen: sin token es 400; con token válido el pedido llega a la lógica (y se decide por
/// permisos). Los clientes de test envían el token solos (AntiforgeryApiHandler); acá se omite a propósito.
/// </summary>
[Collection("HttpIntegration")]
public class ApiAntiforgeryHttpTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public ApiAntiforgeryHttpTests(CustomWebApplicationFactory factory) => _factory = factory;

    private async Task<HttpClient> ClienteAsync()
    {
        await _factory.SeedTestUserAsync();      // SuperAdmin de test: sin él no tiene permisos y todo da 403
        return _factory.CreateAuthenticatedClient();
    }

    private static HttpRequestMessage SinToken(HttpMethod metodo, string url, HttpContent? contenido = null)
    {
        var r = new HttpRequestMessage(metodo, url) { Content = contenido };
        r.Headers.Add(AntiforgeryApiHandler.SinTokenHeader, "1");
        return r;
    }

    public static IEnumerable<object[]> Mutaciones() =>
    [
        ["POST", "/api/cotizacion/vencer-expiradas"],
        ["POST", "/api/cotizacion/1/cancelar"],
        ["POST", "/api/cotizacion/1/conversion/preview"],
        ["POST", "/api/tickets"],
        ["PUT", "/api/tickets/1"],
        ["PATCH", "/api/tickets/1/estado"],
        ["DELETE", "/api/tickets/checklist/1"],
        ["DELETE", "/api/tickets/adjuntos/1"],
        ["POST", "/api/Notificacion/marcarTodasLeidas"],
        ["POST", "/api/Notificacion/1/marcarLeida"],
        ["DELETE", "/api/Notificacion/1"],
        ["POST", "/api/ventas/CalcularTotalesVenta"],
        ["POST", "/Cliente/ActualizarBcra"],
    ];

    [Theory]
    [MemberData(nameof(Mutaciones))]
    public async Task SinToken_Devuelve400(string metodo, string url)
    {
        using var client = await ClienteAsync();
        var contenido = metodo == "DELETE" ? null : JsonContent.Create(new { });

        var respuesta = await client.SendAsync(SinToken(new HttpMethod(metodo), url, contenido));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
    }

    [Theory]
    [MemberData(nameof(Mutaciones))]
    public async Task ConTokenValido_NoEsRechazadoPorAntiforgery(string metodo, string url)
    {
        using var client = await ClienteAsync();
        var contenido = metodo == "DELETE" ? null : JsonContent.Create(new { });

        // El handler de test agrega el token real, como csrf-fetch.js en el navegador.
        var respuesta = await client.SendAsync(new HttpRequestMessage(new HttpMethod(metodo), url) { Content = contenido });

        // El rechazo por antiforgery es un 400 SIN cuerpo; con token el pedido llega a la lógica y responde otra cosa
        // (200, 404 por id inexistente, 403 por permisos, o un 400 con detalle de validación).
        var cuerpo = await respuesta.Content.ReadAsStringAsync();
        Assert.True(respuesta.StatusCode != HttpStatusCode.BadRequest || cuerpo.Length > 0,
            $"{metodo} {url} con token válido fue rechazado como si no lo tuviera.");
    }

    [Fact]
    public async Task LasLecturas_NoExigenToken()
    {
        using var client = await ClienteAsync();

        // Las lecturas (GET) no exigen token: no se rompe ningún flujo de solo lectura.
        var get = await client.SendAsync(SinToken(HttpMethod.Get, "/api/tickets"));

        Assert.NotEqual(HttpStatusCode.BadRequest, get.StatusCode);
    }

    [Fact]
    public async Task Layout_PublicaElTokenParaElJsCompartido()
    {
        using var client = await ClienteAsync();

        var html = await (await client.GetAsync("/Identity/Account/Login")).Content.ReadAsStringAsync();
        var home = await client.GetAsync("/Cotizacion");
        var homeHtml = await home.Content.ReadAsStringAsync();

        // Login usa su propio layout; las pantallas del ERP usan _Layout con <meta name="csrf-token"> y csrf-fetch.js.
        Assert.True(home.StatusCode == HttpStatusCode.OK, $"/Cotizacion devolvió {(int)home.StatusCode}.");
        Assert.Matches(new Regex("<meta name=\"csrf-token\" content=\"[^\"]{20,}\""), homeHtml);
        Assert.Contains("/js/csrf-fetch.js", homeHtml);
        Assert.NotNull(html);
    }
}
