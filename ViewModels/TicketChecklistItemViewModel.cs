namespace TheBuryProject.ViewModels;

public class TicketChecklistItemViewModel
{
    public int Id { get; set; }
    public int TicketId { get; set; }
    public string Descripcion { get; set; } = string.Empty;
    public int Orden { get; set; }
    public bool Completado { get; set; }
    public string? CompletadoPor { get; set; }
    public DateTime? FechaCompletado { get; set; }
}
