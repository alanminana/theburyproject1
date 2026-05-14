using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Representa una unidad física individual de un producto (trazabilidad individual).
    /// CodigoInternoUnidad es obligatorio y generado por sistema.
    /// NumeroSerie es opcional.
    /// </summary>
    public class ProductoUnidad : AuditableEntity
    {
        [Required]
        public int ProductoId { get; set; }

        [Required]
        [StringLength(100)]
        public string CodigoInternoUnidad { get; set; } = string.Empty;

        [StringLength(100)]
        public string? NumeroSerie { get; set; }

        public EstadoUnidad Estado { get; set; } = EstadoUnidad.EnStock;

        [StringLength(200)]
        public string? UbicacionActual { get; set; }

        public DateTime FechaIngreso { get; set; } = DateTime.UtcNow;

        public int? VentaDetalleId { get; set; }

        public int? ClienteId { get; set; }

        public DateTime? FechaVenta { get; set; }

        [StringLength(500)]
        public string? Observaciones { get; set; }

        // Navegación
        public virtual Producto Producto { get; set; } = null!;
        public virtual VentaDetalle? VentaDetalle { get; set; }
        public virtual Cliente? Cliente { get; set; }
        public virtual ICollection<ProductoUnidadMovimiento> Historial { get; set; } = new List<ProductoUnidadMovimiento>();
    }
}
