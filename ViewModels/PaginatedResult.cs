namespace TheBuryProject.ViewModels
{
    /// <summary>
    /// Resultado paginado genérico
    /// </summary>
    /// <typeparam name="T">Tipo de elemento en la colección</typeparam>
    public class PaginatedResult<T>
    {
        /// <summary>
        /// Lista de elementos de la página actual
        /// </summary>
        public List<T> Items { get; set; } = new List<T>();

        /// <summary>
        /// Total de registros en la consulta (sin paginación)
        /// </summary>
        public int TotalRecords { get; set; }

        /// <summary>
        /// Número de página actual
        /// </summary>
        public int PageNumber { get; set; }

        /// <summary>
        /// Tamaño de página
        /// </summary>
        public int PageSize { get; set; }

        /// <summary>
        /// Total de páginas
        /// </summary>
        public int TotalPages => PageSize > 0 ? (int)Math.Ceiling(TotalRecords / (double)PageSize) : 0;

        /// <summary>
        /// Indica si hay página anterior
        /// </summary>
        public bool HasPreviousPage => PageNumber > 1;

        /// <summary>
        /// Indica si hay página siguiente
        /// </summary>
        public bool HasNextPage => PageNumber < TotalPages;

        /// <summary>
        /// Número de primer registro en la página actual
        /// </summary>
        public int FirstRecord => TotalRecords == 0 ? 0 : ((PageNumber - 1) * PageSize) + 1;

        /// <summary>
        /// Número de último registro en la página actual
        /// </summary>
        public int LastRecord
        {
            get
            {
                var last = PageNumber * PageSize;
                return last > TotalRecords ? TotalRecords : last;
            }
        }
    }
}