using TheBuryProject.Services.Models;

namespace TheBuryProject.Services.Interfaces
{
    /// <summary>
    /// Backfill conservador de <c>PagoCuota</c> (PUN-ML2) desde <c>MovimientosCaja</c>
    /// existentes. Idempotente: correrlo varias veces no duplica filas.
    /// </summary>
    public interface IPagoCuotaBackfillService
    {
        Task<PagoCuotaBackfillResultado> EjecutarAsync(CancellationToken cancellationToken = default);
    }
}
