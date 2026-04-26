using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.ViewModels.Requests;

/// <summary>
/// Datos mínimos para crear un nuevo ticket.
/// </summary>
public class CreateTicketRequest
{
    [Required]
    [StringLength(200)]
    public string Titulo { get; set; } = string.Empty;

    [Required]
    [StringLength(4000)]
    public string Descripcion { get; set; } = string.Empty;

    [Required]
    public TipoTicket Tipo { get; set; }

    // Contexto de origen — todos opcionales; el cliente los envía automáticamente
    [StringLength(100)]
    public string? ModuloOrigen { get; set; }

    [StringLength(200)]
    public string? VistaOrigen { get; set; }

    [StringLength(500)]
    public string? UrlOrigen { get; set; }

    [StringLength(200)]
    public string? ContextKey { get; set; }
}
