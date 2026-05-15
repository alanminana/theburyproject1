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
using TheBuryProject.Tests.Helpers;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Integration;

// ---------------------------------------------------------------------------
// Stubs file-scoped
// ---------------------------------------------------------------------------

file sealed class StubNotifE2E : INotificacionService
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

file sealed class StubAlertaStockE2E : IAlertaStockService
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

file sealed class StubCurrentUserE2E : ICurrentUserService
{
    public string GetUsername() => "testqae2e";
    public string GetUserId() => "system";
    public bool IsAuthenticated() => true;
    public string? GetEmail() => "testqae2e@test.com";
    public bool IsInRole(string role) => false;
    public bool HasPermission(string modulo, string accion) => false;
    public string? GetIpAddress() => "127.0.0.1";
}

file sealed class StubValidacionVentaE2E : IValidacionVentaService
{
    public Task<ValidacionVentaResult> ValidarVentaCreditoPersonalAsync(int clienteId, decimal montoVenta, int? creditoId = null) => Task.FromResult(new ValidacionVentaResult { NoViable = false });
    public Task<ValidacionVentaResult> ValidarConfirmacionVentaAsync(int ventaId) => Task.FromResult(new ValidacionVentaResult { NoViable = false, PendienteRequisitos = false, RequiereAutorizacion = false });
    public Task<PrevalidacionResultViewModel> PrevalidarAsync(int clienteId, decimal monto) => throw new NotImplementedException();
    public Task<bool> ClientePuedeRecibirCreditoAsync(int clienteId, decimal montoSolicitado) => throw new NotImplementedException();
    public Task<ResumenCrediticioClienteViewModel> ObtenerResumenCrediticioAsync(int clienteId) => throw new NotImplementedException();
}

file sealed class StubCreditoDisponibleE2E : ICreditoDisponibleService
{
    public Task<decimal> ObtenerLimitePorPuntajeAsync(NivelRiesgoCredito puntaje, CancellationToken ct = default) => Task.FromResult(0m);
    public Task<decimal> CalcularSaldoVigenteAsync(int clienteId, CancellationToken ct = default) => Task.FromResult(0m);
    public Task<CreditoDisponibleResultado> CalcularDisponibleAsync(int clienteId, CancellationToken ct = default) => Task.FromResult(new CreditoDisponibleResultado { Limite = 0m, Disponible = 999_999m });
    public Task<(bool Ok, List<string> Errores)> GuardarLimitesPorPuntajeAsync(IReadOnlyList<(NivelRiesgoCredito Puntaje, decimal LimiteMonto, bool Activo)> items, string usuario) => throw new NotImplementedException();
    public Task<List<PuntajeCreditoLimite>> GetAllLimitesPorPuntajeAsync() => throw new NotImplementedException();
}

// ---------------------------------------------------------------------------

/// <summary>
/// Tests E2E de integración: flujo completo venta → factura → caja → cancelación (Fase 9.5).
///
/// A diferencia de VentaServiceCancelarCajaTests (que usa stubs para MovimientoStock),
/// estos tests usan MovimientoStockService REAL y ProductoUnidadService REAL para
/// verificar que el estado de stock y trazabilidad se revierten correctamente.
///
/// Contratos verificados:
/// - Flujo completo: confirmar → facturar → cancelar → todas las entidades consistentes
/// - Venta.Estado == Cancelada, Factura.Anulada, FechaAnulacion, MotivoAnulacion
/// - MovimientoCaja ingreso intacto + egreso ReversionVenta con mismo monto
/// - Saldo neto de caja == 0 tras cancelación
/// - Producto.StockActual revertido al valor previo
/// - ProductoUnidad trazable: Vendida → EnStock, VentaDetalleId = null
/// - AnularFacturaAsync manual: deja venta en Confirmada, no la cancela
/// - AnularFacturaAsync manual: ingreso original de caja queda intacto (no ReversionVenta, no modificado)
/// - Diferencia contractual: AnularFactura ≠ CancelarVenta (Fase 9.6)
/// </summary>
public class VentaServiceE2ECancelacionTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly VentaService _ventaService;
    private readonly CajaService _cajaService;
    private readonly AperturaCaja _apertura;

    private static int _counter = 950;

    public VentaServiceE2ECancelacionTests()
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
            new StubNotifE2E());

        _apertura = SeedCajaSinc();

        var movimientoStockService = new MovimientoStockService(
            _context,
            NullLogger<MovimientoStockService>.Instance);

        var productoUnidadService = new ProductoUnidadService(
            _context,
            NullLogger<ProductoUnidadService>.Instance);

        _ventaService = new VentaService(
            _context,
            mapper,
            NullLogger<VentaService>.Instance,
            new StubAlertaStockE2E(),
            movimientoStockService,
            new FinancialCalculationService(),
            new VentaValidator(),
            new VentaNumberGenerator(_context, NullLogger<VentaNumberGenerator>.Instance),
            new PrecioVigenteResolver(_context),
            new StubCurrentUserE2E(),
            new StubValidacionVentaE2E(),
            _cajaService,
            new StubCreditoDisponibleE2E(),
            new StubContratoVentaCreditoService(),
            new StubConfiguracionPagoServiceVenta(),
            productoUnidadService: productoUnidadService);
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
        var caja = new Caja { Codigo = "CJE2E", Nombre = "Caja E2E 9.5", Activa = true, Estado = EstadoCaja.Abierta };
        _context.Cajas.Add(caja);
        _context.SaveChanges();

        var ap = new AperturaCaja
        {
            CajaId = caja.Id,
            UsuarioApertura = "testqae2e",
            MontoInicial = 0m,
            Cerrada = false
        };
        _context.AperturasCaja.Add(ap);
        _context.SaveChanges();
        return ap;
    }

    private async Task<(Producto producto, Cliente cliente)> SeedBaseAsync(
        int stockInicial = 10,
        bool requiereNumeroSerie = false)
    {
        var g = Guid.NewGuid().ToString("N")[..6];
        var cat = new Categoria { Codigo = $"C{g}", Nombre = $"Cat{g}" };
        var marca = new Marca { Codigo = $"M{g}", Nombre = $"Mar{g}" };
        _context.Categorias.Add(cat);
        _context.Marcas.Add(marca);
        await _context.SaveChangesAsync();

        var cliente = new Cliente
        {
            Nombre = "TestE2E",
            Apellido = $"T{g}",
            NumeroDocumento = g,
            NivelRiesgo = NivelRiesgoCredito.AprobadoTotal
        };
        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync();

        var producto = new Producto
        {
            Codigo = $"P{g}",
            Nombre = $"Prod{g}",
            CategoriaId = cat.Id,
            MarcaId = marca.Id,
            PrecioVenta = 100m,
            StockActual = stockInicial,
            RequiereNumeroSerie = requiereNumeroSerie
        };
        _context.Productos.Add(producto);
        await _context.SaveChangesAsync();

        return (producto, cliente);
    }

    private async Task<Venta> SeedVentaAsync(
        int clienteId,
        int productoId,
        TipoPago tipoPago = TipoPago.Efectivo,
        decimal total = 100m,
        int? productoUnidadId = null)
    {
        var n = System.Threading.Interlocked.Increment(ref _counter).ToString();
        var venta = new Venta
        {
            ClienteId = clienteId,
            Numero = $"VE2E{n}",
            Estado = EstadoVenta.Presupuesto,
            TipoPago = tipoPago,
            Total = total,
            AperturaCajaId = _apertura.Id
        };
        _context.Ventas.Add(venta);
        await _context.SaveChangesAsync();

        var detalle = new VentaDetalle
        {
            VentaId = venta.Id,
            ProductoId = productoId,
            Cantidad = 1,
            PrecioUnitario = total,
            Subtotal = total,
            ProductoUnidadId = productoUnidadId
        };
        _context.VentaDetalles.Add(detalle);
        await _context.SaveChangesAsync();

        return venta;
    }

    // -------------------------------------------------------------------------
    // 1. E2E Flujo Completo: confirmar → facturar → cancelar → todas las entidades consistentes
    // -------------------------------------------------------------------------

    [Fact]
    public async Task E2E_VentaFacturada_Cancelada_TodasLasEntidadesConsistentes()
    {
        var (producto, cliente) = await SeedBaseAsync(stockInicial: 10);
        var venta = await SeedVentaAsync(cliente.Id, producto.Id, total: 200m);

        // Confirmar: stock baja, caja registra ingreso
        await _ventaService.ConfirmarVentaAsync(venta.Id);

        var stockTrasConfirmar = await _context.Productos.AsNoTracking()
            .Where(p => p.Id == producto.Id)
            .Select(p => p.StockActual)
            .FirstAsync();
        Assert.Equal(9m, stockTrasConfirmar);

        var ingresoTrasConfirmar = await _context.MovimientosCaja
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.VentaId == venta.Id
                                   && m.Tipo == TipoMovimientoCaja.Ingreso
                                   && !m.IsDeleted);
        Assert.NotNull(ingresoTrasConfirmar);
        Assert.Equal(200m, ingresoTrasConfirmar.Monto);

        // Facturar: estado Facturada, factura creada
        await _ventaService.FacturarVentaAsync(
            venta.Id,
            new FacturaViewModel { Tipo = TipoFactura.B, FechaEmision = DateTime.UtcNow });

        var ventaFacturada = await _context.Ventas.AsNoTracking().FirstAsync(v => v.Id == venta.Id);
        Assert.Equal(EstadoVenta.Facturada, ventaFacturada.Estado);

        var facturaCreada = await _context.Facturas
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.VentaId == venta.Id && !f.IsDeleted && !f.Anulada);
        Assert.NotNull(facturaCreada);

        // Cancelar: verifica todo el estado final
        var motivo = "Cancelación QA E2E 9.5";
        var resultado = await _ventaService.CancelarVentaAsync(venta.Id, motivo);
        Assert.True(resultado);

        // A. Venta cancelada
        var ventaFinal = await _context.Ventas.AsNoTracking().FirstAsync(v => v.Id == venta.Id);
        Assert.Equal(EstadoVenta.Cancelada, ventaFinal.Estado);
        Assert.NotNull(ventaFinal.FechaCancelacion);
        Assert.Equal(motivo, ventaFinal.MotivoCancelacion);

        // B. Factura anulada con fecha y motivo
        var facturaFinal = await _context.Facturas
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.VentaId == venta.Id && !f.IsDeleted);
        Assert.NotNull(facturaFinal);
        Assert.True(facturaFinal.Anulada);
        Assert.NotNull(facturaFinal.FechaAnulacion);
        Assert.Contains("Venta cancelada", facturaFinal.MotivoAnulacion, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(motivo, facturaFinal.MotivoAnulacion, StringComparison.OrdinalIgnoreCase);

        // C. MovimientoCaja de ingreso original intacto
        var ingresoFinal = await _context.MovimientosCaja
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == ingresoTrasConfirmar.Id);
        Assert.NotNull(ingresoFinal);
        Assert.False(ingresoFinal.IsDeleted);
        Assert.Equal(TipoMovimientoCaja.Ingreso, ingresoFinal.Tipo);
        Assert.Equal(200m, ingresoFinal.Monto);

        // D. MovimientoCaja de egreso ReversionVenta creado
        var egreso = await _context.MovimientosCaja
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.VentaId == venta.Id
                                   && m.Tipo == TipoMovimientoCaja.Egreso
                                   && m.Concepto == ConceptoMovimientoCaja.ReversionVenta
                                   && !m.IsDeleted);
        Assert.NotNull(egreso);
        Assert.Equal(200m, egreso.Monto);

        // E. Saldo neto == 0 (ingreso − egreso)
        var movimientos = await _context.MovimientosCaja
            .AsNoTracking()
            .Where(m => m.VentaId == venta.Id && !m.IsDeleted)
            .ToListAsync();
        var saldoNeto = movimientos.Sum(m =>
            m.Tipo == TipoMovimientoCaja.Ingreso ? m.Monto : -m.Monto);
        Assert.Equal(0m, saldoNeto);

        // F. Stock revertido al valor inicial
        var stockFinal = await _context.Productos.AsNoTracking()
            .Where(p => p.Id == producto.Id)
            .Select(p => p.StockActual)
            .FirstAsync();
        Assert.Equal(10m, stockFinal);
    }

    // -------------------------------------------------------------------------
    // 2. E2E con unidad trazable: EnStock → Vendida → EnStock
    // -------------------------------------------------------------------------

    [Fact]
    public async Task E2E_ProductoTrazable_CancelacionRevierteUnidadAEnStock()
    {
        var (producto, cliente) = await SeedBaseAsync(stockInicial: 5, requiereNumeroSerie: true);

        // Crear unidad física en EnStock
        var unidad = new ProductoUnidad
        {
            ProductoId = producto.Id,
            CodigoInternoUnidad = $"U-{Guid.NewGuid():N}"[..12],
            Estado = EstadoUnidad.EnStock,
            FechaIngreso = DateTime.UtcNow
        };
        _context.ProductoUnidades.Add(unidad);
        await _context.SaveChangesAsync();

        // Venta con ProductoUnidadId asignado
        var venta = await SeedVentaAsync(
            cliente.Id,
            producto.Id,
            total: 150m,
            productoUnidadId: unidad.Id);

        // Confirmar: unidad pasa a Vendida
        await _ventaService.ConfirmarVentaAsync(venta.Id);

        var unidadTrasConfirmar = await _context.ProductoUnidades.AsNoTracking()
            .FirstAsync(u => u.Id == unidad.Id);
        Assert.Equal(EstadoUnidad.Vendida, unidadTrasConfirmar.Estado);
        Assert.NotNull(unidadTrasConfirmar.VentaDetalleId);

        // Facturar
        await _ventaService.FacturarVentaAsync(
            venta.Id,
            new FacturaViewModel { Tipo = TipoFactura.B, FechaEmision = DateTime.UtcNow });

        // Cancelar: unidad vuelve a EnStock
        await _ventaService.CancelarVentaAsync(venta.Id, "QA trazabilidad E2E 9.5");

        var unidadFinal = await _context.ProductoUnidades.AsNoTracking()
            .FirstAsync(u => u.Id == unidad.Id);
        Assert.Equal(EstadoUnidad.EnStock, unidadFinal.Estado);
        Assert.Null(unidadFinal.VentaDetalleId);
        Assert.Null(unidadFinal.FechaVenta);

        // Factura anulada, venta cancelada, caja neutralizada
        var ventaFinal = await _context.Ventas.AsNoTracking().FirstAsync(v => v.Id == venta.Id);
        Assert.Equal(EstadoVenta.Cancelada, ventaFinal.Estado);

        var facturaFinal = await _context.Facturas
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.VentaId == venta.Id && !f.IsDeleted);
        Assert.NotNull(facturaFinal);
        Assert.True(facturaFinal.Anulada);

        var saldoNeto = (await _context.MovimientosCaja
            .AsNoTracking()
            .Where(m => m.VentaId == venta.Id && !m.IsDeleted)
            .ToListAsync())
            .Sum(m => m.Tipo == TipoMovimientoCaja.Ingreso ? m.Monto : -m.Monto);
        Assert.Equal(0m, saldoNeto);
    }

    // -------------------------------------------------------------------------
    // 3. AnularFacturaAsync manual: venta queda en Confirmada, no en Cancelada
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AnularFacturaManual_VentaFacturada_VentaQuedaConfirmadaNoLaCancela()
    {
        var (producto, cliente) = await SeedBaseAsync();
        var venta = await SeedVentaAsync(cliente.Id, producto.Id, total: 300m);

        await _ventaService.ConfirmarVentaAsync(venta.Id);
        await _ventaService.FacturarVentaAsync(
            venta.Id,
            new FacturaViewModel { Tipo = TipoFactura.B, FechaEmision = DateTime.UtcNow });

        var factura = await _context.Facturas
            .FirstOrDefaultAsync(f => f.VentaId == venta.Id && !f.IsDeleted && !f.Anulada);
        Assert.NotNull(factura);

        // Anular factura manualmente (no cancela la venta)
        var ventaIdDevuelto = await _ventaService.AnularFacturaAsync(factura.Id, "Anulación manual QA 9.5");

        Assert.Equal(venta.Id, ventaIdDevuelto);

        // Factura queda anulada
        var facturaFinal = await _context.Facturas.AsNoTracking().FirstAsync(f => f.Id == factura.Id);
        Assert.True(facturaFinal.Anulada);
        Assert.NotNull(facturaFinal.FechaAnulacion);

        // Venta vuelve a Confirmada (NO Cancelada)
        var ventaFinal = await _context.Ventas.AsNoTracking().FirstAsync(v => v.Id == venta.Id);
        Assert.Equal(EstadoVenta.Confirmada, ventaFinal.Estado);
        Assert.Null(ventaFinal.FechaCancelacion);

        // Sin contramovimiento de caja (AnularFactura no genera ReversionVenta)
        var egreso = await _context.MovimientosCaja
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.VentaId == venta.Id
                                   && m.Tipo == TipoMovimientoCaja.Egreso
                                   && m.Concepto == ConceptoMovimientoCaja.ReversionVenta
                                   && !m.IsDeleted);
        Assert.Null(egreso);
    }

    [Fact]
    public async Task AnularFacturaManual_IngresoOriginalCaja_QuedaIntacto()
    {
        // Contrato 9.6: AnularFactura NO modifica ni elimina el MovimientoCaja de ingreso original.
        // El cobro sigue vigente — solo el comprobante queda invalidado.
        var (producto, cliente) = await SeedBaseAsync();
        var venta = await SeedVentaAsync(cliente.Id, producto.Id, total: 450m);

        await _ventaService.ConfirmarVentaAsync(venta.Id);

        // Verificar que existe el ingreso de caja tras confirmar
        var ingresoAntes = await _context.MovimientosCaja
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.VentaId == venta.Id
                                   && m.Tipo == TipoMovimientoCaja.Ingreso
                                   && !m.IsDeleted);
        Assert.NotNull(ingresoAntes);
        Assert.Equal(450m, ingresoAntes!.Monto);

        await _ventaService.FacturarVentaAsync(
            venta.Id,
            new FacturaViewModel { Tipo = TipoFactura.B, FechaEmision = DateTime.UtcNow });

        var factura = await _context.Facturas
            .FirstOrDefaultAsync(f => f.VentaId == venta.Id && !f.IsDeleted && !f.Anulada);
        Assert.NotNull(factura);

        // Anular factura manualmente
        await _ventaService.AnularFacturaAsync(factura!.Id, "Anulación manual contrato 9.6");

        // Ingreso original de caja: intacto, mismo monto, no eliminado
        var ingresoFinal = await _context.MovimientosCaja
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == ingresoAntes.Id && !m.IsDeleted);
        Assert.NotNull(ingresoFinal);
        Assert.Equal(450m, ingresoFinal!.Monto);
        Assert.Equal(TipoMovimientoCaja.Ingreso, ingresoFinal.Tipo);

        // Sin ReversionVenta egreso generado
        var reversionVenta = await _context.MovimientosCaja
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.VentaId == venta.Id
                                   && m.Tipo == TipoMovimientoCaja.Egreso
                                   && m.Concepto == ConceptoMovimientoCaja.ReversionVenta
                                   && !m.IsDeleted);
        Assert.Null(reversionVenta);

        // Total movimientos de caja para esta venta: solo el ingreso original
        var totalMovimientos = await _context.MovimientosCaja
            .CountAsync(m => m.VentaId == venta.Id && !m.IsDeleted);
        Assert.Equal(1, totalMovimientos);
    }

    // -------------------------------------------------------------------------
    // 4. Venta Confirmada cancelada: stock revertido exactamente
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CancelarVenta_Confirmada_StockActualRevertidoExactamente()
    {
        var (producto, cliente) = await SeedBaseAsync(stockInicial: 20);
        var venta = await SeedVentaAsync(cliente.Id, producto.Id, total: 100m);

        var stockAntes = await _context.Productos.AsNoTracking()
            .Where(p => p.Id == producto.Id)
            .Select(p => p.StockActual)
            .FirstAsync();
        Assert.Equal(20m, stockAntes);

        await _ventaService.ConfirmarVentaAsync(venta.Id);

        var stockTrasConfirmar = await _context.Productos.AsNoTracking()
            .Where(p => p.Id == producto.Id)
            .Select(p => p.StockActual)
            .FirstAsync();
        Assert.Equal(19m, stockTrasConfirmar);

        await _ventaService.CancelarVentaAsync(venta.Id, "Revertir stock QA 9.5");

        var stockFinal = await _context.Productos.AsNoTracking()
            .Where(p => p.Id == producto.Id)
            .Select(p => p.StockActual)
            .FirstAsync();
        Assert.Equal(20m, stockFinal);
    }

    // -------------------------------------------------------------------------
    // 5. Cancelar venta ya cancelada no duplica movimiento de caja
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CancelarVentaYaCancelada_NoDuplicaContramovimientoCaja()
    {
        var (producto, cliente) = await SeedBaseAsync();
        var venta = await SeedVentaAsync(cliente.Id, producto.Id, total: 80m);

        await _ventaService.ConfirmarVentaAsync(venta.Id);
        await _ventaService.CancelarVentaAsync(venta.Id, "Primera cancelación");

        // Segundo intento debe lanzar por estado Cancelada
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _ventaService.CancelarVentaAsync(venta.Id, "Segunda cancelación"));

        var egresos = await _context.MovimientosCaja
            .CountAsync(m => m.VentaId == venta.Id
                          && m.Tipo == TipoMovimientoCaja.Egreso
                          && m.Concepto == ConceptoMovimientoCaja.ReversionVenta
                          && !m.IsDeleted);

        Assert.Equal(1, egresos);
    }

    // -------------------------------------------------------------------------
    // 6. Cancelar venta confirmada: MovimientosStock - un ingreso de cancelación
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CancelarVenta_Confirmada_RegistraMovimientoStockEntrada()
    {
        var (producto, cliente) = await SeedBaseAsync(stockInicial: 5);
        var venta = await SeedVentaAsync(cliente.Id, producto.Id, total: 100m);

        await _ventaService.ConfirmarVentaAsync(venta.Id);

        var movimientosAntes = await _context.MovimientosStock
            .AsNoTracking()
            .Where(m => m.ProductoId == producto.Id)
            .ToListAsync();
        Assert.Contains(movimientosAntes, m => m.Tipo == TipoMovimiento.Salida);

        await _ventaService.CancelarVentaAsync(venta.Id, "Revertir movimiento stock QA 9.5");

        var movimientosDespues = await _context.MovimientosStock
            .AsNoTracking()
            .Where(m => m.ProductoId == producto.Id)
            .ToListAsync();

        // Debe haber al menos una salida (al confirmar) y una entrada (al cancelar)
        Assert.Contains(movimientosDespues, m => m.Tipo == TipoMovimiento.Salida);
        Assert.Contains(movimientosDespues, m => m.Tipo == TipoMovimiento.Entrada);
    }
}
