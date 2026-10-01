namespace TheBuryProject.Services.Models;

public sealed class CotizacionProductoRequest
{
    public int ProductoId { get; init; }
    public int Cantidad { get; init; }
    public decimal? PrecioManual { get; init; }
    public decimal? DescuentoPorcentaje { get; init; }
    public decimal? DescuentoImporte { get; init; }

    /// <summary>Armado N.º 1..6 (null = sin armado). El precio sale de la tabla global, nunca del cliente.</summary>
    public TheBuryProject.Models.Enums.TipoServicioVenta? TipoArmado { get; init; }
    public bool EntregaCajaCerrada { get; init; }

    /// <summary>Unidad física elegida (null = sin elegir). Obligatoria cuando el producto exige
    /// número de serie; el calculador valida que exista, pertenezca al producto y esté EnStock.</summary>
    public int? ProductoUnidadId { get; init; }
}
