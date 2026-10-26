using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// FUN-D-001 / FE-D-001: acciones GET que devolvían una vista inexistente respondían HTTP 500. Cheque (sin vistas) y las
/// acciones legacy de ConfiguracionPago se retiraron (404); Mora/Configuracion redirige a la pantalla vigente.
/// </summary>
[Collection("HttpIntegration")]
public class RutasSinVistaRetiradasHttpTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public RutasSinVistaRetiradasHttpTests(CustomWebApplicationFactory factory) => _factory = factory;

    private async Task<HttpClient> ClienteAsync()
    {
        await _factory.SeedTestUserAsync();
        return _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    [Theory]
    [InlineData("/Cheque")]
    [InlineData("/Cheque/Create")]
    [InlineData("/Cheque/Details/1")]
    [InlineData("/ConfiguracionPago/Create")]
    [InlineData("/ConfiguracionPago/Details/1")]
    [InlineData("/ConfiguracionPago/Edit/1")]
    [InlineData("/ConfiguracionPago/Delete/1")]
    [InlineData("/ConfiguracionPago/ConfigurarTarjeta?configuracionPagoId=1")]
    public async Task RutaRetirada_Responde404_NoUn500(string url)
    {
        var respuesta = await (await ClienteAsync()).GetAsync(url);

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
    }

    [Fact]
    public async Task MoraConfiguracion_RedirigeALaPantallaVigente()
    {
        var respuesta = await (await ClienteAsync()).GetAsync("/Mora/Configuracion");

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        Assert.Contains("/Mora/ConfiguracionExpandida", respuesta.Headers.Location?.ToString());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1_000_000)]
    public async Task NotificacionesLimiteFueraDeRango_NoRompe(int limite)
    {
        var respuesta = await (await ClienteAsync()).GetAsync($"/api/Notificacion?limite={limite}");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
    }
}
