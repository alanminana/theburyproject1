using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.DTOs;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Tests.Helpers;

namespace TheBuryProject.Tests.Integration;

// ---------------------------------------------------------------------------
// PUN-ML10-B — Consulta batch autoritativa de punitorios aplicados pendientes por cuota.
//
// IPunitorioService.ObtenerPunitorioAplicadoPendientePorCuotasAsync debe ser semánticamente
// idéntico, cuota por cuota, a llamar ObtenerPunitorioAplicadoPendienteAsync individualmente —
// pero sin N+1 (una cantidad fija y acotada de queries sin importar cuántas cuotas se pidan).
//
// Reutiliza StubCurrentUserServicePunitorio (internal, declarado en PunitorioServiceTests.cs,
// mismo namespace/ensamblado) para no duplicar el doble de autorización.
// ---------------------------------------------------------------------------

public sealed class PunitorioAplicadoPendientePorCuotasTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly string _dataSource;
    private readonly AppDbContext _context;
    private readonly RelojComercialFake _reloj;
    private readonly StubCurrentUserServicePunitorio _currentUser;
    private readonly PunitorioService _service;
    private int _nextId = 1;

    public PunitorioAplicadoPendientePorCuotasTests()
    {
        _dataSource = $"{Guid.NewGuid():N}";
        _connection = new SqliteConnection($"DataSource={_dataSource};Mode=Memory;Cache=Shared");
        _connection.Open();

        _context = CrearContexto();
        _context.Database.EnsureCreated();

        _reloj = new RelojComercialFake(new DateOnly(2026, 8, 1));
        _currentUser = new StubCurrentUserServicePunitorio();
        _service = CrearServicio(_context, _reloj, _currentUser);
    }

    private AppDbContext CrearContexto(DbCommandInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"DataSource={_dataSource};Mode=Memory;Cache=Shared");
        if (interceptor is not null)
            builder.AddInterceptors(interceptor);
        return new AppDbContext(builder.Options);
    }

    private static PunitorioService CrearServicio(AppDbContext context, RelojComercialFake reloj, ICurrentUserService currentUser) =>
        new(context, new PunitorioCalculator(), reloj, currentUser, NullLogger<PunitorioService>.Instance);

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // Helpers de siembra (mismo patrón que PunitorioServiceTests.cs)
    // -------------------------------------------------------------------------

    private async Task<Cuota> SeedCuotaAsync(decimal montoTotal, DateTime fechaVencimiento)
    {
        var id = _nextId++;
        var cliente = new Cliente { Id = id, Nombre = "Cliente", Apellido = $"Test{id}", NumeroDocumento = $"9300{id:D5}", IsDeleted = false };
        _context.Clientes.Add(cliente);

        var credito = new Credito
        {
            Id = id,
            ClienteId = id,
            IsDeleted = false,
            Numero = $"CRED{id:D4}",
            Estado = EstadoCredito.Activo,
            TasaInteres = 24m,
            MontoSolicitado = montoTotal,
            MontoAprobado = montoTotal,
            SaldoPendiente = montoTotal,
            CantidadCuotas = 1,
            MontoCuota = montoTotal,
            TotalAPagar = montoTotal
        };
        _context.Creditos.Add(credito);

        var cuota = new Cuota
        {
            Id = id,
            CreditoId = id,
            NumeroCuota = 1,
            FechaVencimiento = fechaVencimiento,
            MontoTotal = montoTotal,
            MontoCapital = montoTotal,
            MontoInteres = 0m,
            MontoPagado = 0m,
            MontoPunitorio = 0m,
            Estado = EstadoCuota.Pendiente,
            IsDeleted = false
        };
        _context.Cuotas.Add(cuota);

        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
        return cuota;
    }

    private async Task SeedConfiguracionAsync(
        decimal porcentaje, int periodoDias, int diasGracia, DateOnly vigenteDesde, bool activa = true)
    {
        _context.ConfiguracionesPunitorio.Add(new ConfiguracionPunitorio
        {
            Porcentaje = porcentaje,
            PeriodoDias = periodoDias,
            DiasGracia = diasGracia,
            ProrrateoDiario = true,
            VigenteDesde = vigenteDesde,
            Activa = activa
        });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
    }

    private async Task SeedPagoAsync(
        int cuotaId, DateOnly fechaPagoComercial, decimal importeTotal,
        decimal? importeAplicadoCuota = null,
        EstadoPagoCuota estado = EstadoPagoCuota.Aplicado,
        decimal? importeAplicadoPunitorio = null,
        int? punitorioAplicadoId = null)
    {
        var pago = new PagoCuota
        {
            CuotaId = cuotaId,
            FechaPagoComercial = fechaPagoComercial,
            ImporteTotal = importeTotal,
            ImporteAplicadoCuota = importeAplicadoCuota ?? importeTotal,
            ImporteAplicadoPunitorio = importeAplicadoPunitorio ?? 0m,
            PunitorioAplicadoId = punitorioAplicadoId,
            Origen = OrigenPagoCuota.RegistradoPorSistema,
            Estado = estado,
            HistorialCompleto = true
        };
        _context.PagosCuota.Add(pago);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
    }

    private async Task<PunitorioAplicado> AplicarAsync(int cuotaId, string motivo = "Mora confirmada") =>
        await _service.AplicarAsync(cuotaId, new PunitorioAplicarComando { Motivo = motivo });

    // ===========================================================================================
    // Casos obligatorios
    // ===========================================================================================

    [Fact]
    public async Task ColeccionVacia_DevuelveDiccionarioVacioSinConsultarLaBase()
    {
        var contador = new ContadorDeComandosInterceptor();
        using var contexto = CrearContexto(contador);
        var servicio = CrearServicio(contexto, _reloj, _currentUser);

        var resultado = await servicio.ObtenerPunitorioAplicadoPendientePorCuotasAsync(Array.Empty<int>());

        Assert.Empty(resultado);
        Assert.Equal(0, contador.ReaderCommands);
    }

    [Fact]
    public async Task UnId_CoincideConLaOperacionIndividual()
    {
        var cuota = await SeedCuotaAsync(10_000m, _reloj.HoyComercial.AddDays(-6).ToDateTime(TimeOnly.MinValue));
        await SeedConfiguracionAsync(10m, 20, diasGracia: 5, vigenteDesde: new DateOnly(2025, 1, 1));
        await AplicarAsync(cuota.Id);
        _context.ChangeTracker.Clear();

        var individual = await _service.ObtenerPunitorioAplicadoPendienteAsync(cuota.Id);
        var batch = await _service.ObtenerPunitorioAplicadoPendientePorCuotasAsync([cuota.Id]);

        Assert.Equal(300.00m, individual);
        Assert.Equal(individual, batch[cuota.Id]);
    }

    [Fact]
    public async Task VariosIds_CoincideConLaOperacionIndividualParaCadaUno()
    {
        var vencimiento = _reloj.HoyComercial.AddDays(-6).ToDateTime(TimeOnly.MinValue);
        await SeedConfiguracionAsync(10m, 20, diasGracia: 5, vigenteDesde: new DateOnly(2025, 1, 1));
        var cuotaA = await SeedCuotaAsync(10_000m, vencimiento);
        var cuotaB = await SeedCuotaAsync(5_000m, vencimiento);
        var cuotaC = await SeedCuotaAsync(2_000m, vencimiento);
        await AplicarAsync(cuotaA.Id);
        await AplicarAsync(cuotaB.Id);
        // cuotaC sin aplicación.
        _context.ChangeTracker.Clear();

        var batch = await _service.ObtenerPunitorioAplicadoPendientePorCuotasAsync(
            [cuotaA.Id, cuotaB.Id, cuotaC.Id]);

        Assert.Equal(3, batch.Count);
        foreach (var id in new[] { cuotaA.Id, cuotaB.Id, cuotaC.Id })
        {
            var individual = await _service.ObtenerPunitorioAplicadoPendienteAsync(id);
            Assert.Equal(individual, batch[id]);
        }
    }

    [Fact]
    public async Task IdsDuplicados_SeDeduplicanYDevuelvenUnaSolaEntrada()
    {
        var cuota = await SeedCuotaAsync(10_000m, _reloj.HoyComercial.AddDays(-6).ToDateTime(TimeOnly.MinValue));
        await SeedConfiguracionAsync(10m, 20, diasGracia: 5, vigenteDesde: new DateOnly(2025, 1, 1));
        await AplicarAsync(cuota.Id);
        _context.ChangeTracker.Clear();

        var batch = await _service.ObtenerPunitorioAplicadoPendientePorCuotasAsync(
            [cuota.Id, cuota.Id, cuota.Id]);

        Assert.Single(batch);
        Assert.Equal(300.00m, batch[cuota.Id]);
    }

    [Fact]
    public async Task CuotaSinPunitorio_DevuelveCero()
    {
        var cuota = await SeedCuotaAsync(1_000m, _reloj.HoyComercial.AddDays(-30).ToDateTime(TimeOnly.MinValue));

        var batch = await _service.ObtenerPunitorioAplicadoPendientePorCuotasAsync([cuota.Id]);

        Assert.Equal(0m, batch[cuota.Id]);
    }

    [Fact]
    public async Task CuotaIdInvalido_NoExisteComoCuota_DevuelveCeroSinExcepcion()
    {
        var batch = await _service.ObtenerPunitorioAplicadoPendientePorCuotasAsync([999_999]);

        Assert.Equal(0m, batch[999_999]);
    }

    [Fact]
    public async Task PunitorioAplicadoSinPagos_DevuelveElImporteCompleto()
    {
        var cuota = await SeedCuotaAsync(10_000m, _reloj.HoyComercial.AddDays(-6).ToDateTime(TimeOnly.MinValue));
        await SeedConfiguracionAsync(10m, 20, diasGracia: 5, vigenteDesde: new DateOnly(2025, 1, 1));
        var aplicada = await AplicarAsync(cuota.Id);
        _context.ChangeTracker.Clear();

        var batch = await _service.ObtenerPunitorioAplicadoPendientePorCuotasAsync([cuota.Id]);

        Assert.Equal(aplicada.Importe, batch[cuota.Id]);
        Assert.Equal(300.00m, batch[cuota.Id]);
    }

    [Fact]
    public async Task PagoParcial_RestaSoloElImporteAplicadoPunitorio()
    {
        var cuota = await SeedCuotaAsync(10_000m, _reloj.HoyComercial.AddDays(-6).ToDateTime(TimeOnly.MinValue));
        await SeedConfiguracionAsync(10m, 20, diasGracia: 5, vigenteDesde: new DateOnly(2025, 1, 1));
        var aplicada = await AplicarAsync(cuota.Id);
        await SeedPagoAsync(
            cuota.Id, _reloj.HoyComercial, 100m,
            importeAplicadoCuota: 0m, importeAplicadoPunitorio: 100m, punitorioAplicadoId: aplicada.Id);

        var batch = await _service.ObtenerPunitorioAplicadoPendientePorCuotasAsync([cuota.Id]);

        Assert.Equal(200.00m, batch[cuota.Id]);
    }

    [Fact]
    public async Task PagoTotal_DevuelveCero()
    {
        var cuota = await SeedCuotaAsync(10_000m, _reloj.HoyComercial.AddDays(-6).ToDateTime(TimeOnly.MinValue));
        await SeedConfiguracionAsync(10m, 20, diasGracia: 5, vigenteDesde: new DateOnly(2025, 1, 1));
        var aplicada = await AplicarAsync(cuota.Id);
        await SeedPagoAsync(
            cuota.Id, _reloj.HoyComercial, aplicada.Importe,
            importeAplicadoCuota: 0m, importeAplicadoPunitorio: aplicada.Importe, punitorioAplicadoId: aplicada.Id);

        var batch = await _service.ObtenerPunitorioAplicadoPendientePorCuotasAsync([cuota.Id]);

        Assert.Equal(0m, batch[cuota.Id]);
    }

    [Fact]
    public async Task PagosMayoresAlAplicado_DatoHistoricoInconsistente_NuncaDevuelveNegativo()
    {
        var cuota = await SeedCuotaAsync(10_000m, _reloj.HoyComercial.AddDays(-6).ToDateTime(TimeOnly.MinValue));
        await SeedConfiguracionAsync(10m, 20, diasGracia: 5, vigenteDesde: new DateOnly(2025, 1, 1));
        var aplicada = await AplicarAsync(cuota.Id); // Importe = 300
        await SeedPagoAsync(
            cuota.Id, _reloj.HoyComercial, 500m,
            importeAplicadoCuota: 0m, importeAplicadoPunitorio: 500m, punitorioAplicadoId: aplicada.Id);

        var batch = await _service.ObtenerPunitorioAplicadoPendientePorCuotasAsync([cuota.Id]);
        var individual = await _service.ObtenerPunitorioAplicadoPendienteAsync(cuota.Id);

        Assert.Equal(0m, batch[cuota.Id]);
        Assert.Equal(individual, batch[cuota.Id]);
    }

    [Fact]
    public async Task PunitorioAnulado_NoAportaPendiente()
    {
        var cuota = await SeedCuotaAsync(10_000m, _reloj.HoyComercial.AddDays(-6).ToDateTime(TimeOnly.MinValue));
        await SeedConfiguracionAsync(10m, 20, diasGracia: 5, vigenteDesde: new DateOnly(2025, 1, 1));
        var aplicada = await AplicarAsync(cuota.Id);
        await _service.AnularAsync(aplicada.Id, new PunitorioAnularComando { Motivo = "Error verificado" });
        _context.ChangeTracker.Clear();

        var batch = await _service.ObtenerPunitorioAplicadoPendientePorCuotasAsync([cuota.Id]);

        Assert.Equal(0m, batch[cuota.Id]);
    }

    [Fact]
    public async Task MultiplesAplicacionesHistoricas_SoloCuentaLaActiva()
    {
        var cuota = await SeedCuotaAsync(1_000m, _reloj.HoyComercial.AddDays(-30).ToDateTime(TimeOnly.MinValue));
        var aplicaciones = new[]
        {
            new PunitorioAplicado
            {
                CuotaId = cuota.Id, FechaCalculo = _reloj.HoyComercial, SaldoBase = 1_000m,
                DiasComputados = 10, Importe = 10m, Estado = EstadoPunitorioAplicado.Pagado,
                FechaAplicacion = _reloj.AhoraUtc.AddDays(-2), MotivoAplicacion = "Primera", UsuarioAplicacion = "u1"
            },
            new PunitorioAplicado
            {
                CuotaId = cuota.Id, FechaCalculo = _reloj.HoyComercial, SaldoBase = 1_000m,
                DiasComputados = 20, Importe = 20m, Estado = EstadoPunitorioAplicado.Anulado,
                FechaAplicacion = _reloj.AhoraUtc.AddDays(-1), MotivoAplicacion = "Segunda", UsuarioAplicacion = "u2"
            },
            new PunitorioAplicado
            {
                CuotaId = cuota.Id, FechaCalculo = _reloj.HoyComercial, SaldoBase = 1_000m,
                DiasComputados = 30, Importe = 30m, Estado = EstadoPunitorioAplicado.Aplicado,
                FechaAplicacion = _reloj.AhoraUtc, MotivoAplicacion = "Tercera", UsuarioAplicacion = "u3"
            }
        };
        _context.PunitoriosAplicados.AddRange(aplicaciones);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var batch = await _service.ObtenerPunitorioAplicadoPendientePorCuotasAsync([cuota.Id]);

        Assert.Equal(30m, batch[cuota.Id]);
    }

    [Fact]
    public async Task VariasCuotasMezcladas_CadaUnaResuelveSuPropioEstado()
    {
        var vencimiento = _reloj.HoyComercial.AddDays(-6).ToDateTime(TimeOnly.MinValue);
        await SeedConfiguracionAsync(10m, 20, diasGracia: 5, vigenteDesde: new DateOnly(2025, 1, 1));

        var sinPunitorio = await SeedCuotaAsync(1_000m, _reloj.HoyComercial.AddDays(-30).ToDateTime(TimeOnly.MinValue));

        var aplicadaSinPagos = await SeedCuotaAsync(10_000m, vencimiento);
        var aplicacion1 = await AplicarAsync(aplicadaSinPagos.Id);

        var aplicadaConPagoParcial = await SeedCuotaAsync(10_000m, vencimiento);
        var aplicacion2 = await AplicarAsync(aplicadaConPagoParcial.Id);
        await SeedPagoAsync(
            aplicadaConPagoParcial.Id, _reloj.HoyComercial, 120m,
            importeAplicadoCuota: 0m, importeAplicadoPunitorio: 120m, punitorioAplicadoId: aplicacion2.Id);

        var anulada = await SeedCuotaAsync(10_000m, vencimiento);
        var aplicacion3 = await AplicarAsync(anulada.Id);
        await _service.AnularAsync(aplicacion3.Id, new PunitorioAnularComando { Motivo = "Error" });
        _context.ChangeTracker.Clear();

        var idsSolicitados = new[]
        {
            sinPunitorio.Id, aplicadaSinPagos.Id, aplicadaConPagoParcial.Id, anulada.Id, 999_999
        };
        var batch = await _service.ObtenerPunitorioAplicadoPendientePorCuotasAsync(idsSolicitados);

        Assert.Equal(5, batch.Count);
        Assert.Equal(0m, batch[sinPunitorio.Id]);
        Assert.Equal(300.00m, batch[aplicadaSinPagos.Id]);
        Assert.Equal(180.00m, batch[aplicadaConPagoParcial.Id]);
        Assert.Equal(0m, batch[anulada.Id]);
        Assert.Equal(0m, batch[999_999]);

        foreach (var id in idsSolicitados)
        {
            var individual = await _service.ObtenerPunitorioAplicadoPendienteAsync(id);
            Assert.Equal(individual, batch[id]);
        }
    }

    [Fact]
    public async Task NingunAccesoAMontoPunitorioLegacy_NoAlteraElResultado()
    {
        var cuota = await SeedCuotaAsync(10_000m, _reloj.HoyComercial.AddDays(-6).ToDateTime(TimeOnly.MinValue));
        await SeedConfiguracionAsync(10m, 20, diasGracia: 5, vigenteDesde: new DateOnly(2025, 1, 1));
        await AplicarAsync(cuota.Id);
        _context.ChangeTracker.Clear();

        var baseline = await _service.ObtenerPunitorioAplicadoPendientePorCuotasAsync([cuota.Id]);

        var cuotaTracked = await _context.Cuotas.SingleAsync(c => c.Id == cuota.Id);
        cuotaTracked.MontoPunitorio = 999_999m;
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var despues = await _service.ObtenerPunitorioAplicadoPendientePorCuotasAsync([cuota.Id]);

        Assert.Equal(baseline[cuota.Id], despues[cuota.Id]);
    }

    [Fact]
    public async Task CeroEscrituras_NoModificaNiTrackeaNadaEnLaBase()
    {
        var vencimiento = _reloj.HoyComercial.AddDays(-6).ToDateTime(TimeOnly.MinValue);
        await SeedConfiguracionAsync(10m, 20, diasGracia: 5, vigenteDesde: new DateOnly(2025, 1, 1));
        var cuotaA = await SeedCuotaAsync(10_000m, vencimiento);
        var cuotaB = await SeedCuotaAsync(5_000m, vencimiento);
        await AplicarAsync(cuotaA.Id);
        await AplicarAsync(cuotaB.Id);
        _context.ChangeTracker.Clear();

        var aplicacionesAntes = await _context.PunitoriosAplicados.CountAsync();
        var pagosAntes = await _context.PagosCuota.CountAsync();

        await _service.ObtenerPunitorioAplicadoPendientePorCuotasAsync([cuotaA.Id, cuotaB.Id]);

        Assert.Empty(_context.ChangeTracker.Entries());
        Assert.Equal(aplicacionesAntes, await _context.PunitoriosAplicados.CountAsync());
        Assert.Equal(pagosAntes, await _context.PagosCuota.CountAsync());
    }

    [Fact]
    public async Task CancellationTokenCancelado_SePropaga()
    {
        var cuota = await SeedCuotaAsync(10_000m, _reloj.HoyComercial.AddDays(-6).ToDateTime(TimeOnly.MinValue));
        await SeedConfiguracionAsync(10m, 20, diasGracia: 5, vigenteDesde: new DateOnly(2025, 1, 1));
        await AplicarAsync(cuota.Id);
        _context.ChangeTracker.Clear();

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _service.ObtenerPunitorioAplicadoPendientePorCuotasAsync([cuota.Id], cancellation.Token));
    }

    [Fact]
    public async Task SinNMasUno_CantidadDeQueriesNoCreceConLaCantidadDeCuotas()
    {
        var vencimiento = _reloj.HoyComercial.AddDays(-6).ToDateTime(TimeOnly.MinValue);
        await SeedConfiguracionAsync(10m, 20, diasGracia: 5, vigenteDesde: new DateOnly(2025, 1, 1));

        var cuotaUnica = await SeedCuotaAsync(10_000m, vencimiento);
        var aplicacionUnica = await AplicarAsync(cuotaUnica.Id);
        await SeedPagoAsync(
            cuotaUnica.Id, _reloj.HoyComercial, 50m,
            importeAplicadoCuota: 0m, importeAplicadoPunitorio: 50m, punitorioAplicadoId: aplicacionUnica.Id);

        var cuotasAdicionales = new List<Cuota>();
        for (var i = 0; i < 4; i++)
        {
            var cuota = await SeedCuotaAsync(10_000m, vencimiento);
            var aplicacion = await AplicarAsync(cuota.Id);
            await SeedPagoAsync(
                cuota.Id, _reloj.HoyComercial, 10m,
                importeAplicadoCuota: 0m, importeAplicadoPunitorio: 10m, punitorioAplicadoId: aplicacion.Id);
            cuotasAdicionales.Add(cuota);
        }
        _context.ChangeTracker.Clear();

        var contadorUno = new ContadorDeComandosInterceptor();
        using (var contextoUno = CrearContexto(contadorUno))
        {
            var servicioUno = CrearServicio(contextoUno, _reloj, _currentUser);
            await servicioUno.ObtenerPunitorioAplicadoPendientePorCuotasAsync([cuotaUnica.Id]);
        }

        var contadorCinco = new ContadorDeComandosInterceptor();
        using (var contextoCinco = CrearContexto(contadorCinco))
        {
            var servicioCinco = CrearServicio(contextoCinco, _reloj, _currentUser);
            var todosLosIds = new[] { cuotaUnica.Id }.Concat(cuotasAdicionales.Select(c => c.Id)).ToArray();
            await servicioCinco.ObtenerPunitorioAplicadoPendientePorCuotasAsync(todosLosIds);
        }

        Assert.Equal(2, contadorUno.ReaderCommands);
        Assert.Equal(contadorUno.ReaderCommands, contadorCinco.ReaderCommands);
    }

    [Fact]
    public async Task Equivalencia_BatchIgualaOperacionIndividualParaCadaCuota()
    {
        var vencimiento = _reloj.HoyComercial.AddDays(-6).ToDateTime(TimeOnly.MinValue);
        await SeedConfiguracionAsync(10m, 20, diasGracia: 5, vigenteDesde: new DateOnly(2025, 1, 1));

        var ids = new List<int>();
        for (var i = 0; i < 6; i++)
        {
            var cuota = await SeedCuotaAsync(10_000m + i * 500m, vencimiento);
            ids.Add(cuota.Id);

            if (i % 3 == 0)
                continue; // sin aplicación

            var aplicacion = await AplicarAsync(cuota.Id);

            if (i % 3 == 2)
            {
                await SeedPagoAsync(
                    cuota.Id, _reloj.HoyComercial, 50m,
                    importeAplicadoCuota: 0m, importeAplicadoPunitorio: 50m, punitorioAplicadoId: aplicacion.Id);
            }
        }
        _context.ChangeTracker.Clear();

        var batch = await _service.ObtenerPunitorioAplicadoPendientePorCuotasAsync(ids);

        foreach (var id in ids)
        {
            var individual = await _service.ObtenerPunitorioAplicadoPendienteAsync(id);
            Assert.Equal(individual, batch[id]);
        }
    }

    // -------------------------------------------------------------------------
    // Interceptor focalizado de test: cuenta comandos de lectura ejecutados, para probar
    // la ausencia de N+1 sin instrumentación productiva.
    // -------------------------------------------------------------------------
    private sealed class ContadorDeComandosInterceptor : DbCommandInterceptor
    {
        public int ReaderCommands { get; private set; }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            ReaderCommands++;
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            ReaderCommands++;
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
