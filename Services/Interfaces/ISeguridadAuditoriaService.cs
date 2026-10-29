namespace TheBuryProject.Services.Interfaces;

public interface ISeguridadAuditoriaService
{
    Task RegistrarEventoAsync(string modulo, string accion, string entidad, string? detalle = null);

    /// <summary>
    /// Agrega el evento al contexto SIN guardar: se persiste con el SaveChanges de quien lo llama, en la misma operación que
    /// el cambio auditado (si ese guardado falla, tampoco queda el evento ni el cambio). Para cambios que no pueden quedar
    /// confirmados sin su auditoría.
    /// </summary>
    void AgregarEvento(string modulo, string accion, string entidad, string? detalle = null);

    /// <summary>
    /// Para eventos que un tercero puede generar en volumen (login fallido, bloqueo): guarda el evento salvo que ya exista
    /// uno igual (módulo + acción + entidad + IP de origen) dentro de <paramref name="ventana"/>. Acota el crecimiento de la
    /// tabla sin rate-limiting. Devuelve true si registró un evento nuevo. Nunca lanza: un fallo de auditoría no debe
    /// impedir la respuesta de autenticación.
    /// </summary>
    async Task<bool> RegistrarEventoAgrupadoAsync(string modulo, string accion, string entidad, string? detalle, TimeSpan ventana)
    {
        // Implementación por defecto (dobles de prueba): sin agrupación. SeguridadAuditoriaService la reemplaza.
        await RegistrarEventoAsync(modulo, accion, entidad, detalle);
        return true;
    }

    /// <summary>
    /// Consulta registros de auditoría con filtros opcionales.
    /// Devuelve los registros filtrados y las listas de valores distintos para los dropdowns.
    /// <paramref name="take"/> null (default) devuelve todos los registros; con valor se pagina
    /// (<paramref name="skip"/> + <paramref name="take"/>) y <see cref="AuditoriaQueryResult.TotalRegistros"/>
    /// sigue siendo el total filtrado.
    /// </summary>
    Task<AuditoriaQueryResult> ConsultarEventosAsync(
        string? usuario = null,
        string? modulo = null,
        string? accion = null,
        DateOnly? desde = null,
        DateOnly? hasta = null,
        int skip = 0,
        int? take = null);
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
    /// <summary>Total de registros que cumplen los filtros (independiente de la página pedida).</summary>
    public int TotalRegistros { get; init; }
    public List<string> Usuarios { get; init; } = [];
    public List<string> Modulos { get; init; } = [];
    public List<string> Acciones { get; init; } = [];
}
