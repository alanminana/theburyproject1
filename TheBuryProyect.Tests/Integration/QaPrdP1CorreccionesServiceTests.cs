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
/// QA-PRD-P1: cobertura adicional de las correcciones (más allá de las 31 regresiones del diagnóstico):
/// caminos de aceptación, bordes y no-regresión de B3, B5, B6, B7 y B8. SQLite en memoria, sin datos reales.
/// </summary>
public class QaPrdP1CorreccionesServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly ProductoService _productos;
    private readonly PrecioService _precios;

    public QaPrdP1CorreccionesServiceTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();
        _context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        _context.Database.EnsureCreated();

        var user = new StubUser();
        _productos = new ProductoService(
            _context, NullLogger<ProductoService>.Instance, new HistoricoReal(_context), user, new PrecioVigenteResolver(_context));
        _precios = new PrecioService(_context, NullLogger<PrecioService>.Instance, user, new ConfigurationBuilder().Build());
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // ── B3 ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task B3_Create_NoModificaEspaciosInternosNiMayusculas()
    {
        var (cat, marca) = await SeedCategoriaMarcaAsync();

        var creado = await _productos.CreateAsync(NuevoProducto("  ab  12-x ", cat.Id, marca.Id));

        _context.ChangeTracker.Clear();
        Assert.Equal("ab  12-x", (await _context.Productos.AsNoTracking().FirstAsync(p => p.Id == creado.Id)).Codigo);
    }

    [Fact]
    public async Task B3_Create_EspaciosInternosDistintosSiguenSiendoCodigosDistintos()
    {
        // Regla comercial sin confirmar: solo se recortan los bordes. "AB 12" y "AB  12" no colisionan.
        var (cat, marca) = await SeedCategoriaMarcaAsync();
        await _productos.CreateAsync(NuevoProducto("AB 12", cat.Id, marca.Id));

        var otro = await _productos.CreateAsync(NuevoProducto("AB  12", cat.Id, marca.Id));

        Assert.True(otro.Id > 0);
    }

    [Fact]
    public async Task B3_Create_CodigoSoloEspaciosSeRechaza()
    {
        var (cat, marca) = await SeedCategoriaMarcaAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _productos.CreateAsync(NuevoProducto("   ", cat.Id, marca.Id)));
    }

    [Fact]
    public async Task B3_ProductoPreexistenteConEspaciosColisionaConElCodigoLimpio()
    {
        // Dato legado: un producto ya guardado con espacios de borde (insertado sin pasar por el servicio).
        var (cat, marca) = await SeedCategoriaMarcaAsync();
        var legado = NuevoProducto("  LEG-01  ", cat.Id, marca.Id);
        _context.Productos.Add(legado);
        await _context.SaveChangesAsync();

        Assert.True(await _productos.ExistsCodigoAsync("LEG-01"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _productos.CreateAsync(NuevoProducto("LEG-01", cat.Id, marca.Id)));

        // No se borró ni se fusionó nada: el producto legado sigue intacto, con su código original.
        _context.ChangeTracker.Clear();
        Assert.Equal("  LEG-01  ", (await _context.Productos.AsNoTracking().FirstAsync(p => p.Id == legado.Id)).Codigo);
        Assert.Equal(1, await _context.Productos.CountAsync());
    }

    [Fact]
    public async Task B3_Update_ReeditarUnProductoLegadoConEspaciosLoNormalizaSinColisionarConsigoMismo()
    {
        var (cat, marca) = await SeedCategoriaMarcaAsync();
        var legado = NuevoProducto(" LEG-02 ", cat.Id, marca.Id);
        _context.Productos.Add(legado);
        await _context.SaveChangesAsync();

        _context.ChangeTracker.Clear();
        var edicion = await _context.Productos.AsNoTracking().FirstAsync(p => p.Id == legado.Id);
        edicion.Nombre = "Renombrado";
        await _productos.UpdateAsync(edicion);

        _context.ChangeTracker.Clear();
        Assert.Equal("LEG-02", (await _context.Productos.AsNoTracking().FirstAsync(p => p.Id == legado.Id)).Codigo);
    }

    // ── B5 ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task B5_TodoElCatalogoConConfirmacionYCantidadExacta_SeAplica()
    {
        var ids = await SeedCatalogoAsync(3);

        var r = await _precios.AplicarCambioPrecioDirectoAsync(new AplicarCambioPrecioDirectoViewModel
        {
            Alcance = "filtrados", FiltrosJson = "{}", ValorPorcentaje = 10m, Motivo = "QA-PRD-P1",
            ConfirmarTodoElCatalogo = true, CantidadEsperada = 3
        });

        Assert.True(r.Exitoso, r.Mensaje);
        Assert.Equal(3, r.ProductosActualizados);
        Assert.Equal(3, r.ProductosEnAlcance);
        _context.ChangeTracker.Clear();
        Assert.All(await _context.Productos.AsNoTracking().Where(p => ids.Contains(p.Id)).ToListAsync(),
            p => Assert.Equal(110m, p.PrecioVenta));
    }

    [Theory]
    [InlineData(true, 2)]    // cantidad que no coincide
    [InlineData(true, null)] // sin cantidad
    [InlineData(false, 3)]   // sin confirmación
    public async Task B5_TodoElCatalogo_SinConfirmacionOCantidadExacta_NoModificaNada(bool confirmar, int? esperada)
    {
        var ids = await SeedCatalogoAsync(3);

        var r = await _precios.AplicarCambioPrecioDirectoAsync(new AplicarCambioPrecioDirectoViewModel
        {
            Alcance = "filtrados", FiltrosJson = "{}", ValorPorcentaje = 10m, Motivo = "QA-PRD-P1",
            ConfirmarTodoElCatalogo = confirmar, CantidadEsperada = esperada
        });

        Assert.False(r.Exitoso);
        Assert.Equal(3, r.ProductosEnAlcance);
        Assert.Equal(0, r.ProductosActualizados);
        _context.ChangeTracker.Clear();
        Assert.All(await _context.Productos.AsNoTracking().Where(p => ids.Contains(p.Id)).ToListAsync(),
            p => Assert.Equal(100m, p.PrecioVenta));
        Assert.Equal(0, await _context.CambioPrecioEventos.CountAsync());
        Assert.Equal(0, await _context.PreciosHistoricos.CountAsync());
    }

    [Fact]
    public async Task B5_CantidadEsperadaQueNoCoincide_EnSeleccionados_RechazaSinTocarPrecios()
    {
        var ids = await SeedCatalogoAsync(2);

        var r = await _precios.AplicarCambioPrecioDirectoAsync(new AplicarCambioPrecioDirectoViewModel
        {
            Alcance = "seleccionados", ProductoIdsText = string.Join(",", ids), ValorPorcentaje = 10m,
            Motivo = "QA-PRD-P1", CantidadEsperada = 5
        });

        Assert.False(r.Exitoso);
        _context.ChangeTracker.Clear();
        Assert.All(await _context.Productos.AsNoTracking().Where(p => ids.Contains(p.Id)).ToListAsync(),
            p => Assert.Equal(100m, p.PrecioVenta));
        Assert.Equal(0, await _context.CambioPrecioEventos.CountAsync());
    }

    [Fact]
    public async Task B5_FiltradoConCategoriaYCantidadEsperadaCorrecta_SeAplicaYInformaCantidades()
    {
        var (catA, marca) = await SeedCategoriaMarcaAsync();
        var (catB, _) = await SeedCategoriaMarcaAsync();
        await SeedProductoAsync(catA.Id, marca.Id);
        await SeedProductoAsync(catA.Id, marca.Id);
        var fuera = await SeedProductoAsync(catB.Id, marca.Id);

        var r = await _precios.AplicarCambioPrecioDirectoAsync(new AplicarCambioPrecioDirectoViewModel
        {
            Alcance = "filtrados", FiltrosJson = $"{{\"categoriaId\":{catA.Id}}}", ValorPorcentaje = 10m,
            Motivo = "QA-PRD-P1", CantidadEsperada = 2
        });

        Assert.True(r.Exitoso, r.Mensaje);
        Assert.Equal(2, r.ProductosActualizados);
        Assert.Equal(2, r.ProductosEnAlcance);
        _context.ChangeTracker.Clear();
        Assert.Equal(100m, (await _context.Productos.FindAsync(fuera.Id))!.PrecioVenta);
    }

    [Fact]
    public async Task B5_SoloActivosPorSiSoloNoAcotaElAlcance()
    {
        await SeedCatalogoAsync(2);

        var r = await _precios.AplicarCambioPrecioDirectoAsync(new AplicarCambioPrecioDirectoViewModel
        {
            Alcance = "filtrados", FiltrosJson = "{\"soloActivos\":true}", ValorPorcentaje = 10m, Motivo = "QA-PRD-P1"
        });

        Assert.False(r.Exitoso);
        Assert.Equal(0, await _context.CambioPrecioEventos.CountAsync());
    }

    // ── B6 ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task B6_EventoConUnProductoEnConflicto_BloqueaTodoElEventoYNoTocaNingunPrecio()
    {
        var ids = await SeedCatalogoAsync(2);
        var e1 = await AplicarDirectoAsync(string.Join(",", ids), 10m);   // ambos 100 -> 110
        await AplicarDirectoAsync(ids[0].ToString(), 10m);                 // solo el primero 110 -> 121

        var (exitoso, mensaje, reversionId) = await _precios.RevertirCambioPrecioEventoAsync(e1);

        Assert.False(exitoso);
        Assert.Null(reversionId);
        Assert.Contains("cambiaron de precio", mensaje);
        _context.ChangeTracker.Clear();
        Assert.Equal(121m, (await _context.Productos.FindAsync(ids[0]))!.PrecioVenta);
        Assert.Equal(110m, (await _context.Productos.FindAsync(ids[1]))!.PrecioVenta); // el no conflictivo tampoco se revirtió
        Assert.Null((await _context.CambioPrecioEventos.AsNoTracking().FirstAsync(e => e.Id == e1)).RevertidoEn);
        Assert.Equal(2, await _context.CambioPrecioEventos.CountAsync()); // sin evento de reversión
    }

    [Fact]
    public async Task B6_ElEventoSigueSiendoRevertibleCuandoElConflictoSeResuelve()
    {
        var p = (await SeedCatalogoAsync(1))[0];
        var e1 = await AplicarDirectoAsync(p.ToString(), 10m);            // 100 -> 110
        var e2 = await AplicarDirectoAsync(p.ToString(), 10m);            // 110 -> 121
        Assert.False((await _precios.RevertirCambioPrecioEventoAsync(e1)).Exitoso);

        // Revertir primero el posterior (orden correcto) deja al anterior otra vez revertible.
        Assert.True((await _precios.RevertirCambioPrecioEventoAsync(e2)).Exitoso);
        var (ok, mensaje, _) = await _precios.RevertirCambioPrecioEventoAsync(e1);

        Assert.True(ok, mensaje);
        _context.ChangeTracker.Clear();
        Assert.Equal(100m, (await _context.Productos.FindAsync(p))!.PrecioVenta);
    }

    // ── B7 ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task B7_UpdateAsync_CambioDePrecioDejaEventoIndividualYSePuedeRevertir()
    {
        var (cat, marca) = await SeedCategoriaMarcaAsync();
        var creado = await _productos.CreateAsync(NuevoProducto("B7-01", cat.Id, marca.Id));

        _context.ChangeTracker.Clear();
        var edicion = await _context.Productos.AsNoTracking().FirstAsync(p => p.Id == creado.Id);
        edicion.PrecioVenta = 150m;
        await _productos.UpdateAsync(edicion);

        _context.ChangeTracker.Clear();
        var detalle = await _context.CambioPrecioDetalles.AsNoTracking().Include(d => d.Evento).SingleAsync(d => d.ProductoId == creado.Id);
        Assert.Equal(100m, detalle.PrecioAnterior);
        Assert.Equal(150m, detalle.PrecioNuevo);
        Assert.Equal("individual", detalle.Evento.Alcance);
        Assert.Equal(50m, detalle.Evento.ValorPorcentaje);
        Assert.Equal(1, detalle.Evento.CantidadProductos);
        // El historial "viejo" sigue registrándose (no se reemplaza ni se duplica el evento económico).
        Assert.Equal(1, await _context.PreciosHistoricos.CountAsync(h => h.ProductoId == creado.Id));

        var (ok, mensaje, _) = await _precios.RevertirCambioPrecioEventoAsync(detalle.EventoId);
        Assert.True(ok, mensaje);
        _context.ChangeTracker.Clear();
        Assert.Equal(100m, (await _context.Productos.FindAsync(creado.Id))!.PrecioVenta);
    }

    [Fact]
    public async Task B7_UpdateAsync_SoloCambiaElCosto_NoCreaEventoDePrecioDeVenta()
    {
        var (cat, marca) = await SeedCategoriaMarcaAsync();
        var creado = await _productos.CreateAsync(NuevoProducto("B7-02", cat.Id, marca.Id));

        _context.ChangeTracker.Clear();
        var edicion = await _context.Productos.AsNoTracking().FirstAsync(p => p.Id == creado.Id);
        edicion.PrecioCompra = 60m;
        await _productos.UpdateAsync(edicion);

        Assert.Equal(0, await _context.CambioPrecioEventos.CountAsync());
        Assert.Equal(1, await _context.PreciosHistoricos.CountAsync(h => h.ProductoId == creado.Id));
    }

    // ── B8 ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task B8_Directo_PrecioQuedaPorEncimaDelCosto_NoAdvierte()
    {
        var p = (await SeedCatalogoAsync(1, precioCompra: 50m, precioVenta: 100m))[0];

        var r = await _precios.AplicarCambioPrecioDirectoAsync(new AplicarCambioPrecioDirectoViewModel
        {
            Alcance = "seleccionados", ProductoIdsText = p.ToString(), ValorPorcentaje = -10m, Motivo = "QA-PRD-P1"
        });

        Assert.True(r.Exitoso, r.Mensaje);
        Assert.Empty(r.Advertencias);
        Assert.DoesNotContain("costo", r.Mensaje, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task B8_Directo_BajoCosto_AdvertenciaEstructuradaYElCambioSeAplica()
    {
        var p = (await SeedCatalogoAsync(1, precioCompra: 80m, precioVenta: 100m))[0];

        var r = await _precios.AplicarCambioPrecioDirectoAsync(new AplicarCambioPrecioDirectoViewModel
        {
            Alcance = "seleccionados", ProductoIdsText = p.ToString(), ValorPorcentaje = -30m, Motivo = "QA-PRD-P1"
        });

        Assert.True(r.Exitoso, r.Mensaje);
        var aviso = Assert.Single(r.Advertencias);
        Assert.Contains("por debajo del costo", aviso);
        _context.ChangeTracker.Clear();
        Assert.Equal(70m, (await _context.Productos.FindAsync(p))!.PrecioVenta);
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private async Task<int> AplicarDirectoAsync(string idsCsv, decimal porcentaje)
    {
        var r = await _precios.AplicarCambioPrecioDirectoAsync(new AplicarCambioPrecioDirectoViewModel
        {
            Alcance = "seleccionados", ProductoIdsText = idsCsv, ValorPorcentaje = porcentaje, Motivo = "QA-PRD-P1"
        });
        Assert.True(r.Exitoso, r.Mensaje);
        _context.ChangeTracker.Clear();
        return await _context.CambioPrecioEventos.AsNoTracking().OrderByDescending(e => e.Id).Select(e => e.Id).FirstAsync();
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

    /// <summary>Historial real (PrecioHistoricoService) para verificar que B7 no lo reemplaza ni lo duplica.</summary>
    private sealed class HistoricoReal : IPrecioHistoricoService
    {
        private readonly PrecioHistoricoService _inner;
        public HistoricoReal(AppDbContext context) =>
            _inner = new PrecioHistoricoService(context, NullLogger<PrecioHistoricoService>.Instance);

        public Task<PrecioHistorico> RegistrarCambioAsync(int productoId, decimal precioCompraAnterior, decimal precioCompraNuevo,
            decimal precioVentaAnterior, decimal precioVentaNuevo, string? motivoCambio, string usuarioModificacion) =>
            _inner.RegistrarCambioAsync(productoId, precioCompraAnterior, precioCompraNuevo, precioVentaAnterior, precioVentaNuevo, motivoCambio, usuarioModificacion);
        public Task<List<PrecioHistorico>> GetHistorialByProductoIdAsync(int productoId) => _inner.GetHistorialByProductoIdAsync(productoId);
        public Task<PrecioHistorico?> GetUltimoCambioAsync(int productoId) => _inner.GetUltimoCambioAsync(productoId);
        public Task<bool> RevertirCambioAsync(int historialId) => _inner.RevertirCambioAsync(historialId);
        public Task<PrecioHistoricoEstadisticasViewModel> GetEstadisticasAsync(DateTime? fechaDesde, DateTime? fechaHasta) => _inner.GetEstadisticasAsync(fechaDesde, fechaHasta);
        public Task<PaginatedResult<PrecioHistoricoViewModel>> BuscarAsync(PrecioHistoricoFiltroViewModel filtro) => _inner.BuscarAsync(filtro);
        public Task<PrecioSimulacionViewModel> SimularCambioAsync(int productoId, decimal precioCompraNuevo, decimal precioVentaNuevo) => _inner.SimularCambioAsync(productoId, precioCompraNuevo, precioVentaNuevo);
        public Task MarcarComoNoReversibleAsync(int historialId) => _inner.MarcarComoNoReversibleAsync(historialId);
    }
}
