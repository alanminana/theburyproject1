namespace TheBuryProject.Services.Models;

public sealed class CotizacionProductoRequest
{
    public int ProductoId { get; init; }
    public int Cantidad { get; init; }
    public decimal? PrecioManual { get; init; }
    public decimal? DescuentoPorcentaje { get; init; }
    public decimal? DescuentoImporte { get; init; }
}
