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

file sealed class StubNotifContramov : INotificacionService
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
/// QA-FIN-P0 — RegistrarContramovimientoVentaAsync solo puede revertir el ingreso de la
/// propia venta. Antes caía en un fallback "VentaId == null &amp;&amp; ReferenciaId == ventaId"
/// que, al ser ReferenciaId polimórfico (cuotaId, creditoId, devolucionId, ordenId),
/// seleccionaba movimientos de OTRA operación con el mismo número.
/// </summary>
public class CajaContramovimientoVentaIntegridadTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly CajaService _service;
    private readonly AperturaCaja _apertura;

    public CajaContramovimientoVentaIntegridadTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();
        _context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        _context.Database.EnsureCreated();

        var mapper = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>(), NullLoggerFactory.Instance).CreateMapper();
        _service = new CajaService(_context, mapper, NullLogger<CajaService>.Instance, new StubNotifContramov());

        var caja = new Caja { Codigo = "CJFP0", Nombre = "Caja FIN-P0", Activa = true, Estado = EstadoCaja.Abierta };
        _context.Cajas.Add(caja);
        _context.SaveChanges();
        _apertura = new AperturaCaja { CajaId = caja.Id, UsuarioApertura = "cajero", MontoInicial = 0m, Cerrada = false };
        _context.AperturasCaja.Add(_apertura);
        _context.SaveChanges();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private async Task<MovimientoCaja> AddMovAsync(
        ConceptoMovimientoCaja concepto, int? ventaId, int? referenciaId, string? referencia = null,
        TipoMovimientoCaja tipo = TipoMovimientoCaja.Ingreso, decimal monto = 100m)
    {
        var m = new MovimientoCaja
        {
            AperturaCajaId = _apertura.Id, Tipo = tipo, Concepto = concepto, Monto = monto,
            Descripcion = "mov", VentaId = ventaId, ReferenciaId = referenciaId, Referencia = referencia,
            TipoPago = TipoPago.Efectivo, Usuario = "cajero"
        };
        _context.MovimientosCaja.Add(m);
        await _context.SaveChangesAsync();
        return m;
    }

    private async Task<Venta> AddVentaAsync(string numero)
    {
        var venta = new Venta { Numero = numero, Estado = EstadoVenta.Confirmada, TipoPago = TipoPago.Efectivo, Total = 100m };
        _context.Ventas.Add(venta);
        await _context.SaveChangesAsync();
        return venta;
    }

    [Theory]
    [InlineData(ConceptoMovimientoCaja.CobroCuota)]
    [InlineData(ConceptoMovimientoCaja.AnticipoCredito)]
    [InlineData(ConceptoMovimientoCaja.CancelacionCredito)]
    [InlineData(ConceptoMovimientoCaja.LiquidacionMercadoLibre)]
    [InlineData(ConceptoMovimientoCaja.DepositoEfectivo)]
    [InlineData(ConceptoMovimientoCaja.Otro)]
    public async Task Contramovimiento_NoRevierteMovimientoDeOtraOperacion_AunConMismoId(ConceptoMovimientoCaja conceptoAjeno)
    {
        var venta = await AddVentaAsync("V-FP0-1");
        // Movimiento ajeno: ReferenciaId numéricamente igual al id de la venta, sin VentaId.
        var ajeno = await AddMovAsync(conceptoAjeno, ventaId: null, referenciaId: venta.Id, referencia: "CRED-X-C1");
        var antes = await _context.MovimientosCaja.AsNoTracking().OrderBy(m => m.Id).ToListAsync();

        var resultado = await _service.RegistrarContramovimientoVentaAsync(venta.Id, venta.Numero, "motivo", "cajero");

        Assert.Null(resultado);
        _context.ChangeTracker.Clear();
        var despues = await _context.MovimientosCaja.AsNoTracking().OrderBy(m => m.Id).ToListAsync();
        Assert.Equal(antes.Count, despues.Count);
        Assert.DoesNotContain(despues, m => m.Concepto == ConceptoMovimientoCaja.ReversionVenta);
        Assert.Equal(ajeno.Id, despues.Single().Id);
    }

    [Fact]
    public async Task Contramovimiento_ConIngresoPropioYCobroCuotaHomonimo_RevierteSoloElPropio()
    {
        var venta = await AddVentaAsync("V-FP0-2");
        var ajeno = await AddMovAsync(ConceptoMovimientoCaja.CobroCuota, null, venta.Id, "CRED-9-C1", monto: 777m);
        var propio = await AddMovAsync(ConceptoMovimientoCaja.VentaEfectivo, venta.Id, venta.Id, venta.Numero, monto: 100m);

        var contra = await _service.RegistrarContramovimientoVentaAsync(venta.Id, venta.Numero, "motivo", "cajero");

        Assert.NotNull(contra);
        Assert.Equal(100m, contra!.Monto);
        Assert.Contains($"#{propio.Id}", contra.Observaciones);
        Assert.DoesNotContain($"#{ajeno.Id}:", contra.Observaciones);
        Assert.Equal(1, await _context.MovimientosCaja.CountAsync(m => m.Concepto == ConceptoMovimientoCaja.ReversionVenta));
    }

    [Fact]
    public async Task Contramovimiento_VentaIdApuntandoAConceptoNoVenta_NoSeSelecciona()
    {
        var venta = await AddVentaAsync("V-FP0-3");
        await AddMovAsync(ConceptoMovimientoCaja.CobroCuota, ventaId: venta.Id, referenciaId: 5, referencia: "CRED-1-C1");

        var contra = await _service.RegistrarContramovimientoVentaAsync(venta.Id, venta.Numero, "motivo", "cajero");

        Assert.Null(contra);
        Assert.Equal(1, await _context.MovimientosCaja.CountAsync());
    }

    [Fact]
    public async Task Contramovimiento_LegacySinVentaId_SoloSiConceptoVentaYReferenciaCoincideConNumero()
    {
        var venta = await AddVentaAsync("V-FP0-4");
        var legacy = await AddMovAsync(ConceptoMovimientoCaja.VentaEfectivo, null, venta.Id, venta.Numero, monto: 321m);

        var contra = await _service.RegistrarContramovimientoVentaAsync(venta.Id, venta.Numero, "motivo", "cajero");

        Assert.NotNull(contra);
        Assert.Equal(321m, contra!.Monto);
        Assert.Contains($"#{legacy.Id}", contra.Observaciones);
    }

    [Fact]
    public async Task Contramovimiento_LegacySinVentaId_ConReferenciaDeOtroNumero_NoSeSelecciona()
    {
        var venta = await AddVentaAsync("V-FP0-5");
        await AddMovAsync(ConceptoMovimientoCaja.VentaEfectivo, null, venta.Id, "V-OTRO-99");

        var contra = await _service.RegistrarContramovimientoVentaAsync(venta.Id, venta.Numero, "motivo", "cajero");

        Assert.Null(contra);
    }

    [Fact]
    public async Task Contramovimiento_Idempotente_SegundaLlamadaNoDuplica()
    {
        var venta = await AddVentaAsync("V-FP0-6");
        await AddMovAsync(ConceptoMovimientoCaja.VentaEfectivo, venta.Id, venta.Id, venta.Numero);

        var primera = await _service.RegistrarContramovimientoVentaAsync(venta.Id, venta.Numero, "m", "cajero");
        var segunda = await _service.RegistrarContramovimientoVentaAsync(venta.Id, venta.Numero, "m", "cajero");

        Assert.NotNull(primera);
        Assert.Null(segunda);
        Assert.Equal(1, await _context.MovimientosCaja.CountAsync(m => m.Concepto == ConceptoMovimientoCaja.ReversionVenta));
    }

    [Fact]
    public async Task Contramovimiento_ReversionDeOtraVentaNoCuentaComoYaRevertido()
    {
        var ventaA = await AddVentaAsync("V-FP0-7A");
        var ventaB = await AddVentaAsync("V-FP0-7B");
        await AddMovAsync(ConceptoMovimientoCaja.VentaEfectivo, ventaB.Id, ventaB.Id, ventaB.Numero);
        // Una reversión ajena con ReferenciaId == ventaB.Id pero VentaId de otra venta.
        await AddMovAsync(ConceptoMovimientoCaja.ReversionVenta, ventaA.Id, ventaB.Id, ventaA.Numero, TipoMovimientoCaja.Egreso);

        var contra = await _service.RegistrarContramovimientoVentaAsync(ventaB.Id, ventaB.Numero, "m", "cajero");

        Assert.NotNull(contra);
        Assert.Equal(ventaB.Id, contra!.VentaId);
    }
}
