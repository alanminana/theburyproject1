using System.ComponentModel.DataAnnotations;

namespace TheBuryProject.ViewModels;

/// <summary>
/// ViewModel para mostrar un rol en listas
/// </summary>
public class RolViewModel
{
    public string Id { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public bool Activo { get; set; } = true;
    public int CantidadUsuarios { get; set; }
    public int CantidadUsuariosActivos { get; set; }
    public int CantidadPermisos { get; set; }
}

/// <summary>
/// ViewModel para mostrar detalles de un rol
/// </summary>
public class RolDetalleViewModel
{
    public string Id { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public bool Activo { get; set; } = true;
    public int CantidadUsuarios { get; set; }
    public int CantidadUsuariosActivos { get; set; }
    public List<PermisoViewModel> Permisos { get; set; } = new();
    public List<UsuarioBasicoViewModel> Usuarios { get; set; } = new();
}

/// <summary>
/// ViewModel para crear un nuevo rol
/// </summary>
public class CrearRolViewModel
{
    [Required(ErrorMessage = "El nombre del rol es requerido")]
    [StringLength(100, ErrorMessage = "El nombre no puede exceder 100 caracteres")]
    [Display(Name = "Nombre del Rol")]
    public string Nombre { get; set; } = string.Empty;

    [StringLength(500, ErrorMessage = "La descripción no puede exceder 500 caracteres")]
    [Display(Name = "Descripción")]
    public string? Descripcion { get; set; }

    [Display(Name = "Activo")]
    public bool Activo { get; set; } = true;
}

/// <summary>
/// ViewModel para editar un rol
/// </summary>
public class EditarRolViewModel
{
    [Required]
    public string Id { get; set; } = string.Empty;

    [Required(ErrorMessage = "El nombre del rol es requerido")]
    [StringLength(100, ErrorMessage = "El nombre no puede exceder 100 caracteres")]
    [Display(Name = "Nombre del Rol")]
    public string Nombre { get; set; } = string.Empty;

    [StringLength(500, ErrorMessage = "La descripción no puede exceder 500 caracteres")]
    [Display(Name = "Descripción")]
    public string? Descripcion { get; set; }

    [Display(Name = "Activo")]
    public bool Activo { get; set; } = true;
}

/// <summary>
/// ViewModel para duplicar un rol
/// </summary>
public class DuplicarRolViewModel
{
    [Required]
    public string RolOrigenId { get; set; } = string.Empty;

    public string RolOrigenNombre { get; set; } = string.Empty;

    [Required(ErrorMessage = "El nombre del rol es requerido")]
    [StringLength(100, ErrorMessage = "El nombre no puede exceder 100 caracteres")]
    [Display(Name = "Nombre del Rol")]
    public string Nombre { get; set; } = string.Empty;

    [StringLength(500, ErrorMessage = "La descripción no puede exceder 500 caracteres")]
    [Display(Name = "Descripción")]
    public string? Descripcion { get; set; }

    [Display(Name = "Activo")]
    public bool Activo { get; set; } = true;
}

/// <summary>
/// ViewModel para eliminar un rol
/// </summary>
public class EliminarRolViewModel
{
    public string Id { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public int CantidadUsuarios { get; set; }
}

/// <summary>
/// ViewModel para un permiso
/// </summary>
public class PermisoViewModel
{
    public int Id { get; set; }
    public string ModuloNombre { get; set; } = string.Empty;
    public string AccionNombre { get; set; } = string.Empty;
    public string ClaimValue { get; set; } = string.Empty;
}

/// <summary>
/// ViewModel para información básica de usuario
/// </summary>
public class UsuarioBasicoViewModel
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public bool EmailConfirmed { get; set; }
    public string NombreCompleto { get; set; } = string.Empty;
    public DateTime? UltimoAcceso { get; set; }
    public bool Activo { get; set; } = true;
}

/// <summary>
/// ViewModel para asignar permisos a un rol
/// </summary>
public class AsignarPermisosViewModel
{
    public string RoleId { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public List<ModuloConAccionesViewModel> Modulos { get; set; } = new();
}

/// <summary>
/// ViewModel para un módulo con sus acciones
/// </summary>
public class ModuloConAccionesViewModel
{
    public int ModuloId { get; set; }
    public string ModuloNombre { get; set; } = string.Empty;
    public string ModuloClave { get; set; } = string.Empty;
    public string Categoria { get; set; } = string.Empty;
    public string Icono { get; set; } = string.Empty;
    public List<AccionConEstadoViewModel> Acciones { get; set; } = new();
}

/// <summary>
/// ViewModel para una acción con estado de selección
/// </summary>
public class AccionConEstadoViewModel
{
    public int AccionId { get; set; }
    public string AccionNombre { get; set; } = string.Empty;
    public string AccionClave { get; set; } = string.Empty;
    public bool Seleccionada { get; set; }
}

/// <summary>
/// ViewModel para mostrar usuarios en un rol
/// </summary>
public class UsuariosEnRolViewModel
{
    public string RoleId { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public List<UsuarioBasicoViewModel> Usuarios { get; set; } = new();
}
