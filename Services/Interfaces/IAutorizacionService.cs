using TheBuryProject.Models.Entities;

namespace TheBuryProject.Services.Interfaces;

/// <summary>
/// Servicio para gestión de autorizaciones y umbrales por rol
/// </summary>
public interface IAutorizacionService
{
    // Gestión de Umbrales
    Task<List<UmbralAutorizacion>> ObtenerTodosUmbralesAsync();
    Task<List<UmbralAutorizacion>> ObtenerUmbralesPorRolAsync(string rol);
    Task<UmbralAutorizacion?> ObtenerUmbralAsync(int id);
    Task<UmbralAutorizacion> CrearUmbralAsync(UmbralAutorizacion umbral);
    Task<UmbralAutorizacion> ActualizarUmbralAsync(UmbralAutorizacion umbral);
    Task EliminarUmbralAsync(int id);

    // Validación de Autorizaciones
    Task<(bool Permitido, decimal ValorPermitido, string Mensaje)> ValidarDescuentoVentaAsync(string rol, decimal descuento);
    Task<(bool Permitido, decimal ValorPermitido, string Mensaje)> ValidarMontoVentaAsync(string rol, decimal monto);
    Task<(bool Permitido, decimal ValorPermitido, string Mensaje)> ValidarMontoCreditoAsync(string rol, decimal monto);
    Task<(bool Permitido, decimal ValorPermitido, string Mensaje)> ValidarDescuentoCompraAsync(string rol, decimal descuento);

    // Gestión de Solicitudes
    Task<List<SolicitudAutorizacion>> ObtenerTodasSolicitudesAsync();
    Task<List<SolicitudAutorizacion>> ObtenerSolicitudesPendientesAsync();
    Task<List<SolicitudAutorizacion>> ObtenerSolicitudesPorUsuarioAsync(string usuario);
    Task<SolicitudAutorizacion?> ObtenerSolicitudAsync(int id);
    Task<SolicitudAutorizacion> CrearSolicitudAsync(SolicitudAutorizacion solicitud);
    Task<SolicitudAutorizacion> AprobarSolicitudAsync(int id, string autorizador, string? comentario = null);
    Task<SolicitudAutorizacion> RechazarSolicitudAsync(int id, string autorizador, string comentario);
    Task<SolicitudAutorizacion> CancelarSolicitudAsync(int id);
}