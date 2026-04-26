using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;

namespace TheBuryProject.Models.Entities;

/// <summary>
/// Archivo adjunto vinculado a un Ticket.
/// Sigue el mismo patrón de almacenamiento que DocumentoCliente.
/// </summary>
public class TicketAdjunto : AuditableEntity
{
    [Required]
    public int TicketId { get; set; }

    [Required]
    [StringLength(200)]
    public string NombreArchivo { get; set; } = string.Empty;

    /// <summary>Ruta relativa desde wwwroot (ej: uploads/tickets/abc.pdf)</summary>
    [Required]
    [StringLength(500)]
    public string RutaArchivo { get; set; } = string.Empty;

    [StringLength(100)]
    public string? TipoMIME { get; set; }

    public long TamanoBytes { get; set; }

    // Navegación
    public virtual Ticket Ticket { get; set; } = null!;
}
