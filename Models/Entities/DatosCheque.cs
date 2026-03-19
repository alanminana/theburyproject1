using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Datos espec�ficos del cheque asociado a una venta
    /// </summary>
    public class DatosCheque  : AuditableEntity
    {
        public int VentaId { get; set; }

        [Required]
        [StringLength(50)]
        public string NumeroCheque { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        public string Banco { get; set; } = string.Empty;

        [Required]
        [StringLength(200)]
        public string Titular { get; set; } = string.Empty;

        [StringLength(20)]
        public string? CUIT { get; set; }

        [Required]
        public DateTime FechaEmision { get; set; } = DateTime.UtcNow;

        [Required]
        public DateTime FechaVencimiento { get; set; }

        public decimal Monto { get; set; }

        [StringLength(500)]
        public string? Observaciones { get; set; }

        // Navigation
        public virtual Venta Venta { get; set; } = null!;
    }
}
