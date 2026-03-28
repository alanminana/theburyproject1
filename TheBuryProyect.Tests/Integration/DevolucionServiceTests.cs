using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Tests de integración para DevolucionService.
/// Cubren CrearDevolucionAsync (sin detalles, venta inexistente, cantidad cero,
/// producto duplicado, producto no pertenece a venta, cantidad excede lo vendido,
/// happy path con cálculo de precio/total), AprobarDevolucionAsync (Pendiente→Aprobada,
/// genera NotaCredito, sin RowVersion lanza, estado inválido, no existe),
/// RechazarDevolucionAsync (lanza si Completada, happy path), CompletarDevolucionAsync
/// (solo si Aprobada, reintegra stock), PuedeDevolverVentaAsync y GenerarNumero.
/// </summary>
public class DevolucionServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly DevolucionService _service;

    public DevolucionServiceTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        var movimientoStock = new MovimientoStockService(
            _context, NullLogger<MovimientoStockService>.Instance);

        _service = new DevolucionService(
            _context,
            movimientoStock,
            new StubCurrentUserServiceDev(),
            NullLogger<DevolucionService>.Instance);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<Cliente> SeedClienteAsync()
    {
        var doc = Guid.NewGuid().ToString("N")[..8];
        var cliente = new Cliente
        {
            Nombre = "Test", Apellido = "Dev",
            TipoDocumento = "DNI", NumeroDocumento = doc,
            Email = $"{doc}@test.com", Activo = true
        };
        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync();
        return cliente;
    }

    private async Task<Producto> SeedProductoAsync(decimal stockActual = 100m)
    {
        var codigo = Guid.NewGuid().ToString("N")[..8];
        var cat = new Categoria { Codigo = codigo, Nombre = "Cat-" + codigo, Activo = true };
        var marca = new Marca { Codigo = codigo, Nombre = "Marca-" + codigo, Activo = true };
        _context.Categorias.Add(cat);
        _context.Marcas.Add(marca);
        await _context.SaveChangesAsync();

        var producto = new Producto
        {
            Codigo = codigo, Nombre = "Prod-" + codigo,
            CategoriaId = cat.Id, MarcaId = marca.Id,
            PrecioCompra = 10m, PrecioVenta = 50m,
            PorcentajeIVA = 21m, StockActual = stockActual, Activo = true
        };
        _context.Productos.Add(producto);
        await _context.SaveChangesAsync();
        return producto;
    }

    private async Task<Venta> SeedVentaConDetalleAsync(
        int clienteId, int productoId, int cantidad,
        DateTime? fechaVenta = null)
    {
        var venta = new Venta
        {
            Numero = Guid.NewGuid().ToString("N")[..8],
            ClienteId = clienteId,
            Estado = EstadoVenta.Entregada,
            TipoPago = TipoPago.Efectivo,
            FechaVenta = fechaVenta ?? DateTime.UtcNow,
            Detalles = new List<VentaDetalle>
            {
                new()
                {
                    ProductoId = productoId,
                    Cantidad = cantidad,
                    PrecioUnitario = 50m,
                    Descuento = 0m,
                    Subtotal = cantidad * 50m
                }
            }
        };
        _context.Ventas.Add(venta);
        await _context.SaveChangesAsync();
        await _context.Entry(venta).ReloadAsync();
        return venta;
    }

    private async Task<Devolucion> SeedDevolucionAsync(
        int ventaId, int clienteId,
        EstadoDevolucion estado = EstadoDevolucion.Pendiente,
        TipoResolucionDevolucion tipoResolucion = TipoResolucionDevolucion.CambioMismoProducto)
    {
        var dev = new Devolucion
        {
            NumeroDevolucion = "DEV-" + Guid.NewGuid().ToString("N")[..6],
            VentaId = ventaId,
            ClienteId = clienteId,
            Motivo = MotivoDevolucion.DefectoFabrica,
            Descripcion = "Test",
            Estado = estado,
            TipoResolucion = tipoResolucion,
            FechaDevolucion = DateTime.UtcNow,
            TotalDevolucion = 50m
        };
        _context.Devoluciones.Add(dev);
        await _context.SaveChangesAsync();
        await _context.Entry(dev).ReloadAsync();
        return dev;
    }

    // -------------------------------------------------------------------------
    // CrearDevolucionAsync — validaciones
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Crear_SinDetalles_LanzaExcepcion()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        var venta = await SeedVentaConDetalleAsync(cliente.Id, producto.Id, 5);

        var dev = new Devolucion
        {
            VentaId = venta.Id, ClienteId = cliente.Id,
            Motivo = MotivoDevolucion.DefectoFabrica, Descripcion = "Test"
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CrearDevolucionAsync(dev, new List<DevolucionDetalle>()));
    }

    [Fact]
    public async Task Crear_VentaNoExiste_LanzaExcepcion()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();

        var dev = new Devolucion
        {
            VentaId = 99999, ClienteId = cliente.Id,
            Motivo = MotivoDevolucion.DefectoFabrica, Descripcion = "Test"
        };
        var detalles = new List<DevolucionDetalle>
        {
            new() { ProductoId = producto.Id, Cantidad = 1 }
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CrearDevolucionAsync(dev, detalles));
    }

    [Fact]
    public async Task Crear_CantidadCero_LanzaExcepcion()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        var venta = await SeedVentaConDetalleAsync(cliente.Id, producto.Id, 5);

        var dev = new Devolucion
        {
            VentaId = venta.Id, ClienteId = cliente.Id,
            Motivo = MotivoDevolucion.DefectoFabrica, Descripcion = "Test"
        };
        var detalles = new List<DevolucionDetalle>
        {
            new() { ProductoId = producto.Id, Cantidad = 0 }
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CrearDevolucionAsync(dev, detalles));
    }

    [Fact]
    public async Task Crear_ProductoDuplicadoEnDetalles_LanzaExcepcion()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        var venta = await SeedVentaConDetalleAsync(cliente.Id, producto.Id, 10);

        var dev = new Devolucion
        {
            VentaId = venta.Id, ClienteId = cliente.Id,
            Motivo = MotivoDevolucion.DefectoFabrica, Descripcion = "Test"
        };
        var detalles = new List<DevolucionDetalle>
        {
            new() { ProductoId = producto.Id, Cantidad = 2 },
            new() { ProductoId = producto.Id, Cantidad = 1 }
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CrearDevolucionAsync(dev, detalles));
    }

    [Fact]
    public async Task Crear_ProductoNoPerteneceeAVenta_LanzaExcepcion()
    {
        var cliente = await SeedClienteAsync();
        var producto1 = await SeedProductoAsync();
        var producto2 = await SeedProductoAsync();
        var venta = await SeedVentaConDetalleAsync(cliente.Id, producto1.Id, 5);

        var dev = new Devolucion
        {
            VentaId = venta.Id, ClienteId = cliente.Id,
            Motivo = MotivoDevolucion.DefectoFabrica, Descripcion = "Test"
        };
        var detalles = new List<DevolucionDetalle>
        {
            new() { ProductoId = producto2.Id, Cantidad = 1 } // no está en la venta
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CrearDevolucionAsync(dev, detalles));
    }

    [Fact]
    public async Task Crear_CantidadExcedeLaVendida_LanzaExcepcion()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        var venta = await SeedVentaConDetalleAsync(cliente.Id, producto.Id, cantidad: 3);

        var dev = new Devolucion
        {
            VentaId = venta.Id, ClienteId = cliente.Id,
            Motivo = MotivoDevolucion.DefectoFabrica, Descripcion = "Test"
        };
        var detalles = new List<DevolucionDetalle>
        {
            new() { ProductoId = producto.Id, Cantidad = 10 } // se vendieron 3
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CrearDevolucionAsync(dev, detalles));
    }

    [Fact]
    public async Task Crear_DatosValidos_PersisteDatosYCalculaTotal()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        // Se vendieron 4 unidades a $50 c/u = $200 total
        var venta = await SeedVentaConDetalleAsync(cliente.Id, producto.Id, cantidad: 4);

        var dev = new Devolucion
        {
            VentaId = venta.Id, ClienteId = cliente.Id,
            Motivo = MotivoDevolucion.DefectoFabrica, Descripcion = "Producto defectuoso",
            TipoResolucion = TipoResolucionDevolucion.CambioMismoProducto
        };
        var detalles = new List<DevolucionDetalle>
        {
            new() { ProductoId = producto.Id, Cantidad = 2 }
        };

        var resultado = await _service.CrearDevolucionAsync(dev, detalles);

        Assert.True(resultado.Id > 0);
        Assert.Equal(EstadoDevolucion.Pendiente, resultado.Estado);
        Assert.StartsWith("DEV-", resultado.NumeroDevolucion);
        // 2 unidades × ($200 / 4) = 2 × $50 = $100
        Assert.Equal(100m, resultado.TotalDevolucion);
        Assert.Equal(50m, detalles[0].PrecioUnitario);
    }

    // -------------------------------------------------------------------------
    // AprobarDevolucionAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Aprobar_Pendiente_MarcaAprobada()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        var venta = await SeedVentaConDetalleAsync(cliente.Id, producto.Id, 5);
        var dev = await SeedDevolucionAsync(venta.Id, cliente.Id,
            tipoResolucion: TipoResolucionDevolucion.CambioMismoProducto);

        var resultado = await _service.AprobarDevolucionAsync(dev.Id, "gerente1", dev.RowVersion);

        Assert.Equal(EstadoDevolucion.Aprobada, resultado.Estado);
        Assert.Equal("gerente1", resultado.AprobadoPor);
        Assert.NotNull(resultado.FechaAprobacion);
    }

    [Fact]
    public async Task Aprobar_TipoNotaCredito_GeneraNotaCredito()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        var venta = await SeedVentaConDetalleAsync(cliente.Id, producto.Id, 5);
        var dev = await SeedDevolucionAsync(venta.Id, cliente.Id,
            tipoResolucion: TipoResolucionDevolucion.NotaCredito);

        await _service.AprobarDevolucionAsync(dev.Id, "gerente1", dev.RowVersion);

        var nc = await _context.NotasCredito.FirstOrDefaultAsync(n => n.DevolucionId == dev.Id);
        Assert.NotNull(nc);
        Assert.Equal(EstadoNotaCredito.Vigente, nc!.Estado);
    }

    [Fact]
    public async Task Aprobar_SinRowVersion_LanzaExcepcion()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        var venta = await SeedVentaConDetalleAsync(cliente.Id, producto.Id, 5);
        var dev = await SeedDevolucionAsync(venta.Id, cliente.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.AprobarDevolucionAsync(dev.Id, "gerente1", Array.Empty<byte>()));
    }

    [Fact]
    public async Task Aprobar_EstadoCompletada_LanzaExcepcion()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        var venta = await SeedVentaConDetalleAsync(cliente.Id, producto.Id, 5);
        var dev = await SeedDevolucionAsync(venta.Id, cliente.Id, estado: EstadoDevolucion.Completada);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.AprobarDevolucionAsync(dev.Id, "gerente1", dev.RowVersion));
    }

    [Fact]
    public async Task Aprobar_NoExiste_LanzaKeyNotFoundException()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _service.AprobarDevolucionAsync(99999, "gerente1", new byte[] { 1, 2 }));
    }

    // -------------------------------------------------------------------------
    // RechazarDevolucionAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Rechazar_Pendiente_MarcaRechazada()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        var venta = await SeedVentaConDetalleAsync(cliente.Id, producto.Id, 5);
        var dev = await SeedDevolucionAsync(venta.Id, cliente.Id);

        var resultado = await _service.RechazarDevolucionAsync(
            dev.Id, "Fuera de plazo", dev.RowVersion);

        Assert.Equal(EstadoDevolucion.Rechazada, resultado.Estado);
        Assert.Equal("Fuera de plazo", resultado.ObservacionesInternas);
    }

    [Fact]
    public async Task Rechazar_Completada_LanzaExcepcion()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        var venta = await SeedVentaConDetalleAsync(cliente.Id, producto.Id, 5);
        var dev = await SeedDevolucionAsync(venta.Id, cliente.Id, estado: EstadoDevolucion.Completada);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.RechazarDevolucionAsync(dev.Id, "Motivo", dev.RowVersion));
    }

    [Fact]
    public async Task Rechazar_SinRowVersion_LanzaExcepcion()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        var venta = await SeedVentaConDetalleAsync(cliente.Id, producto.Id, 5);
        var dev = await SeedDevolucionAsync(venta.Id, cliente.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.RechazarDevolucionAsync(dev.Id, "Motivo", Array.Empty<byte>()));
    }

    // -------------------------------------------------------------------------
    // CompletarDevolucionAsync — reintegra stock
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Completar_SoloSiAprobada_MarcaCompletada()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync(stockActual: 10m);
        var venta = await SeedVentaConDetalleAsync(cliente.Id, producto.Id, 5);

        // Crear y aprobar la devolución directamente via DB para tener RowVersion
        var dev = await SeedDevolucionAsync(venta.Id, cliente.Id, estado: EstadoDevolucion.Aprobada,
            tipoResolucion: TipoResolucionDevolucion.CambioMismoProducto);

        // Agregar un detalle que reintegre stock
        var detalle = new DevolucionDetalle
        {
            DevolucionId = dev.Id,
            ProductoId = producto.Id,
            Cantidad = 2,
            PrecioUnitario = 50m,
            Subtotal = 100m,
            AccionRecomendada = AccionProducto.ReintegrarStock
        };
        _context.DevolucionDetalles.Add(detalle);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        // Recargar con detalles para CompletarDevolucionAsync
        var devConDetalles = await _context.Devoluciones
            .Include(d => d.Detalles)
            .Include(d => d.Venta)
            .FirstAsync(d => d.Id == dev.Id);

        var resultado = await _service.CompletarDevolucionAsync(dev.Id, devConDetalles.RowVersion);

        Assert.Equal(EstadoDevolucion.Completada, resultado.Estado);

        // Stock debe haber aumentado en 2
        _context.ChangeTracker.Clear();
        var productoBd = await _context.Productos.FirstAsync(p => p.Id == producto.Id);
        Assert.Equal(12m, productoBd.StockActual);
    }

    [Fact]
    public async Task Completar_EstadoPendiente_LanzaExcepcion()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        var venta = await SeedVentaConDetalleAsync(cliente.Id, producto.Id, 5);
        var dev = await SeedDevolucionAsync(venta.Id, cliente.Id, estado: EstadoDevolucion.Pendiente);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CompletarDevolucionAsync(dev.Id, dev.RowVersion));
    }

    // -------------------------------------------------------------------------
    // PuedeDevolverVentaAsync / GenerarNumeroDevolucionAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PuedeDevolverVenta_VentaReciente_ReturnsTrue()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        var venta = await SeedVentaConDetalleAsync(cliente.Id, producto.Id, 5,
            fechaVenta: DateTime.UtcNow.AddDays(-5)); // 5 días atrás — dentro del plazo

        var puede = await _service.PuedeDevolverVentaAsync(venta.Id);

        Assert.True(puede);
    }

    [Fact]
    public async Task PuedeDevolverVenta_VentaAntigua_ReturnsFalse()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        var venta = await SeedVentaConDetalleAsync(cliente.Id, producto.Id, 5,
            fechaVenta: DateTime.UtcNow.AddDays(-31)); // 31 días — fuera del plazo de 30

        var puede = await _service.PuedeDevolverVentaAsync(venta.Id);

        Assert.False(puede);
    }

    [Fact]
    public async Task GenerarNumeroDevolucion_PrimeraDev_FormatoCorecto()
    {
        var numero = await _service.GenerarNumeroDevolucionAsync();

        Assert.StartsWith("DEV-", numero);
        Assert.Contains(DateTime.UtcNow.ToString("yyyyMM"), numero);
    }
}

// ---------------------------------------------------------------------------
// Stub de ICurrentUserService
// ---------------------------------------------------------------------------
file sealed class StubCurrentUserServiceDev : ICurrentUserService
{
    public string GetUsername() => "testuser";
    public string GetUserId() => "user-test";
    public string GetEmail() => "test@test.com";
    public bool IsAuthenticated() => true;
    public bool IsInRole(string role) => false;
    public bool HasPermission(string modulo, string accion) => true;
    public string GetIpAddress() => "127.0.0.1";
}
