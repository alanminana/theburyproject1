using TheBuryProject.Models.Enums;

namespace TheBuryProject.ViewModels
{
    /// <summary>
    /// Datos que necesita el listado de alertas de stock (filtro + tabla + paginación),
    /// extraído a partial para poder mostrarse tanto en AlertaStock/Index_tw (pantalla
    /// standalone) como embebido en la pestaña "Alertas" de Catalogo/Index_tw (Fase 7).
    /// </summary>
    public class AlertaStockListadoPartialViewModel
    {
        public required PaginatedResult<AlertaStockViewModel> Resultado { get; init; }
        public required AlertaStockFiltroViewModel Filtro { get; init; }
        public TipoAlertaStock[] TiposAlerta { get; init; } = Array.Empty<TipoAlertaStock>();
        public PrioridadAlerta[] Prioridades { get; init; } = Array.Empty<PrioridadAlerta>();
        public EstadoAlerta[] Estados { get; init; } = Array.Empty<EstadoAlerta>();

        /// <summary>Conteo sobre el total filtrado real, no solo la página actual.</summary>
        public int TotalPendientes { get; init; }

        /// <summary>Conteo sobre el total filtrado real, no solo la página actual.</summary>
        public int TotalCriticas { get; init; }

        /// <summary>
        /// True cuando el partial se renderiza dentro de la pestaña "Alertas" de
        /// Catalogo/Index_tw en vez de en la pantalla standalone AlertaStock/Index_tw.
        /// Cambia a qué controller apunta el formulario de filtro/paginación (los campos
        /// de fila — Ver detalle, Kardex, Resolver, Ignorar — siempre apuntan a
        /// AlertaStockController, esté embebido o no).
        /// </summary>
        public bool Embed { get; init; }
    }
}
