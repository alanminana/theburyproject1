namespace TheBuryProject.Services.Models;

public sealed class ProductoPrecioVentaResultado
{
    public int ProductoId { get; init; }
    public decimal PrecioVenta { get; init; }
    public FuentePrecioVigente FuentePrecio { get; init; }
    public int? ListaId { get; init; }
    public string Codigo { get; init; } = string.Empty;
    public string Nombre { get; init; } = string.Empty;
    public decimal StockActual { get; init; }
}
