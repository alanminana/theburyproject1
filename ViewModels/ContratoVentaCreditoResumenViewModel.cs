using TheBuryProject.Models.Enums;

namespace TheBuryProject.ViewModels
{
    public class ContratoVentaCreditoResumenViewModel
    {
        public int VentaId { get; set; }

        public int CreditoId { get; set; }

        public string NumeroContrato { get; set; } = string.Empty;

        public string NumeroPagare { get; set; } = string.Empty;

        public DateTime FechaGeneracionUtc { get; set; }

        public string UsuarioGeneracion { get; set; } = string.Empty;

        public EstadoDocumento EstadoDocumento { get; set; }

        public string? NombreArchivo { get; set; }

        public string? ContentHash { get; set; }
    }
}
