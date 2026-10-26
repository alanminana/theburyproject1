using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// VALID-L1 — campos editables de Venta/Create por el pipeline HTTP real (antiforgery + autorización + model
/// binding + servicio). Cada caso manda un valor fuera de regla contra Venta/CreateAjax y exige que se rechace
/// sin persistir la venta; los casos válidos de borde exigen que se acepten.
/// </summary>
[Collection("HttpIntegration")]
public class VentaCamposValidacionHttpTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private static int _counter = 91000;
    private readonly CustomWebApplicationFactory _factory;

    public VentaCamposValidacionHttpTests(CustomWebApplicationFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.SeedTestUserAsync();
        await using var ctx = await NuevoContextoAsync();
        if (await ctx.AperturasCaja.AnyAsync(a => a.UsuarioApertura == "testuser" && !a.Cerrada && !a.IsDeleted))
            return;
        var n = Interlocked.Increment(ref _counter);
        var caja = new Caja { Codigo = $"VL{n}", Nombre = $"Caja VL {n}", RowVersion = new byte[8] };
        ctx.Cajas.Add(caja);
        await ctx.SaveChangesAsync();
        ctx.AperturasCaja.Add(new AperturaCaja
        {
            CajaId = caja.Id, UsuarioApertura = "testuser", MontoInicial = 0m, Cerrada = false, RowVersion = new byte[8]
        });
        await ctx.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<AppDbContext> NuevoContextoAsync()
    {
        var scope = _factory.Services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        return await factory.CreateDbContextAsync();
    }

    private async Task<(int ClienteId, int ProductoId)> SeedAsync()
    {
        var n = Interlocked.Increment(ref _counter);
        await using var ctx = await NuevoContextoAsync();
        var cat = new Categoria { Codigo = $"CV{n}", Nombre = $"CatV{n}" };
        var mar = new Marca { Codigo = $"MV{n}", Nombre = $"MarV{n}" };
        ctx.Categorias.Add(cat);
        ctx.Marcas.Add(mar);
        await ctx.SaveChangesAsync();
        var prod = new Producto
        {
            Codigo = $"V{n}", Nombre = $"ProdV{n}", PrecioVenta = 1_000m, StockActual = 10,
            CategoriaId = cat.Id, MarcaId = mar.Id
        };
        ctx.Productos.Add(prod);
        var cli = new Cliente
        {
            Nombre = "Valida", Apellido = "Campos", NumeroDocumento = $"7{n}",
            NivelRiesgo = NivelRiesgoCredito.AprobadoTotal
        };
        ctx.Clientes.Add(cli);
        await ctx.SaveChangesAsync();
        return (cli.Id, prod.Id);
    }

    private static async Task<(bool Success, string Cuerpo)> PostAsync(HttpClient client, Dictionary<string, string> campos)
    {
        var login = await client.GetAsync("/Identity/Account/Login");
        var match = Regex.Match(await login.Content.ReadAsStringAsync(), "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, "Sin token antiforgery.");
        campos["__RequestVerificationToken"] = WebUtility.HtmlDecode(match.Groups[1].Value);

        var resp = await client.PostAsync("/Venta/CreateAjax", new FormUrlEncodedContent(campos));
        var cuerpo = await resp.Content.ReadAsStringAsync();
        if (resp.StatusCode != HttpStatusCode.OK)
            return (false, $"HTTP {(int)resp.StatusCode}");
        try
        {
            var json = JsonDocument.Parse(cuerpo).RootElement;
            return (json.TryGetProperty("success", out var s) && s.GetBoolean(), cuerpo);
        }
        catch (JsonException)
        {
            return (false, "respuesta no JSON");
        }
    }

    private static Dictionary<string, string> Form(int clienteId, int productoId) => new()
    {
        ["IdempotencyKey"] = Guid.NewGuid().ToString(),
        ["ClienteId"] = clienteId.ToString(),
        ["FechaVenta"] = DateTime.UtcNow.ToString("yyyy-MM-dd"),
        ["TipoPago"] = "Efectivo",
        ["Detalles[0].ProductoId"] = productoId.ToString(),
        ["Detalles[0].Cantidad"] = "1",
        ["Detalles[0].PrecioUnitario"] = "1000",
        ["Detalles[0].Subtotal"] = "1000",
    };

    private async Task<int> CantidadVentasAsync(int clienteId)
    {
        await using var ctx = await NuevoContextoAsync();
        return await ctx.Ventas.AsNoTracking().CountAsync(v => v.ClienteId == clienteId);
    }

    private async Task AssertRechazadaAsync(string campo, string valor, Action<Dictionary<string, string>>? ajuste = null)
    {
        var (clienteId, productoId) = await SeedAsync();
        var form = Form(clienteId, productoId);
        form[campo] = valor;
        ajuste?.Invoke(form);

        var (success, cuerpo) = await PostAsync(_factory.CreateAuthenticatedClient(), form);

        Assert.False(success, $"{campo}='{valor}' fue ACEPTADO: {cuerpo}");
        Assert.Equal(0, await CantidadVentasAsync(clienteId));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1,5")]
    [InlineData("abc")]
    [InlineData("2147483647")]
    [InlineData("100000")]
    public Task Cantidad_FueraDeRegla_SeRechaza(string cantidad) =>
        AssertRechazadaAsync("Detalles[0].Cantidad", cantidad);

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("20000001")]
    [InlineData("1000,555")]
    [InlineData("1.000")]
    [InlineData("abc")]
    public Task PrecioUnitario_FueraDeRegla_SeRechaza(string precio) =>
        AssertRechazadaAsync("Detalles[0].PrecioUnitario", precio);

    [Theory]
    [InlineData("-1")]
    [InlineData("101")]
    [InlineData("1000")]
    public Task DescuentoDeLinea_FueraDeRango_SeRechaza(string descuento) =>
        AssertRechazadaAsync("Detalles[0].Descuento", descuento);

    [Theory]
    [InlineData("1900-01-01")]
    [InlineData("0001-01-01")]
    [InlineData("2999-12-31")]
    public async Task FechaVenta_FueraDeRango_SeNormalizaAHoy_NuncaSePersisteForjada(string fecha)
    {
        // FIN-S-002: una fecha fuera de [hoy-1, hoy+1] no se persiste; el servidor la reemplaza por la actual.
        var (clienteId, productoId) = await SeedAsync();
        var form = Form(clienteId, productoId);
        form["FechaVenta"] = fecha;

        var (success, cuerpo) = await PostAsync(_factory.CreateAuthenticatedClient(), form);

        Assert.True(success, cuerpo);
        await using var ctx = await NuevoContextoAsync();
        var venta = await ctx.Ventas.AsNoTracking().SingleAsync(v => v.ClienteId == clienteId);
        Assert.InRange(venta.FechaVenta.Date, DateTime.UtcNow.Date.AddDays(-1), DateTime.UtcNow.Date.AddDays(1));
    }

    [Fact]
    public Task FechaVenta_NoEsFecha_SeRechaza() =>
        AssertRechazadaAsync("FechaVenta", "no-es-fecha");

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("99999999")]
    public Task Cliente_Invalido_SeRechaza(string clienteId) =>
        AssertRechazadaAsync("ClienteId", clienteId);

    [Theory]
    [InlineData("999")]
    [InlineData("-1")]
    [InlineData("NoExiste")]
    public Task TipoPago_ValorInexistente_SeRechaza(string tipoPago) =>
        AssertRechazadaAsync("TipoPago", tipoPago);

    [Fact]
    public Task Observaciones_MasDe500Caracteres_SeRechaza() =>
        AssertRechazadaAsync("Observaciones", new string('x', 501));

    [Theory]
    [InlineData("-1")]
    [InlineData("20000001")]
    // El Anticipo del VM no tiene input en las vistas de Venta y solo entra en la huella de idempotencia: el
    // anticipo real se configura en el crédito. Por eso no hay regla "anticipo <= total" a este nivel.
    public Task Anticipo_FueraDeRegla_SeRechaza(string anticipo) =>
        AssertRechazadaAsync("Anticipo", anticipo);

    private static void ConCheque(Dictionary<string, string> form)
    {
        var hoy = DateTime.UtcNow.Date;
        form["TipoPago"] = "Cheque";
        form["DatosCheque.NumeroCheque"] = "00012345";
        form["DatosCheque.Banco"] = "Banco Nación";
        form["DatosCheque.Titular"] = "Cliente de Prueba";
        form["DatosCheque.CUIT"] = "20-12345678-6";
        form["DatosCheque.FechaEmision"] = hoy.ToString("yyyy-MM-dd");
        form["DatosCheque.FechaVencimiento"] = hoy.AddDays(30).ToString("yyyy-MM-dd");
        form["DatosCheque.Monto"] = "1000";
    }

    private async Task AssertChequeRechazadoAsync(string campo, string valor)
    {
        var (clienteId, productoId) = await SeedAsync();
        var form = Form(clienteId, productoId);
        ConCheque(form);
        form[campo] = valor;

        var (success, cuerpo) = await PostAsync(_factory.CreateAuthenticatedClient(), form);

        Assert.False(success, $"{campo}='{valor}' fue ACEPTADO: {cuerpo}");
        Assert.Equal(0, await CantidadVentasAsync(clienteId));
    }

    [Fact]
    public async Task Cheque_Valido_SeAcepta()
    {
        var (clienteId, productoId) = await SeedAsync();
        var form = Form(clienteId, productoId);
        ConCheque(form);

        var (success, cuerpo) = await PostAsync(_factory.CreateAuthenticatedClient(), form);

        Assert.True(success, cuerpo);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("20000001")]
    public Task Cheque_Monto_FueraDeRegla_SeRechaza(string monto) =>
        AssertChequeRechazadoAsync("DatosCheque.Monto", monto);

    [Theory]
    [InlineData("abc")]
    [InlineData("<script>")]
    [InlineData("123 456")]
    public Task Cheque_Numero_NoNumerico_SeRechaza(string numero) =>
        AssertChequeRechazadoAsync("DatosCheque.NumeroCheque", numero);

    [Fact]
    public Task Cheque_Vencimiento_AnteriorALaEmision_SeRechaza() =>
        AssertChequeRechazadoAsync("DatosCheque.FechaVencimiento", DateTime.UtcNow.Date.AddDays(-10).ToString("yyyy-MM-dd"));

    [Fact]
    public Task Cheque_Vencimiento_MasDe360DiasDespuesDeLaEmision_SeRechaza() =>
        AssertChequeRechazadoAsync("DatosCheque.FechaVencimiento", DateTime.UtcNow.Date.AddDays(361).ToString("yyyy-MM-dd"));

    [Fact]
    public async Task Cheque_Vencimiento_A360Dias_SeAcepta()
    {
        var (clienteId, productoId) = await SeedAsync();
        var form = Form(clienteId, productoId);
        ConCheque(form);
        form["DatosCheque.FechaVencimiento"] = DateTime.UtcNow.Date.AddDays(360).ToString("yyyy-MM-dd");

        var (success, cuerpo) = await PostAsync(_factory.CreateAuthenticatedClient(), form);

        Assert.True(success, cuerpo);
    }

    [Theory]
    [InlineData("9 de Julio")]
    [InlineData("Ruta 8")]
    public async Task Envio_Localidad_ConNumeros_SeAcepta(string localidad)
    {
        var (clienteId, productoId) = await SeedAsync();
        var form = Form(clienteId, productoId);
        form["TieneEnvio"] = "true";
        form["Envio.Destinatario"] = "Juan Perez";
        form["Envio.Domicilio"] = "Calle 1 123";
        form["Envio.TipoEnvio"] = "EnvioCiudad";
        form["Envio.Localidad"] = localidad;

        var (success, cuerpo) = await PostAsync(_factory.CreateAuthenticatedClient(), form);

        Assert.True(success, cuerpo);
    }

    [Theory]
    [InlineData("Armado1")]
    [InlineData("99")]
    public async Task Envio_TipoEnvio_QueNoEsUnEnvio_SeRechaza(string tipo)
    {
        var (clienteId, productoId) = await SeedAsync();
        var form = Form(clienteId, productoId);
        form["TieneEnvio"] = "true";
        form["Envio.Destinatario"] = "Juan Perez";
        form["Envio.Domicilio"] = "Calle 1 123";
        form["Envio.TipoEnvio"] = tipo;

        var (success, cuerpo) = await PostAsync(_factory.CreateAuthenticatedClient(), form);

        Assert.False(success, $"Envio.TipoEnvio='{tipo}' fue ACEPTADO: {cuerpo}");
        Assert.Equal(0, await CantidadVentasAsync(clienteId));
    }

    [Fact]
    public Task Cheque_Cuit_ConVerificadorInvalido_SeRechaza() =>
        AssertChequeRechazadoAsync("DatosCheque.CUIT", "20-12345678-0");

    [Fact]
    public async Task VentaValidaDeBorde_SeAcepta()
    {
        var (clienteId, productoId) = await SeedAsync();
        var form = Form(clienteId, productoId);
        form["Detalles[0].Cantidad"] = "10";            // todo el stock
        form["Detalles[0].PrecioUnitario"] = "1000,50"; // formato es-AR no ambiguo
        form["Observaciones"] = new string('x', 500);

        var (success, cuerpo) = await PostAsync(_factory.CreateAuthenticatedClient(), form);

        Assert.True(success, cuerpo);
        Assert.Equal(1, await CantidadVentasAsync(clienteId));
    }
}
