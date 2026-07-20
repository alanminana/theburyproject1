using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;

namespace TheBuryProject.Models.Entities;

/// <summary>
/// Registro de aceptación de los Términos y Condiciones de uso.
/// Un registro por usuario y versión aceptada (histórico, nunca se sobreescribe).
/// </summary>
public class TerminoCondicionAceptacion : AuditableEntity
{
    [Required]
    [StringLength(450)]
    public string UsuarioId { get; set; } = string.Empty;

    [Required]
    [StringLength(256)]
    public string UsuarioNombreUsuario { get; set; } = string.Empty;

    /// <summary>
    /// Nombre que la persona escribió manualmente para confirmar la aceptación.
    /// </summary>
    [Required]
    [StringLength(200)]
    public string NombreIngresado { get; set; } = string.Empty;

    [Required]
    [StringLength(20)]
    public string VersionTerminos { get; set; } = string.Empty;

    public DateTime FechaAceptacion { get; set; } = DateTime.UtcNow;

    [StringLength(64)]
    public string? DireccionIp { get; set; }

    [StringLength(512)]
    public string? UserAgent { get; set; }

    /// <summary>
    /// Vía alternativa de aceptación ("desafiar a los dioses"): omite la comprobación
    /// personal de la cláusula 17.7 sin alterar el resto del flujo de auditoría.
    /// </summary>
    public bool DesafioALosDiosesActivado { get; set; } = false;

    public DateTime? DesafioALosDiosesActivadoEnUtc { get; set; }

    public virtual ApplicationUser? Usuario { get; set; }
}
