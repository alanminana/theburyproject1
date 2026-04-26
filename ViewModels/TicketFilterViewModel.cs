using TheBuryProject.Models.Enums;

namespace TheBuryProject.ViewModels;

/// <summary>
/// Parámetros de filtrado y paginación para el listado de tickets.
/// </summary>
public class TicketFilterViewModel
{
    public EstadoTicket? Estado { get; set; }
    public TipoTicket? Tipo { get; set; }
    public string? ModuloOrigen { get; set; }
    public string? Busqueda { get; set; }
    public DateTime? FechaDesde { get; set; }
    public DateTime? FechaHasta { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
