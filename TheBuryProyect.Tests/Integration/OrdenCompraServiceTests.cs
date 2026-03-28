using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Integration;

// ---------------------------------------------------------------------------
// Stubs internos — sin dependencia de Moq
// ---------------------------------------------------------------------------
file sealed class StubCurrentUserService : ICurrentUserService
{
    public string GetUsername() => "testuser";
    public string GetUserId() => "user-001";
    public string GetEmail() => "test@test.com";
    public bool IsAuthenticated() => true;
    public bool IsInRole(string role) => false;
    public bool HasPermission(string modulo, string accion) => true;
    public string GetIpAddress() => "127.0.0.1";
}

/// <summary>
/// Tests de integración para OrdenCompraService.
/// Cubren CreateAsync (validaciones de proveedor, número duplicado),
/// GenerarNumeroOrdenAsync (secuencia anual), CambiarEstadoAsync,
/// CalcularTotalOrdenAsync y RecepcionarAsync (estado, cantidades, stock).
/// </summary>
public class OrdenCompraServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly OrdenCompraService _service;

    public OrdenCompraServiceTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        var movimientoStockService = new MovimientoStockService(
            _context,
            NullLogger<MovimientoStockService>.Instance);

        _service = new OrdenCompraService(
            _context,
            NullLogger<OrdenCompraService>.Instance,
            movimientoStockService,
            new StubCurrentUserService());
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<Proveedor> SeedProveedorAsync()
    {
        var cuit = Guid.NewGuid().ToString("N")[..11];
        var proveedor = new Proveedor
        {
            Cuit = cuit,
            RazonSocial = "Proveedor-" + cuit,
            Activo = true
        };
        _context.Set<Proveedor>().Add(proveedor);
        await _context.SaveChangesAsync();
        return proveedor;
    }

    private async Task<Producto> SeedProductoAsync(decimal stockInicial = 0m)
    {
        var codigo = Guid.NewGuid().ToString("N")[..8];

        var categoria = new Categoria { Codigo = codigo, Nombre = "Cat-" + codigo, Activo = true };
        _context.Set<Categoria>().Add(categoria);
        var marca = new Marca { Codigo = codigo, Nombre = "Marca-" + codigo, Activo = true };
        _context.Set<Marca>().Add(marca);
        await _context.SaveChangesAsync();

        var producto = new Producto
        {
            Codigo = codigo,
            Nombre = "Prod-" + codigo,
            CategoriaId = categoria.Id,
            MarcaId = marca.Id,
            PrecioCompra = 10m,
            PrecioVenta = 15m,
            PorcentajeIVA = 21m,
            StockActual = stockInicial,
            Activo = true
        };
        _context.Set<Producto>().Add(producto);
        await _context.SaveChangesAsync();
        return producto;
    }

    private async Task<OrdenCompra> SeedOrdenAsync(
        int proveedorId,
        int productoId,
        int cantidad = 10,
        decimal precioUnitario = 100m,
        EstadoOrdenCompra estado = EstadoOrdenCompra.Borrador)
    {
        var numero = $"OC-{DateTime.UtcNow.Year}-{Guid.NewGuid().ToString("N")[..4]}";
        var orden = new OrdenCompra
        {
            Numero = numero,
            ProveedorId = proveedorId,
            Estado = estado,
            FechaEmision = DateTime.UtcNow,
            Detalles = new List<OrdenCompraDetalle>
            {
                new()
                {
                    ProductoId = productoId,
                    Cantidad = cantidad,
                    PrecioUnitario = precioUnitario,
                    Subtotal = cantidad * precioUnitario,
                    CantidadRecibida = 0
                }
            }
        };
        _context.Set<OrdenCompra>().Add(orden);
        await _context.SaveChangesAsync();

        // Reload para que EF rellene RowVersion generado por SQLite
        await _context.Entry(orden).ReloadAsync();
        return orden;
    }

    // -------------------------------------------------------------------------
    // GenerarNumeroOrdenAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GenerarNumero_SinOrdenesExistentes_RetornaPrimero()
    {
        var numero = await _service.GenerarNumeroOrdenAsync();
        var anio = DateTime.UtcNow.Year;
        Assert.Equal($"OC-{anio}-0001", numero);
    }

    [Fact]
    public async Task GenerarNumero_ConOrdenExistente_IncrementaSecuencia()
    {
        var proveedor = await SeedProveedorAsync();
        var producto = await SeedProductoAsync();

        var anio = DateTime.UtcNow.Year;

        // Insertar directamente una orden con número conocido
        _context.Set<OrdenCompra>().Add(new OrdenCompra
        {
            Numero = $"OC-{anio}-0005",
            ProveedorId = proveedor.Id,
            Estado = EstadoOrdenCompra.Borrador,
            FechaEmision = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        var siguiente = await _service.GenerarNumeroOrdenAsync();
        Assert.Equal($"OC-{anio}-0006", siguiente);
    }

    // -------------------------------------------------------------------------
    // CreateAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Create_ProveedorSinProductosAsociados_PermiteCrear()
    {
        // Cuando el proveedor no tiene ProveedorProductos, no se valida asociación
        var proveedor = await SeedProveedorAsync();
        var producto = await SeedProductoAsync();

        var orden = new OrdenCompra
        {
            Numero = "OC-TEST-0001",
            ProveedorId = proveedor.Id,
            Estado = EstadoOrdenCompra.Borrador,
            FechaEmision = DateTime.UtcNow,
            Detalles = new List<OrdenCompraDetalle>
            {
                new() { ProductoId = producto.Id, Cantidad = 5, PrecioUnitario = 20m }
            }
        };

        var resultado = await _service.CreateAsync(orden);

        Assert.True(resultado.Id > 0);
        Assert.Equal("OC-TEST-0001", resultado.Numero);
        // Totales calculados: Subtotal=100, Descuento=0, IVA=21, Total=121
        Assert.Equal(100m, resultado.Subtotal);
        Assert.Equal(21m, resultado.Iva);
        Assert.Equal(121m, resultado.Total);
    }

    [Fact]
    public async Task Create_NumeroDuplicado_LanzaExcepcion()
    {
        var proveedor = await SeedProveedorAsync();
        var producto = await SeedProductoAsync();

        _context.Set<OrdenCompra>().Add(new OrdenCompra
        {
            Numero = "OC-DUP-0001",
            ProveedorId = proveedor.Id,
            Estado = EstadoOrdenCompra.Borrador,
            FechaEmision = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        var ordenDup = new OrdenCompra
        {
            Numero = "OC-DUP-0001",
            ProveedorId = proveedor.Id,
            Estado = EstadoOrdenCompra.Borrador,
            FechaEmision = DateTime.UtcNow,
            Detalles = new List<OrdenCompraDetalle>()
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CreateAsync(ordenDup));
    }

    [Fact]
    public async Task Create_ProveedorNoExiste_LanzaExcepcion()
    {
        var orden = new OrdenCompra
        {
            Numero = "OC-TEST-0002",
            ProveedorId = 99999,
            Estado = EstadoOrdenCompra.Borrador,
            FechaEmision = DateTime.UtcNow,
            Detalles = new List<OrdenCompraDetalle>()
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CreateAsync(orden));
    }

    // -------------------------------------------------------------------------
    // CambiarEstadoAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CambiarEstado_OrdenExistente_ActualizaEstado()
    {
        var proveedor = await SeedProveedorAsync();
        var producto = await SeedProductoAsync();
        var orden = await SeedOrdenAsync(proveedor.Id, producto.Id, estado: EstadoOrdenCompra.Borrador);

        var resultado = await _service.CambiarEstadoAsync(orden.Id, EstadoOrdenCompra.Enviada);

        Assert.True(resultado);
        var ordenBd = await _context.Set<OrdenCompra>().FirstAsync(o => o.Id == orden.Id);
        Assert.Equal(EstadoOrdenCompra.Enviada, ordenBd.Estado);
    }

    [Fact]
    public async Task CambiarEstado_OrdenNoExiste_RetornaFalse()
    {
        var resultado = await _service.CambiarEstadoAsync(99999, EstadoOrdenCompra.Enviada);
        Assert.False(resultado);
    }

    [Fact]
    public async Task CambiarEstado_ARecibida_SetearFechaRecepcion()
    {
        var proveedor = await SeedProveedorAsync();
        var producto = await SeedProductoAsync();
        var orden = await SeedOrdenAsync(proveedor.Id, producto.Id, estado: EstadoOrdenCompra.Confirmada);

        await _service.CambiarEstadoAsync(orden.Id, EstadoOrdenCompra.Recibida);

        var ordenBd = await _context.Set<OrdenCompra>().FirstAsync(o => o.Id == orden.Id);
        Assert.Equal(EstadoOrdenCompra.Recibida, ordenBd.Estado);
        Assert.NotNull(ordenBd.FechaRecepcion);
    }

    // -------------------------------------------------------------------------
    // NumeroOrdenExisteAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task NumeroOrdenExiste_NumeroExistente_ReturnsTrue()
    {
        var proveedor = await SeedProveedorAsync();
        _context.Set<OrdenCompra>().Add(new OrdenCompra
        {
            Numero = "OC-EXIST",
            ProveedorId = proveedor.Id,
            Estado = EstadoOrdenCompra.Borrador,
            FechaEmision = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        var existe = await _service.NumeroOrdenExisteAsync("OC-EXIST");
        Assert.True(existe);
    }

    [Fact]
    public async Task NumeroOrdenExiste_NumeroNoExistente_ReturnsFalse()
    {
        var existe = await _service.NumeroOrdenExisteAsync("OC-NOEXISTE");
        Assert.False(existe);
    }

    // -------------------------------------------------------------------------
    // CalcularTotalOrdenAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CalcularTotal_ConDetalles_RetornaTotalCorrecto()
    {
        var proveedor = await SeedProveedorAsync();
        var producto = await SeedProductoAsync();

        // Crear via service para que CalcularTotales se ejecute
        // 5 unidades x $20 = $100; IVA 21% = $21; Total = $121
        var orden = await _service.CreateAsync(new OrdenCompra
        {
            Numero = "OC-CALC-0001",
            ProveedorId = proveedor.Id,
            Estado = EstadoOrdenCompra.Borrador,
            FechaEmision = DateTime.UtcNow,
            Detalles = new List<OrdenCompraDetalle>
            {
                new() { ProductoId = producto.Id, Cantidad = 5, PrecioUnitario = 20m }
            }
        });

        // CalcularTotalOrdenAsync retorna orden.Total — el valor persiste en CreateAsync
        Assert.Equal(121m, orden.Total);

        // Verificar también via el método público (re-lee de BD)
        // Detach para forzar lectura real desde BD
        _context.ChangeTracker.Clear();
        var total = await _service.CalcularTotalOrdenAsync(orden.Id);
        Assert.Equal(121m, total);
    }

    [Fact]
    public async Task CalcularTotal_OrdenNoExiste_RetornaCero()
    {
        var total = await _service.CalcularTotalOrdenAsync(99999);
        Assert.Equal(0m, total);
    }

    // -------------------------------------------------------------------------
    // RecepcionarAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Recepcionar_OrdenConfirmada_TotalRecibido_EstadoRecibida()
    {
        var proveedor = await SeedProveedorAsync();
        var producto = await SeedProductoAsync(stockInicial: 0m);
        var orden = await SeedOrdenAsync(proveedor.Id, producto.Id,
            cantidad: 10, estado: EstadoOrdenCompra.Confirmada);

        var detalle = await _context.Set<OrdenCompraDetalle>()
            .FirstAsync(d => d.OrdenCompraId == orden.Id);

        var recepcion = new List<RecepcionDetalleViewModel>
        {
            new() { DetalleId = detalle.Id, ProductoId = producto.Id, CantidadARecepcionar = 10 }
        };

        var ordenActualizada = await _service.RecepcionarAsync(orden.Id, orden.RowVersion, recepcion);

        Assert.Equal(EstadoOrdenCompra.Recibida, ordenActualizada.Estado);
        Assert.NotNull(ordenActualizada.FechaRecepcion);

        var stockBd = (await _context.Set<Producto>().FirstAsync(p => p.Id == producto.Id)).StockActual;
        Assert.Equal(10m, stockBd);
    }

    [Fact]
    public async Task Recepcionar_RecepcionParcial_EstadoEnTransito()
    {
        var proveedor = await SeedProveedorAsync();
        var producto = await SeedProductoAsync(stockInicial: 0m);
        var orden = await SeedOrdenAsync(proveedor.Id, producto.Id,
            cantidad: 10, estado: EstadoOrdenCompra.Confirmada);

        var detalle = await _context.Set<OrdenCompraDetalle>()
            .FirstAsync(d => d.OrdenCompraId == orden.Id);

        var recepcion = new List<RecepcionDetalleViewModel>
        {
            new() { DetalleId = detalle.Id, ProductoId = producto.Id, CantidadARecepcionar = 6 }
        };

        var ordenActualizada = await _service.RecepcionarAsync(orden.Id, orden.RowVersion, recepcion);

        Assert.Equal(EstadoOrdenCompra.EnTransito, ordenActualizada.Estado);

        var stockBd = (await _context.Set<Producto>().FirstAsync(p => p.Id == producto.Id)).StockActual;
        Assert.Equal(6m, stockBd);
    }

    [Fact]
    public async Task Recepcionar_EstadoBorrador_LanzaExcepcion()
    {
        var proveedor = await SeedProveedorAsync();
        var producto = await SeedProductoAsync();
        var orden = await SeedOrdenAsync(proveedor.Id, producto.Id,
            estado: EstadoOrdenCompra.Borrador); // estado inválido

        var detalle = await _context.Set<OrdenCompraDetalle>()
            .FirstAsync(d => d.OrdenCompraId == orden.Id);

        var recepcion = new List<RecepcionDetalleViewModel>
        {
            new() { DetalleId = detalle.Id, ProductoId = producto.Id, CantidadARecepcionar = 5 }
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.RecepcionarAsync(orden.Id, orden.RowVersion, recepcion));
    }

    [Fact]
    public async Task Recepcionar_CantidadSuperaSolicitada_LanzaExcepcion()
    {
        var proveedor = await SeedProveedorAsync();
        var producto = await SeedProductoAsync();
        var orden = await SeedOrdenAsync(proveedor.Id, producto.Id,
            cantidad: 5, estado: EstadoOrdenCompra.Confirmada);

        var detalle = await _context.Set<OrdenCompraDetalle>()
            .FirstAsync(d => d.OrdenCompraId == orden.Id);

        var recepcion = new List<RecepcionDetalleViewModel>
        {
            new() { DetalleId = detalle.Id, ProductoId = producto.Id, CantidadARecepcionar = 6 } // excede 5
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.RecepcionarAsync(orden.Id, orden.RowVersion, recepcion));
    }

    [Fact]
    public async Task Recepcionar_RowVersionNulo_LanzaExcepcion()
    {
        var proveedor = await SeedProveedorAsync();
        var producto = await SeedProductoAsync();
        var orden = await SeedOrdenAsync(proveedor.Id, producto.Id,
            estado: EstadoOrdenCompra.Confirmada);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.RecepcionarAsync(orden.Id, Array.Empty<byte>(), new List<RecepcionDetalleViewModel>()));
    }

    [Fact]
    public async Task Recepcionar_OrdenNoExiste_LanzaExcepcion()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.RecepcionarAsync(99999, new byte[8], new List<RecepcionDetalleViewModel>()));
    }

    // =========================================================================
    // GetAllAsync
    // =========================================================================

    [Fact]
    public async Task GetAll_SinOrdenes_RetornaVacio()
    {
        var resultado = await _service.GetAllAsync();
        Assert.Empty(resultado);
    }

    [Fact]
    public async Task GetAll_ConOrdenes_DevuelveTodas()
    {
        var proveedor = await SeedProveedorAsync();
        var producto = await SeedProductoAsync();
        await SeedOrdenAsync(proveedor.Id, producto.Id);
        await SeedOrdenAsync(proveedor.Id, producto.Id);

        var resultado = await _service.GetAllAsync();

        Assert.Equal(2, resultado.Count());
    }

    // =========================================================================
    // GetByIdAsync
    // =========================================================================

    [Fact]
    public async Task GetById_OrdenExistente_RetornaOrden()
    {
        var proveedor = await SeedProveedorAsync();
        var producto = await SeedProductoAsync();
        var orden = await SeedOrdenAsync(proveedor.Id, producto.Id);

        var resultado = await _service.GetByIdAsync(orden.Id);

        Assert.NotNull(resultado);
        Assert.Equal(orden.Id, resultado!.Id);
    }

    [Fact]
    public async Task GetById_OrdenInexistente_RetornaNull()
    {
        var resultado = await _service.GetByIdAsync(99999);
        Assert.Null(resultado);
    }

    // =========================================================================
    // DeleteAsync
    // =========================================================================

    [Fact]
    public async Task Delete_OrdenEnBorrador_RetornaTrue()
    {
        var proveedor = await SeedProveedorAsync();
        var producto = await SeedProductoAsync();
        var orden = await SeedOrdenAsync(proveedor.Id, producto.Id, estado: EstadoOrdenCompra.Borrador);

        var resultado = await _service.DeleteAsync(orden.Id);

        Assert.True(resultado);
        var ordenBd = await _context.OrdenesCompra.IgnoreQueryFilters().FirstAsync(o => o.Id == orden.Id);
        Assert.True(ordenBd.IsDeleted);
    }

    [Fact]
    public async Task Delete_OrdenRecibida_LanzaExcepcion()
    {
        var proveedor = await SeedProveedorAsync();
        var producto = await SeedProductoAsync();
        var orden = await SeedOrdenAsync(proveedor.Id, producto.Id, estado: EstadoOrdenCompra.Recibida);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.DeleteAsync(orden.Id));
    }

    [Fact]
    public async Task Delete_OrdenInexistente_RetornaFalse()
    {
        var resultado = await _service.DeleteAsync(99999);
        Assert.False(resultado);
    }

    // =========================================================================
    // UpdateAsync
    // =========================================================================

    [Fact]
    public async Task Update_SinRowVersion_LanzaExcepcion()
    {
        var proveedor = await SeedProveedorAsync();
        var producto = await SeedProductoAsync();
        var orden = await SeedOrdenAsync(proveedor.Id, producto.Id);

        orden.RowVersion = null!;
        orden.Detalles ??= new List<OrdenCompraDetalle>();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.UpdateAsync(orden));
    }

    [Fact]
    public async Task Update_HappyPath_ActualizaObservaciones()
    {
        var proveedor = await SeedProveedorAsync();
        var producto = await SeedProductoAsync();
        var orden = await SeedOrdenAsync(proveedor.Id, producto.Id);

        orden.Observaciones = "Actualizado";
        orden.Detalles ??= new List<OrdenCompraDetalle>();

        var resultado = await _service.UpdateAsync(orden);

        Assert.Equal("Actualizado", resultado.Observaciones);
    }

    // =========================================================================
    // GetByProveedorIdAsync
    // =========================================================================

    [Fact]
    public async Task GetByProveedor_FiltraPorProveedor()
    {
        var p1 = await SeedProveedorAsync();
        var p2 = await SeedProveedorAsync();
        var producto = await SeedProductoAsync();
        await SeedOrdenAsync(p1.Id, producto.Id);
        await SeedOrdenAsync(p2.Id, producto.Id);

        var resultado = await _service.GetByProveedorIdAsync(p1.Id);

        Assert.Single(resultado);
        Assert.All(resultado, o => Assert.Equal(p1.Id, o.ProveedorId));
    }

    // =========================================================================
    // SearchAsync
    // =========================================================================

    [Fact]
    public async Task Search_PorEstado_FiltraCorrectamente()
    {
        var proveedor = await SeedProveedorAsync();
        var producto = await SeedProductoAsync();
        await SeedOrdenAsync(proveedor.Id, producto.Id, estado: EstadoOrdenCompra.Borrador);
        await SeedOrdenAsync(proveedor.Id, producto.Id, estado: EstadoOrdenCompra.Confirmada);

        var resultado = await _service.SearchAsync(estado: EstadoOrdenCompra.Borrador);

        Assert.Single(resultado);
        Assert.All(resultado, o => Assert.Equal(EstadoOrdenCompra.Borrador, o.Estado));
    }

    [Fact]
    public async Task Search_SinFiltros_DevuelveTodasLasOrdenes()
    {
        var proveedor = await SeedProveedorAsync();
        var producto = await SeedProductoAsync();
        await SeedOrdenAsync(proveedor.Id, producto.Id);
        await SeedOrdenAsync(proveedor.Id, producto.Id);

        var resultado = await _service.SearchAsync();

        Assert.Equal(2, resultado.Count());
    }
}
