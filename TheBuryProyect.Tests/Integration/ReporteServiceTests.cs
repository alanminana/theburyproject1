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

    private async Task<ApplicationUser> SeedUsuarioAsync(string id, string userName)
    {
        var usuario = new ApplicationUser
        {
            Id = id,
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = $"{id}@test.com",
            NormalizedEmail = $"{id}@test.com".ToUpperInvariant(),
            Activo = true
        };
        _context.Users.Add(usuario);
        await _context.SaveChangesAsync();
        return usuario;
    }

    private async Task<Venta> SeedVentaAsync(
        int clienteId, int productoId,
        decimal precioUnitario, int cantidad,
        TipoPago tipoPago = TipoPago.Efectivo,
        DateTime? fecha = null,
        EstadoVenta estado = EstadoVenta.Confirmada,
        string? vendedorUserId = null,
        string? vendedorNombre = null,
        decimal comisionPorcentaje = 0m,
        decimal comisionMonto = 0m)
    {
        var total = precioUnitario * cantidad;
        var venta = new Venta
        {
            Numero = Guid.NewGuid().ToString("N")[..8],
            ClienteId = clienteId,
            Estado = estado,
            TipoPago = tipoPago,
            FechaVenta = fecha ?? DateTime.UtcNow,
            VendedorUserId = vendedorUserId,
            VendedorNombre = vendedorNombre,
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
                    Subtotal = total,
                    ComisionPorcentajeAplicada = comisionPorcentaje,
                    ComisionMonto = comisionMonto
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
    // GenerarReporteComisionesVendedoresAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GenerarReporteComisiones_UsaSnapshotDeVentaDetalle()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        await SeedUsuarioAsync("vend-1", "Vendedor Uno");
        await SeedVentaAsync(
            cliente.Id,
            producto.Id,
            precioUnitario: 100m,
            cantidad: 2,
            vendedorUserId: "vend-1",
            vendedorNombre: "Vendedor Uno",
            estado: EstadoVenta.Facturada,
            comisionPorcentaje: 8m,
            comisionMonto: 16m);

        var resultado = await _service.GenerarReporteComisionesVendedoresAsync(new ComisionVendedorFilterViewModel());

        Assert.Single(resultado.Items);
        Assert.Equal(200m, resultado.TotalVendido);
        Assert.Equal(16m, resultado.TotalComision);
        Assert.Equal(8m, resultado.Items[0].ComisionPorcentajeAplicada);
        Assert.Equal(16m, resultado.Items[0].ComisionMonto);
    }

    [Fact]
    public async Task GenerarReporteComisiones_CambioPosteriorProducto_NoAlteraReporteHistorico()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        await SeedVentaAsync(
            cliente.Id,
            producto.Id,
            precioUnitario: 100m,
            cantidad: 1,
            estado: EstadoVenta.Facturada,
            comisionPorcentaje: 8m,
            comisionMonto: 8m);

        producto.ComisionPorcentaje = 25m;
        await _context.SaveChangesAsync();

        var resultado = await _service.GenerarReporteComisionesVendedoresAsync(new ComisionVendedorFilterViewModel());

        Assert.Single(resultado.Items);
        Assert.Equal(8m, resultado.Items[0].ComisionPorcentajeAplicada);
        Assert.Equal(8m, resultado.Items[0].ComisionMonto);
    }

    [Fact]
    public async Task GenerarReporteComisiones_FiltroPorVendedor_IncluyeSoloVendedor()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        await SeedUsuarioAsync("vend-1", "Uno");
        await SeedUsuarioAsync("vend-2", "Dos");

        await SeedVentaAsync(cliente.Id, producto.Id, 100m, 1, vendedorUserId: "vend-1", vendedorNombre: "Uno", estado: EstadoVenta.Facturada, comisionPorcentaje: 8m, comisionMonto: 8m);
        await SeedVentaAsync(cliente.Id, producto.Id, 100m, 1, vendedorUserId: "vend-2", vendedorNombre: "Dos", estado: EstadoVenta.Facturada, comisionPorcentaje: 8m, comisionMonto: 8m);

        var resultado = await _service.GenerarReporteComisionesVendedoresAsync(new ComisionVendedorFilterViewModel
        {
            VendedorUserId = "vend-1"
        });

        Assert.Single(resultado.Items);
        Assert.Equal("vend-1", resultado.Items[0].VendedorUserId);
    }

    [Fact]
    public async Task GenerarReporteComisiones_FiltroPorRangoFechas_IncluyeSoloRango()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        var hoy = DateTime.UtcNow.Date;

        await SeedVentaAsync(cliente.Id, producto.Id, 100m, 1, fecha: hoy, estado: EstadoVenta.Facturada, comisionPorcentaje: 8m, comisionMonto: 8m);
        await SeedVentaAsync(cliente.Id, producto.Id, 100m, 1, fecha: hoy.AddDays(-10), estado: EstadoVenta.Facturada, comisionPorcentaje: 8m, comisionMonto: 8m);

        var resultado = await _service.GenerarReporteComisionesVendedoresAsync(new ComisionVendedorFilterViewModel
        {
            FechaDesde = hoy.AddDays(-1),
            FechaHasta = hoy
        });

        Assert.Single(resultado.Items);
        Assert.Equal(hoy, resultado.Items[0].FechaVenta.Date);
    }

    [Fact]
    public async Task GenerarReporteComisiones_FiltroPorTipoPago_IncluyeSoloTipo()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();

        await SeedVentaAsync(cliente.Id, producto.Id, 100m, 1, TipoPago.Efectivo, estado: EstadoVenta.Facturada, comisionPorcentaje: 8m, comisionMonto: 8m);
        await SeedVentaAsync(cliente.Id, producto.Id, 100m, 1, TipoPago.Transferencia, estado: EstadoVenta.Facturada, comisionPorcentaje: 8m, comisionMonto: 8m);

        var resultado = await _service.GenerarReporteComisionesVendedoresAsync(new ComisionVendedorFilterViewModel
        {
            TipoPago = TipoPago.Transferencia
        });

        Assert.Single(resultado.Items);
        Assert.Equal(TipoPago.Transferencia, resultado.Items[0].TipoPago);
    }

    [Fact]
    public async Task GenerarReporteComisiones_SoloIncluyeFacturadasYEntregadasPorDefecto()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();

        await SeedVentaAsync(cliente.Id, producto.Id, 100m, 1, estado: EstadoVenta.Facturada, comisionPorcentaje: 8m, comisionMonto: 8m);
        await SeedVentaAsync(cliente.Id, producto.Id, 100m, 1, estado: EstadoVenta.Confirmada, comisionPorcentaje: 8m, comisionMonto: 8m);
        await SeedVentaAsync(cliente.Id, producto.Id, 100m, 1, estado: EstadoVenta.Cancelada, comisionPorcentaje: 8m, comisionMonto: 8m);

        var resultado = await _service.GenerarReporteComisionesVendedoresAsync(new ComisionVendedorFilterViewModel());

        Assert.Single(resultado.Items);
        Assert.Equal(EstadoVenta.Facturada, resultado.Items[0].EstadoVenta);
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

    // -------------------------------------------------------------------------
    // ObtenerVentasAgrupadasAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ObtenerVentasAgrupadas_AgrupadoPorDia_AgrupaPorFecha()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();

        var hoy = DateTime.UtcNow.Date;
        await SeedVentaAsync(cliente.Id, producto.Id, precioUnitario: 100m, cantidad: 2, fecha: hoy);
        await SeedVentaAsync(cliente.Id, producto.Id, precioUnitario: 50m, cantidad: 1, fecha: hoy);

        var resultado = await _service.ObtenerVentasAgrupadasAsync(
            hoy.AddDays(-1), hoy.AddDays(1), "dia");

        Assert.Single(resultado); // ambas ventas del mismo día → un solo grupo
        Assert.Equal(2, resultado[0].Cantidad);
        Assert.Equal(250m, resultado[0].Monto); // 200 + 50
    }

    [Fact]
    public async Task ObtenerVentasAgrupadas_AgrupadoPorDia_DiasDiferentesProducenGruposSeparados()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();

        var hoy = DateTime.UtcNow.Date;
        var ayer = hoy.AddDays(-1);
        await SeedVentaAsync(cliente.Id, producto.Id, precioUnitario: 100m, cantidad: 1, fecha: hoy);
        await SeedVentaAsync(cliente.Id, producto.Id, precioUnitario: 80m, cantidad: 1, fecha: ayer);

        var resultado = await _service.ObtenerVentasAgrupadasAsync(
            ayer.AddDays(-1), hoy.AddDays(1), "dia");

        Assert.Equal(2, resultado.Count);
        Assert.Equal(180m, resultado.Sum(g => g.Monto));
    }

    [Fact]
    public async Task ObtenerVentasAgrupadas_AgrupadoPorMes_AgrupaPorMes()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();

        var mesActual = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        await SeedVentaAsync(cliente.Id, producto.Id, precioUnitario: 200m, cantidad: 1, fecha: mesActual);
        await SeedVentaAsync(cliente.Id, producto.Id, precioUnitario: 150m, cantidad: 1, fecha: mesActual.AddDays(5));

        var resultado = await _service.ObtenerVentasAgrupadasAsync(
            mesActual.AddDays(-1), mesActual.AddDays(40), "mes");

        Assert.Single(resultado); // mismo mes → un grupo
        Assert.Equal(350m, resultado[0].Monto);
    }

    [Fact]
    public async Task ObtenerVentasAgrupadas_AgrupadoPorMes_MesesDiferentesProducenGruposSeparados()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();

        var enero = new DateTime(DateTime.UtcNow.Year, 1, 15);
        var febrero = new DateTime(DateTime.UtcNow.Year, 2, 15);
        await SeedVentaAsync(cliente.Id, producto.Id, precioUnitario: 100m, cantidad: 1, fecha: enero);
        await SeedVentaAsync(cliente.Id, producto.Id, precioUnitario: 200m, cantidad: 1, fecha: febrero);

        var resultado = await _service.ObtenerVentasAgrupadasAsync(
            enero.AddDays(-1), febrero.AddDays(1), "mes");

        Assert.Equal(2, resultado.Count);
        Assert.Equal(300m, resultado.Sum(g => g.Monto));
    }

    [Fact]
    public async Task ObtenerVentasAgrupadas_AgrupadoPorCategoria_AgrupaPorCategoriaProducto()
    {
        var cliente = await SeedClienteAsync();
        var prod1 = await SeedProductoAsync(precioCompra: 10m, precioVenta: 100m);
        var prod2 = await SeedProductoAsync(precioCompra: 20m, precioVenta: 80m);

        var hoy = DateTime.UtcNow.Date;
        await SeedVentaAsync(cliente.Id, prod1.Id, precioUnitario: 100m, cantidad: 2, fecha: hoy);
        await SeedVentaAsync(cliente.Id, prod2.Id, precioUnitario: 80m, cantidad: 1, fecha: hoy);

        var resultado = await _service.ObtenerVentasAgrupadasAsync(
            hoy.AddDays(-1), hoy.AddDays(1), "categoria");

        // Cada producto tiene su propia categoría (seeded con nombre único)
        Assert.Equal(2, resultado.Count);
        Assert.True(resultado.All(g => g.Monto > 0));
    }

    [Fact]
    public async Task ObtenerVentasAgrupadas_GrupoPorDia_GananciaCalculadaCorrectamente()
    {
        var cliente = await SeedClienteAsync();
        // PrecioCompra=20, PrecioVenta=100, cantidad=3 → costo=60, ingreso=300, ganancia=240
        var producto = await SeedProductoAsync(precioCompra: 20m, precioVenta: 100m);

        var hoy = DateTime.UtcNow.Date;
        await SeedVentaAsync(cliente.Id, producto.Id, precioUnitario: 100m, cantidad: 3, fecha: hoy);

        var resultado = await _service.ObtenerVentasAgrupadasAsync(
            hoy.AddDays(-1), hoy.AddDays(1), "dia");

        Assert.Single(resultado);
        Assert.Equal(300m, resultado[0].Monto);
        Assert.Equal(240m, resultado[0].Ganancia);
    }

    [Fact]
    public async Task ObtenerVentasAgrupadas_GrupoPorCategoria_GananciaCalculadaCorrectamente()
    {
        var cliente = await SeedClienteAsync();
        // PrecioCompra=30, PrecioVenta=100, cantidad=2 → ganancia=(100-30)*2=140
        var producto = await SeedProductoAsync(precioCompra: 30m, precioVenta: 100m);

        var hoy = DateTime.UtcNow.Date;
        await SeedVentaAsync(cliente.Id, producto.Id, precioUnitario: 100m, cantidad: 2, fecha: hoy);

        var resultado = await _service.ObtenerVentasAgrupadasAsync(
            hoy.AddDays(-1), hoy.AddDays(1), "categoria");

        Assert.Single(resultado);
        Assert.Equal(140m, resultado[0].Ganancia);
    }

    [Fact]
    public async Task ObtenerVentasAgrupadas_AgrupadoPorDesconocido_RetornaVacio()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();

        var hoy = DateTime.UtcNow.Date;
        await SeedVentaAsync(cliente.Id, producto.Id, precioUnitario: 100m, cantidad: 1, fecha: hoy);

        var resultado = await _service.ObtenerVentasAgrupadasAsync(
            hoy.AddDays(-1), hoy.AddDays(1), "invalido");

        Assert.Empty(resultado);
    }

    [Fact]
    public async Task ObtenerVentasAgrupadas_SinVentasEnRango_RetornaVacio()
    {
        var resultado = await _service.ObtenerVentasAgrupadasAsync(
            DateTime.UtcNow.AddYears(-2),
            DateTime.UtcNow.AddYears(-1),
            "dia");

        Assert.Empty(resultado);
    }

    // =========================================================================
    // GenerarReporteMorosidadAsync
    // =========================================================================

    private async Task<Cuota> SeedCuotaVencidaAsync(int creditoId, decimal montoTotal = 1_000m, int diasVencido = 10)
    {
        var cuota = new Cuota
        {
            CreditoId = creditoId,
            NumeroCuota = 1,
            MontoCapital = montoTotal * 0.8m,
            MontoInteres = montoTotal * 0.2m,
            MontoTotal = montoTotal,
            MontoPagado = 0m,
            Estado = EstadoCuota.Pendiente,
            FechaVencimiento = DateTime.UtcNow.Date.AddDays(-diasVencido)
        };
        _context.Set<Cuota>().Add(cuota);
        await _context.SaveChangesAsync();
        return cuota;
    }

    private async Task<Credito> SeedCreditoAsync(int clienteId, decimal saldo = 5_000m)
    {
        var credito = new Credito
        {
            Numero = Guid.NewGuid().ToString("N")[..10],
            ClienteId = clienteId,
            Estado = EstadoCredito.Activo,
            MontoSolicitado = saldo,
            MontoAprobado = saldo,
            SaldoPendiente = saldo,
            TasaInteres = 3m,
            CantidadCuotas = 12,
            FechaSolicitud = DateTime.UtcNow
        };
        _context.Set<Credito>().Add(credito);
        await _context.SaveChangesAsync();
        return credito;
    }

    [Fact]
    public async Task GenerarReporteMorosidad_SinCuotasVencidas_RetornaReporteVacio()
    {
        var resultado = await _service.GenerarReporteMorosidadAsync();

        Assert.NotNull(resultado);
        Assert.Empty(resultado.ClientesMorosos);
        Assert.Equal(0, resultado.CantidadClientesMorosos);
        Assert.Equal(0m, resultado.TotalDeudaVencida);
    }

    [Fact]
    public async Task GenerarReporteMorosidad_ConClienteMoroso_IncluyeClienteEnResultado()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        await SeedCuotaVencidaAsync(credito.Id, montoTotal: 1_000m);

        var resultado = await _service.GenerarReporteMorosidadAsync();

        Assert.Equal(1, resultado.CantidadClientesMorosos);
        Assert.Equal(1_000m, resultado.TotalDeudaVencida);
        Assert.Equal(cliente.Id, resultado.ClientesMorosos[0].ClienteId);
    }

    [Fact]
    public async Task GenerarReporteMorosidad_MultiplesCuotasMismoCliente_AgrupaPorCliente()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        await SeedCuotaVencidaAsync(credito.Id, montoTotal: 500m, diasVencido: 10);
        // Segunda cuota del mismo crédito — diferente NumeroCuota sería requerido
        // pero como es un credito distinto no hay restricción
        var credito2 = await SeedCreditoAsync(cliente.Id);
        await SeedCuotaVencidaAsync(credito2.Id, montoTotal: 800m, diasVencido: 20);

        var resultado = await _service.GenerarReporteMorosidadAsync();

        // Ambas cuotas pertenecen al mismo cliente → 1 registro en ClientesMorosos
        Assert.Equal(1, resultado.CantidadClientesMorosos);
        Assert.Equal(1_300m, resultado.TotalDeudaVencida);
        Assert.Equal(2, resultado.ClientesMorosos[0].CantidadCreditosVencidos);
    }

    [Fact]
    public async Task GenerarReporteMorosidad_MultiplesClientes_DevuelveUnoporCliente()
    {
        var c1 = await SeedClienteAsync();
        var c2 = await SeedClienteAsync();
        var cr1 = await SeedCreditoAsync(c1.Id);
        var cr2 = await SeedCreditoAsync(c2.Id);
        await SeedCuotaVencidaAsync(cr1.Id, montoTotal: 1_000m);
        await SeedCuotaVencidaAsync(cr2.Id, montoTotal: 2_000m);

        var resultado = await _service.GenerarReporteMorosidadAsync();

        Assert.Equal(2, resultado.CantidadClientesMorosos);
        Assert.Equal(3_000m, resultado.TotalDeudaVencida);
    }

    [Fact]
    public async Task GenerarReporteMorosidad_CalulaPromedioDeudaPorCliente()
    {
        var c1 = await SeedClienteAsync();
        var c2 = await SeedClienteAsync();
        var cr1 = await SeedCreditoAsync(c1.Id);
        var cr2 = await SeedCreditoAsync(c2.Id);
        await SeedCuotaVencidaAsync(cr1.Id, montoTotal: 1_000m);
        await SeedCuotaVencidaAsync(cr2.Id, montoTotal: 3_000m);

        var resultado = await _service.GenerarReporteMorosidadAsync();

        // Promedio = 4000 / 2 = 2000
        Assert.Equal(2_000m, resultado.PromedioDeudaPorCliente);
    }

    // =========================================================================
    // ExportarVentasExcelAsync / ExportarMargenesExcelAsync /
    // ExportarMorosidadExcelAsync / GenerarVentasPdfAsync /
    // GenerarMorosidadPdfAsync
    // =========================================================================

    [Fact]
    public async Task ExportarVentasExcel_SinVentas_RetornaByteArrayNoVacio()
    {
        var filtro = new ReporteVentasFiltroViewModel();

        var resultado = await _service.ExportarVentasExcelAsync(filtro);

        Assert.NotNull(resultado);
        Assert.True(resultado.Length > 0);
    }

    [Fact]
    public async Task ExportarVentasExcel_ConVentas_RetornaArchivoValido()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        await SeedVentaAsync(cliente.Id, producto.Id, 50m, 2);

        var filtro = new ReporteVentasFiltroViewModel();

        var resultado = await _service.ExportarVentasExcelAsync(filtro);

        Assert.True(resultado.Length > 0);
        // Signature de ZIP/Office Open XML: PK magic bytes
        Assert.Equal(0x50, resultado[0]); // 'P'
        Assert.Equal(0x4B, resultado[1]); // 'K'
    }

    [Fact]
    public async Task ExportarMargenesExcel_SinProductos_RetornaByteArrayNoVacio()
    {
        var resultado = await _service.ExportarMargenesExcelAsync();

        Assert.NotNull(resultado);
        Assert.True(resultado.Length > 0);
    }

    [Fact]
    public async Task ExportarMorosidadExcel_SinMorosos_RetornaByteArrayNoVacio()
    {
        var resultado = await _service.ExportarMorosidadExcelAsync();

        Assert.NotNull(resultado);
        Assert.True(resultado.Length > 0);
    }

    [Fact]
    public async Task GenerarVentasPdf_SinVentas_RetornaByteArrayNoVacio()
    {
        var filtro = new ReporteVentasFiltroViewModel();

        var resultado = await _service.GenerarVentasPdfAsync(filtro);

        Assert.NotNull(resultado);
        Assert.True(resultado.Length > 0);
        // PDF magic bytes: %PDF
        Assert.Equal(0x25, resultado[0]); // '%'
        Assert.Equal(0x50, resultado[1]); // 'P'
        Assert.Equal(0x44, resultado[2]); // 'D'
        Assert.Equal(0x46, resultado[3]); // 'F'
    }

    [Fact]
    public async Task GenerarMorosidadPdf_SinMorosos_RetornaByteArrayNoVacio()
    {
        var resultado = await _service.GenerarMorosidadPdfAsync();

        Assert.NotNull(resultado);
        Assert.True(resultado.Length > 0);
        // PDF magic bytes
        Assert.Equal(0x25, resultado[0]);
        Assert.Equal(0x50, resultado[1]);
    }
}
