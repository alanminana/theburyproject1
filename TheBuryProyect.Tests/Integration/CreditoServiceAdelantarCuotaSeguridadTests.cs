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

/// <summary>
/// Remediación de seguridad de <c>AdelantarCuotaAsync</c> (micro-lote 2 de AUDIT-RV-20260722).
///
/// Regla funcional detectada y conservada: adelantar significa cancelar la ÚLTIMA cuota
/// pendiente del plan para reducir el plazo. El servidor resuelve esa cuota; el importe que
/// envía el cliente solo se acepta si coincide con el saldo pendiente calculado por el servidor.
/// A diferencia del pago normal, el adelanto NO admite imputación parcial: la UI no ofrece un
/// importe editable (campo oculto), no hay validación JavaScript de importe y no existían tests
/// que ejercitaran un adelanto parcial.
///
/// Reutiliza los stubs de <see cref="CreditoServicePagarCuotaSeguridadTests"/> para que ambos
/// lotes compartan la misma instrumentación de caja y de ajuste por medio de pago.
/// </summary>
public class CreditoServiceAdelantarCuotaSeguridadTests : IDisposable
{
    private const decimal MontoCuota = 12_480.75m;

    private readonly SqliteConnection _connection;
    private readonly string _dataSource;
    private readonly AppDbContext _context;
    private readonly StubCajaServicePagoSeguro _caja;
    private readonly StubConfiguracionPagoAjuste _configuracionPago;
    private readonly CreditoService _service;

    public CreditoServiceAdelantarCuotaSeguridadTests()
    {
        _dataSource = $"{Guid.NewGuid():N}";
        _connection = new SqliteConnection($"DataSource={_dataSource};Mode=Memory;Cache=Shared");
        _connection.Open();

        _context = CrearContexto();
        _context.Database.EnsureCreated();
        SembrarCajaYApertura(_context);

        _caja = new StubCajaServicePagoSeguro(_context);
        _configuracionPago = new StubConfiguracionPagoAjuste();
        _service = CrearService(_context, _caja, _configuracionPago);
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

    private AppDbContext CrearContexto() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"DataSource={_dataSource};Mode=Memory;Cache=Shared")
            .Options);

    private static CreditoService CrearService(
        AppDbContext context,
        ICajaService caja,
        IConfiguracionPagoService? configuracionPago) =>
        new(context,
            new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>(), NullLoggerFactory.Instance).CreateMapper(),
            NullLogger<CreditoService>.Instance,
            new FinancialCalculationService(),
            caja,
            new StubCreditoDisponibleServiceAdelanto(),
            new StubCurrentUserServiceAdelanto(),
            configuracionPagoService: configuracionPago);

    private async Task<Credito> SeedCreditoAsync(string sufijo, bool creditoEliminado = false)
    {
        var cliente = new Cliente
        {
            Nombre = "FIX-RV-ADELANTO",
            Apellido = "Cliente",
            TipoDocumento = "DNI",
            NumeroDocumento = $"FIXADE{sufijo}",
            Email = "fix-rv-adelanto@test.local"
        };
        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync();

        var credito = new Credito
        {
            Numero = $"FIX-RV-ADELANTO-{sufijo}",
            ClienteId = cliente.Id,
            Estado = EstadoCredito.Activo,
            MontoSolicitado = 120_000m,
            MontoAprobado = 120_000m,
            SaldoPendiente = 120_000m,
            TasaInteres = 0m,
            CantidadCuotas = 3,
            FechaSolicitud = DateTime.UtcNow,
            IsDeleted = creditoEliminado
        };
        _context.Creditos.Add(credito);
        await _context.SaveChangesAsync();
        return credito;
    }

    private async Task<Cuota> SeedCuotaAsync(
        int creditoId,
        int numeroCuota,
        EstadoCuota estado = EstadoCuota.Pendiente,
        decimal montoTotal = MontoCuota,
        decimal montoPagado = 0m,
        decimal montoPunitorio = 0m,
        bool eliminada = false)
    {
        var cuota = new Cuota
        {
            CreditoId = creditoId,
            NumeroCuota = numeroCuota,
            MontoCapital = montoTotal,
            MontoInteres = 0m,
            MontoTotal = montoTotal,
            MontoPagado = montoPagado,
            MontoPunitorio = montoPunitorio,
            Estado = estado,
            // A futuro: el adelanto cancela una cuota que todavía no venció.
            FechaVencimiento = DateTime.UtcNow.AddDays(30 * numeroCuota),
            IsDeleted = eliminada
        };
        _context.Cuotas.Add(cuota);
        await _context.SaveChangesAsync();
        return cuota;
    }

    /// <summary>Crédito de 3 cuotas pendientes. La adelantable es la #3.</summary>
    private async Task<(Credito Credito, Cuota Primera, Cuota Ultima)> SeedPlanAsync(string sufijo)
    {
        var credito = await SeedCreditoAsync(sufijo);
        var primera = await SeedCuotaAsync(credito.Id, 1);
        await SeedCuotaAsync(credito.Id, 2);
        var ultima = await SeedCuotaAsync(credito.Id, 3);
        return (credito, primera, ultima);
    }

    private static PagarCuotaViewModel Adelanto(
        int creditoId,
        int cuotaId,
        decimal monto,
        string medio = "Efectivo") =>
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
        return await _context.Cuotas.AsNoTracking().IgnoreQueryFilters().FirstAsync(c => c.Id == cuotaId);
    }

    /// <summary>Confirma que un rechazo no dejó rastro: ni cuota tocada ni movimiento de caja.</summary>
    private async Task AssertSinEfectosAsync(Cuota cuotaOriginal)
    {
        Assert.Empty(_caja.Movimientos);

        var cuotaBd = await RecargarCuotaAsync(cuotaOriginal.Id);
        Assert.Equal(cuotaOriginal.MontoTotal, cuotaBd.MontoTotal);
        Assert.Equal(cuotaOriginal.MontoPagado, cuotaBd.MontoPagado);
        Assert.Equal(cuotaOriginal.MontoPunitorio, cuotaBd.MontoPunitorio);
        Assert.Equal(cuotaOriginal.RecargoMedioPago, cuotaBd.RecargoMedioPago);
        Assert.Equal(cuotaOriginal.Estado, cuotaBd.Estado);
        Assert.Null(cuotaBd.FechaPago);
    }

    // =========================================================================
    // Casos válidos
    // =========================================================================

    /// <summary>1 y 5 — adelanto completo: cancela la última cuota y no toca las anteriores.</summary>
    [Fact]
    public async Task Adelanto_Valido_CancelaLaUltimaCuotaYDejaIntactasLasAnteriores()
    {
        var (credito, primera, ultima) = await SeedPlanAsync("1");

        var resultado = await _service.AdelantarCuotaAsync(Adelanto(credito.Id, ultima.Id, MontoCuota));

        Assert.True(resultado);

        var ultimaBd = await RecargarCuotaAsync(ultima.Id);
        Assert.Equal(EstadoCuota.Pagada, ultimaBd.Estado);
        Assert.Equal(MontoCuota, ultimaBd.MontoPagado);
        Assert.Contains("[ADELANTO]", ultimaBd.Observaciones);

        var primeraBd = await RecargarCuotaAsync(primera.Id);
        Assert.Equal(EstadoCuota.Pendiente, primeraBd.Estado);
        Assert.Equal(0m, primeraBd.MontoPagado);
    }

    /// <summary>4 — el importe original de la cuota nunca se modifica.</summary>
    [Fact]
    public async Task Adelanto_Valido_NoModificaElImporteOriginalDeLaCuota()
    {
        var (credito, _, ultima) = await SeedPlanAsync("2");

        await _service.AdelantarCuotaAsync(Adelanto(credito.Id, ultima.Id, MontoCuota));

        var ultimaBd = await RecargarCuotaAsync(ultima.Id);
        Assert.Equal(MontoCuota, ultimaBd.MontoTotal);
        Assert.Equal(MontoCuota, ultimaBd.MontoCapital);
        Assert.Equal(0m, ultimaBd.MontoPunitorio);
    }

    /// <summary>3 y 8 — el movimiento de caja conserva el desglose y las referencias.</summary>
    [Fact]
    public async Task Adelanto_Valido_RegistraCajaConDesgloseYReferencias()
    {
        var (credito, _, ultima) = await SeedPlanAsync("3");

        await _service.AdelantarCuotaAsync(Adelanto(credito.Id, ultima.Id, MontoCuota));

        var movimiento = Assert.Single(_caja.Movimientos);
        Assert.Equal(ultima.Id, movimiento.CuotaId);
        Assert.Equal(credito.Numero, movimiento.CreditoNumero);
        Assert.Equal(3, movimiento.NumeroCuota);
        Assert.Equal(MontoCuota, movimiento.MontoBase);
        Assert.Equal(0m, movimiento.RecargoMedioPago);
        Assert.Equal("Efectivo", movimiento.MedioPago);
        Assert.Equal("testuser", movimiento.Usuario);
    }

    /// <summary>6 — el recargo del medio de pago lo calcula el servidor, separado del importe base.</summary>
    [Fact]
    public async Task Adelanto_ConRecargo_LoCalculaElServidorYLoSeparaDelImporteBase()
    {
        _configuracionPago.AjustePorcentaje = 3m; // Transferencia +3%
        var (credito, _, ultima) = await SeedPlanAsync("4");

        await _service.AdelantarCuotaAsync(Adelanto(credito.Id, ultima.Id, MontoCuota, "Transferencia"));

        var recargoEsperado = Math.Round(MontoCuota * 0.03m, 2, MidpointRounding.AwayFromZero);

        var ultimaBd = await RecargarCuotaAsync(ultima.Id);
        Assert.Equal(EstadoCuota.Pagada, ultimaBd.Estado);
        Assert.Equal(MontoCuota, ultimaBd.MontoTotal);   // importe original inmutable
        Assert.Equal(MontoCuota, ultimaBd.MontoPagado);  // el recargo no salda la cuota
        Assert.Equal(recargoEsperado, ultimaBd.RecargoMedioPago);

        var movimiento = Assert.Single(_caja.Movimientos);
        Assert.Equal(MontoCuota, movimiento.MontoBase);
        Assert.Equal(recargoEsperado, movimiento.RecargoMedioPago);
        Assert.Equal(TipoPago.Transferencia, movimiento.TipoPago);
    }

    /// <summary>7 — el descuento del medio de pago también lo calcula el servidor.</summary>
    [Fact]
    public async Task Adelanto_ConDescuento_LoCalculaElServidor()
    {
        _configuracionPago.AjustePorcentaje = -5m; // Efectivo -5%
        var (credito, _, ultima) = await SeedPlanAsync("5");

        await _service.AdelantarCuotaAsync(Adelanto(credito.Id, ultima.Id, MontoCuota));

        var descuentoEsperado = -Math.Round(MontoCuota * 0.05m, 2, MidpointRounding.AwayFromZero);

        var ultimaBd = await RecargarCuotaAsync(ultima.Id);
        Assert.Equal(MontoCuota, ultimaBd.MontoPagado);
        Assert.Equal(descuentoEsperado, ultimaBd.RecargoMedioPago);

        var movimiento = Assert.Single(_caja.Movimientos);
        Assert.Equal(MontoCuota, movimiento.MontoBase);
        Assert.Equal(descuentoEsperado, movimiento.RecargoMedioPago);
    }

    /// <summary>
    /// El adelanto sobre una cuota con pago parcial previo cobra el SALDO, no el total de la
    /// cuota: es el caso que antes producía sobrepago porque el formulario proponía MontoTotal.
    /// </summary>
    [Fact]
    public async Task Adelanto_SobreCuotaConPagoParcialPrevio_CobraSoloElSaldoPendiente()
    {
        var credito = await SeedCreditoAsync("6");
        await SeedCuotaAsync(credito.Id, 1);
        var ultima = await SeedCuotaAsync(credito.Id, 2, EstadoCuota.Parcial, montoPagado: 4_480.75m);

        var saldo = MontoCuota - 4_480.75m;

        var resultado = await _service.AdelantarCuotaAsync(Adelanto(credito.Id, ultima.Id, saldo));

        Assert.True(resultado);

        var ultimaBd = await RecargarCuotaAsync(ultima.Id);
        Assert.Equal(EstadoCuota.Pagada, ultimaBd.Estado);
        Assert.Equal(MontoCuota, ultimaBd.MontoPagado);   // saldada exactamente, sin sobrepago
        Assert.Equal(MontoCuota, ultimaBd.MontoTotal);

        var movimiento = Assert.Single(_caja.Movimientos);
        Assert.Equal(saldo, movimiento.MontoBase);
    }

    /// <summary>
    /// La cuota adelantable la resuelve el servidor: si el cliente no informa CuotaId igual
    /// se cancela la última pendiente (contrato que ya usaban los tests preexistentes).
    /// </summary>
    [Fact]
    public async Task Adelanto_SinCuotaIdInformado_ResuelveLaUltimaPendiente()
    {
        var (credito, _, ultima) = await SeedPlanAsync("7");

        var resultado = await _service.AdelantarCuotaAsync(Adelanto(credito.Id, cuotaId: 0, MontoCuota));

        Assert.True(resultado);

        var ultimaBd = await RecargarCuotaAsync(ultima.Id);
        Assert.Equal(EstadoCuota.Pagada, ultimaBd.Estado);
    }

    // =========================================================================
    // Manipulación del importe
    // =========================================================================

    /// <summary>9 — sobrepago: el importe no puede superar el saldo calculado por el servidor.</summary>
    [Fact]
    public async Task Adelanto_MontoMayorAlSaldo_RechazaYNoDejaRastro()
    {
        var (credito, _, ultima) = await SeedPlanAsync("8");

        var ex = await Assert.ThrowsAsync<PagoCuotaRechazadoException>(() =>
            _service.AdelantarCuotaAsync(Adelanto(credito.Id, ultima.Id, MontoCuota + 0.5m)));

        Assert.Equal(MotivoRechazoPagoCuota.SolicitudInvalida, ex.Motivo);
        await AssertSinEfectosAsync(ultima);
    }

    /// <summary>
    /// 2 — el adelanto no admite imputación parcial: cancela la cuota completa.
    /// La UI no expone un importe editable, por eso un importe menor solo puede ser manipulación.
    /// </summary>
    [Fact]
    public async Task Adelanto_MontoMenorAlSaldo_RechazaPorqueCancelaLaCuotaCompleta()
    {
        var (credito, _, ultima) = await SeedPlanAsync("9");

        var ex = await Assert.ThrowsAsync<PagoCuotaRechazadoException>(() =>
            _service.AdelantarCuotaAsync(Adelanto(credito.Id, ultima.Id, 100m)));

        Assert.Equal(MotivoRechazoPagoCuota.SolicitudInvalida, ex.Motivo);
        Assert.Contains("saldo pendiente", ex.Message);
        await AssertSinEfectosAsync(ultima);
    }

    /// <summary>10 y 11 — importes no positivos.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1500)]
    public async Task Adelanto_MontoNoPositivo_RechazaYNoDejaRastro(decimal monto)
    {
        var (credito, _, ultima) = await SeedPlanAsync($"10-{monto}");

        var ex = await Assert.ThrowsAsync<PagoCuotaRechazadoException>(() =>
            _service.AdelantarCuotaAsync(Adelanto(credito.Id, ultima.Id, monto)));

        Assert.Equal(MotivoRechazoPagoCuota.SolicitudInvalida, ex.Motivo);
        await AssertSinEfectosAsync(ultima);
    }

    /// <summary>
    /// 22 a 25 — todos los valores derivados que envía el navegador se ignoran: importe de la
    /// cuota, punitorio, total y número de cuota. El servidor recalcula y sobrescribe.
    /// </summary>
    [Fact]
    public async Task Adelanto_ConValoresDerivadosManipulados_LosIgnoraYRecalcula()
    {
        _configuracionPago.AjustePorcentaje = 3m;
        var (credito, _, ultima) = await SeedPlanAsync("11");

        var pago = Adelanto(credito.Id, ultima.Id, MontoCuota, "Transferencia");
        pago.MontoCuota = 1m;
        pago.MontoPunitorio = 99_999m;
        pago.TotalAPagar = 1m;
        pago.NumeroCuota = 1;

        await _service.AdelantarCuotaAsync(pago);

        var recargoEsperado = Math.Round(MontoCuota * 0.03m, 2, MidpointRounding.AwayFromZero);

        var ultimaBd = await RecargarCuotaAsync(ultima.Id);
        Assert.Equal(MontoCuota, ultimaBd.MontoTotal);
        Assert.Equal(MontoCuota, ultimaBd.MontoPagado);
        Assert.Equal(0m, ultimaBd.MontoPunitorio);          // el 99.999 enviado se descarta
        Assert.Equal(recargoEsperado, ultimaBd.RecargoMedioPago);

        var movimiento = Assert.Single(_caja.Movimientos);
        Assert.Equal(MontoCuota, movimiento.MontoBase);
        Assert.Equal(recargoEsperado, movimiento.RecargoMedioPago);
        Assert.Equal(3, movimiento.NumeroCuota);            // el NumeroCuota=1 enviado se descarta
    }

    /// <summary>
    /// El punitorio ya APLICADO (PUN-ML6: fuente única, <see cref="PunitorioAplicado"/>, nunca el
    /// campo legacy <c>Cuota.MontoPunitorio</c>) se conserva y entra en el saldo: no se condona.
    /// </summary>
    [Fact]
    public async Task Adelanto_ConPunitorioAplicado_LoConservaYLoIncluyeEnElSaldo()
    {
        var credito = await SeedCreditoAsync("12");
        await SeedCuotaAsync(credito.Id, 1);
        var ultima = await SeedCuotaAsync(credito.Id, 2, EstadoCuota.Pendiente);

        _context.PunitoriosAplicados.Add(new PunitorioAplicado
        {
            CuotaId = ultima.Id,
            FechaCalculo = DateOnly.FromDateTime(DateTime.UtcNow),
            SaldoBase = MontoCuota,
            DiasComputados = 10,
            Importe = 300m,
            Estado = EstadoPunitorioAplicado.Aplicado,
            FechaAplicacion = DateTime.UtcNow,
            MotivoAplicacion = "Seed de test",
            UsuarioAplicacion = "TestUser"
        });
        await _context.SaveChangesAsync();

        var saldo = MontoCuota + 300m;

        await _service.AdelantarCuotaAsync(Adelanto(credito.Id, ultima.Id, saldo));

        var ultimaBd = await RecargarCuotaAsync(ultima.Id);
        Assert.Equal(0m, ultimaBd.MontoPunitorio); // legacy: sin escritor productivo
        Assert.Equal(MontoCuota, ultimaBd.MontoPagado); // sólo el componente de cuota mueve MontoPagado
        Assert.Equal(EstadoCuota.Pagada, ultimaBd.Estado);

        var movimiento = Assert.Single(_caja.Movimientos);
        Assert.Equal(saldo, movimiento.MontoBase); // caja sí ve el total cobrado (cuota + punitorio)

        var aplicadoBd = await _context.PunitoriosAplicados.AsNoTracking().SingleAsync(p => p.CuotaId == ultima.Id);
        Assert.Equal(EstadoPunitorioAplicado.Pagado, aplicadoBd.Estado);
    }

    // =========================================================================
    // Manipulación de la relación crédito ↔ cuota
    // =========================================================================

    /// <summary>12 — crédito inexistente.</summary>
    [Fact]
    public async Task Adelanto_CreditoInexistente_RetornaFalseYNoRegistraCaja()
    {
        var resultado = await _service.AdelantarCuotaAsync(Adelanto(999_999, 999_999, MontoCuota));

        Assert.False(resultado);
        Assert.Empty(_caja.Movimientos);
    }

    /// <summary>13 — cuota inexistente dentro de un crédito real.</summary>
    [Fact]
    public async Task Adelanto_CuotaInexistente_RetornaFalseYNoDejaRastro()
    {
        var (credito, _, ultima) = await SeedPlanAsync("13");

        var resultado = await _service.AdelantarCuotaAsync(Adelanto(credito.Id, 999_999, MontoCuota));

        Assert.False(resultado);
        await AssertSinEfectosAsync(ultima);
    }

    /// <summary>14 — cuota de otro crédito: no se cobra ni se revela su existencia.</summary>
    [Fact]
    public async Task Adelanto_CuotaDeOtroCredito_RetornaFalseYNoTocaNingunaCuota()
    {
        var (creditoA, _, ultimaA) = await SeedPlanAsync("14A");
        var (_, _, ultimaB) = await SeedPlanAsync("14B");

        var resultado = await _service.AdelantarCuotaAsync(Adelanto(creditoA.Id, ultimaB.Id, MontoCuota));

        Assert.False(resultado);
        Assert.Empty(_caja.Movimientos);

        var ultimaBBd = await RecargarCuotaAsync(ultimaB.Id);
        Assert.Equal(EstadoCuota.Pendiente, ultimaBBd.Estado);
        Assert.Equal(0m, ultimaBBd.MontoPagado);

        await AssertSinEfectosAsync(ultimaA);
    }

    /// <summary>15 — cuota eliminada indicada explícitamente.</summary>
    [Fact]
    public async Task Adelanto_CuotaEliminada_RetornaFalseYNoDejaRastro()
    {
        var credito = await SeedCreditoAsync("15");
        var eliminada = await SeedCuotaAsync(credito.Id, 1, eliminada: true);
        var ultima = await SeedCuotaAsync(credito.Id, 2);

        var resultado = await _service.AdelantarCuotaAsync(Adelanto(credito.Id, eliminada.Id, MontoCuota));

        Assert.False(resultado);
        await AssertSinEfectosAsync(ultima);
    }

    /// <summary>16 — crédito eliminado: sus cuotas no son adelantables.</summary>
    [Fact]
    public async Task Adelanto_CreditoEliminado_RetornaFalseYNoDejaRastro()
    {
        var credito = await SeedCreditoAsync("16", creditoEliminado: true);
        var ultima = await SeedCuotaAsync(credito.Id, 1);

        var resultado = await _service.AdelantarCuotaAsync(Adelanto(credito.Id, ultima.Id, MontoCuota));

        Assert.False(resultado);
        await AssertSinEfectosAsync(ultima);
    }

    // =========================================================================
    // Regla funcional del adelanto y estados incompatibles
    // =========================================================================

    /// <summary>
    /// 28 — no puede adelantarse una cuota que no es la última pendiente. Es el control que
    /// impide cancelar una cuota distinta de la que vio el operador en el formulario.
    /// </summary>
    [Fact]
    public async Task Adelanto_CuotaQueNoEsLaUltimaPendiente_RechazaPorConflicto()
    {
        var (credito, primera, ultima) = await SeedPlanAsync("17");

        var ex = await Assert.ThrowsAsync<PagoCuotaRechazadoException>(() =>
            _service.AdelantarCuotaAsync(Adelanto(credito.Id, primera.Id, MontoCuota)));

        Assert.Equal(MotivoRechazoPagoCuota.Conflicto, ex.Motivo);
        Assert.Contains("última cuota pendiente", ex.Message);

        await AssertSinEfectosAsync(primera);
        var ultimaBd = await RecargarCuotaAsync(ultima.Id);
        Assert.Equal(EstadoCuota.Pendiente, ultimaBd.Estado);
    }

    /// <summary>17 — cuota ya pagada indicada explícitamente.</summary>
    [Fact]
    public async Task Adelanto_CuotaYaPagada_RechazaPorConflicto()
    {
        var credito = await SeedCreditoAsync("18");
        var pagada = await SeedCuotaAsync(credito.Id, 1, EstadoCuota.Pagada, montoPagado: MontoCuota);
        await SeedCuotaAsync(credito.Id, 2);

        var ex = await Assert.ThrowsAsync<PagoCuotaRechazadoException>(() =>
            _service.AdelantarCuotaAsync(Adelanto(credito.Id, pagada.Id, MontoCuota)));

        Assert.Equal(MotivoRechazoPagoCuota.Conflicto, ex.Motivo);
        Assert.Empty(_caja.Movimientos);
    }

    /// <summary>18 — cuota cancelada indicada explícitamente.</summary>
    [Fact]
    public async Task Adelanto_CuotaCancelada_RechazaPorConflicto()
    {
        var credito = await SeedCreditoAsync("19");
        var cancelada = await SeedCuotaAsync(credito.Id, 1, EstadoCuota.Cancelada);
        await SeedCuotaAsync(credito.Id, 2);

        var ex = await Assert.ThrowsAsync<PagoCuotaRechazadoException>(() =>
            _service.AdelantarCuotaAsync(Adelanto(credito.Id, cancelada.Id, MontoCuota)));

        Assert.Equal(MotivoRechazoPagoCuota.Conflicto, ex.Motivo);
        await AssertSinEfectosAsync(cancelada);
    }

    /// <summary>19 — estado incompatible: una cuota vencida se paga, no se adelanta.</summary>
    [Fact]
    public async Task Adelanto_CuotaVencida_RechazaPorConflicto()
    {
        var credito = await SeedCreditoAsync("20");
        var vencida = await SeedCuotaAsync(credito.Id, 1, EstadoCuota.Vencida);
        await SeedCuotaAsync(credito.Id, 2);

        var ex = await Assert.ThrowsAsync<PagoCuotaRechazadoException>(() =>
            _service.AdelantarCuotaAsync(Adelanto(credito.Id, vencida.Id, MontoCuota)));

        Assert.Equal(MotivoRechazoPagoCuota.Conflicto, ex.Motivo);
        await AssertSinEfectosAsync(vencida);
    }

    /// <summary>Sin cuotas adelantables (todas pagadas) no hay adelanto posible.</summary>
    [Fact]
    public async Task Adelanto_SinCuotasAdelantables_RetornaFalseYNoRegistraCaja()
    {
        var credito = await SeedCreditoAsync("21");
        await SeedCuotaAsync(credito.Id, 1, EstadoCuota.Pagada, montoPagado: MontoCuota);

        var resultado = await _service.AdelantarCuotaAsync(Adelanto(credito.Id, cuotaId: 0, MontoCuota));

        Assert.False(resultado);
        Assert.Empty(_caja.Movimientos);
    }

    // =========================================================================
    // Medio de pago
    // =========================================================================

    /// <summary>20 — medio de pago inexistente.</summary>
    [Fact]
    public async Task Adelanto_MedioDePagoInexistente_RechazaYNoDejaRastro()
    {
        var (credito, _, ultima) = await SeedPlanAsync("22");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.AdelantarCuotaAsync(Adelanto(credito.Id, ultima.Id, MontoCuota, "Bitcoin")));

        await AssertSinEfectosAsync(ultima);
    }

    /// <summary>21 — medio de pago existente pero deshabilitado en la configuración.</summary>
    [Fact]
    public async Task Adelanto_MedioDePagoDeshabilitado_RechazaYNoDejaRastro()
    {
        var (credito, _, ultima) = await SeedPlanAsync("23");

        _context.ConfiguracionesPago.Add(new ConfiguracionPago
        {
            TipoPago = TipoPago.Transferencia,
            Activo = false
        });
        await _context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<PagoCuotaRechazadoException>(() =>
            _service.AdelantarCuotaAsync(Adelanto(credito.Id, ultima.Id, MontoCuota, "Transferencia")));

        Assert.Equal(MotivoRechazoPagoCuota.SolicitudInvalida, ex.Motivo);
        await AssertSinEfectosAsync(ultima);
    }

    // =========================================================================
    // Doble envío y concurrencia
    // =========================================================================

    /// <summary>
    /// 26 — el reenvío del mismo formulario no vuelve a cobrar: la cuota indicada dejó de ser
    /// la adelantable, así que se rechaza en lugar de cancelar además la cuota anterior.
    /// </summary>
    [Fact]
    public async Task Adelanto_SegundoEnvioIdentico_RechazaYNoDuplicaElCobro()
    {
        var (credito, _, ultima) = await SeedPlanAsync("24");
        var pago = Adelanto(credito.Id, ultima.Id, MontoCuota);

        Assert.True(await _service.AdelantarCuotaAsync(pago));

        var ex = await Assert.ThrowsAsync<PagoCuotaRechazadoException>(() =>
            _service.AdelantarCuotaAsync(Adelanto(credito.Id, ultima.Id, MontoCuota)));

        Assert.Equal(MotivoRechazoPagoCuota.Conflicto, ex.Motivo);
        Assert.Single(_caja.Movimientos);

        var ultimaBd = await RecargarCuotaAsync(ultima.Id);
        Assert.Equal(MontoCuota, ultimaBd.MontoPagado);
    }

    /// <summary>27 — dos adelantos simultáneos: solo uno cobra y solo se registra un movimiento.</summary>
    [Fact]
    public async Task Adelanto_DosIntentosConcurrentes_SoloUnoCobra()
    {
        var (credito, _, ultima) = await SeedPlanAsync("25");

        await using var contextoA = CrearContexto();
        await using var contextoB = CrearContexto();

        var cajaA = new StubCajaServicePagoSeguro(contextoA);
        var cajaB = new StubCajaServicePagoSeguro(contextoB);

        var servicioA = CrearService(contextoA, cajaA, new StubConfiguracionPagoAjuste());
        var servicioB = CrearService(contextoB, cajaB, new StubConfiguracionPagoAjuste());

        var intentoA = servicioA.AdelantarCuotaAsync(Adelanto(credito.Id, ultima.Id, MontoCuota));
        var intentoB = servicioB.AdelantarCuotaAsync(Adelanto(credito.Id, ultima.Id, MontoCuota));

        var resultados = await Task.WhenAll(
            EjecutarProtegidoAsync(intentoA),
            EjecutarProtegidoAsync(intentoB));

        Assert.Equal(1, resultados.Count(r => r));
        Assert.Equal(1, cajaA.Movimientos.Count + cajaB.Movimientos.Count);

        var ultimaBd = await RecargarCuotaAsync(ultima.Id);
        Assert.Equal(EstadoCuota.Pagada, ultimaBd.Estado);
        Assert.Equal(MontoCuota, ultimaBd.MontoPagado);
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
}

file sealed class StubCreditoDisponibleServiceAdelanto : ICreditoDisponibleService
{
    public Task<decimal> ObtenerLimitePorPuntajeAsync(int puntaje, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<decimal> CalcularSaldoVigenteAsync(int clienteId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<CreditoDisponibleResultado> CalcularDisponibleAsync(int clienteId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<(bool Ok, List<string> Errores)> GuardarLimitesPorPuntajeAsync(IReadOnlyList<(int Puntaje, decimal LimiteMonto, bool Activo)> items, string usuario) => throw new NotImplementedException();
    public Task<List<PuntajeCreditoLimite>> GetAllLimitesPorPuntajeAsync() => throw new NotImplementedException();
}

file sealed class StubCurrentUserServiceAdelanto : ICurrentUserService
{
    public string GetUsername() => "testuser";
    public string GetUserId() => "system";
    public bool IsAuthenticated() => true;
    public string? GetEmail() => "test@test.com";
    public bool IsInRole(string role) => false;
    public bool HasPermission(string modulo, string accion) => false;
    public string? GetIpAddress() => "127.0.0.1";
}
