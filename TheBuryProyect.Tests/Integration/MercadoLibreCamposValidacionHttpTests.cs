using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Controllers;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// VALID-L7 — Mercado Libre: respuestas a compradores, devoluciones, lotes de precios y ediciones de publicación.
/// Los valores fuera de regla no deben guardarse ni recortarse en silencio.
/// </summary>
[Collection("HttpIntegration")]
public class MercadoLibreCamposValidacionHttpTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private static int _counter = 98000;
    private readonly CustomWebApplicationFactory _factory;

    public MercadoLibreCamposValidacionHttpTests(CustomWebApplicationFactory factory) => _factory = factory;

    public async Task InitializeAsync() => await _factory.SeedTestUserAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<AppDbContext> NuevoContextoAsync()
    {
        var scope = _factory.Services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        return await factory.CreateDbContextAsync();
    }

    private static async Task<string> TokenAsync(HttpClient client)
    {
        var login = await client.GetAsync("/Identity/Account/Login");
        var match = Regex.Match(await login.Content.ReadAsStringAsync(), "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, "Sin token antiforgery.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private async Task<HttpResponseMessage> PostAsync(string ruta, Dictionary<string, string> campos)
    {
        var client = _factory.CreateAuthenticatedClient();
        campos["__RequestVerificationToken"] = await TokenAsync(client);
        return await client.PostAsync(ruta, new FormUrlEncodedContent(campos));
    }

    private async Task<(MercadoLibreAccount Cuenta, MercadoLibreListing Listing)> SembrarCuentaYListingAsync()
    {
        var n = Interlocked.Increment(ref _counter);
        await using var ctx = await NuevoContextoAsync();
        var cuenta = new MercadoLibreAccount
        {
            MeliUserId = n,
            Nickname = $"QA{n}",
            AccessTokenEncrypted = "x",
            RefreshTokenEncrypted = "x",
            Activa = true
        };
        ctx.MercadoLibreAccounts.Add(cuenta);
        await ctx.SaveChangesAsync();
        var listing = new MercadoLibreListing
        {
            AccountId = cuenta.Id,
            ItemId = $"MLA{n}",
            Titulo = "Listing QA",
            Precio = 1000m,
            AvailableQuantity = 5,
            Status = "active"
        };
        ctx.MercadoLibreListings.Add(listing);
        await ctx.SaveChangesAsync();
        return (cuenta, listing);
    }

    private async Task<MercadoLibreOrder> SembrarOrdenAsync(int accountId)
    {
        var n = Interlocked.Increment(ref _counter);
        await using var ctx = await NuevoContextoAsync();
        var orden = new MercadoLibreOrder
        {
            AccountId = accountId,
            MeliOrderId = n,
            Status = "paid",
            TotalAmount = 1000m,
            FechaCreacionUtc = DateTime.UtcNow,
            BuyerId = 888
        };
        ctx.MercadoLibreOrders.Add(orden);
        await ctx.SaveChangesAsync();
        return orden;
    }

    // ================= Preguntas =================

    private async Task<MercadoLibreQuestion> SembrarPreguntaAsync()
    {
        var (cuenta, listing) = await SembrarCuentaYListingAsync();
        await using var ctx = await NuevoContextoAsync();
        var pregunta = new MercadoLibreQuestion
        {
            AccountId = cuenta.Id,
            QuestionId = Interlocked.Increment(ref _counter),
            ListingId = listing.Id,
            ItemId = listing.ItemId,
            TextoPregunta = "¿Tienen stock?",
            Estado = MercadoLibreQuestionEstado.Pendiente,
            EsSimulada = false
        };
        ctx.MercadoLibreQuestions.Add(pregunta);
        await ctx.SaveChangesAsync();
        return pregunta;
    }

    private async Task<MercadoLibreQuestion> PreguntaAsync(int id)
    {
        await using var ctx = await NuevoContextoAsync();
        return await ctx.MercadoLibreQuestions.AsNoTracking().SingleAsync(q => q.Id == id);
    }

    [Fact]
    public async Task Pregunta_RespuestaValida_SeGuarda()
    {
        var p = await SembrarPreguntaAsync();

        await PostAsync("/MercadoLibre/PreguntaResponder", new() { ["id"] = p.Id.ToString(), ["respuesta"] = "Sí, hay stock." });

        Assert.Equal("Sí, hay stock.", (await PreguntaAsync(p.Id)).RespuestaTexto);
    }

    [Fact]
    public async Task Pregunta_RespuestaMasLargaQue2000_NoSeRecortaNiSeGuarda()
    {
        var p = await SembrarPreguntaAsync();

        await PostAsync("/MercadoLibre/PreguntaResponder", new() { ["id"] = p.Id.ToString(), ["respuesta"] = new string('x', 2001) });

        var luego = await PreguntaAsync(p.Id);
        Assert.Null(luego.RespuestaTexto);
        Assert.Equal(MercadoLibreQuestionEstado.Pendiente, luego.Estado);
    }

    // ================= Devolución =================

    [Fact]
    public async Task Devolucion_NotaMasLargaQue500_NoSeGuarda()
    {
        var (cuenta, _) = await SembrarCuentaYListingAsync();
        var orden = await SembrarOrdenAsync(cuenta.Id);

        await PostAsync("/MercadoLibre/OrdenDevolucion", new()
        {
            ["id"] = orden.Id.ToString(),
            ["decision"] = ((int)MercadoLibreDevolucionEstado.NoReingresa).ToString(),
            ["nota"] = new string('x', 501)
        });

        await using var ctx = await NuevoContextoAsync();
        var luego = await ctx.MercadoLibreOrders.AsNoTracking().SingleAsync(o => o.Id == orden.Id);
        Assert.Null(luego.DevolucionNota);
        Assert.Equal(MercadoLibreDevolucionEstado.Ninguna, luego.DevolucionEstado);
    }

    // ================= Lote de precios =================

    private static Dictionary<string, string> FormLote() => new()
    {
        ["Nombre"] = "Lote QA",
        ["Origen"] = "0",
        ["ValorAjustePorcentaje"] = "10",
        ["SoloVinculadas"] = "true",
    };

    [Theory]
    [InlineData("PrecioDesde", "-1")]
    [InlineData("PrecioHasta", "30000000")]
    [InlineData("PrecioDesde", "30000000")]
    public async Task LotePrecios_RangoFueraDeRegla_NoSeCrea(string campo, string valor)
    {
        var form = FormLote();
        form[campo] = valor;

        var resp = await PostAsync("/MercadoLibre/AumentoSimular", form);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode); // vuelve al formulario con el error
    }

    [Fact]
    public async Task LotePrecios_DesdeMayorQueHasta_NoSeCrea()
    {
        var form = FormLote();
        form["PrecioDesde"] = "5000";
        form["PrecioHasta"] = "1000";

        var resp = await PostAsync("/MercadoLibre/AumentoSimular", form);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task LotePrecios_Valido_Redirige()
    {
        var form = FormLote();
        form["PrecioDesde"] = "1000";
        form["PrecioHasta"] = "5000";

        var resp = await PostAsync("/MercadoLibre/AumentoSimular", form);

        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
    }

    // ================= Ediciones de publicación (reglas puras) =================

    [Theory]
    [InlineData(null, null, null, null, true)]
    [InlineData("Título válido", 1500.50, 10, "SKU-1", true)]
    [InlineData("x", 0.01, 0, null, true)]
    [InlineData("12345678901234567890123456789012345678901234567890123456789012345", null, null, null, false)] // 65 > 60
    [InlineData(null, 0.0, null, null, false)]
    [InlineData(null, -5.0, null, null, false)]
    [InlineData(null, 20000000.01, null, null, false)]
    [InlineData(null, 10.123, null, null, false)]
    [InlineData(null, null, -1, null, false)]
    [InlineData(null, null, 1000000, null, false)]
    public void EdicionListing_ValidaTituloPrecioYStock(string? titulo, double? precio, int? stock, string? sku, bool valido)
    {
        var error = MercadoLibreController.ValidarEdicionListing(titulo, precio.HasValue ? (decimal)precio.Value : null, stock, sku);

        Assert.Equal(valido, error == null);
    }

    [Fact]
    public void EdicionListing_SkuMasLargoQue100_Falla()
        => Assert.NotNull(MercadoLibreController.ValidarEdicionListing(null, null, null, new string('s', 101)));

    [Theory]
    [InlineData(1.0, true)]
    [InlineData(20000000.0, true)]
    [InlineData(0.0, false)]
    [InlineData(-10.0, false)]
    [InlineData(20000000.01, false)]
    [InlineData(10.555, false)]
    public void Liquidacion_ValidaNeto(double neto, bool valido)
        => Assert.Equal(valido, MercadoLibreController.ValidarNetoLiquidacion((decimal)neto) == null);
}
