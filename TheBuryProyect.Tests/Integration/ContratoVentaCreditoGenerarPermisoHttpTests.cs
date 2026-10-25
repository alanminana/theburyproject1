using System.Net;
using System.Text.RegularExpressions;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// FIN-S-004: generar el contrato de una venta a crédito solo exigía ventas.view. Es una función de configuración de crédito:
/// requiere creditos.configure.
/// </summary>
[Collection("HttpIntegration")]
public class ContratoVentaCreditoGenerarPermisoHttpTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public ContratoVentaCreditoGenerarPermisoHttpTests(CustomWebApplicationFactory factory) => _factory = factory;

    private static async Task<string> TokenAsync(HttpClient client)
    {
        var login = await client.GetAsync("/Identity/Account/Login");
        var match = Regex.Match(await login.Content.ReadAsStringAsync(), "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, "Sin token antiforgery.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static async Task<HttpResponseMessage> GenerarAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/ContratoVentaCredito/Generar")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = await TokenAsync(client),
                ["ventaId"] = "999999"
            })
        };
        request.Headers.Add("X-Requested-With", "XMLHttpRequest");
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task SoloConVentasView_NoPuedeGenerarElContrato_403()
    {
        await _factory.SeedUserWithContratoSoloVentasAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.ContratoSoloVentasUserId);

        var respuesta = await GenerarAsync(client);

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
    }

    [Fact]
    public async Task ConCreditosConfigure_LlegaALaLogicaDeGeneracion()
    {
        await _factory.SeedUserWithContratoConfigurarAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.ContratoConfigurarUserId);

        var respuesta = await GenerarAsync(client);

        // Venta inexistente: la lógica responde un error de negocio, no 403 por permisos.
        Assert.NotEqual(HttpStatusCode.Forbidden, respuesta.StatusCode);
    }
}
