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
    // PUN-ML2: persiste de verdad (igual que CajaService real) porque PagoCuota.MovimientoCajaId
    // es una FK real. La Caja/AperturaCaja (Id=1) la siembra el test antes de instanciar el stub.
    private readonly AppDbContext _context;

    public StubCajaServicePunitorio(AppDbContext context) => _context = context;

    public Task<decimal?> ObtenerUltimoEfectivoCierreAsync(int cajaId) => Task.FromResult<decimal?>(null);
    public AperturaCaja? AperturaActivaParaVenta { get; set; } = new() { Id = 1 };

    public async Task<MovimientoCaja?> RegistrarMovimientoCuotaAsync(
        int cuotaId, string creditoNumero, int numeroCuota,
        decimal monto, string medioPago, string usuario)
    {
        if (AperturaActivaParaVenta == null)
            return null;

        var movimiento = new MovimientoCaja
        {
            AperturaCajaId = AperturaActivaParaVenta.Id,
            Tipo = TipoMovimientoCaja.Ingreso,
            Concepto = ConceptoMovimientoCaja.CobroCuota,
            Monto = monto,
            ImporteBase = monto,
            Descripcion = $"Cobro cuota #{numeroCuota} - Crédito {creditoNumero}",
            ReferenciaId = cuotaId,
            MedioPagoDetalle = medioPago,
            Usuario = usuario
        };
        _context.MovimientosCaja.Add(movimiento);
        await _context.SaveChangesAsync();
        return movimiento;
    }

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

file sealed class StubFinancialService : IFinancialCalculationService
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
        decimal semaforoRatioAmarilloMax = 0.15m,
        IReadOnlyCollection<int>? cuotasSinRecargo = null) => throw new NotImplementedException();
}

file sealed class StubCreditoDisponibleService : ICreditoDisponibleService
{
    public Task<decimal> ObtenerLimitePorPuntajeAsync(int puntaje, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<decimal> CalcularSaldoVigenteAsync(int clienteId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<CreditoDisponibleResultado> CalcularDisponibleAsync(int clienteId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<(bool Ok, List<string> Errores)> GuardarLimitesPorPuntajeAsync(IReadOnlyList<(int Puntaje, decimal LimiteMonto, bool Activo)> items, string usuario) => throw new NotImplementedException();
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
/// PUN-ML6: valida la eliminación de la fórmula legacy del cobro productivo
/// (<c>MontoTotal * (TasaInteres / 100 / 12) * (diasAtraso / 30)</c>, ver PUN-ML1). Cobrar una
/// cuota vencida ya NO calcula ni escribe ningún punitorio por sí solo: el único punitorio que se
/// cobra es el de una <see cref="PunitorioAplicado"/> previamente aplicada (autorizada, fuera de
/// este flujo). <c>Credito.TasaInteres</c> no participa en absoluto del cobro.
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
        _context.Cajas.Add(new Caja { Id = 1, Codigo = "C1", Nombre = "Caja test", IsDeleted = false });
        _context.AperturasCaja.Add(new AperturaCaja
        {
            Id = 1, CajaId = 1, MontoInicial = 0m, UsuarioApertura = "TestUser", Cerrada = false, IsDeleted = false
        });
        _context.SaveChanges();

        var mapper = new MapperConfiguration(
                cfg => { cfg.AddProfile<MappingProfile>(); },
                NullLoggerFactory.Instance)
            .CreateMapper();

        _service = new CreditoService(
            _context,
            mapper,
            NullLogger<CreditoService>.Instance,
            new StubFinancialService(),
            new StubCajaServicePunitorio(_context),
            new StubCreditoDisponibleService(),
            new StubCurrentUserService(),
            reloj: _reloj);
    }

    // Fecha comercial fija: el punitorio se mide por días de negocio y no debe depender de la hora real.
    private readonly TheBuryProject.Tests.Helpers.RelojComercialFake _reloj =
        new(new DateOnly(2026, 6, 15));

    private DateTime Hoy => _reloj.HoyComercial.ToDateTime(TimeOnly.MinValue);

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

        // Vencimiento a medianoche relativo a la fecha comercial fija → días de atraso exactos.
        var fechaVencimiento = Hoy.AddDays(-diasVencida);
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
    // Tests — Eliminación legacy (PUN-ML6)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PagarCuota_CuotaVencida_SinAplicacion_NoCobraNiEscribePunitorioAlguno()
    {
        // Vencida 30 días, TasaInteres alta: bajo la fórmula legacy esto habría devengado un
        // punitorio no trivial. Sin una PunitorioAplicado explícita, el cobro no calcula nada.
        const decimal tasaInteres = 24m;
        const decimal montoTotal = 1_000m;
        const int diasVencida = 30;

        var (_, _, cuota) = await SeedCuotaVencida(tasaInteres, montoTotal, diasVencida);

        var pago = new PagarCuotaViewModel
        {
            CreditoId = cuota.CreditoId,
            CuotaId = cuota.Id,
            MontoPagado = montoTotal,
            FechaPago = DateTime.UtcNow,
            MedioPago = "Efectivo"
        };

        await _service.PagarCuotaAsync(pago);

        var cuotaActualizada = await _context.Cuotas.FindAsync(cuota.Id);
        Assert.NotNull(cuotaActualizada);
        Assert.Equal(EstadoCuota.Pagada, cuotaActualizada!.Estado);
        Assert.Equal(montoTotal, cuotaActualizada.MontoPagado);
        // Columna legacy: sin escritor productivo, permanece en el valor seedeado.
        Assert.Equal(0m, cuotaActualizada.MontoPunitorio);

        var fila = await _context.PagosCuota.SingleAsync(p => p.CuotaId == cuota.Id);
        Assert.Equal(0m, fila.ImporteAplicadoPunitorio);
        Assert.Null(fila.PunitorioAplicadoId);
    }

    [Fact]
    public async Task PagarCuota_CambiarTasaInteresDelCredito_NoCambiaElPunitorioCobrado()
    {
        // Dos créditos con TasaInteres muy distinta (0% vs 45%), misma cuota vencida y sin
        // aplicación en ninguno: el punitorio cobrado debe ser idéntico (cero) en ambos —
        // Credito.TasaInteres ya no participa del cobro.
        var (_, _, cuotaSinRecargo) = await SeedCuotaVencida(tasaInteresCredito: 0m, montoTotalCuota: 1_000m, diasVencida: 30);

        var (_, creditoConRecargo, cuotaConRecargo) = await SeedCuotaVencida2(tasaInteresCredito: 45m, montoTotalCuota: 1_000m, diasVencida: 30);

        await _service.PagarCuotaAsync(new PagarCuotaViewModel
        {
            CreditoId = cuotaSinRecargo.CreditoId, CuotaId = cuotaSinRecargo.Id,
            MontoPagado = 1_000m, FechaPago = DateTime.UtcNow, MedioPago = "Efectivo"
        });
        await _service.PagarCuotaAsync(new PagarCuotaViewModel
        {
            CreditoId = creditoConRecargo.Id, CuotaId = cuotaConRecargo.Id,
            MontoPagado = 1_000m, FechaPago = DateTime.UtcNow, MedioPago = "Efectivo"
        });

        var filaSinRecargo = await _context.PagosCuota.SingleAsync(p => p.CuotaId == cuotaSinRecargo.Id);
        var filaConRecargo = await _context.PagosCuota.SingleAsync(p => p.CuotaId == cuotaConRecargo.Id);

        Assert.Equal(0m, filaSinRecargo.ImporteAplicadoPunitorio);
        Assert.Equal(filaSinRecargo.ImporteAplicadoPunitorio, filaConRecargo.ImporteAplicadoPunitorio);
    }

    [Fact]
    public async Task PagarCuota_ConAplicacionActiva_CobraExactamenteElImporteAplicado_NoLaTasaDelCredito()
    {
        // TasaInteres absurdamente alta a propósito: si algún camino productivo la leyera para
        // mora, este test lo detectaría. El único importe que puede cobrarse es el de la
        // aplicación autorizada (PUN-ML5), sembrada acá directamente (sin pasar por
        // IPunitorioService.AplicarAsync: eso ya está cubierto por PunitorioServiceTests).
        const decimal tasaInteresAbsurda = 999m;
        const decimal montoTotal = 1_000m;
        var (_, credito, cuota) = await SeedCuotaVencida(tasaInteresAbsurda, montoTotal, diasVencida: 10);

        _context.PunitoriosAplicados.Add(new PunitorioAplicado
        {
            CuotaId = cuota.Id,
            FechaCalculo = _reloj.HoyComercial,
            SaldoBase = montoTotal,
            DiasComputados = 10,
            Importe = 50m,
            Estado = EstadoPunitorioAplicado.Aplicado,
            FechaAplicacion = _reloj.AhoraUtc,
            MotivoAplicacion = "Seed de test",
            UsuarioAplicacion = "TestUser"
        });
        await _context.SaveChangesAsync();

        await _service.PagarCuotaAsync(new PagarCuotaViewModel
        {
            CreditoId = credito.Id, CuotaId = cuota.Id,
            MontoPagado = montoTotal + 50m, FechaPago = DateTime.UtcNow, MedioPago = "Efectivo"
        });

        var fila = await _context.PagosCuota.SingleAsync(p => p.CuotaId == cuota.Id);
        Assert.Equal(50m, fila.ImporteAplicadoPunitorio);
        Assert.Equal(montoTotal, fila.ImporteAplicadoCuota);

        var aplicacion = await _context.PunitoriosAplicados.SingleAsync(p => p.CuotaId == cuota.Id);
        Assert.Equal(EstadoPunitorioAplicado.Pagado, aplicacion.Estado);

        var cuotaActualizada = await _context.Cuotas.FindAsync(cuota.Id);
        Assert.NotNull(cuotaActualizada);
        Assert.Equal(EstadoCuota.Pagada, cuotaActualizada!.Estado);
        // Sólo el componente de cuota mueve MontoPagado, nunca el de punitorio.
        Assert.Equal(montoTotal, cuotaActualizada.MontoPagado);
    }

    [Fact]
    public async Task PagarCuota_ConConfiguracionPunitorioVigente_PeroSinAplicacion_NoCobraPunitorio()
    {
        // Aunque exista una ConfiguracionPunitorio real que el calculador podría usar, el cobro
        // nunca invoca al calculador ni aplica nada por sí mismo (PUN-ML6: "no debe aplicar un
        // punitorio automáticamente al cobrar").
        _context.ConfiguracionesPunitorio.Add(new ConfiguracionPunitorio
        {
            Porcentaje = 10m,
            PeriodoDias = 20,
            DiasGracia = 5,
            VigenteDesde = new DateOnly(2020, 1, 1),
            Activa = true,
            ProrrateoDiario = true
        });
        await _context.SaveChangesAsync();

        var (_, credito, cuota) = await SeedCuotaVencida(24m, 1_000m, diasVencida: 30);

        await _service.PagarCuotaAsync(new PagarCuotaViewModel
        {
            CreditoId = credito.Id, CuotaId = cuota.Id,
            MontoPagado = 1_000m, FechaPago = DateTime.UtcNow, MedioPago = "Efectivo"
        });

        var fila = await _context.PagosCuota.SingleAsync(p => p.CuotaId == cuota.Id);
        Assert.Equal(0m, fila.ImporteAplicadoPunitorio);
        Assert.Null(fila.PunitorioAplicadoId);
        Assert.Empty(await _context.PunitoriosAplicados.ToListAsync());
    }

    /// <summary>Segundo crédito/cuota independiente, mismos parámetros que <see cref="SeedCuotaVencida"/> con Id=2.</summary>
    private async Task<(Cliente, Credito, Cuota)> SeedCuotaVencida2(
        decimal tasaInteresCredito, decimal montoTotalCuota, int diasVencida)
    {
        var cliente = new Cliente { Id = 2, Nombre = "Otro", Apellido = "Test", NumeroDocumento = "87654321", IsDeleted = false };
        _context.Clientes.Add(cliente);

        var credito = new Credito
        {
            Id = 2, ClienteId = 2, IsDeleted = false, Numero = "CRED0002",
            Estado = EstadoCredito.Activo, TasaInteres = tasaInteresCredito,
            MontoSolicitado = 10_000m, MontoAprobado = 10_000m, SaldoPendiente = montoTotalCuota,
            CantidadCuotas = 12, MontoCuota = montoTotalCuota, TotalAPagar = montoTotalCuota * 12
        };
        _context.Creditos.Add(credito);

        var fechaVencimiento = Hoy.AddDays(-diasVencida);
        var cuota = new Cuota
        {
            Id = 2, CreditoId = 2, NumeroCuota = 1, FechaVencimiento = fechaVencimiento,
            MontoTotal = montoTotalCuota, MontoCapital = montoTotalCuota * 0.8m, MontoInteres = montoTotalCuota * 0.2m,
            MontoPagado = 0m, MontoPunitorio = 0m, Estado = EstadoCuota.Pendiente, IsDeleted = false
        };
        _context.Cuotas.Add(cuota);

        await _context.SaveChangesAsync();
        return (cliente, credito, cuota);
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
            FechaVencimiento = Hoy.AddDays(10), // no vencida
            MontoTotal = 500m, MontoCapital = 400m, MontoInteres = 100m,
            MontoPagado = 0m, MontoPunitorio = 0m,
            Estado = EstadoCuota.Pendiente, IsDeleted = false
        };
        _context.Cuotas.Add(cuota);
        await _context.SaveChangesAsync();

        var pago = new PagarCuotaViewModel
        {
            CreditoId = 2,
            CuotaId = 2,
            MontoPagado = 500m,
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

    // -------------------------------------------------------------------------
    // Tests — Capital y cupo (PUN-ML6): sólo el componente de cuota libera cupo/capital
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PagarCuota_SoloPunitorio_NoLiberaCupoDeCredito()
    {
        var (_, credito, cuota) = await SeedCuotaVencida(24m, 1_000m, diasVencida: 10);

        // El seed deja SaldoPendiente = MontoTotal (1000), inconsistente con MontoCapital (800):
        // normaliza antes de medir, para que la comparación aísle el efecto del pago de punitorio
        // (y no un efecto colateral de la primera recalculación tras un seed inconsistente).
        await _service.RecalcularSaldoCreditoAsync(credito.Id);
        var saldoAntes = (await _context.Creditos.AsNoTracking().SingleAsync(c => c.Id == credito.Id)).SaldoPendiente;
        Assert.Equal(800m, saldoAntes);

        _context.PunitoriosAplicados.Add(new PunitorioAplicado
        {
            CuotaId = cuota.Id,
            FechaCalculo = _reloj.HoyComercial,
            SaldoBase = 1_000m,
            DiasComputados = 10,
            Importe = 50m,
            Estado = EstadoPunitorioAplicado.Aplicado,
            FechaAplicacion = _reloj.AhoraUtc,
            MotivoAplicacion = "Seed de test",
            UsuarioAplicacion = "TestUser"
        });
        await _context.SaveChangesAsync();

        // Paga sólo el punitorio (50), nada de la cuota.
        await _service.PagarCuotaAsync(new PagarCuotaViewModel
        {
            CreditoId = credito.Id, CuotaId = cuota.Id,
            MontoPagado = 50m, FechaPago = DateTime.UtcNow, MedioPago = "Efectivo"
        });

        var creditoBd = await _context.Creditos.AsNoTracking().SingleAsync(c => c.Id == credito.Id);
        Assert.Equal(saldoAntes, creditoBd.SaldoPendiente); // sin cambios: el pago no tocó capital

        var cuotaBd = await _context.Cuotas.AsNoTracking().SingleAsync(c => c.Id == cuota.Id);
        Assert.Equal(0m, cuotaBd.MontoPagado);
    }

    [Fact]
    public async Task PagarCuota_PagoMixto_LiberaCupoUnicamentePorElComponenteDeCuota()
    {
        var (_, credito, cuota) = await SeedCuotaVencida(24m, 1_000m, diasVencida: 10);
        // MontoCapital = montoTotalCuota * 0.8 = 800 (ver SeedCuotaVencida).

        _context.PunitoriosAplicados.Add(new PunitorioAplicado
        {
            CuotaId = cuota.Id,
            FechaCalculo = _reloj.HoyComercial,
            SaldoBase = 1_000m,
            DiasComputados = 10,
            Importe = 50m,
            Estado = EstadoPunitorioAplicado.Aplicado,
            FechaAplicacion = _reloj.AhoraUtc,
            MotivoAplicacion = "Seed de test",
            UsuarioAplicacion = "TestUser"
        });
        await _context.SaveChangesAsync();

        // Paga 550: 50 van a punitorio (prioridad), 500 a cuota.
        await _service.PagarCuotaAsync(new PagarCuotaViewModel
        {
            CreditoId = credito.Id, CuotaId = cuota.Id,
            MontoPagado = 550m, FechaPago = DateTime.UtcNow, MedioPago = "Efectivo"
        });

        var cuotaBd = await _context.Cuotas.AsNoTracking().SingleAsync(c => c.Id == cuota.Id);
        Assert.Equal(500m, cuotaBd.MontoPagado); // sólo el componente de cuota

        // capitalPagadoEstimado = round(500 * (800/1000)) = 400 → capitalPendiente = 800 - 400 = 400.
        var creditoBd = await _context.Creditos.AsNoTracking().SingleAsync(c => c.Id == credito.Id);
        Assert.Equal(400m, creditoBd.SaldoPendiente);
    }

}
