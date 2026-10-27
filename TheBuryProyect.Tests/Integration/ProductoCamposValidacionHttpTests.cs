using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// VALID-L3 — campos editables de Producto y de los cambios de precio por el pipeline HTTP real (antiforgery +
/// autorización + binding + servicio). Cada caso fuera de regla debe rechazarse sin persistir; los de borde se aceptan.
/// </summary>
[Collection("HttpIntegration")]
public class ProductoCamposValidacionHttpTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private static int _counter = 94000;
    private readonly CustomWebApplicationFactory _factory;
    private int _categoriaId;
    private int _marcaId;

    public ProductoCamposValidacionHttpTests(CustomWebApplicationFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.SeedTestUserAsync();
        var n = Interlocked.Increment(ref _counter);
        await using var ctx = await NuevoContextoAsync();
        var cat = new Categoria { Codigo = $"CL3{n}", Nombre = $"CatL3{n}" };
        var mar = new Marca { Codigo = $"ML3{n}", Nombre = $"MarL3{n}" };
        ctx.Categorias.Add(cat);
        ctx.Marcas.Add(mar);
        await ctx.SaveChangesAsync();
        _categoriaId = cat.Id;
        _marcaId = mar.Id;
    }

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

    private static string NuevoCodigo() => $"PL3-{Interlocked.Increment(ref _counter)}";

    private Dictionary<string, string> FormProducto(string codigo) => new()
    {
        ["Codigo"] = codigo,
        ["Nombre"] = "Producto de prueba",
        ["CategoriaId"] = _categoriaId.ToString(),
        ["MarcaId"] = _marcaId.ToString(),
        ["PrecioCompra"] = "1000",
        ["PrecioVenta"] = "1500",
        ["PorcentajeIVA"] = "21",
        ["StockActual"] = "10",
        ["StockMinimo"] = "2",
        ["Activo"] = "true",
    };

    private async Task<bool> CrearAsync(Dictionary<string, string> campos)
    {
        var client = _factory.CreateAuthenticatedClient();
        campos["__RequestVerificationToken"] = await TokenAsync(client);
        var resp = await client.PostAsync("/Producto/CreateAjax", new FormUrlEncodedContent(campos));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement.GetProperty("success").GetBoolean();
    }

    private async Task<int> ProductosConCodigoAsync(string codigo)
    {
        await using var ctx = await NuevoContextoAsync();
        return await ctx.Productos.IgnoreQueryFilters().AsNoTracking().CountAsync(p => p.Codigo == codigo);
    }

    private async Task AssertAltaRechazadaAsync(string campo, string valor)
    {
        var codigo = NuevoCodigo();
        var form = FormProducto(codigo);
        form[campo] = valor;

        Assert.False(await CrearAsync(form), $"{campo}='{valor}' fue ACEPTADO.");
        Assert.Equal(0, await ProductosConCodigoAsync(codigo));
    }

    [Fact]
    public async Task Alta_Valida_SeAcepta()
    {
        var codigo = NuevoCodigo();
        Assert.True(await CrearAsync(FormProducto(codigo)));
        Assert.Equal(1, await ProductosConCodigoAsync(codigo));
    }

    [Theory]
    [InlineData("A/B")]
    [InlineData("<b>x")]
    [InlineData("-X1")]
    [InlineData("A  B")]
    [InlineData("ñandú")]
    [InlineData("X'1")]
    public Task Alta_Codigo_ConFormatoInvalido_SeRechaza(string codigo) =>
        AssertAltaRechazadaAsync("Codigo", codigo);

    [Theory]
    [InlineData("001002")]
    [InlineData("cat-0099")]
    [InlineData("CAT-01")]
    [InlineData("A1.2")]
    [InlineData("Lote 7")]
    public async Task Alta_Codigo_ComoLosDeLaBaseReal_SeAcepta(string baseCodigo)
    {
        var codigo = $"{baseCodigo}-{Interlocked.Increment(ref _counter)}";
        Assert.True(await CrearAsync(FormProducto(codigo)));
    }

    [Fact]
    public Task Alta_Codigo_MasDe50_SeRechaza() => AssertAltaRechazadaAsync("Codigo", new string('A', 51));

    [Theory]
    [InlineData("Nombre", "   ")]
    [InlineData("Nombre", "")]
    public Task Alta_NombreEnBlanco_SeRechaza(string campo, string valor) => AssertAltaRechazadaAsync(campo, valor);

    [Theory]
    [InlineData("Nombre", 201)]
    [InlineData("Descripcion", 1001)]
    public Task Alta_TextosDemasiadoLargos_SeRechazan(string campo, int largo) =>
        AssertAltaRechazadaAsync(campo, new string('x', largo));

    [Theory]
    [InlineData("PrecioVenta", "0")]
    [InlineData("PrecioVenta", "-1")]
    [InlineData("PrecioVenta", "20000001")]
    [InlineData("PrecioVenta", "1.500")]        // ambiguo
    [InlineData("PrecioVenta", "1500,555")]
    [InlineData("PrecioVenta", "abc")]
    [InlineData("PrecioCompra", "-1")]
    [InlineData("PrecioCompra", "20000001")]
    [InlineData("CostoEnvio", "-1")]
    [InlineData("PercepcionesCompra", "-1")]
    [InlineData("PercepcionesCompra", "20000001")]
    [InlineData("OtrosCostosCompra", "-1")]
    [InlineData("OtrosCostosCompra", "20000001")]
    public Task Alta_Importes_FueraDeRegla_SeRechazan(string campo, string valor) =>
        AssertAltaRechazadaAsync(campo, valor);

    [Theory]
    [InlineData("PorcentajeIVA", "-1")]
    [InlineData("PorcentajeIVA", "101")]
    [InlineData("PorcentajeIVA", "19")]          // no es una alícuota argentina
    [InlineData("PorcentajeIVA", "33")]
    [InlineData("PorcentajeIVACompra", "19")]
    [InlineData("PorcentajeIVACompra", "-5")]
    public Task Alta_Iva_FueraDeLasAlicuotasArgentinas_SeRechaza(string campo, string valor) =>
        AssertAltaRechazadaAsync(campo, valor);

    [Theory]
    [InlineData("0")]
    [InlineData("2,5")]
    [InlineData("5")]
    [InlineData("10,5")]
    [InlineData("21")]
    [InlineData("27")]
    public async Task Alta_AlicuotasArgentinas_SeAceptan(string iva)
    {
        var form = FormProducto(NuevoCodigo());
        form["PorcentajeIVA"] = iva;
        Assert.True(await CrearAsync(form));
    }

    [Theory]
    [InlineData("StockActual", "-1")]
    [InlineData("StockActual", "1000000")]
    [InlineData("StockActual", "99999999999")]
    [InlineData("StockMinimo", "-1")]
    [InlineData("StockMinimo", "1000000")]
    [InlineData("ComisionPorcentaje", "-1")]
    [InlineData("ComisionPorcentaje", "101")]
    public Task Alta_StockYComision_FueraDeRegla_SeRechazan(string campo, string valor) =>
        AssertAltaRechazadaAsync(campo, valor);

    [Fact]
    public async Task Alta_CodigoDuplicado_SeRechaza_SinImportarMayusculas()
    {
        var codigo = NuevoCodigo();
        Assert.True(await CrearAsync(FormProducto(codigo)));

        Assert.False(await CrearAsync(FormProducto(codigo)));
        Assert.False(await CrearAsync(FormProducto(codigo.ToLowerInvariant())));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("99999")]
    [InlineData("-5")]
    public async Task Alta_CategoriaOMarcaInexistente_SeRechaza(string id)
    {
        var codigo = NuevoCodigo();
        var form = FormProducto(codigo);
        form["CategoriaId"] = id;
        Assert.False(await CrearAsync(form));
        Assert.Equal(0, await ProductosConCodigoAsync(codigo));

        codigo = NuevoCodigo();
        form = FormProducto(codigo);
        form["MarcaId"] = id;
        Assert.False(await CrearAsync(form));
        Assert.Equal(0, await ProductosConCodigoAsync(codigo));
    }

    // ---- Comisión del vendedor (parámetro string) ----

    private async Task<int> SembrarProductoAsync()
    {
        var codigo = NuevoCodigo();
        Assert.True(await CrearAsync(FormProducto(codigo)));
        await using var ctx = await NuevoContextoAsync();
        return (await ctx.Productos.AsNoTracking().SingleAsync(p => p.Codigo == codigo)).Id;
    }

    private async Task<decimal> ComisionAsync(int id)
    {
        await using var ctx = await NuevoContextoAsync();
        return (await ctx.Productos.AsNoTracking().SingleAsync(p => p.Id == id)).ComisionPorcentaje;
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("101")]
    [InlineData("abc")]
    [InlineData("1e3")]
    public async Task ActualizarComision_FueraDeRegla_NoCambia(string porcentaje)
    {
        var id = await SembrarProductoAsync();
        var antes = await ComisionAsync(id);
        var client = _factory.CreateAuthenticatedClient();

        await client.PostAsync("/Producto/ActualizarComisionVendedor", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = await TokenAsync(client),
            ["productoId"] = id.ToString(),
            ["porcentajeComision"] = porcentaje,
        }));

        Assert.Equal(antes, await ComisionAsync(id));
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("2,5", 2.5)]
    [InlineData("100", 100)]
    public async Task ActualizarComision_ValidaDeBorde_SeAplica(string porcentaje, double esperado)
    {
        var id = await SembrarProductoAsync();
        var client = _factory.CreateAuthenticatedClient();

        await client.PostAsync("/Producto/ActualizarComisionVendedor", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = await TokenAsync(client),
            ["productoId"] = id.ToString(),
            ["porcentajeComision"] = porcentaje,
        }));

        Assert.Equal((decimal)esperado, await ComisionAsync(id));
    }

    // ---- Cambio directo de precio (porcentaje sobre productos seleccionados) ----

    private async Task<decimal> PrecioVentaAsync(int id)
    {
        await using var ctx = await NuevoContextoAsync();
        return (await ctx.Productos.AsNoTracking().SingleAsync(p => p.Id == id)).PrecioVenta;
    }

    private async Task AplicarPrecioDirectoAsync(int productoId, string porcentaje, string? motivo = null)
    {
        var client = _factory.CreateAuthenticatedClient();
        var campos = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = await TokenAsync(client),
            ["Alcance"] = "seleccionados",
            ["ValorPorcentaje"] = porcentaje,
            ["ProductoIdsText"] = productoId.ToString(),
            ["TipoCambio"] = "Porcentaje",
            ["CantidadEsperada"] = "1",
        };
        if (motivo != null) campos["Motivo"] = motivo;
        await client.PostAsync("/Catalogo/AplicarCambioPrecioDirecto", new FormUrlEncodedContent(campos));
    }

    [Theory]
    [InlineData("2000000")]   // +2.000.000 % -> más de 20 millones
    [InlineData("-100")]
    [InlineData("-150")]
    [InlineData("0")]
    [InlineData("abc")]
    public async Task CambioDirecto_PorcentajeFueraDeRegla_NoCambiaElPrecio(string porcentaje)
    {
        var id = await SembrarProductoAsync();
        var antes = await PrecioVentaAsync(id);

        await AplicarPrecioDirectoAsync(id, porcentaje);

        Assert.Equal(antes, await PrecioVentaAsync(id));
    }

    [Fact]
    public async Task CambioDirecto_MotivoDemasiadoLargo_NoCambiaElPrecio()
    {
        var id = await SembrarProductoAsync();
        var antes = await PrecioVentaAsync(id);

        await AplicarPrecioDirectoAsync(id, "10", new string('m', 1001));

        Assert.Equal(antes, await PrecioVentaAsync(id));
    }

    [Fact]
    public async Task CambioDirecto_PorcentajeValido_AplicaElAumento()
    {
        var id = await SembrarProductoAsync();
        var antes = await PrecioVentaAsync(id);

        await AplicarPrecioDirectoAsync(id, "10", "Ajuste de lista");

        var despues = await PrecioVentaAsync(id);
        Assert.True(despues > antes && despues <= antes * 1.11m, $"Precio {antes} -> {despues}");
    }
}
