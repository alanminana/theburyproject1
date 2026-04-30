using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Representa una categor�a de productos en el sistema.
    /// Soporta jerarqu�a (categor�as padre e hijas).
    /// </summary>
    public class Categoria  : AuditableEntity
    {
        /// <summary>
        /// C�digo �nico de la categor�a (ej: "ELEC", "FRIO")
        /// </summary>
        [Required]
        [StringLength(20)]
        public string Codigo { get; set; } = string.Empty;

        /// <summary>
        /// Nombre descriptivo de la categor�a
        /// </summary>
        [Required]
        [StringLength(100)]
        public string Nombre { get; set; } = string.Empty;

        /// <summary>
        /// Descripci�n opcional de la categor�a
        /// </summary>
        [StringLength(500)]
        public string? Descripcion { get; set; }

        /// <summary>
        /// Id de la categor�a padre (null si es ra�z)
        /// </summary>
        public int? ParentId { get; set; }

        /// <summary>
        /// Indica si los productos de esta categor�a requieren control de serie
        /// </summary>
        public bool ControlSerieDefault { get; set; } = false;

        /// <summary>
        /// Alícuota de IVA por defecto para productos de esta categoría.
        /// </summary>
        public int? AlicuotaIVAId { get; set; }

        /// <summary>
        /// Categor�a padre (navegaci�n)
        /// </summary>
        public virtual Categoria? Parent { get; set; }
        /// <summary>
        /// Indica si la marca est� activa
        /// </summary>
        public bool Activo { get; set; } = true;

        /// <summary>
        /// Alícuota de IVA configurable de la categoría.
        /// </summary>
        public virtual AlicuotaIVA? AlicuotaIVA { get; set; }
        /// <summary>
        /// Categor�as hijas (navegaci�n)
        /// </summary>
        public virtual ICollection<Categoria> Children { get; set; } = new List<Categoria>();
    }
}
