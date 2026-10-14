using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// QA-PRD-P0 (B1/B2/B9): los permisos de escritura de stock y de importes se exigen en el servidor
/// y la UI solo ofrece lo que el servidor acepta. Los rechazos no deben dejar movimientos ni historial.
/// </summary>
[Collection("HttpIntegration")]
public class QaPrdPermisosHttpTests : IClassFixture<CustomWebApplicationFactory>
{
    private const int ProductoId = 9100;
    private readonly CustomWebApplicationFactory _factory;

    public QaPrdPermisosHttpTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ── B1: movimientos de stock ─────────────────────────────────────────

    [Fact]
    public async Task MovimientoStock_Post_ConSoloVer_EsRechazadoYNoPersisteNada()
    {
        await SeedProductoAsync();
        await _factory.SeedUserWithStockLectorAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.StockLectorUserId);

        var response = await client.PostAsync("/MovimientoStock/Create", AjusteForm(token: "sin-token", cantidad: 3));

        Assert.True(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Redirect,
            $"Se esperaba rechazo, se obtuvo {(int)response.StatusCode}.");
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, await ContarMovimientosAsync());
        Assert.Equal(10m, await StockAsync());
    }

    [Fact]
    public async Task MovimientoStock_GetCreate_ConSoloVer_EsRechazado()
    {
        await SeedProductoAsync();
        await _factory.SeedUserWithStockLectorAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.StockLectorUserId);

        var response = await client.GetAsync($"/MovimientoStock/Create?productoId={ProductoId}");

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task MovimientoStock_ConSoloVer_PuedeConsultarKardexSinControlesDeEscritura()
    {
        await SeedProductoAsync();
        await _factory.SeedUserWithStockLectorAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.StockLectorUserId);

        var response = await client.GetAsync($"/MovimientoStock/Kardex/{ProductoId}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("data-ajuste-stock-open", html);
        Assert.DoesNotContain("modal-ajuste-stock", html);
    }

    [Fact]
    public async Task MovimientoStock_ConPermisoRegistrar_RegistraElMovimientoYMuestraElControl()
    {
        await SeedProductoAsync();
        await _factory.SeedUserWithStockOperadorAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.StockOperadorUserId);

        var kardex = await (await client.GetAsync($"/MovimientoStock/Kardex/{ProductoId}")).Content.ReadAsStringAsync();
        Assert.Contains("data-ajuste-stock-open", kardex);

        var token = await TokenAsync(client, $"/MovimientoStock/Create?productoId={ProductoId}");
        var response = await client.PostAsync("/MovimientoStock/Create", AjusteForm(token, cantidad: 3));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(1, await ContarMovimientosAsync());
        Assert.Equal(13m, await StockAsync());
    }

    // ── B2: importes del producto ────────────────────────────────────────

    [Fact]
    public async Task EditAjax_SinPreciosApply_RechazaCambioDeImportesYNoDejaHistorial()
    {
        await SeedProductoAsync();
        await _factory.SeedUserWithProductoEditSinCostoEnvioAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.ProductoEditSinCostoEnvioUserId);
        var token = await TokenAsync(client, "/Catalogo");

        var (status, json) = await EditAjaxAsync(client, token, nombre: "Producto QA", precioCompra: 10, precioVenta: 11);

        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        var (compra, venta, nombre) = await PreciosAsync();
        Assert.Equal(1000m, compra);
        Assert.Equal(1500m, venta);
        Assert.Equal("Producto QA", nombre);
        Assert.Equal(0, await ContarHistoricosAsync());
    }

    [Fact]
    public async Task EditAjax_SinPreciosApply_RechazaCambioSoloDelPrecioDeVenta()
    {
        await SeedProductoAsync();
        await _factory.SeedUserWithProductoEditSinCostoEnvioAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.ProductoEditSinCostoEnvioUserId);
        var token = await TokenAsync(client, "/Catalogo");

        var (status, _) = await EditAjaxAsync(client, token, nombre: "Otro nombre", precioCompra: 1000, precioVenta: 1600);

        Assert.Equal(HttpStatusCode.Forbidden, status);
        var (_, venta, nombre) = await PreciosAsync();
        Assert.Equal(1500m, venta);
        Assert.Equal("Producto QA", nombre);
    }

    [Fact]
    public async Task EditAjax_SinPreciosApply_PuedeEditarDatosGeneralesSiLosImportesNoCambian()
    {
        await SeedProductoAsync();
        await _factory.SeedUserWithProductoEditSinCostoEnvioAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.ProductoEditSinCostoEnvioUserId);
        var token = await TokenAsync(client, "/Catalogo");

        var (status, json) = await EditAjaxAsync(client, token, nombre: "Nombre nuevo", precioCompra: 1000, precioVenta: 1500);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(json.RootElement.GetProperty("success").GetBoolean(), json.RootElement.ToString());
        var (compra, venta, nombre) = await PreciosAsync();
        Assert.Equal("Nombre nuevo", nombre);
        Assert.Equal(1000m, compra);
        Assert.Equal(1500m, venta);
        Assert.Equal(0, await ContarHistoricosAsync());
    }

    [Fact]
    public async Task EditAjax_ConPreciosApply_PuedeModificarImportesYQuedaHistorial()
    {
        await SeedProductoAsync();
        await _factory.SeedUserWithProductoEditConPreciosAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.ProductoEditConPreciosUserId);
        var token = await TokenAsync(client, "/Catalogo");

        var (status, json) = await EditAjaxAsync(client, token, nombre: "Producto QA", precioCompra: 1200, precioVenta: 1800);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(json.RootElement.GetProperty("success").GetBoolean(), json.RootElement.ToString());
        var (compra, venta, _) = await PreciosAsync();
        Assert.Equal(1200m, compra);
        Assert.Equal(1800m, venta);
        Assert.Equal(1, await ContarHistoricosAsync());
    }

    // ── B9: la UI refleja los permisos efectivos ─────────────────────────

    [Fact]
    public async Task Catalogo_SoloLectura_NoOfreceCrearEditarNiAjusteMasivo()
    {
        await SeedProductoAsync();
        await _factory.SeedUserWithProductoSoloLecturaAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.ProductoSoloLecturaUserId);

        var html = await (await client.GetAsync("/Catalogo")).Content.ReadAsStringAsync();

        Assert.DoesNotContain("id=\"btn-crear-producto\"", html);
        Assert.DoesNotContain("id=\"btn-ajuste-masivo\"", html);
        Assert.DoesNotContain("data-catalogo-modal-open=\"precio-selection\"", html);
        Assert.DoesNotContain("data-prod-edit-id=\"" + ProductoId + "\"", html);
    }

    [Fact]
    public async Task Catalogo_SinPreciosApply_RenderizaCostosYPrecioDeEdicionComoSoloLectura()
    {
        await SeedProductoAsync();
        await _factory.SeedUserWithProductoEditSinCostoEnvioAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.ProductoEditSinCostoEnvioUserId);

        var html = await (await client.GetAsync("/Catalogo")).Content.ReadAsStringAsync();

        Assert.Contains("data-prod-edit-id=\"" + ProductoId + "\"", html);
        foreach (var id in new[] { "prod-edit-precioCompra", "prod-edit-percepcionesCompra", "prod-edit-otrosCostosCompra", "prod-edit-precioFinal" })
            Assert.Contains("readonly", Tag(html, "input", id));
        Assert.Contains("disabled", Tag(html, "select", "prod-edit-porcentajeIVACompra"));
    }

    [Fact]
    public async Task Catalogo_ConPreciosApply_RenderizaImportesDeEdicionEditables()
    {
        await SeedProductoAsync();
        await _factory.SeedUserWithProductoEditConPreciosAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.ProductoEditConPreciosUserId);

        var html = await (await client.GetAsync("/Catalogo")).Content.ReadAsStringAsync();

        foreach (var id in new[] { "prod-edit-precioCompra", "prod-edit-percepcionesCompra", "prod-edit-otrosCostosCompra", "prod-edit-precioFinal" })
            Assert.DoesNotContain("readonly", Tag(html, "input", id));
        Assert.DoesNotContain("disabled", Tag(html, "select", "prod-edit-porcentajeIVACompra"));
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static string Tag(string html, string tag, string id)
    {
        var match = Regex.Match(html, $"<{tag}[^>]*id=\"{id}\"[^>]*>", RegexOptions.Singleline);
        Assert.True(match.Success, $"No se renderizó <{tag} id=\"{id}\">.");
        return match.Value;
    }

    private static FormUrlEncodedContent AjusteForm(string token, decimal cantidad) =>
        new(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["ProductoId"] = ProductoId.ToString(),
            ["Tipo"] = "Entrada",
            ["Cantidad"] = cantidad.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Motivo"] = "Reposición QA"
        });

    private async Task<(HttpStatusCode, JsonDocument)> EditAjaxAsync(
        HttpClient client, string token, string nombre, int precioCompra, int precioVenta)
    {
        byte[] rowVersion;
        await using (var db = await NewContextAsync())
            rowVersion = (await db.Productos.AsNoTracking().FirstAsync(p => p.Id == ProductoId)).RowVersion;

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Id"] = ProductoId.ToString(),
            ["Codigo"] = "QA-PRD-P0",
            ["Nombre"] = nombre,
            ["CategoriaId"] = "9100",
            ["MarcaId"] = "9100",
            ["PrecioCompra"] = precioCompra.ToString(),
            ["PrecioVenta"] = precioVenta.ToString(),
            ["PorcentajeIVA"] = "21",
            ["StockMinimo"] = "0",
            ["StockActual"] = "10",
            ["Activo"] = "true",
            ["RowVersion"] = Convert.ToBase64String(rowVersion)
        });
        var response = await client.PostAsync($"/Producto/EditAjax?id={ProductoId}", form);
        var body = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body));
    }

    private static async Task<string> TokenAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, $"No se encontró el token antiforgery en {url}.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private async Task<AppDbContext> NewContextAsync()
    {
        var scope = _factory.Services.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        return await contextFactory.CreateDbContextAsync();
    }

    private async Task SeedProductoAsync()
    {
        await using var db = await NewContextAsync();
        var producto = await db.Productos.FirstOrDefaultAsync(p => p.Id == ProductoId);
        if (producto == null)
        {
            db.Categorias.Add(new Categoria { Id = 9100, Nombre = "Cat QA-PRD", RowVersion = new byte[8] });
            db.Marcas.Add(new Marca { Id = 9100, Nombre = "Marca QA-PRD", RowVersion = new byte[8] });
            await db.SaveChangesAsync();
            producto = new Producto
            {
                Id = ProductoId,
                Codigo = "QA-PRD-P0",
                Nombre = "Producto QA",
                CategoriaId = 9100,
                MarcaId = 9100,
                PrecioCompra = 1000m,
                PrecioVenta = 1500m,
                PorcentajeIVA = 21m,
                StockActual = 10m,
                Activo = true,
                RowVersion = new byte[8]
            };
            db.Productos.Add(producto);
        }
        else
        {
            // Estado inicial determinista para cada test (la fábrica comparte la base entre tests).
            producto.Nombre = "Producto QA";
            producto.PrecioCompra = 1000m;
            producto.PrecioVenta = 1500m;
            producto.StockActual = 10m;
        }

        db.MovimientosStock.RemoveRange(db.MovimientosStock.Where(m => m.ProductoId == ProductoId));
        db.PreciosHistoricos.RemoveRange(db.PreciosHistoricos.Where(h => h.ProductoId == ProductoId));
        await db.SaveChangesAsync();
    }

    private async Task<int> ContarMovimientosAsync()
    {
        await using var db = await NewContextAsync();
        return await db.MovimientosStock.CountAsync(m => m.ProductoId == ProductoId);
    }

    private async Task<int> ContarHistoricosAsync()
    {
        await using var db = await NewContextAsync();
        return await db.PreciosHistoricos.CountAsync(h => h.ProductoId == ProductoId);
    }

    private async Task<decimal> StockAsync()
    {
        await using var db = await NewContextAsync();
        return (await db.Productos.AsNoTracking().FirstAsync(p => p.Id == ProductoId)).StockActual;
    }

    private async Task<(decimal Compra, decimal Venta, string Nombre)> PreciosAsync()
    {
        await using var db = await NewContextAsync();
        var p = await db.Productos.AsNoTracking().FirstAsync(x => x.Id == ProductoId);
        return (p.PrecioCompra, p.PrecioVenta, p.Nombre);
    }
}
