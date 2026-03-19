namespace TheBuryProject.ViewModels
{
    /// <summary>
    /// ViewModel para filtros y búsqueda de productos
    /// </summary>
    public class ProductoFilterViewModel
    {
        /// <summary>
        /// Búsqueda por texto en código, nombre o descripción
        /// </summary>
        public string? SearchTerm { get; set; }

        /// <summary>
        /// Filtrar por categoría específica
        /// </summary>
        public int? CategoriaId { get; set; }

        /// <summary>
        /// Filtrar por marca específica
        /// </summary>
        public int? MarcaId { get; set; }

        /// <summary>
        /// Mostrar solo productos con stock bajo
        /// </summary>
        public bool StockBajo { get; set; }

        /// <summary>
        /// Mostrar solo productos activos
        /// </summary>
        public bool SoloActivos { get; set; }

        /// <summary>
        /// Campo por el cual ordenar
        /// </summary>
        public string? OrderBy { get; set; }

        /// <summary>
        /// Dirección del ordenamiento (asc/desc)
        /// </summary>
        public string? OrderDirection { get; set; }

        /// <summary>
        /// Lista de productos filtrados
        /// </summary>
        public IEnumerable<ProductoViewModel> Productos { get; set; } = new List<ProductoViewModel>();

        /// <summary>
        /// Total de resultados encontrados
        /// </summary>
        public int TotalResultados { get; set; }
    }
}