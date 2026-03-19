using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Services;

public class SeguridadAuditoriaService : ISeguridadAuditoriaService
{
    private readonly AppDbContext _context;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<SeguridadAuditoriaService> _logger;

    public SeguridadAuditoriaService(
        AppDbContext context,
        IHttpContextAccessor httpContextAccessor,
        ILogger<SeguridadAuditoriaService> logger)
    {
        _context = context;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public async Task RegistrarEventoAsync(string modulo, string accion, string entidad, string? detalle = null)
    {
        try
        {
            var httpContext = _httpContextAccessor.HttpContext;
            var user = httpContext?.User;

            var evento = new SeguridadEventoAuditoria
            {
                FechaEvento = DateTime.UtcNow,
                UsuarioId = user?.FindFirstValue(ClaimTypes.NameIdentifier),
                UsuarioNombre = user?.Identity?.Name ?? "Sistema",
                Modulo = string.IsNullOrWhiteSpace(modulo) ? "Seguridad" : modulo.Trim(),
                Accion = accion.Trim(),
                Entidad = entidad.Trim(),
                Detalle = string.IsNullOrWhiteSpace(detalle) ? null : detalle.Trim(),
                DireccionIp = httpContext?.Connection.RemoteIpAddress?.ToString()
            };

            _context.Set<SeguridadEventoAuditoria>().Add(evento);
            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo registrar evento de auditoría de seguridad {Modulo}/{Accion} sobre {Entidad}",
                modulo, accion, entidad);
        }
    }
}
