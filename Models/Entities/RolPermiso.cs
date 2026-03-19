using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using TheBuryProject.Models.Base;

namespace TheBuryProject.Models.Entities;

/// <summary>
/// Asocia un rol con un permiso específico (claim)
/// Permite gestión granular de permisos por rol
/// </summary>
public class RolPermiso : AuditableEntity
{
    /// <summary>
    /// ID del rol (Identity)
    /// </summary>
    [Required]
    [StringLength(450)]
    public string RoleId { get; set; } = string.Empty;

    /// <summary>
    /// ID del módulo del sistema
    /// </summary>
    [Required]
    public int ModuloId { get; set; }

    /// <summary>
    /// ID de la acción del módulo
    /// </summary>
    [Required]
    public int AccionId { get; set; }

    /// <summary>
    /// Valor del claim en formato "Modulo.Accion"
    /// Ejemplo: "Ventas.Create", "Productos.Update"
    /// </summary>
    [Required]
    [StringLength(200)]
    public string ClaimValue { get; set; } = string.Empty;

    /// <summary>
    /// Observaciones o notas adicionales
    /// </summary>
    [StringLength(500)]
    public string? Observaciones { get; set; }

    // Navegación
    public virtual IdentityRole Role { get; set; } = null!;
    public virtual ModuloSistema Modulo { get; set; } = null!;
    public virtual AccionModulo Accion { get; set; } = null!;
}