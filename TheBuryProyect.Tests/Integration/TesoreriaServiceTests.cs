using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;
using TheBuryProject.Tests.Helpers;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// D-03 Tesorería administrativa (servicios reales sobre SQLite): catálogo de orígenes de fondos, anticipos aplicables a una o varias
/// facturas del mismo proveedor, un cheque físico por pago o anticipo, idempotencia, reversión y auditoría. Es un registro administrativo:
/// no mueve Caja, stock ni costos.
/// </summary>
public class TesoreriaServiceTests : IDisposable
{
    private static readonly DateOnly Hoy = new(2026, 11, 15);

    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly RelojComercialFake _reloj = new(Hoy);
    private readonly AuditoriaGrabadoraTes _auditoria = new();
    private readonly CuentaPorPagarService _cxp;
    private readonly TesoreriaService _tes;
    private static long _seq = 30_000_000_000;

    public TesoreriaServiceTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();
        _context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        _context.Database.EnsureCreated();
        _cxp = new CuentaPorPagarService(_context, _reloj, NullLogger<CuentaPorPagarService>.Instance, _auditoria);
        _tes = new TesoreriaService(_context, _reloj, NullLogger<TesoreriaService>.Instance, _auditoria);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // ── helpers ─────────────────────────────────────────────────────────

    private async Task<int> NuevoOrigenAsync(string codigo = "EFE", string nombre = "Efectivo administrativo", bool activo = true)
    {
        var r = await _tes.GuardarOrigenAsync(new OrigenFondosRequest { Codigo = codigo, Nombre = nombre, Tipo = TipoOrigenFondos.Efectivo, Activo = activo }, "admin");
        Assert.True(r.Ok, r.Error);
        return r.Id!.Value;
    }

    private async Task<Proveedor> NuevoProveedorAsync(string razon = "Proveedor")
    {
        var p = new Proveedor { Cuit = Interlocked.Increment(ref _seq).ToString(), RazonSocial = razon, Activo = true };
        _context.Proveedores.Add(p);
        await _context.SaveChangesAsync();
        return p;
    }

    private async Task<int> NuevaFacturaAsync(int proveedorId, string numero, decimal total,
        TipoComprobanteProveedor tipo = TipoComprobanteProveedor.Factura)
    {
        var r = await _cxp.RegistrarComprobanteAsync(new ComprobanteProveedorRequest
        {
            ProveedorId = proveedorId, Tipo = tipo, NumeroComprobante = numero, FechaEmision = new DateTime(2026, 11, 1),
            ImporteNeto = total, ImporteIva = 0m, ImporteTotal = total
        }, "t");
        Assert.True(r.Ok, r.Error);
        return r.Id!.Value;
    }

    private static AnticipoProveedorRequest Anticipo(int proveedorId, decimal monto, int origenId, int? chequeId = null, string? clave = null) => new()
    {
        ProveedorId = proveedorId, Fecha = new DateTime(2026, 11, 10), Monto = monto, OrigenFondosId = origenId,
        Medio = chequeId.HasValue ? MedioPagoProveedor.Cheque : MedioPagoProveedor.Transferencia, ChequeId = chequeId, ClaveIdempotencia = clave
    };

    private static PagoProveedorRequest Pago(int proveedorId, decimal monto, int origenId, int? chequeId = null, params (int Id, decimal Monto)[] imp) => new()
    {
        ProveedorId = proveedorId, Fecha = new DateTime(2026, 11, 10), Monto = monto, OrigenFondosId = origenId,
        Medio = chequeId.HasValue ? MedioPagoProveedor.Cheque : MedioPagoProveedor.Transferencia, ChequeId = chequeId,
        Imputaciones = imp.Select(i => new ImputacionRequest { ComprobanteId = i.Id, Monto = i.Monto }).ToList()
    };

    private static ImputacionRequest[] Aplica(params (int Id, decimal Monto)[] imp) =>
        imp.Select(i => new ImputacionRequest { ComprobanteId = i.Id, Monto = i.Monto }).ToArray();

    private async Task<int> NuevoChequeAsync(int proveedorId, decimal monto, EstadoCheque estado = EstadoCheque.Emitido)
    {
        var c = new Cheque { Numero = Interlocked.Increment(ref _seq).ToString(), Banco = "Banco", Monto = monto, ProveedorId = proveedorId, Estado = estado };
        _context.Cheques.Add(c);
        await _context.SaveChangesAsync();
        return c.Id;
    }

    private async Task<decimal> SaldoAsync(int comprobanteId) =>
        (await _context.ComprobantesProveedor.AsNoTracking().SingleAsync(c => c.Id == comprobanteId)).SaldoPendiente;

    // ── catálogo de orígenes de fondos ──────────────────────────────────

    [Fact]
    public async Task Origen_AltaEdicionYBaja_PersistenConAuditoria()
    {
        var id = await NuevoOrigenAsync(" ban-1 ", "Banco principal");
        var origen = await _tes.ObtenerOrigenAsync(id);
        Assert.Equal("BAN-1", origen!.Codigo); // recortado y en mayúsculas
        Assert.True(origen.Activo);

        var edicion = await _tes.GuardarOrigenAsync(new OrigenFondosRequest
        {
            Id = id, Codigo = "BAN-1", Nombre = "Banco principal (cta. cte.)", Tipo = TipoOrigenFondos.CuentaBancaria, Activo = false,
            Orden = 3, RowVersion = origen.RowVersion
        }, "admin");
        Assert.True(edicion.Ok, edicion.Error);
        var editado = await _tes.ObtenerOrigenAsync(id);
        Assert.False(editado!.Activo);
        Assert.Equal(TipoOrigenFondos.CuentaBancaria, editado.Tipo);

        Assert.True((await _tes.EliminarOrigenAsync(id, "admin")).Ok);
        Assert.Null(await _tes.ObtenerOrigenAsync(id));
        Assert.Equal(new[] { "CrearOrigenFondos", "EditarOrigenFondos", "EliminarOrigenFondos" }, _auditoria.Eventos.Select(e => e.Accion).ToArray());
        Assert.All(_auditoria.Eventos, e => Assert.Equal("Tesoreria", e.Modulo));
    }

    [Theory]
    [InlineData("", "Nombre")]
    [InlineData("A B", "Nombre")]
    [InlineData("OK", "")]
    [InlineData("ESTE-CODIGO-ES-DEMASIADO-LARGO-PARA-EL-CAMPO", "Nombre")]
    public async Task Origen_ValidaCodigoYNombre(string codigo, string nombre)
    {
        var r = await _tes.GuardarOrigenAsync(new OrigenFondosRequest { Codigo = codigo, Nombre = nombre }, "admin");
        Assert.False(r.Ok);
        Assert.Empty(await _context.OrigenesFondos.ToListAsync());
    }

    [Fact]
    public async Task Origen_CodigoUnicoSinDistinguirMayusculasNiEspacios_YLaBajaLoLibera()
    {
        var id = await NuevoOrigenAsync("EFE");
        var duplicado = await _tes.GuardarOrigenAsync(new OrigenFondosRequest { Codigo = " efe ", Nombre = "Otro" }, "admin");
        Assert.False(duplicado.Ok);
        Assert.Contains("EFE", duplicado.Error);

        Assert.True((await _tes.EliminarOrigenAsync(id, "admin")).Ok);
        Assert.True((await _tes.GuardarOrigenAsync(new OrigenFondosRequest { Codigo = "efe", Nombre = "Reutiliza" }, "admin")).Ok);
    }

    [Fact]
    public async Task Origen_ConUnRowVersionObsoleto_NoPisaLaEdicionDeOtroUsuario()
    {
        var id = await NuevoOrigenAsync("EFE");
        var origen = (await _tes.ObtenerOrigenAsync(id))!;
        // Otro usuario edita primero (el RowVersion de SQLite no se genera: se simula con uno obsoleto distinto del guardado).
        await _context.Database.ExecuteSqlRawAsync("UPDATE OrigenesFondos SET RowVersion = X'0102030405060708' WHERE Id = {0}", id);
        _context.ChangeTracker.Clear();

        var r = await _tes.GuardarOrigenAsync(new OrigenFondosRequest
        {
            Id = id, Codigo = "EFE", Nombre = "Pisada", RowVersion = origen.RowVersion.Length == 0 ? new byte[] { 9 } : origen.RowVersion
        }, "admin");

        Assert.False(r.Ok);
        Assert.NotEqual("Pisada", (await _context.OrigenesFondos.AsNoTracking().SingleAsync(o => o.Id == id)).Nombre);
    }

    [Fact]
    public async Task Origen_ConMovimientos_NoSeBorra_SeDesactivaYYaNoSeOfrece()
    {
        var origen = await NuevoOrigenAsync();
        var prov = await NuevoProveedorAsync();
        var f = await NuevaFacturaAsync(prov.Id, "F-1", 100m);
        Assert.True((await _cxp.RegistrarPagoAsync(Pago(prov.Id, 100m, origen, null, (f, 100m)), "t")).Ok);

        var eliminar = await _tes.EliminarOrigenAsync(origen, "admin");
        Assert.False(eliminar.Ok);
        Assert.Contains("desactivalo", eliminar.Error);
        Assert.Equal(1, (await _tes.ObtenerOrigenAsync(origen))!.Movimientos);

        var o = (await _tes.ObtenerOrigenAsync(origen))!;
        Assert.True((await _tes.GuardarOrigenAsync(new OrigenFondosRequest
        { Id = origen, Codigo = o.Codigo, Nombre = o.Nombre, Tipo = o.Tipo, Activo = false, RowVersion = o.RowVersion }, "admin")).Ok);
        Assert.Empty(await _tes.ListarOrigenesAsync(soloActivos: true));
        Assert.Single(await _tes.ListarOrigenesAsync());
    }

    [Fact]
    public async Task Pago_ExigeUnOrigenDeFondosActivo()
    {
        var activo = await NuevoOrigenAsync("A");
        var inactivo = await NuevoOrigenAsync("I", "Inactivo", activo: false);
        var prov = await NuevoProveedorAsync();
        var f = await NuevaFacturaAsync(prov.Id, "F-1", 300m);

        var sinOrigen = Pago(prov.Id, 100m, 0, null, (f, 100m));
        sinOrigen.OrigenFondosId = null;
        Assert.Contains("origen", (await _cxp.RegistrarPagoAsync(sinOrigen, "t")).Error);
        Assert.Contains("no existe", (await _cxp.RegistrarPagoAsync(Pago(prov.Id, 100m, 99999, null, (f, 100m)), "t")).Error);
        Assert.Contains("inactivo", (await _cxp.RegistrarPagoAsync(Pago(prov.Id, 100m, inactivo, null, (f, 100m)), "t")).Error);
        Assert.Empty(await _context.PagosProveedor.ToListAsync());

        var ok = await _cxp.RegistrarPagoAsync(Pago(prov.Id, 100m, activo, null, (f, 100m)), "t");
        Assert.True(ok.Ok, ok.Error);
        Assert.Equal(activo, (await _cxp.ObtenerPagoAsync(ok.Id!.Value))!.OrigenFondosId);
    }

    [Fact]
    public async Task Resumen_SumaPagosYAnticiposVigentesPorOrigen_ExcluyeAnuladosYFueraDelPeriodo()
    {
        var banco = await NuevoOrigenAsync("BCO", "Banco");
        var efectivo = await NuevoOrigenAsync("EFE", "Efectivo");
        var prov = await NuevoProveedorAsync();
        var f = await NuevaFacturaAsync(prov.Id, "F-1", 1000m);

        Assert.True((await _cxp.RegistrarPagoAsync(Pago(prov.Id, 300m, banco, null, (f, 300m)), "t")).Ok);
        var anulado = (await _cxp.RegistrarPagoAsync(Pago(prov.Id, 100m, banco, null, (f, 100m)), "t")).Id!.Value;
        Assert.True((await _cxp.AnularPagoAsync(anulado, "error", "t")).Ok);
        Assert.True((await _cxp.RegistrarAnticipoAsync(Anticipo(prov.Id, 200m, banco), "t")).Ok);
        Assert.True((await _cxp.RegistrarAnticipoAsync(Anticipo(prov.Id, 50m, efectivo), "t")).Ok);
        var fuera = Anticipo(prov.Id, 999m, efectivo);
        fuera.Fecha = new DateTime(2026, 9, 1);
        Assert.True((await _cxp.RegistrarAnticipoAsync(fuera, "t")).Ok);

        var resumen = await _tes.ObtenerResumenAsync(new DateTime(2026, 11, 1), new DateTime(2026, 11, 30));

        var b = resumen.Origenes.Single(o => o.OrigenFondosId == banco);
        var e = resumen.Origenes.Single(o => o.OrigenFondosId == efectivo);
        Assert.Equal((300m, 200m, 2), (b.Pagos, b.Anticipos, b.Cantidad));
        Assert.Equal((0m, 50m, 1), (e.Pagos, e.Anticipos, e.Cantidad));
        Assert.Equal(550m, resumen.Total);
    }

    // ── anticipos ───────────────────────────────────────────────────────

    [Fact]
    public async Task Anticipo_SePersisteComoSaldoAFavorConAuditoria_SinTocarLosComprobantes()
    {
        var origen = await NuevoOrigenAsync();
        var prov = await NuevoProveedorAsync();
        var f = await NuevaFacturaAsync(prov.Id, "F-1", 1000m);

        var r = await _cxp.RegistrarAnticipoAsync(Anticipo(prov.Id, 400m, origen), "tester");

        Assert.True(r.Ok, r.Error);
        var item = (await _cxp.ObtenerAnticipoAsync(r.Id!.Value))!;
        Assert.Equal((400m, 400m, origen, "tester", false), (item.Monto, item.SaldoSinAplicar, item.OrigenFondosId, item.CreadoPor, item.Anulado));
        Assert.Equal(1000m, await SaldoAsync(f)); // el anticipo no cambia la deuda hasta aplicarse
        Assert.Contains(_auditoria.Eventos, e => e is { Modulo: "CuentasPorPagar", Accion: "RegistrarAnticipo" });
    }

    [Theory]
    [InlineData(0, "mayor que cero")]
    [InlineData(-5, "mayor que cero")]
    [InlineData(10.123, "2 decimales")]
    public async Task Anticipo_ValidaElMonto(double monto, string mensaje)
    {
        var origen = await NuevoOrigenAsync();
        var prov = await NuevoProveedorAsync();

        var r = await _cxp.RegistrarAnticipoAsync(Anticipo(prov.Id, (decimal)monto, origen), "t");

        Assert.False(r.Ok);
        Assert.Contains(mensaje, r.Error);
        Assert.Empty(await _context.AnticiposProveedor.ToListAsync());
    }

    [Fact]
    public async Task Anticipo_ExigeOrigenActivo_ProveedorExistente_YChequeSoloConMedioCheque()
    {
        var origen = await NuevoOrigenAsync();
        var inactivo = await NuevoOrigenAsync("I", "Inactivo", activo: false);
        var prov = await NuevoProveedorAsync();
        var cheque = await NuevoChequeAsync(prov.Id, 100m);

        var sinOrigen = Anticipo(prov.Id, 100m, 0);
        sinOrigen.OrigenFondosId = null;
        Assert.False((await _cxp.RegistrarAnticipoAsync(sinOrigen, "t")).Ok);
        Assert.False((await _cxp.RegistrarAnticipoAsync(Anticipo(prov.Id, 100m, inactivo), "t")).Ok);
        Assert.False((await _cxp.RegistrarAnticipoAsync(Anticipo(99999, 100m, origen), "t")).Ok);
        var medioIncorrecto = Anticipo(prov.Id, 100m, origen, cheque);
        medioIncorrecto.Medio = MedioPagoProveedor.Efectivo;
        Assert.False((await _cxp.RegistrarAnticipoAsync(medioIncorrecto, "t")).Ok);
        Assert.Empty(await _context.AnticiposProveedor.ToListAsync());
    }

    [Fact]
    public async Task Anticipo_ReenviarElMismoFormulario_NoRegistraUnoSegundo()
    {
        var origen = await NuevoOrigenAsync();
        var prov = await NuevoProveedorAsync();

        var primero = await _cxp.RegistrarAnticipoAsync(Anticipo(prov.Id, 100m, origen, clave: "clave-1"), "t");
        var repetido = await _cxp.RegistrarAnticipoAsync(Anticipo(prov.Id, 100m, origen, clave: "clave-1"), "t");

        Assert.True(primero.Ok && repetido.Ok);
        Assert.True(repetido.YaRegistrado);
        Assert.Equal(primero.Id, repetido.Id);
        Assert.Single(await _context.AnticiposProveedor.ToListAsync());
    }

    [Fact]
    public async Task Anticipo_SeAplicaAVariasFacturasDelMismoProveedor_YElSaldoBajaPorCadaUna()
    {
        var origen = await NuevoOrigenAsync();
        var prov = await NuevoProveedorAsync();
        var f1 = await NuevaFacturaAsync(prov.Id, "F-1", 300m);
        var f2 = await NuevaFacturaAsync(prov.Id, "F-2", 500m);
        var anticipo = (await _cxp.RegistrarAnticipoAsync(Anticipo(prov.Id, 600m, origen), "t")).Id!.Value;

        var r = await _cxp.AplicarAnticipoAsync(anticipo, Aplica((f1, 300m), (f2, 250m)), "clave-aplic", "t");

        Assert.True(r.Ok, r.Error);
        Assert.Equal(0m, await SaldoAsync(f1));
        Assert.Equal(250m, await SaldoAsync(f2));
        Assert.Equal(EstadoComprobanteProveedor.Pagado, (await _context.ComprobantesProveedor.AsNoTracking().SingleAsync(c => c.Id == f1)).Estado);
        Assert.Equal(EstadoComprobanteProveedor.ParcialmentePagado, (await _context.ComprobantesProveedor.AsNoTracking().SingleAsync(c => c.Id == f2)).Estado);
        var item = (await _cxp.ObtenerAnticipoAsync(anticipo))!;
        Assert.Equal(50m, item.SaldoSinAplicar);
        Assert.Equal(2, item.Aplicaciones.Count);
        Assert.Contains(_auditoria.Eventos, e => e.Accion == "AplicarAnticipo");

        // Reenviar la misma aplicación no la duplica.
        var repetida = await _cxp.AplicarAnticipoAsync(anticipo, Aplica((f1, 300m), (f2, 250m)), "clave-aplic", "t");
        Assert.True(repetida.YaRegistrado);
        Assert.Equal(2, (await _cxp.ObtenerAnticipoAsync(anticipo))!.Aplicaciones.Count);
    }

    [Fact]
    public async Task Anticipo_NoSePuedeAplicarMasQueSuSaldoNiMasQueElSaldoDeLaFactura()
    {
        var origen = await NuevoOrigenAsync();
        var prov = await NuevoProveedorAsync();
        var f1 = await NuevaFacturaAsync(prov.Id, "F-1", 300m);
        var f2 = await NuevaFacturaAsync(prov.Id, "F-2", 900m);
        var anticipo = (await _cxp.RegistrarAnticipoAsync(Anticipo(prov.Id, 500m, origen), "t")).Id!.Value;

        var masQueElSaldoDelAnticipo = await _cxp.AplicarAnticipoAsync(anticipo, Aplica((f1, 300m), (f2, 300m)), null, "t");
        var masQueLaFactura = await _cxp.AplicarAnticipoAsync(anticipo, Aplica((f1, 400m)), null, "t");

        Assert.Contains("saldo a favor", masQueElSaldoDelAnticipo.Error);
        Assert.Contains("supera su saldo", masQueLaFactura.Error);
        Assert.Equal(300m, await SaldoAsync(f1));
        Assert.Equal(500m, (await _cxp.ObtenerAnticipoAsync(anticipo))!.SaldoSinAplicar);
        Assert.Empty(await _context.AnticipoAplicaciones.ToListAsync());
    }

    [Fact]
    public async Task Anticipo_SoloSeAplicaAFacturasDelMismoProveedor_NoANotasDeCreditoNiAAnuladas()
    {
        var origen = await NuevoOrigenAsync();
        var prov = await NuevoProveedorAsync();
        var otro = await NuevoProveedorAsync("Otro");
        var ajena = await NuevaFacturaAsync(otro.Id, "F-AJ", 200m);
        var nc = await NuevaFacturaAsync(prov.Id, "NC-1", 100m, TipoComprobanteProveedor.NotaCredito);
        var anulada = await NuevaFacturaAsync(prov.Id, "F-AN", 100m);
        Assert.True((await _cxp.AnularComprobanteAsync(anulada, "carga errónea", "t")).Ok);
        var anticipo = (await _cxp.RegistrarAnticipoAsync(Anticipo(prov.Id, 500m, origen), "t")).Id!.Value;

        Assert.Contains("otro proveedor", (await _cxp.AplicarAnticipoAsync(anticipo, Aplica((ajena, 100m)), null, "t")).Error);
        Assert.Contains("nota de crédito", (await _cxp.AplicarAnticipoAsync(anticipo, Aplica((nc, 50m)), null, "t")).Error);
        Assert.Contains("anulado", (await _cxp.AplicarAnticipoAsync(anticipo, Aplica((anulada, 50m)), null, "t")).Error);
        Assert.False((await _cxp.AplicarAnticipoAsync(anticipo, Aplica((123456, 50m)), null, "t")).Ok);
        Assert.False((await _cxp.AplicarAnticipoAsync(anticipo, Aplica(), null, "t")).Ok);
        Assert.Empty(await _context.AnticipoAplicaciones.ToListAsync());
    }

    [Fact]
    public async Task Anticipo_AnularAplicacion_RestituyeElSaldo_YElAnticipoRecuperaSuSaldoAFavor()
    {
        var origen = await NuevoOrigenAsync();
        var prov = await NuevoProveedorAsync();
        var f = await NuevaFacturaAsync(prov.Id, "F-1", 400m);
        var anticipo = (await _cxp.RegistrarAnticipoAsync(Anticipo(prov.Id, 400m, origen), "t")).Id!.Value;
        Assert.True((await _cxp.AplicarAnticipoAsync(anticipo, Aplica((f, 400m)), null, "t")).Ok);
        var aplicacion = (await _cxp.ObtenerAnticipoAsync(anticipo))!.Aplicaciones.Single().Id;

        Assert.Contains("motivo", (await _cxp.AnularAplicacionAnticipoAsync(aplicacion, " ", "t")).Error);
        Assert.True((await _cxp.AnularAplicacionAnticipoAsync(aplicacion, "mal aplicado", "t")).Ok);
        Assert.Contains("ya está anulada", (await _cxp.AnularAplicacionAnticipoAsync(aplicacion, "otra vez", "t")).Error);

        Assert.Equal(400m, await SaldoAsync(f));
        Assert.Equal(EstadoComprobanteProveedor.Pendiente, (await _context.ComprobantesProveedor.AsNoTracking().SingleAsync(c => c.Id == f)).Estado);
        var item = (await _cxp.ObtenerAnticipoAsync(anticipo))!;
        Assert.Equal(400m, item.SaldoSinAplicar);
        Assert.True(item.Aplicaciones.Single().Anulada); // queda como historia, no se borra
        Assert.Equal("mal aplicado", item.Aplicaciones.Single().MotivoAnulacion);
        Assert.Contains(_auditoria.Eventos, e => e.Accion == "AnularAplicacionAnticipo");
    }

    [Fact]
    public async Task Anticipo_AnularExigeMotivo_SoloSinAplicacionesVigentes_YNoSePuedeAplicarAnulado()
    {
        var origen = await NuevoOrigenAsync();
        var prov = await NuevoProveedorAsync();
        var f = await NuevaFacturaAsync(prov.Id, "F-1", 400m);
        var anticipo = (await _cxp.RegistrarAnticipoAsync(Anticipo(prov.Id, 400m, origen), "t")).Id!.Value;
        Assert.True((await _cxp.AplicarAnticipoAsync(anticipo, Aplica((f, 100m)), null, "t")).Ok);

        Assert.Contains("motivo", (await _cxp.AnularAnticipoAsync(anticipo, null, "t")).Error);
        Assert.Contains("aplicaciones vigentes", (await _cxp.AnularAnticipoAsync(anticipo, "error", "t")).Error);

        var aplicacion = (await _cxp.ObtenerAnticipoAsync(anticipo))!.Aplicaciones.Single().Id;
        Assert.True((await _cxp.AnularAplicacionAnticipoAsync(aplicacion, "revertir", "t")).Ok);
        Assert.True((await _cxp.AnularAnticipoAsync(anticipo, "carga duplicada", "t")).Ok);
        Assert.Contains("ya está anulado", (await _cxp.AnularAnticipoAsync(anticipo, "otra vez", "t")).Error);
        Assert.Contains("anulado", (await _cxp.AplicarAnticipoAsync(anticipo, Aplica((f, 100m)), null, "t")).Error);

        var item = (await _cxp.ObtenerAnticipoAsync(anticipo))!;
        Assert.True(item.Anulado);
        Assert.Equal(0m, item.SaldoSinAplicar);
        Assert.Equal("carga duplicada", item.MotivoAnulacion);
        Assert.Contains(_auditoria.Eventos, e => e.Accion == "AnularAnticipo");
    }

    [Fact]
    public async Task Anticipo_ElComprobanteConAnticipoAplicadoNoSePuedeAnular_YUnPagoNoSobreimputaLoCubiertoPorElAnticipo()
    {
        var origen = await NuevoOrigenAsync();
        var prov = await NuevoProveedorAsync();
        var f = await NuevaFacturaAsync(prov.Id, "F-1", 1000m);
        var anticipo = (await _cxp.RegistrarAnticipoAsync(Anticipo(prov.Id, 700m, origen), "t")).Id!.Value;
        Assert.True((await _cxp.AplicarAnticipoAsync(anticipo, Aplica((f, 700m)), null, "t")).Ok);

        Assert.Contains("aplicados", (await _cxp.AnularComprobanteAsync(f, "error", "t")).Error);
        var sobreimputado = await _cxp.RegistrarPagoAsync(Pago(prov.Id, 400m, origen, null, (f, 400m)), "t");
        Assert.Contains("supera su saldo", sobreimputado.Error);
        Assert.True((await _cxp.RegistrarPagoAsync(Pago(prov.Id, 300m, origen, null, (f, 300m)), "t")).Ok);
        Assert.Equal(0m, await SaldoAsync(f));
    }

    [Fact]
    public async Task EstadoDeCuenta_IncluyeElAnticipoEnElSaldoNeto_YLaAplicacionNoLoCambia()
    {
        var origen = await NuevoOrigenAsync();
        var prov = await NuevoProveedorAsync();
        var f = await NuevaFacturaAsync(prov.Id, "F-1", 1000m);
        var anticipo = (await _cxp.RegistrarAnticipoAsync(Anticipo(prov.Id, 400m, origen), "t")).Id!.Value;

        var antes = (await _cxp.ObtenerEstadoCuentaAsync(prov.Id))!;
        Assert.Equal(600m, antes.SaldoNeto); // 1000 de la factura - 400 ya entregados
        Assert.Equal(400m, antes.AnticiposSinAplicar);
        Assert.Equal(1000m, antes.SaldoComprobantes);
        Assert.Contains(antes.Movimientos, m => m.Tipo == TipoMovimientoCuentaPorPagar.Anticipo && m.AnticipoId == anticipo && m.Haber == 400m);

        Assert.True((await _cxp.AplicarAnticipoAsync(anticipo, Aplica((f, 400m)), null, "t")).Ok);
        var despues = (await _cxp.ObtenerEstadoCuentaAsync(prov.Id))!;
        Assert.Equal(600m, despues.SaldoNeto); // aplicar reclasifica, no cambia la deuda neta
        Assert.Equal(0m, despues.AnticiposSinAplicar);
        Assert.Equal(600m, despues.SaldoComprobantes);

        var aplicacion = (await _cxp.ObtenerAnticipoAsync(anticipo))!.Aplicaciones.Single().Id;
        Assert.True((await _cxp.AnularAplicacionAnticipoAsync(aplicacion, "revertir", "t")).Ok);
        Assert.True((await _cxp.AnularAnticipoAsync(anticipo, "revertir", "t")).Ok);
        var anulado = (await _cxp.ObtenerEstadoCuentaAsync(prov.Id))!;
        Assert.Equal(1000m, anulado.SaldoNeto);
        Assert.Contains(anulado.Movimientos, m => m.Tipo == TipoMovimientoCuentaPorPagar.AnulacionAnticipo && m.Debe == 400m);
    }

    // ── un cheque físico por pago o anticipo ────────────────────────────

    [Fact]
    public async Task Cheque_RespaldaUnUnicoPago_QueSePuedeImputarAVariasFacturas()
    {
        var origen = await NuevoOrigenAsync();
        var prov = await NuevoProveedorAsync();
        var f1 = await NuevaFacturaAsync(prov.Id, "F-1", 300m);
        var f2 = await NuevaFacturaAsync(prov.Id, "F-2", 200m);
        var f3 = await NuevaFacturaAsync(prov.Id, "F-3", 400m);
        var cheque = await NuevoChequeAsync(prov.Id, 500m);

        var primero = await _cxp.RegistrarPagoAsync(Pago(prov.Id, 500m, origen, cheque, (f1, 300m), (f2, 200m)), "t");
        var segundo = await _cxp.RegistrarPagoAsync(Pago(prov.Id, 500m, origen, cheque, (f3, 400m), (f1, 0m)), "t");

        Assert.True(primero.Ok, primero.Error); // un cheque, dos facturas
        Assert.Equal(0m, await SaldoAsync(f1));
        Assert.Equal(0m, await SaldoAsync(f2));
        Assert.False(segundo.Ok);
        Assert.Single(await _context.PagosProveedor.Where(p => p.ChequeId == cheque).ToListAsync());
    }

    [Fact]
    public async Task Cheque_NoSePuedeReutilizarEntrePagosYAnticipos_YAnularLoLibera()
    {
        var origen = await NuevoOrigenAsync();
        var prov = await NuevoProveedorAsync();
        var f = await NuevaFacturaAsync(prov.Id, "F-1", 500m);
        var cheque = await NuevoChequeAsync(prov.Id, 500m);

        var pago = await _cxp.RegistrarPagoAsync(Pago(prov.Id, 500m, origen, cheque, (f, 500m)), "t");
        Assert.True(pago.Ok, pago.Error);
        var comoAnticipo = await _cxp.RegistrarAnticipoAsync(Anticipo(prov.Id, 500m, origen, cheque), "t");
        Assert.False(comoAnticipo.Ok);
        Assert.Contains("ya respalda otro pago o anticipo vigente", comoAnticipo.Error);
        Assert.Empty(await _cxp.ObtenerChequesAsync(prov.Id)); // ya no se ofrece

        Assert.True((await _cxp.AnularPagoAsync(pago.Id!.Value, "error de carga", "t")).Ok);
        Assert.Single(await _cxp.ObtenerChequesAsync(prov.Id)); // liberado

        var anticipo = await _cxp.RegistrarAnticipoAsync(Anticipo(prov.Id, 500m, origen, cheque), "t");
        Assert.True(anticipo.Ok, anticipo.Error);
        Assert.False((await _cxp.RegistrarPagoAsync(Pago(prov.Id, 500m, origen, cheque, (f, 500m)), "t")).Ok);

        Assert.True((await _cxp.AnularAnticipoAsync(anticipo.Id!.Value, "carga duplicada", "t")).Ok);
        Assert.True((await _cxp.RegistrarPagoAsync(Pago(prov.Id, 500m, origen, cheque, (f, 500m)), "t")).Ok);
    }

    [Fact]
    public async Task Cheque_ElImporteDelEgresoDebeCoincidirConElDelCheque_YDebeSerDelProveedor()
    {
        var origen = await NuevoOrigenAsync();
        var prov = await NuevoProveedorAsync();
        var otro = await NuevoProveedorAsync("Otro");
        var f = await NuevaFacturaAsync(prov.Id, "F-1", 900m);
        var cheque = await NuevoChequeAsync(prov.Id, 500m);
        var ajeno = await NuevoChequeAsync(otro.Id, 100m);
        var rechazado = await NuevoChequeAsync(prov.Id, 100m, EstadoCheque.Rechazado);

        var parcial = await _cxp.RegistrarPagoAsync(Pago(prov.Id, 300m, origen, cheque, (f, 300m)), "t");
        Assert.Contains("debe coincidir con el del cheque", parcial.Error);
        Assert.Contains("debe coincidir con el del cheque", (await _cxp.RegistrarAnticipoAsync(Anticipo(prov.Id, 600m, origen, cheque), "t")).Error);
        Assert.Contains("otro proveedor", (await _cxp.RegistrarPagoAsync(Pago(prov.Id, 100m, origen, ajeno, (f, 100m)), "t")).Error);
        Assert.Contains("anulado o rechazado", (await _cxp.RegistrarPagoAsync(Pago(prov.Id, 100m, origen, rechazado, (f, 100m)), "t")).Error);
        Assert.Empty(await _context.PagosProveedor.ToListAsync());
        Assert.Empty(await _context.AnticiposProveedor.ToListAsync());

        Assert.True((await _cxp.RegistrarPagoAsync(Pago(prov.Id, 500m, origen, cheque, (f, 500m)), "t")).Ok);
        // El pago no cambia el ciclo de vida del cheque (registro administrativo).
        Assert.Equal(EstadoCheque.Emitido, (await _context.Cheques.AsNoTracking().SingleAsync(c => c.Id == cheque)).Estado);
    }

    // ── auditoría de todo el libro ──────────────────────────────────────

    [Fact]
    public async Task Auditoria_CadaOperacionDelLibroDejaSuEventoEnLaMismaTransaccion()
    {
        var origen = await NuevoOrigenAsync();
        var prov = await NuevoProveedorAsync();
        var f = await NuevaFacturaAsync(prov.Id, "F-1", 500m);
        var nc = await NuevaFacturaAsync(prov.Id, "NC-1", 50m, TipoComprobanteProveedor.NotaCredito);
        var pago = (await _cxp.RegistrarPagoAsync(Pago(prov.Id, 100m, origen, null, (f, 100m)), "t")).Id!.Value;
        Assert.True((await _cxp.AnularPagoAsync(pago, "error", "t")).Ok);
        Assert.True((await _cxp.ImputarNotaCreditoAsync(nc, Aplica((f, 50m)), null, "t")).Ok);
        var imp = (await _context.ImputacionesNotaCredito.AsNoTracking().SingleAsync()).Id;
        Assert.True((await _cxp.AnularImputacionNotaCreditoAsync(imp, "error", "t")).Ok);
        var sobra = await _cxp.RegistrarPagoAsync(Pago(prov.Id, 900m, origen, null, (f, 900m)), "t"); // rechazada: no audita
        Assert.False(sobra.Ok);
        var antes = _auditoria.Eventos.Count;
        await _cxp.RegistrarPagoAsync(Pago(prov.Id, 900m, origen, null, (f, 900m)), "t");
        Assert.Equal(antes, _auditoria.Eventos.Count);

        var acciones = _auditoria.Eventos.Where(e => e.Modulo == "CuentasPorPagar").Select(e => e.Accion).ToList();
        Assert.Equal(
            new[] { "RegistrarComprobante", "RegistrarComprobante", "RegistrarPago", "AnularPago", "AplicarNotaCredito", "AnularAplicacionNotaCredito" },
            acciones);
    }

    private sealed class AuditoriaGrabadoraTes : ISeguridadAuditoriaService
    {
        public List<(string Modulo, string Accion, string Entidad, string? Detalle)> Eventos { get; } = new();

        public Task RegistrarEventoAsync(string modulo, string accion, string entidad, string? detalle = null)
        {
            Eventos.Add((modulo, accion, entidad, detalle));
            return Task.CompletedTask;
        }

        public void AgregarEvento(string modulo, string accion, string entidad, string? detalle = null) =>
            Eventos.Add((modulo, accion, entidad, detalle));

        public Task<AuditoriaQueryResult> ConsultarEventosAsync(
            string? usuario = null, string? modulo = null, string? accion = null,
            DateOnly? desde = null, DateOnly? hasta = null, int skip = 0, int? take = null) =>
            Task.FromResult(new AuditoriaQueryResult());
    }
}
