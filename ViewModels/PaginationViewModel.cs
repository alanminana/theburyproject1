namespace TheBuryProject.ViewModels
{
    /// <summary>
    /// ViewModel base para paginación
    /// </summary>
    public class PaginationViewModel
    {
        private int _pageNumber = 1;
        private int _pageSize = 10;

        /// <summary>
        /// Número de página actual (comienza en 1)
        /// </summary>
        public int PageNumber
        {
            get => _pageNumber;
            set => _pageNumber = value < 1 ? 1 : value;
        }

        /// <summary>
        /// Cantidad de registros por página
        /// </summary>
        public int PageSize
        {
            get => _pageSize;
            set => _pageSize = value < 1 ? 10 : (value > 100 ? 100 : value);
        }

        /// <summary>
        /// Campo por el cual ordenar
        /// </summary>
        public string? OrderBy { get; set; }

        /// <summary>
        /// Dirección del ordenamiento (asc/desc)
        /// </summary>
        public string OrderDirection { get; set; } = "asc";
    }
}