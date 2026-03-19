using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using TheBuryProject.Models.Base;

namespace TheBuryProject.Models.Entities;

/// <summary>
/// Metadata adicional para los roles de Identity.
/// Permite extender el rol sin reemplazar la implementación base de Identity.
/// </summary>
public class RolMetadata : AuditableEntity
{
    [Required]
    [StringLength(450)]
    public string RoleId { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Descripcion { get; set; }

    public bool Activo { get; set; } = true;

    public virtual IdentityRole Role { get; set; } = null!;
}
