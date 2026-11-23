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
/// QA financiera (Agente 3): la baja lógica de un proveedor no puede esconder dinero. Un proveedor con deuda vigente
/// (comprobantes con saldo) o con anticipos con saldo a favor sin aplicar deja de aparecer en cuentas por pagar y ya no
/// admite pagos ni aplicaciones, por lo que su deuda/saldo queda huérfano. Misma lógica que H-24 en clientes.
/// Servicios reales sobre SQLite en memoria.
/// </summary>
public class ProveedorBajaConSaldoCxpTests : IDisposable
{
    private static readonly DateOnly Hoy = new(2026, 11, 14);
    private static long _seq = 30_000_000_000;

    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly ProveedorService _proveedores;
    private readonly CuentaPorPagarService _cxp;

    public ProveedorBajaConSaldoCxpTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();
        _context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        _context.Database.EnsureCreated();
        _proveedores = new ProveedorService(_context, NullLogger<ProveedorService>.Instance);
        _cxp = new CuentaPorPagarService(_context, new RelojComercialFake(Hoy), NullLogger<CuentaPorPagarService>.Instance);
        _context.OrigenesFondos.Add(new OrigenFondos { Codigo = "EFE", Nombre = "Efectivo administrativo", Tipo = TipoOrigenFondos.Efectivo });
        _context.SaveChanges();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private async Task<Proveedor> SeedProveedorAsync()
    {
        var p = new Proveedor { Cuit = Interlocked.Increment(ref _seq).ToString(), RazonSocial = "Proveedor QA", Activo = true };
        _context.Proveedores.Add(p);
        await _context.SaveChangesAsync();
        return p;
    }

    private async Task<int> FacturaAsync(int proveedorId, decimal total)
    {
        var neto = Math.Round(total / 1.21m, 2);
        var r = await _cxp.RegistrarComprobanteAsync(new ComprobanteProveedorRequest
        {
            ProveedorId = proveedorId, Tipo = TipoComprobanteProveedor.Factura, NumeroComprobante = "A-0001-0001",
            FechaEmision = new DateTime(2026, 11, 1), FechaVencimiento = new DateTime(2026, 12, 1),
            ImporteNeto = neto, ImporteIva = total - neto, ImporteTotal = total
        }, "tester");
        Assert.True(r.Ok, r.Error);
        return r.Id!.Value;
    }

    private async Task<int> AnticipoAsync(int proveedorId, decimal monto)
    {
        var r = await _cxp.RegistrarAnticipoAsync(new AnticipoProveedorRequest
        {
            ProveedorId = proveedorId, Fecha = new DateTime(2026, 11, 10), Monto = monto, OrigenFondosId = 1,
            Medio = MedioPagoProveedor.Transferencia, Referencia = "TRF-1"
        }, "tester");
        Assert.True(r.Ok, r.Error);
        return r.Id!.Value;
    }

    private async Task<bool> EstaActivoAsync(int proveedorId) =>
        !await _context.Proveedores.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.Id == proveedorId).Select(p => p.IsDeleted).SingleAsync();

    [Fact]
    public async Task Baja_ConComprobanteConSaldoPendiente_SeRechazaYElProveedorSigueVigente()
    {
        var prov = await SeedProveedorAsync();
        await FacturaAsync(prov.Id, 1210m);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _proveedores.DeleteAsync(prov.Id));

        Assert.True(await EstaActivoAsync(prov.Id));
        var pendientes = await _cxp.ObtenerPendientesAsync(prov.Id);
        Assert.Equal(1210m, pendientes.Sum(c => c.SaldoPendiente));
    }

    [Fact]
    public async Task Baja_ConAnticipoConSaldoSinAplicar_SeRechazaYElProveedorSigueVigente()
    {
        var prov = await SeedProveedorAsync();
        await AnticipoAsync(prov.Id, 500m);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _proveedores.DeleteAsync(prov.Id));

        Assert.True(await EstaActivoAsync(prov.Id));
    }

    [Fact]
    public async Task Baja_ConNotaCreditoConSaldoSinAplicar_SeRechazaYElProveedorSigueVigente()
    {
        var prov = await SeedProveedorAsync();
        var neto = Math.Round(300m / 1.21m, 2);
        var nc = await _cxp.RegistrarComprobanteAsync(new ComprobanteProveedorRequest
        {
            ProveedorId = prov.Id, Tipo = TipoComprobanteProveedor.NotaCredito, NumeroComprobante = "NC-0001-0001",
            FechaEmision = new DateTime(2026, 11, 1), ImporteNeto = neto, ImporteIva = 300m - neto, ImporteTotal = 300m
        }, "tester");
        Assert.True(nc.Ok, nc.Error);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _proveedores.DeleteAsync(prov.Id));

        Assert.True(await EstaActivoAsync(prov.Id));
    }

    [Fact]
    public async Task Baja_ConTodoSaldado_SigueSiendoPosible()
    {
        var prov = await SeedProveedorAsync();
        var facturaId = await FacturaAsync(prov.Id, 1000m);
        var anticipoId = await AnticipoAsync(prov.Id, 1000m);
        var aplicar = await _cxp.AplicarAnticipoAsync(anticipoId,
            new[] { new ImputacionRequest { ComprobanteId = facturaId, Monto = 1000m } }, null, "tester");
        Assert.True(aplicar.Ok, aplicar.Error);

        Assert.True(await _proveedores.DeleteAsync(prov.Id));
        Assert.False(await EstaActivoAsync(prov.Id));
    }

    [Fact]
    public async Task Baja_ConComprobanteAnuladoYAnticipoAnulado_SigueSiendoPosible()
    {
        var prov = await SeedProveedorAsync();
        var facturaId = await FacturaAsync(prov.Id, 1000m);
        var anticipoId = await AnticipoAsync(prov.Id, 200m);
        Assert.True((await _cxp.AnularComprobanteAsync(facturaId, "carga errónea", "tester")).Ok);
        Assert.True((await _cxp.AnularAnticipoAsync(anticipoId, "carga errónea", "tester")).Ok);

        Assert.True(await _proveedores.DeleteAsync(prov.Id));
    }

    [Fact]
    public async Task Anulacion_PosteriorALaBaja_NoReabreDeudaNiSaldoSobreProveedorEliminado()
    {
        var prov = await SeedProveedorAsync();
        var facturaId = await FacturaAsync(prov.Id, 1000m);
        var anticipoId = await AnticipoAsync(prov.Id, 1000m);
        Assert.True((await _cxp.AplicarAnticipoAsync(anticipoId,
            new[] { new ImputacionRequest { ComprobanteId = facturaId, Monto = 1000m } }, null, "tester")).Ok);
        Assert.True(await _proveedores.DeleteAsync(prov.Id));
        var aplicacionId = await _context.AnticipoAplicaciones.AsNoTracking()
            .Where(a => a.AnticipoProveedorId == anticipoId).Select(a => a.Id).SingleAsync();

        var r = await _cxp.AnularAplicacionAnticipoAsync(aplicacionId, "intento posterior a la baja", "tester");

        Assert.False(r.Ok);
        Assert.Contains("dado de baja", r.Error);
        var saldo = await _context.ComprobantesProveedor.AsNoTracking()
            .Where(c => c.Id == facturaId).Select(c => c.SaldoPendiente).SingleAsync();
        Assert.Equal(0m, saldo);
        Assert.False(await _context.AnticipoAplicaciones.AsNoTracking().Where(a => a.Id == aplicacionId).Select(a => a.Anulada).SingleAsync());
    }
}
