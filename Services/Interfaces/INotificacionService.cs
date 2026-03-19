using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Services.Interfaces
{
    public interface INotificacionService
    {
        // Crear notificaciones
        Task<Notificacion> CrearNotificacionAsync(CrearNotificacionViewModel model);
        Task CrearNotificacionParaUsuarioAsync(string usuario, TipoNotificacion tipo, string titulo, string mensaje, string? url = null, PrioridadNotificacion prioridad = PrioridadNotificacion.Media);
        Task CrearNotificacionParaRolAsync(string rol, TipoNotificacion tipo, string titulo, string mensaje, string? url = null, PrioridadNotificacion prioridad = PrioridadNotificacion.Media);

        // Obtener notificaciones
        Task<List<NotificacionViewModel>> ObtenerNotificacionesUsuarioAsync(string usuario, bool soloNoLeidas = false, int limite = 50);
        Task<int> ObtenerCantidadNoLeidasAsync(string usuario);
        Task<Notificacion?> ObtenerNotificacionPorIdAsync(int id);

        // Marcar como le�da
        Task MarcarComoLeidaAsync(int notificacionId, string usuario, byte[]? rowVersion = null);
        Task MarcarTodasComoLeidasAsync(string usuario);

        // Eliminar
        Task EliminarNotificacionAsync(int id, string usuario, byte[]? rowVersion = null);
        Task LimpiarNotificacionesAntiguasAsync(int diasAntiguedad = 30);

        // Estad�sticas
        Task<ListaNotificacionesViewModel> ObtenerResumenNotificacionesAsync(string usuario);
    }
}