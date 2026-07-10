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
                    Subtotal = cantidad * 50m,
                    CostoUnitarioAlMomento = 17.50m,
                    CostoTotalAlMomento = cantidad * 17.50m
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
        // El número definitivo se deriva del Id real (race-free); mismo formato histórico
        Assert.Equal($"DEV-{DateTime.UtcNow:yyyyMM}-{resultado.Id:D6}", resultado.NumeroDevolucion);
        // 2 unidades × ($200 / 4) = 2 × $50 = $100
        Assert.Equal(100m, resultado.TotalDevolucion);
        Assert.Equal(50m, detalles[0].PrecioUnitario);

        // Los detalles deben quedar PERSISTIDOS (no solo en memoria): de esto dependen
        // el reintegro de stock al completar y la validación de "ya devuelto".
        _context.ChangeTracker.Clear();
        var detallePersistido = await _context.DevolucionDetalles
            .AsNoTracking()
            .SingleAsync(d => d.DevolucionId == resultado.Id);
        Assert.Equal(producto.Id, detallePersistido.ProductoId);
        Assert.Equal(2, detallePersistido.Cantidad);
        Assert.Equal(50m, detallePersistido.PrecioUnitario);
        Assert.Equal(100m, detallePersistido.Subtotal);
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
        // Número derivado del Id real y columna denormalizada poblada
        Assert.Equal($"NC-{DateTime.UtcNow:yyyyMM}-{nc.Id:D6}", nc.NumeroNotaCredito);
        var devPersistida = await _context.Devoluciones.AsNoTracking().SingleAsync(d => d.Id == dev.Id);
        Assert.Equal(nc.Id, devPersistida.NotaCreditoId);
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

    [Fact]
    public async Task Rechazar_AprobadaConNotaCredito_AnulaLaNotaCredito()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        var venta = await SeedVentaConDetalleAsync(cliente.Id, producto.Id, 5);
        var dev = await SeedDevolucionAsync(venta.Id, cliente.Id,
            tipoResolucion: TipoResolucionDevolucion.NotaCredito);

        var aprobada = await _service.AprobarDevolucionAsync(dev.Id, "gerente1", dev.RowVersion);

        await _service.RechazarDevolucionAsync(dev.Id, "Aprobada por error", aprobada.RowVersion);

        // Sin este comportamiento quedaba una NC Vigente de una devolución rechazada
        var nc = await _context.NotasCredito.SingleAsync(n => n.DevolucionId == dev.Id);
        Assert.Equal(EstadoNotaCredito.Cancelada, nc.Estado);
    }

    [Fact]
    public async Task Rechazar_NotaCreditoConUsos_LanzaYNoRechaza()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        var venta = await SeedVentaConDetalleAsync(cliente.Id, producto.Id, 5);
        var dev = await SeedDevolucionAsync(venta.Id, cliente.Id,
            tipoResolucion: TipoResolucionDevolucion.NotaCredito);

        var aprobada = await _service.AprobarDevolucionAsync(dev.Id, "gerente1", dev.RowVersion);
        var nc = await _context.NotasCredito.SingleAsync(n => n.DevolucionId == dev.Id);
        await _service.UtilizarNotaCreditoAsync(nc.Id, 10m);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.RechazarDevolucionAsync(dev.Id, "Motivo", aprobada.RowVersion));

        var devPersistida = await _context.Devoluciones.AsNoTracking().SingleAsync(d => d.Id == dev.Id);
        Assert.Equal(EstadoDevolucion.Aprobada, devPersistida.Estado);
    }

    // -------------------------------------------------------------------------
    // CancelarDevolucionAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Cancelar_Pendiente_MarcaCancelada()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        var venta = await SeedVentaConDetalleAsync(cliente.Id, producto.Id, 5);
        var dev = await SeedDevolucionAsync(venta.Id, cliente.Id);

        var resultado = await _service.CancelarDevolucionAsync(
            dev.Id, "Cargada por error", dev.RowVersion);

        Assert.Equal(EstadoDevolucion.Cancelada, resultado.Estado);
        Assert.Equal("Cargada por error", resultado.ObservacionesInternas);
    }

    [Fact]
    public async Task Cancelar_Aprobada_LanzaExcepcion()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        var venta = await SeedVentaConDetalleAsync(cliente.Id, producto.Id, 5);
        var dev = await SeedDevolucionAsync(venta.Id, cliente.Id, estado: EstadoDevolucion.Aprobada);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CancelarDevolucionAsync(dev.Id, "Motivo", dev.RowVersion));
    }

    [Fact]
    public async Task Cancelar_SinRowVersion_LanzaExcepcion()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        var venta = await SeedVentaConDetalleAsync(cliente.Id, producto.Id, 5);
        var dev = await SeedDevolucionAsync(venta.Id, cliente.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CancelarDevolucionAsync(dev.Id, "Motivo", Array.Empty<byte>()));
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

        var movimiento = await _context.MovimientosStock
            .AsNoTracking()
            .SingleAsync(m => m.ProductoId == producto.Id && m.Referencia == $"DEV-{dev.NumeroDevolucion}");
        Assert.Equal(17.50m, movimiento.CostoUnitarioAlMomento);
        Assert.Equal(35.00m, movimiento.CostoTotalAlMomento);
        Assert.Equal("VentaDetalleSnapshot", movimiento.FuenteCosto);
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

    // -------------------------------------------------------------------------
    // ObtenerTodasDevolucionesAsync / ObtenerDevolucionesPorClienteAsync /
    // ObtenerDevolucionesPorEstadoAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ObtenerTodas_SinDevoluciones_RetornaVacio()
    {
        var resultado = await _service.ObtenerTodasDevolucionesAsync();

        Assert.NotNull(resultado);
        Assert.Empty(resultado);
    }

    [Fact]
    public async Task ObtenerTodas_ConDevoluciones_RetornaLista()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        var venta = await SeedVentaConDetalleAsync(cliente.Id, producto.Id, 5);
        await SeedDevolucionAsync(venta.Id, cliente.Id);
        await SeedDevolucionAsync(venta.Id, cliente.Id);

        var resultado = await _service.ObtenerTodasDevolucionesAsync();

        Assert.True(resultado.Count >= 2);
    }

    [Fact]
    public async Task ObtenerPorCliente_SinDevoluciones_RetornaVacio()
    {
        var resultado = await _service.ObtenerDevolucionesPorClienteAsync(99999);

        Assert.Empty(resultado);
    }

    [Fact]
    public async Task ObtenerPorCliente_ConDevoluciones_RetornaSoloLasDelCliente()
    {
        var cliente1 = await SeedClienteAsync();
        var cliente2 = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        var venta1 = await SeedVentaConDetalleAsync(cliente1.Id, producto.Id, 5);
        var venta2 = await SeedVentaConDetalleAsync(cliente2.Id, producto.Id, 5);
        await SeedDevolucionAsync(venta1.Id, cliente1.Id);
        await SeedDevolucionAsync(venta2.Id, cliente2.Id);

        var resultado = await _service.ObtenerDevolucionesPorClienteAsync(cliente1.Id);

        Assert.All(resultado, d => Assert.Equal(cliente1.Id, d.ClienteId));
    }

    [Fact]
    public async Task ObtenerPorEstado_Pendiente_RetornaSoloPendientes()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        var venta = await SeedVentaConDetalleAsync(cliente.Id, producto.Id, 10);
        await SeedDevolucionAsync(venta.Id, cliente.Id, EstadoDevolucion.Pendiente);
        await SeedDevolucionAsync(venta.Id, cliente.Id, EstadoDevolucion.Aprobada);

        var resultado = await _service.ObtenerDevolucionesPorEstadoAsync(EstadoDevolucion.Pendiente);

        Assert.All(resultado, d => Assert.Equal(EstadoDevolucion.Pendiente, d.Estado));
    }

    // -------------------------------------------------------------------------
    // ObtenerDevolucionAsync / ObtenerDevolucionPorNumeroAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ObtenerDevolucion_Existente_RetornaDevolucion()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        var venta = await SeedVentaConDetalleAsync(cliente.Id, producto.Id, 5);
        var dev = await SeedDevolucionAsync(venta.Id, cliente.Id);

        var resultado = await _service.ObtenerDevolucionAsync(dev.Id);

        Assert.NotNull(resultado);
        Assert.Equal(dev.Id, resultado!.Id);
    }

    [Fact]
    public async Task ObtenerDevolucion_Inexistente_RetornaNull()
    {
        var resultado = await _service.ObtenerDevolucionAsync(99999);

        Assert.Null(resultado);
    }

    [Fact]
    public async Task ObtenerDevolucionPorNumero_Existente_RetornaDevolucion()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        var venta = await SeedVentaConDetalleAsync(cliente.Id, producto.Id, 5);
        var dev = await SeedDevolucionAsync(venta.Id, cliente.Id);

        var resultado = await _service.ObtenerDevolucionPorNumeroAsync(dev.NumeroDevolucion);

        Assert.NotNull(resultado);
        Assert.Equal(dev.Id, resultado!.Id);
    }

    [Fact]
    public async Task ObtenerDevolucionPorNumero_Inexistente_RetornaNull()
    {
        var resultado = await _service.ObtenerDevolucionPorNumeroAsync("DEV-INEXISTENTE");

        Assert.Null(resultado);
    }

    // -------------------------------------------------------------------------
    // ActualizarDevolucionAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ActualizarDevolucion_Existente_ActualizaDescripcion()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        var venta = await SeedVentaConDetalleAsync(cliente.Id, producto.Id, 5);
        var dev = await SeedDevolucionAsync(venta.Id, cliente.Id);

        dev.Descripcion = "Descripción actualizada";
        dev.ObservacionesInternas = "Nueva observación";

        var resultado = await _service.ActualizarDevolucionAsync(dev);

        Assert.Equal("Descripción actualizada", resultado.Descripcion);
        Assert.Equal("Nueva observación", resultado.ObservacionesInternas);
    }

    [Fact]
    public async Task ActualizarDevolucion_Inexistente_LanzaKeyNotFoundException()
    {
        var dev = new Devolucion { Id = 99999, NumeroDevolucion = "DEV-X", VentaId = 1, ClienteId = 1 };

        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.ActualizarDevolucionAsync(dev));
    }

    // -------------------------------------------------------------------------
    // ObtenerDetallesDevolucionAsync / AgregarDetalleAsync /
    // ActualizarEstadoProductoAsync / VerificarAccesoriosAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ObtenerDetalles_SinDetalles_RetornaVacio()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        var venta = await SeedVentaConDetalleAsync(cliente.Id, producto.Id, 5);
        var dev = await SeedDevolucionAsync(venta.Id, cliente.Id);

        var resultado = await _service.ObtenerDetallesDevolucionAsync(dev.Id);

        Assert.Empty(resultado);
    }

    [Fact]
    public async Task ActualizarEstadoProducto_Existente_ActualizaEstadoYAccion()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        var venta = await SeedVentaConDetalleAsync(cliente.Id, producto.Id, 5);
        var dev = await SeedDevolucionAsync(venta.Id, cliente.Id);
        var detalle = new DevolucionDetalle
        {
            DevolucionId = dev.Id, ProductoId = producto.Id,
            Cantidad = 1, PrecioUnitario = 50m, Subtotal = 50m
        };
        _context.DevolucionDetalles.Add(detalle);
        await _context.SaveChangesAsync();

        var resultado = await _service.ActualizarEstadoProductoAsync(
            detalle.Id, EstadoProductoDevuelto.Defectuoso, AccionProducto.ReintegrarStock);

        Assert.Equal(EstadoProductoDevuelto.Defectuoso, resultado.EstadoProducto);
        Assert.Equal(AccionProducto.ReintegrarStock, resultado.AccionRecomendada);
    }

    [Fact]
    public async Task VerificarAccesorios_Existente_ActualizaAccesorios()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        var venta = await SeedVentaConDetalleAsync(cliente.Id, producto.Id, 5);
        var dev = await SeedDevolucionAsync(venta.Id, cliente.Id);
        var detalle = new DevolucionDetalle
        {
            DevolucionId = dev.Id, ProductoId = producto.Id,
            Cantidad = 1, PrecioUnitario = 50m, Subtotal = 50m
        };
        _context.DevolucionDetalles.Add(detalle);
        await _context.SaveChangesAsync();

        var ok = await _service.VerificarAccesoriosAsync(detalle.Id, false, "Cable faltante");

        Assert.True(ok);
        _context.ChangeTracker.Clear();
        var dt = await _context.DevolucionDetalles.FindAsync(detalle.Id);
        Assert.False(dt!.AccesoriosCompletos);
        Assert.Equal("Cable faltante", dt.AccesoriosFaltantes);
    }

    [Fact]
    public async Task VerificarAccesorios_Inexistente_RetornaFalse()
    {
        var resultado = await _service.VerificarAccesoriosAsync(99999, true, null);

        Assert.False(resultado);
    }

    // -------------------------------------------------------------------------
    // Garantías: ObtenerTodasGarantiasAsync / ObtenerGarantiasVigentesAsync /
    // ObtenerGarantiasPorClienteAsync / ObtenerGarantiaAsync /
    // CrearGarantiaAsync / ValidarGarantiaVigenteAsync /
    // ObtenerGarantiasProximasVencerAsync
    // -------------------------------------------------------------------------

    private async Task<Garantia> SeedGarantiaAsync(int clienteId, int productoId, int ventaDetalleId,
        EstadoGarantia estado = EstadoGarantia.Vigente, int meses = 12)
    {
        var g = new Garantia
        {
            NumeroGarantia = "GAR-" + Guid.NewGuid().ToString("N")[..6],
            ClienteId = clienteId,
            ProductoId = productoId,
            VentaDetalleId = ventaDetalleId,
            FechaInicio = DateTime.UtcNow.AddMonths(-1),
            FechaVencimiento = DateTime.UtcNow.AddMonths(meses - 1),
            MesesGarantia = meses,
            Estado = estado
        };
        _context.Garantias.Add(g);
        await _context.SaveChangesAsync();
        return g;
    }

    [Fact]
    public async Task ObtenerTodasGarantias_SinGarantias_RetornaVacio()
    {
        var resultado = await _service.ObtenerTodasGarantiasAsync();

        Assert.Empty(resultado);
    }

    [Fact]
    public async Task ObtenerTodasGarantias_ConGarantias_RetornaLista()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        var venta = await SeedVentaConDetalleAsync(cliente.Id, producto.Id, 2);
        var ventaDetalle = venta.Detalles.First();
        await SeedGarantiaAsync(cliente.Id, producto.Id, ventaDetalle.Id);

        var resultado = await _service.ObtenerTodasGarantiasAsync();

        Assert.True(resultado.Count >= 1);
    }

    [Fact]
    public async Task ObtenerGarantiasVigentes_FiltraVencidas()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        var venta = await SeedVentaConDetalleAsync(cliente.Id, producto.Id, 2);
        var ventaDetalle = venta.Detalles.First();
        var vigente = await SeedGarantiaAsync(cliente.Id, producto.Id, ventaDetalle.Id, EstadoGarantia.Vigente);
        var vencida = await SeedGarantiaAsync(cliente.Id, producto.Id, ventaDetalle.Id, EstadoGarantia.Vencida);

        var resultado = await _service.ObtenerGarantiasVigentesAsync();

        Assert.DoesNotContain(resultado, g => g.Id == vencida.Id);
    }

    [Fact]
    public async Task ObtenerGarantiasPorCliente_FiltraPorCliente()
    {
        var cliente1 = await SeedClienteAsync();
        var cliente2 = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        var venta1 = await SeedVentaConDetalleAsync(cliente1.Id, producto.Id, 2);
        var venta2 = await SeedVentaConDetalleAsync(cliente2.Id, producto.Id, 2);
        await SeedGarantiaAsync(cliente1.Id, producto.Id, venta1.Detalles.First().Id);
        await SeedGarantiaAsync(cliente2.Id, producto.Id, venta2.Detalles.First().Id);

        var resultado = await _service.ObtenerGarantiasPorClienteAsync(cliente1.Id);

        Assert.All(resultado, g => Assert.Equal(cliente1.Id, g.ClienteId));
    }

    [Fact]
    public async Task ObtenerGarantia_Existente_RetornaGarantia()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        var venta = await SeedVentaConDetalleAsync(cliente.Id, producto.Id, 2);
        var garantia = await SeedGarantiaAsync(cliente.Id, producto.Id, venta.Detalles.First().Id);

        var resultado = await _service.ObtenerGarantiaAsync(garantia.Id);

        Assert.NotNull(resultado);
        Assert.Equal(garantia.Id, resultado!.Id);
    }

    [Fact]
    public async Task ObtenerGarantia_Inexistente_RetornaNull()
    {
        var resultado = await _service.ObtenerGarantiaAsync(99999);

        Assert.Null(resultado);
    }

    [Fact]
    public async Task CrearGarantia_Persiste_AsignaNumeroYFechaVencimiento()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        var venta = await SeedVentaConDetalleAsync(cliente.Id, producto.Id, 1);
        var ventaDetalle = venta.Detalles.First();

        var garantia = new Garantia
        {
            ClienteId = cliente.Id,
            ProductoId = producto.Id,
            VentaDetalleId = ventaDetalle.Id,
            FechaInicio = DateTime.UtcNow,
            MesesGarantia = 6
        };

        var resultado = await _service.CrearGarantiaAsync(garantia);

        Assert.True(resultado.Id > 0);
        Assert.StartsWith("GAR-", resultado.NumeroGarantia);
        Assert.Equal(EstadoGarantia.Vigente, resultado.Estado);
        Assert.True(resultado.FechaVencimiento > DateTime.UtcNow);
    }

    [Fact]
    public async Task ValidarGarantiaVigente_Vigente_RetornaTrue()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        var venta = await SeedVentaConDetalleAsync(cliente.Id, producto.Id, 1);
        var garantia = await SeedGarantiaAsync(cliente.Id, producto.Id, venta.Detalles.First().Id);

        var resultado = await _service.ValidarGarantiaVigenteAsync(garantia.Id);

        Assert.True(resultado);
    }

    [Fact]
    public async Task ValidarGarantiaVigente_Expirada_RetornaFalse()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        var venta = await SeedVentaConDetalleAsync(cliente.Id, producto.Id, 1);
        var garantia = await SeedGarantiaAsync(cliente.Id, producto.Id, venta.Detalles.First().Id,
            EstadoGarantia.Vencida);

        var resultado = await _service.ValidarGarantiaVigenteAsync(garantia.Id);

        Assert.False(resultado);
    }

    // -------------------------------------------------------------------------
    // Notas de Crédito: ObtenerTodasNotasCreditoAsync /
    // ObtenerNotasCreditoPorClienteAsync / ObtenerNotasCreditoVigentesAsync /
    // ObtenerNotaCreditoAsync / ObtenerNotaCreditoPorNumeroAsync /
    // CrearNotaCreditoAsync
    // -------------------------------------------------------------------------

    private async Task<NotaCredito> SeedNotaCreditoAsync(int clienteId, int? devolucionId = null,
        EstadoNotaCredito estado = EstadoNotaCredito.Vigente, decimal monto = 100m,
        decimal utilizado = 0m, DateTime? vencimiento = null)
    {
        var nc = new NotaCredito
        {
            NumeroNotaCredito = "NC-" + Guid.NewGuid().ToString("N")[..6],
            ClienteId = clienteId,
            DevolucionId = devolucionId ?? 0,
            FechaEmision = DateTime.UtcNow,
            MontoTotal = monto,
            MontoUtilizado = utilizado,
            Estado = estado,
            FechaVencimiento = vencimiento ?? DateTime.UtcNow.AddYears(1)
        };
        _context.NotasCredito.Add(nc);
        await _context.SaveChangesAsync();
        return nc;
    }

    [Fact]
    public async Task ObtenerTodasNotasCredito_SinNotas_RetornaVacio()
    {
        var resultado = await _service.ObtenerTodasNotasCreditoAsync();

        Assert.Empty(resultado);
    }

    [Fact]
    public async Task ObtenerTodasNotasCredito_ConNotas_RetornaLista()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        var venta = await SeedVentaConDetalleAsync(cliente.Id, producto.Id, 5);
        var dev = await SeedDevolucionAsync(venta.Id, cliente.Id);
        await SeedNotaCreditoAsync(cliente.Id, dev.Id);

        var resultado = await _service.ObtenerTodasNotasCreditoAsync();

        Assert.True(resultado.Count >= 1);
    }

    [Fact]
    public async Task ObtenerNotasCreditoPorCliente_FiltraPorCliente()
    {
        var cliente1 = await SeedClienteAsync();
        var cliente2 = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        var venta1 = await SeedVentaConDetalleAsync(cliente1.Id, producto.Id, 5);
        var venta2 = await SeedVentaConDetalleAsync(cliente2.Id, producto.Id, 5);
        var dev1 = await SeedDevolucionAsync(venta1.Id, cliente1.Id);
        var dev2 = await SeedDevolucionAsync(venta2.Id, cliente2.Id);
        await SeedNotaCreditoAsync(cliente1.Id, dev1.Id);
        await SeedNotaCreditoAsync(cliente2.Id, dev2.Id);

        var resultado = await _service.ObtenerNotasCreditoPorClienteAsync(cliente1.Id);

        Assert.All(resultado, nc => Assert.Equal(cliente1.Id, nc.ClienteId));
    }

    [Fact]
    public async Task ObtenerNotaCredito_Existente_RetornaNotaCredito()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        var venta = await SeedVentaConDetalleAsync(cliente.Id, producto.Id, 5);
        var dev = await SeedDevolucionAsync(venta.Id, cliente.Id);
        var nc = await SeedNotaCreditoAsync(cliente.Id, dev.Id);

        var resultado = await _service.ObtenerNotaCreditoAsync(nc.Id);

        Assert.NotNull(resultado);
        Assert.Equal(nc.Id, resultado!.Id);
    }

    [Fact]
    public async Task ObtenerNotaCredito_Inexistente_RetornaNull()
    {
        var resultado = await _service.ObtenerNotaCreditoAsync(99999);

        Assert.Null(resultado);
    }

    [Fact]
    public async Task ObtenerNotaCreditoPorNumero_Existente_RetornaNotaCredito()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        var venta = await SeedVentaConDetalleAsync(cliente.Id, producto.Id, 5);
        var dev = await SeedDevolucionAsync(venta.Id, cliente.Id);
        var nc = await SeedNotaCreditoAsync(cliente.Id, dev.Id);

        var resultado = await _service.ObtenerNotaCreditoPorNumeroAsync(nc.NumeroNotaCredito);

        Assert.NotNull(resultado);
        Assert.Equal(nc.Id, resultado!.Id);
    }

    [Fact]
    public async Task CrearNotaCredito_Persiste_AsignaNumero()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        var venta = await SeedVentaConDetalleAsync(cliente.Id, producto.Id, 5);
        var dev = await SeedDevolucionAsync(venta.Id, cliente.Id);

        var nc = new NotaCredito
        {
            ClienteId = cliente.Id,
            DevolucionId = dev.Id,
            FechaEmision = DateTime.UtcNow,
            MontoTotal = 250m,
            FechaVencimiento = DateTime.UtcNow.AddYears(1)
        };

        var resultado = await _service.CrearNotaCreditoAsync(nc);

        Assert.True(resultado.Id > 0);
        Assert.Equal($"NC-{DateTime.UtcNow:yyyyMM}-{resultado.Id:D6}", resultado.NumeroNotaCredito);
        Assert.Equal(EstadoNotaCredito.Vigente, resultado.Estado);
        Assert.Equal(0m, resultado.MontoUtilizado);
    }

    // -------------------------------------------------------------------------
    // UtilizarNotaCreditoAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Utilizar_Parcial_DescuentaSaldoYRegistraReferencia()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        var venta = await SeedVentaConDetalleAsync(cliente.Id, producto.Id, 5);
        var dev = await SeedDevolucionAsync(venta.Id, cliente.Id);
        var nc = await SeedNotaCreditoAsync(cliente.Id, dev.Id, monto: 100m);

        var resultado = await _service.UtilizarNotaCreditoAsync(nc.Id, 40m, "aplicada en venta V-000123");

        Assert.Equal(40m, resultado.MontoUtilizado);
        Assert.Equal(60m, resultado.MontoDisponible);
        Assert.Equal(EstadoNotaCredito.UtilizadaParcialmente, resultado.Estado);
        Assert.Contains("V-000123", resultado.Observaciones);
    }

    [Fact]
    public async Task Utilizar_TotalDelSaldo_MarcaUtilizadaTotalmente()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        var venta = await SeedVentaConDetalleAsync(cliente.Id, producto.Id, 5);
        var dev = await SeedDevolucionAsync(venta.Id, cliente.Id);
        var nc = await SeedNotaCreditoAsync(cliente.Id, dev.Id, monto: 100m);

        var resultado = await _service.UtilizarNotaCreditoAsync(nc.Id, 100m);

        Assert.Equal(0m, resultado.MontoDisponible);
        Assert.Equal(EstadoNotaCredito.UtilizadaTotalmente, resultado.Estado);
    }

    [Fact]
    public async Task Utilizar_MontoCeroONegativo_LanzaExcepcion()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        var venta = await SeedVentaConDetalleAsync(cliente.Id, producto.Id, 5);
        var dev = await SeedDevolucionAsync(venta.Id, cliente.Id);
        var nc = await SeedNotaCreditoAsync(cliente.Id, dev.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.UtilizarNotaCreditoAsync(nc.Id, 0m));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.UtilizarNotaCreditoAsync(nc.Id, -5m));
    }

    [Fact]
    public async Task Utilizar_SaldoInsuficiente_LanzaExcepcion()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        var venta = await SeedVentaConDetalleAsync(cliente.Id, producto.Id, 5);
        var dev = await SeedDevolucionAsync(venta.Id, cliente.Id);
        var nc = await SeedNotaCreditoAsync(cliente.Id, dev.Id, monto: 100m);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.UtilizarNotaCreditoAsync(nc.Id, 150m));
    }

    [Fact]
    public async Task Utilizar_Vencida_LanzaExcepcion()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        var venta = await SeedVentaConDetalleAsync(cliente.Id, producto.Id, 5);
        var dev = await SeedDevolucionAsync(venta.Id, cliente.Id);
        var nc = await SeedNotaCreditoAsync(cliente.Id, dev.Id,
            vencimiento: DateTime.UtcNow.AddDays(-1));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.UtilizarNotaCreditoAsync(nc.Id, 10m));
    }

    [Fact]
    public async Task Utilizar_EstadoCancelada_LanzaExcepcion()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        var venta = await SeedVentaConDetalleAsync(cliente.Id, producto.Id, 5);
        var dev = await SeedDevolucionAsync(venta.Id, cliente.Id);
        var nc = await SeedNotaCreditoAsync(cliente.Id, dev.Id,
            estado: EstadoNotaCredito.Cancelada);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.UtilizarNotaCreditoAsync(nc.Id, 10m));
    }

    // -------------------------------------------------------------------------
    // ObtenerNotasCreditoVigentesAsync / ObtenerCreditoDisponibleClienteAsync
    // (regresión: la query usaba MontoDisponible no mapeado y no traducía a SQL)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ObtenerNotasCreditoVigentes_IncluyeParcialesYExcluyeAgotadasVencidasCanceladas()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        var venta = await SeedVentaConDetalleAsync(cliente.Id, producto.Id, 5);

        var devVigente = await SeedDevolucionAsync(venta.Id, cliente.Id);
        var devParcial = await SeedDevolucionAsync(venta.Id, cliente.Id);
        var devAgotada = await SeedDevolucionAsync(venta.Id, cliente.Id);
        var devVencida = await SeedDevolucionAsync(venta.Id, cliente.Id);
        var devCancelada = await SeedDevolucionAsync(venta.Id, cliente.Id);

        var ncVigente = await SeedNotaCreditoAsync(cliente.Id, devVigente.Id, monto: 100m);
        var ncParcial = await SeedNotaCreditoAsync(cliente.Id, devParcial.Id, monto: 100m,
            utilizado: 50m, estado: EstadoNotaCredito.UtilizadaParcialmente);
        await SeedNotaCreditoAsync(cliente.Id, devAgotada.Id, monto: 100m,
            utilizado: 100m, estado: EstadoNotaCredito.UtilizadaTotalmente);
        await SeedNotaCreditoAsync(cliente.Id, devVencida.Id, monto: 100m,
            vencimiento: DateTime.UtcNow.AddDays(-1));
        await SeedNotaCreditoAsync(cliente.Id, devCancelada.Id, monto: 100m,
            estado: EstadoNotaCredito.Cancelada);

        var vigentes = await _service.ObtenerNotasCreditoVigentesAsync(cliente.Id);
        var disponible = await _service.ObtenerCreditoDisponibleClienteAsync(cliente.Id);

        Assert.Equal(2, vigentes.Count);
        Assert.Contains(vigentes, nc => nc.Id == ncVigente.Id);
        Assert.Contains(vigentes, nc => nc.Id == ncParcial.Id);
        Assert.Equal(150m, disponible); // 100 vigente + 50 restante de la parcial
    }

    // -------------------------------------------------------------------------
    // CrearRMAAsync — RMAId real (regresión: quedaba en 0 por asignarse pre-insert)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CrearRMA_PersisteRMAIdRealYNumeroDerivadoDelId()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        var venta = await SeedVentaConDetalleAsync(cliente.Id, producto.Id, 5);
        var dev = await SeedDevolucionAsync(venta.Id, cliente.Id, estado: EstadoDevolucion.Aprobada);

        var proveedor = new Proveedor { Cuit = "20123456789", RazonSocial = "Proveedor Test" };
        _context.Proveedores.Add(proveedor);
        await _context.SaveChangesAsync();

        var rma = new RMA
        {
            DevolucionId = dev.Id,
            ProveedorId = proveedor.Id,
            MotivoSolicitud = "Producto defectuoso"
        };

        var resultado = await _service.CrearRMAAsync(rma, dev.RowVersion);

        Assert.True(resultado.Id > 0);
        Assert.Equal($"RMA-{DateTime.UtcNow:yyyyMM}-{resultado.Id:D6}", resultado.NumeroRMA);

        var devPersistida = await _context.Devoluciones.AsNoTracking().SingleAsync(d => d.Id == dev.Id);
        Assert.Equal(resultado.Id, devPersistida.RMAId);
        Assert.True(devPersistida.RequiereRMA);
    }

    // -------------------------------------------------------------------------
    // ObtenerDiasDesdeVentaAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ObtenerDiasDesdeVenta_VentaExistente_RetornaDias()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        var venta = await SeedVentaConDetalleAsync(cliente.Id, producto.Id, 1,
            fechaVenta: DateTime.UtcNow.AddDays(-10));

        var dias = await _service.ObtenerDiasDesdeVentaAsync(venta.Id);

        Assert.True(dias >= 10);
    }

    [Fact]
    public async Task ObtenerDiasDesdeVenta_VentaInexistente_RetornaMaxValue()
    {
        var dias = await _service.ObtenerDiasDesdeVentaAsync(99999);

        Assert.Equal(int.MaxValue, dias);
    }

    // -------------------------------------------------------------------------
    // Fase 10.3 — Devolución simple con unidad física
    // -------------------------------------------------------------------------

    private async Task<ProductoUnidad> SeedProductoUnidadAsync(
        int productoId,
        EstadoUnidad estado = EstadoUnidad.Vendida)
    {
        var codigo = "U-" + Guid.NewGuid().ToString("N")[..8];
        var unidad = new ProductoUnidad
        {
            ProductoId = productoId,
            CodigoInternoUnidad = codigo,
            Estado = estado,
            FechaIngreso = DateTime.UtcNow
        };
        _context.ProductoUnidades.Add(unidad);
        await _context.SaveChangesAsync();
        return unidad;
    }

    private async Task<Venta> SeedVentaConDetalleYUnidadAsync(
        int clienteId, int productoId, int productoUnidadId, int cantidad = 1)
    {
        var venta = new Venta
        {
            Numero = Guid.NewGuid().ToString("N")[..8],
            ClienteId = clienteId,
            Estado = EstadoVenta.Entregada,
            TipoPago = TipoPago.Efectivo,
            FechaVenta = DateTime.UtcNow,
            Detalles = new List<VentaDetalle>
            {
                new()
                {
                    ProductoId = productoId,
                    Cantidad = cantidad,
                    PrecioUnitario = 50m,
                    Descuento = 0m,
                    Subtotal = cantidad * 50m,
                    CostoUnitarioAlMomento = 17.50m,
                    CostoTotalAlMomento = cantidad * 17.50m,
                    ProductoUnidadId = productoUnidadId
                }
            }
        };
        _context.Ventas.Add(venta);
        await _context.SaveChangesAsync();
        await _context.Entry(venta).ReloadAsync();
        return venta;
    }

    [Fact]
    public async Task Crear_SinUnidad_SigueFuncionandoIgualQueAntes()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        var venta = await SeedVentaConDetalleAsync(cliente.Id, producto.Id, 2);

        var dev = new Devolucion
        {
            VentaId = venta.Id, ClienteId = cliente.Id,
            Motivo = MotivoDevolucion.DefectoFabrica, Descripcion = "Sin unidad"
        };
        var detalles = new List<DevolucionDetalle>
        {
            new() { ProductoId = producto.Id, Cantidad = 1 }
        };

        var resultado = await _service.CrearDevolucionAsync(dev, detalles);

        Assert.True(resultado.Id > 0);
        Assert.Null(detalles[0].ProductoUnidadId);
    }

    [Fact]
    public async Task Crear_ConAutoInferencia_CopiaProductoUnidadIdDeVentaDetalle()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        var unidad = await SeedProductoUnidadAsync(producto.Id, EstadoUnidad.Vendida);
        var venta = await SeedVentaConDetalleYUnidadAsync(cliente.Id, producto.Id, unidad.Id, 1);

        var dev = new Devolucion
        {
            VentaId = venta.Id, ClienteId = cliente.Id,
            Motivo = MotivoDevolucion.DefectoFabrica, Descripcion = "Con auto-inferencia"
        };
        var detalles = new List<DevolucionDetalle>
        {
            new() { ProductoId = producto.Id, Cantidad = 1 }
        };

        await _service.CrearDevolucionAsync(dev, detalles);

        Assert.Equal(unidad.Id, detalles[0].ProductoUnidadId);
    }

    [Fact]
    public async Task Crear_ConUnidadExplicitaValida_PersisiteProductoUnidadId()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        var unidad = await SeedProductoUnidadAsync(producto.Id, EstadoUnidad.Vendida);
        var venta = await SeedVentaConDetalleYUnidadAsync(cliente.Id, producto.Id, unidad.Id, 1);

        var dev = new Devolucion
        {
            VentaId = venta.Id, ClienteId = cliente.Id,
            Motivo = MotivoDevolucion.DefectoFabrica, Descripcion = "Con unidad explícita"
        };
        var detalles = new List<DevolucionDetalle>
        {
            new() { ProductoId = producto.Id, Cantidad = 1, ProductoUnidadId = unidad.Id }
        };

        var resultado = await _service.CrearDevolucionAsync(dev, detalles);

        Assert.True(resultado.Id > 0);
        Assert.Equal(unidad.Id, detalles[0].ProductoUnidadId);
    }

    [Fact]
    public async Task Crear_UnidadDeOtroProducto_LanzaExcepcion()
    {
        var cliente = await SeedClienteAsync();
        var producto1 = await SeedProductoAsync();
        var producto2 = await SeedProductoAsync();
        var unidadDeProducto2 = await SeedProductoUnidadAsync(producto2.Id, EstadoUnidad.Vendida);
        var venta = await SeedVentaConDetalleAsync(cliente.Id, producto1.Id, 2);

        var dev = new Devolucion
        {
            VentaId = venta.Id, ClienteId = cliente.Id,
            Motivo = MotivoDevolucion.DefectoFabrica, Descripcion = "Test"
        };
        var detalles = new List<DevolucionDetalle>
        {
            new() { ProductoId = producto1.Id, Cantidad = 1, ProductoUnidadId = unidadDeProducto2.Id }
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CrearDevolucionAsync(dev, detalles));
    }

    [Fact]
    public async Task Crear_UnidadInexistente_LanzaExcepcion()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        var venta = await SeedVentaConDetalleAsync(cliente.Id, producto.Id, 2);

        var dev = new Devolucion
        {
            VentaId = venta.Id, ClienteId = cliente.Id,
            Motivo = MotivoDevolucion.DefectoFabrica, Descripcion = "Test"
        };
        var detalles = new List<DevolucionDetalle>
        {
            new() { ProductoId = producto.Id, Cantidad = 1, ProductoUnidadId = 99999 }
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CrearDevolucionAsync(dev, detalles));
    }

    [Theory]
    [InlineData(EstadoUnidad.Baja)]
    [InlineData(EstadoUnidad.Faltante)]
    [InlineData(EstadoUnidad.Anulada)]
    [InlineData(EstadoUnidad.EnReparacion)]
    public async Task Crear_UnidadConEstadoIncompatible_LanzaExcepcion(EstadoUnidad estadoIncompatible)
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        var unidad = await SeedProductoUnidadAsync(producto.Id, estadoIncompatible);
        var venta = await SeedVentaConDetalleAsync(cliente.Id, producto.Id, 2);

        var dev = new Devolucion
        {
            VentaId = venta.Id, ClienteId = cliente.Id,
            Motivo = MotivoDevolucion.DefectoFabrica, Descripcion = "Test"
        };
        var detalles = new List<DevolucionDetalle>
        {
            new() { ProductoId = producto.Id, Cantidad = 1, ProductoUnidadId = unidad.Id }
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CrearDevolucionAsync(dev, detalles));
    }

    [Fact]
    public async Task Crear_MultipleVentaDetallesParaMismoProducto_NoAutoInfiere()
    {
        // Cuando hay más de un VentaDetalle para el mismo ProductoId en la venta,
        // la auto-inferencia es ambigua y no debe aplicarse
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        var unidad1 = await SeedProductoUnidadAsync(producto.Id, EstadoUnidad.Vendida);
        var unidad2 = await SeedProductoUnidadAsync(producto.Id, EstadoUnidad.Vendida);

        var venta = new Venta
        {
            Numero = Guid.NewGuid().ToString("N")[..8],
            ClienteId = cliente.Id,
            Estado = EstadoVenta.Entregada,
            TipoPago = TipoPago.Efectivo,
            FechaVenta = DateTime.UtcNow,
            Detalles = new List<VentaDetalle>
            {
                new()
                {
                    ProductoId = producto.Id, Cantidad = 1,
                    PrecioUnitario = 50m, Descuento = 0m, Subtotal = 50m,
                    CostoUnitarioAlMomento = 17.50m, CostoTotalAlMomento = 17.50m,
                    ProductoUnidadId = unidad1.Id
                },
                new()
                {
                    ProductoId = producto.Id, Cantidad = 1,
                    PrecioUnitario = 50m, Descuento = 0m, Subtotal = 50m,
                    CostoUnitarioAlMomento = 17.50m, CostoTotalAlMomento = 17.50m,
                    ProductoUnidadId = unidad2.Id
                }
            }
        };
        _context.Ventas.Add(venta);
        await _context.SaveChangesAsync();

        var dev = new Devolucion
        {
            VentaId = venta.Id, ClienteId = cliente.Id,
            Motivo = MotivoDevolucion.DefectoFabrica, Descripcion = "Test ambigüedad"
        };
        var detalles = new List<DevolucionDetalle>
        {
            new() { ProductoId = producto.Id, Cantidad = 1 }
        };

        await _service.CrearDevolucionAsync(dev, detalles);

        // Con múltiples VentaDetalles para el mismo producto, no debe auto-inferir
        Assert.Null(detalles[0].ProductoUnidadId);
    }

    [Fact]
    public async Task Completar_ConUnidadReintegrar_MarcaUnidadDevuelta()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync(stockActual: 5m);
        var unidad = await SeedProductoUnidadAsync(producto.Id, EstadoUnidad.Vendida);
        var venta = await SeedVentaConDetalleYUnidadAsync(cliente.Id, producto.Id, unidad.Id, 1);

        var dev = await SeedDevolucionAsync(venta.Id, cliente.Id,
            estado: EstadoDevolucion.Aprobada,
            tipoResolucion: TipoResolucionDevolucion.CambioMismoProducto);

        var detalle = new DevolucionDetalle
        {
            DevolucionId = dev.Id,
            ProductoId = producto.Id,
            ProductoUnidadId = unidad.Id,
            Cantidad = 1,
            PrecioUnitario = 50m,
            Subtotal = 50m,
            AccionRecomendada = AccionProducto.ReintegrarStock
        };
        _context.DevolucionDetalles.Add(detalle);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        await _service.CompletarDevolucionAsync(dev.Id, dev.RowVersion);

        _context.ChangeTracker.Clear();
        var unidadBd = await _context.ProductoUnidades.FindAsync(unidad.Id);
        Assert.Equal(EstadoUnidad.Devuelta, unidadBd!.Estado);
    }

    [Fact]
    public async Task Completar_ConUnidadCuarentena_MarcaUnidadDevuelta()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync(stockActual: 5m);
        var unidad = await SeedProductoUnidadAsync(producto.Id, EstadoUnidad.Vendida);
        var venta = await SeedVentaConDetalleYUnidadAsync(cliente.Id, producto.Id, unidad.Id, 1);

        var dev = await SeedDevolucionAsync(venta.Id, cliente.Id,
            estado: EstadoDevolucion.Aprobada,
            tipoResolucion: TipoResolucionDevolucion.CambioMismoProducto);

        var detalle = new DevolucionDetalle
        {
            DevolucionId = dev.Id,
            ProductoId = producto.Id,
            ProductoUnidadId = unidad.Id,
            Cantidad = 1,
            PrecioUnitario = 50m,
            Subtotal = 50m,
            AccionRecomendada = AccionProducto.Cuarentena
        };
        _context.DevolucionDetalles.Add(detalle);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        await _service.CompletarDevolucionAsync(dev.Id, dev.RowVersion);

        _context.ChangeTracker.Clear();
        var unidadBd = await _context.ProductoUnidades.FindAsync(unidad.Id);
        Assert.Equal(EstadoUnidad.Devuelta, unidadBd!.Estado);
    }

    // -------------------------------------------------------------------------
    // Fase 10.5 — Descarte → Baja
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Completar_ConUnidadDescarte_MarcaUnidadBaja()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync(stockActual: 5m);
        var unidad = await SeedProductoUnidadAsync(producto.Id, EstadoUnidad.Vendida);
        var venta = await SeedVentaConDetalleYUnidadAsync(cliente.Id, producto.Id, unidad.Id, 1);

        var dev = await SeedDevolucionAsync(venta.Id, cliente.Id,
            estado: EstadoDevolucion.Aprobada,
            tipoResolucion: TipoResolucionDevolucion.CambioMismoProducto);

        _context.DevolucionDetalles.Add(new DevolucionDetalle
        {
            DevolucionId = dev.Id,
            ProductoId = producto.Id,
            ProductoUnidadId = unidad.Id,
            Cantidad = 1, PrecioUnitario = 50m, Subtotal = 50m,
            AccionRecomendada = AccionProducto.Descarte
        });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        await _service.CompletarDevolucionAsync(dev.Id, dev.RowVersion);

        _context.ChangeTracker.Clear();
        var unidadBd = await _context.ProductoUnidades.FindAsync(unidad.Id);
        Assert.Equal(EstadoUnidad.Baja, unidadBd!.Estado);
    }

    [Fact]
    public async Task Completar_ConUnidadDescarte_RegistraProductoUnidadMovimiento()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync(stockActual: 5m);
        var unidad = await SeedProductoUnidadAsync(producto.Id, EstadoUnidad.Vendida);
        var venta = await SeedVentaConDetalleYUnidadAsync(cliente.Id, producto.Id, unidad.Id, 1);

        var dev = await SeedDevolucionAsync(venta.Id, cliente.Id,
            estado: EstadoDevolucion.Aprobada,
            tipoResolucion: TipoResolucionDevolucion.CambioMismoProducto);

        _context.DevolucionDetalles.Add(new DevolucionDetalle
        {
            DevolucionId = dev.Id,
            ProductoId = producto.Id,
            ProductoUnidadId = unidad.Id,
            Cantidad = 1, PrecioUnitario = 50m, Subtotal = 50m,
            AccionRecomendada = AccionProducto.Descarte
        });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        await _service.CompletarDevolucionAsync(dev.Id, dev.RowVersion);

        _context.ChangeTracker.Clear();
        var movimiento = await _context.ProductoUnidadMovimientos
            .AsNoTracking()
            .FirstOrDefaultAsync(m =>
                m.ProductoUnidadId == unidad.Id &&
                m.EstadoNuevo == EstadoUnidad.Baja);

        Assert.NotNull(movimiento);
        Assert.Equal(EstadoUnidad.Vendida, movimiento!.EstadoAnterior);
        Assert.Equal(EstadoUnidad.Baja, movimiento.EstadoNuevo);
        Assert.Contains(dev.NumeroDevolucion, movimiento.Motivo);
        Assert.Contains(dev.Id.ToString(), movimiento.OrigenReferencia);
    }

    [Fact]
    public async Task Completar_ConUnidadDescarte_NoGeneraMovimientoStock()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync(stockActual: 10m);
        var unidad = await SeedProductoUnidadAsync(producto.Id, EstadoUnidad.Vendida);
        var venta = await SeedVentaConDetalleYUnidadAsync(cliente.Id, producto.Id, unidad.Id, 1);

        var dev = await SeedDevolucionAsync(venta.Id, cliente.Id,
            estado: EstadoDevolucion.Aprobada,
            tipoResolucion: TipoResolucionDevolucion.CambioMismoProducto);

        _context.DevolucionDetalles.Add(new DevolucionDetalle
        {
            DevolucionId = dev.Id,
            ProductoId = producto.Id,
            ProductoUnidadId = unidad.Id,
            Cantidad = 1, PrecioUnitario = 50m, Subtotal = 50m,
            AccionRecomendada = AccionProducto.Descarte
        });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        await _service.CompletarDevolucionAsync(dev.Id, dev.RowVersion);

        _context.ChangeTracker.Clear();
        var productoBd = await _context.Productos.FirstAsync(p => p.Id == producto.Id);
        Assert.Equal(10m, productoBd.StockActual);

        var movimientosStock = await _context.MovimientosStock
            .AsNoTracking()
            .Where(m => m.ProductoId == producto.Id)
            .ToListAsync();
        Assert.Empty(movimientosStock);
    }

    [Fact]
    public async Task Completar_SinUnidadDescarte_SigueFuncionando()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync(stockActual: 10m);
        var venta = await SeedVentaConDetalleAsync(cliente.Id, producto.Id, 3);

        var dev = await SeedDevolucionAsync(venta.Id, cliente.Id,
            estado: EstadoDevolucion.Aprobada,
            tipoResolucion: TipoResolucionDevolucion.CambioMismoProducto);

        _context.DevolucionDetalles.Add(new DevolucionDetalle
        {
            DevolucionId = dev.Id,
            ProductoId = producto.Id,
            Cantidad = 1, PrecioUnitario = 50m, Subtotal = 50m,
            AccionRecomendada = AccionProducto.Descarte
        });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var resultado = await _service.CompletarDevolucionAsync(dev.Id, dev.RowVersion);

        Assert.Equal(EstadoDevolucion.Completada, resultado.Estado);
        _context.ChangeTracker.Clear();
        var movimientosUnidad = await _context.ProductoUnidadMovimientos.AsNoTracking().ToListAsync();
        Assert.Empty(movimientosUnidad);
    }

    [Fact]
    public async Task Completar_ConDescarte_NoAfectaDevueltaNiReparacion()
    {
        // No regresión: Devuelta y EnReparacion no se ven afectadas al agregar soporte Descarte
        var cliente = await SeedClienteAsync();
        var producto1 = await SeedProductoAsync(stockActual: 5m);
        var producto2 = await SeedProductoAsync(stockActual: 5m);
        var producto3 = await SeedProductoAsync(stockActual: 5m);
        var unidad1 = await SeedProductoUnidadAsync(producto1.Id, EstadoUnidad.Vendida);
        var unidad2 = await SeedProductoUnidadAsync(producto2.Id, EstadoUnidad.Vendida);
        var unidad3 = await SeedProductoUnidadAsync(producto3.Id, EstadoUnidad.Vendida);

        var venta = new Venta
        {
            Numero = Guid.NewGuid().ToString("N")[..8],
            ClienteId = cliente.Id,
            Estado = EstadoVenta.Entregada,
            TipoPago = TipoPago.Efectivo,
            FechaVenta = DateTime.UtcNow,
            Detalles = new List<VentaDetalle>
            {
                new() { ProductoId = producto1.Id, Cantidad = 1, PrecioUnitario = 50m, Descuento = 0m, Subtotal = 50m, CostoUnitarioAlMomento = 10m, CostoTotalAlMomento = 10m, ProductoUnidadId = unidad1.Id },
                new() { ProductoId = producto2.Id, Cantidad = 1, PrecioUnitario = 50m, Descuento = 0m, Subtotal = 50m, CostoUnitarioAlMomento = 10m, CostoTotalAlMomento = 10m, ProductoUnidadId = unidad2.Id },
                new() { ProductoId = producto3.Id, Cantidad = 1, PrecioUnitario = 50m, Descuento = 0m, Subtotal = 50m, CostoUnitarioAlMomento = 10m, CostoTotalAlMomento = 10m, ProductoUnidadId = unidad3.Id }
            }
        };
        _context.Ventas.Add(venta);
        await _context.SaveChangesAsync();
        await _context.Entry(venta).ReloadAsync();

        var dev = await SeedDevolucionAsync(venta.Id, cliente.Id,
            estado: EstadoDevolucion.Aprobada,
            tipoResolucion: TipoResolucionDevolucion.CambioMismoProducto);

        _context.DevolucionDetalles.Add(new DevolucionDetalle { DevolucionId = dev.Id, ProductoId = producto1.Id, ProductoUnidadId = unidad1.Id, Cantidad = 1, PrecioUnitario = 50m, Subtotal = 50m, AccionRecomendada = AccionProducto.ReintegrarStock });
        _context.DevolucionDetalles.Add(new DevolucionDetalle { DevolucionId = dev.Id, ProductoId = producto2.Id, ProductoUnidadId = unidad2.Id, Cantidad = 1, PrecioUnitario = 50m, Subtotal = 50m, AccionRecomendada = AccionProducto.Reparacion });
        _context.DevolucionDetalles.Add(new DevolucionDetalle { DevolucionId = dev.Id, ProductoId = producto3.Id, ProductoUnidadId = unidad3.Id, Cantidad = 1, PrecioUnitario = 50m, Subtotal = 50m, AccionRecomendada = AccionProducto.Descarte });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        await _service.CompletarDevolucionAsync(dev.Id, dev.RowVersion);

        _context.ChangeTracker.Clear();
        var u1 = await _context.ProductoUnidades.FindAsync(unidad1.Id);
        var u2 = await _context.ProductoUnidades.FindAsync(unidad2.Id);
        var u3 = await _context.ProductoUnidades.FindAsync(unidad3.Id);
        Assert.Equal(EstadoUnidad.Devuelta, u1!.Estado);
        Assert.Equal(EstadoUnidad.EnReparacion, u2!.Estado);
        Assert.Equal(EstadoUnidad.Baja, u3!.Estado);
    }

    [Fact]
    public async Task Completar_ConUnidad_RegistraProductoUnidadMovimiento()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync(stockActual: 5m);
        var unidad = await SeedProductoUnidadAsync(producto.Id, EstadoUnidad.Vendida);
        var venta = await SeedVentaConDetalleYUnidadAsync(cliente.Id, producto.Id, unidad.Id, 1);

        var dev = await SeedDevolucionAsync(venta.Id, cliente.Id,
            estado: EstadoDevolucion.Aprobada,
            tipoResolucion: TipoResolucionDevolucion.CambioMismoProducto);

        var detalle = new DevolucionDetalle
        {
            DevolucionId = dev.Id,
            ProductoId = producto.Id,
            ProductoUnidadId = unidad.Id,
            Cantidad = 1,
            PrecioUnitario = 50m,
            Subtotal = 50m,
            AccionRecomendada = AccionProducto.ReintegrarStock
        };
        _context.DevolucionDetalles.Add(detalle);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        await _service.CompletarDevolucionAsync(dev.Id, dev.RowVersion);

        _context.ChangeTracker.Clear();
        var movimiento = await _context.ProductoUnidadMovimientos
            .AsNoTracking()
            .FirstOrDefaultAsync(m =>
                m.ProductoUnidadId == unidad.Id &&
                m.EstadoNuevo == EstadoUnidad.Devuelta);

        Assert.NotNull(movimiento);
        Assert.Equal(EstadoUnidad.Vendida, movimiento!.EstadoAnterior);
        Assert.Equal(EstadoUnidad.Devuelta, movimiento.EstadoNuevo);
        Assert.Contains(dev.NumeroDevolucion, movimiento.Motivo);
        Assert.Contains(dev.Id.ToString(), movimiento.OrigenReferencia);
    }

    [Fact]
    public async Task Completar_SinUnidad_StockSigueFuncionandoYNoCreaMovimientoUnidad()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync(stockActual: 10m);
        var venta = await SeedVentaConDetalleAsync(cliente.Id, producto.Id, 3);

        var dev = await SeedDevolucionAsync(venta.Id, cliente.Id,
            estado: EstadoDevolucion.Aprobada,
            tipoResolucion: TipoResolucionDevolucion.CambioMismoProducto);

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

        await _service.CompletarDevolucionAsync(dev.Id, dev.RowVersion);

        _context.ChangeTracker.Clear();
        var productoBd = await _context.Productos.FirstAsync(p => p.Id == producto.Id);
        Assert.Equal(12m, productoBd.StockActual);

        var movimientos = await _context.ProductoUnidadMovimientos.AsNoTracking().ToListAsync();
        Assert.Empty(movimientos);
    }

    // -------------------------------------------------------------------------
    // Fase 10.4 — Garantía / reparación con unidad física
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Completar_ConUnidadReparacion_MarcaUnidadEnReparacion()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync(stockActual: 5m);
        var unidad = await SeedProductoUnidadAsync(producto.Id, EstadoUnidad.Vendida);
        var venta = await SeedVentaConDetalleYUnidadAsync(cliente.Id, producto.Id, unidad.Id, 1);

        var dev = await SeedDevolucionAsync(venta.Id, cliente.Id,
            estado: EstadoDevolucion.Aprobada,
            tipoResolucion: TipoResolucionDevolucion.CambioMismoProducto);

        _context.DevolucionDetalles.Add(new DevolucionDetalle
        {
            DevolucionId = dev.Id,
            ProductoId = producto.Id,
            ProductoUnidadId = unidad.Id,
            Cantidad = 1,
            PrecioUnitario = 50m,
            Subtotal = 50m,
            AccionRecomendada = AccionProducto.Reparacion
        });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        await _service.CompletarDevolucionAsync(dev.Id, dev.RowVersion);

        _context.ChangeTracker.Clear();
        var unidadBd = await _context.ProductoUnidades.FindAsync(unidad.Id);
        Assert.Equal(EstadoUnidad.EnReparacion, unidadBd!.Estado);
    }

    [Fact]
    public async Task Completar_ConUnidadReparacion_RegistraProductoUnidadMovimiento()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync(stockActual: 5m);
        var unidad = await SeedProductoUnidadAsync(producto.Id, EstadoUnidad.Vendida);
        var venta = await SeedVentaConDetalleYUnidadAsync(cliente.Id, producto.Id, unidad.Id, 1);

        var dev = await SeedDevolucionAsync(venta.Id, cliente.Id,
            estado: EstadoDevolucion.Aprobada,
            tipoResolucion: TipoResolucionDevolucion.CambioMismoProducto);

        _context.DevolucionDetalles.Add(new DevolucionDetalle
        {
            DevolucionId = dev.Id,
            ProductoId = producto.Id,
            ProductoUnidadId = unidad.Id,
            Cantidad = 1,
            PrecioUnitario = 50m,
            Subtotal = 50m,
            AccionRecomendada = AccionProducto.Reparacion
        });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        await _service.CompletarDevolucionAsync(dev.Id, dev.RowVersion);

        _context.ChangeTracker.Clear();
        var movimiento = await _context.ProductoUnidadMovimientos
            .AsNoTracking()
            .FirstOrDefaultAsync(m =>
                m.ProductoUnidadId == unidad.Id &&
                m.EstadoNuevo == EstadoUnidad.EnReparacion);

        Assert.NotNull(movimiento);
        Assert.Equal(EstadoUnidad.Vendida, movimiento!.EstadoAnterior);
        Assert.Equal(EstadoUnidad.EnReparacion, movimiento.EstadoNuevo);
        Assert.Contains(dev.NumeroDevolucion, movimiento.Motivo);
        Assert.Contains(dev.Id.ToString(), movimiento.OrigenReferencia);
    }

    [Fact]
    public async Task Completar_ConUnidadReparacion_NoGeneraMovimientoStockAgregado()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync(stockActual: 10m);
        var unidad = await SeedProductoUnidadAsync(producto.Id, EstadoUnidad.Vendida);
        var venta = await SeedVentaConDetalleYUnidadAsync(cliente.Id, producto.Id, unidad.Id, 1);

        var dev = await SeedDevolucionAsync(venta.Id, cliente.Id,
            estado: EstadoDevolucion.Aprobada,
            tipoResolucion: TipoResolucionDevolucion.CambioMismoProducto);

        _context.DevolucionDetalles.Add(new DevolucionDetalle
        {
            DevolucionId = dev.Id,
            ProductoId = producto.Id,
            ProductoUnidadId = unidad.Id,
            Cantidad = 1,
            PrecioUnitario = 50m,
            Subtotal = 50m,
            AccionRecomendada = AccionProducto.Reparacion
        });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        await _service.CompletarDevolucionAsync(dev.Id, dev.RowVersion);

        _context.ChangeTracker.Clear();
        var productoBd = await _context.Productos.FirstAsync(p => p.Id == producto.Id);
        Assert.Equal(10m, productoBd.StockActual);

        var movimientosStock = await _context.MovimientosStock
            .AsNoTracking()
            .Where(m => m.ProductoId == producto.Id)
            .ToListAsync();
        Assert.Empty(movimientosStock);
    }

    [Fact]
    public async Task Completar_SinUnidadReparacion_SigueFuncionando()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync(stockActual: 10m);
        var venta = await SeedVentaConDetalleAsync(cliente.Id, producto.Id, 3);

        var dev = await SeedDevolucionAsync(venta.Id, cliente.Id,
            estado: EstadoDevolucion.Aprobada,
            tipoResolucion: TipoResolucionDevolucion.CambioMismoProducto);

        _context.DevolucionDetalles.Add(new DevolucionDetalle
        {
            DevolucionId = dev.Id,
            ProductoId = producto.Id,
            Cantidad = 1,
            PrecioUnitario = 50m,
            Subtotal = 50m,
            AccionRecomendada = AccionProducto.Reparacion
        });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var resultado = await _service.CompletarDevolucionAsync(dev.Id, dev.RowVersion);

        Assert.Equal(EstadoDevolucion.Completada, resultado.Estado);

        _context.ChangeTracker.Clear();
        var movimientosUnidad = await _context.ProductoUnidadMovimientos.AsNoTracking().ToListAsync();
        Assert.Empty(movimientosUnidad);
    }

    [Fact]
    public async Task Completar_ConUnidadReparacion_NoAfectaOtrasAcciones()
    {
        // Valida no regresión del helper: ReintegrarStock sigue en Devuelta y Reparacion en EnReparacion
        var cliente = await SeedClienteAsync();
        var producto1 = await SeedProductoAsync(stockActual: 5m);
        var producto2 = await SeedProductoAsync(stockActual: 5m);
        var unidad1 = await SeedProductoUnidadAsync(producto1.Id, EstadoUnidad.Vendida);
        var unidad2 = await SeedProductoUnidadAsync(producto2.Id, EstadoUnidad.Vendida);

        var venta = new Venta
        {
            Numero = Guid.NewGuid().ToString("N")[..8],
            ClienteId = cliente.Id,
            Estado = EstadoVenta.Entregada,
            TipoPago = TipoPago.Efectivo,
            FechaVenta = DateTime.UtcNow,
            Detalles = new List<VentaDetalle>
            {
                new()
                {
                    ProductoId = producto1.Id, Cantidad = 1,
                    PrecioUnitario = 50m, Descuento = 0m, Subtotal = 50m,
                    CostoUnitarioAlMomento = 17.50m, CostoTotalAlMomento = 17.50m,
                    ProductoUnidadId = unidad1.Id
                },
                new()
                {
                    ProductoId = producto2.Id, Cantidad = 1,
                    PrecioUnitario = 50m, Descuento = 0m, Subtotal = 50m,
                    CostoUnitarioAlMomento = 17.50m, CostoTotalAlMomento = 17.50m,
                    ProductoUnidadId = unidad2.Id
                }
            }
        };
        _context.Ventas.Add(venta);
        await _context.SaveChangesAsync();
        await _context.Entry(venta).ReloadAsync();

        var dev = await SeedDevolucionAsync(venta.Id, cliente.Id,
            estado: EstadoDevolucion.Aprobada,
            tipoResolucion: TipoResolucionDevolucion.CambioMismoProducto);

        _context.DevolucionDetalles.Add(new DevolucionDetalle
        {
            DevolucionId = dev.Id,
            ProductoId = producto1.Id,
            ProductoUnidadId = unidad1.Id,
            Cantidad = 1, PrecioUnitario = 50m, Subtotal = 50m,
            AccionRecomendada = AccionProducto.ReintegrarStock
        });
        _context.DevolucionDetalles.Add(new DevolucionDetalle
        {
            DevolucionId = dev.Id,
            ProductoId = producto2.Id,
            ProductoUnidadId = unidad2.Id,
            Cantidad = 1, PrecioUnitario = 50m, Subtotal = 50m,
            AccionRecomendada = AccionProducto.Reparacion
        });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        await _service.CompletarDevolucionAsync(dev.Id, dev.RowVersion);

        _context.ChangeTracker.Clear();
        var u1 = await _context.ProductoUnidades.FindAsync(unidad1.Id);
        var u2 = await _context.ProductoUnidades.FindAsync(unidad2.Id);
        Assert.Equal(EstadoUnidad.Devuelta, u1!.Estado);
        Assert.Equal(EstadoUnidad.EnReparacion, u2!.Estado);
    }

    // -------------------------------------------------------------------------
    // Fase 10.6 — DevolverProveedor → Devuelta
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Completar_ConUnidadDevolverProveedor_MarcaUnidadDevuelta()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync(stockActual: 5m);
        var unidad = await SeedProductoUnidadAsync(producto.Id, EstadoUnidad.Vendida);
        var venta = await SeedVentaConDetalleYUnidadAsync(cliente.Id, producto.Id, unidad.Id, 1);

        var dev = await SeedDevolucionAsync(venta.Id, cliente.Id,
            estado: EstadoDevolucion.Aprobada,
            tipoResolucion: TipoResolucionDevolucion.CambioMismoProducto);

        _context.DevolucionDetalles.Add(new DevolucionDetalle
        {
            DevolucionId = dev.Id,
            ProductoId = producto.Id,
            ProductoUnidadId = unidad.Id,
            Cantidad = 1, PrecioUnitario = 50m, Subtotal = 50m,
            AccionRecomendada = AccionProducto.DevolverProveedor
        });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        await _service.CompletarDevolucionAsync(dev.Id, dev.RowVersion);

        _context.ChangeTracker.Clear();
        var unidadBd = await _context.ProductoUnidades.FindAsync(unidad.Id);
        Assert.Equal(EstadoUnidad.Devuelta, unidadBd!.Estado);
    }

    [Fact]
    public async Task Completar_ConUnidadDevolverProveedor_RegistraProductoUnidadMovimiento()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync(stockActual: 5m);
        var unidad = await SeedProductoUnidadAsync(producto.Id, EstadoUnidad.Vendida);
        var venta = await SeedVentaConDetalleYUnidadAsync(cliente.Id, producto.Id, unidad.Id, 1);

        var dev = await SeedDevolucionAsync(venta.Id, cliente.Id,
            estado: EstadoDevolucion.Aprobada,
            tipoResolucion: TipoResolucionDevolucion.CambioMismoProducto);

        _context.DevolucionDetalles.Add(new DevolucionDetalle
        {
            DevolucionId = dev.Id,
            ProductoId = producto.Id,
            ProductoUnidadId = unidad.Id,
            Cantidad = 1, PrecioUnitario = 50m, Subtotal = 50m,
            AccionRecomendada = AccionProducto.DevolverProveedor
        });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        await _service.CompletarDevolucionAsync(dev.Id, dev.RowVersion);

        _context.ChangeTracker.Clear();
        var movimiento = await _context.ProductoUnidadMovimientos
            .AsNoTracking()
            .FirstOrDefaultAsync(m =>
                m.ProductoUnidadId == unidad.Id &&
                m.EstadoNuevo == EstadoUnidad.Devuelta);

        Assert.NotNull(movimiento);
        Assert.Equal(EstadoUnidad.Vendida, movimiento!.EstadoAnterior);
        Assert.Equal(EstadoUnidad.Devuelta, movimiento.EstadoNuevo);
        Assert.Contains("proveedor", movimiento.Motivo, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(dev.NumeroDevolucion, movimiento.Motivo);
        Assert.Contains(dev.Id.ToString(), movimiento.OrigenReferencia);
    }

    [Fact]
    public async Task Completar_ConUnidadDevolverProveedor_NoGeneraMovimientoStock()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync(stockActual: 10m);
        var unidad = await SeedProductoUnidadAsync(producto.Id, EstadoUnidad.Vendida);
        var venta = await SeedVentaConDetalleYUnidadAsync(cliente.Id, producto.Id, unidad.Id, 1);

        var dev = await SeedDevolucionAsync(venta.Id, cliente.Id,
            estado: EstadoDevolucion.Aprobada,
            tipoResolucion: TipoResolucionDevolucion.CambioMismoProducto);

        _context.DevolucionDetalles.Add(new DevolucionDetalle
        {
            DevolucionId = dev.Id,
            ProductoId = producto.Id,
            ProductoUnidadId = unidad.Id,
            Cantidad = 1, PrecioUnitario = 50m, Subtotal = 50m,
            AccionRecomendada = AccionProducto.DevolverProveedor
        });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        await _service.CompletarDevolucionAsync(dev.Id, dev.RowVersion);

        _context.ChangeTracker.Clear();
        var productoBd = await _context.Productos.FirstAsync(p => p.Id == producto.Id);
        Assert.Equal(10m, productoBd.StockActual);

        var movimientosStock = await _context.MovimientosStock
            .AsNoTracking()
            .Where(m => m.ProductoId == producto.Id)
            .ToListAsync();
        Assert.Empty(movimientosStock);
    }

    [Fact]
    public async Task Completar_SinUnidadDevolverProveedor_SigueFuncionando()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync(stockActual: 10m);
        var venta = await SeedVentaConDetalleAsync(cliente.Id, producto.Id, 3);

        var dev = await SeedDevolucionAsync(venta.Id, cliente.Id,
            estado: EstadoDevolucion.Aprobada,
            tipoResolucion: TipoResolucionDevolucion.CambioMismoProducto);

        _context.DevolucionDetalles.Add(new DevolucionDetalle
        {
            DevolucionId = dev.Id,
            ProductoId = producto.Id,
            Cantidad = 1, PrecioUnitario = 50m, Subtotal = 50m,
            AccionRecomendada = AccionProducto.DevolverProveedor
        });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var resultado = await _service.CompletarDevolucionAsync(dev.Id, dev.RowVersion);

        Assert.Equal(EstadoDevolucion.Completada, resultado.Estado);
        _context.ChangeTracker.Clear();
        var movimientosUnidad = await _context.ProductoUnidadMovimientos.AsNoTracking().ToListAsync();
        Assert.Empty(movimientosUnidad);
    }

    [Fact]
    public async Task Completar_ConDevolverProveedor_NoAfectaOtrasAcciones()
    {
        // No regresión: Devuelta, EnReparacion y Baja no se ven afectadas al agregar soporte DevolverProveedor
        var cliente = await SeedClienteAsync();
        var producto1 = await SeedProductoAsync(stockActual: 5m);
        var producto2 = await SeedProductoAsync(stockActual: 5m);
        var producto3 = await SeedProductoAsync(stockActual: 5m);
        var producto4 = await SeedProductoAsync(stockActual: 5m);
        var unidad1 = await SeedProductoUnidadAsync(producto1.Id, EstadoUnidad.Vendida);
        var unidad2 = await SeedProductoUnidadAsync(producto2.Id, EstadoUnidad.Vendida);
        var unidad3 = await SeedProductoUnidadAsync(producto3.Id, EstadoUnidad.Vendida);
        var unidad4 = await SeedProductoUnidadAsync(producto4.Id, EstadoUnidad.Vendida);

        var venta = new Venta
        {
            Numero = Guid.NewGuid().ToString("N")[..8],
            ClienteId = cliente.Id,
            Estado = EstadoVenta.Entregada,
            TipoPago = TipoPago.Efectivo,
            FechaVenta = DateTime.UtcNow,
            Detalles = new List<VentaDetalle>
            {
                new() { ProductoId = producto1.Id, Cantidad = 1, PrecioUnitario = 50m, Descuento = 0m, Subtotal = 50m, CostoUnitarioAlMomento = 10m, CostoTotalAlMomento = 10m, ProductoUnidadId = unidad1.Id },
                new() { ProductoId = producto2.Id, Cantidad = 1, PrecioUnitario = 50m, Descuento = 0m, Subtotal = 50m, CostoUnitarioAlMomento = 10m, CostoTotalAlMomento = 10m, ProductoUnidadId = unidad2.Id },
                new() { ProductoId = producto3.Id, Cantidad = 1, PrecioUnitario = 50m, Descuento = 0m, Subtotal = 50m, CostoUnitarioAlMomento = 10m, CostoTotalAlMomento = 10m, ProductoUnidadId = unidad3.Id },
                new() { ProductoId = producto4.Id, Cantidad = 1, PrecioUnitario = 50m, Descuento = 0m, Subtotal = 50m, CostoUnitarioAlMomento = 10m, CostoTotalAlMomento = 10m, ProductoUnidadId = unidad4.Id }
            }
        };
        _context.Ventas.Add(venta);
        await _context.SaveChangesAsync();
        await _context.Entry(venta).ReloadAsync();

        var dev = await SeedDevolucionAsync(venta.Id, cliente.Id,
            estado: EstadoDevolucion.Aprobada,
            tipoResolucion: TipoResolucionDevolucion.CambioMismoProducto);

        _context.DevolucionDetalles.Add(new DevolucionDetalle { DevolucionId = dev.Id, ProductoId = producto1.Id, ProductoUnidadId = unidad1.Id, Cantidad = 1, PrecioUnitario = 50m, Subtotal = 50m, AccionRecomendada = AccionProducto.ReintegrarStock });
        _context.DevolucionDetalles.Add(new DevolucionDetalle { DevolucionId = dev.Id, ProductoId = producto2.Id, ProductoUnidadId = unidad2.Id, Cantidad = 1, PrecioUnitario = 50m, Subtotal = 50m, AccionRecomendada = AccionProducto.Reparacion });
        _context.DevolucionDetalles.Add(new DevolucionDetalle { DevolucionId = dev.Id, ProductoId = producto3.Id, ProductoUnidadId = unidad3.Id, Cantidad = 1, PrecioUnitario = 50m, Subtotal = 50m, AccionRecomendada = AccionProducto.Descarte });
        _context.DevolucionDetalles.Add(new DevolucionDetalle { DevolucionId = dev.Id, ProductoId = producto4.Id, ProductoUnidadId = unidad4.Id, Cantidad = 1, PrecioUnitario = 50m, Subtotal = 50m, AccionRecomendada = AccionProducto.DevolverProveedor });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        await _service.CompletarDevolucionAsync(dev.Id, dev.RowVersion);

        _context.ChangeTracker.Clear();
        var u1 = await _context.ProductoUnidades.FindAsync(unidad1.Id);
        var u2 = await _context.ProductoUnidades.FindAsync(unidad2.Id);
        var u3 = await _context.ProductoUnidades.FindAsync(unidad3.Id);
        var u4 = await _context.ProductoUnidades.FindAsync(unidad4.Id);
        Assert.Equal(EstadoUnidad.Devuelta, u1!.Estado);
        Assert.Equal(EstadoUnidad.EnReparacion, u2!.Estado);
        Assert.Equal(EstadoUnidad.Baja, u3!.Estado);
        Assert.Equal(EstadoUnidad.Devuelta, u4!.Estado);
    }

    // -------------------------------------------------------------------------
    // Fase 10.4B — ObtenerDevolucionAsync incluye ProductoUnidad
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ObtenerDevolucion_ConProductoUnidad_IncluyeDatosUnidad()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        var unidad = await SeedProductoUnidadAsync(producto.Id, EstadoUnidad.Vendida);
        var venta = await SeedVentaConDetalleYUnidadAsync(cliente.Id, producto.Id, unidad.Id, 1);
        var dev = await SeedDevolucionAsync(venta.Id, cliente.Id);

        _context.DevolucionDetalles.Add(new DevolucionDetalle
        {
            DevolucionId = dev.Id,
            ProductoId = producto.Id,
            ProductoUnidadId = unidad.Id,
            Cantidad = 1, PrecioUnitario = 100m, Subtotal = 100m,
            AccionRecomendada = AccionProducto.ReintegrarStock
        });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var resultado = await _service.ObtenerDevolucionAsync(dev.Id);

        Assert.NotNull(resultado);
        var detalle = Assert.Single(resultado!.Detalles);
        Assert.NotNull(detalle.ProductoUnidad);
        Assert.Equal(unidad.Id, detalle.ProductoUnidad!.Id);
        Assert.Equal(unidad.CodigoInternoUnidad, detalle.ProductoUnidad.CodigoInternoUnidad);
        Assert.Equal(EstadoUnidad.Vendida, detalle.ProductoUnidad.Estado);
    }

    // -------------------------------------------------------------------------
    // CompletarDevolucionAsync — egreso de caja por reembolso
    // -------------------------------------------------------------------------

    private DevolucionService CrearServiceConCaja(ICajaService cajaService) => new(
        _context,
        new MovimientoStockService(_context, NullLogger<MovimientoStockService>.Instance),
        new StubCurrentUserServiceDev(),
        NullLogger<DevolucionService>.Instance,
        cajaService);

    [Fact]
    public async Task Completar_ReembolsoConEgresoCaja_RegistraEgresoConDatosDeLaDevolucion()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync(stockActual: 10m);
        var venta = await SeedVentaConDetalleAsync(cliente.Id, producto.Id, 5);
        var dev = await SeedDevolucionAsync(venta.Id, cliente.Id, estado: EstadoDevolucion.Aprobada,
            tipoResolucion: TipoResolucionDevolucion.ReembolsoDinero);
        dev.RegistrarEgresoCaja = true;
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var caja = new StubCajaServiceDevolucion();
        var service = CrearServiceConCaja(caja);
        var rowVersion = (await _context.Devoluciones.AsNoTracking().FirstAsync(d => d.Id == dev.Id)).RowVersion;

        var resultado = await service.CompletarDevolucionAsync(dev.Id, rowVersion);

        Assert.Equal(EstadoDevolucion.Completada, resultado.Estado);
        var reembolso = Assert.Single(caja.Reembolsos);
        Assert.Equal(dev.Id, reembolso.DevolucionId);
        Assert.Equal(venta.Id, reembolso.VentaId);
        Assert.Equal(venta.Numero, reembolso.VentaNumero);
        Assert.Equal(dev.NumeroDevolucion, reembolso.DevolucionNumero);
        Assert.Equal(50m, reembolso.Monto);
        Assert.Equal("testuser", reembolso.Usuario);
    }

    [Fact]
    public async Task Completar_ReembolsoSinEgresoCaja_NoTocaCaja()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        var venta = await SeedVentaConDetalleAsync(cliente.Id, producto.Id, 5);
        var dev = await SeedDevolucionAsync(venta.Id, cliente.Id, estado: EstadoDevolucion.Aprobada,
            tipoResolucion: TipoResolucionDevolucion.ReembolsoDinero);
        _context.ChangeTracker.Clear();

        var caja = new StubCajaServiceDevolucion();
        var service = CrearServiceConCaja(caja);

        var resultado = await service.CompletarDevolucionAsync(dev.Id, dev.RowVersion);

        Assert.Equal(EstadoDevolucion.Completada, resultado.Estado);
        Assert.Empty(caja.Reembolsos);
    }

    [Fact]
    public async Task Completar_ResolucionNotaCredito_NoTocaCaja()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        var venta = await SeedVentaConDetalleAsync(cliente.Id, producto.Id, 5);
        var dev = await SeedDevolucionAsync(venta.Id, cliente.Id, estado: EstadoDevolucion.Aprobada,
            tipoResolucion: TipoResolucionDevolucion.NotaCredito);
        _context.ChangeTracker.Clear();

        var caja = new StubCajaServiceDevolucion();
        var service = CrearServiceConCaja(caja);

        var resultado = await service.CompletarDevolucionAsync(dev.Id, dev.RowVersion);

        Assert.Equal(EstadoDevolucion.Completada, resultado.Estado);
        Assert.Empty(caja.Reembolsos);
    }

    [Fact]
    public async Task Completar_ReembolsoConEgresoCaja_SinCajaService_LanzaYNoCompleta()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync();
        var venta = await SeedVentaConDetalleAsync(cliente.Id, producto.Id, 5);
        var dev = await SeedDevolucionAsync(venta.Id, cliente.Id, estado: EstadoDevolucion.Aprobada,
            tipoResolucion: TipoResolucionDevolucion.ReembolsoDinero);
        dev.RegistrarEgresoCaja = true;
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var rowVersion = (await _context.Devoluciones.AsNoTracking().FirstAsync(d => d.Id == dev.Id)).RowVersion;

        // _service fue construido sin ICajaService (escenario de configuración incompleta)
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CompletarDevolucionAsync(dev.Id, rowVersion));

        _context.ChangeTracker.Clear();
        var devDb = await _context.Devoluciones.AsNoTracking().FirstAsync(d => d.Id == dev.Id);
        Assert.Equal(EstadoDevolucion.Aprobada, devDb.Estado);
    }

    [Fact]
    public async Task Completar_CajaFalla_RevierteEstadoYStock()
    {
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync(stockActual: 10m);
        var venta = await SeedVentaConDetalleAsync(cliente.Id, producto.Id, 5);
        var dev = await SeedDevolucionAsync(venta.Id, cliente.Id, estado: EstadoDevolucion.Aprobada,
            tipoResolucion: TipoResolucionDevolucion.ReembolsoDinero);
        dev.RegistrarEgresoCaja = true;
        _context.DevolucionDetalles.Add(new DevolucionDetalle
        {
            DevolucionId = dev.Id,
            ProductoId = producto.Id,
            Cantidad = 2,
            PrecioUnitario = 50m,
            Subtotal = 100m,
            AccionRecomendada = AccionProducto.ReintegrarStock
        });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var caja = new StubCajaServiceDevolucion
        {
            ExcepcionAlRegistrar = new InvalidOperationException("No hay una caja abierta para registrar el reembolso.")
        };
        var service = CrearServiceConCaja(caja);
        var rowVersion = (await _context.Devoluciones.AsNoTracking().FirstAsync(d => d.Id == dev.Id)).RowVersion;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CompletarDevolucionAsync(dev.Id, rowVersion));

        _context.ChangeTracker.Clear();
        var devDb = await _context.Devoluciones.AsNoTracking().FirstAsync(d => d.Id == dev.Id);
        Assert.Equal(EstadoDevolucion.Aprobada, devDb.Estado);

        var productoDb = await _context.Productos.AsNoTracking().FirstAsync(p => p.Id == producto.Id);
        Assert.Equal(10m, productoDb.StockActual);
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

// ---------------------------------------------------------------------------
// Stub de ICajaService: registra las llamadas a RegistrarMovimientoDevolucionAsync.
// El resto de los miembros no los usa DevolucionService.
// ---------------------------------------------------------------------------
file sealed class StubCajaServiceDevolucion : ICajaService
{
    public List<(int DevolucionId, int VentaId, string VentaNumero, string DevolucionNumero, decimal Monto, string Usuario)> Reembolsos { get; } = new();
    public Exception? ExcepcionAlRegistrar { get; set; }

    public Task<MovimientoCaja> RegistrarMovimientoDevolucionAsync(int devolucionId, int ventaId, string ventaNumero, string devolucionNumero, decimal monto, string usuario)
    {
        if (ExcepcionAlRegistrar != null)
            throw ExcepcionAlRegistrar;

        Reembolsos.Add((devolucionId, ventaId, ventaNumero, devolucionNumero, monto, usuario));
        return Task.FromResult(new MovimientoCaja { Monto = monto });
    }

    public Task<List<Caja>> ObtenerTodasCajasAsync() => throw new NotSupportedException();
    public Task<Caja?> ObtenerCajaPorIdAsync(int id) => throw new NotSupportedException();
    public Task<Caja> CrearCajaAsync(CajaViewModel model) => throw new NotSupportedException();
    public Task<Caja> ActualizarCajaAsync(int id, CajaViewModel model) => throw new NotSupportedException();
    public Task EliminarCajaAsync(int id, byte[]? rowVersion = null) => throw new NotSupportedException();
    public Task<bool> ExisteCodigoCajaAsync(string codigo, int? cajaIdExcluir = null) => throw new NotSupportedException();
    public Task<AperturaCaja> AbrirCajaAsync(AbrirCajaViewModel model, string usuario) => throw new NotSupportedException();
    public Task<decimal?> ObtenerUltimoEfectivoCierreAsync(int cajaId) => throw new NotSupportedException();
    public Task<AperturaCaja?> ObtenerAperturaActivaAsync(int cajaId) => throw new NotSupportedException();
    public Task<AperturaCaja?> ObtenerAperturaPorIdAsync(int id) => throw new NotSupportedException();
    public Task<List<AperturaCaja>> ObtenerAperturasAbiertasAsync() => throw new NotSupportedException();
    public Task<bool> TieneCajaAbiertaAsync(int cajaId) => throw new NotSupportedException();
    public Task<AperturaCaja?> ObtenerAperturaActivaParaUsuarioAsync(string usuario) => throw new NotSupportedException();
    public Task<bool> ExisteAlgunaCajaAbiertaAsync() => throw new NotSupportedException();
    public Task<MovimientoCaja> RegistrarMovimientoAsync(MovimientoCajaViewModel model, string usuario) => throw new NotSupportedException();
    public Task<List<MovimientoCaja>> ObtenerMovimientosDeAperturaAsync(int aperturaId) => throw new NotSupportedException();
    public Task<decimal> CalcularSaldoActualAsync(int aperturaId) => throw new NotSupportedException();
    public Task<decimal> CalcularSaldoRealAsync(int aperturaId) => throw new NotSupportedException();
    public Task<MovimientoCaja> AcreditarMovimientoAsync(int movimientoId, string usuario) => throw new NotSupportedException();
    public Task<MovimientoCaja?> RegistrarMovimientoVentaAsync(int ventaId, string ventaNumero, decimal monto, TipoPago tipoPago, string usuario) => throw new NotSupportedException();
    public Task<AperturaCaja?> ObtenerAperturaActivaParaVentaAsync() => throw new NotSupportedException();
    public Task<MovimientoCaja?> RegistrarMovimientoCuotaAsync(int cuotaId, string creditoNumero, int numeroCuota, decimal monto, string medioPago, string usuario) => throw new NotSupportedException();
    public Task<MovimientoCaja?> RegistrarMovimientoAnticipoAsync(int creditoId, string creditoNumero, decimal montoAnticipo, string usuario) => throw new NotSupportedException();
    public Task<MovimientoCaja?> RegistrarContramovimientoVentaAsync(int ventaId, string ventaNumero, string motivo, string usuario) => throw new NotSupportedException();
    public Task<CierreCaja> CerrarCajaAsync(CerrarCajaViewModel model, string usuario) => throw new NotSupportedException();
    public Task<CierreCaja?> ObtenerCierrePorIdAsync(int id) => throw new NotSupportedException();
    public Task<List<CierreCaja>> ObtenerHistorialCierresAsync(int? cajaId = null, DateTime? fechaDesde = null, DateTime? fechaHasta = null) => throw new NotSupportedException();
    public Task<DetallesAperturaViewModel> ObtenerDetallesAperturaAsync(int aperturaId) => throw new NotSupportedException();
    public Task<ReporteCajaViewModel> GenerarReporteCajaAsync(DateTime fechaDesde, DateTime fechaHasta, int? cajaId = null) => throw new NotSupportedException();
    public Task<HistorialCierresViewModel> ObtenerEstadisticasCierresAsync(int? cajaId = null, DateTime? fechaDesde = null, DateTime? fechaHasta = null) => throw new NotSupportedException();
}
