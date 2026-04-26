using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.ViewModels.Requests;

/// <summary>
/// Campos editables de un ticket existente (solo mientras no esté Resuelto/Cancelado).
/// </summary>
public class UpdateTicketRequest
{
    [Required]
    [StringLength(200)]
    public string Titulo { get; set; } = string.Empty;

    [Required]
    [StringLength(4000)]
    public string Descripcion { get; set; } = string.Empty;

    [Required]
    public TipoTicket Tipo { get; set; }
}
