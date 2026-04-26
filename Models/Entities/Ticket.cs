using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Models.Entities;

/// <summary>
/// Ticket de incidencia o solicitud interna del ERP.
/// Registra el contexto exacto de origen y soporta adjuntos y checklist.
/// </summary>
public class Ticket : AuditableEntity
{
    [Required]
    [StringLength(200)]
    public string Titulo { get; set; } = string.Empty;

    [Required]
    [StringLength(4000)]
    public string Descripcion { get; set; } = string.Empty;

    [Required]
    public TipoTicket Tipo { get; set; }

    [Required]
    public EstadoTicket Estado { get; set; } = EstadoTicket.Pendiente;

    // Contexto de origen — dónde se generó el ticket dentro del ERP
    [StringLength(100)]
    public string? ModuloOrigen { get; set; }

    [StringLength(200)]
    public string? VistaOrigen { get; set; }

    [StringLength(500)]
    public string? UrlOrigen { get; set; }

    /// <summary>
    /// Identificador libre del contexto de entidad (ej: "venta:42", "cliente:7")
    /// </summary>
    [StringLength(200)]
    public string? ContextKey { get; set; }

    // Resolución — seteados únicamente a través de RegistrarResolucionAsync
    [StringLength(4000)]
    public string? Resolucion { get; set; }

    [StringLength(100)]
    public string? ResueltoPor { get; set; }

    public DateTime? FechaResolucion { get; set; }

    // Navegación
    public virtual ICollection<TicketAdjunto> Adjuntos { get; set; } = new List<TicketAdjunto>();
    public virtual ICollection<TicketChecklistItem> ChecklistItems { get; set; } = new List<TicketChecklistItem>();
}
