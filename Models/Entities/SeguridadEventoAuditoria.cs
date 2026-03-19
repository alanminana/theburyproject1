using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;

namespace TheBuryProject.Models.Entities;

/// <summary>
/// Evento auditable del módulo Seguridad.
/// </summary>
public class SeguridadEventoAuditoria : AuditableEntity
{
    public DateTime FechaEvento { get; set; } = DateTime.UtcNow;

    [StringLength(450)]
    public string? UsuarioId { get; set; }

    [Required]
    [StringLength(256)]
    public string UsuarioNombre { get; set; } = "Sistema";

    [Required]
    [StringLength(100)]
    public string Modulo { get; set; } = "Seguridad";

    [Required]
    [StringLength(100)]
    public string Accion { get; set; } = string.Empty;

    [Required]
    [StringLength(200)]
    public string Entidad { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? Detalle { get; set; }

    [StringLength(64)]
    public string? DireccionIp { get; set; }
}
