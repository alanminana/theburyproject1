using System.ComponentModel.DataAnnotations;

namespace TheBuryProject.ViewModels;

/// <summary>
/// ViewModel para mostrar un módulo en listas
/// </summary>
public class ModuloViewModel
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Clave { get; set; } = string.Empty;
    public string? Categoria { get; set; }
    public string? Icono { get; set; }
    public int CantidadAcciones { get; set; }
    public bool Activo { get; set; }
}

/// <summary>
/// ViewModel para crear un nuevo módulo
/// </summary>
public class CrearModuloViewModel
{
    [Required(ErrorMessage = "El nombre del módulo es requerido")]
    [StringLength(100, ErrorMessage = "El nombre no puede exceder 100 caracteres")]
    [Display(Name = "Nombre del Módulo")]
    public string Nombre { get; set; } = string.Empty;

    [Required(ErrorMessage = "La clave del módulo es requerida")]
    [StringLength(50, ErrorMessage = "La clave no puede exceder 50 caracteres")]
    [RegularExpression(@"^[a-z0-9-]+$", ErrorMessage = "La clave solo puede contener letras minúsculas, números y guiones")]
    [Display(Name = "Clave")]
    public string Clave { get; set; } = string.Empty;

    [StringLength(200, ErrorMessage = "La descripción no puede exceder 200 caracteres")]
    [Display(Name = "Descripción")]
    public string? Descripcion { get; set; }

    [StringLength(50, ErrorMessage = "La categoría no puede exceder 50 caracteres")]
    [Display(Name = "Categoría")]
    public string? Categoria { get; set; }

    [StringLength(50, ErrorMessage = "El icono no puede exceder 50 caracteres")]
    [Display(Name = "Icono (Bootstrap Icons)")]
    public string? Icono { get; set; }

    [Range(0, 999, ErrorMessage = "El orden debe estar entre 0 y 999")]
    [Display(Name = "Orden de Visualización")]
    public int Orden { get; set; }

    [Display(Name = "Activo")]
    public bool Activo { get; set; } = true;
}

/// <summary>
/// ViewModel para editar un módulo
/// </summary>
public class EditarModuloViewModel
{
    [Required]
    public int Id { get; set; }

    [Required(ErrorMessage = "El nombre del módulo es requerido")]
    [StringLength(100, ErrorMessage = "El nombre no puede exceder 100 caracteres")]
    [Display(Name = "Nombre del Módulo")]
    public string Nombre { get; set; } = string.Empty;

    [Required(ErrorMessage = "La clave del módulo es requerida")]
    [StringLength(50, ErrorMessage = "La clave no puede exceder 50 caracteres")]
    [RegularExpression(@"^[a-z0-9-]+$", ErrorMessage = "La clave solo puede contener letras minúsculas, números y guiones")]
    [Display(Name = "Clave")]
    public string Clave { get; set; } = string.Empty;

    [StringLength(200, ErrorMessage = "La descripción no puede exceder 200 caracteres")]
    [Display(Name = "Descripción")]
    public string? Descripcion { get; set; }

    [StringLength(50, ErrorMessage = "La categoría no puede exceder 50 caracteres")]
    [Display(Name = "Categoría")]
    public string? Categoria { get; set; }

    [StringLength(50, ErrorMessage = "El icono no puede exceder 50 caracteres")]
    [Display(Name = "Icono (Bootstrap Icons)")]
    public string? Icono { get; set; }

    [Range(0, 999, ErrorMessage = "El orden debe estar entre 0 y 999")]
    [Display(Name = "Orden de Visualización")]
    public int Orden { get; set; }

    [Display(Name = "Activo")]
    public bool Activo { get; set; }
}

/// <summary>
/// ViewModel para mostrar detalles de un módulo
/// </summary>
public class ModuloDetalleViewModel
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Clave { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public string? Categoria { get; set; }
    public string? Icono { get; set; }
    public int Orden { get; set; }
    public bool Activo { get; set; }
    public List<AccionViewModel> Acciones { get; set; } = new();
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// ViewModel para eliminar un módulo
/// </summary>
public class EliminarModuloViewModel
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Clave { get; set; } = string.Empty;
    public int CantidadAcciones { get; set; }
}