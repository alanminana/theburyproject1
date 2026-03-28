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
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Integration;

// ---------------------------------------------------------------------------
// Stub de INotificacionService — sin dependencia de Moq
// ---------------------------------------------------------------------------
file sealed class StubNotificacionService : INotificacionService
{
    public Task<Notificacion> CrearNotificacionAsync(CrearNotificacionViewModel model)
        => Task.FromResult(new Notificacion());

    public Task CrearNotificacionParaUsuarioAsync(string usuario, TipoNotificacion tipo, string titulo, string mensaje, string? url = null, PrioridadNotificacion prioridad = PrioridadNotificacion.Media)
        => Task.CompletedTask;

    public Task CrearNotificacionParaRolAsync(string rol, TipoNotificacion tipo, string titulo, string mensaje, string? url = null, PrioridadNotificacion prioridad = PrioridadNotificacion.Media)
        => Task.CompletedTask;

    public Task<List<NotificacionViewModel>> ObtenerNotificacionesUsuarioAsync(string usuario, bool soloNoLeidas = false, int limite = 50)
        => Task.FromResult(new List<NotificacionViewModel>());

    public Task<int> ObtenerCantidadNoLeidasAsync(string usuario)
        => Task.FromResult(0);

    public Task<Notificacion?> ObtenerNotificacionPorIdAsync(int id)
        => Task.FromResult<Notificacion?>(null);

    public Task MarcarComoLeidaAsync(int notificacionId, string usuario, byte[]? rowVersion = null)
        => Task.CompletedTask;

    public Task MarcarTodasComoLeidasAsync(string usuario)
        => Task.CompletedTask;

    public Task EliminarNotificacionAsync(int id, string usuario, byte[]? rowVersion = null)
        => Task.CompletedTask;

    public Task LimpiarNotificacionesAntiguasAsync(int diasAntiguedad = 30)
        => Task.CompletedTask;

    public Task<ListaNotificacionesViewModel> ObtenerResumenNotificacionesAsync(string usuario)
        => Task.FromResult(new ListaNotificacionesViewModel());
}

/// <summary>
/// Tests de integración para CajaService.
/// Cubren AbrirCajaAsync, RegistrarMovimientoAsync, CalcularSaldoActualAsync
/// y CerrarCajaAsync: reglas de negocio, validaciones, cálculo de arqueo,
/// tolerancia de diferencia y requisito de justificación.
/// </summary>
public class CajaServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly CajaService _service;

    public CajaServiceTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        var mapper = new MapperConfiguration(
                cfg => cfg.AddProfile<MappingProfile>(),
                NullLoggerFactory.Instance)
            .CreateMapper();

        _service = new CajaService(
            _context,
            mapper,
            NullLogger<CajaService>.Instance,
            new StubNotificacionService());
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<Caja> SeedCajaAsync(bool activa = true, EstadoCaja estado = EstadoCaja.Cerrada)
    {
        var codigo = Guid.NewGuid().ToString("N")[..8];
        var caja = new Caja
        {
            Codigo = codigo,
            Nombre = "Caja-" + codigo,
            Activa = activa,
            Estado = estado
        };
        _context.Set<Caja>().Add(caja);
        await _context.SaveChangesAsync();
        return caja;
    }

    private async Task<AperturaCaja> AbrirCajaAsync(Caja caja, decimal montoInicial = 1000m)
    {
        return await _service.AbrirCajaAsync(
            new AbrirCajaViewModel { CajaId = caja.Id, MontoInicial = montoInicial },
            "testuser");
    }

    private MovimientoCajaViewModel BuildMovimiento(int aperturaId, TipoMovimientoCaja tipo, decimal monto) =>
        new()
        {
            AperturaCajaId = aperturaId,
            Tipo = tipo,
            Concepto = ConceptoMovimientoCaja.AjusteCaja,
            Descripcion = "Test",
            Monto = monto
        };

    // -------------------------------------------------------------------------
    // AbrirCajaAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AbrirCaja_CajaActivaCerrada_CreaAperturaYCambiaEstado()
    {
        var caja = await SeedCajaAsync();

        var apertura = await _service.AbrirCajaAsync(
            new AbrirCajaViewModel { CajaId = caja.Id, MontoInicial = 500m },
            "usuario1");

        Assert.Equal(caja.Id, apertura.CajaId);
        Assert.Equal(500m, apertura.MontoInicial);
        Assert.Equal("usuario1", apertura.UsuarioApertura);
        Assert.False(apertura.Cerrada);

        var cajaBd = await _context.Set<Caja>().FirstAsync(c => c.Id == caja.Id);
        Assert.Equal(EstadoCaja.Abierta, cajaBd.Estado);
    }

    [Fact]
    public async Task AbrirCaja_CajaNoExiste_LanzaExcepcion()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.AbrirCajaAsync(
                new AbrirCajaViewModel { CajaId = 99999, MontoInicial = 100m },
                "usuario1"));
    }

    [Fact]
    public async Task AbrirCaja_CajaInactiva_LanzaExcepcion()
    {
        var caja = await SeedCajaAsync(activa: false);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.AbrirCajaAsync(
                new AbrirCajaViewModel { CajaId = caja.Id, MontoInicial = 100m },
                "usuario1"));
    }

    [Fact]
    public async Task AbrirCaja_CajaYaAbierta_LanzaExcepcion()
    {
        var caja = await SeedCajaAsync();
        await AbrirCajaAsync(caja); // primera apertura

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AbrirCajaAsync(caja)); // segunda apertura — debe fallar
    }

    // -------------------------------------------------------------------------
    // TieneCajaAbiertaAsync / ExisteAlgunaCajaAbiertaAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TieneCajaAbierta_SinApertura_RetornaFalse()
    {
        var caja = await SeedCajaAsync();
        var result = await _service.TieneCajaAbiertaAsync(caja.Id);
        Assert.False(result);
    }

    [Fact]
    public async Task TieneCajaAbierta_ConAperturaActiva_RetornaTrue()
    {
        var caja = await SeedCajaAsync();
        await AbrirCajaAsync(caja);

        var result = await _service.TieneCajaAbiertaAsync(caja.Id);
        Assert.True(result);
    }

    [Fact]
    public async Task ExisteAlgunaCajaAbierta_SinAperturas_RetornaFalse()
    {
        var result = await _service.ExisteAlgunaCajaAbiertaAsync();
        Assert.False(result);
    }

    [Fact]
    public async Task ExisteAlgunaCajaAbierta_ConUnaAbierta_RetornaTrue()
    {
        var caja = await SeedCajaAsync();
        await AbrirCajaAsync(caja);

        var result = await _service.ExisteAlgunaCajaAbiertaAsync();
        Assert.True(result);
    }

    // -------------------------------------------------------------------------
    // RegistrarMovimientoAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RegistrarMovimiento_AperturaActiva_GuardaMovimiento()
    {
        var caja = await SeedCajaAsync();
        var apertura = await AbrirCajaAsync(caja, montoInicial: 1000m);

        var mov = await _service.RegistrarMovimientoAsync(
            BuildMovimiento(apertura.Id, TipoMovimientoCaja.Ingreso, 200m),
            "usuario1");

        Assert.Equal(apertura.Id, mov.AperturaCajaId);
        Assert.Equal(TipoMovimientoCaja.Ingreso, mov.Tipo);
        Assert.Equal(200m, mov.Monto);
        Assert.Equal("usuario1", mov.Usuario);
    }

    [Fact]
    public async Task RegistrarMovimiento_AperturaNoExiste_LanzaExcepcion()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.RegistrarMovimientoAsync(
                BuildMovimiento(99999, TipoMovimientoCaja.Ingreso, 100m),
                "usuario1"));
    }

    [Fact]
    public async Task RegistrarMovimiento_CajaCerrada_LanzaExcepcion()
    {
        var caja = await SeedCajaAsync();
        var apertura = await AbrirCajaAsync(caja, montoInicial: 500m);

        // Cerrar la caja manualmente (sin usar el service para aislar este test)
        var aperturaBd = await _context.Set<AperturaCaja>().FirstAsync(a => a.Id == apertura.Id);
        aperturaBd.Cerrada = true;
        await _context.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.RegistrarMovimientoAsync(
                BuildMovimiento(apertura.Id, TipoMovimientoCaja.Ingreso, 50m),
                "usuario1"));
    }

    // -------------------------------------------------------------------------
    // CalcularSaldoActualAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CalcularSaldo_SinMovimientos_EsMontoInicial()
    {
        var caja = await SeedCajaAsync();
        var apertura = await AbrirCajaAsync(caja, montoInicial: 1000m);

        var saldo = await _service.CalcularSaldoActualAsync(apertura.Id);

        Assert.Equal(1000m, saldo);
    }

    [Fact]
    public async Task CalcularSaldo_ConIngresosYEgresos_CalculaCorrectamente()
    {
        var caja = await SeedCajaAsync();
        var apertura = await AbrirCajaAsync(caja, montoInicial: 1000m);

        await _service.RegistrarMovimientoAsync(
            BuildMovimiento(apertura.Id, TipoMovimientoCaja.Ingreso, 500m), "u");
        await _service.RegistrarMovimientoAsync(
            BuildMovimiento(apertura.Id, TipoMovimientoCaja.Egreso, 200m), "u");

        // 1000 + 500 - 200 = 1300
        var saldo = await _service.CalcularSaldoActualAsync(apertura.Id);
        Assert.Equal(1300m, saldo);
    }

    [Fact]
    public async Task CalcularSaldo_AperturaNoExiste_RetornaCero()
    {
        var saldo = await _service.CalcularSaldoActualAsync(99999);
        Assert.Equal(0m, saldo);
    }

    // -------------------------------------------------------------------------
    // CerrarCajaAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CerrarCaja_SinDiferencia_CierraCorrectamente()
    {
        var caja = await SeedCajaAsync();
        var apertura = await AbrirCajaAsync(caja, montoInicial: 1000m);

        // Monto esperado = 1000 (sin movimientos), contamos exactamente 1000
        var cierre = await _service.CerrarCajaAsync(
            new CerrarCajaViewModel
            {
                AperturaCajaId = apertura.Id,
                EfectivoContado = 1000m,
                ChequesContados = 0m,
                ValesContados = 0m
            },
            "usuario1");

        Assert.Equal(1000m, cierre.MontoEsperadoSistema);
        Assert.Equal(1000m, cierre.MontoTotalReal);
        Assert.Equal(0m, cierre.Diferencia);
        Assert.False(cierre.TieneDiferencia);
        Assert.Equal("usuario1", cierre.UsuarioCierre);

        // La apertura debe quedar cerrada
        var aperturaBd = await _context.Set<AperturaCaja>().FirstAsync(a => a.Id == apertura.Id);
        Assert.True(aperturaBd.Cerrada);

        // El estado de la caja debe ser Cerrada
        var cajaBd = await _context.Set<Caja>().FirstAsync(c => c.Id == caja.Id);
        Assert.Equal(EstadoCaja.Cerrada, cajaBd.Estado);
    }

    [Fact]
    public async Task CerrarCaja_DiferenciaConJustificacion_CierraCorrectamente()
    {
        var caja = await SeedCajaAsync();
        var apertura = await AbrirCajaAsync(caja, montoInicial: 1000m);

        // Contamos 50 pesos menos — hay diferencia real (> 0.01 tolerancia)
        var cierre = await _service.CerrarCajaAsync(
            new CerrarCajaViewModel
            {
                AperturaCajaId = apertura.Id,
                EfectivoContado = 950m,
                ChequesContados = 0m,
                ValesContados = 0m,
                JustificacionDiferencia = "Faltante de caja"
            },
            "usuario1");

        Assert.True(cierre.TieneDiferencia);
        Assert.Equal(-50m, cierre.Diferencia);
        Assert.Equal("Faltante de caja", cierre.JustificacionDiferencia);
    }

    [Fact]
    public async Task CerrarCaja_DiferenciaSinJustificacion_LanzaExcepcion()
    {
        var caja = await SeedCajaAsync();
        var apertura = await AbrirCajaAsync(caja, montoInicial: 1000m);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CerrarCajaAsync(
                new CerrarCajaViewModel
                {
                    AperturaCajaId = apertura.Id,
                    EfectivoContado = 900m,  // diferencia de 100 sin justificación
                    ChequesContados = 0m,
                    ValesContados = 0m,
                    JustificacionDiferencia = null
                },
                "usuario1"));
    }

    [Fact]
    public async Task CerrarCaja_YaCerrada_LanzaExcepcion()
    {
        var caja = await SeedCajaAsync();
        var apertura = await AbrirCajaAsync(caja, montoInicial: 500m);

        // Primer cierre sin diferencia
        await _service.CerrarCajaAsync(
            new CerrarCajaViewModel
            {
                AperturaCajaId = apertura.Id,
                EfectivoContado = 500m
            },
            "usuario1");

        // Segundo cierre sobre la misma apertura
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CerrarCajaAsync(
                new CerrarCajaViewModel
                {
                    AperturaCajaId = apertura.Id,
                    EfectivoContado = 500m
                },
                "usuario1"));
    }

    [Fact]
    public async Task CerrarCaja_NoExiste_LanzaExcepcion()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CerrarCajaAsync(
                new CerrarCajaViewModel { AperturaCajaId = 99999, EfectivoContado = 0m },
                "usuario1"));
    }

    [Fact]
    public async Task CerrarCaja_ConMovimientos_MontoEsperadoIncluye()
    {
        var caja = await SeedCajaAsync();
        var apertura = await AbrirCajaAsync(caja, montoInicial: 1000m);

        await _service.RegistrarMovimientoAsync(
            BuildMovimiento(apertura.Id, TipoMovimientoCaja.Ingreso, 300m), "u");
        await _service.RegistrarMovimientoAsync(
            BuildMovimiento(apertura.Id, TipoMovimientoCaja.Egreso, 100m), "u");

        // Esperado = 1000 + 300 - 100 = 1200
        var cierre = await _service.CerrarCajaAsync(
            new CerrarCajaViewModel
            {
                AperturaCajaId = apertura.Id,
                EfectivoContado = 1200m
            },
            "usuario1");

        Assert.Equal(300m, cierre.TotalIngresosSistema);
        Assert.Equal(100m, cierre.TotalEgresosSistema);
        Assert.Equal(1200m, cierre.MontoEsperadoSistema);
        Assert.Equal(0m, cierre.Diferencia);
    }

    // -------------------------------------------------------------------------
    // ObtenerAperturaActivaAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ObtenerAperturaActiva_SinApertura_RetornaNull()
    {
        var caja = await SeedCajaAsync();
        var result = await _service.ObtenerAperturaActivaAsync(caja.Id);
        Assert.Null(result);
    }

    [Fact]
    public async Task ObtenerAperturaActiva_ConAperturaActiva_RetornaApertura()
    {
        var caja = await SeedCajaAsync();
        var apertura = await AbrirCajaAsync(caja);

        var result = await _service.ObtenerAperturaActivaAsync(caja.Id);

        Assert.NotNull(result);
        Assert.Equal(apertura.Id, result!.Id);
    }
}
