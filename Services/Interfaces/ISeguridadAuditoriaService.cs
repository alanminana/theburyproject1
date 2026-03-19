namespace TheBuryProject.Services.Interfaces;

public interface ISeguridadAuditoriaService
{
    Task RegistrarEventoAsync(string modulo, string accion, string entidad, string? detalle = null);
}
