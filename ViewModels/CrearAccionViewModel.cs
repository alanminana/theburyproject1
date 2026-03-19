using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.ViewModels;



public class CrearAccionViewModel
{
    [Required(ErrorMessage = "El nombre de la acción es requerido")]
    [StringLength(100, ErrorMessage = "El nombre no puede exceder 100 caracteres")]
    [Display(Name = "Nombre de la Acción")]
    public string Nombre { get; set; } = string.Empty;

    [Required(ErrorMessage = "La clave de la acción es requerida")]
    [StringLength(50, ErrorMessage = "La clave no puede exceder 50 caracteres")]
    [RegularExpression(@"^[a-z0-9-]+$", ErrorMessage = "La clave solo puede contener letras minúsculas, números y guiones")]
    [Display(Name = "Clave")]
    public string Clave { get; set; } = string.Empty;

    [StringLength(200, ErrorMessage = "La descripción no puede exceder 200 caracteres")]
    [Display(Name = "Descripción")]
    public string? Descripcion { get; set; }

    [Required(ErrorMessage = "Debe seleccionar un módulo")]
    [Display(Name = "Módulo")]
    public int ModuloId { get; set; }

    [Display(Name = "Activo")]
    public bool Activo { get; set; } = true;
}

