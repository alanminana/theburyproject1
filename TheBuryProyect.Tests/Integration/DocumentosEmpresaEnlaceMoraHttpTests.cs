using System.Net;
using System.Text.RegularExpressions;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// H4-F1: la ayuda del interés de mora en Documentos > Empresa enlazaba a /ConfiguracionMora (retirada en #168, 404).
/// Debe llevar a la pantalla vigente de configuración de mora.
/// </summary>
[Collection("HttpIntegration")]
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

        Assert.True(match.Success, "No se encontró el enlace de ayuda a la configuración de mora.");
        var href = WebUtility.HtmlDecode(match.Groups[1].Value);
        Assert.Equal("/Mora/ConfiguracionExpandida", href);

        var destino = await client.GetAsync(href);
        Assert.Equal(HttpStatusCode.OK, destino.StatusCode);
    }
}
