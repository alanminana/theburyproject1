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

    /// <summary>Armado elegido para el producto (null = sin armado). Se cobra por unidad.</summary>
    public Enums.TipoServicioVenta? TipoArmado { get; set; }
    public bool EntregaCajaCerrada { get; set; }
    public decimal ArmadoPrecioUnitario { get; set; }
    public decimal ArmadoSubtotal { get; set; }

    /// <summary>Unidad física elegida para el producto (null = sin elegir todavía). Igual que en
    /// VentaDetalle, sólo válida con Cantidad = 1; se marca Vendida recién al convertir a Venta.</summary>
    public int? ProductoUnidadId { get; set; }

    public virtual Cotizacion Cotizacion { get; set; } = null!;
    public virtual Producto Producto { get; set; } = null!;
    public virtual ProductoUnidad? ProductoUnidad { get; set; }
}
