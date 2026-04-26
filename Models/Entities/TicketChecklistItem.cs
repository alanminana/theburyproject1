using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;

namespace TheBuryProject.Models.Entities;

/// <summary>
/// Ítem de checklist asociado a un Ticket.
/// Tabla hija (no JSON) para soportar trazabilidad individual de completado.
/// </summary>
public class TicketChecklistItem : AuditableEntity
{
    [Required]
    public int TicketId { get; set; }

    [Required]
    [StringLength(500)]
    public string Descripcion { get; set; } = string.Empty;

    public int Orden { get; set; }

    public bool Completado { get; set; } = false;

    [StringLength(100)]
    public string? CompletadoPor { get; set; }

    public DateTime? FechaCompletado { get; set; }

    // Navegación
    public virtual Ticket Ticket { get; set; } = null!;
}
