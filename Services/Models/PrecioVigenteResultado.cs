namespace TheBuryProject.Services.Models;

public enum FuentePrecioVigente
{
    ProductoPrecioLista = 1,
    ProductoPrecioBase = 2
}

public sealed class PrecioVigenteResultado
{
    public int ProductoId { get; init; }
    public decimal PrecioFinalConIva { get; init; }
    public FuentePrecioVigente FuentePrecio { get; init; }
    public int? ListaId { get; init; }
    public int? ProductoPrecioListaId { get; init; }
    public decimal PrecioBaseProducto { get; init; }
    public decimal CostoSnapshot { get; init; }
    public bool EsFallbackProductoBase { get; init; }
}
