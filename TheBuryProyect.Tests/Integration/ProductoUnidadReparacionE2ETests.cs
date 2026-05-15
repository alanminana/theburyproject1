using AutoMapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;
using TheBuryProject.Services.Validators;
using TheBuryProject.ViewModels;
using TheBuryProject.ViewModels.Requests;

namespace TheBuryProject.Tests.Integration;

// ---------------------------------------------------------------------------
// Stubs file-scoped para este test file
// ---------------------------------------------------------------------------

file sealed class StubCajaE2ERep : ICajaService
{
    private readonly AperturaCaja _apertura;
    public StubCajaE2ERep(AperturaCaja apertura) => _apertura = apertura;

    public Task<AperturaCaja?> ObtenerAperturaActivaParaUsuarioAsync(string usuario)
        => Task.FromResult<AperturaCaja?>(_apertura);
    public Task<MovimientoCaja?> RegistrarMovimientoVentaAsync(int ventaId, string ventaNumero, decimal monto, TipoPago tipoPago, string usuario)
        => Task.FromResult<MovimientoCaja?>(new MovimientoCaja());
    public Task<MovimientoCaja?> RegistrarMovimientoAnticipoAsync(int creditoId, string creditoNumero, decimal montoAnticipo, string usuario)
        => Task.FromResult<MovimientoCaja?>(new MovimientoCaja());
    public Task<MovimientoCaja?> RegistrarContramovimientoVentaAsync(int ventaId, string ventaNumero, string motivo, string usuario)
        => Task.FromResult<MovimientoCaja?>(null);

    public Task<List<Caja>> ObtenerTodasCajasAsync() => throw new NotImplementedException();
    public Task<Caja?> ObtenerCajaPorIdAsync(int id) => throw new NotImplementedException();
    public Task<Caja> CrearCajaAsync(CajaViewModel model) => throw new NotImplementedException();
    public Task<Caja> ActualizarCajaAsync(int id, CajaViewModel model) => throw new NotImplementedException();
    public Task EliminarCajaAsync(int id, byte[]? rowVersion = null) => throw new NotImplementedException();
    public Task<bool> ExisteCodigoCajaAsync(string codigo, int? cajaIdExcluir = null) => throw new NotImplementedException();
    public Task<AperturaCaja> AbrirCajaAsync(AbrirCajaViewModel model, string usuario) => throw new NotImplementedException();
    public Task<AperturaCaja?> ObtenerAperturaActivaAsync(int cajaId) => throw new NotImplementedException();
    public Task<AperturaCaja?> ObtenerAperturaPorIdAsync(int id) => throw new NotImplementedException();
    public Task<List<AperturaCaja>> ObtenerAperturasAbiertasAsync() => throw new NotImplementedException();
    public Task<bool> TieneCajaAbiertaAsync(int cajaId) => throw new NotImplementedException();
    public Task<bool> ExisteAlgunaCajaAbiertaAsync() => throw new NotImplementedException();
    public Task<MovimientoCaja> RegistrarMovimientoAsync(MovimientoCajaViewModel model, string usuario) => throw new NotImplementedException();
    public Task<List<MovimientoCaja>> ObtenerMovimientosDeAperturaAsync(int aperturaId) => throw new NotImplementedException();
    public Task<decimal> CalcularSaldoActualAsync(int aperturaId) => throw new NotImplementedException();
    public Task<decimal> CalcularSaldoRealAsync(int aperturaId) => throw new NotImplementedException();
    public Task<MovimientoCaja> AcreditarMovimientoAsync(int movimientoId, string usuario) => throw new NotImplementedException();
    public Task<AperturaCaja?> ObtenerAperturaActivaParaVentaAsync() => throw new NotImplementedException();
    public Task<MovimientoCaja?> RegistrarMovimientoCuotaAsync(int cuotaId, string creditoNumero, int numeroCuota, decimal monto, string medioPago, string usuario) => throw new NotImplementedException();
    public Task<MovimientoCaja> RegistrarMovimientoDevolucionAsync(int devolucionId, int ventaId, string ventaNumero, string devolucionNumero, decimal monto, string usuario) => throw new NotImplementedException();
    public Task<CierreCaja> CerrarCajaAsync(CerrarCajaViewModel model, string usuario) => throw new NotImplementedException();
    public Task<CierreCaja?> ObtenerCierrePorIdAsync(int id) => throw new NotImplementedException();
    public Task<List<CierreCaja>> ObtenerHistorialCierresAsync(int? cajaId = null, DateTime? fechaDesde = null, DateTime? fechaHasta = null) => throw new NotImplementedException();
    public Task<DetallesAperturaViewModel> ObtenerDetallesAperturaAsync(int aperturaId) => throw new NotImplementedException();
    public Task<ReporteCajaViewModel> GenerarReporteCajaAsync(DateTime fechaDesde, DateTime fechaHasta, int? cajaId = null) => throw new NotImplementedException();
    public Task<HistorialCierresViewModel> ObtenerEstadisticasCierresAsync(int? cajaId = null, DateTime? fechaDesde = null, DateTime? fechaHasta = null) => throw new NotImplementedException();
}

file sealed class StubAlertaStockE2ERep : IAlertaStockService
{
    public Task<int> VerificarYGenerarAlertasAsync(IEnumerable<int> productoIds) => Task.FromResult(0);
    public Task<int> GenerarAlertasStockBajoAsync(CancellationToken ct = default) => Task.FromResult(0);
    public Task<List<AlertaStock>> GetAlertasPendientesAsync() => throw new NotImplementedException();
    public Task<PaginatedResult<AlertaStockViewModel>> BuscarAsync(AlertaStockFiltroViewModel filtro) => throw new NotImplementedException();
    public Task<AlertaStockViewModel?> GetByIdAsync(int id) => throw new NotImplementedException();
    public Task<bool> ResolverAlertaAsync(int id, string usuarioResolucion, string? observaciones = null, byte[]? rowVersion = null) => throw new NotImplementedException();
    public Task<bool> IgnorarAlertaAsync(int id, string usuarioResolucion, string? observaciones = null, byte[]? rowVersion = null) => throw new NotImplementedException();
    public Task<AlertaStockEstadisticasViewModel> GetEstadisticasAsync() => throw new NotImplementedException();
    public Task<List<AlertaStock>> GetAlertasByProductoIdAsync(int productoId) => throw new NotImplementedException();
    public Task<AlertaStock?> VerificarYGenerarAlertaAsync(int productoId) => throw new NotImplementedException();
    public Task<int> LimpiarAlertasAntiguasAsync(int diasAntiguedad = 30, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<List<ProductoCriticoViewModel>> GetProductosCriticosAsync() => throw new NotImplementedException();
}

file sealed class StubCreditoDisponibleE2ERep : ICreditoDisponibleService
{
    public Task<decimal> ObtenerLimitePorPuntajeAsync(NivelRiesgoCredito puntaje, CancellationToken cancellationToken = default)
        => Task.FromResult(0m);
    public Task<decimal> CalcularSaldoVigenteAsync(int clienteId, CancellationToken cancellationToken = default)
        => Task.FromResult(0m);
    public Task<CreditoDisponibleResultado> CalcularDisponibleAsync(int clienteId, CancellationToken cancellationToken = default)
        => Task.FromResult(new CreditoDisponibleResultado { Limite = 0m, Disponible = 999_999m });
    public Task<(bool Ok, List<string> Errores)> GuardarLimitesPorPuntajeAsync(IReadOnlyList<(NivelRiesgoCredito Puntaje, decimal LimiteMonto, bool Activo)> items, string usuario) => throw new NotImplementedException();
    public Task<List<PuntajeCreditoLimite>> GetAllLimitesPorPuntajeAsync() => throw new NotImplementedException();
}

file sealed class StubCurrentUserE2ERep : ICurrentUserService
{
    public string GetUsername() => "e2e-rep-user";
    public string GetUserId() => "system";
    public bool IsAuthenticated() => true;
    public string? GetEmail() => null;
    public bool IsInRole(string role) => false;
    public bool HasPermission(string modulo, string accion) => false;
    public string? GetIpAddress() => null;
}

file sealed class StubValidacionVentaE2ERep : IValidacionVentaService
{
    public Task<ValidacionVentaResult> ValidarVentaCreditoPersonalAsync(int clienteId, decimal montoVenta, int? creditoId = null)
        => Task.FromResult(new ValidacionVentaResult { NoViable = false });
    public Task<PrevalidacionResultViewModel> PrevalidarAsync(int clienteId, decimal monto) => throw new NotImplementedException();
    public Task<ValidacionVentaResult> ValidarConfirmacionVentaAsync(int ventaId) => throw new NotImplementedException();
    public Task<bool> ClientePuedeRecibirCreditoAsync(int clienteId, decimal montoSolicitado) => throw new NotImplementedException();
    public Task<ResumenCrediticioClienteViewModel> ObtenerResumenCrediticioAsync(int clienteId) => throw new NotImplementedException();
}

// ---------------------------------------------------------------------------

/// <summary>
/// Tests E2E Fase 10.8: ciclo completo devolución → reparación → finalización con unidad física.
/// Encadena VentaService + DevolucionService + ProductoUnidadService en el mismo contexto SQLite.
/// Valida: historial completo, invariante de stock agregado, ausencia de movimientos de caja
/// en reparación/finalización, y que no es posible finalizar una reparación dos veces.
/// </summary>
public class ProductoUnidadReparacionE2ETests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly VentaService _ventaService;
    private readonly DevolucionService _devolucionService;
    private readonly ProductoUnidadService _unidadService;

    private static string G() => Guid.NewGuid().ToString("N")[..8];

    public ProductoUnidadReparacionE2ETests()
    {
        _connection = new SqliteConnection($"DataSource={G()};Mode=Memory;Cache=Shared");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        _unidadService = new ProductoUnidadService(_context, NullLogger<ProductoUnidadService>.Instance);

        var movimientoStockReal = new MovimientoStockService(
            _context, NullLogger<MovimientoStockService>.Instance);

        var mapper = new MapperConfiguration(
                cfg => cfg.AddProfile<MappingProfile>(),
                NullLoggerFactory.Instance)
            .CreateMapper();

        var apertura = SeedCajaSync();

        _ventaService = new VentaService(
            _context,
            mapper,
            NullLogger<VentaService>.Instance,
            new StubAlertaStockE2ERep(),
            new StubMovimientoStockEfectivo(),
            new FinancialCalculationService(),
            new VentaValidator(),
            new VentaNumberGenerator(_context, NullLogger<VentaNumberGenerator>.Instance),
            new PrecioVigenteResolver(_context),
            new StubCurrentUserE2ERep(),
            new StubValidacionVentaE2ERep(),
            new StubCajaE2ERep(apertura),
            new StubCreditoDisponibleE2ERep(),
            new StubContratoVentaCreditoService(),
            new StubConfiguracionPagoServiceVenta(),
            productoUnidadService: _unidadService);

        _devolucionService = new DevolucionService(
            _context,
            movimientoStockReal,
            new StubCurrentUserE2ERep(),
            NullLogger<DevolucionService>.Instance);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private AperturaCaja SeedCajaSync()
    {
        var caja = new Caja
        {
            Codigo = "CE2E" + G()[..4],
            Nombre = "Caja E2E Reparacion",
            IsDeleted = false,
            RowVersion = new byte[8]
        };
        _context.Cajas.Add(caja);
        _context.SaveChanges();

        var apertura = new AperturaCaja
        {
            CajaId = caja.Id,
            UsuarioApertura = "e2e-rep-user",
            MontoInicial = 0m,
            Cerrada = false,
            IsDeleted = false,
            RowVersion = new byte[8]
        };
        _context.AperturasCaja.Add(apertura);
        _context.SaveChanges();
        return apertura;
    }

    private async Task<(Producto producto, Cliente cliente)> SeedBaseAsync(
        int stock = 10,
        bool requiereNumeroSerie = true)
    {
        var g = G();
        var cat = new Categoria { Codigo = $"C{g}", Nombre = $"Cat{g}", IsDeleted = false };
        var marca = new Marca { Codigo = $"M{g}", Nombre = $"Mar{g}", IsDeleted = false };
        _context.Categorias.Add(cat);
        _context.Marcas.Add(marca);
        await _context.SaveChangesAsync();

        var cliente = new Cliente
        {
            Nombre = "Test",
            Apellido = $"T{g}",
            NumeroDocumento = g,
            NivelRiesgo = NivelRiesgoCredito.AprobadoTotal,
            IsDeleted = false
        };
        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync();

        var producto = new Producto
        {
            Codigo = $"E2E{g}",
            Nombre = $"ProdE2E{g}",
            CategoriaId = cat.Id,
            MarcaId = marca.Id,
            PrecioVenta = 500m,
            PrecioCompra = 100m,
            PorcentajeIVA = 21m,
            StockActual = stock,
            RequiereNumeroSerie = requiereNumeroSerie,
            IsDeleted = false
        };
        _context.Productos.Add(producto);
        await _context.SaveChangesAsync();

        return (producto, cliente);
    }

    /// <summary>Crea una venta en estado Presupuesto con el detalle apuntando a la unidad dada.</summary>
    private async Task<Venta> SeedVentaPresupuestoAsync(
        Cliente cliente,
        Producto producto,
        ProductoUnidad unidad)
    {
        var venta = new Venta
        {
            ClienteId = cliente.Id,
            Numero = $"VE2E{G()}",
            Estado = EstadoVenta.Presupuesto,
            TipoPago = TipoPago.Efectivo,
            Total = 500m,
            IsDeleted = false
        };
        _context.Ventas.Add(venta);
        await _context.SaveChangesAsync();

        _context.VentaDetalles.Add(new VentaDetalle
        {
            VentaId = venta.Id,
            ProductoId = producto.Id,
            Cantidad = 1,
            PrecioUnitario = 500m,
            Subtotal = 500m,
            CostoUnitarioAlMomento = 100m,
            CostoTotalAlMomento = 100m,
            ProductoUnidadId = unidad.Id,
            IsDeleted = false
        });
        await _context.SaveChangesAsync();

        return venta;
    }

    /// <summary>Crea una devolución ya Aprobada con un detalle que apunta a la unidad.</summary>
    private async Task<Devolucion> SeedDevolucionAprobadaAsync(
        int ventaId,
        int clienteId,
        int productoId,
        int productoUnidadId,
        AccionProducto accion = AccionProducto.Reparacion)
    {
        var dev = new Devolucion
        {
            NumeroDevolucion = $"DEV-{G()}",
            VentaId = ventaId,
            ClienteId = clienteId,
            Motivo = MotivoDevolucion.DefectoFabrica,
            Descripcion = "E2E test reparacion",
            Estado = EstadoDevolucion.Aprobada,
            TipoResolucion = TipoResolucionDevolucion.CambioMismoProducto,
            FechaDevolucion = DateTime.UtcNow,
            TotalDevolucion = 500m
        };
        _context.Devoluciones.Add(dev);
        await _context.SaveChangesAsync();
        await _context.Entry(dev).ReloadAsync();

        _context.DevolucionDetalles.Add(new DevolucionDetalle
        {
            DevolucionId = dev.Id,
            ProductoId = productoId,
            ProductoUnidadId = productoUnidadId,
            Cantidad = 1,
            PrecioUnitario = 500m,
            Subtotal = 500m,
            AccionRecomendada = accion
        });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        return await _context.Devoluciones
            .AsNoTracking()
            .Include(d => d.Detalles)
            .FirstAsync(d => d.Id == dev.Id);
    }

    /// <summary>Venta seeded directamente en Entregada con unidad en estado Vendida (sin pasar por VentaService).</summary>
    private async Task<(Venta venta, ProductoUnidad unidad)> SeedVentaEntregadaConUnidadVendidaAsync(
        Cliente cliente,
        Producto producto)
    {
        var unidad = await _unidadService.CrearUnidadAsync(producto.Id, $"SN-{G()}");

        var venta = new Venta
        {
            ClienteId = cliente.Id,
            Numero = $"VLG{G()}",
            Estado = EstadoVenta.Entregada,
            TipoPago = TipoPago.Efectivo,
            FechaVenta = DateTime.UtcNow,
            Detalles = new List<VentaDetalle>
            {
                new()
                {
                    ProductoId = producto.Id,
                    Cantidad = 1,
                    PrecioUnitario = 500m,
                    Descuento = 0m,
                    Subtotal = 500m,
                    CostoUnitarioAlMomento = 100m,
                    CostoTotalAlMomento = 100m,
                    ProductoUnidadId = unidad.Id
                }
            }
        };
        _context.Ventas.Add(venta);
        await _context.SaveChangesAsync();
        await _context.Entry(venta).ReloadAsync();

        unidad.Estado = EstadoUnidad.Vendida;
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        return (venta, unidad);
    }

    // -------------------------------------------------------------------------
    // Fase 10.8 — A/B/C: Ciclo E2E completo (VentaService → DevolucionService → FinalizarReparacion)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task E2E_CicloCompleto_Reparacion_FinalizarAEnStock()
    {
        var (producto, cliente) = await SeedBaseAsync(stock: 5);

        // 1. Crear unidad EnStock vía ProductoUnidadService
        var unidad = await _unidadService.CrearUnidadAsync(producto.Id, "SN-E2E-ENSTOCK-001");
        Assert.Equal(EstadoUnidad.EnStock, unidad.Estado);

        // 2. Crear venta en Presupuesto
        var venta = await SeedVentaPresupuestoAsync(cliente, producto, unidad);

        var stockInicial = (await _context.Productos.AsNoTracking().FirstAsync(p => p.Id == producto.Id)).StockActual;

        // 3. Confirmar venta vía VentaService → unidad queda Vendida
        await _ventaService.ConfirmarVentaAsync(venta.Id);

        _context.ChangeTracker.Clear();
        var unidadTrasVenta = await _context.ProductoUnidades.AsNoTracking().FirstAsync(u => u.Id == unidad.Id);
        Assert.Equal(EstadoUnidad.Vendida, unidadTrasVenta.Estado);

        // 4. Crear devolución aprobada con acción Reparacion
        var dev = await SeedDevolucionAprobadaAsync(venta.Id, cliente.Id, producto.Id, unidad.Id, AccionProducto.Reparacion);

        // 5. Completar devolución → unidad queda EnReparacion
        await _devolucionService.CompletarDevolucionAsync(dev.Id, dev.RowVersion);

        _context.ChangeTracker.Clear();
        var unidadTrasDevolucion = await _context.ProductoUnidades.AsNoTracking().FirstAsync(u => u.Id == unidad.Id);
        Assert.Equal(EstadoUnidad.EnReparacion, unidadTrasDevolucion.Estado);

        var stockTrasDev = (await _context.Productos.AsNoTracking().FirstAsync(p => p.Id == producto.Id)).StockActual;

        // 6. Finalizar reparación → EnStock
        await _unidadService.FinalizarReparacionAsync(unidad.Id, EstadoUnidad.EnStock, "Reparada OK", "operador-e2e");

        _context.ChangeTracker.Clear();
        var unidadFinal = await _context.ProductoUnidades.AsNoTracking().FirstAsync(u => u.Id == unidad.Id);
        Assert.Equal(EstadoUnidad.EnStock, unidadFinal.Estado);

        // 7. Stock agregado no cambia por reparacion ni por finalización
        var stockFinal = (await _context.Productos.AsNoTracking().FirstAsync(p => p.Id == producto.Id)).StockActual;
        Assert.Equal(stockTrasDev, stockFinal);

        // 8. Historial: Vendida → EnReparacion y EnReparacion → EnStock existen
        var historial = await _context.ProductoUnidadMovimientos
            .AsNoTracking()
            .Where(m => m.ProductoUnidadId == unidad.Id)
            .OrderBy(m => m.FechaCambio)
            .ToListAsync();

        var movReparacion = historial.FirstOrDefault(m =>
            m.EstadoAnterior == EstadoUnidad.Vendida && m.EstadoNuevo == EstadoUnidad.EnReparacion);
        Assert.NotNull(movReparacion);

        var movFinalizacion = historial.FirstOrDefault(m =>
            m.EstadoAnterior == EstadoUnidad.EnReparacion && m.EstadoNuevo == EstadoUnidad.EnStock);
        Assert.NotNull(movFinalizacion);

        Assert.True(movReparacion!.FechaCambio <= movFinalizacion!.FechaCambio);

        // 9. Ningún MovimientoStock generado por reparacion o finalización
        var movimientosStock = await _context.MovimientosStock
            .AsNoTracking()
            .Where(m => m.ProductoId == producto.Id)
            .ToListAsync();
        Assert.Empty(movimientosStock);

        _ = stockInicial; // referencia intencional: stock inicial medido para contexto
    }

    [Fact]
    public async Task E2E_CicloCompleto_Reparacion_FinalizarABaja()
    {
        var (producto, cliente) = await SeedBaseAsync(stock: 5);
        var unidad = await _unidadService.CrearUnidadAsync(producto.Id, "SN-E2E-BAJA-001");

        var venta = await SeedVentaPresupuestoAsync(cliente, producto, unidad);
        await _ventaService.ConfirmarVentaAsync(venta.Id);

        var dev = await SeedDevolucionAprobadaAsync(venta.Id, cliente.Id, producto.Id, unidad.Id, AccionProducto.Reparacion);
        await _devolucionService.CompletarDevolucionAsync(dev.Id, dev.RowVersion);

        _context.ChangeTracker.Clear();
        var unidadEnRep = await _context.ProductoUnidades.AsNoTracking().FirstAsync(u => u.Id == unidad.Id);
        Assert.Equal(EstadoUnidad.EnReparacion, unidadEnRep.Estado);

        var stockAntes = (await _context.Productos.AsNoTracking().FirstAsync(p => p.Id == producto.Id)).StockActual;

        // Finalizar → Baja (no reparable)
        await _unidadService.FinalizarReparacionAsync(unidad.Id, EstadoUnidad.Baja, "No reparable", "operador-e2e");

        _context.ChangeTracker.Clear();
        var unidadFinal = await _context.ProductoUnidades.AsNoTracking().FirstAsync(u => u.Id == unidad.Id);
        Assert.Equal(EstadoUnidad.Baja, unidadFinal.Estado);

        // Stock no cambia por finalización a Baja
        var stockDespues = (await _context.Productos.AsNoTracking().FirstAsync(p => p.Id == producto.Id)).StockActual;
        Assert.Equal(stockAntes, stockDespues);

        // Historial: Vendida → EnReparacion y EnReparacion → Baja
        var historial = await _context.ProductoUnidadMovimientos
            .AsNoTracking()
            .Where(m => m.ProductoUnidadId == unidad.Id)
            .OrderBy(m => m.FechaCambio)
            .ToListAsync();

        Assert.NotNull(historial.FirstOrDefault(m =>
            m.EstadoAnterior == EstadoUnidad.Vendida && m.EstadoNuevo == EstadoUnidad.EnReparacion));
        Assert.NotNull(historial.FirstOrDefault(m =>
            m.EstadoAnterior == EstadoUnidad.EnReparacion && m.EstadoNuevo == EstadoUnidad.Baja));

        // No MovimientoStock por finalización
        var movStock = await _context.MovimientosStock.AsNoTracking()
            .Where(m => m.ProductoId == producto.Id).ToListAsync();
        Assert.Empty(movStock);
    }

    [Fact]
    public async Task E2E_CicloCompleto_Reparacion_FinalizarADevuelta()
    {
        var (producto, cliente) = await SeedBaseAsync(stock: 5);
        var unidad = await _unidadService.CrearUnidadAsync(producto.Id, "SN-E2E-DEVUELTA-001");

        var venta = await SeedVentaPresupuestoAsync(cliente, producto, unidad);
        await _ventaService.ConfirmarVentaAsync(venta.Id);

        var dev = await SeedDevolucionAprobadaAsync(venta.Id, cliente.Id, producto.Id, unidad.Id, AccionProducto.Reparacion);
        await _devolucionService.CompletarDevolucionAsync(dev.Id, dev.RowVersion);

        var stockAntes = (await _context.Productos.AsNoTracking().FirstAsync(p => p.Id == producto.Id)).StockActual;

        // Finalizar → Devuelta (pendiente revisión externa)
        await _unidadService.FinalizarReparacionAsync(unidad.Id, EstadoUnidad.Devuelta, "Pendiente revision externa", "operador-e2e");

        _context.ChangeTracker.Clear();
        var unidadFinal = await _context.ProductoUnidades.AsNoTracking().FirstAsync(u => u.Id == unidad.Id);
        Assert.Equal(EstadoUnidad.Devuelta, unidadFinal.Estado);

        var stockDespues = (await _context.Productos.AsNoTracking().FirstAsync(p => p.Id == producto.Id)).StockActual;
        Assert.Equal(stockAntes, stockDespues);

        var historial = await _context.ProductoUnidadMovimientos
            .AsNoTracking()
            .Where(m => m.ProductoUnidadId == unidad.Id)
            .OrderBy(m => m.FechaCambio)
            .ToListAsync();

        Assert.NotNull(historial.FirstOrDefault(m =>
            m.EstadoAnterior == EstadoUnidad.Vendida && m.EstadoNuevo == EstadoUnidad.EnReparacion));
        Assert.NotNull(historial.FirstOrDefault(m =>
            m.EstadoAnterior == EstadoUnidad.EnReparacion && m.EstadoNuevo == EstadoUnidad.Devuelta));
    }

    // -------------------------------------------------------------------------
    // Fase 10.8 — D: Historial completo con todos los movimientos ordenados
    // -------------------------------------------------------------------------

    [Fact]
    public async Task E2E_HistorialContieneTodosLosMovimientosOrdenados()
    {
        var (producto, cliente) = await SeedBaseAsync(stock: 5);
        var unidad = await _unidadService.CrearUnidadAsync(producto.Id, "SN-E2E-HIST-001");

        var venta = await SeedVentaPresupuestoAsync(cliente, producto, unidad);
        await _ventaService.ConfirmarVentaAsync(venta.Id);

        var dev = await SeedDevolucionAprobadaAsync(venta.Id, cliente.Id, producto.Id, unidad.Id, AccionProducto.Reparacion);
        await _devolucionService.CompletarDevolucionAsync(dev.Id, dev.RowVersion);
        await _unidadService.FinalizarReparacionAsync(unidad.Id, EstadoUnidad.EnStock, "Reparada", "op-hist");

        _context.ChangeTracker.Clear();
        var historial = await _context.ProductoUnidadMovimientos
            .AsNoTracking()
            .Where(m => m.ProductoUnidadId == unidad.Id)
            .OrderBy(m => m.FechaCambio)
            .ToListAsync();

        // Debe haber al menos 3 movimientos: alta/vendida, EnReparacion, finalización
        // (el de alta puede ser EnStock→EnStock, el de venta es EnStock→Vendida)
        Assert.True(historial.Count >= 3, $"Se esperaban al menos 3 movimientos, se encontraron {historial.Count}");

        // Movimiento de reparación debe existir y preceder al de finalización
        var idxRep = historial.FindIndex(m =>
            m.EstadoAnterior == EstadoUnidad.Vendida && m.EstadoNuevo == EstadoUnidad.EnReparacion);
        var idxFin = historial.FindIndex(m =>
            m.EstadoAnterior == EstadoUnidad.EnReparacion && m.EstadoNuevo == EstadoUnidad.EnStock);

        Assert.True(idxRep >= 0, "Falta movimiento Vendida → EnReparacion");
        Assert.True(idxFin >= 0, "Falta movimiento EnReparacion → EnStock");
        Assert.True(idxRep < idxFin, "El movimiento de reparación debe preceder al de finalización");
    }

    // -------------------------------------------------------------------------
    // Fase 10.8 — E: Stock agregado no cambia en ningún paso del ciclo
    // -------------------------------------------------------------------------

    [Fact]
    public async Task E2E_StockAgregadoNoCambiaEnNingunPasoDelCicloReparacion()
    {
        var (producto, cliente) = await SeedBaseAsync(stock: 8);
        var unidad = await _unidadService.CrearUnidadAsync(producto.Id, "SN-E2E-STOCK-001");
        var venta = await SeedVentaPresupuestoAsync(cliente, producto, unidad);

        var stockInicial = (await _context.Productos.AsNoTracking().FirstAsync(p => p.Id == producto.Id)).StockActual;

        await _ventaService.ConfirmarVentaAsync(venta.Id);
        // StubMovimientoStockEfectivo no modifica StockActual → StockActual no cambia en tests

        var dev = await SeedDevolucionAprobadaAsync(venta.Id, cliente.Id, producto.Id, unidad.Id, AccionProducto.Reparacion);
        await _devolucionService.CompletarDevolucionAsync(dev.Id, dev.RowVersion);

        _context.ChangeTracker.Clear();
        var stockTrasReparacion = (await _context.Productos.AsNoTracking().FirstAsync(p => p.Id == producto.Id)).StockActual;
        Assert.Equal(stockInicial, stockTrasReparacion);

        await _unidadService.FinalizarReparacionAsync(unidad.Id, EstadoUnidad.EnStock, "Reparada", "op");

        _context.ChangeTracker.Clear();
        var stockTrasFinalizacion = (await _context.Productos.AsNoTracking().FirstAsync(p => p.Id == producto.Id)).StockActual;
        Assert.Equal(stockInicial, stockTrasFinalizacion);
    }

    // -------------------------------------------------------------------------
    // Fase 10.8 — F: No se puede finalizar reparación dos veces
    // -------------------------------------------------------------------------

    [Fact]
    public async Task E2E_FinalizarReparacion_SegundoIntento_Falla()
    {
        var (producto, cliente) = await SeedBaseAsync(stock: 5);
        var unidad = await _unidadService.CrearUnidadAsync(producto.Id, "SN-E2E-DOBLE-001");

        var venta = await SeedVentaPresupuestoAsync(cliente, producto, unidad);
        await _ventaService.ConfirmarVentaAsync(venta.Id);

        var dev = await SeedDevolucionAprobadaAsync(venta.Id, cliente.Id, producto.Id, unidad.Id, AccionProducto.Reparacion);
        await _devolucionService.CompletarDevolucionAsync(dev.Id, dev.RowVersion);

        // Primera finalización: ok
        await _unidadService.FinalizarReparacionAsync(unidad.Id, EstadoUnidad.EnStock, "Primer intento", "op");

        // Segunda finalización: debe fallar porque la unidad ya no está EnReparacion
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _unidadService.FinalizarReparacionAsync(unidad.Id, EstadoUnidad.EnStock, "Segundo intento", "op"));
    }

    // -------------------------------------------------------------------------
    // Fase 10.8 — G: No MovimientoCaja en devolución con Reparacion ni en finalización
    // -------------------------------------------------------------------------

    [Fact]
    public async Task E2E_NoCajaRegistradaEnReparacionNiEnFinalizacion()
    {
        var (producto, cliente) = await SeedBaseAsync(stock: 5);
        var unidad = await _unidadService.CrearUnidadAsync(producto.Id, "SN-E2E-CAJA-001");
        var venta = await SeedVentaPresupuestoAsync(cliente, producto, unidad);
        await _ventaService.ConfirmarVentaAsync(venta.Id);

        var movCajaAntes = await _context.MovimientosCaja.AsNoTracking().CountAsync();

        var dev = await SeedDevolucionAprobadaAsync(venta.Id, cliente.Id, producto.Id, unidad.Id, AccionProducto.Reparacion);
        await _devolucionService.CompletarDevolucionAsync(dev.Id, dev.RowVersion);
        await _unidadService.FinalizarReparacionAsync(unidad.Id, EstadoUnidad.EnStock, "Reparada", "op");

        _context.ChangeTracker.Clear();
        var movCajaDespues = await _context.MovimientosCaja.AsNoTracking().CountAsync();

        // DevolucionService con cajaService = null no registra caja
        // FinalizarReparacion no tiene concepto de caja
        Assert.Equal(movCajaAntes, movCajaDespues);
    }

    // -------------------------------------------------------------------------
    // Fase 10.8 — H: Regresión acciones previas (D, E, F, G del scope)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task E2E_Regresion_DevolucionReintegrarStock_ConservaBehaviorExistente()
    {
        var (producto, cliente) = await SeedBaseAsync(stock: 10, requiereNumeroSerie: false);
        var (venta, unidad) = await SeedVentaEntregadaConUnidadVendidaAsync(cliente, producto);

        var stockAntes = (await _context.Productos.AsNoTracking().FirstAsync(p => p.Id == producto.Id)).StockActual;

        var dev = await SeedDevolucionAprobadaAsync(venta.Id, cliente.Id, producto.Id, unidad.Id, AccionProducto.ReintegrarStock);
        await _devolucionService.CompletarDevolucionAsync(dev.Id, dev.RowVersion);

        _context.ChangeTracker.Clear();
        var unidadFinal = await _context.ProductoUnidades.AsNoTracking().FirstAsync(u => u.Id == unidad.Id);
        Assert.Equal(EstadoUnidad.Devuelta, unidadFinal.Estado);

        // Stock agregado sube (MovimientoStockService real registra entrada)
        var stockDespues = (await _context.Productos.AsNoTracking().FirstAsync(p => p.Id == producto.Id)).StockActual;
        Assert.True(stockDespues > stockAntes, "ReintegrarStock debe incrementar el stock agregado");

        // Movimiento unidad existe
        var movUnidad = await _context.ProductoUnidadMovimientos
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.ProductoUnidadId == unidad.Id && m.EstadoNuevo == EstadoUnidad.Devuelta);
        Assert.NotNull(movUnidad);
    }

    [Fact]
    public async Task E2E_Regresion_DevolucionDescarte_MarcaBajaYNoGeneraStock()
    {
        var (producto, cliente) = await SeedBaseAsync(stock: 10, requiereNumeroSerie: false);
        var (venta, unidad) = await SeedVentaEntregadaConUnidadVendidaAsync(cliente, producto);

        var stockAntes = (await _context.Productos.AsNoTracking().FirstAsync(p => p.Id == producto.Id)).StockActual;

        var dev = await SeedDevolucionAprobadaAsync(venta.Id, cliente.Id, producto.Id, unidad.Id, AccionProducto.Descarte);
        await _devolucionService.CompletarDevolucionAsync(dev.Id, dev.RowVersion);

        _context.ChangeTracker.Clear();
        var unidadFinal = await _context.ProductoUnidades.AsNoTracking().FirstAsync(u => u.Id == unidad.Id);
        Assert.Equal(EstadoUnidad.Baja, unidadFinal.Estado);

        var stockDespues = (await _context.Productos.AsNoTracking().FirstAsync(p => p.Id == producto.Id)).StockActual;
        Assert.Equal(stockAntes, stockDespues);

        var movUnidad = await _context.ProductoUnidadMovimientos
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.ProductoUnidadId == unidad.Id && m.EstadoNuevo == EstadoUnidad.Baja);
        Assert.NotNull(movUnidad);
    }

    [Fact]
    public async Task E2E_Regresion_DevolucionDevolverProveedor_MarcaDevueltaYNoGeneraStock()
    {
        var (producto, cliente) = await SeedBaseAsync(stock: 10, requiereNumeroSerie: false);
        var (venta, unidad) = await SeedVentaEntregadaConUnidadVendidaAsync(cliente, producto);

        var stockAntes = (await _context.Productos.AsNoTracking().FirstAsync(p => p.Id == producto.Id)).StockActual;

        var dev = await SeedDevolucionAprobadaAsync(venta.Id, cliente.Id, producto.Id, unidad.Id, AccionProducto.DevolverProveedor);
        await _devolucionService.CompletarDevolucionAsync(dev.Id, dev.RowVersion);

        _context.ChangeTracker.Clear();
        var unidadFinal = await _context.ProductoUnidades.AsNoTracking().FirstAsync(u => u.Id == unidad.Id);
        Assert.Equal(EstadoUnidad.Devuelta, unidadFinal.Estado);

        var stockDespues = (await _context.Productos.AsNoTracking().FirstAsync(p => p.Id == producto.Id)).StockActual;
        Assert.Equal(stockAntes, stockDespues);

        var movUnidad = await _context.ProductoUnidadMovimientos
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.ProductoUnidadId == unidad.Id && m.EstadoNuevo == EstadoUnidad.Devuelta);
        Assert.NotNull(movUnidad);
    }

    [Fact]
    public async Task E2E_Regresion_DevolucionSinUnidad_Legacy_NoCreaMovimientoUnidad()
    {
        var (producto, cliente) = await SeedBaseAsync(stock: 10, requiereNumeroSerie: false);

        var venta = new Venta
        {
            ClienteId = cliente.Id,
            Numero = $"VLG{G()}",
            Estado = EstadoVenta.Entregada,
            TipoPago = TipoPago.Efectivo,
            FechaVenta = DateTime.UtcNow,
            Detalles = new List<VentaDetalle>
            {
                new()
                {
                    ProductoId = producto.Id,
                    Cantidad = 2,
                    PrecioUnitario = 500m,
                    Descuento = 0m,
                    Subtotal = 1000m,
                    CostoUnitarioAlMomento = 100m,
                    CostoTotalAlMomento = 200m
                }
            }
        };
        _context.Ventas.Add(venta);
        await _context.SaveChangesAsync();
        await _context.Entry(venta).ReloadAsync();

        var dev = new Devolucion
        {
            NumeroDevolucion = $"DEV-{G()}",
            VentaId = venta.Id,
            ClienteId = cliente.Id,
            Motivo = MotivoDevolucion.DefectoFabrica,
            Descripcion = "Legacy sin unidad",
            Estado = EstadoDevolucion.Aprobada,
            TipoResolucion = TipoResolucionDevolucion.CambioMismoProducto,
            FechaDevolucion = DateTime.UtcNow,
            TotalDevolucion = 500m
        };
        _context.Devoluciones.Add(dev);
        await _context.SaveChangesAsync();
        await _context.Entry(dev).ReloadAsync();

        _context.DevolucionDetalles.Add(new DevolucionDetalle
        {
            DevolucionId = dev.Id,
            ProductoId = producto.Id,
            Cantidad = 1,
            PrecioUnitario = 500m,
            Subtotal = 500m,
            AccionRecomendada = AccionProducto.Reparacion
            // Sin ProductoUnidadId
        });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var devReloaded = await _context.Devoluciones
            .AsNoTracking()
            .Include(d => d.Detalles)
            .FirstAsync(d => d.Id == dev.Id);

        var resultado = await _devolucionService.CompletarDevolucionAsync(devReloaded.Id, devReloaded.RowVersion);

        Assert.Equal(EstadoDevolucion.Completada, resultado.Estado);

        _context.ChangeTracker.Clear();
        var movimientos = await _context.ProductoUnidadMovimientos.AsNoTracking().ToListAsync();
        Assert.Empty(movimientos);
    }
}
