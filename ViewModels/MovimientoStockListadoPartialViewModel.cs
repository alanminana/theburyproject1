using Microsoft.AspNetCore.Mvc.Rendering;

namespace TheBuryProject.ViewModels
{
    /// <summary>
    /// Datos que necesita el listado de movimientos de stock (filtro + KPIs + tabla +
    /// paginación), extraído a partial para poder mostrarse tanto en
    /// MovimientoStock/Index_tw (pantalla standalone) como embebido en la pestaña
    /// "Movimientos" de Catalogo/Index_tw (Fase 7).
    /// </summary>
    public class MovimientoStockListadoPartialViewModel
    {
        public required MovimientoStockFilterViewModel Filtro { get; init; }
        public SelectList Productos { get; init; } = new SelectList(Array.Empty<object>());
        public SelectList Tipos { get; init; } = new SelectList(Array.Empty<object>());

        /// <summary>
        /// True cuando el partial se renderiza dentro de la pestaña "Movimientos" de
        /// Catalogo/Index_tw en vez de en la pantalla standalone MovimientoStock/Index_tw.
        /// Cambia a qué controller apunta el formulario de filtro/paginación (el link de
        /// Kardex por fila siempre apunta a MovimientoStockController, esté embebido o no).
        /// </summary>
        public bool Embed { get; init; }
    }
}
