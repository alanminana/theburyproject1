using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// VALID-L4 — campos editables de Orden de compra, Proveedor y Movimiento de stock por el pipeline HTTP real
/// (antiforgery + autorización + binding + servicio). Cada caso fuera de regla debe rechazarse sin persistir.
/// </summary>
[Collection("HttpIntegration")]
public class ComprasStockCamposValidacionHttpTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private static int _counter = 95000;
    private readonly CustomWebApplicationFactory _factory;
    private int _proveedorId;
    private int _productoId;

    public ComprasStockCamposValidacionHttpTests(CustomWebApplicationFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.SeedTestUserAsync();
        var n = Interlocked.Increment(ref _counter);
        await using var ctx = await NuevoContextoAsync();
        var cat = new Categoria { Codigo = $"CL4{n}", Nombre = $"CatL4{n}" };
        var mar = new Marca { Codigo = $"ML4{n}", Nombre = $"MarL4{n}" };
        ctx.Categorias.Add(cat);
        ctx.Marcas.Add(mar);
        await ctx.SaveChangesAsync();
        var prov = new Proveedor { Cuit = $"3000{n:D7}", RazonSocial = $"Proveedor L4 {n}" };
        var prod = new Producto
        {
            Codigo = $"PL4-{n}", Nombre = $"Producto L4 {n}", PrecioCompra = 100m, PrecioVenta = 150m, PorcentajeIVA = 21m,
            StockActual = 10m, CategoriaId = cat.Id, MarcaId = mar.Id, Activo = true,
        };
        ctx.Proveedores.Add(prov);
        ctx.Productos.Add(prod);
        await ctx.SaveChangesAsync();
        _proveedorId = prov.Id;
        _productoId = prod.Id;
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

    // ================= Orden de compra =================

    private static string NuevoNumeroOc() => $"OCL4-{Interlocked.Increment(ref _counter)}";

    private Dictionary<string, string> FormOc(string numero) => new()
    {
        ["Numero"] = numero,
        ["FechaEmision"] = DateTime.UtcNow.ToString("yyyy-MM-dd"),
        ["ProveedorId"] = _proveedorId.ToString(),
        ["Estado"] = "Borrador",
        ["Detalles[0].ProductoId"] = _productoId.ToString(),
        ["Detalles[0].Cantidad"] = "5",
        ["Detalles[0].PrecioUnitario"] = "100",
    };

    private async Task<HttpResponseMessage> PostOcAsync(Dictionary<string, string> campos)
    {
        var client = _factory.CreateAuthenticatedClient();
        campos["__RequestVerificationToken"] = await TokenAsync(client);
        return await client.PostAsync("/OrdenCompra/Create", new FormUrlEncodedContent(campos));
    }

    private async Task<int> OrdenesConNumeroAsync(string numero)
    {
        await using var ctx = await NuevoContextoAsync();
        return await ctx.OrdenesCompra.IgnoreQueryFilters().AsNoTracking().CountAsync(o => o.Numero == numero);
    }

    private async Task AssertOcRechazadaAsync(string campo, string valor)
    {
        var numero = NuevoNumeroOc();
        var form = FormOc(numero);
        form[campo] = valor;

        await PostOcAsync(form);

        Assert.Equal(0, await OrdenesConNumeroAsync(numero));
    }

    [Fact]
    public async Task Oc_Valida_SeAcepta()
    {
        var numero = NuevoNumeroOc();
        var resp = await PostOcAsync(FormOc(numero));

        Assert.True(resp.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Found, $"Esperaba redirect y fue {(int)resp.StatusCode}.");
        Assert.Equal(1, await OrdenesConNumeroAsync(numero));
    }

    [Theory]
    [InlineData("<b>x")]
    [InlineData("OC/1")]
    [InlineData("  ")]
    public Task Oc_Numero_ConFormatoInvalido_SeRechaza(string numero) => AssertOcRechazadaAsync("Numero", numero);

    [Fact]
    public Task Oc_Numero_MasDe50_SeRechaza() => AssertOcRechazadaAsync("Numero", new string('A', 51));

    [Theory]
    [InlineData("Detalles[0].Cantidad", "0")]
    [InlineData("Detalles[0].Cantidad", "-1")]
    [InlineData("Detalles[0].Cantidad", "1,5")]
    [InlineData("Detalles[0].Cantidad", "abc")]
    [InlineData("Detalles[0].Cantidad", "2147483647")]     // total de la orden > 20M
    [InlineData("Detalles[0].PrecioUnitario", "-1")]
    [InlineData("Detalles[0].PrecioUnitario", "0")]
    [InlineData("Detalles[0].PrecioUnitario", "20000001")]
    [InlineData("Detalles[0].PrecioUnitario", "1.500")]    // ambiguo
    [InlineData("Detalles[0].PrecioUnitario", "100,555")]
    [InlineData("Descuento", "-1")]
    [InlineData("Descuento", "1000")]                      // mayor al subtotal (500)
    [InlineData("Descuento", "20000001")]
    [InlineData("Descuento", "1.500")]
    public Task Oc_Importes_FueraDeRegla_SeRechazan(string campo, string valor) => AssertOcRechazadaAsync(campo, valor);

    [Theory]
    [InlineData("FechaEmision", "1900-01-01")]
    [InlineData("FechaEmision", "2999-12-31")]
    [InlineData("FechaEmision", "no-es-fecha")]
    public Task Oc_FechaEmision_FueraDeRango_SeRechaza(string campo, string valor) => AssertOcRechazadaAsync(campo, valor);

    [Fact]
    public Task Oc_EntregaEstimada_AnteriorALaEmision_SeRechaza() =>
        AssertOcRechazadaAsync("FechaEntregaEstimada", DateTime.UtcNow.AddDays(-30).ToString("yyyy-MM-dd"));

    [Theory]
    [InlineData("Estado", "Recibida")]
    [InlineData("Estado", "EnTransito")]
    [InlineData("Estado", "99")]
    public async Task Oc_EstadoForjado_NoSePersisteComoRecibida(string campo, string valor)
    {
        var numero = NuevoNumeroOc();
        var form = FormOc(numero);
        form[campo] = valor;

        await PostOcAsync(form);

        await using var ctx = await NuevoContextoAsync();
        var oc = await ctx.OrdenesCompra.AsNoTracking().FirstOrDefaultAsync(o => o.Numero == numero);
        // En el alta no se puede saltar el circuito: EnTransito y Recibida solo se alcanzan por CambiarEstado/Recepcionar.
        Assert.True(oc == null || oc.Estado is not (TheBuryProject.Models.Enums.EstadoOrdenCompra.EnTransito
                                                    or TheBuryProject.Models.Enums.EstadoOrdenCompra.Recibida),
            $"Estado forjado '{valor}' persistió como {oc?.Estado}.");
    }

    [Theory]
    [InlineData("ProveedorId", "0")]
    [InlineData("ProveedorId", "99999999")]
    [InlineData("Detalles[0].ProductoId", "0")]
    [InlineData("Detalles[0].ProductoId", "99999999")]
    public Task Oc_ProveedorOProductoInexistente_SeRechaza(string campo, string valor) => AssertOcRechazadaAsync(campo, valor);

    [Theory]
    [InlineData("ProveedorId", "")]
    [InlineData("Estado", "NoExiste")]
    [InlineData("Detalles[0].Cantidad", "abc")]
    public async Task Oc_ErroresDeBinding_SeMuestranEnEspanol(string campo, string valor)
    {
        var numero = NuevoNumeroOc();
        var form = FormOc(numero);
        form[campo] = valor;

        var resp = await PostOcAsync(form);
        var html = WebUtility.HtmlDecode(await resp.Content.ReadAsStringAsync());

        Assert.DoesNotContain("The value", html);
        Assert.DoesNotContain("is invalid", html);
        Assert.DoesNotContain("is not valid for", html);
        Assert.Equal(0, await OrdenesConNumeroAsync(numero));
    }

    [Fact]
    public Task Oc_Observaciones_MasDe1000_SeRechaza() => AssertOcRechazadaAsync("Observaciones", new string('x', 1001));

    [Fact]
    public async Task Oc_NumeroDuplicado_SeRechaza()
    {
        var numero = NuevoNumeroOc();
        await PostOcAsync(FormOc(numero));

        await PostOcAsync(FormOc(numero));

        Assert.Equal(1, await OrdenesConNumeroAsync(numero));
    }

    // ================= Proveedor =================

    private static string NuevoCuit()
    {
        // CUIT válido de empresa (prefijo 30) generado con el verificador módulo 11.
        var baseDigitos = $"30{70_000_000 + Interlocked.Increment(ref _counter):D8}";
        int[] mult = { 5, 4, 3, 2, 7, 6, 5, 4, 3, 2 };
        var suma = 0;
        for (var i = 0; i < 10; i++) suma += (baseDigitos[i] - '0') * mult[i];
        var d = 11 - suma % 11;
        d = d == 11 ? 0 : d == 10 ? 9 : d;
        return baseDigitos + d;
    }

    private static Dictionary<string, string> FormProveedor(string cuit) => new()
    {
        ["Cuit"] = cuit,
        ["RazonSocial"] = "Proveedor de prueba SA",
    };

    private async Task<bool> CrearProveedorAsync(Dictionary<string, string> campos)
    {
        var client = _factory.CreateAuthenticatedClient();
        campos["__RequestVerificationToken"] = await TokenAsync(client);
        var resp = await client.PostAsync("/Proveedor/CreateAjax", new FormUrlEncodedContent(campos));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement.GetProperty("success").GetBoolean();
    }

    private async Task<int> ProveedoresConCuitAsync(string cuit)
    {
        await using var ctx = await NuevoContextoAsync();
        return await ctx.Proveedores.IgnoreQueryFilters().AsNoTracking().CountAsync(p => p.Cuit == cuit);
    }

    [Fact]
    public async Task Proveedor_Valido_SeAcepta()
    {
        var cuit = NuevoCuit();
        var form = FormProveedor(cuit);
        form["Email"] = "compras@proveedor.com";
        form["Telefono"] = "11 4567-8901";
        form["CodigoPostal"] = "C1234ABC";
        form["Ciudad"] = "9 de Julio";
        form["Provincia"] = "Buenos Aires";

        Assert.True(await CrearProveedorAsync(form));
        Assert.Equal(1, await ProveedoresConCuitAsync(cuit));
    }

    // Un CUIT nuevo con el dígito verificador incorrecto casi siempre es un error de tipeo: el servidor lo rechaza.
    // (Los CUIT antiguos con verificador incorrecto siguen editables mientras no se cambien: ProveedorServiceTests.)
    [Fact]
    public async Task Proveedor_CuitNuevoConVerificadorIncorrecto_SeRechaza()
    {
        var cuit = "30502680479";   // el verificador válido sería 8

        Assert.False(await CrearProveedorAsync(FormProveedor(cuit)), "El CUIT con verificador incorrecto fue ACEPTADO.");
        Assert.Equal(0, await ProveedoresConCuitAsync(cuit));
    }

    [Theory]
    [InlineData("1234567890")]       // 10 dígitos
    [InlineData("123456789012")]     // 12 dígitos
    [InlineData("30-50268047-8")]    // con guiones: el formulario exige solo dígitos
    [InlineData("abcdefghijk")]
    public async Task Proveedor_Cuit_Invalido_SeRechaza(string cuit)
    {
        Assert.False(await CrearProveedorAsync(FormProveedor(cuit)), $"CUIT '{cuit}' fue ACEPTADO.");
        Assert.Equal(0, await ProveedoresConCuitAsync(cuit));
    }

    [Fact]
    public async Task Proveedor_CuitDuplicado_SeRechaza()
    {
        var cuit = NuevoCuit();
        Assert.True(await CrearProveedorAsync(FormProveedor(cuit)));

        Assert.False(await CrearProveedorAsync(FormProveedor(cuit)));
        Assert.Equal(1, await ProveedoresConCuitAsync(cuit));
    }

    [Theory]
    [InlineData("RazonSocial", "   ")]
    [InlineData("Email", "sin-arroba")]
    [InlineData("Telefono", "abc")]
    [InlineData("CodigoPostal", "12")]
    [InlineData("Provincia", "Buenos Aires 2")]
    [InlineData("Contacto", "Juan 3")]
    public async Task Proveedor_Campos_FueraDeRegla_SeRechazan(string campo, string valor)
    {
        var cuit = NuevoCuit();
        var form = FormProveedor(cuit);
        form[campo] = valor;

        Assert.False(await CrearProveedorAsync(form), $"{campo}='{valor}' fue ACEPTADO.");
        Assert.Equal(0, await ProveedoresConCuitAsync(cuit));
    }

    [Theory]
    [InlineData("RazonSocial", 201)]
    [InlineData("NombreFantasia", 201)]
    [InlineData("Direccion", 301)]
    [InlineData("Aclaraciones", 2001)]
    public async Task Proveedor_TextosDemasiadoLargos_SeRechazan(string campo, int largo)
    {
        var cuit = NuevoCuit();
        var form = FormProveedor(cuit);
        form[campo] = new string('x', largo);

        Assert.False(await CrearProveedorAsync(form));
        Assert.Equal(0, await ProveedoresConCuitAsync(cuit));
    }

    // ================= Movimiento de stock (ajuste) =================

    private async Task<decimal> StockAsync()
    {
        await using var ctx = await NuevoContextoAsync();
        return (await ctx.Productos.AsNoTracking().SingleAsync(p => p.Id == _productoId)).StockActual;
    }

    private async Task PostAjusteAsync(string tipo, string cantidad, string motivo = "QA ajuste")
    {
        var client = _factory.CreateAuthenticatedClient();
        await client.PostAsync("/MovimientoStock/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = await TokenAsync(client),
            ["ProductoId"] = _productoId.ToString(),
            ["Tipo"] = tipo,
            ["Cantidad"] = cantidad,
            ["Motivo"] = motivo,
            ["RequestId"] = Guid.NewGuid().ToString(),
        }));
    }

    [Theory]
    [InlineData("Entrada", "0")]
    [InlineData("Entrada", "-5")]
    [InlineData("Entrada", "1000000")]
    [InlineData("Entrada", "1.500")]        // ambiguo
    [InlineData("Entrada", "abc")]
    [InlineData("Salida", "11")]            // más que el stock (10)
    [InlineData("Salida", "-1")]
    [InlineData("Ajuste", "-1")]
    [InlineData("Ajuste", "1000000")]
    [InlineData("Otro", "5")]
    [InlineData("99", "5")]
    public async Task Ajuste_FueraDeRegla_NoCambiaElStock(string tipo, string cantidad)
    {
        var antes = await StockAsync();

        await PostAjusteAsync(tipo, cantidad);

        Assert.Equal(antes, await StockAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Ajuste_MotivoEnBlanco_NoCambiaElStock(string motivo)
    {
        var antes = await StockAsync();

        await PostAjusteAsync("Entrada", "5", motivo);

        Assert.Equal(antes, await StockAsync());
    }

    [Fact]
    public async Task Ajuste_MotivoDemasiadoLargo_NoCambiaElStock()
    {
        var antes = await StockAsync();

        await PostAjusteAsync("Entrada", "5", new string('m', 501));

        Assert.Equal(antes, await StockAsync());
    }

    [Fact]
    public async Task Ajuste_EntradaValida_SumaAlStock()
    {
        var antes = await StockAsync();

        await PostAjusteAsync("Entrada", "5");

        Assert.Equal(antes + 5, await StockAsync());
    }
}
