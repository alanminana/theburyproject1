using TheBuryProject.ViewModels;

namespace TheBuryProject.Services.Interfaces
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

        /// <summary>
        /// Cambia la categoría de la publicación (PUT /items {category_id}).
        /// La categoría debe ser hoja y publicable; ML rechaza el cambio si la
        /// publicación tiene ventas o la nueva categoría no es compatible.
        /// </summary>
        Task<(bool Ok, string Mensaje)> EditarCategoriaAsync(
            int listingId, string? categoryId, bool confirmarReal, string usuario, CancellationToken ct = default);

        /// <summary>
        /// Reemplaza TODAS las imágenes de la publicación (PUT /items {pictures}).
        /// Las URLs deben ser públicas (http/https, no localhost): ML las descarga.
        /// </summary>
        Task<(bool Ok, string Mensaje)> EditarImagenesAsync(
            int listingId, IReadOnlyList<string> imagenesUrls, bool confirmarReal, string usuario,
            CancellationToken ct = default);
    }
}
