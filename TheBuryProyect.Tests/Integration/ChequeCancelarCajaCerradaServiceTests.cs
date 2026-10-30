using AutoMapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// B13 - cancelar una venta pagada con cheque cuando la caja del ingreso ya CERRÓ: la contrapartida auditada se asienta en la
/// caja vigente del usuario y la caja cerrada (movimientos, totales, efectivo esperado, saldo real, arqueo) no cambia.
/// </summary>
public class ChequeCancelarCajaCerradaServiceTests : IDisposable
{
    private const string CajeroA = "cajeroA";
    private const string CajeroB = "cajeroB";

    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly CajaService _service;
    private readonly AuditoriaChequeGrabadora _grabadora = new();
    private readonly AperturaCaja _aperturaA;

    public ChequeCancelarCajaCerradaServiceTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();
        _context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        _context.Database.EnsureCreated();

        var mapper = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>(), NullLoggerFactory.Instance).CreateMapper();
        _service = new CajaService(_context, mapper, NullLogger<CajaService>.Instance, new StubNotificacionService(),
            relojComercial: null, auditoria: _grabadora);
        _aperturaA = SeedApertura(CajeroA, "CJA", 1000m);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private AperturaCaja SeedApertura(string usuario, string codigo, decimal montoInicial)
    {
        var caja = new Caja { Codigo = codigo, Nombre = "Caja " + codigo, Activa = true, Estado = EstadoCaja.Abierta };
        _context.Cajas.Add(caja);
        _context.SaveChanges();
        var ap = new AperturaCaja { CajaId = caja.Id, UsuarioApertura = usuario, MontoInicial = montoInicial, Cerrada = false };
        _context.AperturasCaja.Add(ap);
        _context.SaveChanges();
        return ap;
    }

    private async Task<Venta> SeedVentaChequeAsync(decimal total = 100m)
    {
        var g = Guid.NewGuid().ToString("N")[..6];
        var cliente = new Cliente { Nombre = "Test", Apellido = g, NumeroDocumento = g };
        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync();
        var venta = new Venta
        {
            ClienteId = cliente.Id, AperturaCajaId = _aperturaA.Id, Numero = $"VCH-{g}",
            Estado = EstadoVenta.Confirmada, TipoPago = TipoPago.Cheque, Total = total
        };
        _context.Ventas.Add(venta);
        await _context.SaveChangesAsync();
        _context.DatosCheque.Add(new DatosCheque
        {
            VentaId = venta.Id, NumeroCheque = g, Banco = "Banco Nación", Titular = "Titular SA",
            FechaEmision = DateTime.UtcNow.Date, FechaVencimiento = DateTime.UtcNow.Date.AddDays(30), Monto = total
        });
        await _context.SaveChangesAsync();
        return venta;
    }

    private async Task<MovimientoCaja> IngresoAsync(Venta venta) =>
        (await _service.RegistrarMovimientoVentaAsync(venta.Id, venta.Numero, venta.Total, TipoPago.Cheque, CajeroA))!;

    private async Task CerrarAAsync()
    {
        var ap = await _context.AperturasCaja.FirstAsync(a => a.Id == _aperturaA.Id);
        ap.Cerrada = true;
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
    }

    private async Task<MovimientoCaja> LeerAsync(int id)
    {
        _context.ChangeTracker.Clear();
        return await _context.MovimientosCaja.AsNoTracking().FirstAsync(m => m.Id == id);
    }

    private sealed record Foto(int Movimientos, decimal Ingresos, decimal Egresos, decimal Fisica, decimal SaldoTotal, decimal SaldoReal, decimal Pendiente);

    private async Task<Foto> FotoAsync(int aperturaId)
    {
        _context.ChangeTracker.Clear();
        var movs = await _context.MovimientosCaja.AsNoTracking().Where(m => m.AperturaCajaId == aperturaId).ToListAsync();
        var det = await _service.ObtenerDetallesAperturaAsync(aperturaId);
        return new Foto(movs.Count, movs.Where(m => m.Tipo == TipoMovimientoCaja.Ingreso).Sum(m => m.Monto),
            movs.Where(m => m.Tipo == TipoMovimientoCaja.Egreso).Sum(m => m.Monto), det.CajaFisicaEsperada,
            await _service.CalcularSaldoActualAsync(aperturaId), await _service.CalcularSaldoRealAsync(aperturaId),
            det.SaldoPendienteAcreditacion);
    }

    private Task<MovimientoCaja?> CancelarAsync(Venta v, string usuario) =>
        _service.RegistrarContramovimientoVentaAsync(v.Id, v.Numero, "cancelada", usuario);

    // ------------------------------------------------------------------ Pendiente

    [Fact]
    public async Task Pendiente_CajaCerrada_MarcaAnuladoSinEgresoNiMovimientosNuevos_YNoTocaNingunaCaja()
    {
        var venta = await SeedVentaChequeAsync();
        var ingreso = await IngresoAsync(venta);
        await CerrarAAsync();
        var b = SeedApertura(CajeroB, "CJB", 200m);
        var antesA = await FotoAsync(_aperturaA.Id);
        var antesB = await FotoAsync(b.Id);

        var r = await CancelarAsync(venta, CajeroB);

        Assert.Null(r); // no hay dinero cobrado que devolver
        Assert.Equal(EstadoAcreditacionMovimientoCaja.Anulado, (await LeerAsync(ingreso.Id)).EstadoAcreditacion);
        Assert.Equal(0, await _context.MovimientosCaja.CountAsync(m => m.Concepto == ConceptoMovimientoCaja.ReversionVenta));
        var despuesA = await FotoAsync(_aperturaA.Id);
        // Los totales de la caja cerrada no cambian; el cheque deja de figurar como pendiente de acreditación.
        Assert.Equal(antesA with { Pendiente = 0m }, despuesA);
        Assert.Equal(antesB, await FotoAsync(b.Id));
        Assert.Single(_grabadora.Eventos, e => e.Accion == "AnularChequePendienteCajaCerrada");
    }

    [Fact]
    public async Task Pendiente_CajaCerrada_NoNecesitaCajaVigenteYEsIdempotente()
    {
        var venta = await SeedVentaChequeAsync();
        var ingreso = await IngresoAsync(venta);
        await CerrarAAsync();

        Assert.Null(await CancelarAsync(venta, "sinCaja"));
        Assert.Null(await CancelarAsync(venta, "sinCaja"));

        Assert.Equal(EstadoAcreditacionMovimientoCaja.Anulado, (await LeerAsync(ingreso.Id)).EstadoAcreditacion);
        Assert.Equal(1, await _context.MovimientosCaja.CountAsync());
        Assert.Single(_grabadora.Eventos, e => e.Accion == "AnularChequePendienteCajaCerrada");
    }

    [Fact]
    public async Task Pendiente_YaAnulado_NoSePuedeAcreditarNiRechazar()
    {
        var venta = await SeedVentaChequeAsync();
        var ingreso = await IngresoAsync(venta);
        await CerrarAAsync();
        SeedApertura(CajeroB, "CJB", 0m);
        await CancelarAsync(venta, CajeroB);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.AcreditarChequeAsync(ingreso.Id, CajeroB));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.RechazarChequeAsync(ingreso.Id, "x", CajeroB));
    }

    // ------------------------------------------------------------------ Acreditado en su propia caja (que luego cerró)

    [Fact]
    public async Task Acreditado_EnSuCajaCerrada_AsientaEgresoBancarioEnLaCajaVigente_SinTocarLaCerrada()
    {
        var venta = await SeedVentaChequeAsync(100m);
        var ingreso = await IngresoAsync(venta);
        await _service.AcreditarChequeAsync(ingreso.Id, CajeroA); // en el lugar, caja A abierta
        await CerrarAAsync();
        var b = SeedApertura(CajeroB, "CJB", 200m);
        var antesA = await FotoAsync(_aperturaA.Id);
        var antesB = await FotoAsync(b.Id);

        var r = await CancelarAsync(venta, CajeroB);

        Assert.NotNull(r);
        var egreso = await LeerAsync(r!.Id);
        Assert.Equal(b.Id, egreso.AperturaCajaId);
        Assert.Equal(TipoMovimientoCaja.Egreso, egreso.Tipo);
        Assert.Equal(ConceptoMovimientoCaja.ReversionVenta, egreso.Concepto);
        Assert.Equal(EstadoAcreditacionMovimientoCaja.Revertido, egreso.EstadoAcreditacion);
        Assert.Equal(TipoPago.Cheque, egreso.TipoPago);
        Assert.Equal(100m, egreso.Monto);
        Assert.Equal(venta.Id, egreso.VentaId);
        Assert.Equal(ingreso.Id, egreso.MovimientoOrigenId);

        Assert.Equal(antesA, await FotoAsync(_aperturaA.Id)); // caja cerrada intacta (arqueo, efectivo, saldo real, pendiente)
        var despuesB = await FotoAsync(b.Id);
        Assert.Equal(antesB.Fisica, despuesB.Fisica);          // efectivo físico separado
        Assert.Equal(200m - 100m, despuesB.SaldoTotal);
        Assert.Equal(200m - 100m, despuesB.SaldoReal);          // la plata cobrada en A sale de la caja vigente
        Assert.Single(_grabadora.Eventos, e => e.Accion == "ReversionChequeCajaCerrada");
    }

    [Fact]
    public async Task Acreditado_EnCajaCerrada_EsIdempotente_UnaSolaContrapartida()
    {
        var venta = await SeedVentaChequeAsync();
        var ingreso = await IngresoAsync(venta);
        await _service.AcreditarChequeAsync(ingreso.Id, CajeroA);
        await CerrarAAsync();
        SeedApertura(CajeroB, "CJB", 0m);

        Assert.NotNull(await CancelarAsync(venta, CajeroB));
        Assert.Null(await CancelarAsync(venta, CajeroB));

        Assert.Equal(1, await _context.MovimientosCaja.CountAsync(m => m.Concepto == ConceptoMovimientoCaja.ReversionVenta));
        Assert.Single(_grabadora.Eventos, e => e.Accion == "ReversionChequeCajaCerrada");
    }

    [Fact]
    public async Task Acreditado_EnCajaCerrada_SinCajaVigente_SeRechazaSinCambiarNada()
    {
        var venta = await SeedVentaChequeAsync();
        var ingreso = await IngresoAsync(venta);
        await _service.AcreditarChequeAsync(ingreso.Id, CajeroA);
        await CerrarAAsync();
        var antes = await FotoAsync(_aperturaA.Id);
        var eventos = _grabadora.Eventos.Count;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => CancelarAsync(venta, "sinCaja"));

        Assert.Contains("caja abierta", ex.Message);
        Assert.Equal(EstadoAcreditacionMovimientoCaja.Acreditado, (await LeerAsync(ingreso.Id)).EstadoAcreditacion);
        Assert.Equal(0, await _context.MovimientosCaja.CountAsync(m => m.Concepto == ConceptoMovimientoCaja.ReversionVenta));
        Assert.Equal(antes, await FotoAsync(_aperturaA.Id));
        Assert.Equal(eventos, _grabadora.Eventos.Count);
    }

    [Fact]
    public async Task Acreditado_EnCajaCerrada_UnaVezCancelada_NoSePuedeRechazar()
    {
        var venta = await SeedVentaChequeAsync();
        var ingreso = await IngresoAsync(venta);
        await _service.AcreditarChequeAsync(ingreso.Id, CajeroA);
        await CerrarAAsync();
        SeedApertura(CajeroB, "CJB", 0m);
        await CancelarAsync(venta, CajeroB);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.RechazarChequeAsync(ingreso.Id, "rebotado", CajeroB));

        Assert.Contains("cancelada", ex.Message);
        Assert.Equal(0, await _context.MovimientosCaja.CountAsync(m => m.Concepto == ConceptoMovimientoCaja.RechazoCheque));
    }

    // ------------------------------------------------------------------ Acreditado por AcreditacionCheque en la caja vigente

    [Fact]
    public async Task Acreditado_PorAsientoEnCajaVigente_CancelarNeutralizaElSaldoRealDeEsaCaja_YNoTocaLaCerrada()
    {
        var venta = await SeedVentaChequeAsync(100m);
        var ingreso = await IngresoAsync(venta);
        await CerrarAAsync();
        var b = SeedApertura(CajeroB, "CJB", 200m);
        await _service.AcreditarChequeAsync(ingreso.Id, CajeroB); // asiento AcreditacionCheque en B
        var antesA = await FotoAsync(_aperturaA.Id);
        var antesB = await FotoAsync(b.Id);
        Assert.Equal(300m, antesB.SaldoReal);

        var r = await CancelarAsync(venta, CajeroB);

        Assert.NotNull(r);
        Assert.Equal(antesA, await FotoAsync(_aperturaA.Id));
        var despuesB = await FotoAsync(b.Id);
        Assert.Equal(200m, despuesB.SaldoTotal);                 // +100 acreditación -100 reversión
        Assert.Equal(200m, despuesB.SaldoReal);                  // el egreso cuenta aunque el ingreso no tenga VentaId
        Assert.Equal(antesB.Fisica, despuesB.Fisica);
        Assert.Equal(1, await _context.MovimientosCaja.CountAsync(m => m.Concepto == ConceptoMovimientoCaja.ReversionVenta));
    }

    [Fact]
    public async Task Acreditado_PorAsientoEnOtraCajaVigente_CancelaUnTercerUsuarioEnSuPropiaCaja()
    {
        var venta = await SeedVentaChequeAsync(100m);
        var ingreso = await IngresoAsync(venta);
        await CerrarAAsync();
        var b = SeedApertura(CajeroB, "CJB", 200m);
        var c = SeedApertura("cajeroC", "CJC", 50m);
        await _service.AcreditarChequeAsync(ingreso.Id, CajeroB);

        var r = await CancelarAsync(venta, "cajeroC");

        Assert.Equal(c.Id, (await LeerAsync(r!.Id)).AperturaCajaId);
        Assert.Equal(300m, (await FotoAsync(b.Id)).SaldoReal); // B conserva su acreditación; C absorbe la devolución
        Assert.Equal(50m - 100m, (await FotoAsync(c.Id)).SaldoReal);
    }

    // ------------------------------------------------------------------ Rechazado: la salida ya está asentada

    [Fact]
    public async Task Rechazado_Pendiente_CajaCerrada_NoAgregaNada()
    {
        var venta = await SeedVentaChequeAsync();
        var ingreso = await IngresoAsync(venta);
        await CerrarAAsync();
        var b = SeedApertura(CajeroB, "CJB", 0m);
        await _service.RechazarChequeAsync(ingreso.Id, "rebotado", CajeroB);
        var antesA = await FotoAsync(_aperturaA.Id);
        var antesB = await FotoAsync(b.Id);

        Assert.Null(await CancelarAsync(venta, CajeroB));

        Assert.Equal(EstadoAcreditacionMovimientoCaja.Rechazado, (await LeerAsync(ingreso.Id)).EstadoAcreditacion);
        Assert.Equal(antesA, await FotoAsync(_aperturaA.Id));
        Assert.Equal(antesB, await FotoAsync(b.Id));
    }

    [Fact]
    public async Task Rechazado_TrasAcreditar_CajaCerrada_NoDuplicaLaSalida()
    {
        var venta = await SeedVentaChequeAsync(100m);
        var ingreso = await IngresoAsync(venta);
        await _service.AcreditarChequeAsync(ingreso.Id, CajeroA);
        await CerrarAAsync();
        var b = SeedApertura(CajeroB, "CJB", 200m);
        await _service.RechazarChequeAsync(ingreso.Id, "rebotado", CajeroB); // egreso RechazoCheque en B
        var antesB = await FotoAsync(b.Id);

        Assert.Null(await CancelarAsync(venta, CajeroB));

        Assert.Equal(antesB, await FotoAsync(b.Id));
        Assert.Equal(0, await _context.MovimientosCaja.CountAsync(m => m.Concepto == ConceptoMovimientoCaja.ReversionVenta));
        Assert.Equal(1, await _context.MovimientosCaja.CountAsync(m => m.Concepto == ConceptoMovimientoCaja.RechazoCheque));
    }

    // ------------------------------------------------------------------ otros medios: sigue rechazando

    [Fact]
    public async Task OtroMedio_CajaCerrada_SigueRechazandoLaCancelacion()
    {
        var venta = await SeedVentaChequeAsync();
        venta.TipoPago = TipoPago.Efectivo;
        await _context.SaveChangesAsync();
        await _service.RegistrarMovimientoVentaAsync(venta.Id, venta.Numero, venta.Total, TipoPago.Efectivo, CajeroA);
        await CerrarAAsync();
        SeedApertura(CajeroB, "CJB", 0m);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => CancelarAsync(venta, CajeroB));

        Assert.Contains("reversión auditada", ex.Message);
    }

    // ------------------------------------------------------------------ caja abierta: comportamiento previo

    [Fact]
    public async Task CajaOriginalAbierta_ConservaLaReversionEnLaMismaCaja()
    {
        var venta = await SeedVentaChequeAsync(100m);
        var ingreso = await IngresoAsync(venta);
        await _service.AcreditarChequeAsync(ingreso.Id, CajeroA);

        var r = await CancelarAsync(venta, CajeroA);

        Assert.Equal(_aperturaA.Id, (await LeerAsync(r!.Id)).AperturaCajaId);
        Assert.Null((await LeerAsync(r.Id)).MovimientoOrigenId);
        Assert.Equal(1000m, await _service.CalcularSaldoRealAsync(_aperturaA.Id));
    }
}
