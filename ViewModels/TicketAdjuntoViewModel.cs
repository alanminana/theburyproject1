namespace TheBuryProject.ViewModels;

public class TicketAdjuntoViewModel
{
    public int Id { get; set; }
    public int TicketId { get; set; }
    public string NombreArchivo { get; set; } = string.Empty;
    public string RutaArchivo { get; set; } = string.Empty;
    public string? TipoMIME { get; set; }
    public long TamanoBytes { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
}
