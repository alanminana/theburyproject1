using TheBuryProject.Models.Base;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Relaci�n N:N entre Proveedor y Marca
    /// Indica qu� marcas representa cada proveedor
    /// </summary>
    public class ProveedorMarca  : AuditableEntity
    {
        public int ProveedorId { get; set; }
        public int MarcaId { get; set; }

        // Navegaci�n
        public virtual Proveedor Proveedor { get; set; } = null!;
        public virtual Marca Marca { get; set; } = null!;
    }
}