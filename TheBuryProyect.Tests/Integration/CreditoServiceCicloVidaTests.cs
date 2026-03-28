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
// Stubs mínimos para CreditoService — ciclo de vida
// ---------------------------------------------------------------------------

file sealed class StubCajaServiceCiclo : ICajaService
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

file sealed class StubCreditoDisponibleServiceCiclo : ICreditoDisponibleService
{
    public Task<decimal> ObtenerLimitePorPuntajeAsync(NivelRiesgoCredito puntaje, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<decimal> CalcularSaldoVigenteAsync(int clienteId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<CreditoDisponibleResultado> CalcularDisponibleAsync(int clienteId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<(bool Ok, List<string> Errores)> GuardarLimitesPorPuntajeAsync(IReadOnlyList<(NivelRiesgoCredito Puntaje, decimal LimiteMonto, bool Activo)> items, string usuario) => throw new NotImplementedException();
    public Task<List<PuntajeCreditoLimite>> GetAllLimitesPorPuntajeAsync() => throw new NotImplementedException();
}

file sealed class StubCurrentUserServiceCiclo : ICurrentUserService
{
    public string GetUsername() => "testuser";
    public string GetUserId() => "system";
    public bool IsAuthenticated() => true;
    public string? GetEmail() => "test@test.com";
    public bool IsInRole(string role) => false;
    public bool HasPermission(string modulo, string accion) => false;
    public string? GetIpAddress() => "127.0.0.1";
}

/// <summary>
/// Tests de integración para el ciclo de vida de CreditoService:
/// AprobarCreditoAsync, RechazarCreditoAsync, CancelarCreditoAsync,
/// PagarCuotaAsync (happy path, cuota ya pagada, pago parcial→Parcial),
/// RecalcularSaldoCreditoAsync (transición a Finalizado/Activo).
/// </summary>
public class CreditoServiceCicloVidaTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly CreditoService _service;

    public CreditoServiceCicloVidaTests()
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
            new StubCajaServiceCiclo(),
            new StubCreditoDisponibleServiceCiclo(),
            new StubCurrentUserServiceCiclo());
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
        var cliente = new Cliente
        {
            Nombre = "Test",
            Apellido = "Cliente",
            TipoDocumento = "DNI",
            NumeroDocumento = Guid.NewGuid().ToString("N")[..8],
            Email = "test@test.com"
        };
        _context.Set<Cliente>().Add(cliente);
        await _context.SaveChangesAsync();
        return cliente;
    }

    private async Task<Credito> SeedCreditoAsync(
        int clienteId,
        EstadoCredito estado = EstadoCredito.Solicitado,
        decimal montoSolicitado = 10_000m,
        decimal tasaInteres = 3m)
    {
        var credito = new Credito
        {
            Numero = Guid.NewGuid().ToString("N")[..10],
            ClienteId = clienteId,
            Estado = estado,
            MontoSolicitado = montoSolicitado,
            MontoAprobado = estado == EstadoCredito.Solicitado ? 0m : montoSolicitado,
            SaldoPendiente = estado == EstadoCredito.Solicitado ? 0m : montoSolicitado,
            TasaInteres = tasaInteres,
            CantidadCuotas = 3,
            FechaSolicitud = DateTime.UtcNow
        };
        _context.Set<Credito>().Add(credito);
        await _context.SaveChangesAsync();
        return credito;
    }

    private async Task<Cuota> SeedCuotaAsync(
        int creditoId,
        int numero = 1,
        decimal montoTotal = 1000m,
        decimal montoCapital = 800m,
        decimal montoInteres = 200m,
        EstadoCuota estado = EstadoCuota.Pendiente,
        int diasAtraso = 0)
    {
        var cuota = new Cuota
        {
            CreditoId = creditoId,
            NumeroCuota = numero,
            MontoCapital = montoCapital,
            MontoInteres = montoInteres,
            MontoTotal = montoTotal,
            Estado = estado,
            FechaVencimiento = DateTime.UtcNow.AddDays(-diasAtraso)
        };
        _context.Set<Cuota>().Add(cuota);
        await _context.SaveChangesAsync();
        return cuota;
    }

    // -------------------------------------------------------------------------
    // AprobarCreditoAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AprobarCredito_EstadoSolicitado_MarcaAprobadoYSetSaldo()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id, EstadoCredito.Solicitado, 10_000m);

        var resultado = await _service.AprobarCreditoAsync(credito.Id, "gerente1");

        Assert.True(resultado);
        var creditoBd = await _context.Set<Credito>().FirstAsync(c => c.Id == credito.Id);
        Assert.Equal(EstadoCredito.Aprobado, creditoBd.Estado);
        Assert.Equal("gerente1", creditoBd.AprobadoPor);
        Assert.Equal(10_000m, creditoBd.MontoAprobado);
        Assert.Equal(10_000m, creditoBd.SaldoPendiente);
        Assert.NotNull(creditoBd.FechaAprobacion);
    }

    [Fact]
    public async Task AprobarCredito_EstadoNoSolicitado_LanzaExcepcion()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id, EstadoCredito.Aprobado);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.AprobarCreditoAsync(credito.Id, "gerente1"));
    }

    [Fact]
    public async Task AprobarCredito_NoExiste_RetornaFalse()
    {
        var resultado = await _service.AprobarCreditoAsync(99999, "gerente1");
        Assert.False(resultado);
    }

    // -------------------------------------------------------------------------
    // RechazarCreditoAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RechazarCredito_CreditoExistente_MarcaRechazado()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id, EstadoCredito.Solicitado);

        var resultado = await _service.RechazarCreditoAsync(credito.Id, "Ingresos insuficientes");

        Assert.True(resultado);
        var creditoBd = await _context.Set<Credito>().FirstAsync(c => c.Id == credito.Id);
        Assert.Equal(EstadoCredito.Rechazado, creditoBd.Estado);
        Assert.Contains("Ingresos insuficientes", creditoBd.Observaciones);
    }

    [Fact]
    public async Task RechazarCredito_NoExiste_RetornaFalse()
    {
        var resultado = await _service.RechazarCreditoAsync(99999, "Motivo");
        Assert.False(resultado);
    }

    // -------------------------------------------------------------------------
    // CancelarCreditoAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CancelarCredito_CancelaCuotasPendientes()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id, EstadoCredito.Aprobado);
        var cuota1 = await SeedCuotaAsync(credito.Id, numero: 1, estado: EstadoCuota.Pendiente);
        var cuota2 = await SeedCuotaAsync(credito.Id, numero: 2, estado: EstadoCuota.Pendiente);

        var resultado = await _service.CancelarCreditoAsync(credito.Id, "Solicitud del cliente");

        Assert.True(resultado);
        var creditoBd = await _context.Set<Credito>().FirstAsync(c => c.Id == credito.Id);
        Assert.Equal(EstadoCredito.Cancelado, creditoBd.Estado);
        Assert.NotNull(creditoBd.FechaFinalizacion);

        var cuotas = await _context.Set<Cuota>()
            .Where(c => c.CreditoId == credito.Id)
            .ToListAsync();
        Assert.All(cuotas, c => Assert.Equal(EstadoCuota.Cancelada, c.Estado));
    }

    [Fact]
    public async Task CancelarCredito_NoExiste_RetornaFalse()
    {
        var resultado = await _service.CancelarCreditoAsync(99999, "Motivo");
        Assert.False(resultado);
    }

    // -------------------------------------------------------------------------
    // PagarCuotaAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PagarCuota_PagoCompleto_MarcaPagada()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id, EstadoCredito.Aprobado, montoSolicitado: 3_000m);
        var cuota = await SeedCuotaAsync(credito.Id, montoTotal: 1_000m, montoCapital: 800m);

        var pago = new PagarCuotaViewModel
        {
            CuotaId = cuota.Id,
            MontoPagado = 1_000m,
            FechaPago = DateTime.UtcNow,
            MedioPago = "Efectivo"
        };

        var resultado = await _service.PagarCuotaAsync(pago);

        Assert.True(resultado);
        var cuotaBd = await _context.Set<Cuota>().FirstAsync(c => c.Id == cuota.Id);
        Assert.Equal(EstadoCuota.Pagada, cuotaBd.Estado);
        Assert.Equal(1_000m, cuotaBd.MontoPagado);
    }

    [Fact]
    public async Task PagarCuota_PagoParcial_MarcaParcial()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id, EstadoCredito.Aprobado);
        var cuota = await SeedCuotaAsync(credito.Id, montoTotal: 1_000m, montoCapital: 800m);

        var pago = new PagarCuotaViewModel
        {
            CuotaId = cuota.Id,
            MontoPagado = 500m, // pago parcial
            FechaPago = DateTime.UtcNow,
            MedioPago = "Efectivo"
        };

        await _service.PagarCuotaAsync(pago);

        var cuotaBd = await _context.Set<Cuota>().FirstAsync(c => c.Id == cuota.Id);
        Assert.Equal(EstadoCuota.Parcial, cuotaBd.Estado);
        Assert.Equal(500m, cuotaBd.MontoPagado);
    }

    [Fact]
    public async Task PagarCuota_CuotaYaPagada_LanzaExcepcion()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id, EstadoCredito.Aprobado);
        var cuota = await SeedCuotaAsync(credito.Id, estado: EstadoCuota.Pagada);

        var pago = new PagarCuotaViewModel
        {
            CuotaId = cuota.Id,
            MontoPagado = 1_000m,
            FechaPago = DateTime.UtcNow,
            MedioPago = "Efectivo"
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.PagarCuotaAsync(pago));
    }

    [Fact]
    public async Task PagarCuota_CuotaNoExiste_RetornaFalse()
    {
        var pago = new PagarCuotaViewModel
        {
            CuotaId = 99999,
            MontoPagado = 500m,
            FechaPago = DateTime.UtcNow,
            MedioPago = "Efectivo"
        };

        var resultado = await _service.PagarCuotaAsync(pago);
        Assert.False(resultado);
    }

    // -------------------------------------------------------------------------
    // RecalcularSaldoCreditoAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RecalcularSaldo_TodasCuotasPagadas_MarcaFinalizado()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id, EstadoCredito.Aprobado, montoSolicitado: 2_000m);
        await SeedCuotaAsync(credito.Id, numero: 1, montoTotal: 1_000m, montoCapital: 1_000m,
            estado: EstadoCuota.Pagada);
        await SeedCuotaAsync(credito.Id, numero: 2, montoTotal: 1_000m, montoCapital: 1_000m,
            estado: EstadoCuota.Pagada);

        var resultado = await _service.RecalcularSaldoCreditoAsync(credito.Id);

        Assert.True(resultado);
        var creditoBd = await _context.Set<Credito>().FirstAsync(c => c.Id == credito.Id);
        Assert.Equal(EstadoCredito.Finalizado, creditoBd.Estado);
        Assert.NotNull(creditoBd.FechaFinalizacion);
    }

    [Fact]
    public async Task RecalcularSaldo_PrimerCuotaPagada_TransicionaAActivo()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id, EstadoCredito.Aprobado, montoSolicitado: 2_000m);
        await SeedCuotaAsync(credito.Id, numero: 1, montoTotal: 1_000m, montoCapital: 1_000m,
            estado: EstadoCuota.Pagada);
        await SeedCuotaAsync(credito.Id, numero: 2, montoTotal: 1_000m, montoCapital: 1_000m,
            estado: EstadoCuota.Pendiente);

        await _service.RecalcularSaldoCreditoAsync(credito.Id);

        var creditoBd = await _context.Set<Credito>().FirstAsync(c => c.Id == credito.Id);
        Assert.Equal(EstadoCredito.Activo, creditoBd.Estado);
    }

    [Fact]
    public async Task RecalcularSaldo_CreditoNoExiste_RetornaFalse()
    {
        var resultado = await _service.RecalcularSaldoCreditoAsync(99999);
        Assert.False(resultado);
    }

    // -------------------------------------------------------------------------
    // SaldoPendiente después de RecalcularSaldoCreditoAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RecalcularSaldo_CuotasPagadas_SaldoPendienteReduceCapitalProporcional()
    {
        // Cuota 1: montoTotal=1000, montoCapital=800 → capital pendiente proporcional
        // Si MontoPagado=1000 (pagado completo), capitalPagado = 1000*(800/1000)=800 → capitalPendiente=0
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id, EstadoCredito.Aprobado, montoSolicitado: 2_000m);

        // Cuota 1 pagada completa: capital 800, total 1000, pagado 1000
        var c1 = new Cuota
        {
            CreditoId = credito.Id, NumeroCuota = 1,
            MontoCapital = 800m, MontoInteres = 200m, MontoTotal = 1_000m,
            MontoPagado = 1_000m, Estado = EstadoCuota.Pagada,
            FechaVencimiento = DateTime.UtcNow.AddDays(-30)
        };
        // Cuota 2 pendiente: capital 800, total 1000, pagado 0
        var c2 = new Cuota
        {
            CreditoId = credito.Id, NumeroCuota = 2,
            MontoCapital = 800m, MontoInteres = 200m, MontoTotal = 1_000m,
            MontoPagado = 0m, Estado = EstadoCuota.Pendiente,
            FechaVencimiento = DateTime.UtcNow.AddDays(30)
        };
        _context.Set<Cuota>().AddRange(c1, c2);
        await _context.SaveChangesAsync();

        await _service.RecalcularSaldoCreditoAsync(credito.Id);

        var creditoBd = await _context.Set<Credito>().FirstAsync(c => c.Id == credito.Id);
        // Cuota1 pagada → capital pendiente = 0; Cuota2 pendiente → capital pendiente = 800
        Assert.Equal(800m, creditoBd.SaldoPendiente);
    }

    [Fact]
    public async Task RecalcularSaldo_CuotaParcial_SaldoPendienteReduceProporcional()
    {
        // Cuota parcial: montoTotal=1000, montoCapital=800, montoPagado=500
        // proporcionCapital = 800/1000 = 0.8
        // capitalPagado = 500 * 0.8 = 400
        // capitalPendiente = 800 - 400 = 400
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id, EstadoCredito.Aprobado, montoSolicitado: 1_000m);

        var cuota = new Cuota
        {
            CreditoId = credito.Id, NumeroCuota = 1,
            MontoCapital = 800m, MontoInteres = 200m, MontoTotal = 1_000m,
            MontoPagado = 500m, Estado = EstadoCuota.Parcial,
            FechaVencimiento = DateTime.UtcNow.AddDays(-5)
        };
        _context.Set<Cuota>().Add(cuota);
        await _context.SaveChangesAsync();

        await _service.RecalcularSaldoCreditoAsync(credito.Id);

        var creditoBd = await _context.Set<Credito>().FirstAsync(c => c.Id == credito.Id);
        Assert.Equal(400m, creditoBd.SaldoPendiente);
    }

    [Fact]
    public async Task RecalcularSaldo_CuotaCancelada_NoContribuyeAlSaldo()
    {
        // Una cuota cancelada no se suma al saldo pendiente
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id, EstadoCredito.Aprobado, montoSolicitado: 2_000m);

        var cancelada = new Cuota
        {
            CreditoId = credito.Id, NumeroCuota = 1,
            MontoCapital = 800m, MontoInteres = 200m, MontoTotal = 1_000m,
            MontoPagado = 0m, Estado = EstadoCuota.Cancelada,
            FechaVencimiento = DateTime.UtcNow
        };
        var pagada = new Cuota
        {
            CreditoId = credito.Id, NumeroCuota = 2,
            MontoCapital = 800m, MontoInteres = 200m, MontoTotal = 1_000m,
            MontoPagado = 1_000m, Estado = EstadoCuota.Pagada,
            FechaVencimiento = DateTime.UtcNow.AddDays(-30)
        };
        _context.Set<Cuota>().AddRange(cancelada, pagada);
        await _context.SaveChangesAsync();

        await _service.RecalcularSaldoCreditoAsync(credito.Id);

        var creditoBd = await _context.Set<Credito>().FirstAsync(c => c.Id == credito.Id);
        // Cancelada no cuenta; pagada completa → capital pendiente = 0
        Assert.Equal(0m, creditoBd.SaldoPendiente);
        Assert.Equal(EstadoCredito.Finalizado, creditoBd.Estado);
    }

    [Fact]
    public async Task RecalcularSaldo_CuotaCapitalCero_SaldoPendienteCero()
    {
        // MontoCapital=0 → CalcularCapitalPendienteCuota retorna 0 siempre
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id, EstadoCredito.Aprobado, montoSolicitado: 500m);

        var cuota = new Cuota
        {
            CreditoId = credito.Id, NumeroCuota = 1,
            MontoCapital = 0m, MontoInteres = 100m, MontoTotal = 100m,
            MontoPagado = 0m, Estado = EstadoCuota.Pendiente,
            FechaVencimiento = DateTime.UtcNow.AddDays(30)
        };
        _context.Set<Cuota>().Add(cuota);
        await _context.SaveChangesAsync();

        await _service.RecalcularSaldoCreditoAsync(credito.Id);

        var creditoBd = await _context.Set<Credito>().FirstAsync(c => c.Id == credito.Id);
        Assert.Equal(0m, creditoBd.SaldoPendiente);
    }
}
