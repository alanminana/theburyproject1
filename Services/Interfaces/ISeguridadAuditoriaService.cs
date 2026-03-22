namespace TheBuryProject.Services.Interfaces;

public interface ISeguridadAuditoriaService
{
    Task RegistrarEventoAsync(string modulo, string accion, string entidad, string? detalle = null);

    /// <summary>
    /// Consulta registros de auditoría con filtros opcionales.
    /// Devuelve los registros filtrados y las listas de valores distintos para los dropdowns.
    /// </summary>
    Task<AuditoriaQueryResult> ConsultarEventosAsync(
        string? usuario = null,
        string? modulo = null,
        string? accion = null,
        DateOnly? desde = null,
        DateOnly? hasta = null);
}

public sealed class AuditoriaRegistro
{
    public DateTime FechaHora { get; init; }
    public string Usuario { get; init; } = string.Empty;
    public string Accion { get; init; } = string.Empty;
    public string Modulo { get; init; } = string.Empty;
    public string Entidad { get; init; } = string.Empty;
    public string Detalle { get; init; } = string.Empty;
}

public sealed class AuditoriaQueryResult
{
    public List<AuditoriaRegistro> Registros { get; init; } = [];
    public List<string> Usuarios { get; init; } = [];
    public List<string> Modulos { get; init; } = [];
    public List<string> Acciones { get; init; } = [];
}
