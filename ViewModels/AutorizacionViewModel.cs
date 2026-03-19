using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Entities;

namespace TheBuryProject.ViewModels;

/// <summary>
/// ViewModel para crear/editar umbrales de autorización
/// </summary>
public class UmbralAutorizacionViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "El rol es obligatorio")]
    [Display(Name = "Rol")]
    public string Rol { get; set; } = string.Empty;

    [Required(ErrorMessage = "El tipo de umbral es obligatorio")]
    [Display(Name = "Tipo de Umbral")]
    public TipoUmbral TipoUmbral { get; set; }

    [Required(ErrorMessage = "El valor máximo es obligatorio")]
    [Range(0, double.MaxValue, ErrorMessage = "El valor debe ser mayor a 0")]
    [Display(Name = "Valor Máximo Permitido")]
    public decimal ValorMaximo { get; set; }

    [StringLength(500)]
    [Display(Name = "Descripción")]
    public string? Descripcion { get; set; }

    [Display(Name = "Activo")]
    public bool Activo { get; set; } = true;
}

/// <summary>
/// ViewModel para la lista de umbrales
/// </summary>
public class UmbralesListViewModel
{
    public List<UmbralAutorizacion> Umbrales { get; set; } = new();
    public Dictionary<string, List<UmbralAutorizacion>> UmbralesPorRol { get; set; } = new();
}

/// <summary>
/// ViewModel para crear solicitud de autorización
/// </summary>
public class CrearSolicitudAutorizacionViewModel
{
    [Required(ErrorMessage = "El tipo de umbral es obligatorio")]
    [Display(Name = "Tipo de Operación")]
    public TipoUmbral TipoUmbral { get; set; }

    [Required(ErrorMessage = "El valor solicitado es obligatorio")]
    [Range(0, double.MaxValue, ErrorMessage = "El valor debe ser mayor a 0")]
    [Display(Name = "Valor Solicitado")]
    public decimal ValorSolicitado { get; set; }

    [Required(ErrorMessage = "El tipo de operación es obligatorio")]
    [Display(Name = "Tipo de Operación")]
    [StringLength(50)]
    public string TipoOperacion { get; set; } = string.Empty;

    [Display(Name = "Referencia (ID Operación)")]
    public int? ReferenciaOperacionId { get; set; }

    [Required(ErrorMessage = "La justificación es obligatoria")]
    [StringLength(1000, MinimumLength = 20, ErrorMessage = "La justificación debe tener entre 20 y 1000 caracteres")]
    [Display(Name = "Justificación")]
    public string Justificacion { get; set; } = string.Empty;

    // Valores calculados internamente
    public decimal ValorPermitido { get; set; }
    public string UsuarioSolicitante { get; set; } = string.Empty;
    public string RolSolicitante { get; set; } = string.Empty;
}

/// <summary>
/// ViewModel para gestionar solicitud de autorización
/// </summary>
public class GestionarSolicitudViewModel
{
    public int Id { get; set; }
    public string UsuarioSolicitante { get; set; } = string.Empty;
    public string RolSolicitante { get; set; } = string.Empty;
    public TipoUmbral TipoUmbral { get; set; }
    public decimal ValorSolicitado { get; set; }
    public decimal ValorPermitido { get; set; }
    public string TipoOperacion { get; set; } = string.Empty;
    public int? ReferenciaOperacionId { get; set; }
    public string Justificacion { get; set; } = string.Empty;
    public EstadoSolicitud Estado { get; set; }
    public DateTime FechaSolicitud { get; set; }

    [Required(ErrorMessage = "El comentario es obligatorio para aprobar o rechazar")]
    [StringLength(500, MinimumLength = 10, ErrorMessage = "El comentario debe tener entre 10 y 500 caracteres")]
    [Display(Name = "Comentario")]
    public string? ComentarioResolucion { get; set; }
}

/// <summary>
/// ViewModel para la lista de solicitudes
/// </summary>
public class SolicitudesListViewModel
{
    public List<SolicitudAutorizacion> Pendientes { get; set; } = new();
    public List<SolicitudAutorizacion> Resueltas { get; set; } = new();
    public List<SolicitudAutorizacion> MisSolicitudes { get; set; } = new();

    public int TotalPendientes { get; set; }
    public int TotalAprobadas { get; set; }
    public int TotalRechazadas { get; set; }
}

/// <summary>
/// ViewModel para resultado de validación
/// </summary>
public class ResultadoValidacionViewModel
{
    public bool Permitido { get; set; }
    public decimal ValorPermitido { get; set; }
    public decimal ValorSolicitado { get; set; }
    public string Mensaje { get; set; } = string.Empty;
    public TipoUmbral TipoUmbral { get; set; }
    public bool RequiereSolicitud => !Permitido;
}