using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;

namespace TheBuryProject.Models.Entities
{
    public class ProductoCaracteristica : AuditableEntity
    {
        [Required]
        public int ProductoId { get; set; }

        [Required(ErrorMessage = "El nombre de la característica es obligatorio")]
        [StringLength(100, ErrorMessage = "El nombre no puede superar 100 caracteres")]
        public string Nombre { get; set; } = string.Empty;

        [Required(ErrorMessage = "El valor de la característica es obligatorio")]
        [StringLength(300, ErrorMessage = "El valor no puede superar 300 caracteres")]
        public string Valor { get; set; } = string.Empty;

        public virtual Producto Producto { get; set; } = null!;
    }
}
