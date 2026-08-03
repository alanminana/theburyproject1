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
using TheBuryProject.Services.Exceptions;
using TheBuryProject.Services.Models;
using TheBuryProject.Tests.Helpers;
using Xunit;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Micro-lote 6 — F1 (consistencia temporal) y F2 (decisión de cobro de la 1ª cuota).
/// Toda la lógica de vencimiento se mide contra la fecha comercial de Argentina provista por
/// un <see cref="IRelojComercial"/> inyectado y FIJO, de modo que los tests son deterministas
/// e independientes de la hora real del proceso.
/// </summary>
public sealed class CreditoPrimeraCuotaTiempoTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly StubCajaServicePagoSeguro _caja;
    private readonly StubConfiguracionPagoAjuste _configuracionPago;
    private readonly RelojComercialFake _reloj;
    private readonly CreditoService _service;

    // Fecha comercial fija de referencia.
    private static readonly DateOnly HOY = new(2026, 6, 15);

    public CreditoPrimeraCuotaTiempoTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection).Options);
        _context.Database.EnsureCreated();

        // PUN-ML2: MovimientoCaja.AperturaCajaId es FK real que StubCajaServicePagoSeguro
        // necesita resuelta para persistir el movimiento (y con él, el ledger PagoCuota).
        _context.Cajas.Add(new Caja { Id = 1, Codigo = "C1", Nombre = "Caja test", IsDeleted = false });
        _context.AperturasCaja.Add(new AperturaCaja
        {
            Id = 1, CajaId = 1, MontoInicial = 0m, UsuarioApertura = "TestUser", Cerrada = false, IsDeleted = false
        });
        _context.SaveChanges();

        var mapper = new MapperConfiguration(
            cfg => cfg.AddProfile<MappingProfile>(), NullLoggerFactory.Instance).CreateMapper();

        _caja = new StubCajaServicePagoSeguro(_context);
        _configuracionPago = new StubConfiguracionPagoAjuste();
        _reloj = new RelojComercialFake(HOY);

        _service = new CreditoService(
            _context,
            mapper,
            NullLogger<CreditoService>.Instance,
            new StubFinancialServiceCobro1ra(),
            _caja,
            new StubCreditoDisponibleServiceCobro1ra(),
            new StubCurrentUserServiceCobro1ra(),
            configuracionPagoService: _configuracionPago,
            reloj: _reloj);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // =====================================================================
    // Grupo A — Reloj comercial y frontera UTC/Argentina
    // =====================================================================

    [Fact] // 4 + 9: instante UTC del día siguiente, pero todavía "hoy" en Argentina
    public void Reloj_InstanteUtcCruzoDeDia_FechaComercialSigueSiendoHoyEnArgentina()
    {
        // 2026-06-16 01:30 UTC == 2026-06-15 22:30 en Argentina (UTC−3).
        var reloj = RelojComercialFijo.EnUtc(new DateTimeOffset(2026, 6, 16, 1, 30, 0, TimeSpan.Zero));

        Assert.Equal(new DateOnly(2026, 6, 15), reloj.HoyComercial);
        Assert.Equal(16, reloj.AhoraUtc.Day);   // UTC ya es día 16
        Assert.Equal(15, reloj.AhoraComercial.Day); // pero comercialmente es 15
    }

    [Fact] // 5: cambio de día exacto en Argentina
    public void Reloj_MedianocheArgentina_AvanzaLaFechaComercial()
    {
        // 03:00 UTC == 00:00 Argentina → ya es el nuevo día comercial.
        var reloj = RelojComercialFijo.EnUtc(new DateTimeOffset(2026, 6, 16, 3, 0, 0, TimeSpan.Zero));
        Assert.Equal(new DateOnly(2026, 6, 16), reloj.HoyComercial);
    }

    [Fact] // 4: la 1ª cuota que vence hoy en Argentina se cobra aunque UTC ya sea mañana
    public async Task CobrarPrimeraCuota_FronteraUtcArgentina_CobraPorqueEnArgentinaEsHoy()
    {
        // Reloj real anclado a 2026-06-16 01:30 UTC → hoy comercial = 2026-06-15.
        var relojFrontera = RelojComercialFijo.EnUtc(new DateTimeOffset(2026, 6, 16, 1, 30, 0, TimeSpan.Zero));
        var service = CrearServiceCon(relojFrontera);

        var credito = await SeedCreditoConCuota(new DateOnly(2026, 6, 15).ToDateTime(TimeOnly.MinValue));

        var resultado = await service.CobrarPrimeraCuotaAlGenerarAsync(credito.Id, "Efectivo");

        Assert.Equal(EstadoCobroPrimeraCuota.Cobrada, resultado.Estado);
        Assert.Single(_caja.Movimientos);
    }

    // =====================================================================
    // Grupo B — Gate "vence hoy / mañana / ayer"
    // =====================================================================

    [Fact] // 1 + 6: vence hoy → cobra, sin punitorio
    public async Task CobrarPrimeraCuota_VenceHoy_CobraSinPunitorio()
    {
        var credito = await SeedCreditoConCuota(HoyDt);

        var resultado = await _service.CobrarPrimeraCuotaAlGenerarAsync(credito.Id, "Efectivo");

        Assert.Equal(EstadoCobroPrimeraCuota.Cobrada, resultado.Estado);
        Assert.Equal(MontoCuota, resultado.MontoBase);
        var cuota = await RecargarPrimeraCuota(credito.Id);
        Assert.Equal(EstadoCuota.Pagada, cuota.Estado);
        Assert.Equal(0m, cuota.MontoPunitorio);
    }

    [Fact] // 2: vence mañana → no aplica
    public async Task CobrarPrimeraCuota_VenceManana_NoAplica()
    {
        var credito = await SeedCreditoConCuota(HoyDt.AddDays(1));

        var resultado = await _service.CobrarPrimeraCuotaAlGenerarAsync(credito.Id, "Efectivo");

        Assert.Equal(EstadoCobroPrimeraCuota.NoAplica, resultado.Estado);
        Assert.Empty(_caja.Movimientos);
    }

    [Fact] // 3: venció ayer → no aplica al cobro automático de la 1ª cuota (no "vence hoy")
    public async Task CobrarPrimeraCuota_VencioAyer_NoAplica()
    {
        var credito = await SeedCreditoConCuota(HoyDt.AddDays(-1));

        var resultado = await _service.CobrarPrimeraCuotaAlGenerarAsync(credito.Id, "Efectivo");

        Assert.Equal(EstadoCobroPrimeraCuota.NoAplica, resultado.Estado);
        Assert.Empty(_caja.Movimientos);
    }

    // =====================================================================
    // Grupo C — Punitorio por fecha comercial
    // =====================================================================

    [Fact] // 7: venció ayer → sin aplicación autorizada, no devenga punitorio alguno (PUN-ML6)
    public async Task PagarCuota_VencioAyer_SinAplicacion_NoDevengaPunitorio()
    {
        var credito = await SeedCreditoConCuota(HoyDt.AddDays(-30), tasaInteres: 24m);
        var cuota = await RecargarPrimeraCuota(credito.Id);

        await _service.PagarCuotaAsync(new TheBuryProject.ViewModels.PagarCuotaViewModel
        {
            CreditoId = credito.Id,
            CuotaId = cuota.Id,
            MontoPagado = MontoCuota,
            FechaPago = _reloj.AhoraUtc,
            MedioPago = "Efectivo"
        });

        var actualizada = await RecargarPrimeraCuota(credito.Id);
        Assert.Equal(0m, actualizada.MontoPunitorio);

        var fila = await _context.PagosCuota.SingleAsync(p => p.CuotaId == cuota.Id);
        Assert.Equal(0m, fila.ImporteAplicadoPunitorio);
    }

    [Fact] // 7b: venció ayer, con una aplicación autorizada previa → el cobro la prioriza (PUN-ML6)
    public async Task PagarCuota_VencioAyer_ConAplicacion_CobraElImporteAplicado()
    {
        var credito = await SeedCreditoConCuota(HoyDt.AddDays(-30), tasaInteres: 24m);
        var cuota = await RecargarPrimeraCuota(credito.Id);

        _context.PunitoriosAplicados.Add(new PunitorioAplicado
        {
            CuotaId = cuota.Id,
            FechaCalculo = _reloj.HoyComercial,
            SaldoBase = MontoCuota,
            DiasComputados = 30,
            Importe = 20.00m,
            Estado = EstadoPunitorioAplicado.Aplicado,
            FechaAplicacion = _reloj.AhoraUtc,
            MotivoAplicacion = "Seed de test",
            UsuarioAplicacion = "TestUser"
        });
        await _context.SaveChangesAsync();

        await _service.PagarCuotaAsync(new TheBuryProject.ViewModels.PagarCuotaViewModel
        {
            CreditoId = credito.Id,
            CuotaId = cuota.Id,
            MontoPagado = MontoCuota + 20.00m,
            FechaPago = _reloj.AhoraUtc,
            MedioPago = "Efectivo"
        });

        var actualizada = await RecargarPrimeraCuota(credito.Id);
        Assert.Equal(EstadoCuota.Pagada, actualizada.Estado);
        Assert.Equal(MontoCuota, actualizada.MontoPagado);

        var fila = await _context.PagosCuota.SingleAsync(p => p.CuotaId == cuota.Id);
        Assert.Equal(20.00m, fila.ImporteAplicadoPunitorio);
    }

    [Fact] // 8: futura → sin punitorio
    public async Task PagarCuota_Futura_SinPunitorio()
    {
        var credito = await SeedCreditoConCuota(HoyDt.AddDays(15));
        var cuota = await RecargarPrimeraCuota(credito.Id);

        await _service.PagarCuotaAsync(new TheBuryProject.ViewModels.PagarCuotaViewModel
        {
            CreditoId = credito.Id,
            CuotaId = cuota.Id,
            MontoPagado = MontoCuota,
            FechaPago = _reloj.AhoraUtc,
            MedioPago = "Efectivo"
        });

        var actualizada = await RecargarPrimeraCuota(credito.Id);
        Assert.Equal(0m, actualizada.MontoPunitorio);
    }

    // =====================================================================
    // Grupo D — Persistencia de la decisión (ConfigurarCreditoAsync)
    // =====================================================================

    [Fact] // 13 + 14: cobrar inmediato persiste la decisión y el medio
    public async Task ConfigurarCredito_CobrarPrimeraCuota_PersisteDecisionYMedio()
    {
        var credito = await SeedCreditoSinCuotas();

        await _service.ConfigurarCreditoAsync(ComandoConfig(credito.Id, cobrar: true, medio: "Transferencia"));

        var recargado = await _context.Creditos.FindAsync(credito.Id);
        Assert.True(recargado!.CobrarPrimeraCuotaSolicitada);
        Assert.Equal("Transferencia", recargado.MedioPagoPrimeraCuota);
    }

    [Fact] // 15 + 31: sin cobrar → decisión en falso y medio nulo aunque venga un medio
    public async Task ConfigurarCredito_SinCobrar_NoPersisteMedio()
    {
        var credito = await SeedCreditoSinCuotas();

        await _service.ConfigurarCreditoAsync(ComandoConfig(credito.Id, cobrar: false, medio: "Transferencia"));

        var recargado = await _context.Creditos.FindAsync(credito.Id);
        Assert.False(recargado!.CobrarPrimeraCuotaSolicitada);
        Assert.Null(recargado.MedioPagoPrimeraCuota);
    }

    [Fact] // F2 server-authoritative: el cobro toma el medio persistido si no se pasa explícito
    public async Task CobrarPrimeraCuota_SinMedioExplicito_UsaElPersistido()
    {
        var credito = await SeedCreditoConCuota(HoyDt);
        credito.MedioPagoPrimeraCuota = "Transferencia";
        credito.CobrarPrimeraCuotaSolicitada = true;
        await _context.SaveChangesAsync();

        var resultado = await _service.CobrarPrimeraCuotaAlGenerarAsync(credito.Id, medioPago: null);

        Assert.Equal(EstadoCobroPrimeraCuota.Cobrada, resultado.Estado);
        Assert.Equal("Transferencia", resultado.MedioPago);
    }

    [Fact] // sin medio explícito ni persistido → no aplica (no inventa un medio)
    public async Task CobrarPrimeraCuota_SinMedioAlguno_NoAplica()
    {
        var credito = await SeedCreditoConCuota(HoyDt);

        var resultado = await _service.CobrarPrimeraCuotaAlGenerarAsync(credito.Id, medioPago: null);

        Assert.Equal(EstadoCobroPrimeraCuota.NoAplica, resultado.Estado);
        Assert.Empty(_caja.Movimientos);
    }

    // =====================================================================
    // Grupo E — Confirmación: cobro server-authoritative
    // =====================================================================

    [Fact] // 22 + 23 + 26 + 29: importe intacto, recargo separado, único movimiento, medio correcto
    public async Task CobrarPrimeraCuota_ConRecargo_ImporteIntactoRecargoSeparadoUnicoMovimiento()
    {
        _configuracionPago.AjustePorcentaje = 3m; // recargo del medio
        var credito = await SeedCreditoConCuota(HoyDt);

        var resultado = await _service.CobrarPrimeraCuotaAlGenerarAsync(credito.Id, "Transferencia");

        Assert.Equal(EstadoCobroPrimeraCuota.Cobrada, resultado.Estado);
        Assert.Equal(MontoCuota, resultado.MontoBase);
        Assert.Equal(Math.Round(MontoCuota * 0.03m, 2, MidpointRounding.AwayFromZero), resultado.RecargoMedioPago);

        var cuota = await RecargarPrimeraCuota(credito.Id);
        Assert.Equal(MontoCuota, cuota.MontoTotal);   // importe original inmutable
        Assert.Equal(MontoCuota, cuota.MontoPagado);  // el recargo no salda la cuota

        var movimiento = Assert.Single(_caja.Movimientos);
        Assert.Equal(MontoCuota, movimiento.MontoBase);
        Assert.Equal("Transferencia", movimiento.MedioPago);
    }

    [Fact] // 21: la 1ª cuota queda pagada
    public async Task CobrarPrimeraCuota_DejaLaCuotaPagada()
    {
        var credito = await SeedCreditoConCuota(HoyDt);

        await _service.CobrarPrimeraCuotaAlGenerarAsync(credito.Id, "Efectivo");

        var cuota = await RecargarPrimeraCuota(credito.Id);
        Assert.Equal(EstadoCuota.Pagada, cuota.Estado);
    }

    // =====================================================================
    // Grupo F — Casos sin cobro
    // =====================================================================

    [Fact] // 32 + 33: cuota futura → queda pendiente, sin movimiento
    public async Task CobrarPrimeraCuota_Futura_QuedaPendienteSinMovimiento()
    {
        var credito = await SeedCreditoConCuota(HoyDt.AddDays(30));

        var resultado = await _service.CobrarPrimeraCuotaAlGenerarAsync(credito.Id, "Efectivo");

        Assert.Equal(EstadoCobroPrimeraCuota.NoAplica, resultado.Estado);
        var cuota = await RecargarPrimeraCuota(credito.Id);
        Assert.Equal(EstadoCuota.Pendiente, cuota.Estado);
        Assert.Empty(_caja.Movimientos);
    }

    // =====================================================================
    // Grupo G — Manipulación y concurrencia
    // =====================================================================

    [Fact] // 40: medio inexistente → rechazo, sin escritura
    public async Task CobrarPrimeraCuota_MedioInexistente_Rechaza()
    {
        var credito = await SeedCreditoConCuota(HoyDt);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CobrarPrimeraCuotaAlGenerarAsync(credito.Id, "Bitcoin"));

        var cuota = await RecargarPrimeraCuota(credito.Id);
        Assert.Equal(EstadoCuota.Pendiente, cuota.Estado);
        Assert.Empty(_caja.Movimientos);
    }

    [Fact] // 45: medio deshabilitado antes de confirmar → rechazo
    public async Task CobrarPrimeraCuota_MedioDeshabilitado_Rechaza()
    {
        var credito = await SeedCreditoConCuota(HoyDt);
        _context.ConfiguracionesPago.Add(new ConfiguracionPago
        {
            TipoPago = TipoPago.Transferencia, Nombre = "Transferencia", Activo = false
        });
        await _context.SaveChangesAsync();

        await Assert.ThrowsAsync<PagoCuotaRechazadoException>(() =>
            _service.CobrarPrimeraCuotaAlGenerarAsync(credito.Id, "Transferencia"));

        Assert.Empty(_caja.Movimientos);
    }

    [Fact] // 44: caja cerrada antes de confirmar → rechazo
    public async Task CobrarPrimeraCuota_CajaCerrada_Rechaza()
    {
        var credito = await SeedCreditoConCuota(HoyDt);
        _caja.AperturaActivaParaVenta = null; // caja cerrada

        await Assert.ThrowsAsync<PagoCuotaRechazadoException>(() =>
            _service.CobrarPrimeraCuotaAlGenerarAsync(credito.Id, "Efectivo"));

        Assert.Empty(_caja.Movimientos);
    }

    [Fact] // 41 + 42: segundo cobro tras cobrar → no aplica, no duplica
    public async Task CobrarPrimeraCuota_SegundoIntento_NoAplicaNiDuplica()
    {
        var credito = await SeedCreditoConCuota(HoyDt);

        var primero = await _service.CobrarPrimeraCuotaAlGenerarAsync(credito.Id, "Efectivo");
        Assert.Equal(EstadoCobroPrimeraCuota.Cobrada, primero.Estado);

        var segundo = await _service.CobrarPrimeraCuotaAlGenerarAsync(credito.Id, "Efectivo");
        Assert.Equal(EstadoCobroPrimeraCuota.NoAplica, segundo.Estado);

        Assert.Single(_caja.Movimientos);
        var cuota = await RecargarPrimeraCuota(credito.Id);
        Assert.Equal(MontoCuota, cuota.MontoPagado);
    }

    // =====================================================================
    // Helpers
    // =====================================================================

    private const decimal MontoCuota = 100_000m;
    private DateTime HoyDt => _reloj.HoyComercial.ToDateTime(TimeOnly.MinValue);

    private CreditoService CrearServiceCon(IRelojComercial reloj) =>
        new(_context,
            new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>(), NullLoggerFactory.Instance).CreateMapper(),
            NullLogger<CreditoService>.Instance,
            new StubFinancialServiceCobro1ra(),
            _caja,
            new StubCreditoDisponibleServiceCobro1ra(),
            new StubCurrentUserServiceCobro1ra(),
            configuracionPagoService: _configuracionPago,
            reloj: reloj);

    private async Task<Cliente> SeedClienteAsync()
    {
        var cliente = new Cliente
        {
            Nombre = "FIX-RV-PRIMERA",
            Apellido = "Cliente",
            NumeroDocumento = Guid.NewGuid().ToString("N")[..8],
            IsDeleted = false
        };
        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync();
        return cliente;
    }

    private async Task<Credito> SeedCreditoSinCuotas()
    {
        var cliente = await SeedClienteAsync();
        var credito = new Credito
        {
            ClienteId = cliente.Id,
            Numero = $"CRE-{Guid.NewGuid():N}"[..15],
            Estado = EstadoCredito.Solicitado,
            TasaInteres = 24m,
            MontoSolicitado = 200_000m,
            MontoAprobado = 200_000m,
            SaldoPendiente = 200_000m,
            CantidadCuotas = 2,
            MontoCuota = MontoCuota,
            TotalAPagar = MontoCuota * 2,
            IsDeleted = false
        };
        _context.Creditos.Add(credito);
        await _context.SaveChangesAsync();
        return credito;
    }

    private async Task<Credito> SeedCreditoConCuota(DateTime fechaVencimiento, decimal tasaInteres = 24m)
    {
        var cliente = await SeedClienteAsync();
        var credito = new Credito
        {
            ClienteId = cliente.Id,
            Numero = $"CRE-{Guid.NewGuid():N}"[..15],
            Estado = EstadoCredito.Activo,
            TasaInteres = tasaInteres,
            MontoSolicitado = 200_000m,
            MontoAprobado = 200_000m,
            SaldoPendiente = 200_000m,
            CantidadCuotas = 2,
            MontoCuota = MontoCuota,
            TotalAPagar = MontoCuota * 2,
            IsDeleted = false
        };
        _context.Creditos.Add(credito);
        await _context.SaveChangesAsync();

        _context.Cuotas.Add(new Cuota
        {
            CreditoId = credito.Id,
            NumeroCuota = 1,
            FechaVencimiento = fechaVencimiento,
            MontoTotal = MontoCuota,
            MontoCapital = MontoCuota * 0.8m,
            MontoInteres = MontoCuota * 0.2m,
            MontoPagado = 0m,
            MontoPunitorio = 0m,
            Estado = EstadoCuota.Pendiente,
            IsDeleted = false
        });
        _context.Cuotas.Add(new Cuota
        {
            CreditoId = credito.Id,
            NumeroCuota = 2,
            FechaVencimiento = fechaVencimiento.AddMonths(1),
            MontoTotal = MontoCuota,
            MontoCapital = MontoCuota * 0.8m,
            MontoInteres = MontoCuota * 0.2m,
            MontoPagado = 0m,
            MontoPunitorio = 0m,
            Estado = EstadoCuota.Pendiente,
            IsDeleted = false
        });
        await _context.SaveChangesAsync();
        return credito;
    }

    private async Task<Cuota> RecargarPrimeraCuota(int creditoId)
    {
        var cuota = await _context.Cuotas
            .AsNoTracking()
            .Where(c => c.CreditoId == creditoId && !c.IsDeleted)
            .OrderBy(c => c.NumeroCuota)
            .FirstAsync();
        return cuota;
    }

    private static ConfiguracionCreditoComando ComandoConfig(int creditoId, bool cobrar, string? medio) =>
        new()
        {
            CreditoId = creditoId,
            Monto = 200_000m,
            Anticipo = 0m,
            CantidadCuotas = 2,
            TasaMensual = 24m,
            GastosAdministrativos = 0m,
            FechaPrimeraCuota = HOY.ToDateTime(TimeOnly.MinValue),
            MetodoCalculo = MetodoCalculoCredito.Global,
            FuenteConfiguracion = FuenteConfiguracionCredito.Global,
            CuotasMinPermitidas = 1,
            CuotasMaxPermitidas = 12,
            CobrarPrimeraCuota = cobrar,
            MedioPagoPrimeraCuota = medio
        };
}
