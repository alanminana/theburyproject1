using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Models.Enums;
using TheBuryProject.Tests.Concurrency;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// FIN-IDEM-P0 (H-IDEM-1), extremo a extremo por HTTP: el formulario de movimiento manual emite una clave de
/// idempotencia en un campo oculto, la conserva al reenviar el POST y el servidor no duplica el asiento.
/// </summary>
[Collection("HttpIntegration")]
public class CajaMovimientoManualIdempotenciaHttpTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public CajaMovimientoManualIdempotenciaHttpTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Formulario_EmiteClaveUnicaPorApertura_YElReenvioDelPostNoDuplicaElAsiento()
    {
        await _factory.SeedTestUserAsync();
        int aperturaId;
        using (var scope = _factory.Services.CreateScope())
            aperturaId = await ConcSeed.SembrarAperturaAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), "testuser");

        var client = _factory.CreateAuthenticatedClient();

        var (token, clave) = await AbrirFormularioAsync(client, aperturaId);
        var (_, otraClave) = await AbrirFormularioAsync(client, aperturaId);
        Assert.NotEqual(Guid.Empty, clave);
        Assert.NotEqual(clave, otraClave);          // cada apertura del formulario emite un token nuevo

        string descripcion = "IDEM-HTTP " + Guid.NewGuid().ToString("N")[..8];
        async Task<HttpResponseMessage> Enviar(Guid k) => await client.PostAsync("/Caja/RegistrarMovimiento",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["AperturaCajaId"] = aperturaId.ToString(),
                ["IdempotencyKey"] = k.ToString(),
                ["Tipo"] = ((int)TipoMovimientoCaja.Egreso).ToString(),
                ["Concepto"] = ((int)ConceptoMovimientoCaja.GastoOperativo).ToString(),
                ["Monto"] = "25",
                ["Descripcion"] = descripcion
            }));

        // Doble clic / reintento HTTP: el mismo formulario se envía dos veces.
        var r1 = await Enviar(clave);
        var r2 = await Enviar(clave);
        Assert.Equal(HttpStatusCode.Redirect, r1.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, r2.StatusCode);
        Assert.Equal(1, await ContarAsync(aperturaId, descripcion));

        // Otro formulario (otro token) con los mismos datos es un movimiento legítimo distinto.
        var r3 = await Enviar(otraClave);
        Assert.Equal(HttpStatusCode.Redirect, r3.StatusCode);
        Assert.Equal(2, await ContarAsync(aperturaId, descripcion));
    }

    [Theory]
    [InlineData("ausente")]
    [InlineData("vacio")]
    [InlineData("guid-vacio")]
    [InlineData("invalido")]
    public async Task PostSinTokenValido_SeRechazaSinAsiento_YElFormularioEmiteUnoNuevo(string variante)
    {
        await _factory.SeedTestUserAsync();
        int aperturaId;
        using (var scope = _factory.Services.CreateScope())
            aperturaId = await ConcSeed.SembrarAperturaAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), "testuser");

        var client = _factory.CreateAuthenticatedClient();
        var (token, _) = await AbrirFormularioAsync(client, aperturaId);

        string descripcion = "IDEM-HARD " + variante + " " + Guid.NewGuid().ToString("N")[..8];
        var campos = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["AperturaCajaId"] = aperturaId.ToString(),
            ["Tipo"] = ((int)TipoMovimientoCaja.Egreso).ToString(),
            ["Concepto"] = ((int)ConceptoMovimientoCaja.GastoOperativo).ToString(),
            ["Monto"] = "25",
            ["Descripcion"] = descripcion
        };
        switch (variante)
        {
            case "vacio": campos["IdempotencyKey"] = ""; break;
            case "guid-vacio": campos["IdempotencyKey"] = Guid.Empty.ToString(); break;
            case "invalido": campos["IdempotencyKey"] = "no-es-un-guid"; break;
        }

        var respuesta = await client.PostAsync("/Caja/RegistrarMovimiento", new FormUrlEncodedContent(campos));

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        Assert.Contains("/Caja/RegistrarMovimiento", respuesta.Headers.Location!.ToString());
        Assert.Equal(0, await ContarAsync(aperturaId, descripcion));

        // El POST no fabricó clave: el formulario al que se vuelve emite una distinta de vacío.
        var (_, nueva) = await AbrirFormularioAsync(client, aperturaId);
        Assert.NotEqual(Guid.Empty, nueva);
    }

    [Fact]
    public async Task ErrorDeValidacion_ConservaLaMismaClave_YElReintentoValidoRegistraUnSoloAsiento()
    {
        await _factory.SeedTestUserAsync();
        int aperturaId;
        using (var scope = _factory.Services.CreateScope())
            aperturaId = await ConcSeed.SembrarAperturaAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), "testuser");

        var client = _factory.CreateAuthenticatedClient();
        var (token, clave) = await AbrirFormularioAsync(client, aperturaId);

        Dictionary<string, string> Campos(string descripcion) => new()
        {
            ["__RequestVerificationToken"] = token,
            ["AperturaCajaId"] = aperturaId.ToString(),
            ["IdempotencyKey"] = clave.ToString(),
            ["Tipo"] = ((int)TipoMovimientoCaja.Egreso).ToString(),
            ["Concepto"] = ((int)ConceptoMovimientoCaja.GastoOperativo).ToString(),
            ["Monto"] = "25",
            ["Descripcion"] = descripcion
        };

        // Descripción obligatoria vacía: la validación falla, se re-renderiza el formulario con la MISMA clave.
        var invalido = await client.PostAsync("/Caja/RegistrarMovimiento", new FormUrlEncodedContent(Campos("")));
        Assert.Equal(HttpStatusCode.OK, invalido.StatusCode);
        var html = await invalido.Content.ReadAsStringAsync();
        Assert.Contains(clave.ToString(), html, StringComparison.OrdinalIgnoreCase);

        string descripcionOk = "IDEM-HARD ok " + Guid.NewGuid().ToString("N")[..8];
        var r1 = await client.PostAsync("/Caja/RegistrarMovimiento", new FormUrlEncodedContent(Campos(descripcionOk)));
        var r2 = await client.PostAsync("/Caja/RegistrarMovimiento", new FormUrlEncodedContent(Campos(descripcionOk)));
        Assert.Equal(HttpStatusCode.Redirect, r1.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, r2.StatusCode);
        Assert.Equal(1, await ContarAsync(aperturaId, descripcionOk));
    }

    private async Task<int> ContarAsync(int aperturaId, string descripcion)
    {
        using var scope = _factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await ctx.MovimientosCaja.AsNoTracking()
            .CountAsync(m => m.AperturaCajaId == aperturaId && m.Descripcion == descripcion);
    }

    private static async Task<(string token, Guid clave)> AbrirFormularioAsync(HttpClient client, int aperturaId)
    {
        var response = await client.GetAsync($"/Caja/RegistrarMovimiento?aperturaId={aperturaId}");
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(token.Success, "No se encontró el token antiforgery del formulario.");

        var clave = Regex.Match(html, "<input[^>]*name=\"IdempotencyKey\"[^>]*value=\"([^\"]+)\"");
        if (!clave.Success)
            clave = Regex.Match(html, "<input[^>]*value=\"([^\"]+)\"[^>]*name=\"IdempotencyKey\"");
        Assert.True(clave.Success, "El formulario no emite el campo oculto IdempotencyKey.");

        return (WebUtility.HtmlDecode(token.Groups[1].Value), Guid.Parse(clave.Groups[1].Value));
    }
}
