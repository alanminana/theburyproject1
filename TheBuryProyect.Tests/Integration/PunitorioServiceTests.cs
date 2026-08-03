using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using TheBuryProject.Data;
using TheBuryProject.Models.DTOs;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Exceptions;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;
using TheBuryProject.Tests.Helpers;

namespace TheBuryProject.Tests.Integration;

// ---------------------------------------------------------------------------
// PUN-ML5 — Consulta server-authoritative y aplicación autorizada de punitorios.
//
// Usa el IPunitorioCalculator real (PUN-ML4, puro) — no se mockea: lo que se prueba acá es que
// PunitorioService lo alimenta con datos reales de Cuota/PagoCuota/ConfiguracionPunitorio y decide
// correctamente qué persistir. Reloj fake fijado en 2026-08-01 salvo que un test lo mueva.
//
// Autorización: PunitorioService resuelve actor y permiso desde ICurrentUserService (no hay
// controller todavía). El stub de abajo simula una sesión autenticada con permiso de aplicar y
// anular por default ("operador1"); los tests de autorización lo reconfiguran puntualmente.
// ---------------------------------------------------------------------------

internal sealed class StubCurrentUserServicePunitorio : ICurrentUserService
{
    public bool Authenticated { get; set; } = true;
    public string Username { get; set; } = "operador1";
    public HashSet<(string Modulo, string Accion)> Permisos { get; } = new()
    {
        ("cobranzas", "applyfine"),
        ("cobranzas", "revertfine")
    };

    public string GetUsername() => Username;
    public string GetUserId() => "system";
    public bool IsAuthenticated() => Authenticated;
    public string? GetEmail() => "test@test.com";
    public bool IsInRole(string role) => false;
    public bool HasPermission(string modulo, string accion) => Authenticated && Permisos.Contains((modulo, accion));
    public string? GetIpAddress() => "127.0.0.1";
}

public sealed class PunitorioServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly string _dataSource;
    private readonly AppDbContext _context;
    private readonly RelojComercialFake _reloj;
    private readonly StubCurrentUserServicePunitorio _currentUser;
    private readonly PunitorioService _service;
    private int _nextId = 1;

    public PunitorioServiceTests()
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

    private AppDbContext CrearContexto() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"DataSource={_dataSource};Mode=Memory;Cache=Shared")
            .Options);

    private static PunitorioService CrearServicio(AppDbContext context, RelojComercialFake reloj, ICurrentUserService currentUser) =>
        new(context, new PunitorioCalculator(), reloj, currentUser, NullLogger<PunitorioService>.Instance);

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // Helpers de siembra
    // -------------------------------------------------------------------------

    private async Task<Cuota> SeedCuotaAsync(decimal montoTotal, DateTime fechaVencimiento)
    {
        var id = _nextId++;
        var cliente = new Cliente { Id = id, Nombre = "Cliente", Apellido = $"Test{id}", NumeroDocumento = $"9200{id:D5}", IsDeleted = false };
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
        decimal? importeAplicadoCuota = null, bool historialCompleto = true,
        EstadoPagoCuota estado = EstadoPagoCuota.Aplicado)
    {
        _context.PagosCuota.Add(new PagoCuota
        {
            CuotaId = cuotaId,
            FechaPagoComercial = fechaPagoComercial,
            ImporteTotal = importeTotal,
            ImporteAplicadoCuota = historialCompleto ? (importeAplicadoCuota ?? importeTotal) : importeAplicadoCuota,
            ImporteAplicadoPunitorio = 0m,
            Origen = OrigenPagoCuota.RegistradoPorSistema,
            Estado = estado,
            HistorialCompleto = historialCompleto
        });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
    }

    private static PunitorioAplicarComando Comando(string motivo = "Mora confirmada por el operador", byte[]? rowVersion = null) =>
        new() { Motivo = motivo, CuotaRowVersionEsperada = rowVersion };

    // ===========================================================================================
    // Consulta — CalcularCuotaAsync
    // ===========================================================================================

    [Fact]
    public async Task CalcularCuotaAsync_DentroDeGracia_DevuelveCeroYNoPersiste()
    {
        var cuota = await SeedCuotaAsync(1_000m, _reloj.HoyComercial.AddDays(-5).ToDateTime(TimeOnly.MinValue));
        await SeedConfiguracionAsync(10m, 20, diasGracia: 5, vigenteDesde: new DateOnly(2025, 1, 1));
        _context.ChangeTracker.Clear();

        var resultado = await _service.CalcularCuotaAsync(cuota.Id);

        Assert.Equal(EstadoResultadoPunitorio.DentroDeGracia, resultado.EstadoCalculo);
        Assert.Equal(0m, resultado.PunitorioCalculado);
        Assert.Empty(_context.ChangeTracker.Entries());
        Assert.Equal(0, await _context.PunitoriosAplicados.CountAsync());
    }

    [Fact]
    public async Task CalcularCuotaAsync_ConConfiguracionValida_DevuelveResultadoExactoDelCalculator()
    {
        // Ejemplo numérico del plan: $10.000, vencida hace 6 días, gracia 5, 10% cada 20 días → $300.
        var cuota = await SeedCuotaAsync(10_000m, _reloj.HoyComercial.AddDays(-6).ToDateTime(TimeOnly.MinValue));
        await SeedConfiguracionAsync(10m, 20, diasGracia: 5, vigenteDesde: new DateOnly(2025, 1, 1));

        var resultado = await _service.CalcularCuotaAsync(cuota.Id);

        Assert.Equal(EstadoResultadoPunitorio.Calculado, resultado.EstadoCalculo);
        Assert.Equal(300.00m, resultado.PunitorioCalculado);
        Assert.Equal(10_000m, resultado.SaldoImpago);
        Assert.Equal(10_300.00m, resultado.TotalPendienteEstimado);
        Assert.Single(resultado.ConfiguracionesUtilizadas);
    }

    [Fact]
    public async Task CalcularCuotaAsync_ConPagosParciales_UsaElLedger()
    {
        var vencimiento = _reloj.HoyComercial.AddDays(-30);
        var cuotaA = await SeedCuotaAsync(1_000m, vencimiento.ToDateTime(TimeOnly.MinValue));
        var cuotaB = await SeedCuotaAsync(1_000m, vencimiento.ToDateTime(TimeOnly.MinValue));
        await SeedConfiguracionAsync(10m, 20, diasGracia: 5, vigenteDesde: new DateOnly(2025, 1, 1));

        // La cuota B pagó $400 antes del vencimiento: menos saldo base, menos punitorio.
        await SeedPagoAsync(cuotaB.Id, vencimiento.AddDays(-3), 400m);

        var resultadoA = await _service.CalcularCuotaAsync(cuotaA.Id);
        var resultadoB = await _service.CalcularCuotaAsync(cuotaB.Id);

        Assert.Equal(1_000m, resultadoA.SaldoImpago);
        Assert.Equal(600m, resultadoB.SaldoImpago);
        Assert.True(resultadoB.PunitorioCalculado < resultadoA.PunitorioCalculado);
    }

    [Fact]
    public async Task CalcularCuotaAsync_ConHistorialIncompleto_NoDevuelveTotalAplicable()
    {
        var vencimiento = _reloj.HoyComercial.AddDays(-30);
        var cuota = await SeedCuotaAsync(1_000m, vencimiento.ToDateTime(TimeOnly.MinValue));
        await SeedConfiguracionAsync(10m, 20, diasGracia: 5, vigenteDesde: new DateOnly(2025, 1, 1));
        await SeedPagoAsync(cuota.Id, vencimiento.AddDays(10), 200m, importeAplicadoCuota: null, historialCompleto: false);

        var resultado = await _service.CalcularCuotaAsync(cuota.Id);

        Assert.Equal(EstadoResultadoPunitorio.HistorialIncompleto, resultado.EstadoCalculo);
        Assert.Null(resultado.PunitorioCalculado);
        Assert.Null(resultado.TotalPendienteEstimado);
        Assert.False(resultado.HistorialCompleto);
        Assert.NotNull(resultado.MotivoNoCalculo);
    }

    [Fact]
    public async Task CalcularCuotaAsync_Repetida_NoCambiaDbNiChangeTracker()
    {
        var cuota = await SeedCuotaAsync(1_000m, _reloj.HoyComercial.AddDays(-30).ToDateTime(TimeOnly.MinValue));
        await SeedConfiguracionAsync(10m, 20, diasGracia: 5, vigenteDesde: new DateOnly(2025, 1, 1));
        _context.ChangeTracker.Clear();

        for (var i = 0; i < 3; i++)
            await _service.CalcularCuotaAsync(cuota.Id);

        Assert.Empty(_context.ChangeTracker.Entries());
        Assert.Equal(0, await _context.PunitoriosAplicados.CountAsync());
        var cuotaBd = await _context.Cuotas.AsNoTracking().SingleAsync(c => c.Id == cuota.Id);
        Assert.Equal(0m, cuotaBd.MontoPunitorio); // el campo legacy no se toca
    }

    [Fact]
    public async Task CalcularCuotaAsync_CuotaInexistente_SeRechaza()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.CalcularCuotaAsync(999_999));
    }

    [Fact]
    public async Task CalcularCuotaAsync_SinFechaExplicita_UsaHoyComercialDelReloj()
    {
        var cuota = await SeedCuotaAsync(1_000m, _reloj.HoyComercial.AddDays(-6).ToDateTime(TimeOnly.MinValue));
        await SeedConfiguracionAsync(10m, 20, diasGracia: 5, vigenteDesde: new DateOnly(2025, 1, 1));

        var resultado = await _service.CalcularCuotaAsync(cuota.Id);

        Assert.Equal(_reloj.HoyComercial, resultado.FechaCalculo);
    }

    [Fact]
    public async Task CalcularCuotaAsync_ConPunitorioAplicadoPendiente_LoRefleja()
    {
        var cuota = await SeedCuotaAsync(10_000m, _reloj.HoyComercial.AddDays(-6).ToDateTime(TimeOnly.MinValue));
        await SeedConfiguracionAsync(10m, 20, diasGracia: 5, vigenteDesde: new DateOnly(2025, 1, 1));

        await _service.AplicarAsync(cuota.Id, Comando());
        _context.ChangeTracker.Clear();

        var resultado = await _service.CalcularCuotaAsync(cuota.Id);

        Assert.Equal(300.00m, resultado.PunitorioAplicadoPendiente);
    }

    // ===========================================================================================
    // Aplicación — AplicarAsync
    // ===========================================================================================

    [Fact]
    public async Task AplicarAsync_Valido_PersisteSnapshotExacto()
    {
        var cuota = await SeedCuotaAsync(10_000m, _reloj.HoyComercial.AddDays(-6).ToDateTime(TimeOnly.MinValue));
        var config = new ConfiguracionPunitorio
        {
            Porcentaje = 10m, PeriodoDias = 20, DiasGracia = 5, ProrrateoDiario = true,
            VigenteDesde = new DateOnly(2025, 1, 1), Activa = true
        };
        _context.ConfiguracionesPunitorio.Add(config);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var aplicado = await _service.AplicarAsync(cuota.Id, Comando("Confirmado por supervisor"));

        Assert.Equal(300.00m, aplicado.Importe);
        Assert.Equal(10_000m, aplicado.SaldoBase);
        Assert.Equal(6, aplicado.DiasComputados);
        Assert.Equal(EstadoPunitorioAplicado.Aplicado, aplicado.Estado);
        Assert.Equal("operador1", aplicado.UsuarioAplicacion);
        Assert.Equal("Confirmado por supervisor", aplicado.MotivoAplicacion);
        Assert.Equal(_reloj.HoyComercial, aplicado.FechaCalculo);
        Assert.Equal(_reloj.AhoraUtc, aplicado.FechaAplicacion);
        Assert.Equal(config.Id, aplicado.ConfiguracionPunitorioId);
        Assert.Equal(10m, aplicado.Porcentaje);
        Assert.Contains("\"punitorioRedondeado\":300", aplicado.DesgloseSnapshotJson.Replace(" ", ""), StringComparison.OrdinalIgnoreCase);

        var enBd = await _context.PunitoriosAplicados.AsNoTracking().SingleAsync(p => p.Id == aplicado.Id);
        Assert.Equal(300.00m, enBd.Importe);
    }

    [Fact]
    public async Task AplicarAsync_IgnoraElMontoPunitorioLegacyDeLaCuota()
    {
        var cuota = await SeedCuotaAsync(10_000m, _reloj.HoyComercial.AddDays(-6).ToDateTime(TimeOnly.MinValue));
        await SeedConfiguracionAsync(10m, 20, diasGracia: 5, vigenteDesde: new DateOnly(2025, 1, 1));

        // Campo legacy manipulado con un valor absurdo: AplicarAsync nunca debe leerlo como autoridad.
        var cuotaTracked = await _context.Cuotas.SingleAsync(c => c.Id == cuota.Id);
        cuotaTracked.MontoPunitorio = 99_999m;
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var aplicado = await _service.AplicarAsync(cuota.Id, Comando());

        Assert.Equal(300.00m, aplicado.Importe);
    }

    [Fact]
    public async Task AplicarAsync_DentroDeGracia_NoSePuedeAplicar()
    {
        var cuota = await SeedCuotaAsync(1_000m, _reloj.HoyComercial.AddDays(-5).ToDateTime(TimeOnly.MinValue));
        await SeedConfiguracionAsync(10m, 20, diasGracia: 5, vigenteDesde: new DateOnly(2025, 1, 1));

        var ex = await Assert.ThrowsAsync<PunitorioAplicadoRechazadoException>(
            () => _service.AplicarAsync(cuota.Id, Comando()));

        Assert.Equal(MotivoRechazoPunitorioAplicado.NoAplicable, ex.Motivo);
        Assert.Equal(EstadoResultadoPunitorio.DentroDeGracia, ex.EstadoCalculo);
        Assert.Equal(0, await _context.PunitoriosAplicados.CountAsync());
    }

    [Fact]
    public async Task AplicarAsync_SinConfiguracion_NoSePuedeAplicar()
    {
        var cuota = await SeedCuotaAsync(1_000m, _reloj.HoyComercial.AddDays(-30).ToDateTime(TimeOnly.MinValue));

        var ex = await Assert.ThrowsAsync<PunitorioAplicadoRechazadoException>(
            () => _service.AplicarAsync(cuota.Id, Comando()));

        Assert.Equal(MotivoRechazoPunitorioAplicado.NoAplicable, ex.Motivo);
        Assert.Equal(EstadoResultadoPunitorio.SinConfiguracion, ex.EstadoCalculo);
        Assert.Equal(0, await _context.PunitoriosAplicados.CountAsync());
    }

    [Fact]
    public async Task AplicarAsync_ConfiguracionInactiva_NoSePuedeAplicar()
    {
        var cuota = await SeedCuotaAsync(1_000m, _reloj.HoyComercial.AddDays(-30).ToDateTime(TimeOnly.MinValue));
        await SeedConfiguracionAsync(10m, 20, diasGracia: 5, vigenteDesde: new DateOnly(2025, 1, 1), activa: false);

        var ex = await Assert.ThrowsAsync<PunitorioAplicadoRechazadoException>(
            () => _service.AplicarAsync(cuota.Id, Comando()));

        Assert.Equal(EstadoResultadoPunitorio.ConfiguracionInactiva, ex.EstadoCalculo);
        Assert.Equal(0, await _context.PunitoriosAplicados.CountAsync());
    }

    [Fact]
    public async Task AplicarAsync_HistorialIncompleto_NoSePuedeAplicar()
    {
        var vencimiento = _reloj.HoyComercial.AddDays(-30);
        var cuota = await SeedCuotaAsync(1_000m, vencimiento.ToDateTime(TimeOnly.MinValue));
        await SeedConfiguracionAsync(10m, 20, diasGracia: 5, vigenteDesde: new DateOnly(2025, 1, 1));
        await SeedPagoAsync(cuota.Id, vencimiento.AddDays(10), 200m, importeAplicadoCuota: null, historialCompleto: false);

        var ex = await Assert.ThrowsAsync<PunitorioAplicadoRechazadoException>(
            () => _service.AplicarAsync(cuota.Id, Comando()));

        Assert.Equal(EstadoResultadoPunitorio.HistorialIncompleto, ex.EstadoCalculo);
        Assert.Equal(0, await _context.PunitoriosAplicados.CountAsync());
    }

    [Fact]
    public async Task AplicarAsync_SinSaldo_NoSePuedeAplicar()
    {
        var vencimiento = _reloj.HoyComercial.AddDays(-30);
        var cuota = await SeedCuotaAsync(1_000m, vencimiento.ToDateTime(TimeOnly.MinValue));
        await SeedConfiguracionAsync(10m, 20, diasGracia: 5, vigenteDesde: new DateOnly(2025, 1, 1));
        await SeedPagoAsync(cuota.Id, vencimiento.AddDays(-1), 1_000m); // salda antes de vencer

        var ex = await Assert.ThrowsAsync<PunitorioAplicadoRechazadoException>(
            () => _service.AplicarAsync(cuota.Id, Comando()));

        Assert.Equal(EstadoResultadoPunitorio.SinSaldo, ex.EstadoCalculo);
        Assert.Equal(0, await _context.PunitoriosAplicados.CountAsync());
    }

    [Fact]
    public async Task AplicarAsync_ConfiguracionActivaCero_NoCreaAplicacionDeCero()
    {
        var cuota = await SeedCuotaAsync(1_000m, _reloj.HoyComercial.AddDays(-30).ToDateTime(TimeOnly.MinValue));
        await SeedConfiguracionAsync(0m, 20, diasGracia: 5, vigenteDesde: new DateOnly(2025, 1, 1));

        // La consulta SÍ debe mostrar un cálculo válido de $0 (no es un estado de error).
        var consulta = await _service.CalcularCuotaAsync(cuota.Id);
        Assert.Equal(EstadoResultadoPunitorio.Calculado, consulta.EstadoCalculo);
        Assert.Equal(0m, consulta.PunitorioCalculado);

        var ex = await Assert.ThrowsAsync<PunitorioAplicadoRechazadoException>(
            () => _service.AplicarAsync(cuota.Id, Comando()));

        Assert.Equal(MotivoRechazoPunitorioAplicado.NoAplicable, ex.Motivo);
        Assert.Equal(0, await _context.PunitoriosAplicados.CountAsync());
    }

    [Fact]
    public async Task AplicarAsync_UsuarioYFechaSeObtienenDelServidor()
    {
        var relojFijo = new RelojComercialFake(new DateOnly(2026, 8, 1)) { AhoraUtc = new DateTime(2026, 8, 1, 15, 30, 0, DateTimeKind.Utc) };
        var currentUserServidor = new StubCurrentUserServicePunitorio { Username = "operador-servidor" };
        var servicio = CrearServicio(_context, relojFijo, currentUserServidor);
        var cuota = await SeedCuotaAsync(10_000m, relojFijo.HoyComercial.AddDays(-6).ToDateTime(TimeOnly.MinValue));
        await SeedConfiguracionAsync(10m, 20, diasGracia: 5, vigenteDesde: new DateOnly(2025, 1, 1));

        var aplicado = await servicio.AplicarAsync(cuota.Id, Comando());

        Assert.Equal(relojFijo.AhoraUtc, aplicado.FechaAplicacion);
        Assert.Equal(relojFijo.HoyComercial, aplicado.FechaCalculo);
        Assert.Equal("operador-servidor", aplicado.UsuarioAplicacion);
    }

    [Fact]
    public async Task AplicarAsync_MotivoVacio_SeRechaza()
    {
        var cuota = await SeedCuotaAsync(10_000m, _reloj.HoyComercial.AddDays(-6).ToDateTime(TimeOnly.MinValue));
        await SeedConfiguracionAsync(10m, 20, diasGracia: 5, vigenteDesde: new DateOnly(2025, 1, 1));

        var ex = await Assert.ThrowsAsync<PunitorioAplicadoRechazadoException>(
            () => _service.AplicarAsync(cuota.Id, Comando(motivo: "   ")));

        Assert.Equal(MotivoRechazoPunitorioAplicado.SolicitudInvalida, ex.Motivo);
        Assert.Equal(0, await _context.PunitoriosAplicados.CountAsync());
    }

    [Fact]
    public async Task AplicarAsync_CuotaInexistente_SeRechaza()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _service.AplicarAsync(999_999, Comando()));
    }

    [Fact]
    public async Task AplicarAsync_NoModificaMontoPagadoDeLaCuota()
    {
        var cuota = await SeedCuotaAsync(10_000m, _reloj.HoyComercial.AddDays(-6).ToDateTime(TimeOnly.MinValue));
        await SeedConfiguracionAsync(10m, 20, diasGracia: 5, vigenteDesde: new DateOnly(2025, 1, 1));

        await _service.AplicarAsync(cuota.Id, Comando());

        var cuotaBd = await _context.Cuotas.AsNoTracking().SingleAsync(c => c.Id == cuota.Id);
        Assert.Equal(0m, cuotaBd.MontoPagado);
        // PUN-ML7: AplicarAsync sí recalcula Estado (antes quedaba Pendiente sin importar la fecha,
        // un estado persistido stale) — sin pagos, una cuota vencida hace 6 días resuelve Vencida.
        Assert.Equal(EstadoCuota.Vencida, cuotaBd.Estado);
    }

    [Fact]
    public async Task AplicarAsync_NoRegistraCaja()
    {
        var cuota = await SeedCuotaAsync(10_000m, _reloj.HoyComercial.AddDays(-6).ToDateTime(TimeOnly.MinValue));
        await SeedConfiguracionAsync(10m, 20, diasGracia: 5, vigenteDesde: new DateOnly(2025, 1, 1));

        await _service.AplicarAsync(cuota.Id, Comando());

        Assert.Equal(0, await _context.MovimientosCaja.CountAsync());
    }

    // ===========================================================================================
    // Idempotencia / concurrencia
    // ===========================================================================================

    [Fact]
    public async Task AplicarAsync_DobleClick_NoDuplica()
    {
        var cuota = await SeedCuotaAsync(10_000m, _reloj.HoyComercial.AddDays(-6).ToDateTime(TimeOnly.MinValue));
        await SeedConfiguracionAsync(10m, 20, diasGracia: 5, vigenteDesde: new DateOnly(2025, 1, 1));

        await _service.AplicarAsync(cuota.Id, Comando());

        var ex = await Assert.ThrowsAsync<PunitorioAplicadoRechazadoException>(
            () => _service.AplicarAsync(cuota.Id, Comando()));

        Assert.Equal(MotivoRechazoPunitorioAplicado.Conflicto, ex.Motivo);
        Assert.Equal(1, await _context.PunitoriosAplicados.CountAsync());
    }

    [Fact]
    public async Task AplicarAsync_ReintentoIdenticoTrasExito_DevuelveConflicto()
    {
        var cuota = await SeedCuotaAsync(10_000m, _reloj.HoyComercial.AddDays(-6).ToDateTime(TimeOnly.MinValue));
        await SeedConfiguracionAsync(10m, 20, diasGracia: 5, vigenteDesde: new DateOnly(2025, 1, 1));

        var primera = await _service.AplicarAsync(cuota.Id, Comando());

        var ex = await Assert.ThrowsAsync<PunitorioAplicadoRechazadoException>(
            () => _service.AplicarAsync(cuota.Id, Comando()));

        Assert.Equal(MotivoRechazoPunitorioAplicado.Conflicto, ex.Motivo);
        var activas = await _context.PunitoriosAplicados
            .Where(p => p.CuotaId == cuota.Id && p.Estado == EstadoPunitorioAplicado.Aplicado)
            .ToListAsync();
        Assert.Single(activas);
        Assert.Equal(primera.Id, activas[0].Id);
    }

    [Fact]
    public async Task AplicarAsync_DosContextosConcurrentes_SoloUnaAplicacionPersiste()
    {
        var cuota = await SeedCuotaAsync(10_000m, _reloj.HoyComercial.AddDays(-6).ToDateTime(TimeOnly.MinValue));
        await SeedConfiguracionAsync(10m, 20, diasGracia: 5, vigenteDesde: new DateOnly(2025, 1, 1));
        _context.ChangeTracker.Clear();

        using var contextoA = CrearContexto();
        using var contextoB = CrearContexto();
        var relojA = new RelojComercialFake(_reloj.HoyComercial) { AhoraUtc = _reloj.AhoraUtc };
        var relojB = new RelojComercialFake(_reloj.HoyComercial) { AhoraUtc = _reloj.AhoraUtc };
        var servicioA = CrearServicio(contextoA, relojA, new StubCurrentUserServicePunitorio { Username = "operadorA" });
        var servicioB = CrearServicio(contextoB, relojB, new StubCurrentUserServicePunitorio { Username = "operadorB" });

        var intentoA = Task.Run(() => servicioA.AplicarAsync(cuota.Id, Comando()));
        var intentoB = Task.Run(() => servicioB.AplicarAsync(cuota.Id, Comando()));

        var resultados = await Task.WhenAll(
            EjecutarProtegidoAsync(intentoA),
            EjecutarProtegidoAsync(intentoB));

        Assert.Equal(1, resultados.Count(exito => exito));

        var activas = await _context.PunitoriosAplicados
            .Where(p => p.CuotaId == cuota.Id && p.Estado == EstadoPunitorioAplicado.Aplicado)
            .ToListAsync();
        Assert.Single(activas);
    }

    /// <summary>El intento perdedor puede fallar por el chequeo previo o por el índice de la base; ambos son rechazo.</summary>
    private static async Task<bool> EjecutarProtegidoAsync(Task<PunitorioAplicado> intento)
    {
        try
        {
            await intento;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    [Fact]
    public async Task AplicarAsync_SiCambioElSaldoDesdeLaConsulta_Recalcula()
    {
        var vencimiento = _reloj.HoyComercial.AddDays(-30);
        var cuota = await SeedCuotaAsync(1_000m, vencimiento.ToDateTime(TimeOnly.MinValue));
        await SeedConfiguracionAsync(10m, 20, diasGracia: 5, vigenteDesde: new DateOnly(2025, 1, 1));

        var previsualizado = await _service.CalcularCuotaAsync(cuota.Id);
        Assert.Equal(1_000m, previsualizado.SaldoImpago);

        // Entre la consulta y la aplicación, se registra un pago que reduce el saldo.
        await SeedPagoAsync(cuota.Id, vencimiento.AddDays(-2), 400m);

        var aplicado = await _service.AplicarAsync(cuota.Id, Comando());

        Assert.Equal(600m, aplicado.SaldoBase);
        Assert.True(aplicado.Importe < previsualizado.PunitorioCalculado);
    }

    [Fact]
    public async Task AplicarAsync_SiCambioLaConfiguracionDesdeLaConsulta_Recalcula()
    {
        var vencimiento = _reloj.HoyComercial.AddDays(-30);
        var cuota = await SeedCuotaAsync(1_000m, vencimiento.ToDateTime(TimeOnly.MinValue));
        await SeedConfiguracionAsync(10m, 20, diasGracia: 5, vigenteDesde: new DateOnly(2025, 1, 1));

        var previsualizado = await _service.CalcularCuotaAsync(cuota.Id);

        // Nueva versión (append-only) vigente antes del vencimiento: cambia la tasa aplicable a todo el período.
        await SeedConfiguracionAsync(20m, 20, diasGracia: 5, vigenteDesde: vencimiento.AddDays(-100));

        var aplicado = await _service.AplicarAsync(cuota.Id, Comando());

        Assert.True(aplicado.Importe > previsualizado.PunitorioCalculado);
    }

    [Fact]
    public async Task AplicarAsync_ConRowVersionDesactualizada_SeRechazaComoConflicto()
    {
        var cuota = await SeedCuotaAsync(10_000m, _reloj.HoyComercial.AddDays(-6).ToDateTime(TimeOnly.MinValue));
        await SeedConfiguracionAsync(10m, 20, diasGracia: 5, vigenteDesde: new DateOnly(2025, 1, 1));

        // RowVersion distinto al real de la cuota (ej.: el que el operador vio quedó desactualizado
        // porque otro proceso tocó la cuota). No se depende de que un UPDATE real regenere el token:
        // SQLite (proveedor de test) solo lo genera ValueGeneratedOnAdd, no en cada UPDATE — ver
        // AppDbContext.OnModelCreating. Alcanza con que difiera del real para ejercer la rama.
        var rowVersionDesactualizada = (byte[])cuota.RowVersion.Clone();
        rowVersionDesactualizada[0] = unchecked((byte)(rowVersionDesactualizada[0] + 1));

        var ex = await Assert.ThrowsAsync<PunitorioAplicadoRechazadoException>(
            () => _service.AplicarAsync(cuota.Id, Comando(rowVersion: rowVersionDesactualizada)));

        Assert.Equal(MotivoRechazoPunitorioAplicado.Conflicto, ex.Motivo);
        Assert.Equal(0, await _context.PunitoriosAplicados.CountAsync());
    }

    // ===========================================================================================
    // Anulación — AnularAsync
    // ===========================================================================================

    [Fact]
    public async Task AnularAsync_ConservaLaFilaYNoCuentaComoPendiente()
    {
        var cuota = await SeedCuotaAsync(10_000m, _reloj.HoyComercial.AddDays(-6).ToDateTime(TimeOnly.MinValue));
        await SeedConfiguracionAsync(10m, 20, diasGracia: 5, vigenteDesde: new DateOnly(2025, 1, 1));
        var aplicado = await _service.AplicarAsync(cuota.Id, Comando());
        _context.ChangeTracker.Clear();

        _currentUser.Username = "supervisor1";
        var anulado = await _service.AnularAsync(
            aplicado.Id, new PunitorioAnularComando { Motivo = "Error de carga" });

        Assert.Equal(EstadoPunitorioAplicado.Anulado, anulado.Estado);
        Assert.Equal("supervisor1", anulado.UsuarioAnulacion);
        Assert.Equal("Error de carga", anulado.MotivoAnulacion);
        Assert.Equal(_reloj.AhoraUtc, anulado.FechaAnulacion);

        var enBd = await _context.PunitoriosAplicados.AsNoTracking().SingleAsync(p => p.Id == aplicado.Id);
        Assert.Equal(EstadoPunitorioAplicado.Anulado, enBd.Estado); // fila conservada, no borrada
        Assert.Equal(300.00m, enBd.Importe); // los datos de cálculo no se tocan

        var pendiente = await _service.ObtenerPunitorioAplicadoPendienteAsync(cuota.Id);
        Assert.Equal(0m, pendiente);

        var consulta = await _service.CalcularCuotaAsync(cuota.Id);
        Assert.Equal(0m, consulta.PunitorioAplicadoPendiente);
        Assert.Equal(300.00m, consulta.PunitorioCalculado); // el cálculo al día sigue funcionando
    }

    [Fact]
    public async Task AnularAsync_MotivoVacio_SeRechaza()
    {
        var cuota = await SeedCuotaAsync(10_000m, _reloj.HoyComercial.AddDays(-6).ToDateTime(TimeOnly.MinValue));
        await SeedConfiguracionAsync(10m, 20, diasGracia: 5, vigenteDesde: new DateOnly(2025, 1, 1));
        var aplicado = await _service.AplicarAsync(cuota.Id, Comando());

        var ex = await Assert.ThrowsAsync<PunitorioAplicadoRechazadoException>(
            () => _service.AnularAsync(aplicado.Id, new PunitorioAnularComando { Motivo = "" }));

        Assert.Equal(MotivoRechazoPunitorioAplicado.SolicitudInvalida, ex.Motivo);
    }

    [Fact]
    public async Task AnularAsync_AplicacionInexistente_SeRechaza()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _service.AnularAsync(999_999, new PunitorioAnularComando { Motivo = "Motivo" }));
    }

    [Fact]
    public async Task AnularAsync_NoSePuedeAnularDosVeces()
    {
        var cuota = await SeedCuotaAsync(10_000m, _reloj.HoyComercial.AddDays(-6).ToDateTime(TimeOnly.MinValue));
        await SeedConfiguracionAsync(10m, 20, diasGracia: 5, vigenteDesde: new DateOnly(2025, 1, 1));
        var aplicado = await _service.AplicarAsync(cuota.Id, Comando());

        await _service.AnularAsync(aplicado.Id, new PunitorioAnularComando { Motivo = "Primera anulación" });

        var ex = await Assert.ThrowsAsync<PunitorioAplicadoRechazadoException>(
            () => _service.AnularAsync(aplicado.Id, new PunitorioAnularComando { Motivo = "Segunda anulación" }));

        Assert.Equal(MotivoRechazoPunitorioAplicado.Conflicto, ex.Motivo);
    }

    [Fact]
    public async Task AnularAsync_NoSePuedeAnularUnaPagada()
    {
        var cuota = await SeedCuotaAsync(10_000m, _reloj.HoyComercial.AddDays(-6).ToDateTime(TimeOnly.MinValue));
        await SeedConfiguracionAsync(10m, 20, diasGracia: 5, vigenteDesde: new DateOnly(2025, 1, 1));
        var aplicado = await _service.AplicarAsync(cuota.Id, Comando());

        // El productor real de Pagado es CreditoService (PUN-ML6, cobro): acá se simula el estado
        // directamente para aislar el guardia de AnularAsync sin depender de la cadena de cobro.
        var tracked = await _context.PunitoriosAplicados.SingleAsync(p => p.Id == aplicado.Id);
        tracked.Estado = EstadoPunitorioAplicado.Pagado;
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var ex = await Assert.ThrowsAsync<PunitorioAplicadoRechazadoException>(
            () => _service.AnularAsync(aplicado.Id, new PunitorioAnularComando { Motivo = "Intento inválido" }));

        Assert.Equal(MotivoRechazoPunitorioAplicado.Conflicto, ex.Motivo);
    }

    [Fact]
    public async Task AnularAsync_ConRowVersionDesactualizada_TrasOtraAnulacion_SeRechaza()
    {
        var cuota = await SeedCuotaAsync(10_000m, _reloj.HoyComercial.AddDays(-6).ToDateTime(TimeOnly.MinValue));
        await SeedConfiguracionAsync(10m, 20, diasGracia: 5, vigenteDesde: new DateOnly(2025, 1, 1));
        var aplicado = await _service.AplicarAsync(cuota.Id, Comando());
        var rowVersionVieja = (byte[])aplicado.RowVersion.Clone();
        _context.ChangeTracker.Clear();

        // Otra transacción anula primero (cambia RowVersion en la base).
        _currentUser.Username = "supervisor1";
        await _service.AnularAsync(aplicado.Id, new PunitorioAnularComando { Motivo = "Anulación real" });
        _context.ChangeTracker.Clear();

        // Un segundo operador, con el RowVersion viejo (visto antes de la primera anulación), reintenta.
        _currentUser.Username = "supervisor2";
        await Assert.ThrowsAsync<PunitorioAplicadoRechazadoException>(
            () => _service.AnularAsync(
                aplicado.Id,
                new PunitorioAnularComando { Motivo = "Anulación tardía", RowVersionEsperado = rowVersionVieja }));

        var enBd = await _context.PunitoriosAplicados.AsNoTracking().SingleAsync(p => p.Id == aplicado.Id);
        Assert.Equal("supervisor1", enBd.UsuarioAnulacion); // el rollback no pisó la primera anulación válida
        Assert.Equal("Anulación real", enBd.MotivoAnulacion);
    }

    [Fact]
    public async Task AnularAsync_DosContextosConcurrentes_SoloUnaAnulacionPersiste()
    {
        var cuota = await SeedCuotaAsync(10_000m, _reloj.HoyComercial.AddDays(-6).ToDateTime(TimeOnly.MinValue));
        await SeedConfiguracionAsync(10m, 20, diasGracia: 5, vigenteDesde: new DateOnly(2025, 1, 1));
        var aplicado = await _service.AplicarAsync(cuota.Id, Comando());
        _context.ChangeTracker.Clear();

        using var contextoA = CrearContexto();
        using var contextoB = CrearContexto();
        var servicioA = CrearServicio(contextoA, _reloj, new StubCurrentUserServicePunitorio { Username = "supervisorA" });
        var servicioB = CrearServicio(contextoB, _reloj, new StubCurrentUserServicePunitorio { Username = "supervisorB" });

        var intentoA = Task.Run(() => servicioA.AnularAsync(
            aplicado.Id, new PunitorioAnularComando { Motivo = "Anulación A" }));
        var intentoB = Task.Run(() => servicioB.AnularAsync(
            aplicado.Id, new PunitorioAnularComando { Motivo = "Anulación B" }));

        var resultados = await Task.WhenAll(
            EjecutarProtegidoAsync(intentoA),
            EjecutarProtegidoAsync(intentoB));

        Assert.Equal(1, resultados.Count(exito => exito));

        var enBd = await _context.PunitoriosAplicados.AsNoTracking().SingleAsync(p => p.Id == aplicado.Id);
        Assert.Equal(EstadoPunitorioAplicado.Anulado, enBd.Estado);
        Assert.True(enBd.MotivoAnulacion is "Anulación A" or "Anulación B");
    }

    [Fact]
    public async Task PunitorioAplicadoPendiente_SumaSoloAplicacionesActivas()
    {
        var vencimiento = _reloj.HoyComercial.AddDays(-30);
        var cuota = await SeedCuotaAsync(2_000m, vencimiento.ToDateTime(TimeOnly.MinValue));
        await SeedConfiguracionAsync(10m, 20, diasGracia: 5, vigenteDesde: new DateOnly(2025, 1, 1));

        var aplicado = await _service.AplicarAsync(cuota.Id, Comando());
        Assert.Equal(aplicado.Importe, await _service.ObtenerPunitorioAplicadoPendienteAsync(cuota.Id));

        await _service.AnularAsync(aplicado.Id, new PunitorioAnularComando { Motivo = "Ajuste" });
        Assert.Equal(0m, await _service.ObtenerPunitorioAplicadoPendienteAsync(cuota.Id));
    }

    // ===========================================================================================
    // Autorización — actor y permiso resueltos por el servidor (corrección post-cierre de PUN-ML5)
    // ===========================================================================================

    [Fact]
    public async Task AplicarAsync_UsuarioNoAutenticado_SeRechaza()
    {
        var cuota = await SeedCuotaAsync(10_000m, _reloj.HoyComercial.AddDays(-6).ToDateTime(TimeOnly.MinValue));
        await SeedConfiguracionAsync(10m, 20, diasGracia: 5, vigenteDesde: new DateOnly(2025, 1, 1));
        _currentUser.Authenticated = false;

        var ex = await Assert.ThrowsAsync<PunitorioAplicadoRechazadoException>(
            () => _service.AplicarAsync(cuota.Id, Comando()));

        Assert.Equal(MotivoRechazoPunitorioAplicado.NoAutorizado, ex.Motivo);
        Assert.Equal(0, await _context.PunitoriosAplicados.CountAsync());
    }

    [Fact]
    public async Task AplicarAsync_UsuarioAutenticadoSinPermiso_SeRechaza()
    {
        var cuota = await SeedCuotaAsync(10_000m, _reloj.HoyComercial.AddDays(-6).ToDateTime(TimeOnly.MinValue));
        await SeedConfiguracionAsync(10m, 20, diasGracia: 5, vigenteDesde: new DateOnly(2025, 1, 1));
        _currentUser.Permisos.Remove(("cobranzas", "applyfine"));

        var ex = await Assert.ThrowsAsync<PunitorioAplicadoRechazadoException>(
            () => _service.AplicarAsync(cuota.Id, Comando()));

        Assert.Equal(MotivoRechazoPunitorioAplicado.NoAutorizado, ex.Motivo);
        Assert.Equal(0, await _context.PunitoriosAplicados.CountAsync());
    }

    [Fact]
    public async Task AplicarAsync_UsuarioConPermiso_PuedeAplicarYElActorPersistidoCoincideConElAutenticado()
    {
        var cuota = await SeedCuotaAsync(10_000m, _reloj.HoyComercial.AddDays(-6).ToDateTime(TimeOnly.MinValue));
        await SeedConfiguracionAsync(10m, 20, diasGracia: 5, vigenteDesde: new DateOnly(2025, 1, 1));
        _currentUser.Username = "operador-real";

        var aplicado = await _service.AplicarAsync(cuota.Id, Comando());

        Assert.Equal("operador-real", aplicado.UsuarioAplicacion);
    }

    [Fact]
    public async Task AplicarAsync_RechazoDeAutorizacion_NoModificaNadaDeLaCuotaNiCaja()
    {
        var cuota = await SeedCuotaAsync(10_000m, _reloj.HoyComercial.AddDays(-6).ToDateTime(TimeOnly.MinValue));
        await SeedConfiguracionAsync(10m, 20, diasGracia: 5, vigenteDesde: new DateOnly(2025, 1, 1));
        _currentUser.Authenticated = false;

        await Assert.ThrowsAsync<PunitorioAplicadoRechazadoException>(
            () => _service.AplicarAsync(cuota.Id, Comando()));

        Assert.Equal(0, await _context.PunitoriosAplicados.CountAsync());
        Assert.Equal(0, await _context.MovimientosCaja.CountAsync());
        var cuotaBd = await _context.Cuotas.AsNoTracking().SingleAsync(c => c.Id == cuota.Id);
        Assert.Equal(0m, cuotaBd.MontoPagado);
        Assert.Equal(0m, cuotaBd.MontoPunitorio);
        Assert.Equal(EstadoCuota.Pendiente, cuotaBd.Estado);
    }

    [Fact]
    public async Task AnularAsync_UsuarioSinPermisoDeAnulacion_NoPuedeAnular()
    {
        var cuota = await SeedCuotaAsync(10_000m, _reloj.HoyComercial.AddDays(-6).ToDateTime(TimeOnly.MinValue));
        await SeedConfiguracionAsync(10m, 20, diasGracia: 5, vigenteDesde: new DateOnly(2025, 1, 1));
        var aplicado = await _service.AplicarAsync(cuota.Id, Comando());

        // El mismo usuario que pudo aplicar (tiene applyfine) pierde el permiso de anular.
        _currentUser.Permisos.Remove(("cobranzas", "revertfine"));

        var ex = await Assert.ThrowsAsync<PunitorioAplicadoRechazadoException>(
            () => _service.AnularAsync(aplicado.Id, new PunitorioAnularComando { Motivo = "Motivo" }));

        Assert.Equal(MotivoRechazoPunitorioAplicado.NoAutorizado, ex.Motivo);
        var enBd = await _context.PunitoriosAplicados.AsNoTracking().SingleAsync(p => p.Id == aplicado.Id);
        Assert.Equal(EstadoPunitorioAplicado.Aplicado, enBd.Estado); // no se tocó
    }

    [Fact]
    public async Task AnularAsync_UsuarioConPermisoDeAnulacion_PuedeAnular()
    {
        var cuota = await SeedCuotaAsync(10_000m, _reloj.HoyComercial.AddDays(-6).ToDateTime(TimeOnly.MinValue));
        await SeedConfiguracionAsync(10m, 20, diasGracia: 5, vigenteDesde: new DateOnly(2025, 1, 1));
        var aplicado = await _service.AplicarAsync(cuota.Id, Comando());

        var anulado = await _service.AnularAsync(aplicado.Id, new PunitorioAnularComando { Motivo = "Motivo" });

        Assert.Equal(EstadoPunitorioAplicado.Anulado, anulado.Estado);
    }

    [Fact]
    public async Task AplicarAsync_UsuarioConPermisoDeAplicarPeroNoDeAnular_NoPuedeAnular()
    {
        var cuota = await SeedCuotaAsync(10_000m, _reloj.HoyComercial.AddDays(-6).ToDateTime(TimeOnly.MinValue));
        await SeedConfiguracionAsync(10m, 20, diasGracia: 5, vigenteDesde: new DateOnly(2025, 1, 1));
        _currentUser.Permisos.Remove(("cobranzas", "revertfine"));

        // Con solo applyfine, sigue pudiendo aplicar...
        var aplicado = await _service.AplicarAsync(cuota.Id, Comando());
        Assert.Equal(EstadoPunitorioAplicado.Aplicado, aplicado.Estado);

        // ...pero no anular.
        var ex = await Assert.ThrowsAsync<PunitorioAplicadoRechazadoException>(
            () => _service.AnularAsync(aplicado.Id, new PunitorioAnularComando { Motivo = "Motivo" }));
        Assert.Equal(MotivoRechazoPunitorioAplicado.NoAutorizado, ex.Motivo);
    }

    /// <summary>
    /// El único dato de identidad que puede llegar del navegador es el "Username" de este stub de
    /// test — que simula el HttpContext.User real, nunca el request. A nivel de contrato, los DTOs
    /// que sí cruzan la frontera HTTP (comando) no exponen ningún campo de usuario/permiso: no hay
    /// forma de que un payload falsificado altere el actor persistido porque no existe el campo.
    /// </summary>
    [Fact]
    public void PunitorioAplicarComando_NoExponeCamposDeIdentidadNiPermiso()
    {
        var nombresDeCampos = typeof(PunitorioAplicarComando).GetProperties()
            .Select(p => p.Name.ToLowerInvariant())
            .ToList();

        Assert.DoesNotContain(nombresDeCampos, n =>
            n.Contains("usuario") || n.Contains("user") || n.Contains("permiso") || n.Contains("autoriz"));
    }

    [Fact]
    public void PunitorioAnularComando_NoExponeCamposDeIdentidadNiPermiso()
    {
        var nombresDeCampos = typeof(PunitorioAnularComando).GetProperties()
            .Select(p => p.Name.ToLowerInvariant())
            .ToList();

        Assert.DoesNotContain(nombresDeCampos, n =>
            n.Contains("usuario") || n.Contains("user") || n.Contains("permiso") || n.Contains("autoriz"));
    }

    /// <summary>
    /// Ningún caller — interno o externo — puede eludir la autorización pasando un string arbitrario:
    /// la firma pública de ambos métodos no acepta ningún parámetro de tipo <c>string</c> aparte de
    /// los que ya viajan dentro de los DTOs de comando (verificado arriba). El actor y el permiso
    /// solo pueden llegar desde <see cref="ICurrentUserService"/>, inyectado por el contenedor de DI.
    /// </summary>
    [Fact]
    public void AplicarYAnularAsync_NoAceptanUnParametroStringDeActorOPermiso()
    {
        var metodoAplicar = typeof(IPunitorioService).GetMethod(nameof(IPunitorioService.AplicarAsync))!;
        var metodoAnular = typeof(IPunitorioService).GetMethod(nameof(IPunitorioService.AnularAsync))!;

        Assert.DoesNotContain(metodoAplicar.GetParameters(), p => p.ParameterType == typeof(string));
        Assert.DoesNotContain(metodoAnular.GetParameters(), p => p.ParameterType == typeof(string));
    }

    // ===========================================================================================
    // PUN-ML6 — Progreso de cobro (fuente: PagoCuota.PunitorioAplicadoId)
    // ===========================================================================================

    /// <summary>
    /// Siembra un pago efectivo atribuido a una aplicación (como lo haría CreditoService al cobrar,
    /// PUN-ML6), sin pasar por el flujo de cobro completo: aísla el helper de progreso.
    /// </summary>
    private async Task SeedPagoPunitorioAsync(
        int cuotaId, int punitorioAplicadoId, decimal importe, EstadoPagoCuota estado = EstadoPagoCuota.Aplicado)
    {
        _context.PagosCuota.Add(new PagoCuota
        {
            CuotaId = cuotaId,
            FechaPagoComercial = _reloj.HoyComercial,
            ImporteTotal = importe,
            ImporteAplicadoCuota = 0m,
            ImporteAplicadoPunitorio = importe,
            PunitorioAplicadoId = punitorioAplicadoId,
            Origen = OrigenPagoCuota.RegistradoPorSistema,
            Estado = estado,
            HistorialCompleto = true
        });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
    }

    [Fact]
    public async Task ObtenerPunitorioAplicadoPendienteAsync_NetaLosPagosEfectivosYaAtribuidos()
    {
        var vencimiento = _reloj.HoyComercial.AddDays(-6);
        var cuota = await SeedCuotaAsync(10_000m, vencimiento.ToDateTime(TimeOnly.MinValue));
        await SeedConfiguracionAsync(10m, 20, diasGracia: 5, vigenteDesde: new DateOnly(2025, 1, 1));
        var aplicado = await _service.AplicarAsync(cuota.Id, Comando());

        await SeedPagoPunitorioAsync(cuota.Id, aplicado.Id, 100m);

        var pendiente = await _service.ObtenerPunitorioAplicadoPendienteAsync(cuota.Id);
        Assert.Equal(200m, pendiente); // 300 aplicado - 100 ya cobrado
    }

    [Fact]
    public async Task ObtenerPunitorioAplicadoPendienteAsync_IgnoraPagosAnulados()
    {
        var vencimiento = _reloj.HoyComercial.AddDays(-6);
        var cuota = await SeedCuotaAsync(10_000m, vencimiento.ToDateTime(TimeOnly.MinValue));
        await SeedConfiguracionAsync(10m, 20, diasGracia: 5, vigenteDesde: new DateOnly(2025, 1, 1));
        var aplicado = await _service.AplicarAsync(cuota.Id, Comando());

        await SeedPagoPunitorioAsync(cuota.Id, aplicado.Id, 100m, EstadoPagoCuota.Anulado);

        var pendiente = await _service.ObtenerPunitorioAplicadoPendienteAsync(cuota.Id);
        Assert.Equal(300m, pendiente); // un pago anulado no cuenta como efectivamente cobrado
    }

    [Fact]
    public async Task ObtenerAplicacionActivaConProgresoAsync_DevuelveMontoPagadoYPendienteCorrectos()
    {
        var vencimiento = _reloj.HoyComercial.AddDays(-6);
        var cuota = await SeedCuotaAsync(10_000m, vencimiento.ToDateTime(TimeOnly.MinValue));
        await SeedConfiguracionAsync(10m, 20, diasGracia: 5, vigenteDesde: new DateOnly(2025, 1, 1));
        var aplicado = await _service.AplicarAsync(cuota.Id, Comando());
        await SeedPagoPunitorioAsync(cuota.Id, aplicado.Id, 120m);

        var progreso = await _service.ObtenerAplicacionActivaConProgresoAsync(cuota.Id);

        Assert.NotNull(progreso);
        Assert.Equal(aplicado.Id, progreso!.Aplicacion.Id);
        Assert.Equal(120m, progreso.MontoPagado);
        Assert.Equal(180m, progreso.MontoPendiente);
    }

    [Fact]
    public async Task ObtenerAplicacionActivaConProgresoAsync_SinAplicacionActiva_DevuelveNull()
    {
        var cuota = await SeedCuotaAsync(1_000m, _reloj.HoyComercial.AddDays(10).ToDateTime(TimeOnly.MinValue));

        var progreso = await _service.ObtenerAplicacionActivaConProgresoAsync(cuota.Id);

        Assert.Null(progreso);
    }

    [Fact]
    public async Task AnularAsync_NoSePuedeAnularUnaAplicacionConPagosParciales()
    {
        var vencimiento = _reloj.HoyComercial.AddDays(-6);
        var cuota = await SeedCuotaAsync(10_000m, vencimiento.ToDateTime(TimeOnly.MinValue));
        await SeedConfiguracionAsync(10m, 20, diasGracia: 5, vigenteDesde: new DateOnly(2025, 1, 1));
        var aplicado = await _service.AplicarAsync(cuota.Id, Comando());

        // Pago parcial: cubre 100 de los 300 (la aplicación sigue Estado=Aplicado, no llega a Pagado).
        await SeedPagoPunitorioAsync(cuota.Id, aplicado.Id, 100m);

        var ex = await Assert.ThrowsAsync<PunitorioAplicadoRechazadoException>(
            () => _service.AnularAsync(aplicado.Id, new PunitorioAnularComando { Motivo = "Intento inválido" }));

        Assert.Equal(MotivoRechazoPunitorioAplicado.Conflicto, ex.Motivo);

        var enBd = await _context.PunitoriosAplicados.AsNoTracking().SingleAsync(p => p.Id == aplicado.Id);
        Assert.Equal(EstadoPunitorioAplicado.Aplicado, enBd.Estado); // no se tocó
    }

    // ===========================================================================================
    // PUN-ML6 — Aplicaciones sucesivas (diferencial tras una aplicación pagada)
    // ===========================================================================================

    [Fact]
    public async Task AplicarAsync_ConAplicacionPagadaYMasDiasTranscurridos_AplicaSoloElDiferencial()
    {
        var vencimiento = _reloj.HoyComercial.AddDays(-6);
        var cuota = await SeedCuotaAsync(10_000m, vencimiento.ToDateTime(TimeOnly.MinValue));
        await SeedConfiguracionAsync(10m, 20, diasGracia: 5, vigenteDesde: new DateOnly(2025, 1, 1));

        var primera = await _service.AplicarAsync(cuota.Id, Comando());
        Assert.Equal(300.00m, primera.Importe);

        var trackedPrimera = await _context.PunitoriosAplicados.SingleAsync(p => p.Id == primera.Id);
        trackedPrimera.Estado = EstadoPunitorioAplicado.Pagado;
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        // Avanza 10 días más (16 días de atraso en total): teórico acumulado = 10000*0.10*16/20 = 800.
        _reloj.HoyComercial = vencimiento.AddDays(16);

        var segunda = await _service.AplicarAsync(cuota.Id, Comando("Diferencial por más días"));

        Assert.Equal(500.00m, segunda.Importe); // 800 teórico - 300 ya aplicado
        Assert.NotEqual(primera.Id, segunda.Id);

        var todas = await _context.PunitoriosAplicados.Where(p => p.CuotaId == cuota.Id).ToListAsync();
        Assert.Equal(2, todas.Count);
    }

    [Fact]
    public async Task AplicarAsync_ConAplicacionPagadaYMismoCalculo_RechazaComoNadaNuevoParaAplicar()
    {
        var vencimiento = _reloj.HoyComercial.AddDays(-6);
        var cuota = await SeedCuotaAsync(10_000m, vencimiento.ToDateTime(TimeOnly.MinValue));
        await SeedConfiguracionAsync(10m, 20, diasGracia: 5, vigenteDesde: new DateOnly(2025, 1, 1));

        var primera = await _service.AplicarAsync(cuota.Id, Comando());

        var tracked = await _context.PunitoriosAplicados.SingleAsync(p => p.Id == primera.Id);
        tracked.Estado = EstadoPunitorioAplicado.Pagado;
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        // Mismo día, mismo cálculo: nada nuevo que aplicar.
        var ex = await Assert.ThrowsAsync<PunitorioAplicadoRechazadoException>(
            () => _service.AplicarAsync(cuota.Id, Comando()));

        Assert.Equal(MotivoRechazoPunitorioAplicado.NoAplicable, ex.Motivo);
        Assert.Equal(1, await _context.PunitoriosAplicados.CountAsync());
    }

    [Fact]
    public async Task AplicarAsync_ConAplicacionAnuladaPrevia_NoSeDescuentaDelNuevoCalculo()
    {
        var vencimiento = _reloj.HoyComercial.AddDays(-6);
        var cuota = await SeedCuotaAsync(10_000m, vencimiento.ToDateTime(TimeOnly.MinValue));
        await SeedConfiguracionAsync(10m, 20, diasGracia: 5, vigenteDesde: new DateOnly(2025, 1, 1));

        var primera = await _service.AplicarAsync(cuota.Id, Comando());
        await _service.AnularAsync(primera.Id, new PunitorioAnularComando { Motivo = "Error de carga" });
        _context.ChangeTracker.Clear();

        // Mismo día, mismo teórico (300): al no descontarse la anulada, la nueva aplicación es por
        // el importe TOTAL, no un diferencial.
        var segunda = await _service.AplicarAsync(cuota.Id, Comando("Reaplicación tras anular"));

        Assert.Equal(300.00m, segunda.Importe);
    }

    [Fact]
    public async Task AplicarAsync_ConfiguracionRetroactivaReduceElCalculoPorDebajoDeLoAplicado_RechazaComoConflicto()
    {
        var vencimiento = _reloj.HoyComercial.AddDays(-6);
        var cuota = await SeedCuotaAsync(10_000m, vencimiento.ToDateTime(TimeOnly.MinValue));
        await SeedConfiguracionAsync(10m, 20, diasGracia: 5, vigenteDesde: new DateOnly(2025, 1, 1));

        var primera = await _service.AplicarAsync(cuota.Id, Comando());
        Assert.Equal(300.00m, primera.Importe);

        var tracked = await _context.PunitoriosAplicados.SingleAsync(p => p.Id == primera.Id);
        tracked.Estado = EstadoPunitorioAplicado.Pagado;
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        // Configuración retroactiva con porcentaje menor, vigente ANTES del vencimiento: el
        // teórico recalculado (60) queda por debajo de lo ya aplicado (300). No se genera un
        // importe negativo — se rechaza como conflicto explícito, sin borrar historia.
        await SeedConfiguracionAsync(2m, 20, diasGracia: 5, vigenteDesde: vencimiento.AddDays(-10));

        var ex = await Assert.ThrowsAsync<PunitorioAplicadoRechazadoException>(
            () => _service.AplicarAsync(cuota.Id, Comando("Recalcular tras ajuste retroactivo")));

        Assert.Equal(MotivoRechazoPunitorioAplicado.Conflicto, ex.Motivo);
        Assert.Equal(1, await _context.PunitoriosAplicados.CountAsync());

        var enBd = await _context.PunitoriosAplicados.SingleAsync();
        Assert.Equal(EstadoPunitorioAplicado.Pagado, enBd.Estado); // la histórica no se tocó
    }

    [Fact]
    public async Task AplicarAsync_Sucesiva_SnapshotIncluyeTeoricoPrevioYNuevo()
    {
        var vencimiento = _reloj.HoyComercial.AddDays(-6);
        var cuota = await SeedCuotaAsync(10_000m, vencimiento.ToDateTime(TimeOnly.MinValue));
        await SeedConfiguracionAsync(10m, 20, diasGracia: 5, vigenteDesde: new DateOnly(2025, 1, 1));

        var primera = await _service.AplicarAsync(cuota.Id, Comando());
        var tracked = await _context.PunitoriosAplicados.SingleAsync(p => p.Id == primera.Id);
        tracked.Estado = EstadoPunitorioAplicado.Pagado;
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        _reloj.HoyComercial = vencimiento.AddDays(16);
        var segunda = await _service.AplicarAsync(cuota.Id, Comando());

        var snapshot = JsonSerializer.Deserialize<PunitorioAplicadoSnapshot>(segunda.DesgloseSnapshotJson);
        Assert.NotNull(snapshot);
        Assert.Equal(800.00m, snapshot!.PunitorioTeoricoAcumulado);
        Assert.Equal(300.00m, snapshot.ImportePreviamenteAplicado);
        Assert.Equal(500.00m, snapshot.ImporteNuevoAplicado);
    }

    // ===========================================================================================
    // PUN-ML7 — AplicarAsync/AnularAsync recalculan y persisten Cuota.Estado (y FechaPago)
    // ===========================================================================================

    [Fact]
    public async Task AplicarAsync_SobreCuotaPagada_LaPasaAParcialYLimpiaFechaPago()
    {
        // Cuota vencida hace 30 días, pagada en su totalidad 10 días después del vencimiento: el
        // calculador sigue devolviendo un punitorio positivo por la ventana [vencimiento, pago) aun
        // con saldo actual cero — "capital cero" no implica "sin punitorio pendiente por aplicar".
        var vencimiento = _reloj.HoyComercial.AddDays(-30);
        var cuota = await SeedCuotaAsync(1_000m, vencimiento.ToDateTime(TimeOnly.MinValue));
        await SeedConfiguracionAsync(10m, 20, diasGracia: 5, vigenteDesde: new DateOnly(2025, 1, 1));

        var fechaPago = vencimiento.AddDays(10);
        await SeedPagoAsync(cuota.Id, fechaPago, 1_000m);

        var tracked = await _context.Cuotas.SingleAsync(c => c.Id == cuota.Id);
        tracked.MontoPagado = 1_000m;
        tracked.Estado = EstadoCuota.Pagada;
        tracked.FechaPago = fechaPago.ToDateTime(TimeOnly.MinValue);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var aplicado = await _service.AplicarAsync(cuota.Id, Comando());

        Assert.True(aplicado.Importe > 0m);

        var cuotaBd = await _context.Cuotas.AsNoTracking().SingleAsync(c => c.Id == cuota.Id);
        // Capital cero + punitorio aplicado pendiente no está pagada (contrato congelado PUN-ML6/7).
        Assert.Equal(EstadoCuota.Parcial, cuotaBd.Estado);
        // FechaPago ya no representa "cuándo quedó saldada": se limpia.
        Assert.Null(cuotaBd.FechaPago);
        // El componente de capital de la cuota no se toca al aplicar un punitorio.
        Assert.Equal(1_000m, cuotaBd.MontoPagado);
    }

    [Fact]
    public async Task AnularAsync_UnicaAplicacionSinPagos_RecalculaLaCuotaAPagada()
    {
        // Cuota con capital saldado pero mantenida Parcial exclusivamente por un punitorio aplicado
        // pendiente (mismo estado que dejaría AplicarAsync sobre una cuota pagada, test de arriba).
        var cuota = await SeedCuotaAsync(1_000m, _reloj.HoyComercial.AddDays(-30).ToDateTime(TimeOnly.MinValue));
        var tracked = await _context.Cuotas.SingleAsync(c => c.Id == cuota.Id);
        tracked.MontoPagado = 1_000m;
        tracked.Estado = EstadoCuota.Parcial;
        await _context.SaveChangesAsync();

        var aplicado = new PunitorioAplicado
        {
            CuotaId = cuota.Id,
            FechaCalculo = _reloj.HoyComercial,
            SaldoBase = 1_000m,
            DiasComputados = 5,
            Importe = 50m,
            Estado = EstadoPunitorioAplicado.Aplicado,
            FechaAplicacion = _reloj.AhoraUtc,
            MotivoAplicacion = "Seed de test",
            UsuarioAplicacion = "operador1"
        };
        _context.PunitoriosAplicados.Add(aplicado);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var anulado = await _service.AnularAsync(aplicado.Id, new PunitorioAnularComando { Motivo = "Aplicación errónea" });

        Assert.Equal(EstadoPunitorioAplicado.Anulado, anulado.Estado);

        var cuotaBd = await _context.Cuotas.AsNoTracking().SingleAsync(c => c.Id == cuota.Id);
        // Anular una aplicación no pagada permite recalcular el estado (contrato PUN-ML7).
        Assert.Equal(EstadoCuota.Pagada, cuotaBd.Estado);
        Assert.Equal(_reloj.HoyComercial.ToDateTime(TimeOnly.MinValue), cuotaBd.FechaPago);
    }

    [Fact]
    public async Task AnularAsync_ConPagosParciales_NoRecalculaLaCuota()
    {
        // Camino ya rechazado por AnularAsync (AnularAsync_NoSePuedeAnularUnaAplicacionConPagosParciales):
        // acá se confirma además que, al rechazar, Cuota.Estado tampoco se toca.
        var cuota = await SeedCuotaAsync(10_000m, _reloj.HoyComercial.AddDays(-6).ToDateTime(TimeOnly.MinValue));
        await SeedConfiguracionAsync(10m, 20, diasGracia: 5, vigenteDesde: new DateOnly(2025, 1, 1));
        var aplicado = await _service.AplicarAsync(cuota.Id, Comando());

        await SeedPagoAsync(
            cuota.Id, _reloj.HoyComercial, importeTotal: 100m, importeAplicadoCuota: 0m);
        var pagoParcial = await _context.PagosCuota.SingleAsync(p => p.CuotaId == cuota.Id);
        pagoParcial.ImporteAplicadoPunitorio = 100m;
        pagoParcial.PunitorioAplicadoId = aplicado.Id;
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var estadoAntes = (await _context.Cuotas.AsNoTracking().SingleAsync(c => c.Id == cuota.Id)).Estado;

        await Assert.ThrowsAsync<PunitorioAplicadoRechazadoException>(
            () => _service.AnularAsync(aplicado.Id, new PunitorioAnularComando { Motivo = "Intento inválido" }));

        var cuotaBd = await _context.Cuotas.AsNoTracking().SingleAsync(c => c.Id == cuota.Id);
        Assert.Equal(estadoAntes, cuotaBd.Estado);
    }
}
