using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Cuotas espec�ficas generadas por una venta a cr�dito personal
    /// </summary>
    public class VentaCreditoCuota  : AuditableEntity
    {
        public int VentaId { get; set; }
        public int CreditoId { get; set; }

        public int NumeroCuota { get; set; }

        [Required]
        public DateTime FechaVencimiento { get; set; }

        public decimal Monto { get; set; }
        public decimal Saldo { get; set; }

        public bool Pagada { get; set; } = false;
        public DateTime? FechaPago { get; set; }
        public decimal? MontoPagado { get; set; }

        [StringLength(500)]
        public string? Observaciones { get; set; }

        // Navigation
        public virtual Venta Venta { get; set; } = null!;
        public virtual Credito Credito { get; set; } = null!;
    }
}