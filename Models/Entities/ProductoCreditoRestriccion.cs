using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Restriccion de credito personal declarada para un producto.
    /// </summary>
    public class ProductoCreditoRestriccion : AuditableEntity
    {
        public int ProductoId { get; set; }

        public bool Activo { get; set; } = true;

        public bool Permitido { get; set; } = true;

        public int? MaxCuotasCredito { get; set; }

        [StringLength(500)]
        public string? Observaciones { get; set; }

        public virtual Producto Producto { get; set; } = null!;
    }
}
