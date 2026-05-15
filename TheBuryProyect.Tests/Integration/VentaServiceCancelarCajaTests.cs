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

namespace TheBuryProject.Tests.Integration;

// ---------------------------------------------------------------------------
// Stubs file-scoped
// ---------------------------------------------------------------------------

file sealed class StubNotifCancelarCaja : INotificacionService
{
    public Task<Notificacion> CrearNotificacionAsync(CrearNotificacionViewModel model) => Task.FromResult(new Notificacion());
    public Task CrearNotificacionParaUsuarioAsync(string u, TipoNotificacion t, string ti, string m, string? url = null, PrioridadNotificacion p = PrioridadNotificacion.Media) => Task.CompletedTask;
    public Task CrearNotificacionParaRolAsync(string r, TipoNotificacion t, string ti, string m, string? url = null, PrioridadNotificacion p = PrioridadNotificacion.Media) => Task.CompletedTask;
    public Task<List<NotificacionViewModel>> ObtenerNotificacionesUsuarioAsync(string u, bool s = false, int l = 50) => Task.FromResult(new List<NotificacionViewModel>());
    public Task<int> ObtenerCantidadNoLeidasAsync(string u) => Task.FromResult(0);
    public Task<Notificacion?> ObtenerNotificacionPorIdAsync(int id) => Task.FromResult<Notificacion?>(null);
    public Task MarcarComoLeidaAsync(int id, string u, byte[]? rv = null) => Task.CompletedTask;
    public Task MarcarTodasComoLeidasAsync(string u) => Task.CompletedTask;
    public Task EliminarNotificacionAsync(int id, string u, byte[]? rv = null) => Task.CompletedTask;
    public Task LimpiarNotificacionesAntiguasAsync(int d = 30) => Task.CompletedTask;
    public Task<ListaNotificacionesViewModel> ObtenerResumenNotificacionesAsync(string u) => Task.FromResult(new ListaNotificacionesViewModel());
}

file sealed class StubMovimientoStockCancelarCaja : IMovimientoStockService
{
    public Task<List<MovimientoStock>> RegistrarSalidasAsync(List<(int productoId, decimal cantidad, string? referencia)> salidas, string motivo, string? usuarioActual = null, IReadOnlyList<MovimientoStockCostoLinea>? costos = null) => Task.FromResult(new List<MovimientoStock>());
    public Task<List<MovimientoStock>> RegistrarEntradasAsync(List<(int productoId, decimal cantidad, string? referencia)> entradas, string motivo, string? usuarioActual = null, int? ordenCompraId = null, IReadOnlyList<MovimientoStockCostoLinea>? costos = null) => Task.FromResult(new List<MovimientoStock>());
    public Task<IEnumerable<MovimientoStock>> GetAllAsync() => throw new NotImplementedException();
    public Task<MovimientoStock?> GetByIdAsync(int id) => throw new NotImplementedException();
    public Task<IEnumerable<MovimientoStock>> GetByProductoIdAsync(int productoId) => throw new NotImplementedException();
    public Task<IEnumerable<MovimientoStock>> GetByOrdenCompraIdAsync(int ordenCompraId) => throw new NotImplementedException();
    public Task<IEnumerable<MovimientoStock>> GetByTipoAsync(TipoMovimiento tipo) => throw new NotImplementedException();
    public Task<IEnumerable<MovimientoStock>> GetByFechaRangoAsync(DateTime fechaDesde, DateTime fechaHasta) => throw new NotImplementedException();
    public Task<IEnumerable<MovimientoStock>> SearchAsync(int? productoId = null, TipoMovimiento? tipo = null, DateTime? fechaDesde = null, DateTime? fechaHasta = null, string? orderBy = null, string? orderDirection = "desc") => throw new NotImplementedException();
    public Task<MovimientoStock> CreateAsync(MovimientoStock movimiento) => throw new NotImplementedException();
    public Task<MovimientoStock> RegistrarAjusteAsync(int productoId, TipoMovimiento tipo, decimal cantidad, string? referencia, string motivo, string? usuarioActual = null, int? ordenCompraId = null) => throw new NotImplementedException();
    public Task<bool> HayStockDisponibleAsync(int productoId, decimal cantidad) => Task.FromResult(true);
    public Task<(bool Valido, string Mensaje)> ValidarCantidadAsync(decimal cantidad) => Task.FromResult((true, string.Empty));
}

file sealed class StubAlertaStockCancelarCaja : IAlertaStockService
{
    public Task<int> VerificarYGenerarAlertasAsync(IEnumerable<int> productoIds) => Task.FromResult(0);
    public Task<int> GenerarAlertasStockBajoAsync(CancellationToken ct = default) => Task.FromResult(0);
    public Task<List<AlertaStock>> GetAlertasPendientesAsync() => throw new NotImplementedException();
    public Task<PaginatedResult<AlertaStockViewModel>> BuscarAsync(AlertaStockFiltroViewModel filtro) => throw new NotImplementedException();
    public Task<AlertaStockViewModel?> GetByIdAsync(int id) => throw new NotImplementedException();
    public Task<bool> ResolverAlertaAsync(int id, string u, string? obs = null, byte[]? rv = null) => throw new NotImplementedException();
    public Task<bool> IgnorarAlertaAsync(int id, string u, string? obs = null, byte[]? rv = null) => throw new NotImplementedException();
    public Task<AlertaStockEstadisticasViewModel> GetEstadisticasAsync() => throw new NotImplementedException();
    public Task<List<AlertaStock>> GetAlertasByProductoIdAsync(int productoId) => throw new NotImplementedException();
    public Task<AlertaStock?> VerificarYGenerarAlertaAsync(int productoId) => throw new NotImplementedException();
    public Task<int> LimpiarAlertasAntiguasAsync(int diasAntiguedad = 30, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<List<ProductoCriticoViewModel>> GetProductosCriticosAsync() => throw new NotImplementedException();
}

file sealed class StubCurrentUserCancelarCaja : ICurrentUserService
{
    public string GetUsername() => "testcj";
    public string GetUserId() => "system";
    public bool IsAuthenticated() => true;
    public string? GetEmail() => "testcj@test.com";
    public bool IsInRole(string role) => false;
    public bool HasPermission(string modulo, string accion) => false;
    public string? GetIpAddress() => "127.0.0.1";
}

file sealed class StubValidacionVentaCancelarCaja : IValidacionVentaService
{
    public Task<ValidacionVentaResult> ValidarVentaCreditoPersonalAsync(int clienteId, decimal montoVenta, int? creditoId = null) => Task.FromResult(new ValidacionVentaResult { NoViable = false });
    public Task<ValidacionVentaResult> ValidarConfirmacionVentaAsync(int ventaId) => Task.FromResult(new ValidacionVentaResult { NoViable = false, PendienteRequisitos = false, RequiereAutorizacion = false });
    public Task<PrevalidacionResultViewModel> PrevalidarAsync(int clienteId, decimal monto) => throw new NotImplementedException();
    public Task<bool> ClientePuedeRecibirCreditoAsync(int clienteId, decimal montoSolicitado) => throw new NotImplementedException();
    public Task<ResumenCrediticioClienteViewModel> ObtenerResumenCrediticioAsync(int clienteId) => throw new NotImplementedException();
}

file sealed class StubCreditoDisponibleCancelarCaja : ICreditoDisponibleService
{
    public Task<decimal> ObtenerLimitePorPuntajeAsync(NivelRiesgoCredito puntaje, CancellationToken ct = default) => Task.FromResult(0m);
    public Task<decimal> CalcularSaldoVigenteAsync(int clienteId, CancellationToken ct = default) => Task.FromResult(0m);
    public Task<CreditoDisponibleResultado> CalcularDisponibleAsync(int clienteId, CancellationToken ct = default) => Task.FromResult(new CreditoDisponibleResultado { Limite = 0m, Disponible = 999_999m });
    public Task<(bool Ok, List<string> Errores)> GuardarLimitesPorPuntajeAsync(IReadOnlyList<(NivelRiesgoCredito Puntaje, decimal LimiteMonto, bool Activo)> items, string usuario) => throw new NotImplementedException();
    public Task<List<PuntajeCreditoLimite>> GetAllLimitesPorPuntajeAsync() => throw new NotImplementedException();
}

file sealed class StubContratoVentaCreditoCancelarCaja : IContratoVentaCreditoService
{
    public Task<bool> ExisteContratoGeneradoAsync(int ventaId) => Task.FromResult(true);
    public Task<ContratoVentaCreditoValidacionResult> ValidarDatosParaGenerarAsync(int ventaId) => Task.FromResult(new ContratoVentaCreditoValidacionResult());
    public Task<ContratoVentaCredito> GenerarAsync(int ventaId, string usuario) => throw new NotImplementedException();
    public Task<ContratoVentaCredito> GenerarPdfAsync(int ventaId, string usuario) => throw new NotImplementedException();
    public Task<ContratoVentaCreditoPdfArchivo?> ObtenerPdfAsync(int ventaId) => Task.FromResult<ContratoVentaCreditoPdfArchivo?>(null);
    public Task<bool> ExistePlantillaActivaAsync() => Task.FromResult(true);
    public Task<ContratoVentaCredito?> ObtenerContratoPorVentaAsync(int ventaId) => Task.FromResult<ContratoVentaCredito?>(null);
    public Task<ContratoVentaCredito?> ObtenerContratoPorCreditoAsync(int creditoId) => Task.FromResult<ContratoVentaCredito?>(null);
}

file sealed class StubConfiguracionPagoCancelarCaja : IConfiguracionPagoService
{
    public Task<MaxCuotasSinInteresResultado?> ObtenerMaxCuotasSinInteresEfectivoAsync(int tarjetaId, IEnumerable<int> productoIds) => Task.FromResult<MaxCuotasSinInteresResultado?>(null);
    public Task<List<ConfiguracionPagoViewModel>> GetAllAsync() => throw new NotImplementedException();
    public Task<ConfiguracionPagoViewModel?> GetByIdAsync(int id) => throw new NotImplementedException();
    public Task<ConfiguracionPagoViewModel?> GetByTipoPagoAsync(TipoPago tipoPago) => throw new NotImplementedException();
    public Task<decimal?> ObtenerTasaInteresMensualCreditoPersonalAsync() => throw new NotImplementedException();
    public Task<ConfiguracionPagoViewModel> CreateAsync(ConfiguracionPagoViewModel viewModel) => throw new NotImplementedException();
    public Task<ConfiguracionPagoViewModel?> UpdateAsync(int id, ConfiguracionPagoViewModel viewModel) => throw new NotImplementedException();
    public Task<bool> DeleteAsync(int id) => throw new NotImplementedException();
    public Task GuardarConfiguracionesModalAsync(IReadOnlyList<ConfiguracionPagoViewModel> configuraciones) => throw new NotImplementedException();
    public Task<List<ConfiguracionTarjetaViewModel>> GetTarjetasActivasAsync() => throw new NotImplementedException();
    public Task<List<TarjetaActivaVentaResultado>> GetTarjetasActivasParaVentaAsync() => throw new NotImplementedException();
    public Task<ConfiguracionTarjetaViewModel?> GetTarjetaByIdAsync(int id) => throw new NotImplementedException();
    public Task<bool> ValidarDescuento(TipoPago tipoPago, decimal descuento) => throw new NotImplementedException();
    public Task<decimal> CalcularRecargo(TipoPago tipoPago, decimal monto) => throw new NotImplementedException();
    public Task<List<PerfilCreditoViewModel>> GetPerfilesCreditoAsync() => throw new NotImplementedException();
    public Task<List<PerfilCreditoViewModel>> GetPerfilesCreditoActivosAsync() => throw new NotImplementedException();
    public Task GuardarCreditoPersonalAsync(CreditoPersonalConfigViewModel config) => throw new NotImplementedException();
    public Task<ParametrosCreditoCliente> ObtenerParametrosCreditoClienteAsync(int clienteId, decimal tasaGlobal) => throw new NotImplementedException();
    public Task<(int Min, int Max, string Descripcion, string? PerfilNombre)> ResolverRangoCuotasAsync(MetodoCalculoCredito metodo, int? perfilId, int? clienteId) => Task.FromResult((1, 24, "Global", (string?)null));
}

// ---------------------------------------------------------------------------

/// <summary>
/// Tests de integración: contramovimiento de caja al cancelar venta (Fase 9.3).
///
/// Usa CajaService REAL sobre SQLite in-memory para verificar que el egreso de
/// reversión se crea correctamente en la DB al cancelar una venta confirmada o
/// facturada con ingreso en caja.
///
/// Contratos verificados:
/// - Cancelar venta Confirmada/Efectivo → egreso ReversionVenta creado
/// - Saldo neto de caja queda igual al monto inicial (ingreso - egreso = 0)
/// - Ingreso original no es eliminado ni modificado
/// - Cancelar venta Facturada → contramovimiento + factura anulada
/// - No duplica contramovimiento (guard anti-doble)
/// - Venta sin ingreso en caja → sin contramovimiento (Cotizacion, CreditoPersonal)
/// - Venta termina en Cancelada
/// </summary>
public class VentaServiceCancelarCajaTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly VentaService _ventaService;
    private readonly CajaService _cajaService;
    private readonly AperturaCaja _apertura;

    private static int _counter = 930;

    public VentaServiceCancelarCajaTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        var mapper = new MapperConfiguration(
                cfg => cfg.AddProfile<MappingProfile>(),
                NullLoggerFactory.Instance)
            .CreateMapper();

        _cajaService = new CajaService(
            _context,
            mapper,
            NullLogger<CajaService>.Instance,
            new StubNotifCancelarCaja());

        _apertura = SeedCajaSinc();

        _ventaService = new VentaService(
            _context,
            mapper,
            NullLogger<VentaService>.Instance,
            new StubAlertaStockCancelarCaja(),
            new StubMovimientoStockCancelarCaja(),
            new FinancialCalculationService(),
            new VentaValidator(),
            new VentaNumberGenerator(_context, NullLogger<VentaNumberGenerator>.Instance),
            new PrecioVigenteResolver(_context),
            new StubCurrentUserCancelarCaja(),
            new StubValidacionVentaCancelarCaja(),
            _cajaService,
            new StubCreditoDisponibleCancelarCaja(),
            new StubContratoVentaCreditoCancelarCaja(),
            new StubConfiguracionPagoCancelarCaja());
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
        var caja = new Caja { Codigo = "CJ93", Nombre = "Caja 9.3", Activa = true, Estado = EstadoCaja.Abierta };
        _context.Cajas.Add(caja);
        _context.SaveChanges();

        var ap = new AperturaCaja
        {
            CajaId = caja.Id,
            UsuarioApertura = "testcj",
            MontoInicial = 0m,
            Cerrada = false
        };
        _context.AperturasCaja.Add(ap);
        _context.SaveChanges();
        return ap;
    }

    private async Task<(Producto producto, Cliente cliente)> SeedBaseAsync(int stock = 10)
    {
        var g = Guid.NewGuid().ToString("N")[..6];
        var cat = new Categoria { Codigo = $"C{g}", Nombre = $"Cat{g}" };
        var marca = new Marca { Codigo = $"M{g}", Nombre = $"Mar{g}" };
        _context.Categorias.Add(cat);
        _context.Marcas.Add(marca);
        await _context.SaveChangesAsync();

        var cliente = new Cliente { Nombre = "Test", Apellido = $"T{g}", NumeroDocumento = g, NivelRiesgo = NivelRiesgoCredito.AprobadoTotal };
        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync();

        var producto = new Producto { Codigo = $"P{g}", Nombre = $"Prod{g}", CategoriaId = cat.Id, MarcaId = marca.Id, PrecioVenta = 100m, StockActual = stock };
        _context.Productos.Add(producto);
        await _context.SaveChangesAsync();

        return (producto, cliente);
    }

    private async Task<Venta> SeedVentaAsync(int clienteId, int productoId, TipoPago tipoPago = TipoPago.Efectivo, EstadoVenta estado = EstadoVenta.Presupuesto, decimal total = 100m)
    {
        var n = System.Threading.Interlocked.Increment(ref _counter).ToString();
        var venta = new Venta { ClienteId = clienteId, Numero = $"V93{n}", Estado = estado, TipoPago = tipoPago, Total = total };
        _context.Ventas.Add(venta);
        await _context.SaveChangesAsync();

        var detalle = new VentaDetalle { VentaId = venta.Id, ProductoId = productoId, Cantidad = 1, PrecioUnitario = total, Subtotal = total };
        _context.VentaDetalles.Add(detalle);
        await _context.SaveChangesAsync();

        return venta;
    }

    // -------------------------------------------------------------------------
    // 1. Cancelar venta Confirmada/Efectivo crea egreso de reversión
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CancelarVenta_Confirmada_Efectivo_CreaContramovimientoEgreso()
    {
        var (producto, cliente) = await SeedBaseAsync();
        var venta = await SeedVentaAsync(cliente.Id, producto.Id);

        await _ventaService.ConfirmarVentaAsync(venta.Id);

        var ingresos = await _context.MovimientosCaja
            .Where(m => m.VentaId == venta.Id && m.Tipo == TipoMovimientoCaja.Ingreso && !m.IsDeleted)
            .ToListAsync();
        Assert.Single(ingresos);

        await _ventaService.CancelarVentaAsync(venta.Id, "Cancelación test 9.3");

        var egreso = await _context.MovimientosCaja
            .FirstOrDefaultAsync(m => m.VentaId == venta.Id
                                   && m.Tipo == TipoMovimientoCaja.Egreso
                                   && m.Concepto == ConceptoMovimientoCaja.ReversionVenta
                                   && !m.IsDeleted);

        Assert.NotNull(egreso);
        Assert.Equal(100m, egreso.Monto);
        Assert.Equal(TipoPago.Efectivo, egreso.TipoPago);
    }

    // -------------------------------------------------------------------------
    // 2. Saldo de caja queda neutralizado tras cancelación
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CancelarVenta_Confirmada_Efectivo_SaldoCajaNeutralizado()
    {
        var (producto, cliente) = await SeedBaseAsync();
        var venta = await SeedVentaAsync(cliente.Id, producto.Id, total: 250m);

        await _ventaService.ConfirmarVentaAsync(venta.Id);

        var saldoAntes = await _cajaService.CalcularSaldoActualAsync(_apertura.Id);
        Assert.Equal(250m, saldoAntes);

        await _ventaService.CancelarVentaAsync(venta.Id, "Reversa saldo");

        var saldoDespues = await _cajaService.CalcularSaldoActualAsync(_apertura.Id);
        Assert.Equal(0m, saldoDespues);
    }

    // -------------------------------------------------------------------------
    // 3. Ingreso original no se elimina ni modifica
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CancelarVenta_Confirmada_Efectivo_IngresoOriginalIntacto()
    {
        var (producto, cliente) = await SeedBaseAsync();
        var venta = await SeedVentaAsync(cliente.Id, producto.Id, total: 150m);

        await _ventaService.ConfirmarVentaAsync(venta.Id);

        var ingresoBefore = await _context.MovimientosCaja
            .AsNoTracking()
            .FirstAsync(m => m.VentaId == venta.Id && m.Tipo == TipoMovimientoCaja.Ingreso && !m.IsDeleted);

        await _ventaService.CancelarVentaAsync(venta.Id, "Reversa integridad");

        var ingresoAfter = await _context.MovimientosCaja
            .AsNoTracking()
            .FirstAsync(m => m.Id == ingresoBefore.Id);

        Assert.False(ingresoAfter.IsDeleted);
        Assert.Equal(150m, ingresoAfter.Monto);
        Assert.Equal(TipoMovimientoCaja.Ingreso, ingresoAfter.Tipo);
    }

    // -------------------------------------------------------------------------
    // 4. Venta Facturada: contramovimiento creado + factura anulada
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CancelarVenta_Facturada_CreaContramovimiento_YFacturaAnulada()
    {
        var (producto, cliente) = await SeedBaseAsync();
        var venta = await SeedVentaAsync(cliente.Id, producto.Id);

        await _ventaService.ConfirmarVentaAsync(venta.Id);
        await _ventaService.FacturarVentaAsync(venta.Id, new FacturaViewModel { Tipo = TipoFactura.B, FechaEmision = DateTime.UtcNow });

        var ventaFacturada = await _context.Ventas.AsNoTracking().FirstAsync(v => v.Id == venta.Id);
        Assert.Equal(EstadoVenta.Facturada, ventaFacturada.Estado);

        await _ventaService.CancelarVentaAsync(venta.Id, "Cancelar facturada 9.3");

        // Contramovimiento creado
        var egreso = await _context.MovimientosCaja
            .FirstOrDefaultAsync(m => m.VentaId == venta.Id
                                   && m.Tipo == TipoMovimientoCaja.Egreso
                                   && m.Concepto == ConceptoMovimientoCaja.ReversionVenta
                                   && !m.IsDeleted);
        Assert.NotNull(egreso);

        // Factura anulada (de Fase 9.2)
        var factura = await _context.Facturas.FirstOrDefaultAsync(f => f.VentaId == venta.Id && !f.IsDeleted);
        Assert.NotNull(factura);
        Assert.True(factura.Anulada);

        // Venta en Cancelada
        var ventaCancel = await _context.Ventas.AsNoTracking().FirstAsync(v => v.Id == venta.Id);
        Assert.Equal(EstadoVenta.Cancelada, ventaCancel.Estado);
    }

    // -------------------------------------------------------------------------
    // 5. No duplica contramovimiento (guard: reintento de cancelación bloqueado)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CancelarVenta_IntentoCancelarDosVeces_NoHayDuplicadoDeContramovimiento()
    {
        var (producto, cliente) = await SeedBaseAsync();
        var venta = await SeedVentaAsync(cliente.Id, producto.Id);

        await _ventaService.ConfirmarVentaAsync(venta.Id);
        await _ventaService.CancelarVentaAsync(venta.Id, "Primera cancelación");

        // Segundo intento debe lanzar por estado Cancelada
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _ventaService.CancelarVentaAsync(venta.Id, "Segunda cancelación"));

        var egresos = await _context.MovimientosCaja
            .Where(m => m.VentaId == venta.Id
                     && m.Tipo == TipoMovimientoCaja.Egreso
                     && m.Concepto == ConceptoMovimientoCaja.ReversionVenta
                     && !m.IsDeleted)
            .CountAsync();

        Assert.Equal(1, egresos);
    }

    // -------------------------------------------------------------------------
    // 6. Cotización/Presupuesto: sin contramovimiento
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CancelarVenta_Presupuesto_NoCreaBContramovimiento()
    {
        var (producto, cliente) = await SeedBaseAsync();
        var venta = await SeedVentaAsync(cliente.Id, producto.Id, estado: EstadoVenta.Presupuesto);

        // No llamar a ConfirmarVentaAsync → sin ingreso en caja
        await _ventaService.CancelarVentaAsync(venta.Id, "Presupuesto cancelado");

        var movimientos = await _context.MovimientosCaja
            .Where(m => m.VentaId == venta.Id && !m.IsDeleted)
            .CountAsync();

        Assert.Equal(0, movimientos);
    }

    // -------------------------------------------------------------------------
    // 7. CreditoPersonal confirmado: sin ingreso en caja → sin contramovimiento
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CancelarVenta_Confirmada_CreditoPersonal_NoContramovimiento()
    {
        var (producto, cliente) = await SeedBaseAsync();
        // Sembramos directamente en Confirmada con CreditoPersonal
        // (ConfirmarVentaAsync para crédito es complejo; este test valida solo la lógica de caja)
        var n = System.Threading.Interlocked.Increment(ref _counter).ToString();
        var venta = new Venta
        {
            ClienteId = cliente.Id,
            Numero = $"VCP{n}",
            Estado = EstadoVenta.Confirmada,
            TipoPago = TipoPago.CreditoPersonal,
            Total = 200m,
            AperturaCajaId = _apertura.Id,
            FechaConfirmacion = DateTime.UtcNow
        };
        _context.Ventas.Add(venta);
        await _context.SaveChangesAsync();
        var detalle = new VentaDetalle { VentaId = venta.Id, ProductoId = producto.Id, Cantidad = 1, PrecioUnitario = 200m, Subtotal = 200m };
        _context.VentaDetalles.Add(detalle);
        await _context.SaveChangesAsync();

        // Sin ingreso en caja para esta venta (CreditoPersonal)
        var ingresosAntes = await _context.MovimientosCaja
            .Where(m => m.VentaId == venta.Id && !m.IsDeleted)
            .CountAsync();
        Assert.Equal(0, ingresosAntes);

        await _ventaService.CancelarVentaAsync(venta.Id, "Crédito cancelado");

        var movimientosDespues = await _context.MovimientosCaja
            .Where(m => m.VentaId == venta.Id && !m.IsDeleted)
            .CountAsync();

        Assert.Equal(0, movimientosDespues);
    }

    // -------------------------------------------------------------------------
    // 8. Venta queda en Cancelada
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CancelarVenta_Confirmada_Efectivo_VentaQuedaCancelada()
    {
        var (producto, cliente) = await SeedBaseAsync();
        var venta = await SeedVentaAsync(cliente.Id, producto.Id);
        await _ventaService.ConfirmarVentaAsync(venta.Id);

        var resultado = await _ventaService.CancelarVentaAsync(venta.Id, "Estado final");

        Assert.True(resultado);
        var ventaDb = await _context.Ventas.AsNoTracking().FirstAsync(v => v.Id == venta.Id);
        Assert.Equal(EstadoVenta.Cancelada, ventaDb.Estado);
        Assert.NotNull(ventaDb.FechaCancelacion);
        Assert.Equal("Estado final", ventaDb.MotivoCancelacion);
    }

    // -------------------------------------------------------------------------
    // 9. CajaService aislado: RegistrarContramovimientoVentaAsync con ingreso existente
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RegistrarContramovimientoVenta_IngresoExistente_CreaEgresoReversionVenta()
    {
        var (producto, cliente) = await SeedBaseAsync();
        var venta = await SeedVentaAsync(cliente.Id, producto.Id);

        var ingreso = new MovimientoCaja
        {
            AperturaCajaId = _apertura.Id,
            FechaMovimiento = DateTime.UtcNow,
            Tipo = TipoMovimientoCaja.Ingreso,
            Concepto = ConceptoMovimientoCaja.VentaEfectivo,
            TipoPago = TipoPago.Efectivo,
            VentaId = venta.Id,
            Monto = 300m,
            Descripcion = $"Venta {venta.Numero}",
            Referencia = venta.Numero,
            ReferenciaId = venta.Id,
            Usuario = "testcj"
        };
        _context.MovimientosCaja.Add(ingreso);
        await _context.SaveChangesAsync();

        var resultado = await _cajaService.RegistrarContramovimientoVentaAsync(venta.Id, venta.Numero, "Test cancel", "testcj");

        Assert.NotNull(resultado);
        Assert.Equal(TipoMovimientoCaja.Egreso, resultado.Tipo);
        Assert.Equal(ConceptoMovimientoCaja.ReversionVenta, resultado.Concepto);
        Assert.Equal(300m, resultado.Monto);
        Assert.Equal(venta.Id, resultado.VentaId);
        Assert.Equal(_apertura.Id, resultado.AperturaCajaId);
    }

    // -------------------------------------------------------------------------
    // 10. CajaService aislado: sin ingreso → retorna null
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RegistrarContramovimientoVenta_SinIngreso_RetornaNull()
    {
        var resultado = await _cajaService.RegistrarContramovimientoVentaAsync(99999, "VNULL", "sin caja", "testcj");

        Assert.Null(resultado);
    }

    // -------------------------------------------------------------------------
    // 11. CajaService aislado: guard anti-doble reversión
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RegistrarContramovimientoVenta_YaRevertido_RetornaNull()
    {
        var (producto, cliente) = await SeedBaseAsync();
        var venta = await SeedVentaAsync(cliente.Id, producto.Id);

        var ingreso = new MovimientoCaja
        {
            AperturaCajaId = _apertura.Id,
            FechaMovimiento = DateTime.UtcNow,
            Tipo = TipoMovimientoCaja.Ingreso,
            Concepto = ConceptoMovimientoCaja.VentaEfectivo,
            VentaId = venta.Id,
            Monto = 80m,
            Descripcion = $"Venta {venta.Numero}",
            Referencia = venta.Numero,
            ReferenciaId = venta.Id,
            Usuario = "testcj"
        };
        var egresoPrevio = new MovimientoCaja
        {
            AperturaCajaId = _apertura.Id,
            FechaMovimiento = DateTime.UtcNow,
            Tipo = TipoMovimientoCaja.Egreso,
            Concepto = ConceptoMovimientoCaja.ReversionVenta,
            VentaId = venta.Id,
            Monto = 80m,
            Descripcion = $"Reversión {venta.Numero}",
            Referencia = venta.Numero,
            ReferenciaId = venta.Id,
            Usuario = "testcj"
        };
        _context.MovimientosCaja.AddRange(ingreso, egresoPrevio);
        await _context.SaveChangesAsync();

        var resultado = await _cajaService.RegistrarContramovimientoVentaAsync(venta.Id, venta.Numero, "duplicado", "testcj");

        Assert.Null(resultado);

        // Sigue habiendo exactamente un egreso, no dos
        var egresos = await _context.MovimientosCaja
            .CountAsync(m => m.VentaId == venta.Id && m.Tipo == TipoMovimientoCaja.Egreso && !m.IsDeleted);
        Assert.Equal(1, egresos);
    }
}
