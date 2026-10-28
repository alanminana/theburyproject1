using System.ComponentModel.DataAnnotations;
using TheBuryProject.Validation;

namespace TheBuryProject.ViewModels;

public class SeguridadEditarUsuarioViewModel
{
    [Required]
    public string Id { get; set; } = string.Empty;

    [Required(ErrorMessage = "El nombre de usuario es requerido")]
    [StringLength(50, ErrorMessage = "El nombre de usuario no puede exceder 50 caracteres")]
    [RegularExpression(@"^[A-Za-z0-9._@+-]{3,50}$", ErrorMessage = "El nombre de usuario debe tener entre 3 y 50 caracteres: letras sin tildes, números y . _ - @ + (sin espacios).")]
    [Display(Name = "Username")]
    public string UserName { get; set; } = string.Empty;

    [Required(ErrorMessage = "El email es requerido")]
    [StringLength(256, ErrorMessage = "El email no puede exceder 256 caracteres")]
    [EmailAddress(ErrorMessage = "Email inválido")]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [StringLength(100)]
    [SoloLetras(MinLength = 2)]
    [Display(Name = "Nombre")]
    public string? Nombre { get; set; }

    [StringLength(100)]
    [SoloLetras(MinLength = 2)]
    [Display(Name = "Apellido")]
    public string? Apellido { get; set; }

    [TelefonoArgentino]
    [Display(Name = "Teléfono")]
    public string? Telefono { get; set; }

    [Display(Name = "Roles")]
    public List<string> RolesSeleccionados { get; set; } = new();

    [Display(Name = "Sucursal")]
    public int? SucursalId { get; set; }

    [Display(Name = "Activo")]
    public bool Activo { get; set; }

    public byte[]? RowVersion { get; set; }

    public List<string> AllRoles { get; set; } = new();
    public List<SucursalOptionViewModel> AllSucursales { get; set; } = new();
}
