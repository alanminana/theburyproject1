using TheBuryProject.Models.Enums;

namespace TheBuryProject.ViewModels;

/// <summary>
/// Proyección completa de un ticket, incluyendo adjuntos y checklist.
/// </summary>
public class TicketDetalleViewModel
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

    public string? Resolucion { get; set; }
    public string? ResueltoPor { get; set; }
    public DateTime? FechaResolucion { get; set; }

    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public List<TicketAdjuntoViewModel> Adjuntos { get; set; } = new();
    public List<TicketChecklistItemViewModel> ChecklistItems { get; set; } = new();
}
