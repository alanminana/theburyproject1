namespace TheBuryProject.Services.Exceptions;

/// <summary>
/// Excepcion de dominio para el cierre de caja con diferencia de arqueo sin justificacion.
/// Es un rechazo de validacion esperado, no un fallo del sistema.
/// </summary>
public sealed class DiferenciaCajaSinJustificacionException : InvalidOperationException
{
    public DiferenciaCajaSinJustificacionException()
        : base("Debe proporcionar una justificación para la diferencia encontrada")
    {
    }
}
