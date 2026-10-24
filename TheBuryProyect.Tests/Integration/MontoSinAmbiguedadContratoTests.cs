using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Controllers;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Validation;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// FIN-S-017 (revisión adversarial de #113): la regla de montos ambiguos también cubre los importes que no llevan
/// [MontoArgentino] (cobro de cuotas, precios de servicios, gastos administrativos, descuento de la orden de compra y los
/// importes de la simulación de plan), marcados con [MontoSinAmbiguedad].
/// </summary>
public class MontoSinAmbiguedadContratoTests
{
    public static IEnumerable<object[]> PropiedadesDeDinero() => new[]
    {
        new object[] { typeof(TheBuryProject.ViewModels.PagoCuota.PagarCuotaInputModel), nameof(TheBuryProject.ViewModels.PagoCuota.PagarCuotaInputModel.MontoIngresado) },
        new object[] { typeof(ServicioVentaPrecioItemViewModel), nameof(ServicioVentaPrecioItemViewModel.Precio) },
        new object[] { typeof(ConfiguracionCreditoVentaViewModel), nameof(ConfiguracionCreditoVentaViewModel.GastosAdministrativos) },
        new object[] { typeof(OrdenCompraViewModel), nameof(OrdenCompraViewModel.Descuento) },
    };

    [Theory]
    [MemberData(nameof(PropiedadesDeDinero))]
    public void ImportesEnDinero_SinMontoArgentino_DeclaranElMarcador(Type tipo, string propiedad)
    {
        var p = tipo.GetProperty(propiedad)!;

        Assert.True(p.IsDefined(typeof(MontoSinAmbiguedadAttribute), inherit: true),
            $"{tipo.Name}.{propiedad} es un importe y debe declarar [MontoSinAmbiguedad].");
    }

    [Theory]
    [InlineData("totalVenta")]
    [InlineData("anticipo")]
    [InlineData("gastosAdministrativos")]
    public void SimularPlanVenta_ImportesDeLaQuery_DeclaranElMarcador(string parametro)
    {
        var metodo = typeof(CreditoController).GetMethod(nameof(CreditoController.SimularPlanVenta))!;
        var p = metodo.GetParameters().Single(x => x.Name == parametro);

        Assert.True(p.IsDefined(typeof(MontoSinAmbiguedadAttribute), inherit: true));
    }
}

[Collection("HttpIntegration")]
public class ServiciosVentaPrecioAmbiguoHttpTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public ServiciosVentaPrecioAmbiguoHttpTests(CustomWebApplicationFactory factory) => _factory = factory;

    private async Task<string> TokenAsync(HttpClient client)
    {
        var response = await client.GetAsync("/ServiciosVenta");
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var m = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(m.Success, "No se encontró el token antiforgery.");
        return WebUtility.HtmlDecode(m.Groups[1].Value);
    }

    private static Task<HttpResponseMessage> GuardarAsync(HttpClient client, string token, string precio) =>
        client.PostAsync("/ServiciosVenta/Guardar", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Envios[0].Tipo"] = ((int)TipoServicioVenta.EnvioCiudad).ToString(),
            ["Envios[0].Precio"] = precio,
            ["Envios[0].Activo"] = "true"
        }));

    private async Task<decimal?> PrecioEnvioCiudadAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await ctx.ServiciosVentaPrecios.AsNoTracking()
            .Where(s => s.Tipo == TipoServicioVenta.EnvioCiudad && !s.IsDeleted)
            .Select(s => (decimal?)s.Precio).FirstOrDefaultAsync();
    }

    [Theory]
    [InlineData("1.500")]
    [InlineData("1,500")]
    public async Task Guardar_PrecioAmbiguo_ConservaElPrecioVigente_NoLoPisaPorCeroNiPor1con5(string ambiguo)
    {
        await _factory.SeedUserWithConfiguracionActualizaAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.ConfiguracionActualizaUserId);
        var token = await TokenAsync(client);
        await GuardarAsync(client, token, "2500");
        Assert.Equal(2500m, await PrecioEnvioCiudadAsync());

        var response = await GuardarAsync(client, token, ambiguo);

        Assert.True(response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Found);
        Assert.Equal(2500m, await PrecioEnvioCiudadAsync());
    }

    [Fact]
    public async Task Guardar_PrecioNoAmbiguo_SigueGuardando()
    {
        await _factory.SeedUserWithConfiguracionActualizaAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.ConfiguracionActualizaUserId);
        var token = await TokenAsync(client);

        await GuardarAsync(client, token, "3.500,00");

        Assert.Equal(3500m, await PrecioEnvioCiudadAsync());
    }
}
