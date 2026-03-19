using TheBuryProject.Models.Enums;

namespace TheBuryProject.ViewModels
{
    public class AlertaCobranzaViewModel
    {
        public int Id { get; set; }
        public byte[]? RowVersion { get; set; }
        public int CreditoId { get; set; }
        public int ClienteId { get; set; }
        public string ClienteNombre { get; set; } = string.Empty;
        public string ClienteDocumento { get; set; } = string.Empty;
        public TipoAlertaCobranza Tipo { get; set; }
        public PrioridadAlerta Prioridad { get; set; }
        public string Mensaje { get; set; } = string.Empty;
        public decimal MontoVencido { get; set; }
        public int CuotasVencidas { get; set; }
        public DateTime FechaAlerta { get; set; }
        public bool Resuelta { get; set; }
        public DateTime? FechaResolucion { get; set; }
        public string? Observaciones { get; set; }

        // Propiedades computadas para la vista
        public string Titulo { get; set; } = string.Empty;
        public string TipoNombre { get; set; } = string.Empty;
        public string IconoAlerta { get; set; } = string.Empty;
        public string ColorAlerta { get; set; } = string.Empty;
        public bool Leida { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
