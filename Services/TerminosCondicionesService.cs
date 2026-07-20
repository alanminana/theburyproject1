using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Services;

public class TerminosCondicionesService : ITerminosCondicionesService
{
    public string VersionActual => "2026-07-19";

    private readonly AppDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<TerminosCondicionesService> _logger;

    public TerminosCondicionesService(
        AppDbContext context,
        ICurrentUserService currentUserService,
        ILogger<TerminosCondicionesService> logger)
    {
        _context = context;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<bool> UsuarioAceptoVersionActualAsync(string usuarioId)
    {
        if (string.IsNullOrWhiteSpace(usuarioId))
            return false;

        return await _context.TerminosCondicionesAceptaciones
            .AsNoTracking()
            .AnyAsync(a => a.UsuarioId == usuarioId && a.VersionTerminos == VersionActual);
    }

    public async Task RegistrarAceptacionAsync(string usuarioId, string usuarioNombreUsuario, string nombreIngresado)
    {
        var aceptacion = new TerminoCondicionAceptacion
        {
            UsuarioId = usuarioId,
            UsuarioNombreUsuario = usuarioNombreUsuario,
            NombreIngresado = nombreIngresado.Trim(),
            VersionTerminos = VersionActual,
            FechaAceptacion = DateTime.UtcNow,
            DireccionIp = _currentUserService.GetIpAddress(),
            UserAgent = _currentUserService.GetUserAgent(),
            CreatedBy = usuarioNombreUsuario
        };

        _context.TerminosCondicionesAceptaciones.Add(aceptacion);
        await _context.SaveChangesAsync();
    }

    public async Task<bool> ActivarDesafioALosDiosesAsync(string usuarioId, string usuarioNombreUsuario)
    {
        if (string.IsNullOrWhiteSpace(usuarioId))
            return false;

        try
        {
            if (await UsuarioAceptoVersionActualAsync(usuarioId))
                return true;

            var aceptacion = new TerminoCondicionAceptacion
            {
                UsuarioId = usuarioId,
                UsuarioNombreUsuario = usuarioNombreUsuario,
                NombreIngresado = "(desafío a los dioses)",
                VersionTerminos = VersionActual,
                FechaAceptacion = DateTime.UtcNow,
                DesafioALosDiosesActivado = true,
                DesafioALosDiosesActivadoEnUtc = DateTime.UtcNow,
                DireccionIp = _currentUserService.GetIpAddress(),
                UserAgent = _currentUserService.GetUserAgent(),
                CreatedBy = usuarioNombreUsuario
            };

            _context.TerminosCondicionesAceptaciones.Add(aceptacion);
            await _context.SaveChangesAsync();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al activar 'desafío a los dioses' para el usuario {UsuarioId}", usuarioId);
            return false;
        }
    }
}
