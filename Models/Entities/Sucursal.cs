using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;

namespace TheBuryProject.Models.Entities;

/// <summary>
/// Catálogo de sucursales del sistema.
/// Seguridad usa esta tabla como fuente de verdad para asignación relacional de usuarios.
/// </summary>
public class Sucursal : AuditableEntity
{
    [Required]
    [StringLength(120)]
    public string Nombre { get; set; } = string.Empty;

    public bool Activa { get; set; } = true;

    public virtual ICollection<ApplicationUser> Usuarios { get; set; } = new List<ApplicationUser>();
}
