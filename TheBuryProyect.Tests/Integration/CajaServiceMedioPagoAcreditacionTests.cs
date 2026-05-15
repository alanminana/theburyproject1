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
// Stub file-scoped
// ---------------------------------------------------------------------------

file sealed class StubNotifMedioPago : INotificacionService
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
/// Tests de integración para Fase 9.4:
/// Verifica que cada TipoPago genera el Concepto correcto en MovimientoCaja
/// y que el EstadoAcreditacion se asigna según las reglas de negocio V1:
///   - Efectivo → VentaEfectivo, Acreditado
///   - Transferencia → VentaTransferencia, Pendiente
///   - MercadoPago → VentaMercadoPago, Pendiente
///   - TarjetaDebito/TarjetaCredito/Tarjeta → VentaTarjeta, Pendiente
///   - Cheque → VentaCheque, Pendiente
///   - ReversionVenta (contramovimiento) → EstadoAcreditacion = Revertido
/// </summary>
public class CajaServiceMedioPagoAcreditacionTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly CajaService _service;
    private readonly AperturaCaja _apertura;

    public CajaServiceMedioPagoAcreditacionTests()
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
            new StubNotifMedioPago());

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
        var caja = new Caja { Codigo = "CJ94", Nombre = "Caja 9.4", Activa = true, Estado = EstadoCaja.Abierta };
        _context.Cajas.Add(caja);
        _context.SaveChanges();

        var ap = new AperturaCaja
        {
            CajaId = caja.Id,
            UsuarioApertura = "cajero94",
            MontoInicial = 0m,
            Cerrada = false
        };
        _context.AperturasCaja.Add(ap);
        _context.SaveChanges();
        return ap;
    }

    private async Task<Venta> SeedVentaAsync(TipoPago tipoPago, decimal total = 500m)
    {
        var g = Guid.NewGuid().ToString("N")[..6];
        var cliente = new Cliente { Nombre = "Test", Apellido = g, NumeroDocumento = g };
        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync();

        var venta = new Venta
        {
            ClienteId = cliente.Id,
            AperturaCajaId = _apertura.Id,
            Numero = $"V94-{g}",
            Estado = EstadoVenta.Confirmada,
            TipoPago = tipoPago,
            Total = total
        };
        _context.Ventas.Add(venta);
        await _context.SaveChangesAsync();
        return venta;
    }

    // -------------------------------------------------------------------------
    // Concepto correcto por TipoPago
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Efectivo_CreaVentaEfectivo_Acreditado()
    {
        var venta = await SeedVentaAsync(TipoPago.Efectivo, 300m);

        var mov = await _service.RegistrarMovimientoVentaAsync(
            venta.Id, venta.Numero, venta.Total, venta.TipoPago, "cajero94");

        Assert.NotNull(mov);
        Assert.Equal(ConceptoMovimientoCaja.VentaEfectivo, mov!.Concepto);
        Assert.Equal(EstadoAcreditacionMovimientoCaja.Acreditado, mov.EstadoAcreditacion);
    }

    [Fact]
    public async Task Transferencia_CreaVentaTransferencia_Pendiente()
    {
        var venta = await SeedVentaAsync(TipoPago.Transferencia, 1_200m);

        var mov = await _service.RegistrarMovimientoVentaAsync(
            venta.Id, venta.Numero, venta.Total, venta.TipoPago, "cajero94");

        Assert.NotNull(mov);
        Assert.Equal(ConceptoMovimientoCaja.VentaTransferencia, mov!.Concepto);
        Assert.Equal(EstadoAcreditacionMovimientoCaja.Pendiente, mov.EstadoAcreditacion);
    }

    [Fact]
    public async Task MercadoPago_CreaVentaMercadoPago_Pendiente()
    {
        var venta = await SeedVentaAsync(TipoPago.MercadoPago, 2_000m);

        var mov = await _service.RegistrarMovimientoVentaAsync(
            venta.Id, venta.Numero, venta.Total, venta.TipoPago, "cajero94");

        Assert.NotNull(mov);
        Assert.Equal(ConceptoMovimientoCaja.VentaMercadoPago, mov!.Concepto);
        Assert.Equal(EstadoAcreditacionMovimientoCaja.Pendiente, mov.EstadoAcreditacion);
    }

    [Fact]
    public async Task TarjetaDebito_CreaVentaTarjeta_Pendiente()
    {
        var venta = await SeedVentaAsync(TipoPago.TarjetaDebito, 800m);

        var mov = await _service.RegistrarMovimientoVentaAsync(
            venta.Id, venta.Numero, venta.Total, venta.TipoPago, "cajero94");

        Assert.NotNull(mov);
        Assert.Equal(ConceptoMovimientoCaja.VentaTarjeta, mov!.Concepto);
        Assert.Equal(EstadoAcreditacionMovimientoCaja.Pendiente, mov.EstadoAcreditacion);
    }

    [Fact]
    public async Task TarjetaCredito_CreaVentaTarjeta_Pendiente()
    {
        var venta = await SeedVentaAsync(TipoPago.TarjetaCredito, 1_500m);

        var mov = await _service.RegistrarMovimientoVentaAsync(
            venta.Id, venta.Numero, venta.Total, venta.TipoPago, "cajero94");

        Assert.NotNull(mov);
        Assert.Equal(ConceptoMovimientoCaja.VentaTarjeta, mov!.Concepto);
        Assert.Equal(EstadoAcreditacionMovimientoCaja.Pendiente, mov.EstadoAcreditacion);
    }

    [Fact]
    public async Task TarjetaGenerica_CreaVentaTarjeta_Pendiente()
    {
        var venta = await SeedVentaAsync(TipoPago.Tarjeta, 600m);

        var mov = await _service.RegistrarMovimientoVentaAsync(
            venta.Id, venta.Numero, venta.Total, venta.TipoPago, "cajero94");

        Assert.NotNull(mov);
        Assert.Equal(ConceptoMovimientoCaja.VentaTarjeta, mov!.Concepto);
        Assert.Equal(EstadoAcreditacionMovimientoCaja.Pendiente, mov.EstadoAcreditacion);
    }

    [Fact]
    public async Task Cheque_CreaVentaCheque_Pendiente()
    {
        var venta = await SeedVentaAsync(TipoPago.Cheque, 900m);

        var mov = await _service.RegistrarMovimientoVentaAsync(
            venta.Id, venta.Numero, venta.Total, venta.TipoPago, "cajero94");

        Assert.NotNull(mov);
        Assert.Equal(ConceptoMovimientoCaja.VentaCheque, mov!.Concepto);
        Assert.Equal(EstadoAcreditacionMovimientoCaja.Pendiente, mov.EstadoAcreditacion);
    }

    // -------------------------------------------------------------------------
    // Transferencia y MercadoPago ya no se confunden con Efectivo
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Transferencia_NoEsVentaEfectivo()
    {
        var venta = await SeedVentaAsync(TipoPago.Transferencia, 700m);

        var mov = await _service.RegistrarMovimientoVentaAsync(
            venta.Id, venta.Numero, venta.Total, venta.TipoPago, "cajero94");

        Assert.NotNull(mov);
        Assert.NotEqual(ConceptoMovimientoCaja.VentaEfectivo, mov!.Concepto);
    }

    [Fact]
    public async Task MercadoPago_NoEsVentaEfectivo()
    {
        var venta = await SeedVentaAsync(TipoPago.MercadoPago, 1_100m);

        var mov = await _service.RegistrarMovimientoVentaAsync(
            venta.Id, venta.Numero, venta.Total, venta.TipoPago, "cajero94");

        Assert.NotNull(mov);
        Assert.NotEqual(ConceptoMovimientoCaja.VentaEfectivo, mov!.Concepto);
    }

    // -------------------------------------------------------------------------
    // Contramovimiento (ReversionVenta) → EstadoAcreditacion = Revertido
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ReversionTransferencia_EstadoAcreditacionRevertido()
    {
        var venta = await SeedVentaAsync(TipoPago.Transferencia, 800m);

        var ingreso = new MovimientoCaja
        {
            AperturaCajaId = _apertura.Id,
            FechaMovimiento = DateTime.UtcNow,
            Tipo = TipoMovimientoCaja.Ingreso,
            Concepto = ConceptoMovimientoCaja.VentaTransferencia,
            EstadoAcreditacion = EstadoAcreditacionMovimientoCaja.Pendiente,
            TipoPago = TipoPago.Transferencia,
            VentaId = venta.Id,
            Monto = 800m,
            Descripcion = $"Venta {venta.Numero}",
            Referencia = venta.Numero,
            ReferenciaId = venta.Id,
            Usuario = "cajero94"
        };
        _context.MovimientosCaja.Add(ingreso);
        await _context.SaveChangesAsync();

        var contramov = await _service.RegistrarContramovimientoVentaAsync(
            venta.Id, venta.Numero, "Cancelación test", "cajero94");

        Assert.NotNull(contramov);
        Assert.Equal(ConceptoMovimientoCaja.ReversionVenta, contramov!.Concepto);
        Assert.Equal(EstadoAcreditacionMovimientoCaja.Revertido, contramov.EstadoAcreditacion);
        Assert.Equal(TipoPago.Transferencia, contramov.TipoPago);
    }

    [Fact]
    public async Task ReversionMercadoPago_EstadoAcreditacionRevertido()
    {
        var venta = await SeedVentaAsync(TipoPago.MercadoPago, 2_500m);

        var ingreso = new MovimientoCaja
        {
            AperturaCajaId = _apertura.Id,
            FechaMovimiento = DateTime.UtcNow,
            Tipo = TipoMovimientoCaja.Ingreso,
            Concepto = ConceptoMovimientoCaja.VentaMercadoPago,
            EstadoAcreditacion = EstadoAcreditacionMovimientoCaja.Pendiente,
            TipoPago = TipoPago.MercadoPago,
            VentaId = venta.Id,
            Monto = 2_500m,
            Descripcion = $"Venta {venta.Numero}",
            Referencia = venta.Numero,
            ReferenciaId = venta.Id,
            Usuario = "cajero94"
        };
        _context.MovimientosCaja.Add(ingreso);
        await _context.SaveChangesAsync();

        var contramov = await _service.RegistrarContramovimientoVentaAsync(
            venta.Id, venta.Numero, "Cancelación test", "cajero94");

        Assert.NotNull(contramov);
        Assert.Equal(EstadoAcreditacionMovimientoCaja.Revertido, contramov!.EstadoAcreditacion);
    }

    [Fact]
    public async Task ReversionEfectivo_EstadoAcreditacionRevertido()
    {
        var venta = await SeedVentaAsync(TipoPago.Efectivo, 400m);

        var ingreso = new MovimientoCaja
        {
            AperturaCajaId = _apertura.Id,
            FechaMovimiento = DateTime.UtcNow,
            Tipo = TipoMovimientoCaja.Ingreso,
            Concepto = ConceptoMovimientoCaja.VentaEfectivo,
            EstadoAcreditacion = EstadoAcreditacionMovimientoCaja.Acreditado,
            TipoPago = TipoPago.Efectivo,
            VentaId = venta.Id,
            Monto = 400m,
            Descripcion = $"Venta {venta.Numero}",
            Referencia = venta.Numero,
            ReferenciaId = venta.Id,
            Usuario = "cajero94"
        };
        _context.MovimientosCaja.Add(ingreso);
        await _context.SaveChangesAsync();

        var contramov = await _service.RegistrarContramovimientoVentaAsync(
            venta.Id, venta.Numero, "Cancelación efectivo", "cajero94");

        Assert.NotNull(contramov);
        Assert.Equal(EstadoAcreditacionMovimientoCaja.Revertido, contramov!.EstadoAcreditacion);
        Assert.Equal(TipoPago.Efectivo, contramov.TipoPago);
    }

    // -------------------------------------------------------------------------
    // Saldo de caja (V1): incluye todos los movimientos independientemente del estado
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Saldo_IncluyeTransferenciaPendiente()
    {
        var venta = await SeedVentaAsync(TipoPago.Transferencia, 1_000m);

        await _service.RegistrarMovimientoVentaAsync(
            venta.Id, venta.Numero, venta.Total, venta.TipoPago, "cajero94");

        var saldo = await _service.CalcularSaldoActualAsync(_apertura.Id);

        Assert.Equal(1_000m, saldo);
    }

    [Fact]
    public async Task Saldo_NeutralDespusDeCancelarTransferencia()
    {
        var venta = await SeedVentaAsync(TipoPago.Transferencia, 1_000m);

        await _service.RegistrarMovimientoVentaAsync(
            venta.Id, venta.Numero, venta.Total, venta.TipoPago, "cajero94");

        await _service.RegistrarContramovimientoVentaAsync(
            venta.Id, venta.Numero, "Cancelación", "cajero94");

        var saldo = await _service.CalcularSaldoActualAsync(_apertura.Id);

        Assert.Equal(0m, saldo);
    }
}
