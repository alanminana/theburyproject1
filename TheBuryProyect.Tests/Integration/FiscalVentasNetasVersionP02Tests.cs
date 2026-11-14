using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Tests.Helpers;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// P-02 / P5 (cobertura): cómo reaccionan <see cref="VentasNetas"/>, el reporte de ventas y las devoluciones ante
/// (a) un cambio posterior de la configuración y (b) una venta con versión de criterio fiscal NO registrada.
/// Los tests de (b) CARACTERIZAN el comportamiento actual (fail-closed solo donde se necesita el criterio: reparto del
/// recargo de débito); si ese comportamiento es el deseado es una decisión pendiente (docs/p02-cobertura-versionado.md).
/// </summary>
public class FiscalVentasNetasVersionP02Tests : IDisposable
{
    private static readonly DateTime Hoy = new(2026, 10, 15);

    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly ReporteService _reporte;
    private static int _n = 72_000;

    public FiscalVentasNetasVersionP02Tests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();
        _context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        _context.Database.EnsureCreated();
        _reporte = new ReporteService(_context, NullLogger<ReporteService>.Instance, null, new RelojComercialFake(DateOnly.FromDateTime(Hoy)));
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    /// <summary>
    /// Venta facturada de 2 × 121 (neto 200, IVA 42). Con <paramref name="debito"/> suma un recargo de débito de 12,10
    /// (neto 10): total 254,10, IVA 44,10.
    /// </summary>
    private async Task<Venta> SeedVentaAsync(int version, decimal? snapshot, bool debito, bool conDevolucion)
    {
        var n = Interlocked.Increment(ref _n);
        var cat = new Categoria { Codigo = $"CV{n}", Nombre = $"Cat {n}", Activo = true };
        var marca = new Marca { Codigo = $"MV{n}", Nombre = $"Marca {n}", Activo = true };
        _context.Categorias.Add(cat);
        _context.Marcas.Add(marca);
        await _context.SaveChangesAsync();
        var cliente = new Cliente
        {
            Nombre = "Test", Apellido = "VN", TipoDocumento = "DNI", NumeroDocumento = n.ToString("D8"),
            Email = $"vn{n}@test.com", Activo = true
        };
        var producto = new Producto
        {
            Codigo = $"PV{n}", Nombre = $"Prod {n}", CategoriaId = cat.Id, MarcaId = marca.Id, PrecioCompra = 60m,
            PrecioVenta = 121m, PorcentajeIVA = 21m, StockActual = 50m, StockMinimo = 1m, Activo = true
        };
        _context.Clientes.Add(cliente);
        _context.Productos.Add(producto);
        await _context.SaveChangesAsync();

        var total = debito ? 254.10m : 242m;
        var iva = debito ? 44.10m : 42m;
        var venta = new Venta
        {
            Numero = $"VN-{n}", ClienteId = cliente.Id, Estado = EstadoVenta.Facturada,
            TipoPago = debito ? TipoPago.TarjetaDebito : TipoPago.Efectivo, FechaVenta = Hoy.AddHours(10),
            Subtotal = total - iva, IVA = iva, Total = total,
            CriterioFiscalVersion = version, AlicuotaServiciosSnapshot = snapshot,
            Detalles = new List<VentaDetalle>
            {
                new()
                {
                    ProductoId = producto.Id, Cantidad = 2, PrecioUnitario = 121m, Subtotal = 242m, SubtotalFinal = 242m,
                    PorcentajeIVA = 21m, SubtotalNeto = 200m, SubtotalFinalNeto = 200m, SubtotalIVA = 42m, SubtotalFinalIVA = 42m,
                    CostoUnitarioAlMomento = 60m, CostoTotalAlMomento = 120m
                }
            }
        };
        _context.Ventas.Add(venta);
        await _context.SaveChangesAsync();
        if (debito)
        {
            _context.DatosTarjeta.Add(new DatosTarjeta
            {
                VentaId = venta.Id, NombreTarjeta = "Debito", TipoTarjeta = TipoTarjeta.Debito, RecargoAplicado = 12.10m
            });
            await _context.SaveChangesAsync();
        }
        if (conDevolucion)
        {
            _context.Devoluciones.Add(new Devolucion
            {
                VentaId = venta.Id, ClienteId = cliente.Id, NumeroDevolucion = $"D-{n}", FechaDevolucion = Hoy.AddHours(12),
                Motivo = MotivoDevolucion.DefectoFabrica, Descripcion = "test", Estado = EstadoDevolucion.Completada,
                TotalDevolucion = 121m,
                Detalles = new List<DevolucionDetalle>
                {
                    new() { ProductoId = producto.Id, Cantidad = 1, PrecioUnitario = 121m, Subtotal = 121m, EstadoProducto = EstadoProductoDevuelto.Nuevo }
                }
            });
            await _context.SaveChangesAsync();
        }
        _context.ChangeTracker.Clear();
        return venta;
    }

    private async Task<(Venta Venta, List<VentaDetalle> Detalles)> CargarAsync(int ventaId) =>
        (await _context.Ventas.AsNoTracking().SingleAsync(v => v.Id == ventaId),
         await _context.VentaDetalles.AsNoTracking().Where(d => d.VentaId == ventaId).ToListAsync());

    private static ReporteVentasFiltroViewModel Mes() =>
        new() { FechaDesde = new DateTime(2026, 10, 1), FechaHasta = new DateTime(2026, 10, 31) };

    // ───────────────────── (a) un cambio de configuración no altera los cálculos de VentasNetas ─────────────────────

    [Theory]
    [InlineData(21)]    // con snapshot
    [InlineData(null)]  // sin snapshot (histórica): VentasNetas no usa la alícuota de servicios
    public async Task VentasNetas_ConRecargoDebitoYDevolucion_NoDependeDeLaConfiguracionDeAlicuota(int? snapshot)
    {
        var venta = await SeedVentaAsync(CriterioFiscalRegistry.VersionInicial, snapshot, debito: true, conDevolucion: true);

        async Task<(decimal Neto, decimal DevBruto, decimal DevNeto)> Medir()
        {
            var (v, detalles) = await CargarAsync(venta.Id);
            var dev = Assert.Single(await VentasNetas.ObtenerDevolucionesAsync(_context, Hoy.Date, null));
            return (VentasNetas.CalcularIngresoNeto(v, 12.10m, detalles), dev.Bruto, dev.Neto);
        }

        (decimal, decimal, decimal) antes, durante;
        using (ProductoIvaResolver.UsarPorcentajeDefaultParaPruebas(21m)) antes = await Medir();
        using (ProductoIvaResolver.UsarPorcentajeDefaultParaPruebas(10.5m)) durante = await Medir();

        Assert.Equal(antes, durante);
        Assert.Equal(200m, antes.Item1);   // 254,10 − 44,10 − 10 (neto del recargo)
    }

    [Fact]
    public async Task ReporteVentas_ConSnapshot_NoCambiaAlModificarLaConfiguracion()
    {
        await SeedVentaAsync(CriterioFiscalRegistry.VersionInicial, 21m, debito: true, conDevolucion: false);

        TheBuryProject.ViewModels.ReporteVentasResultadoViewModel a, b;
        using (ProductoIvaResolver.UsarPorcentajeDefaultParaPruebas(21m)) a = await _reporte.GenerarReporteVentasAsync(Mes());
        using (ProductoIvaResolver.UsarPorcentajeDefaultParaPruebas(10.5m)) b = await _reporte.GenerarReporteVentasAsync(Mes());

        Assert.Equal(a.Ventas.Single().IngresoNeto, b.Ventas.Single().IngresoNeto);
        Assert.Equal(a.Ventas.Single().Ganancia, b.Ventas.Single().Ganancia);
        Assert.Equal(a.TotalVentas, b.TotalVentas);
    }

    // ───────────────────── (b) versión fiscal no registrada ─────────────────────

    [Fact]
    public async Task VersionDesconocida_SinRecargoDebito_VentasNetasYReporte_NoNecesitanElCriterio_YNoFallan()
    {
        var venta = await SeedVentaAsync(version: 99, snapshot: 21m, debito: false, conDevolucion: true);
        var (v, detalles) = await CargarAsync(venta.Id);

        Assert.Equal(200m, VentasNetas.CalcularIngresoNeto(v, 0m, detalles));   // total − IVA: no consulta el criterio
        var dev = Assert.Single(await VentasNetas.ObtenerDevolucionesAsync(_context, Hoy.Date, null));
        Assert.Equal((121m, 100m), (dev.Bruto, dev.Neto));

        var r = await _reporte.GenerarReporteVentasAsync(Mes());
        Assert.Equal(200m, r.Ventas.Single().IngresoNeto);
    }

    [Fact]
    public async Task VersionDesconocida_ConRecargoDebito_CalcularIngresoNeto_FallaConExcepcionTipada_SinUsarOtraRegla()
    {
        var venta = await SeedVentaAsync(version: 99, snapshot: 21m, debito: true, conDevolucion: false);
        var (v, detalles) = await CargarAsync(venta.Id);

        var ex = Assert.Throws<CriterioFiscalDesconocidoException>(() => VentasNetas.CalcularIngresoNeto(v, 12.10m, detalles));
        Assert.Equal(99, ex.Version);
    }

    [Fact]
    public async Task VersionDesconocida_ConRecargoDebito_DevolucionesNetas_FallaConExcepcionTipada()
    {
        await SeedVentaAsync(version: 99, snapshot: 21m, debito: true, conDevolucion: true);

        var ex = await Assert.ThrowsAsync<CriterioFiscalDesconocidoException>(
            () => VentasNetas.ObtenerDevolucionesAsync(_context, Hoy.Date, null));
        Assert.Equal(99, ex.Version);
    }

    [Fact]
    public async Task VersionDesconocida_ConRecargoDebito_ReporteDeVentas_Caracteriza_UnaVentaCorruptaRompeTodoElReporte()
    {
        // CARACTERIZACIÓN (riesgo documentado, no comportamiento deseado confirmado): a diferencia del reporte de IVA
        // (que excluye la venta y la informa aparte), el reporte de ventas propaga la excepción. Excluir/avisar cambiaría
        // los totales del reporte: decisión del propietario (docs/p02-cobertura-versionado.md).
        await SeedVentaAsync(CriterioFiscalRegistry.VersionInicial, 21m, debito: false, conDevolucion: false);
        await SeedVentaAsync(version: 99, snapshot: 21m, debito: true, conDevolucion: false);

        await Assert.ThrowsAsync<CriterioFiscalDesconocidoException>(() => _reporte.GenerarReporteVentasAsync(Mes()));
    }
}
