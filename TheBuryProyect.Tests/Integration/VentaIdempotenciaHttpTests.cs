using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// FIN-S-018: un doble envío (doble clic, reintento de red, dos pestañas con el mismo formulario) a Venta/CreateAjax
/// creaba dos ventas idénticas. Ahora el formulario emite una clave y el servidor devuelve la venta ya creada. Por el
/// pipeline HTTP real (antiforgery + autorización + binding + servicio). La concurrencia real (índice único) se prueba
/// contra SQL Server en FinS002VentaSqlServerTests [FS2-IDEM]: SQLite comparte una sola conexión y no admite altas paralelas.
/// </summary>
[Collection("HttpIntegration")]
public class VentaIdempotenciaHttpTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private static int _counter = 18000;
    private readonly CustomWebApplicationFactory _factory;

    public VentaIdempotenciaHttpTests(CustomWebApplicationFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.SeedTestUserAsync();
        await using var ctx = await NuevoContextoAsync();
        if (await ctx.AperturasCaja.AnyAsync(a => a.UsuarioApertura == "testuser" && !a.Cerrada && !a.IsDeleted))
            return;
        var n = Interlocked.Increment(ref _counter);
        var caja = new Caja { Codigo = $"I{n}", Nombre = $"Caja IDEM {n}", RowVersion = new byte[8] };
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
        var cat = new Categoria { Codigo = $"CI{n}", Nombre = $"CatI{n}" };
        var mar = new Marca { Codigo = $"MI{n}", Nombre = $"MarI{n}" };
        ctx.Categorias.Add(cat);
        ctx.Marcas.Add(mar);
        await ctx.SaveChangesAsync();
        var prod = new Producto
        {
            Codigo = $"I{n}", Nombre = $"ProdI{n}", PrecioVenta = 1_000m, StockActual = 50,
            CategoriaId = cat.Id, MarcaId = mar.Id
        };
        ctx.Productos.Add(prod);
        await ctx.SaveChangesAsync();
        return (await SeedClienteAsync(), prod.Id);
    }

    private async Task<int> SeedClienteAsync()
    {
        await using var ctx = await NuevoContextoAsync();
        var cli = new Cliente
        {
            Nombre = "Idem", Apellido = "FS18", NumeroDocumento = $"7{Interlocked.Increment(ref _counter)}",
            NivelRiesgo = NivelRiesgoCredito.AprobadoTotal
        };
        ctx.Clientes.Add(cli);
        await ctx.SaveChangesAsync();
        return cli.Id;
    }

    private static async Task<string> TokenAsync(HttpClient client)
    {
        var login = await client.GetAsync("/Identity/Account/Login");
        var match = Regex.Match(await login.Content.ReadAsStringAsync(), "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, "Sin token antiforgery.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static Dictionary<string, string> Form(int clienteId, int productoId, Guid? clave) => new()
    {
        ["IdempotencyKey"] = clave?.ToString() ?? string.Empty,
        ["ClienteId"] = clienteId.ToString(),
        ["FechaVenta"] = DateTime.UtcNow.ToString("yyyy-MM-dd"),
        ["TipoPago"] = "Efectivo",
        ["Detalles[0].ProductoId"] = productoId.ToString(),
        ["Detalles[0].Cantidad"] = "1",
        ["Detalles[0].PrecioUnitario"] = "1000",
        ["Detalles[0].Subtotal"] = "1000",
    };

    private static async Task<JsonElement> EnviarAsync(HttpClient client, string token, Dictionary<string, string> campos)
    {
        var copia = new Dictionary<string, string>(campos) { ["__RequestVerificationToken"] = token };
        var resp = await client.PostAsync("/Venta/CreateAjax", new FormUrlEncodedContent(copia));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private async Task<List<Venta>> VentasDeAsync(int clienteId)
    {
        await using var ctx = await NuevoContextoAsync();
        return await ctx.Ventas.AsNoTracking().Where(v => v.ClienteId == clienteId).ToListAsync();
    }

    [Fact]
    public async Task MismoFormularioEnviadoDosVeces_CreaUnaSolaVenta_YDevuelveLaMisma()
    {
        var (clienteId, productoId) = await SeedAsync();
        var client = _factory.CreateAuthenticatedClient();
        var token = await TokenAsync(client);
        var form = Form(clienteId, productoId, Guid.NewGuid());

        var primera = await EnviarAsync(client, token, form);
        var segunda = await EnviarAsync(client, token, form);

        Assert.True(primera.GetProperty("success").GetBoolean(), primera.ToString());
        Assert.True(segunda.GetProperty("success").GetBoolean(), segunda.ToString());
        Assert.Equal(primera.GetProperty("redirectUrl").GetString(), segunda.GetProperty("redirectUrl").GetString());
        var ventas = await VentasDeAsync(clienteId);
        Assert.Single(ventas);
        Assert.Equal(Guid.Parse(form["IdempotencyKey"]), ventas[0].IdempotencyKey);
    }

    [Fact]
    public async Task ClavesDistintas_CreanVentasDistintas()
    {
        var (clienteId, productoId) = await SeedAsync();
        var client = _factory.CreateAuthenticatedClient();
        var token = await TokenAsync(client);

        var a = await EnviarAsync(client, token, Form(clienteId, productoId, Guid.NewGuid()));
        var b = await EnviarAsync(client, token, Form(clienteId, productoId, Guid.NewGuid()));

        Assert.True(a.GetProperty("success").GetBoolean() && b.GetProperty("success").GetBoolean());
        Assert.Equal(2, (await VentasDeAsync(clienteId)).Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("no-es-un-guid")]
    public async Task SinClaveValida_NoCreaNada(string clave)
    {
        var (clienteId, productoId) = await SeedAsync();
        var client = _factory.CreateAuthenticatedClient();
        var token = await TokenAsync(client);
        var form = Form(clienteId, productoId, null);
        form["IdempotencyKey"] = clave;

        var json = await EnviarAsync(client, token, form);

        Assert.False(json.GetProperty("success").GetBoolean(), json.ToString());
        Assert.Empty(await VentasDeAsync(clienteId));
    }

    [Fact]
    public async Task MismaClaveConOtroCliente_SeRechaza_YNoCreaUnaSegundaVenta()
    {
        var (clienteA, productoId) = await SeedAsync();
        var clienteB = await SeedClienteAsync();
        var client = _factory.CreateAuthenticatedClient();
        var token = await TokenAsync(client);
        var clave = Guid.NewGuid();

        var primera = await EnviarAsync(client, token, Form(clienteA, productoId, clave));
        var reuso = await EnviarAsync(client, token, Form(clienteB, productoId, clave));

        Assert.True(primera.GetProperty("success").GetBoolean(), primera.ToString());
        Assert.False(reuso.GetProperty("success").GetBoolean(), reuso.ToString());
        Assert.Single(await VentasDeAsync(clienteA));
        Assert.Empty(await VentasDeAsync(clienteB));
    }

    [Theory]
    [InlineData("Detalles[0].Cantidad", "3")]
    [InlineData("Detalles[0].PrecioUnitario", "999")]
    [InlineData("Descuento", "50")]
    [InlineData("Envio.TipoEnvio", "Rural")]
    [InlineData("Envio.Domicilio", "Otra calle 123")]
    [InlineData("DatosTarjeta.CantidadCuotas", "6")]
    [InlineData("AplicarExcepcionDocumental", "true")]
    public async Task MismaClaveConOtroCarrito_SeRechaza_YNoDevuelveLaVentaAnterior(string campo, string valor)
    {
        // Atras del navegador (bfcache) o respuesta perdida: el formulario conserva la clave pero el operador cambio el
        // carrito. Responder exito con la venta anterior perderia en silencio lo que pidio.
        var (clienteId, productoId) = await SeedAsync();
        var client = _factory.CreateAuthenticatedClient();
        var token = await TokenAsync(client);
        var clave = Guid.NewGuid();
        var original = Form(clienteId, productoId, clave);
        var modificado = Form(clienteId, productoId, clave);
        modificado[campo] = valor;

        var primera = await EnviarAsync(client, token, original);
        var segunda = await EnviarAsync(client, token, modificado);

        Assert.True(primera.GetProperty("success").GetBoolean(), primera.ToString());
        Assert.False(segunda.GetProperty("success").GetBoolean(), segunda.ToString());
        Assert.Contains("carrito o datos distintos", segunda.ToString());
        Assert.Single(await VentasDeAsync(clienteId));
    }

    [Fact]
    public void Huella_NoDependeDelOrdenDeLasLineas_PeroSiDelContenido()
    {
        VentaViewModel Vm(params (int Producto, int Cantidad)[] lineas) => new()
        {
            ClienteId = 7, TipoPago = TipoPago.Efectivo,
            Detalles = lineas.Select(l => new VentaDetalleViewModel { ProductoId = l.Producto, Cantidad = l.Cantidad, PrecioUnitario = 10m }).ToList()
        };

        var ab = VentaService.HuellaDeSolicitud(Vm((1, 1), (2, 2)));
        var ba = VentaService.HuellaDeSolicitud(Vm((2, 2), (1, 1)));
        var otra = VentaService.HuellaDeSolicitud(Vm((1, 1), (2, 3)));
        var otroCliente = VentaService.HuellaDeSolicitud(new VentaViewModel { ClienteId = 8, TipoPago = TipoPago.Efectivo, Detalles = Vm((1, 1), (2, 2)).Detalles });

        Assert.Equal(ab, ba);
        Assert.NotEqual(ab, otra);
        Assert.NotEqual(ab, otroCliente);
        Assert.Equal(64, ab.Length);
    }

    [Fact]
    public async Task MismaClave_ConOtraFechaYOtroToken_SigueSiendoElMismoReintento()
    {
        // La fecha, el token antiforgery y la version de concurrencia cambian legitimamente entre reintentos del mismo
        // formulario (p. ej. cruzando la medianoche o recargando el token); no son una operacion distinta.
        var (clienteId, productoId) = await SeedAsync();
        var client = _factory.CreateAuthenticatedClient();
        var clave = Guid.NewGuid();
        var original = Form(clienteId, productoId, clave);
        var reintento = Form(clienteId, productoId, clave);
        reintento["FechaVenta"] = DateTime.UtcNow.AddHours(-3).ToString("yyyy-MM-dd");
        reintento["RowVersion"] = "AAAAAAAAB9E=";

        var primera = await EnviarAsync(client, await TokenAsync(client), original);
        var segunda = await EnviarAsync(client, await TokenAsync(client), reintento);

        Assert.True(primera.GetProperty("success").GetBoolean(), primera.ToString());
        Assert.True(segunda.GetProperty("success").GetBoolean(), segunda.ToString());
        Assert.Equal(primera.GetProperty("redirectUrl").GetString(), segunda.GetProperty("redirectUrl").GetString());
        Assert.Single(await VentasDeAsync(clienteId));
    }

    [Fact]
    public void HuellaDeFormulario_IncluyeTodoSalvoLoQueCambiaEntreReintentos()
    {
        static Microsoft.AspNetCore.Http.FormCollection Form(params (string K, string V)[] c) =>
            new(c.ToDictionary(x => x.K, x => new Microsoft.Extensions.Primitives.StringValues(x.V)));

        static Microsoft.AspNetCore.Http.FormCollection RepetidoForm(params string[] v) =>
            new(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues> { ["Detalles[0].Cantidad"] = v });

        var baseForm = HuellaFormulario.De(Form(("ClienteId", "1"), ("Detalles[0].Cantidad", "1")));

        // El orden de llegada importa: el binder lo usa para resolver alias y repetidos.
        Assert.NotEqual(baseForm, HuellaFormulario.De(Form(("Detalles[0].Cantidad", "1"), ("ClienteId", "1"))));
        Assert.NotEqual(
            HuellaFormulario.De(Form(("Detalles[0][Cantidad]", "1"), ("Detalles[0][Cantidad][]", "3"))),
            HuellaFormulario.De(Form(("Detalles[0][Cantidad][]", "3"), ("Detalles[0][Cantidad]", "1"))));
        Assert.NotEqual(HuellaFormulario.De(RepetidoForm("1", "3")), HuellaFormulario.De(RepetidoForm("3", "1")));       // orden de valores repetidos
        Assert.Equal(baseForm, HuellaFormulario.De(Form(("ClienteId", "1"), ("Detalles[0].Cantidad", "1"),
            ("__RequestVerificationToken", "x"), ("IdempotencyKey", "y"), ("RowVersion", "z"), ("FechaVenta", "2026-01-01"))));
        Assert.NotEqual(baseForm, HuellaFormulario.De(Form(("ClienteId", "1"), ("Detalles[0].Cantidad", "2"))));
        Assert.NotEqual(baseForm, HuellaFormulario.De(Form(("ClienteId", "1"), ("Detalles[0].Cantidad", "1"), ("CampoQueHoyNoExiste", "1"))));

        // Un valor con saltos de linea o "=" no puede imitar a otro campo (la serializacion es inequivoca).
        Assert.NotEqual(
            HuellaFormulario.De(Form(("Observaciones", "a\nObservacionesZ=b"))),
            HuellaFormulario.De(Form(("Observaciones", "a"), ("ObservacionesZ", "b"))));
        Assert.NotEqual(
            HuellaFormulario.De(RepetidoForm("a", "b")),
            HuellaFormulario.De(RepetidoForm("a\u001fb")));
    }

    private static async Task<JsonElement> EnviarParesAsync(HttpClient client, string token, string url, IEnumerable<KeyValuePair<string, string>> pares)
    {
        var contenido = pares.Append(new KeyValuePair<string, string>("__RequestVerificationToken", token));
        var resp = await client.PostAsync(url, new FormUrlEncodedContent(contenido));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    [Fact]
    public async Task CamposEnLaQuery_NoSeEnlazan_YNoPermitenEvadirLaHuella()
    {
        // El modelo se enlaza solo desde el formulario ([FromForm]): un ClienteId en la URL no cuenta, de modo que dos POST con
        // el mismo formulario y distinto ClienteId en la query no pueden pasar por la misma operacion.
        var (clienteA, productoId) = await SeedAsync();
        var clienteB = await SeedClienteAsync();
        var client = _factory.CreateAuthenticatedClient();
        var token = await TokenAsync(client);
        var clave = Guid.NewGuid();
        var sinCliente = Form(0, productoId, clave);
        sinCliente.Remove("ClienteId");

        var a = await EnviarParesAsync(client, token, $"/Venta/CreateAjax?ClienteId={clienteA}", sinCliente);
        var b = await EnviarParesAsync(client, token, $"/Venta/CreateAjax?ClienteId={clienteB}", sinCliente);

        Assert.False(a.GetProperty("success").GetBoolean(), a.ToString());
        Assert.False(b.GetProperty("success").GetBoolean(), b.ToString());
        Assert.Empty(await VentasDeAsync(clienteA));
        Assert.Empty(await VentasDeAsync(clienteB));
    }

    [Fact]
    public async Task ValoresRepetidosEnOtroOrden_SonOtraOperacion_YSeRechazan()
    {
        // El binder toma el PRIMER valor de un campo escalar repetido: Cantidad=1&Cantidad=3 y Cantidad=3&Cantidad=1 son
        // ventas distintas aunque tengan los mismos valores.
        var (clienteId, productoId) = await SeedAsync();
        var client = _factory.CreateAuthenticatedClient();
        var token = await TokenAsync(client);
        var clave = Guid.NewGuid();
        var baseForm = Form(clienteId, productoId, clave);
        baseForm.Remove("Detalles[0].Cantidad");
        List<KeyValuePair<string, string>> Con(params string[] cantidades) =>
            baseForm.Concat(cantidades.Select(c => new KeyValuePair<string, string>("Detalles[0].Cantidad", c))).ToList();

        var primera = await EnviarParesAsync(client, token, "/Venta/CreateAjax", Con("1", "3"));
        var invertida = await EnviarParesAsync(client, token, "/Venta/CreateAjax", Con("3", "1"));

        Assert.True(primera.GetProperty("success").GetBoolean(), primera.ToString());
        Assert.False(invertida.GetProperty("success").GetBoolean(), invertida.ToString());
        Assert.Single(await VentasDeAsync(clienteId));
    }

    [Fact]
    public async Task AliasDeClavesEnOtroOrden_SonOtraOperacion_YSeRechazan()
    {
        // "A[B]" y "A[B][]" se normalizan al mismo nombre y gana el ultimo en llegar: invertir el orden cambia la cantidad.
        var (clienteId, productoId) = await SeedAsync();
        var client = _factory.CreateAuthenticatedClient();
        var token = await TokenAsync(client);
        var baseForm = Form(clienteId, productoId, Guid.NewGuid());
        baseForm.Remove("Detalles[0].Cantidad");
        List<KeyValuePair<string, string>> Con(params (string K, string V)[] alias) =>
            baseForm.Concat(alias.Select(a => new KeyValuePair<string, string>(a.K, a.V))).ToList();

        var primera = await EnviarParesAsync(client, token, "/Venta/CreateAjax", Con(("Detalles[0][Cantidad]", "1"), ("Detalles[0][Cantidad][]", "3")));
        var invertida = await EnviarParesAsync(client, token, "/Venta/CreateAjax", Con(("Detalles[0][Cantidad][]", "3"), ("Detalles[0][Cantidad]", "1")));

        Assert.False(invertida.GetProperty("success").GetBoolean(), invertida.ToString());
        Assert.True(primera.GetProperty("success").GetBoolean() || !(await VentasDeAsync(clienteId)).Any(), primera.ToString());
        Assert.True((await VentasDeAsync(clienteId)).Count <= 1);
    }

    [Fact]
    public async Task PostMvcDeAltaSinClave_NoCreaNadaYVuelveAlFormulario()
    {
        var (clienteId, productoId) = await SeedAsync();
        var client = _factory.CreateAuthenticatedClient();
        var token = await TokenAsync(client);
        var form = Form(clienteId, productoId, null);
        form["__RequestVerificationToken"] = token;
        form["Estado"] = ((int)EstadoVenta.Presupuesto).ToString();

        var resp = await client.PostAsync("/Venta/Create", new FormUrlEncodedContent(form));

        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.EndsWith("/Venta/Create", resp.Headers.Location!.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await VentasDeAsync(clienteId));
    }

    [Fact]
    public async Task FormularioDeAlta_EmiteUnaClaveNuevaPorCadaApertura()
    {
        var client = _factory.CreateAuthenticatedClient();
        string Clave(string html)
        {
            var m = Regex.Match(html, "name=\"IdempotencyKey\"[^>]*value=\"([0-9a-fA-F-]{36})\"");
            if (!m.Success)
                m = Regex.Match(html, "value=\"([0-9a-fA-F-]{36})\"[^>]*name=\"IdempotencyKey\"");
            Assert.True(m.Success, "El formulario de alta no trae el campo IdempotencyKey.");
            return m.Groups[1].Value;
        }

        var uno = Clave(await (await client.GetAsync("/Venta/Create")).Content.ReadAsStringAsync());
        var dos = Clave(await (await client.GetAsync("/Venta/Create")).Content.ReadAsStringAsync());

        Assert.NotEqual(Guid.Empty, Guid.Parse(uno));
        Assert.NotEqual(uno, dos);
    }

    [Fact]
    public void MapeoDeEdicion_NoPisaLaClaveDeLaVentaExistente()
    {
        var config = new MapperConfiguration(c => c.AddProfile<MappingProfile>(), NullLoggerFactory.Instance);
        var mapper = config.CreateMapper();
        var clave = Guid.NewGuid();
        var existente = new Venta { Numero = "V-1", IdempotencyKey = clave };

        mapper.Map(new VentaViewModel { IdempotencyKey = Guid.NewGuid(), Numero = "V-1" }, existente);
        mapper.Map(new VentaViewModel { IdempotencyKey = null, Numero = "V-1" }, existente);

        Assert.Equal(clave, existente.IdempotencyKey);
    }

    [Theory]
    [InlineData("Cannot insert duplicate key row in object 'dbo.Ventas' with unique index 'IX_Ventas_IdempotencyKey'.", true)]
    [InlineData("SQLite Error 19: 'UNIQUE constraint failed: Ventas.IdempotencyKey'.", true)]
    [InlineData("Cannot insert duplicate key row in object 'dbo.Ventas' with unique index 'IX_Ventas_Numero'.", false)]
    [InlineData("SQLite Error 19: 'UNIQUE constraint failed: Ventas.Numero'.", false)]
    public void ViolacionDeIndice_SoloLaDeLaClaveSeTrataComoReintento(string mensaje, bool esperado)
    {
        var ex = new DbUpdateException("error", new InvalidOperationException(mensaje));

        Assert.Equal(esperado, VentaService.EsViolacionIndiceIdempotenciaVenta(ex));
    }
}
