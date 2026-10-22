namespace TheBuryProject.Services.Exceptions
{
    /// <summary>
    /// Motivo funcional por el que el servidor rechaza configurar un crédito. Permite al
    /// controller responder con el estado HTTP correcto sin filtrar detalles internos.
    /// </summary>
    public enum MotivoRechazoConfiguracionCreditoServicio
    {
        /// <summary>El crédito o la venta indicados no existen (HTTP 404).</summary>
        NoEncontrado = 0,

        /// <summary>Crédito, venta y cliente no son consistentes, o el estado no admite configurar (HTTP 409).</summary>
        Conflicto = 1
    }

    /// <summary>
    /// Rechazo controlado de <c>ConfigurarCreditoAsync</c>. Hereda de
    /// <see cref="InvalidOperationException"/> para conservar el contrato vigente con callers y tests.
    /// </summary>
    public class ConfiguracionCreditoRechazadaException : InvalidOperationException
    {
        public ConfiguracionCreditoRechazadaException(MotivoRechazoConfiguracionCreditoServicio motivo, string message)
            : base(message)
        {
            Motivo = motivo;
        }

        public MotivoRechazoConfiguracionCreditoServicio Motivo { get; }
    }
}
