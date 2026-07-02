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
// Stubs mínimos — accesibles solo en este archivo
// ---------------------------------------------------------------------------

file sealed class StubCreditoDisponiblePago : ICreditoDisponibleService
{
    public Task<decimal> ObtenerLimitePorPuntajeAsync(int puntaje, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<decimal> CalcularSaldoVigenteAsync(int clienteId, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<CreditoDisponibleResultado> CalcularDisponibleAsync(int clienteId, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<(bool Ok, List<string> Errores)> GuardarLimitesPorPuntajeAsync(IReadOnlyList<(int Puntaje, decimal LimiteMonto, bool Activo)> items, string usuario) => throw new NotImplementedException();
    public Task<List<PuntajeCreditoLimite>> GetAllLimitesPorPuntajeAsync() => throw new NotImplementedException();
}

file sealed class StubCurrentUserPago : ICurrentUserService
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
// Tests e2e: PagarCuotaAsync → RecalcularSaldo → CalcularSaldoVigente/Disponible
// ---------------------------------------------------------------------------

/// <summary>
/// Verifica que el flujo PagarCuotaAsync → RecalcularSaldoCreditoAsync libera
/// correctamente el disponible reportado por CreditoDisponibleService.
/// Estos tests usan una instancia real de CreditoDisponibleService sobre el
/// mismo DbContext en memoria, sin stubs para las assertions finales.
/// </summary>
public class CreditoServicePagoDisponibleTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly CreditoService _creditoService;
    private readonly CreditoDisponibleService _disponibleService;
    private readonly StubCajaServiceCiclo _cajaStub;

    // Límite configurado en seed para puntaje 5
    private const decimal LimitePuntaje5 = 100_000m;

    public CreditoServicePagoDisponibleTests()
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

        _cajaStub = new StubCajaServiceCiclo();

        _creditoService = new CreditoService(
            _context,
            mapper,
            NullLogger<CreditoService>.Instance,
            new FinancialCalculationService(),
            _cajaStub,
            new StubCreditoDisponiblePago(),
            new StubCurrentUserPago());

        _disponibleService = new CreditoDisponibleService(
            _context,
            NullLogger<CreditoDisponibleService>.Instance);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Fija el mismo límite para todos los puntajes (0-5). PagarCuotaAsync recalcula
    /// PuntajeCliente en tiempo real (FASE 6C), así que el puntaje post-pago puede no ser
    /// el mismo con el que se sembró el cliente. Estos tests validan la mecánica de
    /// saldo/disponible, no el resultado del scoring, por eso el límite debe ser uniforme.
    /// </summary>
    private async Task SetLimitePuntaje5Async(decimal monto)
    {
        var presets = await _context.PuntajesCreditoLimite.ToListAsync();
        foreach (var preset in presets)
        {
            preset.LimiteMonto = monto;
        }
        await _context.SaveChangesAsync();
    }

    private async Task<Cliente> SeedClienteAsync(int puntaje = 5)
    {
        var cliente = new Cliente
        {
            Nombre = "Test",
            Apellido = "Disponible",
            TipoDocumento = "DNI",
            NumeroDocumento = Guid.NewGuid().ToString("N")[..8],
            PuntajeCliente = puntaje
        };
        _context.Set<Cliente>().Add(cliente);
        await _context.SaveChangesAsync();
        return cliente;
    }

    private async Task<Credito> SeedCreditoAsync(int clienteId, decimal saldoPendiente, EstadoCredito estado = EstadoCredito.Activo)
    {
        var credito = new Credito
        {
            Numero = Guid.NewGuid().ToString("N")[..10],
            ClienteId = clienteId,
            Estado = estado,
            MontoSolicitado = saldoPendiente,
            MontoAprobado = saldoPendiente,
            SaldoPendiente = saldoPendiente,
            TasaInteres = 3m,
            CantidadCuotas = 1,
            FechaSolicitud = DateTime.UtcNow
        };
        _context.Set<Credito>().Add(credito);
        await _context.SaveChangesAsync();
        return credito;
    }

    private async Task<Cuota> SeedCuotaAsync(
        int creditoId,
        decimal montoCapital,
        decimal montoInteres,
        decimal montoTotal)
    {
        var cuota = new Cuota
        {
            CreditoId = creditoId,
            NumeroCuota = 1,
            MontoCapital = montoCapital,
            MontoInteres = montoInteres,
            MontoTotal = montoTotal,
            MontoPagado = 0m,
            MontoPunitorio = 0m,
            Estado = EstadoCuota.Pendiente,
            FechaVencimiento = DateTime.UtcNow.AddDays(30)
        };
        _context.Set<Cuota>().Add(cuota);
        await _context.SaveChangesAsync();
        return cuota;
    }

    // -------------------------------------------------------------------------
    // Test 1 — Pago parcial libera disponible proporcionalmente
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PagoParcial_ReduceSaldoPendienteProporcional_YLiberaDisponibleParcialmente()
    {
        // Arrange
        await SetLimitePuntaje5Async(LimitePuntaje5);

        var cliente = await SeedClienteAsync(puntaje: 5);
        // Crédito: capital=800, SaldoPendiente inicial=800
        var credito = await SeedCreditoAsync(cliente.Id, saldoPendiente: 800m, EstadoCredito.Activo);
        // Cuota: capital=800, interés=200, total=1000 → proporción capital = 0.8
        var cuota = await SeedCuotaAsync(credito.Id, montoCapital: 800m, montoInteres: 200m, montoTotal: 1_000m);

        // Estado inicial: saldo vigente = 800, disponible = 99_200
        var disponibleInicial = await _disponibleService.CalcularDisponibleAsync(cliente.Id);
        Assert.Equal(800m, disponibleInicial.SaldoVigente);
        Assert.Equal(LimitePuntaje5 - 800m, disponibleInicial.Disponible);

        // Act — pago parcial de 500 sobre 1000 total
        var resultado = await _creditoService.PagarCuotaAsync(new PagarCuotaViewModel
        {
            CuotaId = cuota.Id,
            MontoPagado = 500m,
            FechaPago = DateTime.UtcNow,
            MedioPago = "Efectivo"
        });

        // Assert — pago registrado
        Assert.True(resultado);

        // Cuota: estado Parcial, MontoPagado = 500
        _context.ChangeTracker.Clear();
        var cuotaBd = await _context.Set<Cuota>().FirstAsync(c => c.Id == cuota.Id);
        Assert.Equal(EstadoCuota.Parcial, cuotaBd.Estado);
        Assert.Equal(500m, cuotaBd.MontoPagado);

        // Crédito: SaldoPendiente recalculado
        // capitalPagadoEstimado = 500 * (800/1000) = 400 → SaldoPendiente = 800 - 400 = 400
        var creditoBd = await _context.Set<Credito>().FirstAsync(c => c.Id == credito.Id);
        Assert.Equal(400m, creditoBd.SaldoPendiente);
        Assert.Equal(EstadoCredito.Activo, creditoBd.Estado);

        // CreditoDisponibleService refleja la liberación proporcional
        var disponiblePost = await _disponibleService.CalcularDisponibleAsync(cliente.Id);
        Assert.Equal(400m, disponiblePost.SaldoVigente);
        Assert.Equal(LimitePuntaje5 - 400m, disponiblePost.Disponible);
    }

    // -------------------------------------------------------------------------
    // Test 2 — Pago total libera disponible completo
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PagoTotal_FinalziaCredito_YLiberaDisponibleCompleto()
    {
        // Arrange
        await SetLimitePuntaje5Async(LimitePuntaje5);

        var cliente = await SeedClienteAsync(puntaje: 5);
        var credito = await SeedCreditoAsync(cliente.Id, saldoPendiente: 800m, EstadoCredito.Activo);
        var cuota = await SeedCuotaAsync(credito.Id, montoCapital: 800m, montoInteres: 200m, montoTotal: 1_000m);

        // Act — pago total
        var resultado = await _creditoService.PagarCuotaAsync(new PagarCuotaViewModel
        {
            CuotaId = cuota.Id,
            MontoPagado = 1_000m,
            FechaPago = DateTime.UtcNow,
            MedioPago = "Efectivo"
        });

        // Assert — pago registrado
        Assert.True(resultado);

        _context.ChangeTracker.Clear();

        // Cuota: Pagada
        var cuotaBd = await _context.Set<Cuota>().FirstAsync(c => c.Id == cuota.Id);
        Assert.Equal(EstadoCuota.Pagada, cuotaBd.Estado);

        // Crédito: SaldoPendiente = 0, Estado = Finalizado
        // capitalPagadoEstimado = 1000 * (800/1000) = 800 → SaldoPendiente = 0
        var creditoBd = await _context.Set<Credito>().FirstAsync(c => c.Id == credito.Id);
        Assert.Equal(0m, creditoBd.SaldoPendiente);
        Assert.Equal(EstadoCredito.Finalizado, creditoBd.Estado);

        // Finalizado queda excluido de EstadosVigentes → SaldoVigente = 0
        var saldoVigente = await _disponibleService.CalcularSaldoVigenteAsync(cliente.Id);
        Assert.Equal(0m, saldoVigente);

        // Disponible = Limite completo
        var disponiblePost = await _disponibleService.CalcularDisponibleAsync(cliente.Id);
        Assert.Equal(0m, disponiblePost.SaldoVigente);
        Assert.Equal(LimitePuntaje5, disponiblePost.Disponible);
    }

    // -------------------------------------------------------------------------
    // Test 3 — Múltiples créditos: pago total de uno libera solo su cupo
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PagoTotal_UnoDeDosCreditos_LiberaSoloCupoDelCreditoPagado()
    {
        // Arrange
        await SetLimitePuntaje5Async(LimitePuntaje5);

        var cliente = await SeedClienteAsync(puntaje: 5);

        // Crédito A: 800 pendiente
        var creditoA = await SeedCreditoAsync(cliente.Id, saldoPendiente: 800m, EstadoCredito.Activo);
        var cuotaA = await SeedCuotaAsync(creditoA.Id, montoCapital: 800m, montoInteres: 200m, montoTotal: 1_000m);

        // Crédito B: 500 pendiente (no se toca)
        var creditoB = await SeedCreditoAsync(cliente.Id, saldoPendiente: 500m, EstadoCredito.Activo);
        await SeedCuotaAsync(creditoB.Id, montoCapital: 500m, montoInteres: 100m, montoTotal: 600m);

        // SaldoVigente inicial = 800 + 500 = 1300
        var disponibleInicial = await _disponibleService.CalcularDisponibleAsync(cliente.Id);
        Assert.Equal(1_300m, disponibleInicial.SaldoVigente);
        Assert.Equal(LimitePuntaje5 - 1_300m, disponibleInicial.Disponible);

        // Act — pagar solo el crédito A completo
        await _creditoService.PagarCuotaAsync(new PagarCuotaViewModel
        {
            CuotaId = cuotaA.Id,
            MontoPagado = 1_000m,
            FechaPago = DateTime.UtcNow,
            MedioPago = "Efectivo"
        });

        // Assert — crédito A Finalizado, crédito B intacto
        _context.ChangeTracker.Clear();
        var creditoABd = await _context.Set<Credito>().FirstAsync(c => c.Id == creditoA.Id);
        Assert.Equal(EstadoCredito.Finalizado, creditoABd.Estado);
        Assert.Equal(0m, creditoABd.SaldoPendiente);

        var creditoBBd = await _context.Set<Credito>().FirstAsync(c => c.Id == creditoB.Id);
        Assert.Equal(EstadoCredito.Activo, creditoBBd.Estado);
        Assert.Equal(500m, creditoBBd.SaldoPendiente);

        // SaldoVigente = solo el B (A es Finalizado y queda excluido)
        var disponiblePost = await _disponibleService.CalcularDisponibleAsync(cliente.Id);
        Assert.Equal(500m, disponiblePost.SaldoVigente);
        Assert.Equal(LimitePuntaje5 - 500m, disponiblePost.Disponible);
    }
}
