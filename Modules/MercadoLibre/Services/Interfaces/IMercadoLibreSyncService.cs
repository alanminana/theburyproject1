using TheBuryProject.Modules.MercadoLibre.ViewModels;

namespace TheBuryProject.Modules.MercadoLibre.Services.Interfaces
{
    public enum MercadoLibreSyncTipo
    {
        Stock = 0,
        Precio = 1,
        StockYPrecio = 2
    }

    /// <summary>
    /// Sincronización ERP → Mercado Libre de stock y precio (Fase B).
    /// El ERP es la fuente de verdad. Reglas duras:
    /// - Solo publicaciones vinculadas a un Producto.
    /// - Con ModoSimulacion activo NUNCA se llama a la API: se calcula y se
    ///   registra en MercadoLibreSyncLog con prefijo SIMULADO.
    /// - Variaciones: PUT mínimo {id, campo}; jamás arrays destructivos.
    /// </summary>
    public interface IMercadoLibreSyncService
    {
        /// <summary>
        /// Calcula el preview de sincronización (valores ML actuales vs valores
        /// objetivo del ERP) sin tocar nada.
        /// </summary>
        Task<MercadoLibreSyncPreviewViewModel> PrepararPreviewAsync(
            IReadOnlyCollection<int> listingIds,
            MercadoLibreSyncTipo tipo,
            CancellationToken ct = default);

        /// <summary>
        /// Aplica la sincronización. Si la configuración está en ModoSimulacion,
        /// el resultado es siempre simulado aunque confirmarReal sea true.
        /// </summary>
        Task<MercadoLibreSyncResultViewModel> AplicarAsync(
            IReadOnlyCollection<int> listingIds,
            MercadoLibreSyncTipo tipo,
            bool confirmarReal,
            string usuario,
            CancellationToken ct = default);
    }
}
