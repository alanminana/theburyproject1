using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Representa una orden de compra realizada a un proveedor
    /// </summary>
    public class OrdenCompra  : AuditableEntity
    {
        /// <summary>
        /// N?mero de orden de compra (formato: OC-YYYYMMDD-0001)
        /// </summary>
        [Required]
        [StringLength(50)]
        public string Numero { get; set; } = string.Empty;

        /// <summary>
        /// Fecha de emisi?n de la orden
        /// </summary>
        public DateTime FechaEmision { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Fecha estimada de entrega
        /// </summary>
        public DateTime? FechaEntregaEstimada { get; set; }

        /// <summary>
        /// Fecha real de recepci?n
        /// </summary>
        public DateTime? FechaRecepcion { get; set; }

        /// <summary>
        /// Proveedor asociado
        /// </summary>
        public int ProveedorId { get; set; }

        /// <summary>
        /// Estado de la orden
        /// </summary>
        public EstadoOrdenCompra Estado { get; set; } = EstadoOrdenCompra.Borrador;

        /// <summary>
        /// Subtotal (suma de items)
        /// </summary>
        public decimal Subtotal { get; set; }

        /// <summary>
        /// Descuento aplicado
        /// </summary>
        public decimal Descuento { get; set; }

        /// <summary>
        /// IVA
        /// </summary>
        public decimal Iva { get; set; }

        /// <summary>
        /// Total de la orden
        /// </summary>
        public decimal Total { get; set; }

        /// <summary>
        /// Observaciones generales
        /// </summary>
        [StringLength(1000)]
        public string? Observaciones { get; set; }

        // Navegaci?n
        public virtual Proveedor Proveedor { get; set; } = null!;
        public virtual ICollection<OrdenCompraDetalle> Detalles { get; set; } = new List<OrdenCompraDetalle>();
    }
}
