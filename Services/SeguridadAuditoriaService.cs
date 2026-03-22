using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Services;

public class SeguridadAuditoriaService : ISeguridadAuditoriaService
{
    private readonly AppDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<SeguridadAuditoriaService> _logger;

    public SeguridadAuditoriaService(
        AppDbContext context,
        ICurrentUserService currentUserService,
        ILogger<SeguridadAuditoriaService> logger)
    {
        _context = context;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task RegistrarEventoAsync(string modulo, string accion, string entidad, string? detalle = null)
    {
        try
        {
            var evento = new SeguridadEventoAuditoria
            {
                FechaEvento = DateTime.UtcNow,
                UsuarioId = _currentUserService.GetUserId(),
                UsuarioNombre = _currentUserService.GetUsername(),
                Modulo = string.IsNullOrWhiteSpace(modulo) ? "Seguridad" : modulo.Trim(),
                Accion = accion.Trim(),
                Entidad = entidad.Trim(),
                Detalle = string.IsNullOrWhiteSpace(detalle) ? null : detalle.Trim(),
                DireccionIp = _currentUserService.GetIpAddress()
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

    public async Task<AuditoriaQueryResult> ConsultarEventosAsync(
        string? usuario = null,
        string? modulo = null,
        string? accion = null,
        DateOnly? desde = null,
        DateOnly? hasta = null)
    {
        var query = _context.SeguridadEventosAuditoria
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(usuario))
            query = query.Where(r => r.UsuarioNombre == usuario);

        if (!string.IsNullOrWhiteSpace(modulo))
            query = query.Where(r => r.Modulo == modulo);

        if (!string.IsNullOrWhiteSpace(accion))
            query = query.Where(r => r.Accion == accion);

        if (desde.HasValue)
        {
            var desdeDate = desde.Value.ToDateTime(TimeOnly.MinValue);
            query = query.Where(r => r.FechaEvento >= desdeDate);
        }

        if (hasta.HasValue)
        {
            var hastaDate = hasta.Value.ToDateTime(TimeOnly.MaxValue);
            query = query.Where(r => r.FechaEvento <= hastaDate);
        }

        var registros = await query
            .OrderByDescending(r => r.FechaEvento)
            .Select(r => new AuditoriaRegistro
            {
                FechaHora = r.FechaEvento,
                Usuario = r.UsuarioNombre,
                Accion = r.Accion,
                Modulo = r.Modulo,
                Entidad = r.Entidad,
                Detalle = r.Detalle ?? string.Empty
            })
            .ToListAsync();

        var allEvents = _context.SeguridadEventosAuditoria.AsNoTracking();

        return new AuditoriaQueryResult
        {
            Registros = registros,
            Usuarios = await allEvents.Select(r => r.UsuarioNombre).Distinct().OrderBy(u => u).ToListAsync(),
            Modulos = await allEvents.Select(r => r.Modulo).Distinct().OrderBy(m => m).ToListAsync(),
            Acciones = await allEvents.Select(r => r.Accion).Distinct().OrderBy(a => a).ToListAsync()
        };
    }
}
