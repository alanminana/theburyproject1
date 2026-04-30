using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TheBuryProject.Models.Base;

namespace TheBuryProject.Models.Entities
{
    public class VentaDetalle  : AuditableEntity
    {
        [Required]
        public int VentaId { get; set; }

        [Required]
        public int ProductoId { get; set; }

        [Required]
        public int Cantidad { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal PrecioUnitario { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Descuento { get; set; } = 0;

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal Subtotal { get; set; }

        [Column(TypeName = "decimal(5,2)")]
        public decimal PorcentajeIVA { get; set; }

        public int? AlicuotaIVAId { get; set; }

        [StringLength(100)]
        public string? AlicuotaIVANombre { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal PrecioUnitarioNeto { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal IVAUnitario { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal SubtotalNeto { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal SubtotalIVA { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal DescuentoGeneralProrrateado { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal SubtotalFinalNeto { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal SubtotalFinalIVA { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal SubtotalFinal { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal CostoUnitarioAlMomento { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal CostoTotalAlMomento { get; set; }

        [Column(TypeName = "decimal(5,2)")]
        public decimal ComisionPorcentajeAplicada { get; set; } = 0m;

        [Column(TypeName = "decimal(18,2)")]
        public decimal ComisionMonto { get; set; } = 0m;

        [StringLength(200)]
        public string? Observaciones { get; set; }

        // Navegaci�n
        public virtual Venta Venta { get; set; } = null!;
        public virtual Producto Producto { get; set; } = null!;
    }
}
