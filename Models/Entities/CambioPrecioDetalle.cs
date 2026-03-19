using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;

namespace TheBuryProject.Models.Entities;

/// <summary>
/// Detalle por producto de un evento de cambio directo de precio.
/// </summary>
public class CambioPrecioDetalle : AuditableEntity
{
    [Required]
    public int EventoId { get; set; }

    [Required]
    public int ProductoId { get; set; }

    [Required]
    public decimal PrecioAnterior { get; set; }

    [Required]
    public decimal PrecioNuevo { get; set; }

    public virtual CambioPrecioEvento Evento { get; set; } = null!;
    public virtual Producto Producto { get; set; } = null!;
}
