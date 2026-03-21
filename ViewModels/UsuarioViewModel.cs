using System.ComponentModel.DataAnnotations;

namespace TheBuryProject.ViewModels;

/// <summary>
/// ViewModel para mostrar un usuario en listas
/// </summary>
public class UsuarioViewModel
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public bool EmailConfirmed { get; set; }
    public bool LockoutEnabled { get; set; }
    public DateTimeOffset? LockoutEnd { get; set; }
    public List<string> Roles { get; set; } = new();
    public bool Activo { get; set; } = true;
    public string? NombreCompleto { get; set; }
    public string? Sucursal { get; set; }
    public int? SucursalId { get; set; }
    public DateTime? UltimoAcceso { get; set; }
    public DateTime FechaCreacion { get; set; }
}

/// <summary>
/// ViewModel para crear un nuevo usuario
/// </summary>
public class CrearUsuarioViewModel
{
    [Required(ErrorMessage = "El nombre de usuario es requerido")]
    [StringLength(50, ErrorMessage = "El nombre de usuario no puede exceder 50 caracteres")]
    [Display(Name = "Nombre de Usuario")]
    public string UserName { get; set; } = string.Empty;

    [Required(ErrorMessage = "El email es requerido")]
    [EmailAddress(ErrorMessage = "Email inválido")]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "La contraseña es requerida")]
    [StringLength(100, ErrorMessage = "La {0} debe tener al menos {2} y máximo {1} caracteres.", MinimumLength = 6)]
    [DataType(DataType.Password)]
    [Display(Name = "Contraseña")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "La confirmación de contraseña es requerida")]
    [DataType(DataType.Password)]
    [Display(Name = "Confirmar Contraseña")]
    [Compare("Password", ErrorMessage = "La contraseña y la confirmación no coinciden.")]
    public string ConfirmPassword { get; set; } = string.Empty;

    [StringLength(100)]
    [Display(Name = "Nombre")]
    public string? Nombre { get; set; }

    [StringLength(100)]
    [Display(Name = "Apellido")]
    public string? Apellido { get; set; }

    [Phone(ErrorMessage = "Teléfono inválido")]
    [Display(Name = "Teléfono")]
    public string? Telefono { get; set; }

    [MinLength(1, ErrorMessage = "Debe seleccionar al menos un rol")]
    [Display(Name = "Roles")]
    public List<string> RolesSeleccionados { get; set; } = new();

    [Display(Name = "Sucursal")]
    public int? SucursalId { get; set; }

    [Display(Name = "Activo")]
    public bool Activo { get; set; } = true;

    [Display(Name = "Email Confirmado")]
    public bool EmailConfirmed { get; set; } = true;
}

/// <summary>
/// ViewModel para mostrar detalles de un usuario
/// </summary>
public class UsuarioDetalleViewModel
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public bool EmailConfirmed { get; set; }
    public bool LockoutEnabled { get; set; }
    public DateTimeOffset? LockoutEnd { get; set; }
    public bool Activo { get; set; }
    public List<string> Roles { get; set; } = new();
    public List<string> Permisos { get; set; } = new();
}

/// <summary>
/// ViewModel para eliminar un usuario
/// </summary>
public class EliminarUsuarioViewModel
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = new();
}

/// <summary>
/// ViewModel para cambiar contraseña de usuario
/// </summary>
public class CambiarPasswordUsuarioViewModel
{
    [Required]
    public string UserId { get; set; } = string.Empty;

    public string UserName { get; set; } = string.Empty;

    [Required(ErrorMessage = "La nueva contraseña es requerida")]
    [DataType(DataType.Password)]
    [Display(Name = "Nueva Contraseña")]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "La confirmación de contraseña es requerida")]
    [DataType(DataType.Password)]
    [Display(Name = "Confirmar Nueva Contraseña")]
    [Compare("NewPassword", ErrorMessage = "La contraseña y la confirmación no coinciden.")]
    public string ConfirmPassword { get; set; } = string.Empty;

    public int RequiredLength { get; set; }
    public bool RequireUppercase { get; set; }
    public bool RequireLowercase { get; set; }
    public bool RequireDigit { get; set; }
    public bool RequireNonAlphanumeric { get; set; }
}

/// <summary>
/// ViewModel para bloquear un usuario
/// </summary>
public class BloquearUsuarioViewModel
{
    [Required]
    public string UserId { get; set; } = string.Empty;

    public string UserName { get; set; } = string.Empty;

    [Required(ErrorMessage = "El motivo de bloqueo es requerido")]
    [StringLength(250, ErrorMessage = "El motivo no puede exceder 250 caracteres")]
    [Display(Name = "Motivo de Bloqueo")]
    public string MotivoBloqueo { get; set; } = string.Empty;

    [Display(Name = "Bloqueado Hasta")]
    public DateTime? BloqueadoHasta { get; set; }

    public string? ReturnUrl { get; set; }
}
