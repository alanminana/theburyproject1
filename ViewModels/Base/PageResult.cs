namespace TheBuryProject.ViewModels.Base
{
    /// <summary>
    /// Resultado paginado genérico para listados.
    /// Contiene los items de la página actual y metadatos de paginación.
    /// </summary>
    /// <typeparam name="T">Tipo de elemento en la colección</typeparam>
    public sealed class PageResult<T>
    {
        /// <summary>
        /// Items de la página actual
        /// </summary>
        public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();

        /// <summary>
        /// Total de items en todas las páginas
        /// </summary>
        public int Total { get; init; }

        /// <summary>
        /// Número de página actual (base 1)
        /// </summary>
        public int Page { get; init; }

        /// <summary>
        /// Cantidad de items por página
        /// </summary>
        public int PageSize { get; init; }

        /// <summary>
        /// Total de páginas calculado
        /// </summary>
        public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)Total / PageSize) : 0;

        /// <summary>
        /// Indica si hay página anterior
        /// </summary>
        public bool HasPreviousPage => Page > 1;

        /// <summary>
        /// Indica si hay página siguiente
        /// </summary>
        public bool HasNextPage => Page < TotalPages;
    }
}