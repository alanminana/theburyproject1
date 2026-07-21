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
// Stubs mínimos para CreditoService (spec 2.4: cobro de 1ª cuota al generar)
// ---------------------------------------------------------------------------

internal sealed class StubCajaServiceCobro1ra : ICajaService
{
    public AperturaCaja? AperturaActivaParaVenta { get; set; } = new() { Id = 1 };
    public int RegistrarMovimientoCuotaCallCount { get; private set; }

    public Task<MovimientoCaja?> RegistrarMovimientoCuotaAsync(
        int cuotaId, string creditoNumero, int numeroCuota,
        decimal monto, string medioPago, string usuario)
    {
        RegistrarMovimientoCuotaCallCount++;
        return Task.FromResult<MovimientoCaja?>(new MovimientoCaja());
    }

    public Task<AperturaCaja?> ObtenerAperturaActivaParaVentaAsync() => Task.FromResult(AperturaActivaParaVenta);

    public Task<decimal?> ObtenerUltimoEfectivoCierreAsync(int cajaId) => Task.FromResult<decimal?>(null);
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

internal sealed class StubFinancialServiceCobro1ra : IFinancialCalculationService
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

internal sealed class StubCreditoDisponibleServiceCobro1ra : ICreditoDisponibleService
{
    public Task<decimal> ObtenerLimitePorPuntajeAsync(int puntaje, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<decimal> CalcularSaldoVigenteAsync(int clienteId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<CreditoDisponibleResultado> CalcularDisponibleAsync(int clienteId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<(bool Ok, List<string> Errores)> GuardarLimitesPorPuntajeAsync(IReadOnlyList<(int Puntaje, decimal LimiteMonto, bool Activo)> items, string usuario) => throw new NotImplementedException();
    public Task<List<PuntajeCreditoLimite>> GetAllLimitesPorPuntajeAsync() => throw new NotImplementedException();
}

internal sealed class StubCurrentUserServiceCobro1ra : ICurrentUserService
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
/// Spec 2.4: CobrarPrimeraCuotaAlGenerarAsync cobra la 1ª cuota solo si vence hoy y está
/// pendiente, reutilizando PagarCuotaAsync (impacta en caja y salda la cuota). En cualquier
/// otro caso devuelve NoAplica sin cobrar.
/// </summary>
public class CreditoServiceCobroPrimeraCuotaTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly StubCajaServiceCobro1ra _caja;
    private readonly CreditoService _service;

    public CreditoServiceCobroPrimeraCuotaTests()
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

        _caja = new StubCajaServiceCobro1ra();
        _service = new CreditoService(
            _context,
            mapper,
            NullLogger<CreditoService>.Instance,
            new StubFinancialServiceCobro1ra(),
            _caja,
            new StubCreditoDisponibleServiceCobro1ra(),
            new StubCurrentUserServiceCobro1ra());
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private async Task<Credito> SeedCreditoConPrimeraCuota(
        DateTime fechaPrimeraCuota,
        EstadoCuota estadoPrimeraCuota = EstadoCuota.Pendiente,
        decimal montoCuota = 100_000m)
    {
        var cliente = new Cliente
        {
            Id = 1,
            Nombre = "Juan",
            Apellido = "Test",
            NumeroDocumento = "12345678",
            IsDeleted = false
        };
        _context.Clientes.Add(cliente);

        var credito = new Credito
        {
            Id = 1,
            ClienteId = 1,
            IsDeleted = false,
            Numero = "CRED0001",
            Estado = EstadoCredito.Activo,
            TasaInteres = 24m,
            MontoSolicitado = 200_000m,
            MontoAprobado = 200_000m,
            SaldoPendiente = 200_000m,
            CantidadCuotas = 2,
            MontoCuota = montoCuota,
            TotalAPagar = montoCuota * 2
        };
        _context.Creditos.Add(credito);

        _context.Cuotas.Add(new Cuota
        {
            Id = 1,
            CreditoId = 1,
            NumeroCuota = 1,
            FechaVencimiento = fechaPrimeraCuota,
            MontoTotal = montoCuota,
            MontoCapital = montoCuota * 0.8m,
            MontoInteres = montoCuota * 0.2m,
            MontoPagado = 0m,
            MontoPunitorio = 0m,
            Estado = estadoPrimeraCuota,
            IsDeleted = false
        });
        _context.Cuotas.Add(new Cuota
        {
            Id = 2,
            CreditoId = 1,
            NumeroCuota = 2,
            FechaVencimiento = fechaPrimeraCuota.AddMonths(1),
            MontoTotal = montoCuota,
            MontoCapital = montoCuota * 0.8m,
            MontoInteres = montoCuota * 0.2m,
            MontoPagado = 0m,
            MontoPunitorio = 0m,
            Estado = EstadoCuota.Pendiente,
            IsDeleted = false
        });

        await _context.SaveChangesAsync();
        return credito;
    }

    [Fact]
    public async Task CobrarPrimeraCuota_VenceHoyPendiente_CobraYSaldaLaCuota()
    {
        var credito = await SeedCreditoConPrimeraCuota(DateTime.Today);

        var resultado = await _service.CobrarPrimeraCuotaAlGenerarAsync(credito.Id, "Efectivo");

        Assert.Equal(EstadoCobroPrimeraCuota.Cobrada, resultado.Estado);
        Assert.Equal(1, resultado.NumeroCuota);
        Assert.Equal(100_000m, resultado.MontoBase);
        Assert.Equal("Efectivo", resultado.MedioPago);
        Assert.Equal(1, _caja.RegistrarMovimientoCuotaCallCount);

        var cuota = await _context.Cuotas.FindAsync(1);
        Assert.NotNull(cuota);
        Assert.Equal(EstadoCuota.Pagada, cuota!.Estado);
        Assert.Equal(100_000m, cuota.MontoPagado);
    }

    [Fact]
    public async Task CobrarPrimeraCuota_VenceManana_NoAplicaYNoCobra()
    {
        var credito = await SeedCreditoConPrimeraCuota(DateTime.Today.AddDays(1));

        var resultado = await _service.CobrarPrimeraCuotaAlGenerarAsync(credito.Id, "Efectivo");

        Assert.Equal(EstadoCobroPrimeraCuota.NoAplica, resultado.Estado);
        Assert.Equal(0, _caja.RegistrarMovimientoCuotaCallCount);

        var cuota = await _context.Cuotas.FindAsync(1);
        Assert.Equal(EstadoCuota.Pendiente, cuota!.Estado);
        Assert.Equal(0m, cuota.MontoPagado);
    }

    [Fact]
    public async Task CobrarPrimeraCuota_PrimeraCuotaYaPagada_NoAplica()
    {
        var credito = await SeedCreditoConPrimeraCuota(DateTime.Today, EstadoCuota.Pagada);

        var resultado = await _service.CobrarPrimeraCuotaAlGenerarAsync(credito.Id, "Efectivo");

        Assert.Equal(EstadoCobroPrimeraCuota.NoAplica, resultado.Estado);
        Assert.Equal(0, _caja.RegistrarMovimientoCuotaCallCount);
    }

    [Fact]
    public async Task CobrarPrimeraCuota_CreditoInexistente_NoAplica()
    {
        var resultado = await _service.CobrarPrimeraCuotaAlGenerarAsync(999, "Efectivo");

        Assert.Equal(EstadoCobroPrimeraCuota.NoAplica, resultado.Estado);
        Assert.Equal(0, _caja.RegistrarMovimientoCuotaCallCount);
    }
}
