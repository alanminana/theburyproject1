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
    // Devuelve disponible ilimitado para no bloquear los tests de CreateAsync
    public Task<CreditoDisponibleResultado> CalcularDisponibleAsync(int clienteId, CancellationToken cancellationToken = default)
        => Task.FromResult(new CreditoDisponibleResultado { Limite = 1_000_000m, Disponible = 1_000_000m });
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

    private async Task<Credito> SeedCreditoAsync(
        int clienteId,
        decimal tasaInteres = 3m,
        EstadoCredito estado = EstadoCredito.Aprobado,
        decimal saldoPendiente = 12_000m,
        DateTime? fechaAprobacion = null)
    {
        var credito = new Credito
        {
            Numero = Guid.NewGuid().ToString("N")[..10],
            ClienteId = clienteId,
            Estado = estado,
            MontoSolicitado = 12_000m,
            MontoAprobado = 12_000m,
            SaldoPendiente = saldoPendiente,
            TasaInteres = tasaInteres,
            CantidadCuotas = 3,
            FechaSolicitud = DateTime.UtcNow,
            FechaAprobacion = fechaAprobacion
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

        Assert.NotNull(resultado.PlanPagos);
        Assert.True(resultado.PlanPagos[0].SaldoCapital < modelo.MontoSolicitado);
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

        Assert.NotNull(resultado.PlanPagos);
        var ultimaCuota = resultado.PlanPagos.Last();
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
        Assert.NotNull(cuota.Observaciones);
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

    // =========================================================================
    // GetPrimeraCuotaPendienteAsync
    // =========================================================================

    [Fact]
    public async Task GetPrimeraCuotaPendiente_SinCuotas_RetornaNull()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);

        var resultado = await _service.GetPrimeraCuotaPendienteAsync(credito.Id);

        Assert.Null(resultado);
    }

    [Fact]
    public async Task GetPrimeraCuotaPendiente_TodasPagadas_RetornaNull()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        await SeedCuotaAsync(credito.Id, 1, EstadoCuota.Pagada);
        await SeedCuotaAsync(credito.Id, 2, EstadoCuota.Pagada);

        var resultado = await _service.GetPrimeraCuotaPendienteAsync(credito.Id);

        Assert.Null(resultado);
    }

    [Fact]
    public async Task GetPrimeraCuotaPendiente_VariasPendientes_RetornaLaMenorNumero()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        await SeedCuotaAsync(credito.Id, 3, EstadoCuota.Pendiente);
        await SeedCuotaAsync(credito.Id, 1, EstadoCuota.Pendiente);
        await SeedCuotaAsync(credito.Id, 2, EstadoCuota.Pendiente);

        var resultado = await _service.GetPrimeraCuotaPendienteAsync(credito.Id);

        Assert.NotNull(resultado);
        Assert.Equal(1, resultado!.NumeroCuota);
    }

    [Fact]
    public async Task GetPrimeraCuotaPendiente_InclujeVencidaYParcial()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        await SeedCuotaAsync(credito.Id, 1, EstadoCuota.Pagada);
        await SeedCuotaAsync(credito.Id, 2, EstadoCuota.Vencida);  // debe incluirse
        await SeedCuotaAsync(credito.Id, 3, EstadoCuota.Parcial);  // debe incluirse

        var resultado = await _service.GetPrimeraCuotaPendienteAsync(credito.Id);

        Assert.NotNull(resultado);
        Assert.Equal(2, resultado!.NumeroCuota); // Vencida es la primera no-Pagada
    }

    // =========================================================================
    // GetUltimaCuotaPendienteAsync
    // =========================================================================

    [Fact]
    public async Task GetUltimaCuotaPendiente_SinCuotas_RetornaNull()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);

        var resultado = await _service.GetUltimaCuotaPendienteAsync(credito.Id);

        Assert.Null(resultado);
    }

    [Fact]
    public async Task GetUltimaCuotaPendiente_SoloPagadas_RetornaNull()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        await SeedCuotaAsync(credito.Id, 1, EstadoCuota.Pagada);

        var resultado = await _service.GetUltimaCuotaPendienteAsync(credito.Id);

        Assert.Null(resultado);
    }

    [Fact]
    public async Task GetUltimaCuotaPendiente_VariasPendientes_RetornaLaMayorNumero()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        await SeedCuotaAsync(credito.Id, 1, EstadoCuota.Pendiente);
        await SeedCuotaAsync(credito.Id, 2, EstadoCuota.Pendiente);
        await SeedCuotaAsync(credito.Id, 3, EstadoCuota.Pendiente);

        var resultado = await _service.GetUltimaCuotaPendienteAsync(credito.Id);

        Assert.NotNull(resultado);
        Assert.Equal(3, resultado!.NumeroCuota);
    }

    [Fact]
    public async Task GetUltimaCuotaPendiente_VencidaNoIncluida_SoloPendienteYParcial()
    {
        // GetUltimaCuotaPendienteAsync solo filtra Pendiente | Parcial (no Vencida)
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        await SeedCuotaAsync(credito.Id, 1, EstadoCuota.Parcial);
        await SeedCuotaAsync(credito.Id, 2, EstadoCuota.Vencida);  // Vencida no cuenta aquí
        await SeedCuotaAsync(credito.Id, 3, EstadoCuota.Pendiente);

        var resultado = await _service.GetUltimaCuotaPendienteAsync(credito.Id);

        Assert.NotNull(resultado);
        Assert.Equal(3, resultado!.NumeroCuota);
    }

    // =========================================================================
    // GetByIdAsync
    // =========================================================================

    [Fact]
    public async Task GetById_Inexistente_RetornaNull()
    {
        var resultado = await _service.GetByIdAsync(id: 99_999);

        Assert.Null(resultado);
    }

    [Fact]
    public async Task GetById_Existente_RetornaViewModel()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);

        var resultado = await _service.GetByIdAsync(credito.Id);

        Assert.NotNull(resultado);
        Assert.Equal(credito.Id, resultado!.Id);
        Assert.Equal(credito.Numero, resultado.Numero);
    }

    [Fact]
    public async Task GetById_IncluyCuotasOrdenadas()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        await SeedCuotaAsync(credito.Id, 3, EstadoCuota.Pendiente);
        await SeedCuotaAsync(credito.Id, 1, EstadoCuota.Pendiente);
        await SeedCuotaAsync(credito.Id, 2, EstadoCuota.Pendiente);

        var resultado = await _service.GetByIdAsync(credito.Id);

        Assert.NotNull(resultado);
        Assert.NotNull(resultado!.Cuotas);
        Assert.Equal(3, resultado.Cuotas.Count);
        Assert.Equal(1, resultado.Cuotas[0].NumeroCuota);
        Assert.Equal(2, resultado.Cuotas[1].NumeroCuota);
        Assert.Equal(3, resultado.Cuotas[2].NumeroCuota);
    }

    // =========================================================================
    // GetByClienteIdAsync
    // =========================================================================

    [Fact]
    public async Task GetByClienteId_SinCreditos_RetornaVacio()
    {
        var cliente = await SeedClienteAsync();

        var resultado = await _service.GetByClienteIdAsync(cliente.Id);

        Assert.Empty(resultado);
    }

    [Fact]
    public async Task GetByClienteId_ConCreditos_RetornaSoloDelCliente()
    {
        var cliente1 = await SeedClienteAsync();
        var cliente2 = await SeedClienteAsync();
        await SeedCreditoAsync(cliente1.Id);
        await SeedCreditoAsync(cliente1.Id);
        await SeedCreditoAsync(cliente2.Id); // no debe aparecer

        var resultado = await _service.GetByClienteIdAsync(cliente1.Id);

        Assert.Equal(2, resultado.Count);
        Assert.All(resultado, c => Assert.Equal(cliente1.Id, c.ClienteId));
    }

    // =========================================================================
    // GetAllAsync — filtros
    // =========================================================================

    [Fact]
    public async Task GetAll_SinFiltro_RetornaTodos()
    {
        var cliente = await SeedClienteAsync();
        await SeedCreditoAsync(cliente.Id);
        await SeedCreditoAsync(cliente.Id);

        var resultado = await _service.GetAllAsync();

        Assert.True(resultado.Count >= 2);
    }

    [Fact]
    public async Task GetAll_FiltroEstado_RetornaSoloEseEstado()
    {
        var cliente = await SeedClienteAsync();
        var aprobado = await SeedCreditoAsync(cliente.Id);

        // Cambiar uno a Rechazado directamente en DB
        var rechazado = await SeedCreditoAsync(cliente.Id);
        rechazado.Estado = EstadoCredito.Rechazado;
        await _context.SaveChangesAsync();

        var resultado = await _service.GetAllAsync(new CreditoFilterViewModel
        {
            Estado = EstadoCredito.Aprobado
        });

        Assert.All(resultado, c => Assert.Equal(EstadoCredito.Aprobado, c.Estado));
        Assert.Contains(resultado, c => c.Id == aprobado.Id);
        Assert.DoesNotContain(resultado, c => c.Id == rechazado.Id);
    }

    [Fact]
    public async Task GetAll_FiltroNumero_RetornaSoloContieneSubstring()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        // El número generado es random; buscamos por un substring exacto del número
        var subNumero = credito.Numero![..4];

        var resultado = await _service.GetAllAsync(new CreditoFilterViewModel
        {
            Numero = subNumero
        });

        Assert.Contains(resultado, c => c.Id == credito.Id);
    }

    [Fact]
    public async Task GetAll_FiltroMontoMinimo_ExcluyeMenores()
    {
        var cliente = await SeedClienteAsync();

        var chico = new Credito
        {
            Numero = Guid.NewGuid().ToString("N")[..10],
            ClienteId = cliente.Id,
            Estado = EstadoCredito.Aprobado,
            MontoSolicitado = 1_000m,
            MontoAprobado = 1_000m,
            SaldoPendiente = 1_000m,
            TasaInteres = 3m,
            CantidadCuotas = 3,
            FechaSolicitud = DateTime.UtcNow
        };
        var grande = new Credito
        {
            Numero = Guid.NewGuid().ToString("N")[..10],
            ClienteId = cliente.Id,
            Estado = EstadoCredito.Aprobado,
            MontoSolicitado = 50_000m,
            MontoAprobado = 50_000m,
            SaldoPendiente = 50_000m,
            TasaInteres = 3m,
            CantidadCuotas = 12,
            FechaSolicitud = DateTime.UtcNow
        };
        _context.Creditos.AddRange(chico, grande);
        await _context.SaveChangesAsync();

        var resultado = await _service.GetAllAsync(new CreditoFilterViewModel
        {
            MontoMinimo = 10_000m
        });

        Assert.DoesNotContain(resultado, c => c.Id == chico.Id);
        Assert.Contains(resultado, c => c.Id == grande.Id);
    }

    [Fact]
    public async Task GetAll_FiltroSoloCuotasVencidas_RetornaSoloConVencidas()
    {
        var cliente = await SeedClienteAsync();
        var sinVencidas = await SeedCreditoAsync(cliente.Id);
        await SeedCuotaAsync(sinVencidas.Id, 1, EstadoCuota.Pendiente, diasAtrasado: -30); // futura

        var conVencida = await SeedCreditoAsync(cliente.Id);
        await SeedCuotaAsync(conVencida.Id, 1, EstadoCuota.Pendiente, diasAtrasado: 10); // vencida

        var resultado = await _service.GetAllAsync(new CreditoFilterViewModel
        {
            SoloCuotasVencidas = true
        });

        Assert.Contains(resultado, c => c.Id == conVencida.Id);
        Assert.DoesNotContain(resultado, c => c.Id == sinVencidas.Id);
    }

    // =========================================================================
    // DeleteAsync
    // =========================================================================

    [Fact]
    public async Task Delete_Inexistente_RetornaFalse()
    {
        var resultado = await _service.DeleteAsync(id: 99_999);

        Assert.False(resultado);
    }

    [Fact]
    public async Task Delete_EstadoSolicitado_SoftDeleteCreditoYCuotas()
    {
        var cliente = await SeedClienteAsync();
        var credito = new Credito
        {
            Numero = Guid.NewGuid().ToString("N")[..10],
            ClienteId = cliente.Id,
            Estado = EstadoCredito.Solicitado,
            MontoSolicitado = 5_000m,
            MontoAprobado = 0m,
            SaldoPendiente = 0m,
            TasaInteres = 3m,
            CantidadCuotas = 3,
            FechaSolicitud = DateTime.UtcNow
        };
        _context.Creditos.Add(credito);
        await _context.SaveChangesAsync();
        await SeedCuotaAsync(credito.Id, 1, EstadoCuota.Pendiente);

        var resultado = await _service.DeleteAsync(credito.Id);

        Assert.True(resultado);
        _context.ChangeTracker.Clear();
        var eliminado = await _context.Creditos.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == credito.Id);
        Assert.True(eliminado!.IsDeleted);
        var cuotaEliminada = await _context.Cuotas.IgnoreQueryFilters()
            .FirstAsync(c => c.CreditoId == credito.Id);
        Assert.True(cuotaEliminada.IsDeleted);
    }

    [Fact]
    public async Task Delete_EstadoAprobado_LanzaInvalidOperation()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id); // estado Aprobado

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.DeleteAsync(credito.Id));
    }

    [Fact]
    public async Task Delete_ConCuotaPagada_LanzaInvalidOperation()
    {
        var cliente = await SeedClienteAsync();
        var credito = new Credito
        {
            Numero = Guid.NewGuid().ToString("N")[..10],
            ClienteId = cliente.Id,
            Estado = EstadoCredito.Solicitado,
            MontoSolicitado = 5_000m,
            MontoAprobado = 0m,
            SaldoPendiente = 0m,
            TasaInteres = 3m,
            CantidadCuotas = 3,
            FechaSolicitud = DateTime.UtcNow
        };
        _context.Creditos.Add(credito);
        await _context.SaveChangesAsync();
        await SeedCuotaAsync(credito.Id, 1, EstadoCuota.Pagada);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.DeleteAsync(credito.Id));
    }

    // =========================================================================
    // CreateAsync
    // =========================================================================

    [Fact]
    public async Task Create_ClienteInexistente_LanzaInvalidOperation()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.CreateAsync(new CreditoViewModel
            {
                ClienteId = 99_999,
                MontoSolicitado = 10_000m,
                TasaInteres = 3m,
                CantidadCuotas = 6
            }));
    }

    [Fact]
    public async Task Create_ClienteValido_GeneraNumeroYPersiste()
    {
        var cliente = await SeedClienteAsync();

        var resultado = await _service.CreateAsync(new CreditoViewModel
        {
            ClienteId = cliente.Id,
            MontoSolicitado = 10_000m,
            TasaInteres = 3m,
            CantidadCuotas = 6
        });

        Assert.True(resultado.Id > 0);
        Assert.False(string.IsNullOrWhiteSpace(resultado.Numero));
        var enDb = await _context.Creditos.FindAsync(resultado.Id);
        Assert.NotNull(enDb);
    }

    [Fact]
    public async Task Create_EstadoDefault_QuedaSolicitado()
    {
        var cliente = await SeedClienteAsync();

        var resultado = await _service.CreateAsync(new CreditoViewModel
        {
            ClienteId = cliente.Id,
            MontoSolicitado = 5_000m,
            TasaInteres = 2m,
            CantidadCuotas = 3
        });

        Assert.Equal(EstadoCredito.Solicitado, resultado.Estado);
    }

    [Fact]
    public async Task Create_MontoAprobadoIgualMontoSolicitado()
    {
        var cliente = await SeedClienteAsync();

        var resultado = await _service.CreateAsync(new CreditoViewModel
        {
            ClienteId = cliente.Id,
            MontoSolicitado = 8_000m,
            TasaInteres = 2m,
            CantidadCuotas = 6
        });

        Assert.Equal(8_000m, resultado.MontoAprobado);
        Assert.Equal(8_000m, resultado.SaldoPendiente);
    }

    // =========================================================================
    // UpdateAsync
    // =========================================================================

    [Fact]
    public async Task Update_Existente_ActualizaYRetornaTrue()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);

        var vm = new CreditoViewModel
        {
            Id = credito.Id,
            Numero = credito.Numero,
            ClienteId = cliente.Id,
            MontoSolicitado = credito.MontoSolicitado,
            MontoAprobado = credito.MontoAprobado,
            SaldoPendiente = credito.SaldoPendiente,
            TasaInteres = credito.TasaInteres,
            CantidadCuotas = credito.CantidadCuotas,
            Estado = EstadoCredito.Aprobado
        };

        var resultado = await _service.UpdateAsync(vm);

        Assert.True(resultado);
    }

    [Fact]
    public async Task Update_CreditoInexistente_RetornaFalse()
    {
        var vm = new CreditoViewModel
        {
            Id = 99999,
            ClienteId = 1,
            MontoSolicitado = 1_000m,
            TasaInteres = 2m,
            CantidadCuotas = 3,
            Estado = EstadoCredito.Aprobado
        };

        var resultado = await _service.UpdateAsync(vm);

        Assert.False(resultado);
    }

    // =========================================================================
    // CreatePendienteConfiguracionAsync
    // =========================================================================

    [Fact]
    public async Task CreatePendienteConfiguracion_Persiste_EstadoPendienteConfiguracion()
    {
        var cliente = await SeedClienteAsync();

        var resultado = await _service.CreatePendienteConfiguracionAsync(cliente.Id, 5_000m);

        Assert.True(resultado.Id > 0);
        Assert.Equal(EstadoCredito.PendienteConfiguracion, resultado.Estado);
        Assert.Equal(5_000m, resultado.MontoSolicitado);
    }

    [Fact]
    public async Task CreatePendienteConfiguracion_CuotasYTasaCero()
    {
        var cliente = await SeedClienteAsync();

        var resultado = await _service.CreatePendienteConfiguracionAsync(cliente.Id, 3_000m);

        Assert.Equal(0, resultado.CantidadCuotas);
        Assert.Equal(0m, resultado.TasaInteres);
    }

    // =========================================================================
    // GetCuotasByCreditoAsync / GetCuotaByIdAsync
    // =========================================================================

    [Fact]
    public async Task GetCuotasByCredito_SinCuotas_RetornaVacio()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);

        var resultado = await _service.GetCuotasByCreditoAsync(credito.Id);

        Assert.Empty(resultado);
    }

    [Fact]
    public async Task GetCuotasByCredito_ConCuotas_RetornaOrdenadas()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        await SeedCuotaAsync(credito.Id, 3, EstadoCuota.Pendiente);
        await SeedCuotaAsync(credito.Id, 1, EstadoCuota.Pendiente);
        await SeedCuotaAsync(credito.Id, 2, EstadoCuota.Pendiente);

        var resultado = await _service.GetCuotasByCreditoAsync(credito.Id);

        Assert.Equal(3, resultado.Count);
        Assert.Equal(1, resultado[0].NumeroCuota);
        Assert.Equal(2, resultado[1].NumeroCuota);
        Assert.Equal(3, resultado[2].NumeroCuota);
    }

    [Fact]
    public async Task GetCuotaById_Existente_RetornaCuota()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        var cuota = await SeedCuotaAsync(credito.Id, 1, EstadoCuota.Pendiente);

        var resultado = await _service.GetCuotaByIdAsync(cuota.Id);

        Assert.NotNull(resultado);
        Assert.Equal(cuota.Id, resultado!.Id);
        Assert.Equal(1, resultado.NumeroCuota);
    }

    [Fact]
    public async Task GetCuotaById_Inexistente_RetornaNull()
    {
        var resultado = await _service.GetCuotaByIdAsync(99999);

        Assert.Null(resultado);
    }
}
