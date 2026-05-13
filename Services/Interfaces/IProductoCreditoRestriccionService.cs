using TheBuryProject.Services.Models;

namespace TheBuryProject.Services.Interfaces;

public interface IProductoCreditoRestriccionService
{
    Task<ProductoCreditoRestriccionResultado> ResolverAsync(
        IEnumerable<int> productoIds,
        CancellationToken cancellationToken = default);
}
