namespace TheBuryProject.ViewModels;

/// <summary>
/// Contadores globales de tickets para las métricas del dashboard del módulo.
/// No incluye filtros — siempre refleja el total de tickets no eliminados.
/// </summary>
public class TicketMetricasViewModel
{
    public int Total { get; set; }
    public int Pendientes { get; set; }
    public int EnCurso { get; set; }
    public int Resueltos { get; set; }
    public int Cancelados { get; set; }
    public int Recientes { get; set; }
}
