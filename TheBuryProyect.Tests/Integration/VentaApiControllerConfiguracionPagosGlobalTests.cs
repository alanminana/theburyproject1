using System.Net;
using System.Text.Json;

namespace TheBuryProject.Tests.Integration;

public sealed class VentaApiControllerConfiguracionPagosGlobalTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public VentaApiControllerConfiguracionPagosGlobalTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task VentaApiController_ConfiguracionPagosGlobal_RutaHttpRespondeOkConListaVacia()
    {
        await _factory.SeedTestUserAsync();
        using var client = _factory.CreateAuthenticatedClient();

        var response = await client.GetAsync("/api/ventas/configuracion-pagos-global");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var json = await JsonDocument.ParseAsync(stream);
        Assert.Empty(json.RootElement.GetProperty("medios").EnumerateArray());
    }
}
