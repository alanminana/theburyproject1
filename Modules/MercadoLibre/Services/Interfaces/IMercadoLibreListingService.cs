using TheBuryProject.Modules.MercadoLibre.Entities;
using TheBuryProject.Modules.MercadoLibre.ViewModels;

namespace TheBuryProject.Modules.MercadoLibre.Services.Interfaces
{
    /// <summary>
    /// Importación y administración de publicaciones EXISTENTES de Mercado Libre.
    /// No crea publicaciones nuevas (eso es de IMercadoLibrePublicacionService).
    /// </summary>
    public interface IMercadoLibreListingService
    {
        /// <summary>
        /// Importa/actualiza todas las publicaciones del vendedor (idempotente:
        /// upsert por ItemId). Devuelve el resumen de la corrida.
        /// </summary>
        Task<MercadoLibreImportResultViewModel> ImportarPublicacionesAsync(int accountId, CancellationToken ct = default);

        /// <summary>
        /// Lista publicaciones con su producto vinculado y sugerencia de match
        /// por SellerSku == Producto.Codigo.
        /// </summary>
        Task<List<MercadoLibreListingViewModel>> GetListingsAsync(string? filtroVinculo = null, CancellationToken ct = default);

        /// <summary>
        /// Vincula explícitamente una publicación con un producto interno.
        /// No modifica el producto.
        /// </summary>
        Task VincularProductoAsync(int listingId, int productoId, CancellationToken ct = default);

        /// <summary>
        /// Quita la vinculación. No modifica el producto.
        /// </summary>
        Task DesvincularProductoAsync(int listingId, CancellationToken ct = default);

        /// <summary>
        /// Prellena el formulario de alta de producto desde una publicación sin
        /// vincular (código=SKU, nombre=título, precio y stock de ML). Null si no existe.
        /// </summary>
        Task<MercadoLibreCrearProductoViewModel?> GetCrearProductoViewModelAsync(int listingId, CancellationToken ct = default);

        /// <summary>
        /// Crea un Producto interno a partir de una publicación sin vincular
        /// (alta canónica vía IProductoService, con stock inicial trazado) y
        /// deja la publicación vinculada a él. NO modifica nada en Mercado Libre.
        /// Devuelve el id del producto creado.
        /// </summary>
        Task<int> CrearProductoDesdeListingAsync(
            MercadoLibreCrearProductoViewModel viewModel, string usuario, CancellationToken ct = default);

        /// <summary>
        /// Configura el origen de stock de la publicación (override del global).
        /// origen null = volver al origen global. UnidadFisicaEspecifica exige
        /// producto vinculado y una unidad física de ese producto.
        /// </summary>
        Task ConfigurarOrigenStockAsync(
            int listingId, MercadoLibreOrigenStock? origen, int? productoUnidadId, string usuario, CancellationToken ct = default);

        /// <summary>
        /// Configura vínculo y origen de stock de una variación ML. productoId null
        /// vuelve al fallback permitido de la publicación; origen null vuelve al
        /// origen efectivo de publicación/global.
        /// </summary>
        Task ConfigurarVariacionAsync(
            int listingId,
            long variationId,
            int? productoId,
            MercadoLibreOrigenStock? origen,
            int? productoUnidadId,
            string usuario,
            CancellationToken ct = default);
    }
}
