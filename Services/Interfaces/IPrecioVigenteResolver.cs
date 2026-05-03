using TheBuryProject.Services.Models;

namespace TheBuryProject.Services.Interfaces;

public interface IPrecioVigenteResolver
{
    Task<PrecioVigenteResultado?> ResolverAsync(
        int productoId,
        int? listaId = null,
        DateTime? fecha = null);

    Task<IReadOnlyDictionary<int, PrecioVigenteResultado>> ResolverBatchAsync(
        IEnumerable<int> productoIds,
        int? listaId = null,
        DateTime? fecha = null,
        CancellationToken cancellationToken = default);
}
