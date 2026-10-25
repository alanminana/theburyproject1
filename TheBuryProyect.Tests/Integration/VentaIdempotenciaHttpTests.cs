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
