using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Services;

public sealed class PrecioVigenteResolver : IPrecioVigenteResolver
{
    private readonly AppDbContext _context;

    public PrecioVigenteResolver(AppDbContext context)
    {
        _context = context;
    }

    public async Task<PrecioVigenteResultado?> ResolverAsync(
        int productoId,
        int? listaId = null,
        DateTime? fecha = null)
    {
        var resultados = await ResolverBatchAsync(new[] { productoId }, listaId, fecha);
        return resultados.GetValueOrDefault(productoId);
    }

    public async Task<IReadOnlyDictionary<int, PrecioVigenteResultado>> ResolverBatchAsync(
        IEnumerable<int> productoIds,
        int? listaId = null,
        DateTime? fecha = null,
        CancellationToken cancellationToken = default)
    {
        var ids = productoIds
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        if (ids.Count == 0)
            return new Dictionary<int, PrecioVigenteResultado>();

        var fechaResolucion = fecha ?? DateTime.UtcNow;
        var producto = await _context.Productos
            .AsNoTracking()
            .Where(p => ids.Contains(p.Id) && !p.IsDeleted)
            .Select(p => new
            {
                p.Id,
                p.PrecioVenta,
                p.PrecioCompra
            })
            .ToListAsync(cancellationToken);

        if (producto.Count == 0)
            return new Dictionary<int, PrecioVigenteResultado>();

        var productos = producto.ToDictionary(p => p.Id);
        var listaActivaId = await ResolverListaActivaIdAsync(listaId, cancellationToken);
        var preciosListaPorProductoId = new Dictionary<int, (int Id, int ListaId, decimal Precio, decimal Costo)>();

        if (listaActivaId.HasValue)
        {
            var preciosLista = await _context.ProductosPrecios
                .AsNoTracking()
                .Where(p => productos.Keys.Contains(p.ProductoId)
                            && p.ListaId == listaActivaId.Value
                            && p.VigenciaDesde <= fechaResolucion
                            && (p.VigenciaHasta == null || p.VigenciaHasta >= fechaResolucion)
                            && p.EsVigente
                            && !p.IsDeleted
                            && p.Lista.Activa
                            && !p.Lista.IsDeleted)
                .OrderByDescending(p => p.VigenciaDesde)
                .Select(p => new
                {
                    p.Id,
                    p.ProductoId,
                    p.ListaId,
                    p.Precio,
                    p.Costo
                })
                .ToListAsync(cancellationToken);

            preciosListaPorProductoId = preciosLista
                .GroupBy(p => p.ProductoId)
                .ToDictionary(
                    g => g.Key,
                    g =>
                    {
                        var p = g.First();
                        return (p.Id, p.ListaId, p.Precio, p.Costo);
                    });
        }

        var resultados = new Dictionary<int, PrecioVigenteResultado>(productos.Count);
        foreach (var item in producto)
        {
            if (preciosListaPorProductoId.TryGetValue(item.Id, out var precioLista))
            {
                resultados[item.Id] = new PrecioVigenteResultado
                {
                    ProductoId = item.Id,
                    PrecioFinalConIva = precioLista.Precio,
                    FuentePrecio = FuentePrecioVigente.ProductoPrecioLista,
                    ListaId = precioLista.ListaId,
                    ProductoPrecioListaId = precioLista.Id,
                    PrecioBaseProducto = item.PrecioVenta,
                    CostoSnapshot = precioLista.Costo,
                    EsFallbackProductoBase = false
                };
                continue;
            }

            resultados[item.Id] = new PrecioVigenteResultado
            {
                ProductoId = item.Id,
                PrecioFinalConIva = item.PrecioVenta,
                FuentePrecio = FuentePrecioVigente.ProductoPrecioBase,
                ListaId = listaActivaId,
                ProductoPrecioListaId = null,
                PrecioBaseProducto = item.PrecioVenta,
                CostoSnapshot = item.PrecioCompra,
                EsFallbackProductoBase = true
            };
        }

        return resultados;
    }

    private async Task<int?> ResolverListaActivaIdAsync(
        int? listaId,
        CancellationToken cancellationToken = default)
    {
        if (listaId.HasValue)
        {
            return await _context.ListasPrecios
                .AsNoTracking()
                .Where(l => l.Id == listaId.Value && l.Activa && !l.IsDeleted)
                .Select(l => (int?)l.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return await _context.ListasPrecios
            .AsNoTracking()
            .Where(l => l.EsPredeterminada && l.Activa && !l.IsDeleted)
            .Select(l => (int?)l.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
