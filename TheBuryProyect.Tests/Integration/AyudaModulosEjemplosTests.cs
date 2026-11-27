using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Los importes que muestran las ayudas de Cuentas por pagar, Tesorería y Valoración de stock deben ser los que
/// calcula el ERP. Si cambia una regla, estas pruebas obligan a actualizar el texto de la ayuda.
/// </summary>
public class AyudaModulosEjemplosTests : IDisposable
{
    private static readonly DateOnly Hoy = new(2026, 11, 14);

    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly CuentaPorPagarService _cxp;
    private readonly TesoreriaService _tesoreria;

    public AyudaModulosEjemplosTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();
        _context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        _context.Database.EnsureCreated();
        var reloj = new RelojComercialFake(Hoy);
        _cxp = new CuentaPorPagarService(_context, reloj, NullLogger<CuentaPorPagarService>.Instance);
        _tesoreria = new TesoreriaService(_context, reloj, NullLogger<TesoreriaService>.Instance);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private async Task<int> OrigenAsync(string codigo, string nombre)
    {
        var r = await _tesoreria.GuardarOrigenAsync(
            new OrigenFondosRequest { Codigo = codigo, Nombre = nombre, Tipo = TipoOrigenFondos.Otro, Activo = true }, "qa");
        Assert.True(r.Ok, r.Error);
        return r.Id!.Value;
    }

    private async Task<int> ProveedorAsync()
    {
        var p = new Proveedor { Cuit = "30712345671", RazonSocial = "Electro Norte", Activo = true };
        _context.Proveedores.Add(p);
        await _context.SaveChangesAsync();
        return p.Id;
    }

    private async Task VerificarSaldosAsync(int proveedorId, decimal factura, decimal neto, decimal sinAplicar)
    {
        var cuenta = await _cxp.ObtenerEstadoCuentaAsync(proveedorId);
        Assert.Equal(factura, cuenta!.SaldoComprobantes);
        Assert.Equal(neto, cuenta.SaldoNeto);
        Assert.Equal(sinAplicar, cuenta.AnticiposSinAplicar);
    }

    [Fact]
    public async Task CuentasPorPagar_ElEjemploPasoAPaso_CoincideConElServicio()
    {
        var proveedor = await ProveedorAsync();
        var banco = await OrigenAsync("BCO", "Banco - cuenta corriente");

        var factura = await _cxp.RegistrarComprobanteAsync(new ComprobanteProveedorRequest
        {
            ProveedorId = proveedor, Tipo = TipoComprobanteProveedor.Factura, NumeroComprobante = "A-0001-00000001",
            FechaEmision = new DateTime(2026, 11, 1), ImporteNeto = 1_000_000m, ImporteIva = 210_000m, ImporteTotal = 1_210_000m
        }, "qa");
        Assert.True(factura.Ok, factura.Error);
        await VerificarSaldosAsync(proveedor, 1_210_000m, 1_210_000m, 0m);

        var anticipo = await _cxp.RegistrarAnticipoAsync(new AnticipoProveedorRequest
        {
            ProveedorId = proveedor, Fecha = new DateTime(2026, 11, 2), Monto = 200_000m, OrigenFondosId = banco
        }, "qa");
        Assert.True(anticipo.Ok, anticipo.Error);
        await VerificarSaldosAsync(proveedor, 1_210_000m, 1_010_000m, 200_000m);

        var aplicacion = await _cxp.AplicarAnticipoAsync(
            anticipo.Id!.Value, [new ImputacionRequest { ComprobanteId = factura.Id!.Value, Monto = 200_000m }], null, "qa");
        Assert.True(aplicacion.Ok, aplicacion.Error);
        await VerificarSaldosAsync(proveedor, 1_010_000m, 1_010_000m, 0m);

        var pago = await _cxp.RegistrarPagoAsync(new PagoProveedorRequest
        {
            ProveedorId = proveedor, Fecha = new DateTime(2026, 11, 3), Monto = 500_000m, OrigenFondosId = banco,
            Imputaciones = [new ImputacionRequest { ComprobanteId = factura.Id!.Value, Monto = 500_000m }]
        }, "qa");
        Assert.True(pago.Ok, pago.Error);
        await VerificarSaldosAsync(proveedor, 510_000m, 510_000m, 0m);
    }

    [Fact]
    public async Task Tesoreria_ElEjemploPasoAPaso_CoincideConElServicio()
    {
        var proveedor = await ProveedorAsync();
        var banco = await OrigenAsync("BCO", "Banco - cuenta corriente");
        var efectivo = await OrigenAsync("EFE", "Efectivo administración");
        var factura = await _cxp.RegistrarComprobanteAsync(new ComprobanteProveedorRequest
        {
            ProveedorId = proveedor, Tipo = TipoComprobanteProveedor.Factura, NumeroComprobante = "A-0001-00000002",
            FechaEmision = new DateTime(2026, 11, 1), ImporteNeto = 1_000_000m, ImporteIva = 210_000m, ImporteTotal = 1_210_000m
        }, "qa");

        async Task VerificarAsync(decimal enBanco, decimal enEfectivo)
        {
            var resumen = await _tesoreria.ObtenerResumenAsync(new DateTime(2026, 11, 1), new DateTime(2026, 11, 14));
            Assert.Equal(enBanco, resumen.Origenes.Single(o => o.OrigenFondosId == banco).Total);
            Assert.Equal(enEfectivo, resumen.Origenes.Single(o => o.OrigenFondosId == efectivo).Total);
            Assert.Equal(enBanco + enEfectivo, resumen.Total);
        }

        var pago = await _cxp.RegistrarPagoAsync(new PagoProveedorRequest
        {
            ProveedorId = proveedor, Fecha = new DateTime(2026, 11, 3), Monto = 500_000m, OrigenFondosId = banco,
            Imputaciones = [new ImputacionRequest { ComprobanteId = factura.Id!.Value, Monto = 500_000m }]
        }, "qa");
        Assert.True(pago.Ok, pago.Error);
        await VerificarAsync(500_000m, 0m);

        var anticipo = await _cxp.RegistrarAnticipoAsync(new AnticipoProveedorRequest
        {
            ProveedorId = proveedor, Fecha = new DateTime(2026, 11, 4), Monto = 80_000m, OrigenFondosId = efectivo,
            Medio = MedioPagoProveedor.Efectivo
        }, "qa");
        Assert.True(anticipo.Ok, anticipo.Error);
        await VerificarAsync(500_000m, 80_000m);

        var anulacion = await _cxp.AnularAnticipoAsync(anticipo.Id!.Value, "Cargado por error", "qa");
        Assert.True(anulacion.Ok, anulacion.Error);
        await VerificarAsync(500_000m, 0m);
    }

    [Fact]
    public void Valoracion_ElSimuladorUsaElPromedioMovilReal()
    {
        // Valores iniciales del simulador: 5 a $200.000, se venden 2 y se compran 5 a $240.000.
        var r = ValoracionStockSimulador.Simular(5, 200_000m, 2, 5, 240_000m);

        Assert.Equal(8, r.Stock);
        Assert.Equal(225_000m, r.CostoPromedio);
        Assert.Equal(1_800_000m, r.ValorGerencial);
        Assert.Equal(240_000m, r.UltimoCosto);
        Assert.Equal(1_920_000m, r.ValorAUltimoCosto);
        Assert.Equal(120_000m, r.Diferencia);
    }

    [Fact]
    public void Valoracion_VendiendoTodoAntesDeRecomprar_QuedaElCostoNuevo()
    {
        var r = ValoracionStockSimulador.Simular(5, 200_000m, 5, 5, 240_000m);

        Assert.Equal(5, r.Stock);
        Assert.Equal(240_000m, r.CostoPromedio);
        Assert.Equal(0m, r.Diferencia);
    }
}
