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
// Stubs mínimos reutilizados para este archivo
// ---------------------------------------------------------------------------

file sealed class StubCajaServiceConsultas : ICajaService
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

file sealed class StubCreditoDisponibleServiceConsultas : ICreditoDisponibleService
{
    public Task<decimal> ObtenerLimitePorPuntajeAsync(NivelRiesgoCredito puntaje, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<decimal> CalcularSaldoVigenteAsync(int clienteId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<CreditoDisponibleResultado> CalcularDisponibleAsync(int clienteId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<(bool Ok, List<string> Errores)> GuardarLimitesPorPuntajeAsync(IReadOnlyList<(NivelRiesgoCredito Puntaje, decimal LimiteMonto, bool Activo)> items, string usuario) => throw new NotImplementedException();
    public Task<List<PuntajeCreditoLimite>> GetAllLimitesPorPuntajeAsync() => throw new NotImplementedException();
}

file sealed class StubCurrentUserServiceConsultas : ICurrentUserService
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
/// Tests para CreditoService — métodos sin cobertura previa:
///
/// SimularCreditoAsync (pure, sin DB):
/// - Tasa cero → cuota = monto / cuotas
/// - Tasa positiva → cuota > monto/cuotas
/// - TotalAPagar = MontoCuota * CantidadCuotas
/// - TotalIntereses = TotalAPagar - MontoSolicitado (tasa cero → 0)
/// - PlanPagos tiene la cantidad exacta de entradas
/// - Primera cuota del plan: SaldoCapital < MontoSolicitado
/// - Última cuota del plan: SaldoCapital ≈ 0
///
/// AdelantarCuotaAsync (con DB):
/// - Sin cuotas pendientes → retorna false
/// - Paga la ÚLTIMA cuota pendiente, no la primera
/// - Agrega observación "[ADELANTO]"
/// - Cuota queda Pagada si MontoPagado >= MontoTotal
///
/// ActualizarEstadoCuotasAsync (con DB):
/// - Cuotas Pendiente vencidas → Vencida
/// - Cuotas Pendiente no vencidas → sin cambio
/// - Cuotas ya en otro estado → sin cambio
///
/// GetCuotasVencidasAsync (con DB):
/// - Sin vencidas → lista vacía
/// - Vencidas en estados Pendiente/Parcial/Vencida → incluidas
/// - Vencidas en estado Pagada → excluidas
/// - Ordenadas por FechaVencimiento ASC
/// </summary>
public class CreditoServiceConsultasTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly CreditoService _service;

    private static int _counter = 800;

    public CreditoServiceConsultasTests()
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

        _service = new CreditoService(
            _context,
            mapper,
            NullLogger<CreditoService>.Instance,
            new FinancialCalculationService(),
            new StubCajaServiceConsultas(),
            new StubCreditoDisponibleServiceConsultas(),
            new StubCurrentUserServiceConsultas());
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<Cliente> SeedClienteAsync()
    {
        var suffix = Interlocked.Increment(ref _counter).ToString();
        var cliente = new Cliente
        {
            Nombre = "Test",
            Apellido = "Consulta",
            NumeroDocumento = $"9{suffix}"
        };
        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync();
        return cliente;
    }

    private async Task<Credito> SeedCreditoAsync(int clienteId, decimal tasaInteres = 3m)
    {
        var credito = new Credito
        {
            Numero = Guid.NewGuid().ToString("N")[..10],
            ClienteId = clienteId,
            Estado = EstadoCredito.Aprobado,
            MontoSolicitado = 12_000m,
            MontoAprobado = 12_000m,
            SaldoPendiente = 12_000m,
            TasaInteres = tasaInteres,
            CantidadCuotas = 3,
            FechaSolicitud = DateTime.UtcNow
        };
        _context.Creditos.Add(credito);
        await _context.SaveChangesAsync();
        return credito;
    }

    private async Task<Cuota> SeedCuotaAsync(
        int creditoId,
        int numero,
        EstadoCuota estado,
        decimal montoTotal = 1_000m,
        decimal montoCapital = 800m,
        decimal montoInteres = 200m,
        int diasAtrasado = 0)
    {
        var cuota = new Cuota
        {
            CreditoId = creditoId,
            NumeroCuota = numero,
            MontoCapital = montoCapital,
            MontoInteres = montoInteres,
            MontoTotal = montoTotal,
            Estado = estado,
            FechaVencimiento = DateTime.UtcNow.AddDays(-diasAtrasado)
        };
        _context.Cuotas.Add(cuota);
        await _context.SaveChangesAsync();
        return cuota;
    }

    // =========================================================================
    // SimularCreditoAsync — pure (no DB)
    // =========================================================================

    [Fact]
    public async Task Simular_TasaCero_CuotaEsMontoSobreCuotas()
    {
        var modelo = new SimularCreditoViewModel
        {
            MontoSolicitado = 12_000m,
            TasaInteresMensual = 0m,
            CantidadCuotas = 3
        };

        var resultado = await _service.SimularCreditoAsync(modelo);

        Assert.Equal(4_000m, resultado.MontoCuota);
    }

    [Fact]
    public async Task Simular_TasaCero_TotalInteresesEsCero()
    {
        var modelo = new SimularCreditoViewModel
        {
            MontoSolicitado = 12_000m,
            TasaInteresMensual = 0m,
            CantidadCuotas = 3
        };

        var resultado = await _service.SimularCreditoAsync(modelo);

        Assert.Equal(0m, resultado.TotalIntereses);
    }

    [Fact]
    public async Task Simular_TasaPositiva_CuotaMayorQueSinInteres()
    {
        var modelo = new SimularCreditoViewModel
        {
            MontoSolicitado = 12_000m,
            TasaInteresMensual = 0.05m,
            CantidadCuotas = 3
        };

        var resultado = await _service.SimularCreditoAsync(modelo);

        Assert.True(resultado.MontoCuota > 12_000m / 3);
    }

    [Fact]
    public async Task Simular_TotalAPagarEsCuotaTimesCuotas()
    {
        var modelo = new SimularCreditoViewModel
        {
            MontoSolicitado = 10_000m,
            TasaInteresMensual = 0.03m,
            CantidadCuotas = 6
        };

        var resultado = await _service.SimularCreditoAsync(modelo);

        Assert.Equal(resultado.MontoCuota * resultado.CantidadCuotas, resultado.TotalAPagar);
    }

    [Fact]
    public async Task Simular_PlanPagos_TieneCantidadCorrecta()
    {
        var modelo = new SimularCreditoViewModel
        {
            MontoSolicitado = 6_000m,
            TasaInteresMensual = 0.02m,
            CantidadCuotas = 6
        };

        var resultado = await _service.SimularCreditoAsync(modelo);

        Assert.NotNull(resultado.PlanPagos);
        Assert.Equal(6, resultado.PlanPagos!.Count);
    }

    [Fact]
    public async Task Simular_PlanPagos_PrimeraCuotaReduceSaldo()
    {
        var modelo = new SimularCreditoViewModel
        {
            MontoSolicitado = 10_000m,
            TasaInteresMensual = 0.03m,
            CantidadCuotas = 3
        };

        var resultado = await _service.SimularCreditoAsync(modelo);

        Assert.True(resultado.PlanPagos![0].SaldoCapital < modelo.MontoSolicitado);
    }

    [Fact]
    public async Task Simular_PlanPagos_UltimaCuotaSaldoCero()
    {
        var modelo = new SimularCreditoViewModel
        {
            MontoSolicitado = 10_000m,
            TasaInteresMensual = 0.03m,
            CantidadCuotas = 3
        };

        var resultado = await _service.SimularCreditoAsync(modelo);

        var ultimaCuota = resultado.PlanPagos!.Last();
        Assert.Equal(0m, ultimaCuota.SaldoCapital);
    }

    // =========================================================================
    // AdelantarCuotaAsync
    // =========================================================================

    [Fact]
    public async Task Adelantar_SinCuotasPendientes_RetornaFalse()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        // Todas las cuotas pagadas
        await SeedCuotaAsync(credito.Id, 1, EstadoCuota.Pagada);

        var resultado = await _service.AdelantarCuotaAsync(new PagarCuotaViewModel
        {
            CreditoId = credito.Id,
            MontoPagado = 1_000m,
            MedioPago = "Efectivo",
            FechaPago = DateTime.UtcNow
        });

        Assert.False(resultado);
    }

    [Fact]
    public async Task Adelantar_ConVariasPendientes_PagaLaUltima()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        var cuota1 = await SeedCuotaAsync(credito.Id, 1, EstadoCuota.Pendiente);
        var cuota3 = await SeedCuotaAsync(credito.Id, 3, EstadoCuota.Pendiente);

        await _service.AdelantarCuotaAsync(new PagarCuotaViewModel
        {
            CreditoId = credito.Id,
            MontoPagado = 1_000m,
            MedioPago = "Efectivo",
            FechaPago = DateTime.UtcNow
        });

        _context.ChangeTracker.Clear();
        var c1 = await _context.Cuotas.FindAsync(cuota1.Id);
        var c3 = await _context.Cuotas.FindAsync(cuota3.Id);

        // La primera NO fue tocada
        Assert.Equal(EstadoCuota.Pendiente, c1!.Estado);
        // La última fue pagada
        Assert.Equal(EstadoCuota.Pagada, c3!.Estado);
    }

    [Fact]
    public async Task Adelantar_QuedaPagada_SiMontoCubreTotal()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        var cuota = await SeedCuotaAsync(credito.Id, 1, EstadoCuota.Pendiente, montoTotal: 1_000m);

        await _service.AdelantarCuotaAsync(new PagarCuotaViewModel
        {
            CreditoId = credito.Id,
            MontoPagado = 1_000m,
            MedioPago = "Efectivo",
            FechaPago = DateTime.UtcNow
        });

        _context.ChangeTracker.Clear();
        var actualizada = await _context.Cuotas.FindAsync(cuota.Id);
        Assert.Equal(EstadoCuota.Pagada, actualizada!.Estado);
    }

    [Fact]
    public async Task Adelantar_ObservacionContieneEtiquetaAdelanto()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        await SeedCuotaAsync(credito.Id, 1, EstadoCuota.Pendiente, montoTotal: 1_000m);

        await _service.AdelantarCuotaAsync(new PagarCuotaViewModel
        {
            CreditoId = credito.Id,
            MontoPagado = 1_000m,
            MedioPago = "Efectivo",
            FechaPago = DateTime.UtcNow
        });

        _context.ChangeTracker.Clear();
        var cuota = await _context.Cuotas.FirstAsync(c => c.CreditoId == credito.Id);
        Assert.Contains("[ADELANTO]", cuota.Observaciones);
    }

    // =========================================================================
    // ActualizarEstadoCuotasAsync
    // =========================================================================

    [Fact]
    public async Task ActualizarEstados_PendienteVencida_CambiaAVencida()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        var cuota = await SeedCuotaAsync(credito.Id, 1, EstadoCuota.Pendiente, diasAtrasado: 5);

        await _service.ActualizarEstadoCuotasAsync();

        _context.ChangeTracker.Clear();
        var actualizada = await _context.Cuotas.FindAsync(cuota.Id);
        Assert.Equal(EstadoCuota.Vencida, actualizada!.Estado);
    }

    [Fact]
    public async Task ActualizarEstados_PendienteNoVencida_NoSeCambia()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        var cuota = await SeedCuotaAsync(credito.Id, 1, EstadoCuota.Pendiente, diasAtrasado: -30); // futura

        await _service.ActualizarEstadoCuotasAsync();

        _context.ChangeTracker.Clear();
        var noTocada = await _context.Cuotas.FindAsync(cuota.Id);
        Assert.Equal(EstadoCuota.Pendiente, noTocada!.Estado);
    }

    [Fact]
    public async Task ActualizarEstados_ParcialVencida_NoSeCambia()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        // Parcial vencida: el método solo cambia Pendiente → Vencida
        var cuota = await SeedCuotaAsync(credito.Id, 1, EstadoCuota.Parcial, diasAtrasado: 5);

        await _service.ActualizarEstadoCuotasAsync();

        _context.ChangeTracker.Clear();
        var noTocada = await _context.Cuotas.FindAsync(cuota.Id);
        Assert.Equal(EstadoCuota.Parcial, noTocada!.Estado);
    }

    // =========================================================================
    // GetCuotasVencidasAsync
    // =========================================================================

    [Fact]
    public async Task GetCuotasVencidas_SinVencidas_RetornaVacio()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        await SeedCuotaAsync(credito.Id, 1, EstadoCuota.Pendiente, diasAtrasado: -30); // futura

        var resultado = await _service.GetCuotasVencidasAsync();

        Assert.Empty(resultado);
    }

    [Fact]
    public async Task GetCuotasVencidas_PendienteVencida_EstaIncluida()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        await SeedCuotaAsync(credito.Id, 1, EstadoCuota.Pendiente, diasAtrasado: 10);

        var resultado = await _service.GetCuotasVencidasAsync();

        Assert.Single(resultado);
    }

    [Fact]
    public async Task GetCuotasVencidas_PagadaVencida_EstaExcluida()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        await SeedCuotaAsync(credito.Id, 1, EstadoCuota.Pagada, diasAtrasado: 10);

        var resultado = await _service.GetCuotasVencidasAsync();

        Assert.Empty(resultado);
    }

    [Fact]
    public async Task GetCuotasVencidas_VariasVencidas_OrdenPorFechaAsc()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        // Más atrasada primero en la colección (menor fecha)
        await SeedCuotaAsync(credito.Id, 2, EstadoCuota.Vencida, diasAtrasado: 20);
        await SeedCuotaAsync(credito.Id, 1, EstadoCuota.Pendiente, diasAtrasado: 5);

        var resultado = await _service.GetCuotasVencidasAsync();

        Assert.Equal(2, resultado.Count);
        Assert.True(resultado[0].FechaVencimiento <= resultado[1].FechaVencimiento);
    }
}
