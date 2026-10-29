using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Tests.Integration;

/// <summary>Autoridad del servidor para la baja de producto: bloquea con stock o con OC abierta pendiente.</summary>
public class ProductoBajaValidacionTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly ProductoService _service;

    public ProductoBajaValidacionTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();
        _context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        _context.Database.EnsureCreated();
        var precioHistorico = new PrecioHistoricoService(_context, NullLogger<PrecioHistoricoService>.Instance);
        _service = new ProductoService(_context, NullLogger<ProductoService>.Instance, precioHistorico,
            new StubUserBaja(), new PrecioVigenteResolver(_context));
    }

    public void Dispose() { _context.Dispose(); _connection.Dispose(); }

    private async Task<Producto> SeedProductoAsync(decimal stock)
    {
        var code = Guid.NewGuid().ToString("N")[..8];
        var cat = new Categoria { Codigo = code, Nombre = "Cat-" + code, Activo = true };
        var marca = new Marca { Codigo = code, Nombre = "Marca-" + code, Activo = true };
        _context.Categorias.Add(cat);
        _context.Marcas.Add(marca);
        await _context.SaveChangesAsync();
        var p = new Producto
        {
            Codigo = code, Nombre = "Prod-" + code, CategoriaId = cat.Id, MarcaId = marca.Id,
            PrecioCompra = 10m, PrecioVenta = 20m, PorcentajeIVA = 21m, StockActual = stock, Activo = true
        };
        _context.Productos.Add(p);
        await _context.SaveChangesAsync();
        return p;
    }

    private async Task SeedOcAsync(int productoId, EstadoOrdenCompra estado, int cantidad, int recibida)
    {
        var code = Guid.NewGuid().ToString("N")[..8];
        var prov = new Proveedor { RazonSocial = "Prov-" + code, Cuit = "20" + Random.Shared.Next(10000000, 99999999) + "0", Activo = true };
        _context.Proveedores.Add(prov);
        await _context.SaveChangesAsync();
        var oc = new OrdenCompra { Numero = "OC-" + code, ProveedorId = prov.Id, Estado = estado };
        oc.Detalles.Add(new OrdenCompraDetalle { ProductoId = productoId, Cantidad = cantidad, CantidadRecibida = recibida, PrecioUnitario = 10m });
        _context.OrdenesCompra.Add(oc);
        await _context.SaveChangesAsync();
    }

    [Fact]
    public async Task Delete_ConStock_Bloquea()
    {
        var p = await SeedProductoAsync(3m);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.DeleteAsync(p.Id));
        Assert.Contains("stock", ex.Message);
        Assert.False((await _context.Productos.FindAsync(p.Id))!.IsDeleted);
    }

    [Theory]
    [InlineData(EstadoOrdenCompra.Borrador)]
    [InlineData(EstadoOrdenCompra.Enviada)]
    [InlineData(EstadoOrdenCompra.Confirmada)]
    [InlineData(EstadoOrdenCompra.EnTransito)]
    public async Task Delete_ConOcAbiertaPendiente_Bloquea(EstadoOrdenCompra estado)
    {
        var p = await SeedProductoAsync(0m);
        await SeedOcAsync(p.Id, estado, 5, 2);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.DeleteAsync(p.Id));
        Assert.Contains("órdenes de compra abiertas", ex.Message);
        Assert.False((await _context.Productos.FindAsync(p.Id))!.IsDeleted);
    }

    [Fact]
    public async Task Delete_SinStockNiOcAbierta_Permite()
    {
        var p = await SeedProductoAsync(0m);
        await SeedOcAsync(p.Id, EstadoOrdenCompra.Cancelada, 5, 0);
        await SeedOcAsync(p.Id, EstadoOrdenCompra.Recibida, 5, 5);
        await SeedOcAsync(p.Id, EstadoOrdenCompra.Confirmada, 5, 5); // sin pendiente
        Assert.True(await _service.DeleteAsync(p.Id));
        Assert.True((await _context.Productos.IgnoreQueryFilters().FirstAsync(x => x.Id == p.Id)).IsDeleted);
    }

    private sealed class StubUserBaja : ICurrentUserService
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
