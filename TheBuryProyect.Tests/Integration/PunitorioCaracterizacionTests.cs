using AutoMapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Helpers;
using TheBuryProject.Models.DTOs;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Integration;

// ---------------------------------------------------------------------------
// PUN-ML1 — Caracterización del comportamiento actual de punitorios por mora.
//
// Este archivo NO modifica código productivo. Tiene dos mitades, separadas por región:
//
//   LEGACY (verde)          documenta, con asserts sobre el sistema real, el comportamiento
//                            que existe HOY — incluidos sus bugs. Sirve de red de seguridad:
//                            si un lote futuro cambia alguno de estos números sin querer, esta
//                            mitad se pone roja y avisa.
//
//   CONTRATO NUEVO (rojo)   expresa la regla de negocio aprobada (ver plan de punitorios,
//                            §8-9) contra el mismo sistema real. Está roja a propósito: nada
//                            de esto está implementado todavía. Se espera que se ponga verde
//                            recién en PUN-ML6, cuando el cobro deje de usar
//                            CalcularPunitorioActualizado.
//
// Ningún assert de la mitad LEGACY debe leerse como "esto es lo correcto": es exactamente lo
// que el pedido pide no perpetuar. La mitad CONTRATO NUEVO es la que fija la regla correcta.
// ---------------------------------------------------------------------------

file sealed class StubCajaServiceCaracterizacion : ICajaService
{
    // PUN-ML2: RegistrarMovimientoCuotaAsync ahora persiste de verdad (igual que CajaService
    // real), porque PagoCuota.MovimientoCajaId es una FK real que el pago debe poder resolver.
    private readonly AppDbContext _context;
    private bool _aperturaPersistida;

    public StubCajaServiceCaracterizacion(AppDbContext context) => _context = context;

    public Task<decimal?> ObtenerUltimoEfectivoCierreAsync(int cajaId) => Task.FromResult<decimal?>(null);
    public AperturaCaja? AperturaActivaParaVenta { get; set; } = new() { Id = 1 };

    public async Task<MovimientoCaja?> RegistrarMovimientoCuotaAsync(
        int cuotaId, string creditoNumero, int numeroCuota,
        decimal monto, string medioPago, string usuario)
    {
        if (AperturaActivaParaVenta == null)
            return null;

        if (!_aperturaPersistida)
        {
            _context.Cajas.Add(new Caja { Id = AperturaActivaParaVenta.Id, Codigo = "C1", Nombre = "Caja test", IsDeleted = false });
            _context.AperturasCaja.Add(new AperturaCaja
            {
                Id = AperturaActivaParaVenta.Id, CajaId = AperturaActivaParaVenta.Id,
                MontoInicial = 0m, UsuarioApertura = usuario, Cerrada = false, IsDeleted = false
            });
            await _context.SaveChangesAsync();
            _aperturaPersistida = true;
        }

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
    public Task<Dictionary<int, DateTime>> ObtenerUltimosCierresPorCajaAsync() => throw new NotImplementedException();
    public Task<DetallesAperturaViewModel> ObtenerDetallesAperturaAsync(int aperturaId) => throw new NotImplementedException();
    public Task<ReporteCajaViewModel> GenerarReporteCajaAsync(DateTime fechaDesde, DateTime fechaHasta, int? cajaId = null) => throw new NotImplementedException();
    public Task<HistorialCierresViewModel> ObtenerEstadisticasCierresAsync(int? cajaId = null, DateTime? fechaDesde = null, DateTime? fechaHasta = null) => throw new NotImplementedException();
}

file sealed class StubFinancialServiceCaracterizacion : IFinancialCalculationService
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

file sealed class StubCreditoDisponibleServiceCaracterizacion : ICreditoDisponibleService
{
    public Task<decimal> ObtenerLimitePorPuntajeAsync(int puntaje, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<decimal> CalcularSaldoVigenteAsync(int clienteId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<CreditoDisponibleResultado> CalcularDisponibleAsync(int clienteId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<(bool Ok, List<string> Errores)> GuardarLimitesPorPuntajeAsync(IReadOnlyList<(int Puntaje, decimal LimiteMonto, bool Activo)> items, string usuario) => throw new NotImplementedException();
    public Task<List<PuntajeCreditoLimite>> GetAllLimitesPorPuntajeAsync() => throw new NotImplementedException();
}

file sealed class StubCurrentUserServiceCaracterizacion : ICurrentUserService
{
    public string GetUsername() => "TestUser";
    public string GetUserId() => "system";
    public bool IsAuthenticated() => true;
    public string? GetEmail() => "test@test.com";
    public bool IsInRole(string role) => false;

    // Autorizado para aplicar/anular punitorios (PUN-ML6, aplicación explícita antes del cobro).
    public bool HasPermission(string modulo, string accion) =>
        modulo == "cobranzas" && (accion == "applyfine" || accion == "revertfine");

    public string? GetIpAddress() => "127.0.0.1";
}

public class PunitorioCaracterizacionTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly CreditoService _service;
    private readonly TheBuryProject.Tests.Helpers.RelojComercialFake _reloj;

    public PunitorioCaracterizacionTests()
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

        _reloj = new TheBuryProject.Tests.Helpers.RelojComercialFake(new DateOnly(2026, 1, 1));

        _service = new CreditoService(
            _context,
            mapper,
            NullLogger<CreditoService>.Instance,
            new StubFinancialServiceCaracterizacion(),
            new StubCajaServiceCaracterizacion(_context),
            new StubCreditoDisponibleServiceCaracterizacion(),
            new StubCurrentUserServiceCaracterizacion(),
            reloj: _reloj);
    }

    private DateTime Hoy => _reloj.HoyComercial.ToDateTime(TimeOnly.MinValue);

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // Helpers de seed
    // -------------------------------------------------------------------------

    private int _nextId = 1;

    private async Task<(Cliente cliente, Credito credito, Cuota cuota)> SeedCreditoConCuota(
        decimal tasaInteresCredito,
        decimal montoTotalCuota,
        decimal montoCapital,
        decimal montoInteres,
        DateTime fechaVencimiento)
    {
        var id = _nextId++;

        var cliente = new Cliente
        {
            Id = id,
            Nombre = "Cliente",
            Apellido = $"Test{id}",
            NumeroDocumento = $"9000{id:D5}",
            IsDeleted = false
        };
        _context.Clientes.Add(cliente);

        var credito = new Credito
        {
            Id = id,
            ClienteId = id,
            IsDeleted = false,
            Numero = $"CRED{id:D4}",
            Estado = EstadoCredito.Activo,
            TasaInteres = tasaInteresCredito,
            MontoSolicitado = montoTotalCuota,
            MontoAprobado = montoTotalCuota,
            SaldoPendiente = montoCapital,
            CantidadCuotas = 1,
            MontoCuota = montoTotalCuota,
            TotalAPagar = montoTotalCuota
        };
        _context.Creditos.Add(credito);

        var cuota = new Cuota
        {
            Id = id,
            CreditoId = id,
            NumeroCuota = 1,
            FechaVencimiento = fechaVencimiento,
            MontoTotal = montoTotalCuota,
            MontoCapital = montoCapital,
            MontoInteres = montoInteres,
            MontoPagado = 0m,
            MontoPunitorio = 0m,
            Estado = EstadoCuota.Pendiente,
            IsDeleted = false
        };
        _context.Cuotas.Add(cuota);

        await _context.SaveChangesAsync();
        return (cliente, credito, cuota);
    }

    private async Task PagarAsync(int creditoId, int cuotaId, decimal monto)
    {
        var pago = new PagarCuotaViewModel
        {
            CreditoId = creditoId,
            CuotaId = cuotaId,
            MontoPagado = monto,
            FechaPago = Hoy,
            MedioPago = "Efectivo"
        };
        var ok = await _service.PagarCuotaAsync(pago);
        Assert.True(ok, $"El pago de la cuota {cuotaId} debería haberse registrado.");
    }

    /// <summary>
    /// Configuración que reproduce los parámetros del contrato local (<see cref="ContratoPunitorioNuevo"/>):
    /// 10% cada 20 días, 5 días de gracia. Sólo la usan los tests de la región CONTRATO NUEVO que
    /// necesitan aplicar de verdad (vía <see cref="AplicarPunitorioAsync"/>) — la región LEGACY
    /// prueba justamente la ausencia de cómputo automático, con o sin configuración presente.
    /// </summary>
    private async Task SeedConfiguracionPunitorioContratoAsync()
    {
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
    }

    /// <summary>
    /// Aplica un punitorio de verdad (PUN-ML5, misma autorización real) usando el mismo
    /// <c>AppDbContext</c>/reloj del test. PUN-ML6 prohíbe que el cobro aplique nada por sí
    /// solo: todo punitorio que un test quiera cobrar tiene que pasar primero por acá.
    /// </summary>
    private async Task<PunitorioAplicado> AplicarPunitorioAsync(int cuotaId, string motivo = "Aplicación de test")
    {
        var punitorioService = new PunitorioService(
            _context,
            new PunitorioCalculator(),
            _reloj,
            new StubCurrentUserServiceCaracterizacion(),
            NullLogger<PunitorioService>.Instance);

        return await punitorioService.AplicarAsync(cuotaId, new PunitorioAplicarComando { Motivo = motivo });
    }

    // ===========================================================================================
    // REGIÓN 1 — COMPORTAMIENTO LEGACY (verde). Documenta el bug actual, no lo valida como regla.
    // ===========================================================================================

    [Fact]
    public async Task Legacy_UsaTasaInteresDelCreditoComoTasaAnual_YaNoAplica_PUN_ML6()
    {
        // Documentaba que el punitorio usaba MontoTotal * (TasaInteres/100/12) * (diasAtraso/30):
        // la tasa del CRÉDITO, no una configuración de punitorio separada. PUN-ML6 elimina esa
        // fórmula del cobro productivo por completo: sin importar la tasa, cobrar una cuota
        // vencida ya no calcula ni escribe ningún punitorio por sí sola.
        var (_, _, cuota24) = await SeedCreditoConCuota(24m, 1_000m, 800m, 200m, Hoy.AddDays(-30));
        await PagarAsync(cuota24.CreditoId, cuota24.Id, 1_000m);
        var actualizada24 = await _context.Cuotas.FindAsync(cuota24.Id);

        var (_, _, cuota36) = await SeedCreditoConCuota(36m, 1_000m, 800m, 200m, Hoy.AddDays(-30));
        await PagarAsync(cuota36.CreditoId, cuota36.Id, 1_000m);
        var actualizada36 = await _context.Cuotas.FindAsync(cuota36.Id);

        Assert.NotNull(actualizada24);
        Assert.NotNull(actualizada36);
        Assert.Equal(0m, actualizada24!.MontoPunitorio);
        Assert.Equal(0m, actualizada36!.MontoPunitorio);
        Assert.Equal(actualizada24.MontoPunitorio, actualizada36.MontoPunitorio);
    }

    [Fact]
    public async Task Legacy_ConRecargoCeroEnElCredito_NuncaDevengaPunitorio()
    {
        // A1: un crédito con 0% de recargo total (caso explícitamente pedido) no genera punitorio
        // jamás, sin importar cuántos días de atraso tenga. La regla nueva exige lo contrario.
        var (_, _, cuota) = await SeedCreditoConCuota(0m, 1_000m, 800m, 200m, Hoy.AddDays(-60));

        await PagarAsync(cuota.CreditoId, cuota.Id, 1_000m);
        var actualizada = await _context.Cuotas.FindAsync(cuota.Id);

        Assert.NotNull(actualizada);
        Assert.Equal(0m, actualizada!.MontoPunitorio);
    }

    [Fact]
    public async Task Legacy_BaseDelCalculo_IgnoraPagosParcialesPrevios_YaNoAplica_PUN_ML6()
    {
        // Documentaba que la fórmula usaba cuota.MontoTotal (valor original), nunca
        // cuota.MontoPagado: una cuota con pago parcial previo devengaba el mismo punitorio que
        // una idéntica sin pagos. PUN-ML6: sin aplicación autorizada explícita ninguna de las dos
        // devenga nada — el punitorio cobrado es cero para ambas, con o sin pago parcial previo.
        // (El contrato correcto — que SÍ deben diferir cuando hay una aplicación real — está en
        // ContratoNuevo_LaBaseDebeSerElSaldoImpago_UnPagoParcialPrevioDebeReducirElPunitorio.)
        var fechaVencimiento = Hoy.AddDays(10);

        var (_, creditoA, cuotaA) = await SeedCreditoConCuota(24m, 1_000m, 800m, 200m, fechaVencimiento);
        var (_, creditoB, cuotaB) = await SeedCreditoConCuota(24m, 1_000m, 800m, 200m, fechaVencimiento);

        await PagarAsync(creditoB.Id, cuotaB.Id, 400m);

        _reloj.HoyComercial = DateOnly.FromDateTime(fechaVencimiento).AddDays(30);

        await PagarAsync(creditoA.Id, cuotaA.Id, 100m);
        await PagarAsync(creditoB.Id, cuotaB.Id, 100m);

        var finalA = await _context.Cuotas.FindAsync(cuotaA.Id);
        var finalB = await _context.Cuotas.FindAsync(cuotaB.Id);

        Assert.NotNull(finalA);
        Assert.NotNull(finalB);
        Assert.Equal(0m, finalA!.MontoPunitorio);
        Assert.Equal(0m, finalB!.MontoPunitorio);
    }

    [Fact]
    public async Task Legacy_DiasDeGraciaSonIgnorados_YaNoAplica_PUN_ML6()
    {
        // Documentaba que el día 1 de atraso ya devengaba punitorio (sin noción de gracia).
        // PUN-ML6: sin aplicación autorizada explícita, ni el día 1 ni ningún otro día devengan
        // nada por sí solos — la gracia ahora vive exclusivamente en ConfiguracionPunitorio,
        // consultada por IPunitorioCalculator, nunca por el cobro.
        var (_, credito, cuota) = await SeedCreditoConCuota(24m, 1_000m, 800m, 200m, Hoy.AddDays(-1));

        await PagarAsync(credito.Id, cuota.Id, 1_000m);
        var actualizada = await _context.Cuotas.FindAsync(cuota.Id);

        Assert.NotNull(actualizada);
        Assert.Equal(0m, actualizada!.MontoPunitorio);
    }

    [Fact]
    public async Task Legacy_MontoPunitorioSePisaEnCadaCobro_YaNoAplica_PUN_ML6()
    {
        // Documentaba que cuota.MontoPunitorio se REASIGNABA (no se acumulaba) en cada cobro,
        // recalculado desde cero con los días de atraso vigentes al momento del pago. PUN-ML6
        // elimina el escritor: dos cobros parciales sucesivos sobre la misma cuota vencida, en
        // fechas comerciales distintas, dejan el campo legacy exactamente como fue seedeado.
        var fechaVencimiento = Hoy;
        var (_, credito, cuota) = await SeedCreditoConCuota(24m, 1_000m, 800m, 200m, fechaVencimiento);

        _reloj.HoyComercial = DateOnly.FromDateTime(fechaVencimiento).AddDays(10);
        await PagarAsync(credito.Id, cuota.Id, 100m);

        _reloj.HoyComercial = DateOnly.FromDateTime(fechaVencimiento).AddDays(40);
        await PagarAsync(credito.Id, cuota.Id, 100m);

        var final = await _context.Cuotas.FindAsync(cuota.Id);

        Assert.NotNull(final);
        Assert.Equal(0m, final!.MontoPunitorio);
    }

    [Fact]
    public async Task Legacy_A3_PagarUnMontoEquivalenteAlPunitorio_LiberaCapitalPendienteIndebidamente()
    {
        // A3: CalcularCapitalPendienteCuota imputa cuota.MontoPagado proporcionalmente a capital
        // (MontoCapital/MontoTotal), sin distinguir si lo pagado fue capital, interés o
        // punitorio. Pagar un monto que conceptualmente es 100% punitorio libera cupo de
        // crédito como si una parte hubiese sido capital.
        var (_, credito, cuota) = await SeedCreditoConCuota(24m, 1_000m, 800m, 200m, Hoy.AddDays(-150));
        // 1000 * (24/100/12) * (150/30) = 100.00 exactos: el punitorio devengado a los 150 días.
        var punitorioEsperado = 1_000m * (24m / 100m / 12m) * (150m / 30m);
        Assert.Equal(100.00m, punitorioEsperado);

        // Paga exactamente el punitorio, nada de capital ni interés.
        await PagarAsync(credito.Id, cuota.Id, 100m);

        var creditoActualizado = await _context.Creditos.FindAsync(credito.Id);
        Assert.NotNull(creditoActualizado);

        // capitalPagadoEstimado = round(100 * (800/1000)) = 80 → capitalPendiente = 720.
        // Si el pago hubiera imputado 100% a punitorio (regla nueva), SaldoPendiente seguiría
        // siendo 800.00: ningún peso del pago corresponde a capital.
        Assert.Equal(720.00m, creditoActualizado!.SaldoPendiente);
        Assert.True(creditoActualizado.SaldoPendiente < 800m,
            "Pagar solo punitorio no debería reducir el capital pendiente (cupo liberado indebidamente).");
    }

    [Fact]
    public async Task Legacy_A2_ElValorMostradoAntesDePagar_YaNoAplica_PUN_ML6()
    {
        // A2 documentaba que el servidor recalculaba un punitorio mayor a último momento (nunca
        // mostrado antes de pagar), dejando parcial un pago que el operador creía total. PUN-ML6:
        // sin una aplicación autorizada explícita no hay ningún punitorio oculto que "descubrir"
        // al cobrar — pagar el MontoTotal completo salda la cuota exactamente, sin sorpresas.
        var (_, credito, cuota) = await SeedCreditoConCuota(24m, 1_000m, 800m, 200m, Hoy.AddDays(-90));

        Assert.Equal(0m, cuota.MontoPunitorio);

        await PagarAsync(credito.Id, cuota.Id, 1_000m);

        var final = await _context.Cuotas.FindAsync(cuota.Id);
        Assert.NotNull(final);

        Assert.Equal(0m, final!.MontoPunitorio);
        Assert.Equal(EstadoCuota.Pagada, final.Estado);
    }

    // ===========================================================================================
    // REGIÓN 2 — CONTRATO NUEVO (rojo salvo donde se indica). Regla aprobada, pendiente de
    // implementación en PUN-ML2..ML6. Estos asserts describen lo que el sistema DEBERÍA hacer,
    // no lo que hace.
    // ===========================================================================================

    /// <summary>
    /// Reproduce el contrato aprobado (plan de punitorios, §8-9): interés simple sobre saldo
    /// impago, con gracia y prorrateo por período configurable. NO es código productivo — solo
    /// expresa el resultado que PUN-ML4..ML6 deben producir, para contrastarlo contra el
    /// comportamiento real actual.
    /// </summary>
    private static class ContratoPunitorioNuevo
    {
        public static decimal Calcular(
            decimal saldoImpago, decimal porcentaje, int periodoDias, int diasGracia, int diasTranscurridos)
        {
            if (diasTranscurridos <= diasGracia)
                return 0m;

            var importe = saldoImpago * (porcentaje / 100m) * diasTranscurridos / periodoDias;
            return Math.Round(importe, 2, MidpointRounding.AwayFromZero);
        }
    }

    [Fact]
    public async Task ContratoNuevo_DentroDeLaGracia_PunitorioDebeSerCero()
    {
        // Config de contrato: 10% cada 20 días, 5 días de gracia (ejemplo del pedido). A los 5
        // días de atraso (== gracia) no hay punitorio autoritativo que aplicar — PunitorioService
        // lo rechaza como no aplicable — y por lo tanto el cobro no puede cobrar nada.
        await SeedConfiguracionPunitorioContratoAsync();
        var (_, credito, cuota) = await SeedCreditoConCuota(24m, 1_000m, 800m, 200m, Hoy.AddDays(-5));

        var esperadoPorContrato = ContratoPunitorioNuevo.Calcular(
            saldoImpago: 1_000m, porcentaje: 10m, periodoDias: 20, diasGracia: 5, diasTranscurridos: 5);
        Assert.Equal(0m, esperadoPorContrato); // el propio contrato, verificado contra sí mismo

        await Assert.ThrowsAsync<TheBuryProject.Services.Exceptions.PunitorioAplicadoRechazadoException>(
            () => AplicarPunitorioAsync(cuota.Id));

        await PagarAsync(credito.Id, cuota.Id, 1_000m);
        var fila = await _context.PagosCuota.SingleAsync(p => p.CuotaId == cuota.Id);
        Assert.Equal(0m, fila.ImporteAplicadoPunitorio);
    }

    [Fact]
    public async Task ContratoNuevo_AlSuperarLaGracia_CuentaRetroactivoDesdeElVencimiento()
    {
        // Ejemplo numérico del plan (§8): cuota $10.000, vencimiento hace 6 días, gracia 5,
        // 10% cada 20 días → punitorio esperado $300,00. PUN-ML6: el cobro nunca lo calcula por sí
        // solo — se aplica explícitamente (autorización real, PUN-ML5) y sólo eso puede cobrarse.
        await SeedConfiguracionPunitorioContratoAsync();
        var (_, credito, cuota) = await SeedCreditoConCuota(24m, 10_000m, 8_000m, 2_000m, Hoy.AddDays(-6));

        var esperadoPorContrato = ContratoPunitorioNuevo.Calcular(
            saldoImpago: 10_000m, porcentaje: 10m, periodoDias: 20, diasGracia: 5, diasTranscurridos: 6);
        Assert.Equal(300.00m, esperadoPorContrato);

        var aplicado = await AplicarPunitorioAsync(cuota.Id);
        Assert.Equal(esperadoPorContrato, aplicado.Importe); // el calculador real (PUN-ML4) coincide con el contrato

        await PagarAsync(credito.Id, cuota.Id, 10_000m + esperadoPorContrato);

        var fila = await _context.PagosCuota.SingleAsync(p => p.CuotaId == cuota.Id);
        Assert.Equal(esperadoPorContrato, fila.ImporteAplicadoPunitorio); // efectivamente cobrado
    }

    [Fact]
    public async Task ContratoNuevo_LaBaseDebeSerElSaldoImpago_UnPagoParcialPrevioDebeReducirElPunitorio()
    {
        // Mismo escenario que Legacy_BaseDelCalculo_IgnoraPagosParcialesPrevios, pero expresado
        // como el contrato que debe cumplirse: la cuota que ya pagó $400 antes del vencimiento
        // debe generar una aplicación de punitorio MENOR que la que no pagó nada, porque el
        // calculador (PUN-ML4) usa el saldo impago real como base — nunca MontoTotal.
        await SeedConfiguracionPunitorioContratoAsync();
        var fechaVencimiento = Hoy.AddDays(10);

        var (_, creditoA, cuotaA) = await SeedCreditoConCuota(24m, 1_000m, 800m, 200m, fechaVencimiento);
        var (_, creditoB, cuotaB) = await SeedCreditoConCuota(24m, 1_000m, 800m, 200m, fechaVencimiento);

        await PagarAsync(creditoB.Id, cuotaB.Id, 400m);

        _reloj.HoyComercial = DateOnly.FromDateTime(fechaVencimiento).AddDays(30);

        var aplicadoA = await AplicarPunitorioAsync(cuotaA.Id);
        var aplicadoB = await AplicarPunitorioAsync(cuotaB.Id);

        Assert.True(aplicadoB.Importe < aplicadoA.Importe,
            $"El punitorio aplicado a la cuota con pago parcial previo (${aplicadoB.Importe}) debería ser " +
            $"menor al de la cuota sin pagos (${aplicadoA.Importe}), porque la base debe ser el saldo impago.");
    }

    [Fact]
    public async Task ContratoNuevo_ElPunitorioNoDebeDependerDeLaTasaInteresDelCredito()
    {
        // Dos créditos con el mismo saldo impago y los mismos días de atraso, pero TasaInteres
        // distinta (0% vs 45% de recargo total del plan), deben generar la MISMA aplicación de
        // punitorio, porque la configuración de punitorio es independiente de TasaInteres.
        await SeedConfiguracionPunitorioContratoAsync();

        var (_, _, cuotaRecargoCero) = await SeedCreditoConCuota(0m, 1_000m, 800m, 200m, Hoy.AddDays(-30));
        var (_, _, cuotaConRecargo) = await SeedCreditoConCuota(45m, 1_000m, 800m, 200m, Hoy.AddDays(-30));

        var aplicadoSinRecargo = await AplicarPunitorioAsync(cuotaRecargoCero.Id);
        var aplicadoConRecargo = await AplicarPunitorioAsync(cuotaConRecargo.Id);

        Assert.True(aplicadoSinRecargo.Importe > 0m); // no es una igualdad vacía: ambas SÍ generaron punitorio real
        Assert.Equal(aplicadoSinRecargo.Importe, aplicadoConRecargo.Importe);
    }

    [Fact]
    public async Task ContratoNuevo_CuotasDelMismoCredito_SeCalculanDeFormaIndependiente()
    {
        // Este punto del contrato YA se cumple con la arquitectura actual (la fórmula recibe
        // una única Cuota), así que queda en verde: pagar una cuota no debe alterar el
        // punitorio de otra cuota del mismo crédito que no fue tocada.
        var id = _nextId++;
        var cliente = new Cliente { Id = id, Nombre = "Cliente", Apellido = $"Test{id}", NumeroDocumento = $"9100{id:D5}", IsDeleted = false };
        _context.Clientes.Add(cliente);

        var credito = new Credito
        {
            Id = id, ClienteId = id, IsDeleted = false, Numero = $"CRED{id:D4}",
            Estado = EstadoCredito.Activo, TasaInteres = 24m,
            MontoSolicitado = 3_000m, MontoAprobado = 3_000m, SaldoPendiente = 3_000m,
            CantidadCuotas = 2, MontoCuota = 1_500m, TotalAPagar = 3_000m
        };
        _context.Creditos.Add(credito);

        var cuota1 = new Cuota
        {
            Id = 1000 + id, CreditoId = id, NumeroCuota = 1,
            FechaVencimiento = Hoy.AddDays(-10), MontoTotal = 1_000m, MontoCapital = 800m, MontoInteres = 200m,
            MontoPagado = 0m, MontoPunitorio = 0m, Estado = EstadoCuota.Pendiente, IsDeleted = false
        };
        var cuota2 = new Cuota
        {
            Id = 2000 + id, CreditoId = id, NumeroCuota = 2,
            FechaVencimiento = Hoy.AddDays(-20), MontoTotal = 2_000m, MontoCapital = 1_600m, MontoInteres = 400m,
            MontoPagado = 0m, MontoPunitorio = 0m, Estado = EstadoCuota.Pendiente, IsDeleted = false
        };
        _context.Cuotas.AddRange(cuota1, cuota2);
        await _context.SaveChangesAsync();

        await PagarAsync(id, cuota1.Id, 1_000m);

        var cuota2TrasPagarCuota1 = await _context.Cuotas.FindAsync(cuota2.Id);
        Assert.NotNull(cuota2TrasPagarCuota1);
        Assert.Equal(0m, cuota2TrasPagarCuota1!.MontoPunitorio); // no se tocó: sigue en su valor seed
    }

    [Fact]
    public async Task ContratoNuevo_ConsultarUnaCuotaNuncaDebePersistirNiRecalcularSuPunitorio()
    {
        // Este punto del contrato YA se cumple: GetCuotaByIdAsync es de solo lectura (AsNoTracking,
        // sin SaveChanges). Verde — sirve de red de seguridad para cuando exista PunitorioService.
        var (_, _, cuota) = await SeedCreditoConCuota(24m, 1_000m, 800m, 200m, Hoy.AddDays(-45));

        for (var i = 0; i < 3; i++)
        {
            var vm = await _service.GetCuotaByIdAsync(cuota.Id);
            Assert.NotNull(vm);
        }

        var tras = await _context.Cuotas.AsNoTracking().FirstAsync(c => c.Id == cuota.Id);
        Assert.Equal(0m, tras.MontoPunitorio);
        Assert.Equal(0m, tras.MontoPagado);
        Assert.Equal(EstadoCuota.Pendiente, tras.Estado);
    }
}
