namespace TheBuryProject.Services.Models;

public sealed record CreditoRangoProductoResultado(
    int Min,
    int Max,
    int MaxBase,
    int? MaxProducto,
    int? ProductoIdRestrictivo,
    string? ProductoRestrictivoNombre,
    string? DescripcionProducto,
    string? Error)
{
    /// <summary>Rango base sin restricción por producto.</summary>
    public static CreditoRangoProductoResultado SinRestriccion(int minBase, int maxBase) =>
        new(minBase, maxBase, maxBase, null, null, null, null, null);
}
