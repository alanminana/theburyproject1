using Microsoft.AspNetCore.Mvc.Rendering;

namespace TheBuryProject.ViewModels
{
    /// <summary>
    /// ViewModel unificado para la vista de Catálogo con productos y precios.
    /// Se construye a partir de ResultadoCatalogo del servicio.
    /// </summary>
    public class CatalogoUnificadoViewModel
    {
        // ============================================
        // FILTROS (para binding del formulario)
        // ============================================

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
        /// Lista de precios seleccionada (null = predeterminada)
        /// </summary>
        public int? ListaPrecioId { get; set; }

        // ============================================
        // DATOS (del servicio)
        // ============================================

        /// <summary>
        /// Lista de productos filtrados con precios (usando FilaCatalogo del servicio)
        /// </summary>
        public IEnumerable<FilaCatalogo> Productos { get; set; } = new List<FilaCatalogo>();

        /// <summary>
        /// Total de resultados encontrados
        /// </summary>
        public int TotalResultados { get; set; }

        // ============================================
        // DROPDOWNS (SelectList para la vista)
        // ============================================

        /// <summary>
        /// Lista de categorías para el filtro
        /// </summary>
        public SelectList? CategoriasFiltro { get; set; }

        /// <summary>
        /// Lista de marcas para el filtro
        /// </summary>
        public SelectList? MarcasFiltro { get; set; }

        /// <summary>
        /// Listas de precios disponibles
        /// </summary>
        public SelectList? ListasPreciosFiltro { get; set; }

        /// <summary>
        /// Nombre de la lista de precios actual
        /// </summary>
        public string ListaPrecioActualNombre { get; set; } = "Predeterminada";

        // ============================================
        // LISTAS PARA JS (acciones masivas)
        // ============================================

        /// <summary>
        /// Categorías disponibles para el JS de acciones masivas
        /// </summary>
        public IEnumerable<OpcionDropdown> Categorias { get; set; } = Enumerable.Empty<OpcionDropdown>();

        /// <summary>
        /// Marcas disponibles para el JS de acciones masivas
        /// </summary>
        public IEnumerable<OpcionDropdown> Marcas { get; set; } = Enumerable.Empty<OpcionDropdown>();

        /// <summary>
        /// Listas de precios disponibles para el JS de acciones masivas
        /// </summary>
        public IEnumerable<OpcionDropdown> ListasPrecios { get; set; } = Enumerable.Empty<OpcionDropdown>();

        // ============================================
        // MÉTRICAS
        // ============================================

        /// <summary>
        /// Total de categorías en el sistema
        /// </summary>
        public int TotalCategorias { get; set; }

        /// <summary>
        /// Total de marcas en el sistema
        /// </summary>
        public int TotalMarcas { get; set; }

        // ============================================
        // LISTADOS PARA TABS
        // ============================================

        /// <summary>
        /// Categorías completas para la pestaña de categorías
        /// </summary>
        public IEnumerable<CategoriaViewModel> CategoriasListado { get; set; } = Enumerable.Empty<CategoriaViewModel>();

        /// <summary>
        /// Marcas completas para la pestaña de marcas
        /// </summary>
        public IEnumerable<MarcaViewModel> MarcasListado { get; set; } = Enumerable.Empty<MarcaViewModel>();

        /// <summary>
        /// Productos activos
        /// </summary>
        public int ProductosActivos => Productos.Count(p => p.Activo);

        /// <summary>
        /// Productos con stock bajo o sin stock
        /// </summary>
        public int ProductosStockBajo => Productos.Count(p => p.StockCritico);

        /// <summary>
        /// Indica si hay filtros activos
        /// </summary>
        public bool TieneFiltrosActivos =>
            !string.IsNullOrEmpty(SearchTerm) ||
            CategoriaId.HasValue ||
            MarcaId.HasValue ||
            StockBajo ||
            SoloActivos;

        // ============================================
        // FACTORY METHOD
        // ============================================

        /// <summary>
        /// Crea un ViewModel a partir del resultado del servicio y los filtros originales
        /// </summary>
        public static CatalogoUnificadoViewModel Desde(ResultadoCatalogo resultado, FiltrosCatalogo filtros)
        {
            return new CatalogoUnificadoViewModel
            {
                // Filtros (para mantener estado en formulario)
                SearchTerm = filtros.TextoBusqueda,
                CategoriaId = filtros.CategoriaId,
                MarcaId = filtros.MarcaId,
                StockBajo = filtros.SoloStockBajo,
                SoloActivos = filtros.SoloActivos,
                OrderBy = filtros.OrdenarPor,
                OrderDirection = filtros.DireccionOrden,
                ListaPrecioId = resultado.ListaPrecioId,

                // Datos
                Productos = resultado.Filas,
                TotalResultados = resultado.TotalResultados,

                // Dropdowns (convertir a SelectList)
                CategoriasFiltro = new SelectList(resultado.Categorias, "Id", "Nombre", filtros.CategoriaId),
                MarcasFiltro = new SelectList(resultado.Marcas, "Id", "Nombre", filtros.MarcaId),
                ListasPreciosFiltro = new SelectList(resultado.ListasPrecios, "Id", "Nombre", resultado.ListaPrecioId),
                ListaPrecioActualNombre = resultado.ListaPrecioNombre,

                // Listas para JS de acciones masivas
                Categorias = resultado.Categorias,
                Marcas = resultado.Marcas,
                ListasPrecios = resultado.ListasPrecios,

                // Métricas
                TotalCategorias = resultado.TotalCategorias,
                TotalMarcas = resultado.TotalMarcas
            };
        }
    }

}
