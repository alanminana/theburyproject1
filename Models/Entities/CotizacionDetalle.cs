using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;

namespace TheBuryProject.Models.Entities;

public class CotizacionDetalle : AuditableEntity
{
    public int CotizacionId { get; set; }
    public int ProductoId { get; set; }

    [Required]
    [StringLength(50)]
    public string CodigoProductoSnapshot { get; set; } = string.Empty;

    [Required]
    [StringLength(200)]
    public string NombreProductoSnapshot { get; set; } = string.Empty;

    public decimal Cantidad { get; set; }
    public decimal PrecioUnitarioSnapshot { get; set; }
    public decimal? DescuentoPorcentajeSnapshot { get; set; }
    public decimal? DescuentoImporteSnapshot { get; set; }
    public decimal Subtotal { get; set; }

    public virtual Cotizacion Cotizacion { get; set; } = null!;
    public virtual Producto Producto { get; set; } = null!;
}
