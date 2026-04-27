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
