using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Tests de integración para DashboardService.
/// Cubren GetDashboardDataAsync: base vacía (todos los KPIs en cero), conteo de clientes,
/// KPIs de ventas (hoy/mes/año/ticket promedio), KPIs de créditos, cuotas vencidas,
/// KPIs de productos (total, stock bajo, valor stock), y listas de detalle.
/// </summary>
public class DashboardServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly DashboardService _service;

    public DashboardServiceTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        _service = new DashboardService(_context, NullLogger<DashboardService>.Instance);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<Cliente> SeedClienteAsync(bool activo = true)
    {
        var doc = Guid.NewGuid().ToString("N")[..8];
        var cliente = new Cliente
        {
            Nombre = "Test", Apellido = "Cliente",
            TipoDocumento = "DNI", NumeroDocumento = doc,
            Email = $"{doc}@test.com", Activo = activo
        };
        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync();
        return cliente;
    }

    private async Task<(Categoria, Marca)> SeedCategoriaYMarcaAsync()
    {
        var code = Guid.NewGuid().ToString("N")[..8];
        var cat = new Categoria { Codigo = code, Nombre = "Cat-" + code, Activo = true };
        var marca = new Marca { Codigo = code, Nombre = "Marca-" + code, Activo = true };
        _context.Categorias.Add(cat);
        _context.Marcas.Add(marca);
        await _context.SaveChangesAsync();
        return (cat, marca);
    }

    private async Task<Producto> SeedProductoAsync(
        decimal precioCompra = 10m, decimal precioVenta = 50m,
        decimal stockActual = 5m, decimal stockMinimo = 3m)
    {
        var (cat, marca) = await SeedCategoriaYMarcaAsync();
        var code = Guid.NewGuid().ToString("N")[..8];
        var prod = new Producto
        {
            Codigo = code, Nombre = "Prod-" + code,
            CategoriaId = cat.Id, MarcaId = marca.Id,
            PrecioCompra = precioCompra, PrecioVenta = precioVenta,
            PorcentajeIVA = 21m, StockActual = stockActual,
            StockMinimo = stockMinimo, Activo = true
        };
        _context.Productos.Add(prod);
        await _context.SaveChangesAsync();
        return prod;
    }

    private async Task<Venta> SeedVentaAsync(int clienteId, decimal total = 100m, DateTime? fecha = null)
    {
        var venta = new Venta
        {
            Numero = Guid.NewGuid().ToString("N")[..8],
            ClienteId = clienteId,
            Estado = EstadoVenta.Confirmada,
            TipoPago = TipoPago.Efectivo,
            FechaVenta = fecha ?? DateTime.Today,
            Subtotal = total, Total = total
        };
        _context.Ventas.Add(venta);
        await _context.SaveChangesAsync();
        return venta;
    }

    private async Task<Credito> SeedCreditoActivoAsync(int clienteId,
        decimal totalAPagar = 1000m, decimal saldoPendiente = 500m)
    {
        var credito = new Credito
        {
            Numero = Guid.NewGuid().ToString("N")[..8],
            ClienteId = clienteId,
            Estado = EstadoCredito.Activo,
            TotalAPagar = totalAPagar,
            SaldoPendiente = saldoPendiente,
            CantidadCuotas = 12,
            MontoCuota = totalAPagar / 12
        };
        _context.Creditos.Add(credito);
        await _context.SaveChangesAsync();
        return credito;
    }

    private async Task<VentaDetalle> SeedVentaConDetalleAsync(
        int clienteId, int productoId, int cantidad,
        decimal subtotal, decimal subtotalFinal = 0m,
        DateTime? fechaVenta = null)
    {
        var fecha = fechaVenta ?? DateTime.Today;
        var venta = new Venta
        {
            Numero = Guid.NewGuid().ToString("N")[..8],
            ClienteId = clienteId,
            Estado = EstadoVenta.Confirmada,
            TipoPago = TipoPago.Efectivo,
            FechaVenta = fecha,
            Subtotal = subtotal,
            Total = subtotalFinal > 0m ? subtotalFinal : subtotal,
            Detalles = new List<VentaDetalle>
            {
                new()
                {
                    ProductoId = productoId,
                    Cantidad = cantidad,
                    PrecioUnitario = subtotal / cantidad,
                    Subtotal = subtotal,
                    SubtotalFinal = subtotalFinal
                }
            }
        };
        _context.Ventas.Add(venta);
        await _context.SaveChangesAsync();
        return venta.Detalles.First();
    }

    private int _cuotaCounter = 1;
    private async Task<Cuota> SeedCuotaAsync(int creditoId, EstadoCuota estado,
        DateTime fechaVencimiento, decimal montoTotal = 100m,
        decimal montoPagado = 0m, DateTime? fechaPago = null)
    {
        var cuota = new Cuota
        {
            CreditoId = creditoId,
            NumeroCuota = _cuotaCounter++,
            Estado = estado,
            FechaVencimiento = fechaVencimiento,
            MontoTotal = montoTotal,
            MontoPagado = montoPagado,
            FechaPago = fechaPago
        };
        _context.Cuotas.Add(cuota);
        await _context.SaveChangesAsync();
        return cuota;
    }

    // -------------------------------------------------------------------------
    // Base vacía — todos los KPIs en cero
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetDashboard_BaseVacia_TodosKPIsEnCero()
    {
        var resultado = await _service.GetDashboardDataAsync();

        Assert.Equal(0, resultado.TotalClientes);
        Assert.Equal(0, resultado.ClientesActivos);
        Assert.Equal(0, resultado.ClientesNuevosMes);
        Assert.Equal(0m, resultado.VentasTotalesHoy);
        Assert.Equal(0m, resultado.VentasTotalesMes);
        Assert.Equal(0, resultado.CantidadVentasMes);
        Assert.Equal(0m, resultado.VentasTotalesAnio);
        Assert.Equal(0m, resultado.TicketPromedio);
        Assert.Equal(0, resultado.CreditosActivos);
        Assert.Equal(0m, resultado.MontoTotalCreditos);
        Assert.Equal(0m, resultado.SaldoPendienteTotal);
        Assert.Equal(0, resultado.CuotasVencidasTotal);
        Assert.Equal(0m, resultado.MontoVencidoTotal);
        Assert.Equal(0, resultado.ProductosTotales);
        Assert.Equal(0, resultado.ProductosStockBajo);
        Assert.Equal(0m, resultado.ValorStockPrecioVenta);
        Assert.Equal(0m, resultado.ValorStockCostoActual);
        Assert.Equal(0m, resultado.ValorTotalStock);
    }

    // -------------------------------------------------------------------------
    // KPIs de clientes
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetDashboard_ConClientes_KPIsClientes()
    {
        await SeedClienteAsync(activo: true);
        await SeedClienteAsync(activo: true);
        await SeedClienteAsync(activo: false);

        var resultado = await _service.GetDashboardDataAsync();

        Assert.Equal(3, resultado.TotalClientes);
        Assert.Equal(2, resultado.ClientesActivos);
        Assert.Equal(3, resultado.ClientesNuevosMes); // todos creados este mes
    }

    // -------------------------------------------------------------------------
    // KPIs de ventas
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetDashboard_ConVentasHoy_SumaVentasHoy()
    {
        var cliente = await SeedClienteAsync();

        await SeedVentaAsync(cliente.Id, total: 150m, fecha: DateTime.Today);
        await SeedVentaAsync(cliente.Id, total: 200m, fecha: DateTime.Today.AddDays(-1));

        var resultado = await _service.GetDashboardDataAsync();

        Assert.Equal(150m, resultado.VentasTotalesHoy);
    }

    [Fact]
    public async Task GetDashboard_ConVentasMes_SumaVentasMes()
    {
        var cliente = await SeedClienteAsync();
        var inicioMes = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

        await SeedVentaAsync(cliente.Id, total: 100m, fecha: inicioMes);
        await SeedVentaAsync(cliente.Id, total: 200m, fecha: DateTime.Today);

        var resultado = await _service.GetDashboardDataAsync();

        Assert.Equal(300m, resultado.VentasTotalesMes);
        Assert.Equal(2, resultado.CantidadVentasMes);
    }

    [Fact]
    public async Task GetDashboard_TicketPromedio_CalculaCorrectamente()
    {
        var cliente = await SeedClienteAsync();
        var inicioMes = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

        await SeedVentaAsync(cliente.Id, total: 100m, fecha: inicioMes);
        await SeedVentaAsync(cliente.Id, total: 200m, fecha: inicioMes.AddDays(1));

        var resultado = await _service.GetDashboardDataAsync();

        Assert.Equal(150m, resultado.TicketPromedio); // (100 + 200) / 2
    }

    [Fact]
    public async Task GetDashboard_VentasFueraDelAnio_NoSeIncluyen()
    {
        var cliente = await SeedClienteAsync();

        await SeedVentaAsync(cliente.Id, total: 999m, fecha: new DateTime(DateTime.Today.Year - 1, 12, 31));

        var resultado = await _service.GetDashboardDataAsync();

        Assert.Equal(0m, resultado.VentasTotalesAnio);
    }

    // -------------------------------------------------------------------------
    // KPIs de créditos
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetDashboard_ConCreditosActivos_KPIsCreditos()
    {
        var cliente = await SeedClienteAsync();
        await SeedCreditoActivoAsync(cliente.Id, totalAPagar: 1000m, saldoPendiente: 600m);
        await SeedCreditoActivoAsync(cliente.Id, totalAPagar: 2000m, saldoPendiente: 1500m);

        var resultado = await _service.GetDashboardDataAsync();

        Assert.Equal(2, resultado.CreditosActivos);
        Assert.Equal(3000m, resultado.MontoTotalCreditos);
        Assert.Equal(2100m, resultado.SaldoPendienteTotal);
    }

    // -------------------------------------------------------------------------
    // KPIs de cuotas vencidas
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetDashboard_ConCuotasVencidas_KPIsCuotasVencidas()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoActivoAsync(cliente.Id);

        // Cuota vencida (pendiente + vencimiento pasado)
        await SeedCuotaAsync(credito.Id, EstadoCuota.Pendiente,
            fechaVencimiento: DateTime.Today.AddDays(-5), montoTotal: 200m);
        // Cuota no vencida
        await SeedCuotaAsync(credito.Id, EstadoCuota.Pendiente,
            fechaVencimiento: DateTime.Today.AddDays(5), montoTotal: 200m);

        var resultado = await _service.GetDashboardDataAsync();

        Assert.Equal(1, resultado.CuotasVencidasTotal);
        Assert.Equal(200m, resultado.MontoVencidoTotal);
    }

    // -------------------------------------------------------------------------
    // KPIs de productos
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetDashboard_ConProductos_KPIsProductos()
    {
        // stock > minimo → no bajo
        await SeedProductoAsync(precioCompra: 40m, precioVenta: 100m, stockActual: 10m, stockMinimo: 5m);
        // stock < minimo → stock bajo
        await SeedProductoAsync(precioCompra: 120m, precioVenta: 200m, stockActual: 2m, stockMinimo: 5m);

        var resultado = await _service.GetDashboardDataAsync();

        Assert.Equal(2, resultado.ProductosTotales);
        Assert.Equal(1, resultado.ProductosStockBajo);
        // ValorStockPrecioVenta = (100 * 10) + (200 * 2) = 1000 + 400 = 1400
        Assert.Equal(1400m, resultado.ValorStockPrecioVenta);
        // ValorStockCostoActual = (40 * 10) + (120 * 2) = 400 + 240 = 640
        Assert.Equal(640m, resultado.ValorStockCostoActual);
        // Alias temporal para compatibilidad: mantiene la semantica previa de precio de venta.
        Assert.Equal(1400m, resultado.ValorTotalStock);
    }

    // -------------------------------------------------------------------------
    // Listas de detalle — solo verifican que no lanzan excepción y retornan listas
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetDashboard_ListasDetalle_NoLanzanExcepcion()
    {
        var resultado = await _service.GetDashboardDataAsync();

        Assert.NotNull(resultado.VentasUltimos7Dias);
        Assert.NotNull(resultado.VentasUltimos12Meses);
        Assert.NotNull(resultado.ProductosMasVendidos);
        Assert.NotNull(resultado.CreditosPorEstado);
        Assert.NotNull(resultado.CobranzaUltimos6Meses);
        Assert.NotNull(resultado.CuotasProximasVencer);
        Assert.NotNull(resultado.CuotasVencidasLista);
        Assert.NotNull(resultado.OrdenesCompraPendientes);
    }

    [Fact]
    public async Task GetDashboard_VentasUltimos7Dias_ContieneEntradaParaCadaDia()
    {
        var cliente = await SeedClienteAsync();
        await SeedVentaAsync(cliente.Id, total: 50m, fecha: DateTime.Today);

        var resultado = await _service.GetDashboardDataAsync();

        // Debe haber 8 entradas (hace7Dias..hoy inclusive)
        Assert.Equal(8, resultado.VentasUltimos7Dias.Count);
        // El día de hoy debe sumar 50
        var hoy = resultado.VentasUltimos7Dias.FirstOrDefault(v => v.Fecha == DateTime.Today);
        Assert.NotNull(hoy);
        Assert.Equal(50m, hoy!.Total);
    }

    // -------------------------------------------------------------------------
    // ProductosMasVendidos — SubtotalFinal con fallback
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetDashboard_ProductosMasVendidos_UsaSubtotalFinalCuandoExiste()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        // Subtotal=200, SubtotalFinal=180 → TotalVendido debe ser 180
        await SeedVentaConDetalleAsync(cliente.Id, producto.Id, cantidad: 2, subtotal: 200m, subtotalFinal: 180m);

        var resultado = await _service.GetDashboardDataAsync();

        Assert.Single(resultado.ProductosMasVendidos);
        Assert.Equal(180m, resultado.ProductosMasVendidos[0].TotalVendido);
    }

    [Fact]
    public async Task GetDashboard_ProductosMasVendidos_FallbackASubtotalSiSubtotalFinalEsCero()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        // Subtotal=200, SubtotalFinal=0 → TotalVendido debe caer en Subtotal=200
        await SeedVentaConDetalleAsync(cliente.Id, producto.Id, cantidad: 2, subtotal: 200m, subtotalFinal: 0m);

        var resultado = await _service.GetDashboardDataAsync();

        Assert.Single(resultado.ProductosMasVendidos);
        Assert.Equal(200m, resultado.ProductosMasVendidos[0].TotalVendido);
    }
}
