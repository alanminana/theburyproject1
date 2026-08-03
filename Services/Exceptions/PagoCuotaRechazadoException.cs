namespace TheBuryProject.Services.Exceptions
{
    /// <summary>
    /// Motivo funcional por el que el servidor rechaza un cobro de cuota. Permite al
    /// controller responder con el estado HTTP correcto sin filtrar detalles internos.
    /// </summary>
    public enum MotivoRechazoPagoCuota
    {
        /// <summary>Datos del pedido inválidos o manipulados (HTTP 400).</summary>
        SolicitudInvalida = 0,

        /// <summary>El estado actual de la cuota o de la caja no admite el cobro (HTTP 409).</summary>
        Conflicto = 1
    }

    /// <summary>
    /// Rechazo controlado de un cobro de cuota. Hereda de <see cref="InvalidOperationException"/>
    /// para mantener el contrato vigente de <c>PagarCuotaAsync</c> con los callers y tests
    /// existentes, agregando el motivo para el mapeo HTTP.
    /// </summary>
    public class PagoCuotaRechazadoException : InvalidOperationException
    {
        public PagoCuotaRechazadoException(MotivoRechazoPagoCuota motivo, string message)
            : base(message)
        {
            Motivo = motivo;
        }

        public MotivoRechazoPagoCuota Motivo { get; }
    }
}
