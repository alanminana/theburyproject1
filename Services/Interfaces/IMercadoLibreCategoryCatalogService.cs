using TheBuryProject.ViewModels;

namespace TheBuryProject.Services.Interfaces
{
    /// <summary>
    /// Consulta del catálogo local de categorías ML (importado por
    /// <see cref="IMercadoLibreCategoryCatalogImportService"/>). No llama a Mercado Libre.
    /// </summary>
    public interface IMercadoLibreCategoryCatalogService
    {
        /// <summary>True si hay al menos una categoría importada para el site.</summary>
        Task<bool> HayCatalogoAsync(string siteId = "MLA", CancellationToken ct = default);

        /// <summary>Estado de la última importación (para la sección de Configuración).</summary>
        Task<MercadoLibreCatalogoEstadoVm> GetEstadoAsync(string siteId = "MLA", CancellationToken ct = default);

        /// <summary>
        /// Busca categorías por CategoryId exacto, nombre o ruta. Prioriza hojas
        /// publicables, coincidencia exacta y total_items desc.
        /// </summary>
        Task<IReadOnlyList<CatalogoCategoriaVm>> BuscarCategoriasAsync(
            string? texto, int limit = 20, string siteId = "MLA", CancellationToken ct = default);

        Task<CatalogoCategoriaVm?> GetCategoryAsync(
            string categoryId, string siteId = "MLA", CancellationToken ct = default);

        /// <summary>Todos los atributos de la categoría (excepto los ocultos no requeridos).</summary>
        Task<IReadOnlyList<CatalogoAtributoVm>> GetAttributesAsync(
            string categoryId, string siteId = "MLA", CancellationToken ct = default);

        /// <summary>
        /// Atributos a mostrar/exigir en el formulario: required, new_required (según
        /// condición), conditional_required y catalog_required. Excluye read_only y
        /// ocultos no requeridos. Cada uno trae EsBloqueante/EsRecomendado calculados.
        /// </summary>
        Task<IReadOnlyList<CatalogoAtributoVm>> GetRequiredAttributesAsync(
            string categoryId, string condition = "new", string? listingTypeId = null,
            string siteId = "MLA", CancellationToken ct = default);

        /// <summary>(Existe en catálogo, EsHoja, ListingAllowed).</summary>
        Task<(bool Existe, bool EsHoja, bool ListingAllowed)> IsLeafListingAllowedAsync(
            string categoryId, string siteId = "MLA", CancellationToken ct = default);
    }
}
