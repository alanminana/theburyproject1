using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Alícuota de IVA configurable para productos y categorías.
    /// </summary>
    public class AlicuotaIVA : AuditableEntity
    {
        [Required]
        [StringLength(20)]
        public string Codigo { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        public string Nombre { get; set; } = string.Empty;

        [Range(0, 100)]
        public decimal Porcentaje { get; set; }

        public bool Activa { get; set; } = true;

        public bool EsPredeterminada { get; set; }
    }
}
