using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Representa una marca de productos en el sistema.
    /// Soporta jerarqu�a (marcas padre e hijas/submarcas).
    /// </summary>
    public class Marca  : AuditableEntity

    {
        /// <summary>
        /// C�digo �nico de la marca (ej: "SAM", "LG", "WHI")
        /// </summary>
        [Required]
        [StringLength(20)]
        public string Codigo { get; set; } = string.Empty;

        /// <summary>
        /// Nombre de la marca
        /// </summary>
        [Required]
        [StringLength(100)]
        public string Nombre { get; set; } = string.Empty;

        /// <summary>
        /// Descripci�n opcional de la marca
        /// </summary>
        [StringLength(500)]
        public string? Descripcion { get; set; }

        /// <summary>
        /// Id de la marca padre (null si es ra�z, ej: Samsung es padre, Samsung Galaxy es hija)
        /// </summary>
        public int? ParentId { get; set; }

        /// <summary>
        /// Pa�s de origen de la marca
        /// </summary>
        [StringLength(100)]
        public string? PaisOrigen { get; set; }

        /// <summary>
        /// Marca padre (navegaci�n)
        /// </summary>
        public virtual Marca? Parent { get; set; }
        /// <summary>
        /// Indica si la marca est� activa
        /// </summary>
        public bool Activo { get; set; } = true;
        /// <summary>
        /// SubMarcas (navegaci�n)
        /// </summary>
        public virtual ICollection<Marca> Children { get; set; } = new List<Marca>();
    }
}
