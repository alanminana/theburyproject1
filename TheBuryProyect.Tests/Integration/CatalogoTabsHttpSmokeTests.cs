using System.Net;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Smoke test HTTP real (con motor Razor real, no solo el ViewModel) para las pestañas
/// Alertas / Movimientos embebidas en Catalogo/Index_tw (Fase 7). Un build en verde no
/// garantiza que los 2 partials nuevos rendericen sin excepción en runtime — esto sí lo
/// verifica.
/// </summary>
[Collection("HttpIntegration")]
public class CatalogoTabsHttpSmokeTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public CatalogoTabsHttpSmokeTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData("/Catalogo/Index")]
    [InlineData("/Catalogo/Index?tab=alertas")]
    [InlineData("/Catalogo/Index?tab=movimientos")]
    public async Task Get_CatalogoIndex_ConCadaPestana_RenderizaSinError(string url)
    {
        await _factory.SeedTestUserAsync();
        var client = _factory.CreateAuthenticatedClient();

        var response = await client.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        if (response.StatusCode != HttpStatusCode.OK)
        {
            Assert.Fail($"Status {response.StatusCode} para {url}. Body:\n{body}");
        }
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Productos", body);
        Assert.Contains("Alertas", body);
        Assert.Contains("Movimientos", body);
    }

    [Fact]
    public async Task Get_AlertaStockIndex_Standalone_SigueRenderizandoIgualQueAntes()
    {
        await _factory.SeedTestUserAsync();
        var client = _factory.CreateAuthenticatedClient();

        var response = await client.GetAsync("/AlertaStock/Index");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Alertas de stock", body);
    }

    [Fact]
    public async Task Get_MovimientoStockIndex_Standalone_SigueRenderizandoIgualQueAntes()
    {
        await _factory.SeedTestUserAsync();
        var client = _factory.CreateAuthenticatedClient();

        var response = await client.GetAsync("/MovimientoStock/Index");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Movimientos de stock", body);
    }
}
