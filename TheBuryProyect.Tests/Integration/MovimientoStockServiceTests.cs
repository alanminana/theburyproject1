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
/// Tests de integración para MovimientoStockService.
/// Cubren RegistrarAjusteAsync, RegistrarEntradasAsync y RegistrarSalidasAsync:
/// validaciones de cantidad, actualización de stock, stock insuficiente,
/// ajuste absoluto, batch con múltiples productos.
/// </summary>
public class MovimientoStockServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly MovimientoStockService _service;

    public MovimientoStockServiceTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        _service = new MovimientoStockService(
            _context,
            NullLogger<MovimientoStockService>.Instance);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<Producto> SeedProductoAsync(decimal stockActual = 100m, decimal precioCompra = 10m)
    {
        var codigo = Guid.NewGuid().ToString("N")[..10];

        var categoria = new Categoria
        {
            Codigo = codigo,
            Nombre = "Cat-" + codigo,
            Activo = true
        };
        _context.Set<Categoria>().Add(categoria);
        await _context.SaveChangesAsync();

        var marca = new Marca
        {
            Codigo = codigo,
            Nombre = "Marca-" + codigo,
            Activo = true
        };
        _context.Set<Marca>().Add(marca);
        await _context.SaveChangesAsync();

        var producto = new Producto
        {
            Codigo = codigo,
            Nombre = "Prod-" + codigo,
            CategoriaId = categoria.Id,
            MarcaId = marca.Id,
            PrecioCompra = precioCompra,
            PrecioVenta = 15m,
            PorcentajeIVA = 21m,
            StockActual = stockActual,
            Activo = true
        };
        _context.Set<Producto>().Add(producto);
        await _context.SaveChangesAsync();

        return producto;
    }

    // -------------------------------------------------------------------------
    // ValidarCantidadAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ValidarCantidad_CantidadPositiva_Valida()
    {
        var (valido, _) = await _service.ValidarCantidadAsync(10m);
        Assert.True(valido);
    }

    [Fact]
    public async Task ValidarCantidad_CantidadCero_Invalida()
    {
        var (valido, mensaje) = await _service.ValidarCantidadAsync(0m);
        Assert.False(valido);
        Assert.Contains("mayor", mensaje, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidarCantidad_CantidadNegativa_Invalida()
    {
        var (valido, _) = await _service.ValidarCantidadAsync(-5m);
        Assert.False(valido);
    }

    [Fact]
    public async Task ValidarCantidad_SuperaMaximo_Invalida()
    {
        var (valido, mensaje) = await _service.ValidarCantidadAsync(1_000_000m);
        Assert.False(valido);
        Assert.Contains("exceder", mensaje, StringComparison.OrdinalIgnoreCase);
    }

    // -------------------------------------------------------------------------
    // HayStockDisponibleAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task HayStockDisponible_StockSuficiente_ReturnsTrue()
    {
        var producto = await SeedProductoAsync(stockActual: 50m);
        var result = await _service.HayStockDisponibleAsync(producto.Id, 50m);
        Assert.True(result);
    }

    [Fact]
    public async Task HayStockDisponible_StockInsuficiente_ReturnsFalse()
    {
        var producto = await SeedProductoAsync(stockActual: 10m);
        var result = await _service.HayStockDisponibleAsync(producto.Id, 11m);
        Assert.False(result);
    }

    [Fact]
    public async Task HayStockDisponible_ProductoNoExiste_ReturnsFalse()
    {
        var result = await _service.HayStockDisponibleAsync(99999, 1m);
        Assert.False(result);
    }

    // -------------------------------------------------------------------------
    // RegistrarAjusteAsync — Entrada
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RegistrarAjuste_Entrada_IncrementaStock()
    {
        var producto = await SeedProductoAsync(stockActual: 100m);

        var mov = await _service.RegistrarAjusteAsync(
            producto.Id, TipoMovimiento.Entrada, 50m,
            referencia: null, motivo: "Compra");

        Assert.Equal(TipoMovimiento.Entrada, mov.Tipo);
        Assert.Equal(50m, mov.Cantidad);
        Assert.Equal(100m, mov.StockAnterior);
        Assert.Equal(150m, mov.StockNuevo);

        var stockBd = (await _context.Set<Producto>()
            .FirstAsync(p => p.Id == producto.Id)).StockActual;
        Assert.Equal(150m, stockBd);
    }

    [Fact]
    public async Task RegistrarAjuste_GuardaCostoFallbackYFuenteAjusteManual()
    {
        var producto = await SeedProductoAsync(stockActual: 100m, precioCompra: 12.34m);

        var mov = await _service.RegistrarAjusteAsync(
            producto.Id, TipoMovimiento.Entrada, 3m,
            referencia: null, motivo: "Ajuste");

        Assert.Equal(12.34m, mov.CostoUnitarioAlMomento);
        Assert.Equal(37.02m, mov.CostoTotalAlMomento);
        Assert.Equal("AjusteManual", mov.FuenteCosto);
        Assert.Equal(3m, mov.Cantidad);
        Assert.Equal(100m, mov.StockAnterior);
        Assert.Equal(103m, mov.StockNuevo);
    }

    // -------------------------------------------------------------------------
    // RegistrarAjusteAsync — Salida
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RegistrarAjuste_Salida_DecrementaStock()
    {
        var producto = await SeedProductoAsync(stockActual: 100m);

        var mov = await _service.RegistrarAjusteAsync(
            producto.Id, TipoMovimiento.Salida, 30m,
            referencia: "V-001", motivo: "Venta");

        Assert.Equal(TipoMovimiento.Salida, mov.Tipo);
        Assert.Equal(30m, mov.Cantidad);
        Assert.Equal(100m, mov.StockAnterior);
        Assert.Equal(70m, mov.StockNuevo);

        var stockBd = (await _context.Set<Producto>()
            .FirstAsync(p => p.Id == producto.Id)).StockActual;
        Assert.Equal(70m, stockBd);
    }

    [Fact]
    public async Task RegistrarAjuste_SalidaStockInsuficiente_LanzaExcepcion()
    {
        var producto = await SeedProductoAsync(stockActual: 20m);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.RegistrarAjusteAsync(
                producto.Id, TipoMovimiento.Salida, 21m,
                referencia: null, motivo: "Test"));
    }

    // -------------------------------------------------------------------------
    // RegistrarAjusteAsync — Ajuste absoluto
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RegistrarAjuste_AjusteAbsoluto_SetearStockExacto()
    {
        var producto = await SeedProductoAsync(stockActual: 100m);

        // Ajuste a 75 → delta debe ser -25 registrado en el movimiento
        var mov = await _service.RegistrarAjusteAsync(
            producto.Id, TipoMovimiento.Ajuste, 75m,
            referencia: null, motivo: "Inventario");

        Assert.Equal(TipoMovimiento.Ajuste, mov.Tipo);
        Assert.Equal(-25m, mov.Cantidad); // delta = 75 - 100 = -25
        Assert.Equal(100m, mov.StockAnterior);
        Assert.Equal(75m, mov.StockNuevo);

        var stockBd = (await _context.Set<Producto>()
            .FirstAsync(p => p.Id == producto.Id)).StockActual;
        Assert.Equal(75m, stockBd);
    }

    [Fact]
    public async Task RegistrarAjuste_AjusteNegativo_LanzaExcepcion()
    {
        var producto = await SeedProductoAsync(stockActual: 100m);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.RegistrarAjusteAsync(
                producto.Id, TipoMovimiento.Ajuste, -1m,
                referencia: null, motivo: "Test"));
    }

    // -------------------------------------------------------------------------
    // RegistrarAjusteAsync — producto no encontrado
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RegistrarAjuste_ProductoNoExiste_LanzaExcepcion()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.RegistrarAjusteAsync(
                99999, TipoMovimiento.Entrada, 10m,
                referencia: null, motivo: "Test"));
    }

    // -------------------------------------------------------------------------
    // RegistrarAjusteAsync — usuario asignado
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RegistrarAjuste_UsuarioNulo_UsaSistema()
    {
        var producto = await SeedProductoAsync();

        var mov = await _service.RegistrarAjusteAsync(
            producto.Id, TipoMovimiento.Entrada, 5m,
            referencia: null, motivo: "Test", usuarioActual: null);

        Assert.Equal("Sistema", mov.CreatedBy);
    }

    [Fact]
    public async Task RegistrarAjuste_UsuarioExplícito_AsignaUsuario()
    {
        var producto = await SeedProductoAsync();

        var mov = await _service.RegistrarAjusteAsync(
            producto.Id, TipoMovimiento.Entrada, 5m,
            referencia: null, motivo: "Test", usuarioActual: "user42");

        Assert.Equal("user42", mov.CreatedBy);
    }

    // -------------------------------------------------------------------------
    // RegistrarEntradasAsync — batch
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RegistrarEntradas_ListaVacia_RetornaVacio()
    {
        var result = await _service.RegistrarEntradasAsync(
            new List<(int, decimal, string?)>(), "Test");
        Assert.Empty(result);
    }

    [Fact]
    public async Task RegistrarEntradas_MultipleProductos_ActualizaStockTodos()
    {
        var p1 = await SeedProductoAsync(stockActual: 0m);
        var p2 = await SeedProductoAsync(stockActual: 50m);

        var entradas = new List<(int, decimal, string?)>
        {
            (p1.Id, 20m, "Ref1"),
            (p2.Id, 10m, "Ref2")
        };

        var movimientos = await _service.RegistrarEntradasAsync(entradas, "Batch entrada");

        Assert.Equal(2, movimientos.Count);

        var stock1 = (await _context.Set<Producto>().FirstAsync(p => p.Id == p1.Id)).StockActual;
        var stock2 = (await _context.Set<Producto>().FirstAsync(p => p.Id == p2.Id)).StockActual;
        Assert.Equal(20m, stock1);
        Assert.Equal(60m, stock2);
    }

    [Fact]
    public async Task RegistrarEntradas_GuardaCostoFallbackDesdeProducto()
    {
        var producto = await SeedProductoAsync(stockActual: 5m, precioCompra: 7.25m);

        var movimientos = await _service.RegistrarEntradasAsync(
            new List<(int, decimal, string?)> { (producto.Id, 4m, "E1") },
            "Entrada");

        var mov = Assert.Single(movimientos);
        Assert.Equal(7.25m, mov.CostoUnitarioAlMomento);
        Assert.Equal(29.00m, mov.CostoTotalAlMomento);
        Assert.Equal("ProductoActual", mov.FuenteCosto);
        Assert.Equal(5m, mov.StockAnterior);
        Assert.Equal(9m, mov.StockNuevo);
    }

    [Fact]
    public async Task RegistrarEntradas_CostoInformadoCero_UsaFallbackProductoActual()
    {
        var producto = await SeedProductoAsync(stockActual: 5m, precioCompra: 11.11m);
        var entradas = new List<(int, decimal, string?)> { (producto.Id, 2m, "E1") };
        var costos = new List<MovimientoStockCostoLinea>
        {
            new(producto.Id, 2m, "E1", 0m, "OrdenCompraDetalle")
        };

        var movimientos = await _service.RegistrarEntradasAsync(entradas, "Entrada", costos: costos);

        var mov = Assert.Single(movimientos);
        Assert.Equal(11.11m, mov.CostoUnitarioAlMomento);
        Assert.Equal(22.22m, mov.CostoTotalAlMomento);
        Assert.Equal("ProductoActual", mov.FuenteCosto);
    }

    [Fact]
    public async Task RegistrarEntradas_ProductoNoExiste_LanzaExcepcion()
    {
        var entradas = new List<(int, decimal, string?)> { (99999, 10m, null) };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.RegistrarEntradasAsync(entradas, "Test"));
    }

    [Fact]
    public async Task RegistrarEntradas_CantidadCero_LanzaExcepcion()
    {
        var producto = await SeedProductoAsync();
        var entradas = new List<(int, decimal, string?)> { (producto.Id, 0m, null) };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.RegistrarEntradasAsync(entradas, "Test"));
    }

    // -------------------------------------------------------------------------
    // RegistrarSalidasAsync — batch
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RegistrarSalidas_ListaVacia_RetornaVacio()
    {
        var result = await _service.RegistrarSalidasAsync(
            new List<(int, decimal, string?)>(), "Test");
        Assert.Empty(result);
    }

    [Fact]
    public async Task RegistrarSalidas_StockSuficiente_DecrementaCorrectamente()
    {
        var p1 = await SeedProductoAsync(stockActual: 100m);
        var p2 = await SeedProductoAsync(stockActual: 80m);

        var salidas = new List<(int, decimal, string?)>
        {
            (p1.Id, 40m, "S1"),
            (p2.Id, 30m, "S2")
        };

        var movimientos = await _service.RegistrarSalidasAsync(salidas, "Ventas");

        Assert.Equal(2, movimientos.Count);

        var stock1 = (await _context.Set<Producto>().FirstAsync(p => p.Id == p1.Id)).StockActual;
        var stock2 = (await _context.Set<Producto>().FirstAsync(p => p.Id == p2.Id)).StockActual;
        Assert.Equal(60m, stock1);
        Assert.Equal(50m, stock2);
    }

    [Fact]
    public async Task RegistrarSalidas_GuardaCostoFallbackDesdeProducto()
    {
        var producto = await SeedProductoAsync(stockActual: 10m, precioCompra: 8.50m);

        var movimientos = await _service.RegistrarSalidasAsync(
            new List<(int, decimal, string?)> { (producto.Id, 4m, "S1") },
            "Salida");

        var mov = Assert.Single(movimientos);
        Assert.Equal(8.50m, mov.CostoUnitarioAlMomento);
        Assert.Equal(34.00m, mov.CostoTotalAlMomento);
        Assert.Equal("ProductoActual", mov.FuenteCosto);
        Assert.Equal(10m, mov.StockAnterior);
        Assert.Equal(6m, mov.StockNuevo);
    }

    [Fact]
    public async Task RegistrarSalidas_StockInsuficiente_LanzaExcepcion()
    {
        var producto = await SeedProductoAsync(stockActual: 5m);
        var salidas = new List<(int, decimal, string?)> { (producto.Id, 10m, null) };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.RegistrarSalidasAsync(salidas, "Test"));
    }

    [Fact]
    public async Task RegistrarSalidas_MismoProductoDosVeces_AcumulaCorrectamente()
    {
        var producto = await SeedProductoAsync(stockActual: 100m);

        var salidas = new List<(int, decimal, string?)>
        {
            (producto.Id, 30m, "S1"),
            (producto.Id, 40m, "S2")
        };

        // total = 70 <= 100 → válido
        var movimientos = await _service.RegistrarSalidasAsync(salidas, "Test");

        Assert.Equal(2, movimientos.Count);
        var stockBd = (await _context.Set<Producto>().FirstAsync(p => p.Id == producto.Id)).StockActual;
        Assert.Equal(30m, stockBd); // 100 - 30 - 40
    }

    // =========================================================================
    // GetAllAsync
    // =========================================================================

    [Fact]
    public async Task GetAll_SinMovimientos_RetornaVacio()
    {
        var resultado = await _service.GetAllAsync();
        Assert.Empty(resultado);
    }

    [Fact]
    public async Task GetAll_ConMovimientos_DevuelveTodos()
    {
        var p1 = await SeedProductoAsync();
        var p2 = await SeedProductoAsync();
        await _service.RegistrarAjusteAsync(p1.Id, TipoMovimiento.Entrada, 10m, null, "test", "user");
        await _service.RegistrarAjusteAsync(p2.Id, TipoMovimiento.Entrada, 5m, null, "test", "user");

        var resultado = await _service.GetAllAsync();

        Assert.Equal(2, resultado.Count());
    }

    // =========================================================================
    // GetByIdAsync
    // =========================================================================

    [Fact]
    public async Task GetById_MovimientoExistente_RetornaMovimiento()
    {
        var producto = await SeedProductoAsync();
        var movimiento = await _service.RegistrarAjusteAsync(producto.Id, TipoMovimiento.Entrada, 10m, null, "test", "user");

        var resultado = await _service.GetByIdAsync(movimiento.Id);

        Assert.NotNull(resultado);
        Assert.Equal(movimiento.Id, resultado!.Id);
    }

    [Fact]
    public async Task GetById_Inexistente_RetornaNull()
    {
        var resultado = await _service.GetByIdAsync(99999);
        Assert.Null(resultado);
    }

    // =========================================================================
    // GetByProductoIdAsync
    // =========================================================================

    [Fact]
    public async Task GetByProductoId_FiltraPorProducto()
    {
        var p1 = await SeedProductoAsync();
        var p2 = await SeedProductoAsync();
        await _service.RegistrarAjusteAsync(p1.Id, TipoMovimiento.Entrada, 10m, null, "test", "user");
        await _service.RegistrarAjusteAsync(p2.Id, TipoMovimiento.Entrada, 5m, null, "test", "user");

        var resultado = await _service.GetByProductoIdAsync(p1.Id);

        Assert.Single(resultado);
        Assert.All(resultado, m => Assert.Equal(p1.Id, m.ProductoId));
    }

    [Fact]
    public async Task GetByProductoId_SinMovimientos_RetornaVacio()
    {
        var producto = await SeedProductoAsync();

        var resultado = await _service.GetByProductoIdAsync(producto.Id);

        Assert.Empty(resultado);
    }

    // =========================================================================
    // GetByTipoAsync
    // =========================================================================

    [Fact]
    public async Task GetByTipo_FiltraPorTipo()
    {
        var producto = await SeedProductoAsync();
        await _service.RegistrarAjusteAsync(producto.Id, TipoMovimiento.Entrada, 10m, null, "test", "user");
        await _service.RegistrarAjusteAsync(producto.Id, TipoMovimiento.Salida, 5m, null, "test", "user");

        var entradas = await _service.GetByTipoAsync(TipoMovimiento.Entrada);

        Assert.Single(entradas);
        Assert.All(entradas, m => Assert.Equal(TipoMovimiento.Entrada, m.Tipo));
    }

    // =========================================================================
    // GetByFechaRangoAsync
    // =========================================================================

    [Fact]
    public async Task GetByFechaRango_SinMovimientosEnRango_RetornaVacio()
    {
        var producto = await SeedProductoAsync();
        await _service.RegistrarAjusteAsync(producto.Id, TipoMovimiento.Entrada, 10m, null, "test", "user");

        var resultado = await _service.GetByFechaRangoAsync(
            DateTime.UtcNow.AddYears(-2),
            DateTime.UtcNow.AddYears(-1));

        Assert.Empty(resultado);
    }

    [Fact]
    public async Task GetByFechaRango_ConMovimientosEnRango_DevuelveResultados()
    {
        var producto = await SeedProductoAsync();
        await _service.RegistrarAjusteAsync(producto.Id, TipoMovimiento.Entrada, 10m, null, "test", "user");

        var resultado = await _service.GetByFechaRangoAsync(
            DateTime.UtcNow.AddHours(-1),
            DateTime.UtcNow.AddHours(1));

        Assert.Single(resultado);
    }

    // =========================================================================
    // SearchAsync
    // =========================================================================

    [Fact]
    public async Task Search_SinFiltros_DevuelveTodosLosMovimientos()
    {
        var p1 = await SeedProductoAsync();
        var p2 = await SeedProductoAsync();
        await _service.RegistrarAjusteAsync(p1.Id, TipoMovimiento.Entrada, 10m, null, "test", "user");
        await _service.RegistrarAjusteAsync(p2.Id, TipoMovimiento.Salida, 5m, null, "test", "user");

        var resultado = await _service.SearchAsync();

        Assert.Equal(2, resultado.Count());
    }

    [Fact]
    public async Task Search_PorTipo_FiltraCorrectamente()
    {
        var producto = await SeedProductoAsync();
        await _service.RegistrarAjusteAsync(producto.Id, TipoMovimiento.Entrada, 10m, null, "test", "user");
        await _service.RegistrarAjusteAsync(producto.Id, TipoMovimiento.Salida, 3m, null, "test", "user");

        var resultado = await _service.SearchAsync(tipo: TipoMovimiento.Entrada);

        Assert.Single(resultado);
        Assert.All(resultado, m => Assert.Equal(TipoMovimiento.Entrada, m.Tipo));
    }

    // =========================================================================
    // CreateAsync
    // =========================================================================

    [Fact]
    public async Task Create_PersisteBrutoSinModificarStock()
    {
        var producto = await SeedProductoAsync(stockActual: 50m);

        var movimiento = new MovimientoStock
        {
            ProductoId = producto.Id,
            Tipo = TipoMovimiento.Entrada,
            Cantidad = 20m,
            Motivo = "Recepción manual"
        };

        var resultado = await _service.CreateAsync(movimiento);

        Assert.True(resultado.Id > 0);
        // CreateAsync no modifica stock directamente (a diferencia de RegistrarAjusteAsync)
        var productoBd = await _context.Set<Producto>().FirstAsync(p => p.Id == producto.Id);
        Assert.Equal(50m, productoBd.StockActual); // sin cambio
    }

    [Fact]
    public async Task Create_RespetaCostoInformadoYCalculaTotal()
    {
        var producto = await SeedProductoAsync(stockActual: 50m, precioCompra: 10m);

        var movimiento = new MovimientoStock
        {
            ProductoId = producto.Id,
            Tipo = TipoMovimiento.Entrada,
            Cantidad = 3m,
            CostoUnitarioAlMomento = 22.22m,
            FuenteCosto = "OrdenCompraDetalle",
            Motivo = "Recepcion manual"
        };

        var resultado = await _service.CreateAsync(movimiento);

        Assert.Equal(22.22m, resultado.CostoUnitarioAlMomento);
        Assert.Equal(66.66m, resultado.CostoTotalAlMomento);
        Assert.Equal("OrdenCompraDetalle", resultado.FuenteCosto);

        var productoBd = await _context.Set<Producto>().FirstAsync(p => p.Id == producto.Id);
        Assert.Equal(50m, productoBd.StockActual);
    }

    // =========================================================================
    // GetByOrdenCompraIdAsync
    // =========================================================================

    [Fact]
    public async Task GetByOrdenCompra_SinMovimientos_RetornaVacio()
    {
        var proveedor = new Proveedor
        {
            Cuit = Guid.NewGuid().ToString("N")[..11],
            RazonSocial = "Prov Test",
            Activo = true
        };
        _context.Set<Proveedor>().Add(proveedor);
        var orden = new OrdenCompra
        {
            Numero = "OC-MOV-001",
            ProveedorId = 0, // se asigna abajo
            Estado = EstadoOrdenCompra.Borrador,
            FechaEmision = DateTime.UtcNow
        };
        _context.Set<Proveedor>().Add(proveedor);
        await _context.SaveChangesAsync();
        orden.ProveedorId = proveedor.Id;
        _context.Set<OrdenCompra>().Add(orden);
        await _context.SaveChangesAsync();

        var resultado = await _service.GetByOrdenCompraIdAsync(orden.Id);

        Assert.Empty(resultado);
    }

    [Fact]
    public async Task GetByOrdenCompra_ConMovimientos_RetornaSolosLosDeEsaOrden()
    {
        var producto = await SeedProductoAsync();
        var proveedor = new Proveedor
        {
            Cuit = Guid.NewGuid().ToString("N")[..11],
            RazonSocial = "Prov OC",
            Activo = true
        };
        _context.Set<Proveedor>().Add(proveedor);
        await _context.SaveChangesAsync();

        var orden = new OrdenCompra
        {
            Numero = "OC-MOV-002",
            ProveedorId = proveedor.Id,
            Estado = EstadoOrdenCompra.Borrador,
            FechaEmision = DateTime.UtcNow
        };
        _context.Set<OrdenCompra>().Add(orden);
        await _context.SaveChangesAsync();

        _context.Set<MovimientoStock>().Add(new MovimientoStock
        {
            ProductoId = producto.Id,
            OrdenCompraId = orden.Id,
            Tipo = TipoMovimiento.Entrada,
            Cantidad = 10m,
            Motivo = "Recepción"
        });
        _context.Set<MovimientoStock>().Add(new MovimientoStock
        {
            ProductoId = producto.Id,
            OrdenCompraId = null, // sin orden
            Tipo = TipoMovimiento.Entrada,
            Cantidad = 5m,
            Motivo = "Ajuste"
        });
        await _context.SaveChangesAsync();

        var resultado = await _service.GetByOrdenCompraIdAsync(orden.Id);

        Assert.Single(resultado);
        Assert.Equal(orden.Id, resultado.First().OrdenCompraId);
    }

    // -------------------------------------------------------------------------
    // GetByProductoIdAsync — Fase 8.5: cobertura adicional desde catálogo
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetByProductoId_ConMultiplesMovimientos_RetornaTodos()
    {
        var p1 = await SeedProductoAsync(stockActual: 100m);
        var p2 = await SeedProductoAsync(stockActual: 50m);

        _context.Set<MovimientoStock>().AddRange(
            new MovimientoStock { ProductoId = p1.Id, Tipo = TipoMovimiento.Entrada, Cantidad = 10m, Motivo = "Entrada p1" },
            new MovimientoStock { ProductoId = p1.Id, Tipo = TipoMovimiento.Salida, Cantidad = 5m, Motivo = "Salida p1" },
            new MovimientoStock { ProductoId = p2.Id, Tipo = TipoMovimiento.Entrada, Cantidad = 20m, Motivo = "Entrada p2" }
        );
        await _context.SaveChangesAsync();

        var resultado = await _service.GetByProductoIdAsync(p1.Id);

        Assert.Equal(2, resultado.Count());
        Assert.All(resultado, m => Assert.Equal(p1.Id, m.ProductoId));
    }

    [Fact]
    public async Task GetByProductoId_ExcluyeMovimientosEliminados()
    {
        var producto = await SeedProductoAsync(stockActual: 100m);

        _context.Set<MovimientoStock>().AddRange(
            new MovimientoStock { ProductoId = producto.Id, Tipo = TipoMovimiento.Entrada, Cantidad = 10m, Motivo = "Visible", IsDeleted = false },
            new MovimientoStock { ProductoId = producto.Id, Tipo = TipoMovimiento.Entrada, Cantidad = 5m, Motivo = "Eliminado", IsDeleted = true }
        );
        await _context.SaveChangesAsync();

        var resultado = await _service.GetByProductoIdAsync(producto.Id);

        Assert.Single(resultado);
        Assert.Equal("Visible", resultado.First().Motivo);
    }
}
