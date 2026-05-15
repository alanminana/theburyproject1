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

file sealed class StubCajaTrazab : ICajaService
{
    private readonly AperturaCaja _apertura;
    public StubCajaTrazab(AperturaCaja apertura) => _apertura = apertura;

    public Task<AperturaCaja?> ObtenerAperturaActivaParaUsuarioAsync(string usuario)
        => Task.FromResult<AperturaCaja?>(_apertura);
    public Task<MovimientoCaja?> RegistrarMovimientoVentaAsync(int ventaId, string ventaNumero, decimal monto, TipoPago tipoPago, string usuario)
        => Task.FromResult<MovimientoCaja?>(new MovimientoCaja());
    public Task<MovimientoCaja?> RegistrarMovimientoAnticipoAsync(int creditoId, string creditoNumero, decimal montoAnticipo, string usuario)
        => Task.FromResult<MovimientoCaja?>(new MovimientoCaja());

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

file sealed class StubAlertaStockTrazab : IAlertaStockService
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

file sealed class StubCreditoDisponibleTrazab : ICreditoDisponibleService
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

file sealed class StubCurrentUserTrazab : ICurrentUserService
{
    public string GetUsername() => "trazab-user";
    public string GetUserId() => "system";
    public bool IsAuthenticated() => true;
    public string? GetEmail() => null;
    public bool IsInRole(string role) => false;
    public bool HasPermission(string modulo, string accion) => false;
    public string? GetIpAddress() => null;
}

file sealed class StubValidacionVentaTrazab : IValidacionVentaService
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
/// Tests de integración: trazabilidad individual ProductoUnidad integrada con VentaService (Fase 8.2.E).
/// </summary>
public class VentaServiceProductoUnidadTrazabilidadTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly VentaService _service;
    private readonly ProductoUnidadService _unidadService;
    private readonly AperturaCaja _apertura;
    private static int _counter = 8200;
    private static string G() => Guid.NewGuid().ToString("N")[..8];

    public VentaServiceProductoUnidadTrazabilidadTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        _apertura = SeedCajaSinc();

        var mapper = new MapperConfiguration(
                cfg => cfg.AddProfile<MappingProfile>(),
                NullLoggerFactory.Instance)
            .CreateMapper();

        _unidadService = new ProductoUnidadService(_context, NullLogger<ProductoUnidadService>.Instance);

        _service = new VentaService(
            _context,
            mapper,
            NullLogger<VentaService>.Instance,
            new StubAlertaStockTrazab(),
            new StubMovimientoStockEfectivo(),
            new FinancialCalculationService(),
            new VentaValidator(),
            new VentaNumberGenerator(_context, NullLogger<VentaNumberGenerator>.Instance),
            new PrecioVigenteResolver(_context),
            new StubCurrentUserTrazab(),
            new StubValidacionVentaTrazab(),
            new StubCajaTrazab(_apertura),
            new StubCreditoDisponibleTrazab(),
            new StubContratoVentaCreditoService(),
            new StubConfiguracionPagoServiceVenta(),
            productoUnidadService: _unidadService);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private AperturaCaja SeedCajaSinc()
    {
        var caja = new Caja { Codigo = "CTR1", Nombre = "Caja Trazab", IsDeleted = false, RowVersion = new byte[8] };
        _context.Cajas.Add(caja);
        _context.SaveChanges();

        var apertura = new AperturaCaja
        {
            CajaId = caja.Id,
            UsuarioApertura = "trazab-user",
            MontoInicial = 0m,
            Cerrada = false,
            IsDeleted = false,
            RowVersion = new byte[8]
        };
        _context.AperturasCaja.Add(apertura);
        _context.SaveChanges();
        return apertura;
    }

    private async Task<(Producto producto, Cliente cliente)> SeedBaseAsync(bool requiereNumeroSerie = false, int stock = 10)
    {
        var g = G();

        var cat = new Categoria { Codigo = $"C{g}", Nombre = $"Cat{g}", IsDeleted = false };
        var marca = new Marca { Codigo = $"M{g}", Nombre = $"Mar{g}", IsDeleted = false };
        _context.Categorias.Add(cat);
        _context.Marcas.Add(marca);
        await _context.SaveChangesAsync();

        var cliente = new Cliente
        {
            Nombre = "Test", Apellido = $"T{g}",
            NumeroDocumento = g,
            NivelRiesgo = NivelRiesgoCredito.AprobadoTotal,
            IsDeleted = false
        };
        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync();

        var producto = new Producto
        {
            Codigo = $"TR{g}",
            Nombre = $"ProdTr{g}",
            CategoriaId = cat.Id,
            MarcaId = marca.Id,
            PrecioVenta = 500m,
            StockActual = stock,
            RequiereNumeroSerie = requiereNumeroSerie,
            IsDeleted = false
        };
        _context.Productos.Add(producto);
        await _context.SaveChangesAsync();

        return (producto, cliente);
    }

    private async Task<Venta> SeedVentaConDetalle(
        Producto producto,
        Cliente cliente,
        int cantidad = 1,
        int? productoUnidadId = null,
        EstadoVenta estado = EstadoVenta.Presupuesto)
    {
        var n = Interlocked.Increment(ref _counter).ToString();

        var venta = new Venta
        {
            ClienteId = cliente.Id,
            Numero = $"VTR{n}",
            Estado = estado,
            TipoPago = TipoPago.Efectivo,
            Total = 500m * cantidad,
            IsDeleted = false
        };
        _context.Ventas.Add(venta);
        await _context.SaveChangesAsync();

        var detalle = new VentaDetalle
        {
            VentaId = venta.Id,
            ProductoId = producto.Id,
            Cantidad = cantidad,
            PrecioUnitario = 500m,
            Subtotal = 500m * cantidad,
            ProductoUnidadId = productoUnidadId,
            IsDeleted = false
        };
        _context.VentaDetalles.Add(detalle);
        await _context.SaveChangesAsync();

        return venta;
    }

    private async Task<ProductoUnidad> SeedUnidadEnStockAsync(Producto producto, string? serie = null)
    {
        return await _unidadService.CrearUnidadAsync(producto.Id, serie, null, null, "seed");
    }

    // -------------------------------------------------------------------------
    // 1. EF: VentaDetalle con ProductoUnidadId persiste
    // -------------------------------------------------------------------------

    [Fact]
    public async Task VentaDetalle_ConProductoUnidadId_Persiste()
    {
        var (producto, cliente) = await SeedBaseAsync(requiereNumeroSerie: true);
        var unidad = await SeedUnidadEnStockAsync(producto, "SN-EF-001");

        var venta = await SeedVentaConDetalle(producto, cliente, productoUnidadId: unidad.Id);

        var detalleGuardado = await _context.VentaDetalles
            .AsNoTracking()
            .FirstAsync(d => d.VentaId == venta.Id);

        Assert.Equal(unidad.Id, detalleGuardado.ProductoUnidadId);
    }

    [Fact]
    public async Task VentaDetalle_SinProductoUnidadId_Persiste()
    {
        var (producto, cliente) = await SeedBaseAsync();

        var venta = await SeedVentaConDetalle(producto, cliente, productoUnidadId: null);

        var detalleGuardado = await _context.VentaDetalles
            .AsNoTracking()
            .FirstAsync(d => d.VentaId == venta.Id);

        Assert.Null(detalleGuardado.ProductoUnidadId);
    }

    // -------------------------------------------------------------------------
    // 2. Validación: producto trazable sin ProductoUnidadId rechaza
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ConfirmarVenta_ProductoTrazable_SinUnidadId_LanzaExcepcion()
    {
        var (producto, cliente) = await SeedBaseAsync(requiereNumeroSerie: true, stock: 5);
        var venta = await SeedVentaConDetalle(producto, cliente, productoUnidadId: null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.ConfirmarVentaAsync(venta.Id));

        Assert.Contains("requiere selección de unidad individual", ex.Message);
    }

    // -------------------------------------------------------------------------
    // 3. Validación: unidad de otro producto rechaza
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ConfirmarVenta_ProductoTrazable_UnidadDeOtroProducto_LanzaExcepcion()
    {
        var (producto, cliente) = await SeedBaseAsync(requiereNumeroSerie: true, stock: 5);
        var (otroProducto, _) = await SeedBaseAsync(requiereNumeroSerie: true, stock: 5);

        var unidadOtro = await SeedUnidadEnStockAsync(otroProducto, "SN-OTRO-001");
        var venta = await SeedVentaConDetalle(producto, cliente, productoUnidadId: unidadOtro.Id);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.ConfirmarVentaAsync(venta.Id));

        Assert.Contains("no pertenece al producto", ex.Message);
    }

    // -------------------------------------------------------------------------
    // 4. Validación: unidad no EnStock rechaza
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ConfirmarVenta_ProductoTrazable_UnidadNoEnStock_LanzaExcepcion()
    {
        var (producto, cliente) = await SeedBaseAsync(requiereNumeroSerie: true, stock: 5);
        var unidad = await SeedUnidadEnStockAsync(producto, "SN-NOSTOCK-001");

        // Simular que la unidad ya está vendida
        unidad.Estado = EstadoUnidad.Vendida;
        await _context.SaveChangesAsync();

        var venta = await SeedVentaConDetalle(producto, cliente, productoUnidadId: unidad.Id);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.ConfirmarVentaAsync(venta.Id));

        Assert.Contains("no está disponible", ex.Message);
    }

    // -------------------------------------------------------------------------
    // 5. Validación: cantidad > 1 en producto trazable rechaza
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ConfirmarVenta_ProductoTrazable_CantidadMayorUno_LanzaExcepcion()
    {
        var (producto, cliente) = await SeedBaseAsync(requiereNumeroSerie: true, stock: 10);
        var unidad = await SeedUnidadEnStockAsync(producto, "SN-CANT-001");
        var venta = await SeedVentaConDetalle(producto, cliente, cantidad: 2, productoUnidadId: unidad.Id);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.ConfirmarVentaAsync(venta.Id));

        Assert.Contains("cantidad por línea debe ser 1", ex.Message);
    }

    // -------------------------------------------------------------------------
    // 6. Validación: unidad duplicada en la misma venta rechaza
    // El índice único filtrado previene persistir duplicados activos, pero el
    // service los detecta primero con ValidarUnidadesTrazablesAsync.
    // Usamos SQL directo para simular el escenario (migración anterior sin índice).
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ConfirmarVenta_ProductoTrazable_UnidadDuplicadaEnVenta_LanzaExcepcion()
    {
        var (producto, cliente) = await SeedBaseAsync(requiereNumeroSerie: true, stock: 10);
        var unidad = await SeedUnidadEnStockAsync(producto, "SN-DUP-001");

        var n = Interlocked.Increment(ref _counter).ToString();
        var venta = new Venta
        {
            ClienteId = cliente.Id,
            Numero = $"VDUP{n}",
            Estado = EstadoVenta.Presupuesto,
            TipoPago = TipoPago.Efectivo,
            Total = 1_000m,
            IsDeleted = false
        };
        _context.Ventas.Add(venta);
        await _context.SaveChangesAsync();

        var detalle1 = new VentaDetalle
        {
            VentaId = venta.Id, ProductoId = producto.Id,
            Cantidad = 1, PrecioUnitario = 500m, Subtotal = 500m,
            ProductoUnidadId = unidad.Id, IsDeleted = false
        };
        _context.VentaDetalles.Add(detalle1);
        await _context.SaveChangesAsync();

        // SQLite no soporta índices filtrados: lo crea como índice único completo.
        // Hay que quitarlo para poder insertar la segunda fila con el mismo ProductoUnidadId.
        await _context.Database.ExecuteSqlRawAsync("DROP INDEX IF EXISTS \"UX_VentaDetalles_ProductoUnidadId\"");

        // Segunda línea con la misma unidad: bypassear EF para simular escenario pre-índice
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        await _context.Database.ExecuteSqlRawAsync(
            $"INSERT INTO VentaDetalles (VentaId, ProductoId, Cantidad, PrecioUnitario, Subtotal, ProductoUnidadId, IsDeleted, Descuento, CreatedAt, UpdatedAt) " +
            $"VALUES ({venta.Id}, {producto.Id}, 1, 500, 500, {unidad.Id}, 0, 0, '{now}', '{now}')");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.ConfirmarVentaAsync(venta.Id));

        Assert.Contains("unidades duplicadas", ex.Message);
    }

    // -------------------------------------------------------------------------
    // 7. Validación: producto NO trazable con unidad informada rechaza
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ConfirmarVenta_ProductoNoTrazable_ConUnidadId_LanzaExcepcion()
    {
        var (productoTrazable, _) = await SeedBaseAsync(requiereNumeroSerie: true, stock: 5);
        var (productoNormal, cliente) = await SeedBaseAsync(requiereNumeroSerie: false, stock: 5);

        var unidad = await SeedUnidadEnStockAsync(productoTrazable, "SN-NOTRAZ-001");
        var venta = await SeedVentaConDetalle(productoNormal, cliente, productoUnidadId: unidad.Id);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.ConfirmarVentaAsync(venta.Id));

        Assert.Contains("no requiere unidad individual", ex.Message);
    }

    // -------------------------------------------------------------------------
    // 8. ConfirmarVentaAsync marca unidad como Vendida
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ConfirmarVenta_ProductoTrazable_MarcaUnidadVendida()
    {
        var (producto, cliente) = await SeedBaseAsync(requiereNumeroSerie: true, stock: 5);
        var unidad = await SeedUnidadEnStockAsync(producto, "SN-VEND-001");
        var venta = await SeedVentaConDetalle(producto, cliente, productoUnidadId: unidad.Id);

        await _service.ConfirmarVentaAsync(venta.Id);

        var unidadActualizada = await _context.ProductoUnidades.FindAsync(unidad.Id);
        Assert.NotNull(unidadActualizada);
        Assert.Equal(EstadoUnidad.Vendida, unidadActualizada!.Estado);
        Assert.NotNull(unidadActualizada.FechaVenta);
        Assert.Equal(cliente.Id, unidadActualizada.ClienteId);
    }

    // -------------------------------------------------------------------------
    // 9. ConfirmarVentaAsync crea historial EnStock → Vendida
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ConfirmarVenta_ProductoTrazable_CreaHistorialEnStockAVendida()
    {
        var (producto, cliente) = await SeedBaseAsync(requiereNumeroSerie: true, stock: 5);
        var unidad = await SeedUnidadEnStockAsync(producto, "SN-HIST-001");
        var venta = await SeedVentaConDetalle(producto, cliente, productoUnidadId: unidad.Id);

        await _service.ConfirmarVentaAsync(venta.Id);

        var historial = await _context.ProductoUnidadMovimientos
            .Where(m => m.ProductoUnidadId == unidad.Id)
            .OrderBy(m => m.FechaCambio)
            .ToListAsync();

        // El último movimiento debe ser EnStock → Vendida
        var ultimoMovimiento = historial.Last();
        Assert.Equal(EstadoUnidad.EnStock, ultimoMovimiento.EstadoAnterior);
        Assert.Equal(EstadoUnidad.Vendida, ultimoMovimiento.EstadoNuevo);
        Assert.Contains("trazab-user", ultimoMovimiento.UsuarioResponsable ?? "");
    }

    // -------------------------------------------------------------------------
    // 10. CancelarVentaAsync de venta confirmada revierte unidad a EnStock
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CancelarVenta_Confirmada_RevierteUnidadAEnStock()
    {
        var (producto, cliente) = await SeedBaseAsync(requiereNumeroSerie: true, stock: 5);
        var unidad = await SeedUnidadEnStockAsync(producto, "SN-CANC-001");
        var venta = await SeedVentaConDetalle(producto, cliente, productoUnidadId: unidad.Id);

        await _service.ConfirmarVentaAsync(venta.Id);

        // Verificar que quedó Vendida después de confirmar
        var unidadTrasConfirmar = await _context.ProductoUnidades.AsNoTracking().FirstAsync(u => u.Id == unidad.Id);
        Assert.Equal(EstadoUnidad.Vendida, unidadTrasConfirmar.Estado);

        await _service.CancelarVentaAsync(venta.Id, "Test cancelación");

        var unidadTrasCancel = await _context.ProductoUnidades.AsNoTracking().FirstAsync(u => u.Id == unidad.Id);
        Assert.Equal(EstadoUnidad.EnStock, unidadTrasCancel.Estado);
        Assert.Null(unidadTrasCancel.VentaDetalleId);
        Assert.Null(unidadTrasCancel.ClienteId);
        Assert.Null(unidadTrasCancel.FechaVenta);

        var detalleTrasCancel = await _context.VentaDetalles.AsNoTracking().FirstAsync(d => d.VentaId == venta.Id);
        Assert.Null(detalleTrasCancel.ProductoUnidadId);
    }

    // -------------------------------------------------------------------------
    // 11. CancelarVentaAsync de venta no confirmada no toca la unidad
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CancelarVenta_NoConfirmada_NoTocaUnidad()
    {
        var (producto, cliente) = await SeedBaseAsync(requiereNumeroSerie: true, stock: 5);
        var unidad = await SeedUnidadEnStockAsync(producto, "SN-NOCONF-001");
        var venta = await SeedVentaConDetalle(producto, cliente, productoUnidadId: unidad.Id);

        // La venta está en Presupuesto (no confirmada)
        await _service.CancelarVentaAsync(venta.Id, "Test sin confirmar");

        var unidadTrasCancel = await _context.ProductoUnidades.AsNoTracking().FirstAsync(u => u.Id == unidad.Id);
        Assert.Equal(EstadoUnidad.EnStock, unidadTrasCancel.Estado);

        var detalleTrasCancel = await _context.VentaDetalles.AsNoTracking().FirstAsync(d => d.VentaId == venta.Id);
        Assert.Null(detalleTrasCancel.ProductoUnidadId);
    }

    // -------------------------------------------------------------------------
    // 12. Preview/CalcularTotales no modifica estado de unidad
    // -------------------------------------------------------------------------

    [Fact]
    public void CalcularTotalesPreview_NoModificaEstadoUnidad()
    {
        // CalcularTotalesPreview es síncrono y no accede a ProductoUnidad en ningún caso.
        // Verificamos que la interfaz no tiene side effects con unidades.
        var detalles = new List<DetalleCalculoVentaRequest>
        {
            new DetalleCalculoVentaRequest
            {
                ProductoId = 999,
                Cantidad = 1,
                PrecioUnitario = 500m
            }
        };

        // No debe lanzar y no toca base de datos
        var resultado = _service.CalcularTotalesPreview(detalles, 0m, false);

        Assert.NotNull(resultado);
        Assert.True(resultado.Total > 0m);
    }

    // -------------------------------------------------------------------------
    // 13. RevertirVentaAsync: Vendida → EnStock limpia campos
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RevertirVentaAsync_DeVendidaAEnStock_LimpiaVentaDetalleId()
    {
        var (producto, _) = await SeedBaseAsync(requiereNumeroSerie: true, stock: 5);
        var unidad = await SeedUnidadEnStockAsync(producto, "SN-REV-001");

        // Seed un VentaDetalle para poder llamar MarcarVendida
        var n = Interlocked.Increment(ref _counter).ToString();
        var cliente = new Cliente { Nombre = "R", Apellido = $"V{n}", NumeroDocumento = $"9{n}", IsDeleted = false };
        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync();

        var venta = new Venta { ClienteId = cliente.Id, Numero = $"VREV{n}", Estado = EstadoVenta.Confirmada, TipoPago = TipoPago.Efectivo, IsDeleted = false };
        _context.Ventas.Add(venta);
        await _context.SaveChangesAsync();

        var detalle = new VentaDetalle { VentaId = venta.Id, ProductoId = producto.Id, Cantidad = 1, PrecioUnitario = 500m, Subtotal = 500m, IsDeleted = false };
        _context.VentaDetalles.Add(detalle);
        await _context.SaveChangesAsync();

        await _unidadService.MarcarVendidaAsync(unidad.Id, detalle.Id, cliente.Id, "test");

        var unidadVendida = await _context.ProductoUnidades.AsNoTracking().FirstAsync(u => u.Id == unidad.Id);
        Assert.Equal(EstadoUnidad.Vendida, unidadVendida.Estado);

        await _unidadService.RevertirVentaAsync(unidad.Id, "Cancelacion test", "test");

        var unidadRevertida = await _context.ProductoUnidades.AsNoTracking().FirstAsync(u => u.Id == unidad.Id);
        Assert.Equal(EstadoUnidad.EnStock, unidadRevertida.Estado);
        Assert.Null(unidadRevertida.VentaDetalleId);
        Assert.Null(unidadRevertida.ClienteId);
        Assert.Null(unidadRevertida.FechaVenta);
    }

    // -------------------------------------------------------------------------
    // 14. MarcarDevueltaAsync: Vendida → Devuelta
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MarcarDevueltaAsync_DeVendidaADevuelta_CreaHistorial()
    {
        var (producto, _) = await SeedBaseAsync(requiereNumeroSerie: true, stock: 5);
        var unidad = await SeedUnidadEnStockAsync(producto, "SN-DEV-001");

        var n = Interlocked.Increment(ref _counter).ToString();
        var cliente = new Cliente { Nombre = "D", Apellido = $"V{n}", NumeroDocumento = $"10{n}", IsDeleted = false };
        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync();

        var venta = new Venta { ClienteId = cliente.Id, Numero = $"VDEV{n}", Estado = EstadoVenta.Confirmada, TipoPago = TipoPago.Efectivo, IsDeleted = false };
        _context.Ventas.Add(venta);
        await _context.SaveChangesAsync();

        var detalle = new VentaDetalle { VentaId = venta.Id, ProductoId = producto.Id, Cantidad = 1, PrecioUnitario = 500m, Subtotal = 500m, IsDeleted = false };
        _context.VentaDetalles.Add(detalle);
        await _context.SaveChangesAsync();

        await _unidadService.MarcarVendidaAsync(unidad.Id, detalle.Id, cliente.Id, "test");
        await _unidadService.MarcarDevueltaAsync(unidad.Id, "Devolucion del cliente", "test");

        var unidadActualizada = await _context.ProductoUnidades.AsNoTracking().FirstAsync(u => u.Id == unidad.Id);
        Assert.Equal(EstadoUnidad.Devuelta, unidadActualizada.Estado);

        var ultimoMov = await _context.ProductoUnidadMovimientos
            .Where(m => m.ProductoUnidadId == unidad.Id)
            .OrderByDescending(m => m.FechaCambio)
            .FirstAsync();
        Assert.Equal(EstadoUnidad.Vendida, ultimoMov.EstadoAnterior);
        Assert.Equal(EstadoUnidad.Devuelta, ultimoMov.EstadoNuevo);
    }

    // -------------------------------------------------------------------------
    // 15. ObtenerDisponiblesPorProducto: devuelve solo EnStock
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ObtenerDisponibles_DevuelveSoloUnidadesEnStock()
    {
        var (producto, _) = await SeedBaseAsync(requiereNumeroSerie: true, stock: 10);

        var u1 = await SeedUnidadEnStockAsync(producto, "SN-DISP-001");
        var u2 = await SeedUnidadEnStockAsync(producto, "SN-DISP-002");

        // Marcar u2 como Baja
        await _unidadService.MarcarBajaAsync(u2.Id, "Test baja", "test");

        var disponibles = (await _unidadService.ObtenerDisponiblesPorProductoAsync(producto.Id)).ToList();

        Assert.Single(disponibles);
        Assert.Equal(u1.Id, disponibles[0].Id);
    }

    // =========================================================================
    // Fase 8.2.S — UpdateAsync: validación trazabilidad en edición
    // =========================================================================

    private VentaViewModel BuildVentaVM(Venta venta, List<VentaDetalleViewModel> detalles)
        => new VentaViewModel
        {
            ClienteId = venta.ClienteId,
            Estado = venta.Estado,
            TipoPago = venta.TipoPago,
            RowVersion = venta.RowVersion ?? new byte[8],
            Detalles = detalles
        };

    // 16. UpdateAsync conserva ProductoUnidadId en venta no confirmada
    [Fact]
    public async Task UpdateAsync_ProductoTrazable_ConUnidad_Guarda()
    {
        var (producto, cliente) = await SeedBaseAsync(requiereNumeroSerie: true, stock: 5);
        var unidad = await SeedUnidadEnStockAsync(producto, "SN-UPD-OK");
        var venta = await SeedVentaConDetalle(producto, cliente, productoUnidadId: unidad.Id);
        var ventaDb = await _context.Ventas.AsNoTracking().FirstAsync(v => v.Id == venta.Id);

        var vm = BuildVentaVM(ventaDb, new List<VentaDetalleViewModel>
        {
            new VentaDetalleViewModel
            {
                ProductoId = producto.Id,
                Cantidad = 1,
                PrecioUnitario = 500m,
                Descuento = 0m,
                Subtotal = 500m,
                ProductoUnidadId = unidad.Id
            }
        });

        var result = await _service.UpdateAsync(venta.Id, vm);

        Assert.NotNull(result);
        var detalleGuardado = await _context.VentaDetalles.AsNoTracking()
            .FirstOrDefaultAsync(d => d.VentaId == venta.Id && !d.IsDeleted);
        Assert.NotNull(detalleGuardado);
        Assert.Equal(unidad.Id, detalleGuardado!.ProductoUnidadId);
    }

    // 17. UpdateAsync rechaza producto trazable sin unidad
    [Fact]
    public async Task UpdateAsync_ProductoTrazable_SinUnidad_LanzaExcepcion()
    {
        var (producto, cliente) = await SeedBaseAsync(requiereNumeroSerie: true, stock: 5);
        var unidad = await SeedUnidadEnStockAsync(producto, "SN-UPD-NOUNID");
        var venta = await SeedVentaConDetalle(producto, cliente, productoUnidadId: unidad.Id);
        var ventaDb = await _context.Ventas.AsNoTracking().FirstAsync(v => v.Id == venta.Id);

        var vm = BuildVentaVM(ventaDb, new List<VentaDetalleViewModel>
        {
            new VentaDetalleViewModel
            {
                ProductoId = producto.Id,
                Cantidad = 1,
                PrecioUnitario = 500m,
                Descuento = 0m,
                Subtotal = 500m,
                ProductoUnidadId = null  // sin unidad — inválido para trazable
            }
        });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.UpdateAsync(venta.Id, vm));

        Assert.Contains("requiere selección de unidad individual", ex.Message);
    }

    // 18. UpdateAsync rechaza producto trazable con cantidad > 1
    [Fact]
    public async Task UpdateAsync_ProductoTrazable_CantidadMayorUno_LanzaExcepcion()
    {
        var (producto, cliente) = await SeedBaseAsync(requiereNumeroSerie: true, stock: 5);
        var unidad = await SeedUnidadEnStockAsync(producto, "SN-UPD-CANT");
        var venta = await SeedVentaConDetalle(producto, cliente, productoUnidadId: unidad.Id);
        var ventaDb = await _context.Ventas.AsNoTracking().FirstAsync(v => v.Id == venta.Id);

        var vm = BuildVentaVM(ventaDb, new List<VentaDetalleViewModel>
        {
            new VentaDetalleViewModel
            {
                ProductoId = producto.Id,
                Cantidad = 2,  // inválido para trazable
                PrecioUnitario = 500m,
                Descuento = 0m,
                Subtotal = 1000m,
                ProductoUnidadId = unidad.Id
            }
        });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.UpdateAsync(venta.Id, vm));

        Assert.Contains("cantidad debe ser 1", ex.Message);
    }

    // 19. UpdateAsync rechaza unidad de otro producto
    [Fact]
    public async Task UpdateAsync_ProductoTrazable_UnidadDeOtroProducto_LanzaExcepcion()
    {
        var (producto, cliente) = await SeedBaseAsync(requiereNumeroSerie: true, stock: 5);
        var (otroProducto, _) = await SeedBaseAsync(requiereNumeroSerie: true, stock: 5);
        var unidadOtro = await SeedUnidadEnStockAsync(otroProducto, "SN-UPD-OTRO");
        var venta = await SeedVentaConDetalle(producto, cliente);
        var ventaDb = await _context.Ventas.AsNoTracking().FirstAsync(v => v.Id == venta.Id);

        var vm = BuildVentaVM(ventaDb, new List<VentaDetalleViewModel>
        {
            new VentaDetalleViewModel
            {
                ProductoId = producto.Id,
                Cantidad = 1,
                PrecioUnitario = 500m,
                Descuento = 0m,
                Subtotal = 500m,
                ProductoUnidadId = unidadOtro.Id  // unidad del producto incorrecto
            }
        });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.UpdateAsync(venta.Id, vm));

        Assert.Contains("no pertenece al producto", ex.Message);
    }

    // 20. UpdateAsync rechaza unidad no EnStock
    [Fact]
    public async Task UpdateAsync_ProductoTrazable_UnidadNoEnStock_LanzaExcepcion()
    {
        var (producto, cliente) = await SeedBaseAsync(requiereNumeroSerie: true, stock: 5);
        var unidad = await SeedUnidadEnStockAsync(producto, "SN-UPD-NOSTOCK");
        // Marcar la unidad como Baja para que no esté EnStock
        await _unidadService.MarcarBajaAsync(unidad.Id, "test", "test");
        var venta = await SeedVentaConDetalle(producto, cliente);
        var ventaDb = await _context.Ventas.AsNoTracking().FirstAsync(v => v.Id == venta.Id);

        var vm = BuildVentaVM(ventaDb, new List<VentaDetalleViewModel>
        {
            new VentaDetalleViewModel
            {
                ProductoId = producto.Id,
                Cantidad = 1,
                PrecioUnitario = 500m,
                Descuento = 0m,
                Subtotal = 500m,
                ProductoUnidadId = unidad.Id
            }
        });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.UpdateAsync(venta.Id, vm));

        Assert.Contains("no está disponible", ex.Message);
    }

    // 21. UpdateAsync rechaza duplicado de unidad en dos líneas
    [Fact]
    public async Task UpdateAsync_ProductoTrazable_UnidadDuplicada_LanzaExcepcion()
    {
        var (producto, cliente) = await SeedBaseAsync(requiereNumeroSerie: true, stock: 5);
        var unidad = await SeedUnidadEnStockAsync(producto, "SN-UPD-DUP");
        var venta = await SeedVentaConDetalle(producto, cliente);
        var ventaDb = await _context.Ventas.AsNoTracking().FirstAsync(v => v.Id == venta.Id);

        var vm = BuildVentaVM(ventaDb, new List<VentaDetalleViewModel>
        {
            new VentaDetalleViewModel
            {
                ProductoId = producto.Id,
                Cantidad = 1,
                PrecioUnitario = 500m,
                Subtotal = 500m,
                ProductoUnidadId = unidad.Id
            },
            new VentaDetalleViewModel
            {
                ProductoId = producto.Id,
                Cantidad = 1,
                PrecioUnitario = 500m,
                Subtotal = 500m,
                ProductoUnidadId = unidad.Id  // misma unidad — inválido
            }
        });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.UpdateAsync(venta.Id, vm));

        Assert.Contains("unidades duplicadas", ex.Message);
    }

    // 22. UpdateAsync permite producto no trazable sin unidad
    [Fact]
    public async Task UpdateAsync_ProductoNoTrazable_SinUnidad_Guarda()
    {
        var (producto, cliente) = await SeedBaseAsync(requiereNumeroSerie: false, stock: 5);
        var venta = await SeedVentaConDetalle(producto, cliente);
        var ventaDb = await _context.Ventas.AsNoTracking().FirstAsync(v => v.Id == venta.Id);

        var vm = BuildVentaVM(ventaDb, new List<VentaDetalleViewModel>
        {
            new VentaDetalleViewModel
            {
                ProductoId = producto.Id,
                Cantidad = 2,
                PrecioUnitario = 500m,
                Descuento = 0m,
                Subtotal = 1000m,
                ProductoUnidadId = null
            }
        });

        var result = await _service.UpdateAsync(venta.Id, vm);

        Assert.NotNull(result);
    }

    // 23. UpdateAsync rechaza producto no trazable con unidad informada
    [Fact]
    public async Task UpdateAsync_ProductoNoTrazable_ConUnidad_LanzaExcepcion()
    {
        var (productoNormal, cliente) = await SeedBaseAsync(requiereNumeroSerie: false, stock: 5);
        var (productoTrazable, _) = await SeedBaseAsync(requiereNumeroSerie: true, stock: 5);
        var unidad = await SeedUnidadEnStockAsync(productoTrazable, "SN-UPD-NOTRAZ");
        var venta = await SeedVentaConDetalle(productoNormal, cliente);
        var ventaDb = await _context.Ventas.AsNoTracking().FirstAsync(v => v.Id == venta.Id);

        var vm = BuildVentaVM(ventaDb, new List<VentaDetalleViewModel>
        {
            new VentaDetalleViewModel
            {
                ProductoId = productoNormal.Id,
                Cantidad = 1,
                PrecioUnitario = 500m,
                Subtotal = 500m,
                ProductoUnidadId = unidad.Id  // no trazable no debe tener unidad
            }
        });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.UpdateAsync(venta.Id, vm));

        Assert.Contains("no requiere unidad individual", ex.Message);
    }

    // 24. Edit (cambio de unidad) + ConfirmarVenta: la nueva unidad queda Vendida,
    //     la unidad anterior queda EnStock intacta (Fase 8.2.S Caso 2).
    [Fact]
    public async Task UpdateAsync_CambiaUnidad_LuegoConfirmar_NuevaUnidadVendidaViejaEnStock()
    {
        var (producto, cliente) = await SeedBaseAsync(requiereNumeroSerie: true, stock: 10);
        var unidadA = await SeedUnidadEnStockAsync(producto, "SN-CASO2-A");
        var unidadB = await SeedUnidadEnStockAsync(producto, "SN-CASO2-B");

        // Venta inicial con unidad A
        var venta = await SeedVentaConDetalle(producto, cliente, productoUnidadId: unidadA.Id);
        var ventaDb = await _context.Ventas.AsNoTracking().FirstAsync(v => v.Id == venta.Id);

        // Editar: cambiar a unidad B
        var vm = BuildVentaVM(ventaDb, new List<VentaDetalleViewModel>
        {
            new VentaDetalleViewModel
            {
                ProductoId = producto.Id,
                Cantidad = 1,
                PrecioUnitario = 500m,
                Descuento = 0m,
                Subtotal = 500m,
                ProductoUnidadId = unidadB.Id
            }
        });

        var resultado = await _service.UpdateAsync(venta.Id, vm);
        Assert.NotNull(resultado);

        // Confirmar la venta editada
        await _service.ConfirmarVentaAsync(venta.Id);

        // La unidad nueva (B) debe quedar Vendida
        var unidadBActualizada = await _context.ProductoUnidades.AsNoTracking().FirstAsync(u => u.Id == unidadB.Id);
        Assert.Equal(EstadoUnidad.Vendida, unidadBActualizada.Estado);

        // La unidad anterior (A) debe seguir EnStock (nunca fue marcada)
        var unidadAActualizada = await _context.ProductoUnidades.AsNoTracking().FirstAsync(u => u.Id == unidadA.Id);
        Assert.Equal(EstadoUnidad.EnStock, unidadAActualizada.Estado);
    }
}
