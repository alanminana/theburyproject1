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
    public Task<MovimientoCaja?> RegistrarMovimientoAnticipoAsync(int creditoId, string creditoNumero, decimal montoAnticipo, string usuario, int? ventaId = null)
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
    public Task<Dictionary<int, DateTime>> ObtenerUltimosCierresPorCajaAsync() => throw new NotImplementedException();
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
    public Task<(IEnumerable<MovimientoStock> Items, int Total, decimal TotalEntradas, decimal TotalSalidas, int TotalAjustes)> SearchPaginadoAsync(int? productoId = null, TipoMovimiento? tipo = null, DateTime? fechaDesde = null, DateTime? fechaHasta = null, string? orderBy = null, string? orderDirection = "desc", int pageNumber = 1, int pageSize = 20) => throw new NotImplementedException();
    public Task<MovimientoStock> CreateAsync(MovimientoStock movimiento) => throw new NotImplementedException();
    public Task<MovimientoStock> RegistrarAjusteAsync(int productoId, TipoMovimiento tipo, decimal cantidad, string? referencia, string motivo, string? usuarioActual = null, int? ordenCompraId = null, Guid? requestId = null) => throw new NotImplementedException();
    public Task<bool> HayStockDisponibleAsync(int productoId, decimal cantidad) => throw new NotImplementedException();
    public Task<(bool Valido, string Mensaje)> ValidarCantidadAsync(decimal cantidad) => throw new NotImplementedException();
}

file sealed class StubValidacionVentaCancelaCredito : IValidacionVentaService
{
    public Task<ValidacionVentaResult> ValidarVentaCreditoPersonalAsync(int clienteId, decimal montoVenta, int? creditoId = null)
        => Task.FromResult(new ValidacionVentaResult { NoViable = false });
    public Task<ValidacionVentaResult> ValidarConfirmacionVentaAsync(int ventaId) => throw new NotImplementedException();
    public Task<PrevalidacionResultViewModel> PrevalidarAsync(int clienteId, decimal monto) => throw new NotImplementedException();
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

        var disponibleAntes = await _disponibleService.CalcularDisponibleAsync(venta.ClienteId!.Value);
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

        var saldoVigente = await _disponibleService.CalcularSaldoVigenteAsync(venta.ClienteId!.Value);
        Assert.Equal(0m, saldoVigente);

        var disponibleDespues = await _disponibleService.CalcularDisponibleAsync(venta.ClienteId!.Value);
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

        var disponibleAntes = await _disponibleService.CalcularDisponibleAsync(venta.ClienteId!.Value);
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

        var saldoVigente = await _disponibleService.CalcularSaldoVigenteAsync(venta.ClienteId!.Value);
        Assert.Equal(0m, saldoVigente);

        var disponibleDespues = await _disponibleService.CalcularDisponibleAsync(venta.ClienteId!.Value);
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

        // Precondición: el crédito quedó Generado con cuotas reales en Credito.Cuotas
        var creditoGenerado = await _context.Creditos.FirstAsync(c => c.Id == credito.Id);
        Assert.Equal(EstadoCredito.Generado, creditoGenerado.Estado);
        var cuotasReales = await _context.Cuotas.Where(c => c.CreditoId == credito.Id).ToListAsync();
        Assert.Equal(4, cuotasReales.Count);

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

    // -------------------------------------------------------------------------
    // QA-FIN-P0 — Una venta financiada con pagos aplicados NO se cancela
    // mientras no exista una reversión auditada. El rechazo no escribe nada.
    // -------------------------------------------------------------------------

    private sealed record EstadoFinanciero(
        EstadoVenta VentaEstado,
        string? VentaMotivo,
        EstadoCredito CreditoEstado,
        decimal CreditoSaldo,
        string CuotasEstado,
        int Pagos,
        string PagosEstado,
        int Movimientos,
        string MovimientosFirma);

    private async Task<EstadoFinanciero> CapturarEstadoAsync(int ventaId, int creditoId)
    {
        _context.ChangeTracker.Clear();
        var v = await _context.Ventas.AsNoTracking().FirstAsync(x => x.Id == ventaId);
        var c = await _context.Creditos.AsNoTracking().FirstAsync(x => x.Id == creditoId);
        var cuotas = await _context.Cuotas.AsNoTracking().Where(x => x.CreditoId == creditoId)
            .OrderBy(x => x.Id).ToListAsync();
        var pagos = await _context.Set<PagoCuota>().AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        var movs = await _context.MovimientosCaja.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        return new EstadoFinanciero(
            v.Estado, v.MotivoCancelacion, c.Estado, c.SaldoPendiente,
            string.Join(",", cuotas.Select(x => $"{x.Id}:{(int)x.Estado}:{x.MontoPagado}")),
            pagos.Count,
            string.Join(",", pagos.Select(x => $"{x.Id}:{(int)x.Estado}")),
            movs.Count,
            string.Join(",", movs.Select(x => $"{x.Id}:{(int)x.Tipo}:{(int)x.Concepto}:{x.Monto}:{x.IsDeleted}")));
    }

    private async Task<(Venta venta, Credito credito, List<Cuota> cuotas)> SeedVentaFinanciadaConfirmadaAsync()
    {
        await SetLimitePuntaje5Async(LimitePuntaje5);
        var (venta, credito, _) = await SeedVentaCreditoConfigurableAsync(total: 2_000m, cantidadCuotas: 4);
        Assert.True(await _service.ConfirmarVentaCreditoAsync(venta.Id));
        _context.ChangeTracker.Clear();
        var cuotas = await _context.Cuotas.Where(c => c.CreditoId == credito.Id).OrderBy(c => c.NumeroCuota).ToListAsync();
        Assert.Equal(4, cuotas.Count);
        return (venta, credito, cuotas);
    }

    [Fact]
    public async Task CancelarVenta_ConCuotaPagadaEnLedger_RechazaSinModificarNada()
    {
        var (venta, credito, cuotas) = await SeedVentaFinanciadaConfirmadaAsync();
        var cuota = cuotas[0];
        var mov = new MovimientoCaja
        {
            AperturaCajaId = _apertura.Id, Tipo = TipoMovimientoCaja.Ingreso,
            Concepto = ConceptoMovimientoCaja.CobroCuota, Monto = cuota.MontoTotal,
            Descripcion = "Cobro cuota", ReferenciaId = cuota.Id, Usuario = TestUser
        };
        _context.MovimientosCaja.Add(mov);
        await _context.SaveChangesAsync();
        cuota.Estado = EstadoCuota.Pagada;
        cuota.MontoPagado = cuota.MontoTotal;
        _context.Set<PagoCuota>().Add(new PagoCuota
        {
            CuotaId = cuota.Id, FechaPagoComercial = DateOnly.FromDateTime(DateTime.UtcNow),
            ImporteTotal = cuota.MontoTotal, ImporteAplicadoCuota = cuota.MontoTotal,
            MovimientoCajaId = mov.Id, Origen = OrigenPagoCuota.RegistradoPorSistema,
            Estado = EstadoPagoCuota.Aplicado
        });
        await _context.SaveChangesAsync();

        var antes = await CapturarEstadoAsync(venta.Id, credito.Id);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.CancelarVentaAsync(venta.Id, "Intento con cuota pagada"));
        Assert.Contains("pagos", ex.Message, StringComparison.OrdinalIgnoreCase);

        var despues = await CapturarEstadoAsync(venta.Id, credito.Id);
        Assert.Equal(antes, despues);
        Assert.Equal(EstadoVenta.Confirmada, despues.VentaEstado);
        Assert.Null(despues.VentaMotivo);
    }

    [Fact]
    public async Task CancelarVenta_ConCuotaPagadaSinLedger_LegacyMontoPagado_Rechaza()
    {
        var (venta, credito, cuotas) = await SeedVentaFinanciadaConfirmadaAsync();
        cuotas[1].Estado = EstadoCuota.Parcial;
        cuotas[1].MontoPagado = 10m;
        await _context.SaveChangesAsync();

        var antes = await CapturarEstadoAsync(venta.Id, credito.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.CancelarVentaAsync(venta.Id, "Legacy sin ledger"));

        Assert.Equal(antes, await CapturarEstadoAsync(venta.Id, credito.Id));
    }

    [Fact]
    public async Task CancelarVenta_ConCobroCuotaEnCajaSinLedger_Rechaza()
    {
        var (venta, credito, cuotas) = await SeedVentaFinanciadaConfirmadaAsync();
        _context.MovimientosCaja.Add(new MovimientoCaja
        {
            AperturaCajaId = _apertura.Id, Tipo = TipoMovimientoCaja.Ingreso,
            Concepto = ConceptoMovimientoCaja.CobroCuota, Monto = 100m,
            Descripcion = "Cobro cuota huérfano", ReferenciaId = cuotas[2].Id, Usuario = TestUser
        });
        await _context.SaveChangesAsync();

        var antes = await CapturarEstadoAsync(venta.Id, credito.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.CancelarVentaAsync(venta.Id, "Cobro en caja sin ledger"));

        Assert.Equal(antes, await CapturarEstadoAsync(venta.Id, credito.Id));
    }

    [Fact]
    public async Task CancelarVenta_PagoAnuladoEnLedger_NoBloquea()
    {
        var (venta, credito, cuotas) = await SeedVentaFinanciadaConfirmadaAsync();
        _context.Set<PagoCuota>().Add(new PagoCuota
        {
            CuotaId = cuotas[0].Id, FechaPagoComercial = DateOnly.FromDateTime(DateTime.UtcNow),
            ImporteTotal = 50m, Origen = OrigenPagoCuota.RegistradoPorSistema,
            Estado = EstadoPagoCuota.Anulado
        });
        await _context.SaveChangesAsync();

        Assert.True(await _service.CancelarVentaAsync(venta.Id, "Pago ya anulado"));
        Assert.Equal(EstadoVenta.Cancelada, (await CapturarEstadoAsync(venta.Id, credito.Id)).VentaEstado);
    }

    [Fact]
    public async Task CancelarVenta_DosSolicitudesSecuenciales_LaSegundaSeRechazaSinEfectos()
    {
        var (venta, credito, _) = await SeedVentaFinanciadaConfirmadaAsync();

        Assert.True(await _service.CancelarVentaAsync(venta.Id, "Primera"));
        var tras1 = await CapturarEstadoAsync(venta.Id, credito.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.CancelarVentaAsync(venta.Id, "Segunda"));

        var tras2 = await CapturarEstadoAsync(venta.Id, credito.Id);
        Assert.Equal(tras1, tras2);
        Assert.Equal("Primera", tras2.VentaMotivo);
    }
}

file sealed class StubAlertaStockServiceCancelaCredito : IAlertaStockService
{
    public Task<int> VerificarYGenerarAlertasAsync(IEnumerable<int> productoIds) => Task.FromResult(0);
    public Task<int> GenerarAlertasStockBajoAsync(CancellationToken ct = default) => Task.FromResult(0);
    public Task<List<AlertaStock>> GetAlertasPendientesAsync() => throw new NotImplementedException();
    public Task<PaginatedResult<AlertaStockViewModel>> BuscarAsync(AlertaStockFiltroViewModel filtro) => throw new NotImplementedException();
    public Task<(int Pendientes, int Criticas)> ContarPorEstadoAsync(AlertaStockFiltroViewModel filtro) => throw new NotImplementedException();
    public Task<AlertaStockViewModel?> GetByIdAsync(int id) => throw new NotImplementedException();
    public Task<bool> ResolverAlertaAsync(int id, string usuarioResolucion, string? observaciones = null, byte[]? rowVersion = null) => throw new NotImplementedException();
    public Task<bool> IgnorarAlertaAsync(int id, string usuarioResolucion, string? observaciones = null, byte[]? rowVersion = null) => throw new NotImplementedException();
    public Task<bool> MarcarEnProcesoAsync(int id, string usuario, string? observaciones = null, byte[]? rowVersion = null) => throw new NotImplementedException();
    public Task<AlertaStockEstadisticasViewModel> GetEstadisticasAsync() => throw new NotImplementedException();
    public Task<List<AlertaStockViewModel>> GetAlertasByProductoIdAsync(int productoId) => throw new NotImplementedException();
    public Task<AlertaStock?> VerificarYGenerarAlertaAsync(int productoId) => throw new NotImplementedException();
    public Task<int> LimpiarAlertasAntiguasAsync(int diasAntiguedad = 30, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<List<ProductoCriticoViewModel>> GetProductosCriticosAsync() => throw new NotImplementedException();
}

/// <summary>
/// H2-C2 - Vencimiento de borradores de credito abandonados: libera el cupo reutilizando la cancelacion de venta y
/// solo alcanza borradores sin ninguna operacion real.
/// </summary>
public class BorradorCreditoVencimientoTests : IDisposable
{
    private const decimal LimitePuntaje5 = 100_000m;
    private static readonly DateTime Ahora = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);

    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly VentaService _ventaService;
    private readonly BorradorCreditoVencimientoService _service;
    private readonly CreditoDisponibleService _disponible;
    private readonly AperturaCaja _apertura;

    private static int _counter = 900;

    public BorradorCreditoVencimientoTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        var caja = new Caja { Codigo = "C09", Nombre = "Caja Test", IsDeleted = false, RowVersion = new byte[8] };
        _context.Cajas.Add(caja);
        _context.SaveChanges();
        _apertura = new AperturaCaja { CajaId = caja.Id, UsuarioApertura = "u", MontoInicial = 0m, Cerrada = false, RowVersion = new byte[8] };
        _context.AperturasCaja.Add(_apertura);
        _context.SaveChanges();

        var preset = _context.PuntajesCreditoLimite.First(p => p.Puntaje == 5);
        preset.LimiteMonto = LimitePuntaje5;
        _context.SaveChanges();

        _disponible = new CreditoDisponibleService(_context, NullLogger<CreditoDisponibleService>.Instance);
        var mapper = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>(), NullLoggerFactory.Instance).CreateMapper();
        _ventaService = new VentaService(
            _context, mapper, NullLogger<VentaService>.Instance,
            new StubAlertaStockServiceCancelaCredito(), new StubMovimientoStockServiceCancelaCredito(),
            new FinancialCalculationService(), new VentaValidator(),
            new VentaNumberGenerator(_context, NullLogger<VentaNumberGenerator>.Instance),
            new PrecioVigenteResolver(_context), new StubCurrentUserCancelaCredito(),
            new StubValidacionVentaCancelaCredito(), new StubCajaServiceCancelaCredito(_apertura),
            _disponible, new StubContratoVentaCreditoService(existeContratoGenerado: true),
            new StubConfiguracionPagoServiceVenta());
        _service = new BorradorCreditoVencimientoService(
            _context, _ventaService, NullLogger<BorradorCreditoVencimientoService>.Instance);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private async Task<(Venta venta, Credito credito)> SeedBorradorAsync(
        DateTime actividadUtc,
        decimal total = 1_000m,
        EstadoVenta estadoVenta = EstadoVenta.PendienteFinanciacion,
        EstadoCredito estadoCredito = EstadoCredito.PendienteConfiguracion)
    {
        var suffix = Interlocked.Increment(ref _counter).ToString();
        var cliente = new Cliente
        {
            Nombre = "Test", Apellido = "Borrador", TipoDocumento = "DNI", NumeroDocumento = suffix,
            PuntajeCliente = 5, NivelRiesgo = NivelRiesgoCredito.AprobadoTotal, IsDeleted = false
        };
        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync();

        var credito = new Credito
        {
            ClienteId = cliente.Id, Numero = $"CRED{suffix}", Estado = estadoCredito,
            MontoSolicitado = total, MontoAprobado = total, SaldoPendiente = total,
            TasaInteres = 0, CantidadCuotas = 0, FechaSolicitud = actividadUtc, IsDeleted = false
        };
        _context.Creditos.Add(credito);
        await _context.SaveChangesAsync();

        var venta = new Venta
        {
            ClienteId = cliente.Id, CreditoId = credito.Id, Numero = $"VTA{suffix}",
            Estado = estadoVenta, TipoPago = TipoPago.CreditoPersonal, Total = total, IsDeleted = false,
            CreatedAt = actividadUtc,
            FechaConfiguracionCredito = estadoVenta == EstadoVenta.PendienteFinanciacion ? null : actividadUtc
        };
        _context.Ventas.Add(venta);
        await _context.SaveChangesAsync();

        // SaveChanges sella CreatedAt/UpdatedAt con la hora actual: se fija la ultima actividad con ExecuteUpdate.
        await _context.Ventas.Where(v => v.Id == venta.Id).ExecuteUpdateAsync(s => s
            .SetProperty(v => v.CreatedAt, actividadUtc).SetProperty(v => v.UpdatedAt, (DateTime?)null));
        await _context.Creditos.Where(c => c.Id == credito.Id).ExecuteUpdateAsync(s => s
            .SetProperty(c => c.CreatedAt, actividadUtc).SetProperty(c => c.UpdatedAt, (DateTime?)null));
        _context.ChangeTracker.Clear();
        return (venta, credito);
    }

    private async Task SetPlazoAsync(int? dias)
    {
        _context.ConfiguracionesCredito.RemoveRange(_context.ConfiguracionesCredito);
        _context.ConfiguracionesCredito.Add(new ConfiguracionCredito { DiasVencimientoBorradorCredito = dias });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
    }

    private async Task AssertSinCambiosAsync(Venta venta, Credito credito, EstadoVenta estadoVenta, EstadoCredito estadoCredito)
    {
        _context.ChangeTracker.Clear();
        var v = await _context.Ventas.AsNoTracking().FirstAsync(x => x.Id == venta.Id);
        var c = await _context.Creditos.AsNoTracking().FirstAsync(x => x.Id == credito.Id);
        Assert.Equal(estadoVenta, v.Estado);
        Assert.Equal(venta.CreditoId, v.CreditoId);
        Assert.Equal(estadoCredito, c.Estado);
        Assert.Equal(credito.SaldoPendiente, c.SaldoPendiente);
    }

    [Fact]
    public async Task BorradorVencido_SeCancelaYLiberaCupo()
    {
        var (venta, credito) = await SeedBorradorAsync(Ahora.AddDays(-8), total: 4_000m);
        Assert.Equal(4_000m, (await _disponible.CalcularDisponibleAsync(venta.ClienteId!.Value)).SaldoVigente);

        var resultado = await _service.VencerBorradoresAsync(Ahora);

        Assert.True(resultado.Habilitado);
        Assert.Equal(7, resultado.PlazoDias);
        Assert.Equal(new[] { venta.Id }, resultado.VentasVencidasIds);
        _context.ChangeTracker.Clear();
        var v = await _context.Ventas.AsNoTracking().FirstAsync(x => x.Id == venta.Id);
        var c = await _context.Creditos.AsNoTracking().FirstAsync(x => x.Id == credito.Id);
        Assert.Equal(EstadoVenta.Cancelada, v.Estado);
        Assert.NotNull(v.FechaCancelacion);
        Assert.Contains("Borrador de crédito vencido", v.MotivoCancelacion);
        Assert.Null(v.CreditoId);
        Assert.Equal(EstadoCredito.Cancelado, c.Estado);
        Assert.Equal(0m, c.SaldoPendiente);
        var despues = await _disponible.CalcularDisponibleAsync(venta.ClienteId!.Value);
        Assert.Equal(0m, despues.SaldoVigente);
        Assert.Equal(LimitePuntaje5, despues.Disponible);
    }

    [Fact]
    public async Task LimiteExactoDelPlazo_VenceUnSegundoAntesNo()
    {
        var (exacto, creditoExacto) = await SeedBorradorAsync(Ahora.AddDays(-7));
        var (casi, creditoCasi) = await SeedBorradorAsync(Ahora.AddDays(-7).AddSeconds(1));

        var resultado = await _service.VencerBorradoresAsync(Ahora);

        Assert.Equal(new[] { exacto.Id }, resultado.VentasVencidasIds);
        await AssertSinCambiosAsync(casi, creditoCasi, EstadoVenta.PendienteFinanciacion, EstadoCredito.PendienteConfiguracion);
        _context.ChangeTracker.Clear();
        Assert.Equal(EstadoVenta.Cancelada, (await _context.Ventas.AsNoTracking().FirstAsync(x => x.Id == exacto.Id)).Estado);
        Assert.Equal(EstadoCredito.Cancelado, (await _context.Creditos.AsNoTracking().FirstAsync(x => x.Id == creditoExacto.Id)).Estado);
    }

    [Fact]
    public async Task Idempotente_SegundaCorridaNoHaceNada()
    {
        var (venta, credito) = await SeedBorradorAsync(Ahora.AddDays(-10));

        var primera = await _service.VencerBorradoresAsync(Ahora);
        _context.ChangeTracker.Clear();
        var tras1 = await _context.Creditos.AsNoTracking().FirstAsync(x => x.Id == credito.Id);
        var ventaTras1 = await _context.Ventas.AsNoTracking().FirstAsync(x => x.Id == venta.Id);
        var segunda = await _service.VencerBorradoresAsync(Ahora);
        _context.ChangeTracker.Clear();
        var tras2 = await _context.Creditos.AsNoTracking().FirstAsync(x => x.Id == credito.Id);
        var ventaTras2 = await _context.Ventas.AsNoTracking().FirstAsync(x => x.Id == venta.Id);

        Assert.Single(primera.VentasVencidasIds);
        Assert.Empty(segunda.VentasVencidasIds);
        Assert.Equal(0, segunda.Evaluados);
        Assert.Equal(tras1.Observaciones, tras2.Observaciones);
        Assert.Equal(ventaTras1.FechaCancelacion, ventaTras2.FechaCancelacion);
        Assert.Equal(ventaTras1.MotivoCancelacion, ventaTras2.MotivoCancelacion);
    }

    [Fact]
    public async Task SegundaInstanciaConCandidatoViejo_NoCancelaDosVeces()
    {
        // Simula dos instancias: la segunda evaluo al candidato antes de que la primera lo cancelara.
        var (venta, _) = await SeedBorradorAsync(Ahora.AddDays(-10));
        var limite = Ahora.AddDays(-7);

        Assert.True(await _ventaService.VencerBorradorCreditoAsync(venta.Id, limite, "Primera"));
        _context.ChangeTracker.Clear();
        Assert.False(await _ventaService.VencerBorradorCreditoAsync(venta.Id, limite, "Segunda"));

        _context.ChangeTracker.Clear();
        Assert.Equal("Primera", (await _context.Ventas.AsNoTracking().FirstAsync(x => x.Id == venta.Id)).MotivoCancelacion);
    }

    [Fact]
    public async Task BorradorReciente_NoSeToca()
    {
        var (venta, credito) = await SeedBorradorAsync(Ahora.AddDays(-2));

        var resultado = await _service.VencerBorradoresAsync(Ahora);

        Assert.Empty(resultado.VentasVencidasIds);
        await AssertSinCambiosAsync(venta, credito, EstadoVenta.PendienteFinanciacion, EstadoCredito.PendienteConfiguracion);
    }

    [Theory]
    [InlineData(EstadoVenta.Presupuesto, EstadoCredito.Configurado)]
    [InlineData(EstadoVenta.Confirmada, EstadoCredito.Generado)]
    [InlineData(EstadoVenta.Facturada, EstadoCredito.Generado)]
    [InlineData(EstadoVenta.Entregada, EstadoCredito.Activo)]
    public async Task VentasConfiguradasOConfirmadas_NuncaSeTocan(EstadoVenta estadoVenta, EstadoCredito estadoCredito)
    {
        var (venta, credito) = await SeedBorradorAsync(Ahora.AddDays(-90), estadoVenta: estadoVenta, estadoCredito: estadoCredito);

        var resultado = await _service.VencerBorradoresAsync(Ahora);

        Assert.Empty(resultado.VentasVencidasIds);
        await AssertSinCambiosAsync(venta, credito, estadoVenta, estadoCredito);
    }

    [Fact]
    public async Task BorradorConCuotasGeneradas_NoSeToca()
    {
        var (venta, credito) = await SeedBorradorAsync(Ahora.AddDays(-30));
        _context.Cuotas.Add(new Cuota
        {
            CreditoId = credito.Id, NumeroCuota = 1, MontoCapital = 100m, MontoInteres = 0m, MontoTotal = 100m,
            FechaVencimiento = Ahora.AddDays(30), Estado = EstadoCuota.Pendiente
        });
        await _context.SaveChangesAsync();

        var resultado = await _service.VencerBorradoresAsync(Ahora);

        Assert.Empty(resultado.VentasVencidasIds);
        await AssertSinCambiosAsync(venta, credito, EstadoVenta.PendienteFinanciacion, EstadoCredito.PendienteConfiguracion);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BorradorConMovimientoDeCaja_NoSeToca(bool esAnticipoPorCredito)
    {
        var (venta, credito) = await SeedBorradorAsync(Ahora.AddDays(-30));
        _context.MovimientosCaja.Add(new MovimientoCaja
        {
            AperturaCajaId = _apertura.Id, FechaMovimiento = Ahora.AddDays(-29), Tipo = TipoMovimientoCaja.Ingreso,
            Concepto = ConceptoMovimientoCaja.AnticipoCredito, Monto = 100m, Descripcion = "Anticipo",
            Usuario = "u",
            ReferenciaId = esAnticipoPorCredito ? credito.Id : null,
            VentaId = esAnticipoPorCredito ? null : venta.Id
        });
        await _context.SaveChangesAsync();

        var resultado = await _service.VencerBorradoresAsync(Ahora);

        Assert.Empty(resultado.VentasVencidasIds);
        await AssertSinCambiosAsync(venta, credito, EstadoVenta.PendienteFinanciacion, EstadoCredito.PendienteConfiguracion);
    }

    [Fact]
    public async Task BorradorConCreditoConfiguradoEnFecha_NoSeToca()
    {
        var (venta, credito) = await SeedBorradorAsync(Ahora.AddDays(-30));
        await _context.Ventas.Where(v => v.Id == venta.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.FechaConfiguracionCredito, Ahora.AddDays(-29)));

        var resultado = await _service.VencerBorradoresAsync(Ahora);

        Assert.Empty(resultado.VentasVencidasIds);
        await AssertSinCambiosAsync(venta, credito, EstadoVenta.PendienteFinanciacion, EstadoCredito.PendienteConfiguracion);
    }

    [Fact]
    public async Task PlazoConfigurable_RespetaLaConfiguracion()
    {
        await SetPlazoAsync(30);
        var (venta, credito) = await SeedBorradorAsync(Ahora.AddDays(-20));

        var con30 = await _service.VencerBorradoresAsync(Ahora);
        Assert.Equal(30, con30.PlazoDias);
        Assert.Empty(con30.VentasVencidasIds);
        await AssertSinCambiosAsync(venta, credito, EstadoVenta.PendienteFinanciacion, EstadoCredito.PendienteConfiguracion);

        await SetPlazoAsync(15);
        var con15 = await _service.VencerBorradoresAsync(Ahora);
        Assert.Equal(new[] { venta.Id }, con15.VentasVencidasIds);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public async Task PlazoNuloOCero_DesactivaElVencimiento(int? dias)
    {
        await SetPlazoAsync(dias);
        var (venta, credito) = await SeedBorradorAsync(Ahora.AddDays(-365));

        var resultado = await _service.VencerBorradoresAsync(Ahora);

        Assert.False(resultado.Habilitado);
        await AssertSinCambiosAsync(venta, credito, EstadoVenta.PendienteFinanciacion, EstadoCredito.PendienteConfiguracion);
    }
}
