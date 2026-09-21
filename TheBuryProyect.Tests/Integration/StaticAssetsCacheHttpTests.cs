using System.Net;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// MOBILE-DEBT-01: los estaticos se sirven con Cache-Control explicito y la fuente de iconos que carga el sitio
/// es la instancia recortada (0.77 MB) y no la variable completa (3.9 MB).
/// </summary>
[Collection("HttpIntegration")]
public class StaticAssetsCacheHttpTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public StaticAssetsCacheHttpTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Get_FuenteDeIconos_TieneCacheDe30Dias()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/fonts/material-symbols-outlined-fill.woff2");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("public,max-age=2592000", response.Headers.CacheControl?.ToString().Replace(" ", ""));
    }

    [Fact]
    public async Task Get_EstaticoConVersion_EsInmutable()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/css/erp-table-cards.css?v=hash");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cacheControl = response.Headers.CacheControl?.ToString() ?? string.Empty;
        Assert.Contains("max-age=31536000", cacheControl);
        Assert.Contains("immutable", cacheControl);
    }

    [Fact]
    public async Task Get_CssEstatico_SeComprimeSiElClienteLoAcepta()
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/css/tailwind.css?v=hash");
        request.Headers.AcceptEncoding.ParseAdd("gzip");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("gzip", response.Content.Headers.ContentEncoding);
    }

    [Fact]
    public async Task Get_Html_NoSeComprime_PorBreach()
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/Identity/Account/Login");
        request.Headers.AcceptEncoding.ParseAdd("gzip");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(response.Content.Headers.ContentEncoding);
    }

    [Fact]
    public async Task Get_LocalFontsCss_ReferenciaLaFuenteRecortada()
    {
        var client = _factory.CreateClient();

        var css = await client.GetStringAsync("/css/local-fonts.css");

        Assert.Contains("/fonts/material-symbols-outlined-fill.woff2", css);
        Assert.DoesNotContain("url('/fonts/material-symbols-outlined.woff2')", css);
    }
}
