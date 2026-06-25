using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Asociación entre una Caja y un usuario con rol Vendedor.
    /// Padrón informativo / de control: permite saber qué vendedores están asignados a cada caja
    /// para visibilidad y reportes. No restringe ventas (sin enforcement).
    /// </summary>
    public class CajaVendedor : AuditableEntity
    {
        [Required]
        public int CajaId { get; set; }

        [Required]
        [StringLength(450)]
        public string VendedorUserId { get; set; } = string.Empty;

        // Navegación
        public virtual Caja Caja { get; set; } = null!;
        public virtual ApplicationUser Vendedor { get; set; } = null!;
    }
}
