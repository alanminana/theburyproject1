using TheBuryProject.Models.Entities;
using TheBuryProject.Services;

namespace TheBuryProject.Tests.Unit;

public class ProductoIvaResolverTests
{
    private static Producto BuildProducto(decimal porcentajeIVA = 21m) => new()
    {
        Codigo = "TEST",
        Nombre = "Producto Test",
        CategoriaId = 1,
        MarcaId = 1,
        PrecioCompra = 10m,
        PrecioVenta = 121m,
        PorcentajeIVA = porcentajeIVA
    };

    private static AlicuotaIVA BuildAlicuota(decimal porcentaje, bool activa = true) => new()
    {
        Id = 1,
        Codigo = "TEST_IVA",
        Nombre = $"IVA {porcentaje}%",
        Porcentaje = porcentaje,
        Activa = activa,
        IsDeleted = false
    };

    // ─── Prioridad 1: AlicuotaIVA del producto activa ───────────────────────

    [Fact]
    public void ResolverPorcentaje_ConAlicuotaProductoActiva21_Devuelve21()
    {
        var producto = BuildProducto(porcentajeIVA: 0m);
        producto.AlicuotaIVA = BuildAlicuota(21m);

        var resultado = ProductoIvaResolver.ResolverPorcentajeIVAProducto(producto);

        Assert.Equal(21m, resultado);
    }

    [Fact]
    public void ResolverPorcentaje_ConAlicuotaProductoActiva105_Devuelve105()
    {
        var producto = BuildProducto(porcentajeIVA: 21m);
        producto.AlicuotaIVA = BuildAlicuota(10.5m);

        var resultado = ProductoIvaResolver.ResolverPorcentajeIVAProducto(producto);

        Assert.Equal(10.5m, resultado);
    }

    [Fact]
    public void ResolverPorcentaje_ConAlicuotaProductoActivaExento_DevuelveCero()
    {
        var producto = BuildProducto(porcentajeIVA: 21m);
        producto.AlicuotaIVA = BuildAlicuota(0m);

        var resultado = ProductoIvaResolver.ResolverPorcentajeIVAProducto(producto);

        Assert.Equal(0m, resultado);
    }

    // ─── Prioridad 2: AlicuotaIVA de categoría ──────────────────────────────

    [Fact]
    public void ResolverPorcentaje_SinAlicuotaProductoConAlicuotaCategoria_UsaCategoria()
    {
        var producto = BuildProducto(porcentajeIVA: 0m);
        producto.AlicuotaIVA = null;
        producto.Categoria = new Categoria
        {
            Id = 1, Codigo = "CAT", Nombre = "Categoría",
            AlicuotaIVA = BuildAlicuota(27m)
        };

        var resultado = ProductoIvaResolver.ResolverPorcentajeIVAProducto(producto);

        Assert.Equal(27m, resultado);
    }

    [Fact]
    public void ResolverPorcentaje_AlicuotaProductoInactiva_CaeACategoriaActiva()
    {
        var producto = BuildProducto(porcentajeIVA: 0m);
        producto.AlicuotaIVA = BuildAlicuota(21m, activa: false); // inactiva → ignorada
        producto.Categoria = new Categoria
        {
            Id = 1, Codigo = "CAT", Nombre = "Categoría",
            AlicuotaIVA = BuildAlicuota(10.5m)
        };

        var resultado = ProductoIvaResolver.ResolverPorcentajeIVAProducto(producto);

        Assert.Equal(10.5m, resultado);
    }

    // ─── Prioridad 3: PorcentajeIVA del producto como fallback ──────────────

    [Fact]
    public void ResolverPorcentaje_SinAlicuotasUsaPorcentajeIVAProducto()
    {
        var producto = BuildProducto(porcentajeIVA: 10.5m);
        // AlicuotaIVA y Categoria quedan null (valor por defecto en la entidad)

        var resultado = ProductoIvaResolver.ResolverPorcentajeIVAProducto(producto);

        Assert.Equal(10.5m, resultado);
    }

    [Fact]
    public void ResolverPorcentaje_AlicuotaProductoEliminada_UsaPorcentajeIVAProducto()
    {
        var alicuotaEliminada = BuildAlicuota(21m);
        alicuotaEliminada.IsDeleted = true;

        var producto = BuildProducto(porcentajeIVA: 10.5m);
        producto.AlicuotaIVA = alicuotaEliminada;
        // Categoria queda null (valor por defecto)

        var resultado = ProductoIvaResolver.ResolverPorcentajeIVAProducto(producto);

        Assert.Equal(10.5m, resultado);
    }

    // ─── Prioridad 4: default 21% ───────────────────────────────────────────

    [Fact]
    public void ResolverPorcentaje_SinNingunaFuenteUsaDefault21()
    {
        // PorcentajeIVA = -1 (inválido) y sin alícuotas
        var producto = new Producto
        {
            Codigo = "X", Nombre = "X", CategoriaId = 1, MarcaId = 1,
            PrecioCompra = 1m, PrecioVenta = 1m,
            PorcentajeIVA = -1m  // fuera de rango → no aplica como fallback
        };

        var resultado = ProductoIvaResolver.ResolverPorcentajeIVAProducto(producto);

        Assert.Equal(21m, resultado);
    }
}
