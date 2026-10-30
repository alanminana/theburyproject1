using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Tests.Helpers;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// H10 (decisiones D3 y D5 de docs/iva-recargos-verificacion.md): ventas brutas, devoluciones y netas en Reportes y
/// Dashboard, y ganancia/margen calculados sobre neto sin IVA. Fórmulas en docs/reportes-ganancia-devoluciones.md.
/// Cada escenario fija montos a mano para que el resultado esperado no dependa del código bajo prueba.
/// </summary>
public class ReporteDevolucionesNetasTests : IDisposable
{
    private static readonly DateTime Hoy = new(2026, 10, 15);

    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly ReporteService _reporte;
    private readonly DashboardService _dashboard;

    public ReporteDevolucionesNetasTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();
        _context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        _context.Database.EnsureCreated();

        var reloj = new RelojComercialFake(DateOnly.FromDateTime(Hoy));
        _reporte = new ReporteService(_context, NullLogger<ReporteService>.Instance, null, reloj);
        _dashboard = new DashboardService(_context, NullLogger<DashboardService>.Instance, reloj);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // ------------------------------------------------------------------ seeds

    private async Task<Cliente> SeedClienteAsync()
    {
        var doc = Guid.NewGuid().ToString("N")[..8];
        var cliente = new Cliente
        {
            Nombre = "Test", Apellido = "Neta", TipoDocumento = "DNI", NumeroDocumento = doc,
            Email = $"{doc}@test.com", Activo = true
        };
        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync();
        return cliente;
    }

    private async Task<Producto> SeedProductoAsync(decimal precioCompra = 60m, decimal precioVenta = 121m, decimal iva = 21m)
    {
        var codigo = Guid.NewGuid().ToString("N")[..8];
        var cat = new Categoria { Codigo = codigo, Nombre = "Cat-" + codigo, Activo = true };
        var marca = new Marca { Codigo = codigo, Nombre = "Marca-" + codigo, Activo = true };
        _context.Categorias.Add(cat);
        _context.Marcas.Add(marca);
        await _context.SaveChangesAsync();

        var producto = new Producto
        {
            Codigo = codigo, Nombre = "Prod-" + codigo, CategoriaId = cat.Id, MarcaId = marca.Id,
            PrecioCompra = precioCompra, PrecioVenta = precioVenta, PorcentajeIVA = iva,
            StockActual = 50m, StockMinimo = 1m, Activo = true
        };
        _context.Productos.Add(producto);
        await _context.SaveChangesAsync();
        return producto;
    }

    /// <summary>
    /// Venta con una línea: <paramref name="cantidad"/> unidades a <paramref name="brutoUnitario"/> con IVA incluido.
    /// Total = bruto; IVA y Subtotal (neto) derivados de la alícuota, como los persiste VentaService.
    /// </summary>
    private async Task<Venta> SeedVentaAsync(
        int clienteId, Producto producto, int cantidad = 2, decimal brutoUnitario = 121m, decimal costoUnitario = 60m,
        decimal iva = 21m, DateTime? fecha = null, EstadoVenta estado = EstadoVenta.Facturada, TipoPago tipoPago = TipoPago.Efectivo,
        decimal? totalOverride = null, decimal? ivaOverride = null)
    {
        var bruto = cantidad * brutoUnitario;
        var neto = Math.Round(bruto / (1m + iva / 100m), 2, MidpointRounding.AwayFromZero);
        var total = totalOverride ?? bruto;
        var venta = new Venta
        {
            Numero = Guid.NewGuid().ToString("N")[..8],
            ClienteId = clienteId,
            Estado = estado,
            TipoPago = tipoPago,
            FechaVenta = fecha ?? Hoy.AddHours(10),
            Subtotal = total - (ivaOverride ?? bruto - neto),
            IVA = ivaOverride ?? bruto - neto,
            Total = total,
            Detalles = new List<VentaDetalle>
            {
                new()
                {
                    ProductoId = producto.Id, Cantidad = cantidad, PrecioUnitario = brutoUnitario,
                    Subtotal = bruto, SubtotalFinal = bruto, PorcentajeIVA = iva,
                    SubtotalNeto = neto, SubtotalFinalNeto = neto,
                    SubtotalIVA = bruto - neto, SubtotalFinalIVA = bruto - neto,
                    CostoUnitarioAlMomento = costoUnitario, CostoTotalAlMomento = costoUnitario * cantidad
                }
            }
        };
        _context.Ventas.Add(venta);
        await _context.SaveChangesAsync();
        return venta;
    }

    private async Task<Devolucion> SeedDevolucionAsync(
        Venta venta, Producto producto, int cantidad, decimal subtotal, EstadoDevolucion estado = EstadoDevolucion.Completada,
        DateTime? fecha = null)
    {
        var dev = new Devolucion
        {
            VentaId = venta.Id,
            ClienteId = venta.ClienteId!.Value,
            NumeroDevolucion = "D-" + Guid.NewGuid().ToString("N")[..8],
            FechaDevolucion = fecha ?? Hoy.AddHours(12),
            Motivo = MotivoDevolucion.DefectoFabrica,
            Descripcion = "test",
            Estado = estado,
            TotalDevolucion = subtotal,
            Detalles = new List<DevolucionDetalle>
            {
                new()
                {
                    ProductoId = producto.Id, Cantidad = cantidad, PrecioUnitario = subtotal / cantidad, Subtotal = subtotal,
                    EstadoProducto = EstadoProductoDevuelto.Nuevo
                }
            }
        };
        _context.Devoluciones.Add(dev);
        await _context.SaveChangesAsync();
        return dev;
    }

    private static ReporteVentasFiltroViewModel Mes() =>
        new() { FechaDesde = new DateTime(2026, 10, 1), FechaHasta = new DateTime(2026, 10, 31) };

    // ------------------------------------------------------- D5: neto sin IVA

    [Fact]
    public async Task Ganancia_SeCalculaSobreNetoSinIva()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        // 2 x 121 con IVA 21% = 242 (neto 200, IVA 42); costo 2 x 60 = 120.
        await SeedVentaAsync(cliente.Id, producto);

        var r = await _reporte.GenerarReporteVentasAsync(Mes());

        var item = Assert.Single(r.Ventas);
        Assert.Equal(242m, item.Total);
        Assert.Equal(200m, item.IngresoNeto);
        Assert.Equal(80m, item.Ganancia);           // 200 - 120 (antes: 242 - 120 = 122)
        Assert.Equal(40m, item.MargenPorcentaje);   // 80 / 200
        Assert.Equal(80m, r.TotalGanancia);
        Assert.Equal(40m, r.MargenPromedio);
        Assert.Equal(242m, r.TotalVentas);
    }

    [Fact]
    public async Task Ganancia_RecargoDebitoSeExcluyeEnSuPorcionNeta()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        // Total 242 + recargo de débito 12,10 (con IVA 21%: neto 10) = 254,10; neto total 210.
        var venta = await SeedVentaAsync(cliente.Id, producto, tipoPago: TipoPago.TarjetaDebito, totalOverride: 254.10m, ivaOverride: 44.10m);
        _context.DatosTarjeta.Add(new DatosTarjeta
        {
            VentaId = venta.Id, NombreTarjeta = "Debito", TipoTarjeta = TipoTarjeta.Debito, RecargoAplicado = 12.10m
        });
        await _context.SaveChangesAsync();

        var r = await _reporte.GenerarReporteVentasAsync(Mes());

        var item = Assert.Single(r.Ventas);
        Assert.Equal(12.10m, item.RecargoDebitoAplicado);
        Assert.Equal(200m, item.IngresoNeto);   // 254,10 - 44,10 - 10
        Assert.Equal(80m, item.Ganancia);
    }

    [Fact]
    public async Task Ganancia_ProductoSinCosto_SeTomaCostoCeroSinFallar()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync(precioCompra: 0m);
        await SeedVentaAsync(cliente.Id, producto, costoUnitario: 0m);

        var r = await _reporte.GenerarReporteVentasAsync(Mes());

        var item = Assert.Single(r.Ventas);
        Assert.Equal(0m, item.Costo);
        Assert.Equal(200m, item.Ganancia);
        Assert.Equal(100m, item.MargenPorcentaje);
    }

    [Fact]
    public async Task Ganancia_VentaSinSnapshotIva_UsaTotalComoNeto()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        // Venta legacy: IVA = 0 persistido => neto = total.
        await SeedVentaAsync(cliente.Id, producto, iva: 0m);

        var r = await _reporte.GenerarReporteVentasAsync(Mes());

        var item = Assert.Single(r.Ventas);
        Assert.Equal(242m, item.IngresoNeto);
        Assert.Equal(122m, item.Ganancia);
    }

    // ------------------------------------------------------- D3: devoluciones

    [Fact]
    public async Task Reporte_DevolucionParcial_RestaBrutoNetoYRevierteCosto()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        var venta = await SeedVentaAsync(cliente.Id, producto);
        await SeedDevolucionAsync(venta, producto, cantidad: 1, subtotal: 121m);

        var r = await _reporte.GenerarReporteVentasAsync(Mes());

        Assert.Equal(242m, r.TotalVentas);
        Assert.Equal(121m, r.TotalDevoluciones);
        Assert.Equal(1, r.CantidadDevoluciones);
        Assert.Equal(121m, r.TotalVentasNetas);
        Assert.Equal(100m, r.TotalDevolucionesNeto);
        Assert.Equal(60m, r.TotalCostoDevuelto);
        Assert.Equal(80m, r.TotalGananciaVentas);
        Assert.Equal(40m, r.TotalGanancia);         // 80 - (100 - 60)
        Assert.Equal(40m, r.MargenPromedio);        // 40 / (200 - 100)
    }

    [Fact]
    public async Task Reporte_DevolucionTotal_DejaVentasNetasEnCeroYGananciaCero()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        var venta = await SeedVentaAsync(cliente.Id, producto);
        await SeedDevolucionAsync(venta, producto, cantidad: 2, subtotal: 242m);

        var r = await _reporte.GenerarReporteVentasAsync(Mes());

        Assert.Equal(0m, r.TotalVentasNetas);
        Assert.Equal(0m, r.TotalGanancia);
        Assert.Equal(0m, r.MargenPromedio);
    }

    [Theory]
    [InlineData(EstadoDevolucion.Aprobada, true)]
    [InlineData(EstadoDevolucion.Completada, true)]
    [InlineData(EstadoDevolucion.Pendiente, false)]
    [InlineData(EstadoDevolucion.EnRevision, false)]
    [InlineData(EstadoDevolucion.Rechazada, false)]
    [InlineData(EstadoDevolucion.Cancelada, false)]
    public async Task Reporte_SoloAprobadasYCompletadasRestan(EstadoDevolucion estado, bool resta)
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        var venta = await SeedVentaAsync(cliente.Id, producto);
        await SeedDevolucionAsync(venta, producto, 1, 121m, estado);

        var r = await _reporte.GenerarReporteVentasAsync(Mes());
        var d = await _dashboard.GetDashboardDataAsync();

        var esperadoDev = resta ? 121m : 0m;
        Assert.Equal(esperadoDev, r.TotalDevoluciones);
        Assert.Equal(242m - esperadoDev, r.TotalVentasNetas);
        Assert.Equal(242m - esperadoDev, d.VentasTotalesMes);
    }

    [Fact]
    public async Task Reporte_LaDevolucionCuentaEnElPeriodoDeSuFecha_NoEnElDeLaVenta()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        // Venta de septiembre, devuelta en octubre.
        var venta = await SeedVentaAsync(cliente.Id, producto, fecha: new DateTime(2026, 9, 20, 10, 0, 0));
        await SeedDevolucionAsync(venta, producto, 1, 121m, fecha: new DateTime(2026, 10, 5, 10, 0, 0));

        var octubre = await _reporte.GenerarReporteVentasAsync(Mes());
        var septiembre = await _reporte.GenerarReporteVentasAsync(
            new ReporteVentasFiltroViewModel { FechaDesde = new DateTime(2026, 9, 1), FechaHasta = new DateTime(2026, 9, 30) });

        Assert.Equal(0m, octubre.TotalVentas);
        Assert.Equal(121m, octubre.TotalDevoluciones);
        Assert.Equal(-121m, octubre.TotalVentasNetas);
        Assert.Equal(-40m, octubre.TotalGanancia);   // -(100 - 60)
        Assert.Equal(242m, septiembre.TotalVentas);
        Assert.Equal(0m, septiembre.TotalDevoluciones);
    }

    [Fact]
    public async Task Reporte_DevolucionDeVentaCancelada_NoRestaPorqueLaVentaNoCuenta()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        var venta = await SeedVentaAsync(cliente.Id, producto, estado: EstadoVenta.Cancelada);
        await SeedDevolucionAsync(venta, producto, 1, 121m);

        var r = await _reporte.GenerarReporteVentasAsync(Mes());

        Assert.Equal(0m, r.TotalVentas);
        Assert.Equal(0m, r.TotalDevoluciones);
    }

    [Fact]
    public async Task Reporte_FiltroDeCliente_RestaSoloDevolucionesDeEseCliente()
    {
        var c1 = await SeedClienteAsync();
        var c2 = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        var v1 = await SeedVentaAsync(c1.Id, producto);
        var v2 = await SeedVentaAsync(c2.Id, producto);
        await SeedDevolucionAsync(v1, producto, 1, 121m);
        await SeedDevolucionAsync(v2, producto, 2, 242m);

        var filtro = Mes();
        filtro.ClienteId = c1.Id;
        var r = await _reporte.GenerarReporteVentasAsync(filtro);

        Assert.Equal(242m, r.TotalVentas);
        Assert.Equal(121m, r.TotalDevoluciones);
    }

    [Fact]
    public async Task Reporte_DevolucionConIva105_UsaLaAlicuotaDeLaLinea()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync(iva: 10.5m);
        // 1 x 110,50 con IVA 10,5% => neto 100,00; costo 40.
        var venta = await SeedVentaAsync(cliente.Id, producto, cantidad: 1, brutoUnitario: 110.50m, costoUnitario: 40m, iva: 10.5m);
        await SeedDevolucionAsync(venta, producto, 1, 110.50m);

        var r = await _reporte.GenerarReporteVentasAsync(Mes());

        Assert.Equal(100m, r.TotalDevolucionesNeto);
        Assert.Equal(40m, r.TotalCostoDevuelto);
        Assert.Equal(0m, r.TotalGanancia);
    }

    [Fact]
    public async Task Reporte_DevolucionValuadaPorLoRealmenteCobrado_NoPorElPrecioDeLista()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        // Venta con descuento: se cobró 100 con IVA (neto 82,64...) por 1 unidad de lista 121.
        var venta = await SeedVentaAsync(cliente.Id, producto, cantidad: 1, brutoUnitario: 100m, costoUnitario: 50m);
        await SeedDevolucionAsync(venta, producto, 1, 100m);

        var r = await _reporte.GenerarReporteVentasAsync(Mes());

        Assert.Equal(100m, r.TotalDevoluciones);
        Assert.Equal(82.64m, r.TotalDevolucionesNeto);
        Assert.Equal(0m, r.TotalGanancia);
    }

    // --------------------------------------------------------------- Dashboard

    [Fact]
    public async Task Dashboard_VentasDelMes_ConcuerdaConReporteNetoYConSql()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        var v1 = await SeedVentaAsync(cliente.Id, producto);
        await SeedVentaAsync(cliente.Id, producto, cantidad: 1);
        await SeedDevolucionAsync(v1, producto, 1, 121m);

        var d = await _dashboard.GetDashboardDataAsync();
        var r = await _reporte.GenerarReporteVentasAsync(Mes());

        // Contraste independiente: 242 + 121 - 121 = 242.
        Assert.Equal(363m, d.VentasBrutasMes);
        Assert.Equal(121m, d.DevolucionesMes);
        Assert.Equal(1, d.CantidadDevolucionesMes);
        Assert.Equal(242m, d.VentasTotalesMes);
        Assert.Equal(r.TotalVentasNetas, d.VentasTotalesMes);
        Assert.Equal(r.TotalDevoluciones, d.DevolucionesMes);
        Assert.Equal(2, d.CantidadVentasMes);
        Assert.Equal(242m, d.VentasTotalesHoy);
        Assert.Equal(242m, d.VentasTotalesAnio);
    }

    [Fact]
    public async Task Dashboard_GraficoDiario_RestaLaDevolucionEnSuDia()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        var venta = await SeedVentaAsync(cliente.Id, producto, fecha: Hoy.AddDays(-2).AddHours(10));
        await SeedDevolucionAsync(venta, producto, 1, 121m, fecha: Hoy.AddHours(9));

        var d = await _dashboard.GetDashboardDataAsync();

        Assert.Equal(242m, d.VentasUltimos7Dias.Single(x => x.Fecha == Hoy.AddDays(-2)).Total);
        Assert.Equal(-121m, d.VentasUltimos7Dias.Single(x => x.Fecha == Hoy).Total);
        var mes = d.VentasUltimos12Meses.Single(x => x.Anio == 2026 && x.Mes == 10);
        Assert.Equal(121m, mes.Total);
    }

    // ------------------------------------------------------------ Márgenes

    [Fact]
    public async Task Margenes_CalculaSobrePrecioNetoSinIva()
    {
        var producto = await SeedProductoAsync(precioCompra: 60m, precioVenta: 121m, iva: 21m);

        var r = await _reporte.GenerarReporteMargenesAsync();

        var p = Assert.Single(r.Productos, x => x.Id == producto.Id);
        Assert.Equal(121m, p.PrecioVenta);
        Assert.Equal(100m, p.PrecioVentaNeto);
        Assert.Equal(40m, p.Ganancia);              // 100 - 60 (antes 121 - 60 = 61)
        Assert.Equal(40m, p.MargenPorcentaje);      // 40 / 100
        Assert.Equal(40m * 50m, p.GananciaPotencial);
    }

    [Fact]
    public async Task Margenes_RestaDevolucionesDeLasUnidadesVendidasUltimos30Dias()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        var venta = await SeedVentaAsync(cliente.Id, producto, cantidad: 4, fecha: DateTime.UtcNow.AddDays(-3));
        await SeedDevolucionAsync(venta, producto, 1, 121m, fecha: DateTime.UtcNow.AddDays(-1));

        var r = await _reporte.GenerarReporteMargenesAsync();

        Assert.Equal(3, Assert.Single(r.Productos, x => x.Id == producto.Id).VentasUltimos30Dias);
    }
}
