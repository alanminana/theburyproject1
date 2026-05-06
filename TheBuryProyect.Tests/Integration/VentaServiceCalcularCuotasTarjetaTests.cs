using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Tests de integración directos para VentaService.CalcularCuotasTarjetaAsync.
///
/// Antes de esta fase el método estaba cubierto solo indirectamente (vía stubs de controller).
/// Estos tests caracterizan el contrato real del método contra SQLite in-memory.
///
/// Cubre:
/// - SinInteres: MontoCuota = monto / cuotas, MontoTotal = monto, TasaInteres = 0
/// - ConInteres: sistema francés, MontoTotal = MontoCuota * cuotas (sin redondeo adicional)
/// - ConInteres sin tasa configurada (null): no lanza, montos quedan null
/// - TarjetaId inexistente: lanza InvalidOperationException
/// - SinInteres una cuota: MontoCuota == monto
/// - ConInteres tasa = 0m (HasValue pero cero): FinancialCalculationService divide monto / cuotas
/// </summary>
public class VentaServiceCalcularCuotasTarjetaTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly VentaService _service;

    public VentaServiceCalcularCuotasTarjetaTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        _service = BuildService(_context);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // ── Infraestructura ──────────────────────────────────────────────

    private static VentaService BuildService(AppDbContext ctx) =>
        new VentaService(
            ctx,
            null!,   // IMapper — no usado en CalcularCuotasTarjetaAsync
            NullLogger<VentaService>.Instance,
            null!,   // IAlertaStockService
            null!,   // IMovimientoStockService
            new FinancialCalculationService(),
            null!,   // IVentaValidator
            null!,   // VentaNumberGenerator
            null!,   // IPrecioVigenteResolver
            null!,   // ICurrentUserService
            null!,   // IValidacionVentaService
            null!,   // ICajaService
            null!,   // ICreditoDisponibleService
            new StubContratoVentaCreditoService(),
            new StubConfiguracionPagoServiceVenta());

    private async Task<ConfiguracionTarjeta> SeedTarjeta(
        TipoCuotaTarjeta? tipoCuota,
        decimal? tasaInteresesMensual = null,
        int maxCuotas = 12,
        string nombre = "Visa Test",
        bool activa = true)
    {
        var configPago = new ConfiguracionPago
        {
            TipoPago = TipoPago.TarjetaCredito,
            Nombre = "Tarjeta Crédito Test"
        };
        _context.ConfiguracionesPago.Add(configPago);
        await _context.SaveChangesAsync();

        var tarjeta = new ConfiguracionTarjeta
        {
            ConfiguracionPagoId = configPago.Id,
            NombreTarjeta = nombre,
            TipoTarjeta = TipoTarjeta.Credito,
            Activa = activa,
            PermiteCuotas = true,
            CantidadMaximaCuotas = maxCuotas,
            TipoCuota = tipoCuota,
            TasaInteresesMensual = tasaInteresesMensual
        };
        _context.ConfiguracionesTarjeta.Add(tarjeta);
        await _context.SaveChangesAsync();
        return tarjeta;
    }

    // ── Tests ────────────────────────────────────────────────────────

    // -------------------------------------------------------------------------
    // 1. SinInteres: MontoCuota = monto / cuotas, MontoTotal = monto, TasaInteres = 0
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CalcularCuotas_SinInteres_CalculaMontoCuotaYTotalSinInteres()
    {
        var tarjeta = await SeedTarjeta(TipoCuotaTarjeta.SinInteres, maxCuotas: 12);

        var resultado = await _service.CalcularCuotasTarjetaAsync(tarjeta.Id, monto: 12_000m, cuotas: 6);

        Assert.Equal(2_000m, resultado.MontoCuota);
        Assert.Equal(12_000m, resultado.MontoTotalConInteres);
        Assert.Equal(0m, resultado.TasaInteres);
        Assert.Equal(TipoCuotaTarjeta.SinInteres, resultado.TipoCuota);
        Assert.Equal(tarjeta.Id, resultado.ConfiguracionTarjetaId);
        Assert.Equal("Visa Test", resultado.NombreTarjeta);
        Assert.Equal(6, resultado.CantidadCuotas);
    }

    // -------------------------------------------------------------------------
    // 2. ConInteres: sistema francés, MontoTotal = MontoCuota * cuotas
    //    PMT(12.000, 10% mensual, 6 cuotas) ≈ 2.755,29
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CalcularCuotas_ConInteres_CalculaSistemaFrances()
    {
        var tarjeta = await SeedTarjeta(TipoCuotaTarjeta.ConInteres, tasaInteresesMensual: 10m, maxCuotas: 12);

        var resultado = await _service.CalcularCuotasTarjetaAsync(tarjeta.Id, monto: 12_000m, cuotas: 6);

        Assert.Equal(10m, resultado.TasaInteres);
        Assert.Equal(TipoCuotaTarjeta.ConInteres, resultado.TipoCuota);
        Assert.NotNull(resultado.MontoCuota);
        Assert.NotNull(resultado.MontoTotalConInteres);
        // Tolerancia ±1 por precisión double→decimal en Math.Pow
        Assert.InRange(resultado.MontoCuota!.Value, 2_754m, 2_757m);
        // MontoTotal es exactamente MontoCuota * cuotas (VentaService no redondea de nuevo)
        Assert.Equal(resultado.MontoCuota.Value * 6, resultado.MontoTotalConInteres!.Value);
        // El cliente paga más que el capital original
        Assert.True(resultado.MontoTotalConInteres.Value > 12_000m);
    }

    // -------------------------------------------------------------------------
    // 3. ConInteres sin tasa (null): falla explícitamente para evitar previews vacíos.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CalcularCuotas_ConInteresSinTasa_LanzaInvalidOperationException()
    {
        var tarjeta = await SeedTarjeta(TipoCuotaTarjeta.ConInteres, tasaInteresesMensual: null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.CalcularCuotasTarjetaAsync(tarjeta.Id, monto: 12_000m, cuotas: 6));

        Assert.Equal("La tarjeta con interés no tiene tasa configurada", ex.Message);
    }

    // -------------------------------------------------------------------------
    // 4. TarjetaId inexistente → InvalidOperationException
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CalcularCuotas_TarjetaInexistente_LanzaInvalidOperationException()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.CalcularCuotasTarjetaAsync(int.MaxValue, monto: 1_000m, cuotas: 3));

        Assert.Contains("no encontrada", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CalcularCuotas_TarjetaInactiva_LanzaInvalidOperationException()
    {
        var tarjeta = await SeedTarjeta(
            TipoCuotaTarjeta.SinInteres,
            nombre: "Visa Inactiva",
            activa: false);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.CalcularCuotasTarjetaAsync(tarjeta.Id, monto: 1_000m, cuotas: 3));

        Assert.Contains("no está disponible", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // -------------------------------------------------------------------------
    // 5. SinInteres una cuota: MontoCuota == monto (identidad)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CalcularCuotas_SinInteres_UnaCuota_MontoCuotaIgualAlMonto()
    {
        var tarjeta = await SeedTarjeta(TipoCuotaTarjeta.SinInteres, maxCuotas: 12);

        var resultado = await _service.CalcularCuotasTarjetaAsync(tarjeta.Id, monto: 5_000m, cuotas: 1);

        Assert.Equal(5_000m, resultado.MontoCuota);
        Assert.Equal(5_000m, resultado.MontoTotalConInteres);
        Assert.Equal(0m, resultado.TasaInteres);
        Assert.Equal(1, resultado.CantidadCuotas);
    }

    // -------------------------------------------------------------------------
    // 6. ConInteres con TasaInteresesMensual = 0m (HasValue pero cero):
    //    el else-if entra, tasaDecimal = 0/100 = 0, FinancialService divide monto/cuotas.
    //    Produce el mismo resultado numérico que SinInteres pero con TipoCuota = ConInteres.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CalcularCuotas_ConInteresTasaCero_UsaFinancialServiceYDivideMontoEnCuotas()
    {
        var tarjeta = await SeedTarjeta(TipoCuotaTarjeta.ConInteres, tasaInteresesMensual: 0m, maxCuotas: 6);

        var resultado = await _service.CalcularCuotasTarjetaAsync(tarjeta.Id, monto: 6_000m, cuotas: 3);

        Assert.Equal(0m, resultado.TasaInteres);
        Assert.Equal(TipoCuotaTarjeta.ConInteres, resultado.TipoCuota);
        Assert.NotNull(resultado.MontoCuota);
        Assert.NotNull(resultado.MontoTotalConInteres);
        Assert.Equal(2_000m, resultado.MontoCuota!.Value);
        Assert.Equal(6_000m, resultado.MontoTotalConInteres!.Value);
    }
}
