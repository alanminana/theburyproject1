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
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Integration;

// ---------------------------------------------------------------------------
// Stubs mínimos para CreditoService (solo métodos usados por PagarCuotaAsync).
// Duplicados a propósito respecto de CreditoServicePunitorioTests: son `file`
// (scope por archivo) y este archivo cubre un contrato distinto (FASE 6B).
// ---------------------------------------------------------------------------

file sealed class StubCajaServicePuntaje : ICajaService
{
    public Task<decimal?> ObtenerUltimoEfectivoCierreAsync(int cajaId) => Task.FromResult<decimal?>(null);
    public AperturaCaja? AperturaActivaParaVenta { get; set; } = new() { Id = 1 };

    public Task<MovimientoCaja?> RegistrarMovimientoCuotaAsync(
        int cuotaId, string creditoNumero, int numeroCuota,
        decimal monto, string medioPago, string usuario)
        => Task.FromResult<MovimientoCaja?>(new MovimientoCaja());

    public Task<AperturaCaja?> ObtenerAperturaActivaParaUsuarioAsync(string usuario) => throw new NotImplementedException();
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
    public Task<AperturaCaja?> ObtenerAperturaActivaParaVentaAsync() => Task.FromResult(AperturaActivaParaVenta);
    public Task<MovimientoCaja?> RegistrarMovimientoAnticipoAsync(int creditoId, string creditoNumero, decimal montoAnticipo, string usuario) => throw new NotImplementedException();
    public Task<MovimientoCaja> RegistrarMovimientoDevolucionAsync(int devolucionId, int ventaId, string ventaNumero, string devolucionNumero, decimal monto, string usuario) => throw new NotImplementedException();
    public Task<MovimientoCaja?> RegistrarContramovimientoVentaAsync(int ventaId, string ventaNumero, string motivo, string usuario) => throw new NotImplementedException();
    public Task<CierreCaja> CerrarCajaAsync(CerrarCajaViewModel model, string usuario) => throw new NotImplementedException();
    public Task<CierreCaja?> ObtenerCierrePorIdAsync(int id) => throw new NotImplementedException();
    public Task<List<CierreCaja>> ObtenerHistorialCierresAsync(int? cajaId = null, DateTime? fechaDesde = null, DateTime? fechaHasta = null) => throw new NotImplementedException();
    public Task<DetallesAperturaViewModel> ObtenerDetallesAperturaAsync(int aperturaId) => throw new NotImplementedException();
    public Task<ReporteCajaViewModel> GenerarReporteCajaAsync(DateTime fechaDesde, DateTime fechaHasta, int? cajaId = null) => throw new NotImplementedException();
    public Task<HistorialCierresViewModel> ObtenerEstadisticasCierresAsync(int? cajaId = null, DateTime? fechaDesde = null, DateTime? fechaHasta = null) => throw new NotImplementedException();
}

file sealed class StubFinancialServicePuntaje : IFinancialCalculationService
{
    public decimal CalcularCuotaSistemaFrances(decimal monto, decimal tasaMensual, int cuotas) => throw new NotImplementedException();
    public decimal CalcularTotalConInteres(decimal monto, decimal tasaMensual, int cuotas) => throw new NotImplementedException();
    public decimal CalcularCFTEA(decimal totalAPagar, decimal montoInicial, int cuotas) => throw new NotImplementedException();
    public decimal CalcularInteresTotal(decimal monto, decimal tasaMensual, int cuotas) => throw new NotImplementedException();
    public decimal ComputePmt(decimal tasaMensual, int cuotas, decimal monto) => throw new NotImplementedException();
    public decimal ComputeFinancedAmount(decimal total, decimal anticipo) => throw new NotImplementedException();
    public decimal CalcularCFTEADesdeTasa(decimal tasaMensual) => throw new NotImplementedException();
    public TheBuryProject.Models.DTOs.SimulacionPlanCreditoDto SimularPlanCredito(
        decimal totalVenta, decimal anticipo, int cuotas, decimal tasaMensual,
        decimal gastosAdministrativos, DateTime fechaPrimeraCuota,
        decimal semaforoRatioVerdeMax = 0.08m,
        decimal semaforoRatioAmarilloMax = 0.15m) => throw new NotImplementedException();
}

file sealed class StubCreditoDisponibleServicePuntaje : ICreditoDisponibleService
{
    public Task<decimal> ObtenerLimitePorPuntajeAsync(int puntaje, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<decimal> CalcularSaldoVigenteAsync(int clienteId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<CreditoDisponibleResultado> CalcularDisponibleAsync(int clienteId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<(bool Ok, List<string> Errores)> GuardarLimitesPorPuntajeAsync(IReadOnlyList<(int Puntaje, decimal LimiteMonto, bool Activo)> items, string usuario) => throw new NotImplementedException();
    public Task<List<PuntajeCreditoLimite>> GetAllLimitesPorPuntajeAsync() => throw new NotImplementedException();
}

file sealed class StubCurrentUserServicePuntaje : ICurrentUserService
{
    public string GetUsername() => "TestUser";
    public string GetUserId() => "system";
    public bool IsAuthenticated() => true;
    public string? GetEmail() => "test@test.com";
    public bool IsInRole(string role) => false;
    public bool HasPermission(string modulo, string accion) => false;
    public string? GetIpAddress() => "127.0.0.1";
}

// ---------------------------------------------------------------------------

/// <summary>
/// FASE 6B — tests de contrato (tests-only) para el recálculo automático de
/// PuntajeCliente cuando se paga una cuota. Definen el comportamiento esperado
/// antes de implementarlo en CreditoService.PagarCuotaAsync (FASE 6C).
///
/// Contrato propuesto:
/// - Origen de auditoría automática: "RecalculoAutomaticoPago".
/// - RegistradoPor: usuario que ejecutó el pago (_currentUserService.GetUsername()).
/// - Se audita en ClientePuntajeHistorial SOLO si PuntajeCliente cambia.
/// - El puntaje esperado se calcula reusando ClienteScoringCalculator (misma
///   fórmula que ClienteScoringService), nunca reimplementado a mano en el test.
/// </summary>
[Trait("Category", "Scoring")]
public sealed class CreditoServicePuntajeClienteRecalculoTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly CreditoService _service;

    public CreditoServicePuntajeClienteRecalculoTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        var mapper = new MapperConfiguration(
                cfg => { cfg.AddProfile<MappingProfile>(); },
                NullLoggerFactory.Instance)
            .CreateMapper();

        _service = new CreditoService(
            _context,
            mapper,
            NullLogger<CreditoService>.Instance,
            new StubFinancialServicePuntaje(),
            new StubCajaServicePuntaje(),
            new StubCreditoDisponibleServicePuntaje(),
            new StubCurrentUserServicePuntaje());
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // Helpers de seed y oráculo
    // -------------------------------------------------------------------------

    private async Task<Cliente> SeedClienteAsync(int puntajeInicial, DateTime createdAt)
    {
        var cliente = new Cliente
        {
            Nombre = "Juan",
            Apellido = "Test",
            TipoDocumento = "DNI",
            NumeroDocumento = Guid.NewGuid().ToString("N")[..8],
            Telefono = "1122334455",
            Domicilio = "Calle Falsa 123",
            CreatedAt = createdAt,
            PuntajeCliente = puntajeInicial
        };
        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync();
        return cliente;
    }

    private async Task<Cuota> SeedCuotaAsync(
        int clienteId,
        DateTime fechaVencimiento,
        decimal montoTotal,
        string numeroCredito)
    {
        var credito = new Credito
        {
            ClienteId = clienteId,
            Numero = numeroCredito,
            Estado = EstadoCredito.Activo,
            TasaInteres = 24m,
            MontoSolicitado = montoTotal,
            MontoAprobado = montoTotal,
            SaldoPendiente = montoTotal,
            CantidadCuotas = 1,
            MontoCuota = montoTotal,
            TotalAPagar = montoTotal
        };
        _context.Creditos.Add(credito);
        await _context.SaveChangesAsync();

        var cuota = new Cuota
        {
            CreditoId = credito.Id,
            NumeroCuota = 1,
            FechaVencimiento = fechaVencimiento,
            MontoTotal = montoTotal,
            MontoCapital = montoTotal * 0.8m,
            MontoInteres = montoTotal * 0.2m,
            MontoPagado = 0m,
            MontoPunitorio = 0m,
            Estado = EstadoCuota.Pendiente,
            IsDeleted = false
        };
        _context.Cuotas.Add(cuota);
        await _context.SaveChangesAsync();
        return cuota;
    }

    /// <summary>
    /// Oráculo: recalcula el puntaje esperado con la MISMA fórmula que usa
    /// ClienteScoringService/ClienteScoringCalculator, a partir del estado real
    /// persistido en la base luego del pago. No reimplementa la fórmula.
    /// </summary>
    private async Task<int> CalcularPuntajeEsperadoAsync(int clienteId)
    {
        var cliente = await _context.Clientes.AsNoTracking().FirstAsync(c => c.Id == clienteId);

        var ventas = await _context.Ventas
            .AsNoTracking()
            .Where(v => v.ClienteId == clienteId && !v.IsDeleted)
            .ToListAsync();

        var creditos = await _context.Creditos
            .AsNoTracking()
            .Include(c => c.Cuotas)
            .Where(c => c.ClienteId == clienteId && !c.IsDeleted)
            .ToListAsync();

        var config = ConfiguracionScoringCliente.CrearDefault();
        var ahora = DateTime.UtcNow;

        var snapshot = ClienteScoringCalculator.CalcularSnapshot(cliente.CreatedAt, ventas, creditos, ahora);
        return ClienteScoringCalculator.CalcularPuntaje(snapshot, cliente.Sueldo, config, ahora);
    }

    // -------------------------------------------------------------------------
    // Tests de contrato
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PagarCuotaAtrasada_RecalculaPuntajeClienteYPenaliza()
    {
        var ahora = DateTime.UtcNow;
        // Antigüedad >= 12 meses: suma el bonus de antigüedad para aislar el
        // efecto del atraso (si penalizara igual sin el bonus, no probaría nada).
        var cliente = await SeedClienteAsync(puntajeInicial: 3, createdAt: ahora.AddDays(-400));
        var cuota = await SeedCuotaAsync(cliente.Id, ahora.AddDays(-10), montoTotal: 1000m, "CRED-ATR-1");

        var pago = new PagarCuotaViewModel
        {
            CuotaId = cuota.Id,
            MontoPagado = 1000m,
            FechaPago = ahora,
            MedioPago = "Efectivo"
        };

        await _service.PagarCuotaAsync(pago);

        var puntajeEsperado = await CalcularPuntajeEsperadoAsync(cliente.Id);
        // Precondición del escenario: con la config default, pagar tarde debe penalizar.
        Assert.True(puntajeEsperado < 3, "El escenario de atraso debe representar una penalización real.");

        var clienteActualizado = await _context.Clientes.AsNoTracking().FirstAsync(c => c.Id == cliente.Id);
        Assert.Equal(puntajeEsperado, clienteActualizado.PuntajeCliente);
    }

    [Fact]
    public async Task PagarCuotaEnTermino_RecalculaPuntajeClienteSinPenalizar()
    {
        var ahora = DateTime.UtcNow;
        var cliente = await SeedClienteAsync(puntajeInicial: 0, createdAt: ahora.AddDays(-5));
        var cuota = await SeedCuotaAsync(cliente.Id, ahora.AddDays(10), montoTotal: 500m, "CRED-TERM-1");

        var pago = new PagarCuotaViewModel
        {
            CuotaId = cuota.Id,
            MontoPagado = 500m,
            FechaPago = ahora,
            MedioPago = "Efectivo"
        };

        await _service.PagarCuotaAsync(pago);

        var puntajeEsperado = await CalcularPuntajeEsperadoAsync(cliente.Id);
        // Precondición del escenario: pagar en término con la config default da bonus, no penaliza.
        Assert.True(puntajeEsperado >= 0, "El escenario en término no debe penalizar por debajo del puntaje inicial.");

        var clienteActualizado = await _context.Clientes.AsNoTracking().FirstAsync(c => c.Id == cliente.Id);
        Assert.Equal(puntajeEsperado, clienteActualizado.PuntajeCliente);
    }

    [Fact]
    public async Task PagarCuota_CambioPuntaje_RegistraHistorialPuntajeCliente()
    {
        var ahora = DateTime.UtcNow;
        var cliente = await SeedClienteAsync(puntajeInicial: 3, createdAt: ahora.AddDays(-400));
        var cuota = await SeedCuotaAsync(cliente.Id, ahora.AddDays(-10), montoTotal: 1000m, "CRED-HIST-1");

        var pago = new PagarCuotaViewModel
        {
            CuotaId = cuota.Id,
            MontoPagado = 1000m,
            FechaPago = ahora,
            MedioPago = "Efectivo"
        };

        await _service.PagarCuotaAsync(pago);

        var puntajeEsperado = await CalcularPuntajeEsperadoAsync(cliente.Id);
        Assert.NotEqual(3, puntajeEsperado); // precondición: debe haber cambio real de puntaje

        var historial = await _context.ClientesPuntajeHistorial
            .AsNoTracking()
            .Where(h => h.ClienteId == cliente.Id)
            .ToListAsync();

        var registro = Assert.Single(historial);
        Assert.Equal(puntajeEsperado, registro.Puntaje);
        Assert.Equal("RecalculoAutomaticoPago", registro.Origen);
        Assert.Equal("TestUser", registro.RegistradoPor);
    }

    [Fact]
    public async Task PagarCuota_SinCambioPuntaje_NoDuplicaHistorial()
    {
        var ahora = DateTime.UtcNow;
        // PuntajeCliente inicial ya coincide con el que resultará del recálculo
        // (cliente nuevo + 1 crédito pagado en término = +2 sobre base 0).
        var cliente = await SeedClienteAsync(puntajeInicial: 2, createdAt: ahora.AddDays(-5));
        var cuota = await SeedCuotaAsync(cliente.Id, ahora.AddDays(10), montoTotal: 500m, "CRED-SINCAMBIO-1");

        var pago = new PagarCuotaViewModel
        {
            CuotaId = cuota.Id,
            MontoPagado = 500m,
            FechaPago = ahora,
            MedioPago = "Efectivo"
        };

        await _service.PagarCuotaAsync(pago);

        var puntajeEsperado = await CalcularPuntajeEsperadoAsync(cliente.Id);
        Assert.Equal(2, puntajeEsperado); // precondición: el escenario no debe cambiar el puntaje

        var historial = await _context.ClientesPuntajeHistorial
            .AsNoTracking()
            .Where(h => h.ClienteId == cliente.Id)
            .ToListAsync();

        Assert.Empty(historial);
    }

    [Fact]
    public async Task PagarCuota_NoTocaBcraGaranteAutorizacion()
    {
        var ahora = DateTime.UtcNow;
        var cliente = await SeedClienteAsync(puntajeInicial: 3, createdAt: ahora.AddDays(-400));

        cliente.SituacionCrediticiaBcra = 1;
        cliente.SituacionCrediticiaDescripcion = "Situación 1 - Normal";
        cliente.SituacionCrediticiaPeriodo = "202606";
        cliente.SituacionCrediticiaUltimaConsultaUtc = ahora.AddDays(-1);
        cliente.SituacionCrediticiaConsultaOk = true;
        cliente.GaranteId = null;
        cliente.NivelRiesgo = NivelRiesgoCredito.AprobadoLimitado;
        cliente.PuntajeRiesgo = 8m;
        cliente.EstadoCrediticio = EstadoCrediticioCliente.Apto;
        await _context.SaveChangesAsync();

        var cuota = await SeedCuotaAsync(cliente.Id, ahora.AddDays(-10), montoTotal: 1000m, "CRED-BCRA-1");

        var pago = new PagarCuotaViewModel
        {
            CuotaId = cuota.Id,
            MontoPagado = 1000m,
            FechaPago = ahora,
            MedioPago = "Efectivo"
        };

        await _service.PagarCuotaAsync(pago);

        var clienteActualizado = await _context.Clientes.AsNoTracking().FirstAsync(c => c.Id == cliente.Id);

        Assert.Equal(1, clienteActualizado.SituacionCrediticiaBcra);
        Assert.Equal("Situación 1 - Normal", clienteActualizado.SituacionCrediticiaDescripcion);
        Assert.Equal("202606", clienteActualizado.SituacionCrediticiaPeriodo);
        Assert.True(clienteActualizado.SituacionCrediticiaConsultaOk);
        Assert.Null(clienteActualizado.GaranteId);
        Assert.Equal(NivelRiesgoCredito.AprobadoLimitado, clienteActualizado.NivelRiesgo);
        Assert.Equal(8m, clienteActualizado.PuntajeRiesgo);
        Assert.Equal(EstadoCrediticioCliente.Apto, clienteActualizado.EstadoCrediticio);
    }
}
