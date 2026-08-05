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
using TheBuryProject.ViewModels.Requests;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// PUN-ML9-E: adaptación del pago múltiple al modelo autoritativo de capital + punitorio
/// aplicado. La distribución punitorio→capital (<c>DistribuirPago</c>) ya era correcta desde
/// PUN-ML6; lo que este lote agrega es la validación de RowVersion por cuota (antes inexistente —
/// el request no llevaba ningún campo de concurrencia) y el preview read-only que reusa
/// exactamente el mismo cálculo que la confirmación (<c>PlanificarPagoMultipleAsync</c>).
/// </summary>
public class CreditoServicePagoMultipleComandoTests : IDisposable
{
    private const decimal MontoCuota = 8_400m;

    private readonly SqliteConnection _connection;
    private readonly string _dataSource;
    private readonly AppDbContext _context;
    private readonly StubCajaServicePagoSeguro _caja;
    private readonly StubConfiguracionPagoAjuste _configuracionPago;
    private readonly CreditoService _service;
    private readonly TheBuryProject.Tests.Helpers.RelojComercialFake _reloj =
        new(new DateOnly(2026, 6, 15));

    public CreditoServicePagoMultipleComandoTests()
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
            new StubCreditoDisponibleServicePagoMultiple(),
            new StubCurrentUserServicePagoMultiple(),
            configuracionPagoService: configuracionPago,
            reloj: reloj);

    private async Task<Cliente> SeedClienteAsync(string sufijo)
    {
        var cliente = new Cliente
        {
            Nombre = "FIX-ML9E-MULT",
            Apellido = "Cliente",
            TipoDocumento = "DNI",
            NumeroDocumento = $"FIXML9EM{sufijo}",
            Email = "fix-ml9e-multiple@test.local"
        };
        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync();
        return cliente;
    }

    private async Task<Credito> SeedCreditoAsync(int clienteId, string sufijo)
    {
        var credito = new Credito
        {
            Numero = $"FIX-ML9E-MULT-{sufijo}",
            ClienteId = clienteId,
            Estado = EstadoCredito.Activo,
            MontoSolicitado = 100_000m,
            MontoAprobado = 100_000m,
            SaldoPendiente = 100_000m,
            TasaInteres = 0m,
            CantidadCuotas = 3,
            FechaSolicitud = DateTime.UtcNow
        };
        _context.Creditos.Add(credito);
        await _context.SaveChangesAsync();
        return credito;
    }

    private async Task<Cuota> SeedCuotaAsync(
        int creditoId,
        int numeroCuota,
        EstadoCuota estado = EstadoCuota.Pendiente,
        decimal montoPagado = 0m)
    {
        var cuota = new Cuota
        {
            CreditoId = creditoId,
            NumeroCuota = numeroCuota,
            MontoCapital = MontoCuota,
            MontoInteres = 0m,
            MontoTotal = MontoCuota,
            MontoPagado = montoPagado,
            MontoPunitorio = 0m,
            Estado = estado,
            FechaVencimiento = _reloj.HoyComercial.AddDays(-numeroCuota).ToDateTime(TimeOnly.MinValue)
        };
        _context.Cuotas.Add(cuota);
        await _context.SaveChangesAsync();
        return cuota;
    }

    private async Task<Cuota> RecargarCuotaAsync(int cuotaId)
    {
        _context.ChangeTracker.Clear();
        return await _context.Cuotas.AsNoTracking().FirstAsync(c => c.Id == cuotaId);
    }

    private static Dictionary<int, string> RowVersionesDe(params Cuota[] cuotas) =>
        cuotas.ToDictionary(c => c.Id, c => Convert.ToBase64String(c.RowVersion));

    // =========================================================================
    // Preview
    // =========================================================================

    [Fact]
    public async Task Preview_NoPersisteNada_YComponeCapitalPunitorioYTotalPorCuota()
    {
        var cliente = await SeedClienteAsync("PREV1");
        var credito = await SeedCreditoAsync(cliente.Id, "PREV1");
        var cuota1 = await SeedCuotaAsync(credito.Id, 1);
        var cuota2 = await SeedCuotaAsync(credito.Id, 2);
        _context.PunitoriosAplicados.Add(new PunitorioAplicado
        {
            CuotaId = cuota2.Id,
            FechaCalculo = _reloj.HoyComercial,
            SaldoBase = MontoCuota,
            DiasComputados = 3,
            Importe = 150m,
            Estado = EstadoPunitorioAplicado.Aplicado,
            FechaAplicacion = _reloj.AhoraUtc,
            MotivoAplicacion = "Seed",
            UsuarioAplicacion = "TestUser"
        });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var preview = await _service.PrevisualizarPagoMultipleAsync(
            cliente.Id, new List<int> { cuota1.Id, cuota2.Id }, "Efectivo");

        Assert.Equal(2, preview.Cuotas.Count);
        Assert.Equal(MontoCuota * 2, preview.CapitalTotal);
        Assert.Equal(150m, preview.PunitorioTotal);
        Assert.Equal(0m, preview.RecargoTotal);
        Assert.Equal(MontoCuota * 2 + 150m, preview.TotalCaja);

        var previewCuota2 = preview.Cuotas.Single(c => c.CuotaId == cuota2.Id);
        Assert.Equal(150m, previewCuota2.PunitorioAplicadoPendiente);
        Assert.Equal(MontoCuota, previewCuota2.CapitalPendiente);
        Assert.Equal(MontoCuota + 150m, previewCuota2.Total);

        Assert.Empty(_caja.Movimientos);
        var cuota1Bd = await RecargarCuotaAsync(cuota1.Id);
        Assert.Equal(0m, cuota1Bd.MontoPagado);
        Assert.Equal(EstadoCuota.Pendiente, cuota1Bd.Estado);
    }

    [Fact]
    public async Task Preview_ConRecargoDeMedio_LoAgregaAlTotalDeCajaPorCuotaYAgregado()
    {
        _configuracionPago.AjustePorcentaje = 2m;
        var cliente = await SeedClienteAsync("PREV2");
        var credito = await SeedCreditoAsync(cliente.Id, "PREV2");
        var cuota = await SeedCuotaAsync(credito.Id, 1);

        var preview = await _service.PrevisualizarPagoMultipleAsync(
            cliente.Id, new List<int> { cuota.Id }, "Transferencia");

        var recargoEsperado = Math.Round(MontoCuota * 0.02m, 2, MidpointRounding.AwayFromZero);
        Assert.Equal(recargoEsperado, preview.RecargoTotal);
        Assert.Equal(MontoCuota + recargoEsperado, preview.TotalCaja);
        Assert.Equal(recargoEsperado, preview.Cuotas.Single().RecargoMedioPago);
    }

    // =========================================================================
    // RowVersion / concurrencia
    // =========================================================================

    [Fact]
    public async Task Confirmar_SinRowVersionDeUnaCuota_RechazaTodoYNoPersisteNada()
    {
        var cliente = await SeedClienteAsync("RV1");
        var credito = await SeedCreditoAsync(cliente.Id, "RV1");
        var cuota1 = await SeedCuotaAsync(credito.Id, 1);
        var cuota2 = await SeedCuotaAsync(credito.Id, 2);

        var request = new PagoMultipleCuotasRequest
        {
            ClienteId = cliente.Id,
            CuotaIds = new List<int> { cuota1.Id, cuota2.Id },
            RowVersionsPorCuota = RowVersionesDe(cuota1), // falta cuota2
            MedioPago = "Efectivo"
        };

        var ex = await Assert.ThrowsAsync<PagoCuotaRechazadoException>(() => _service.PagarCuotasAsync(request));
        Assert.Equal(MotivoRechazoPagoCuota.SolicitudInvalida, ex.Motivo);

        Assert.Empty(_caja.Movimientos);
        Assert.Equal(0m, (await RecargarCuotaAsync(cuota1.Id)).MontoPagado);
        Assert.Equal(0m, (await RecargarCuotaAsync(cuota2.Id)).MontoPagado);
    }

    [Fact]
    public async Task Confirmar_ConRowVersionVencidaDeUnaCuota_RechazaTodoYNingunaCuotaCambia()
    {
        var cliente = await SeedClienteAsync("RV2");
        var credito = await SeedCreditoAsync(cliente.Id, "RV2");
        var cuota1 = await SeedCuotaAsync(credito.Id, 1);
        var cuota2 = await SeedCuotaAsync(credito.Id, 2);
        var rowVersions = RowVersionesDe(cuota1, cuota2);

        // Otra operación toca la cuota2 primero (rota su RowVersion bajo el interceptor de test).
        using (var otroContexto = CrearContexto())
        {
            var cuotaAjena = await otroContexto.Cuotas.SingleAsync(c => c.Id == cuota2.Id);
            cuotaAjena.Observaciones = "Tocada por otra operación";
            await otroContexto.SaveChangesAsync();
        }

        // _context sigue trackeando cuota1/cuota2 desde el seed (mismo contexto de todo el test):
        // sin limpiar el tracker, la próxima consulta devolvería la instancia en memoria (aún con
        // el RowVersion viejo) en vez de releer la fila real ya rotada por otroContexto.
        _context.ChangeTracker.Clear();

        var request = new PagoMultipleCuotasRequest
        {
            ClienteId = cliente.Id,
            CuotaIds = new List<int> { cuota1.Id, cuota2.Id },
            RowVersionsPorCuota = rowVersions,
            MedioPago = "Efectivo"
        };

        var ex = await Assert.ThrowsAsync<PagoCuotaRechazadoException>(() => _service.PagarCuotasAsync(request));
        Assert.Equal(MotivoRechazoPagoCuota.Conflicto, ex.Motivo);

        // Rollback total: ni siquiera la cuota1, cuyo RowVersion SÍ era vigente, quedó tocada.
        Assert.Empty(_caja.Movimientos);
        Assert.Equal(0m, (await RecargarCuotaAsync(cuota1.Id)).MontoPagado);
        Assert.Equal(EstadoCuota.Pendiente, (await RecargarCuotaAsync(cuota1.Id)).Estado);
        Assert.Empty(await _context.PagosCuota.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Confirmar_ConRowVersionVigente_CobraYUnaSegundaConfirmacionConLaMismaVersionRechaza()
    {
        var cliente = await SeedClienteAsync("RV3");
        var credito = await SeedCreditoAsync(cliente.Id, "RV3");
        var cuota = await SeedCuotaAsync(credito.Id, 1);
        var request = new PagoMultipleCuotasRequest
        {
            ClienteId = cliente.Id,
            CuotaIds = new List<int> { cuota.Id },
            RowVersionsPorCuota = RowVersionesDe(cuota),
            MedioPago = "Efectivo"
        };

        var resultado = await _service.PagarCuotasAsync(request);
        Assert.Equal(1, resultado.CantidadCuotas);
        Assert.Single(_caja.Movimientos);

        // Doble envío: mismo comando, RowVersion ya no vigente (o la cuota ya está pagada) —
        // cualquiera de los dos motivos rechaza, nunca duplica el cobro.
        await Assert.ThrowsAsync<PagoCuotaRechazadoException>(() => _service.PagarCuotasAsync(request));
        Assert.Single(_caja.Movimientos);
    }

    // =========================================================================
    // Atomicidad
    // =========================================================================

    [Fact]
    public async Task Confirmar_UnaCuotaYaPagadaEntreVarias_RevierteTodoElLote()
    {
        var cliente = await SeedClienteAsync("ATOM1");
        var credito = await SeedCreditoAsync(cliente.Id, "ATOM1");
        var cuotaValida = await SeedCuotaAsync(credito.Id, 1);
        var cuotaPagada = await SeedCuotaAsync(credito.Id, 2, EstadoCuota.Pagada, montoPagado: MontoCuota);

        var request = new PagoMultipleCuotasRequest
        {
            ClienteId = cliente.Id,
            CuotaIds = new List<int> { cuotaValida.Id, cuotaPagada.Id },
            RowVersionsPorCuota = RowVersionesDe(cuotaValida, cuotaPagada),
            MedioPago = "Efectivo"
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.PagarCuotasAsync(request));

        // Cero pagos parciales: ni siquiera la cuota válida quedó tocada.
        Assert.Empty(_caja.Movimientos);
        Assert.Empty(await _context.PagosCuota.AsNoTracking().ToListAsync());
        Assert.Equal(0m, (await RecargarCuotaAsync(cuotaValida.Id)).MontoPagado);

        var creditoBd = await _context.Creditos.AsNoTracking().SingleAsync(c => c.Id == credito.Id);
        Assert.Equal(100_000m, creditoBd.SaldoPendiente); // cupo intacto
    }

    // =========================================================================
    // Composición y cupo
    // =========================================================================

    [Fact]
    public async Task Confirmar_DosCuotasConPunitorioAplicado_CreaUnPagoCuotaPorCuotaYSoloElCapitalLiberaCupo()
    {
        var cliente = await SeedClienteAsync("COMP1");
        var credito = await SeedCreditoAsync(cliente.Id, "COMP1");
        var cuota1 = await SeedCuotaAsync(credito.Id, 1);
        var cuota2 = await SeedCuotaAsync(credito.Id, 2);
        // Tercera cuota del plan (CantidadCuotas=3) que queda deliberadamente sin pagar, para que
        // el saldo pendiente de capital tras el pago múltiple sea significativo (no simplemente 0).
        await SeedCuotaAsync(credito.Id, 3);
        _context.PunitoriosAplicados.Add(new PunitorioAplicado
        {
            CuotaId = cuota1.Id,
            FechaCalculo = _reloj.HoyComercial,
            SaldoBase = MontoCuota,
            DiasComputados = 2,
            Importe = 90m,
            Estado = EstadoPunitorioAplicado.Aplicado,
            FechaAplicacion = _reloj.AhoraUtc,
            MotivoAplicacion = "Seed",
            UsuarioAplicacion = "TestUser"
        });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var request = new PagoMultipleCuotasRequest
        {
            ClienteId = cliente.Id,
            CuotaIds = new List<int> { cuota1.Id, cuota2.Id },
            RowVersionsPorCuota = RowVersionesDe(
                await RecargarCuotaAsync(cuota1.Id),
                await RecargarCuotaAsync(cuota2.Id)),
            MedioPago = "Efectivo"
        };

        var resultado = await _service.PagarCuotasAsync(request);

        Assert.Equal(2, resultado.CantidadCuotas);
        Assert.Equal(90m, resultado.MoraTotal);
        Assert.Equal(MontoCuota * 2, resultado.Subtotal);
        Assert.Equal(MontoCuota * 2 + 90m, resultado.TotalPagado);

        var pagos = await _context.PagosCuota.AsNoTracking().ToListAsync();
        Assert.Equal(2, pagos.Count);
        Assert.All(pagos, p => Assert.NotEqual(0, p.Id));
        Assert.Contains(pagos, p => p.CuotaId == cuota1.Id && p.ImporteAplicadoPunitorio == 90m);

        var resultadoCuota1 = resultado.Cuotas.Single(c => c.CuotaId == cuota1.Id);
        Assert.NotEqual(0, resultadoCuota1.PagoCuotaId);
        Assert.Equal(90m, resultadoCuota1.Mora);

        var creditoBd = await _context.Creditos.AsNoTracking().SingleAsync(c => c.Id == credito.Id);
        // SaldoPendiente se recalcula como la suma del capital pendiente de TODAS las cuotas del
        // plan: cuota1 y cuota2 quedaron en 0 (capital saldado; el punitorio no cuenta acá), y la
        // cuota3 (deliberadamente sin pagar) es la única que sigue aportando capital pendiente.
        Assert.Equal(MontoCuota, creditoBd.SaldoPendiente);
    }
}

file sealed class StubCreditoDisponibleServicePagoMultiple : ICreditoDisponibleService
{
    public Task<decimal> ObtenerLimitePorPuntajeAsync(int puntaje, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<decimal> CalcularSaldoVigenteAsync(int clienteId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<CreditoDisponibleResultado> CalcularDisponibleAsync(int clienteId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<(bool Ok, List<string> Errores)> GuardarLimitesPorPuntajeAsync(IReadOnlyList<(int Puntaje, decimal LimiteMonto, bool Activo)> items, string usuario) => throw new NotImplementedException();
    public Task<List<PuntajeCreditoLimite>> GetAllLimitesPorPuntajeAsync() => throw new NotImplementedException();
}

file sealed class StubCurrentUserServicePagoMultiple : ICurrentUserService
{
    public string GetUsername() => "testuser";
    public string GetUserId() => "system";
    public bool IsAuthenticated() => true;
    public string? GetEmail() => "test@test.com";
    public bool IsInRole(string role) => false;
    public bool HasPermission(string modulo, string accion) => false;
    public string? GetIpAddress() => "127.0.0.1";
}
