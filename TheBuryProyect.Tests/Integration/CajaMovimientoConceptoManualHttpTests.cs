using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Tests.Concurrency;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// FIN-S-007: Caja/RegistrarMovimiento aceptaba cualquier valor de ConceptoMovimientoCaja (incluidos los que genera el
/// sistema, como la liquidación de Mercado Libre o la reversión de venta) y combinaciones incoherentes con el tipo.
/// El select del formulario era la única barrera. Ahora el servidor exige el mismo catálogo que ofrece la UI.
/// </summary>
[Collection("HttpIntegration")]
public class CajaMovimientoConceptoManualHttpTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string User = CustomWebApplicationFactory.CajaCierrePermsUserId;
    private readonly CustomWebApplicationFactory _factory;

    public CajaMovimientoConceptoManualHttpTests(CustomWebApplicationFactory factory) => _factory = factory;

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

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string token, int aperturaId, string tipo, string concepto, string marca) =>
        client.PostAsync("/Caja/RegistrarMovimiento", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["AperturaCajaId"] = aperturaId.ToString(),
            ["IdempotencyKey"] = Guid.NewGuid().ToString(),
            ["Tipo"] = tipo,
            ["Concepto"] = concepto,
            ["Descripcion"] = marca,
            ["Monto"] = "10"
        }));

    private async Task<int> CantidadAsync(int aperturaId, string marca)
    {
        using var scope = _factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await ctx.MovimientosCaja.AsNoTracking().CountAsync(m => m.AperturaCajaId == aperturaId && m.Descripcion == marca);
    }

    [Theory]
    [InlineData("0", "8")]    // ingreso: liquidación de Mercado Libre (la genera el sistema)
    [InlineData("0", "6")]    // ingreso: transferencia (la genera el sistema)
    [InlineData("0", "7")]    // ingreso: Mercado Pago (la genera el sistema)
    [InlineData("0", "21")]   // ingreso: reversión de venta (la genera el sistema)
    [InlineData("0", "10")]   // ingreso con un concepto de egreso
    [InlineData("0", "99")]   // ingreso "Otro": la UI solo lo ofrece para egresos
    [InlineData("1", "3")]    // egreso con un concepto de ingreso (cobro de cuota)
    [InlineData("1", "21")]   // egreso: reversión de venta
    public async Task Rechaza_ConceptoNoAdmitidoParaElTipo_YNoRegistraNada(string tipo, string concepto)
    {
        var aperturaId = await SembrarAperturaPropiaAsync();
        var client = _factory.CreateClientWithUserId(User);
        var token = await TokenAsync(client, aperturaId);
        var marca = "FIN-S-007 " + Guid.NewGuid().ToString("N")[..8];

        var response = await PostAsync(client, token, aperturaId, tipo, concepto, marca);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);   // vuelve al formulario con el error
        Assert.Contains("El concepto no es válido para este tipo de movimiento.", html);
        Assert.Equal(0, await CantidadAsync(aperturaId, marca));
    }

    [Fact]
    public async Task Rechaza_ConceptoFueraDelEnum_YNoRegistraNada()
    {
        var aperturaId = await SembrarAperturaPropiaAsync();
        var client = _factory.CreateClientWithUserId(User);
        var token = await TokenAsync(client, aperturaId);
        var marca = "FIN-S-007 " + Guid.NewGuid().ToString("N")[..8];

        var response = await PostAsync(client, token, aperturaId, "1", "77", marca);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);   // vuelve al formulario con el error
        Assert.Equal(0, await CantidadAsync(aperturaId, marca));
    }

    [Theory]
    [InlineData("0", "0")]
    [InlineData("0", "5")]
    [InlineData("1", "10")]
    [InlineData("1", "99")]
    public async Task Acepta_LosConceptosQueLaUiOfrece(string tipo, string concepto)
    {
        var aperturaId = await SembrarAperturaPropiaAsync();
        var client = _factory.CreateClientWithUserId(User);
        var token = await TokenAsync(client, aperturaId);
        var marca = "FIN-S-007 " + Guid.NewGuid().ToString("N")[..8];

        var response = await PostAsync(client, token, aperturaId, tipo, concepto, marca);

        Assert.True(response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Found,
            $"Esperaba redirect y fue {(int)response.StatusCode}.");
        Assert.Equal(1, await CantidadAsync(aperturaId, marca));
    }

    [Fact]
    public void Catalogo_CoincideConElQueOfreceElFormulario()
    {
        // Evita que el servidor y el JS se desalineen sin que nadie lo note: se lee el JS real del repositorio.
        var js = File.ReadAllText(ArchivoRepo("wwwroot", "js", "caja-registrar-movimiento.js"));
        var ingreso = ValoresDe(js, "CONCEPTOS_INGRESO");
        var egreso = ValoresDe(js, "CONCEPTOS_EGRESO");

        foreach (var c in Enum.GetValues<ConceptoMovimientoCaja>())
        {
            Assert.Equal(ingreso.Contains((int)c), ConceptosMovimientoCajaManual.EsPermitido(TipoMovimientoCaja.Ingreso, c));
            Assert.Equal(egreso.Contains((int)c), ConceptosMovimientoCajaManual.EsPermitido(TipoMovimientoCaja.Egreso, c));
        }
    }

    private static HashSet<int> ValoresDe(string js, string constante)
    {
        var bloque = Regex.Match(js, constante + @"\s*=\s*\[(.*?)\];", RegexOptions.Singleline);
        Assert.True(bloque.Success, constante + " no encontrada en el JS.");
        return Regex.Matches(bloque.Groups[1].Value, @"value:\s*'(\d+)'").Select(m => int.Parse(m.Groups[1].Value)).ToHashSet();
    }

    private static string ArchivoRepo(params string[] partes)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TheBuryProyect.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(new[] { dir!.FullName }.Concat(partes).ToArray());
    }
}
