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

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// PUN-ML9-E: contrato comando-based del adelanto (<see cref="AdelantoCuotaComando"/>), que
/// reemplaza en el controller al legacy <c>AdelantarCuotaAsync(PagarCuotaViewModel)</c> — éste
/// último se conserva intacto (ver <see cref="CreditoServiceAdelantarCuotaSeguridadTests"/>) porque
/// sigue siendo un contrato de servicio válido para otros consumidores.
///
/// A diferencia del legacy, el comando no lleva <c>CuotaId</c> ni importe: el servidor resuelve
/// siempre la última cuota pendiente y su total autoritativo (capital + punitorio aplicado
/// pendiente). Reutiliza la misma infraestructura de <see cref="CreditoServicePagarCuotaSeguridadTests"/>
/// (stubs de caja/medio de pago, interceptor de rotación de RowVersion bajo SQLite).
/// </summary>
public class CreditoServiceAdelantoComandoTests : IDisposable
{
    private const decimal MontoCuota = 15_250.50m;

    private readonly SqliteConnection _connection;
    private readonly string _dataSource;
    private readonly AppDbContext _context;
    private readonly StubCajaServicePagoSeguro _caja;
    private readonly StubConfiguracionPagoAjuste _configuracionPago;
    private readonly CreditoService _service;
    private readonly TheBuryProject.Tests.Helpers.RelojComercialFake _reloj =
        new(new DateOnly(2026, 6, 15));

    public CreditoServiceAdelantoComandoTests()
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
            new StubCreditoDisponibleServiceAdelantoComando(),
            new StubCurrentUserServiceAdelantoComando(),
            configuracionPagoService: configuracionPago,
            reloj: reloj);

    private async Task<Credito> SeedCreditoAsync(string sufijo)
    {
        var cliente = new Cliente
        {
            Nombre = "FIX-ML9E-ADEL",
            Apellido = "Cliente",
            TipoDocumento = "DNI",
            NumeroDocumento = $"FIXML9E{sufijo}",
            Email = "fix-ml9e-adelanto@test.local"
        };
        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync();

        var credito = new Credito
        {
            Numero = $"FIX-ML9E-ADEL-{sufijo}",
            ClienteId = cliente.Id,
            Estado = EstadoCredito.Activo,
            MontoSolicitado = 90_000m,
            MontoAprobado = 90_000m,
            SaldoPendiente = 90_000m,
            TasaInteres = 0m,
            CantidadCuotas = 2,
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
            FechaVencimiento = _reloj.HoyComercial.AddDays(30 * numeroCuota).ToDateTime(TimeOnly.MinValue)
        };
        _context.Cuotas.Add(cuota);
        await _context.SaveChangesAsync();
        return cuota;
    }

    /// <summary>Plan de dos cuotas pendientes; la #2 es la adelantable.</summary>
    private async Task<(Credito Credito, Cuota Ultima)> SeedPlanAsync(string sufijo)
    {
        var credito = await SeedCreditoAsync(sufijo);
        await SeedCuotaAsync(credito.Id, 1);
        var ultima = await SeedCuotaAsync(credito.Id, 2);
        return (credito, ultima);
    }

    private static AdelantoCuotaComando Comando(int creditoId, byte[] rowVersion, string medio = "Efectivo") =>
        new(creditoId, medio, "COMP-ML9E", "Adelanto de prueba", rowVersion);

    private async Task<Cuota> RecargarCuotaAsync(int cuotaId)
    {
        _context.ChangeTracker.Clear();
        return await _context.Cuotas.AsNoTracking().FirstAsync(c => c.Id == cuotaId);
    }

    // =========================================================================
    // Contexto y preview
    // =========================================================================

    [Fact]
    public async Task ObtenerContexto_SinCreditoValido_DevuelveNull()
    {
        Assert.Null(await _service.ObtenerContextoAdelantoAsync(999_999));
    }

    [Fact]
    public async Task ObtenerContexto_SinCuotasAdelantables_DevuelveNull()
    {
        var credito = await SeedCreditoAsync("SIN-CUOTA");
        await SeedCuotaAsync(credito.Id, 1, EstadoCuota.Pagada, montoPagado: MontoCuota);

        Assert.Null(await _service.ObtenerContextoAdelantoAsync(credito.Id));
    }

    /// <summary>
    /// Regresión: <c>ResolverCuotaAdelantableAsync</c> excluía <see cref="EstadoCuota.Vencida"/> de
    /// "última cuota pendiente", pese a que el resto del sistema (gate de "Pagar" individual en
    /// Details_tw, <c>PlanificarPagoMultipleAsync</c>) trata Vencida como cobrable igual que
    /// Pendiente/Parcial. Encontrado con Playwright real (E2E PUN-ML9-E): aplicar un punitorio deja
    /// la cuota en Vencida (efecto colateral de <c>EstadoCuotaResolver.Resolver</c> dentro de
    /// <c>PunitorioService.AplicarAsync</c>), y esa misma cuota dejaba de ser adelantable —
    /// el adelanto saltaba en silencio a una cuota anterior en vez de la recién vencida.
    /// </summary>
    [Fact]
    public async Task ObtenerContexto_UltimaCuotaVencida_SigueSiendoAdelantable()
    {
        var credito = await SeedCreditoAsync("VENC1");
        await SeedCuotaAsync(credito.Id, 1);
        var ultima = await SeedCuotaAsync(credito.Id, 2, EstadoCuota.Vencida);

        var contexto = await _service.ObtenerContextoAdelantoAsync(credito.Id);

        Assert.NotNull(contexto);
        Assert.Equal(ultima.Id, contexto!.CuotaId);
    }

    [Fact]
    public async Task ObtenerContexto_ResuelveLaUltimaCuotaPendienteYTotalAutoritativo()
    {
        var (credito, ultima) = await SeedPlanAsync("CTX1");

        var contexto = await _service.ObtenerContextoAdelantoAsync(credito.Id);

        Assert.NotNull(contexto);
        Assert.Equal(ultima.Id, contexto!.CuotaId);
        Assert.Equal(MontoCuota, contexto.CapitalPendiente);
        Assert.Equal(0m, contexto.PunitorioAplicadoPendiente);
        // Total = capital pendiente + punitorio aplicado pendiente — nunca el calculado informativo.
        Assert.Equal(MontoCuota, contexto.TotalCobrableActual);
    }

    [Fact]
    public async Task ObtenerContexto_ConPunitorioAplicado_SumaCapitalMasAplicadoPendiente()
    {
        var (credito, ultima) = await SeedPlanAsync("CTX2");
        _context.PunitoriosAplicados.Add(new PunitorioAplicado
        {
            CuotaId = ultima.Id,
            FechaCalculo = _reloj.HoyComercial,
            SaldoBase = MontoCuota,
            DiasComputados = 5,
            Importe = 450m,
            Estado = EstadoPunitorioAplicado.Aplicado,
            FechaAplicacion = _reloj.AhoraUtc,
            MotivoAplicacion = "Seed",
            UsuarioAplicacion = "TestUser"
        });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var contexto = await _service.ObtenerContextoAdelantoAsync(credito.Id);

        Assert.Equal(450m, contexto!.PunitorioAplicadoPendiente);
        Assert.Equal(MontoCuota + 450m, contexto.TotalCobrableActual);
    }

    [Fact]
    public async Task Preview_NoPersisteNada()
    {
        var (credito, ultima) = await SeedPlanAsync("PREV1");
        var rowVersion = (await RecargarCuotaAsync(ultima.Id)).RowVersion;

        var preview = await _service.PrevisualizarAdelantoAsync(Comando(credito.Id, rowVersion));

        Assert.NotNull(preview);
        Assert.Equal(MontoCuota, preview!.AplicadoCapital);
        Assert.Equal(0m, preview.AplicadoPunitorio);
        Assert.Empty(_caja.Movimientos);

        var cuotaBd = await RecargarCuotaAsync(ultima.Id);
        Assert.Equal(0m, cuotaBd.MontoPagado);
        Assert.Equal(EstadoCuota.Pendiente, cuotaBd.Estado);
    }

    [Fact]
    public async Task Preview_ConRecargoDeMedio_LoSeparaDelTotalACancelar()
    {
        _configuracionPago.AjustePorcentaje = 4m;
        var (credito, ultima) = await SeedPlanAsync("PREV2");
        var rowVersion = (await RecargarCuotaAsync(ultima.Id)).RowVersion;

        var preview = await _service.PrevisualizarAdelantoAsync(Comando(credito.Id, rowVersion, "Transferencia"));

        var recargoEsperado = Math.Round(MontoCuota * 0.04m, 2, MidpointRounding.AwayFromZero);
        Assert.Equal(recargoEsperado, preview!.RecargoMedioPago);
        Assert.Equal(MontoCuota + recargoEsperado, preview.TotalCaja);
    }

    // =========================================================================
    // Confirmación
    // =========================================================================

    [Fact]
    public async Task Registrar_ConRowVersionVigente_CancelaLaUltimaCuotaYLiberaSoloCapital()
    {
        var (credito, ultima) = await SeedPlanAsync("REG1");
        var rowVersion = (await RecargarCuotaAsync(ultima.Id)).RowVersion;

        var resultado = await _service.RegistrarAdelantoAsync(Comando(credito.Id, rowVersion));

        Assert.NotNull(resultado);
        Assert.Equal(ultima.Id, resultado!.CuotaId);
        Assert.Equal(MontoCuota, resultado.AplicadoCapital);
        Assert.Equal(0m, resultado.AplicadoPunitorio);
        Assert.Equal(EstadoCuota.Pagada, resultado.EstadoFinal);

        var cuotaBd = await RecargarCuotaAsync(ultima.Id);
        Assert.Equal(EstadoCuota.Pagada, cuotaBd.Estado);
        Assert.Equal(MontoCuota, cuotaBd.MontoPagado);
        Assert.Single(_caja.Movimientos);
    }

    [Fact]
    public async Task Registrar_ConPunitorioAplicadoPendiente_LoIncluyeEnElTotalYPagaConPrioridad()
    {
        var (credito, ultima) = await SeedPlanAsync("REG2");
        _context.PunitoriosAplicados.Add(new PunitorioAplicado
        {
            CuotaId = ultima.Id,
            FechaCalculo = _reloj.HoyComercial,
            SaldoBase = MontoCuota,
            DiasComputados = 5,
            Importe = 200m,
            Estado = EstadoPunitorioAplicado.Aplicado,
            FechaAplicacion = _reloj.AhoraUtc,
            MotivoAplicacion = "Seed",
            UsuarioAplicacion = "TestUser"
        });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
        var rowVersion = (await RecargarCuotaAsync(ultima.Id)).RowVersion;

        var resultado = await _service.RegistrarAdelantoAsync(Comando(credito.Id, rowVersion));

        Assert.Equal(200m, resultado!.AplicadoPunitorio);
        Assert.Equal(MontoCuota, resultado.AplicadoCapital);

        var cuotaBd = await RecargarCuotaAsync(ultima.Id);
        // Sólo el componente de capital movió MontoPagado — el punitorio no libera cupo.
        Assert.Equal(MontoCuota, cuotaBd.MontoPagado);

        var aplicadoBd = await _context.PunitoriosAplicados.AsNoTracking().SingleAsync(p => p.CuotaId == ultima.Id);
        Assert.Equal(EstadoPunitorioAplicado.Pagado, aplicadoBd.Estado);
    }

    [Fact]
    public async Task Registrar_ConRowVersionVencida_RechazaPorConflictoYNoDejaRastro()
    {
        var (credito, ultima) = await SeedPlanAsync("RV1");
        var rowVersionInicial = (await RecargarCuotaAsync(ultima.Id)).RowVersion;

        // Otra operación toca la cuota primero (rota su RowVersion bajo el interceptor de test).
        using (var otroContexto = CrearContexto())
        {
            var cuotaAjena = await otroContexto.Cuotas.SingleAsync(c => c.Id == ultima.Id);
            cuotaAjena.Observaciones = "Tocada por otra operación";
            await otroContexto.SaveChangesAsync();
        }

        var ex = await Assert.ThrowsAsync<PagoCuotaRechazadoException>(() =>
            _service.RegistrarAdelantoAsync(Comando(credito.Id, rowVersionInicial)));

        Assert.Equal(MotivoRechazoPagoCuota.Conflicto, ex.Motivo);
        Assert.Empty(_caja.Movimientos);

        var cuotaBd = await RecargarCuotaAsync(ultima.Id);
        Assert.Equal(EstadoCuota.Pendiente, cuotaBd.Estado);
        Assert.Equal(0m, cuotaBd.MontoPagado);
    }

    [Fact]
    public async Task Registrar_RowVersionMalformadaOVacia_Rechaza()
    {
        var (credito, _) = await SeedPlanAsync("RV2");

        var ex = await Assert.ThrowsAsync<PagoCuotaRechazadoException>(() =>
            _service.RegistrarAdelantoAsync(Comando(credito.Id, Array.Empty<byte>())));

        Assert.Equal(MotivoRechazoPagoCuota.SolicitudInvalida, ex.Motivo);
    }

    [Fact]
    public async Task Registrar_SegundoEnvioConMismoComando_RechazaYNoDuplicaElCobro()
    {
        var (credito, ultima) = await SeedPlanAsync("DBL1");
        var rowVersion = (await RecargarCuotaAsync(ultima.Id)).RowVersion;
        var comando = Comando(credito.Id, rowVersion);

        var primero = await _service.RegistrarAdelantoAsync(comando);
        Assert.NotNull(primero);

        // El mismo comando (mismo RowVersion original) reenviado: la cuota ya no es adelantable
        // (quedó Pagada) y su RowVersion real cambió — cualquiera de los dos motivos rechaza,
        // nunca duplica el cobro.
        await Assert.ThrowsAsync<PagoCuotaRechazadoException>(() =>
            _service.RegistrarAdelantoAsync(comando));

        Assert.Single(_caja.Movimientos);
        var cuotaBd = await RecargarCuotaAsync(ultima.Id);
        Assert.Equal(MontoCuota, cuotaBd.MontoPagado);
    }

    [Fact]
    public async Task Registrar_DosIntentosConcurrentes_SoloUnoCobra()
    {
        var (credito, ultima) = await SeedPlanAsync("CONC1");
        var rowVersion = (await RecargarCuotaAsync(ultima.Id)).RowVersion;

        await using var contextoA = CrearContexto();
        await using var contextoB = CrearContexto();
        var cajaA = new StubCajaServicePagoSeguro(contextoA);
        var cajaB = new StubCajaServicePagoSeguro(contextoB);
        var servicioA = CrearService(contextoA, cajaA, new StubConfiguracionPagoAjuste(), _reloj);
        var servicioB = CrearService(contextoB, cajaB, new StubConfiguracionPagoAjuste(), _reloj);

        var comandoA = Comando(credito.Id, rowVersion);
        var comandoB = Comando(credito.Id, rowVersion);

        var intentoA = servicioA.RegistrarAdelantoAsync(comandoA);
        var intentoB = servicioB.RegistrarAdelantoAsync(comandoB);

        var resultados = await Task.WhenAll(
            EjecutarProtegidoAsync(intentoA),
            EjecutarProtegidoAsync(intentoB));

        Assert.Equal(1, resultados.Count(r => r));
        Assert.Equal(1, cajaA.Movimientos.Count + cajaB.Movimientos.Count);

        var cuotaBd = await RecargarCuotaAsync(ultima.Id);
        Assert.Equal(EstadoCuota.Pagada, cuotaBd.Estado);
        Assert.Equal(MontoCuota, cuotaBd.MontoPagado);
    }

    private static async Task<bool> EjecutarProtegidoAsync(Task<PagoCuotaResultado?> intento)
    {
        try
        {
            var resultado = await intento;
            return resultado is not null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    [Fact]
    public async Task Registrar_ConValoresDerivadosDelComando_ElServidorSiempreRecalculaYNoAceptaImporte()
    {
        // El comando no tiene ningún campo de importe/fecha/estado que el navegador pueda mandar:
        // sólo medio, comprobante, observaciones y RowVersion. No hay nada que "manipular" salvo el
        // RowVersion (cubierto arriba) — este test documenta la ausencia estructural del resto.
        var comandoType = typeof(AdelantoCuotaComando);
        var propiedades = comandoType.GetProperties().Select(p => p.Name).ToArray();

        Assert.DoesNotContain("MontoPagado", propiedades);
        Assert.DoesNotContain("MontoIngresado", propiedades);
        Assert.DoesNotContain("MontoCuota", propiedades);
        Assert.DoesNotContain("MontoPunitorio", propiedades);
        Assert.DoesNotContain("TotalAPagar", propiedades);
        Assert.DoesNotContain("FechaPago", propiedades);
        Assert.DoesNotContain("Estado", propiedades);
        Assert.DoesNotContain("CuotaId", propiedades);
        await Task.CompletedTask;
    }
}

file sealed class StubCreditoDisponibleServiceAdelantoComando : ICreditoDisponibleService
{
    public Task<decimal> ObtenerLimitePorPuntajeAsync(int puntaje, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<decimal> CalcularSaldoVigenteAsync(int clienteId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<CreditoDisponibleResultado> CalcularDisponibleAsync(int clienteId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<(bool Ok, List<string> Errores)> GuardarLimitesPorPuntajeAsync(IReadOnlyList<(int Puntaje, decimal LimiteMonto, bool Activo)> items, string usuario) => throw new NotImplementedException();
    public Task<List<PuntajeCreditoLimite>> GetAllLimitesPorPuntajeAsync() => throw new NotImplementedException();
}

file sealed class StubCurrentUserServiceAdelantoComando : ICurrentUserService
{
    public string GetUsername() => "testuser";
    public string GetUserId() => "system";
    public bool IsAuthenticated() => true;
    public string? GetEmail() => "test@test.com";
    public bool IsInRole(string role) => false;
    public bool HasPermission(string modulo, string accion) => false;
    public string? GetIpAddress() => "127.0.0.1";
}
