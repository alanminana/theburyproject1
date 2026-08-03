namespace TheBuryProject.Services.Exceptions
{
    /// <summary>
    /// Motivo funcional por el que el servidor rechaza la creación de una versión de
    /// <c>ConfiguracionPunitorio</c>. Permite al controller (fuera de alcance en PUN-ML3) responder
    /// con el estado HTTP correcto sin filtrar detalles internos.
    /// </summary>
    public enum MotivoRechazoConfiguracionPunitorio
    {
        /// <summary>Valores del comando inválidos (porcentaje negativo, período &lt; 1, etc.) (HTTP 400).</summary>
        SolicitudInvalida = 0,

        /// <summary>
        /// El estado actual de las versiones ya existentes no admite esta vigencia: vigencia igual
        /// o anterior a una ya existente, retroactividad sin motivo/autorización, o choque
        /// concurrente contra el índice único (HTTP 409).
        /// </summary>
        Conflicto = 1
    }

    /// <summary>
    /// Rechazo controlado de la creación de una versión de <c>ConfiguracionPunitorio</c>. Hereda de
    /// <see cref="InvalidOperationException"/>, mismo patrón que <c>PagoCuotaRechazadoException</c>
    /// (PUN-ML2).
    /// </summary>
    public class ConfiguracionPunitorioRechazadaException : InvalidOperationException
    {
        public ConfiguracionPunitorioRechazadaException(MotivoRechazoConfiguracionPunitorio motivo, string message)
            : base(message)
        {
            Motivo = motivo;
        }

        public ConfiguracionPunitorioRechazadaException(MotivoRechazoConfiguracionPunitorio motivo, string message, Exception innerException)
            : base(message, innerException)
        {
            Motivo = motivo;
        }

        public MotivoRechazoConfiguracionPunitorio Motivo { get; }
    }
}
