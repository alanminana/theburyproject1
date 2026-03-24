using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TheBuryProject.Filters;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Controllers;

[Authorize]
[PermisoRequerido(Modulo = "notificaciones", Accion = "view")]
[ApiController]
[Route("api/[controller]")]
public class NotificacionController : ControllerBase
{
    private readonly INotificacionService _notificacionService;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<NotificacionController> _logger;

    public NotificacionController(
        INotificacionService notificacionService,
        ICurrentUserService currentUser,
        ILogger<NotificacionController> logger)
    {
        _notificacionService = notificacionService;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <summary>
    /// Obtener notificaciones del usuario actual
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> ObtenerNotificaciones([FromQuery] bool soloNoLeidas = false, [FromQuery] int limite = 20)
    {
        try
        {
            if (!_currentUser.IsAuthenticated())
            {
                return Unauthorized();
            }

            var notificaciones = await _notificacionService.ObtenerNotificacionesUsuarioAsync(
                _currentUser.GetUsername(),
                soloNoLeidas,
                limite);

            return Ok(notificaciones);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al obtener notificaciones");
            return StatusCode(500, new { error = "Error al obtener notificaciones" });
        }
    }

    /// <summary>
    /// Obtener cantidad de notificaciones no leídas
    /// </summary>
    [HttpGet("noLeidas/cantidad")]
    public async Task<IActionResult> ObtenerCantidadNoLeidas()
    {
        try
        {
            if (!_currentUser.IsAuthenticated())
            {
                return Unauthorized();
            }

            var cantidad = await _notificacionService.ObtenerCantidadNoLeidasAsync(
                _currentUser.GetUsername());

            return Ok(new { cantidad });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al obtener cantidad de notificaciones no leídas");
            return StatusCode(500, new { error = "Error al obtener cantidad" });
        }
    }

    /// <summary>
    /// Marcar notificación como leída
    /// </summary>
    [HttpPost("{id}/marcarLeida")]
    [PermisoRequerido(Modulo = "notificaciones", Accion = "update")]
    public async Task<IActionResult> MarcarComoLeida(int id, [FromQuery] byte[]? rowVersion = null)
    {
        try
        {
            if (!_currentUser.IsAuthenticated())
            {
                return Unauthorized();
            }

            await _notificacionService.MarcarComoLeidaAsync(id, _currentUser.GetUsername(), rowVersion);

            return Ok(new { success = true });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al marcar notificación {NotificacionId} como leída", id);
            return StatusCode(500, new { error = "Error al marcar como leída" });
        }
    }

    /// <summary>
    /// Marcar todas las notificaciones como leídas
    /// </summary>
    [HttpPost("marcarTodasLeidas")]
    [PermisoRequerido(Modulo = "notificaciones", Accion = "update")]
    public async Task<IActionResult> MarcarTodasComoLeidas()
    {
        try
        {
            if (!_currentUser.IsAuthenticated())
            {
                return Unauthorized();
            }

            await _notificacionService.MarcarTodasComoLeidasAsync(_currentUser.GetUsername());

            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al marcar todas como leídas");
            return StatusCode(500, new { error = "Error al marcar todas como leídas" });
        }
    }

    /// <summary>
    /// Eliminar notificación
    /// </summary>
    [HttpDelete("{id}")]
    [PermisoRequerido(Modulo = "notificaciones", Accion = "delete")]
    public async Task<IActionResult> EliminarNotificacion(int id, [FromQuery] byte[]? rowVersion = null)
    {
        try
        {
            if (!_currentUser.IsAuthenticated())
            {
                return Unauthorized();
            }

            await _notificacionService.EliminarNotificacionAsync(id, _currentUser.GetUsername(), rowVersion);

            return Ok(new { success = true });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al eliminar notificación {NotificacionId}", id);
            return StatusCode(500, new { error = "Error al eliminar notificación" });
        }
    }
}
