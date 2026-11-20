using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Tests.Integration;

internal sealed class UsuarioNcExterna(bool permiso) : ICurrentUserService
{
    public string GetUsername() => "contable";
    public string GetUserId() => "u-1";
    public bool IsAuthenticated() => true;
    public string? GetEmail() => null;
    public bool IsInRole(string role) => false;
    public bool HasPermission(string modulo, string accion) => permiso;
    public string? GetIpAddress() => "127.0.0.1";
}

internal sealed class AuditoriaNcExterna(AppDbContext context) : ISeguridadAuditoriaService
{
    public List<(string Modulo, string Accion, string Entidad, string? Detalle)> Eventos { get; } = new();
    public Task RegistrarEventoAsync(string modulo, string accion, string entidad, string? detalle = null)
    { AgregarEvento(modulo, accion, entidad, detalle); return context.SaveChangesAsync(); }
    public void AgregarEvento(string modulo, string accion, string entidad, string? detalle = null)
        => Eventos.Add((modulo, accion, entidad, detalle));
    public Task<AuditoriaQueryResult> ConsultarEventosAsync(string? usuario = null, string? modulo = null, string? accion = null,
        DateOnly? desde = null, DateOnly? hasta = null, int skip = 0, int? take = null)
        => Task.FromResult(new AuditoriaQueryResult());
}

internal sealed class FallaUnaVezInterceptor : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
{
    public bool Armada { get; set; }
    public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
        Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,
        Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (!Armada) return base.SavingChangesAsync(eventData, result, cancellationToken);
        Armada = false;
        throw new DbUpdateException("falla simulada", new InvalidOperationException("error transitorio"));
    }
}

/// <summary>
/// P-02 / P3 (preparado): vínculo auditable con la nota de crédito fiscal externa. Valida formato, fecha, importe, duplicados,
/// permiso y auditoría, y que NO cambia ningún importe ni el Reporte de IVA. Además: política comercial de devoluciones (C13/C14)
/// y reintegro exacto con el descuento de efectivo solo sobre productos (C5).
/// </summary>
public class DevolucionesNcExternaP02Tests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly AuditoriaNcExterna _auditoria;
    private static int _n = 71_000;

    public DevolucionesNcExternaP02Tests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();
        _context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(new TheBuryProject.Tests.Infrastructure.SqliteRowVersionRotationInterceptor())
            .Options);
        _context.Database.EnsureCreated();
        _auditoria = new AuditoriaNcExterna(_context);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private NotaCreditoFiscalExternaService Servicio(bool permiso = true) =>
        new(_context, new UsuarioNcExterna(permiso), _auditoria, NullLogger<NotaCreditoFiscalExternaService>.Instance);

    private async Task<NotaCredito> SeedNotaCreditoAsync(decimal totalVenta = 1000m, decimal montoNc = 500m, bool conFactura = true)
    {
        var n = Interlocked.Increment(ref _n);
        var cliente = new Cliente { Nombre = "N", Apellido = "Ext", TipoDocumento = "DNI", NumeroDocumento = n.ToString("D8"), Email = $"nc{n}@t.com" };
        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync();
        var venta = new Venta
        {
            Numero = $"VNC-{n}", ClienteId = cliente.Id, Estado = EstadoVenta.Facturada, TipoPago = TipoPago.Efectivo,
            FechaVenta = DateTime.UtcNow.AddDays(-1), Subtotal = totalVenta / 1.21m, IVA = totalVenta - totalVenta / 1.21m, Total = totalVenta
        };
        _context.Ventas.Add(venta);
        await _context.SaveChangesAsync();
        if (conFactura)
        {
            _context.Facturas.Add(new Factura
            {
                VentaId = venta.Id, Numero = $"FNC-{n}", Tipo = TipoFactura.B, FechaEmision = DateTime.UtcNow.AddDays(-1),
                Subtotal = venta.Subtotal, IVA = venta.IVA, Total = venta.Total
            });
            await _context.SaveChangesAsync();
        }

        var devolucion = new Devolucion
        {
            VentaId = venta.Id, ClienteId = cliente.Id, NumeroDevolucion = $"DEV-{n}", Motivo = MotivoDevolucion.Garantia,
            Descripcion = "x", Estado = EstadoDevolucion.Aprobada, TipoResolucion = TipoResolucionDevolucion.NotaCredito, TotalDevolucion = montoNc
        };
        _context.Devoluciones.Add(devolucion);
        await _context.SaveChangesAsync();
        var nc = new NotaCredito { DevolucionId = devolucion.Id, ClienteId = cliente.Id, NumeroNotaCredito = $"NC-{n}", MontoTotal = montoNc };
        _context.NotasCredito.Add(nc);
        await _context.SaveChangesAsync();
        return nc;
    }

    private static NotaCreditoFiscalExternaRequest Req(decimal importe = 500m, string numero = "12345", DateTime? fecha = null) => new()
    {
        PuntoVenta = "3", Tipo = "ncb", Numero = numero, Fecha = fecha ?? FechaComercial.DiaComercial(DateTime.UtcNow), Importe = importe, Cae = "12345678901234"
    };

    [Fact]
    public async Task Registrar_Valido_GuardaElVinculo_NormalizaCeros_Audita_YNoTocaImportes()
    {
        var nc = await SeedNotaCreditoAsync();
        var ventaAntes = await _context.Ventas.AsNoTracking().SingleAsync(v => v.Id == nc.Devolucion.VentaId);

        var r = await Servicio().RegistrarAsync(nc.Id, Req());

        Assert.True(r.Exitoso, string.Join("; ", r.Errores));
        var guardada = await _context.NotasCredito.AsNoTracking().SingleAsync(n => n.Id == nc.Id);
        Assert.Equal(("00003", "NCB", "00012345", 500m), (guardada.FiscalExternaPuntoVenta, guardada.FiscalExternaTipo, guardada.FiscalExternaNumero, guardada.FiscalExternaImporte));
        Assert.Equal("contable", guardada.FiscalExternaRegistradaPor);
        Assert.NotNull(guardada.FiscalExternaRegistradaEn);
        var evento = Assert.Single(_auditoria.Eventos);
        Assert.Equal(("devoluciones", "vincular_nc_fiscal_externa"), (evento.Modulo, evento.Accion));
        // Sin efecto fiscal: ningún importe cambia.
        Assert.Equal(500m, guardada.MontoTotal);
        var ventaDespues = await _context.Ventas.AsNoTracking().SingleAsync(v => v.Id == nc.Devolucion.VentaId);
        Assert.Equal((ventaAntes.Total, ventaAntes.Subtotal, ventaAntes.IVA), (ventaDespues.Total, ventaDespues.Subtotal, ventaDespues.IVA));
    }

    [Fact]
    public async Task Registrar_SinPermiso_NoHaceNada()
    {
        var nc = await SeedNotaCreditoAsync();

        var r = await Servicio(permiso: false).RegistrarAsync(nc.Id, Req());

        Assert.False(r.Exitoso);
        Assert.Null((await _context.NotasCredito.AsNoTracking().SingleAsync(n => n.Id == nc.Id)).FiscalExternaNumero);
        Assert.Empty(_auditoria.Eventos);
    }

    [Theory]
    [InlineData("", "NCB", "1", 100, "punto de venta")]
    [InlineData("123456", "NCB", "1", 100, "punto de venta")]
    [InlineData("1", "FAC", "1", 100, "tipo")]
    [InlineData("1", "NCB", "123456789", 100, "número")]
    [InlineData("1", "NCB", "ab", 100, "número")]
    [InlineData("1", "NCB", "1", 0, "mayor a cero")]
    public async Task Registrar_FormatoInvalido_Rechaza(string pv, string tipo, string numero, double importe, string fragmento)
    {
        var nc = await SeedNotaCreditoAsync();

        var r = await Servicio().RegistrarAsync(nc.Id, new NotaCreditoFiscalExternaRequest
        { PuntoVenta = pv, Tipo = tipo, Numero = numero, Fecha = FechaComercial.DiaComercial(DateTime.UtcNow), Importe = (decimal)importe });

        Assert.False(r.Exitoso);
        Assert.Contains(r.Errores, e => e.Contains(fragmento, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Registrar_CaeInvalido_FechaFuturaOAnteriorALaVenta_Rechaza()
    {
        var nc = await SeedNotaCreditoAsync();

        var cae = await Servicio().RegistrarAsync(nc.Id, new NotaCreditoFiscalExternaRequest
        { PuntoVenta = "1", Tipo = "NCA", Numero = "1", Fecha = FechaComercial.DiaComercial(DateTime.UtcNow), Importe = 100m, Cae = "123" });
        var futura = await Servicio().RegistrarAsync(nc.Id, Req(100m, fecha: FechaComercial.DiaComercial(DateTime.UtcNow).AddDays(3)));
        var anterior = await Servicio().RegistrarAsync(nc.Id, Req(100m, fecha: FechaComercial.DiaComercial(DateTime.UtcNow).AddDays(-10)));

        Assert.Contains(cae.Errores, e => e.Contains("CAE"));
        Assert.Contains(futura.Errores, e => e.Contains("futura"));
        Assert.Contains(anterior.Errores, e => e.Contains("anterior a la venta"));
    }

    [Fact]
    public async Task Registrar_ImporteMayorALaNcInternaOALaVenta_Rechaza()
    {
        var nc = await SeedNotaCreditoAsync(totalVenta: 1000m, montoNc: 500m);

        var r = await Servicio().RegistrarAsync(nc.Id, Req(importe: 800m));
        var venta = await Servicio().RegistrarAsync(nc.Id, Req(importe: 1500m));

        Assert.Contains(r.Errores, e => e.Contains("nota de crédito interna"));
        Assert.Contains(venta.Errores, e => e.Contains("total de la venta"));
    }

    [Fact]
    public async Task Registrar_VentaSinFacturaVigente_Rechaza()
    {
        var nc = await SeedNotaCreditoAsync(conFactura: false);

        var r = await Servicio().RegistrarAsync(nc.Id, Req());

        Assert.False(r.Exitoso);
        Assert.Contains(r.Errores, e => e.Contains("factura vigente"));
    }

    [Fact]
    public async Task Registrar_ComprobanteDuplicadoOYaVinculada_Rechaza()
    {
        var primera = await SeedNotaCreditoAsync();
        var segunda = await SeedNotaCreditoAsync();
        Assert.True((await Servicio().RegistrarAsync(primera.Id, Req())).Exitoso);

        var duplicado = await Servicio().RegistrarAsync(segunda.Id, Req());
        var reintento = await Servicio().RegistrarAsync(primera.Id, Req(numero: "999"));

        Assert.Contains(duplicado.Errores, e => e.Contains("otra nota de crédito"));
        Assert.Contains(reintento.Errores, e => e.Contains("ya tiene un comprobante"));
        Assert.Null((await _context.NotasCredito.AsNoTracking().SingleAsync(n => n.Id == segunda.Id)).FiscalExternaNumero);
    }

    [Fact]
    public async Task IndiceUnico_ImpideDosNotasConElMismoComprobante_AunqueSeSalteLaValidacion()
    {
        var a = await SeedNotaCreditoAsync();
        var b = await SeedNotaCreditoAsync();
        a.FiscalExternaPuntoVenta = "00001"; a.FiscalExternaTipo = "NCA"; a.FiscalExternaNumero = "00000001";
        await _context.SaveChangesAsync();
        b.FiscalExternaPuntoVenta = "00001"; b.FiscalExternaTipo = "NCA"; b.FiscalExternaNumero = "00000001";

        await Assert.ThrowsAsync<DbUpdateException>(() => _context.SaveChangesAsync());
    }

    // Hallazgo de Codex: un guardado fallido no deja el vínculo ni la auditoría pendientes en el contexto compartido.
    [Fact]
    public async Task Registrar_GuardadoFallido_NoDejaVinculoNiAuditoriaPendientes_YElReintentoFunciona()
    {
        var nc = await SeedNotaCreditoAsync();
        var falla = new FallaUnaVezInterceptor();
        var contexto = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection).AddInterceptors(new TheBuryProject.Tests.Infrastructure.SqliteRowVersionRotationInterceptor(), falla).Options);
        var usuario = new UsuarioNcExterna(true);
        var auditoriaReal = new SeguridadAuditoriaService(contexto, usuario, NullLogger<SeguridadAuditoriaService>.Instance);
        var servicio = new NotaCreditoFiscalExternaService(contexto, usuario, auditoriaReal, NullLogger<NotaCreditoFiscalExternaService>.Instance);

        falla.Armada = true;
        var fallido = await servicio.RegistrarAsync(nc.Id, Req());

        Assert.False(fallido.Exitoso);
        Assert.Empty(contexto.ChangeTracker.Entries<SeguridadEventoAuditoria>().Where(e => e.State == EntityState.Added));
        Assert.Null((await contexto.NotasCredito.AsNoTracking().SingleAsync(n => n.Id == nc.Id)).FiscalExternaNumero);
        Assert.Null(contexto.NotasCredito.Local.Single(n => n.Id == nc.Id).FiscalExternaNumero);   // la NC rastreada quedó restaurada

        // Un guardado posterior del mismo contexto no persiste la operación rechazada.
        await contexto.SaveChangesAsync();
        Assert.Empty(await contexto.SeguridadEventosAuditoria.AsNoTracking().ToListAsync());

        // El reintento (ya sin la falla) funciona y deja exactamente un vínculo y un evento.
        var reintento = await servicio.RegistrarAsync(nc.Id, Req());
        Assert.True(reintento.Exitoso, string.Join("; ", reintento.Errores));
        Assert.Single(await contexto.SeguridadEventosAuditoria.AsNoTracking().ToListAsync());
        Assert.Equal("00012345", (await contexto.NotasCredito.AsNoTracking().SingleAsync(n => n.Id == nc.Id)).FiscalExternaNumero);
        await contexto.DisposeAsync();
    }

    [Fact]
    public async Task VentasSinVinculo_NoColisionanEnElIndice()
    {
        await SeedNotaCreditoAsync();
        await SeedNotaCreditoAsync();   // ambas con FiscalExterna* nulos: el índice es filtrado
        Assert.Equal(2, await _context.NotasCredito.CountAsync());
    }

    // ─── Política comercial C13/C14 (informativa: nunca bloquea) ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(false, 71, MotivoDevolucion.Arrepentimiento, false, false)]   // presencial dentro de 72 h
    [InlineData(false, 73, MotivoDevolucion.Arrepentimiento, false, true)]    // presencial fuera de 72 h
    [InlineData(true, 24 * 29, MotivoDevolucion.Arrepentimiento, false, false)] // ML dentro de 30 días
    [InlineData(true, 24 * 31, MotivoDevolucion.Arrepentimiento, false, true)]  // ML fuera de 30 días
    [InlineData(false, 100, MotivoDevolucion.Garantia, true, false)]          // garantía: derecho legal, nunca fuera de política
    [InlineData(false, 300, MotivoDevolucion.DefectoFabrica, true, false)]
    public void Politica_PlazoComercial(bool ml, double horas, MotivoDevolucion motivo, bool protegido, bool fuera)
    {
        var ahora = new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

        var e = DevolucionPolitica.Evaluar(ml, ahora.AddHours(-horas), ahora, motivo, reintegroEnDinero: false);

        Assert.Equal(protegido, e.MotivoProtegido);
        Assert.Equal(fuera, e.FueraDePlazoComercial);
        Assert.Equal(fuera, e.FueraDePolitica);
    }

    [Theory]
    [InlineData(false, 23, MotivoDevolucion.NoCumpleExpectativas, false)]
    [InlineData(false, 25, MotivoDevolucion.NoCumpleExpectativas, true)]   // dinero después de 24 h: corresponde nota de crédito
    [InlineData(false, 25, MotivoDevolucion.Garantia, false)]              // garantía: el reintegro es un derecho
    [InlineData(true, 25, MotivoDevolucion.NoCumpleExpectativas, false)]   // Mercado Libre: lo condiciona la plataforma
    public void Politica_ReintegroEnDineroSoloEnLasPrimeras24Horas(bool ml, double horas, MotivoDevolucion motivo, bool fuera)
    {
        var ahora = new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

        var e = DevolucionPolitica.Evaluar(ml, ahora.AddHours(-horas), ahora, motivo, reintegroEnDinero: true);

        Assert.Equal(fuera, e.ReintegroEnDineroFueraDePolitica);
    }

    // ─── C5: devolver todo reintegra exactamente lo cobrado por los productos ───────────────────────────────────────────

    [Fact]
    public void RecargoPorLinea_C5_V2_DescuentoSoloSobreProductos_ReintegraLoCobradoPorLosProductos()
    {
        // 1.000,49 (10,5 %) + envío 200: base 1.200,49 -> 1.200; -5 % de 1.000 = -50 => total 1.150. Los productos pagaron 950.
        var cobrado = new[] { 1000.49m };

        var v2 = DevolucionService.RecargoPorLinea(cobrado, 200m, 1150m, CriterioFiscalRegistry.Resolver(CriterioFiscalRegistry.VersionDosRedondeoComercial));
        var v1 = DevolucionService.RecargoPorLinea(cobrado, 200m, 1150m, CriterioFiscalRegistry.Resolver(CriterioFiscalRegistry.VersionInicial));

        Assert.Equal(950.00m, cobrado[0] + v2[0]);   // V2: 1.150 - 200 de envío
        Assert.NotEqual(950.00m, cobrado[0] + v1[0]); // V1 conserva el prorrateo histórico (no se toca)
    }
}
