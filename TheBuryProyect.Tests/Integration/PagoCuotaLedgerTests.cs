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
using TheBuryProject.ViewModels.Requests;

namespace TheBuryProject.Tests.Integration;

// ---------------------------------------------------------------------------
// PUN-ML2 — Ledger de pagos por cuota (PagoCuota) y su integración con los caminos de cobro.
//
// A diferencia de los stubs de PUN-ML1 (que devuelven un MovimientoCaja transitorio, sin
// persistir), acá RegistrarMovimientoCuotaAsync persiste de verdad — igual que CajaService real
// — porque PagoCuota.MovimientoCajaId es una FK real y el índice único filtrado necesita datos
// consistentes para poder ejercitarse.
// ---------------------------------------------------------------------------

sealed class FakeCajaServiceConLedger : ICajaService
{
    private readonly AppDbContext _context;
    public int AperturaCajaId { get; set; } = 1;
    public bool SimularSinCajaAbierta { get; set; }

    /// <summary>
    /// Cuando está seteado, el próximo cobro NO crea un MovimientoCaja nuevo: devuelve una
    /// referencia a uno YA existente (y ya ligado a otro PagoCuota), para forzar en un test un
    /// choque real contra el índice único filtrado de PagosCuota.MovimientoCajaId.
    /// </summary>
    public int? ForzarProximoMovimientoCajaId { get; set; }

    public FakeCajaServiceConLedger(AppDbContext context) => _context = context;

    public Task<AperturaCaja?> ObtenerAperturaActivaParaVentaAsync() =>
        Task.FromResult<AperturaCaja?>(SimularSinCajaAbierta ? null : new AperturaCaja { Id = AperturaCajaId });

    public async Task<MovimientoCaja?> RegistrarMovimientoCuotaAsync(
        int cuotaId, string creditoNumero, int numeroCuota,
        decimal monto, string medioPago, string usuario)
        => await RegistrarMovimientoCuotaAsync(cuotaId, creditoNumero, numeroCuota, monto, 0m, null, medioPago, usuario);

    public async Task<MovimientoCaja?> RegistrarMovimientoCuotaAsync(
        int cuotaId, string creditoNumero, int numeroCuota,
        decimal montoBase, decimal recargoMedioPago, TipoPago? tipoPago, string medioPago, string usuario)
    {
        if (SimularSinCajaAbierta)
            return null;

        if (ForzarProximoMovimientoCajaId.HasValue)
            return new MovimientoCaja { Id = ForzarProximoMovimientoCajaId.Value };

        var movimiento = new MovimientoCaja
        {
            AperturaCajaId = AperturaCajaId,
            FechaMovimiento = DateTime.UtcNow,
            Tipo = TipoMovimientoCaja.Ingreso,
            Concepto = ConceptoMovimientoCaja.CobroCuota,
            Monto = montoBase + recargoMedioPago,
            ImporteBase = montoBase,
            RecargoMedioPago = recargoMedioPago > 0 ? recargoMedioPago : null,
            TipoPago = tipoPago,
            MedioPagoDetalle = medioPago,
            Descripcion = $"Cobro cuota #{numeroCuota} - Crédito {creditoNumero}",
            Referencia = $"{creditoNumero}-C{numeroCuota}",
            ReferenciaId = cuotaId,
            Usuario = usuario
        };

        _context.MovimientosCaja.Add(movimiento);
        await _context.SaveChangesAsync();
        return movimiento;
    }

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

file sealed class StubFinancialServiceLedger : IFinancialCalculationService
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

file sealed class StubCreditoDisponibleServiceLedger : ICreditoDisponibleService
{
    public Task<decimal> ObtenerLimitePorPuntajeAsync(int puntaje, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<decimal> CalcularSaldoVigenteAsync(int clienteId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<CreditoDisponibleResultado> CalcularDisponibleAsync(int clienteId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<(bool Ok, List<string> Errores)> GuardarLimitesPorPuntajeAsync(IReadOnlyList<(int Puntaje, decimal LimiteMonto, bool Activo)> items, string usuario) => throw new NotImplementedException();
    public Task<List<PuntajeCreditoLimite>> GetAllLimitesPorPuntajeAsync() => throw new NotImplementedException();
}

file sealed class StubCurrentUserServiceLedger : ICurrentUserService
{
    public string GetUsername() => "TestUser";
    public string GetUserId() => "system";
    public bool IsAuthenticated() => true;
    public string? GetEmail() => "test@test.com";
    public bool IsInRole(string role) => false;
    public bool HasPermission(string modulo, string accion) => false;
    public string? GetIpAddress() => "127.0.0.1";
}

public class PagoCuotaLedgerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly CreditoService _service;
    private readonly TheBuryProject.Tests.Helpers.RelojComercialFake _reloj;
    private readonly FakeCajaServiceConLedger _caja;
    private readonly DbContextOptions<AppDbContext> _options;
    private int _nextId = 1;

    public PagoCuotaLedgerTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        // Sin este pragma, SQLite no rechaza un DELETE que deja una FK huérfana (a diferencia de
        // SQL Server, que siempre la enforcea). Necesario para que los tests de integridad
        // referencial de PagoCuota (más abajo) verifiquen algo real y no un falso positivo.
        using (var pragma = _connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA foreign_keys = ON;";
            pragma.ExecuteNonQuery();
        }

        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(_options);
        _context.Database.EnsureCreated();

        var mapper = new MapperConfiguration(
                cfg => { cfg.AddProfile<MappingProfile>(); },
                NullLoggerFactory.Instance)
            .CreateMapper();

        _reloj = new TheBuryProject.Tests.Helpers.RelojComercialFake(new DateOnly(2026, 1, 1));
        _caja = new FakeCajaServiceConLedger(_context);

        _service = new CreditoService(
            _context,
            mapper,
            NullLogger<CreditoService>.Instance,
            new StubFinancialServiceLedger(),
            _caja,
            new StubCreditoDisponibleServiceLedger(),
            new StubCurrentUserServiceLedger(),
            reloj: _reloj);

        // Caja/Apertura reales: MovimientoCaja.AperturaCajaId es FK real.
        _context.Cajas.Add(new Caja { Id = 1, Codigo = "C1", Nombre = "Caja 1", IsDeleted = false });
        _context.AperturasCaja.Add(new AperturaCaja
        {
            Id = 1, CajaId = 1, MontoInicial = 0m, UsuarioApertura = "TestUser", Cerrada = false, IsDeleted = false
        });
        _context.SaveChanges();
    }

    private DateTime Hoy => _reloj.HoyComercial.ToDateTime(TimeOnly.MinValue);

    /// <summary>
    /// Contexto nuevo sobre la misma conexión, sin nada trackeado. Usado para los tests de
    /// integridad referencial: si se borra con <c>_context</c>, EF detecta client-side que la
    /// entidad dependiente ya está trackeada y corta antes de tocar la base (comportamiento real,
    /// pero no ejercita la restricción de la base). Con un contexto limpio, el DELETE se manda
    /// directo a SQLite y es la FK real (PRAGMA foreign_keys, ver constructor) la que rechaza.
    /// </summary>
    private AppDbContext NuevoContextoDesconectado() => new(_options);

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private async Task<(Credito credito, Cuota cuota)> SeedCreditoConCuota(
        decimal montoTotal, decimal montoCapital, decimal montoInteres, DateTime fechaVencimiento, decimal tasaInteres = 24m)
    {
        var id = _nextId++;

        _context.Clientes.Add(new Cliente
        {
            Id = id, Nombre = "Cliente", Apellido = $"Test{id}", NumeroDocumento = $"9200{id:D5}", IsDeleted = false
        });

        var credito = new Credito
        {
            Id = id, ClienteId = id, IsDeleted = false, Numero = $"CRED{id:D4}",
            Estado = EstadoCredito.Activo, TasaInteres = tasaInteres,
            MontoSolicitado = montoTotal, MontoAprobado = montoTotal, SaldoPendiente = montoCapital,
            CantidadCuotas = 1, MontoCuota = montoTotal, TotalAPagar = montoTotal
        };
        _context.Creditos.Add(credito);

        var cuota = new Cuota
        {
            Id = id, CreditoId = id, NumeroCuota = 1, FechaVencimiento = fechaVencimiento,
            MontoTotal = montoTotal, MontoCapital = montoCapital, MontoInteres = montoInteres,
            MontoPagado = 0m, MontoPunitorio = 0m, Estado = EstadoCuota.Pendiente, IsDeleted = false
        };
        _context.Cuotas.Add(cuota);

        await _context.SaveChangesAsync();
        return (credito, cuota);
    }

    private async Task<bool> PagarAsync(int creditoId, int cuotaId, decimal monto)
    {
        var pago = new PagarCuotaViewModel
        {
            CreditoId = creditoId, CuotaId = cuotaId, MontoPagado = monto, FechaPago = Hoy, MedioPago = "Efectivo"
        };
        return await _service.PagarCuotaAsync(pago);
    }

    // ===========================================================================================
    // Pago manual de una cuota (RegistrarPagoCuotaAsync)
    // ===========================================================================================

    [Fact]
    public async Task PagoDeUnaCuota_CreaExactamenteUnaFilaDeLedger()
    {
        var (credito, cuota) = await SeedCreditoConCuota(1_000m, 800m, 200m, Hoy.AddDays(10));

        Assert.True(await PagarAsync(credito.Id, cuota.Id, 1_000m));

        var filas = await _context.PagosCuota.Where(p => p.CuotaId == cuota.Id).ToListAsync();
        Assert.Single(filas);

        var fila = filas[0];
        Assert.Equal(1_000m, fila.ImporteTotal);
        Assert.Equal(OrigenPagoCuota.RegistradoPorSistema, fila.Origen);
        Assert.Equal(EstadoPagoCuota.Aplicado, fila.Estado);
        Assert.Equal(_reloj.HoyComercial, fila.FechaPagoComercial);
        Assert.NotNull(fila.MovimientoCajaId);
        Assert.Equal("Efectivo", fila.MedioPago);
    }

    [Fact]
    public async Task PagoParcialSinPunitorio_ComposicionEsConfiableYCompleta()
    {
        var (credito, cuota) = await SeedCreditoConCuota(1_000m, 800m, 200m, Hoy.AddDays(10));

        Assert.True(await PagarAsync(credito.Id, cuota.Id, 400m));

        var fila = await _context.PagosCuota.SingleAsync(p => p.CuotaId == cuota.Id);
        Assert.True(fila.HistorialCompleto);
        Assert.Equal(400m, fila.ImporteAplicadoCuota);
        Assert.Equal(0m, fila.ImporteAplicadoPunitorio);
        Assert.Null(fila.MotivoIncompleto);
    }

    /// <summary>
    /// Siembra una aplicación de punitorio activa directamente (sin pasar por
    /// <c>IPunitorioService.AplicarAsync</c>: esa autorización ya está cubierta por
    /// <c>PunitorioServiceTests</c>). PUN-ML6: es la ÚNICA fuente legítima de punitorio a cobrar.
    /// </summary>
    private async Task<PunitorioAplicado> SeedPunitorioAplicadoAsync(int cuotaId, decimal importe)
    {
        var aplicado = new PunitorioAplicado
        {
            CuotaId = cuotaId,
            FechaCalculo = _reloj.HoyComercial,
            SaldoBase = importe,
            DiasComputados = 1,
            Importe = importe,
            Estado = EstadoPunitorioAplicado.Aplicado,
            FechaAplicacion = _reloj.AhoraUtc,
            MotivoAplicacion = "Seed de test",
            UsuarioAplicacion = "TestUser"
        };
        _context.PunitoriosAplicados.Add(aplicado);
        await _context.SaveChangesAsync();
        return aplicado;
    }

    [Fact]
    public async Task PagoTotalConPunitorioAplicado_ComposicionEsExactaYVinculadaALaAplicacion()
    {
        // PUN-ML6: con prioridad punitorio → cuota decidida, la composición de un pago nuevo
        // siempre es evidencia exacta (ya no hay caso ambiguo).
        var (credito, cuota) = await SeedCreditoConCuota(1_000m, 800m, 200m, Hoy.AddDays(-150));
        var aplicado = await SeedPunitorioAplicadoAsync(cuota.Id, 100.00m);

        // Paga TODO (cuota + punitorio): 1000 + 100 = 1100.
        Assert.True(await PagarAsync(credito.Id, cuota.Id, 1_100m));

        var fila = await _context.PagosCuota.SingleAsync(p => p.CuotaId == cuota.Id);
        Assert.True(fila.HistorialCompleto);
        Assert.Equal(1_100m, fila.ImporteTotal);
        Assert.Equal(100.00m, fila.ImporteAplicadoPunitorio);
        Assert.Equal(1_000.00m, fila.ImporteAplicadoCuota);
        Assert.Equal(aplicado.Id, fila.PunitorioAplicadoId);

        var aplicadoBd = await _context.PunitoriosAplicados.AsNoTracking().SingleAsync(p => p.Id == aplicado.Id);
        Assert.Equal(EstadoPunitorioAplicado.Pagado, aplicadoBd.Estado);
    }

    [Fact]
    public async Task PagoParcialConPunitorioAplicado_PriorizaPunitorioYComposicionSigueCompleta()
    {
        // Mismo escenario, pero paga solo una parte (60 de los 1100 que debe): con la prioridad
        // punitorio → cuota (PUN-ML6), los 60 se imputan íntegros a punitorio y nada a cuota.
        var (credito, cuota) = await SeedCreditoConCuota(1_000m, 800m, 200m, Hoy.AddDays(-150));
        var aplicado = await SeedPunitorioAplicadoAsync(cuota.Id, 100.00m);

        Assert.True(await PagarAsync(credito.Id, cuota.Id, 60m));

        var fila = await _context.PagosCuota.SingleAsync(p => p.CuotaId == cuota.Id);
        Assert.True(fila.HistorialCompleto);
        Assert.Equal(60m, fila.ImporteTotal);
        Assert.Equal(0m, fila.ImporteAplicadoCuota);
        Assert.Equal(60m, fila.ImporteAplicadoPunitorio);
        Assert.Null(fila.MotivoIncompleto);
        Assert.Equal(aplicado.Id, fila.PunitorioAplicadoId);

        var cuotaBd = await _context.Cuotas.AsNoTracking().SingleAsync(c => c.Id == cuota.Id);
        Assert.Equal(0m, cuotaBd.MontoPagado); // el componente de punitorio nunca mueve MontoPagado
        // PUN-ML7 (corrección): un pago 100% punitorio no mueve MontoPagado, pero eso no implica
        // Pendiente — la cuota sigue venciendo hace 150 días con capital 100% impago, así que
        // EstadoCuotaResolver.Resolver (única autoridad; ya no existe el resolver de 3 args sin
        // noción de fecha) la resuelve Vencida. Antes de esta corrección este caso quedaba oculto
        // detrás de un falso "Pendiente" — exactamente el bug que motivó eliminar el resolver
        // paralelo de CreditoService.
        Assert.Equal(EstadoCuota.Vencida, cuotaBd.Estado);

        var aplicadoBd = await _context.PunitoriosAplicados.AsNoTracking().SingleAsync(p => p.Id == aplicado.Id);
        Assert.Equal(EstadoPunitorioAplicado.Aplicado, aplicadoBd.Estado); // sigue pendiente por 40
    }

    [Fact]
    public async Task PagoDeVariasCuotas_CreaUnaFilaPorCuota_ComposicionSiempreConfiable()
    {
        var (creditoA, cuotaA) = await SeedCreditoConCuota(1_000m, 800m, 200m, Hoy.AddDays(-10));
        var (creditoB, cuotaB) = await SeedCreditoConCuota(2_000m, 1_600m, 400m, Hoy.AddDays(-20));

        // Mismo cliente para ambas, requerido por PagarCuotasAsync.
        creditoB.ClienteId = creditoA.ClienteId;
        await _context.SaveChangesAsync();

        var request = new PagoMultipleCuotasRequest
        {
            ClienteId = creditoA.ClienteId,
            CuotaIds = new List<int> { cuotaA.Id, cuotaB.Id },
            RowVersionsPorCuota = new Dictionary<int, string>
            {
                [cuotaA.Id] = Convert.ToBase64String(cuotaA.RowVersion),
                [cuotaB.Id] = Convert.ToBase64String(cuotaB.RowVersion)
            },
            MedioPago = "Efectivo"
        };

        var resultado = await _service.PagarCuotasAsync(request);
        Assert.Equal(2, resultado.CantidadCuotas);

        var filas = await _context.PagosCuota
            .Where(p => p.CuotaId == cuotaA.Id || p.CuotaId == cuotaB.Id)
            .ToListAsync();

        Assert.Equal(2, filas.Count);
        Assert.All(filas, f => Assert.True(f.HistorialCompleto));
        Assert.All(filas, f => Assert.NotNull(f.MovimientoCajaId));

        var suma = filas.Sum(f => (f.ImporteAplicadoCuota ?? 0m) + (f.ImporteAplicadoPunitorio ?? 0m));
        Assert.Equal(filas.Sum(f => f.ImporteTotal), suma);
    }

    [Fact]
    public async Task PrimeraCuotaAlConfirmar_QuedaRegistradaEnElLedger()
    {
        var (credito, cuota) = await SeedCreditoConCuota(1_000m, 800m, 200m, Hoy);

        var resultado = await _service.CobrarPrimeraCuotaAlGenerarAsync(credito.Id, "Efectivo");
        Assert.Equal(EstadoCobroPrimeraCuota.Cobrada, resultado.Estado);

        var fila = await _context.PagosCuota.SingleAsync(p => p.CuotaId == cuota.Id);
        Assert.Equal(1_000m, fila.ImporteTotal);
        Assert.Equal(OrigenPagoCuota.RegistradoPorSistema, fila.Origen);
    }

    [Fact]
    public async Task AdelantoDeCuota_QuedaRegistradoEnElLedger()
    {
        var (credito, cuota) = await SeedCreditoConCuota(1_000m, 800m, 200m, Hoy.AddDays(10));

        var pago = new PagarCuotaViewModel { CreditoId = credito.Id, CuotaId = cuota.Id, MontoPagado = 1_000m, FechaPago = Hoy, MedioPago = "Efectivo" };
        Assert.True(await _service.AdelantarCuotaAsync(pago));

        var fila = await _context.PagosCuota.SingleAsync(p => p.CuotaId == cuota.Id);
        Assert.Equal(1_000m, fila.ImporteTotal);
        Assert.True(fila.HistorialCompleto);
    }

    // ===========================================================================================
    // Transacción: el ledger vive y muere con la operación principal
    // ===========================================================================================

    [Fact]
    public async Task SiFallaCaja_NoQuedaLedgerNiCuotaModificada()
    {
        var (credito, cuota) = await SeedCreditoConCuota(1_000m, 800m, 200m, Hoy.AddDays(10));
        _caja.SimularSinCajaAbierta = true;

        await Assert.ThrowsAsync<TheBuryProject.Services.Exceptions.PagoCuotaRechazadoException>(
            () => PagarAsync(credito.Id, cuota.Id, 1_000m));

        Assert.Empty(await _context.PagosCuota.Where(p => p.CuotaId == cuota.Id).ToListAsync());
        var cuotaFinal = await _context.Cuotas.AsNoTracking().SingleAsync(c => c.Id == cuota.Id);
        Assert.Equal(0m, cuotaFinal.MontoPagado);
        Assert.Equal(EstadoCuota.Pendiente, cuotaFinal.Estado);
    }

    [Fact]
    public async Task SiFallaElLedgerPorConflictoDeUnicidad_NoQuedaPagoParcialNiMovimientoNuevo()
    {
        // Fuerza una violación real del índice único filtrado: pre-existe un PagoCuota que ya
        // apunta a un MovimientoCaja con Id conocido; la caja fake se configura para devolver
        // exactamente ese mismo Id en el próximo cobro (simula una corrupción/carrera real).
        var (credito, cuota) = await SeedCreditoConCuota(1_000m, 800m, 200m, Hoy.AddDays(10));
        var (credito2, cuota2) = await SeedCreditoConCuota(500m, 400m, 100m, Hoy.AddDays(10));

        // Primer cobro real: genera un MovimientoCaja + PagoCuota legítimos.
        Assert.True(await PagarAsync(credito2.Id, cuota2.Id, 500m));
        var movimientoExistente = await _context.PagosCuota.AsNoTracking()
            .Where(p => p.CuotaId == cuota2.Id)
            .Select(p => p.MovimientoCajaId!.Value)
            .SingleAsync();

        // Fuerza que el próximo cobro reutilice ese mismo MovimientoCajaId (choque de unicidad).
        _caja.ForzarProximoMovimientoCajaId = movimientoExistente;

        await Assert.ThrowsAnyAsync<Exception>(() => PagarAsync(credito.Id, cuota.Id, 1_000m));

        var cuotaFinal = await _context.Cuotas.AsNoTracking().SingleAsync(c => c.Id == cuota.Id);
        Assert.Equal(0m, cuotaFinal.MontoPagado);
        Assert.Equal(EstadoCuota.Pendiente, cuotaFinal.Estado);
        Assert.Empty(await _context.PagosCuota.Where(p => p.CuotaId == cuota.Id).ToListAsync());
    }

    [Fact]
    public async Task NoSeDuplicaAnteReintento_SegundoPagoSobreCuotaYaSaldadaEsRechazado()
    {
        var (credito, cuota) = await SeedCreditoConCuota(1_000m, 800m, 200m, Hoy.AddDays(10));

        Assert.True(await PagarAsync(credito.Id, cuota.Id, 1_000m));

        await Assert.ThrowsAsync<TheBuryProject.Services.Exceptions.PagoCuotaRechazadoException>(
            () => PagarAsync(credito.Id, cuota.Id, 1_000m));

        Assert.Single(await _context.PagosCuota.Where(p => p.CuotaId == cuota.Id).ToListAsync());
    }

    [Fact]
    public async Task Anulacion_NoBorraFisicamente_LaFilaSigueSiendoConsultable()
    {
        // No existe todavía un flujo que anule un pago (PUN-ML1, diagnóstico): este test verifica
        // solo la garantía estructural — el modelo permite marcar Anulado sin borrar la fila.
        var (credito, cuota) = await SeedCreditoConCuota(1_000m, 800m, 200m, Hoy.AddDays(10));
        Assert.True(await PagarAsync(credito.Id, cuota.Id, 1_000m));

        var fila = await _context.PagosCuota.SingleAsync(p => p.CuotaId == cuota.Id);
        fila.Estado = EstadoPagoCuota.Anulado;
        fila.FechaAnulacion = _reloj.AhoraUtc;
        await _context.SaveChangesAsync();

        var filaTrasAnular = await _context.PagosCuota.AsNoTracking().SingleAsync(p => p.Id == fila.Id);
        Assert.Equal(EstadoPagoCuota.Anulado, filaTrasAnular.Estado);
        Assert.NotNull(filaTrasAnular.FechaAnulacion);
        Assert.False(filaTrasAnular.IsDeleted);
    }

    // ===========================================================================================
    // Integridad referencial: ninguna FK de PagoCuota puede borrar el ledger en cascada.
    // ===========================================================================================

    [Fact]
    public async Task EliminarCuotaConPagosRegistrados_EsRechazadoYElLedgerPermanece()
    {
        var (credito, cuota) = await SeedCreditoConCuota(1_000m, 800m, 200m, Hoy.AddDays(10));
        Assert.True(await PagarAsync(credito.Id, cuota.Id, 1_000m));

        var rowVersion = await _context.Cuotas.AsNoTracking()
            .Where(c => c.Id == cuota.Id).Select(c => c.RowVersion).SingleAsync();

        using (var fresco = NuevoContextoDesconectado())
        {
            // RowVersion real: si no coincide, SQLite reporta 0 filas afectadas
            // (DbUpdateConcurrencyException) antes de siquiera llegar a evaluar la FK.
            fresco.Cuotas.Attach(new Cuota { Id = cuota.Id, RowVersion = rowVersion });
            fresco.Cuotas.Remove(fresco.Cuotas.Local.Single());
            await Assert.ThrowsAsync<DbUpdateException>(() => fresco.SaveChangesAsync());
        }

        Assert.NotNull(await _context.Cuotas.AsNoTracking().SingleOrDefaultAsync(c => c.Id == cuota.Id));
        Assert.NotNull(await _context.PagosCuota.AsNoTracking().SingleOrDefaultAsync(p => p.CuotaId == cuota.Id));
    }

    [Fact]
    public async Task EliminarMovimientoCajaReferenciado_EsRechazadoYElLedgerPermanece()
    {
        var (credito, cuota) = await SeedCreditoConCuota(1_000m, 800m, 200m, Hoy.AddDays(10));
        Assert.True(await PagarAsync(credito.Id, cuota.Id, 1_000m));

        var movimientoId = await _context.PagosCuota.AsNoTracking()
            .Where(p => p.CuotaId == cuota.Id)
            .Select(p => p.MovimientoCajaId!.Value)
            .SingleAsync();
        var rowVersion = await _context.MovimientosCaja.AsNoTracking()
            .Where(m => m.Id == movimientoId).Select(m => m.RowVersion).SingleAsync();

        using (var fresco = NuevoContextoDesconectado())
        {
            fresco.MovimientosCaja.Attach(new MovimientoCaja { Id = movimientoId, RowVersion = rowVersion });
            fresco.MovimientosCaja.Remove(fresco.MovimientosCaja.Local.Single());
            await Assert.ThrowsAsync<DbUpdateException>(() => fresco.SaveChangesAsync());
        }

        Assert.NotNull(await _context.MovimientosCaja.AsNoTracking().SingleOrDefaultAsync(m => m.Id == movimientoId));
        Assert.NotNull(await _context.PagosCuota.AsNoTracking().SingleOrDefaultAsync(p => p.MovimientoCajaId == movimientoId));
    }

    [Fact]
    public async Task ReversionQueReferenciaOtraFila_NoPermiteBorrarElOrigenNiProduceCascada()
    {
        var (credito, cuota) = await SeedCreditoConCuota(1_000m, 800m, 200m, Hoy.AddDays(10));
        Assert.True(await PagarAsync(credito.Id, cuota.Id, 1_000m));

        var filaOriginal = await _context.PagosCuota.SingleAsync(p => p.CuotaId == cuota.Id);

        // No hay productor real de reversiones todavía (PUN-ML1, diagnóstico): se arma la fila a
        // mano para probar únicamente la garantía estructural de la autorreferencia.
        var filaReversion = new PagoCuota
        {
            CuotaId = cuota.Id,
            FechaPagoComercial = _reloj.HoyComercial,
            ImporteTotal = 0m,
            Origen = OrigenPagoCuota.Reversion,
            Estado = EstadoPagoCuota.Aplicado,
            HistorialCompleto = true,
            PagoCuotaOrigenId = filaOriginal.Id
        };
        _context.PagosCuota.Add(filaReversion);
        await _context.SaveChangesAsync();

        var rowVersion = await _context.PagosCuota.AsNoTracking()
            .Where(p => p.Id == filaOriginal.Id).Select(p => p.RowVersion).SingleAsync();

        using (var fresco = NuevoContextoDesconectado())
        {
            fresco.PagosCuota.Attach(new PagoCuota { Id = filaOriginal.Id, RowVersion = rowVersion });
            fresco.PagosCuota.Remove(fresco.PagosCuota.Local.Single());
            await Assert.ThrowsAsync<DbUpdateException>(() => fresco.SaveChangesAsync());
        }

        var filasFinal = await _context.PagosCuota.AsNoTracking()
            .Where(p => p.CuotaId == cuota.Id)
            .ToListAsync();
        Assert.Equal(2, filasFinal.Count);
        Assert.Contains(filasFinal, p => p.Id == filaOriginal.Id);
        Assert.Contains(filasFinal, p => p.Id == filaReversion.Id && p.PagoCuotaOrigenId == filaOriginal.Id);
    }

    [Fact]
    public async Task Reversion_CambiaEstadoDeAmbasFilasSinBorrarLaOriginal()
    {
        var (credito, cuota) = await SeedCreditoConCuota(1_000m, 800m, 200m, Hoy.AddDays(10));
        Assert.True(await PagarAsync(credito.Id, cuota.Id, 1_000m));

        var filaOriginal = await _context.PagosCuota.SingleAsync(p => p.CuotaId == cuota.Id);
        filaOriginal.Estado = EstadoPagoCuota.Revertido;

        var filaReversion = new PagoCuota
        {
            CuotaId = cuota.Id,
            FechaPagoComercial = _reloj.HoyComercial,
            ImporteTotal = 1_000m,
            Origen = OrigenPagoCuota.Reversion,
            Estado = EstadoPagoCuota.Aplicado,
            HistorialCompleto = true,
            PagoCuotaOrigenId = filaOriginal.Id
        };
        _context.PagosCuota.Add(filaReversion);
        await _context.SaveChangesAsync();

        var filasFinal = await _context.PagosCuota.AsNoTracking()
            .Where(p => p.CuotaId == cuota.Id)
            .ToListAsync();
        Assert.Equal(2, filasFinal.Count);

        var original = filasFinal.Single(p => p.Id == filaOriginal.Id);
        Assert.Equal(EstadoPagoCuota.Revertido, original.Estado);
        Assert.False(original.IsDeleted);

        var reversion = filasFinal.Single(p => p.Id == filaReversion.Id);
        Assert.Equal(EstadoPagoCuota.Aplicado, reversion.Estado);
        Assert.Equal(filaOriginal.Id, reversion.PagoCuotaOrigenId);
    }
}
