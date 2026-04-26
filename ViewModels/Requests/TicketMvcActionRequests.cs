using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.ViewModels.Requests;

public class CambiarEstadoTicketsRequest
{
    [Required]
    public string TicketIds { get; set; } = string.Empty;

    [Required]
    public EstadoTicket? NuevoEstado { get; set; }

    [StringLength(4000)]
    public string? Descripcion { get; set; }

    public string? ReturnUrl { get; set; }
}

public class EliminarTicketsRequest
{
    [Required]
    public string TicketIds { get; set; } = string.Empty;

    public string? ReturnUrl { get; set; }
}
