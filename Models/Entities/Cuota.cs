using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Representa una cuota individual de un cr�dito
    /// </summary>
    public class Cuota  : AuditableEntity
    {
        public int CreditoId { get; set; }

        [Required]
        public int NumeroCuota { get; set; }

        [Required]
        public decimal MontoCapital { get; set; }

        [Required]
        public decimal MontoInteres { get; set; }

        [Required]
        public decimal MontoTotal { get; set; }

        [Required]
        public DateTime FechaVencimiento { get; set; }

        public DateTime? FechaPago { get; set; }

        public decimal MontoPagado { get; set; } = 0;

        public decimal MontoPunitorio { get; set; } = 0;

        /// <summary>
        /// Recargo (o descuento, si es negativo) del medio de pago aplicado al cobrar.
        /// Concepto separado del valor original de la cuota: no modifica MontoTotal.
        /// </summary>
        public decimal RecargoMedioPago { get; set; } = 0;

        public EstadoCuota Estado { get; set; } = EstadoCuota.Pendiente;

        [StringLength(500)]
        public string? Observaciones { get; set; }

        // Datos de pago
        [StringLength(50)]
        public string? MedioPago { get; set; }

        [StringLength(100)]
        public string? ComprobantePago { get; set; }

        // Navigation Properties
        public virtual Credito Credito { get; set; } = null!;
    }
}