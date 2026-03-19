using System.ComponentModel.DataAnnotations;

namespace TheBuryProject.ViewModels
{
    /// <summary>
    /// ViewModel para crear y editar categorías
    /// </summary>
    public class CategoriaViewModel
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "El código es obligatorio")]
        [StringLength(20, ErrorMessage = "El código no puede tener más de 20 caracteres")]
        [Display(Name = "Código")]
        public string Codigo { get; set; } = string.Empty;

        [Required(ErrorMessage = "El nombre es obligatorio")]
        [StringLength(100, ErrorMessage = "El nombre no puede tener más de 100 caracteres")]
        [Display(Name = "Nombre")]
        public string Nombre { get; set; } = string.Empty;

        [StringLength(500, ErrorMessage = "La descripci�n no puede tener más de 500 caracteres")]
        [Display(Name = "Descripci�n")]
        public string? Descripcion { get; set; }

        [Display(Name = "Categoría Padre")]
        public int? ParentId { get; set; }

        [Display(Name = "Control de Serie por Defecto")]
        public bool ControlSerieDefault { get; set; }

        // Para el dropdown de categorías padre
        [Display(Name = "Nombre Categoría Padre")]
        public string? ParentNombre { get; set; }
        [Display(Name = "Activo")]
        public bool Activo { get; set; } = true;

        /// <summary>
        /// RowVersion para control de concurrencia optimista.
        /// Debe enviarse en POST/PUT para detectar conflictos.
        /// </summary>
        public byte[]? RowVersion { get; set; }
    }
}