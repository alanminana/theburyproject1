using TheBuryProject.Modules.MercadoLibre.ViewModels;

namespace TheBuryProject.Modules.MercadoLibre.Services.Interfaces
{
    /// <summary>
    /// Aumentos masivos de precio en Mercado Libre (Fase I), espejo del patrón
    /// PriceChangeBatch interno: Simulado → Aplicado → Revertido.
    /// - Preview y snapshot del precio anterior son OBLIGATORIOS.
    /// - Aplicar respeta ModoSimulacion y exige confirmación explícita.
    /// - El rollback re-publica el precio anterior de cada item del lote.
    /// - Origen PorcentajeSobrePrecioMl nunca toca precios internos del ERP.
    /// </summary>
    public interface IMercadoLibrePriceBatchService
    {
        /// <summary>Crea el lote en estado Simulado con el snapshot de precios actuales.</summary>
        Task<int> SimularAsync(MercadoLibrePriceBatchRequest request, string usuario, CancellationToken ct = default);

        /// <summary>Aplica el lote. Con ModoSimulacion=true registra simulacion local; en modo real exige confirmacion explicita.</summary>
        Task<(bool Ok, string Mensaje)> AplicarAsync(int batchId, bool confirmarReal, string usuario, CancellationToken ct = default);

        /// <summary>Revierte un lote aplicado re-publicando los precios anteriores.</summary>
        Task<(bool Ok, string Mensaje)> RevertirAsync(int batchId, string? motivo, bool confirmarReal, string usuario, CancellationToken ct = default);

        /// <summary>Cancela un lote simulado que no se va a aplicar.</summary>
        Task CancelarAsync(int batchId, string usuario, CancellationToken ct = default);

        Task<List<MercadoLibrePriceBatchListViewModel>> GetBatchesAsync(CancellationToken ct = default);

        Task<MercadoLibrePriceBatchDetalleViewModel?> GetBatchAsync(int batchId, CancellationToken ct = default);
    }
}
