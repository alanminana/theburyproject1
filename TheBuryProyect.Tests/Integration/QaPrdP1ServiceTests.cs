using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// QA-PRD-P1 (B3, B5, B6): regresiones de integridad a nivel servicio, sobre SQLite en memoria
/// (sin datos reales). Cada test aserta el comportamiento DESEADO: mientras el defecto siga abierto
/// el test FALLA y esa falla es la reproducción. Los tests "guarda" documentan lo que NO debe cambiar.
/// </summary>
public class QaPrdP1ServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly ProductoService _productos;
    private readonly PrecioService _precios;

    public QaPrdP1ServiceTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();
        _context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        _context.Database.EnsureCreated();

        var user = new StubUser();
        _productos = new ProductoService(
            _context, NullLogger<ProductoService>.Instance, new StubHistorico(), user, new PrecioVigenteResolver(_context));
        _precios = new PrecioService(_context, NullLogger<PrecioService>.Instance, user, new ConfigurationBuilder().Build());
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // ── B3: el código no se normaliza ────────────────────────────────────

    [Theory]
    [InlineData("  QA-B3-01")]
    [InlineData("QA-B3-01  ")]
    [InlineData("  QA-B3-01  ")]
    [InlineData("\tQA-B3-01\t")]
    public async Task B3_Create_CodigoConEspaciosDeBordeColisionaConElExistente(string codigoConEspacios)
    {
        var (cat, marca) = await SeedCategoriaMarcaAsync();
        await _productos.CreateAsync(NuevoProducto("QA-B3-01", cat.Id, marca.Id));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _productos.CreateAsync(NuevoProducto(codigoConEspacios, cat.Id, marca.Id)));

        Assert.Equal(1, await _context.Productos.CountAsync(p => p.Codigo.Trim() == "QA-B3-01"));
    }

    [Fact]
    public async Task B3_Create_CodigoNuevoConEspaciosSePersisteSinEllos()
    {
        var (cat, marca) = await SeedCategoriaMarcaAsync();

        var creado = await _productos.CreateAsync(NuevoProducto("  QA-B3-02  ", cat.Id, marca.Id));

        _context.ChangeTracker.Clear();
        Assert.Equal("QA-B3-02", (await _context.Productos.AsNoTracking().FirstAsync(p => p.Id == creado.Id)).Codigo);
    }

    [Fact]
    public async Task B3_ExistsCodigo_IgnoraEspaciosDeBorde()
    {
        var (cat, marca) = await SeedCategoriaMarcaAsync();
        await _productos.CreateAsync(NuevoProducto("QA-B3-03", cat.Id, marca.Id));

        Assert.True(await _productos.ExistsCodigoAsync("  QA-B3-03 "));
    }

    [Fact]
    public async Task B3_Update_NoPermiteRenombrarAUnCodigoOcupadoAgregandoEspacios()
    {
        var (cat, marca) = await SeedCategoriaMarcaAsync();
        await _productos.CreateAsync(NuevoProducto("QA-B3-04", cat.Id, marca.Id));
        var otro = await _productos.CreateAsync(NuevoProducto("QA-B3-05", cat.Id, marca.Id));

        _context.ChangeTracker.Clear();
        var edicion = await _context.Productos.AsNoTracking().FirstAsync(p => p.Id == otro.Id);
        edicion.Codigo = " QA-B3-04 ";

        await Assert.ThrowsAsync<InvalidOperationException>(() => _productos.UpdateAsync(edicion));
    }

    [Fact]
    public async Task B3_Guarda_CodigoExactoYDistintoCasoYaSeRechazaOSeAcepta_SinCambios()
    {
        // Guarda de no-regresión: el rechazo del código exacto ya funciona y no debe romperse al normalizar.
        var (cat, marca) = await SeedCategoriaMarcaAsync();
        await _productos.CreateAsync(NuevoProducto("QA-B3-06", cat.Id, marca.Id));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _productos.CreateAsync(NuevoProducto("QA-B3-06", cat.Id, marca.Id)));
    }

    // ── B5: filtros vacíos = todo el catálogo ────────────────────────────

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"busqueda\":\"   \"}")]
    [InlineData("{\"categoriaId\":0,\"marcaId\":0}")]
    [InlineData("{\"soloActivos\":false,\"stockBajo\":false}")]
    public async Task B5_Directo_FiltradosSinNingunFiltroEfectivo_NoModificaNada(string filtrosJson)
    {
        var ids = await SeedCatalogoAsync(cantidad: 3);

        var resultado = await _precios.AplicarCambioPrecioDirectoAsync(new AplicarCambioPrecioDirectoViewModel
        {
            Alcance = "filtrados",
            FiltrosJson = filtrosJson,
            ValorPorcentaje = 10m,
            Motivo = "QA-PRD-P1"
        });

        Assert.False(resultado.Exitoso, "Un ajuste 'filtrados' sin filtro efectivo debe exigir alcance explícito.");
        Assert.All(await _context.Productos.AsNoTracking().Where(p => ids.Contains(p.Id)).ToListAsync(),
            p => Assert.Equal(100m, p.PrecioVenta));
        Assert.Equal(0, await _context.CambioPrecioEventos.CountAsync());
    }

    [Fact]
    public async Task B5_Guarda_FiltradoConCategoria_AplicaSoloALosDeEsaCategoria()
    {
        var (catA, marca) = await SeedCategoriaMarcaAsync();
        var (catB, _) = await SeedCategoriaMarcaAsync();
        var a = await SeedProductoAsync(catA.Id, marca.Id);
        var b = await SeedProductoAsync(catB.Id, marca.Id);

        var resultado = await _precios.AplicarCambioPrecioDirectoAsync(new AplicarCambioPrecioDirectoViewModel
        {
            Alcance = "filtrados",
            FiltrosJson = $"{{\"categoriaId\":{catA.Id}}}",
            ValorPorcentaje = 10m,
            Motivo = "QA-PRD-P1"
        });

        Assert.True(resultado.Exitoso, resultado.Mensaje);
        _context.ChangeTracker.Clear();
        Assert.Equal(110m, (await _context.Productos.FindAsync(a.Id))!.PrecioVenta);
        Assert.Equal(100m, (await _context.Productos.FindAsync(b.Id))!.PrecioVenta);
    }

    [Fact]
    public async Task B5_Guarda_SeleccionadosSigueFuncionando()
    {
        var ids = await SeedCatalogoAsync(cantidad: 2);

        var resultado = await _precios.AplicarCambioPrecioDirectoAsync(new AplicarCambioPrecioDirectoViewModel
        {
            Alcance = "seleccionados",
            ProductoIdsText = ids[0].ToString(),
            ValorPorcentaje = 10m,
            Motivo = "QA-PRD-P1"
        });

        Assert.True(resultado.Exitoso, resultado.Mensaje);
        _context.ChangeTracker.Clear();
        Assert.Equal(110m, (await _context.Productos.FindAsync(ids[0]))!.PrecioVenta);
        Assert.Equal(100m, (await _context.Productos.FindAsync(ids[1]))!.PrecioVenta);
    }

    // ── B6: la reversión pisa cambios posteriores ────────────────────────

    [Fact]
    public async Task B6_RevertirEventoViejo_NoPisaElPrecioDeUnCambioPosterior()
    {
        var p = (await SeedCatalogoAsync(1))[0];
        var e1 = await AplicarDirectoAsync(p, 10m);   // 100 -> 110
        await AplicarDirectoAsync(p, 10m);            // 110 -> 121 (cambio posterior)

        await _precios.RevertirCambioPrecioEventoAsync(e1);

        _context.ChangeTracker.Clear();
        Assert.Equal(121m, (await _context.Productos.FindAsync(p))!.PrecioVenta);
    }

    [Fact]
    public async Task B6_RevertirEventoViejo_NoMarcaElEventoComoRevertidoSiNoRevirtioNada()
    {
        var p = (await SeedCatalogoAsync(1))[0];
        var e1 = await AplicarDirectoAsync(p, 10m);
        await AplicarDirectoAsync(p, 10m);

        var (exitoso, _, reversionId) = await _precios.RevertirCambioPrecioEventoAsync(e1);

        _context.ChangeTracker.Clear();
        var evento = await _context.CambioPrecioEventos.AsNoTracking().FirstAsync(e => e.Id == e1);
        Assert.False(exitoso && reversionId.HasValue && evento.RevertidoEn.HasValue,
            "Una reversión que no pudo aplicarse a ningún producto no debe dejar el evento como revertido.");
    }

    [Fact]
    public async Task B6_Guarda_RevertirElUltimoEventoRestauraElPrecioAnterior()
    {
        var p = (await SeedCatalogoAsync(1))[0];
        var e1 = await AplicarDirectoAsync(p, 10m);

        var (exitoso, mensaje, _) = await _precios.RevertirCambioPrecioEventoAsync(e1);

        Assert.True(exitoso, mensaje);
        _context.ChangeTracker.Clear();
        Assert.Equal(100m, (await _context.Productos.FindAsync(p))!.PrecioVenta);
    }

    [Fact]
    public async Task B6_Guarda_NoSePuedeRevertirDosVecesElMismoEvento()
    {
        var p = (await SeedCatalogoAsync(1))[0];
        var e1 = await AplicarDirectoAsync(p, 10m);
        await _precios.RevertirCambioPrecioEventoAsync(e1);

        var (exitoso, _, _) = await _precios.RevertirCambioPrecioEventoAsync(e1);

        Assert.False(exitoso);
    }

    // ── B8: venta bajo costo en el ajuste directo ────────────────────────

    [Fact]
    public async Task B8_Directo_BajarPorDebajoDelCosto_SeAceptaPeroInformaAdvertencia()
    {
        // Política vigente: NO se bloquea (el batch y la simulación solo advierten). Se fija que el
        // ajuste directo también advierta, sin impedir el cambio.
        var p = (await SeedCatalogoAsync(1, precioCompra: 80m, precioVenta: 100m))[0];

        var resultado = await _precios.AplicarCambioPrecioDirectoAsync(new AplicarCambioPrecioDirectoViewModel
        {
            Alcance = "seleccionados",
            ProductoIdsText = p.ToString(),
            ValorPorcentaje = -30m,   // 100 -> 70 < costo 80
            Motivo = "QA-PRD-P1"
        });

        Assert.True(resultado.Exitoso, resultado.Mensaje);
        _context.ChangeTracker.Clear();
        Assert.Equal(70m, (await _context.Productos.FindAsync(p))!.PrecioVenta);
        Assert.Contains("costo", resultado.Mensaje ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private async Task<int> AplicarDirectoAsync(int productoId, decimal porcentaje)
    {
        var r = await _precios.AplicarCambioPrecioDirectoAsync(new AplicarCambioPrecioDirectoViewModel
        {
            Alcance = "seleccionados",
            ProductoIdsText = productoId.ToString(),
            ValorPorcentaje = porcentaje,
            Motivo = "QA-PRD-P1"
        });
        Assert.True(r.Exitoso, r.Mensaje);
        _context.ChangeTracker.Clear();
        return await _context.CambioPrecioEventos.AsNoTracking()
            .OrderByDescending(e => e.Id).Select(e => e.Id).FirstAsync();
    }

    private async Task<List<int>> SeedCatalogoAsync(int cantidad, decimal precioCompra = 50m, decimal precioVenta = 100m)
    {
        var (cat, marca) = await SeedCategoriaMarcaAsync();
        var ids = new List<int>();
        for (var i = 0; i < cantidad; i++)
            ids.Add((await SeedProductoAsync(cat.Id, marca.Id, precioCompra, precioVenta)).Id);
        return ids;
    }

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

    private async Task<Producto> SeedProductoAsync(int catId, int marcaId, decimal compra = 50m, decimal venta = 100m)
    {
        var producto = NuevoProducto(Guid.NewGuid().ToString("N")[..8], catId, marcaId);
        producto.PrecioCompra = compra;
        producto.PrecioVenta = venta;
        _context.Productos.Add(producto);
        await _context.SaveChangesAsync();
        await _context.Entry(producto).ReloadAsync();
        return producto;
    }

    private static Producto NuevoProducto(string codigo, int catId, int marcaId) => new()
    {
        Codigo = codigo,
        Nombre = "Prod-" + codigo.Trim(),
        CategoriaId = catId,
        MarcaId = marcaId,
        PrecioCompra = 50m,
        PrecioVenta = 100m,
        PorcentajeIVA = 21m,
        StockActual = 0m,
        StockMinimo = 0m,
        Activo = true
    };

    private sealed class StubUser : ICurrentUserService
    {
        public string GetUsername() => "qa-prd-p1";
        public string GetUserId() => "qa-prd-p1-id";
        public string GetEmail() => "qa@test.local";
        public bool IsAuthenticated() => true;
        public bool IsInRole(string role) => false;
        public bool HasPermission(string modulo, string accion) => true;
        public string GetIpAddress() => "127.0.0.1";
    }

    private sealed class StubHistorico : IPrecioHistoricoService
    {
        public Task<PrecioHistorico> RegistrarCambioAsync(
            int productoId, decimal precioCompraAnterior, decimal precioCompraNuevo,
            decimal precioVentaAnterior, decimal precioVentaNuevo,
            string? motivoCambio, string usuarioModificacion) =>
            Task.FromResult(new PrecioHistorico { ProductoId = productoId });

        public Task<List<PrecioHistorico>> GetHistorialByProductoIdAsync(int productoId)
            => Task.FromResult(new List<PrecioHistorico>());

        public Task<PrecioHistorico?> GetUltimoCambioAsync(int productoId)
            => Task.FromResult<PrecioHistorico?>(null);

        public Task<bool> RevertirCambioAsync(int historialId) => Task.FromResult(false);

        public Task<PrecioHistoricoEstadisticasViewModel> GetEstadisticasAsync(DateTime? fechaDesde, DateTime? fechaHasta)
            => Task.FromResult(new PrecioHistoricoEstadisticasViewModel());

        public Task<PaginatedResult<PrecioHistoricoViewModel>> BuscarAsync(PrecioHistoricoFiltroViewModel filtro)
            => Task.FromResult(new PaginatedResult<PrecioHistoricoViewModel>());

        public Task<PrecioSimulacionViewModel> SimularCambioAsync(int productoId, decimal precioCompraNuevo, decimal precioVentaNuevo)
            => Task.FromResult(new PrecioSimulacionViewModel());

        public Task MarcarComoNoReversibleAsync(int historialId) => Task.CompletedTask;
    }
}
