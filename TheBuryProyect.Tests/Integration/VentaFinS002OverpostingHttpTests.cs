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
/// PROMPT 85 — FIN-S-002 (A1/A2/F1/F2/F3) por el pipeline HTTP real: formularios forjados contra
/// Venta/CreateAjax y Venta/EditAjax (antiforgery + autorización + model binding + servicio).
/// </summary>
[Collection("HttpIntegration")]
public class VentaFinS002OverpostingHttpTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private static int _counter = 85000;
    private readonly CustomWebApplicationFactory _factory;

    public VentaFinS002OverpostingHttpTests(CustomWebApplicationFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.SeedTestUserAsync();
        await using var ctx = await NuevoContextoAsync();
        if (await ctx.AperturasCaja.AnyAsync(a => a.UsuarioApertura == "testuser" && !a.Cerrada && !a.IsDeleted))
            return;
        var n = Interlocked.Increment(ref _counter);
        var caja = new Caja { Codigo = $"H{n}", Nombre = $"Caja HTTP {n}", RowVersion = new byte[8] };
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
        var cat = new Categoria { Codigo = $"CH{n}", Nombre = $"CatH{n}" };
        var mar = new Marca { Codigo = $"MH{n}", Nombre = $"MarH{n}" };
        ctx.Categorias.Add(cat);
        ctx.Marcas.Add(mar);
        await ctx.SaveChangesAsync();
        var prod = new Producto
        {
            Codigo = $"H{n}", Nombre = $"ProdH{n}", PrecioVenta = 1_000m, StockActual = 10,
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
            Nombre = "Http", Apellido = "FS2", NumeroDocumento = $"6{Interlocked.Increment(ref _counter)}",
            NivelRiesgo = NivelRiesgoCredito.AprobadoTotal
        };
        ctx.Clientes.Add(cli);
        await ctx.SaveChangesAsync();
        return cli.Id;
    }

    private static async Task<JsonElement> PostJsonAsync(HttpClient client, string ruta, Dictionary<string, string> campos)
    {
        var login = await client.GetAsync("/Identity/Account/Login");
        var match = Regex.Match(await login.Content.ReadAsStringAsync(), "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, "Sin token antiforgery.");
        campos["__RequestVerificationToken"] = WebUtility.HtmlDecode(match.Groups[1].Value);

        var resp = await client.PostAsync(ruta, new FormUrlEncodedContent(campos));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private static Dictionary<string, string> Form(int clienteId, int productoId, string tipoPago = "Efectivo") => new()
    {
        ["IdempotencyKey"] = Guid.NewGuid().ToString(),   // FIN-S-018: el alta exige la clave que emite el formulario
        ["ClienteId"] = clienteId.ToString(),
        ["FechaVenta"] = DateTime.UtcNow.ToString("yyyy-MM-dd"),
        ["TipoPago"] = tipoPago,
        ["Detalles[0].ProductoId"] = productoId.ToString(),
        ["Detalles[0].Cantidad"] = "1",
        ["Detalles[0].PrecioUnitario"] = "1000",
        ["Detalles[0].Subtotal"] = "1000",
    };

    private async Task<Venta> VentaDeAsync(int clienteId)
    {
        await using var ctx = await NuevoContextoAsync();
        return await ctx.Ventas.AsNoTracking().SingleAsync(v => v.ClienteId == clienteId);
    }

    [Fact]
    public async Task CreateAjax_CamposDeServidorForjados_NoSePersisten()
    {
        var (clienteId, productoId) = await SeedAsync();
        var form = Form(clienteId, productoId);
        form["Estado"] = "Facturada";
        form["FechaConfiguracionCredito"] = "2020-01-01";
        form["CreatedAt"] = "2001-01-01";
        form["CreditoId"] = "99999";

        var json = await PostJsonAsync(_factory.CreateAuthenticatedClient(), "/Venta/CreateAjax", form);

        Assert.True(json.GetProperty("success").GetBoolean(), json.ToString());
        var v = await VentaDeAsync(clienteId);
        Assert.Equal(EstadoVenta.Cotizacion, v.Estado);
        Assert.Null(v.FechaConfiguracionCredito);
        Assert.Null(v.CreditoId);
        Assert.True(v.CreatedAt >= DateTime.UtcNow.AddMinutes(-5), $"CreatedAt forjado persistido: {v.CreatedAt}");
    }

    [Fact]
    public async Task CreateAjax_CreditoPersonalConCreditoDeOtroCliente_Rechaza()
    {
        var (clienteId, productoId) = await SeedAsync();
        var otro = await SeedClienteAsync();
        int creditoAjenoId;
        await using (var ctx = await NuevoContextoAsync())
        {
            var credito = new Credito
            {
                ClienteId = otro, Numero = $"H-{Interlocked.Increment(ref _counter)}",
                Estado = EstadoCredito.Activo, SaldoPendiente = 1_000_000m, RowVersion = new byte[8]
            };
            ctx.Creditos.Add(credito);
            await ctx.SaveChangesAsync();
            creditoAjenoId = credito.Id;
        }
        var form = Form(clienteId, productoId, "CreditoPersonal");
        form["CreditoId"] = creditoAjenoId.ToString();

        var json = await PostJsonAsync(_factory.CreateAuthenticatedClient(), "/Venta/CreateAjax", form);

        Assert.False(json.GetProperty("success").GetBoolean(), json.ToString());
        await using var check = await NuevoContextoAsync();
        Assert.False(await check.Ventas.AnyAsync(v => v.ClienteId == clienteId));
    }

    [Fact]
    public async Task EditAjax_CambiarClienteConCreditoAsociado_Rechaza_Y_FechaFueraDeRangoSeConserva()
    {
        var (clienteId, productoId) = await SeedAsync();
        var client = _factory.CreateAuthenticatedClient();
        var creada = await PostJsonAsync(client, "/Venta/CreateAjax", Form(clienteId, productoId));
        Assert.True(creada.GetProperty("success").GetBoolean(), creada.ToString());

        var original = await VentaDeAsync(clienteId);
        await using (var ctx = await NuevoContextoAsync())
        {
            var credito = new Credito
            {
                ClienteId = clienteId, Numero = $"H-{Interlocked.Increment(ref _counter)}",
                Estado = EstadoCredito.Activo, SaldoPendiente = 1_000_000m, RowVersion = new byte[8]
            };
            ctx.Creditos.Add(credito);
            await ctx.SaveChangesAsync();
            // SQLite no genera rowversion en el alta: se fija uno para ejercitar el flujo de edición.
            await ctx.Ventas.Where(v => v.Id == original.Id).ExecuteUpdateAsync(s => s
                .SetProperty(v => v.CreditoId, credito.Id)
                .SetProperty(v => v.RowVersion, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }));
        }

        // A2: cambiar el cliente con crédito asociado.
        var otro = await SeedClienteAsync();
        var form = Form(otro, productoId);
        form["RowVersion"] = Convert.ToBase64String((await VentaDeAsync(clienteId)).RowVersion);
        var rechazo = await PostJsonAsync(client, $"/Venta/EditAjax/{original.Id}", form);
        Assert.False(rechazo.GetProperty("success").GetBoolean(), rechazo.ToString());
        Assert.Contains("cambiar el cliente", rechazo.GetProperty("message").GetString());
        Assert.Equal(clienteId, (await VentaDeAsync(clienteId)).ClienteId);

        // F1: fecha retroactiva fuera de rango, mismo cliente → se guarda pero la fecha no cambia.
        form = Form(clienteId, productoId);
        form["FechaVenta"] = DateTime.UtcNow.AddDays(-30).ToString("yyyy-MM-dd");
        form["RowVersion"] = Convert.ToBase64String((await VentaDeAsync(clienteId)).RowVersion);
        var ok = await PostJsonAsync(client, $"/Venta/EditAjax/{original.Id}", form);
        Assert.True(ok.GetProperty("success").GetBoolean(), ok.ToString());
        Assert.Equal(original.FechaVenta, (await VentaDeAsync(clienteId)).FechaVenta);
    }
}


