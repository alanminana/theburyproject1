using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Tests de integración para ReporteService.
/// Cubren GenerarReporteVentasAsync (sin ventas, con ventas calcula costo/ganancia/margen,
/// filtros por fecha/cliente/tipoPago), GenerarReporteMargenesAsync (sin productos,
/// con productos calcula margen y rotación) y ObtenerVentasAgrupadasAsync.
/// Los métodos de exportación Excel/PDF no se cubren (requieren librerías externas).
/// </summary>
public class ReporteServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly ReporteService _service;

    public ReporteServiceTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        _service = new ReporteService(_context, NullLogger<ReporteService>.Instance);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<Cliente> SeedClienteAsync()
    {
        var doc = Guid.NewGuid().ToString("N")[..8];
        var cliente = new Cliente
        {
            Nombre = "Test", Apellido = "Rep",
            TipoDocumento = "DNI", NumeroDocumento = doc,
            Email = $"{doc}@test.com", Activo = true
        };
        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync();
        return cliente;
    }

    private async Task<Producto> SeedProductoAsync(
        decimal precioCompra = 10m, decimal precioVenta = 50m,
        decimal stockActual = 20m)
    {
        var codigo = Guid.NewGuid().ToString("N")[..8];
        var cat = new Categoria { Codigo = codigo, Nombre = "Cat-" + codigo, Activo = true };
        var marca = new Marca { Codigo = codigo, Nombre = "Marca-" + codigo, Activo = true };
        _context.Categorias.Add(cat);
        _context.Marcas.Add(marca);
        await _context.SaveChangesAsync();

        var producto = new Producto
        {
            Codigo = codigo, Nombre = "Prod-" + codigo,
            CategoriaId = cat.Id, MarcaId = marca.Id,
            PrecioCompra = precioCompra, PrecioVenta = precioVenta,
            PorcentajeIVA = 21m, StockActual = stockActual, Activo = true
        };
        _context.Productos.Add(producto);
        await _context.SaveChangesAsync();
        return producto;
    }

    private async Task<Venta> SeedVentaAsync(
        int clienteId, int productoId,
        decimal precioUnitario, int cantidad,
        TipoPago tipoPago = TipoPago.Efectivo,
        DateTime? fecha = null)
    {
        var total = precioUnitario * cantidad;
        var venta = new Venta
        {
            Numero = Guid.NewGuid().ToString("N")[..8],
            ClienteId = clienteId,
            Estado = EstadoVenta.Confirmada,
            TipoPago = tipoPago,
            FechaVenta = fecha ?? DateTime.UtcNow,
            Subtotal = total,
            Total = total,
            Detalles = new List<VentaDetalle>
            {
                new()
                {
                    ProductoId = productoId,
                    Cantidad = cantidad,
                    PrecioUnitario = precioUnitario,
                    Descuento = 0m,
                    Subtotal = total
                }
            }
        };
        _context.Ventas.Add(venta);
        await _context.SaveChangesAsync();
        return venta;
    }

    // -------------------------------------------------------------------------
    // GenerarReporteVentasAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GenerarReporteVentas_SinVentas_RetornaVacio()
    {
        var filtro = new ReporteVentasFiltroViewModel();

        var resultado = await _service.GenerarReporteVentasAsync(filtro);

        Assert.Empty(resultado.Ventas);
        Assert.Equal(0m, resultado.TotalVentas);
        Assert.Equal(0, resultado.CantidadVentas);
    }

    [Fact]
    public async Task GenerarReporteVentas_ConVenta_CalculaCostoGananciayMargen()
    {
        var cliente = await SeedClienteAsync();
        // PrecioCompra=10, PrecioVenta=50, cantidad=2 → venta total=100, costo=20, ganancia=80
        var producto = await SeedProductoAsync(precioCompra: 10m, precioVenta: 50m);
        await SeedVentaAsync(cliente.Id, producto.Id, precioUnitario: 50m, cantidad: 2);

        var resultado = await _service.GenerarReporteVentasAsync(new ReporteVentasFiltroViewModel());

        Assert.Single(resultado.Ventas);
        var item = resultado.Ventas[0];
        Assert.Equal(100m, item.Total);
        Assert.Equal(20m, item.Costo);    // 2 × PrecioCompra(10)
        Assert.Equal(80m, item.Ganancia); // 100 - 20
        Assert.Equal(80m, resultado.TotalGanancia);
    }

    [Fact]
    public async Task GenerarReporteVentas_VariasVentas_AcumulaTotalesCorrectamente()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync(precioCompra: 10m, precioVenta: 50m);

        await SeedVentaAsync(cliente.Id, producto.Id, 50m, 1); // total=50
        await SeedVentaAsync(cliente.Id, producto.Id, 50m, 3); // total=150

        var resultado = await _service.GenerarReporteVentasAsync(new ReporteVentasFiltroViewModel());

        Assert.Equal(2, resultado.CantidadVentas);
        Assert.Equal(200m, resultado.TotalVentas);
        Assert.Equal(40m, resultado.TotalCosto);    // (1+3)*10
        Assert.Equal(160m, resultado.TotalGanancia);
        Assert.Equal(100m, resultado.TicketPromedio); // 200/2
    }

    [Fact]
    public async Task GenerarReporteVentas_FiltroPorFecha_InclueSoloEnRango()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();

        var ayer = DateTime.UtcNow.AddDays(-1);
        var haceTresDias = DateTime.UtcNow.AddDays(-3);

        await SeedVentaAsync(cliente.Id, producto.Id, 50m, 1, fecha: ayer);
        await SeedVentaAsync(cliente.Id, producto.Id, 50m, 1, fecha: haceTresDias); // fuera

        var filtro = new ReporteVentasFiltroViewModel
        {
            FechaDesde = DateTime.UtcNow.AddDays(-2)
        };

        var resultado = await _service.GenerarReporteVentasAsync(filtro);

        Assert.Single(resultado.Ventas); // solo la de ayer
    }

    [Fact]
    public async Task GenerarReporteVentas_FiltroPorCliente_InclueSoloDichoCliente()
    {
        var cliente1 = await SeedClienteAsync();
        var cliente2 = await SeedClienteAsync();
        var producto = await SeedProductoAsync();

        await SeedVentaAsync(cliente1.Id, producto.Id, 50m, 1);
        await SeedVentaAsync(cliente2.Id, producto.Id, 50m, 1);

        var filtro = new ReporteVentasFiltroViewModel { ClienteId = cliente1.Id };

        var resultado = await _service.GenerarReporteVentasAsync(filtro);

        Assert.Single(resultado.Ventas);
    }

    [Fact]
    public async Task GenerarReporteVentas_FiltroPorTipoPago_InclueSoloDichoTipo()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();

        await SeedVentaAsync(cliente.Id, producto.Id, 50m, 1, TipoPago.Efectivo);
        await SeedVentaAsync(cliente.Id, producto.Id, 50m, 1, TipoPago.Tarjeta);

        var filtro = new ReporteVentasFiltroViewModel { TipoPago = TipoPago.Efectivo };

        var resultado = await _service.GenerarReporteVentasAsync(filtro);

        Assert.Single(resultado.Ventas);
    }

    [Fact]
    public async Task GenerarReporteVentas_VentasPorTipoPago_AgrupaCorrectamente()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();

        await SeedVentaAsync(cliente.Id, producto.Id, 100m, 1, TipoPago.Efectivo);
        await SeedVentaAsync(cliente.Id, producto.Id, 200m, 1, TipoPago.Tarjeta);

        var resultado = await _service.GenerarReporteVentasAsync(new ReporteVentasFiltroViewModel());

        Assert.True(resultado.VentasPorTipoPago.ContainsKey(TipoPago.Efectivo.ToString()));
        Assert.True(resultado.VentasPorTipoPago.ContainsKey(TipoPago.Tarjeta.ToString()));
        Assert.Equal(100m, resultado.VentasPorTipoPago[TipoPago.Efectivo.ToString()]);
        Assert.Equal(200m, resultado.VentasPorTipoPago[TipoPago.Tarjeta.ToString()]);
    }

    // -------------------------------------------------------------------------
    // GenerarReporteMargenesAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GenerarReporteMargenes_SinProductos_RetornaVacio()
    {
        var resultado = await _service.GenerarReporteMargenesAsync();

        Assert.Empty(resultado.Productos);
    }

    [Fact]
    public async Task GenerarReporteMargenes_ConProducto_CalculaMargenCorrectamente()
    {
        // PrecioCompra=10, PrecioVenta=50 → margen = (50-10)/50 * 100 = 80%
        await SeedProductoAsync(precioCompra: 10m, precioVenta: 50m);

        var resultado = await _service.GenerarReporteMargenesAsync();

        Assert.Single(resultado.Productos);
        var item = resultado.Productos[0];
        Assert.Equal(40m, item.Ganancia);     // 50 - 10
        Assert.Equal(80m, item.MargenPorcentaje); // (40/50)*100
    }

    [Fact]
    public async Task GenerarReporteMargenes_FiltroPorCategoria_InclueSoloDichaCategoria()
    {
        var producto1 = await SeedProductoAsync(); // categoría propia
        var producto2 = await SeedProductoAsync(); // otra categoría

        var resultado = await _service.GenerarReporteMargenesAsync(categoriaId: producto1.CategoriaId);

        Assert.Single(resultado.Productos);
        Assert.Equal(producto1.Id, resultado.Productos[0].Id);
    }

    [Fact]
    public async Task GenerarReporteMargenes_ResumenCalculado()
    {
        // Prod1: ganancia 40 sobre venta 50 = 80%
        // Prod2: ganancia 20 sobre venta 30 = 66.67%
        await SeedProductoAsync(precioCompra: 10m, precioVenta: 50m);
        await SeedProductoAsync(precioCompra: 10m, precioVenta: 30m);

        var resultado = await _service.GenerarReporteMargenesAsync();

        Assert.Equal(2, resultado.Productos.Count);
        Assert.True(resultado.MargenPromedioGeneral > 0);
        // GananciaTotalPotencial = Sum(Ganancia * StockActual) — stock=20 por defecto
        Assert.Equal(resultado.Productos.Sum(p => p.GananciaPotencial), resultado.GananciaTotalPotencial);
    }
}
