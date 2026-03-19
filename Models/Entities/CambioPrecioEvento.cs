using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;

namespace TheBuryProject.Models.Entities;

/// <summary>
/// Evento de cambio directo de precio en catalogo.
/// </summary>
public class CambioPrecioEvento : AuditableEntity
{
    [Required]
    public DateTime Fecha { get; set; }

    [Required]
    [StringLength(100)]
    public string Usuario { get; set; } = string.Empty;

    [Required]
    [StringLength(20)]
    public string Alcance { get; set; } = string.Empty; // seleccionados | filtrados | reversion

    [Required]
    public decimal ValorPorcentaje { get; set; }

    [StringLength(1000)]
    public string? Motivo { get; set; }

    public string? FiltrosJson { get; set; }

    public int CantidadProductos { get; set; }

    public DateTime? RevertidoEn { get; set; }

    [StringLength(100)]
    public string? RevertidoPor { get; set; }

    public virtual ICollection<CambioPrecioDetalle> Detalles { get; set; } = new List<CambioPrecioDetalle>();
}
