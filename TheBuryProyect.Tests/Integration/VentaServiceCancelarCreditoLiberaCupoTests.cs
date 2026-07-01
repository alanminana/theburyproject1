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
// Stubs mínimos — accesibles solo en este archivo
// ---------------------------------------------------------------------------

file sealed class StubCajaServiceCancelaCredito : ICajaService
{
    private readonly AperturaCaja _apertura;
    public StubCajaServiceCancelaCredito(AperturaCaja apertura) => _apertura = apertura;

    public Task<decimal?> ObtenerUltimoEfectivoCierreAsync(int cajaId) => Task.FromResult<decimal?>(null);
    public Task<AperturaCaja?> ObtenerAperturaActivaParaUsuarioAsync(string usuario) => Task.FromResult<AperturaCaja?>(_apertura);
    public Task<MovimientoCaja?> RegistrarContramovimientoVentaAsync(int ventaId, string ventaNumero, string motivo, string usuario)
        => Task.FromResult<MovimientoCaja?>(null);
    public Task<MovimientoCaja?> RegistrarMovimientoAnticipoAsync(int creditoId, string creditoNumero, decimal montoAnticipo, string usuario)
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
    public Task<MovimientoCaja?> RegistrarMovimientoVentaAsync(int ventaId, string ventaNumero, decimal monto, TipoPago tipoPago, string usuario) => throw new NotImplementedException();
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

file sealed class StubMovimientoStockServiceCancelaCredito : IMovimientoStockService
{
    public Task<List<MovimientoStock>> RegistrarSalidasAsync(
        List<(int productoId, decimal cantidad, string? referencia)> salidas,
        string motivo,
        string? usuarioActual = null,
        IReadOnlyList<MovimientoStockCostoLinea>? costos = null)
        => Task.FromResult(new List<MovimientoStock>());

    public Task<List<MovimientoStock>> RegistrarEntradasAsync(
        List<(int productoId, decimal cantidad, string? referencia)> entradas,
        string motivo,
        string? usuarioActual = null,
        int? ordenCompraId = null,
        IReadOnlyList<MovimientoStockCostoLinea>? costos = null)
        => Task.FromResult(new List<MovimientoStock>());

    public Task<IEnumerable<MovimientoStock>> GetAllAsync() => throw new NotImplementedException();
    public Task<MovimientoStock?> GetByIdAsync(int id) => throw new NotImplementedException();
    public Task<IEnumerable<MovimientoStock>> GetByProductoIdAsync(int productoId) => throw new NotImplementedException();
    public Task<IEnumerable<MovimientoStock>> GetByOrdenCompraIdAsync(int ordenCompraId) => throw new NotImplementedException();
    public Task<IEnumerable<MovimientoStock>> GetByTipoAsync(TipoMovimiento tipo) => throw new NotImplementedException();
    public Task<IEnumerable<MovimientoStock>> GetByFechaRangoAsync(DateTime fechaDesde, DateTime fechaHasta) => throw new NotImplementedException();
    public Task<IEnumerable<MovimientoStock>> SearchAsync(int? productoId = null, TipoMovimiento? tipo = null, DateTime? fechaDesde = null, DateTime? fechaHasta = null, string? orderBy = null, string? orderDirection = "desc") => throw new NotImplementedException();
    public Task<MovimientoStock> CreateAsync(MovimientoStock movimiento) => throw new NotImplementedException();
    public Task<MovimientoStock> RegistrarAjusteAsync(int productoId, TipoMovimiento tipo, decimal cantidad, string? referencia, string motivo, string? usuarioActual = null, int? ordenCompraId = null) => throw new NotImplementedException();
    public Task<bool> HayStockDisponibleAsync(int productoId, decimal cantidad) => throw new NotImplementedException();
    public Task<(bool Valido, string Mensaje)> ValidarCantidadAsync(decimal cantidad) => throw new NotImplementedException();
}

file sealed class StubValidacionVentaCancelaCredito : IValidacionVentaService
{
    public Task<ValidacionVentaResult> ValidarVentaCreditoPersonalAsync(int clienteId, decimal montoVenta, int? creditoId = null)
        => Task.FromResult(new ValidacionVentaResult { NoViable = false });
    public Task<ValidacionVentaResult> ValidarConfirmacionVentaAsync(int ventaId) => throw new NotImplementedException();
    public Task<PrevalidacionResultViewModel> PrevalidarAsync(int clienteId, decimal monto) => throw new NotImplementedException();
    public Task<bool> ClientePuedeRecibirCreditoAsync(int clienteId, decimal montoSolicitado) => throw new NotImplementedException();
    public Task<ResumenCrediticioClienteViewModel> ObtenerResumenCrediticioAsync(int clienteId) => throw new NotImplementedException();
}

file sealed class StubCurrentUserCancelaCredito : ICurrentUserService
{
    public string GetUsername() => "testuser";
    public string GetUserId() => "system";
    public bool IsAuthenticated() => true;
    public string? GetEmail() => "test@test.com";
    public bool IsInRole(string role) => false;
    public bool HasPermission(string modulo, string accion) => false;
    public string? GetIpAddress() => "127.0.0.1";
}

// ---------------------------------------------------------------------------

/// <summary>
/// FASE 3C — Cubre el bug de "Credito zombie": al cancelar/rechazar una venta a crédito
/// personal, el Credito asociado debe cancelarse (Estado fuera de EstadosVigentes,
/// SaldoPendiente = 0) para que CreditoDisponibleService libere el cupo. Usa una
/// instancia real de CreditoDisponibleService sobre el mismo DbContext, sin stubs
/// para las assertions de cupo.
/// </summary>
public class VentaServiceCancelarCreditoLiberaCupoTests : IDisposable
{
    private const string TestUser = "testuser";
    private const decimal LimitePuntaje5 = 100_000m;

    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly VentaService _service;
    private readonly CreditoDisponibleService _disponibleService;
    private readonly AperturaCaja _apertura;

    private static int _counter = 400;

    public VentaServiceCancelarCreditoLiberaCupoTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        _apertura = SeedCajaSinc();

        _disponibleService = new CreditoDisponibleService(_context, NullLogger<CreditoDisponibleService>.Instance);

        var mapper = new MapperConfiguration(
                cfg => cfg.AddProfile<MappingProfile>(),
                NullLoggerFactory.Instance)
            .CreateMapper();

        _service = new VentaService(
            _context,
            mapper,
            NullLogger<VentaService>.Instance,
            new StubAlertaStockServiceCancelaCredito(),
            new StubMovimientoStockServiceCancelaCredito(),
            new FinancialCalculationService(),
            new VentaValidator(),
            new VentaNumberGenerator(_context, NullLogger<VentaNumberGenerator>.Instance),
            new PrecioVigenteResolver(_context),
            new StubCurrentUserCancelaCredito(),
            new StubValidacionVentaCancelaCredito(),
            new StubCajaServiceCancelaCredito(_apertura),
            _disponibleService,
            new StubContratoVentaCreditoService(existeContratoGenerado: true),
            new StubConfiguracionPagoServiceVenta());
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // Seed helpers
    // -------------------------------------------------------------------------

    private AperturaCaja SeedCajaSinc()
    {
        var caja = new Caja { Codigo = "C01", Nombre = "Caja Test", IsDeleted = false, RowVersion = new byte[8] };
        _context.Cajas.Add(caja);
        _context.SaveChanges();

        var apertura = new AperturaCaja
        {
            CajaId = caja.Id,
            UsuarioApertura = TestUser,
            MontoInicial = 0m,
            Cerrada = false,
            IsDeleted = false,
            RowVersion = new byte[8]
        };
        _context.AperturasCaja.Add(apertura);
        _context.SaveChanges();
        return apertura;
    }

    private async Task SetLimitePuntaje5Async(decimal monto)
    {
        var preset = await _context.PuntajesCreditoLimite.FirstAsync(p => p.Puntaje == 5);
        preset.LimiteMonto = monto;
        await _context.SaveChangesAsync();
    }

    private async Task<Cliente> SeedClienteAsync(int puntaje = 5)
    {
        var suffix = Interlocked.Increment(ref _counter).ToString();
        var cliente = new Cliente
        {
            Nombre = "Test",
            Apellido = "Cupo",
            TipoDocumento = "DNI",
            NumeroDocumento = suffix,
            PuntajeCliente = puntaje,
            NivelRiesgo = NivelRiesgoCredito.AprobadoTotal,
            IsDeleted = false
        };
        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync();
        return cliente;
    }

    /// <summary>
    /// Siembra una venta a crédito personal PENDIENTE (no confirmada) con su Credito en
    /// PendienteConfiguracion, tal como lo deja CrearCreditoPendienteParaVentaAsync al crear
    /// la venta real. Sin Detalles: no se ejercitan DevolverStock/DescontarStock.
    /// </summary>
    private async Task<(Venta venta, Credito credito)> SeedVentaCreditoPendienteAsync(decimal total = 1_000m)
    {
        var suffix = Interlocked.Increment(ref _counter).ToString();
        var cliente = await SeedClienteAsync();

        var credito = new Credito
        {
            ClienteId = cliente.Id,
            Numero = $"CRED{suffix}",
            Estado = EstadoCredito.PendienteConfiguracion,
            MontoSolicitado = total,
            MontoAprobado = total,
            SaldoPendiente = total,
            TasaInteres = 0,
            CantidadCuotas = 0,
            FechaSolicitud = DateTime.UtcNow,
            IsDeleted = false
        };
        _context.Creditos.Add(credito);
        await _context.SaveChangesAsync();

        var venta = new Venta
        {
            ClienteId = cliente.Id,
            CreditoId = credito.Id,
            Numero = $"VTA{suffix}",
            Estado = EstadoVenta.PendienteFinanciacion,
            TipoPago = TipoPago.CreditoPersonal,
            Total = total,
            IsDeleted = false
        };
        _context.Ventas.Add(venta);
        await _context.SaveChangesAsync();

        return (venta, credito);
    }

    /// <summary>
    /// Siembra una venta a crédito personal lista para CONFIRMAR (Credito en Configurado),
    /// igual que VentaServiceConfirmarCreditoTests.SeedVentaConfirmable pero sin Detalles.
    /// Total == MontoAprobado ⇒ anticipo = 0 (evita depender de RegistrarMovimientoAnticipoAsync).
    /// </summary>
    private async Task<(Venta venta, Credito credito, Cliente cliente)> SeedVentaCreditoConfigurableAsync(
        decimal total = 1_000m,
        int cantidadCuotas = 3,
        decimal tasaInteres = 3m)
    {
        var suffix = Interlocked.Increment(ref _counter).ToString();
        var cliente = await SeedClienteAsync();

        var credito = new Credito
        {
            ClienteId = cliente.Id,
            Numero = $"CRED{suffix}",
            Estado = EstadoCredito.Configurado,
            TasaInteres = tasaInteres,
            MontoSolicitado = total,
            MontoAprobado = total,
            SaldoPendiente = total,
            CantidadCuotas = cantidadCuotas,
            MontoCuota = 0m,
            TotalAPagar = 0m,
            FechaPrimeraCuota = DateTime.UtcNow.AddMonths(1),
            IsDeleted = false
        };
        _context.Creditos.Add(credito);
        await _context.SaveChangesAsync();

        var venta = new Venta
        {
            ClienteId = cliente.Id,
            CreditoId = credito.Id,
            Numero = $"VTA{suffix}",
            Estado = EstadoVenta.Presupuesto,
            TipoPago = TipoPago.CreditoPersonal,
            Total = total,
            FechaConfiguracionCredito = DateTime.UtcNow,
            IsDeleted = false
        };
        _context.Ventas.Add(venta);
        await _context.SaveChangesAsync();

        return (venta, credito, cliente);
    }

    // -------------------------------------------------------------------------
    // Test 1 — Cancelar venta crédito pendiente libera cupo
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CancelarVentaCreditoPendiente_LiberaCupo()
    {
        await SetLimitePuntaje5Async(LimitePuntaje5);

        var (venta, credito) = await SeedVentaCreditoPendienteAsync(total: 1_000m);

        var disponibleAntes = await _disponibleService.CalcularDisponibleAsync(venta.ClienteId);
        Assert.Equal(1_000m, disponibleAntes.SaldoVigente);
        Assert.Equal(LimitePuntaje5 - 1_000m, disponibleAntes.Disponible);

        var resultado = await _service.CancelarVentaAsync(venta.Id, "Cancelada por el cliente");

        Assert.True(resultado);

        _context.ChangeTracker.Clear();
        var ventaBd = await _context.Ventas.FirstAsync(v => v.Id == venta.Id);
        Assert.Equal(EstadoVenta.Cancelada, ventaBd.Estado);

        var creditoBd = await _context.Creditos.FirstAsync(c => c.Id == credito.Id);
        Assert.Equal(EstadoCredito.Cancelado, creditoBd.Estado);
        Assert.Equal(0m, creditoBd.SaldoPendiente);

        var saldoVigente = await _disponibleService.CalcularSaldoVigenteAsync(venta.ClienteId);
        Assert.Equal(0m, saldoVigente);

        var disponibleDespues = await _disponibleService.CalcularDisponibleAsync(venta.ClienteId);
        Assert.Equal(LimitePuntaje5, disponibleDespues.Disponible);
    }

    // -------------------------------------------------------------------------
    // Test 2 — Rechazar venta crédito pendiente libera cupo
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RechazarVentaCreditoPendiente_LiberaCupo()
    {
        await SetLimitePuntaje5Async(LimitePuntaje5);

        var (venta, credito) = await SeedVentaCreditoPendienteAsync(total: 1_500m);
        venta.EstadoAutorizacion = EstadoAutorizacionVenta.PendienteAutorizacion;
        await _context.SaveChangesAsync();

        var disponibleAntes = await _disponibleService.CalcularDisponibleAsync(venta.ClienteId);
        Assert.Equal(1_500m, disponibleAntes.SaldoVigente);

        var resultado = await _service.RechazarVentaAsync(venta.Id, "supervisor", "No cumple requisitos");

        Assert.True(resultado);

        _context.ChangeTracker.Clear();
        var ventaBd = await _context.Ventas.FirstAsync(v => v.Id == venta.Id);
        Assert.Equal(EstadoAutorizacionVenta.Rechazada, ventaBd.EstadoAutorizacion);
        Assert.Null(ventaBd.CreditoId);

        var creditoBd = await _context.Creditos.FirstAsync(c => c.Id == credito.Id);
        Assert.Equal(EstadoCredito.Cancelado, creditoBd.Estado);
        Assert.Equal(0m, creditoBd.SaldoPendiente);

        var saldoVigente = await _disponibleService.CalcularSaldoVigenteAsync(venta.ClienteId);
        Assert.Equal(0m, saldoVigente);

        var disponibleDespues = await _disponibleService.CalcularDisponibleAsync(venta.ClienteId);
        Assert.Equal(LimitePuntaje5, disponibleDespues.Disponible);
    }

    // -------------------------------------------------------------------------
    // Test 3 — Cancelar venta crédito generada (Credito.Cuotas reales) libera cupo
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CancelarVentaCreditoGenerada_LiberaCupo()
    {
        await SetLimitePuntaje5Async(LimitePuntaje5);

        var (venta, credito, cliente) = await SeedVentaCreditoConfigurableAsync(total: 2_000m, cantidadCuotas: 4);

        var confirmada = await _service.ConfirmarVentaCreditoAsync(venta.Id);
        Assert.True(confirmada);

        _context.ChangeTracker.Clear();

        // Precondición: el crédito quedó Generado con cuotas reales en Credito.Cuotas (no VentaCreditoCuotas)
        var creditoGenerado = await _context.Creditos.FirstAsync(c => c.Id == credito.Id);
        Assert.Equal(EstadoCredito.Generado, creditoGenerado.Estado);
        var cuotasReales = await _context.Cuotas.Where(c => c.CreditoId == credito.Id).ToListAsync();
        Assert.Equal(4, cuotasReales.Count);
        var cuotasVentaLegacy = await _context.VentaCreditoCuotas.Where(c => c.VentaId == venta.Id).ToListAsync();
        Assert.Empty(cuotasVentaLegacy);

        var disponibleAntes = await _disponibleService.CalcularDisponibleAsync(cliente.Id);
        Assert.Equal(2_000m, disponibleAntes.SaldoVigente);
        Assert.Equal(LimitePuntaje5 - 2_000m, disponibleAntes.Disponible);

        // Act — cancelar la venta ya confirmada/generada
        var resultado = await _service.CancelarVentaAsync(venta.Id, "Cliente desiste tras confirmar");

        Assert.True(resultado);

        _context.ChangeTracker.Clear();
        var ventaBd = await _context.Ventas.FirstAsync(v => v.Id == venta.Id);
        Assert.Equal(EstadoVenta.Cancelada, ventaBd.Estado);

        var creditoBd = await _context.Creditos.FirstAsync(c => c.Id == credito.Id);
        Assert.Equal(EstadoCredito.Cancelado, creditoBd.Estado);
        Assert.Equal(0m, creditoBd.SaldoPendiente);

        var cuotasBd = await _context.Cuotas.Where(c => c.CreditoId == credito.Id).ToListAsync();
        Assert.All(cuotasBd, c => Assert.Equal(EstadoCuota.Cancelada, c.Estado));

        var saldoVigente = await _disponibleService.CalcularSaldoVigenteAsync(cliente.Id);
        Assert.Equal(0m, saldoVigente);

        var disponibleDespues = await _disponibleService.CalcularDisponibleAsync(cliente.Id);
        Assert.Equal(LimitePuntaje5, disponibleDespues.Disponible);
    }
}

file sealed class StubAlertaStockServiceCancelaCredito : IAlertaStockService
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
