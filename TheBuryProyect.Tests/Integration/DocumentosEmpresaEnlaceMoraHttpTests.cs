using System.Net;
using System.Text.RegularExpressions;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// H4-F1: la ayuda del interés de mora en Documentos > Empresa enlazaba a /ConfiguracionMora (retirada en #168, 404).
/// Desde MORA-CONTRACTUAL el interés sale de los punitorios: debe llevar a la pantalla vigente (Configuración → Punitorios).
/// </summary>
[Collection("HttpIntegration.3")]
public class DocumentosEmpresaEnlaceMoraHttpTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;

    public DocumentosEmpresaEnlaceMoraHttpTests(CustomWebApplicationFactory factory) => _factory = factory;

    public async Task InitializeAsync() => await _factory.SeedTestUserAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Empresa_EnlaceConfiguracionMora_ApuntaAPantallaVigente()
    {
        var client = _factory.CreateAuthenticatedClient();

        var html = await (await client.GetAsync("/DocumentosConfig/Empresa")).Content.ReadAsStringAsync();
        var match = Regex.Match(html, "id=\"interes-mora-ayuda\"[^>]*>.*?<a href=\"([^\"]+)\"", RegexOptions.Singleline);

        Assert.True(match.Success, "No se encontró el enlace de ayuda a la configuración de punitorios.");
        var href = WebUtility.HtmlDecode(match.Groups[1].Value);
        Assert.Equal("/ConfiguracionPago/CreditoPersonal", href);

        var destino = await client.GetAsync(href);
        Assert.Equal(HttpStatusCode.OK, destino.StatusCode);
    }
}
