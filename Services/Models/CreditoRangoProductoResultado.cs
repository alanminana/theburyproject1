namespace TheBuryProject.Services.Models;

public sealed record CreditoRangoProductoResultado(
    int Min,
    int Max,
    int MaxBase,
    int? MaxProducto,
    int? ProductoIdRestrictivo,
    string? ProductoRestrictivoNombre,
    string? DescripcionProducto,
    string? Error);
