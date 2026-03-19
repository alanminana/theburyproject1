using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;

namespace TheBuryProject.Models.Entities;

/// <summary>
/// Registra solicitudes de autorización para excepciones de umbrales
/// </summary>
public class SolicitudAutorizacion : AuditableEntity
{
    [Required]
    [StringLength(50)]
    public string UsuarioSolicitante { get; set; } = string.Empty;

    [Required]
    [StringLength(50)]
    public string RolSolicitante { get; set; } = string.Empty;

    [Required]
    public TipoUmbral TipoUmbral { get; set; }

    [Required]
    public decimal ValorSolicitado { get; set; }

    [Required]
    public decimal ValorPermitido { get; set; }

    [Required]
    [StringLength(50)]
    public string TipoOperacion { get; set; } = string.Empty; // "Venta", "Compra", "Credito"

    public int? ReferenciaOperacionId { get; set; }

    [Required]
    [StringLength(1000)]
    public string Justificacion { get; set; } = string.Empty;

    [Required]
    public EstadoSolicitud Estado { get; set; } = EstadoSolicitud.Pendiente;

    [StringLength(50)]
    public string? UsuarioAutorizador { get; set; }

    public DateTime? FechaResolucion { get; set; }

    [StringLength(500)]
    public string? ComentarioResolucion { get; set; }
}

/// <summary>
/// Estados de una solicitud de autorización
/// </summary>
public enum EstadoSolicitud
{
    [Display(Name = "Pendiente")]
    Pendiente = 0,

    [Display(Name = "Aprobada")]
    Aprobada = 1,

    [Display(Name = "Rechazada")]
    Rechazada = 2,

    [Display(Name = "Cancelada")]
    Cancelada = 3
}