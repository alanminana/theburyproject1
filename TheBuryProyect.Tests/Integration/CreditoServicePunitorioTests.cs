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
// Stubs mínimos para CreditoService (solo métodos usados por PagarCuotaAsync)
// ---------------------------------------------------------------------------

file sealed class StubCajaServicePunitorio : ICajaService
{
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
    public Task<MovimientoCaja?> RegistrarMovimientoVentaAsync(int ventaId, string ventaNumero, decimal monto, TipoPago tipoPago, string usuario) => throw new NotImplementedException();
    public Task<AperturaCaja?> ObtenerAperturaActivaParaVentaAsync() => throw new NotImplementedException();
    public Task<MovimientoCaja?> RegistrarMovimientoAnticipoAsync(int creditoId, string creditoNumero, decimal montoAnticipo, string usuario) => throw new NotImplementedException();
    public Task<MovimientoCaja> RegistrarMovimientoDevolucionAsync(int devolucionId, int ventaId, string ventaNumero, string devolucionNumero, decimal monto, string usuario) => throw new NotImplementedException();
    public Task<CierreCaja> CerrarCajaAsync(CerrarCajaViewModel model, string usuario) => throw new NotImplementedException();
    public Task<CierreCaja?> ObtenerCierrePorIdAsync(int id) => throw new NotImplementedException();
    public Task<List<CierreCaja>> ObtenerHistorialCierresAsync(int? cajaId = null, DateTime? fechaDesde = null, DateTime? fechaHasta = null) => throw new NotImplementedException();
    public Task<DetallesAperturaViewModel> ObtenerDetallesAperturaAsync(int aperturaId) => throw new NotImplementedException();
    public Task<ReporteCajaViewModel> GenerarReporteCajaAsync(DateTime fechaDesde, DateTime fechaHasta, int? cajaId = null) => throw new NotImplementedException();
    public Task<HistorialCierresViewModel> ObtenerEstadisticasCierresAsync(int? cajaId = null, DateTime? fechaDesde = null, DateTime? fechaHasta = null) => throw new NotImplementedException();
}

file sealed class StubFinancialService : IFinancialCalculationService
{
    public decimal CalcularCuotaSistemaFrances(decimal monto, decimal tasaMensual, int cuotas) => throw new NotImplementedException();
    public decimal CalcularTotalConInteres(decimal monto, decimal tasaMensual, int cuotas) => throw new NotImplementedException();
    public decimal CalcularCFTEA(decimal totalAPagar, decimal montoInicial, int cuotas) => throw new NotImplementedException();
    public decimal CalcularInteresTotal(decimal monto, decimal tasaMensual, int cuotas) => throw new NotImplementedException();
    public decimal ComputePmt(decimal tasaMensual, int cuotas, decimal monto) => throw new NotImplementedException();
    public decimal ComputeFinancedAmount(decimal total, decimal anticipo) => throw new NotImplementedException();
    public decimal CalcularCFTEADesdeTasa(decimal tasaMensual) => throw new NotImplementedException();
}

file sealed class StubCreditoDisponibleService : ICreditoDisponibleService
{
    public Task<decimal> ObtenerLimitePorPuntajeAsync(NivelRiesgoCredito puntaje, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<decimal> CalcularSaldoVigenteAsync(int clienteId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<CreditoDisponibleResultado> CalcularDisponibleAsync(int clienteId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<(bool Ok, List<string> Errores)> GuardarLimitesPorPuntajeAsync(IReadOnlyList<(NivelRiesgoCredito Puntaje, decimal LimiteMonto, bool Activo)> items, string usuario) => throw new NotImplementedException();
    public Task<List<PuntajeCreditoLimite>> GetAllLimitesPorPuntajeAsync() => throw new NotImplementedException();
}

file sealed class StubCurrentUserService : ICurrentUserService
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
/// Valida que PagarCuotaAsync calcula MontoPunitorio usando la TasaInteres del crédito,
/// alineado con MoraAlertasService.CalcularMora.
/// Fórmula esperada: MontoTotal * (TasaInteres / 100 / 12) * (diasAtraso / 30)
/// </summary>
public class CreditoServicePunitorioTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly CreditoService _service;

    public CreditoServicePunitorioTests()
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
            new StubFinancialService(),
            new StubCajaServicePunitorio(),
            new StubCreditoDisponibleService(),
            new StubCurrentUserService());
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // Helpers de seed
    // -------------------------------------------------------------------------

    private async Task<(Cliente, Credito, Cuota)> SeedCuotaVencida(
        decimal tasaInteresCredito,
        decimal montoTotalCuota,
        int diasVencida)
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
            TasaInteres = tasaInteresCredito,
            MontoSolicitado = 10_000m,
            MontoAprobado = 10_000m,
            SaldoPendiente = montoTotalCuota,
            CantidadCuotas = 12,
            MontoCuota = montoTotalCuota,
            TotalAPagar = montoTotalCuota * 12
        };
        _context.Creditos.Add(credito);

        var fechaVencimiento = DateTime.UtcNow.AddDays(-diasVencida);
        var cuota = new Cuota
        {
            Id = 1,
            CreditoId = 1,
            NumeroCuota = 1,
            FechaVencimiento = fechaVencimiento,
            MontoTotal = montoTotalCuota,
            MontoCapital = montoTotalCuota * 0.8m,
            MontoInteres = montoTotalCuota * 0.2m,
            MontoPagado = 0m,
            MontoPunitorio = 0m,
            Estado = EstadoCuota.Pendiente,
            IsDeleted = false
        };
        _context.Cuotas.Add(cuota);

        await _context.SaveChangesAsync();
        return (cliente, credito, cuota);
    }

    // -------------------------------------------------------------------------
    // Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PagarCuota_MontoPunitorio_UsaTasaInteresDelCredito()
    {
        // Arrange
        // TasaInteres = 24% anual = 2% mensual (caso donde antes coincidía con el hardcode)
        const decimal tasaInteres = 24m;       // 24% anual almacenado en la entidad
        const decimal montoTotal = 1_000m;
        const int diasVencida = 30;

        var (_, _, cuota) = await SeedCuotaVencida(tasaInteres, montoTotal, diasVencida);

        var pago = new PagarCuotaViewModel
        {
            CuotaId = cuota.Id,
            MontoPagado = 9_999m,    // pago alto para que quede Pagada; no es el foco del test
            FechaPago = DateTime.UtcNow,
            MedioPago = "Efectivo"
        };

        // Act
        await _service.PagarCuotaAsync(pago);

        // Assert — fórmula: MontoTotal * (TasaInteres / 100 / 12) * (diasAtraso / 30)
        var cuotaActualizada = await _context.Cuotas.FindAsync(cuota.Id);
        var diasReales = (DateTime.UtcNow - cuota.FechaVencimiento).Days;
        var punitorioEsperado = montoTotal * (tasaInteres / 100m / 12m) * (diasReales / 30m);

        Assert.NotNull(cuotaActualizada);
        // Tolerancia de 0.01 por diferencia de microsegundos entre cálculo del service y el test
        Assert.InRange(cuotaActualizada!.MontoPunitorio,
            punitorioEsperado - 0.01m,
            punitorioEsperado + 0.01m);
    }

    [Fact]
    public async Task PagarCuota_MontoPunitorio_VarìaSegunTasaDelCredito()
    {
        // Arrange: tasa diferente al antiguo hardcode (0.02m = 2%)
        // TasaInteres = 36% anual = 3% mensual → punitorio mayor
        const decimal tasaInteres = 36m;
        const decimal montoTotal = 1_000m;
        const int diasVencida = 30;

        var (_, _, cuota) = await SeedCuotaVencida(tasaInteres, montoTotal, diasVencida);

        var pago = new PagarCuotaViewModel
        {
            CuotaId = cuota.Id,
            MontoPagado = 9_999m,
            FechaPago = DateTime.UtcNow,
            MedioPago = "Efectivo"
        };

        // Act
        await _service.PagarCuotaAsync(pago);

        // Assert — con 36% anual el punitorio debe ser > que con 24% anual
        var cuotaActualizada = await _context.Cuotas.FindAsync(cuota.Id);
        var diasReales = (DateTime.UtcNow - cuota.FechaVencimiento).Days;
        var punitorioEsperado = montoTotal * (tasaInteres / 100m / 12m) * (diasReales / 30m);

        Assert.NotNull(cuotaActualizada);
        Assert.InRange(cuotaActualizada!.MontoPunitorio,
            punitorioEsperado - 0.01m,
            punitorioEsperado + 0.01m);
        // Confirma que usa tasa del crédito: 3% > 2% para mismos días
        Assert.True(cuotaActualizada.MontoPunitorio > montoTotal * 0.02m * (diasReales / 30m),
            "Con tasa 36% anual el punitorio debe ser mayor al calculado con 0.02m fijo");
    }

    [Fact]
    public async Task PagarCuota_SinVencer_PunitorioEsCero()
    {
        // Arrange: cuota con vencimiento en el futuro
        var cliente = new Cliente { Id = 2, Nombre = "Ana", Apellido = "Test", NumeroDocumento = "99999999", IsDeleted = false };
        _context.Clientes.Add(cliente);

        var credito = new Credito
        {
            Id = 2, ClienteId = 2, IsDeleted = false, Numero = "CRED0002",
            Estado = EstadoCredito.Activo, TasaInteres = 24m,
            MontoSolicitado = 5_000m, MontoAprobado = 5_000m, SaldoPendiente = 500m,
            CantidadCuotas = 12, MontoCuota = 500m, TotalAPagar = 6_000m
        };
        _context.Creditos.Add(credito);

        var cuota = new Cuota
        {
            Id = 2, CreditoId = 2, NumeroCuota = 1,
            FechaVencimiento = DateTime.UtcNow.AddDays(10), // no vencida
            MontoTotal = 500m, MontoCapital = 400m, MontoInteres = 100m,
            MontoPagado = 0m, MontoPunitorio = 0m,
            Estado = EstadoCuota.Pendiente, IsDeleted = false
        };
        _context.Cuotas.Add(cuota);
        await _context.SaveChangesAsync();

        var pago = new PagarCuotaViewModel
        {
            CuotaId = 2,
            MontoPagado = 9_999m,
            FechaPago = DateTime.UtcNow,
            MedioPago = "Efectivo"
        };

        // Act
        await _service.PagarCuotaAsync(pago);

        // Assert
        var cuotaActualizada = await _context.Cuotas.FindAsync(2);
        Assert.NotNull(cuotaActualizada);
        Assert.Equal(0m, cuotaActualizada!.MontoPunitorio);
    }
}
