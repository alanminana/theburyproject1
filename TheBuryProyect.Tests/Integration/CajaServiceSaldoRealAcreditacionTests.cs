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

file sealed class StubNotifSaldoReal : INotificacionService
{
    public Task<Notificacion> CrearNotificacionAsync(CrearNotificacionViewModel model) => Task.FromResult(new Notificacion());
    public Task CrearNotificacionParaUsuarioAsync(string u, TipoNotificacion t, string ti, string m, string? url = null, PrioridadNotificacion p = PrioridadNotificacion.Media) => Task.CompletedTask;
    public Task CrearNotificacionParaRolAsync(string r, TipoNotificacion t, string ti, string m, string? url = null, PrioridadNotificacion p = PrioridadNotificacion.Media) => Task.CompletedTask;
    public Task<List<NotificacionViewModel>> ObtenerNotificacionesUsuarioAsync(string u, bool s = false, int l = 50) => Task.FromResult(new List<NotificacionViewModel>());
    public Task<int> ObtenerCantidadNoLeidasAsync(string u) => Task.FromResult(0);
    public Task<Notificacion?> ObtenerNotificacionPorIdAsync(int id) => Task.FromResult<Notificacion?>(null);
    public Task MarcarComoLeidaAsync(int id, string u, byte[]? rv = null) => Task.CompletedTask;
    public Task MarcarTodasComoLeidasAsync(string u) => Task.CompletedTask;
    public Task EliminarNotificacionAsync(int id, string u, byte[]? rv = null) => Task.CompletedTask;
    public Task LimpiarNotificacionesAntiguasAsync(int d = 30) => Task.CompletedTask;
    public Task<ListaNotificacionesViewModel> ObtenerResumenNotificacionesAsync(string u) => Task.FromResult(new ListaNotificacionesViewModel());
}

/// <summary>
/// Tests de integración para Fase 9.4B:
/// CalcularSaldoRealAsync y AcreditarMovimientoAsync.
/// </summary>
public class CajaServiceSaldoRealAcreditacionTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly CajaService _service;
    private readonly AperturaCaja _apertura;

    public CajaServiceSaldoRealAcreditacionTests()
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
            new StubNotifSaldoReal());

        _apertura = SeedAperturaSync();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private AperturaCaja SeedAperturaSync()
    {
        var caja = new Caja { Codigo = "CJ9B", Nombre = "Caja 9.4B", Activa = true, Estado = EstadoCaja.Abierta };
        _context.Cajas.Add(caja);
        _context.SaveChanges();

        var ap = new AperturaCaja
        {
            CajaId = caja.Id,
            UsuarioApertura = "cajero9b",
            MontoInicial = 0m,
            Cerrada = false
        };
        _context.AperturasCaja.Add(ap);
        _context.SaveChanges();
        return ap;
    }

    private int SeedVentaSync(TipoPago tipoPago = TipoPago.Efectivo, decimal total = 500m)
    {
        var g = Guid.NewGuid().ToString("N")[..6];
        var cliente = new Cliente { Nombre = "Test", Apellido = g, NumeroDocumento = g };
        _context.Clientes.Add(cliente);
        _context.SaveChanges();

        var venta = new Venta
        {
            ClienteId = cliente.Id,
            AperturaCajaId = _apertura.Id,
            Numero = $"V9B-{g}",
            Estado = EstadoVenta.Confirmada,
            TipoPago = tipoPago,
            Total = total
        };
        _context.Ventas.Add(venta);
        _context.SaveChanges();
        return venta.Id;
    }

    private MovimientoCaja SeedIngresoSync(
        decimal monto,
        EstadoAcreditacionMovimientoCaja? estado,
        int? ventaId = null,
        TipoPago? tipoPago = null)
    {
        var mov = new MovimientoCaja
        {
            AperturaCajaId = _apertura.Id,
            FechaMovimiento = DateTime.UtcNow,
            Tipo = TipoMovimientoCaja.Ingreso,
            Concepto = ConceptoMovimientoCaja.VentaEfectivo,
            EstadoAcreditacion = estado,
            TipoPago = tipoPago,
            VentaId = ventaId,
            Monto = monto,
            Descripcion = "Test ingreso",
            Usuario = "cajero9b"
        };
        _context.MovimientosCaja.Add(mov);
        _context.SaveChanges();
        return mov;
    }

    private MovimientoCaja SeedEgresoSync(
        decimal monto,
        EstadoAcreditacionMovimientoCaja? estado,
        ConceptoMovimientoCaja concepto = ConceptoMovimientoCaja.GastoOperativo,
        int? ventaId = null)
    {
        var mov = new MovimientoCaja
        {
            AperturaCajaId = _apertura.Id,
            FechaMovimiento = DateTime.UtcNow,
            Tipo = TipoMovimientoCaja.Egreso,
            Concepto = concepto,
            EstadoAcreditacion = estado,
            VentaId = ventaId,
            Monto = monto,
            Descripcion = "Test egreso",
            Usuario = "cajero9b"
        };
        _context.MovimientosCaja.Add(mov);
        _context.SaveChanges();
        return mov;
    }

    // -------------------------------------------------------------------------
    // SaldoReal — reglas base
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SaldoReal_CeroSinMovimientos()
    {
        var saldo = await _service.CalcularSaldoRealAsync(_apertura.Id);
        Assert.Equal(0m, saldo);
    }

    [Fact]
    public async Task SaldoReal_IngresoEfectivoAcreditado_IncluyeEnSaldoReal()
    {
        SeedIngresoSync(500m, EstadoAcreditacionMovimientoCaja.Acreditado, tipoPago: TipoPago.Efectivo);

        var saldo = await _service.CalcularSaldoRealAsync(_apertura.Id);

        Assert.Equal(500m, saldo);
    }

    [Fact]
    public async Task SaldoReal_TransferenciaPendiente_NoIncrementaSaldoReal()
    {
        SeedIngresoSync(1_000m, EstadoAcreditacionMovimientoCaja.Pendiente, tipoPago: TipoPago.Transferencia);

        var saldo = await _service.CalcularSaldoRealAsync(_apertura.Id);

        Assert.Equal(0m, saldo);
    }

    [Fact]
    public async Task SaldoReal_MercadoPagoPendiente_NoIncrementaSaldoReal()
    {
        SeedIngresoSync(2_000m, EstadoAcreditacionMovimientoCaja.Pendiente, tipoPago: TipoPago.MercadoPago);

        var saldo = await _service.CalcularSaldoRealAsync(_apertura.Id);

        Assert.Equal(0m, saldo);
    }

    [Fact]
    public async Task SaldoReal_IngresoSinEstado_IncluyeEnSaldoReal()
    {
        // Movimientos manuales sin estado (efectivo manual, cobros de cuota) deben contar
        SeedIngresoSync(300m, estado: null);

        var saldo = await _service.CalcularSaldoRealAsync(_apertura.Id);

        Assert.Equal(300m, saldo);
    }

    [Fact]
    public async Task SaldoReal_EgresoSinEstado_ReduceSaldoReal()
    {
        SeedIngresoSync(500m, EstadoAcreditacionMovimientoCaja.Acreditado);
        SeedEgresoSync(200m, estado: null);

        var saldo = await _service.CalcularSaldoRealAsync(_apertura.Id);

        Assert.Equal(300m, saldo);
    }

    // -------------------------------------------------------------------------
    // SaldoOperativo mantiene comportamiento anterior (incluye pendientes)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SaldoOperativo_IncluyeTransferenciaPendiente()
    {
        SeedIngresoSync(1_000m, EstadoAcreditacionMovimientoCaja.Pendiente, tipoPago: TipoPago.Transferencia);

        var saldo = await _service.CalcularSaldoActualAsync(_apertura.Id);

        Assert.Equal(1_000m, saldo);
    }

    [Fact]
    public async Task SaldoOperativo_YSaldoReal_DifienenConPendiente()
    {
        SeedIngresoSync(500m, EstadoAcreditacionMovimientoCaja.Acreditado, tipoPago: TipoPago.Efectivo);
        SeedIngresoSync(1_000m, EstadoAcreditacionMovimientoCaja.Pendiente, tipoPago: TipoPago.Transferencia);

        var operativo = await _service.CalcularSaldoActualAsync(_apertura.Id);
        var real = await _service.CalcularSaldoRealAsync(_apertura.Id);

        Assert.Equal(1_500m, operativo);
        Assert.Equal(500m, real);
    }

    // -------------------------------------------------------------------------
    // Reversión: solo impacta SaldoReal si el ingreso original era Acreditado
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SaldoReal_ReversionDeIngresoAcreditado_ReduceSaldoReal()
    {
        // Venta efectivo: ingreso Acreditado
        var ventaId = SeedVentaSync(TipoPago.Efectivo, 400m);
        SeedIngresoSync(400m, EstadoAcreditacionMovimientoCaja.Acreditado, ventaId: ventaId);
        SeedEgresoSync(400m, EstadoAcreditacionMovimientoCaja.Revertido,
            ConceptoMovimientoCaja.ReversionVenta, ventaId: ventaId);

        var saldo = await _service.CalcularSaldoRealAsync(_apertura.Id);

        Assert.Equal(0m, saldo);
    }

    [Fact]
    public async Task SaldoReal_ReversionDeIngresoPendiente_NoImpactaSaldoReal()
    {
        // Venta transferencia: ingreso Pendiente (nunca llegó al saldo real)
        var ventaId = SeedVentaSync(TipoPago.Transferencia, 800m);
        SeedIngresoSync(800m, EstadoAcreditacionMovimientoCaja.Pendiente, ventaId: ventaId);
        SeedEgresoSync(800m, EstadoAcreditacionMovimientoCaja.Revertido,
            ConceptoMovimientoCaja.ReversionVenta, ventaId: ventaId);

        var saldo = await _service.CalcularSaldoRealAsync(_apertura.Id);

        Assert.Equal(0m, saldo); // ni el ingreso ni el egreso impactan SaldoReal
    }

    // -------------------------------------------------------------------------
    // TransferenciaPendiente → Acreditado: ahora sí impacta SaldoReal
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AlAcreditarTransferencia_SaldoRealAumenta()
    {
        var mov = SeedIngresoSync(1_200m, EstadoAcreditacionMovimientoCaja.Pendiente,
            tipoPago: TipoPago.Transferencia);

        var antes = await _service.CalcularSaldoRealAsync(_apertura.Id);
        Assert.Equal(0m, antes);

        await _service.AcreditarMovimientoAsync(mov.Id, "cajero9b");

        var despues = await _service.CalcularSaldoRealAsync(_apertura.Id);
        Assert.Equal(1_200m, despues);
    }

    // -------------------------------------------------------------------------
    // AcreditarMovimientoAsync — validaciones de estado
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AcreditarMovimiento_Pendiente_Exito()
    {
        var mov = SeedIngresoSync(500m, EstadoAcreditacionMovimientoCaja.Pendiente,
            tipoPago: TipoPago.Transferencia);

        var resultado = await _service.AcreditarMovimientoAsync(mov.Id, "cajero9b");

        Assert.Equal(EstadoAcreditacionMovimientoCaja.Acreditado, resultado.EstadoAcreditacion);
        // UpdatedAt es gestionado por AppDbContext.SaveChangesAsync — verificar que se seteó
        Assert.NotNull(resultado.UpdatedAt);
    }

    [Fact]
    public async Task AcreditarMovimiento_YaAcreditado_LanzaExcepcion()
    {
        var mov = SeedIngresoSync(500m, EstadoAcreditacionMovimientoCaja.Acreditado);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.AcreditarMovimientoAsync(mov.Id, "cajero9b"));
    }

    [Fact]
    public async Task AcreditarMovimiento_Revertido_LanzaExcepcion()
    {
        var mov = SeedEgresoSync(500m, EstadoAcreditacionMovimientoCaja.Revertido,
            ConceptoMovimientoCaja.ReversionVenta);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.AcreditarMovimientoAsync(mov.Id, "cajero9b"));
    }

    [Fact]
    public async Task AcreditarMovimiento_Egreso_LanzaExcepcion()
    {
        var egreso = SeedEgresoSync(200m, estado: null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.AcreditarMovimientoAsync(egreso.Id, "cajero9b"));
    }

    [Fact]
    public async Task AcreditarMovimiento_NoExiste_LanzaExcepcion()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.AcreditarMovimientoAsync(99999, "cajero9b"));
    }

    // -------------------------------------------------------------------------
    // Cancelación sigue neutralizando caja (regresión 9.3)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CancelacionTransferencia_SaldoOperativoNeutral()
    {
        var g = Guid.NewGuid().ToString("N")[..6];
        var cliente = new Cliente { Nombre = "Test", Apellido = g, NumeroDocumento = g };
        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync();

        var venta = new Venta
        {
            ClienteId = cliente.Id,
            AperturaCajaId = _apertura.Id,
            Numero = $"V9B-{g}",
            Estado = EstadoVenta.Confirmada,
            TipoPago = TipoPago.Transferencia,
            Total = 1_000m
        };
        _context.Ventas.Add(venta);
        await _context.SaveChangesAsync();

        await _service.RegistrarMovimientoVentaAsync(
            venta.Id, venta.Numero, venta.Total, venta.TipoPago, "cajero9b");

        await _service.RegistrarContramovimientoVentaAsync(
            venta.Id, venta.Numero, "Test cancelación", "cajero9b");

        var saldoOperativo = await _service.CalcularSaldoActualAsync(_apertura.Id);
        var saldoReal = await _service.CalcularSaldoRealAsync(_apertura.Id);

        Assert.Equal(0m, saldoOperativo);
        Assert.Equal(0m, saldoReal);
    }
}
