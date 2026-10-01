namespace TheBuryProject.Services.Models;

public sealed class CotizacionProductoResultado
{
    public int ProductoId { get; init; }
    public string Codigo { get; init; } = string.Empty;
    public string Nombre { get; init; } = string.Empty;
    public int Cantidad { get; init; }
    public decimal PrecioUnitario { get; init; }
    public decimal Subtotal { get; init; }
    public TheBuryProject.Models.Enums.TipoServicioVenta? TipoArmado { get; init; }
    public bool EntregaCajaCerrada { get; init; }
    public decimal ArmadoPrecioUnitario { get; init; }
    public decimal ArmadoSubtotal { get; init; }
    public int? ProductoUnidadId { get; init; }
    public string? ProductoUnidadEtiqueta { get; init; }
    public bool TieneRestricciones { get; init; }
    public List<string> Advertencias { get; init; } = new();
}
