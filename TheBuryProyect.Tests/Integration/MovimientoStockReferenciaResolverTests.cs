using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Integration;

public class MovimientoStockReferenciaResolverTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly MovimientoStockReferenciaResolver _resolver;

    public MovimientoStockReferenciaResolverTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        _resolver = new MovimientoStockReferenciaResolver(_context);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private async Task<Cliente> SeedCliente(string nombre, string apellido)
    {
        var cliente = new Cliente
        {
            Nombre = nombre,
            Apellido = apellido,
            TipoDocumento = "DNI",
            NumeroDocumento = Guid.NewGuid().ToString("N")[..8]
        };
        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync();
        return cliente;
    }

    [Fact]
    public async Task Enriquecer_SalidaPorVentaEfectivo_ResuelveVentaClienteYMedio()
    {
        var cliente = await SeedCliente("Juan", "Pérez");
        var venta = new Venta
        {
            Numero = "V-2026-0045",
            ClienteId = cliente.Id,
            TipoPago = TipoPago.Efectivo,
            Estado = EstadoVenta.Confirmada,
            FechaVenta = DateTime.UtcNow,
            Total = 1000m
        };
        _context.Ventas.Add(venta);
        await _context.SaveChangesAsync();

        var movs = new List<MovimientoStockViewModel>
        {
            new() { Id = 1, Tipo = TipoMovimiento.Salida, Referencia = "Venta V-2026-0045" }
        };

        await _resolver.EnriquecerAsync(movs);

        var m = movs[0];
        Assert.Equal("Venta", m.ReferenciaTipo);
        Assert.Equal(venta.Id, m.VentaId);
        Assert.Equal(cliente.Id, m.ClienteId);
        Assert.Equal("Juan Pérez", m.ClienteNombre);
        Assert.Equal("Efectivo", m.MedioPagoTexto);
        Assert.Contains("Juan Pérez", m.ReferenciaTexto);
        Assert.Contains("Efectivo", m.ReferenciaTexto);
    }

    [Fact]
    public async Task Enriquecer_VentaCreditoPersonal_MuestraCantidadCuotas()
    {
        var cliente = await SeedCliente("María", "Gómez");
        var credito = new Credito
        {
            Numero = "CRE-1",
            ClienteId = cliente.Id,
            Estado = EstadoCredito.Generado,
            MontoSolicitado = 6000m,
            MontoAprobado = 6000m,
            SaldoPendiente = 6000m,
            TasaInteres = 5m,
            CantidadCuotas = 6,
            FechaSolicitud = DateTime.UtcNow
        };
        _context.Creditos.Add(credito);
        await _context.SaveChangesAsync();

        var venta = new Venta
        {
            Numero = "V-2026-0046",
            ClienteId = cliente.Id,
            TipoPago = TipoPago.CreditoPersonal,
            CreditoId = credito.Id,
            Estado = EstadoVenta.Confirmada,
            FechaVenta = DateTime.UtcNow,
            Total = 6000m
        };
        _context.Ventas.Add(venta);
        await _context.SaveChangesAsync();

        var movs = new List<MovimientoStockViewModel>
        {
            new() { Id = 1, Tipo = TipoMovimiento.Salida, Referencia = "Venta V-2026-0046" }
        };

        await _resolver.EnriquecerAsync(movs);

        Assert.Equal("Crédito personal, 6 cuotas", movs[0].MedioPagoTexto);
    }

    [Fact]
    public async Task Enriquecer_EntradaPorOrdenCompra_ResuelveProveedor()
    {
        var proveedor = new Proveedor { RazonSocial = "Frávega", Cuit = "30-11111111-1" };
        _context.Proveedores.Add(proveedor);
        await _context.SaveChangesAsync();

        var orden = new OrdenCompra
        {
            Numero = "OC-2026-0001",
            ProveedorId = proveedor.Id,
            FechaEmision = DateTime.UtcNow
        };
        _context.OrdenesCompra.Add(orden);
        await _context.SaveChangesAsync();

        var movs = new List<MovimientoStockViewModel>
        {
            new() { Id = 1, Tipo = TipoMovimiento.Entrada, OrdenCompraId = orden.Id, OrdenCompraNumero = "OC-2026-0001" }
        };

        await _resolver.EnriquecerAsync(movs);

        var m = movs[0];
        Assert.Equal("OrdenCompra", m.ReferenciaTipo);
        Assert.Equal(proveedor.Id, m.ProveedorId);
        Assert.Equal("Frávega", m.ProveedorNombre);
        Assert.Contains("OC-2026-0001", m.ReferenciaTexto);
        Assert.Contains("Frávega", m.ReferenciaTexto);
    }

    [Fact]
    public async Task Enriquecer_SinReferenciaResoluble_ClasificaComoAjusteUOtro()
    {
        var movs = new List<MovimientoStockViewModel>
        {
            new() { Id = 1, Tipo = TipoMovimiento.Ajuste, Referencia = null, Motivo = "Recuento físico" },
            new() { Id = 2, Tipo = TipoMovimiento.Salida, Referencia = "Venta V-INEXISTENTE" }
        };

        await _resolver.EnriquecerAsync(movs);

        Assert.Equal("Ajuste", movs[0].ReferenciaTipo);
        Assert.Equal("Recuento físico", movs[0].ReferenciaTexto);
        Assert.Equal("Otro", movs[1].ReferenciaTipo);
    }
}
