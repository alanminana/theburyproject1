using TheBuryProject.Models.Entities;

namespace TheBuryProject.Services.Models
{
    /// <summary>
    /// Aplicación activa (Modelo A: a lo sumo una por cuota) junto con cuánto de su
    /// <see cref="Entities.PunitorioAplicado.Importe"/> ya fue efectivamente cobrado (PUN-ML6).
    /// "Efectivamente cobrado" cuenta únicamente <c>PagoCuota.Estado == Aplicado</c> vinculado por
    /// <c>PagoCuota.PunitorioAplicadoId</c> — anulados y revertidos no cuentan.
    /// </summary>
    public sealed class PunitorioAplicadoProgreso
    {
        public required PunitorioAplicado Aplicacion { get; init; }
        public required decimal MontoPagado { get; init; }

        /// <summary><c>Aplicacion.Importe - MontoPagado</c>, nunca negativo.</summary>
        public required decimal MontoPendiente { get; init; }
    }
}
