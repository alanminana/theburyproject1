using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Regresión BLOCKER staging 2026-09-23: <c>accionConfirmacion</c> no viajaba en el POST final
/// de Venta/Edit sin importar qué botón lo disparara (venta-create.js deshabilitaba el propio
/// submitter dentro de su handler de "submit", excluyéndolo del form-data real — ver
/// e2e/venta-edit-confirmar-post-blocker.spec.js para la regresión del lado navegador/JS).
///
/// Este archivo cubre el lado servidor de la misma cadena que dispara Edit(POST) vía
/// ProcesarAccionPostGuardadoAsync → EjecutarConfirmarVentaAsync (la misma lógica que expone
/// POST /Venta/Confirmar/{id}): confirmar una venta debe cambiar su Estado, descontar stock
/// exactamente una vez, y repetir la confirmación no debe duplicar ese descuento.
/// </summary>
[Collection("HttpIntegration")]
public class VentaConfirmarStockHttpTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string TestUser = "testuser";
    private static int _counter = 51000;
    private readonly CustomWebApplicationFactory _factory;

    public VentaConfirmarStockHttpTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Confirmar_DescuentaStockUnaVezYQuedaConfirmada()
    {
        await _factory.SeedTestUserAsync();
        await SeedCajaAbiertaAsync();
        var (ventaId, productoId, cantidad, stockInicial) = await SeedVentaConfirmableAsync();
        var client = _factory.CreateAuthenticatedClient();
        var token = await GetAntiForgeryTokenAsync(client, ventaId);

        var response = await PostConfirmarAsync(client, ventaId, token);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains($"/Venta/Details/{ventaId}", response.Headers.Location?.OriginalString ?? string.Empty);

        var (estado, stockActual) = await LeerEstadoYStockAsync(ventaId, productoId);
        Assert.Equal(EstadoVenta.Confirmada, estado);
        Assert.Equal(stockInicial - cantidad, stockActual);
    }

    [Fact]
    public async Task Confirmar_Repetido_NoDuplicaElDescuentoDeStock()
    {
        await _factory.SeedTestUserAsync();
        await SeedCajaAbiertaAsync();
        var (ventaId, productoId, cantidad, stockInicial) = await SeedVentaConfirmableAsync();
        var client = _factory.CreateAuthenticatedClient();

        var token1 = await GetAntiForgeryTokenAsync(client, ventaId);
        await PostConfirmarAsync(client, ventaId, token1);

        var (estadoTrasPrimera, stockTrasPrimera) = await LeerEstadoYStockAsync(ventaId, productoId);
        Assert.Equal(EstadoVenta.Confirmada, estadoTrasPrimera);
        Assert.Equal(stockInicial - cantidad, stockTrasPrimera);

        // Segundo intento de confirmar la misma venta: el guard de estado
        // (VentaValidator.ValidarEstadoParaConfirmacion) rechaza confirmar algo que ya no está
        // en Cotización/Presupuesto/PendienteRequisitos. El controller atrapa la excepción,
        // deja un TempData de error y redirige — nunca debe llegar a descontar stock de nuevo.
        var token2 = await GetAntiForgeryTokenAsync(client, ventaId);
        var response2 = await PostConfirmarAsync(client, ventaId, token2);

        Assert.Equal(HttpStatusCode.Redirect, response2.StatusCode);
        Assert.Contains($"/Venta/Details/{ventaId}", response2.Headers.Location?.OriginalString ?? string.Empty);

        var (estadoTrasSegunda, stockTrasSegunda) = await LeerEstadoYStockAsync(ventaId, productoId);
        Assert.Equal(EstadoVenta.Confirmada, estadoTrasSegunda);
        Assert.Equal(stockInicial - cantidad, stockTrasSegunda);
    }

    private static async Task<HttpResponseMessage> PostConfirmarAsync(HttpClient client, int ventaId, string token)
    {
        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["id"] = ventaId.ToString()
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/Venta/Confirmar")
        {
            Content = new FormUrlEncodedContent(form)
        };

        return await client.SendAsync(request);
    }

    /// <summary>
    /// El antiforgery token está atado a la cookie de sesión, no a la venta: alcanza con
    /// pedir cualquier página autenticada del mismo cliente HTTP antes de cada POST
    /// (Details siempre es accesible, confirmada o no).
    /// </summary>
    private static async Task<string> GetAntiForgeryTokenAsync(HttpClient client, int ventaId)
    {
        var response = await client.GetAsync($"/Venta/Details/{ventaId}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var match = Regex.Match(
            html,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"",
            RegexOptions.CultureInvariant);

        Assert.True(match.Success, "No se encontró el token antiforgery en Venta/Details.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private async Task<(EstadoVenta Estado, decimal StockActual)> LeerEstadoYStockAsync(int ventaId, int productoId)
    {
        using var scope = _factory.Services.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();

        var estado = await context.Ventas.Where(v => v.Id == ventaId).Select(v => v.Estado).SingleAsync();
        var stock = await context.Productos.Where(p => p.Id == productoId).Select(p => p.StockActual).SingleAsync();
        return (estado, stock);
    }

    private async Task SeedCajaAbiertaAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();

        if (await context.AperturasCaja.AnyAsync(a => !a.Cerrada && a.UsuarioApertura == TestUser))
        {
            return;
        }

        var caja = new Caja
        {
            Codigo = "CAJA-CONFIRM-STOCK",
            Nombre = "Caja Confirmar Stock",
            IsDeleted = false,
            RowVersion = new byte[8]
        };
        context.Cajas.Add(caja);
        await context.SaveChangesAsync();

        context.AperturasCaja.Add(new AperturaCaja
        {
            CajaId = caja.Id,
            UsuarioApertura = TestUser,
            MontoInicial = 0m,
            Cerrada = false,
            IsDeleted = false,
            RowVersion = new byte[8]
        });
        await context.SaveChangesAsync();
    }

    private async Task<(int VentaId, int ProductoId, int Cantidad, decimal StockInicial)> SeedVentaConfirmableAsync()
    {
        var suffix = Interlocked.Increment(ref _counter);

        using var scope = _factory.Services.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();

        var marca = new Marca { Codigo = $"MCS{suffix}", Nombre = $"Marca {suffix}", Activo = true };
        var categoria = new Categoria { Codigo = $"CCS{suffix}", Nombre = $"Categoria {suffix}", Activo = true };
        var cliente = new Cliente
        {
            Nombre = $"ClienteConfirmStock{suffix}",
            Apellido = "FIX-RV-CONFIRM-STOCK",
            NumeroDocumento = $"41{suffix}",
            Telefono = "1111-3333",
            Domicilio = "Calle Confirm Stock 123"
        };

        context.Marcas.Add(marca);
        context.Categorias.Add(categoria);
        context.Clientes.Add(cliente);
        await context.SaveChangesAsync();

        const decimal stockInicial = 10m;
        const int cantidad = 3;

        var producto = new Producto
        {
            Codigo = $"PCS{suffix}",
            Nombre = $"ProductoConfirmStock{suffix}",
            CategoriaId = categoria.Id,
            MarcaId = marca.Id,
            PrecioCompra = 100m,
            PrecioVenta = 121m,
            PorcentajeIVA = 21m,
            StockActual = stockInicial,
            Activo = true
        };
        context.Productos.Add(producto);
        await context.SaveChangesAsync();

        var venta = new Venta
        {
            ClienteId = cliente.Id,
            Numero = $"VCS-{suffix}",
            FechaVenta = new DateTime(2026, 9, 23, 10, 0, 0, DateTimeKind.Utc),
            Estado = EstadoVenta.Presupuesto,
            TipoPago = TipoPago.Efectivo,
            Subtotal = 100m * cantidad,
            IVA = 21m * cantidad,
            Total = 121m * cantidad,
            EstadoAutorizacion = EstadoAutorizacionVenta.NoRequiere
        };
        venta.Detalles.Add(new VentaDetalle
        {
            ProductoId = producto.Id,
            Cantidad = cantidad,
            PrecioUnitario = 121m,
            Subtotal = 121m * cantidad,
            PorcentajeIVA = 21m,
            AlicuotaIVANombre = "IVA 21%",
            SubtotalFinalNeto = 100m * cantidad,
            SubtotalFinalIVA = 21m * cantidad,
            SubtotalFinal = 121m * cantidad
        });

        context.Ventas.Add(venta);
        await context.SaveChangesAsync();

        return (venta.Id, producto.Id, cantidad, stockInicial);
    }
}
