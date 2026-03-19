using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TheBuryProject.Models.Base;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Movimiento de entrada o salida de efectivo en caja
    /// </summary>
    public class MovimientoCaja  : AuditableEntity
    {
        [Required]
        public int AperturaCajaId { get; set; }

        [Required]
        public DateTime FechaMovimiento { get; set; } = DateTime.UtcNow;

        [Required]
        public TipoMovimientoCaja Tipo { get; set; }

        [Required]
        public ConceptoMovimientoCaja Concepto { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal Monto { get; set; }

        [Required]
        [StringLength(500)]
        public string Descripcion { get; set; } = string.Empty;

        [StringLength(100)]
        public string? Referencia { get; set; }

        public int? ReferenciaId { get; set; }

        [Required]
        [StringLength(50)]
        public string Usuario { get; set; } = string.Empty;

        [StringLength(500)]
        public string? Observaciones { get; set; }

        // Navegaci�n
        public virtual AperturaCaja AperturaCaja { get; set; } = null!;
    }
}