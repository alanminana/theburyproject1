using TheBuryProject.Models.Base;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Relaci�n N:N entre Proveedor y Producto
    /// Indica qu� productos puede proveer cada proveedor
    /// </summary>
    public class ProveedorProducto  : AuditableEntity
    {
        public int ProveedorId { get; set; }
        public int ProductoId { get; set; }

        // Navegaci�n
        public virtual Proveedor Proveedor { get; set; } = null!;
        public virtual Producto Producto { get; set; } = null!;
    }
}