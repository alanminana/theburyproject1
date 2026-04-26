using TheBuryProject.Models.Enums;

namespace TheBuryProject.ViewModels;

/// <summary>
/// Proyección de un ticket para listados. Sin colecciones.
/// </summary>
public class TicketViewModel
{
    public int Id { get; set; }
    public string Titulo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public TipoTicket Tipo { get; set; }
    public string TipoNombre { get; set; } = string.Empty;
    public EstadoTicket Estado { get; set; }
    public string EstadoNombre { get; set; } = string.Empty;
    public string? ModuloOrigen { get; set; }
    public string? VistaOrigen { get; set; }
    public string? UrlOrigen { get; set; }
    public string? ContextKey { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? FechaResolucion { get; set; }
}
