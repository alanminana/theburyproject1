using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Services;

public sealed class ProductoCreditoRestriccionService : IProductoCreditoRestriccionService
{
    private readonly AppDbContext _context;

    public ProductoCreditoRestriccionService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<ProductoCreditoRestriccionResultado> ResolverAsync(
        IEnumerable<int> productoIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(productoIds);

        var ids = productoIds
            .Where(id => id > 0)
            .Distinct()
            .ToArray();

        if (ids.Length == 0)
        {
            return new ProductoCreditoRestriccionResultado();
        }

        var restricciones = await _context.ProductoCreditoRestricciones
            .AsNoTracking()
            .Where(r =>
                ids.Contains(r.ProductoId)
                && r.Activo
                && !r.IsDeleted
                && !r.Producto.IsDeleted)
            .OrderBy(r => r.ProductoId)
            .Select(r => new
            {
                r.ProductoId,
                r.Permitido,
                r.MaxCuotasCredito
            })
            .ToListAsync(cancellationToken);

        var bloqueantes = restricciones
            .Where(r => !r.Permitido)
            .Select(r => r.ProductoId)
            .Distinct()
            .ToArray();

        var restrictivas = restricciones
            .Where(r => r.Permitido && r.MaxCuotasCredito.HasValue)
            .OrderBy(r => r.MaxCuotasCredito!.Value)
            .ThenBy(r => r.ProductoId)
            .ToArray();

        return new ProductoCreditoRestriccionResultado
        {
            Permitido = bloqueantes.Length == 0,
            MaxCuotasCredito = restrictivas.Length == 0
                ? null
                : restrictivas[0].MaxCuotasCredito,
            ProductoIdsBloqueantes = bloqueantes,
            ProductoIdsRestrictivos = restrictivas
                .Select(r => r.ProductoId)
                .Distinct()
                .ToArray()
        };
    }
}
