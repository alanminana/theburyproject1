using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Configuraci�n espec�fica para pagos con tarjeta
    /// </summary>
    public class ConfiguracionTarjeta  : AuditableEntity
    {
        public int ConfiguracionPagoId { get; set; }

        [Required]
        [StringLength(100)]
        public string NombreTarjeta { get; set; } = string.Empty; // Ej: Visa, Mastercard, Cabal

        [Required]
        public TipoTarjeta TipoTarjeta { get; set; }

        public bool Activa { get; set; } = true;

        // Para tarjeta de cr�dito
        public bool PermiteCuotas { get; set; } = false;
        public int? CantidadMaximaCuotas { get; set; }
        public TipoCuotaTarjeta? TipoCuota { get; set; }
        public decimal? TasaInteresesMensual { get; set; } // Si tiene inter�s

        // Para tarjeta de d�bito
        public bool TieneRecargoDebito { get; set; } = false;
        public decimal? PorcentajeRecargoDebito { get; set; }

        [StringLength(500)]
        public string? Observaciones { get; set; }

        // Navigation
        public virtual ConfiguracionPago ConfiguracionPago { get; set; } = null!;
    }
}