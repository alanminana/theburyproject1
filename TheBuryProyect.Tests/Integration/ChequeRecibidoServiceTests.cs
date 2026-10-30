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

internal sealed class AuditoriaChequeGrabadora : ISeguridadAuditoriaService
{
    public List<(string Modulo, string Accion, string Entidad, string? Detalle)> Eventos { get; } = new();
    public Task RegistrarEventoAsync(string modulo, string accion, string entidad, string? detalle = null)
    { Eventos.Add((modulo, accion, entidad, detalle)); return Task.CompletedTask; }
    public void AgregarEvento(string modulo, string accion, string entidad, string? detalle = null)
        => Eventos.Add((modulo, accion, entidad, detalle));
    public Task<AuditoriaQueryResult> ConsultarEventosAsync(string? usuario = null, string? modulo = null, string? accion = null,
        DateOnly? desde = null, DateOnly? hasta = null, int skip = 0, int? take = null)
        => Task.FromResult(new AuditoriaQueryResult());
}

/// <summary>
/// Hito 10 - circuito del cheque RECIBIDO: acreditar (D1-A), rechazar (D2-A), devolución (D4), cuota con cheque (D5).
/// Dos cajas: A (la del cheque, que luego cierra) y B (la vigente del usuario que acredita/rechaza).
/// </summary>
public class ChequeRecibidoServiceTests : IDisposable
{
    private const string CajeroA = "cajeroA";
    private const string CajeroB = "cajeroB";

    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly CajaService _service;
    private readonly ISeguridadAuditoriaService _auditoria;
    private readonly AuditoriaChequeGrabadora _grabadora;
    private AperturaCaja _aperturaA = null!;

    public ChequeRecibidoServiceTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();
        _context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        _context.Database.EnsureCreated();

        var mapper = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>(), NullLoggerFactory.Instance).CreateMapper();
        _grabadora = new AuditoriaChequeGrabadora();
        _auditoria = _grabadora;
        _service = new CajaService(_context, mapper, NullLogger<CajaService>.Instance, new StubNotificacionService(),
            relojComercial: null, auditoria: _auditoria);
        _aperturaA = SeedApertura(CajeroA, "CJA", 1000m);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // ------------------------------------------------------------------ helpers

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

    private async Task<Venta> SeedVentaChequeAsync(decimal total = 900m, string numeroCheque = "123456", string banco = "Banco Nación")
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
            VentaId = venta.Id, NumeroCheque = numeroCheque, Banco = banco, Titular = "Titular SA",
            FechaEmision = DateTime.UtcNow.Date, FechaVencimiento = DateTime.UtcNow.Date.AddDays(30), Monto = total
        });
        await _context.SaveChangesAsync();
        return venta;
    }

    private async Task<MovimientoCaja> RegistrarIngresoAsync(Venta venta)
    {
        var mov = await _service.RegistrarMovimientoVentaAsync(venta.Id, venta.Numero, venta.Total, TipoPago.Cheque, CajeroA);
        return mov!;
    }

    private async Task CerrarAperturaAAsync()
    {
        var ap = await _context.AperturasCaja.FirstAsync(a => a.Id == _aperturaA.Id);
        ap.Cerrada = true;
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
    }

    private async Task<MovimientoCaja> LeerMovAsync(int id)
    {
        _context.ChangeTracker.Clear();
        return await _context.MovimientosCaja.AsNoTracking().FirstAsync(m => m.Id == id);
    }

    private async Task<DatosCheque> LeerDatosAsync(int ventaId)
    {
        _context.ChangeTracker.Clear();
        return await _context.DatosCheque.AsNoTracking().FirstAsync(d => d.VentaId == ventaId);
    }

    private sealed record FotoCajaCerrada(int Movimientos, decimal Ingresos, decimal Egresos, decimal Fisica, decimal SaldoReal, bool Cerrada);

    private async Task<FotoCajaCerrada> FotoAAsync()
    {
        _context.ChangeTracker.Clear();
        var movs = await _context.MovimientosCaja.AsNoTracking().Where(m => m.AperturaCajaId == _aperturaA.Id).ToListAsync();
        var det = await _service.ObtenerDetallesAperturaAsync(_aperturaA.Id);
        var ap = await _context.AperturasCaja.AsNoTracking().FirstAsync(a => a.Id == _aperturaA.Id);
        return new FotoCajaCerrada(movs.Count, movs.Where(m => m.Tipo == TipoMovimientoCaja.Ingreso).Sum(m => m.Monto),
            movs.Where(m => m.Tipo == TipoMovimientoCaja.Egreso).Sum(m => m.Monto), det.CajaFisicaEsperada,
            await _service.CalcularSaldoRealAsync(_aperturaA.Id), ap.Cerrada);
    }

    // ------------------------------------------------------------------ D1-A: acreditar

    [Fact]
    public async Task Acreditar_ConCajaAbierta_CambiaElEstadoEnElLugarYDejaTrazabilidad()
    {
        var venta = await SeedVentaChequeAsync();
        var ingreso = await RegistrarIngresoAsync(venta);

        var r = await _service.AcreditarChequeAsync(ingreso.Id, CajeroA);

        Assert.Null(r.MovimientoAsentadoId);
        var mov = await LeerMovAsync(ingreso.Id);
        Assert.Equal(EstadoAcreditacionMovimientoCaja.Acreditado, mov.EstadoAcreditacion);
        Assert.Equal(1, await _context.MovimientosCaja.CountAsync());
        var datos = await LeerDatosAsync(venta.Id);
        Assert.NotNull(datos.AcreditadoAt);
        Assert.Equal(CajeroA, datos.AcreditadoPor);
        Assert.Single(_grabadora.Eventos, e => e.Accion == "AcreditarCheque");
    }

    [Fact]
    public async Task Acreditar_ConCajaCerrada_AsientaNuevoIngresoNoFisicoEnLaCajaVigenteSinTocarLaCerrada()
    {
        var venta = await SeedVentaChequeAsync(900m);
        var ingreso = await RegistrarIngresoAsync(venta);
        await CerrarAperturaAAsync();
        var aperturaB = SeedApertura(CajeroB, "CJB", 200m);
        var antes = await FotoAAsync();
        var fisicaBAntes = (await _service.ObtenerDetallesAperturaAsync(aperturaB.Id)).CajaFisicaEsperada;

        var r = await _service.AcreditarChequeAsync(ingreso.Id, CajeroB);

        Assert.NotNull(r.MovimientoAsentadoId);
        Assert.Equal(aperturaB.Id, r.AperturaAsentadaId);
        var asiento = await LeerMovAsync(r.MovimientoAsentadoId!.Value);
        Assert.Equal(aperturaB.Id, asiento.AperturaCajaId);
        Assert.Equal(TipoMovimientoCaja.Ingreso, asiento.Tipo);
        Assert.Equal(ConceptoMovimientoCaja.AcreditacionCheque, asiento.Concepto);
        Assert.Equal(TipoPago.Cheque, asiento.TipoPago);
        Assert.Equal(EstadoAcreditacionMovimientoCaja.Acreditado, asiento.EstadoAcreditacion);
        Assert.Equal(ingreso.Id, asiento.MovimientoOrigenId);
        Assert.Equal(900m, asiento.Monto);
        Assert.Equal(venta.Id, asiento.ReferenciaId); // sin FK a Ventas: no invierte el orden de locks
        Assert.Null(asiento.VentaId);

        // El original queda marcado Acreditado; la caja cerrada no cambia en cantidades, totales, efectivo ni saldo real.
        Assert.Equal(EstadoAcreditacionMovimientoCaja.Acreditado, (await LeerMovAsync(ingreso.Id)).EstadoAcreditacion);
        var despues = await FotoAAsync();
        Assert.Equal(antes, despues);
        Assert.True(despues.Cerrada);

        // Efectivo físico / arqueo de la caja vigente: intactos. Saldo real: suma el cheque acreditado.
        var detB = await _service.ObtenerDetallesAperturaAsync(aperturaB.Id);
        Assert.Equal(fisicaBAntes, detB.CajaFisicaEsperada);
        Assert.Equal(200m, detB.CajaFisicaEsperada);
        Assert.Equal(900m, detB.TotalIngresosDigitales);
        Assert.Equal(200m + 900m, await _service.CalcularSaldoRealAsync(aperturaB.Id));

        var datos = await LeerDatosAsync(venta.Id);
        Assert.Equal(CajeroB, datos.AcreditadoPor);
        var evento = Assert.Single(_grabadora.Eventos, e => e.Accion == "AcreditarCheque");
        Assert.DoesNotContain("Titular", evento.Detalle);
    }

    [Fact]
    public async Task Acreditar_EsIdempotente_UnSegundoIntentoFallaYNoDuplicaNiAudita()
    {
        var venta = await SeedVentaChequeAsync();
        var ingreso = await RegistrarIngresoAsync(venta);
        await CerrarAperturaAAsync();
        SeedApertura(CajeroB, "CJB", 0m);

        await _service.AcreditarChequeAsync(ingreso.Id, CajeroB);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.AcreditarChequeAsync(ingreso.Id, CajeroB));

        Assert.Equal(1, await _context.MovimientosCaja.CountAsync(m => m.Concepto == ConceptoMovimientoCaja.AcreditacionCheque));
        Assert.Single(_grabadora.Eventos, e => e.Accion == "AcreditarCheque");
    }

    [Fact]
    public async Task Acreditar_ConCajaCerradaYSinCajaAbiertaPropia_FallaSinCambiarNada()
    {
        var venta = await SeedVentaChequeAsync();
        var ingreso = await RegistrarIngresoAsync(venta);
        await CerrarAperturaAAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.AcreditarChequeAsync(ingreso.Id, "sinCaja"));

        Assert.Contains("caja abierta", ex.Message);
        Assert.Equal(EstadoAcreditacionMovimientoCaja.Pendiente, (await LeerMovAsync(ingreso.Id)).EstadoAcreditacion);
        Assert.Empty(_grabadora.Eventos);
    }

    [Fact]
    public async Task Acreditar_RechazaMovimientosQueNoSonChequesRecibidos()
    {
        var transferencia = new MovimientoCaja
        {
            AperturaCajaId = _aperturaA.Id, Tipo = TipoMovimientoCaja.Ingreso, Concepto = ConceptoMovimientoCaja.VentaTransferencia,
            TipoPago = TipoPago.Transferencia, EstadoAcreditacion = EstadoAcreditacionMovimientoCaja.Pendiente, Monto = 10m,
            Descripcion = "t", Usuario = CajeroA
        };
        _context.MovimientosCaja.Add(transferencia);
        await _context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.AcreditarChequeAsync(transferencia.Id, CajeroA));
        Assert.Contains("no es un cheque", ex.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.AcreditarChequeAsync(999_999, CajeroA));
    }

    // ------------------------------------------------------------------ D2-A: rechazar

    [Fact]
    public async Task Rechazar_Pendiente_MarcaRechazadoConMotivoUsuarioFechaYAuditoria_SinCancelarLaVenta()
    {
        var venta = await SeedVentaChequeAsync();
        var ingreso = await RegistrarIngresoAsync(venta);

        await _service.RechazarChequeAsync(ingreso.Id, "  sin fondos  ", CajeroA);

        Assert.Equal(EstadoAcreditacionMovimientoCaja.Rechazado, (await LeerMovAsync(ingreso.Id)).EstadoAcreditacion);
        var datos = await LeerDatosAsync(venta.Id);
        Assert.NotNull(datos.RechazadoAt);
        Assert.Equal(CajeroA, datos.RechazadoPor);
        Assert.Equal("sin fondos", datos.MotivoRechazo);
        Assert.True(datos.Rechazado);

        _context.ChangeTracker.Clear();
        var ventaDb = await _context.Ventas.AsNoTracking().FirstAsync(v => v.Id == venta.Id);
        Assert.Equal(EstadoVenta.Confirmada, ventaDb.Estado); // NO se cancela sola
        Assert.Equal(1, await _context.MovimientosCaja.CountAsync()); // nada que contrapesar: no era dinero real

        var evento = Assert.Single(_grabadora.Eventos, e => e.Accion == "RechazarCheque");
        Assert.Contains("sin fondos", evento.Detalle);
        Assert.DoesNotContain("Titular", evento.Detalle);
        Assert.Equal(0m, (await _service.ObtenerDetallesAperturaAsync(_aperturaA.Id)).SaldoPendienteAcreditacion);
        Assert.Equal(1000m, (await _service.ObtenerDetallesAperturaAsync(_aperturaA.Id)).SaldoActual); // el ingreso rechazado no suma
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Rechazar_SinMotivo_Falla(string motivo)
    {
        var venta = await SeedVentaChequeAsync();
        var ingreso = await RegistrarIngresoAsync(venta);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.RechazarChequeAsync(ingreso.Id, motivo, CajeroA));

        Assert.Contains("motivo", ex.Message);
        Assert.Equal(EstadoAcreditacionMovimientoCaja.Pendiente, (await LeerMovAsync(ingreso.Id)).EstadoAcreditacion);
    }

    [Fact]
    public async Task Rechazar_Pendiente_EnCajaCerrada_NoNecesitaCajaAbiertaYNoAgregaMovimientos()
    {
        var venta = await SeedVentaChequeAsync();
        var ingreso = await RegistrarIngresoAsync(venta);
        await CerrarAperturaAAsync();

        await _service.RechazarChequeAsync(ingreso.Id, "cuenta cerrada", "cualquiera");

        Assert.Equal(EstadoAcreditacionMovimientoCaja.Rechazado, (await LeerMovAsync(ingreso.Id)).EstadoAcreditacion);
        Assert.Equal(1, await _context.MovimientosCaja.CountAsync());
    }

    [Fact]
    public async Task Rechazar_Acreditado_GeneraEgresoDeContrapartidaEnLaCajaVigenteYLaVentaQuedaPendienteDeCobro()
    {
        var venta = await SeedVentaChequeAsync(900m);
        var ingreso = await RegistrarIngresoAsync(venta);
        await _service.AcreditarChequeAsync(ingreso.Id, CajeroA);
        await CerrarAperturaAAsync();
        var aperturaB = SeedApertura(CajeroB, "CJB", 200m);
        var antes = await FotoAAsync();

        var r = await _service.RechazarChequeAsync(ingreso.Id, "firma no coincide", CajeroB);

        Assert.NotNull(r.MovimientoAsentadoId);
        var egreso = await LeerMovAsync(r.MovimientoAsentadoId!.Value);
        Assert.Equal(aperturaB.Id, egreso.AperturaCajaId);
        Assert.Equal(TipoMovimientoCaja.Egreso, egreso.Tipo);
        Assert.Equal(ConceptoMovimientoCaja.RechazoCheque, egreso.Concepto);
        Assert.Equal(TipoPago.Cheque, egreso.TipoPago);
        Assert.Equal(900m, egreso.Monto);
        Assert.Equal(ingreso.Id, egreso.MovimientoOrigenId);

        Assert.Equal(antes, await FotoAAsync()); // la caja cerrada no cambió
        var detB = await _service.ObtenerDetallesAperturaAsync(aperturaB.Id);
        Assert.Equal(200m, detB.CajaFisicaEsperada); // arqueo físico intacto
        Assert.Equal(200m - 900m, await _service.CalcularSaldoRealAsync(aperturaB.Id)); // el dinero que ya se había contado sale

        var datos = await LeerDatosAsync(venta.Id);
        Assert.True(datos.Rechazado);
        Assert.Equal(CajeroB, datos.RechazadoPor);
        _context.ChangeTracker.Clear();
        Assert.Equal(EstadoVenta.Confirmada, (await _context.Ventas.AsNoTracking().FirstAsync(v => v.Id == venta.Id)).Estado);
    }

    [Fact]
    public async Task Rechazar_Acreditado_AcreditadoPorLaVia_CajaCerrada_ContrapesaConSuPropioEgreso()
    {
        var venta = await SeedVentaChequeAsync(900m);
        var ingreso = await RegistrarIngresoAsync(venta);
        await CerrarAperturaAAsync();
        var aperturaB = SeedApertura(CajeroB, "CJB", 0m);
        await _service.AcreditarChequeAsync(ingreso.Id, CajeroB);

        await _service.RechazarChequeAsync(ingreso.Id, "rebotado", CajeroB);

        Assert.Equal(0m, await _service.CalcularSaldoRealAsync(aperturaB.Id)); // +900 acreditación, -900 rechazo
    }

    [Fact]
    public async Task Rechazar_EsIdempotente_UnSegundoIntentoFallaYNoDuplicaElEgreso()
    {
        var venta = await SeedVentaChequeAsync();
        var ingreso = await RegistrarIngresoAsync(venta);
        await _service.AcreditarChequeAsync(ingreso.Id, CajeroA);
        await CerrarAperturaAAsync();
        SeedApertura(CajeroB, "CJB", 0m);

        await _service.RechazarChequeAsync(ingreso.Id, "uno", CajeroB);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.RechazarChequeAsync(ingreso.Id, "dos", CajeroB));

        Assert.Equal(1, await _context.MovimientosCaja.CountAsync(m => m.Concepto == ConceptoMovimientoCaja.RechazoCheque));
        Assert.Single(_grabadora.Eventos, e => e.Accion == "RechazarCheque");
    }

    [Fact]
    public async Task Rechazado_NoSePuedeAcreditar_YAcreditadoNoSePuedeAcreditarDeNuevo()
    {
        var venta = await SeedVentaChequeAsync();
        var ingreso = await RegistrarIngresoAsync(venta);
        await _service.RechazarChequeAsync(ingreso.Id, "rebotado", CajeroA);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.AcreditarChequeAsync(ingreso.Id, CajeroA));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.RechazarChequeAsync(ingreso.Id, "otra vez", CajeroA));
    }

    [Fact]
    public async Task Rechazar_ChequeDeVentaCancelada_Falla()
    {
        var venta = await SeedVentaChequeAsync();
        var ingreso = await RegistrarIngresoAsync(venta);
        await _service.AcreditarChequeAsync(ingreso.Id, CajeroA);
        await _service.RegistrarContramovimientoVentaAsync(venta.Id, venta.Numero, "cancelada", CajeroA);
        var v = await _context.Ventas.FirstAsync(x => x.Id == venta.Id);
        v.Estado = EstadoVenta.Cancelada;
        await _context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.RechazarChequeAsync(ingreso.Id, "x", CajeroA));
        Assert.Contains("cancelada", ex.Message);
    }

    // ------------------------------------------------------------------ consultas de UI

    [Fact]
    public async Task ChequesPendientes_SeOrdenanPorVencimientoYMarcanPorVencer()
    {
        var lejano = await SeedVentaChequeAsync(100m, "1");
        var cercano = await SeedVentaChequeAsync(200m, "2");
        var datosCercano = await _context.DatosCheque.FirstAsync(d => d.VentaId == cercano.Id);
        datosCercano.FechaVencimiento = DateTime.UtcNow.Date.AddDays(3);
        var datosLejano = await _context.DatosCheque.FirstAsync(d => d.VentaId == lejano.Id);
        datosLejano.FechaVencimiento = DateTime.UtcNow.Date.AddDays(90);
        await _context.SaveChangesAsync();
        await RegistrarIngresoAsync(lejano);
        await RegistrarIngresoAsync(cercano);

        var lista = await _service.ObtenerChequesPendientesAsync();

        Assert.Equal(new[] { "2", "1" }, lista.Select(c => c.NumeroCheque).ToArray());
        Assert.True(lista[0].PorVencer);
        Assert.False(lista[1].PorVencer);
    }

    [Fact]
    public async Task ChequesPendientes_NoIncluyeAcreditadosNiRechazados_YElEstadoDeLaVentaRefleja()
    {
        var acreditado = await SeedVentaChequeAsync(100m, "11");
        var rechazado = await SeedVentaChequeAsync(100m, "22");
        var pendiente = await SeedVentaChequeAsync(100m, "33");
        var ia = await RegistrarIngresoAsync(acreditado);
        var ir = await RegistrarIngresoAsync(rechazado);
        await RegistrarIngresoAsync(pendiente);
        await _service.AcreditarChequeAsync(ia.Id, CajeroA);
        await _service.RechazarChequeAsync(ir.Id, "m", CajeroA);

        var lista = await _service.ObtenerChequesPendientesAsync();

        Assert.Equal("33", Assert.Single(lista).NumeroCheque);
        Assert.Equal("Acreditado", (await _service.ObtenerEstadoChequeVentaAsync(acreditado.Id))!.Estado);
        var est = (await _service.ObtenerEstadoChequeVentaAsync(rechazado.Id))!;
        Assert.True(est.Rechazado);
        Assert.Equal("Rechazado", est.Estado);
        Assert.Equal("Pendiente de acreditar", (await _service.ObtenerEstadoChequeVentaAsync(pendiente.Id))!.Estado);
    }

    // ------------------------------------------------------------------ D4: devolución

    [Fact]
    public async Task Devolucion_ConChequePendiente_BloqueaElReembolsoEnEfectivo()
    {
        var venta = await SeedVentaChequeAsync();
        await RegistrarIngresoAsync(venta);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.RegistrarMovimientoDevolucionAsync(1, venta.Id, venta.Numero, "DEV-1", 100m, CajeroA));

        Assert.Contains("todavía no fue acreditado", ex.Message);
    }

    [Fact]
    public async Task Devolucion_ConChequeAcreditado_PermiteElReembolso()
    {
        var venta = await SeedVentaChequeAsync();
        var ingreso = await RegistrarIngresoAsync(venta);
        await _service.AcreditarChequeAsync(ingreso.Id, CajeroA);

        var mov = await _service.RegistrarMovimientoDevolucionAsync(1, venta.Id, venta.Numero, "DEV-1", 100m, CajeroA);

        Assert.Equal(ConceptoMovimientoCaja.DevolucionCliente, mov.Concepto);
    }

    [Fact]
    public async Task Devolucion_ConChequeRechazado_NoHayReembolso()
    {
        var venta = await SeedVentaChequeAsync();
        var ingreso = await RegistrarIngresoAsync(venta);
        await _service.RechazarChequeAsync(ingreso.Id, "rebotado", CajeroA);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.RegistrarMovimientoDevolucionAsync(1, venta.Id, venta.Numero, "DEV-1", 100m, CajeroA));

        Assert.Contains("rechazado", ex.Message);
    }

    [Fact]
    public async Task Devolucion_ConChequeAcreditadoYLuegoRechazado_NoHayReembolso()
    {
        var venta = await SeedVentaChequeAsync();
        var ingreso = await RegistrarIngresoAsync(venta);
        await _service.AcreditarChequeAsync(ingreso.Id, CajeroA);
        await _service.RechazarChequeAsync(ingreso.Id, "rebotado", CajeroA);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.RegistrarMovimientoDevolucionAsync(1, venta.Id, venta.Numero, "DEV-1", 100m, CajeroA));
    }

    [Fact]
    public async Task Devolucion_VentaSinCheque_NoSeAfecta()
    {
        var venta = await SeedVentaChequeAsync();
        venta.TipoPago = TipoPago.Efectivo;
        await _context.SaveChangesAsync();
        await _service.RegistrarMovimientoVentaAsync(venta.Id, venta.Numero, venta.Total, TipoPago.Efectivo, CajeroA);

        var mov = await _service.RegistrarMovimientoDevolucionAsync(1, venta.Id, venta.Numero, "DEV-1", 100m, CajeroA);

        Assert.Equal(TipoMovimientoCaja.Egreso, mov.Tipo);
    }

    // ------------------------------------------------------------------ D5: cuota con cheque

    [Fact]
    public async Task CuotaConCheque_CajaLoRechazaComoDefensaEnProfundidad_SinMovimientos()
    {
        // D5 (propietario): las cuotas de crédito no aceptan cheques; la caja no asienta un cobro de cuota con cheque.
        var antes = await _context.MovimientosCaja.CountAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.RegistrarMovimientoCuotaAsync(
            cuotaId: 7, creditoNumero: "CR-1", numeroCuota: 1, montoBase: 500m, recargoMedioPago: 0m,
            tipoPago: TipoPago.Cheque, medioPago: "Cheque", usuario: CajeroA, aperturaIdCandidata: _aperturaA.Id));

        Assert.Contains("No se aceptan cheques", ex.Message);
        Assert.Equal(antes, await _context.MovimientosCaja.CountAsync());
    }

    [Theory]
    [InlineData("Cheque")]
    [InlineData("cheque")]
    [InlineData("  CHEQUE ")]
    [InlineData("4")]
    public async Task CuotaConCheque_SobrecargaLegacyConTextoDelMedio_TambienSeRechaza(string medio)
    {
        var antes = await _context.MovimientosCaja.CountAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.RegistrarMovimientoCuotaAsync(
            cuotaId: 7, creditoNumero: "CR-1", numeroCuota: 1, monto: 500m, medioPago: medio, usuario: CajeroA));

        Assert.Contains("No se aceptan cheques", ex.Message);
        Assert.Equal(antes, await _context.MovimientosCaja.CountAsync());
    }

    [Theory]
    [InlineData("Cheque")]
    [InlineData("cheque")]
    [InlineData("4")]
    public async Task CuotaConCheque_EnumNuloYTextoDelMedio_SeRechaza(string medio)
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.RegistrarMovimientoCuotaAsync(
            cuotaId: 7, creditoNumero: "CR-1", numeroCuota: 1, montoBase: 500m, recargoMedioPago: 0m,
            tipoPago: null, medioPago: medio, usuario: CajeroA, aperturaIdCandidata: _aperturaA.Id));
        Assert.Contains("No se aceptan cheques", ex.Message);
    }

    [Fact]
    public async Task ChequeDuplicados_UnChequeHistoricoDeCuotaSeDetectaPorSuDetalle_SoloLectura()
    {
        _context.MovimientosCaja.Add(new MovimientoCaja
        {
            AperturaCajaId = _aperturaA.Id, Tipo = TipoMovimientoCaja.Ingreso, Concepto = ConceptoMovimientoCaja.CobroCuota,
            TipoPago = TipoPago.Cheque, EstadoAcreditacion = EstadoAcreditacionMovimientoCaja.Pendiente, Monto = 500m,
            Descripcion = "historico", Usuario = CajeroA, FechaMovimiento = DateTime.UtcNow,
            MedioPagoDetalle = ChequeDuplicados.Detalle("Galicia", "99", DateTime.UtcNow.AddDays(10))
        });
        await _context.SaveChangesAsync();

        Assert.True(await ChequeDuplicados.ExisteEnCuotasAsync(_context, " gALICIA ", " 99 "));
        Assert.False(await ChequeDuplicados.ExisteEnCuotasAsync(_context, "Galicia", "9"));
        Assert.False(await ChequeDuplicados.ExisteEnCuotasAsync(_context, "Macro", "99"));
    }

    [Fact]
    public async Task CuotaConEfectivo_NoCambia_SigueSiendoDineroRealInmediato()
    {
        var mov = await _service.RegistrarMovimientoCuotaAsync(
            cuotaId: 7, creditoNumero: "CR-1", numeroCuota: 1, montoBase: 500m, recargoMedioPago: 0m,
            tipoPago: TipoPago.Efectivo, medioPago: "Efectivo", usuario: CajeroA, aperturaIdCandidata: _aperturaA.Id);

        Assert.Null(mov!.EstadoAcreditacion);
        Assert.Equal(1500m, await _service.CalcularSaldoRealAsync(_aperturaA.Id));
    }

    // ------------------------------------------------------------------ cancelacion x rechazo: saldos neutros (total y real)

    [Theory]
    [InlineData(false, false)] // pendiente, no rechazado
    [InlineData(false, true)]  // pendiente, rechazado
    [InlineData(true, false)]  // acreditado, no rechazado
    [InlineData(true, true)]   // acreditado, rechazado
    public async Task CancelarVentaConCheque_CajaAbierta_DejaNeutrosElSaldoTotalYElReal(bool acreditado, bool rechazado)
    {
        var venta = await SeedVentaChequeAsync(100m);
        var ingreso = await RegistrarIngresoAsync(venta);
        if (acreditado) await _service.AcreditarChequeAsync(ingreso.Id, CajeroA);
        if (rechazado) await _service.RechazarChequeAsync(ingreso.Id, "rebotado", CajeroA);

        await _service.RegistrarContramovimientoVentaAsync(venta.Id, venta.Numero, "cancelada", CajeroA);

        Assert.Equal(1000m, await _service.CalcularSaldoActualAsync(_aperturaA.Id)); // fondo inicial: neto 0
        Assert.Equal(1000m, await _service.CalcularSaldoRealAsync(_aperturaA.Id));
        var reversiones = await _context.MovimientosCaja.CountAsync(m => m.Concepto == ConceptoMovimientoCaja.ReversionVenta);
        Assert.Equal(rechazado ? 0 : 1, reversiones); // un cheque rechazado ya tiene su salida: no se duplica
        var det = await _service.ObtenerDetallesAperturaAsync(_aperturaA.Id);
        Assert.Equal(0m, det.SaldoPendienteAcreditacion);
        Assert.Equal(1000m, det.CajaFisicaEsperada);
    }

    [Fact]
    public async Task CancelarVenta_DespuesDeRechazarUnAcreditado_NoDuplicaLaSalida_ElSaldoRealNoQuedaNegativo()
    {
        var venta = await SeedVentaChequeAsync(100m);
        var ingreso = await RegistrarIngresoAsync(venta);
        await _service.AcreditarChequeAsync(ingreso.Id, CajeroA);
        await _service.RechazarChequeAsync(ingreso.Id, "rebotado", CajeroA);

        await _service.RegistrarContramovimientoVentaAsync(venta.Id, venta.Numero, "cancelada", CajeroA);

        Assert.True(await _service.CalcularSaldoRealAsync(_aperturaA.Id) >= 1000m);
        Assert.Equal(1, await _context.MovimientosCaja.CountAsync(m => m.Tipo == TipoMovimientoCaja.Egreso));
    }

    // ------------------------------------------------------------------ normalización de la clave del cheque (ventas)

    [Fact]
    public void ChequeDuplicados_NormalizaBancoYNumeroParaLaClaveDeDuplicado()
    {
        Assert.Equal("BANCO DE LA NACION", ChequeDuplicados.Normalizar("  banco   de la	nacion "));
        Assert.Equal(ChequeDuplicados.Clave("Galicia", "99"), ChequeDuplicados.Clave("  gALICIA ", " 99 "));
        Assert.NotEqual(ChequeDuplicados.Clave("Galicia", "99"), ChequeDuplicados.Clave("Galicia", "9"));
    }
}
