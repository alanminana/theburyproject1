using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// VALID-L10 — precios de envíos y armados: tope 20.000.000 y 2 decimales (antes admitía hasta 999.999.999,99 y el
/// servicio redondeaba en silencio). Un valor fuera de regla no pisa el precio vigente.
/// </summary>
[Collection("HttpIntegration")]
public class ServiciosVentaCamposValidacionHttpTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public ServiciosVentaCamposValidacionHttpTests(CustomWebApplicationFactory factory) => _factory = factory;

    private async Task<string> TokenAsync(HttpClient client)
    {
        var html = await (await client.GetAsync("/ServiciosVenta")).Content.ReadAsStringAsync();
        var m = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(m.Success, "No se encontró el token antiforgery.");
        return WebUtility.HtmlDecode(m.Groups[1].Value);
    }

    private static Task<HttpResponseMessage> GuardarAsync(HttpClient client, string token, string precio) =>
        client.PostAsync("/ServiciosVenta/Guardar", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Envios[0].Tipo"] = ((int)TipoServicioVenta.EnvioRural).ToString(),
            ["Envios[0].Precio"] = precio,
            ["Envios[0].Activo"] = "true"
        }));

    private async Task<decimal?> PrecioAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await ctx.ServiciosVentaPrecios.AsNoTracking()
            .Where(s => s.Tipo == TipoServicioVenta.EnvioRural && !s.IsDeleted)
            .Select(s => (decimal?)s.Precio).FirstOrDefaultAsync();
    }

    private async Task<(HttpClient Client, string Token)> ClienteConPrecioBaseAsync()
    {
        await _factory.SeedUserWithConfiguracionActualizaAsync();
        var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.ConfiguracionActualizaUserId);
        var token = await TokenAsync(client);
        await GuardarAsync(client, token, "1000");
        Assert.Equal(1000m, await PrecioAsync());
        return (client, token);
    }

    [Theory]
    [InlineData("20000000", 20000000)]
    [InlineData("0", 0)]
    [InlineData("1500,50", 1500.5)]
    public async Task Guardar_PrecioEnRango_SeGuarda(string precio, double esperado)
    {
        var (client, token) = await ClienteConPrecioBaseAsync();

        await GuardarAsync(client, token, precio);

        Assert.Equal((decimal)esperado, await PrecioAsync());
        client.Dispose();
    }

    [Theory]
    [InlineData("20000001")]
    [InlineData("999999999,99")]
    [InlineData("10,555")]
    [InlineData("-5")]
    public async Task Guardar_PrecioFueraDeRegla_ConservaElVigente(string precio)
    {
        var (client, token) = await ClienteConPrecioBaseAsync();

        await GuardarAsync(client, token, precio);

        Assert.Equal(1000m, await PrecioAsync());
        client.Dispose();
    }
}
