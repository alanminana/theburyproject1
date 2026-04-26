using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.ViewModels.Requests;

/// <summary>
/// Solicitud de cambio de estado de un ticket.
/// La validación de transición válida se realiza en TicketService.
/// </summary>
public class UpdateTicketStatusRequest
{
    [Required]
    public EstadoTicket NuevoEstado { get; set; }
}
