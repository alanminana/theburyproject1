using System.ComponentModel.DataAnnotations;

namespace TheBuryProject.ViewModels.Requests;

/// <summary>
/// Datos para registrar la resolución de un ticket.
/// Al aplicarse, el estado pasa automáticamente a Resuelto.
/// </summary>
public class UpdateTicketResolutionRequest
{
    [Required]
    [StringLength(4000)]
    public string Resolucion { get; set; } = string.Empty;
}
