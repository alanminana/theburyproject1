using AutoMapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Exceptions;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Integration;

// ---------------------------------------------------------------------------
// Stubs — remediación G7/G8 de PagarCuota
// ---------------------------------------------------------------------------

/// <summary>Caja que registra el desglose completo del movimiento para poder auditarlo.</summary>
internal sealed class StubCajaServicePagoSeguro : ICajaService
{
    // PUN-ML2: persiste de verdad (igual que CajaService real) porque PagoCuota.MovimientoCajaId
    // es una FK real que el pago debe poder resolver. La Caja/AperturaCaja (Id=1) la siembra el
    // test antes de instanciar este stub — no acá adentro, para no chocar con el escenario de
    // concurrencia que usa dos DbContext distintos sobre el mismo SQLite de caché compartida.
    private readonly AppDbContext _context;

    public StubCajaServicePagoSeguro(AppDbContext context) => _context = context;

    public AperturaCaja? AperturaActivaParaVenta { get; set; } = new() { Id = 1 };

    public List<(int CuotaId, string CreditoNumero, int NumeroCuota, decimal MontoBase,
        decimal RecargoMedioPago, TipoPago? TipoPago, string MedioPago, string Usuario)> Movimientos { get; } = new();

    public async Task<MovimientoCaja?> RegistrarMovimientoCuotaAsync(
        int cuotaId, string creditoNumero, int numeroCuota,
        decimal montoBase, decimal recargoMedioPago, TipoPago? tipoPago,
        string medioPago, string usuario)
    {
        if (AperturaActivaParaVenta == null)
            return null;

        Movimientos.Add((cuotaId, creditoNumero, numeroCuota, montoBase, recargoMedioPago, tipoPago, medioPago, usuario));

        var movimiento = new MovimientoCaja
        {
            AperturaCajaId = AperturaActivaParaVenta.Id,
            Tipo = TipoMovimientoCaja.Ingreso,
            Concepto = ConceptoMovimientoCaja.CobroCuota,
            Monto = montoBase + recargoMedioPago,
            ImporteBase = montoBase,
            RecargoMedioPago = recargoMedioPago > 0 ? recargoMedioPago : null,
            DescuentoMedioPago = recargoMedioPago < 0 ? -recargoMedioPago : null,
            TipoPago = tipoPago,
            MedioPagoDetalle = medioPago,
            ReferenciaId = cuotaId,
            Referencia = $"{creditoNumero}-C{numeroCuota}",
            Usuario = usuario
        };
        _context.MovimientosCaja.Add(movimiento);
        await _context.SaveChangesAsync();
        return movimiento;
    }

    public Task<MovimientoCaja?> RegistrarMovimientoCuotaAsync(
        int cuotaId, string creditoNumero, int numeroCuota,
        decimal monto, string medioPago, string usuario)
        => RegistrarMovimientoCuotaAsync(cuotaId, creditoNumero, numeroCuota, monto, 0m, null, medioPago, usuario);

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
    public Task<Dictionary<int, DateTime>> ObtenerUltimosCierresPorCajaAsync() => throw new NotImplementedException();
    public Task<DetallesAperturaViewModel> ObtenerDetallesAperturaAsync(int aperturaId) => throw new NotImplementedException();
    public Task<ReporteCajaViewModel> GenerarReporteCajaAsync(DateTime fechaDesde, DateTime fechaHasta, int? cajaId = null) => throw new NotImplementedException();
    public Task<HistorialCierresViewModel> ObtenerEstadisticasCierresAsync(int? cajaId = null, DateTime? fechaDesde = null, DateTime? fechaHasta = null) => throw new NotImplementedException();
}

/// <summary>
/// Devuelve un porcentaje de ajuste fijo del medio de pago (positivo = recargo,
/// negativo = descuento). Re-implementa el miembro de la interfaz, que en la
/// definición trae una implementación default de 0%.
/// </summary>
internal sealed class StubConfiguracionPagoAjuste : StubConfiguracionPagoServiceVenta, IConfiguracionPagoService
{
    public decimal AjustePorcentaje { get; set; }

    public Task<decimal> ObtenerPorcentajeAjusteUnPagoAsync(TipoPago tipoPago)
        => Task.FromResult(AjustePorcentaje);
}

/// <summary>
/// Remediación de los hallazgos G7 (monto de pago manipulable) y G8 (falta de validación
/// crédito↔cuota) del informe AUDIT-RV-20260722.
///
/// Regla implementada: el servidor resuelve la cuota desde su relación con el crédito y
/// recalcula punitorio, saldo, recargo/descuento, total y estado. Del cliente solo se acepta
/// el importe a imputar y únicamente dentro de (0, saldo]; el pago parcial sigue siendo válido
/// (estado Parcial, acumulación en MontoPagado) porque es una funcionalidad vigente del módulo.
/// </summary>
public class CreditoServicePagarCuotaSeguridadTests : IDisposable
{
    private const decimal MontoCuota = 19_534.20m;

    private readonly SqliteConnection _connection;
    private readonly string _dataSource;
    private readonly AppDbContext _context;
    private readonly StubCajaServicePagoSeguro _caja;
    private readonly StubConfiguracionPagoAjuste _configuracionPago;
    private readonly CreditoService _service;

    // Fecha comercial fija: el cobro de la 1ª cuota ("vence hoy") debe ser determinista.
    private readonly TheBuryProject.Tests.Helpers.RelojComercialFake _reloj =
        new(new DateOnly(2026, 6, 15));

    public CreditoServicePagarCuotaSeguridadTests()
    {
        _dataSource = $"{Guid.NewGuid():N}";
        _connection = new SqliteConnection($"DataSource={_dataSource};Mode=Memory;Cache=Shared");
        _connection.Open();

        _context = CrearContexto();
        _context.Database.EnsureCreated();
        SembrarCajaYApertura(_context);

        _caja = new StubCajaServicePagoSeguro(_context);
        _configuracionPago = new StubConfiguracionPagoAjuste();
        _service = CrearService(_context, _caja, _configuracionPago, _reloj);
    }

    // PUN-ML2: MovimientoCaja.AperturaCajaId es FK real; se siembra una vez por conexión SQLite
    // compartida (Cache=Shared), visible para cualquier AppDbContext adicional que abra el test.
    private static void SembrarCajaYApertura(AppDbContext context)
    {
        context.Cajas.Add(new Caja { Id = 1, Codigo = "C1", Nombre = "Caja test", IsDeleted = false });
        context.AperturasCaja.Add(new AperturaCaja
        {
            Id = 1, CajaId = 1, MontoInicial = 0m, UsuarioApertura = "TestUser", Cerrada = false, IsDeleted = false
        });
        context.SaveChanges();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // Infraestructura
    // -------------------------------------------------------------------------

    // AddInterceptors: SQL Server regenera RowVersion en cada UPDATE; SQLite no (ver
    // SqliteRowVersionRotationInterceptor). Sin esto, el token de concurrencia de un segundo
    // envío con el mismo comando nunca quedaría stale bajo SQLite.
    private AppDbContext CrearContexto() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"DataSource={_dataSource};Mode=Memory;Cache=Shared")
            .AddInterceptors(new TheBuryProject.Tests.Infrastructure.SqliteRowVersionRotationInterceptor())
            .Options);

    private static CreditoService CrearService(
        AppDbContext context,
        ICajaService caja,
        IConfiguracionPagoService? configuracionPago,
        IRelojComercial? reloj = null) =>
        new(context,
            new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>(), NullLoggerFactory.Instance).CreateMapper(),
            NullLogger<CreditoService>.Instance,
            new FinancialCalculationService(),
            caja,
            new StubCreditoDisponibleServicePagoSeguro(),
            new StubCurrentUserServicePagoSeguro(),
            configuracionPagoService: configuracionPago,
            reloj: reloj);

    private async Task<Cliente> SeedClienteAsync(string documento)
    {
        var cliente = new Cliente
        {
            Nombre = "FIX-RV-PAGO",
            Apellido = "Cliente",
            TipoDocumento = "DNI",
            NumeroDocumento = documento,
            Email = "fix-rv-pago@test.local"
        };
        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync();
        return cliente;
    }

    private async Task<Credito> SeedCreditoAsync(int clienteId, string numero)
    {
        var credito = new Credito
        {
            Numero = numero,
            ClienteId = clienteId,
            Estado = EstadoCredito.Activo,
            MontoSolicitado = 200_000m,
            MontoAprobado = 200_000m,
            SaldoPendiente = 200_000m,
            TasaInteres = 0m,
            CantidadCuotas = 9,
            FechaSolicitud = DateTime.UtcNow
        };
        _context.Creditos.Add(credito);
        await _context.SaveChangesAsync();
        return credito;
    }

    private async Task<Cuota> SeedCuotaAsync(
        int creditoId,
        int numeroCuota = 1,
        decimal montoTotal = MontoCuota,
        decimal montoPagado = 0m,
        EstadoCuota estado = EstadoCuota.Pendiente)
    {
        var cuota = new Cuota
        {
            CreditoId = creditoId,
            NumeroCuota = numeroCuota,
            MontoCapital = montoTotal,
            MontoInteres = 0m,
            MontoTotal = montoTotal,
            MontoPagado = montoPagado,
            MontoPunitorio = 0m,
            Estado = estado,
            // Vence hoy (fecha comercial fija): sin punitorio, el saldo es exactamente MontoTotal - MontoPagado.
            FechaVencimiento = _reloj.HoyComercial.ToDateTime(TimeOnly.MinValue)
        };
        _context.Cuotas.Add(cuota);
        await _context.SaveChangesAsync();
        return cuota;
    }

    /// <summary>Crédito con una cuota pendiente de <see cref="MontoCuota"/>, listo para cobrar.</summary>
    private async Task<(Credito Credito, Cuota Cuota)> SeedEscenarioAsync(string sufijo = "1")
    {
        var cliente = await SeedClienteAsync($"FIXRV{sufijo}");
        var credito = await SeedCreditoAsync(cliente.Id, $"FIX-RV-PAGO-{sufijo}");
        var cuota = await SeedCuotaAsync(credito.Id);
        return (credito, cuota);
    }

    private static PagarCuotaViewModel Pago(int creditoId, int cuotaId, decimal monto, string medio = "Efectivo") =>
        new()
        {
            CreditoId = creditoId,
            CuotaId = cuotaId,
            MontoPagado = monto,
            FechaPago = DateTime.UtcNow,
            MedioPago = medio
        };

    private async Task<Cuota> RecargarCuotaAsync(int cuotaId)
    {
        _context.ChangeTracker.Clear();
        return await _context.Cuotas.AsNoTracking().FirstAsync(c => c.Id == cuotaId);
    }

    private static PagoCuotaIndividualComando Comando(
        Cuota cuota,
        decimal monto,
        string medio = "Efectivo") =>
        new(cuota.Id, monto, medio, "COMP-TEST", "Pago individual", cuota.RowVersion.ToArray());

    private async Task<PunitorioAplicado> SeedPunitorioAplicadoAsync(Cuota cuota, decimal importe)
    {
        var aplicado = new PunitorioAplicado
        {
            CuotaId = cuota.Id,
            FechaCalculo = _reloj.HoyComercial,
            SaldoBase = cuota.MontoTotal - cuota.MontoPagado,
            DiasComputados = 1,
            Importe = importe,
            Estado = EstadoPunitorioAplicado.Aplicado,
            FechaAplicacion = _reloj.AhoraUtc,
            MotivoAplicacion = "Seed pago individual",
            UsuarioAplicacion = "TestUser"
        };
        _context.PunitoriosAplicados.Add(aplicado);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
        return aplicado;
    }

    /// <summary>Confirma que un rechazo no dejó rastro: ni cuota tocada ni movimiento de caja.</summary>
    private async Task AssertSinEfectosAsync(Cuota cuotaOriginal)
    {
        Assert.Empty(_caja.Movimientos);

        var cuotaBd = await RecargarCuotaAsync(cuotaOriginal.Id);
        Assert.Equal(cuotaOriginal.MontoTotal, cuotaBd.MontoTotal);
        Assert.Equal(cuotaOriginal.MontoPagado, cuotaBd.MontoPagado);
        Assert.Equal(cuotaOriginal.RecargoMedioPago, cuotaBd.RecargoMedioPago);
        Assert.Equal(cuotaOriginal.Estado, cuotaBd.Estado);
        Assert.Null(cuotaBd.FechaPago);
    }

    // =========================================================================
    // Casos válidos
    // =========================================================================

    [Fact]
    public async Task ContextoPagoIndividual_SeparaCalculadoInformativoDeAplicadoYCobrable()
    {
        var (_, cuota) = await SeedEscenarioAsync("CTX");
        var tracked = await _context.Cuotas.SingleAsync(c => c.Id == cuota.Id);
        tracked.FechaVencimiento = _reloj.HoyComercial.AddDays(-10).ToDateTime(TimeOnly.MinValue);
        tracked.Estado = EstadoCuota.Vencida;
        tracked.MontoPunitorio = 777m;
        _context.ConfiguracionesPunitorio.Add(new ConfiguracionPunitorio
        {
            Porcentaje = 10m,
            PeriodoDias = 30,
            DiasGracia = 0,
            ProrrateoDiario = true,
            VigenteDesde = _reloj.HoyComercial.AddYears(-1),
            Activa = true
        });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var contexto = Assert.IsType<PagoCuotaContextoResultado>(
            await _service.ObtenerContextoPagoCuotaAsync(cuota.Id));

        Assert.Equal(MontoCuota, contexto.CapitalPendiente);
        Assert.True(contexto.PunitorioCalculadoInformativo > 0m);
        Assert.Equal(0m, contexto.PunitorioAplicadoPendiente);
        Assert.Equal(MontoCuota, contexto.TotalCobrableActual);
        Assert.NotEqual(777m, contexto.PunitorioCalculadoInformativo);
    }

    [Fact]
    public async Task PreviewPagoIndividual_SinAplicacion_ImputaCapitalYNoEscribe()
    {
        var (_, cuotaSeed) = await SeedEscenarioAsync("PRE");
        var cuota = await RecargarCuotaAsync(cuotaSeed.Id);

        var primera = Assert.IsType<PagoCuotaPreviewResultado>(
            await _service.PrevisualizarPagoCuotaAsync(Comando(cuota, 125m)));
        var segunda = Assert.IsType<PagoCuotaPreviewResultado>(
            await _service.PrevisualizarPagoCuotaAsync(Comando(cuota, 125m)));

        Assert.Equal(0m, primera.AplicadoPunitorio);
        Assert.Equal(125m, primera.AplicadoCapital);
        Assert.Equal(0m, primera.Excedente);
        Assert.Equal(primera, segunda);
        Assert.Empty(_caja.Movimientos);
        Assert.Empty(await _context.PagosCuota.AsNoTracking().ToListAsync());
        Assert.Equal(0m, (await RecargarCuotaAsync(cuota.Id)).MontoPagado);
    }

    [Theory]
    [InlineData(40, 40, 0)]
    [InlineData(100, 100, 0)]
    [InlineData(150, 100, 50)]
    public async Task PreviewPagoIndividual_RespetaPrioridadPunitorioLuegoCapital(
        decimal monto,
        decimal esperadoPunitorio,
        decimal esperadoCapital)
    {
        var (_, cuotaSeed) = await SeedEscenarioAsync($"DIST{monto}");
        await SeedPunitorioAplicadoAsync(cuotaSeed, 100m);
        var cuota = await RecargarCuotaAsync(cuotaSeed.Id);

        var preview = Assert.IsType<PagoCuotaPreviewResultado>(
            await _service.PrevisualizarPagoCuotaAsync(Comando(cuota, monto)));

        Assert.Equal(esperadoPunitorio, preview.AplicadoPunitorio);
        Assert.Equal(esperadoCapital, preview.AplicadoCapital);
        Assert.Equal(100m - esperadoPunitorio, preview.PunitorioRestante);
        Assert.Equal(MontoCuota - esperadoCapital, preview.CapitalRestante);
        Assert.Empty(_caja.Movimientos);
        Assert.Empty(await _context.PagosCuota.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task PreviewPagoIndividual_RecargoSeCalculaSeparadoSinPersistir()
    {
        _configuracionPago.AjustePorcentaje = 3m;
        var (_, cuotaSeed) = await SeedEscenarioAsync("RECPRE");
        var cuota = await RecargarCuotaAsync(cuotaSeed.Id);

        var preview = Assert.IsType<PagoCuotaPreviewResultado>(
            await _service.PrevisualizarPagoCuotaAsync(Comando(cuota, 100m, "Transferencia")));

        Assert.Equal(100m, preview.ImporteIngresado);
        Assert.Equal(3m, preview.RecargoMedioPago);
        Assert.Equal(103m, preview.TotalCaja);
        Assert.Empty(_caja.Movimientos);
        Assert.Empty(await _context.PagosCuota.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task ConfirmarPagoIndividual_DevuelveResultadoRealYLiberaSoloCapital()
    {
        _configuracionPago.AjustePorcentaje = 4m;
        var (credito, cuotaSeed) = await SeedEscenarioAsync("CONF");
        await SeedPunitorioAplicadoAsync(cuotaSeed, 50m);
        var cuota = await RecargarCuotaAsync(cuotaSeed.Id);

        var resultado = Assert.IsType<PagoCuotaResultado>(
            await _service.RegistrarPagoCuotaIndividualAsync(Comando(cuota, 75m, "Transferencia")));

        Assert.Equal(75m, resultado.ImporteRecibido);
        Assert.Equal(50m, resultado.AplicadoPunitorio);
        Assert.Equal(25m, resultado.AplicadoCapital);
        Assert.Equal(3m, resultado.RecargoMedioPago);
        Assert.Equal(78m, resultado.TotalCaja);
        Assert.True(resultado.PagoCuotaId > 0);
        Assert.True(resultado.MovimientoCajaId > 0);
        Assert.NotEqual(Convert.ToBase64String(cuota.RowVersion), resultado.CuotaRowVersionBase64);

        var cuotaBd = await RecargarCuotaAsync(cuota.Id);
        Assert.Equal(25m, cuotaBd.MontoPagado);
        var creditoBd = await _context.Creditos.AsNoTracking().SingleAsync(c => c.Id == credito.Id);
        Assert.Equal(MontoCuota - 25m, creditoBd.SaldoPendiente);
        var ledger = await _context.PagosCuota.AsNoTracking().SingleAsync();
        Assert.Equal(50m, ledger.ImporteAplicadoPunitorio);
        Assert.Equal(25m, ledger.ImporteAplicadoCuota);
    }

    [Fact]
    public async Task ConfirmarPagoIndividual_DoblePostParcialConMismoToken_SoloPersisteUno()
    {
        var (_, cuotaSeed) = await SeedEscenarioAsync("TOKEN");
        var cuota = await RecargarCuotaAsync(cuotaSeed.Id);
        var comando = Comando(cuota, 100m);

        var primero = Assert.IsType<PagoCuotaResultado>(
            await _service.RegistrarPagoCuotaIndividualAsync(comando));
        var ex = await Assert.ThrowsAsync<PagoCuotaRechazadoException>(() =>
            _service.RegistrarPagoCuotaIndividualAsync(comando));

        Assert.Equal(MotivoRechazoPagoCuota.Conflicto, ex.Motivo);
        Assert.NotEqual(Convert.ToBase64String(cuota.RowVersion), primero.CuotaRowVersionBase64);
        Assert.Single(_caja.Movimientos);
        Assert.Single(await _context.PagosCuota.AsNoTracking().ToListAsync());
        Assert.Equal(100m, (await RecargarCuotaAsync(cuota.Id)).MontoPagado);
    }

    [Fact]
    public async Task PagarCuota_SaldoCompleto_MarcaPagadaYRegistraCaja()
    {
        var (credito, cuota) = await SeedEscenarioAsync();

        var resultado = await _service.PagarCuotaAsync(Pago(credito.Id, cuota.Id, MontoCuota));

        Assert.True(resultado);

        var cuotaBd = await RecargarCuotaAsync(cuota.Id);
        Assert.Equal(EstadoCuota.Pagada, cuotaBd.Estado);
        Assert.Equal(MontoCuota, cuotaBd.MontoPagado);
        // El importe original de la cuota nunca se toca.
        Assert.Equal(MontoCuota, cuotaBd.MontoTotal);

        var movimiento = Assert.Single(_caja.Movimientos);
        Assert.Equal(cuota.Id, movimiento.CuotaId);
        Assert.Equal(credito.Numero, movimiento.CreditoNumero);
        Assert.Equal(MontoCuota, movimiento.MontoBase);
        Assert.Equal(0m, movimiento.RecargoMedioPago);
        Assert.Equal("testuser", movimiento.Usuario);
    }

    [Fact]
    public async Task PagarCuota_ConRecargo_LoCalculaElServidorYLoSeparaDelImporteBase()
    {
        _configuracionPago.AjustePorcentaje = 3m; // Transferencia +3%
        var (credito, cuota) = await SeedEscenarioAsync();

        await _service.PagarCuotaAsync(Pago(credito.Id, cuota.Id, MontoCuota, "Transferencia"));

        var recargoEsperado = Math.Round(MontoCuota * 0.03m, 2, MidpointRounding.AwayFromZero);

        var cuotaBd = await RecargarCuotaAsync(cuota.Id);
        Assert.Equal(EstadoCuota.Pagada, cuotaBd.Estado);
        Assert.Equal(MontoCuota, cuotaBd.MontoTotal);       // importe original inmutable
        Assert.Equal(MontoCuota, cuotaBd.MontoPagado);      // el recargo no salda la cuota
        Assert.Equal(recargoEsperado, cuotaBd.RecargoMedioPago);

        var movimiento = Assert.Single(_caja.Movimientos);
        Assert.Equal(MontoCuota, movimiento.MontoBase);
        Assert.Equal(recargoEsperado, movimiento.RecargoMedioPago);
        Assert.Equal(TipoPago.Transferencia, movimiento.TipoPago);
    }

    [Fact]
    public async Task PagarCuota_ConDescuento_LoCalculaElServidor()
    {
        _configuracionPago.AjustePorcentaje = -5m; // Efectivo -5%
        var (credito, cuota) = await SeedEscenarioAsync();

        await _service.PagarCuotaAsync(Pago(credito.Id, cuota.Id, MontoCuota));

        var descuentoEsperado = -Math.Round(MontoCuota * 0.05m, 2, MidpointRounding.AwayFromZero);

        var cuotaBd = await RecargarCuotaAsync(cuota.Id);
        Assert.Equal(EstadoCuota.Pagada, cuotaBd.Estado);
        Assert.Equal(MontoCuota, cuotaBd.MontoPagado);
        Assert.Equal(descuentoEsperado, cuotaBd.RecargoMedioPago);

        var movimiento = Assert.Single(_caja.Movimientos);
        Assert.Equal(MontoCuota, movimiento.MontoBase);
        Assert.Equal(descuentoEsperado, movimiento.RecargoMedioPago);
    }

    [Fact]
    public async Task CobrarPrimeraCuota_IgnoraImporteDeEntradaYCobraElSaldoDelServidor()
    {
        _configuracionPago.AjustePorcentaje = 3m;
        var (credito, cuota) = await SeedEscenarioAsync();

        var resultado = await _service.CobrarPrimeraCuotaAlGenerarAsync(credito.Id, "Transferencia");

        Assert.Equal(EstadoCobroPrimeraCuota.Cobrada, resultado.Estado);
        Assert.Equal(cuota.Id, resultado.CuotaId);
        Assert.Equal(MontoCuota, resultado.MontoBase);
        Assert.Equal(Math.Round(MontoCuota * 0.03m, 2, MidpointRounding.AwayFromZero), resultado.RecargoMedioPago);

        var cuotaBd = await RecargarCuotaAsync(cuota.Id);
        Assert.Equal(EstadoCuota.Pagada, cuotaBd.Estado);
        Assert.Equal(MontoCuota, cuotaBd.MontoTotal);

        var movimiento = Assert.Single(_caja.Movimientos);
        Assert.Equal(MontoCuota, movimiento.MontoBase);
    }

    [Fact]
    public async Task CobrarPrimeraCuota_ConCuotaYaImputada_NoAplicaYNoTocaCaja()
    {
        var cliente = await SeedClienteAsync("FIXRVP1");
        var credito = await SeedCreditoAsync(cliente.Id, "FIX-RV-PAGO-P1");
        var cuota = await SeedCuotaAsync(credito.Id, montoPagado: 4_534.20m, estado: EstadoCuota.Parcial);

        var resultado = await _service.CobrarPrimeraCuotaAlGenerarAsync(credito.Id, "Efectivo");

        Assert.Equal(EstadoCobroPrimeraCuota.NoAplica, resultado.Estado);
        Assert.Empty(_caja.Movimientos);

        var cuotaBd = await RecargarCuotaAsync(cuota.Id);
        Assert.Equal(EstadoCuota.Parcial, cuotaBd.Estado);
        Assert.Equal(4_534.20m, cuotaBd.MontoPagado);
    }

    // =========================================================================
    // G7 — manipulación del monto
    // =========================================================================

    [Fact]
    public async Task PagarCuota_MontoMenorAlSaldo_SeImputaComoParcialSinAlterarElImporteOriginal()
    {
        // Caso reproducido en la auditoría (saldo 19.534,20 / envío 123,45): ya no puede
        // saldar la cuota ni tocar su importe. El pago parcial es funcionalidad vigente
        // (EstadoCuota.Parcial + acumulación), por eso se acepta acotado, no se rechaza.
        var (credito, cuota) = await SeedEscenarioAsync();

        await _service.PagarCuotaAsync(Pago(credito.Id, cuota.Id, 123.45m));

        var cuotaBd = await RecargarCuotaAsync(cuota.Id);
        Assert.Equal(EstadoCuota.Parcial, cuotaBd.Estado);
        Assert.Equal(123.45m, cuotaBd.MontoPagado);
        Assert.Equal(MontoCuota, cuotaBd.MontoTotal);

        var movimiento = Assert.Single(_caja.Movimientos);
        Assert.Equal(123.45m, movimiento.MontoBase);
    }

    [Fact]
    public async Task PagarCuota_MontoMayorAlSaldo_Rechaza()
    {
        var (credito, cuota) = await SeedEscenarioAsync();

        var ex = await Assert.ThrowsAsync<PagoCuotaRechazadoException>(() =>
            _service.PagarCuotaAsync(Pago(credito.Id, cuota.Id, MontoCuota + 1m)));

        Assert.Equal(MotivoRechazoPagoCuota.SolicitudInvalida, ex.Motivo);
        Assert.Contains("no puede superar el saldo", ex.Message);
        await AssertSinEfectosAsync(cuota);
    }

    [Fact]
    public async Task PagarCuota_MontoCero_Rechaza()
    {
        var (credito, cuota) = await SeedEscenarioAsync();

        var ex = await Assert.ThrowsAsync<PagoCuotaRechazadoException>(() =>
            _service.PagarCuotaAsync(Pago(credito.Id, cuota.Id, 0m)));

        Assert.Equal(MotivoRechazoPagoCuota.SolicitudInvalida, ex.Motivo);
        await AssertSinEfectosAsync(cuota);
    }

    [Fact]
    public async Task PagarCuota_MontoNegativo_Rechaza()
    {
        var (credito, cuota) = await SeedEscenarioAsync();

        var ex = await Assert.ThrowsAsync<PagoCuotaRechazadoException>(() =>
            _service.PagarCuotaAsync(Pago(credito.Id, cuota.Id, -500m)));

        Assert.Equal(MotivoRechazoPagoCuota.SolicitudInvalida, ex.Motivo);
        await AssertSinEfectosAsync(cuota);
    }

    [Fact]
    public async Task PagarCuota_RecargoDescuentoYTotalDelCliente_SonIgnorados()
    {
        _configuracionPago.AjustePorcentaje = 3m;
        var (credito, cuota) = await SeedEscenarioAsync();

        var pago = Pago(credito.Id, cuota.Id, MontoCuota, "Transferencia");
        // Valores manipulados por el cliente: el servidor no debe tomar ninguno.
        pago.MontoCuota = 1m;
        pago.MontoPunitorio = 99_999m;
        pago.TotalAPagar = 1m;

        await _service.PagarCuotaAsync(pago);

        var cuotaBd = await RecargarCuotaAsync(cuota.Id);
        Assert.Equal(MontoCuota, cuotaBd.MontoTotal);
        Assert.Equal(MontoCuota, cuotaBd.MontoPagado);
        Assert.Equal(0m, cuotaBd.MontoPunitorio);
        Assert.Equal(Math.Round(MontoCuota * 0.03m, 2, MidpointRounding.AwayFromZero), cuotaBd.RecargoMedioPago);

        var movimiento = Assert.Single(_caja.Movimientos);
        Assert.Equal(MontoCuota, movimiento.MontoBase);
        Assert.Equal(Math.Round(MontoCuota * 0.03m, 2, MidpointRounding.AwayFromZero), movimiento.RecargoMedioPago);
    }

    // =========================================================================
    // G8 — pertenencia crédito ↔ cuota
    // =========================================================================

    [Fact]
    public async Task PagarCuota_CuotaDeOtroCredito_RechazaSinTocarLaCuota()
    {
        var (creditoA, _) = await SeedEscenarioAsync("A");

        var clienteB = await SeedClienteAsync("FIXRVB");
        var creditoB = await SeedCreditoAsync(clienteB.Id, "FIX-RV-PAGO-B");
        var cuotaB = await SeedCuotaAsync(creditoB.Id);

        // Combinación inconsistente: crédito A + cuota del crédito B.
        var resultado = await _service.PagarCuotaAsync(Pago(creditoA.Id, cuotaB.Id, MontoCuota));

        Assert.False(resultado);
        await AssertSinEfectosAsync(cuotaB);
    }

    [Fact]
    public async Task PagarCuota_CreditoInexistente_Rechaza()
    {
        var (_, cuota) = await SeedEscenarioAsync();

        var resultado = await _service.PagarCuotaAsync(Pago(999_999, cuota.Id, MontoCuota));

        Assert.False(resultado);
        await AssertSinEfectosAsync(cuota);
    }

    [Fact]
    public async Task PagarCuota_CuotaInexistente_Rechaza()
    {
        var (credito, cuota) = await SeedEscenarioAsync();

        var resultado = await _service.PagarCuotaAsync(Pago(credito.Id, 999_999, MontoCuota));

        Assert.False(resultado);
        await AssertSinEfectosAsync(cuota);
    }

    [Fact]
    public async Task PagarCuota_CuotaEliminada_Rechaza()
    {
        var (credito, cuota) = await SeedEscenarioAsync();

        var tracked = await _context.Cuotas.FirstAsync(c => c.Id == cuota.Id);
        tracked.IsDeleted = true;
        await _context.SaveChangesAsync();

        var resultado = await _service.PagarCuotaAsync(Pago(credito.Id, cuota.Id, MontoCuota));

        Assert.False(resultado);
        Assert.Empty(_caja.Movimientos);
    }

    // =========================================================================
    // Estados y medio de pago
    // =========================================================================

    [Fact]
    public async Task PagarCuota_CuotaYaPagada_RechazaConConflicto()
    {
        var cliente = await SeedClienteAsync("FIXRVPG");
        var credito = await SeedCreditoAsync(cliente.Id, "FIX-RV-PAGO-PG");
        var cuota = await SeedCuotaAsync(credito.Id, montoPagado: MontoCuota, estado: EstadoCuota.Pagada);

        var ex = await Assert.ThrowsAsync<PagoCuotaRechazadoException>(() =>
            _service.PagarCuotaAsync(Pago(credito.Id, cuota.Id, MontoCuota)));

        Assert.Equal(MotivoRechazoPagoCuota.Conflicto, ex.Motivo);
        Assert.Empty(_caja.Movimientos);
    }

    [Fact]
    public async Task PagarCuota_CuotaSinSaldo_RechazaConConflicto()
    {
        // Estado Parcial pero ya cubierto: combinación de estado inválida para cobrar.
        var cliente = await SeedClienteAsync("FIXRVSS");
        var credito = await SeedCreditoAsync(cliente.Id, "FIX-RV-PAGO-SS");
        var cuota = await SeedCuotaAsync(credito.Id, montoPagado: MontoCuota, estado: EstadoCuota.Parcial);

        var ex = await Assert.ThrowsAsync<PagoCuotaRechazadoException>(() =>
            _service.PagarCuotaAsync(Pago(credito.Id, cuota.Id, 100m)));

        Assert.Equal(MotivoRechazoPagoCuota.Conflicto, ex.Motivo);
        Assert.Contains("no tiene saldo pendiente", ex.Message);
        Assert.Empty(_caja.Movimientos);
    }

    [Fact]
    public async Task PagarCuota_CuotaCancelada_RechazaConConflicto()
    {
        var cliente = await SeedClienteAsync("FIXRVCA");
        var credito = await SeedCreditoAsync(cliente.Id, "FIX-RV-PAGO-CA");
        var cuota = await SeedCuotaAsync(credito.Id, estado: EstadoCuota.Cancelada);

        var ex = await Assert.ThrowsAsync<PagoCuotaRechazadoException>(() =>
            _service.PagarCuotaAsync(Pago(credito.Id, cuota.Id, MontoCuota)));

        Assert.Equal(MotivoRechazoPagoCuota.Conflicto, ex.Motivo);
        await AssertSinEfectosAsync(cuota);
    }

    [Fact]
    public async Task PagarCuota_MedioPagoInexistente_Rechaza()
    {
        var (credito, cuota) = await SeedEscenarioAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.PagarCuotaAsync(Pago(credito.Id, cuota.Id, MontoCuota, "Bitcoin")));

        Assert.Contains("Medio de pago inválido", ex.Message);
        await AssertSinEfectosAsync(cuota);
    }

    [Fact]
    public async Task PagarCuota_MedioPagoDeshabilitado_Rechaza()
    {
        var (credito, cuota) = await SeedEscenarioAsync();

        _context.ConfiguracionesPago.Add(new ConfiguracionPago
        {
            TipoPago = TipoPago.Transferencia,
            Nombre = "Transferencia",
            Activo = false
        });
        await _context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<PagoCuotaRechazadoException>(() =>
            _service.PagarCuotaAsync(Pago(credito.Id, cuota.Id, MontoCuota, "Transferencia")));

        Assert.Equal(MotivoRechazoPagoCuota.SolicitudInvalida, ex.Motivo);
        Assert.Contains("no está habilitado", ex.Message);
        await AssertSinEfectosAsync(cuota);
    }

    // =========================================================================
    // Cobros duplicados y concurrencia
    // =========================================================================

    [Fact]
    public async Task PagarCuota_SegundoEnvioDelMismoPago_NoDuplicaElCobro()
    {
        var (credito, cuota) = await SeedEscenarioAsync();

        Assert.True(await _service.PagarCuotaAsync(Pago(credito.Id, cuota.Id, MontoCuota)));

        var ex = await Assert.ThrowsAsync<PagoCuotaRechazadoException>(() =>
            _service.PagarCuotaAsync(Pago(credito.Id, cuota.Id, MontoCuota)));

        Assert.Equal(MotivoRechazoPagoCuota.Conflicto, ex.Motivo);
        Assert.Single(_caja.Movimientos);

        var cuotaBd = await RecargarCuotaAsync(cuota.Id);
        Assert.Equal(MontoCuota, cuotaBd.MontoPagado);
    }

    [Fact]
    public async Task PagarCuota_DosIntentosConcurrentes_SoloUnoCobra()
    {
        var (credito, cuota) = await SeedEscenarioAsync();

        // Cada intento con su propio DbContext/servicio, como dos requests simultáneos.
        using var contextoA = CrearContexto();
        using var contextoB = CrearContexto();
        var cajaA = new StubCajaServicePagoSeguro(contextoA);
        var cajaB = new StubCajaServicePagoSeguro(contextoB);
        var servicioA = CrearService(contextoA, cajaA, _configuracionPago);
        var servicioB = CrearService(contextoB, cajaB, _configuracionPago);

        var intentoA = Task.Run(() => servicioA.PagarCuotaAsync(Pago(credito.Id, cuota.Id, MontoCuota)));
        var intentoB = Task.Run(() => servicioB.PagarCuotaAsync(Pago(credito.Id, cuota.Id, MontoCuota)));

        var resultados = await Task.WhenAll(
            EjecutarProtegidoAsync(intentoA),
            EjecutarProtegidoAsync(intentoB));

        Assert.Equal(1, resultados.Count(r => r));
        Assert.Equal(1, cajaA.Movimientos.Count + cajaB.Movimientos.Count);

        var cuotaBd = await RecargarCuotaAsync(cuota.Id);
        Assert.Equal(EstadoCuota.Pagada, cuotaBd.Estado);
        Assert.Equal(MontoCuota, cuotaBd.MontoPagado);
    }

    /// <summary>
    /// PUN-ML6: dos cobros simultáneos contra la misma cuota, cada uno intentando cubrir una
    /// aplicación de punitorio activa (Importe 50) con un pago de 50. La transacción serializable
    /// obliga a que se resuelvan en algún orden real (nunca los dos viendo "50 pendientes" a la
    /// vez): el que corre primero paga contra el punitorio y lo salda; el que corre segundo ya no
    /// encuentra aplicación activa (Estado pasó a Pagado) y su pago se imputa íntegro a la cuota.
    /// Ninguno de los dos falla — pero la aplicación jamás recibe más de su Importe.
    /// </summary>
    [Fact]
    public async Task PagarCuota_DosIntentosConcurrentes_ConAplicacionActiva_NoSobrepaganLaAplicacion()
    {
        var (credito, cuota) = await SeedEscenarioAsync();

        var aplicado = new PunitorioAplicado
        {
            CuotaId = cuota.Id,
            FechaCalculo = _reloj.HoyComercial,
            SaldoBase = MontoCuota,
            DiasComputados = 1,
            Importe = 50m,
            Estado = EstadoPunitorioAplicado.Aplicado,
            FechaAplicacion = _reloj.AhoraUtc,
            MotivoAplicacion = "Seed de test",
            UsuarioAplicacion = "TestUser"
        };
        _context.PunitoriosAplicados.Add(aplicado);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        using var contextoA = CrearContexto();
        using var contextoB = CrearContexto();
        var cajaA = new StubCajaServicePagoSeguro(contextoA);
        var cajaB = new StubCajaServicePagoSeguro(contextoB);
        var servicioA = CrearService(contextoA, cajaA, _configuracionPago, _reloj);
        var servicioB = CrearService(contextoB, cajaB, _configuracionPago, _reloj);

        var intentoA = Task.Run(() => servicioA.PagarCuotaAsync(Pago(credito.Id, cuota.Id, 50m)));
        var intentoB = Task.Run(() => servicioB.PagarCuotaAsync(Pago(credito.Id, cuota.Id, 50m)));

        var resultados = await Task.WhenAll(
            EjecutarProtegidoAsync(intentoA),
            EjecutarProtegidoAsync(intentoB));

        // Ambos pagos son legítimos y ninguno tiene motivo para ser rechazado (el saldo de la
        // cuota sobra para los dos): lo que importa es a dónde se imputa cada uno.
        Assert.Equal(2, resultados.Count(r => r));

        var pagosPunitorio = await _context.PagosCuota
            .Where(p => p.PunitorioAplicadoId == aplicado.Id && p.Estado == EstadoPagoCuota.Aplicado)
            .ToListAsync();

        var totalAtribuidoAlPunitorio = pagosPunitorio.Sum(p => p.ImporteAplicadoPunitorio ?? 0m);
        Assert.Equal(50m, totalAtribuidoAlPunitorio); // nunca más que el Importe de la aplicación

        var aplicadoBd = await _context.PunitoriosAplicados.AsNoTracking().SingleAsync(p => p.Id == aplicado.Id);
        Assert.Equal(EstadoPunitorioAplicado.Pagado, aplicadoBd.Estado);

        // El pago que NO fue al punitorio se imputó íntegro a la cuota: no se pierde ni se duplica.
        var cuotaBd = await RecargarCuotaAsync(cuota.Id);
        Assert.Equal(50m, cuotaBd.MontoPagado);
    }

    /// <summary>El intento perdedor puede fallar por conflicto de estado o de base; ambos son rechazo.</summary>
    private static async Task<bool> EjecutarProtegidoAsync(Task<bool> intento)
    {
        try
        {
            return await intento;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // =========================================================================
    // PUN-ML7 — semántica de Cuota.FechaPago (fecha comercial del servidor, solo al quedar Pagada)
    // =========================================================================

    [Fact]
    public async Task PagarCuota_SaldoCompleto_FechaPago_UsaFechaComercialDelServidor_IgnorandoLaDelBrowser()
    {
        var (credito, cuota) = await SeedEscenarioAsync();

        // El operador (o un cliente HTTP malicioso) manda una fecha completamente distinta a hoy:
        // el servidor debe ignorarla por completo.
        var pago = Pago(credito.Id, cuota.Id, MontoCuota);
        pago.FechaPago = _reloj.HoyComercial.AddYears(-5).ToDateTime(TimeOnly.MinValue);

        await _service.PagarCuotaAsync(pago);

        var cuotaBd = await RecargarCuotaAsync(cuota.Id);
        Assert.Equal(EstadoCuota.Pagada, cuotaBd.Estado);
        Assert.Equal(_reloj.HoyComercial.ToDateTime(TimeOnly.MinValue), cuotaBd.FechaPago);
    }

    [Fact]
    public async Task PagarCuota_MontoMenorAlSaldo_NoEstableceFechaPago()
    {
        var (credito, cuota) = await SeedEscenarioAsync();

        await _service.PagarCuotaAsync(Pago(credito.Id, cuota.Id, 123.45m));

        var cuotaBd = await RecargarCuotaAsync(cuota.Id);
        Assert.Equal(EstadoCuota.Parcial, cuotaBd.Estado);
        Assert.Null(cuotaBd.FechaPago);
    }

    [Fact]
    public async Task PagarCuota_ImputadoIntegramenteAPunitorio_NoEstableceFechaPagoNiMarcaPagada()
    {
        var (credito, cuota) = await SeedEscenarioAsync();
        var aplicado = new PunitorioAplicado
        {
            CuotaId = cuota.Id,
            FechaCalculo = _reloj.HoyComercial,
            SaldoBase = MontoCuota,
            DiasComputados = 1,
            Importe = 50m,
            Estado = EstadoPunitorioAplicado.Aplicado,
            FechaAplicacion = _reloj.AhoraUtc,
            MotivoAplicacion = "Seed de test",
            UsuarioAplicacion = "TestUser"
        };
        _context.PunitoriosAplicados.Add(aplicado);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        // Paga exactamente el punitorio pendiente: DistribuirPago lo imputa todo ahí, nada a cuota.
        await _service.PagarCuotaAsync(Pago(credito.Id, cuota.Id, 50m));

        var cuotaBd = await RecargarCuotaAsync(cuota.Id);
        Assert.Equal(0m, cuotaBd.MontoPagado);
        Assert.Equal(EstadoCuota.Pendiente, cuotaBd.Estado);
        Assert.Null(cuotaBd.FechaPago);
    }

    [Fact]
    public async Task PagarCuota_ImputadoIntegramenteAPunitorio_SobreCuotaYaVencida_ResuelveVencidaNoPendiente()
    {
        // PUN-ML7 (corrección): antes de eliminar el resolver de 3 args de CreditoService, este
        // escenario era el bug real que motivó la corrección — un pago que se imputa íntegramente a
        // punitorio (MontoPagado se queda en 0) sobre una cuota cuyo capital sigue vencido debía
        // seguir reflejando la mora real (Vencida), no "esconderla" detrás de Pendiente.
        var cliente = await SeedClienteAsync("FIXRVVENC");
        var credito = await SeedCreditoAsync(cliente.Id, "FIX-RV-PAGO-VENC");
        var cuota = new Cuota
        {
            CreditoId = credito.Id,
            NumeroCuota = 1,
            MontoCapital = MontoCuota,
            MontoInteres = 0m,
            MontoTotal = MontoCuota,
            MontoPagado = 0m,
            MontoPunitorio = 0m,
            // Ya vencida hace 10 días — y ya transicionada por ActualizarEstadoCuotasAsync, como
            // ocurriría en producción antes de que alguien pague el punitorio.
            Estado = EstadoCuota.Vencida,
            FechaVencimiento = _reloj.HoyComercial.AddDays(-10).ToDateTime(TimeOnly.MinValue)
        };
        _context.Cuotas.Add(cuota);
        await _context.SaveChangesAsync();

        var aplicado = new PunitorioAplicado
        {
            CuotaId = cuota.Id,
            FechaCalculo = _reloj.HoyComercial,
            SaldoBase = MontoCuota,
            DiasComputados = 10,
            Importe = 50m,
            Estado = EstadoPunitorioAplicado.Aplicado,
            FechaAplicacion = _reloj.AhoraUtc,
            MotivoAplicacion = "Seed de test",
            UsuarioAplicacion = "TestUser"
        };
        _context.PunitoriosAplicados.Add(aplicado);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        // Paga exactamente el punitorio pendiente: DistribuirPago lo imputa todo ahí, nada a cuota.
        await _service.PagarCuotaAsync(Pago(credito.Id, cuota.Id, 50m));

        var cuotaBd = await RecargarCuotaAsync(cuota.Id);
        Assert.Equal(0m, cuotaBd.MontoPagado);
        Assert.Equal(EstadoCuota.Vencida, cuotaBd.Estado);
        Assert.Null(cuotaBd.FechaPago);
    }
}

file sealed class StubCreditoDisponibleServicePagoSeguro : ICreditoDisponibleService
{
    public Task<decimal> ObtenerLimitePorPuntajeAsync(int puntaje, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<decimal> CalcularSaldoVigenteAsync(int clienteId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<CreditoDisponibleResultado> CalcularDisponibleAsync(int clienteId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<(bool Ok, List<string> Errores)> GuardarLimitesPorPuntajeAsync(IReadOnlyList<(int Puntaje, decimal LimiteMonto, bool Activo)> items, string usuario) => throw new NotImplementedException();
    public Task<List<PuntajeCreditoLimite>> GetAllLimitesPorPuntajeAsync() => throw new NotImplementedException();
}

file sealed class StubCurrentUserServicePagoSeguro : ICurrentUserService
{
    public string GetUsername() => "testuser";
    public string GetUserId() => "system";
    public bool IsAuthenticated() => true;
    public string? GetEmail() => "test@test.com";
    public bool IsInRole(string role) => false;
    public bool HasPermission(string modulo, string accion) => false;
    public string? GetIpAddress() => "127.0.0.1";
}
