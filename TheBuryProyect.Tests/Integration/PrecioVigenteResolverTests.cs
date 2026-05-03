using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Tests.Integration;

public class PrecioVigenteResolverTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly PrecioVigenteResolver _resolver;

    public PrecioVigenteResolverTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();
        _resolver = new PrecioVigenteResolver(_context);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task ResolverAsync_ConListaExplicitaYPrecioVigente_UsaPrecioLista()
    {
        var producto = await SeedProductoAsync(precioCompra: 60m, precioVenta: 100m);
        var lista = await SeedListaAsync(esPredeterminada: false);
        var precioLista = await SeedPrecioListaAsync(producto.Id, lista.Id, precio: 150m, costo: 60m);

        var resultado = await _resolver.ResolverAsync(producto.Id, lista.Id);

        Assert.NotNull(resultado);
        Assert.Equal(producto.Id, resultado!.ProductoId);
        Assert.Equal(150m, resultado.PrecioFinalConIva);
        Assert.Equal(FuentePrecioVigente.ProductoPrecioLista, resultado.FuentePrecio);
        Assert.Equal(lista.Id, resultado.ListaId);
        Assert.Equal(precioLista.Id, resultado.ProductoPrecioListaId);
        Assert.Equal(100m, resultado.PrecioBaseProducto);
        Assert.Equal(60m, resultado.CostoSnapshot);
        Assert.False(resultado.EsFallbackProductoBase);
    }

    [Fact]
    public async Task ResolverAsync_SinListaExplicita_UsaListaPredeterminadaActiva()
    {
        var producto = await SeedProductoAsync(precioVenta: 100m);
        var lista = await SeedListaAsync(esPredeterminada: true);
        var precioLista = await SeedPrecioListaAsync(producto.Id, lista.Id, precio: 175m, costo: 60m);

        var resultado = await _resolver.ResolverAsync(producto.Id);

        Assert.NotNull(resultado);
        Assert.Equal(175m, resultado!.PrecioFinalConIva);
        Assert.Equal(FuentePrecioVigente.ProductoPrecioLista, resultado.FuentePrecio);
        Assert.Equal(lista.Id, resultado.ListaId);
        Assert.Equal(precioLista.Id, resultado.ProductoPrecioListaId);
        Assert.False(resultado.EsFallbackProductoBase);
    }

    [Fact]
    public async Task ResolverAsync_SinPrecioVigente_CaeAPrecioVentaProducto()
    {
        var producto = await SeedProductoAsync(precioCompra: 60m, precioVenta: 100m);
        var lista = await SeedListaAsync(esPredeterminada: true);

        var resultado = await _resolver.ResolverAsync(producto.Id);

        Assert.NotNull(resultado);
        Assert.Equal(100m, resultado!.PrecioFinalConIva);
        Assert.Equal(FuentePrecioVigente.ProductoPrecioBase, resultado.FuentePrecio);
        Assert.Equal(lista.Id, resultado.ListaId);
        Assert.Null(resultado.ProductoPrecioListaId);
        Assert.Equal(100m, resultado.PrecioBaseProducto);
        Assert.Equal(60m, resultado.CostoSnapshot);
        Assert.True(resultado.EsFallbackProductoBase);
    }

    [Fact]
    public async Task ResolverAsync_IgnoraPreciosNoVigentes()
    {
        var producto = await SeedProductoAsync(precioVenta: 100m);
        var lista = await SeedListaAsync(esPredeterminada: true);
        await SeedPrecioListaAsync(
            producto.Id,
            lista.Id,
            precio: 150m,
            costo: 60m,
            vigenciaDesde: DateTime.UtcNow.AddDays(-10),
            vigenciaHasta: DateTime.UtcNow.AddDays(-1));

        var resultado = await _resolver.ResolverAsync(producto.Id);

        Assert.NotNull(resultado);
        Assert.Equal(100m, resultado!.PrecioFinalConIva);
        Assert.Equal(FuentePrecioVigente.ProductoPrecioBase, resultado.FuentePrecio);
        Assert.True(resultado.EsFallbackProductoBase);
    }

    [Fact]
    public async Task ResolverAsync_IgnoraListaInactiva()
    {
        var producto = await SeedProductoAsync(precioVenta: 100m);
        var lista = await SeedListaAsync(esPredeterminada: true, activa: false);
        await SeedPrecioListaAsync(producto.Id, lista.Id, precio: 150m, costo: 60m);

        var resultado = await _resolver.ResolverAsync(producto.Id, lista.Id);

        Assert.NotNull(resultado);
        Assert.Equal(100m, resultado!.PrecioFinalConIva);
        Assert.Equal(FuentePrecioVigente.ProductoPrecioBase, resultado.FuentePrecio);
        Assert.Null(resultado.ListaId);
        Assert.Null(resultado.ProductoPrecioListaId);
        Assert.True(resultado.EsFallbackProductoBase);
    }

    [Fact]
    public async Task ResolverAsync_PrecioListaSeInterpretaComoFinalConIvaIncluido()
    {
        var producto = await SeedProductoAsync(precioVenta: 999m);
        var lista = await SeedListaAsync(esPredeterminada: true);
        await SeedPrecioListaAsync(producto.Id, lista.Id, precio: 1210m, costo: 60m);

        var resultado = await _resolver.ResolverAsync(producto.Id);

        Assert.NotNull(resultado);
        Assert.Equal(1210m, resultado!.PrecioFinalConIva);
    }

    [Fact]
    public async Task ResolverBatchAsync_ConListaExplicita_UsaPrecioLista()
    {
        var producto = await SeedProductoAsync(precioVenta: 100m);
        var lista = await SeedListaAsync(esPredeterminada: false);
        var precioLista = await SeedPrecioListaAsync(producto.Id, lista.Id, precio: 150m, costo: 60m);

        var resultados = await _resolver.ResolverBatchAsync(new[] { producto.Id }, lista.Id);

        var resultado = Assert.Single(resultados).Value;
        Assert.Equal(150m, resultado.PrecioFinalConIva);
        Assert.Equal(FuentePrecioVigente.ProductoPrecioLista, resultado.FuentePrecio);
        Assert.Equal(lista.Id, resultado.ListaId);
        Assert.Equal(precioLista.Id, resultado.ProductoPrecioListaId);
        Assert.False(resultado.EsFallbackProductoBase);
    }

    [Fact]
    public async Task ResolverBatchAsync_SinListaExplicita_UsaListaPredeterminada()
    {
        var producto = await SeedProductoAsync(precioVenta: 100m);
        var lista = await SeedListaAsync(esPredeterminada: true);
        await SeedPrecioListaAsync(producto.Id, lista.Id, precio: 175m, costo: 60m);

        var resultados = await _resolver.ResolverBatchAsync(new[] { producto.Id });

        var resultado = resultados[producto.Id];
        Assert.Equal(175m, resultado.PrecioFinalConIva);
        Assert.Equal(FuentePrecioVigente.ProductoPrecioLista, resultado.FuentePrecio);
        Assert.Equal(lista.Id, resultado.ListaId);
        Assert.False(resultado.EsFallbackProductoBase);
    }

    [Fact]
    public async Task ResolverBatchAsync_ProductoSinPrecioLista_CaeAPrecioVentaProducto()
    {
        var producto = await SeedProductoAsync(precioCompra: 60m, precioVenta: 100m);
        var lista = await SeedListaAsync(esPredeterminada: true);

        var resultados = await _resolver.ResolverBatchAsync(new[] { producto.Id });

        var resultado = resultados[producto.Id];
        Assert.Equal(100m, resultado.PrecioFinalConIva);
        Assert.Equal(FuentePrecioVigente.ProductoPrecioBase, resultado.FuentePrecio);
        Assert.Equal(lista.Id, resultado.ListaId);
        Assert.Null(resultado.ProductoPrecioListaId);
        Assert.Equal(60m, resultado.CostoSnapshot);
        Assert.True(resultado.EsFallbackProductoBase);
    }

    [Fact]
    public async Task ResolverBatchAsync_MezclaProductosConYSinPrecioLista()
    {
        var conLista = await SeedProductoAsync(precioVenta: 100m);
        var sinLista = await SeedProductoAsync(precioVenta: 80m);
        var lista = await SeedListaAsync(esPredeterminada: true);
        var precioLista = await SeedPrecioListaAsync(conLista.Id, lista.Id, precio: 150m, costo: 60m);

        var resultados = await _resolver.ResolverBatchAsync(new[] { conLista.Id, sinLista.Id });

        Assert.Equal(2, resultados.Count);
        Assert.Equal(150m, resultados[conLista.Id].PrecioFinalConIva);
        Assert.Equal(precioLista.Id, resultados[conLista.Id].ProductoPrecioListaId);
        Assert.False(resultados[conLista.Id].EsFallbackProductoBase);
        Assert.Equal(80m, resultados[sinLista.Id].PrecioFinalConIva);
        Assert.True(resultados[sinLista.Id].EsFallbackProductoBase);
    }

    [Fact]
    public async Task ResolverBatchAsync_IgnoraListaInactiva()
    {
        var producto = await SeedProductoAsync(precioVenta: 100m);
        var lista = await SeedListaAsync(esPredeterminada: true, activa: false);
        await SeedPrecioListaAsync(producto.Id, lista.Id, precio: 150m, costo: 60m);

        var resultados = await _resolver.ResolverBatchAsync(new[] { producto.Id }, lista.Id);

        var resultado = resultados[producto.Id];
        Assert.Equal(100m, resultado.PrecioFinalConIva);
        Assert.Equal(FuentePrecioVigente.ProductoPrecioBase, resultado.FuentePrecio);
        Assert.Null(resultado.ListaId);
        Assert.True(resultado.EsFallbackProductoBase);
    }

    [Fact]
    public async Task ResolverBatchAsync_ProductoInexistente_SeOmiteDelResultado()
    {
        var producto = await SeedProductoAsync(precioVenta: 100m);
        var inexistenteId = producto.Id + 9999;

        var resultados = await _resolver.ResolverBatchAsync(new[] { producto.Id, inexistenteId });

        Assert.True(resultados.ContainsKey(producto.Id));
        Assert.False(resultados.ContainsKey(inexistenteId));
        Assert.Single(resultados);
    }

    private async Task<Producto> SeedProductoAsync(decimal precioCompra = 60m, decimal precioVenta = 100m)
    {
        var code = Guid.NewGuid().ToString("N")[..8];
        var categoria = new Categoria { Codigo = "C" + code, Nombre = "Cat-" + code, Activo = true };
        var marca = new Marca { Codigo = "M" + code, Nombre = "Marca-" + code, Activo = true };
        _context.Categorias.Add(categoria);
        _context.Marcas.Add(marca);
        await _context.SaveChangesAsync();

        var producto = new Producto
        {
            Codigo = "P" + code,
            Nombre = "Prod-" + code,
            CategoriaId = categoria.Id,
            MarcaId = marca.Id,
            PrecioCompra = precioCompra,
            PrecioVenta = precioVenta,
            PorcentajeIVA = 21m,
            StockActual = 10m,
            Activo = true
        };

        _context.Productos.Add(producto);
        await _context.SaveChangesAsync();
        return producto;
    }

    private async Task<ListaPrecio> SeedListaAsync(bool esPredeterminada, bool activa = true)
    {
        var code = Guid.NewGuid().ToString("N")[..8];
        var lista = new ListaPrecio
        {
            Codigo = "L" + code,
            Nombre = "Lista-" + code,
            Tipo = TipoListaPrecio.Contado,
            Activa = activa,
            EsPredeterminada = esPredeterminada,
            Orden = 1
        };

        _context.ListasPrecios.Add(lista);
        await _context.SaveChangesAsync();
        return lista;
    }

    private async Task<ProductoPrecioLista> SeedPrecioListaAsync(
        int productoId,
        int listaId,
        decimal precio,
        decimal costo,
        DateTime? vigenciaDesde = null,
        DateTime? vigenciaHasta = null,
        bool esVigente = true)
    {
        var precioLista = new ProductoPrecioLista
        {
            ProductoId = productoId,
            ListaId = listaId,
            Precio = precio,
            Costo = costo,
            MargenValor = precio - costo,
            MargenPorcentaje = costo > 0 ? ((precio - costo) / costo) * 100 : 0,
            VigenciaDesde = vigenciaDesde ?? DateTime.UtcNow.AddDays(-1),
            VigenciaHasta = vigenciaHasta,
            EsVigente = esVigente,
            EsManual = true,
            CreadoPor = "test"
        };

        _context.ProductosPrecios.Add(precioLista);
        await _context.SaveChangesAsync();
        return precioLista;
    }
}
