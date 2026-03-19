using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using TheBuryProject.Filters;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Controllers;

[Authorize]
[PermisoRequerido(Modulo = "notificaciones", Accion = "view")]
[ApiController]
[Route("api/[controller]")]
public class NotificacionController : ControllerBase
{
    private readonly INotificacionService _notificacionService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<NotificacionController> _logger;

    public NotificacionController(
        INotificacionService notificacionService,
        UserManager<ApplicationUser> userManager,
        ILogger<NotificacionController> logger)
    {
        _notificacionService = notificacionService;
        _userManager = userManager;
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
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Unauthorized();
            }

            var notificaciones = await _notificacionService.ObtenerNotificacionesUsuarioAsync(
                user.UserName ?? user.Email ?? "",
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
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Unauthorized();
            }

            var cantidad = await _notificacionService.ObtenerCantidadNoLeidasAsync(
                user.UserName ?? user.Email ?? "");

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
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Unauthorized();
            }

            await _notificacionService.MarcarComoLeidaAsync(id, user.UserName ?? user.Email ?? "", rowVersion);

            return Ok(new { success = true });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error al marcar notificación {id} como leída");
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
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Unauthorized();
            }

            await _notificacionService.MarcarTodasComoLeidasAsync(user.UserName ?? user.Email ?? "");

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
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Unauthorized();
            }

            await _notificacionService.EliminarNotificacionAsync(id, user.UserName ?? user.Email ?? "", rowVersion);

            return Ok(new { success = true });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error al eliminar notificación {id}");
            return StatusCode(500, new { error = "Error al eliminar notificación" });
        }
    }
}
