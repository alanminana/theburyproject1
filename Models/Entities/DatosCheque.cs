using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Representa los datos o snapshot del cheque recibido como medio de pago en una venta,
    /// asociado a Venta y sin ciclo de vida de tesoreria.
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

        /// <summary>Instante UTC en que el cheque se acreditó (manual). Null mientras no se acreditó.</summary>
        public DateTime? AcreditadoAt { get; set; }

        [StringLength(50)]
        public string? AcreditadoPor { get; set; }

        /// <summary>Instante UTC en que se registró el rechazo del banco. Null = no rechazado.</summary>
        public DateTime? RechazadoAt { get; set; }

        [StringLength(50)]
        public string? RechazadoPor { get; set; }

        [StringLength(500)]
        public string? MotivoRechazo { get; set; }

        /// <summary>Cheque rechazado por el banco: la operación queda pendiente de regularización (cobro por otro medio o cancelación manual).</summary>
        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public bool Rechazado => RechazadoAt.HasValue;

        // Navigation
        public virtual Venta Venta { get; set; } = null!;
    }
}
