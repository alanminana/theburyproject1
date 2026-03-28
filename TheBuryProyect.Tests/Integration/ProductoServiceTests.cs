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
/// Tests de integración para ProductoService.
/// Cubren CreateAsync (código único, crea MovimientoStock si stock inicial > 0,
/// validaciones), UpdateAsync (RowVersion, código duplicado, registra historial
/// si precios cambian, no modifica StockActual), DeleteAsync (soft-delete),
/// ActualizarStockAsync (entrada/salida/cero/stock insuficiente),
/// ExistsCodigoAsync y GetProductosConStockBajoAsync.
/// </summary>
public class ProductoServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly ProductoService _service;

    public ProductoServiceTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        _service = new ProductoService(
            _context,
            NullLogger<ProductoService>.Instance,
            new StubPrecioHistoricoServiceProd(),
            new StubCurrentUserServiceProd());
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<(Categoria cat, Marca marca)> SeedCategoriaMarcaAsync()
    {
        var codigo = Guid.NewGuid().ToString("N")[..8];
        var cat = new Categoria { Codigo = codigo, Nombre = "Cat-" + codigo, Activo = true };
        var marca = new Marca { Codigo = codigo, Nombre = "Marca-" + codigo, Activo = true };
        _context.Categorias.Add(cat);
        _context.Marcas.Add(marca);
        await _context.SaveChangesAsync();
        return (cat, marca);
    }

    private async Task<Producto> SeedProductoAsync(
        decimal stockActual = 10m,
        decimal stockMinimo = 5m,
        decimal precioCompra = 10m,
        decimal precioVenta = 50m)
    {
        var (cat, marca) = await SeedCategoriaMarcaAsync();
        var codigo = Guid.NewGuid().ToString("N")[..8];
        var producto = new Producto
        {
            Codigo = codigo,
            Nombre = "Prod-" + codigo,
            CategoriaId = cat.Id,
            MarcaId = marca.Id,
            PrecioCompra = precioCompra,
            PrecioVenta = precioVenta,
            PorcentajeIVA = 21m,
            StockActual = stockActual,
            StockMinimo = stockMinimo,
            Activo = true
        };
        _context.Productos.Add(producto);
        await _context.SaveChangesAsync();
        await _context.Entry(producto).ReloadAsync();
        return producto;
    }

    private Producto BuildProducto(int categoriaId, int marcaId,
        string? codigo = null, decimal stockActual = 0m)
    {
        var cod = codigo ?? Guid.NewGuid().ToString("N")[..8];
        return new Producto
        {
            Codigo = cod,
            Nombre = "Prod-" + cod,
            CategoriaId = categoriaId,
            MarcaId = marcaId,
            PrecioCompra = 10m,
            PrecioVenta = 50m,
            PorcentajeIVA = 21m,
            StockActual = stockActual,
            StockMinimo = 5m,
            Activo = true
        };
    }

    // -------------------------------------------------------------------------
    // CreateAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Create_DatosValidos_Persiste()
    {
        var (cat, marca) = await SeedCategoriaMarcaAsync();
        var producto = BuildProducto(cat.Id, marca.Id);

        var resultado = await _service.CreateAsync(producto);

        Assert.True(resultado.Id > 0);
        var bd = await _context.Productos.FirstOrDefaultAsync(p => p.Id == resultado.Id);
        Assert.NotNull(bd);
    }

    [Fact]
    public async Task Create_ConStockInicial_CreaMovimientoStock()
    {
        var (cat, marca) = await SeedCategoriaMarcaAsync();
        var producto = BuildProducto(cat.Id, marca.Id, stockActual: 15m);

        await _service.CreateAsync(producto);

        var movimiento = await _context.MovimientosStock
            .FirstOrDefaultAsync(m => m.ProductoId == producto.Id);
        Assert.NotNull(movimiento);
        Assert.Equal(TipoMovimiento.Entrada, movimiento!.Tipo);
        Assert.Equal(15m, movimiento.Cantidad);
        Assert.Equal(0m, movimiento.StockAnterior);
        Assert.Equal(15m, movimiento.StockNuevo);
    }

    [Fact]
    public async Task Create_SinStockInicial_NoCreaMovimiento()
    {
        var (cat, marca) = await SeedCategoriaMarcaAsync();
        var producto = BuildProducto(cat.Id, marca.Id, stockActual: 0m);

        await _service.CreateAsync(producto);

        var movimientos = await _context.MovimientosStock
            .Where(m => m.ProductoId == producto.Id)
            .ToListAsync();
        Assert.Empty(movimientos);
    }

    [Fact]
    public async Task Create_CodigoDuplicado_LanzaExcepcion()
    {
        var existente = await SeedProductoAsync();
        var (cat, marca) = await SeedCategoriaMarcaAsync();
        var duplicado = BuildProducto(cat.Id, marca.Id, codigo: existente.Codigo);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CreateAsync(duplicado));
    }

    [Fact]
    public async Task Create_CodigoVacio_LanzaExcepcion()
    {
        var (cat, marca) = await SeedCategoriaMarcaAsync();
        var producto = BuildProducto(cat.Id, marca.Id);
        producto.Codigo = "  ";

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CreateAsync(producto));
    }

    [Fact]
    public async Task Create_StockNegativo_LanzaExcepcion()
    {
        var (cat, marca) = await SeedCategoriaMarcaAsync();
        var producto = BuildProducto(cat.Id, marca.Id);
        producto.StockActual = -1m;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CreateAsync(producto));
    }

    // -------------------------------------------------------------------------
    // UpdateAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Update_SinRowVersion_LanzaExcepcion()
    {
        var producto = await SeedProductoAsync();

        var update = new Producto
        {
            Id = producto.Id,
            Codigo = producto.Codigo,
            Nombre = "Modificado",
            CategoriaId = producto.CategoriaId,
            MarcaId = producto.MarcaId,
            PrecioCompra = producto.PrecioCompra,
            PrecioVenta = producto.PrecioVenta,
            StockActual = producto.StockActual,
            StockMinimo = producto.StockMinimo,
            Activo = true
            // RowVersion = null → debe lanzar
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.UpdateAsync(update));
    }

    [Fact]
    public async Task Update_CodigoDuplicadoDeOtroProducto_LanzaExcepcion()
    {
        var prod1 = await SeedProductoAsync();
        var prod2 = await SeedProductoAsync();

        var update = new Producto
        {
            Id = prod2.Id,
            Codigo = prod1.Codigo, // código de otro producto
            Nombre = prod2.Nombre,
            CategoriaId = prod2.CategoriaId,
            MarcaId = prod2.MarcaId,
            PrecioCompra = prod2.PrecioCompra,
            PrecioVenta = prod2.PrecioVenta,
            StockActual = prod2.StockActual,
            StockMinimo = prod2.StockMinimo,
            Activo = true,
            RowVersion = prod2.RowVersion
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.UpdateAsync(update));
    }

    [Fact]
    public async Task Update_NoModificaStockActualViaUpdate()
    {
        var producto = await SeedProductoAsync(stockActual: 10m);

        var update = new Producto
        {
            Id = producto.Id,
            Codigo = producto.Codigo,
            Nombre = producto.Nombre,
            CategoriaId = producto.CategoriaId,
            MarcaId = producto.MarcaId,
            PrecioCompra = producto.PrecioCompra,
            PrecioVenta = producto.PrecioVenta,
            StockActual = 999m, // intentamos cambiar el stock
            StockMinimo = producto.StockMinimo,
            Activo = true,
            RowVersion = producto.RowVersion
        };

        await _service.UpdateAsync(update);

        _context.ChangeTracker.Clear();
        var bd = await _context.Productos.FirstAsync(p => p.Id == producto.Id);
        Assert.Equal(10m, bd.StockActual); // no cambió
    }

    [Fact]
    public async Task Update_PreciosCambian_RegistraHistorico()
    {
        var producto = await SeedProductoAsync(precioCompra: 10m, precioVenta: 50m);

        var update = new Producto
        {
            Id = producto.Id,
            Codigo = producto.Codigo,
            Nombre = producto.Nombre,
            CategoriaId = producto.CategoriaId,
            MarcaId = producto.MarcaId,
            PrecioCompra = 15m, // cambió
            PrecioVenta = 60m,  // cambió
            StockActual = producto.StockActual,
            StockMinimo = producto.StockMinimo,
            Activo = true,
            RowVersion = producto.RowVersion
        };

        await _service.UpdateAsync(update);

        // El stub registra en la BD — verificamos que se llamó via el contador
        Assert.Equal(1, StubPrecioHistoricoServiceProd.LlamadasRegistrar);
    }

    [Fact]
    public async Task Update_PreciosSinCambio_NoRegistraHistorico()
    {
        StubPrecioHistoricoServiceProd.LlamadasRegistrar = 0;
        var producto = await SeedProductoAsync(precioCompra: 10m, precioVenta: 50m);

        var update = new Producto
        {
            Id = producto.Id,
            Codigo = producto.Codigo,
            Nombre = "Nombre Modificado",
            CategoriaId = producto.CategoriaId,
            MarcaId = producto.MarcaId,
            PrecioCompra = 10m, // igual
            PrecioVenta = 50m,  // igual
            StockActual = producto.StockActual,
            StockMinimo = producto.StockMinimo,
            Activo = true,
            RowVersion = producto.RowVersion
        };

        await _service.UpdateAsync(update);

        Assert.Equal(0, StubPrecioHistoricoServiceProd.LlamadasRegistrar);
    }

    // -------------------------------------------------------------------------
    // DeleteAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Delete_ProductoExistente_SoftDelete()
    {
        var producto = await SeedProductoAsync();

        var resultado = await _service.DeleteAsync(producto.Id);

        Assert.True(resultado);
        var bd = await _context.Productos
            .IgnoreQueryFilters()
            .FirstAsync(p => p.Id == producto.Id);
        Assert.True(bd.IsDeleted);
    }

    [Fact]
    public async Task Delete_ProductoNoExiste_RetornaFalse()
    {
        var resultado = await _service.DeleteAsync(99999);

        Assert.False(resultado);
    }

    // -------------------------------------------------------------------------
    // ActualizarStockAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ActualizarStock_EntradaPositiva_IncrementaStock()
    {
        var producto = await SeedProductoAsync(stockActual: 10m);

        var resultado = await _service.ActualizarStockAsync(producto.Id, 5m);

        Assert.Equal(15m, resultado.StockActual);

        var movimiento = await _context.MovimientosStock
            .Where(m => m.ProductoId == producto.Id)
            .OrderByDescending(m => m.Id)
            .FirstAsync();
        Assert.Equal(TipoMovimiento.Entrada, movimiento.Tipo);
        Assert.Equal(5m, movimiento.Cantidad);
        Assert.Equal(10m, movimiento.StockAnterior);
        Assert.Equal(15m, movimiento.StockNuevo);
    }

    [Fact]
    public async Task ActualizarStock_SalidaNegativa_ReduceStock()
    {
        var producto = await SeedProductoAsync(stockActual: 10m);

        var resultado = await _service.ActualizarStockAsync(producto.Id, -3m);

        Assert.Equal(7m, resultado.StockActual);

        var movimiento = await _context.MovimientosStock
            .Where(m => m.ProductoId == producto.Id)
            .OrderByDescending(m => m.Id)
            .FirstAsync();
        Assert.Equal(TipoMovimiento.Salida, movimiento.Tipo);
        Assert.Equal(3m, movimiento.Cantidad);
    }

    [Fact]
    public async Task ActualizarStock_CantidadCero_NoModifica()
    {
        var producto = await SeedProductoAsync(stockActual: 10m);

        var resultado = await _service.ActualizarStockAsync(producto.Id, 0m);

        Assert.Equal(10m, resultado.StockActual);
        var movimientos = await _context.MovimientosStock
            .Where(m => m.ProductoId == producto.Id)
            .ToListAsync();
        Assert.Empty(movimientos);
    }

    [Fact]
    public async Task ActualizarStock_StockInsuficiente_LanzaExcepcion()
    {
        var producto = await SeedProductoAsync(stockActual: 3m);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.ActualizarStockAsync(producto.Id, -10m));
    }

    [Fact]
    public async Task ActualizarStock_ProductoNoExiste_LanzaExcepcion()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.ActualizarStockAsync(99999, 5m));
    }

    // -------------------------------------------------------------------------
    // ExistsCodigoAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExistsCodigo_CodigoExistente_ReturnsTrue()
    {
        var producto = await SeedProductoAsync();

        var existe = await _service.ExistsCodigoAsync(producto.Codigo);

        Assert.True(existe);
    }

    [Fact]
    public async Task ExistsCodigo_CodigoNoExistente_ReturnsFalse()
    {
        var existe = await _service.ExistsCodigoAsync("CODIGO-NOEXISTE");

        Assert.False(existe);
    }

    [Fact]
    public async Task ExistsCodigo_ExcluyendoMismoId_ReturnsFalse()
    {
        var producto = await SeedProductoAsync();

        var existe = await _service.ExistsCodigoAsync(producto.Codigo, producto.Id);

        Assert.False(existe);
    }

    // -------------------------------------------------------------------------
    // GetProductosConStockBajoAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetProductosConStockBajo_ProductoConStockBajo_Incluido()
    {
        await SeedProductoAsync(stockActual: 2m, stockMinimo: 10m); // bajo

        var resultado = await _service.GetProductosConStockBajoAsync();

        Assert.Single(resultado);
    }

    [Fact]
    public async Task GetProductosConStockBajo_ProductoConStockSuficiente_NoIncluido()
    {
        await SeedProductoAsync(stockActual: 20m, stockMinimo: 10m); // suficiente

        var resultado = await _service.GetProductosConStockBajoAsync();

        Assert.Empty(resultado);
    }

    [Fact]
    public async Task GetProductosConStockBajo_StockExactoMinimo_Incluido()
    {
        // Stock == mínimo → se considera bajo (<=)
        await SeedProductoAsync(stockActual: 10m, stockMinimo: 10m);

        var resultado = await _service.GetProductosConStockBajoAsync();

        Assert.Single(resultado);
    }

    // =========================================================================
    // GetAllAsync
    // =========================================================================

    [Fact]
    public async Task GetAll_SinProductos_RetornaVacio()
    {
        var resultado = await _service.GetAllAsync();
        Assert.Empty(resultado);
    }

    [Fact]
    public async Task GetAll_ConProductos_DevuelveTodos()
    {
        await SeedProductoAsync();
        await SeedProductoAsync();

        var resultado = await _service.GetAllAsync();

        Assert.Equal(2, resultado.Count());
    }

    [Fact]
    public async Task GetAll_ExcluyeEliminados()
    {
        var producto = await SeedProductoAsync();
        await _service.DeleteAsync(producto.Id);

        var resultado = await _service.GetAllAsync();

        Assert.Empty(resultado);
    }

    // =========================================================================
    // GetByIdAsync
    // =========================================================================

    [Fact]
    public async Task GetById_ProductoExistente_RetornaProducto()
    {
        var producto = await SeedProductoAsync();

        var resultado = await _service.GetByIdAsync(producto.Id);

        Assert.NotNull(resultado);
        Assert.Equal(producto.Id, resultado!.Id);
        Assert.Equal(producto.Codigo, resultado.Codigo);
    }

    [Fact]
    public async Task GetById_ProductoInexistente_RetornaNull()
    {
        var resultado = await _service.GetByIdAsync(99999);
        Assert.Null(resultado);
    }

    // =========================================================================
    // GetByCategoriaAsync
    // =========================================================================

    [Fact]
    public async Task GetByCategoria_FiltraPorCategoria()
    {
        var p1 = await SeedProductoAsync(); // tiene su propia categoría
        var p2 = await SeedProductoAsync(); // categoría diferente

        var resultado = await _service.GetByCategoriaAsync(p1.CategoriaId);

        var lista = resultado.ToList();
        Assert.Single(lista);
        Assert.Equal(p1.Id, lista[0].Id);
    }

    [Fact]
    public async Task GetByCategoria_CategoriaInexistente_RetornaVacio()
    {
        var resultado = await _service.GetByCategoriaAsync(99999);
        Assert.Empty(resultado);
    }

    // =========================================================================
    // GetByMarcaAsync
    // =========================================================================

    [Fact]
    public async Task GetByMarca_FiltraPorMarca()
    {
        var p1 = await SeedProductoAsync();
        var p2 = await SeedProductoAsync();

        var resultado = await _service.GetByMarcaAsync(p1.MarcaId);

        var lista = resultado.ToList();
        Assert.Single(lista);
        Assert.Equal(p1.Id, lista[0].Id);
    }

    [Fact]
    public async Task GetByMarca_MarcaInexistente_RetornaVacio()
    {
        var resultado = await _service.GetByMarcaAsync(99999);
        Assert.Empty(resultado);
    }

    // =========================================================================
    // SearchAsync
    // =========================================================================

    [Fact]
    public async Task Search_SinFiltros_DevuelveTodosLosProductos()
    {
        await SeedProductoAsync();
        await SeedProductoAsync();

        var resultado = await _service.SearchAsync();

        Assert.Equal(2, resultado.Count());
    }

    [Fact]
    public async Task Search_PorNombre_FiltraCorrectamente()
    {
        var p1 = await SeedProductoAsync();
        await SeedProductoAsync();

        // SearchAsync filtra por Nombre.Contains(term)
        var resultado = await _service.SearchAsync(searchTerm: p1.Nombre);

        Assert.All(resultado, p => Assert.Contains(p1.Nombre, p.Nombre));
    }

    [Fact]
    public async Task Search_SoloActivos_ExcluyeInactivos()
    {
        var activo = await SeedProductoAsync();
        var inactivo = await SeedProductoAsync();
        // Desactivar el segundo
        inactivo.Activo = false;
        _context.Set<Producto>().Update(inactivo);
        await _context.SaveChangesAsync();

        var resultado = await _service.SearchAsync(soloActivos: true);

        Assert.All(resultado, p => Assert.True(p.Activo));
    }

    // =========================================================================
    // SearchIdsAsync
    // =========================================================================

    [Fact]
    public async Task SearchIds_SinFiltros_DevuelveIdsDeProductos()
    {
        var p1 = await SeedProductoAsync();
        var p2 = await SeedProductoAsync();

        var ids = await _service.SearchIdsAsync();

        Assert.Contains(p1.Id, ids);
        Assert.Contains(p2.Id, ids);
    }

    [Fact]
    public async Task SearchIds_PorCategoria_DevuelveSolosDeEsaCategoria()
    {
        var p1 = await SeedProductoAsync();
        await SeedProductoAsync();

        var ids = await _service.SearchIdsAsync(categoriaId: p1.CategoriaId);

        Assert.Single(ids);
        Assert.Equal(p1.Id, ids[0]);
    }
}

// ---------------------------------------------------------------------------
// Stubs
// ---------------------------------------------------------------------------
file sealed class StubCurrentUserServiceProd : ICurrentUserService
{
    public string GetUsername() => "testuser";
    public string GetUserId() => "user-test";
    public string GetEmail() => "test@test.com";
    public bool IsAuthenticated() => true;
    public bool IsInRole(string role) => false;
    public bool HasPermission(string modulo, string accion) => true;
    public string GetIpAddress() => "127.0.0.1";
}

file sealed class StubPrecioHistoricoServiceProd : IPrecioHistoricoService
{
    public static int LlamadasRegistrar { get; set; }

    public Task<PrecioHistorico> RegistrarCambioAsync(
        int productoId, decimal precioCompraAnterior, decimal precioCompraNuevo,
        decimal precioVentaAnterior, decimal precioVentaNuevo,
        string? motivoCambio, string usuarioModificacion)
    {
        LlamadasRegistrar++;
        return Task.FromResult(new PrecioHistorico
        {
            ProductoId = productoId,
            PrecioCompraAnterior = precioCompraAnterior,
            PrecioCompraNuevo = precioCompraNuevo,
            PrecioVentaAnterior = precioVentaAnterior,
            PrecioVentaNuevo = precioVentaNuevo
        });
    }

    public Task<List<PrecioHistorico>> GetHistorialByProductoIdAsync(int productoId)
        => Task.FromResult(new List<PrecioHistorico>());

    public Task<PrecioHistorico?> GetUltimoCambioAsync(int productoId)
        => Task.FromResult<PrecioHistorico?>(null);

    public Task<bool> RevertirCambioAsync(int historialId)
        => Task.FromResult(false);

    public Task<PrecioHistoricoEstadisticasViewModel> GetEstadisticasAsync(
        DateTime? fechaDesde, DateTime? fechaHasta)
        => Task.FromResult(new PrecioHistoricoEstadisticasViewModel());

    public Task<PaginatedResult<PrecioHistoricoViewModel>> BuscarAsync(
        PrecioHistoricoFiltroViewModel filtro)
        => Task.FromResult(new PaginatedResult<PrecioHistoricoViewModel>());

    public Task<PrecioSimulacionViewModel> SimularCambioAsync(
        int productoId, decimal precioCompraNuevo, decimal precioVentaNuevo)
        => Task.FromResult(new PrecioSimulacionViewModel());

    public Task MarcarComoNoReversibleAsync(int historialId)
        => Task.CompletedTask;
}
