namespace TheBuryProject.Models.DTOs
{
    /// <summary>Parámetros para <c>IPunitorioService.AnularAsync</c> (PUN-ML5).</summary>
    public sealed class PunitorioAnularComando
    {
        public required string Motivo { get; init; }

        /// <summary><c>PunitorioAplicado.RowVersion</c> tal como la vio el operador. Opcional.</summary>
        public byte[]? RowVersionEsperado { get; init; }
    }
}
