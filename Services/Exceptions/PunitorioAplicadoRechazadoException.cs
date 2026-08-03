using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Services.Exceptions
{
    /// <summary>
    /// Rechazo controlado de aplicar o anular un <see cref="Models.Entities.PunitorioAplicado"/>
    /// (PUN-ML5). Mismo patrón que <see cref="ConfiguracionPunitorioRechazadaException"/> y
    /// <see cref="PagoCuotaRechazadoException"/>: hereda de <see cref="InvalidOperationException"/>
    /// y transporta el motivo funcional para que el controller (fuera de alcance en este lote)
    /// mapee el HTTP status sin filtrar detalles internos.
    /// </summary>
    public class PunitorioAplicadoRechazadoException : InvalidOperationException
    {
        public PunitorioAplicadoRechazadoException(MotivoRechazoPunitorioAplicado motivo, string message)
            : base(message)
        {
            Motivo = motivo;
        }

        public PunitorioAplicadoRechazadoException(MotivoRechazoPunitorioAplicado motivo, string message, Exception innerException)
            : base(message, innerException)
        {
            Motivo = motivo;
        }

        public MotivoRechazoPunitorioAplicado Motivo { get; }

        /// <summary>Estado que devolvió el calculador cuando el rechazo es <see cref="MotivoRechazoPunitorioAplicado.NoAplicable"/>. <c>null</c> en los demás motivos.</summary>
        public EstadoResultadoPunitorio? EstadoCalculo { get; init; }
    }
}
