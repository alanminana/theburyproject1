using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Tests.Concurrency;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// FIN-S-006 (hallazgo de la revisión adversarial): Cerrar y RegistrarMovimiento evaluaban el ModelState ANTES de
/// autorizar, así que un POST con el modelo inválido y una apertura ajena re-renderizaba el formulario con los datos
/// financieros de esa caja (fondo inicial, ingresos, egresos, esperado, responsable, nombre y saldo). Ahora se autoriza
/// primero. Pipeline HTTP completo: autenticación, permisos por claims, antiforgery, controller y servicio.
/// </summary>
[Collection("HttpIntegration")]
public class CajaModeloInvalidoAutorizacionHttpTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string User = CustomWebApplicationFactory.CajaCierrePermsUserId;
    private readonly CustomWebApplicationFactory _factory;

    public CajaModeloInvalidoAutorizacionHttpTests(CustomWebApplicationFactory factory) => _factory = factory;

    private sealed record Escenario(int AperturaPropia, string CajaPropia, int AperturaAjena, string CajaAjena, string ResponsableAjeno);

    private async Task<Escenario> SembrarAsync()
    {
        await _factory.SeedUserWithCajaCierrePermissionAsync(User);
        using var scope = _factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var sufijo = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var aperturaPropia = await ConcSeed.SembrarAperturaAsync(ctx, User);
        var aperturaAjena = await ConcSeed.SembrarAperturaAsync(ctx, "responsable-ajeno");

        var propia = await ctx.AperturasCaja.Include(a => a.Caja).FirstAsync(a => a.Id == aperturaPropia);
        var ajena = await ctx.AperturasCaja.Include(a => a.Caja).FirstAsync(a => a.Id == aperturaAjena);
        propia.Caja.Nombre = $"CAJA-PROPIA-{sufijo}";
        ajena.Caja.Nombre = $"CAJA-AJENA-{sufijo}";
        ajena.UsuarioApertura = $"RESPONSABLE-AJENO-{sufijo}";
        ajena.MontoInicial = 7777.77m;
        ctx.CajaVendedores.Add(new CajaVendedor { CajaId = propia.CajaId, VendedorUserId = User });
        await ctx.SaveChangesAsync();

        return new Escenario(aperturaPropia, propia.Caja.Nombre, aperturaAjena, ajena.Caja.Nombre, ajena.UsuarioApertura);
    }

    private static async Task<string> TokenAsync(HttpClient client, int aperturaPropia)
    {
        var response = await client.GetAsync($"/Caja/RegistrarMovimiento?aperturaId={aperturaPropia}");
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var m = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(m.Success, "No se encontró el token antiforgery.");
        return WebUtility.HtmlDecode(m.Groups[1].Value);
    }

    private static Task<HttpResponseMessage> CerrarInvalidoAsync(HttpClient client, string token, int aperturaId) =>
        client.PostAsync("/Caja/Cerrar", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["AperturaCajaId"] = aperturaId.ToString(),
            ["EfectivoContado"] = "abc"        // el binding invalida el modelo
        }));

    private static Task<HttpResponseMessage> MovimientoInvalidoAsync(HttpClient client, string token, int aperturaId) =>
        client.PostAsync("/Caja/RegistrarMovimiento", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["AperturaCajaId"] = aperturaId.ToString(),
            ["IdempotencyKey"] = Guid.NewGuid().ToString(),
            ["Monto"] = "abc"                  // el binding invalida el modelo
        }));

    [Fact]
    public async Task Cerrar_ModeloInvalido_AperturaAjena_NoRevelaDatosFinancieros()
    {
        var e = await SembrarAsync();
        var client = _factory.CreateClientWithUserId(User);
        var token = await TokenAsync(client, e.AperturaPropia);

        var response = await CerrarInvalidoAsync(client, token, e.AperturaAjena);
        var cuerpo = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.DoesNotContain(e.CajaAjena, cuerpo);
        Assert.DoesNotContain(e.ResponsableAjeno, cuerpo);
        Assert.DoesNotContain("7777", cuerpo);
    }

    [Fact]
    public async Task Cerrar_ModeloInvalido_AperturaPropia_SigueMostrandoElFormulario()
    {
        var e = await SembrarAsync();
        var client = _factory.CreateClientWithUserId(User);
        var token = await TokenAsync(client, e.AperturaPropia);

        var response = await CerrarInvalidoAsync(client, token, e.AperturaPropia);
        var cuerpo = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(e.CajaPropia, cuerpo);
    }

    [Fact]
    public async Task RegistrarMovimiento_ModeloInvalido_AperturaAjena_NoRevelaNombreNiSaldo()
    {
        var e = await SembrarAsync();
        var client = _factory.CreateClientWithUserId(User);
        var token = await TokenAsync(client, e.AperturaPropia);

        var response = await MovimientoInvalidoAsync(client, token, e.AperturaAjena);
        var cuerpo = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.DoesNotContain(e.CajaAjena, cuerpo);
        Assert.DoesNotContain("7777", cuerpo);
    }

    [Fact]
    public async Task RegistrarMovimiento_ModeloInvalido_AperturaPropia_SigueMostrandoElFormulario()
    {
        var e = await SembrarAsync();
        var client = _factory.CreateClientWithUserId(User);
        var token = await TokenAsync(client, e.AperturaPropia);

        var response = await MovimientoInvalidoAsync(client, token, e.AperturaPropia);
        var cuerpo = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(e.CajaPropia, cuerpo);
    }
}
