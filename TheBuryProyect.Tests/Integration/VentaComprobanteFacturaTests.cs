using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Tests.Integration;

[Collection("HttpIntegration")]
public class VentaComprobanteFacturaTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private static int _counter = 9000;

    public VentaComprobanteFacturaTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ComprobanteFactura_FacturaInexistente_Devuelve404()
    {
        await _factory.SeedTestUserAsync();
        var client = _factory.CreateAuthenticatedClient();

        var response = await client.GetAsync("/Venta/ComprobanteFactura?facturaId=999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ComprobanteFactura_MuestraNumeroTipoYTotal()
    {
        await _factory.SeedTestUserAsync();
        var facturaId = await SeedFacturaAsync(new[]
        {
            new DetalleSeed("P21", "Producto 21", 1, 121m, 21m, "IVA 21%", 100m, 21m, 121m)
        });
        var client = _factory.CreateAuthenticatedClient();

        var response = await client.GetAsync($"/Venta/ComprobanteFactura?facturaId={facturaId}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Factura B", html);
        Assert.Contains("FC-", html);
        Assert.Contains("121", html);
    }

    [Fact]
    public async Task ComprobanteFactura_VentaMixta_MuestraResumenPorAlicuota()
    {
        await _factory.SeedTestUserAsync();
        var facturaId = await SeedFacturaAsync(new[]
        {
            new DetalleSeed("P21", "Producto 21", 1, 121m, 21m, "IVA 21%", 100m, 21m, 121m),
            new DetalleSeed("P105", "Producto 10.5", 1, 110.5m, 10.5m, "IVA 10.5%", 100m, 10.5m, 110.5m),
            new DetalleSeed("P0", "Producto exento", 1, 50m, 0m, "Exento 0%", 50m, 0m, 50m)
        });
        var client = _factory.CreateAuthenticatedClient();

        var response = await client.GetAsync($"/Venta/ComprobanteFactura?facturaId={facturaId}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Resumen por al", html);
        Assert.Contains("IVA 21%", html);
        Assert.Contains("IVA 10.5%", html);
        Assert.Contains("Exento 0%", html);
    }

    [Fact]
    public async Task Details_VentaFacturadaMixta_MuestraResumenPorAlicuota()
    {
        await _factory.SeedTestUserAsync();
        var ventaId = await SeedVentaDetailsAsync(
            EstadoVenta.Facturada,
            incluirFactura: true,
            facturaAnulada: false,
            new[]
            {
                new DetalleSeed("D21", "Detalle 21", 1, 121m, 21m, "IVA 21%", 100m, 21m, 121m),
                new DetalleSeed("D105", "Detalle 10.5", 1, 110.5m, 10.5m, "IVA 10.5%", 100m, 10.5m, 110.5m),
                new DetalleSeed("D0", "Detalle exento", 1, 50m, 0m, "Exento 0%", 50m, 0m, 50m)
            });
        var client = _factory.CreateAuthenticatedClient();

        var response = await client.GetAsync($"/Venta/Details/{ventaId}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Base imponible", html);
        Assert.Contains("IVA 21%", html);
        Assert.Contains("IVA 10.5%", html);
        Assert.Contains("Exento 0%", html);
    }

    [Fact]
    public async Task Details_VentaConfirmadaSinFactura_NoMuestraResumenPorAlicuota()
    {
        await _factory.SeedTestUserAsync();
        var ventaId = await SeedVentaDetailsAsync(
            EstadoVenta.Confirmada,
            incluirFactura: false,
            facturaAnulada: false,
            new[]
            {
                new DetalleSeed("C21", "Confirmada 21", 1, 121m, 21m, "IVA 21%", 100m, 21m, 121m)
            });
        var client = _factory.CreateAuthenticatedClient();

        var response = await client.GetAsync($"/Venta/Details/{ventaId}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("Base imponible", html);
    }

    [Fact]
    public async Task Details_FacturaAnuladaSinActiva_NoMuestraResumenPorAlicuota()
    {
        await _factory.SeedTestUserAsync();
        var ventaId = await SeedVentaDetailsAsync(
            EstadoVenta.Confirmada,
            incluirFactura: true,
            facturaAnulada: true,
            new[]
            {
                new DetalleSeed("A21", "Anulada 21", 1, 121m, 21m, "IVA 21%", 100m, 21m, 121m)
            });
        var client = _factory.CreateAuthenticatedClient();

        var response = await client.GetAsync($"/Venta/Details/{ventaId}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("Base imponible", html);
    }

    private async Task<int> SeedFacturaAsync(IReadOnlyCollection<DetalleSeed> detalles)
    {
        var suffix = Interlocked.Increment(ref _counter);

        using var scope = _factory.Services.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();

        var marca = new Marca { Codigo = $"MC{suffix}", Nombre = $"Marca {suffix}", Activo = true };
        var categoria = new Categoria { Codigo = $"CT{suffix}", Nombre = $"Categoria {suffix}", Activo = true };
        var cliente = new Cliente
        {
            Nombre = "Cliente",
            Apellido = $"Factura {suffix}",
            NumeroDocumento = $"30{suffix}",
            Telefono = "1111-1111",
            Domicilio = "Calle Test 123"
        };

        context.Marcas.Add(marca);
        context.Categorias.Add(categoria);
        context.Clientes.Add(cliente);
        await context.SaveChangesAsync();

        var venta = new Venta
        {
            ClienteId = cliente.Id,
            Numero = $"V-{suffix}",
            FechaVenta = new DateTime(2026, 4, 30, 10, 0, 0, DateTimeKind.Utc),
            Estado = EstadoVenta.Facturada,
            TipoPago = TipoPago.Efectivo,
            Subtotal = detalles.Sum(d => d.SubtotalFinalNeto),
            IVA = detalles.Sum(d => d.SubtotalFinalIVA),
            Total = detalles.Sum(d => d.SubtotalFinal),
            EstadoAutorizacion = EstadoAutorizacionVenta.NoRequiere
        };

        foreach (var detalle in detalles)
        {
            var producto = new Producto
            {
                Codigo = $"{detalle.Codigo}-{suffix}",
                Nombre = detalle.Nombre,
                CategoriaId = categoria.Id,
                MarcaId = marca.Id,
                PrecioCompra = 1m,
                PrecioVenta = detalle.PrecioUnitario,
                PorcentajeIVA = detalle.PorcentajeIVA,
                Activo = true
            };
            context.Productos.Add(producto);
            await context.SaveChangesAsync();

            venta.Detalles.Add(new VentaDetalle
            {
                ProductoId = producto.Id,
                Cantidad = detalle.Cantidad,
                PrecioUnitario = detalle.PrecioUnitario,
                Subtotal = detalle.SubtotalFinal,
                PorcentajeIVA = detalle.PorcentajeIVA,
                AlicuotaIVANombre = detalle.AlicuotaNombre,
                SubtotalFinalNeto = detalle.SubtotalFinalNeto,
                SubtotalFinalIVA = detalle.SubtotalFinalIVA,
                SubtotalFinal = detalle.SubtotalFinal
            });
        }

        context.Ventas.Add(venta);
        await context.SaveChangesAsync();

        var factura = new Factura
        {
            VentaId = venta.Id,
            Numero = $"FC-{suffix}",
            Tipo = TipoFactura.B,
            FechaEmision = new DateTime(2026, 4, 30),
            Subtotal = venta.Subtotal,
            IVA = venta.IVA,
            Total = venta.Total
        };
        context.Facturas.Add(factura);
        await context.SaveChangesAsync();

        return factura.Id;
    }

    private async Task<int> SeedVentaDetailsAsync(
        EstadoVenta estado,
        bool incluirFactura,
        bool facturaAnulada,
        IReadOnlyCollection<DetalleSeed> detalles)
    {
        var suffix = Interlocked.Increment(ref _counter);

        using var scope = _factory.Services.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();

        var marca = new Marca { Codigo = $"MD{suffix}", Nombre = $"Marca Details {suffix}", Activo = true };
        var categoria = new Categoria { Codigo = $"CD{suffix}", Nombre = $"Categoria Details {suffix}", Activo = true };
        var cliente = new Cliente
        {
            Nombre = "Cliente",
            Apellido = $"Details {suffix}",
            NumeroDocumento = $"31{suffix}",
            Telefono = "2222-2222",
            Domicilio = "Calle Details 123"
        };

        context.Marcas.Add(marca);
        context.Categorias.Add(categoria);
        context.Clientes.Add(cliente);
        await context.SaveChangesAsync();

        var venta = new Venta
        {
            ClienteId = cliente.Id,
            Numero = $"VD-{suffix}",
            FechaVenta = new DateTime(2026, 4, 30, 11, 0, 0, DateTimeKind.Utc),
            Estado = estado,
            TipoPago = TipoPago.Efectivo,
            Subtotal = detalles.Sum(d => d.SubtotalFinalNeto),
            IVA = detalles.Sum(d => d.SubtotalFinalIVA),
            Total = detalles.Sum(d => d.SubtotalFinal),
            EstadoAutorizacion = EstadoAutorizacionVenta.NoRequiere
        };

        foreach (var detalle in detalles)
        {
            var producto = new Producto
            {
                Codigo = $"{detalle.Codigo}-{suffix}",
                Nombre = detalle.Nombre,
                CategoriaId = categoria.Id,
                MarcaId = marca.Id,
                PrecioCompra = 1m,
                PrecioVenta = detalle.PrecioUnitario,
                PorcentajeIVA = detalle.PorcentajeIVA,
                Activo = true
            };
            context.Productos.Add(producto);
            await context.SaveChangesAsync();

            venta.Detalles.Add(new VentaDetalle
            {
                ProductoId = producto.Id,
                Cantidad = detalle.Cantidad,
                PrecioUnitario = detalle.PrecioUnitario,
                Subtotal = detalle.SubtotalFinal,
                PorcentajeIVA = detalle.PorcentajeIVA,
                AlicuotaIVANombre = detalle.AlicuotaNombre,
                SubtotalFinalNeto = detalle.SubtotalFinalNeto,
                SubtotalFinalIVA = detalle.SubtotalFinalIVA,
                SubtotalFinal = detalle.SubtotalFinal
            });
        }

        context.Ventas.Add(venta);
        await context.SaveChangesAsync();

        if (incluirFactura)
        {
            context.Facturas.Add(new Factura
            {
                VentaId = venta.Id,
                Numero = $"FD-{suffix}",
                Tipo = TipoFactura.B,
                FechaEmision = new DateTime(2026, 4, 30),
                Subtotal = venta.Subtotal,
                IVA = venta.IVA,
                Total = venta.Total,
                Anulada = facturaAnulada,
                FechaAnulacion = facturaAnulada ? new DateTime(2026, 4, 30) : null
            });
            await context.SaveChangesAsync();
        }

        return venta.Id;
    }

    private sealed record DetalleSeed(
        string Codigo,
        string Nombre,
        int Cantidad,
        decimal PrecioUnitario,
        decimal PorcentajeIVA,
        string AlicuotaNombre,
        decimal SubtotalFinalNeto,
        decimal SubtotalFinalIVA,
        decimal SubtotalFinal);
}
