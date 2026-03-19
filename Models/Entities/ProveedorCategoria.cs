using TheBuryProject.Models.Base;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Relaci�n N:N entre Proveedor y Categor�a
    /// Indica en qu� categor�as se especializa cada proveedor
    /// </summary>
    public class ProveedorCategoria  : AuditableEntity
    {
        public int ProveedorId { get; set; }
        public int CategoriaId { get; set; }

        // Navegaci�n
        public virtual Proveedor Proveedor { get; set; } = null!;
        public virtual Categoria Categoria { get; set; } = null!;
    }
}