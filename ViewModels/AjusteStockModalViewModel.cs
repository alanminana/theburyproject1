using Microsoft.AspNetCore.Mvc.Rendering;

namespace TheBuryProject.ViewModels
{
    /// <summary>
    /// Datos para renderizar el modal "Registrar ajuste de stock" (_AjusteStockModal.cshtml),
    /// reusado en MovimientoStock/Index_tw, MovimientoStock/Kardex_tw y Producto/FichaInventario
    /// en reemplazo de la navegación de página completa a MovimientoStock/Create_tw.
    /// </summary>
    public class AjusteStockModalViewModel
    {
        public required AjusteStockViewModel Ajuste { get; set; }

        /// <summary>
        /// true cuando el producto ya está determinado por la pantalla que abre el modal
        /// (Kardex, Ficha de inventario): se muestra fijo, sin selector. false en
        /// MovimientoStock/Index_tw, donde el ajuste puede ser para cualquier producto.
        /// </summary>
        public bool ProductoBloqueado { get; set; }

        /// <summary>Solo se usa cuando ProductoBloqueado es false.</summary>
        public SelectList? Productos { get; set; }

        public SelectList? Tipos { get; set; }
    }
}
