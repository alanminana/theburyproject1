using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Models;
using TheBuryProject.Tests.Helpers;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// D-03: libro administrativo de cuentas por pagar. Servicio real sobre SQLite en memoria.
/// Cubre AC-03-1 (alta y unicidad), AC-03-2 (pago e imputación), AC-03-4 (anulación), AC-03-5 (notas de crédito),
/// AC-03-6 (estado de cuenta y antigüedad) y AC-03-8 (no regresión de Caja, stock, costo e IVA).
/// La concurrencia (AC-03-3) se prueba contra SQL Server en <c>Concurrency/CuentaPorPagarSqlServerTests</c>.
/// </summary>
public class CuentaPorPagarServiceTests : IDisposable
{
    private static readonly DateOnly Hoy = new(2026, 11, 14);

    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly RelojComercialFake _reloj = new(Hoy);
    private readonly CuentaPorPagarService _service;
    private static long _seq = 20_000_000_000;

    public CuentaPorPagarServiceTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();
        _context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        _context.Database.EnsureCreated();
        _service = new CuentaPorPagarService(_context, _reloj, NullLogger<CuentaPorPagarService>.Instance);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // ── helpers ─────────────────────────────────────────────────────────

    private async Task<Proveedor> SeedProveedorAsync(string razon = "Proveedor Uno")
    {
        var p = new Proveedor { Cuit = Interlocked.Increment(ref _seq).ToString(), RazonSocial = razon, Activo = true };
        _context.Proveedores.Add(p);
        await _context.SaveChangesAsync();
        return p;
    }

    private static ComprobanteProveedorRequest Factura(int proveedorId, string numero = "A-0001-00000001",
        decimal total = 1210m, DateTime? emision = null, DateTime? vencimiento = null,
        TipoComprobanteProveedor tipo = TipoComprobanteProveedor.Factura)
    {
        var neto = Math.Round(total / 1.21m, 2);
        return new ComprobanteProveedorRequest
        {
            ProveedorId = proveedorId, Tipo = tipo, NumeroComprobante = numero,
            FechaEmision = emision ?? (vencimiento.HasValue && vencimiento.Value < new DateTime(2026, 11, 1)
                ? vencimiento.Value.AddDays(-5) : new DateTime(2026, 11, 1)),
            FechaVencimiento = vencimiento,
            ImporteNeto = neto, ImporteIva = total - neto, ImporteTotal = total
        };
    }

    private async Task<int> NuevoComprobanteAsync(int proveedorId, string numero, decimal total,
        TipoComprobanteProveedor tipo = TipoComprobanteProveedor.Factura, DateTime? vencimiento = null)
    {
        var r = await _service.RegistrarComprobanteAsync(Factura(proveedorId, numero, total, vencimiento: vencimiento, tipo: tipo), "tester");
        Assert.True(r.Ok, r.Error);
        return r.Id!.Value;
    }

    private static PagoProveedorRequest Pago(int proveedorId, decimal monto, params (int Id, decimal Monto)[] imputaciones) => new()
    {
        ProveedorId = proveedorId, Fecha = new DateTime(2026, 11, 10), Medio = MedioPagoProveedor.Transferencia, Monto = monto,
        Referencia = "TRF-1",
        Imputaciones = imputaciones.Select(i => new ImputacionRequest { ComprobanteId = i.Id, Monto = i.Monto }).ToList()
    };

    private async Task<ComprobanteProveedor> LeerAsync(int id) =>
        await _context.ComprobantesProveedor.AsNoTracking().SingleAsync(c => c.Id == id);

    // ── AC-03-1: alta, unicidad e importes ──────────────────────────────

    [Fact]
    public async Task Registrar_PersisteComprobantePendienteConSaldoIgualAlTotalYNumeroNormalizado()
    {
        var prov = await SeedProveedorAsync();

        var r = await _service.RegistrarComprobanteAsync(Factura(prov.Id, "  a-0001-1 ", 1210m, vencimiento: new DateTime(2026, 12, 1)), "tester");

        Assert.True(r.Ok, r.Error);
        var c = await LeerAsync(r.Id!.Value);
        Assert.Equal("A-0001-1", c.NumeroComprobante);
        Assert.Equal(EstadoComprobanteProveedor.Pendiente, c.Estado);
        Assert.Equal(1210m, c.SaldoPendiente);
        Assert.Equal(1000m, c.ImporteNeto);
        Assert.Equal(210m, c.ImporteIva);
        Assert.Equal("tester", c.CreatedBy);
        Assert.Equal(new DateTime(2026, 12, 1), c.FechaVencimiento);
    }

    [Fact]
    public async Task Registrar_DuplicadoMismoProveedorTipoYNumero_SeRechazaSinPersistir()
    {
        var prov = await SeedProveedorAsync();
        await NuevoComprobanteAsync(prov.Id, "F-1", 100m);

        var r = await _service.RegistrarComprobanteAsync(Factura(prov.Id, "f-1", 100m), "tester");

        Assert.False(r.Ok);
        Assert.Contains("Ya existe", r.Error);
        Assert.Equal(1, await _context.ComprobantesProveedor.CountAsync());
    }

    [Fact]
    public async Task Registrar_MismoNumeroEnOtroTipoOtroProveedorOTrasAnular_SePermite()
    {
        var p1 = await SeedProveedorAsync("P1");
        var p2 = await SeedProveedorAsync("P2");
        var f = await NuevoComprobanteAsync(p1.Id, "X-1", 100m);

        Assert.True((await _service.RegistrarComprobanteAsync(Factura(p1.Id, "X-1", 100m, tipo: TipoComprobanteProveedor.NotaCredito), "t")).Ok);
        Assert.True((await _service.RegistrarComprobanteAsync(Factura(p2.Id, "X-1", 100m), "t")).Ok);

        Assert.True((await _service.AnularComprobanteAsync(f, "carga errónea", "t")).Ok);
        Assert.True((await _service.RegistrarComprobanteAsync(Factura(p1.Id, "X-1", 100m), "t")).Ok);
    }

    [Theory]
    [InlineData(1000, 210, 1210, true)]
    [InlineData(1000, 210, 1210.01, true)]   // tolerancia: exactamente 0,01
    [InlineData(1000, 210, 1209.99, true)]
    [InlineData(1000, 210, 1210.02, false)]  // fuera de tolerancia
    [InlineData(1000, 210, 1209.98, false)]
    [InlineData(1000, 0, 1000, true)]        // sin IVA informado es válido
    public async Task Registrar_NetoMasIvaContraTotal_RespetaLaTolerancia(double neto, double iva, double total, bool esperado)
    {
        var prov = await SeedProveedorAsync();
        var req = Factura(prov.Id, "T-1", 1m);
        req.ImporteNeto = (decimal)neto;
        req.ImporteIva = (decimal)iva;
        req.ImporteTotal = (decimal)total;

        var r = await _service.RegistrarComprobanteAsync(req, "t");

        Assert.Equal(esperado, r.Ok);
        Assert.Equal(esperado ? 1 : 0, await _context.ComprobantesProveedor.CountAsync());
    }

    [Fact]
    public async Task Registrar_VencimientoAnteriorALaEmision_SeRechazaPeroElMismoDiaSePermite()
    {
        var prov = await SeedProveedorAsync();
        var emision = new DateTime(2026, 11, 5);

        var anterior = await _service.RegistrarComprobanteAsync(Factura(prov.Id, "V-1", emision: emision, vencimiento: emision.AddDays(-1)), "t");
        var mismoDia = await _service.RegistrarComprobanteAsync(Factura(prov.Id, "V-2", emision: emision, vencimiento: emision), "t");

        Assert.False(anterior.Ok);
        Assert.Contains("vencimiento", anterior.Error);
        Assert.True(mismoDia.Ok, mismoDia.Error);
    }

    [Theory]
    [InlineData("", 100)]
    [InlineData("   ", 100)]
    [InlineData("F-1", 0)]
    [InlineData("F-1", -5)]
    public async Task Registrar_DatosObligatoriosInvalidos_SeRechaza(string numero, double total)
    {
        var prov = await SeedProveedorAsync();
        var req = Factura(prov.Id, numero, (decimal)total);
        req.ImporteNeto = (decimal)total;
        req.ImporteIva = 0m;

        var r = await _service.RegistrarComprobanteAsync(req, "t");

        Assert.False(r.Ok);
    }

    [Fact]
    public async Task Registrar_ImporteConMasDeDosDecimalesOSinFecha_SeRechaza()
    {
        var prov = await SeedProveedorAsync();
        var tresDecimales = Factura(prov.Id, "D-1", 100m);
        tresDecimales.ImporteTotal = 100.005m;
        tresDecimales.ImporteNeto = 100.005m;
        tresDecimales.ImporteIva = 0m;
        var sinFecha = Factura(prov.Id, "D-2", 100m);
        sinFecha.FechaEmision = null;

        Assert.False((await _service.RegistrarComprobanteAsync(tresDecimales, "t")).Ok);
        Assert.False((await _service.RegistrarComprobanteAsync(sinFecha, "t")).Ok);
    }

    [Fact]
    public async Task Registrar_ProveedorInexistenteOOrdenDeOtroProveedor_SeRechaza()
    {
        var p1 = await SeedProveedorAsync("P1");
        var p2 = await SeedProveedorAsync("P2");
        var oc = new OrdenCompra { Numero = "OC-1", ProveedorId = p1.Id, Estado = EstadoOrdenCompra.Confirmada, FechaEmision = DateTime.UtcNow };
        _context.OrdenesCompra.Add(oc);
        await _context.SaveChangesAsync();

        var sinProveedor = await _service.RegistrarComprobanteAsync(Factura(99999), "t");
        var ocAjena = Factura(p2.Id, "O-1");
        ocAjena.OrdenCompraId = oc.Id;
        var ocPropia = Factura(p1.Id, "O-2");
        ocPropia.OrdenCompraId = oc.Id;

        Assert.False(sinProveedor.Ok);
        Assert.False((await _service.RegistrarComprobanteAsync(ocAjena, "t")).Ok);
        var ok = await _service.RegistrarComprobanteAsync(ocPropia, "t");
        Assert.True(ok.Ok, ok.Error);
        Assert.Equal(oc.Id, (await LeerAsync(ok.Id!.Value)).OrdenCompraId);
    }

    // ── AC-03-2: pago e imputación ──────────────────────────────────────

    [Fact]
    public async Task Pago_ParcialYLuegoTotal_RecalculaSaldoYEstado()
    {
        var prov = await SeedProveedorAsync();
        var f = await NuevoComprobanteAsync(prov.Id, "F-1", 1000m);

        var parcial = await _service.RegistrarPagoAsync(Pago(prov.Id, 400m, (f, 400m)), "t");
        Assert.True(parcial.Ok, parcial.Error);
        var c1 = await LeerAsync(f);
        Assert.Equal(600m, c1.SaldoPendiente);
        Assert.Equal(EstadoComprobanteProveedor.ParcialmentePagado, c1.Estado);

        var resto = await _service.RegistrarPagoAsync(Pago(prov.Id, 600m, (f, 600m)), "t");
        Assert.True(resto.Ok, resto.Error);
        var c2 = await LeerAsync(f);
        Assert.Equal(0m, c2.SaldoPendiente);
        Assert.Equal(EstadoComprobanteProveedor.Pagado, c2.Estado);
        Assert.Equal(2, await _context.PagosProveedor.CountAsync());
    }

    [Fact]
    public async Task Pago_ImputadoAVariosComprobantes_AplicaCadaImporte()
    {
        var prov = await SeedProveedorAsync();
        var f1 = await NuevoComprobanteAsync(prov.Id, "F-1", 500m);
        var f2 = await NuevoComprobanteAsync(prov.Id, "F-2", 700m);

        var r = await _service.RegistrarPagoAsync(Pago(prov.Id, 800m, (f1, 500m), (f2, 300m)), "t");

        Assert.True(r.Ok, r.Error);
        Assert.Equal(EstadoComprobanteProveedor.Pagado, (await LeerAsync(f1)).Estado);
        var c2 = await LeerAsync(f2);
        Assert.Equal(400m, c2.SaldoPendiente);
        Assert.Equal(EstadoComprobanteProveedor.ParcialmentePagado, c2.Estado);
        Assert.Equal(2, await _context.PagoProveedorImputaciones.CountAsync());
    }

    [Fact]
    public async Task Pago_ImputacionMayorAlSaldo_SeRechazaYNoPersisteNada()
    {
        var prov = await SeedProveedorAsync();
        var f = await NuevoComprobanteAsync(prov.Id, "F-1", 1000m);
        Assert.True((await _service.RegistrarPagoAsync(Pago(prov.Id, 700m, (f, 700m)), "t")).Ok);

        var r = await _service.RegistrarPagoAsync(Pago(prov.Id, 400m, (f, 400m)), "t");

        Assert.False(r.Ok);
        Assert.Contains("supera su saldo", r.Error);
        Assert.Equal(1, await _context.PagosProveedor.CountAsync());
        Assert.Equal(300m, (await LeerAsync(f)).SaldoPendiente);
    }

    [Fact]
    public async Task Pago_LaSumaDeImputacionesDebeIgualarElMonto()
    {
        var prov = await SeedProveedorAsync();
        var f = await NuevoComprobanteAsync(prov.Id, "F-1", 1000m);

        var mayor = await _service.RegistrarPagoAsync(Pago(prov.Id, 300m, (f, 400m)), "t");
        var menor = await _service.RegistrarPagoAsync(Pago(prov.Id, 500m, (f, 400m)), "t");
        var sinImputar = await _service.RegistrarPagoAsync(Pago(prov.Id, 500m), "t");

        Assert.False(mayor.Ok);
        Assert.Contains("superan el monto", mayor.Error);
        Assert.False(menor.Ok);
        Assert.False(sinImputar.Ok);
        Assert.Equal(0, await _context.PagosProveedor.CountAsync());
        Assert.Equal(1000m, (await LeerAsync(f)).SaldoPendiente);
    }

    [Fact]
    public async Task Pago_ComprobanteDeOtroProveedorNotaDeCreditoOAnulado_SeRechaza()
    {
        var p1 = await SeedProveedorAsync("P1");
        var p2 = await SeedProveedorAsync("P2");
        var ajena = await NuevoComprobanteAsync(p2.Id, "F-AJ", 100m);
        var nc = await NuevoComprobanteAsync(p1.Id, "NC-1", 100m, TipoComprobanteProveedor.NotaCredito);
        var anulada = await NuevoComprobanteAsync(p1.Id, "F-AN", 100m);
        Assert.True((await _service.AnularComprobanteAsync(anulada, "error", "t")).Ok);

        Assert.False((await _service.RegistrarPagoAsync(Pago(p1.Id, 100m, (ajena, 100m)), "t")).Ok);
        Assert.False((await _service.RegistrarPagoAsync(Pago(p1.Id, 100m, (nc, 100m)), "t")).Ok);
        Assert.False((await _service.RegistrarPagoAsync(Pago(p1.Id, 100m, (anulada, 100m)), "t")).Ok);
        Assert.False((await _service.RegistrarPagoAsync(Pago(p1.Id, 100m, (123456, 100m)), "t")).Ok);
        Assert.Equal(0, await _context.PagosProveedor.CountAsync());
    }

    [Fact]
    public async Task Pago_MontoInvalidoMismoComprobanteDosVecesOImporteNegativo_SeRechaza()
    {
        var prov = await SeedProveedorAsync();
        var f = await NuevoComprobanteAsync(prov.Id, "F-1", 1000m);

        Assert.False((await _service.RegistrarPagoAsync(Pago(prov.Id, 0m, (f, 0m)), "t")).Ok);
        Assert.False((await _service.RegistrarPagoAsync(Pago(prov.Id, -10m, (f, -10m)), "t")).Ok);
        Assert.False((await _service.RegistrarPagoAsync(Pago(prov.Id, 100m, (f, 50m), (f, 50m)), "t")).Ok);
        Assert.False((await _service.RegistrarPagoAsync(Pago(prov.Id, 100.001m, (f, 100.001m)), "t")).Ok);
        Assert.Equal(0, await _context.PagosProveedor.CountAsync());
    }

    [Fact]
    public async Task Pago_FilasSinImporteSeIgnoran()
    {
        var prov = await SeedProveedorAsync();
        var f1 = await NuevoComprobanteAsync(prov.Id, "F-1", 100m);
        var f2 = await NuevoComprobanteAsync(prov.Id, "F-2", 100m);
        var req = Pago(prov.Id, 100m, (f1, 100m));
        req.Imputaciones.Add(new ImputacionRequest { ComprobanteId = f2, Monto = null });
        req.Imputaciones.Add(new ImputacionRequest { ComprobanteId = 0, Monto = 0m });

        var r = await _service.RegistrarPagoAsync(req, "t");

        Assert.True(r.Ok, r.Error);
        Assert.Equal(100m, (await LeerAsync(f2)).SaldoPendiente);
    }

    [Fact]
    public async Task Pago_ConChequeDelProveedor_LoReferenciaSinCambiarSuEstado()
    {
        var prov = await SeedProveedorAsync();
        var otro = await SeedProveedorAsync("Otro");
        var f = await NuevoComprobanteAsync(prov.Id, "F-1", 500m);
        var cheque = new Cheque { Numero = "0001", Banco = "Banco", Monto = 500m, ProveedorId = prov.Id, Estado = EstadoCheque.Emitido };
        var ajeno = new Cheque { Numero = "0002", Banco = "Banco", Monto = 500m, ProveedorId = otro.Id, Estado = EstadoCheque.Emitido };
        _context.Cheques.AddRange(cheque, ajeno);
        await _context.SaveChangesAsync();

        var okReq = Pago(prov.Id, 500m, (f, 500m));
        okReq.Medio = MedioPagoProveedor.Cheque;
        okReq.ChequeId = cheque.Id;
        var ajenoReq = Pago(prov.Id, 100m, (f, 100m));
        ajenoReq.Medio = MedioPagoProveedor.Cheque;
        ajenoReq.ChequeId = ajeno.Id;
        var medioIncorrecto = Pago(prov.Id, 100m, (f, 100m));
        medioIncorrecto.ChequeId = cheque.Id;

        Assert.False((await _service.RegistrarPagoAsync(ajenoReq, "t")).Ok);
        Assert.False((await _service.RegistrarPagoAsync(medioIncorrecto, "t")).Ok);
        var r = await _service.RegistrarPagoAsync(okReq, "t");

        Assert.True(r.Ok, r.Error);
        Assert.Equal(cheque.Id, (await _context.PagosProveedor.AsNoTracking().SingleAsync()).ChequeId);
        Assert.Equal(EstadoCheque.Emitido, (await _context.Cheques.AsNoTracking().SingleAsync(c => c.Id == cheque.Id)).Estado);
    }

    [Fact]
    public async Task Pago_ReenviarElMismoFormulario_NoRegistraUnSegundoPago()
    {
        var prov = await SeedProveedorAsync();
        var f = await NuevoComprobanteAsync(prov.Id, "F-1", 1000m);
        var req = Pago(prov.Id, 400m, (f, 400m));
        req.ClaveIdempotencia = "formulario-1";

        var primero = await _service.RegistrarPagoAsync(req, "t");
        var repetido = await _service.RegistrarPagoAsync(req, "t");

        Assert.True(primero.Ok && repetido.Ok);
        Assert.False(primero.YaRegistrado);
        Assert.True(repetido.YaRegistrado);
        Assert.Equal(primero.Id, repetido.Id);
        Assert.Equal(1, await _context.PagosProveedor.CountAsync());
        Assert.Equal(600m, (await LeerAsync(f)).SaldoPendiente);
    }

    // ── AC-03-4: anulación ──────────────────────────────────────────────

    [Fact]
    public async Task AnularPago_RestituyeSaldosYQuedaTrazadoSinBorrarHistoria()
    {
        var prov = await SeedProveedorAsync();
        var f1 = await NuevoComprobanteAsync(prov.Id, "F-1", 500m);
        var f2 = await NuevoComprobanteAsync(prov.Id, "F-2", 700m);
        var pagoId = (await _service.RegistrarPagoAsync(Pago(prov.Id, 800m, (f1, 500m), (f2, 300m)), "t")).Id!.Value;

        var r = await _service.AnularPagoAsync(pagoId, "transferencia rechazada", "supervisor");

        Assert.True(r.Ok, r.Error);
        var c1 = await LeerAsync(f1);
        var c2 = await LeerAsync(f2);
        Assert.Equal((500m, EstadoComprobanteProveedor.Pendiente), (c1.SaldoPendiente, c1.Estado));
        Assert.Equal((700m, EstadoComprobanteProveedor.Pendiente), (c2.SaldoPendiente, c2.Estado));
        var pago = await _context.PagosProveedor.AsNoTracking().SingleAsync();
        Assert.True(pago.Anulado);
        Assert.Equal("supervisor", pago.AnuladoPor);
        Assert.Equal("transferencia rechazada", pago.MotivoAnulacion);
        Assert.NotNull(pago.FechaAnulacion);
        Assert.Equal(2, await _context.PagoProveedorImputaciones.CountAsync()); // historia conservada
    }

    [Fact]
    public async Task AnularPago_SinMotivoOYaAnulado_SeRechaza_YPermiteVolverAPagar()
    {
        var prov = await SeedProveedorAsync();
        var f = await NuevoComprobanteAsync(prov.Id, "F-1", 1000m);
        var pagoId = (await _service.RegistrarPagoAsync(Pago(prov.Id, 1000m, (f, 1000m)), "t")).Id!.Value;

        Assert.False((await _service.AnularPagoAsync(pagoId, "  ", "t")).Ok);
        Assert.True((await _service.AnularPagoAsync(pagoId, "error", "t")).Ok);
        var segunda = await _service.AnularPagoAsync(pagoId, "otra vez", "t");
        Assert.False(segunda.Ok);
        Assert.Contains("ya está anulado", segunda.Error);
        Assert.False((await _service.AnularPagoAsync(987654, "x", "t")).Ok);

        var nuevo = await _service.RegistrarPagoAsync(Pago(prov.Id, 1000m, (f, 1000m)), "t");
        Assert.True(nuevo.Ok, nuevo.Error);
        Assert.Equal(EstadoComprobanteProveedor.Pagado, (await LeerAsync(f)).Estado);
    }

    [Fact]
    public async Task AnularComprobante_SoloSinImputacionesVigentes_ConMotivoYSinBorrar()
    {
        var prov = await SeedProveedorAsync();
        var f = await NuevoComprobanteAsync(prov.Id, "F-1", 1000m);
        var pagoId = (await _service.RegistrarPagoAsync(Pago(prov.Id, 100m, (f, 100m)), "t")).Id!.Value;

        var conPago = await _service.AnularComprobanteAsync(f, "carga errónea", "t");
        Assert.False(conPago.Ok);
        Assert.Contains("pagos o notas de crédito aplicados", conPago.Error);

        Assert.True((await _service.AnularPagoAsync(pagoId, "error", "t")).Ok);
        Assert.False((await _service.AnularComprobanteAsync(f, "", "t")).Ok);
        var ok = await _service.AnularComprobanteAsync(f, "carga errónea", "anulador");

        Assert.True(ok.Ok, ok.Error);
        var c = await LeerAsync(f);
        Assert.Equal(EstadoComprobanteProveedor.Anulado, c.Estado);
        Assert.Equal(0m, c.SaldoPendiente);
        Assert.Equal("anulador", c.AnuladoPor);
        Assert.Equal("carga errónea", c.MotivoAnulacion);
        Assert.False((await _service.AnularComprobanteAsync(f, "otra", "t")).Ok);
        Assert.Equal(1, await _context.ComprobantesProveedor.CountAsync()); // no se borra
    }

    // ── AC-03-5: notas de crédito ───────────────────────────────────────

    [Fact]
    public async Task NotaDeCredito_SeAplicaAFacturasDelMismoProveedorReduciendoSaldoDeAmbas()
    {
        var prov = await SeedProveedorAsync();
        var f = await NuevoComprobanteAsync(prov.Id, "F-1", 1000m);
        var nc = await NuevoComprobanteAsync(prov.Id, "NC-1", 300m, TipoComprobanteProveedor.NotaCredito);

        var r = await _service.ImputarNotaCreditoAsync(nc, new[] { new ImputacionRequest { ComprobanteId = f, Monto = 200m } }, null, "t");

        Assert.True(r.Ok, r.Error);
        var cf = await LeerAsync(f);
        Assert.Equal((800m, EstadoComprobanteProveedor.ParcialmentePagado), (cf.SaldoPendiente, cf.Estado));
        var cnc = await LeerAsync(nc);
        Assert.Equal((100m, EstadoComprobanteProveedor.ParcialmentePagado), (cnc.SaldoPendiente, cnc.Estado));

        var resto = await _service.ImputarNotaCreditoAsync(nc, new[] { new ImputacionRequest { ComprobanteId = f, Monto = 100m } }, null, "t");
        Assert.True(resto.Ok, resto.Error);
        Assert.Equal(EstadoComprobanteProveedor.Pagado, (await LeerAsync(nc)).Estado);
        Assert.Equal(700m, (await LeerAsync(f)).SaldoPendiente);
    }

    [Fact]
    public async Task NotaDeCredito_NoPuedeSuperarElSaldoDeLaNotaNiElDeLaFactura()
    {
        var prov = await SeedProveedorAsync();
        var f = await NuevoComprobanteAsync(prov.Id, "F-1", 1000m);
        var chica = await NuevoComprobanteAsync(prov.Id, "F-2", 100m);
        var nc = await NuevoComprobanteAsync(prov.Id, "NC-1", 300m, TipoComprobanteProveedor.NotaCredito);

        var masQueLaNota = await _service.ImputarNotaCreditoAsync(nc, new[] { new ImputacionRequest { ComprobanteId = f, Monto = 300.01m } }, null, "t");
        var variasMasQueLaNota = await _service.ImputarNotaCreditoAsync(nc, new[]
        {
            new ImputacionRequest { ComprobanteId = f, Monto = 200m },
            new ImputacionRequest { ComprobanteId = chica, Monto = 100m + 0.01m }
        }, null, "t");
        var masQueLaFactura = await _service.ImputarNotaCreditoAsync(nc, new[] { new ImputacionRequest { ComprobanteId = chica, Monto = 100.01m } }, null, "t");

        Assert.False(masQueLaNota.Ok);
        Assert.Contains("saldo de la nota de crédito", masQueLaNota.Error);
        Assert.False(variasMasQueLaNota.Ok);
        Assert.False(masQueLaFactura.Ok);
        Assert.Equal(0, await _context.ImputacionesNotaCredito.CountAsync());
        Assert.Equal(300m, (await LeerAsync(nc)).SaldoPendiente);
    }

    [Fact]
    public async Task NotaDeCredito_NoSeAplicaAOtroProveedorANotasDeCreditoNiDesdeUnaFactura()
    {
        var p1 = await SeedProveedorAsync("P1");
        var p2 = await SeedProveedorAsync("P2");
        var nc = await NuevoComprobanteAsync(p1.Id, "NC-1", 300m, TipoComprobanteProveedor.NotaCredito);
        var nc2 = await NuevoComprobanteAsync(p1.Id, "NC-2", 50m, TipoComprobanteProveedor.NotaCredito);
        var ajena = await NuevoComprobanteAsync(p2.Id, "F-AJ", 500m);
        var propia = await NuevoComprobanteAsync(p1.Id, "F-1", 500m);

        Assert.False((await _service.ImputarNotaCreditoAsync(nc, new[] { new ImputacionRequest { ComprobanteId = ajena, Monto = 100m } }, null, "t")).Ok);
        Assert.False((await _service.ImputarNotaCreditoAsync(nc, new[] { new ImputacionRequest { ComprobanteId = nc2, Monto = 10m } }, null, "t")).Ok);
        Assert.False((await _service.ImputarNotaCreditoAsync(propia, new[] { new ImputacionRequest { ComprobanteId = propia, Monto = 10m } }, null, "t")).Ok);
        Assert.False((await _service.ImputarNotaCreditoAsync(nc, new[] { new ImputacionRequest { ComprobanteId = nc, Monto = 10m } }, null, "t")).Ok);
        Assert.False((await _service.ImputarNotaCreditoAsync(nc, Array.Empty<ImputacionRequest>(), null, "t")).Ok);
        Assert.Equal(0, await _context.ImputacionesNotaCredito.CountAsync());
    }

    [Fact]
    public async Task NotaDeCredito_ReenviarElMismoFormulario_NoDuplicaLaAplicacion()
    {
        var prov = await SeedProveedorAsync();
        var f = await NuevoComprobanteAsync(prov.Id, "F-1", 1000m);
        var nc = await NuevoComprobanteAsync(prov.Id, "NC-1", 300m, TipoComprobanteProveedor.NotaCredito);
        var imp = new[] { new ImputacionRequest { ComprobanteId = f, Monto = 100m } };

        var primero = await _service.ImputarNotaCreditoAsync(nc, imp, "clave-1", "t");
        var repetido = await _service.ImputarNotaCreditoAsync(nc, imp, "clave-1", "t");

        Assert.True(primero.Ok && repetido.Ok);
        Assert.True(repetido.YaRegistrado);
        Assert.Equal(1, await _context.ImputacionesNotaCredito.CountAsync());
        Assert.Equal(900m, (await LeerAsync(f)).SaldoPendiente);
    }

    [Fact]
    public async Task NotaDeCredito_AnularLaAplicacionRestituyeAmbosSaldos_YBloqueaAnularLaNotaMientrasEstaAplicada()
    {
        var prov = await SeedProveedorAsync();
        var f = await NuevoComprobanteAsync(prov.Id, "F-1", 1000m);
        var nc = await NuevoComprobanteAsync(prov.Id, "NC-1", 300m, TipoComprobanteProveedor.NotaCredito);
        Assert.True((await _service.ImputarNotaCreditoAsync(nc, new[] { new ImputacionRequest { ComprobanteId = f, Monto = 300m } }, null, "t")).Ok);
        var impId = (await _context.ImputacionesNotaCredito.AsNoTracking().SingleAsync()).Id;

        Assert.False((await _service.AnularComprobanteAsync(nc, "error", "t")).Ok);
        Assert.False((await _service.AnularComprobanteAsync(f, "error", "t")).Ok);
        Assert.False((await _service.AnularImputacionNotaCreditoAsync(impId, "", "t")).Ok);

        var r = await _service.AnularImputacionNotaCreditoAsync(impId, "aplicada a la factura equivocada", "supervisor");

        Assert.True(r.Ok, r.Error);
        Assert.Equal((1000m, EstadoComprobanteProveedor.Pendiente), ((await LeerAsync(f)).SaldoPendiente, (await LeerAsync(f)).Estado));
        Assert.Equal((300m, EstadoComprobanteProveedor.Pendiente), ((await LeerAsync(nc)).SaldoPendiente, (await LeerAsync(nc)).Estado));
        var fila = await _context.ImputacionesNotaCredito.AsNoTracking().SingleAsync();
        Assert.True(fila.Anulada);
        Assert.Equal("supervisor", fila.AnuladaPor);
        Assert.False((await _service.AnularImputacionNotaCreditoAsync(impId, "otra vez", "t")).Ok);
        Assert.True((await _service.AnularComprobanteAsync(nc, "ya sin aplicar", "t")).Ok);
    }

    [Fact]
    public async Task NotaDeCredito_ElPagoPosteriorSoloPuedeCubrirElSaldoQueQuedaTrasLaNota()
    {
        var prov = await SeedProveedorAsync();
        var f = await NuevoComprobanteAsync(prov.Id, "F-1", 1000m);
        var nc = await NuevoComprobanteAsync(prov.Id, "NC-1", 300m, TipoComprobanteProveedor.NotaCredito);
        Assert.True((await _service.ImputarNotaCreditoAsync(nc, new[] { new ImputacionRequest { ComprobanteId = f, Monto = 300m } }, null, "t")).Ok);

        Assert.False((await _service.RegistrarPagoAsync(Pago(prov.Id, 800m, (f, 800m)), "t")).Ok);
        var ok = await _service.RegistrarPagoAsync(Pago(prov.Id, 700m, (f, 700m)), "t");

        Assert.True(ok.Ok, ok.Error);
        Assert.Equal(EstadoComprobanteProveedor.Pagado, (await LeerAsync(f)).Estado);
    }

    // ── AC-03-6: estado de cuenta, antigüedad y listados ────────────────

    [Fact]
    public async Task EstadoDeCuenta_ArmaElLibroConSaldoCorridoQueCoincideConLosDatos()
    {
        var prov = await SeedProveedorAsync();
        var f1 = await NuevoComprobanteAsync(prov.Id, "F-1", 1000m);
        await NuevoComprobanteAsync(prov.Id, "ND-1", 200m, TipoComprobanteProveedor.NotaDebito);
        var nc = await NuevoComprobanteAsync(prov.Id, "NC-1", 150m, TipoComprobanteProveedor.NotaCredito);
        Assert.True((await _service.ImputarNotaCreditoAsync(nc, new[] { new ImputacionRequest { ComprobanteId = f1, Monto = 100m } }, null, "t")).Ok);
        var pagoOk = (await _service.RegistrarPagoAsync(Pago(prov.Id, 400m, (f1, 400m)), "t")).Id!.Value;
        var pagoMalo = (await _service.RegistrarPagoAsync(Pago(prov.Id, 100m, (f1, 100m)), "t")).Id!.Value;
        Assert.True((await _service.AnularPagoAsync(pagoMalo, "error", "t")).Ok);

        var ec = await _service.ObtenerEstadoCuentaAsync(prov.Id);

        Assert.NotNull(ec);
        // 1000 + 200 - 150 (NC) - 400 (pago vigente) = 650; el pago anulado no cuenta.
        Assert.Equal(650m, ec!.SaldoNeto);
        Assert.Equal(ec.Movimientos.Last().SaldoCorrido, ec.SaldoNeto);
        // Saldos pendientes de las facturas: F-1 = 1000 - 100 - 400 = 500; ND-1 = 200; NC sin aplicar = 50.
        Assert.Equal(700m, ec.SaldoComprobantes);
        Assert.Equal(50m, ec.NotasCreditoSinAplicar);
        Assert.Equal(ec.SaldoComprobantes - ec.NotasCreditoSinAplicar, ec.SaldoNeto);
        Assert.Contains(ec.Movimientos, m => m.Tipo == TipoMovimientoCuentaPorPagar.AnulacionPago && m.Debe == 100m && m.PagoId == pagoMalo);
        Assert.Contains(ec.Movimientos, m => m.Tipo == TipoMovimientoCuentaPorPagar.Pago && m.Haber == 400m && m.PagoId == pagoOk);
        // El saldo corrido se arma de forma acumulativa.
        decimal acumulado = 0m;
        foreach (var m in ec.Movimientos)
        {
            acumulado += m.Debe - m.Haber;
            Assert.Equal(acumulado, m.SaldoCorrido);
        }
    }

    [Fact]
    public async Task EstadoDeCuenta_ProveedorInexistente_DevuelveNulo_YAislaPorProveedor()
    {
        var p1 = await SeedProveedorAsync("P1");
        var p2 = await SeedProveedorAsync("P2");
        await NuevoComprobanteAsync(p1.Id, "F-1", 100m);
        await NuevoComprobanteAsync(p2.Id, "F-1", 999m);

        Assert.Null(await _service.ObtenerEstadoCuentaAsync(424242));
        Assert.Equal(100m, (await _service.ObtenerEstadoCuentaAsync(p1.Id))!.SaldoNeto);
        Assert.Equal(999m, (await _service.ObtenerEstadoCuentaAsync(p2.Id))!.SaldoNeto);
    }

    [Theory]
    [InlineData(1, 0)]     // vence mañana: por vencer
    [InlineData(0, 0)]     // vence hoy: por vencer
    [InlineData(-1, 1)]    // ayer: 1 día vencido
    [InlineData(-30, 1)]   // borde superior 1-30
    [InlineData(-31, 2)]   // borde inferior 31-60
    [InlineData(-60, 2)]   // borde superior 31-60
    [InlineData(-61, 3)]   // más de 60
    [InlineData(-400, 3)]
    public async Task Antiguedad_UbicaCadaFacturaEnSuCubetaPorLosBordesDeDias(int diasDesdeHoy, int cubeta)
    {
        var prov = await SeedProveedorAsync();
        var venc = Hoy.AddDays(diasDesdeHoy).ToDateTime(TimeOnly.MinValue);
        await _service.RegistrarComprobanteAsync(
            Factura(prov.Id, "F-1", 100m, emision: venc.AddDays(-5), vencimiento: venc), "t");

        var a = await _service.ObtenerAntiguedadAsync();

        var linea = Assert.Single(a.Proveedores);
        var esperado = new[] { linea.PorVencer, linea.Vencido1a30, linea.Vencido31a60, linea.VencidoMas60 };
        for (var i = 0; i < 4; i++)
            Assert.Equal(i == cubeta ? 100m : 0m, esperado[i]);
        Assert.Equal(100m, a.Total);
    }

    [Fact]
    public async Task Antiguedad_UsaElSaldoPendienteYExcluyePagadasAnuladasNotasDeCreditoYSinVencimientoVaPorVencer()
    {
        var prov = await SeedProveedorAsync();
        var otro = await SeedProveedorAsync("Otro");
        var vencida = await NuevoComprobanteAsync(prov.Id, "F-VENC", 1000m, vencimiento: new DateTime(2026, 10, 1));  // 44 días
        var pagada = await NuevoComprobanteAsync(prov.Id, "F-PAG", 500m, vencimiento: new DateTime(2026, 10, 1));
        var anulada = await NuevoComprobanteAsync(prov.Id, "F-ANU", 500m, vencimiento: new DateTime(2026, 10, 1));
        await NuevoComprobanteAsync(prov.Id, "F-SIN", 250m);                                                           // sin vencimiento
        await NuevoComprobanteAsync(prov.Id, "NC-1", 300m, TipoComprobanteProveedor.NotaCredito, new DateTime(2026, 9, 1));
        await NuevoComprobanteAsync(otro.Id, "F-OTRO", 40m, vencimiento: new DateTime(2026, 1, 1));
        Assert.True((await _service.RegistrarPagoAsync(Pago(prov.Id, 400m, (vencida, 400m)), "t")).Ok);
        Assert.True((await _service.RegistrarPagoAsync(Pago(prov.Id, 500m, (pagada, 500m)), "t")).Ok);
        Assert.True((await _service.AnularComprobanteAsync(anulada, "error", "t")).Ok);

        var propio = await _service.ObtenerAntiguedadAsync(prov.Id);
        var todos = await _service.ObtenerAntiguedadAsync();

        var linea = Assert.Single(propio.Proveedores);
        Assert.Equal(250m, linea.PorVencer);
        Assert.Equal(600m, linea.Vencido31a60);
        Assert.Equal(0m, linea.Vencido1a30);
        Assert.Equal(850m, propio.Total);
        Assert.Equal(2, todos.Proveedores.Count);
        Assert.Equal(40m, todos.VencidoMas60);
        Assert.Equal(890m, todos.Total);
    }

    [Fact]
    public async Task Antiguedad_UsaElDiaComercialNoElUtc()
    {
        var prov = await SeedProveedorAsync();
        await NuevoComprobanteAsync(prov.Id, "F-1", 100m, vencimiento: new DateTime(2026, 11, 14));

        Assert.Equal(100m, (await _service.ObtenerAntiguedadAsync()).PorVencer);

        _reloj.HoyComercial = Hoy.AddDays(1);
        var manana = await _service.ObtenerAntiguedadAsync();

        Assert.Equal(0m, manana.PorVencer);
        Assert.Equal(100m, manana.Vencido1a30);
    }

    [Fact]
    public async Task Listado_FiltraPorProveedorEstadoVencimientoBusquedaYPagina()
    {
        var p1 = await SeedProveedorAsync("P1");
        var p2 = await SeedProveedorAsync("P2");
        var venc = await NuevoComprobanteAsync(p1.Id, "AAA-1", 100m, vencimiento: new DateTime(2026, 11, 1));
        await NuevoComprobanteAsync(p1.Id, "BBB-2", 200m, vencimiento: new DateTime(2026, 12, 1));
        var pagada = await NuevoComprobanteAsync(p1.Id, "CCC-3", 300m, vencimiento: new DateTime(2026, 10, 1));
        await NuevoComprobanteAsync(p2.Id, "AAA-1", 400m);
        Assert.True((await _service.RegistrarPagoAsync(Pago(p1.Id, 300m, (pagada, 300m)), "t")).Ok);

        var todos = await _service.ListarComprobantesAsync(new FiltroComprobantesProveedor());
        var delP1 = await _service.ListarComprobantesAsync(new FiltroComprobantesProveedor { ProveedorId = p1.Id });
        var pagadas = await _service.ListarComprobantesAsync(new FiltroComprobantesProveedor { Estado = EstadoComprobanteProveedor.Pagado });
        var vencidos = await _service.ListarComprobantesAsync(new FiltroComprobantesProveedor { SoloVencidos = true });
        var buscados = await _service.ListarComprobantesAsync(new FiltroComprobantesProveedor { Busqueda = "aaa" });
        var rango = await _service.ListarComprobantesAsync(new FiltroComprobantesProveedor
        { VencimientoDesde = new DateTime(2026, 11, 30), VencimientoHasta = new DateTime(2026, 12, 31) });
        var pagina = await _service.ListarComprobantesAsync(new FiltroComprobantesProveedor { TamanoPagina = 2, Pagina = 99 });

        Assert.Equal(4, todos.Total);
        Assert.Equal(700m, todos.SaldoDeuda); // 100 + 200 + 400 (la pagada no suma)
        Assert.Equal(3, delP1.Total);
        Assert.Equal("CCC-3", Assert.Single(pagadas.Items).NumeroComprobante);
        var vencido = Assert.Single(vencidos.Items);
        Assert.Equal(venc, vencido.Id);
        Assert.Equal(13, vencido.DiasVencido);
        Assert.Equal(2, buscados.Total);
        Assert.Equal("BBB-2", Assert.Single(rango.Items).NumeroComprobante);
        Assert.Equal(2, pagina.TotalPaginas);
        Assert.Equal(2, pagina.Pagina);
        Assert.Equal(2, pagina.Items.Count);
    }

    [Fact]
    public async Task Detalle_MuestraLasImputacionesDePagosYNotasDeCredito()
    {
        var prov = await SeedProveedorAsync();
        var f = await NuevoComprobanteAsync(prov.Id, "F-1", 1000m);
        var nc = await NuevoComprobanteAsync(prov.Id, "NC-1", 300m, TipoComprobanteProveedor.NotaCredito);
        Assert.True((await _service.ImputarNotaCreditoAsync(nc, new[] { new ImputacionRequest { ComprobanteId = f, Monto = 100m } }, null, "t")).Ok);
        var pagoId = (await _service.RegistrarPagoAsync(Pago(prov.Id, 200m, (f, 200m)), "t")).Id!.Value;

        var detalle = await _service.ObtenerComprobanteAsync(f);
        var detalleNc = await _service.ObtenerComprobanteAsync(nc);
        var pago = await _service.ObtenerPagoAsync(pagoId);

        Assert.Equal(700m, detalle!.Cabecera.SaldoPendiente);
        Assert.Equal(200m, Assert.Single(detalle.Pagos).Monto);
        Assert.Equal("NC-1", Assert.Single(detalle.NotasCredito).NotaCreditoNumero);
        Assert.Equal("F-1", Assert.Single(detalleNc!.NotasCredito).ComprobanteNumero);
        Assert.Equal("F-1", Assert.Single(pago!.Imputaciones).ComprobanteNumero);
        Assert.Null(await _service.ObtenerComprobanteAsync(777777));
        Assert.Single(await _service.ObtenerPendientesAsync(prov.Id));
        Assert.Single(await _service.ObtenerNotasCreditoConSaldoAsync(prov.Id));
    }

    // ── AC-03-8: no regresión de Caja, stock, costo e IVA ───────────────

    private async Task<string> HuellaDeLoQueNoDebeCambiarAsync()
    {
        var productos = await _context.Productos.AsNoTracking().OrderBy(p => p.Id)
            .Select(p => new { p.Id, p.StockActual, p.PrecioCompra, p.PrecioVenta, p.PorcentajeIVA, p.CostoEnvio, p.PercepcionesCompra, p.OtrosCostosCompra })
            .ToListAsync();
        var cheques = await _context.Cheques.AsNoTracking().OrderBy(c => c.Id).Select(c => new { c.Id, c.Estado, c.Monto }).ToListAsync();
        var ordenes = await _context.OrdenesCompra.AsNoTracking().OrderBy(o => o.Id).Select(o => new { o.Id, o.Estado, o.Total, o.Iva }).ToListAsync();
        return string.Join("|",
            await _context.MovimientosCaja.CountAsync(), await _context.AperturasCaja.CountAsync(),
            await _context.MovimientosStock.CountAsync(), await _context.Ventas.CountAsync(),
            await _context.OrdenCompraDetalles.CountAsync(),
            System.Text.Json.JsonSerializer.Serialize(productos), System.Text.Json.JsonSerializer.Serialize(cheques),
            System.Text.Json.JsonSerializer.Serialize(ordenes));
    }

    [Fact]
    public async Task NoRegresion_TodasLasOperacionesDejanCajaStockCostoEIvaIntactos()
    {
        var prov = await SeedProveedorAsync();
        var categoria = new Categoria { Codigo = "CAT", Nombre = "Cat", Activo = true };
        var marca = new Marca { Codigo = "MAR", Nombre = "Marca", Activo = true };
        _context.AddRange(categoria, marca);
        await _context.SaveChangesAsync();
        var producto = new Producto
        {
            Codigo = "P1", Nombre = "Producto", CategoriaId = categoria.Id, MarcaId = marca.Id,
            PrecioCompra = 10m, PrecioVenta = 15m, PorcentajeIVA = 21m, StockActual = 7m, Activo = true
        };
        _context.Productos.Add(producto);
        var oc = new OrdenCompra
        {
            Numero = "OC-NR", ProveedorId = prov.Id, Estado = EstadoOrdenCompra.Recibida, FechaEmision = DateTime.UtcNow,
            Subtotal = 100m, Iva = 21m, Total = 121m
        };
        var cheque = new Cheque { Numero = "9", Banco = "B", Monto = 121m, ProveedorId = prov.Id, Estado = EstadoCheque.Emitido };
        _context.AddRange(oc, cheque);
        await _context.SaveChangesAsync();

        var antes = await HuellaDeLoQueNoDebeCambiarAsync();

        var req = Factura(prov.Id, "NR-1", 121m);
        req.OrdenCompraId = oc.Id;
        var f = (await _service.RegistrarComprobanteAsync(req, "t")).Id!.Value;
        var nc = await NuevoComprobanteAsync(prov.Id, "NR-NC", 21m, TipoComprobanteProveedor.NotaCredito);
        Assert.True((await _service.ImputarNotaCreditoAsync(nc, new[] { new ImputacionRequest { ComprobanteId = f, Monto = 21m } }, null, "t")).Ok);
        var pago = Pago(prov.Id, 50m, (f, 50m));
        pago.Medio = MedioPagoProveedor.Efectivo;
        var pagoId = (await _service.RegistrarPagoAsync(pago, "t")).Id!.Value;
        var cheq = Pago(prov.Id, 50m, (f, 50m));
        cheq.Medio = MedioPagoProveedor.Cheque;
        cheq.ChequeId = cheque.Id;
        Assert.True((await _service.RegistrarPagoAsync(cheq, "t")).Ok);
        Assert.True((await _service.AnularPagoAsync(pagoId, "error", "t")).Ok);
        await _service.ObtenerEstadoCuentaAsync(prov.Id);
        await _service.ObtenerAntiguedadAsync();

        Assert.Equal(antes, await HuellaDeLoQueNoDebeCambiarAsync());
        // El IVA se guarda tal como lo informa el proveedor: no se recalcula ni se acumula en ninguna otra tabla.
        var c = await LeerAsync(f);
        Assert.Equal((100m, 21m, 121m), (c.ImporteNeto, c.ImporteIva, c.ImporteTotal));
    }

    [Theory]
    [InlineData("CajaService")]
    [InlineData("MovimientoStock")]
    [InlineData("Producto")]
    [InlineData("PrecioCompra")]
    [InlineData("PorcentajeIVA")]
    [InlineData("Venta")]
    public void Contrato_ElServicioNoReferenciaCajaStockCostoNiIvaDeProductos(string prohibido)
    {
        var codigo = File.ReadAllText(Path.Combine(FindRepoRoot(), "Services", "CuentaPorPagarService.cs"));

        Assert.DoesNotContain(prohibido, codigo);
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TheBuryProyect.csproj")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("No se encontró la raíz del repositorio.");
    }
}
