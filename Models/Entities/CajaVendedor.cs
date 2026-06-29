using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Asociación entre una Caja y un usuario habilitado en ella: vendedores que venden contra la caja
    /// y cajeros que la operan. Es la base del enforcement "cada usuario sobre su propia caja"
    /// (RBAC define el verbo; esta membresía define sobre qué caja).
    /// Nota: la tabla/columna conservan el nombre histórico (CajaVendedores / VendedorUserId)
    /// por estabilidad de migración, pero almacenan cualquier usuario habilitado (vendedor o cajero).
    /// </summary>
    public class CajaVendedor : AuditableEntity
    {
        [Required]
        public int CajaId { get; set; }

        /// <summary>Id del usuario habilitado (vendedor o cajero). Nombre histórico conservado.</summary>
        [Required]
        [StringLength(450)]
        public string VendedorUserId { get; set; } = string.Empty;

        // Navegación
        public virtual Caja Caja { get; set; } = null!;
        public virtual ApplicationUser Vendedor { get; set; } = null!;
    }
}
