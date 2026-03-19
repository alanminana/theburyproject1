using TheBuryProject.Models.Enums;

namespace TheBuryProject.ViewModels;

/// <summary>
/// ViewModel para mostrar notificación
/// </summary>
public class NotificacionViewModel
{
    public int Id { get; set; }
    public byte[]? RowVersion { get; set; }
    public TipoNotificacion Tipo { get; set; }
    public PrioridadNotificacion Prioridad { get; set; }
    public string Titulo { get; set; } = string.Empty;
    public string Mensaje { get; set; } = string.Empty;
    public string? Url { get; set; }
    public string? IconoCss { get; set; }
    public bool Leida { get; set; }
    public DateTime FechaNotificacion { get; set; }
    public string TiempoTranscurrido { get; set; } = string.Empty;
}

/// <summary>
/// ViewModel para lista de notificaciones
/// </summary>
public class ListaNotificacionesViewModel
{
    public List<NotificacionViewModel> Notificaciones { get; set; } = new();
    public int TotalNoLeidas { get; set; }
    public int TotalNotificaciones { get; set; }
}

/// <summary>
/// ViewModel para crear notificación
/// </summary>
public class CrearNotificacionViewModel
{
    public string UsuarioDestino { get; set; } = string.Empty;
    public TipoNotificacion Tipo { get; set; }
    public PrioridadNotificacion Prioridad { get; set; }
    public string Titulo { get; set; } = string.Empty;
    public string Mensaje { get; set; } = string.Empty;
    public string? Url { get; set; }
    public string? EntidadOrigen { get; set; }
    public int? EntidadOrigenId { get; set; }
}