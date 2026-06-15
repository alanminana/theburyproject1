using TheBuryProject.Modules.MercadoLibre.ViewModels;

namespace TheBuryProject.Modules.MercadoLibre.Services.Interfaces
{
    /// <summary>
    /// ABM de publicaciones existentes (Fase E): detalle, edición de título/
    /// precio/stock/SKU/descripción y cambios de estado (pausar/reactivar/finalizar).
    /// Toda acción respeta ModoSimulacion y queda en MercadoLibreSyncLog.
    /// No crea publicaciones nuevas (Fase F, fuera del MVP).
    /// </summary>
    public interface IMercadoLibreListingAdminService
    {
        Task<MercadoLibreListingDetalleViewModel?> GetDetalleAsync(int listingId, CancellationToken ct = default);

        /// <summary>
        /// Cambia el estado en ML: accion = pausar | reactivar | finalizar.
        /// Finalizar (closed) es IRREVERSIBLE en Mercado Libre.
        /// </summary>
        Task<(bool Ok, string Mensaje)> CambiarEstadoAsync(
            int listingId, string accion, bool confirmarReal, string usuario, CancellationToken ct = default);

        /// <summary>
        /// Edita título / precio / stock / SKU con payload mínimo. Los campos
        /// null no se tocan. ML puede rechazar cambios de título con ventas.
        /// </summary>
        Task<(bool Ok, string Mensaje)> EditarAsync(
            int listingId,
            string? titulo,
            decimal? precio,
            int? stock,
            string? sellerSku,
            bool confirmarReal,
            string usuario,
            CancellationToken ct = default);

        /// <summary>Reemplaza la descripción (texto plano).</summary>
        Task<(bool Ok, string Mensaje)> EditarDescripcionAsync(
            int listingId, string plainText, bool confirmarReal, string usuario, CancellationToken ct = default);
    }
}
