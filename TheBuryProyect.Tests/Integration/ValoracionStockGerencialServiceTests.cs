using System.Text.Json;
using System.Text.Json.Serialization;
using ClosedXML.Excel;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// R-03: valoración gerencial con el Kardex real (recepciones de orden de compra, salidas, ajustes) y prueba de NO REGRESIÓN:
/// calcular y exportar no cambia ningún costo, precio, margen ni movimiento persistido.
/// </summary>
public class ValoracionStockGerencialServiceTests : IDisposable
{
    private sealed class UsuarioStub : ICurrentUserService
    {
        public string GetUsername() => "testuser";
        public string GetUserId() => "user-001";
        public string GetEmail() => "test@test.com";
        public bool IsAuthenticated() => true;
        public bool IsInRole(string role) => false;
        public bool HasPermission(string modulo, string accion) => true;
        public string GetIpAddress() => "127.0.0.1";
    }

    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly OrdenCompraService _ordenes;
    private readonly MovimientoStockService _movimientos;
    private readonly ValoracionStockGerencialService _service;

    public ValoracionStockGerencialServiceTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();
        _context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        _context.Database.EnsureCreated();

        _movimientos = new MovimientoStockService(_context, NullLogger<MovimientoStockService>.Instance);
        _ordenes = new OrdenCompraService(_context, NullLogger<OrdenCompraService>.Instance, _movimientos, new UsuarioStub());
        _service = new ValoracionStockGerencialService(_context);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // ── helpers ─────────────────────────────────────────────────────────

    private async Task<Proveedor> SeedProveedorAsync()
    {
        var cuit = Guid.NewGuid().ToString("N")[..11];
        var p = new Proveedor { Cuit = cuit, RazonSocial = "Proveedor-" + cuit, Activo = true };
        _context.Proveedores.Add(p);
        await _context.SaveChangesAsync();
        return p;
    }

    private async Task<Producto> SeedProductoAsync(decimal precioCompraFicha = 10m, decimal precioVenta = 15m)
    {
        var codigo = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var categoria = new Categoria { Codigo = codigo, Nombre = "Cat-" + codigo, Activo = true };
        var marca = new Marca { Codigo = codigo, Nombre = "Marca-" + codigo, Activo = true };
        _context.AddRange(categoria, marca);
        await _context.SaveChangesAsync();
        var producto = new Producto
        {
            Codigo = codigo, Nombre = "Prod-" + codigo, CategoriaId = categoria.Id, MarcaId = marca.Id,
            PrecioCompra = precioCompraFicha, PrecioVenta = precioVenta, PorcentajeIVA = 21m, StockActual = 0m, Activo = true
        };
        _context.Productos.Add(producto);
        await _context.SaveChangesAsync();
        return producto;
    }

    /// <summary>Recepciona una orden de compra completa con el servicio real (deja una entrada de Kardex con fuente OrdenCompraDetalle).</summary>
    private async Task RecibirCompraAsync(Proveedor proveedor, Producto producto, int cantidad, decimal precioUnitario)
    {
        var orden = new OrdenCompra
        {
            Numero = $"OC-{DateTime.UtcNow.Year}-{Guid.NewGuid().ToString("N")[..6]}",
            ProveedorId = proveedor.Id, Estado = EstadoOrdenCompra.Confirmada, FechaEmision = DateTime.UtcNow,
            Detalles = new List<OrdenCompraDetalle>
            {
                new() { ProductoId = producto.Id, Cantidad = cantidad, PrecioUnitario = precioUnitario, Subtotal = cantidad * precioUnitario }
            }
        };
        _context.OrdenesCompra.Add(orden);
        await _context.SaveChangesAsync();
        await _context.Entry(orden).ReloadAsync();
        var detalle = orden.Detalles.Single();

        await _ordenes.RecepcionarAsync(orden.Id, orden.RowVersion, new List<RecepcionDetalleViewModel>
        {
            new() { DetalleId = detalle.Id, ProductoId = producto.Id, CantidadARecepcionar = cantidad }
        });
    }

    private Task SalidaAsync(Producto producto, decimal cantidad) =>
        _movimientos.RegistrarSalidasAsync(new List<(int, decimal, string?)> { (producto.Id, cantidad, "venta de prueba") }, "Venta", "testuser");

    private async Task<ItemValoracionStock> ValorarAsync(Producto producto)
    {
        var r = await _service.CalcularAsync(new FiltroValoracionStock { Busqueda = producto.Codigo, IncluirSinStock = true });
        return r.Items.Single(i => i.ProductoId == producto.Id);
    }

    // ── cálculo con el Kardex real ───────────────────────────────────────

    [Fact]
    public async Task ComprasRecibidasADistintoPrecioYSalidas_DanElPromedioMovilYElUltimoCosto()
    {
        var proveedor = await SeedProveedorAsync();
        var producto = await SeedProductoAsync(precioCompraFicha: 10m);
        await RecibirCompraAsync(proveedor, producto, 10, 100m);
        await RecibirCompraAsync(proveedor, producto, 10, 120m);
        await SalidaAsync(producto, 5);

        var item = await ValorarAsync(producto);

        Assert.Equal(EstadoValoracionStock.PromedioMovilExacto, item.Estado);
        Assert.Equal(15m, item.StockActual);
        Assert.Equal(110m, item.CostoPromedioPonderado);
        Assert.Equal(120m, item.UltimoCostoCompra);
        Assert.Equal(15m * 110m, item.ValorGerencial);
        Assert.Equal(15m * 120m, item.ValorAUltimoCosto);
        Assert.Equal(150m, item.DiferenciaVsUltimoCosto);
        // El costo de ficha es otro dato (el que usan precios y márgenes): se informa, no se mezcla.
        Assert.Equal(10m, item.CostoFichaProducto);
    }

    [Fact]
    public async Task UnaTerceraCompraRecalculaElPromedioSobreElSaldoRestante()
    {
        var proveedor = await SeedProveedorAsync();
        var producto = await SeedProductoAsync();
        await RecibirCompraAsync(proveedor, producto, 10, 100m);
        await RecibirCompraAsync(proveedor, producto, 10, 120m);
        await SalidaAsync(producto, 5);
        await RecibirCompraAsync(proveedor, producto, 5, 200m);

        var item = await ValorarAsync(producto);

        Assert.Equal(20m, item.StockActual);
        Assert.Equal((15m * 110m + 5m * 200m) / 20m, item.CostoPromedioPonderado);
        Assert.Equal(200m, item.UltimoCostoCompra);
    }

    [Fact]
    public async Task AjusteManualNegativoYPositivo_SeTratanComoSalidaYEntradaAlPromedio()
    {
        var proveedor = await SeedProveedorAsync();
        var producto = await SeedProductoAsync(precioCompraFicha: 50m);
        await RecibirCompraAsync(proveedor, producto, 10, 100m);
        await _movimientos.RegistrarAjusteAsync(producto.Id, TipoMovimiento.Salida, 2m, "ref", "rotura", "testuser");

        var item = await ValorarAsync(producto);

        Assert.Equal(8m, item.StockActual);
        Assert.Equal(EstadoValoracionStock.PromedioMovilExacto, item.Estado);
        Assert.Equal(100m, item.CostoPromedioPonderado);
        Assert.Equal(800m, item.ValorGerencial);
    }

    [Fact]
    public async Task ProductoConStockSinCompras_NoTieneCostoCalculable_YElSinStockSoloAparecePidiendolo()
    {
        var sinCompras = await SeedProductoAsync();
        sinCompras.StockActual = 4m; // stock cargado fuera de una compra
        var sinStock = await SeedProductoAsync();
        await _context.SaveChangesAsync();

        var porDefecto = await _service.CalcularAsync(new FiltroValoracionStock());
        Assert.Contains(porDefecto.Items, i => i.ProductoId == sinCompras.Id && i.Estado == EstadoValoracionStock.SinCompras
                                               && i.CostoPromedioPonderado == null && i.ValorGerencial == null);
        Assert.DoesNotContain(porDefecto.Items, i => i.ProductoId == sinStock.Id);

        var conSinStock = await _service.CalcularAsync(new FiltroValoracionStock { Estado = EstadoValoracionStock.SinStock });
        Assert.Contains(conSinStock.Items, i => i.ProductoId == sinStock.Id && i.Estado == EstadoValoracionStock.SinStock);
        Assert.DoesNotContain(conSinStock.Items, i => i.ProductoId == sinCompras.Id);
    }

    [Fact]
    public async Task StockEditadoFueraDelKardex_UsaLaComposicionPorLasUltimasCompras()
    {
        var proveedor = await SeedProveedorAsync();
        var producto = await SeedProductoAsync();
        await RecibirCompraAsync(proveedor, producto, 10, 100m);
        await RecibirCompraAsync(proveedor, producto, 10, 120m);
        // Edición directa del stock (sin movimiento): el Kardex ya no concilia.
        var p = await _context.Productos.SingleAsync(x => x.Id == producto.Id);
        p.StockActual = 15m;
        await _context.SaveChangesAsync();

        var item = await ValorarAsync(producto);

        Assert.Equal(EstadoValoracionStock.ComposicionPorCompras, item.Estado);
        Assert.Equal((10m * 120m + 5m * 100m) / 15m, item.CostoPromedioPonderado);
        Assert.Equal(15m, item.UnidadesCubiertas);
    }

    [Fact]
    public async Task ElResumenSumaSoloLosProductosConCoberturaCompleta()
    {
        var proveedor = await SeedProveedorAsync();
        var completo = await SeedProductoAsync();
        await RecibirCompraAsync(proveedor, completo, 10, 100m);
        var sinCompras = await SeedProductoAsync();
        sinCompras.StockActual = 3m;
        await _context.SaveChangesAsync();

        var r = await _service.CalcularAsync(new FiltroValoracionStock { Busqueda = null });

        Assert.Equal(1000m, r.Resumen.ValorGerencialTotal);
        Assert.Equal(1000m, r.Resumen.ValorAUltimoCostoTotal);
        Assert.Equal(0m, r.Resumen.DiferenciaVsUltimoCostoTotal);
        Assert.Equal(1, r.Resumen.ProductosConValorCompleto);
        Assert.Equal(1, r.Resumen.ProductosSinCompras);
    }

    // ── no regresión: nada persistido cambia ─────────────────────────────

    private async Task<string> SnapshotAsync()
    {
        var opciones = new JsonSerializerOptions { ReferenceHandler = ReferenceHandler.IgnoreCycles, WriteIndented = false };
        var partes = new List<string>
        {
            JsonSerializer.Serialize(await _context.Productos.AsNoTracking().IgnoreQueryFilters().OrderBy(x => x.Id).ToListAsync(), opciones),
            JsonSerializer.Serialize(await _context.ProductosPrecios.AsNoTracking().IgnoreQueryFilters().OrderBy(x => x.Id).ToListAsync(), opciones),
            JsonSerializer.Serialize(await _context.ListasPrecios.AsNoTracking().IgnoreQueryFilters().OrderBy(x => x.Id).ToListAsync(), opciones),
            JsonSerializer.Serialize(await _context.MovimientosStock.AsNoTracking().IgnoreQueryFilters().OrderBy(x => x.Id).ToListAsync(), opciones),
            JsonSerializer.Serialize(await _context.PriceChangeBatches.AsNoTracking().IgnoreQueryFilters().OrderBy(x => x.Id).ToListAsync(), opciones),
            JsonSerializer.Serialize(await _context.OrdenesCompra.AsNoTracking().IgnoreQueryFilters().OrderBy(x => x.Id).ToListAsync(), opciones),
            JsonSerializer.Serialize(await _context.Set<OrdenCompraDetalle>().AsNoTracking().IgnoreQueryFilters().OrderBy(x => x.Id).ToListAsync(), opciones)
        };
        return string.Join("\n#\n", partes);
    }

    [Fact]
    public async Task NoRegresion_CalcularYExportar_NoCambianNingunCostoPrecioMargenNiMovimientoPersistido()
    {
        var proveedor = await SeedProveedorAsync();
        var producto = await SeedProductoAsync(precioCompraFicha: 77m, precioVenta: 200m);
        await RecibirCompraAsync(proveedor, producto, 10, 100m);
        await RecibirCompraAsync(proveedor, producto, 10, 120m);
        await SalidaAsync(producto, 4);
        var lista = new ListaPrecio { Nombre = "L", Codigo = "L" + Guid.NewGuid().ToString("N")[..6], Tipo = TipoListaPrecio.Contado, Activa = true, Orden = 1 };
        _context.ListasPrecios.Add(lista);
        await _context.SaveChangesAsync();
        _context.ProductosPrecios.Add(new ProductoPrecioLista
        {
            ProductoId = producto.Id, ListaId = lista.Id, VigenciaDesde = DateTime.UtcNow.AddDays(-1), Costo = 77m, Precio = 200m,
            MargenValor = 123m, MargenPorcentaje = 159.74m, EsVigente = true, EsManual = true, CreadoPor = "seed"
        });
        await _context.SaveChangesAsync();

        var antes = await SnapshotAsync();

        var resultado = await _service.CalcularAsync(new FiltroValoracionStock { IncluirSinStock = true });
        var excel = await _service.ExportarAsync(new FiltroValoracionStock { IncluirSinStock = true });

        Assert.NotEmpty(resultado.Items);
        Assert.NotEmpty(excel);
        Assert.False(_context.ChangeTracker.HasChanges());
        Assert.Equal(antes, await SnapshotAsync());

        // Y el costo de ficha (el que usan precios y márgenes) sigue siendo el de ficha, no el promedio.
        var persistido = await _context.Productos.AsNoTracking().SingleAsync(p => p.Id == producto.Id);
        Assert.Equal(77m, persistido.PrecioCompra);
        Assert.Equal(200m, persistido.PrecioVenta);
        Assert.Equal(16m, persistido.StockActual);
    }

    [Fact]
    public async Task NoRegresion_ElPrecioAutomaticoSigueUsandoElCostoQueSePasa_NoElPromedio()
    {
        // CalcularPrecioAutomaticoAsync no depende del Kardex: la valoración gerencial no cambia la sugerencia de precios.
        var proveedor = await SeedProveedorAsync();
        var producto = await SeedProductoAsync(precioCompraFicha: 50m);
        await RecibirCompraAsync(proveedor, producto, 10, 100m);
        var lista = new ListaPrecio { Nombre = "M", Codigo = "M" + Guid.NewGuid().ToString("N")[..6], Tipo = TipoListaPrecio.Contado, Activa = true, Orden = 1, MargenPorcentaje = 40m, ReglaRedondeo = "ninguno" };
        _context.ListasPrecios.Add(lista);
        await _context.SaveChangesAsync();

        var precios = new PrecioService(_context, NullLogger<PrecioService>.Instance, new UsuarioStub(),
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        var antes = await precios.CalcularPrecioAutomaticoAsync(producto.Id, lista.Id, 50m);
        await _service.CalcularAsync(new FiltroValoracionStock());
        var despues = await precios.CalcularPrecioAutomaticoAsync(producto.Id, lista.Id, 50m);

        Assert.Equal(70m, antes);
        Assert.Equal(antes, despues);
    }

    [Fact]
    public async Task Exportar_EscribeLaLeyendaYDejaVaciasLasCeldasSinValor()
    {
        var proveedor = await SeedProveedorAsync();
        var conCosto = await SeedProductoAsync();
        await RecibirCompraAsync(proveedor, conCosto, 10, 100m);
        var sinCompras = await SeedProductoAsync();
        sinCompras.StockActual = 3m;
        await _context.SaveChangesAsync();

        var bytes = await _service.ExportarAsync(new FiltroValoracionStock());

        using var wb = new XLWorkbook(new MemoryStream(bytes));
        var ws = wb.Worksheet(1);
        Assert.Contains("No es valuación fiscal ni contable", ws.Cell(2, 1).GetString());
        var filaSinCompras = ws.RowsUsed().Single(r => r.Cell(1).GetString() == sinCompras.Codigo);
        Assert.True(filaSinCompras.Cell(7).IsEmpty());   // costo promedio
        Assert.True(filaSinCompras.Cell(12).IsEmpty());  // valor gerencial
        var filaConCosto = ws.RowsUsed().Single(r => r.Cell(1).GetString() == conCosto.Codigo);
        Assert.Equal(100d, filaConCosto.Cell(7).GetDouble());
        Assert.Equal(1000d, filaConCosto.Cell(12).GetDouble());
    }
}
