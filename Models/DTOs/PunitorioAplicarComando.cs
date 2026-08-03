namespace TheBuryProject.Models.DTOs
{
    /// <summary>
    /// Parámetros que el navegador puede enviar para <c>IPunitorioService.AplicarAsync</c> (PUN-ML5).
    /// Deliberadamente NO incluye importe, saldo, días, porcentaje, período, configuración ni fecha de
    /// cálculo: el servidor los recalcula siempre desde <see cref="Interfaces.IPunitorioCalculator"/>
    /// inmediatamente antes de persistir. Ningún valor de este comando participa como autoridad
    /// financiera — únicamente identifican la operación y su justificación.
    /// </summary>
    public sealed class PunitorioAplicarComando
    {
        public required string Motivo { get; init; }

        /// <summary>
        /// <c>Cuota.RowVersion</c> tal como la vio el operador cuando se le mostró el cálculo
        /// consultado (opcional). Si se envía y no coincide con la fila real, se rechaza como
        /// conflicto antes de recalcular — el recálculo server-side ya garantiza la corrección del
        /// importe, esto es solo una señal explícita más rápida de "esto cambió desde que lo viste".
        /// </summary>
        public byte[]? CuotaRowVersionEsperada { get; init; }
    }
}
