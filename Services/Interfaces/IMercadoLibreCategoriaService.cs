using TheBuryProject.ViewModels;

namespace TheBuryProject.Services.Interfaces
{
    /// <summary>
    /// Acceso al árbol de categorías de Mercado Libre, consumido ONLINE (sin
    /// persistir el árbol completo). El predictor y el detalle de categoría son
    /// recursos públicos: el token se usa si hay cuenta conectada (mejor cuota) y
    /// es obligatorio solo para listar las categorías raíz del site.
    /// </summary>
    public interface IMercadoLibreCategoriaService
    {
        /// <summary>
        /// Sugiere categorías hoja para un texto (título del producto) vía el
        /// predictor domain_discovery. Devuelve lista vacía si el texto es corto.
        /// </summary>
        Task<IReadOnlyList<CategoriaSugerenciaVm>> SugerirAsync(string? consulta, CancellationToken ct = default);

        /// <summary>
        /// Navega un nivel del árbol: categorías raíz del site si categoryId es null,
        /// o las hijas de la categoría indicada. Si la categoría es hoja, Hijos viene
        /// vacío y EsHoja en true.
        /// </summary>
        Task<CategoriaNivelVm> ListarHijosAsync(string? categoryId, CancellationToken ct = default);

        /// <summary>
        /// Resuelve una categoría contra ML: nombre, ruta, si es hoja y si permite
        /// publicar. Es la fuente autoritativa que el picker snapshotea en el borrador.
        /// </summary>
        Task<CategoriaResueltaVm> ResolverAsync(string categoryId, CancellationToken ct = default);
    }
}
