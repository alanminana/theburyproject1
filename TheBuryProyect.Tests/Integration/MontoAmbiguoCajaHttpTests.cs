using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Tests.Concurrency;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// FIN-S-017 por HTTP real (metadatos MVC reales): en un campo de dinero ([MontoArgentino]) "1.500" y "1,500" se
/// persistían como 1,50 (subestimación x1000). Ahora se rechazan por ambiguos y no se registra ningún movimiento;
/// los formatos no ambiguos se siguen aceptando. Comprueba además que el binder ve el atributo en ejecución real.
/// </summary>
[Collection("HttpIntegration")]
public class MontoAmbiguoCajaHttpTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string User = CustomWebApplicationFactory.CajaCierrePermsUserId;
    private readonly CustomWebApplicationFactory _factory;

    public MontoAmbiguoCajaHttpTests(CustomWebApplicationFactory factory) => _factory = factory;

    private async Task<int> SembrarAperturaPropiaAsync()
    {
        await _factory.SeedUserWithCajaCierrePermissionAsync(User);
        using var scope = _factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var aperturaId = await ConcSeed.SembrarAperturaAsync(ctx, User);
        var cajaId = (await ctx.AperturasCaja.AsNoTracking().FirstAsync(a => a.Id == aperturaId)).CajaId;
        ctx.CajaVendedores.Add(new CajaVendedor { CajaId = cajaId, VendedorUserId = User });
        await ctx.SaveChangesAsync();
        return aperturaId;
    }

    private static async Task<string> TokenAsync(HttpClient client, int aperturaId)
    {
        var response = await client.GetAsync($"/Caja/RegistrarMovimiento?aperturaId={aperturaId}");
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var m = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(m.Success, "No se encontró el token antiforgery.");
        return WebUtility.HtmlDecode(m.Groups[1].Value);
    }

    private static Task<HttpResponseMessage> PostMovimientoAsync(HttpClient client, string token, int aperturaId, string monto) =>
        client.PostAsync("/Caja/RegistrarMovimiento", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["AperturaCajaId"] = aperturaId.ToString(),
            ["IdempotencyKey"] = Guid.NewGuid().ToString(),
            ["Tipo"] = "0",          // Ingreso
            ["Concepto"] = "99",     // Otro
            ["Descripcion"] = "FIN-S-017",
            ["Monto"] = monto
        }));

    private async Task<List<decimal>> MontosRegistradosAsync(int aperturaId)
    {
        using var scope = _factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await ctx.MovimientosCaja.AsNoTracking()
            .Where(m => m.AperturaCajaId == aperturaId && m.Descripcion == "FIN-S-017")
            .Select(m => m.Monto).ToListAsync();
    }

    [Theory]
    [InlineData("1.500")]
    [InlineData("1,500")]
    public async Task RegistrarMovimiento_MontoAmbiguo_SeRechaza_YNoRegistraNada(string monto)
    {
        var aperturaId = await SembrarAperturaPropiaAsync();
        var client = _factory.CreateClientWithUserId(User);
        var token = await TokenAsync(client, aperturaId);

        var response = await PostMovimientoAsync(client, token, aperturaId, monto);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);   // vuelve al formulario con el error
        Assert.Contains("ambiguo", html);
        Assert.Empty(await MontosRegistradosAsync(aperturaId));
    }

    [Theory]
    [InlineData("1500", 1500)]
    [InlineData("1500,50", 1500.50)]
    [InlineData("1.500,00", 1500)]
    [InlineData("1.5", 1.5)]
    public async Task RegistrarMovimiento_MontoNoAmbiguo_SeRegistraConElValorEsperado(string monto, double esperado)
    {
        var aperturaId = await SembrarAperturaPropiaAsync();
        var client = _factory.CreateClientWithUserId(User);
        var token = await TokenAsync(client, aperturaId);

        var response = await PostMovimientoAsync(client, token, aperturaId, monto);

        Assert.True(response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Found,
            $"Esperaba redirect y fue {(int)response.StatusCode}.");
        Assert.Equal((decimal)esperado, Assert.Single(await MontosRegistradosAsync(aperturaId)));
    }
}
