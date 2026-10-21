using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Tests.Concurrency;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// FIN-S-006 por HTTP real (pipeline completo: autenticacion, permisos por claims, antiforgery, controller, servicio):
/// el cajero con caja.edit y padron solo en la caja A no puede acreditar un movimiento de la caja B con la PoC original
/// (aperturaId propio + movimientoId ajeno), y su flujo legitimo sigue funcionando.
/// </summary>
[Collection("HttpIntegration")]
public class CajaAcreditarMovimientoAutorizacionHttpTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public CajaAcreditarMovimientoAutorizacionHttpTests(CustomWebApplicationFactory factory) => _factory = factory;

    private async Task<(int AperturaA, int MovA, int AperturaB, int MovB)> SembrarDosCajasAsync(string userId)
    {
        await _factory.SeedUserWithCajaEditPermissionAsync(userId);
        using var scope = _factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var aperturaA = await ConcSeed.SembrarAperturaAsync(ctx, userId);
        var aperturaB = await ConcSeed.SembrarAperturaAsync(ctx, "otro-cajero");
        var cajaA = (await ctx.AperturasCaja.AsNoTracking().FirstAsync(a => a.Id == aperturaA)).CajaId;
        ctx.CajaVendedores.Add(new CajaVendedor { CajaId = cajaA, VendedorUserId = userId });

        MovimientoCaja Pendiente(int apertura) => new()
        {
            AperturaCajaId = apertura, Tipo = TipoMovimientoCaja.Ingreso, Concepto = ConceptoMovimientoCaja.VentaTransferencia,
            TipoPago = TipoPago.Transferencia, EstadoAcreditacion = EstadoAcreditacionMovimientoCaja.Pendiente, Monto = 40m,
            Descripcion = "FIN-S-006", Usuario = "seed", FechaMovimiento = DateTime.UtcNow
        };
        var a = Pendiente(aperturaA);
        var b = Pendiente(aperturaB);
        ctx.MovimientosCaja.AddRange(a, b);
        await ctx.SaveChangesAsync();
        return (aperturaA, a.Id, aperturaB, b.Id);
    }

    private static async Task<string> TokenAsync(HttpClient client, int aperturaId)
    {
        var response = await client.GetAsync($"/Caja/RegistrarMovimiento?aperturaId={aperturaId}");
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var m = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(m.Success, "No se encontro el token antiforgery.");
        return WebUtility.HtmlDecode(m.Groups[1].Value);
    }

    private static Task<HttpResponseMessage> AcreditarAsync(HttpClient client, string token, int movimientoId, int aperturaId) =>
        client.PostAsync("/Caja/AcreditarMovimiento", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["movimientoId"] = movimientoId.ToString(),
            ["aperturaId"] = aperturaId.ToString()
        }));

    private async Task<EstadoAcreditacionMovimientoCaja?> EstadoAsync(int movimientoId)
    {
        using var scope = _factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await ctx.MovimientosCaja.AsNoTracking().FirstAsync(m => m.Id == movimientoId)).EstadoAcreditacion;
    }

    [Fact]
    public async Task PoCOriginal_AperturaPropia_MovimientoAjeno_NoAcredita()
    {
        const string user = CustomWebApplicationFactory.CajaEditPermsUserId;
        var (aperturaA, _, _, movB) = await SembrarDosCajasAsync(user);
        var client = _factory.CreateClientWithUserId(user);
        var token = await TokenAsync(client, aperturaA);

        var r = await AcreditarAsync(client, token, movB, aperturaA);

        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Equal(EstadoAcreditacionMovimientoCaja.Pendiente, await EstadoAsync(movB));
    }

    [Fact]
    public async Task AperturaAjena_MovimientoAjeno_NoAcredita()
    {
        const string user = CustomWebApplicationFactory.CajaEditPermsUserId;
        var (aperturaA, _, aperturaB, movB) = await SembrarDosCajasAsync(user);
        var client = _factory.CreateClientWithUserId(user);
        var token = await TokenAsync(client, aperturaA);

        var r = await AcreditarAsync(client, token, movB, aperturaB);

        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Equal(EstadoAcreditacionMovimientoCaja.Pendiente, await EstadoAsync(movB));
    }

    [Fact]
    public async Task FlujoLegitimo_MiembroAcreditaSuPropioMovimiento()
    {
        const string user = CustomWebApplicationFactory.CajaEditPermsUserId;
        var (aperturaA, movA, _, movB) = await SembrarDosCajasAsync(user);
        var client = _factory.CreateClientWithUserId(user);
        var token = await TokenAsync(client, aperturaA);

        var r = await AcreditarAsync(client, token, movA, aperturaA);

        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Equal(EstadoAcreditacionMovimientoCaja.Acreditado, await EstadoAsync(movA));
        Assert.Equal(EstadoAcreditacionMovimientoCaja.Pendiente, await EstadoAsync(movB));
    }
}
