using TheBuryProject.Models.Base;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Detalle de items de una orden de compra
    /// </summary>
    public class OrdenCompraDetalle  : AuditableEntity
    {
        /// <summary>
        /// Orden de compra asociada
        /// </summary>
        public int OrdenCompraId { get; set; }

        /// <summary>
        /// Producto solicitado
        /// </summary>
        public int ProductoId { get; set; }

        /// <summary>
        /// Cantidad solicitada
        /// </summary>
        public int Cantidad { get; set; }

        /// <summary>
        /// Precio unitario acordado
        /// </summary>
        public decimal PrecioUnitario { get; set; }

        /// <summary>
        /// Subtotal de la l�nea (Cantidad * PrecioUnitario)
        /// </summary>
        public decimal Subtotal { get; set; }

        /// <summary>
        /// Cantidad recibida
        /// </summary>
        public int CantidadRecibida { get; set; }

        /// <summary>
        /// Observaciones del item
        /// </summary>
        public string? Observaciones { get; set; }

        // Navegaci�n
        public virtual OrdenCompra OrdenCompra { get; set; } = null!;
        public virtual Producto Producto { get; set; } = null!;
    }
}