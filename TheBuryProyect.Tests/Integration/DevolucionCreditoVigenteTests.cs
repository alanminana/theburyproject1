using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Tests.Integration;

/// <summary>HITO4-E1: no se devuelve una venta cuyo crédito personal sigue vigente (crear, aprobar, completar).</summary>
public class DevolucionCreditoVigenteTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly DevolucionService _service;

    public DevolucionCreditoVigenteTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();
        _context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        _context.Database.EnsureCreated();
        _service = new DevolucionService(_context,
            new MovimientoStockService(_context, NullLogger<MovimientoStockService>.Instance),
            new StubUserDevCred(), NullLogger<DevolucionService>.Instance);
    }

    public void Dispose() { _context.Dispose(); _connection.Dispose(); }

    private async Task<(Cliente Cliente, Producto Producto, Venta Venta)> SeedAsync(EstadoCredito? estadoCredito)
    {
        var doc = Guid.NewGuid().ToString("N")[..8];
        var cliente = new Cliente { Nombre = "Test", Apellido = "Dev", TipoDocumento = "DNI", NumeroDocumento = doc, Email = $"{doc}@test.com", Activo = true };
        var cat = new Categoria { Codigo = doc, Nombre = "Cat-" + doc, Activo = true };
        var marca = new Marca { Codigo = doc, Nombre = "Marca-" + doc, Activo = true };
        _context.AddRange(cliente, cat, marca);
        await _context.SaveChangesAsync();
        var producto = new Producto { Codigo = doc, Nombre = "Prod-" + doc, CategoriaId = cat.Id, MarcaId = marca.Id, PrecioCompra = 10m, PrecioVenta = 50m, PorcentajeIVA = 21m, StockActual = 100m, Activo = true };
        _context.Productos.Add(producto);
        await _context.SaveChangesAsync();

        var venta = new Venta
        {
            Numero = doc, ClienteId = cliente.Id, Estado = EstadoVenta.Entregada, TipoPago = TipoPago.CreditoPersonal,
            FechaVenta = DateTime.UtcNow,
            Detalles = new List<VentaDetalle> { new() { ProductoId = producto.Id, Cantidad = 5, PrecioUnitario = 50m, Subtotal = 250m, CostoUnitarioAlMomento = 17.5m, CostoTotalAlMomento = 87.5m } }
        };
        _context.Ventas.Add(venta);
        await _context.SaveChangesAsync();

        if (estadoCredito != null)
        {
            var credito = new Credito { ClienteId = cliente.Id, Numero = "CR-" + doc, Estado = estadoCredito.Value };
            _context.Creditos.Add(credito);
            await _context.SaveChangesAsync();
            venta.CreditoId = credito.Id;
            await _context.SaveChangesAsync();
        }
        return (cliente, producto, venta);
    }

    private async Task SetEstadoCreditoAsync(Venta venta, EstadoCredito estado)
    {
        var c = await _context.Creditos.FirstAsync(x => x.Id == venta.CreditoId);
        c.Estado = estado;
        await _context.SaveChangesAsync();
    }

    private Task<Devolucion> CrearAsync(Cliente c, Producto p, Venta v) => _service.CrearDevolucionAsync(
        new Devolucion { VentaId = v.Id, ClienteId = c.Id, Motivo = MotivoDevolucion.DefectoFabrica, Descripcion = "Test" },
        new List<DevolucionDetalle> { new() { ProductoId = p.Id, Cantidad = 1 } });

    private async Task<Devolucion> SeedDevolucionAsync(Venta v, Cliente c, EstadoDevolucion estado)
    {
        var dev = new Devolucion
        {
            NumeroDevolucion = "DEV-" + Guid.NewGuid().ToString("N")[..6], VentaId = v.Id, ClienteId = c.Id,
            Motivo = MotivoDevolucion.DefectoFabrica, Descripcion = "Test", Estado = estado,
            TipoResolucion = TipoResolucionDevolucion.CambioMismoProducto, FechaDevolucion = DateTime.UtcNow, TotalDevolucion = 50m
        };
        _context.Devoluciones.Add(dev);
        await _context.SaveChangesAsync();
        await _context.Entry(dev).ReloadAsync();
        return dev;
    }

    [Theory]
    [InlineData(EstadoCredito.Configurado)]
    [InlineData(EstadoCredito.Generado)]
    [InlineData(EstadoCredito.Activo)]
    [InlineData(EstadoCredito.PendienteConfiguracion)]
    public async Task Crear_ConCreditoVigente_Bloquea(EstadoCredito estado)
    {
        var (c, p, v) = await SeedAsync(estado);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => CrearAsync(c, p, v));
        Assert.Contains("crédito personal vigente", ex.Message);
        Assert.Equal(0, await _context.Devoluciones.CountAsync());
    }

    [Theory]
    [InlineData(EstadoCredito.Cancelado)]
    [InlineData(EstadoCredito.Finalizado)]
    [InlineData(null)]
    public async Task Crear_SinCreditoVigente_Permite(EstadoCredito? estado)
    {
        var (c, p, v) = await SeedAsync(estado);
        Assert.True((await CrearAsync(c, p, v)).Id > 0);
    }

    [Fact]
    public async Task Aprobar_ConCreditoVigente_Bloquea_YPermiteTrasCancelarlo()
    {
        var (c, _, v) = await SeedAsync(EstadoCredito.Activo);
        var dev = await SeedDevolucionAsync(v, c, EstadoDevolucion.Pendiente);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.AprobarDevolucionAsync(dev.Id, "gerente", dev.RowVersion));
        Assert.Contains("crédito personal vigente", ex.Message);

        await SetEstadoCreditoAsync(v, EstadoCredito.Cancelado);
        _context.ChangeTracker.Clear();
        dev = await _context.Devoluciones.AsNoTracking().FirstAsync(d => d.Id == dev.Id);
        var aprobada = await _service.AprobarDevolucionAsync(dev.Id, "gerente", dev.RowVersion);
        Assert.Equal(EstadoDevolucion.Aprobada, aprobada.Estado);
    }

    [Fact]
    public async Task Completar_ConCreditoVigente_Bloquea_NoToqueEstado()
    {
        var (c, _, v) = await SeedAsync(EstadoCredito.Generado);
        var dev = await SeedDevolucionAsync(v, c, EstadoDevolucion.Aprobada);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CompletarDevolucionAsync(dev.Id, dev.RowVersion));
        Assert.Contains("crédito personal vigente", ex.Message);
        _context.ChangeTracker.Clear();
        Assert.Equal(EstadoDevolucion.Aprobada, (await _context.Devoluciones.FirstAsync(d => d.Id == dev.Id)).Estado);
    }

    private sealed class StubUserDevCred : ICurrentUserService
    {
        public string GetUsername() => "testuser";
        public string GetUserId() => "user-test";
        public string GetEmail() => "test@test.com";
        public bool IsAuthenticated() => true;
        public bool IsInRole(string role) => false;
        public bool HasPermission(string modulo, string accion) => true;
        public string GetIpAddress() => "127.0.0.1";
    }
}
