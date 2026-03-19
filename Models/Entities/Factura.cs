using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TheBuryProject.Models.Base;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Models.Entities
{
    public class Factura  : AuditableEntity
    {
        [Required]
        public int VentaId { get; set; }

        [Required]
        [StringLength(50)]
        public string Numero { get; set; } = string.Empty;

        [Required]
        public TipoFactura Tipo { get; set; } = TipoFactura.B;

        [StringLength(50)]
        public string? PuntoVenta { get; set; }

        [Required]
        [DataType(DataType.Date)]
        public DateTime FechaEmision { get; set; } = DateTime.UtcNow;

        [Column(TypeName = "decimal(18,2)")]
        public decimal Subtotal { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal IVA { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Total { get; set; }

        [StringLength(100)]
        public string? CAE { get; set; }

        public DateTime? FechaVencimientoCAE { get; set; }

        public bool Anulada { get; set; } = false;

        public DateTime? FechaAnulacion { get; set; }

        [StringLength(500)]
        public string? MotivoAnulacion { get; set; }

        // Navegaci�n
        public virtual Venta Venta { get; set; } = null!;
    }
}