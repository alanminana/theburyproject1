namespace TheBuryProject.Services.Models;

public sealed class ProductoCreditoRestriccionResultado
{
    public bool Permitido { get; init; } = true;

    public int? MaxCuotasCredito { get; init; }

    public IReadOnlyList<int> ProductoIdsBloqueantes { get; init; } = Array.Empty<int>();

    public IReadOnlyList<int> ProductoIdsRestrictivos { get; init; } = Array.Empty<int>();
}
