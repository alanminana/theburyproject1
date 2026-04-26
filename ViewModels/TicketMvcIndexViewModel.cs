using TheBuryProject.Models.Enums;
using TheBuryProject.ViewModels.Base;

namespace TheBuryProject.ViewModels;

/// <summary>
/// ViewModel para la vista MVC de listado de tickets (no la API).
/// Combina los resultados paginados con los filtros activos y métricas de resumen.
/// </summary>
public class TicketMvcIndexViewModel
{
    public PageResult<TicketViewModel> Resultado { get; set; } = null!;
    public TicketFilterViewModel Filtro { get; set; } = new();

    // Métricas calculadas sobre el total (no solo la página actual)
    public int TotalTickets { get; set; }
    public int Pendientes { get; set; }
    public int EnCurso { get; set; }
    public int Resueltos { get; set; }
    public int Cancelados { get; set; }
    public int Recientes { get; set; }

    public bool PuedeCambiarEstado { get; set; }
    public bool PuedeResolver { get; set; }
    public bool PuedeEliminar { get; set; }

    public int Abiertos => Pendientes + EnCurso;

    public bool FiltroActivo =>
        !string.IsNullOrEmpty(Filtro.Busqueda) ||
        Filtro.Estado.HasValue ||
        Filtro.Tipo.HasValue ||
        !string.IsNullOrEmpty(Filtro.ModuloOrigen) ||
        Filtro.FechaDesde.HasValue ||
        Filtro.FechaHasta.HasValue;
}
