namespace TheBuryProject.ViewModels
{
    /// <summary>
    /// Datos del aviso "Filtros activos" que muestran las pestañas de Venta/Index derivadas del
    /// listado filtrado de Operaciones (solo presentación, no interviene en la consulta).
    /// </summary>
    public class VentaFiltrosActivosAvisoViewModel
    {
        /// <summary>Clave de la pestaña (valor de data-venta-tab), usada también como fragmento del link.</summary>
        public string Pestana { get; init; } = string.Empty;

        public string Texto { get; init; } = string.Empty;
    }
}
